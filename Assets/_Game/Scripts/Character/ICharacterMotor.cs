using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 角色移动/状态接口。
    /// </summary>
    public interface ICharacterMotor
    {
        // ─── 移动模式 ───
        enum LocomotionType { OnlyFree, OnlyStrafe }
        LocomotionType locomotionType { get; set; }
        bool isStrafing     { get; set; }
        bool lockRotation   { get; set; }
        bool rotateByWorld  { get; set; }
        bool turnOnSpotAnim { get; set; }
        bool lockMovement   { get; set; }

        /// <summary>战斗攻击朝向(世界空间水平单位向量)。由 SkillController 在物理步内写入当帧值,
        /// CharacterMotor 的 Strafe 物理速度方向以此为准,而非 transform.rotation(后者在 LateUpdate 才更新),
        /// 消除方向切换的相位延迟。</summary>
        Vector3 CombatFacing { get; set; }

        // ─── 状态查询 ───
        bool isGrounded   { get; }
        bool isCrouching  { get; }
        bool isSprinting  { get; }
        bool isJumping    { get; }
        bool isRolling    { get; }
        bool isDead       { get; set; }
        bool actions      { get; }
        bool customAction { get; set; }
        bool isInAction   { get; }
        bool ragdolled    { get; }
        bool isSliding    { get; }  // 陡坡滑落中（保持 isGrounded=true，但有滑落速度）
        bool landHigh     { get; }  // 高落差着地硬直中

        // ─── 数值 ───
        float currentHealth  { get; }
        float currentStamina { get; }

        // ─── 输入 ───
        Vector2 input { get; set; }

        // ─── 速度控制 ───
        float speedMultiplier { get; set; }

        // ─── 行为 ───
        void Sprint(bool active);
        void Jump();
        void Roll();
        void Crouch();
        void AirControl();
        void ForceStopMovement();
        void TakeDamage(float damage);
        void Heal(float amount);

        // ─── 重力 / 碰撞 ───
        void DisableGravityAndCollision();
        void EnableGravityAndCollision(float normalizedTime);

        // ─── Root Motion 控制 ───
        void EnableRootMotionPosition();
        void DisableRootMotionPosition();
        void EnableRootMotionRotation();
        void DisableRootMotionRotation();
        bool IsUsingRootMotionPosition { get; }
    }
}
