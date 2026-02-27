# CS2-EntityFix-SW2

---

<div align="center">
  <a href="./README.en.md"><img src="https://flagcdn.com/48x36/gb.png" alt="English" width="48" height="36" /> <strong>English</strong></a>  
  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;
  <a href="./README.md"><img src="https://flagcdn.com/48x36/cn.png" alt="中文" width="48" height="36" /> <strong>中文版</strong></a>
</div>

<hr>
Original author: DarkerZ; Plugin creation and porting: Xiaolin Wudi
<hr>

Migrate CS2-EntityFix to the full version of SwiftlyS2, providing fixes for game_player_equip, game_ui, IgniteLifeTime, point_viewcontrol, trigger_gravity, and more.

## Migrated Features

- `game_player_equip` Fixes: `StripFirst` + `TriggerForActivatedPlayer` + `TriggerForAllPlayers`

- `game_ui` Fixes:

- Handling `Activate/Deactivate`

- Forward button input is now `InValue` (`Pressed*` / `Unpressed*`)

- `point_viewcontrol` Fixes:

- `EnableCamera/DisableCamera`

- `EnableCameraAll/DisableCameraAll`

- Handling FOV / Freeze / Disarm flags

- `IgniteLifeTime`: Inputting `ignitelifetime` now supports continuous damage and slowing

- `trigger_gravity`: Apply gravity and restore default gravity on StartTouch/EndTouch

- Map gravity configuration: `resources/maps/<mapname>.json`

## Project Structure

- `CS2-EntityFix-SW2.csproj`

- `src/EntityFixSw2.cs`

- `resources/config/config.json`

- `resources/maps/README.md`

- `resources/gamedata/README.md`

## Configuration

File: `resources/config/config.json`

```json

{
"IgniteVelocity": 0.45,

"IgniteRepeat": 0.5,

"IgniteDamage": 1,

"IgniteParticle": "particles/burning_fx/env_fire_small.vpcf"

}
```

## Map Gravity Overlay

Place `<map>.json` (e.g., `de_dust2.json`) in `resources/maps`:

```json

{
"123456": 0.2,

"654321": 0.5

}
```

- Key: `Trigger_gravity`'s `UniqueHammerID`

- Value: The percentage of gravity applied when touching this entity

## Dependencies

- SwiftlyS2 (`SwiftlyS2.CS2`)

## Build

```bash
dotnet build CS2-EntityFix-SW2.csproj -c Release

```