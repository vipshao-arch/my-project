// 2026-07-21:SO 节点体系已废弃(仅旧资产反序列化兼容),豁免 CS0618。
#pragma warning disable 0618

using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// 起手 VFX / SFX 节点。
    /// 触发时机：OnCast（动画 t=0）。多段连击/蓄力时可放多个本节点覆盖不同时机。
    /// 替代 SkillData.vfxOnCast + sfxOnCast 槽位。
    /// </summary>
    public class CastVFXNode : SkillNode
    {
        [Header("Prefab")]
        [Tooltip("起手 VFX prefab（自带 Stop Action = Destroy）。推荐：枪口闪光、刀光起手、蓄力光环。")]
        public GameObject prefab;

        [Tooltip("起手音效。")]
        public AudioClip sfx;

        [Header("Spawn")]
        [Tooltip("生成骨骼（HumanBodyBones 名字，如 RightHand/Spine/Head；或 Hierarchy 节点名；留空=角色根）。")]
        public string spawnBone = "RightHand";

        [Tooltip("是否世界空间生成（true=不挂骨骼；false=跟随骨骼运动）。")]
        public bool worldSpace = true;

        [Tooltip("相对骨骼位置偏移（米）。")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("相对骨骼旋转偏移（欧拉角，度）。")]
        public Vector3 rotationOffset = Vector3.zero;

        [Tooltip("prefab 实例 scale 倍率（1=原大小）。")]
        public float scale = 1f;

        [Tooltip("prefab 自销毁秒数（0=用默认 2s；自带销毁逻辑的 prefab 不需要）。")]
        public float destroyAfterSeconds = 0f;

        [Header("Timing")]
        [Tooltip("起手后延迟多少秒再触发（默认 0 = 立刻）。蓄力技能可填 >0。")]
        public float triggerTime = 0f;

        [Header("Audio")]
        [Range(0f, 30f)]
        [Tooltip("SFX 音调随机百分比（0=不随机）。")]
        public float sfxPitchRandomPercent = 10f;

        private bool _fired;

        public override void OnCast(NodeContext ctx)
        {
            _fired = false;
        }

        public override void OnTick(NodeContext ctx)
        {
            if (_fired) return;
            if (ctx.animTime < triggerTime) return;
            _fired = true;

            SpawnVFX(ctx);
            if (sfx != null)
                SkillAnimPlayer.PlaySfxAtPoint(sfx, ctx.caster.position, sfxPitchRandomPercent);
        }

        public override void OnEnd(NodeContext ctx) { _fired = false; }

        private void SpawnVFX(NodeContext ctx)
        {
            if (prefab == null) return;
            var spawnPoint = ResolveSpawnBone(ctx);

            // [DIAG] 运行时诊断 — 与预览 EmitVFXFromNode 逐字段对比
            Debug.Log($"[RUNTIME CastVFX] prefab={prefab.name} animTime={ctx.animTime:F4} "
                     +$"triggerTime={triggerTime:F4} spawnBone=\"{spawnBone}\" resolvedBone={spawnPoint.name} "
                     +$"worldSpace={worldSpace} posOffset={positionOffset.ToString("F4")} rotOffset={rotationOffset.ToString("F3")} "
                     +$"scale={scale:F2} destroyAfter={destroyAfterSeconds}", ctx.source);

            Vector3 pos = spawnPoint.position + spawnPoint.TransformDirection(positionOffset);
            Quaternion rot;
            if (worldSpace)
                rot = Quaternion.LookRotation(ctx.caster.forward, Vector3.up) * Quaternion.Euler(rotationOffset);
            else
                rot = spawnPoint.rotation * Quaternion.Euler(rotationOffset);

            GameObject go = Instantiate(prefab, pos, rot);
            if (scale != 1f) go.transform.localScale = go.transform.localScale * scale;
            if (!worldSpace) go.transform.SetParent(spawnPoint, worldPositionStays: true);
            if (destroyAfterSeconds > 0f) Destroy(go, destroyAfterSeconds);
            ctx.spawnedThisCast.Add(go);
        }

        private Transform ResolveSpawnBone(NodeContext ctx)
        {
            if (string.IsNullOrEmpty(spawnBone)) return ctx.caster;

            // 1. HumanBodyBones 枚举查找（RightHand / LeftHand / Head 等）
            var animator = ctx.source != null ? ctx.source.GetComponent<Animator>() : null;
            if (animator != null && animator.isHuman &&
                System.Enum.TryParse<HumanBodyBones>(spawnBone, true, out HumanBodyBones bone))
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null) return t;
            }

            // 2. 角色层级子物体查找
            var charBone = FindChildByName(ctx.caster, spawnBone);
            if (charBone != null) return charBone;

            // 3. ★ 方案 A：查找武器子级挂载点（VFX_BladeTip / VFX_Muzzle 等）
            var weaponHolder = ctx.source != null ? ctx.source.GetComponent<WeaponHolder>() : null;
            if (weaponHolder != null)
            {
                var weaponMount = weaponHolder.GetWeaponMountPoint(spawnBone);
                if (weaponMount != null) return weaponMount;
            }

            // 4. 退到角色根
            return ctx.caster;
        }

        private static Transform FindChildByName(Transform parent, string n)
        {
            foreach (Transform c in parent)
            {
                if (c.name == n) return c;
                var f = FindChildByName(c, n);
                if (f != null) return f;
            }
            return null;
        }
    }

    /// <summary>
    /// 中段 VFX 节点：动画中段 triggerTime 触发的特效（拖尾/多段粒子/持续光效）。
    /// 替代 SkillData.vfxEntries[] 中除 cast/hit 之外的槽位。
    /// </summary>
    public class MidVFXNode : SkillNode
    {
        [Tooltip("中段 VFX prefab。")]
        public GameObject prefab;

        [Tooltip("相对动画开始的触发时间（秒）。支持多个本节点覆盖不同时机。")]
        public float triggerTime = 0.2f;

        [Tooltip("生成骨骼（留空=角色根）。")]
        public string spawnBone;

        [Tooltip("世界空间生成。true=不挂骨骼（推荐拖尾）。")]
        public bool worldSpace = true;

        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationOffset = Vector3.zero;
        public float scale = 1f;
        public float destroyAfterSeconds = 0f;

        private bool _fired;

        public override void OnCast(NodeContext ctx) { _fired = false; }

        public override void OnTick(NodeContext ctx)
        {
            if (_fired || prefab == null) return;
            if (ctx.animTime < triggerTime) return;
            _fired = true;

            // [DIAG] MidVFX 运行时诊断
            string sb = spawnBone ?? "(null)";
            var resolvedBone = string.IsNullOrEmpty(spawnBone) ? ctx.caster : FindBone(ctx, spawnBone) ?? ctx.caster;
            Debug.Log($"[RUNTIME MidVFX] prefab={prefab.name} animTime={ctx.animTime:F4} "
                     +$"triggerTime={triggerTime:F4} spawnBone=\"{sb}\" resolvedBone={resolvedBone?.name ?? "null"} "
                     +$"worldSpace={worldSpace} posOffset={positionOffset.ToString("F4")} rotOffset={rotationOffset.ToString("F3")} "
                     +$"scale={scale:F2} destroyAfter={destroyAfterSeconds}", ctx.source);

            var spawnPoint = resolvedBone;
            Vector3 pos = spawnPoint.position + spawnPoint.TransformDirection(positionOffset);
            Quaternion rot = worldSpace
                ? Quaternion.LookRotation(ctx.caster.forward, Vector3.up) * Quaternion.Euler(rotationOffset)
                : spawnPoint.rotation * Quaternion.Euler(rotationOffset);

            GameObject go = Instantiate(prefab, pos, rot);
            if (scale != 1f) go.transform.localScale = go.transform.localScale * scale;
            if (!worldSpace) go.transform.SetParent(spawnPoint, worldPositionStays: true);
            if (destroyAfterSeconds > 0f) Destroy(go, destroyAfterSeconds);
            ctx.spawnedThisCast.Add(go);
        }

        public override void OnEnd(NodeContext ctx) { _fired = false; }

        private static Transform FindBone(NodeContext ctx, string name)
        {
            // 1. HumanBodyBones 枚举查找
            var animator = ctx.source != null ? ctx.source.GetComponent<Animator>() : null;
            if (animator != null && animator.isHuman &&
                System.Enum.TryParse<HumanBodyBones>(name, true, out HumanBodyBones bone))
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null) return t;
            }
            // 2. 角色层级子物体
            var charBone = FindChildByName(ctx.caster, name);
            if (charBone != null) return charBone;
            // 3. 武器子级挂载点
            var weaponHolder = ctx.source != null ? ctx.source.GetComponent<WeaponHolder>() : null;
            if (weaponHolder != null)
            {
                var weaponMount = weaponHolder.GetWeaponMountPoint(name);
                if (weaponMount != null) return weaponMount;
            }
            return null;
        }

        private static Transform FindChildByName(Transform parent, string n)
        {
            foreach (Transform c in parent)
            {
                if (c.name == n) return c;
                var f = FindChildByName(c, n);
                if (f != null) return f;
            }
            return null;
        }
    }

    /// <summary>
    /// 命中 VFX / SFX 节点。
    /// 触发时机：OnHit（命中帧），对 hit.targets 中每个目标都播一次。
    /// 替代 SkillData.vfxOnHit + sfxOnHit 槽位。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Hit VFX", fileName = "Node_HitVFX")]
    public class HitVFXNode : SkillNode
    {
        [Tooltip("命中 VFX prefab（自带销毁）。")]
        public GameObject prefab;

        [Tooltip("命中音效。")]
        public AudioClip sfx;

        [Tooltip("命中点 VFX 旋转偏移（度）。")]
        public Vector3 rotationOffset = Vector3.zero;

        [Tooltip("prefab scale 倍率。")]
        public float scale = 1f;

        [Tooltip("prefab 自销毁秒数（0=默认 2s）。")]
        public float destroyAfterSeconds = 2f;

        [Range(0f, 30f)]
        public float sfxPitchRandomPercent = 10f;

        public override void OnCast(NodeContext ctx) { }

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (hit.targets == null || hit.targets.Count == 0) return;

            for (int i = 0; i < hit.targets.Count; i++)
            {
                var t = hit.targets[i];
                if (t == null) continue;
                Vector3 hitPoint = t.transform.position;
                Vector3 hitDir   = (t.transform.position - ctx.caster.position).normalized;

                if (prefab != null)
                {
                    var rot = Quaternion.LookRotation(hitDir.sqrMagnitude > 0.001f ? hitDir : ctx.caster.forward)
                              * Quaternion.Euler(rotationOffset);
                    var go = Instantiate(prefab, hitPoint, rot);
                    if (scale != 1f) go.transform.localScale = go.transform.localScale * scale;
                    if (destroyAfterSeconds > 0f) Destroy(go, destroyAfterSeconds);
                    ctx.spawnedThisCast.Add(go);
                }
                if (sfx != null)
                    SkillAnimPlayer.PlaySfxAtPoint(sfx, hitPoint, sfxPitchRandomPercent);
            }
        }
    }
}
