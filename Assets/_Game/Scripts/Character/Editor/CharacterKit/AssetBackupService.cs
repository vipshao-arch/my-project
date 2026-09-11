#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// 资产备份服务 — 在破坏性操作（DeleteAsset / CopyAsset）前自动创建带时间戳的备份。
    ///
    /// 备份存储位置：{资产所在文件夹}/_Backups/{yyyy-MM-dd_HHmmss}/{原始文件名}
    ///
    /// 使用方式：
    ///   // 备份单个文件
    ///   AssetBackupService.BackupBeforeOverwrite(existingAssetPath);
    ///
    ///   // 备份整个文件夹
    ///   AssetBackupService.BackupDirectory(folderPath);
    ///
    /// 设计约束：
    ///   - 备份副本与原始文件在同一文件夹的 _Backups 子目录下，便于资源引用修复
    ///   - 使用 AssetDatabase.CopyAsset 确保 .meta 文件同步
    ///   - 失败时仅记录日志，不阻断执行流程
    /// </summary>
    public static class AssetBackupService
    {
        /// <summary>备份根目录名（位于资产所在目录下）。</summary>
        private const string BackupFolderName = "_Backups";

        /// <summary>
        /// 在覆盖/删除前备份指定资产。
        /// 返回备份后的资产路径，失败返回 null。
        /// </summary>
        /// <param name="assetPath">要备份的资产路径（Assets/ 开头的相对路径）</param>
        /// <returns>备份副本的路径，或 null</returns>
        public static string BackupBeforeOverwrite(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("[AssetBackup] assetPath 为空，跳过备份");
                return null;
            }

            // 检查源文件是否存在
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                // 文件不存在 — 无需备份（首次创建场景）
                return null;
            }

            try
            {
                string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
                string fileName = Path.GetFileName(assetPath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);

                // 构建备份目录: {dir}/_Backups/{timestamp}/
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                string backupDir = $"{dir}/{BackupFolderName}/{timestamp}";
                EnsureDirectoryExists(backupDir);

                // 目标备份路径
                string backupPath = $"{backupDir}/{fileName}";

                // 使用 CopyAsset 保持 .meta 同步
                if (AssetDatabase.CopyAsset(assetPath, backupPath))
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[AssetBackup] ✔ 已备份: {assetPath} → {backupPath}");
                    return backupPath;
                }
                else
                {
                    Debug.LogWarning($"[AssetBackup] ❌ CopyAsset 失败: {assetPath} → {backupPath}");
                    return null;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AssetBackup] 备份异常: {assetPath}\n{e}");
                return null;
            }
        }

        /// <summary>
        /// 备份指定目录下的所有资产（含子目录）。
        /// 适用于整个角色文件夹的批量备份。
        /// </summary>
        /// <param name="folderPath">要备份的文件夹路径（Assets/ 开头）</param>
        /// <returns>备份根目录路径</returns>
        public static string BackupDirectory(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                Debug.LogWarning($"[AssetBackup] 文件夹无效，跳过备份: {folderPath}");
                return null;
            }

            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                string backupRoot = $"{folderPath}/{BackupFolderName}/{timestamp}";
                EnsureDirectoryExists(backupRoot);

                int count = 0;
                string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
                foreach (var guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    // 跳过 _Backups 自身
                    if (assetPath.Contains($"/{BackupFolderName}/")) continue;
                    // 跳过 .meta 文件（CopyAsset 会自动处理）
                    if (assetPath.EndsWith(".meta")) continue;

                    string relativePath = assetPath.Substring(folderPath.Length + 1);
                    string destPath = $"{backupRoot}/{relativePath}";

                    // 确保目标目录存在
                    string destDir = Path.GetDirectoryName(destPath).Replace('\\', '/');
                    if (!string.IsNullOrEmpty(destDir))
                        EnsureDirectoryExists(destDir);

                    if (AssetDatabase.CopyAsset(assetPath, destPath))
                        count++;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[AssetBackup] ✔ 已备份 {count} 个资产: {folderPath} → {backupRoot}");
                return backupRoot;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AssetBackup] 目录备份异常: {folderPath}\n{e}");
                return null;
            }
        }

        /// <summary>确保 AssetDatabase 目录存在（递归创建）。</summary>
        private static void EnsureDirectoryExists(string assetDir)
        {
            // 使用 IO 直接创建物理目录，然后刷新 AssetDatabase
            string fullPath = Path.GetFullPath(assetDir);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>清理超过指定天数的旧备份。</summary>
        public static void CleanOldBackups(string rootFolder, int maxAgeDays = 30)
        {
            string backupsDir = $"{rootFolder}/{BackupFolderName}";
            string fullPath = Path.GetFullPath(backupsDir);
            if (!Directory.Exists(fullPath)) return;

            var cutoff = DateTime.Now.AddDays(-maxAgeDays);
            foreach (var dir in Directory.GetDirectories(fullPath))
            {
                var dirInfo = new DirectoryInfo(dir);
                if (dirInfo.CreationTime < cutoff)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        // 同时删除 .meta
                        string metaFile = dir + ".meta";
                        if (File.Exists(metaFile)) File.Delete(metaFile);
                        Debug.Log($"[AssetBackup] 清理过期备份: {Path.GetFileName(dir)}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[AssetBackup] 清理失败: {dir}\n{e}");
                    }
                }
            }

            AssetDatabase.Refresh();
        }
    }
}
#endif
