// 2026-07-21:SO 节点体系已废弃(仅旧资产反序列化兼容),豁免 CS0618。
#pragma warning disable 0618

using UnityEngine;
using System.Collections.Generic;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 近战挥刀节点（AreaOfEffect / Melee Swing）。
    /// 独立实现：OverlapCapsule + 扇形过滤 + ClosestPoint。
    /// 命中后调用 ctx.skill 上挂的所有 HitVFXNode 节点来播命中表现。
    /// 替代 SkillData.effectType = AreaOfEffect 时的所有数值字段。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Melee Swing", fileName = "Node_MeleeSwing")]
    public class MeleeSwingNode : SkillNode
    {
        [Header("Hit")]
        [Tooltip("范围（米），胶囊长度 = range。")]
        public float range = 2f;

        [Tooltip("命中半径（米），胶囊半径。")]
        public float hitRadius = 1.5f;

        [Tooltip("命中半角（度），总扇形 = 2*hitAngle。60 = 120° 默认挥刀。")]
        [Range(10f, 180f)]
        public float hitAngle = 60f;

        [Tooltip("单次伤害（或治疗，负数=治疗）。")]
        public float damage = 5f;

        [Header("Origin")]
        [Tooltip("命中胶囊起点 Y 偏移（米，0.8=腰部高度）。")]
        public float originHeight = 0.8f;

        [Tooltip("命中胶囊在角色前方偏移（米，0=从脚下判定）。")]
        public float originForwardOffset = 0f;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            Vector3 origin  = ctx.caster.position + Vector3.up * originHeight + ctx.caster.forward * originForwardOffset;
            Vector3 forward = ctx.caster.forward;
            float   half    = hitAngle;

            Vector3 capsuleTip = origin + forward * range;
            Collider[] hits = Physics.OverlapCapsule(origin, capsuleTip, hitRadius, ctx.hitMask);

            var hitList = new List<GameObject>();
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i];
                if (col.gameObject == ctx.source) continue;
                if (hitList.Contains(col.gameObject)) continue;

                var d = col.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;

                Vector3 closest  = col.ClosestPoint(origin);
                Vector3 toTarget = closest - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f) toTarget = forward;
                float angle = Vector3.Angle(forward, toTarget.normalized);
                if (angle > half) continue;

                float dmg = damage > 0f ? damage : ctx.damage;
                SkillNodeBehaviorUtil.ApplyDamage(ctx, d, dmg, toTarget.normalized);
                hitList.Add(col.gameObject);
            }

            // 把命中结果广播给同一 SkillData 上的所有 HitVFXNode
            var hitInfo = HitInfo.Many(hitList, origin, forward);
            SkillRunner.NotifyHit(ctx, hitInfo);
        }
    }
}
