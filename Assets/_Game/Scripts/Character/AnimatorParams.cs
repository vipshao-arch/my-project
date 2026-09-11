using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// Animator 参数契约(2026-07-30 建立)。
    ///
    /// 全项目 Animator 参数名的唯一来源:所有脚本读写参数一律使用本类常量,
    /// 禁止再出现 StringToHash("字面量") / SetBool("字面量")。
    /// 修改参数名时只需改这里 + Controller YAML + 全量引用检查。
    ///
    /// 命名约定:PascalCase。死亡参数统一为 IsDead(2026-07-30 由 isDead 迁移)。
    /// </summary>
    public static class AnimatorParams
    {
        // ── 移动输入(NormalState 驱动) ──
        public static readonly int InputMagnitude   = Animator.StringToHash("InputMagnitude");
        public static readonly int InputHorizontal  = Animator.StringToHash("InputHorizontal");
        public static readonly int InputVertical    = Animator.StringToHash("InputVertical");

        // ── 移动状态 ──
        public static readonly int IsGrounded       = Animator.StringToHash("IsGrounded");
        public static readonly int IsStrafing       = Animator.StringToHash("IsStrafing");
        public static readonly int IsCrouching      = Animator.StringToHash("IsCrouching");
        // 兼容 DefaultCharacterController 旧 Crouch 状态机条件；运行时同时写 IsCrouching/Crouch。
        public static readonly int Crouch           = Animator.StringToHash("Crouch");
        public static readonly int IsCustomAction   = Animator.StringToHash("IsCustomAction");
        public static readonly int VerticalVelocity = Animator.StringToHash("VerticalVelocity");
        public static readonly int GroundDistance   = Animator.StringToHash("GroundDistance");
        public static readonly int Falling          = Animator.StringToHash("Falling");
        public static readonly int Speed            = Animator.StringToHash("Speed");

        // ── 动作(ActionState 子状态机路由) ──
        public static readonly int ActionState      = Animator.StringToHash("ActionState");
        public static readonly int Roll             = Animator.StringToHash("Roll");
        public static readonly int LandHigh         = Animator.StringToHash("LandHigh");
        public static readonly int QuickStop        = Animator.StringToHash("QuickStop");
        public static readonly int ResetState       = Animator.StringToHash("ResetState");
        public static readonly int RandomIdle       = Animator.StringToHash("RandomIdle");

        // ── 战斗 ──
        public static readonly int IsDead             = Animator.StringToHash("IsDead");
        public static readonly int Hit                = Animator.StringToHash("Hit");
        public static readonly int Attack             = Animator.StringToHash("Attack");
        public static readonly int AttackIndex        = Animator.StringToHash("AttackIndex");
        public static readonly int MoveAttack         = Animator.StringToHash("MoveAttack");
        public static readonly int TurnOnSpotDirection = Animator.StringToHash("TurnOnSpotDirection");
        public static readonly int SkillState         = Animator.StringToHash("SkillState");

        // ── 攀爬(梯子交互) ──
        public static readonly int EnterLadderBottom = Animator.StringToHash("EnterLadderBottom");
        public static readonly int EnterLadderTop    = Animator.StringToHash("EnterLadderTop");
        public static readonly int ClimbLadder       = Animator.StringToHash("ClimbLadder");
        public static readonly int ExitLadderTop     = Animator.StringToHash("ExitLadderTop");
        public static readonly int ExitLadderBottom  = Animator.StringToHash("ExitLadderBottom");
    }
}
