# CS2-EntityFix-SW2 - 原作者 By: DarkerZ 插件制作移植 By: 晓琳吴迪

---

<div align="center">
  <a href="./README.md"><img src="https://flagcdn.com/48x36/cn.png" alt="中文" width="48" height="36" /> <strong>中文版</strong></a>  
  &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;
  <a href="./README.en.md"><img src="https://flagcdn.com/48x36/gb.png" alt="English" width="48" height="36" /> <strong>English</strong></a>
</div>

<hr>

将 CS2-EntityFix 迁移到 SwiftlyS2 的完整版本，提供 game_player_equip、game_ui、IgniteLifeTime、point_viewcontrol、trigger_gravity 等修复能力。

## 功能

- game_player_equip：支持 Use、TriggerForActivatedPlayer、TriggerForAllPlayers
- game_ui：支持 Activate/Deactivate 与按钮触发输入
- IgniteLifeTime：可配置粒子、持续时间与伤害
- point_viewcontrol：支持 Enable/Disable/All 与冻结、FOV、缴械行为
- trigger_gravity：支持 HammerID 配置的重力值与 EndTouch 恢复
- TestActivator 空激活修复

## 依赖

- SwiftlyS2（建议不低于 1.1.5）

## 安装

1. 编译并将插件输出目录复制到 `addons/swiftly/plugins/CS2-EntityFix-SW2/`
2. 将 `resources` 目录复制到同一插件目录
3. 将 `config.json` 放到插件目录
4. 需要重力修复时，按下面说明生成并放置地图配置

## 配置说明

文件：`config.json`

- Ignite_Velocity：燃烧时速度倍率，范围建议 0.001 - 1.0
- Ignite_Repeat：燃烧判定间隔（秒），建议 0.1 - 1.0
- Ignite_Damage：每次判定伤害
- Ignite_Particle：燃烧粒子路径

## 重力配置

生成的重力配置文件需放置在：

```
CS2-EntityFix-SW2/maps/<地图名>.json
```

文件结构示例：

```json
{
  "100275": 0.2,
  "100276": 0.01
}
```

需要配合 HammerID 修复插件才能保证触发器的 UniqueHammerID 正常。

## 重载配置

控制台执行：

```
css_entityfix_reload 1
```

## 目录结构

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

## 工具

`tools/CS2-ParseGravity` 用于生成 trigger_gravity 的 HammerID 重力配置。
