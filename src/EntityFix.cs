using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Convars;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2.EntityFix.SwiftlyS2;

[PluginMetadata(
    Id = "CS2-EntityFix-SW2",
    Name = "CS2 EntityFix SW2",
    Author = "DarkerZ",
    Version = "1.DZ.15a",
    Description = "Fixes game_player_equip, game_ui, IgniteLifeTime, point_viewcontrol, trigger_gravity",
    Website = "https://github.com/darkerz7/CS2-EntityFix"
)]
public partial class EntityFix(ISwiftlyCore core) : BasePlugin(core)
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CTriggerGravity_GravityTouchDelegate(nint pEntity, nint pOther);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CBaseEntity_SetGravityScaleDelegate(nint pEntity, float flGravityScale);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CBaseFilter_InputTestActivatorDelegate(nint pEntity, nint pInputData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CTriggerGravity_EndTouchDelegate(nint pEntity, nint pOther);

    [StructLayout(LayoutKind.Sequential)]
    private struct InputData_t
    {
        public nint pActivator;
        public nint pCaller;
        public nint value;
        public int nOutputID;
    }

    private class ConfigData
    {
        public float Ignite_Velocity { get; set; } = 0.45f;
        public float Ignite_Repeat { get; set; } = 0.5f;
        public int Ignite_Damage { get; set; } = 1;
        public string Ignite_Particle { get; set; } = "particles/burning_fx/env_fire_small.vpcf";
    }

    private class GameUIState(CLogicCase gameUI)
    {
        public CEntityInstance? Activator;
        public CLogicCase GameUI = gameUI;
    }

    private class IgniteState(CEntityInstance entity, CParticleSystem? particle, DateTime endTime)
    {
        public CEntityInstance Entity = entity;
        public CParticleSystem? Particle = particle;
        public DateTime EndTime = endTime;
        public bool Active = true;
    }

    private class ViewControlState(CLogicRelay viewControl)
    {
        public CLogicRelay ViewControl = viewControl;
        public CEntityInstance? Target;
        public List<CCSPlayerController> Players = [];
    }

    [Flags]
    private enum PlayerButtons : uint
    {
        Attack = 1 << 0,
        Jump = 1 << 1,
        Duck = 1 << 2,
        Forward = 1 << 3,
        Back = 1 << 4,
        Use = 1 << 5,
        Left = 1 << 7,
        Right = 1 << 8,
        Moveleft = 1 << 9,
        Moveright = 1 << 10,
        Attack2 = 1 << 11,
        Reload = 1 << 13,
        Speed = 1 << 17,
        Look = 1 << 28
    }

    [Flags]
    private enum PlayerFlags : uint
    {
        FL_FROZEN = 1 << 5,
        FL_ATCONTROLS = 1 << 6
    }

    private readonly List<GameUIState> _gameUis = [];
    private readonly List<IgniteState> _ignites = [];
    private readonly Dictionary<nint, ViewControlState> _viewControls = [];
    private readonly ConcurrentDictionary<int, uint> _lastButtons = new();

    private ConfigData _config = new();
    private Dictionary<string, float>? _gravityConfig;
    private string? _currentMapName;
    private string _pluginDirectory = string.Empty;

    private IConVar<bool>? _reloadConVar;

    private IUnmanagedFunction<CTriggerGravity_GravityTouchDelegate>? _gravityTouchFunc;
    private Guid? _gravityTouchHookId;
    private IUnmanagedFunction<CBaseEntity_SetGravityScaleDelegate>? _setGravityScaleFunc;
    private IUnmanagedFunction<CBaseFilter_InputTestActivatorDelegate>? _inputTestActivatorFunc;
    private Guid? _inputTestActivatorHookId;
    private IUnmanagedFunction<CTriggerGravity_EndTouchDelegate>? _endTouchFunc;
    private Guid? _endTouchHookId;

    private Guid? _roundStartHookId;
    private Guid? _roundEndHookId;
    private Guid? _playerDeathHookId;
    private Guid? _playerDisconnectHookId;

    public override void Load(bool hotReload)
    {
        _pluginDirectory = Path.GetDirectoryName(GetType().Assembly.Location) ?? AppContext.BaseDirectory;
        LoadConfig();
        EnsureGravityConfigLoaded();

        _reloadConVar = Core.ConVar.CreateOrFind("css_entityfix_reload", "Reload config file of EntityFix", false, ConvarFlags.SERVER_CAN_EXECUTE);
        Core.Event.OnConVarValueChanged += OnConVarValueChanged;

        _setGravityScaleFunc = Core.Memory.GetUnmanagedFunctionByAddress<CBaseEntity_SetGravityScaleDelegate>(Core.GameData.GetSignature("CBaseEntity::SetGravityScale"));

        InstallInputTestActivatorHook();
        InstallGravityTouchHook();
        InstallEndTouchHook();

        Core.Event.OnEntityIdentityAcceptInputHook += OnEntityIdentityAcceptInputHook;
        Core.Event.OnClientProcessUsercmds += OnClientProcessUsercmds;
        Core.Event.OnClientDisconnected += OnClientDisconnected;

        _roundStartHookId = Core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
        _roundEndHookId = Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
        _playerDeathHookId = Core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
        _playerDisconnectHookId = Core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
    }

    public override void Unload()
    {
        Core.Event.OnConVarValueChanged -= OnConVarValueChanged;
        Core.Event.OnEntityIdentityAcceptInputHook -= OnEntityIdentityAcceptInputHook;
        Core.Event.OnClientProcessUsercmds -= OnClientProcessUsercmds;
        Core.Event.OnClientDisconnected -= OnClientDisconnected;

        if (_gravityTouchHookId.HasValue)
            _gravityTouchFunc?.RemoveHook(_gravityTouchHookId.Value);
        if (_inputTestActivatorHookId.HasValue)
            _inputTestActivatorFunc?.RemoveHook(_inputTestActivatorHookId.Value);
        if (_endTouchHookId.HasValue)
            _endTouchFunc?.RemoveHook(_endTouchHookId.Value);

        if (_roundStartHookId.HasValue)
            Core.GameEvent.Unhook(_roundStartHookId.Value);
        if (_roundEndHookId.HasValue)
            Core.GameEvent.Unhook(_roundEndHookId.Value);
        if (_playerDeathHookId.HasValue)
            Core.GameEvent.Unhook(_playerDeathHookId.Value);
        if (_playerDisconnectHookId.HasValue)
            Core.GameEvent.Unhook(_playerDisconnectHookId.Value);

        ClearAllIgnites();
        _gameUis.Clear();
        _viewControls.Clear();
        _lastButtons.Clear();
    }

    private void InstallInputTestActivatorHook()
    {
        _inputTestActivatorFunc = Core.Memory.GetUnmanagedFunctionByAddress<CBaseFilter_InputTestActivatorDelegate>(Core.GameData.GetSignature("CBaseFilter::InputTestActivator"));
        if (_inputTestActivatorFunc == null)
            return;

        _inputTestActivatorHookId = _inputTestActivatorFunc.AddHook(original =>
        {
            return (nint pEntity, nint pInputData) =>
            {
                try
                {
                    var input = Marshal.PtrToStructure<InputData_t>(pInputData);
                    if (input.pActivator == nint.Zero)
                        return;
                    original()(pEntity, pInputData);
                }
                catch
                {
                    original()(pEntity, pInputData);
                }
            };
        });
    }

    private void InstallGravityTouchHook()
    {
        _gravityTouchFunc = Core.Memory.GetUnmanagedFunctionByAddress<CTriggerGravity_GravityTouchDelegate>(Core.GameData.GetSignature("CTriggerGravity::GravityTouch"));
        if (_gravityTouchFunc == null)
            return;

        _gravityTouchHookId = _gravityTouchFunc.AddHook(original =>
        {
            return (nint pEntity, nint pOther) =>
            {
                if (HandleGravityTouch(pEntity, pOther))
                    return;
                original()(pEntity, pOther);
            };
        });
    }

    private void InstallEndTouchHook()
    {
        var vtable = Core.Memory.GetVTableAddress("server", "CTriggerGravity");
        if (!vtable.HasValue)
            return;

        int offset = Core.GameData.GetOffset("CBaseEntity::EndTouch");
        if (offset == -1)
            return;

        _endTouchFunc = Core.Memory.GetUnmanagedFunctionByVTable<CTriggerGravity_EndTouchDelegate>(vtable.Value, offset);
        if (_endTouchFunc == null)
            return;

        _endTouchHookId = _endTouchFunc.AddHook(original =>
        {
            return (nint pEntity, nint pOther) =>
            {
                original()(pEntity, pOther);
                HandleGravityEndTouch(pEntity, pOther);
            };
        });
    }

    private void OnEntityIdentityAcceptInputHook(IOnEntityIdentityAcceptInputHookEvent @event)
    {
        var input = @event.InputName;
        if (string.IsNullOrEmpty(input))
            return;

        var identity = @event.Identity;
        if (identity == null || !identity.IsValid)
            return;

        var entityInstance = identity.EntityInstance;
        if (entityInstance == null || !entityInstance.IsValid)
            return;

        var activator = @event.Activator;
        var value = TryToString(@event.VariantValue);

        if (input.Contains("ignitel", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(value, out float duration))
                IgnitePawn(activator, duration);
            return;
        }

        if (string.Equals(identity.DesignerName, "game_player_equip", StringComparison.OrdinalIgnoreCase))
        {
            HandleGamePlayerEquipInput(entityInstance, activator, input, value);
            return;
        }

        if (IsGameUI(entityInstance))
        {
            if (string.Equals(input, "activate", StringComparison.OrdinalIgnoreCase))
                OnGameUI(activator, entityInstance.As<CLogicCase>(), true);
            else if (string.Equals(input, "deactivate", StringComparison.OrdinalIgnoreCase))
                OnGameUI(activator, entityInstance.As<CLogicCase>(), false);
            return;
        }

        if (IsViewControl(entityInstance))
        {
            HandleViewControlInput(entityInstance.As<CLogicRelay>(), activator, input);
        }
    }

    private void HandleGamePlayerEquipInput(CEntityInstance entity, CEntityInstance? activator, string input, string? value)
    {
        var equip = entity.As<CGamePlayerEquip>();
        if (equip == null || !equip.IsValid)
            return;

        const uint SF_PLAYEREQUIP_STRIPFIRST = 0x0002;
        if ((equip.Spawnflags & SF_PLAYEREQUIP_STRIPFIRST) == 0)
            return;

        if (string.Equals(input, "use", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "triggerforactivatedplayer", StringComparison.OrdinalIgnoreCase))
        {
            var player = EntityToPlayer(activator);
            if (player != null && player.IsValid && player.PlayerPawn.Value != null && player.PlayerPawn.Value.Valid() && player.PlayerPawn.Value.IsPlayerAlive())
            {
                var itemServices = player.PlayerPawn.Value.ItemServices;
                itemServices?.RemoveItems();
                if (string.Equals(input, "triggerforactivatedplayer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                    itemServices?.GiveItem(value);
            }
        }
        else if (string.Equals(input, "triggerforallplayers", StringComparison.OrdinalIgnoreCase))
        {
            var players = Core.PlayerManager.GetAllValidPlayers();
            foreach (var p in players)
            {
                var pawn = p.PlayerPawn;
                if (pawn.Valid() && pawn.IsPlayerAlive())
                {
                    pawn.ItemServices?.RemoveItems();
                }
            }
        }
    }

    private void HandleViewControlInput(CLogicRelay relay, CEntityInstance? activator, string input)
    {
        if (relay == null || !relay.IsValid)
            return;

        var state = GetOrCreateViewControlState(relay);
        if (state.Target == null || !state.Target.IsValid)
        {
            state.Target = FindEntityByName(relay.Target);
        }

        switch (input.ToLowerInvariant())
        {
            case "enablecamera":
                {
                    var player = EntityToPlayer(activator);
                    if (player != null)
                        EnableCamera(state, player);
                }
                break;
            case "disablecamera":
                {
                    var player = EntityToPlayer(activator);
                    if (player != null)
                        DisableCamera(state, player);
                }
                break;
            case "enablecameraall":
                EnableCameraAll(state);
                break;
            case "disablecameraall":
                DisableCameraAll(state);
                break;
        }
    }

    private ViewControlState GetOrCreateViewControlState(CLogicRelay relay)
    {
        if (_viewControls.TryGetValue(relay.Address, out var existing))
            return existing;

        var state = new ViewControlState(relay);
        _viewControls[relay.Address] = state;
        return state;
    }

    private void EnableCamera(ViewControlState state, CCSPlayerController controller)
    {
        if (!state.Players.Contains(controller))
            state.Players.Add(controller);
        UpdateViewControlState(state, controller, true);
    }

    private void DisableCamera(ViewControlState state, CCSPlayerController controller)
    {
        state.Players.Remove(controller);
        UpdateViewControlState(state, controller, false);
    }

    private void EnableCameraAll(ViewControlState state)
    {
        var players = Core.PlayerManager.GetAllValidPlayers();
        foreach (var p in players)
        {
            if (p.Controller is { IsValid: true } controller)
                EnableCamera(state, controller);
        }
    }

    private void DisableCameraAll(ViewControlState state)
    {
        foreach (var controller in state.Players.ToList())
        {
            if (controller is { IsValid: true })
                UpdateViewControlState(state, controller, false);
        }
        state.Players.Clear();
    }

    private void UpdateViewControlState(ViewControlState state, CCSPlayerController controller, bool enable)
    {
        if (controller == null || !controller.IsValid || state.Target == null || !state.Target.IsValid)
            return;

        var pawn = controller.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        var cameraServices = pawn.CameraServices;
        if (cameraServices != null)
        {
            cameraServices.ViewEntity.Raw = enable ? state.Target.Entity!.EntityHandle.Raw : uint.MaxValue;
        }

        if ((state.ViewControl.Spawnflags & 64) != 0)
        {
            if (enable && state.ViewControl.Health is >= 16 and <= 179)
                controller.DesiredFOV = (uint)state.ViewControl.Health;
            else
                controller.DesiredFOV = 90;
            controller.DesiredFOVUpdated();
        }

        if ((state.ViewControl.Spawnflags & 32) != 0)
        {
            if (enable)
                pawn.Flags |= (uint)PlayerFlags.FL_FROZEN;
            else
                pawn.Flags &= ~(uint)PlayerFlags.FL_FROZEN;
        }

        if ((state.ViewControl.Spawnflags & 128) != 0 && enable)
        {
            var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon != null && activeWeapon.IsValid)
            {
                activeWeapon.NextPrimaryAttackTick = Math.Max(activeWeapon.NextPrimaryAttackTick, Core.Engine.Server.TickCount + 24);
                activeWeapon.NextSecondaryAttackTick = Math.Max(activeWeapon.NextSecondaryAttackTick, Core.Engine.Server.TickCount + 24);
                activeWeapon.NextPrimaryAttackTickUpdated();
                activeWeapon.NextSecondaryAttackTickUpdated();
            }
        }
    }

    private void OnGameUI(CEntityInstance? activator, CLogicCase gameUI, bool activate)
    {
        if (activator == null || !activator.IsValid || gameUI == null || !gameUI.IsValid)
            return;

        if ((gameUI.Spawnflags & 32) != 0)
        {
            var player = EntityToPlayer(activator);
            if (player != null && player.PlayerPawn.Value != null)
            {
                if (activate)
                    player.PlayerPawn.Value.Flags |= (uint)PlayerFlags.FL_ATCONTROLS;
                else
                    player.PlayerPawn.Value.Flags &= ~(uint)PlayerFlags.FL_ATCONTROLS;
            }
        }

        var state = _gameUis.FirstOrDefault(g => g.GameUI == gameUI);
        if (state == null)
        {
            state = new GameUIState(gameUI);
            _gameUis.Add(state);
        }

        state.Activator = activate ? activator : null;

        Core.Scheduler.NextWorldUpdate(() =>
        {
            if (activator != null && activator.IsValid && gameUI != null && gameUI.IsValid)
                TryAcceptInput(gameUI, "InValue", activator, gameUI, activate ? "PlayerOn" : "PlayerOff");
        });
    }

    private void OnClientProcessUsercmds(IOnClientProcessUsercmdsEvent @event)
    {
        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player == null || !player.IsValid || player.Controller is not { IsValid: true } controller)
            return;

        var currentButtons = GetCurrentButtons(@event.Usercmds);
        var lastButtons = _lastButtons.GetOrAdd(player.UserID, 0);
        var pressed = currentButtons & ~lastButtons;
        var released = lastButtons & ~currentButtons;
        _lastButtons[player.UserID] = currentButtons;

        foreach (var state in _gameUis.ToList())
        {
            if (state.GameUI == null || !state.GameUI.IsValid)
            {
                _gameUis.Remove(state);
                continue;
            }

            var activatorPlayer = EntityToPlayer(state.Activator);
            if (activatorPlayer == null || activatorPlayer != controller)
                continue;

            if ((state.GameUI.Spawnflags & 256) != 0 && (pressed & (uint)PlayerButtons.Jump) != 0)
            {
                TryAcceptInput(state.GameUI, "Deactivate", state.Activator, state.GameUI, null);
                continue;
            }

            HandleGameUIButton(state, pressed, released, PlayerButtons.Forward, "PressedForward", "UnpressedForward");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Moveleft, "PressedMoveLeft", "UnpressedMoveLeft");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Back, "PressedBack", "UnpressedBack");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Moveright, "PressedMoveRight", "UnpressedMoveRight");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Attack, "PressedAttack", "UnpressedAttack");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Attack2, "PressedAttack2", "UnpressedAttack2");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Speed, "PressedSpeed", "UnpressedSpeed");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Duck, "PressedDuck", "UnpressedDuck");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Use, "PressedUse", "UnpressedUse");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Reload, "PressedReload", "UnpressedReload");
            HandleGameUIButton(state, pressed, released, PlayerButtons.Look, "PressedLook", "UnpressedLook");
        }

        foreach (var vc in _viewControls.Values.ToList())
        {
            if (vc.ViewControl == null || !vc.ViewControl.IsValid)
            {
                _viewControls.Remove(vc.ViewControl.Address);
                continue;
            }

            if (vc.Players.Contains(controller))
                UpdateViewControlState(vc, controller, true);
        }
    }

    private void HandleGameUIButton(GameUIState state, uint pressed, uint released, PlayerButtons button, string pressedValue, string releasedValue)
    {
        if ((pressed & (uint)button) != 0)
            TryAcceptInput(state.GameUI, "InValue", state.Activator, state.GameUI, pressedValue);
        if ((released & (uint)button) != 0)
            TryAcceptInput(state.GameUI, "InValue", state.Activator, state.GameUI, releasedValue);
    }

    private uint GetCurrentButtons(IList<CSGOUserCmdPB> cmds)
    {
        if (cmds == null || cmds.Count == 0)
            return 0;

        var cmd = cmds[^1];
        return (uint)cmd.Buttons;
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player == null || !player.IsValid || player.Controller is not { IsValid: true } controller)
            return;

        OnGameUIEventDeactivate(controller);
        foreach (var vc in _viewControls.Values.ToList())
        {
            if (vc.Target != null && vc.Target.IsValid)
                DisableCamera(vc, controller);
        }

        _lastButtons.TryRemove(player.UserID, out _);
    }

    private HookResult OnRoundStart(EventRoundStart @event)
    {
        EnsureGravityConfigLoaded();
        ClearAllIgnites();
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event)
    {
        _gameUis.Clear();
        _viewControls.Clear();
        ClearAllIgnites();
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event)
    {
        OnGameUIEventDeactivate(@event.UserIdPlayer);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event)
    {
        OnGameUIEventDeactivate(@event.UserIdPlayer);
        if (@event.UserIdPlayer != null && @event.UserIdPlayer.IsValid)
        {
            foreach (var vc in _viewControls.Values.ToList())
            {
                if (vc.Target != null && vc.Target.IsValid)
                    DisableCamera(vc, @event.UserIdPlayer);
            }
        }
        return HookResult.Continue;
    }

    private void OnGameUIEventDeactivate(CCSPlayerController? controller)
    {
        if (controller == null || !controller.IsValid || controller.Pawn == null || !controller.Pawn.IsValid)
            return;

        foreach (var state in _gameUis.ToList())
        {
            if (state.Activator?.Index == controller.Pawn.Index)
                TryAcceptInput(state.GameUI, "Deactivate", state.Activator, state.GameUI, null);
        }
    }

    private bool HandleGravityTouch(nint pEntity, nint pOther)
    {
        try
        {
            var trigger = Core.Memory.ToSchemaClass<CBaseEntity>(pEntity);
            var other = Core.Memory.ToSchemaClass<CBaseEntity>(pOther);
            if (trigger == null || other == null || !trigger.IsValid || !other.IsValid)
                return false;

            if (!string.Equals(other.DesignerName, "player", StringComparison.OrdinalIgnoreCase))
                return false;

            var pawn = other.As<CCSPlayerPawn>();
            if (!pawn.Valid() || !pawn.IsPlayerAlive())
                return false;

            float gravityValue = 0.01f;
            if (_gravityConfig != null && _gravityConfig.Count > 0 && !string.IsNullOrEmpty(trigger.UniqueHammerID))
            {
                if (_gravityConfig.TryGetValue(trigger.UniqueHammerID, out var value))
                    gravityValue = value;
            }

            SetGravityScale(pawn, gravityValue);
            return true;
        }
        catch (Exception ex)
        {
            Core.Logger.LogError("HandleGravityTouch: {0}", ex.Message);
            return false;
        }
    }

    private void HandleGravityEndTouch(nint pEntity, nint pOther)
    {
        try
        {
            var trigger = Core.Memory.ToSchemaClass<CBaseEntity>(pEntity);
            var other = Core.Memory.ToSchemaClass<CBaseEntity>(pOther);
            if (trigger == null || other == null || !trigger.IsValid || !other.IsValid)
                return;

            if (!string.Equals(other.DesignerName, "player", StringComparison.OrdinalIgnoreCase))
                return;
            if (!string.Equals(trigger.DesignerName, "trigger_gravity", StringComparison.OrdinalIgnoreCase))
                return;

            var pawn = other.As<CCSPlayerPawn>();
            if (!pawn.Valid() || !pawn.IsPlayerAlive())
                return;

            SetGravityScale(pawn, 1.0f);
        }
        catch (Exception ex)
        {
            Core.Logger.LogError("HandleGravityEndTouch: {0}", ex.Message);
        }
    }

    private void SetGravityScale(CBaseEntity entity, float scale)
    {
        if (_setGravityScaleFunc == null || entity == null || !entity.IsValid)
            return;
        entity.GravityScale = scale;
        entity.GravityScaleUpdated();
        entity.ActualGravityScale = scale;
        _setGravityScaleFunc.Call(entity.Address, scale);
    }

    private void IgnitePawn(CEntityInstance? activator, float duration)
    {
        if (activator == null || !activator.IsValid)
            return;

        var existing = _ignites.FirstOrDefault(i => i.Entity.IsValid && i.Entity.Index == activator.Index);
        if (existing != null && existing.Active)
        {
            existing.EndTime = DateTime.UtcNow.AddSeconds(duration);
            return;
        }

        var particle = CreateIgniteParticle(activator);
        var state = new IgniteState(activator, particle, DateTime.UtcNow.AddSeconds(duration));
        _ignites.Add(state);
        ScheduleIgniteTick(state);
    }

    private CParticleSystem? CreateIgniteParticle(CEntityInstance entity)
    {
        var pawn = entity.As<CBaseEntity>();
        if (pawn == null || !pawn.IsValid)
            return null;

        var particle = Core.EntitySystem.CreateEntityByName<CParticleSystem>("info_particle_system");
        if (particle == null || !particle.IsValid)
            return null;

        particle.EffectName = _config.Ignite_Particle;
        particle.TintCP = 1;
        particle.Tint = System.Drawing.Color.FromArgb(255, 255, 0, 0);
        particle.StartActive = true;
        particle.Teleport(pawn.AbsOrigin, pawn.AbsRotation, pawn.AbsVelocity);
        particle.DispatchSpawn();

        return particle;
    }

    private void ScheduleIgniteTick(IgniteState state)
    {
        Core.Scheduler.DelayBySeconds(_config.Ignite_Repeat, () => IgniteTick(state));
    }

    private void IgniteTick(IgniteState state)
    {
        if (!state.Active)
            return;

        if (DateTime.UtcNow >= state.EndTime || state.Entity == null || !state.Entity.IsValid)
        {
            ClearIgnite(state);
            return;
        }

        try
        {
            if (state.Particle != null && state.Particle.IsValid)
            {
                var pawn = state.Entity.As<CBaseEntity>();
                if (pawn != null && pawn.IsValid)
                    state.Particle.Teleport(pawn.AbsOrigin, pawn.AbsRotation, pawn.AbsVelocity);
            }

            var player = EntityToPlayer(state.Entity);
            if (player != null && player.PlayerPawn.Value != null && player.PlayerPawn.Value.Valid())
            {
                var pawn = player.PlayerPawn.Value;
                pawn.VelocityModifier *= _config.Ignite_Velocity;
                pawn.VelocityModifierUpdated();
                pawn.Health -= _config.Ignite_Damage;
                pawn.HealthUpdated();
                if (pawn.Health <= 0)
                    pawn.CommitSuicide(true, true);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogError("IgniteTick: {0}", ex.Message);
        }

        ScheduleIgniteTick(state);
    }

    private void ClearIgnite(IgniteState state)
    {
        state.Active = false;

        if (state.Particle != null && state.Particle.IsValid)
        {
            TryAcceptInput(state.Particle, "Stop", state.Entity, state.Entity, null);
            state.Particle.Remove();
        }

        var player = EntityToPlayer(state.Entity);
        if (player != null && player.PlayerPawn.Value != null && player.PlayerPawn.Value.Valid())
        {
            player.PlayerPawn.Value.VelocityModifier = 1.0f;
            player.PlayerPawn.Value.VelocityModifierUpdated();
        }

        _ignites.Remove(state);
    }

    private void ClearAllIgnites()
    {
        foreach (var ignite in _ignites.ToList())
        {
            ClearIgnite(ignite);
        }
        _ignites.Clear();
    }

    private void OnConVarValueChanged(IOnConVarValueChanged @event)
    {
        if (_reloadConVar == null)
            return;

        if (@event.ConVarName != _reloadConVar.Name)
            return;

        if (bool.TryParse(@event.NewValue, out var value) && value)
        {
            LoadConfig();
            EnsureGravityConfigLoaded();
        }
    }

    private void LoadConfig()
    {
        try
        {
            var configPath = Path.Combine(_pluginDirectory, "config.json");
            if (!File.Exists(configPath))
            {
                _config = new ConfigData();
                return;
            }

            var data = File.ReadAllText(configPath);
            var parsed = JsonSerializer.Deserialize<ConfigData>(data);
            _config = parsed ?? new ConfigData();
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarning("LoadConfig failed: {0}", ex.Message);
            _config = new ConfigData();
        }
    }

    private void EnsureGravityConfigLoaded()
    {
        var mapName = GetCurrentMapName();
        if (string.IsNullOrEmpty(mapName))
            return;

        if (string.Equals(_currentMapName, mapName, StringComparison.OrdinalIgnoreCase) && _gravityConfig != null)
            return;

        _currentMapName = mapName;
        var mapPath = Path.Combine(_pluginDirectory, "maps", $"{mapName}.json");
        if (!File.Exists(mapPath))
        {
            _gravityConfig = null;
            return;
        }

        try
        {
            var data = File.ReadAllText(mapPath);
            _gravityConfig = JsonSerializer.Deserialize<Dictionary<string, float>>(data);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarning("LoadGravityConfig failed: {0}", ex.Message);
            _gravityConfig = null;
        }
    }

    private string? GetCurrentMapName()
    {
        try
        {
            return Core.Engine.Server.MapName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsGameUI(CEntityInstance entity)
    {
        return entity != null &&
               entity.IsValid &&
               string.Equals(entity.DesignerName, "logic_case", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrEmpty(entity.PrivateVScripts) &&
               string.Equals(entity.PrivateVScripts, "game_ui", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsViewControl(CEntityInstance entity)
    {
        return entity != null &&
               entity.IsValid &&
               string.Equals(entity.DesignerName, "logic_relay", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrEmpty(entity.PrivateVScripts) &&
               string.Equals(entity.PrivateVScripts, "point_viewcontrol", StringComparison.OrdinalIgnoreCase);
    }

    private CEntityInstance? FindEntityByName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return Core.EntitySystem.GetAllEntities().FirstOrDefault(e => e.IsValid && e.Entity != null && e.Entity.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static CCSPlayerController? EntityToPlayer(CEntityInstance? entity)
    {
        if (entity != null && entity.IsValid && string.Equals(entity.DesignerName, "player", StringComparison.OrdinalIgnoreCase))
        {
            var pawn = entity.As<CCSPlayerPawn>();
            if (pawn is not null && pawn.IsValid)
            {
                var player = pawn.ToPlayer();
                if (player is not null && player.IsValid && player.Controller is not null && player.Controller.IsValid)
                {
                    return player.Controller;
                }
            }
        }
        return null;
    }

    private static void TryAcceptInput(CEntityInstance entity, string input, CEntityInstance? activator, CEntityInstance? caller, string? value)
    {
        if (entity == null || !entity.IsValid)
            return;

        if (string.IsNullOrEmpty(value))
            entity.AcceptInput(input, activator, caller);
        else
            entity.AcceptInput(input, activator, caller, value);
    }

    private string? TryToString(CVariant<CVariantDefaultAllocator> cVariant)
    {
        if (!cVariant.TryGetBool(out var value))
        {
            if (!cVariant.TryGetChar(out var value2))
            {
                if (!cVariant.TryGetInt16(out var value3))
                {
                    if (!cVariant.TryGetUInt16(out var value4))
                    {
                        if (!cVariant.TryGetInt32(out var value5))
                        {
                            if (!cVariant.TryGetUInt32(out var value6))
                            {
                                if (!cVariant.TryGetInt64(out var value7))
                                {
                                    if (!cVariant.TryGetUInt64(out var value8))
                                    {
                                        if (!cVariant.TryGetFloat(out var value9))
                                        {
                                            if (!cVariant.TryGetDouble(out var value10))
                                            {
                                                if (!cVariant.TryGetResourceHandle(out var value11))
                                                {
                                                    if (!cVariant.TryGetUtlStringToken(out var value12))
                                                    {
                                                        if (!cVariant.TryGetHScript(out var value13))
                                                        {
                                                            if (!cVariant.TryGetCHandle(out CHandle<CBaseEntity> value14))
                                                            {
                                                                if (!cVariant.TryGetVector2D(out var value15))
                                                                {
                                                                    if (!cVariant.TryGetVector(out var value16))
                                                                    {
                                                                        if (!cVariant.TryGetVector4D(out var value17))
                                                                        {
                                                                            if (!cVariant.TryGetQAngle(out var value18))
                                                                            {
                                                                                if (!cVariant.TryGetQuaternion(out var value19))
                                                                                {
                                                                                    if (!cVariant.TryGetColor(out var value20))
                                                                                    {
                                                                                        if (!cVariant.TryGetString(out string? value21))
                                                                                        {
                                                                                            return string.Empty;
                                                                                        }

                                                                                        return value21;
                                                                                    }

                                                                                    return $"{value20}";
                                                                                }

                                                                                return $"{value19}";
                                                                            }

                                                                            return $"{value18}";
                                                                        }

                                                                        return $"{value17}";
                                                                    }

                                                                    return $"{value16}";
                                                                }

                                                                return $"{value15}";
                                                            }

                                                            return $"{value14.Raw}";
                                                        }

                                                        return $"{value13}";
                                                    }

                                                    return $"{value12}";
                                                }

                                                return $"{value11}";
                                            }

                                            return $"{value10}";
                                        }

                                        return $"{value9}";
                                    }

                                    return $"{value8}";
                                }

                                return $"{value7}";
                            }

                            return $"{value6}";
                        }

                        return $"{value5}";
                    }

                    return $"{value4}";
                }

                return $"{value3}";
            }

            return $"{value2}";
        }

        return $"{value}";
    }
}
