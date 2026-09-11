using Game.Character;
using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// 视线判定服务(2026-07-30 建立,P2 立体基建)。
    ///
    /// 攻守互见统一入口:技能命中格挡(SkillNodeBehaviorUtil)与 AI 威胁判断共用同一语义——
    /// "攻击眼位 → 目标受击点"的 Linecast 被 obstacleMask 层非 Trigger 碰撞体挡住 = 不可见。
    /// 角色(IDamageable)不充当掩体;施法者自身与目标自身不算遮挡。
    ///
    /// 与 L0~L3 掩体分级配合:L1 低掩体挡胸口视线,蹲伏(受击点下降)即安全;
    /// 高位观察者(高打低)越顶可见;抛物线不走视线(天然越掩体)。
    /// </summary>
    public static class LineOfSightService
    {
        /// <summary>默认掩体层(Default)。</summary>
        public static LayerMask DefaultObstacleMask => 1;

        // ─────────────────────────────────────────────────────────────
        //  单点判定
        // ─────────────────────────────────────────────────────────────

        /// <summary>眼位 → 目标受击点(bounds.center)是否通视。</summary>
        public static bool CanSee(Vector3 eyePos, Collider target, LayerMask obstacleMask, GameObject source = null)
        {
            if (target == null) return false;
            return !IsBlocked(eyePos, target.bounds.center, obstacleMask, source, target);
        }

        /// <summary>观察者(transform + 眼高)→ 目标是否通视。</summary>
        public static bool CanSee(Transform observer, float eyeHeight, Collider target,
            LayerMask obstacleMask, GameObject source = null)
        {
            if (observer == null) return false;
            Vector3 eye = observer.position + Vector3.up * eyeHeight;
            return CanSee(eye, target, obstacleMask, source != null ? source : observer.gameObject);
        }

        // ─────────────────────────────────────────────────────────────
        //  多点采样(掩体探头/AI 精确互见用)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 受击点三点采样:胸口(bounds.center)/头(bounds 顶-0.2m)/膝(bounds 底+0.2m),
        /// 任意一点通视即视为可见。
        /// </summary>
        public static bool CanSeeMulti(Vector3 eyePos, Collider target, LayerMask obstacleMask, GameObject source = null)
        {
            if (target == null) return false;
            var b = target.bounds;
            Vector3 chest = b.center;
            Vector3 head  = new Vector3(b.center.x, b.max.y - 0.2f, b.center.z);
            Vector3 knee  = new Vector3(b.center.x, b.min.y + 0.2f, b.center.z);
            return !IsBlocked(eyePos, chest, obstacleMask, source, target)
                || !IsBlocked(eyePos, head,  obstacleMask, source, target)
                || !IsBlocked(eyePos, knee,  obstacleMask, source, target);
        }

        /// <summary>目标身上的"可命中点"列表(供 AI 找探头位/弹道瞄准)。</summary>
        public static void GetAimPoints(Collider target, out Vector3 chest, out Vector3 head, out Vector3 knee)
        {
            var b = target.bounds;
            chest = b.center;
            head  = new Vector3(b.center.x, b.max.y - 0.2f, b.center.z);
            knee  = new Vector3(b.center.x, b.min.y + 0.2f, b.center.z);
        }

        // ─────────────────────────────────────────────────────────────
        //  核心判定(与 SkillNodeBehaviorUtil.IsBlockedByObstacle 同语义)
        // ─────────────────────────────────────────────────────────────

        public static bool IsBlocked(Vector3 eyePos, Vector3 targetPoint, LayerMask obstacleMask,
            GameObject source, Collider targetCollider = null)
        {
            if (obstacleMask.value == 0) return false;
            if ((targetPoint - eyePos).sqrMagnitude < 0.0001f) return false;

            if (!Physics.Linecast(eyePos, targetPoint, out RaycastHit hit,
                    obstacleMask, QueryTriggerInteraction.Ignore))
                return false;

            if (targetCollider != null && hit.collider == targetCollider) return false;
            if (source != null && hit.collider.transform.IsChildOf(source.transform)) return false;
            if (hit.collider.GetComponentInParent<IDamageable>() != null) return false;
            return true;
        }
    }
}
