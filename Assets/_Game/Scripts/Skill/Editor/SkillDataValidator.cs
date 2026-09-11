// 2026-07-21 执行层统一: SkillData 自动验证器。
// 在 Wizard 加载技能 / EnemySetup 生成 SkillData 后调用，
// 检查 graphData 中每个节点的配置合理性。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// SkillData 自动验证器 — 聚焦 graphData 节点层配置错误。
    /// SkillData 自身的 SelfCheckForPreview / SelfCheckForRuntime 负责顶层字段检查。
    /// </summary>
    public static class SkillDataValidator
    {
        public struct Issue
        {
            public enum Severity { Info, Warning, Error }

            public Severity Level;
            public string   Message;

            /// <summary>出问题的节点（用于 Wizard 定位），UIMessage 场景可以为 null。</summary>
            public SkillNodeData Source;

            public static Issue Info(string msg, SkillNodeData src = null) =>
                new Issue { Level = Severity.Info, Message = msg, Source = src };

            public static Issue Warning(string msg, SkillNodeData src = null) =>
                new Issue { Level = Severity.Warning, Message = msg, Source = src };

            public static Issue Error(string msg, SkillNodeData src = null) =>
                new Issue { Level = Severity.Error, Message = msg, Source = src };
        }

        // ── 已知合法骨骼/武器挂点名（仅用于 spawnBone 字符串校验） ──
        private static readonly HashSet<string> KnownBoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // HumanBodyBones 常见枚举值
            "Hips", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg",
            "LeftFoot", "RightFoot", "Spine", "Chest", "UpperChest", "Neck", "Head",
            "LeftShoulder", "RightShoulder", "LeftUpperArm", "RightUpperArm",
            "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand",
            "LeftToes", "RightToes", "LeftEye", "RightEye", "Jaw",
            "LeftThumbProximal", "LeftThumbIntermediate", "LeftThumbDistal",
            "LeftIndexProximal", "LeftIndexIntermediate", "LeftIndexDistal",
            "LeftMiddleProximal", "LeftMiddleIntermediate", "LeftMiddleDistal",
            "LeftRingProximal", "LeftRingIntermediate", "LeftRingDistal",
            "LeftLittleProximal", "LeftLittleIntermediate", "LeftLittleDistal",
            "RightThumbProximal", "RightThumbIntermediate", "RightThumbDistal",
            "RightIndexProximal", "RightIndexIntermediate", "RightIndexDistal",
            "RightMiddleProximal", "RightMiddleIntermediate", "RightMiddleDistal",
            "RightRingProximal", "RightRingIntermediate", "RightRingDistal",
            "RightLittleProximal", "RightLittleIntermediate", "RightLittleDistal",
            "LastBone",
            // WeaponHolder 常见 mount 名
            "VFX_BladeTip", "VFX_Muzzle", "VFX_Handle", "VFX_Grip",
            "VFX_WeaponRoot", "VFX_SlashTop", "VFX_SlashMid",
            "WeaponHolder",
            // 常见 Bip001 骨骼名（非 Unity 枚举，递归查找）
            "Bip001", "Bip001 Spine", "Bip001 Spine1", "Bip001 R Hand",
            "Bip001 L Hand", "Bip001 Head", "Bip001 R Foot", "Bip001 L Foot",
            "Bip001 Weapon", "Bip001 Prop1",
        };

        /// <summary>
        /// 验证一个 SkillData。返回问题列表，空列表 = 通过。
        /// </summary>
        /// <param name="data">待验证技能数据</param>
        /// <param name="defaultClipLength">clipLength 获取失败时的回退值（默认 1f）</param>
        public static IReadOnlyList<Issue> Validate(SkillData data, float defaultClipLength = 1f)
        {
            var issues = new List<Issue>();
            if (data == null) return issues;

            float clipLen = GetClipLength(data, defaultClipLength);

            // ── R0: graphData 为空 ──
            if (data.graphData == null || data.graphData.Count == 0)
            {
                issues.Add(Issue.Warning("graphData 为空，技能释放后不会产生伤害/VFX/SFX/移动"));
                return issues;
            }

            bool hasVFXNode = false;

            foreach (var node in data.graphData)
            {
                if (node == null) continue;

                // R1: VFX 节点 prefab 非空
                CheckPrefab(issues, node);
                // R2: triggerTime 在 [0, clipLength] 内
                CheckTriggerTime(issues, node, data, clipLen);
                // R3: spawnBone 可识别
                CheckSpawnBone(issues, node);

                hasVFXNode |= IsVFXNode(node);
            }

            // R5: Melee 节点 hitAngle
            foreach (var node in data.graphData)
            {
                if (node is MeleeSwingData melee)
                    CheckMeleeAngle(issues, melee);
            }

            // R6: 至少一个 VFX 节点（Info 级）
            if (!hasVFXNode)
                issues.Add(Issue.Info("graphData 中没有 VFX 节点（CastVFXData/HitVFXData/MidVFXData），释放时无特效"));

            return issues;
        }

        // ════════════════════════════════════════════════════════════
        //  各项检查
        // ════════════════════════════════════════════════════════════

        private static void CheckPrefab(List<Issue> issues, SkillNodeData node)
        {
            GameObject prefab = GetPrefab(node);
            if (prefab == null && HasAnyPrefabField(node))
            {
                string nodeName = node.GetType().Name;
                issues.Add(Issue.Warning($"[{nodeName}] prefab 为空 — VFX 节点将不会生成特效", node));
            }
        }

        private static void CheckTriggerTime(List<Issue> issues, SkillNodeData node, SkillData data, float clipLen)
        {
            float tt = node.GetTriggerTime(data, clipLen);
            if (tt < 0f)
                issues.Add(Issue.Warning($"[{node.GetType().Name}] triggerTime < 0（{tt:F2}s），建议设为 0~{clipLen:F1} 内", node));
            else if (tt > clipLen + 0.001f)
                issues.Add(Issue.Warning($"[{node.GetType().Name}] triggerTime（{tt:F2}s）超出 clip 长度（{clipLen:F2}s），节点永远不会触发", node));
        }

        private static void CheckSpawnBone(List<Issue> issues, SkillNodeData node)
        {
            string bone = GetSpawnBone(node);
            if (string.IsNullOrEmpty(bone)) return; // 空 = 使用默认 fallback，OK

            if (!KnownBoneNames.Contains(bone))
            {
                issues.Add(Issue.Info(
                    $"[{node.GetType().Name}] spawnBone=\"{bone}\" 不是已知骨骼/挂点名。运行时将找不到骨骼并退回角色根",
                    node));
            }
        }

        private static void CheckMeleeAngle(List<Issue> issues, MeleeSwingData melee)
        {
            if (melee.hitAngle <= 0f)
                issues.Add(Issue.Error("[MeleeSwingData] hitAngle 必须大于 0（当前为 0）", melee));
            else if (melee.hitAngle > 180f)
                issues.Add(Issue.Warning($"[MeleeSwingData] hitAngle={melee.hitAngle:F0}° 超过 180°，是否误填？", melee));
        }

        // ════════════════════════════════════════════════════════════
        //  辅助
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 获取动画长度：animClips[0].length → defaultClipLength。
        /// 修正自原方案中 data.clipLength(不存在)的错误。
        /// </summary>
        public static float GetClipLength(SkillData data, float fallback = 1f)
        {
            if (data != null && data.animClips != null && data.animClips.Length > 0 && data.animClips[0] != null)
                return data.animClips[0].length;
            return fallback;
        }

        private static bool IsVFXNode(SkillNodeData node)
        {
            return node is CastVFXData || node is HitVFXData || node is MidVFXData;
        }

        private static GameObject GetPrefab(SkillNodeData node)
        {
            if (node is CastVFXData c) return c.prefab;
            if (node is HitVFXData h) return h.prefab;
            if (node is MidVFXData m) return m.prefab;
            return null;
        }

        private static bool HasAnyPrefabField(SkillNodeData node)
        {
            return node is CastVFXData || node is HitVFXData || node is MidVFXData;
        }

        private static string GetSpawnBone(SkillNodeData node)
        {
            if (node is CastVFXData c) return c.spawnBone;
            if (node is HitVFXData h) return h.spawnBone;
            if (node is MidVFXData m) return m.spawnBone;
            return null;
        }
    }
}
