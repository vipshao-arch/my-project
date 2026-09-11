using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 敌人行为参数配置资产（Character Kit Phase 1 数据化重构）。
    ///
    /// 用途：把 EnemyAI 组件上原本硬编码在 Prefab Inspector 里的 20+ 个感知/攻击/移动参数
    /// 抽成独立 ScriptableObject，实现：
    ///   1. 同一份配置可供多个敌人 Prefab 复用（如"精英怪""普通怪"共享一套感知参数）
    ///   2. 策划可脱离 Prefab 直接调参、对比、版本化管理
    ///   3. Character Kit → Enemy Tab 提供可视化编辑 + 一键提取/应用
    ///
    /// 兼容策略（渐进迁移，零风险）：
    ///   - EnemyAI 上新增 <see cref="behaviorData"/> 字段，留空时完全不影响现有行为
    ///     （所有旧 Prefab 继续用 Inspector 上直接配置的值）
    ///   - 一旦拖入 behaviorData 资产，Awake() 会调用 <see cref="ApplyTo"/> 覆盖对应字段
    ///   - 通过 Character Kit 的"提取"按钮可从已有 EnemyAI 一键生成默认配置资产
    /// </summary>
    [CreateAssetMenu(fileName = "NewEnemyBehavior", menuName = "Game/Character System/Enemy Behavior Data")]
    public class EnemyBehaviorData : ScriptableObject
    {
        [Header("Detection（感知）")]
        [Tooltip("视野范围（米）")]
        public float detectionRange = 8f;
        [Tooltip("视野角度（度，-1=全方向感知）\n180 = 前方半圆（推荐），120 = 前方扇形偏小，360 = 全方向")]
        public float detectionAngle = 180f;
        [Tooltip("可检测的目标 Layer")]
        public LayerMask targetLayer = 1 << 8;

        [Header("Combat（攻击）")]
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

        [Header("Movement（追击/巡逻）")]
        [Tooltip("追踪速度倍率（相对 EnemyMotor.moveSpeed）")]
        public float chaseSpeedMultiplier = 1.2f;
        [Tooltip("巡逻半径（米）")]
        public float patrolRadius = 3f;
        [Tooltip("巡逻间隔（秒）")]
        public float patrolInterval = 3f;
        [Tooltip("脱离战斗后返回出生点的距离阈值")]
        public float loseTargetRange = 12f;

        [Header("Health Bar（血条）")]
        public bool showHealthBar = true;
        public float healthBarWidth = 80f;
        public float healthBarHeight = 8f;
        public float healthBarHeightOffset = 2.2f;
        public float healthBarScale = 0.01f;

        [Header("Skill Cast（主动技能）")]
        [Tooltip("技能释放概率（0~1）")]
        [Range(0f, 1f)]
        public float skillCastChance = 0.3f;
        [Tooltip("技能释放距离（米）")]
        public float skillCastRange = 5f;

        [Header("Debug")]
        public bool debugDraw = false;

        // ═══════════════════════════════════════════════════════════════
        // 数据 ↔ 组件 双向桥接（显式赋值，运行时零反射开销）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>把本资产的值写入目标 EnemyAI（Awake 时调用，或 Editor "应用" 按钮）</summary>
        public void ApplyTo(EnemyAI ai)
        {
            if (ai == null) return;
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
            ai.skillCastChance = skillCastChance;
            ai.skillCastRange = skillCastRange;
            ai.debugDraw = debugDraw;
        }

        /// <summary>从目标 EnemyAI 读取当前值回填本资产（Editor "提取" 按钮用）</summary>
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
            skillCastChance = ai.skillCastChance;
            skillCastRange = ai.skillCastRange;
            debugDraw = ai.debugDraw;
        }
    }
}
