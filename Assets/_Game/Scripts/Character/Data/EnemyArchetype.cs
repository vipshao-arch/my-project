using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 敌人"配方"数据模型（Character Kit Phase 2 — 统一数据层）。
    ///
    /// 解决问题：技能/敌人/武器三处数据重复声明（动画、伤害、范围、攻击 clip 列表
    /// 在 SkillData、EnemyAI、WeaponData 中各存一份，调一处必漏另一处）。
    ///
    /// 设计思路：
    ///   EnemyArchetype = EnemyBehaviorData + 攻击技能绑定 + 武器配置 + 掉落表 + 难度乘数
    ///   策划在一个资产里完成敌人的全部配置，Character Kit → Enemy Tab 提供可视化编辑。
    ///
    /// 使用方式：
    ///   1. 在 EnemyAI 上新增 archetype 字段，拖入 EnemyArchetype 资产
    ///   2. Awake() 调用 ApplyTo(ai) 把行为参数/技能/武器一次性写入 EnemyAI
    ///   3. archetype 为空 = 完全沿用 Prefab 上当前值（向后兼容）
    ///
    /// 与 EnemyBehaviorData 的关系：
    ///   EnemyArchetype 包含 EnemyBehaviorData 的全部字段（直接内联，不嵌套引用），
    ///   同时增加技能绑定、武器配置、掉落表。旧的 EnemyBehaviorData 仍可独立使用，
    ///   但推荐新敌人统一用 EnemyArchetype。
    /// </summary>
    [CreateAssetMenu(fileName = "NewEnemyArchetype", menuName = "Game/Character System/Enemy Archetype")]
    public class EnemyArchetype : ScriptableObject
    {
        // ═══════════════════════════════════════════════════════════════
        // 基础身份
        // ═══════════════════════════════════════════════════════════════
        [Header("═══ 基础身份 ═══")]
        [Tooltip("配方显示名（例：『哥布林战士』『精英骷髅』）")]
        public string archetypeName = "未命名敌人配方";

        [Tooltip("配方描述，给策划看的备忘")]
        [TextArea(2, 4)]
        public string description = "";

        [Tooltip("敌人分类标签（用于筛选/对比）")]
        public EnemyClass enemyClass = EnemyClass.Common;

        // ═══════════════════════════════════════════════════════════════
        // Detection（感知）— 内联 EnemyBehaviorData 字段
        // ═══════════════════════════════════════════════════════════════
        [Header("═══ 感知（Detection） ═══")]
        [Tooltip("视野范围（米）")]
        public float detectionRange = 8f;
        [Tooltip("视野角度（度，-1=全方向感知）\n180 = 前方半圆（推荐），120 = 前方扇形偏小，360 = 全方向")]
        public float detectionAngle = 180f;
        [Tooltip("可检测的目标 Layer")]
        public LayerMask targetLayer = 1 << 8;

        // ═══════════════════════════════════════════════════════════════
        // Combat（攻击）— 内联 EnemyBehaviorData 字段
        // ═══════════════════════════════════════════════════════════════
        [Header("═══ 攻击（Combat） ═══")]
        [Tooltip("攻击范围（米）")]
        public float attackRange = 1.5f;
        [Tooltip("攻击扇形半角（度）。实际判定角度 = hitAngle * 2。")]
        [Range(10f, 180f)]
        public float attackHitAngle = 90f;
        [Tooltip("攻击间隔（秒）")]
        public float attackCooldown = 1.5f;
        [Tooltip("攻击伤害")]
        public float attackDamage = 10f;
        [Tooltip("攻击击退力度")]
        public float attackKnockback = 3f;
        [Tooltip("攻击前摇（秒）")]
        public float attackStartUpTime = 0.15f;
        [Tooltip("命中判定延时（秒）")]
        public float attackHitDelay = 0.3f;
        [Tooltip("攻击后摇（秒）")]
        public float attackRecoverTime = 0.4f;
        [Tooltip("后摇前段锁位比例（0~1）")]
        [Range(0f, 1f)]
        public float lockRecoveryRatio = 0.35f;

        // ═══════════════════════════════════════════════════════════════
        // Movement（追击/巡逻）— 内联 EnemyBehaviorData 字段
        // ═══════════════════════════════════════════════════════════════
        [Header("═══ 移动（追击/巡逻） ═══")]
        [Tooltip("追踪速度倍率（相对 EnemyMotor.moveSpeed）")]
        public float chaseSpeedMultiplier = 1.2f;
        [Tooltip("巡逻半径（米）")]
        public float patrolRadius = 3f;
        [Tooltip("巡逻间隔（秒）")]
        public float patrolInterval = 3f;
        [Tooltip("脱离战斗后返回出生点的距离阈值")]
        public float loseTargetRange = 12f;

        // ═══════════════════════════════════════════════════════════════
        // Health Bar（血条）
        // ═══════════════════════════════════════════════════════════════
        [Header("═══ Health Bar（血条） ═══")]
        public bool showHealthBar = true;
        public float healthBarWidth = 80f;
        public float healthBarHeight = 8f;
        public float healthBarHeightOffset = 2.2f;
        public float healthBarScale = 0.01f;

        // ═══════════════════════════════════════════════════════════════
        // 掉落表
        // ═══════════════════════════════════════════════════════════════
        [Header("═══ 掉落表 ═══")]
        [Tooltip("掉落物品列表（目前仅存引用，掉落逻辑后续接入）")]
        public EnemyDropEntry[] dropTable = new EnemyDropEntry[0];

        // ═══════════════════════════════════════════════════════════════
        // Debug
        // ═══════════════════════════════════════════════════════════════
        [Header("═══ 调试（Debug） ═══")]
        public bool debugDraw = false;

        // ═══════════════════════════════════════════════════════════════
        // 数据 ↔ 组件 双向桥接
        // ═══════════════════════════════════════════════════════════════

        /// <summary>把本资产的值写入目标 EnemyAI（Awake 时调用，或 Editor "应用" 按钮）。</summary>
        public void ApplyTo(EnemyAI ai)
        {
            if (ai == null) return;

            // 行为参数
            ai.detectionRange = detectionRange;
            ai.detectionAngle = detectionAngle;
            ai.targetLayer = targetLayer;
            ai.attackRange = attackRange;
            ai.attackHitAngle = attackHitAngle;
            ai.attackCooldown = attackCooldown;
            ai.attackDamage = attackDamage;
            ai.attackKnockback = attackKnockback;
            ai.attackStartUpTime = attackStartUpTime;
            ai.attackHitDelay = attackHitDelay;
            ai.attackRecoverTime = attackRecoverTime;
            ai.lockRecoveryRatio = lockRecoveryRatio;
            ai.chaseSpeedMultiplier = chaseSpeedMultiplier;
            ai.patrolRadius = patrolRadius;
            ai.patrolInterval = patrolInterval;
            ai.loseTargetRange = loseTargetRange;
            ai.showHealthBar = showHealthBar;
            ai.healthBarWidth = healthBarWidth;
            ai.healthBarHeight = healthBarHeight;
            ai.healthBarHeightOffset = healthBarHeightOffset;
            ai.healthBarScale = healthBarScale;

            ai.debugDraw = debugDraw;
        }

        /// <summary>从目标 EnemyAI 读取当前值回填本资产（Editor "提取" 按钮用）。</summary>
        public void CopyFrom(EnemyAI ai)
        {
            if (ai == null) return;

            detectionRange = ai.detectionRange;
            detectionAngle = ai.detectionAngle;
            targetLayer = ai.targetLayer;
            attackRange = ai.attackRange;
            attackHitAngle = ai.attackHitAngle;
            attackCooldown = ai.attackCooldown;
            attackDamage = ai.attackDamage;
            attackKnockback = ai.attackKnockback;
            attackStartUpTime = ai.attackStartUpTime;
            attackHitDelay = ai.attackHitDelay;
            attackRecoverTime = ai.attackRecoverTime;
            lockRecoveryRatio = ai.lockRecoveryRatio;
            chaseSpeedMultiplier = ai.chaseSpeedMultiplier;
            patrolRadius = ai.patrolRadius;
            patrolInterval = ai.patrolInterval;
            loseTargetRange = ai.loseTargetRange;
            showHealthBar = ai.showHealthBar;
            healthBarWidth = ai.healthBarWidth;
            healthBarHeight = ai.healthBarHeight;
            healthBarHeightOffset = ai.healthBarHeightOffset;
            healthBarScale = ai.healthBarScale;

            debugDraw = ai.debugDraw;
        }

        /// <summary>从另一个 EnemyArchetype 复制所有字段值（深拷贝，用于从模板克隆）。</summary>
        public void CopyFromTemplate(EnemyArchetype template)
        {
            if (template == null) return;
            archetypeName = template.archetypeName;
            description = template.description;
            enemyClass = template.enemyClass;
            detectionRange = template.detectionRange;
            detectionAngle = template.detectionAngle;
            targetLayer = template.targetLayer;
            attackRange = template.attackRange;
            attackHitAngle = template.attackHitAngle;
            attackCooldown = template.attackCooldown;
            attackDamage = template.attackDamage;
            attackKnockback = template.attackKnockback;
            attackStartUpTime = template.attackStartUpTime;
            attackHitDelay = template.attackHitDelay;
            attackRecoverTime = template.attackRecoverTime;
            lockRecoveryRatio = template.lockRecoveryRatio;
            chaseSpeedMultiplier = template.chaseSpeedMultiplier;
            patrolRadius = template.patrolRadius;
            patrolInterval = template.patrolInterval;
            loseTargetRange = template.loseTargetRange;
            showHealthBar = template.showHealthBar;
            healthBarWidth = template.healthBarWidth;
            healthBarHeight = template.healthBarHeight;
            healthBarHeightOffset = template.healthBarHeightOffset;
            healthBarScale = template.healthBarScale;
            dropTable = template.dropTable;
            debugDraw = template.debugDraw;
        }
    }

    /// <summary>敌人分类标签。</summary>
    public enum EnemyClass
    {
        Common,     // 普通怪
        Elite,      // 精英怪
        Boss,       // Boss
        MiniBoss,   // 小 Boss
        Swarm,      // 群怪/杂兵
        Ranged,     // 远程
        Caster,     // 法师
        Assassin,   // 刺客
        Tank,       // 坦克
    }

    /// <summary>敌人掉落条目。</summary>
    [System.Serializable]
    public class EnemyDropEntry
    {
        [Tooltip("掉落物品 Prefab（金币/药水/装备等）")]
        public GameObject itemPrefab;

        [Tooltip("掉落概率（0~1）")]
        [Range(0f, 1f)]
        public float dropChance = 0.5f;

        [Tooltip("最小掉落数量")]
        public int minCount = 1;

        [Tooltip("最大掉落数量")]
        public int maxCount = 1;
    }
}
