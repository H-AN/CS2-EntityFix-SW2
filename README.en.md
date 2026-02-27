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

## Features

- game_player_equip: Supports Use, TriggerForActivatedPlayer, TriggerForAllPlayers

- game_ui: Supports Activate/Deactivate and button-triggered input

- IgniteLifeTime: Configurable particles, duration, and damage

- point_viewcontrol: Supports Enable/Disable/All and freeze, FOV, and disarm behaviors

- trigger_gravity: Supports gravity values ​​configured by HammerID and EndTouch restoration

- TestActivator empty activation fix

## Dependencies

- SwiftlyS2 (Recommended version: 1.1.5 or higher)

## Installation

1. Compile and copy the plugin output directory to `addons/swiftly/plugins/CS2-EntityFix-SW2/`

2. Copy the `resources` directory to the same plugin directory

3. Place `config.json` in the plugin directory

4. When gravity fix is ​​needed, generate and place the map configuration as described below

## Configuration Instructions

File: `config.json`

- Ignite_Velocity: Speed ​​multiplier during burning, recommended range 0.001 - 1.0

- Ignite_Repeat: Burning detection interval (seconds), recommended range 0.1 - 1.0

- Ignite_Damage: Damage per detection

- Ignite_Particle: Burning particle path

## Gravity Configuration

The generated gravity configuration file should be placed in:

``` CS2-EntityFix-SW2/maps/<map name>.json

```

Example file structure:

```json

{
"100275": 0.2,

"100276": 0.01

}
```

Requires the HammerID fix plugin to ensure the trigger's UniqueHammerID is correct.

## Reload Configuration

Console execution:

```
css_entityfix_reload 1

```
## Directory Structure

```
CS2-EntityFix-SW2/
  config.json
  resources/
    gamedata/
      signatures.jsonc
      offsets.jsonc
  src/
    EntityFix.cs
    Extensions.cs
  tools/
    CS2-ParseGravity/
```
## Tools

`tools/CS2-ParseGravity` is used to generate the HammerID gravity configuration for trigger_gravity.
