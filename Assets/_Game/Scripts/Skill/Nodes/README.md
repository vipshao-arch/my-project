# SkillNode 节点系统（方案 C）

## 架构
- **SkillData** —— 身份/动画/通用 VFX 公共部分 + `nodes[]` 数组
- **SkillNode**（abstract）—— 单职责节点,4 阶段回调：`OnCast / OnTick / OnHit / OnEnd`
- **NodeContext** —— 节点运行上下文（caster / hitMask / damage / range / animTime…）
- **SkillRunner** —— 统一调度者，挂在 SkillAnimPlayer 内部

## 已实现的节点

### VFX 节点（`Nodes/VFXNodes.cs`）
| 节点 | 替代字段 | 触发时机 |
|---|---|---|
| `CastVFXNode` | `vfxOnCast` + `sfxOnCast` | OnTick（triggerTime） |
| `MidVFXNode`  | `vfxEntries[]` | OnTick（triggerTime） |
| `HitVFXNode`  | `vfxOnHit` + `sfxOnHit` | OnHit（命中帧） |

### 判定节点
| 节点 | 文件 | 替代 EffectType |
|---|---|---|
| `MeleeSwingNode` | `Nodes/MeleeSwingNode.cs` | AreaOfEffect |
| `RectShotNode` | `Nodes/ShotNodes.cs` | RectShot |
| `BeamNode` | `Nodes/ShotNodes.cs` | Beam |
| `ChainBounceNode` | `Nodes/ShotNodes.cs` | ChainBounce |
| `CurvedProjectileNode` | `Nodes/ShotNodes.cs` | CurvedProjectile |
| `AOECircularNode` | `Nodes/SpawnAndChannelNodes.cs` | AOECircular |
| `TrapNode` | 同上 | Trap |
| `WallNode` | 同上 | Wall |
| `SummonNode` | 同上 | Summon |
| `ChanneledNode` | 同上 | Channeled |

## 新建一个节点（菜单路径）
`Project 右键 → Create → Game/Skill Node → <具体类型>`

## 迁移老 .asset
1. 选中 1~N 个 `SkillData` 资产
2. 菜单 `Tools → Skill System → Migrate SkillData → Nodes`
3. 自动备份为 `_pre_nodes.asset`,并在同目录建 `{name}_nodes/` 子文件夹放节点资产
4. 原 .asset 写入 `nodes[]` 引用新节点资产
5. 回滚：把 `_pre_nodes.asset` 改回原名即可

## 老的 `vfxOnCast` / `vfxOnHit` / `vfxEntries` 字段
- 保留在 SkillData（兼容老 .asset）
- SkillAnimPlayer.LateUpdate 仍会消费它们
- HitDetector.OnHitFrame：nodes[] 非空走节点,空走老 switch（effectType）
- EnemyAI.SpawnAttackVFX / DelayedDamage：优先读节点,回退老字段
- 新建技能建议**只用 nodes[]**

## 挥刀特效现在配在哪里
- **节点路径**：`CastVFXNode.prefab`（起手刀光） + `HitVFXNode.prefab`（命中火花）
- 老路径（兼容）：`attackSkillData.vfxOnCast.prefab` + `attackSkillData.vfxOnHit.prefab`
- 音效：`CastVFXNode.sfx` / `HitVFXNode.sfx`
- 判定范围/角度：`MeleeSwingNode.range / hitRadius / hitAngle`
