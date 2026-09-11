// 2026-07-21:SO 节点体系已废弃(仅旧资产反序列化兼容),豁免 CS0618。
#pragma warning disable 0618

using UnityEngine;
using System.Collections.Generic;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 圆形 AOE 节点（AOECircular）。
    /// OverlapSphere 结算范围内的所有目标。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/AOE Circular", fileName = "Node_AOECircular")]
    public class AOECircularNode : SkillNode
    {
        [Tooltip("圆形影响半径（米）。")]
        public float radius = 4f;
        [Tooltip("AOE 中心：true=以 maxRange 处的目标点为中心，false=以施法者脚下。")]
        public bool centerIsTargetPoint = true;
        [Tooltip("单次伤害（负数=治疗）。")]
        public float damage = 10f;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            Vector3 center = centerIsTargetPoint
                ? ctx.caster.position + ctx.caster.forward * ctx.range
                : ctx.caster.position;

            Collider[] hits = Physics.OverlapSphere(center, radius, ctx.hitMask);
            var hitList = new List<GameObject>();
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c.gameObject == ctx.source) continue;
                var d = c.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;
                float dmg = damage > 0f ? damage : ctx.damage;
                SkillNodeBehaviorUtil.ApplyDamage(ctx, d, dmg, (c.transform.position - center).normalized);
                hitList.Add(c.gameObject);
            }
            SkillRunner.NotifyHit(ctx, HitInfo.Many(hitList, center, ctx.caster.forward));
        }
    }

    /// <summary>
    /// 地面陷阱节点（Trap）。
    /// 在目标点实例化 prefab，按 lifetime 销毁。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Trap", fileName = "Node_Trap")]
    public class TrapNode : SkillNode
    {
        [Tooltip("陷阱 prefab（需自带触发器 + 自销毁或挂 TrapLifetime）。")]
        public GameObject trapPrefab;
        [Tooltip("存在时间（秒），0=无限。")]
        public float lifetime = 30f;
        [Tooltip("同一陷阱两次触发的最小间隔。")]
        public float triggerCooldown = 0.5f;
        [Tooltip("陷阱相对施法者前方的距离（米）。")]
        public float spawnDistance = 2f;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (trapPrefab == null) return;
            Vector3 pos = ctx.caster.position + ctx.caster.forward * spawnDistance;
            var go = Instantiate(trapPrefab, pos, ctx.caster.rotation);
            if (lifetime > 0f) Destroy(go, lifetime);
            ctx.spawnedThisCast.Add(go);
        }
    }

    /// <summary>
    /// 墙体/掩体节点（Wall）。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Wall", fileName = "Node_Wall")]
    public class WallNode : SkillNode
    {
        public GameObject wallPrefab;
        public float width  = 6f;
        public float height = 3f;
        public float lifetime = 5f;
        [Tooltip("墙体相对施法者前方的距离（米）。")]
        public float spawnDistance = 1f;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (wallPrefab == null) return;
            Vector3 pos = ctx.caster.position + ctx.caster.forward * spawnDistance;
            var go = Instantiate(wallPrefab, pos, ctx.caster.rotation);
            go.transform.localScale = new Vector3(width, height, 1f);
            if (lifetime > 0f) Destroy(go, lifetime);
            ctx.spawnedThisCast.Add(go);
        }
    }

    /// <summary>
    /// 召唤节点（Summon）。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Summon", fileName = "Node_Summon")]
    public class SummonNode : SkillNode
    {
        public GameObject summonPrefab;
        public float lifetime = 15f;
        [Range(1, 8)] public int count = 1;
        public float spawnDistance = 2f;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (summonPrefab == null) return;
            for (int i = 0; i < count; i++)
            {
                float angle = (count > 1) ? Mathf.Lerp(-30f, 30f, (float)i / (count - 1)) : 0f;
                Quaternion rot = Quaternion.AngleAxis(angle, Vector3.up) * ctx.caster.rotation;
                Vector3 pos = ctx.caster.position + rot * Vector3.forward * spawnDistance;
                var go = Instantiate(summonPrefab, pos, rot);
                if (lifetime > 0f) Destroy(go, lifetime);
                ctx.spawnedThisCast.Add(go);
            }
        }
    }

    /// <summary>
    /// 持续施法节点（Channeled）。
    /// 在 channelDuration 内每 channelTickInterval 触发一次 OnHit 重入。
    /// 由 SkillRunner 监听。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Channeled", fileName = "Node_Channeled")]
    public class ChanneledNode : SkillNode
    {
        [Tooltip("总施法时长（秒）。0=用 SkillData.channelDuration 字段。")]
        public float durationOverride = 0f;
        [Tooltip("DOT/HOT 周期（秒）。")]
        public float tickInterval = 1f;
        [Tooltip("单次结算量。")]
        public float tickAmount = 5f;
        [Tooltip("施法期间是否锁定移动。")]
        public bool lockMovement = true;
        [Tooltip("单次伤害（可选覆盖 tickAmount）。")]
        public float damage = 0f;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            // Channeled 的"命中"是周期性的，由 SkillRunner 在动画期间反复调用本节点 OnHit
            // 每次 OnHit 走最近的子节点链（递归）：本节点只决定 tick 时机
            // 这里简化：直接对 hit.targets 中的目标造成 tickAmount。
            //
            // damage 优先级策略：
            //   damage(可选覆盖) > tickAmount(默认)
            //   - SkillRecipeBuilder 只设 tickAmount = recipe.damage,不设 damage(保持 0)
            //   - 手动编辑 .asset 时可设 damage 覆盖(如多段技能每段不同伤害)
            //   - ctx.damage 来自 HitDetector,不参与此逻辑(Channeled 用自己节点配置的伤害)
            if (hit.targets == null) return;
            float amount = damage > 0f ? damage : tickAmount;
            for (int i = 0; i < hit.targets.Count; i++)
            {
                var d = hit.targets[i] != null ? hit.targets[i].GetComponentInParent<IDamageable>() : null;
                if (d == null || d.isDead) continue;
                SkillNodeBehaviorUtil.ApplyDamage(ctx, d, amount, ctx.caster.forward);
            }
        }

        public float GetDuration(SkillData data)
        {
            return durationOverride > 0f ? durationOverride : data.channelDuration;
        }

        public float GetTickInterval() => Mathf.Max(0.05f, tickInterval);
    }
}
