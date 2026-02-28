using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2_EntityFix_SW2;

[PluginMetadata(
    Id = "CS2-EntityFix-SW2",
    Name = "Entity Fix SW2",
    Author = "DarkerZ [RUS] & XiaoLinWuDi",
    Version = "1.0.0",
    Description = "Fixes game_player_equip, game_ui, point_viewcontrol, IgniteLifeTime and trigger_gravity",
    Website = "https://github.com/swiftly-solution/swiftlys2")]
public sealed class EntityFixSw2(ISwiftlyCore core) : BasePlugin(core)
{
    private const uint FlagAtControls = 1u << 6;
    private const uint FlagFrozen = 1u << 5;

    private const string DefaultIgniteParticle = "particles/burning_fx/env_fire_small.vpcf";
    private const float DefaultIgniteVelocity = 0.45f;
    private const float DefaultIgniteRepeat = 0.5f;
    private const int DefaultIgniteDamage = 1;
    private const float DefaultTriggerGravity = 0.01f;

    private readonly List<GameUiState> _gameUiStates = [];
    private readonly List<ViewControlState> _viewControls = [];
    private readonly ConcurrentDictionary<int, IgniteState> _igniteStates = [];
    private Dictionary<string, float>? _mapGravity;

    private ConfigModel _config = new();
    private string _igniteParticle = DefaultIgniteParticle;
    private float _igniteVelocity = DefaultIgniteVelocity;
    private float _igniteRepeat = DefaultIgniteRepeat;
    private int _igniteDamage = DefaultIgniteDamage;

    public override void Load(bool hotReload)
    {
        LoadConfig();

        Core.Event.OnEntityIdentityAcceptInputHook += OnEntityIdentityAcceptInputHook;
        Core.Event.OnEntityCreated += OnEntityCreated;
        Core.Event.OnEntityDeleted += OnEntityDeleted;
        Core.Event.OnEntityStartTouch += OnEntityStartTouch;
        Core.Event.OnEntityEndTouch += OnEntityEndTouch;
        Core.Event.OnClientKeyStateChanged += OnClientKeyStateChanged;
        Core.Event.OnClientDisconnected += OnClientDisconnected;
        Core.Event.OnMapLoad += OnMapLoad;
        Core.Event.OnTick += OnTick;

        ResetRuntime();
    }

    public override void Unload()
    {
        Core.Event.OnEntityIdentityAcceptInputHook -= OnEntityIdentityAcceptInputHook;
        Core.Event.OnEntityCreated -= OnEntityCreated;
        Core.Event.OnEntityDeleted -= OnEntityDeleted;
        Core.Event.OnEntityStartTouch -= OnEntityStartTouch;
        Core.Event.OnEntityEndTouch -= OnEntityEndTouch;
        Core.Event.OnClientKeyStateChanged -= OnClientKeyStateChanged;
        Core.Event.OnClientDisconnected -= OnClientDisconnected;
        Core.Event.OnMapLoad -= OnMapLoad;
        Core.Event.OnTick -= OnTick;

        ResetRuntime();
    }

    private void OnMapLoad(IOnMapLoadEvent @event)
    {
        ResetRuntime();
        LoadMapGravity(@event.MapName);
    }

    private void OnEntityCreated(IOnEntityCreatedEvent @event)
    {
        var entity = @event.Entity;
        if (entity == null || !entity.IsValid || !entity.IsValidEntity || !entity.IsValidEntity)
        {
            return;
        }

        if (IsGameUi(entity))
        {
            _gameUiStates.Add(new GameUiState(entity.As<CLogicCase>()));
            return;
        }

        if (IsPointViewControl(entity))
        {
            _viewControls.Add(new ViewControlState(entity.As<CLogicRelay>()));
        }
    }

    private void OnEntityDeleted(IOnEntityDeletedEvent @event)
    {
        var entity = @event.Entity;
        if (entity == null)
        {
            return;
        }

        if (IsGameUi(entity))
        {
            var logicCase = entity.As<CLogicCase>();
            foreach (var state in _gameUiStates.Where(x => x.Entity == logicCase).ToList())
            {
                if (state.Activator != null)
                {
                    SafeAcceptInput(state.Entity, "Deactivate", state.Activator, state.Entity, null);
                }

                _gameUiStates.Remove(state);
            }

            return;
        }

        if (IsPointViewControl(entity))
        {
            var relay = entity.As<CLogicRelay>();
            foreach (var state in _viewControls.Where(x => x.Entity == relay).ToList())
            {
                DisableCameraAll(state);
                _viewControls.Remove(state);
            }

            return;
        }

        foreach (var viewControl in _viewControls)
        {
            if (viewControl.Target != null && viewControl.Target == entity)
            {
                DisableCameraAll(viewControl);
                viewControl.Target = null;
            }
        }
    }

    private void OnEntityStartTouch(IOnEntityStartTouchEvent @event)
    {
        var trigger = @event.Entity;
        var other = @event.OtherEntity;
        if (trigger == null || other == null || !trigger.IsValid  || !trigger.IsValidEntity || !other.IsValid || !other.IsValidEntity)
        {
            return;
        }

        if (!string.Equals(trigger.DesignerName, "trigger_gravity", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(other.DesignerName, "player", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var pawn = other.As<CCSPlayerPawn>();
        if (pawn == null || !pawn.IsValid || !pawn.IsValidEntity)
        {
            return;
        }

        var gravityScale = ResolveGravityScale(trigger);
        SetGravityScale(pawn, gravityScale);
    }

    private void OnEntityEndTouch(IOnEntityEndTouchEvent @event)
    {
        var trigger = @event.Entity;
        var other = @event.OtherEntity;
        if (trigger == null || other == null || !trigger.IsValid  || !trigger.IsValidEntity || !other.IsValid || !other.IsValidEntity)
        {
            return;
        }

        if (!string.Equals(trigger.DesignerName, "trigger_gravity", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(other.DesignerName, "player", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var pawn = other.As<CCSPlayerPawn>();
        if (pawn == null || !pawn.IsValid || !pawn.IsValidEntity)
        {
            return;
        }

        SetGravityScale(pawn, 1.0f);
    }

    private void OnClientKeyStateChanged(IOnClientKeyStateChangedEvent @event)
    {
        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player == null || !player.IsValid)
        {
            return;
        }

        var inputName = ConvertKeyToGameUiInput(@event.Key, @event.Pressed);
        if (string.IsNullOrEmpty(inputName))
        {
            return;
        }

        foreach (var state in _gameUiStates)
        {
            if (state.Activator == null || !state.Activator.IsValid || !state.Activator.IsValidEntity)
            {
                continue;
            }

            var activatorPlayer = ResolvePlayer(state.Activator);
            if (activatorPlayer == null || !activatorPlayer.IsValid  || activatorPlayer.PlayerID != player.PlayerID)
            {
                continue;
            }

            if ((state.Entity.Spawnflags & 256) != 0 && @event.Pressed && IsJumpKey(@event.Key))
            {
                SafeAcceptInput(state.Entity, "Deactivate", state.Activator, state.Entity, null);
                continue;
            }

            SafeAcceptInput(state.Entity, "InValue", state.Activator, state.Entity, inputName);
        }
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player == null || !player.IsValid)
        {
            return;
        }

        foreach (var state in _gameUiStates)
        {
            if (state.Activator == null || !state.Activator.IsValid || !state.Activator.IsValidEntity)
            {
                continue;
            }

            var activatorPlayer = ResolvePlayer(state.Activator);
            if (activatorPlayer != null && activatorPlayer.PlayerID == player.PlayerID)
            {
                SafeAcceptInput(state.Entity, "Deactivate", state.Activator, state.Entity, null);
            }
        }

        foreach (var vc in _viewControls)
        {
            vc.ActivePlayerIds.Remove(player.PlayerID);
        }

        _igniteStates.TryRemove(player.PlayerID, out _);
    }

    private void OnTick()
    {
        var now = DateTime.UtcNow;

        foreach (var state in _igniteStates.Values.ToList())
        {
            if (state.Player == null || !state.Player.IsValid || state.Player.PlayerPawn == null || !state.Player.PlayerPawn.IsValid || !state.Player.PlayerPawn.IsValidEntity)
            {
                _igniteStates.TryRemove(state.PlayerId, out _);
                continue;
            }

            if (now >= state.EndAt)
            {
                state.Player.PlayerPawn.VelocityModifier = 1.0f;
                _igniteStates.TryRemove(state.PlayerId, out _);
                continue;
            }

            if (now < state.NextTickAt)
            {
                continue;
            }

            state.NextTickAt = now.AddSeconds(_igniteRepeat);
            var pawn = state.Player.PlayerPawn;
            pawn.VelocityModifier *= _igniteVelocity;
            pawn.VelocityModifierUpdated();
            pawn.Health -= _igniteDamage;
            pawn.HealthUpdated();
            if (pawn.Health <= 0)
            {
                pawn.CommitSuicide(true, true);
            }
        }

        foreach (var viewControl in _viewControls)
        {
            var target = ResolveOrRefreshTarget(viewControl);
            if (target == null || !target.IsValid || !target.IsValidEntity)
            {
                continue;
            }

            foreach (var playerId in viewControl.ActivePlayerIds.ToList())
            {
                var player = Core.PlayerManager.GetPlayer(playerId);
                if (player == null || !player.IsValid)
                {
                    viewControl.ActivePlayerIds.Remove(playerId);
                    continue;
                }

                UpdateViewControlPlayerState(viewControl, player, true);
            }
        }
    }

    private void OnEntityIdentityAcceptInputHook(IOnEntityIdentityAcceptInputHookEvent @event)
    {
        var input = @event.InputName;
        if (string.IsNullOrWhiteSpace(input) || @event.EntityInstance == null || !@event.EntityInstance.IsValid || !@event.EntityInstance.IsValidEntity)
        {
            return;
        }

        var entity = @event.EntityInstance;
        var valueText = TryToString(@event.VariantValue);

        if (input.StartsWith("ignitel", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
            {
                StartIgnite(@event.Activator, duration);
            }

            return;
        }

        if (string.Equals(entity.DesignerName, "game_player_equip", StringComparison.OrdinalIgnoreCase))
        {
            HandleGamePlayerEquipInput(entity.As<CGamePlayerEquip>(), input, valueText, @event.Activator);
            return;
        }

        if (IsGameUi(entity))
        {
            HandleGameUiInput(entity.As<CLogicCase>(), input, @event.Activator);
            return;
        }

        if (IsPointViewControl(entity))
        {
            HandlePointViewControlInput(entity.As<CLogicRelay>(), input, @event.Activator);
        }
    }

    private void HandleGamePlayerEquipInput(CGamePlayerEquip equip, string input, string valueText, CEntityInstance? activator)
    {
        const uint SF_PLAYEREQUIP_STRIPFIRST = 0x0002;

        if ((equip.Spawnflags & SF_PLAYEREQUIP_STRIPFIRST) == 0)
        {
            return;
        }

        if (string.Equals(input, "Use", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "TriggerForActivatedPlayer", StringComparison.OrdinalIgnoreCase))
        {
            var player = ResolvePlayer(activator);
            if (player?.PlayerPawn == null || !player.PlayerPawn.IsValid)
            {
                return;
            }

            player.PlayerPawn.ItemServices?.RemoveItems();
            if (string.Equals(input, "TriggerForActivatedPlayer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(valueText))
            {
                player.PlayerPawn.ItemServices?.GiveItem(valueText);
            }

            return;
        }

        if (string.Equals(input, "TriggerForAllPlayers", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var player in Core.PlayerManager.GetAllValidPlayers())
            {
                if (player.PlayerPawn == null || !player.PlayerPawn.IsValid || !player.PlayerPawn.IsValidEntity)
                {
                    continue;
                }

                player.PlayerPawn.ItemServices?.RemoveItems();
            }
        }
    }

    private void HandleGameUiInput(CLogicCase gameUi, string input, CEntityInstance? activator)
    {
        if (string.Equals(input, "Activate", StringComparison.OrdinalIgnoreCase))
        {
            OnGameUiActivation(gameUi, activator, true);
            return;
        }

        if (string.Equals(input, "Deactivate", StringComparison.OrdinalIgnoreCase))
        {
            OnGameUiActivation(gameUi, activator, false);
        }
    }

    private void HandlePointViewControlInput(CLogicRelay relay, string input, CEntityInstance? activator)
    {
        var state = _viewControls.FirstOrDefault(x => x.Entity == relay);
        if (state == null)
        {
            return;
        }

        _ = ResolveOrRefreshTarget(state);

        if (string.Equals(input, "EnableCamera", StringComparison.OrdinalIgnoreCase))
        {
            var player = ResolvePlayer(activator);
            if (player != null)
            {
                state.ActivePlayerIds.Add(player.PlayerID);
                UpdateViewControlPlayerState(state, player, true);
            }

            return;
        }

        if (string.Equals(input, "DisableCamera", StringComparison.OrdinalIgnoreCase))
        {
            var player = ResolvePlayer(activator);
            if (player != null)
            {
                state.ActivePlayerIds.Remove(player.PlayerID);
                UpdateViewControlPlayerState(state, player, false);
            }

            return;
        }

        if (string.Equals(input, "EnableCameraAll", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var player in Core.PlayerManager.GetAllValidPlayers())
            {
                state.ActivePlayerIds.Add(player.PlayerID);
                UpdateViewControlPlayerState(state, player, true);
            }

            return;
        }

        if (string.Equals(input, "DisableCameraAll", StringComparison.OrdinalIgnoreCase))
        {
            DisableCameraAll(state);
        }
    }

    private void OnGameUiActivation(CLogicCase gameUi, CEntityInstance? activator, bool activate)
    {
        if (activator == null || !activator.IsValid || !activator.IsValidEntity)
        {
            return;
        }

        var state = _gameUiStates.FirstOrDefault(x => x.Entity == gameUi);
        if (state == null)
        {
            state = new GameUiState(gameUi);
            _gameUiStates.Add(state);
        }

        if ((gameUi.Spawnflags & 32) != 0)
        {
            var player = ResolvePlayer(activator);
            if (player?.PlayerPawn != null && player.PlayerPawn.IsValid)
            {
                if (activate)
                {
                    player.PlayerPawn.Flags |= FlagAtControls;
                }
                else
                {
                    player.PlayerPawn.Flags &= ~FlagAtControls;
                }
            }
        }

        state.Activator = activate ? activator : null;
        SafeAcceptInput(gameUi, "InValue", activator, gameUi, activate ? "PlayerOn" : "PlayerOff");
    }

    private void StartIgnite(CEntityInstance? activator, float duration)
    {
        if (activator == null || !activator.IsValid || !activator.IsValidEntity)
        {
            return;
        }

        var player = ResolvePlayer(activator);
        if (player?.PlayerPawn == null || !player.PlayerPawn.IsValid)
        {
            return;
        }

        var endAt = DateTime.UtcNow.AddSeconds(duration);
        var playerId = player.PlayerID;

        if (_igniteStates.TryGetValue(playerId, out var existing))
        {
            if (existing.EndAt < endAt)
            {
                existing.EndAt = endAt;
            }

            return;
        }

        _igniteStates[playerId] = new IgniteState
        {
            Player = player,
            PlayerId = playerId,
            EndAt = endAt,
            NextTickAt = DateTime.UtcNow
        };

        TryAttachIgniteParticle(player.PlayerPawn);
    }

    private void TryAttachIgniteParticle(CCSPlayerPawn pawn)
    {
        try
        {
            var particle = Core.EntitySystem.CreateEntityByDesignerName<CParticleSystem>("info_particle_system");
            if (particle == null || !particle.IsValid || !particle.IsValidEntity)
            {
                return;
            }

            particle.EffectName = _igniteParticle;
            particle.StartActive = true;
            particle.Teleport(pawn.AbsOrigin, pawn.AbsRotation, pawn.AbsVelocity);
            particle.DispatchSpawn();
            Core.Scheduler.DelayBySeconds(0.15f, () =>
            {
                if (particle.IsValid && particle.IsValidEntity)
                {
                    particle.AcceptInput("Kill", string.Empty, null, null);
                }
            });
        }
        catch
        {
        }
    }

    private void UpdateViewControlPlayerState(ViewControlState state, IPlayer player, bool enable)
    {
        var target = ResolveOrRefreshTarget(state);
        if (target == null || !target.IsValid || !target.IsValidEntity)
        {
            return;
        }

        if (player.Controller == null || !player.Controller.IsValid || player.PlayerPawn == null || !player.PlayerPawn.IsValid || !player.Controller.IsValidEntity)
        {
            return;
        }

        var pawn = player.PlayerPawn;
        if (pawn.CameraServices == null)
        {
            return;
        }

        if (enable)
        {
            pawn.CameraServices.ViewEntity.Raw = target.Entity!.EntityHandle.Raw;
        }
        else
        {
            pawn.CameraServices.ViewEntity.Raw = uint.MaxValue;
        }

        if ((state.Entity.Spawnflags & 64) != 0)
        {
            player.Controller.DesiredFOV = enable && state.Entity.Health is >= 16 and <= 179
                ? (uint)state.Entity.Health
                : 90;
            player.Controller.DesiredFOVUpdated();
        }

        if ((state.Entity.Spawnflags & 32) != 0)
        {
            if (enable)
            {
                pawn.Flags |= FlagFrozen;
            }
            else
            {
                pawn.Flags &= ~FlagFrozen;
            }
        }

        if ((state.Entity.Spawnflags & 128) != 0 && enable)
        {
            pawn.WeaponServices?.ActiveWeapon.Value?.AcceptInput("Disable", string.Empty, null, null);
        }
    }

    private void DisableCameraAll(ViewControlState state)
    {
        foreach (var playerId in state.ActivePlayerIds.ToList())
        {
            var player = Core.PlayerManager.GetPlayer(playerId);
            if (player != null && player.IsValid)
            {
                UpdateViewControlPlayerState(state, player, false);
            }
        }

        state.ActivePlayerIds.Clear();
    }

    private CEntityInstance? ResolveOrRefreshTarget(ViewControlState state)
    {
        if (state.Target != null && state.Target.IsValid && state.Target.IsValidEntity)
        {
            return state.Target;
        }

        if (string.IsNullOrWhiteSpace(state.Entity.Target))
        {
            return null;
        }

        state.Target = Core.EntitySystem.GetAllEntities().FirstOrDefault(entity =>
            entity.IsValid &&
            entity.IsValidEntity &&
            entity.Entity != null &&
            string.Equals(entity.Entity.Name, state.Entity.Target, StringComparison.Ordinal));

        return state.Target;
    }

    private float ResolveGravityScale(CBaseEntity trigger)
    {
        if (_mapGravity != null && !string.IsNullOrWhiteSpace(trigger.UniqueHammerID) && _mapGravity.TryGetValue(trigger.UniqueHammerID, out var value))
        {
            return value;
        }

        if (trigger.GravityScale > 0.0f)
        {
            return trigger.GravityScale;
        }

        return DefaultTriggerGravity;
    }

    private void SetGravityScale(CBaseEntity entity, float gravityScale)
    {
        entity.GravityScale = gravityScale;
        entity.GravityScaleUpdated();
        entity.ActualGravityScale = gravityScale;
    }

    private IPlayer? ResolvePlayer(CEntityInstance? entity)
    {
        if (entity == null || !entity.IsValid  || !entity.IsValidEntity || !string.Equals(entity.DesignerName, "player", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pawn = entity.As<CCSPlayerPawn>();
        if (pawn == null || !pawn.IsValid || !pawn.IsValidEntity)
        {
            return null;
        }

        return Core.PlayerManager.GetPlayerFromPawn(pawn);
    }

    private static bool IsGameUi(CEntityInstance entity)
    {
        return entity != null &&
               entity.IsValid &&
               entity.IsValidEntity &&
               string.Equals(entity.DesignerName, "logic_case", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(entity.PrivateVScripts, "game_ui", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPointViewControl(CEntityInstance entity)
    {
        return entity != null &&
               entity.IsValid &&
               entity.IsValidEntity &&
               string.Equals(entity.DesignerName, "logic_relay", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(entity.PrivateVScripts, "point_viewcontrol", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJumpKey(KeyKind key)
    {
        return string.Equals(key.ToString(), "Space", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ConvertKeyToGameUiInput(KeyKind key, bool pressed)
    {
        var status = pressed ? "Pressed" : "Unpressed";
        return key.ToString() switch
        {
            "W" => $"{status}Forward",
            "S" => $"{status}Back",
            "A" or "A2" => $"{status}MoveLeft",
            "D" or "D2" => $"{status}MoveRight",
            "Mouse1" => $"{status}Attack",
            "Mouse2" => $"{status}Attack2",
            "Shift" or "Shift2" or "UnknownKeySpeed" => $"{status}Speed",
            "Ctrl" => $"{status}Duck",
            "E" => $"{status}Use",
            "R" => $"{status}Reload",
            "Alt" or "Alt2" => $"{status}Look",
            _ => null
        };
    }

    private void SafeAcceptInput(CEntityInstance entity, string input, CEntityInstance? activator, CEntityInstance? caller, string? value)
    {
        try
        {
            entity.AcceptInput(input, value ?? string.Empty, activator, caller);
        }
        catch
        {
        }
    }

    private static string TryToString(CVariant<CVariantDefaultAllocator> variant)
    {
        if (variant.TryGetString(out string? s) && !string.IsNullOrEmpty(s))
        {
            return s;
        }

        if (variant.TryGetFloat(out var f))
        {
            return f.ToString(CultureInfo.InvariantCulture);
        }

        if (variant.TryGetDouble(out var d))
        {
            return d.ToString(CultureInfo.InvariantCulture);
        }

        if (variant.TryGetInt32(out var i))
        {
            return i.ToString(CultureInfo.InvariantCulture);
        }

        if (variant.TryGetUInt32(out var ui))
        {
            return ui.ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private void LoadConfig()
    {
        var configPath = Path.Combine(Core.PluginPath, "resources", "config", "config.json");
        if (!File.Exists(configPath))
        {
            _config = new ConfigModel();
            ApplyConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            _config = JsonSerializer.Deserialize<ConfigModel>(json) ?? new ConfigModel();
        }
        catch
        {
            _config = new ConfigModel();
        }

        ApplyConfig();
    }

    private void ApplyConfig()
    {
        _igniteVelocity = _config.IgniteVelocity is >= 0.001f and <= 1.0f ? _config.IgniteVelocity : DefaultIgniteVelocity;
        _igniteRepeat = _config.IgniteRepeat is >= 0.1f and <= 1.0f ? _config.IgniteRepeat : DefaultIgniteRepeat;
        _igniteDamage = _config.IgniteDamage is >= 1 and <= 1000 ? _config.IgniteDamage : DefaultIgniteDamage;
        _igniteParticle = string.IsNullOrWhiteSpace(_config.IgniteParticle) ? DefaultIgniteParticle : _config.IgniteParticle;
    }

    private void LoadMapGravity(string mapName)
    {
        _mapGravity = null;
        var mapPath = Path.Combine(Core.PluginPath, "resources", "maps", $"{mapName}.json");
        if (!File.Exists(mapPath))
        {
            return;
        }

        try
        {
            _mapGravity = JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(mapPath));
        }
        catch
        {
            _mapGravity = null;
        }
    }

    private void ResetRuntime()
    {
        _gameUiStates.Clear();
        _viewControls.Clear();
        _igniteStates.Clear();
    }

    private sealed class GameUiState(CLogicCase entity)
    {
        public CLogicCase Entity { get; } = entity;
        public CEntityInstance? Activator { get; set; }
    }

    private sealed class ViewControlState(CLogicRelay entity)
    {
        public CLogicRelay Entity { get; } = entity;
        public CEntityInstance? Target { get; set; }
        public HashSet<int> ActivePlayerIds { get; } = [];
    }

    private sealed class IgniteState
    {
        public required IPlayer Player { get; init; }
        public required int PlayerId { get; init; }
        public required DateTime EndAt { get; set; }
        public required DateTime NextTickAt { get; set; }
    }

    private sealed class ConfigModel
    {
        public float IgniteVelocity { get; set; } = DefaultIgniteVelocity;
        public float IgniteRepeat { get; set; } = DefaultIgniteRepeat;
        public int IgniteDamage { get; set; } = DefaultIgniteDamage;
        public string IgniteParticle { get; set; } = DefaultIgniteParticle;
    }
}
