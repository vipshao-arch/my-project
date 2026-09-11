# Action 资产搭建指南

本文档总结 StepUp / JumpOver / ClimbUp / Ladder 四类交互资产的正确搭建流程与关键约束。

## 一、资产总览

| 资产 | 组件 | 动画状态（Controller） | 触发方式 | 用途 |
|---|---|---|---|---|
| stepup | `CharacterActionTrigger` | `StepUp` | 按键 E | 跨上台阶/低障碍 |
| jumpover | `CharacterActionTrigger` | `JumpOver` | 按键 E | 翻越障碍物 |
| climbup | `CharacterActionTrigger` | `ClimbUp` | 按键 E | 攀上高台/墙 |
| ladder | `CharacterLadderTrigger` | `ClimbLadder` | 自动（碰触+方向） | 爬梯子上下 |

> 快捷创建：菜单 `Tools/Level Kit/Create StepUp | JumpOver | ClimbUp | Ladder`，或窗口 `Tools/Level Kit/Action Asset Setup`。
> 可视化微调：`Tools/Level Kit/Action Trigger Setup`（只针对 `CharacterActionTrigger`）。

---

## 二、StepUp / JumpOver / ClimbUp（CharacterActionTrigger）

### 结构

```
<name> (Tag = Action)
  ├─ BoxCollider          (isTrigger = true)
  ├─ CharacterActionTrigger
  └─ target               (手撑点/对齐目标，纯 Transform，无 Collider)
```

### 字段配置

| 字段 | StepUp | JumpOver | ClimbUp |
|---|---|---|---|
| `playAnimation` | `StepUp` | `JumpOver` | `ClimbUp` |
| `autoAction` | false | false | false |
| `disableCollision` | **true** | **true** | **true** |
| `disableGravity` | true | true | true |
| `resetPlayerSettings` | true | true | true |
| `endExitTimeAnimation` | **0.95** | **0.95** | **0.95** |
| `avatarTarget` | LeftHand | LeftHand | LeftHand |
| `matchTargetMask` | (0,1,1) | (0,1,1) | (0,1,1) |
| `startMatchTarget` | 0.2 | 0 | 0 |
| `endMatchTarget` | 0.6 | 0.3 | 0.3 |
| `useTriggerRotation` | true | true | true |
| `activeFromForward` | true | true | true |
| `exitSpeed` | 0 | 0 | 0 |

### `target`（matchTarget）摆放

- 是"手撑点"或"对齐目标"，放在障碍物顶部边缘、角色动作中手应到达的位置。
- 默认 `localPosition ≈ (0, 0.65, 0.6)`，需按实际障碍物高度/厚度调整。
- `matchTargetMask=(0,1,1)` 表示只匹配 Y/Z，X 方向由动画自由表现。

---

## 三、Ladder（CharacterLadderTrigger）

### 结构

```
ladder (root, 无组件)
  ├─ EnterLadderBottom   (CharacterLadderTrigger + BoxCollider trigger, Tag=LadderTrigger)
  │    ├─ matchTargetBottom   (纯 Transform，无 Collider)
  │    └─ stair_start         (BoxCollider trigger，实际底部进出区)
  └─ ExitLadderTop       (CharacterLadderTrigger + BoxCollider trigger, Tag=LadderTrigger)
       ├─ matchTargetTop      (纯 Transform，无 Collider)
       └─ stair_end           (BoxCollider trigger，实际顶部进出区)
```

### 字段配置

| 字段 | EnterLadderBottom | ExitLadderTop |
|---|---|---|
| `playAnimation` | `EnterLadderBottom` | `EnterLadderTop` |
| `exitAnimation` | `ExitLadderBottom` | `ExitLadderTop` |
| `isEntryTrigger` | true | true |
| `isExitTrigger` | true | true |
| `autoAction` | true | true |
| `useTriggerRotation` | true | true |
| `activeFromForward` | true | true |
| `reverseEnterForward` | **false** | **true** |
| `matchTarget` | matchTargetBottom | matchTargetTop |
| `endpointHeightOffset` | 0 | 0 |

### 关键约束（务必遵守）

1. **matchTargetTop 与 matchTargetBottom 必须在同一条垂直线上（XZ 完全一致）**。
   否则挂梯轴是斜的，角色爬梯时 XZ 会漂移、上下离梯位置对不上。

2. **两个 matchTarget 必须在梯子横杆的同一侧（挂梯侧）**。
   攀爬朝向由 `GetLadderFacing = (ladderRoot.position - matchTarget.position)` 的水平方向推导；
   若底部/顶部 matchTarget 分居横杆两侧，攀爬朝向会相反。

3. **matchTarget 的 Y = 底部/顶部横杆高度**（角色挂梯时身体所在高度）。
   底部通常在 0，顶部通常在梯顶横杆处（比顶部平台低约 1m）。

4. `stair_start` / `stair_end` 是实际进出触发区（`CharacterLadderAction` 通过子 Collider 名判断入口），必须是 `BoxCollider` + `isTrigger=true`。

---

## 四、动画导入约束（本次排查的坑）

1. **共享 Avatar 的 `rootMotionBoneName` 必须为 `Hips`**。
   项目动作动画都用 `Copy From Other Avatar` 引用 `h_gravesAvatar`（来源 `h_graves.FBX`），
   若该 Avatar 的 `rootMotionBoneName` 为空，所有提取 XZ Root Motion 的动画（jumpOver/stepUp/roll_short 等）会位移跳变卡顿。
   修复位置：`h_graves.FBX.meta` → `humanDescription.rootMotionBoneName`，改 FBX 自身 meta 无效（Copy 模式会忽略）。

2. **提取位移的动画**（`keepOriginalPositionXZ=0`）必须配 `rootMotionBoneName=Hips`；
   **保留位移的动画**（`keepOriginalPositionXZ=1`，如梯子 climb）则不需要。

3. 运行时行为（`CharacterActionHandler`）：
   - `disableCollision` 必须真正禁用角色胶囊体（`_col`），否则翻越时胶囊体撞障碍物会顿挫。
   - `updateMode` 保持 `AnimatePhysics`（Root Motion 位移与 Rigidbody 物理同步，切 Normal 会跳帧卡顿）。
   - 切 `applyRootMotion=true` 前先调用 `_motor.PrepareAnimatorRootMotion(...)` 同步根基准，避免首帧陈旧 delta 顿挫。

---

## 五、快速上手

1. 打开 `Tools/Level Kit/Action Asset Setup` 窗口（或直接用 `Create *` 菜单项），在相机前方生成资产。
2. 把生成物移动到目标障碍物/平台处。
3. 按实际尺寸调整 `BoxCollider` 尺寸、`target`（动作）或 `matchTarget`（梯子）位置。
4. 角色需挂 `CharacterActionHandler`（动作）与 `CharacterLadderAction`（梯子，通常已随角色 prefab 配置）。
5. Play 实测：进/出衔接、翻越是否卡顿、梯子挂梯轴是否垂直。
