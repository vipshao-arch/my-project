using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 主角移动手感配置资产（Character Kit Phase 1 数据化重构）。
    ///
    /// 用途：把 CharacterMotor 上原本硬编码的移动/跳跃/受击/血量/耐力等数值参数抽成
    /// 独立 ScriptableObject，方便策划维护多套"手感配置"（如"轻盈手感" / "沉重手感"）
    /// 并在 Character Kit → Player Tab 中快速切换对比，无需改代码或逐项翻 Inspector。
    ///
    /// 兼容策略（渐进迁移，零风险）：
    ///   - CharacterMotor 新增 <see cref="configData"/> 字段，留空时完全不影响现有行为
    ///   - 拖入资产后 Awake() 调用 <see cref="ApplyTo"/> 覆盖对应字段
    ///   - Character Kit 提供"从当前角色提取默认配置"一键生成资产
    /// </summary>
    [CreateAssetMenu(fileName = "NewPlayerConfig", menuName = "Game/Character System/Player Config Data")]
    public class PlayerConfigData : ScriptableObject
    {
        [Header("自由视角速度")]
        [Tooltip("自由视角下的行走速度（米/秒）")]
        public float freeWalkSpeed = 2f;
        [Tooltip("自由视角下的跑步速度（米/秒）")]
        public float freeRunSpeed = 3f;
        [Tooltip("自由视角下的冲刺速度（米/秒）")]
        public float freeSprintSpeed = 5f;
        [Tooltip("自由视角下的蹲伏速度（米/秒）")]
        public float freeCrouchSpeed = 1.5f;
        [Tooltip("自由视角下的旋转速度（度/秒）")]
        public float freeRotationSpeed = 10f;

        [Header("锁定视角速度")]
        [Tooltip("锁定目标时的行走速度（米/秒）")]
        public float strafeWalkSpeed = 2f;
        [Tooltip("锁定目标时的跑步速度（米/秒）")]
        public float strafeRunSpeed = 3f;
        [Tooltip("锁定目标时的冲刺速度（米/秒）")]
        public float strafeSprintSpeed = 4.5f;
        [Tooltip("锁定目标时的蹲伏速度（米/秒）")]
        public float strafeCrouchSpeed = 1.5f;
        [Tooltip("锁定目标时的旋转速度（度/秒）")]
        public float strafeRotationSpeed = 10f;

        [Header("跳跃")]
        [Tooltip("跳跃高度（米）")]
        public float jumpHeight = 4f;
        [Tooltip("跳跃前冲距离（米）")]
        public float jumpForward = 3f;
        [Tooltip("跳跃持续时间（秒）")]
        public float jumpTimer = 0.3f;
        [Tooltip("是否允许空中控制方向")]
        public bool jumpAirControl = true;
        [Tooltip("土狼时间：离开平台后仍可跳跃的宽限时间（秒）")]
        public float coyoteTime = 0.15f;
        [Tooltip("跳跃缓冲：提前按下跳跃键的有效时间（秒）")]
        public float jumpBufferTime = 0.12f;

        [Header("击退")]
        [Tooltip("被击退的持续时间（秒）")]
        public float knockbackDuration = 0.5f;

        [Header("速度平滑")]
        [Tooltip("速度过渡的平滑系数（越大越灵敏）")]
        public float speedLerpRate = 12f;

        [Header("地面检测")]
        [Tooltip("检测地面的最小距离（米）")]
        public float groundMinDistance = 0.2f;
        [Tooltip("检测地面的最大距离（米）")]
        public float groundMaxDistance = 0.5f;
        [Tooltip("可攀爬的最大坡度（度）")]
        public float slopeLimit = 45f;
        [Tooltip("额外重力加速度")]
        public float extraGravity = -10f;
        [Tooltip("上台阶结束偏移（米）")]
        public float stepOffsetEnd = 0.45f;
        [Tooltip("上台阶起始偏移（米）")]
        public float stepOffsetStart = 0.05f;
        [Tooltip("上台阶平滑过渡系数")]
        public float stepSmooth = 4f;

        [Header("数值资源")]
        [Tooltip("最大生命值")]
        public float maxHealth = 100f;
        [Tooltip("生命值每秒恢复量")]
        public float healthRecovery = 0f;
        [Tooltip("受击后开始恢复生命的延迟（秒）")]
        public float healthRecoveryDelay = 3f;
        [Tooltip("最大耐力值")]
        public float maxStamina = 100f;
        [Tooltip("耐力每秒恢复量")]
        public float staminaRecovery = 20f;
        [Tooltip("最大法力值")]
        public float maxMana = 100f;
        [Tooltip("法力每秒恢复量")]
        public float manaRecovery = 0.3f;
        [Tooltip("冲刺每秒消耗耐力")]
        public float sprintStaminaCost = 25f;
        [Tooltip("跳跃消耗耐力")]
        public float jumpStaminaCost = 15f;
        [Tooltip("翻滚消耗耐力")]
        public float rollStaminaCost = 15f;

        [Header("布娃娃/杂项")]
        [Tooltip("进入布娃娃状态的速度阈值（低于此值触发 Ragdoll）")]
        public float ragdollVelocity = -50f;
        [Tooltip("随机待机动画间隔（秒），0=禁用随机待机")]
        public float randomIdleTime = 0f;

        // ═══════════════════════════════════════════════════════════════
        // 数据 ↔ 组件 双向桥接
        // ═══════════════════════════════════════════════════════════════

        /// <summary>把本资产的值写入目标 CharacterMotor（Awake 时调用，或 Editor "应用" 按钮）</summary>
        public void ApplyTo(CharacterMotor motor)
        {
            if (motor == null) return;
            motor.freeWalkSpeed = freeWalkSpeed;
            motor.freeRunSpeed = freeRunSpeed;
            motor.freeSprintSpeed = freeSprintSpeed;
            motor.freeCrouchSpeed = freeCrouchSpeed;
            motor.freeRotationSpeed = freeRotationSpeed;
            motor.strafeWalkSpeed = strafeWalkSpeed;
            motor.strafeRunSpeed = strafeRunSpeed;
            motor.strafeSprintSpeed = strafeSprintSpeed;
            motor.strafeCrouchSpeed = strafeCrouchSpeed;
            motor.strafeRotationSpeed = strafeRotationSpeed;
            motor.jumpHeight = jumpHeight;
            motor.jumpForward = jumpForward;
            motor.jumpTimer = jumpTimer;
            motor.jumpAirControl = jumpAirControl;
            motor.coyoteTime = coyoteTime;
            motor.jumpBufferTime = jumpBufferTime;
            motor.knockbackDuration = knockbackDuration;
            motor.speedLerpRate = speedLerpRate;
            motor.groundMinDistance = groundMinDistance;
            motor.groundMaxDistance = groundMaxDistance;
            motor.slopeLimit = slopeLimit;
            motor.extraGravity = extraGravity;
            motor.stepOffsetEnd = stepOffsetEnd;
            motor.stepOffsetStart = stepOffsetStart;
            motor.stepSmooth = stepSmooth;
            motor.maxHealth = maxHealth;
            motor.healthRecovery = healthRecovery;
            motor.healthRecoveryDelay = healthRecoveryDelay;
            motor.maxStamina = maxStamina;
            motor.staminaRecovery = staminaRecovery;
            motor.maxMana = maxMana;
            motor.manaRecovery = manaRecovery;
            motor.sprintStaminaCost = sprintStaminaCost;
            motor.jumpStaminaCost = jumpStaminaCost;
            motor.rollStaminaCost = rollStaminaCost;
            motor.ragdollVelocity = ragdollVelocity;
            motor.randomIdleTime = randomIdleTime;
        }

        /// <summary>从目标 CharacterMotor 读取当前值回填本资产（Editor "提取" 按钮用）</summary>
        public void CopyFrom(CharacterMotor motor)
        {
            if (motor == null) return;
            freeWalkSpeed = motor.freeWalkSpeed;
            freeRunSpeed = motor.freeRunSpeed;
            freeSprintSpeed = motor.freeSprintSpeed;
            freeCrouchSpeed = motor.freeCrouchSpeed;
            freeRotationSpeed = motor.freeRotationSpeed;
            strafeWalkSpeed = motor.strafeWalkSpeed;
            strafeRunSpeed = motor.strafeRunSpeed;
            strafeSprintSpeed = motor.strafeSprintSpeed;
            strafeCrouchSpeed = motor.strafeCrouchSpeed;
            strafeRotationSpeed = motor.strafeRotationSpeed;
            jumpHeight = motor.jumpHeight;
            jumpForward = motor.jumpForward;
            jumpTimer = motor.jumpTimer;
            jumpAirControl = motor.jumpAirControl;
            coyoteTime = motor.coyoteTime;
            jumpBufferTime = motor.jumpBufferTime;
            knockbackDuration = motor.knockbackDuration;
            speedLerpRate = motor.speedLerpRate;
            groundMinDistance = motor.groundMinDistance;
            groundMaxDistance = motor.groundMaxDistance;
            slopeLimit = motor.slopeLimit;
            extraGravity = motor.extraGravity;
            stepOffsetEnd = motor.stepOffsetEnd;
            stepOffsetStart = motor.stepOffsetStart;
            stepSmooth = motor.stepSmooth;
            maxHealth = motor.maxHealth;
            healthRecovery = motor.healthRecovery;
            healthRecoveryDelay = motor.healthRecoveryDelay;
            maxStamina = motor.maxStamina;
            staminaRecovery = motor.staminaRecovery;
            maxMana = motor.maxMana;
            manaRecovery = motor.manaRecovery;
            sprintStaminaCost = motor.sprintStaminaCost;
            jumpStaminaCost = motor.jumpStaminaCost;
            rollStaminaCost = motor.rollStaminaCost;
            ragdollVelocity = motor.ragdollVelocity;
            randomIdleTime = motor.randomIdleTime;
        }

        /// <summary>从另一个 PlayerConfigData 复制所有字段值（深拷贝，用于从模板克隆）。</summary>
        public void CopyFromTemplate(PlayerConfigData template)
        {
            if (template == null) return;
            freeWalkSpeed = template.freeWalkSpeed;
            freeRunSpeed = template.freeRunSpeed;
            freeSprintSpeed = template.freeSprintSpeed;
            freeCrouchSpeed = template.freeCrouchSpeed;
            freeRotationSpeed = template.freeRotationSpeed;
            strafeWalkSpeed = template.strafeWalkSpeed;
            strafeRunSpeed = template.strafeRunSpeed;
            strafeSprintSpeed = template.strafeSprintSpeed;
            strafeCrouchSpeed = template.strafeCrouchSpeed;
            strafeRotationSpeed = template.strafeRotationSpeed;
            jumpHeight = template.jumpHeight;
            jumpForward = template.jumpForward;
            jumpTimer = template.jumpTimer;
            jumpAirControl = template.jumpAirControl;
            coyoteTime = template.coyoteTime;
            jumpBufferTime = template.jumpBufferTime;
            knockbackDuration = template.knockbackDuration;
            speedLerpRate = template.speedLerpRate;
            groundMinDistance = template.groundMinDistance;
            groundMaxDistance = template.groundMaxDistance;
            slopeLimit = template.slopeLimit;
            extraGravity = template.extraGravity;
            stepOffsetEnd = template.stepOffsetEnd;
            stepOffsetStart = template.stepOffsetStart;
            stepSmooth = template.stepSmooth;
            maxHealth = template.maxHealth;
            healthRecovery = template.healthRecovery;
            healthRecoveryDelay = template.healthRecoveryDelay;
            maxStamina = template.maxStamina;
            staminaRecovery = template.staminaRecovery;
            maxMana = template.maxMana;
            manaRecovery = template.manaRecovery;
            sprintStaminaCost = template.sprintStaminaCost;
            jumpStaminaCost = template.jumpStaminaCost;
            rollStaminaCost = template.rollStaminaCost;
            ragdollVelocity = template.ragdollVelocity;
            randomIdleTime = template.randomIdleTime;
        }
    }
}
