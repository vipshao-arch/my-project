// =====================================================================
//  PreviewSkillEffectSink —— 编辑器预览侧效果落地(2026-07-21 执行层统一)
//
//  与 RuntimeSkillEffectSink 对偶:
//    - VFX → SkillPreviewRuntime 离屏池(HideAndDontSave + 手动 Simulate)
//    - SFX → 静音 AudioSource 池(保留时长推进语义)
//    - 命中判定 → Gizmo 事件(_liveHits,Handles 绘制)
//    - 骨骼解析 → 预览实例层级(带缓存,对齐运行时优先级)
//
//  节点行为(SkillNodeData.Behaviors)不感知本类,只调 ctx.sink。
// =====================================================================

using UnityEngine;

namespace Game.SkillSystem.EditorTools
{
    public class PreviewSkillEffectSink : ISkillEffectSink
    {
        private readonly SkillPreviewRuntime _rt;

        public PreviewSkillEffectSink(SkillPreviewRuntime rt)
        {
            _rt = rt;
        }

        public Transform ResolveSpawnBone(NodeContext ctx, string boneName)
            => _rt != null ? _rt.ResolveBone(boneName) : (ctx != null ? ctx.caster : null);

        public GameObject SpawnVFX(GameObject prefab, Vector3 pos, Quaternion rot,
                                   float scale, Transform parent, float destroyAfterSeconds)
            => _rt != null
                ? _rt.PooledSpawnVFX(prefab, pos, rot, scale, parent, destroyAfterSeconds)
                : null;

        public void PlaySFX(AudioClip clip, Vector3 pos, float pitchRandomPercent)
        {
            if (_rt == null || clip == null) return;
            _rt.PooledPlaySFX(clip, pitchRandomPercent);
        }

        public void EmitHitGizmo(SkillNodeData node, Vector3 center, float radius,
                                 float angleDeg, float durationHint)
        {
            if (_rt == null) return;
            _rt.PooledEmitHitGizmo(node, center, radius, angleDeg, durationHint);
        }
    }
}
