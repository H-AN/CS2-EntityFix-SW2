# CS2-EntityFix-SW2 

---

<div align="center">
  <a href="./README.md"><img src="https://flagcdn.com/48x36/cn.png" alt="中文" width="48" height="36" /> <strong>中文版</strong></a>  
  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;
  <a href="./README.en.md"><img src="https://flagcdn.com/48x36/gb.png" alt="English" width="48" height="36" /> <strong>English</strong></a>
</div>

<hr>
插件原作者 : DarkerZ ; 插件制作移植 : 晓琳吴迪
<hr>
将 CS2-EntityFix 迁移到 SwiftlyS2 的完整版本，提供 game_player_equip、game_ui、IgniteLifeTime、point_viewcontrol、trigger_gravity 等修复能力。

## 已迁移功能

- `game_player_equip` 修复：`StripFirst` + `TriggerForActivatedPlayer` + `TriggerForAllPlayers`
- `game_ui` 修复：
  - 处理 `Activate/Deactivate`
  - 转发按键输入为 `InValue`（`Pressed*` / `Unpressed*`）
- `point_viewcontrol` 修复：
  - `EnableCamera/DisableCamera`
  - `EnableCameraAll/DisableCameraAll`
  - 处理 FOV / Freeze / Disarm 标志位
- `IgniteLifeTime`：输入 `ignitelifetime` 支持持续伤害与减速
- `trigger_gravity`：StartTouch/EndTouch 应用重力并恢复默认重力
- 地图重力配置：`resources/maps/<mapname>.json`

## 项目结构

- `CS2-EntityFix-SW2.csproj`
- `src/EntityFixSw2.cs`
- `resources/config/config.json`
- `resources/maps/README.md`
- `resources/gamedata/README.md`

## 配置

文件：`resources/config/config.json`

```json
{
  "IgniteVelocity": 0.45,
  "IgniteRepeat": 0.5,
  "IgniteDamage": 1,
  "IgniteParticle": "particles/burning_fx/env_fire_small.vpcf"
}
```

## 地图重力覆盖

在 `resources/maps` 放置 `<map>.json`（例如 `de_dust2.json`）：

```json
{
  "123456": 0.2,
  "654321": 0.5
}
```

- Key：`trigger_gravity` 的 `UniqueHammerID`
- Value：接触该实体时应用的重力比例

## 依赖

- SwiftlyS2 (`SwiftlyS2.CS2`)

## 构建

```bash
dotnet build CS2-EntityFix-SW2.csproj -c Release
```