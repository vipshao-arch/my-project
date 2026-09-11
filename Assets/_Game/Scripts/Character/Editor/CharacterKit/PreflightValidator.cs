#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// 执行前校验器 — 纯只读，不改变任何资产。
    ///
    /// 在 Setup 流程的 DeleteAsset / CopyAsset 之前运行，
    /// 逐项检查并返回 阻断 / 警告 / 通过 三级结果。
    ///
    /// 使用方式：
    ///   var results = PreflightValidator.ValidateCharacter(context);
    ///   var blocks = results.FindAll(r => r.severity == Severity.Block);
    ///   if (blocks.Count > 0) { /* 显示阻断原因，拒绝执行 */ }
    /// </summary>
    public static class PreflightValidator
    {
        public enum Severity
        {
            Pass,    // 通过
            Warning, // 警告（不阻断，但建议修复）
            Block    // 阻断（拒绝执行）
        }

        public struct Result
        {
            public Severity severity;
            public string check;   // 检查项名称
            public string detail;  // 详细说明
        }

        // ═══════════════════════════════════════════════════════════════
        // Character Setup 校验
        // ═══════════════════════════════════════════════════════════════

        public class CharacterContext
        {
            public AnimatorController sourceController;
            public string controllerPath;
            public string folderPath;
            public string characterName;
            public string modelFBXPath;
            public CharacterAnimSetAsset animSet;
            public Object folderAsset; // 拖入的资源文件夹
        }

        /// <summary>校验 Character Setup 执行前的所有前提条件。</summary>
        public static List<Result> ValidateCharacter(CharacterContext ctx)
        {
            var results = new List<Result>();

            // 1. 文件夹有效性
            if (ctx.folderAsset == null || string.IsNullOrEmpty(ctx.folderPath))
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "资源路径",
                    detail = "未指定资源文件夹，请拖入角色的资源目录"
                });
            }
            else if (!AssetDatabase.IsValidFolder(ctx.folderPath))
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "资源路径",
                    detail = $"文件夹路径无效: {ctx.folderPath}"
                });
            }

            // 2. Controller 模板
            if (ctx.sourceController == null)
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "Controller 模板",
                    detail = "未指定 Animator Controller 模板"
                });
            }

            // 3. 角色名
            if (string.IsNullOrWhiteSpace(ctx.characterName))
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "角色名",
                    detail = "角色名为空，无法生成资产"
                });
            }

            // 4. 目标 Controller 是否已存在（警告，非阻断）
            if (!string.IsNullOrEmpty(ctx.controllerPath) &&
                File.Exists(Path.GetFullPath(ctx.controllerPath).Replace('/', '\\')))
            {
                results.Add(new Result
                {
                    severity = Severity.Warning,
                    check = "目标 Controller",
                    detail = $"目标 Controller 已存在，将被覆盖: {ctx.controllerPath}"
                });
            }

            // 5. 模型 FBX
            if (string.IsNullOrEmpty(ctx.modelFBXPath))
            {
                results.Add(new Result
                {
                    severity = Severity.Warning,
                    check = "模型 FBX",
                    detail = "未找到模型 FBX，装配时将跳过"
                });
            }
            else if (!File.Exists(Path.GetFullPath(ctx.modelFBXPath).Replace('/', '\\')))
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "模型 FBX",
                    detail = $"模型 FBX 文件不存在: {ctx.modelFBXPath}"
                });
            }
            else
            {
                // 6. Humanoid Avatar 可用性
                var importer = AssetImporter.GetAtPath(ctx.modelFBXPath) as ModelImporter;
                if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
                {
                    results.Add(new Result
                    {
                        severity = Severity.Warning,
                        check = "Avatar 类型",
                        detail = "模型 FBX 当前非 Humanoid 类型，执行时将自动转换"
                    });
                }
            }

            // 7. AnimSet 模板
            if (ctx.animSet == null)
            {
                results.Add(new Result
                {
                    severity = Severity.Warning,
                    check = "AnimSet 模板",
                    detail = "未指定 AnimSet 模板，将使用默认预设"
                });
            }

            // 8. 源 Controller 路径有效性
            if (ctx.sourceController != null)
            {
                string srcPath = AssetDatabase.GetAssetPath(ctx.sourceController);
                if (string.IsNullOrEmpty(srcPath))
                {
                    results.Add(new Result
                    {
                        severity = Severity.Block,
                        check = "Controller 源路径",
                        detail = "无法获取 Controller 模板的资源路径"
                    });
                }
            }

            return results;
        }

        // ═══════════════════════════════════════════════════════════════
        // Enemy Setup 校验
        // ═══════════════════════════════════════════════════════════════

        public class EnemyContext
        {
            public AnimatorController sourceController;
            public string controllerPath;
            public string folderPath;
            public string enemyName;
            public string modelFBXPath;
            public EnemyAnimSetAsset animSet;
            public Object folderAsset;
        }

        /// <summary>校验 Enemy Setup 执行前的所有前提条件。</summary>
        public static List<Result> ValidateEnemy(EnemyContext ctx)
        {
            var results = new List<Result>();

            // 1. 文件夹有效性
            if (ctx.folderAsset == null || string.IsNullOrEmpty(ctx.folderPath))
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "资源路径",
                    detail = "未指定资源文件夹，请拖入敌人的资源目录"
                });
            }
            else if (!AssetDatabase.IsValidFolder(ctx.folderPath))
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "资源路径",
                    detail = $"文件夹路径无效: {ctx.folderPath}"
                });
            }

            // 2. Controller 模板
            if (ctx.sourceController == null)
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "Controller 模板",
                    detail = "未指定 Animator Controller 模板"
                });
            }
            else
            {
                // 校验模板 Controller 结构完整性
                if (!DefaultConfigCreator.ValidateEnemyController(ctx.sourceController, out string reason))
                {
                    results.Add(new Result
                    {
                        severity = Severity.Block,
                        check = "Controller 模板校验",
                        detail = $"模板 Controller 结构损坏: {reason}\n删除损坏的模板资产后，系统会在下次装配时自动重建"
                    });
                }
            }

            // 3. 敌人名
            if (string.IsNullOrWhiteSpace(ctx.enemyName))
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "敌人名",
                    detail = "敌人名为空，无法生成资产"
                });
            }

            // 4. 目标 Controller 是否已存在
            if (!string.IsNullOrEmpty(ctx.controllerPath) &&
                File.Exists(Path.GetFullPath(ctx.controllerPath).Replace('/', '\\')))
            {
                results.Add(new Result
                {
                    severity = Severity.Warning,
                    check = "目标 Controller",
                    detail = $"目标 Controller 已存在，将被覆盖: {ctx.controllerPath}"
                });
            }

            // 5. 模型 FBX
            if (string.IsNullOrEmpty(ctx.modelFBXPath))
            {
                results.Add(new Result
                {
                    severity = Severity.Warning,
                    check = "模型 FBX",
                    detail = "未找到模型 FBX，装配时将跳过"
                });
            }
            else if (!File.Exists(Path.GetFullPath(ctx.modelFBXPath).Replace('/', '\\')))
            {
                results.Add(new Result
                {
                    severity = Severity.Block,
                    check = "模型 FBX",
                    detail = $"模型 FBX 文件不存在: {ctx.modelFBXPath}"
                });
            }
            else
            {
                var importer = AssetImporter.GetAtPath(ctx.modelFBXPath) as ModelImporter;
                if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
                {
                    results.Add(new Result
                    {
                        severity = Severity.Warning,
                        check = "Avatar 类型",
                        detail = "模型 FBX 当前非 Humanoid 类型，执行时将自动转换"
                    });
                }
            }

            // 6. AnimSet
            if (ctx.animSet == null)
            {
                results.Add(new Result
                {
                    severity = Severity.Warning,
                    check = "AnimSet",
                    detail = "未指定 AnimSet，攻击/技能动画将跳过"
                });
            }

            return results;
        }

        // ═══════════════════════════════════════════════════════════════
        // 辅助
        // ═══════════════════════════════════════════════════════════════

        /// <summary>检查结果列表中是否有阻断项。</summary>
        public static bool HasBlocks(List<Result> results)
        {
            return results.Exists(r => r.severity == Severity.Block);
        }

        /// <summary>格式化校验结果为 UI 友好的字符串。</summary>
        public static string FormatResults(List<Result> results)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in results)
            {
                string icon = r.severity == Severity.Block ? "⊘" :
                              r.severity == Severity.Warning ? "⚠" : "✔";
                sb.AppendLine($"{icon} [{r.check}] {r.detail}");
            }
            return sb.ToString();
        }
    }
}
#endif
