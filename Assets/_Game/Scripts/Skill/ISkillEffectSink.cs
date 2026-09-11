// =====================================================================
//  ISkillEffectSink —— 技能效果落地接口（2026-07-21 执行层统一重构）
//
//  目的:
//    节点行为(SkillNodeData.OnCast/OnTick/OnHit)只描述"做什么",
//    "做到哪里"由 sink 决定:
//      - RuntimeSkillEffectSink  → 游戏世界(Instantiate/有声/真实命中)
//      - PreviewSkillEffectSink  → 编辑器离屏预览(HideAndDontSave 池/静音/Gizmo)
//
//  这样同一份节点行为代码被 SkillRunner(运行时)与
//  SkillPreviewRuntime(编辑器预览)共用,彻底消除双份实现。
// =====================================================================

using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// 技能效果落地接口。节点行为通过 ctx.sink 调用,
    /// 预览与运行时各自实现,互不影响。
    /// </summary>
    public interface ISkillEffectSink
    {
        /// <summary>解析 VFX 挂点骨骼。找不到时返回 null(调用方兜底 ctx.caster)。</summary>
        Transform ResolveSpawnBone(NodeContext ctx, string boneName);

        /// <summary>
        /// 生成一个 VFX/表现实例。
        /// scale:统一缩放倍率(1=不缩放); parent:非 null 时挂为其子物体;
        /// destroyAfterSeconds &lt;= 0 表示"不指定"(运行时=不销毁,预览=走默认回收)。
        /// 返回生成的实例(可能为 null,如 prefab 为 null)。
        /// </summary>
        GameObject SpawnVFX(GameObject prefab, Vector3 pos, Quaternion rot,
                            float scale, Transform parent, float destroyAfterSeconds);

        /// <summary>播放音效。预览侧实现为静音播放(保留时长推进语义)。</summary>
        void PlaySFX(AudioClip clip, Vector3 pos, float pitchRandomPercent);

        /// <summary>
        /// 画一个命中判定示意(预览 Gizmo)。运行时默认空实现。
        /// angleDeg:扇形半角(度),&gt;=360 或 &lt;=0 表示整圆。
        /// </summary>
        void EmitHitGizmo(SkillNodeData node, Vector3 center, float radius,
                          float angleDeg, float durationHint);
    }

    /// <summary>
    /// 运行时 sink:VFX 进游戏世界,SFX 有声,命中走真实物理(在节点行为内)。
    /// 单例无状态,直接 RuntimeSkillEffectSink.Instance 使用。
    /// </summary>
    public class RuntimeSkillEffectSink : ISkillEffectSink
    {
        public static readonly RuntimeSkillEffectSink Instance = new RuntimeSkillEffectSink();

        public Transform ResolveSpawnBone(NodeContext ctx, string boneName)
        {
            if (ctx == null || ctx.caster == null) return null;
            if (string.IsNullOrEmpty(boneName)) return ctx.caster;

            // 1. HumanBodyBones 枚举查找(RightHand / LeftHand / Head 等)
            var animator = ctx.source != null ? ctx.source.GetComponent<Animator>() : null;
            if (animator != null && animator.isHuman &&
                System.Enum.TryParse<HumanBodyBones>(boneName, true, out HumanBodyBones bone))
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null) return t;
            }

            // 2. 角色层级子物体查找
            var charBone = FindChildByName(ctx.caster, boneName);
            if (charBone != null) return charBone;

            // 3. 武器子级挂载点(VFX_BladeTip / VFX_Muzzle 等)
            var weaponHolder = ctx.source != null ? ctx.source.GetComponent<WeaponHolder>() : null;
            if (weaponHolder != null)
            {
                var weaponMount = weaponHolder.GetWeaponMountPoint(boneName);
                if (weaponMount != null) return weaponMount;
            }

            // 4. 退到角色根
            return ctx.caster;
        }

        public GameObject SpawnVFX(GameObject prefab, Vector3 pos, Quaternion rot,
                                   float scale, Transform parent, float destroyAfterSeconds)
        {
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, pos, rot);
            if (scale != 1f) go.transform.localScale = go.transform.localScale * scale;
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: true);
            if (destroyAfterSeconds > 0f) Object.Destroy(go, destroyAfterSeconds);
            return go;
        }

        public void PlaySFX(AudioClip clip, Vector3 pos, float pitchRandomPercent)
        {
            if (clip == null) return;
            SkillAnimPlayer.PlaySfxAtPoint(clip, pos, pitchRandomPercent);
        }

        public void EmitHitGizmo(SkillNodeData node, Vector3 center, float radius,
                                 float angleDeg, float durationHint)
        {
            // 运行时不画预览 Gizmo(debugDraw 由 CombatRangeDebugDraw 等专用组件承担)
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
}
