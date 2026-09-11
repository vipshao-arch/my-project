// =====================================================================
//  SkillTrace — 统一诊断日志（预览 & 运行时用同一格式输出）
//
//  目标：技能预览和 Play Mode 释放时输出同一格式的诊断摘要，
//        便于逐项对照 controller / state / layer / clip / triggerTime 等关键字段。
//
//  使用：
//    SkillTrace.LogPreview(animator, data, stateName, layerIndex, clipLength, ...)
//    SkillTrace.LogRuntime(animator, data, stateName, layerIndex, clipLength,
//                          frontSwingTime, backSwingStart, ...)
// =====================================================================

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// 预览快照：由 LogPreview() 写入，供 LogCompare() 与运行时逐项对照。
    /// </summary>
    [System.Serializable]
    public class TraceSnapshot
    {
        public string skillName;
        public string controllerName;
        public string stateName;
        public string layerName;
        public int layerIndex;
        public string clipName;
        public float clipLength;
        public float frontSwingRatio;
        public float frontSwingTime;
        public float backSwingRatio;
        public float backSwingStart;
    }

    public static class SkillTrace
    {
        // ── 公共开关（Editor 通过菜单/按钮控制） ──
        public static bool Enabled = false;

        // ── 上次预览快照（键 = skillName） ──
        public static TraceSnapshot LastPreview { get; private set; }

        // ── 反射缓存：读取节点 triggerTime ──
        private static readonly Dictionary<string, System.Reflection.FieldInfo> s_triggerTimeCache
            = new Dictionary<string, System.Reflection.FieldInfo>();

        private static float GetNodeTriggerTime(SkillNodeData node)
        {
            if (node == null) return 0f;
            var t = node.GetType();
            var key = t.FullName;
            if (!s_triggerTimeCache.TryGetValue(key, out var fi))
            {
                fi = t.GetField("triggerTime",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                s_triggerTimeCache[key] = fi;
            }
            if (fi == null) return 0f;
            try { return (float)fi.GetValue(node); }
            catch { return 0f; }
        }

        // ── 公共：格式化节点列表 ──
        private static void AppendNodes(StringBuilder sb, List<SkillNodeData> graphData)
        {
            if (graphData == null || graphData.Count == 0)
            {
                sb.AppendLine("  nodes=(empty)");
                return;
            }
            for (int i = 0; i < graphData.Count; i++)
            {
                var node = graphData[i];
                if (node == null) continue;
                float tt = GetNodeTriggerTime(node);
                sb.AppendLine($"  node[{i}]={node.GetType().Name} triggerTime={tt:F3}");
            }
        }

        // ── 公共：格式化 VFX 挂点信息 ──
        private static void AppendSpawnInfo(StringBuilder sb, SkillNodeData node, Animator animator)
        {
            if (node == null || animator == null) return;
            var t = node.GetType();
            var spawnBoneField = t.GetField("spawnBone",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var worldSpaceField = t.GetField("worldSpace",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var posOffsetField = t.GetField("positionOffset",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            string spawnBone = null;
            bool worldSpace = true;
            Vector3 posOffset = Vector3.zero;

            if (spawnBoneField != null)
                spawnBone = spawnBoneField.GetValue(node) as string;
            if (worldSpaceField != null)
                worldSpace = (bool)worldSpaceField.GetValue(node);
            if (posOffsetField != null)
                posOffset = (Vector3)posOffsetField.GetValue(node);

            if (!string.IsNullOrEmpty(spawnBone))
            {
                bool resolved = false;
                if (animator != null && animator.isHuman &&
                    System.Enum.TryParse<HumanBodyBones>(spawnBone, true, out HumanBodyBones bone))
                {
                    resolved = animator.GetBoneTransform(bone) != null;
                }
                else
                {
                    resolved = animator.transform.Find(spawnBone) != null
                        || FindDeepChild(animator.transform, spawnBone) != null;
                }
                sb.AppendLine($"  spawnBone={spawnBone} resolved={resolved}");
            }
            sb.AppendLine($"  worldSpace={worldSpace}");
            if (posOffset != Vector3.zero)
                sb.AppendLine($"  positionOffset=({posOffset.x:F2}, {posOffset.y:F2}, {posOffset.z:F2})");
        }

        private static Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindDeepChild(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════
        //  预览侧 Trace
        // ════════════════════════════════════════════════════════════
        public static void LogPreview(
            Animator animator,
            SkillData data,
            string stateName,
            int layerIndex,
            string clipName,
            float clipLength,
            float frontSwingRatio,
            float backSwingRatio)
        {
            if (!Enabled || data == null) return;

            var controller = animator != null ? animator.runtimeAnimatorController : null;
            string layerName = (layerIndex >= 0 && animator != null)
                ? animator.GetLayerName(layerIndex) : "n/a";
            float frontSwingTime = clipLength * frontSwingRatio;
            float backSwingStart = clipLength * (1f - backSwingRatio);
            Vector3 fwd = animator != null ? animator.transform.forward : Vector3.forward;

            var sb = new StringBuilder();
            sb.AppendLine($"[SkillTrace:PREVIEW] skill={data.skillName}");
            sb.AppendLine($"  controller={controller?.name ?? "null"}");
            sb.AppendLine($"  state={stateName}");
            sb.AppendLine($"  layer={layerName}({layerIndex})");
            sb.AppendLine($"  clip={clipName}");
            sb.AppendLine($"  clipLength={clipLength:F3}");
            sb.AppendLine($"  frontSwingRatio={frontSwingRatio:F3}");
            sb.AppendLine($"  frontSwingTime={frontSwingTime:F3}");
            sb.AppendLine($"  backSwingRatio={backSwingRatio:F3}");
            sb.AppendLine($"  backSwingStart={backSwingStart:F3}");
            AppendNodes(sb, data.graphData);
            sb.AppendLine($"  forward=({fwd.x:F2}, {fwd.y:F2}, {fwd.z:F2})");

            Debug.Log(sb.ToString());

            // P5:缓存快照,供 LogCompare 对照
            LastPreview = new TraceSnapshot
            {
                skillName        = data.skillName,
                controllerName   = controller?.name ?? "null",
                stateName        = stateName,
                layerName        = layerName,
                layerIndex       = layerIndex,
                clipName         = clipName,
                clipLength       = clipLength,
                frontSwingRatio  = frontSwingRatio,
                frontSwingTime   = frontSwingTime,
                backSwingRatio   = backSwingRatio,
                backSwingStart   = backSwingStart,
            };
        }

        // ════════════════════════════════════════════════════════════
        //  运行时 Trace
        // ════════════════════════════════════════════════════════════
        public static void LogRuntime(
            Animator animator,
            SkillData data,
            string stateName,
            int layerIndex,
            string clipName,
            float clipLength,
            float frontSwingRatio,
            float backSwingRatio,
            float frontSwingTime,
            float backSwingStart)
        {
            if (!Enabled || data == null) return;

            var controller = animator != null ? animator.runtimeAnimatorController : null;
            string layerName = (layerIndex >= 0 && animator != null)
                ? animator.GetLayerName(layerIndex) : "n/a";
            Vector3 fwd = animator != null ? animator.transform.forward : Vector3.forward;

            var sb = new StringBuilder();
            sb.AppendLine($"[SkillTrace:RUNTIME] skill={data.skillName}");
            sb.AppendLine($"  controller={controller?.name ?? "null"}");
            sb.AppendLine($"  state={stateName}");
            sb.AppendLine($"  layer={layerName}({layerIndex})");
            sb.AppendLine($"  clip={clipName}");
            sb.AppendLine($"  clipLength={clipLength:F3}");
            sb.AppendLine($"  frontSwingRatio={frontSwingRatio:F3}");
            sb.AppendLine($"  frontSwingTime={frontSwingTime:F3}");
            sb.AppendLine($"  backSwingRatio={backSwingRatio:F3}");
            sb.AppendLine($"  backSwingStart={backSwingStart:F3}");
            AppendNodes(sb, data.graphData);
            sb.AppendLine($"  forward=({fwd.x:F2}, {fwd.y:F2}, {fwd.z:F2})");

            Debug.Log(sb.ToString());

            // P5:自动对比预览快照
            LogCompare(data.skillName, controller?.name ?? "null", stateName, layerName, layerIndex,
                       clipName, clipLength, frontSwingRatio, backSwingRatio, frontSwingTime, backSwingStart);
        }

        // ════════════════════════════════════════════════════════════
        //  自动对比（P5：预览理论值 vs 运行时实测值）
        // ════════════════════════════════════════════════════════════
        private static void LogCompare(
            string skillName,
            string controllerName,
            string stateName,
            string layerName,
            int layerIndex,
            string clipName,
            float clipLength,
            float frontSwingRatio,
            float backSwingRatio,
            float runtimeFrontSwingTime,
            float runtimeBackSwingStart)
        {
            if (LastPreview == null) return;

            var preview = LastPreview;
            // 仅对比同名技能（避免切换技能后误对照）
            if (preview.skillName != skillName) return;

            float tolerance = Mathf.Max(0.02f, clipLength * 0.02f); // 2% 或 20ms 取大者

            var sb = new StringBuilder();
            sb.AppendLine($"[SkillTrace:COMPARE] {skillName}");
            int warnings = 0;

            // 基础字段快速对照
            if (preview.controllerName != controllerName)
            { sb.AppendLine($"  ⚠ controller: preview={preview.controllerName} runtime={controllerName}"); warnings++; }
            if (preview.stateName != stateName)
            { sb.AppendLine($"  ⚠ state: preview={preview.stateName} runtime={stateName}"); warnings++; }
            if (preview.clipName != clipName)
            { sb.AppendLine($"  ⚠ clip: preview={preview.clipName} runtime={clipName}"); warnings++; }

            // frontSwingTime:预览用 ratio*clipLength（理论值），运行时用实际计时
            float frontDelta = Mathf.Abs(runtimeFrontSwingTime - preview.frontSwingTime);
            if (frontDelta > tolerance)
            {
                sb.AppendLine($"  ⚠ frontSwingTime: preview={preview.frontSwingTime:F3}s runtime={runtimeFrontSwingTime:F3}s delta={frontDelta:F3}s");
                warnings++;
            }
            else
                sb.AppendLine($"  ✓ frontSwingTime={preview.frontSwingTime:F3}s (delta={frontDelta:F3}s)");

            // backSwingStart:同上
            float backDelta = Mathf.Abs(runtimeBackSwingStart - preview.backSwingStart);
            if (backDelta > tolerance)
            {
                sb.AppendLine($"  ⚠ backSwingStart: preview={preview.backSwingStart:F3}s runtime={runtimeBackSwingStart:F3}s delta={backDelta:F3}s");
                warnings++;
            }
            else
                sb.AppendLine($"  ✓ backSwingStart={preview.backSwingStart:F3}s (delta={backDelta:F3}s)");

            sb.AppendLine(warnings > 0
                ? $"  result={warnings} MISMATCH(ES) — 预览理论值与运行时实测值不一致，请检查动画事件/过渡"
                : $"  result=ALL MATCH ✓");

            Debug.Log(sb.ToString());
        }
    }
}
