// 2026-07-21:SO 节点体系已废弃(仅旧资产反序列化兼容),豁免 CS0618。
#pragma warning disable 0618

using UnityEngine;
using System.Collections.Generic;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 矩形投射物节点（RectShot：散弹/冲击波/扇形弹幕/穿透激光/追踪弹）。
    /// 委托给现有 RectProjectile + RectShotVFX 组件。
    /// 替代 SkillData.rectShot* + projectile* 字段。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Rect Shot", fileName = "Node_RectShot")]
    public class RectShotNode : SkillNode
    {
        [Header("Damage")]
        [Tooltip("单发伤害。")]
        public float damage = 5f;

        [Header("Geometry")]
        public ShotShape shape = ShotShape.Rect;
        [Tooltip("矩形宽度（米）。Rect 模式 = 横向判定宽度;Line = 自动忽略;Cone = 末端最大宽度。")]
        public float width  = 2f;
        [Tooltip("矩形高度（米）。一般等于或略大于角色身高。")]
        public float height = 1.8f;

        [Header("Travel")]
        public float speed = 18f;
        public float maxRange = 6f;

        [Header("Spread")]
        [Range(1, 9)]
        [Tooltip("一次发射的投射物数量。")]
        public int count = 1;
        [Range(0f, 90f)]
        [Tooltip("多发散弹的扇形分散角度（度）。仅 count>1 时生效。")]
        public float spreadAngle = 0f;

        [Header("Hit Mode")]
        public ShotHitMode hitMode = ShotHitMode.Stop;
        [Range(0, 99)]
        public int pierceCount = 0;

        [Header("Homing")]
        public bool homingEnabled = false;
        [Range(0f, 1440f)] public float homingTurnRate     = 540f;
        public float homingSearchRadius = 10f;

        [Header("Initial Offset")]
        [Range(-90f, 90f)] public float initialYawOffset   = 0f;
        [Range(-45f, 45f)] public float initialPitchOffset = 0f;
        [Tooltip("弹体生成点离地高度（米，默认 0.9 = 角色腰部）。")]
        public float spawnHeight = 0.9f;
        [Range(0f, 2f)] public float forwardOffset = 0.3f;

        [Header("Projectile Prefab")]
        [Tooltip("飞行弹体 prefab（裸 GO，如 ETFX BulletSmallBlue）。留空走 RectShotVFX Scene 视图 Gizmos 绘制（Game 视图不显示）。")]
        public GameObject projectilePrefab;
        public Vector3 projectileLocalOffset      = Vector3.zero;
        public Vector3 projectileLocalEulerOffset = Vector3.zero;
        public float   projectileLifetime        = 2f;
        public float   projectileLocalScale       = 1f;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            int   c     = Mathf.Max(1, count);
            float half  = c > 1 ? spreadAngle * 0.5f : 0f;

            for (int i = 0; i < c; i++)
            {
                float spreadYaw = 0f;
                if (c > 1) spreadYaw = Mathf.Lerp(-half, half, (float)i / (c - 1));
                float totalYaw = spreadYaw + initialYawOffset;
                Quaternion yawRot   = Quaternion.AngleAxis(totalYaw, Vector3.up);
                Quaternion pitchRot = Quaternion.AngleAxis(initialPitchOffset, Vector3.right);
                Vector3 shotDir = (yawRot * pitchRot) * ctx.caster.forward;

                Vector3 spawnPos = ctx.caster.position + shotDir * forwardOffset;
                spawnPos.y = ctx.caster.position.y + spawnHeight;

                var go = new GameObject(c > 1
                    ? $"[RectShot]_{ctx.source.name}_{i}"
                    : $"[RectShot]_{ctx.source.name}");
                go.transform.position = spawnPos;
                go.transform.rotation = Quaternion.LookRotation(shotDir);

                var rect = go.AddComponent<RectProjectile>();
                rect.shotWidth  = width;
                rect.shotHeight = height;
                rect.speed      = speed;
                rect.maxRange   = maxRange;

                // 走最完整 Initialize：把节点的所有字段灌进 RectProjectile
                float dmg = damage > 0f ? damage : ctx.damage;
                rect.Initialize(dmg, ctx.source, ctx.hitMask, ctx.skill, projectilePrefab,
                                projectileLocalOffset, projectileLocalEulerOffset,
                                projectileLifetime, projectileLocalScale,
                                shape, hitMode, pierceCount, homingEnabled, homingTurnRate, homingSearchRadius);
            }
        }
    }

    /// <summary>
    /// 光束节点（Beam：SphereCastAll 沿前方扫掠，宽度 = beamWidth）。
    /// 替代 SkillData.effectType = Beam。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Beam", fileName = "Node_Beam")]
    public class BeamNode : SkillNode
    {
        [Header("Beam")]
        [Tooltip("光束宽度（米），SphereCastAll 球半径。")]
        public float beamWidth = 0.25f;
        [Tooltip("光束长度（米）。")]
        public float range = 10f;
        [Tooltip("起点 Y 偏移（米，1.0=胸口）。")]
        public float originHeight = 1f;
        [Tooltip("单次伤害（或治疗）。")]
        public float damage = 10f;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            Vector3 origin  = ctx.caster.position + Vector3.up * originHeight;
            Vector3 forward = ctx.caster.forward;
            RaycastHit[] hits = Physics.SphereCastAll(origin, beamWidth, forward, range, ctx.hitMask);

            var hitList = new List<GameObject>();
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i].collider;
                if (c.gameObject == ctx.source) continue;
                if (hitList.Contains(c.gameObject)) continue;
                var d = c.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;
                float dmg = damage > 0f ? damage : ctx.damage;
                SkillNodeBehaviorUtil.ApplyDamage(ctx, d, dmg, forward);
                hitList.Add(c.gameObject);
            }

            SkillRunner.NotifyHit(ctx, HitInfo.Many(hitList, origin, forward));
        }
    }

    /// <summary>
    /// 链式弹射节点（ChainBounce）。
    /// 命中首发后用 Physics.OverlapSphere 搜索 N 个最近目标，按 falloff 衰减。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Chain Bounce", fileName = "Node_ChainBounce")]
    public class ChainBounceNode : SkillNode
    {
        [Range(1, 12)]
        [Tooltip("链式弹射总次数（含首发）。")]
        public int bounceCount = 4;
        [Tooltip("弹射搜索半径（米）。")]
        public float searchRadius = 8f;
        [Range(0.1f, 1f)]
        [Tooltip("每次弹射伤害衰减（0.7 = 每次剩 70%）。")]
        public float damageFalloff = 0.7f;
        [Tooltip("单次弹射最大距离（米），0=不限。")]
        public float maxBounceRange = 12f;
        [Tooltip("首发伤害。")]
        public float damage = 5f;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            // 首发：ctx.caster 半径内找最近目标作为链起点
            var chain = new List<GameObject>();
            var first = FindClosestTarget(ctx.caster.position, chain, ctx);
            if (first != null)
            {
                chain.Add(first);
                var d0 = first.GetComponentInParent<IDamageable>();
                float currentDmg = damage > 0f ? damage : ctx.damage;
                if (d0 != null)
                    SkillNodeBehaviorUtil.ApplyDamage(ctx, d0, currentDmg, (first.transform.position - ctx.caster.position).normalized);

                Vector3 lastPos = first.transform.position;
                for (int i = 1; i < bounceCount; i++)
                {
                    var next = FindClosestTarget(lastPos, chain, ctx);
                    if (next == null) break;
                    chain.Add(next);
                    currentDmg *= damageFalloff;
                    var d = next.GetComponentInParent<IDamageable>();
                    if (d == null) break;
                    SkillNodeBehaviorUtil.ApplyDamage(ctx, d, currentDmg, (next.transform.position - lastPos).normalized);
                    lastPos = next.transform.position;
                    if (maxBounceRange > 0f &&
                        Vector3.Distance(ctx.caster.position, lastPos) > maxBounceRange) break;
                }
            }

            SkillRunner.NotifyHit(ctx, HitInfo.Many(chain, ctx.caster.position, ctx.caster.forward));
        }

        private GameObject FindClosestTarget(Vector3 from, List<GameObject> exclude, NodeContext ctx)
        {
            Collider[] hits = Physics.OverlapSphere(from, searchRadius, ctx.hitMask);
            float best = float.MaxValue;
            GameObject bestGo = null;
            for (int i = 0; i < hits.Length; i++)
            {
                var go = hits[i].gameObject;
                if (go == ctx.source) continue;
                if (exclude.Contains(go)) continue;
                var d = go.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;
                float dist = (go.transform.position - from).sqrMagnitude;
                if (dist < best) { best = dist; bestGo = go; }
            }
            return bestGo;
        }
    }

    /// <summary>
    /// 抛物线投射物节点（CurvedProjectile）。
    /// 用 RectProjectile 现有逻辑 + 简易重力修正：发射方向 = forward + pitch offset，
    /// 飞行中按 curveGravity 累积垂直速度，落地自毁。
    ///
    /// 注：当前实现走 RectProjectile（它的 _destroyed 在命中时停下）。
    /// 真实抛物线在落地判定（y 速度反向且 |y| < curveGroundSnapY）时销毁。
    /// 本节点用一个独立的 ParabolicProjectile MonoBehaviour 承载这部分逻辑。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Curved Projectile", fileName = "Node_CurvedProjectile")]
    public class CurvedProjectileNode : SkillNode
    {
        [Header("Geometry")]
        public float width  = 2f;
        public float height = 1.8f;
        public float speed  = 12f;
        public float maxRange = 8f;

        [Header("Arc")]
        [Tooltip("重力加速度（米/秒²），默认 9.8 = 地球重力。")]
        public float gravity = 9.8f;
        [Tooltip("落地判定阈值（米）。Y 速度反向且 |y| < 此值时视为落地。")]
        public float groundSnapY = 0.2f;
        [Tooltip("初始俯仰角（度，正=上仰），决定抛物线弧度。")]
        [Range(0f, 80f)]
        public float launchPitch = 30f;

        [Header("Spawn")]
        public float spawnHeight = 0.9f;
        public float forwardOffset = 0.3f;

        [Header("Hit Mode")]
        public ShotHitMode hitMode = ShotHitMode.Stop;
        public int pierceCount = 0;

        [Header("Damage")]
        [Tooltip("首发伤害。0=用 ctx.damage(graphData 公共伤害值)。")]
        public float damage = 0f;

        [Header("Projectile Prefab")]
        public GameObject projectilePrefab;
        public Vector3 projectileLocalOffset      = Vector3.zero;
        public Vector3 projectileLocalEulerOffset = Vector3.zero;
        public float projectileLifetime = 3f;
        public float projectileLocalScale = 1f;

        // 2026-07-21 执行层统一:行为已迁移至 CurvedProjectileData(SkillNodeData.Behaviors.cs)。
        // 本 SO 节点类仅保留用于旧 Node_*.asset 反序列化兼容,不再参与运行时执行。
        public override void OnHit(NodeContext ctx, HitInfo hit) { }
    }
}
