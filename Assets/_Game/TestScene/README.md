# CharacterSystem 测试场景模板

## 场景文件
`TestScene/test_scene.unity`

## 场景结构

```
test_scene
├── Directional Light
├── Main Camera (TopdownCameraController)
├── EventSystem
├── Canvas (CharacterHUD)
│   ├── HP_Slider
│   ├── Stamina_Slider
│   └── SkillDebugHUD
├── Environment (Prefab)        ← 地形、静态碰撞体、测试平台
├── _actions (Prefab)           ← CharacterActionTrigger 集合（梯子/跳跃触发器）
└── graves_Character (Prefab)   ← 角色主体（Motor/Input/Camera/Skill/Ladder）
```

## 依赖组件（graves_Character）

| 组件 | 命名空间 | 功能 |
|------|----------|------|
| CharacterMotor | Game.Character | 移动/跳跃/翻滚/蹲下/Root Motion |
| CharacterInputHandler | Game.Character | WASD + 右键点移 + Shift冲刺 |
| TopdownCameraController | Game.Character | 俯角60°摄像机，滚轮缩放4-15m |
| CharacterActionHandler | Game.Character | E键触发动作（翻越/爬升/踏步） |
| CharacterLadderAction | Game.Character | 梯子攀爬（E进/Space出） |
| CharacterHUD | Game.Character | HP/Stamina Slider绑定 |
| SkillController | Game.SkillSystem | 技能系统入口 |
| SkillAnimPlayer | Game.SkillSystem | 技能动画播放 |
| SkillMovementController | Game.SkillSystem | 技能期间移动控制 |
| WeaponHolder | Game.SkillSystem | 武器挂载与切换 |

## 操作按键

| 按键 | 功能 |
|------|------|
| WASD | 移动 |
| 右键点击 | Click & Move |
| Space | 跳跃 |
| Shift | 冲刺切换（Toggle） |
| C | 蹲下 |
| Q | 翻滚 |
| E | 动作交互（梯子进入/翻越/爬升） |

## Editor 工具（Tools > Character System）

| 菜单 | 功能 |
|------|------|
| Setup / Character Setup Tool | 一键为角色 Prefab 添加所有组件 |
| Setup / Create Character HUD | 在场景中创建完整 HUD Canvas |
| Setup / Ladder Setup | 配置梯子触发器 |
| Setup / Ragdoll Auto Setup | 自动设置布娃娃系统 |
| Diagnostics / Character Diagnostics | 诊断角色组件挂载状态 |
| Diagnostics / Print Animator Layers | 打印 Animator 层级信息 |
| Animator / Setup Skill Animator Layer | 添加技能动画层 |
| Export / Export Package | 打包导出 CharacterSystem |
