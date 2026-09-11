#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Game.SkillSystem;

namespace Game.SkillSystem.EditorTools
{
    /// <summary>
    /// 技能资产动画轨道批量修复(2026-07-31,配合 Timeline 动画轨道显示)。
    ///
    /// Timeline 的"动画"轨道由 graphData 中的 AnimClipLayerData(单段)/MultiStageLayerData(多段)
    /// 节点渲染;老资产缺这类节点 → 时间轴无动画轨道。本工具一次扫描全部 SkillData 并写盘:
    ///   单段:有 animClips 但无动画层节点 → 注入 AnimClipLayerData(animClips[0]);
    ///   多段:multiStage.enabled 且 segments 有 clip 但无 MultiStageLayerData
    ///        → 按段重建(triggerTime = i × stageInterval)。
    /// 已有动画层节点的资产跳过(幂等,可反复执行)。
    /// </summary>
    public static class RepairSkillAnimTracks
    {
        [MenuItem("Tools/Combat Sandbox/Repair Skill Anim Tracks")]
        public static void Run()
        {
            int single = 0, multi = 0, resolved = 0, corrected = 0, skipped = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:SkillData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<SkillData>(path);
                if (data == null) continue;
                total++;

                bool hasAnimLayer = data.graphData != null
                    && data.graphData.Exists(n => n is AnimClipLayerData || n is MultiStageLayerData);
                if (hasAnimLayer)
                {
                    // 矫正:已有动画层节点统一标 visualOnly(时间轴参照,运行时不调度——
                    // test09 的 clip 名与 Controller State 名不同名,直接调度会报"State 不存在")
                    bool fixedAny = false;
                    foreach (var n in data.graphData)
                    {
                        if (n is AnimClipLayerData || n is MultiStageLayerData)
                        {
                            if (!n.visualOnly) { n.visualOnly = true; fixedAny = true; }
                        }
                    }
                    if (fixedAny) { EditorUtility.SetDirty(data); corrected++; }
                    else skipped++;
                    continue;
                }

                bool dirty = false;

                // ── 单段:顶层 animClips 有 clip → 注入动画层节点 ──
                if (data.animClips != null && data.animClips.Length > 0 && data.animClips[0] != null)
                {
                    if (data.graphData == null)
                        data.graphData = new System.Collections.Generic.List<SkillNodeData>();
                    data.graphData.Insert(0, new AnimClipLayerData
                    {
                        animClip = data.animClips[0],
                        triggerTime = 0f,
                        visualOnly = true,
                    });
                    single++;
                    dirty = true;
                }
                // ── 多段:segments 有 clip → 重建 MultiStageLayerData ──
                else if (data.multiStage != null && data.multiStage.segments != null
                         && data.multiStage.segments.Count > 0)
                {
                    if (data.graphData == null)
                        data.graphData = new System.Collections.Generic.List<SkillNodeData>();
                    int added = 0;
                    for (int i = 0; i < data.multiStage.segments.Count; i++)
                    {
                        var seg = data.multiStage.segments[i];
                        if (seg == null || seg.animClip == null) continue;
                        data.graphData.Insert(added, new MultiStageLayerData
                        {
                            stageName = string.IsNullOrEmpty(seg.stageName) ? $"第{i + 1}段" : seg.stageName,
                            animClip = seg.animClip,
                            triggerTime = i * data.multiStage.stageInterval,
                            animSpeed = Mathf.Clamp(seg.animSpeed, 0.1f, 4f),
                            visualOnly = true,
                        });
                        added++;
                    }
                    if (added > 0) { multi++; dirty = true; }
                }

                // ── 反查注入:animClips 空(Animator Trigger 状态机路径技能)──
                // 按状态名推导规则(对齐 SkillAnimPlayer.GetAnimatorStateName)在主 Controller
                // 找同名状态,取其 motion clip 注入动画层节点。
                if (!dirty)
                {
                    var clip = ResolveClipFromController(data);
                    if (clip != null)
                    {
                        if (data.graphData == null)
                            data.graphData = new System.Collections.Generic.List<SkillNodeData>();
                        data.graphData.Insert(0, new AnimClipLayerData
                        {
                            animClip = clip,
                            triggerTime = 0f,
                            visualOnly = true,
                        });
                        resolved++;
                        dirty = true;
                    }
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(data);
                    Debug.Log($"[AnimTrackRepair] {path}: 已补动画轨道节点");
                }
                else skipped++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimTrackRepair] 完成:扫描 {total},单段注入 {single},多段重建 {multi},反查注入 {resolved},矫正 {corrected},跳过 {skipped}(单行报告防 MCP 截断)");
        }

        // ── 主 Controller 状态反查(与 OverrideControllerTool 同源常量) ──
        private const string MasterControllerPath = "Assets/_Game/character/Common/DefaultCharacterController.controller";

        /// <summary>按运行时规则推导 Animator 状态名,在主 Controller 找同名状态并取其 clip。</summary>
        private static AnimationClip ResolveClipFromController(SkillData data)
        {
            string stateName = DeriveStateName(data);
            if (string.IsNullOrEmpty(stateName)) return null;

            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(MasterControllerPath);
            if (ctrl == null) return null;

            foreach (var layer in ctrl.layers)
            {
                foreach (var child in layer.stateMachine.states)
                {
                    if (child.state != null && child.state.name == stateName
                        && child.state.motion is AnimationClip clip && clip != null)
                        return clip;
                }
                // 子状态机一层(防御:技能状态一般不嵌套,但主骨架可能分组)
                foreach (var sub in layer.stateMachine.stateMachines)
                {
                    foreach (var child in sub.stateMachine.states)
                    {
                        if (child.state != null && child.state.name == stateName
                            && child.state.motion is AnimationClip clip && clip != null)
                            return clip;
                    }
                }
            }
            return null;
        }

        /// <summary>状态名推导——严格对齐 SkillAnimPlayer.GetAnimatorStateName 的运行时规则。</summary>
        private static string DeriveStateName(SkillData data)
        {
            if (!string.IsNullOrWhiteSpace(data.animClipName)) return data.animClipName;
            if (data.category == SkillCategory.BasicAttack) return "Attack_" + data.animStateId;
            if (!string.IsNullOrEmpty(data.animTrigger) && data.animTrigger.StartsWith("Skill") && data.animTrigger.Length > 5)
                return "Skill_" + data.animTrigger.Substring(5);
            return "Skill_Q";
        }
    }
}
#endif
