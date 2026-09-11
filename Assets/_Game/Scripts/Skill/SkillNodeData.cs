// =====================================================================
//  SkillNodeData —— §1 重构（方案 ②+①）节点数据 POCO 基类与 14 个子类
//
//  目的:把原来分散在 ScriptableObject 节点（SkillNode）里的字段
//        内嵌到 SkillData 自身的 graphData 列表（[SerializeReference]）。
//  收益:
//    1) 一个技能 = 1 个 .asset,字段可视化、无需跳到 Node_xxx.asset
//    2) 老 .asset 通过 GraphAdapter 双向转换,零迁移成本
//    3) 编辑器（SkillBuilderWizard）顶部模式切换条直接读写 graphData
//  风险:
//    - [SerializeReference] 列表里 Inspector 默认显示类名,需自定义 PropertyDrawer
//      （§4 阶段补,先用内置黑盒也行）
//
//  字段映射约定:
//    - POCO 字段名 = SkillNode 同名字段,类型一致（UnityObject 引用用 UnityEngine.Object 子类）
//    - 不复制 SkillNode.editorTrackRow / editorDuration（仅编辑器缓存,运行时不读）
// =====================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.SkillSystem
{
    // ────────────────────────────────────────────────────────────────────
    //  基类:序列化引用入口点
    // ────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 节点数据 POCO 基类。
    /// 用 [SerializeReference] 实现多态内嵌,无需 ScriptableObject 包装。
    /// 所有子类字段名 = 对应 SkillNode 字段名,GraphAdapter 做 1:1 字段拷贝。
    /// </summary>
    [Serializable]
    public abstract class SkillNodeData
    {
        // 编辑器缓存（运行时不读，与 SkillNode.editorTrackRow/editorDuration 对齐）
        [HideInInspector] public int   editorTrackRow = -1;
        [HideInInspector] public float editorDuration  = 0f;

        /// <summary>
        /// 仅编辑器可视化(2026-07-31,对齐 test07):Timeline 动画轨道显示用,
        /// 运行时 SkillAnimPlayer.ScheduleGraphAnimationLayers 跳过不调度——
        /// 适用于"技能动画由 Animator Trigger 状态机播放,clip 仅作时间轴参照"的注入节点。
        /// 注意:与 NodeContext.visualOnly(联机远端表现模式)是两个概念,勿混。
        /// </summary>
        [HideInInspector] public bool visualOnly = false;

        // §4.1:triggerTime 不上提基类 — 它只对"动画中段触发"类节点(VFX / 多段)有意义,
        // 一次性触发类(AOE / Trap / Wall / MeleeSwing 命中帧由动画前摇决定)不强求这个字段。
        // 时间轴条用反射读 triggerTime 字段,没有就当 0(显示在轨道 0 时刻)。

        /// <summary>节点类目（用于 Builder 拼装面板分组与时间轴条轨道分类）。</summary>
        public abstract SkillNodeCategory Category { get; }

        /// <summary>节点显示名（Builder 列表里用）。</summary>
        public abstract string DisplayName { get; }

        // ────────────────────────────────────────────────────────────
        //  执行层统一(2026-07-21):行为虚方法
        //
        //  节点行为直接挂在 POCO 上,运行时(SkillRunner)与编辑器预览
        //  (SkillPreviewRuntime)共用同一份实现,差异由 ctx.isPreview /
        //  ctx.sink 屏蔽。原 ScriptableObject SkillNode 双轨体系废弃。
        //
        //  ⚠ per-cast 状态(_fired 等)禁止放本类字段 —— POCO 是共享资产,
        //     多角色/多次施法共用同一实例,状态走 ctx.firedOnce。
        // ────────────────────────────────────────────────────────────

        /// <summary>节点在时间轴上的触发时刻(秒)。clipLength 供 frontSwing 类节点换算。</summary>
        public virtual float GetTriggerTime(SkillData skill, float clipLength) => 0f;

        /// <summary>动画/技能开始瞬间(t=0)。</summary>
        public virtual void OnCast(NodeContext ctx) { }

        /// <summary>动画播放期间每帧调用。</summary>
        public virtual void OnTick(NodeContext ctx) { }

        /// <summary>命中帧触发:triggerTime 到点 / 前摇结束 / 外部命中回调。</summary>
        public virtual void OnHit(NodeContext ctx, HitInfo hit) { }

        /// <summary>动画结束/被打断。收尾。</summary>
        public virtual void OnEnd(NodeContext ctx) { }

        /// <summary>编辑器预览的触发入口。默认与运行时命中帧同语义(走 OnHit),子类可覆写。</summary>
        public virtual void OnPreviewTrigger(NodeContext ctx) { OnHit(ctx, default); }
    }

    /// <summary>节点类目,用于编辑器分组和时间轴轨道分类。</summary>
    public enum SkillNodeCategory
    {
        VFX = 0,       // CastVFX / MidVFX / HitVFX
        Melee = 1,     // MeleeSwing
        Shot = 2,      // RectShot / Beam / ChainBounce / CurvedProjectile
        Spawn = 3,     // AOECircular / Trap / Wall / Summon
        Channeled = 4, // Channeled
        Movement = 5,  // Movement
        Buff = 6,      // StatusEffect(自身增益/目标减益)
        AnimClip = 7,  // AnimClipLayerData —— 动画片段层
        MultiStage = 8,// MultiStageLayerData —— 多段层
    }

    // ────────────────────────────────────────────────────────────────────
    //  ① VFX 节点数据
    // ────────────────────────────────────────────────────────────────────
    [Serializable]
    public partial class CastVFXData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.VFX;
        public override string DisplayName => "起手 VFX/SFX";

        // Prefab
        public GameObject prefab;
        public AudioClip  sfx;
        // Spawn
        public string  spawnBone = "RightHand";
        public bool    worldSpace = true;
        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationOffset = Vector3.zero;
        public float   scale = 1f;
        public float   destroyAfterSeconds = 0f;
        // Timing — 保留子类字段以兼容已序列化 .asset(基类也有同名默认字段,但子类值优先生效)
        public float triggerTime = 0f;
        // Audio
        public float sfxPitchRandomPercent = 10f;
    }

    [Serializable]
    public partial class MidVFXData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.VFX;
        public override string DisplayName => "中段 VFX";

        public GameObject prefab;
        public float   triggerTime = 0.2f;
        public string  spawnBone;
        public bool    worldSpace = true;
        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationOffset = Vector3.zero;
        public float   scale = 1f;
        public float   destroyAfterSeconds = 0f;
    }

    [Serializable]
    public partial class HitVFXData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.VFX;
        public override string DisplayName => "命中 VFX/SFX";

        public GameObject prefab;
        public AudioClip  sfx;
        // 生成点 — 与 CastVFXData 保持同字段名,Wizard 反射 + Runtime 统一读取
        [Tooltip("挂点骨骼名(留空=命中目标中心;填 'RootBone' 等可定位到目标身体部位)。")]
        public string  spawnBone = "";
        [Tooltip("是否世界坐标偏移(true=偏移相对世界轴;false=偏移相对命中目标本地坐标)。")]
        public bool    worldSpace = true;
        [Tooltip("命中特效相对生成点的位置偏移(米)。")]
        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationOffset = Vector3.zero;
        public float   scale = 1f;
        public float   destroyAfterSeconds = 2f;
        public float   sfxPitchRandomPercent = 10f;
        [Tooltip("命中特效延迟触发时刻(秒)。0=命中瞬间立即播放。")]
        public float   triggerTime = 0f;
    }

    // ────────────────────────────────────────────────────────────────────
    //  ② 近战节点数据
    // ────────────────────────────────────────────────────────────────────
    [Serializable]
    public partial class MeleeSwingData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Melee;
        public override string DisplayName => "近战挥击";

        [Tooltip("命中判定触发时刻(秒)。对应动画前摇结束、剑气/拳风生效的那一帧。")]
        public float triggerTime = 0f;
        public float range           = 2f;
        public float hitRadius       = 1.5f;
        [Range(10f, 180f)] public float hitAngle = 60f;
        public float damage          = 5f;
        public float originHeight    = 0.8f;
        public float originForwardOffset = 0f;

        [Tooltip("纵向判定范围(米)。判定盒高度,支持高打低/低打高。0=回退为 hitRadius(旧胶囊行为)。")]
        public float verticalRange = 2f;
        [Tooltip("是否检测掩体格挡(true=攻击视线被掩体挡住的目标不受伤)。旧资产反序列化缺失=false(旧行为不格挡),需手动开启。")]
        public bool checkObstacle = true;
        [Tooltip("掩体所在层(默认 Default)。")]
        public LayerMask obstacleMask = 1;
    }

    // ────────────────────────────────────────────────────────────────────
    //  ③ 弹体 / 光束 / 链式 / 抛物线节点数据
    // ────────────────────────────────────────────────────────────────────
    [Serializable]
    public partial class RectShotData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Shot;
        public override string DisplayName => "矩形弹幕";

        [Tooltip("弹幕发射时刻(秒)。对应施法动画前摇结束、弹体实际生成的那一帧。")]
        public float triggerTime = 0f;
        public float damage      = 5f;

        // Geometry
        public ShotShape shape = ShotShape.Rect;
        public float width  = 2f;
        public float height = 1.8f;
        // Travel
        public float speed     = 18f;
        public float maxRange  = 6f;
        // Spread
        [Range(1, 9)]  public int   count        = 1;
        [Range(0f, 90f)] public float spreadAngle = 0f;
        // Hit Mode
        public ShotHitMode hitMode = ShotHitMode.Stop;
        [Range(0, 99)]  public int   pierceCount = 0;
        // Homing
        public bool  homingEnabled = false;
        [Range(0f, 1440f)] public float homingTurnRate = 540f;
        public float homingSearchRadius = 10f;
        // Initial Offset
        [Range(-90f, 90f)] public float initialYawOffset   = 0f;
        [Range(-45f, 45f)] public float initialPitchOffset = 0f;
        public float spawnHeight  = 0.9f;
        [Range(0f, 2f)] public float forwardOffset = 0.3f;
        // Projectile Prefab
        public GameObject projectilePrefab;
        public Vector3 projectileLocalOffset      = Vector3.zero;
        public Vector3 projectileLocalEulerOffset = Vector3.zero;
        public float   projectileLifetime  = 2f;
        public float   projectileLocalScale = 1f;

        [Tooltip("弹体飞行中是否被掩体拦截(true=撞掩体即停并销毁)。旧资产反序列化缺失=false,需手动开启。")]
        public bool checkObstacle = true;
        [Tooltip("掩体所在层(默认 Default)。")]
        public LayerMask obstacleMask = 1;
    }

    [Serializable]
    public partial class BeamData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Shot;
        public override string DisplayName => "光束";

        [Tooltip("光束触发时刻(秒)。对应激光/火焰吐息开始照射的那一帧。")]
        public float triggerTime = 0f;
        public float beamWidth     = 0.25f;
        public float range         = 10f;
        public float originHeight  = 1f;
        public float damage        = 10f;

        [Tooltip("是否检测掩体格挡(true=被掩体挡住的目标不受照射)。旧资产反序列化缺失=false,需手动开启。")]
        public bool checkObstacle = true;
        [Tooltip("掩体所在层(默认 Default)。")]
        public LayerMask obstacleMask = 1;
    }

    [Serializable]
    public partial class ChainBounceData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Shot;
        public override string DisplayName => "链式弹射";

        [Tooltip("链式弹射触发时刻(秒)。对应闪电链/连锁魔法开始跳跃的那一帧。")]
        public float triggerTime = 0f;
        public float damage      = 5f;
        [Range(1, 12)] public int   bounceCount     = 4;
        public float searchRadius      = 8f;
        [Range(0.1f, 1f)] public float damageFalloff = 0.7f;
        public float maxBounceRange    = 12f;

        [Tooltip("是否检测掩体格挡(true=闪电链不能穿墙弹跳)。旧资产反序列化缺失=false,需手动开启。")]
        public bool checkObstacle = true;
        [Tooltip("掩体所在层(默认 Default)。")]
        public LayerMask obstacleMask = 1;
    }

    [Serializable]
    public partial class CurvedProjectileData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Shot;
        public override string DisplayName => "抛物线弹体";

        [Tooltip("抛物线弹体发射时刻(秒)。对应榴弹/手雷出手的那一帧。")]
        public float triggerTime = 0f;

        public float width  = 2f;
        public float height = 1.8f;
        public float speed  = 12f;
        public float maxRange = 8f;
        public float gravity = 9.8f;
        public float groundSnapY = 0.2f;
        [Range(0f, 80f)] public float launchPitch = 30f;
        public float spawnHeight  = 0.9f;
        public float forwardOffset = 0.3f;
        public ShotHitMode hitMode = ShotHitMode.Stop;
        public int pierceCount     = 0;
        public float damage        = 0f;
        public GameObject projectilePrefab;
        public Vector3 projectileLocalOffset      = Vector3.zero;
        public Vector3 projectileLocalEulerOffset = Vector3.zero;
        public float   projectileLifetime  = 3f;
        public float   projectileLocalScale = 1f;
    }

    // ────────────────────────────────────────────────────────────────────
    //  ④ 生成 / AOE / 陷阱 / 墙体 / 召唤节点数据
    // ────────────────────────────────────────────────────────────────────
    [Serializable]
    public partial class AOECircularData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Spawn;
        public override string DisplayName => "圆形 AOE";

        [Tooltip("AOE 爆炸触发时刻(秒)。对应陨石落地/爆炸判定的那一帧。")]
        public float triggerTime = 0f;
        public float radius    = 4f;
        public bool  centerIsTargetPoint = true;
        public float damage    = 10f;

        [Tooltip("立体高度(米):>0 时判定改圆柱(平面半径 radius × 高度 height,从爆心所在平面向上延伸),\n" +
                 "高台/坡道立体空间可控;0=旧行为(完整球体判定,向上半球全覆盖)。\n" +
                 "旧资产反序列化缺失=0,保持旧行为。")]
        public float height = 0f;

        [Tooltip("是否检测掩体格挡(true=爆心与目标被掩体隔开则不受伤害)。旧资产反序列化缺失=false,需手动开启。")]
        public bool checkObstacle = true;
        [Tooltip("掩体所在层(默认 Default)。")]
        public LayerMask obstacleMask = 1;
    }

    [Serializable]
    public partial class TrapData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Spawn;
        public override string DisplayName => "陷阱";

        public GameObject trapPrefab;
        public float lifetime         = 30f;
        public float triggerCooldown  = 0.5f;
        public float spawnDistance    = 2f;
    }

    [Serializable]
    public partial class WallData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Spawn;
        public override string DisplayName => "墙体";

        public GameObject wallPrefab;
        public float width   = 6f;
        public float height  = 3f;
        public float lifetime = 5f;
        public float spawnDistance = 1f;
    }

    [Serializable]
    public partial class SummonData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Spawn;
        public override string DisplayName => "召唤";

        public GameObject summonPrefab;
        public float lifetime = 15f;
        [Range(1, 8)] public int count = 1;
        public float spawnDistance = 2f;
    }

    // ────────────────────────────────────────────────────────────────────
    //  ⑤ 持续施法 / 移动策略节点数据
    // ────────────────────────────────────────────────────────────────────
    [Serializable]
    public partial class ChanneledData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Channeled;
        public override string DisplayName => "持续施法";

        public float durationOverride = 0f;
        public float tickInterval     = 1f;
        public float tickAmount       = 5f;
        public bool  lockMovement     = true;
        public float damage           = 0f;
    }

    [Serializable]
    public partial class MovementData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Movement;
        public override string DisplayName => "移动/朝向";

        [Tooltip("移动策略生效时刻(秒)。对应技能前摇结束、角色开始冲刺/锁足的那一帧。")]
        public float triggerTime = 0f;
        public MovementPolicy policy            = MovementPolicy.FullMove;
        [Range(0f, 2f)] public float speedMultiplier = 0.5f;
        public FacingMode    facingMode         = FacingMode.Free;
        public bool   dashOnCast     = false;
        public float  dashForce      = 5f;
        public float  dashDuration   = 0.2f;
    }

    [Serializable]
    public partial class StatusEffectData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.Buff;
        public override string DisplayName => "状态效果";

        [Tooltip("Buff/Debuff 施加时刻(秒)。对应技能命中、增益生效的那一帧。")]
        public float triggerTime = 0f;

        [Tooltip("要施加的 StatusEffect 资产(留空需要在节点 Inspector 后补)。")]
        public StatusEffect effect;

        [Tooltip("施加目标:true=施法者自身(增益),false=被命中目标(减益)。")]
        public bool applyToSelf = false;

        [Tooltip("命中多少个目标就施加多少次(false=只施加一次)。")]
        public bool applyPerTarget = true;
    }

    // ────────────────────────────────────────────────────────────────────
    //  ⑥ 时间轴层 —— 动画片段层 / 多段层
    //
    //  目的:让动画片段(animClips[i])和多段配置(multiStage.segments[i])在时间轴编辑器中
    //         与 graphData 节点同等对待,作为"层"出现在 Layers 列与轨道上,可拖动调 triggerTime。
    //  数据流:同样内嵌在 SkillData.graphData([SerializeReference] 多态),无需额外的 .asset
    //  工具支持:Wizard 顶部 + 按钮 → 弹菜单三选一(节点类型 / 动画片段 / 多段)→ 写入 graphData
    //  运行时:SkillRunner 阶段按节点类型分发执行(节点 = 现有逻辑;动画片段层 = 播 animClip;
    //         多段层 = 走 multiStage.segments 流水线)。
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 动画片段层 — 对应 SkillData.animClips[i] 的"在时间轴上的可视化层"。
    /// 字段:AnimationClip + triggerTime + editorDuration(条上显示用)。
    /// </summary>
    [Serializable]
    public partial class AnimClipLayerData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.AnimClip;
        public override string DisplayName => "动画片段";

        [Tooltip("绑定的 AnimationClip(对应 SkillData.animClips[i])。")]
        public AnimationClip animClip;

        [Tooltip("在时间轴上的触发时刻(秒)。可拖动调整,运行时由 SkillRunner 调度。")]
        public float triggerTime = 0f;

        [Tooltip("动画播放速度倍率(1=原速,2=2倍快,0.5=慢动作)。")]
        [Range(0.1f, 4f)]
        public float animSpeed = 1f;

        [Tooltip("淡入时长(秒):从当前姿势混合到本动画的过渡时间。0=硬切。")]
        [Range(0f, 0.5f)]
        public float fadeInDuration = 0.05f;

        [Tooltip("淡出时长(秒):本动画结束后混合回 Idle 的时间。0=硬切。")]
        [Range(0f, 0.5f)]
        public float fadeOutDuration = 0.1f;

        // editorDuration 复用基类 SkillNodeData.editorDuration(避免 [SerializeReference] 同名字段冲突)
        // — AnimClipLayerData:0 = 用 animClip.length 自动适配
    }

    /// <summary>
    /// 多段层 — 对应 SkillData.multiStage.segments[i] 的"在时间轴上的可视化层"。
    /// 字段:段名 + 段专用动画片段 + 段间隔 + 命中时机 + 段独立 VFX/SFX。
    /// </summary>
    [Serializable]
    public partial class MultiStageLayerData : SkillNodeData
    {
        public override SkillNodeCategory Category => SkillNodeCategory.MultiStage;
        public override string DisplayName => "多段配置";

        [Tooltip("段标签/段名(只用于 Inspector 识别,例:\"第1段:抬刀\")。")]
        public string stageName = "段";

        [Tooltip("本段专用的动画片段(优先于 SkillData.animClips[i],留空 = 沿用共用)。")]
        public AnimationClip animClip;

        [Tooltip("本段在时间轴上的触发时刻(秒)。可拖动调整。")]
        public float triggerTime = 0f;

        [Tooltip("本段动画播放速度倍率(1=原速,2=2 倍快,0.5=慢动作)。")]
        public float animSpeed = 1f;

        [Tooltip("本段起手时播放的 VFX prefab(留空 = 沿用 SkillData.vfxOnCast)。")]
        public GameObject vfxOnCast;

        [Tooltip("本段起手时播放的 SFX 音效(留空 = 沿用 SkillData.sfxOnCast)。")]
        public AudioClip sfxOnCast;

        [Tooltip("本段 SFX 音高随机百分比(0~30)。")]
        public float sfxPitchRandomPercent = 10f;

        // ── P3:起手 VFX 生成配置(字段名对齐 CastVFXData,供 EmitVFXFromNode 反射读取) ──
        [Tooltip("起手 VFX 挂点骨骼名(如 RightHand)。留空=角色根节点。")]
        public string spawnBone;

        [Tooltip("起手 VFX 是否世界空间(true=跟随世界方向,不受骨骼旋转影响)。")]
        public bool worldSpace;

        [Tooltip("起手 VFX 生成位置偏移(相对挂点骨骼本地坐标)。")]
        public Vector3 positionOffset;

        [Tooltip("起手 VFX 旋转偏移(欧拉角,相对挂点骨骼朝向)。")]
        public Vector3 rotationOffset;

        [Tooltip("起手 VFX 缩放倍率(1=原大小)。")]
        public float scale = 1f;

        [Tooltip("起手 VFX 自动销毁时间(秒)。0=不自动销毁(30s 兜底)。")]
        public float destroyAfterSeconds;

        // ── P3 废弃:命中 VFX/SFX 请用独立的 HitVFXData 节点替代(运行时忽略以下字段) ──
        [HideInInspector, Tooltip("段命中 VFX — P3 废弃,请添加独立的 HitVFXData 节点替代。")]
        public GameObject vfxOnHit;

        [HideInInspector, Tooltip("段命中 SFX — P3 废弃,请添加独立的 HitVFXData 节点替代。")]
        public AudioClip sfxOnHit;

        [Range(0f, 1f)]
        [Tooltip("本段命中判定时机(0~1 归一化,在本段动画播到该比例时触发命中检测)。")]
        public float hitAtNormalized = 0f;

        [Tooltip("本段是否真的产生命中(出伤/挂 debuff/弹体)。")]
        public bool dealsDamage = true;

        [Tooltip("本段命中后施加的伤害倍率(相对 graphData 伤害值)。")]
        public float damageMultiplier = 1f;
    }
}
