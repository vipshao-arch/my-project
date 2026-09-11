using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// SkillNode —— 方案 C：组合式技能节点系统
    ///
    /// 设计原则：
    ///   - 一个技能（SkillData）由若干有序的 SkillNode 组合而成
    ///   - 每个节点只承担一种职责（起手 VFX、命中判定、抛物线飞行、链式弹射……）
    ///   - 节点是 ScriptableObject，可以独立编辑/复用/组合
    ///   - 公共数据（身份/动画/通用 VFX）留在 SkillData 本体，避免每个节点重复
    ///
    /// 执行模型（三阶段，由 SkillRunner 驱动）：
    ///   OnCast   —— 技能/攻击开始瞬间（动画 t=0）
    ///   OnTick   —— 动画播放期间每帧调用（用来播 triggerTime 触发的中段 VFX）
    ///   OnHit    —— 命中帧（前摇结束 / 外部回调）
    ///   OnEnd    —— 动画结束 / 被打断
    ///
    /// 节点通过 NodeContext 访问：
    ///   - skill    : 所属 SkillData（拿身份/动画/通用 VFX/伤害等）
    ///   - caster   : 施法者 Transform（含 Animator/Motor）
    ///   - source   : 施法者 GameObject
    ///   - hitMask  : 命中层
    ///   - damage   : 修正后的伤害
    ///   - range    : 修正后的射程
    ///   - hitRadius: 修正后的命中半径
    ///   - animTime : 动画播放时间（秒）
    ///   - hit      : 命中信息（命中阶段填入，OnEnd 之后由 Runner 清空）
    ///
    /// 2026-07-21 执行层统一:行为已迁移至 SkillNodeData(SkillNodeData.Behaviors.cs),
    /// SkillRunner 直接遍历 graphData 调用,不再建虚拟 SO 节点。
    /// 本体系仅保留用于旧 Node_*.asset 反序列化与 FromLegacy 迁移读取。
    /// </summary>
    [Obsolete("已废弃:行为迁移至 SkillNodeData(2026-07-21 执行层统一)。仅保留用于旧资产反序列化/FromLegacy 迁移。")]
    public abstract class SkillNode : ScriptableObject
    {
        // ── Timeline 编辑器专用（运行时不读）──────────────
        [HideInInspector] public int   editorTrackRow = -1;   // 轨道行号（-1=自动按类目）
        [HideInInspector] public float editorDuration = 0f;   // 区间时长（秒，0=点事件）
        // ────────────────────────────────────────────────────

        // ── 三阶段回调：节点按需 override ─────────────────────────────

        /// <summary>动画/技能开始的瞬间（t=0）。用于起手 VFX/SFX/锁定移动。</summary>
        public virtual void OnCast(NodeContext ctx) { }

        /// <summary>动画播放期间每帧调用。triggerTime 触发的中段 VFX 在这里执行。</summary>
        public virtual void OnTick(NodeContext ctx) { }

        /// <summary>命中帧触发：前摇结束 / 外部明确指定"打到目标了"。</summary>
        public virtual void OnHit(NodeContext ctx, HitInfo hit) { }

        /// <summary>动画结束 / 被打断。释放资源、停粒子、收尾。</summary>
        public virtual void OnEnd(NodeContext ctx) { }
    }

    /// <summary>
    /// 节点执行的上下文（每帧/每次回调由 SkillRunner 构造）。
    /// 不缓存、不挂在任何资产上，纯运行时。
    ///
    /// 2026-07-21 执行层统一：同一份 NodeContext 同时服务运行时(SkillRunner)
    /// 与编辑器预览(SkillPreviewRuntime)：
    ///   - isPreview = true 时节点行为跳过 Physics/伤害,只经 sink 播表现
    ///   - sink 决定效果落地方式(游戏世界 vs 离屏预览池)
    ///   - firedOnce 承载原 SO 节点上的 per-cast _fired 状态(POCO 不可持有运行时状态)
    /// </summary>
    public class NodeContext
    {
        public SkillData skill;          // 所属技能（公共数据从这里拿）
        public GameObject source;        // 施法者 GameObject（含 HitDetector / SkillController / SkillAnimPlayer）
        public Transform caster;         // 施法者 Transform（= source.transform）
        public LayerMask hitMask;        // 可命中层（HitDetector.hitMask）
        public float damage;             // 修正后伤害
        public float range;              // 修正后射程
        public float animTime;           // 当前动画播放时间（秒）
        public bool debugDraw;           // 是否开启调试绘制

        // ── 执行层统一(2026-07-21) ─────────────────────────────────
        /// <summary>true=编辑器预览模式:跳过 Physics 查询与伤害结算,只播表现。</summary>
        public bool isPreview;
        /// <summary>
        /// 预览受击目标列表(2026-08-03,编辑器填充):isPreview 时 HitVFXData 等
        /// "命中目标"语义节点的落点;空列表=回退施法者位置(旧行为)。
        /// </summary>
        public System.Collections.Generic.List<Transform> previewHitTargets;
        /// <summary>
        /// true=远端表现模式(S2-5b,联机):VFX/弹道/动画全部真实生成,
        /// 但一切伤害落地被抑制(ApplyDamage 收口跳过、投射物 visualOnly、HitDetector 不启用)。
        /// 与 isPreview 的差异:preview 不生成真实弹道,visualOnly 生成但标记不伤。
        /// </summary>
        public bool visualOnly;
        /// <summary>效果落地接口。运行时=RuntimeSkillEffectSink,预览=PreviewSkillEffectSink。</summary>
        public ISkillEffectSink sink;
        /// <summary>当前正在回调的节点在 graphData 中的索引(由 Runner/预览在回调前设置,-1=无)。</summary>
        public int currentNodeIndex = -1;
        /// <summary>per-cast 触发去重表(节点索引)。替代原 SO 节点实例上的 _fired 字段。</summary>
        public readonly HashSet<int> firedOnce = new HashSet<int>();

        // 单实例复用（避免每帧 new），由 SkillRunner 在 OnCast 前 Reset
        public readonly List<GameObject> spawnedThisCast = new List<GameObject>();

        /// <summary>StartCast 时重置 per-cast 状态。</summary>
        public void ResetPerCast()
        {
            spawnedThisCast.Clear();
            firedOnce.Clear();
            currentNodeIndex = -1;
        }

        /// <summary>
        /// 标记当前节点"本次施法已触发过"。返回 true=首次触发(调用方应继续执行)。
        /// 替代原 SO 节点的 _fired 字段(POCO 是共享资产,不能存 per-cast 状态)。
        /// </summary>
        public bool MarkFiredOnce()
        {
            if (currentNodeIndex < 0) return true;   // 无索引语境(如 HitVFX 广播)不去重
            return firedOnce.Add(currentNodeIndex);
        }

        /// <summary>
        /// 把命中结果广播给同 SkillData.graphData 上的所有 HitVFXData 节点。
        /// 替代原 SkillRunner.NotifyHit(每次重建虚拟 SO 节点)。
        /// </summary>
        public void BroadcastHit(HitInfo hit)
        {
            if (skill == null || skill.graphData == null) return;
            int saved = currentNodeIndex;
            for (int i = 0; i < skill.graphData.Count; i++)
            {
                if (skill.graphData[i] is HitVFXData hv)
                {
                    currentNodeIndex = i;
                    hv.OnHit(this, hit);
                }
            }
            currentNodeIndex = saved;
        }
    }

    /// <summary>
    /// 命中帧传给节点的命中信息。
    /// AreaOfEffect/Beam：单次 OnHit（hit.targets 是所有被打到的目标）。
    /// RectShot：节点内部循环里把每个 RectProjectile 的 OnTargetHit 回调再次路由到 hit.targets。
    /// Trap/Wall/Summon：单次 OnHit（无具体目标，targets 为空）。
    /// </summary>
    public struct HitInfo
    {
        public List<GameObject> targets;     // 本次命中结算的所有目标
        public Vector3 origin;               // 施法者位置（用于 Trap/Wall 摆位）
        public Vector3 forward;              // 朝向（同上）

        public static HitInfo Single(GameObject target, Vector3 origin, Vector3 forward)
        {
            var list = target != null
                ? new List<GameObject> { target }
                : new List<GameObject>();
            return new HitInfo { targets = list, origin = origin, forward = forward };
        }

        public static HitInfo Many(IList<GameObject> ts, Vector3 origin, Vector3 forward)
        {
            return new HitInfo
            {
                targets = ts != null ? new List<GameObject>(ts) : new List<GameObject>(),
                origin  = origin,
                forward = forward,
            };
        }
    }
}
