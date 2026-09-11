#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// 统一目标上下文 — 以 Prefab GUID 为核心标识，解决"步骤完成状态绑定错误目标"的问题。
    ///
    /// 核心设计：
    ///   - 每个目标角色/敌人由 Prefab GUID 唯一标识
    ///   - 步骤完成状态绑定到 (PrefabGUID, StepType) 而非 (Character/Enemy, StepType)
    ///   - 提供输入指纹，检测目标资产是否在上次完成后被修改
    ///   - EditorPrefs 仅保存 UI 视图状态，不保存"完成事实"；完成事实由本类管理
    ///
    /// 使用方式：
    ///   var ctx = CharacterKitTargetContext.FromPrefab(prefab);
    ///   ctx.MarkStepComplete(StepType.CharacterSetup);
    ///   bool isDone = ctx.IsStepComplete(StepType.CharacterSetup);
    ///   bool dirty = ctx.HasTargetChangedSince("lastFingerprint");
    /// </summary>
    public class CharacterKitTargetContext
    {
        // ═══════════════════════════════════════════════════════════════
        // 持久化键名空间（与旧的 CharacterKit_Pipeline_* 隔离）
        // ═══════════════════════════════════════════════════════════════
        private const string KeyPrefix = "CK_Target_";

        // ═══════════════════════════════════════════════════════════════
        // 核心标识
        // ═══════════════════════════════════════════════════════════════

        /// <summary>目标 Prefab 的 GUID（主要标识符）。</summary>
        public string prefabGuid { get; private set; }

        /// <summary>目标 Prefab 的资产路径。</summary>
        public string prefabPath { get; private set; }

        /// <summary>目标类型。</summary>
        public SetupPipeline.PipelineTarget targetType { get; private set; }

        /// <summary>资源文件夹路径（如 Assets/_Game/Resources/Character/MainPlayer/Bai）。</summary>
        public string resourceFolder { get; private set; }

        /// <summary>角色/敌人名称。</summary>
        public string displayName { get; private set; }

        // ═══════════════════════════════════════════════════════════════
        // 步骤完成状态（按 GUID 隔离）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>获取指定步骤的完成状态。</summary>
        public bool IsStepComplete(SetupPipeline.StepType step)
        {
            return EditorPrefs.GetBool(StepKey(step), false);
        }

        /// <summary>标记指定步骤为已完成。</summary>
        public void MarkStepComplete(SetupPipeline.StepType step, bool complete = true)
        {
            EditorPrefs.SetBool(StepKey(step), complete);
        }

        /// <summary>清除所有步骤完成标记。</summary>
        public void ClearAllStepMarks()
        {
            foreach (SetupPipeline.StepType step in Enum.GetValues(typeof(SetupPipeline.StepType)))
            {
                EditorPrefs.DeleteKey(StepKey(step));
            }
        }

        /// <summary>清除当前目标的所有数据（步骤标记 + 指纹）。</summary>
        public void ClearAll()
        {
            ClearAllStepMarks();
            ClearFingerprint();
        }

        // ═══════════════════════════════════════════════════════════════
        // 输入指纹 — 检测目标资产是否已变化
        // ═══════════════════════════════════════════════════════════════

        /// <summary>计算并保存当前目标状态的指纹（用于后续变化检测）。</summary>
        public string ComputeAndSaveFingerprint()
        {
            string fp = ComputeFingerprint();
            EditorPrefs.SetString(FingerprintKey(), fp);
            return fp;
        }

        /// <summary>检查目标资产自上次指纹以来是否发生了变化。</summary>
        public bool HasTargetChanged()
        {
            string saved = EditorPrefs.GetString(FingerprintKey(), "");
            if (string.IsNullOrEmpty(saved)) return true; // 无指纹 = 视为已变化
            string current = ComputeFingerprint();
            return saved != current;
        }

        /// <summary>清除指纹记录。</summary>
        public void ClearFingerprint()
        {
            EditorPrefs.DeleteKey(FingerprintKey());
        }

        // ═══════════════════════════════════════════════════════════════
        // 工厂方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>从 Prefab 创建目标上下文。</summary>
        /// <param name="prefab">目标 Prefab（场景实例或资产均可）</param>
        /// <param name="targetType">目标类型</param>
        /// <returns>上下文实例，如果 prefab 无效则返回 null</returns>
        public static CharacterKitTargetContext FromPrefab(
            GameObject prefab, SetupPipeline.PipelineTarget targetType)
        {
            if (prefab == null) return null;

            // 优先用 Prefab 资产路径（场景实例则通过 PrefabUtility 获取）
            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefab);
            if (string.IsNullOrEmpty(assetPath))
                assetPath = AssetDatabase.GetAssetPath(prefab);

            if (string.IsNullOrEmpty(assetPath)) return null;

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return null;

            string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(assetPath);

            return new CharacterKitTargetContext
            {
                prefabGuid = guid,
                prefabPath = assetPath,
                targetType = targetType,
                resourceFolder = folder,
                displayName = name,
            };
        }

        /// <summary>从资源目录和目标名称创建上下文（用于尚未生成 Prefab 的场景）。</summary>
        /// <param name="resourceFolder">资源文件夹路径</param>
        /// <param name="displayName">角色/敌人名称</param>
        /// <param name="targetType">目标类型</param>
        /// <param name="expectedPrefabPath">预期的 Prefab 路径（如已生成则传入 GUID 对应的路径）</param>
        /// <returns>上下文实例</returns>
        public static CharacterKitTargetContext FromFolder(
            string resourceFolder, string displayName,
            SetupPipeline.PipelineTarget targetType, string expectedPrefabPath = null)
        {
            string guid = "";
            string prefabPath = expectedPrefabPath ?? "";

            // 尝试从已有 Prefab 获取 GUID
            if (!string.IsNullOrEmpty(expectedPrefabPath))
            {
                guid = AssetDatabase.AssetPathToGUID(expectedPrefabPath);
            }

            // 如果还没有 GUID，基于文件夹路径生成确定性标识符
            if (string.IsNullOrEmpty(guid))
            {
                guid = GenerateDeterministicId(resourceFolder, displayName, targetType);
            }

            return new CharacterKitTargetContext
            {
                prefabGuid = guid,
                prefabPath = prefabPath,
                targetType = targetType,
                resourceFolder = resourceFolder ?? "",
                displayName = displayName ?? "Unknown",
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // 便捷属性
        // ═══════════════════════════════════════════════════════════════

        /// <summary>是否有有效的 Prefab GUID。</summary>
        public bool hasValidGuid => !string.IsNullOrEmpty(prefabGuid);

        /// <summary>获取当前目标类型的标签。</summary>
        public string targetTypeLabel =>
            targetType == SetupPipeline.PipelineTarget.Character ? "主角" : "敌人";

        /// <summary>获取人类可读的摘要。</summary>
        public string summary =>
            $"[{targetTypeLabel}] {displayName}  ({resourceFolder})";

        // ═══════════════════════════════════════════════════════════════
        // 步骤完成状态查询（便捷方法）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>获取所有步骤的完成状态映射。</summary>
        public Dictionary<SetupPipeline.StepType, bool> GetAllStepStates()
        {
            var dict = new Dictionary<SetupPipeline.StepType, bool>();
            foreach (SetupPipeline.StepType step in Enum.GetValues(typeof(SetupPipeline.StepType)))
            {
                dict[step] = IsStepComplete(step);
            }
            return dict;
        }

        /// <summary>获取已完成的步骤数。</summary>
        public int CompletedStepCount
        {
            get
            {
                int count = 0;
                foreach (var kv in GetAllStepStates())
                    if (kv.Value) count++;
                return count;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 内部方法
        // ═══════════════════════════════════════════════════════════════

        private string StepKey(SetupPipeline.StepType step)
        {
            return $"{KeyPrefix}{prefabGuid}_{step}";
        }

        private string FingerprintKey()
        {
            return $"{KeyPrefix}{prefabGuid}_Fingerprint";
        }

        /// <summary>计算当前目标状态的 SHA256 指纹。</summary>
        private string ComputeFingerprint()
        {
            var sb = new StringBuilder();
            sb.Append(prefabPath);
            sb.Append(resourceFolder);
            sb.Append(displayName);

            // 如果 Prefab 存在，加入其修改时间
            if (!string.IsNullOrEmpty(prefabPath))
            {
                string fullPath = Path.GetFullPath(prefabPath);
                if (File.Exists(fullPath))
                {
                    sb.Append(File.GetLastWriteTimeUtc(fullPath).Ticks);
                }
            }

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        /// <summary>基于文件夹+名称+类型生成确定性标识符。</summary>
        private static string GenerateDeterministicId(
            string folder, string name, SetupPipeline.PipelineTarget targetType)
        {
            string input = $"{folder}|{name}|{targetType}";
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                // 取前 16 字节转为 GUID 格式
                byte[] guidBytes = new byte[16];
                Array.Copy(hash, guidBytes, 16);
                return new Guid(guidBytes).ToString("N");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 静态工具方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>清理所有 CharacterKit 目标上下文数据（不包括旧的 CharacterKit_Pipeline_* 键）。</summary>
        public static void ClearAllTargetsData()
        {
            // 仅清理 CK_Target_ 前缀的键
            // EditorPrefs 没有批量删除 API，需要遍历已知键
            // 实际使用时按需清理即可，这里提供占位
            Debug.Log("[CharacterKitTargetContext] 已请求清理所有目标数据"
                + "（可通过 DeleteAll CK_Target_ 前缀键完成，需要 EditorPrefs 包装）");
        }
    }
}
#endif
