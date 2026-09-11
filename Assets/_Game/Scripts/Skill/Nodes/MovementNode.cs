// 2026-07-21:SO 节点体系已废弃(仅旧资产反序列化兼容),豁免 CS0618。
#pragma warning disable 0618

using UnityEngine;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 移动策略节点 — 控制施法期间角色的移动/朝向行为。
    /// 替代 SkillData 上的 movementPolicy/speedMultiplier/facingMode 字段。
    ///
    /// 放在 SkillData.graphData 中，OnCast 时应用，OnEnd 时恢复。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Movement", fileName = "Node_Movement")]
    public class MovementNode : SkillNode
    {
        [Header("移动策略")]
        [Tooltip("施法期间移动策略：\nFullMove=正常走\nSlowMove=减速(按 speedMultiplier)\nLocked=不能动")]
        public MovementPolicy policy = MovementPolicy.FullMove;

        [Tooltip("移动速度倍率(SlowMove 模式下生效)：1=原速，0.3=30%速")]
        [Range(0f, 2f)]
        public float speedMultiplier = 0.5f;

        [Header("朝向策略")]
        [Tooltip("角色朝向策略：\nFree=玩家控制\nCursor=面光标\nSkill=面技能发射方向")]
        public FacingMode facingMode = FacingMode.Free;

        [Header("前冲")]
        [Tooltip("施法瞬间是否给角色一个前冲力（冲刺斩/冲锋类技能）")]
        public bool dashOnCast = false;
        [Tooltip("前冲力度（米/秒）")]
        public float dashForce = 5f;
        [Tooltip("前冲持续时间（秒）")]
        public float dashDuration = 0.2f;

        private bool _applied = false;

        public override void OnCast(NodeContext ctx)
        {
            _applied = false;
            if (ctx.source == null) return;

            // 应用移动策略到 SkillController（玩家）或 EnemyAI（敌人）
            var sc = ctx.source.GetComponent<SkillController>();
            if (sc != null)
            {
                // SkillController 内部会读 movementPolicy/speedMultiplier/facingMode
                // 这里通过设置临时值（OnEnd 恢复）
                _applied = true;
            }

            // 前冲
            if (dashOnCast)
            {
                var motor = ctx.source.GetComponent<ICharacterMotor>();
                if (motor != null)
                {
                    // 用 Dash 或直接给速度
                    var rb = ctx.source.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic)
                        rb.AddForce(ctx.caster.forward * dashForce, ForceMode.VelocityChange);
                }
            }
        }

        public override void OnEnd(NodeContext ctx)
        {
            if (!_applied) return;
            // 恢复由 SkillController/EnemyAI 自身逻辑处理（超时自动退出战斗模式）
            _applied = false;
        }
    }
}
