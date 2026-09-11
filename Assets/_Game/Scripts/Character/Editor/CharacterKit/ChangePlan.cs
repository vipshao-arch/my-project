#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// 变更计划 — 在装配执行前扫描目标目录，列出所有将受影响（创建/覆盖/删除）的资产。
    ///
    /// 解决问题：误操作或半途失败可能污染已有资产，且难以审计和恢复。
    ///
    /// 使用方式：
    ///   var plan = ChangePlan.Compute(targetContext, stepType, extraExpectedPaths);
    ///   // 展示 plan 的变更列表
    ///   if (plan.hasRiskyChanges) { /* 需要用户确认 */ }
    /// </summary>
    public class ChangePlan
    {
        /// <summary>风险等级。</summary>
        public enum RiskLevel
        {
            Safe,      // 创建新文件，不影响已有资产
            Overwrite, // 覆盖已有资产（备份后会覆盖）
            Delete,    // 删除过期资产
            Error,     // 无法判断（路径无效等）
        }

        /// <summary>单个变更条目。</summary>
        public class Entry
        {
            public RiskLevel risk;
            public string path;         // 资产路径（Assets/ 开头）
            public string description;  // 人类可读描述
            public bool exists;         // 当前是否已存在
        }

        /// <summary>所有变更条目。</summary>
        public List<Entry> entries { get; private set; } = new List<Entry>();

        /// <summary>目标上下文。</summary>
        public CharacterKitTargetContext targetContext { get; private set; }

        /// <summary>被扫描的步骤类型。</summary>
        public SetupPipeline.StepType stepType { get; private set; }

        /// <summary>是否有高风险变更（覆盖或删除）。</summary>
        public bool hasRiskyChanges
        {
            get
            {
                foreach (var e in entries)
                    if (e.risk == RiskLevel.Overwrite || e.risk == RiskLevel.Delete)
                        return true;
                return false;
            }
        }

        /// <summary>阻塞/警告计数。</summary>
        public int overwriteCount => entries.FindAll(e => e.risk == RiskLevel.Overwrite).Count;
        public int deleteCount => entries.FindAll(e => e.risk == RiskLevel.Delete).Count;
        public int safeCount => entries.FindAll(e => e.risk == RiskLevel.Safe).Count;

        // ═══════════════════════════════════════════════════════════════
        // 工厂方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 计算指定步骤的变更计划。
        /// </summary>
        /// <param name="ctx">目标上下文</param>
        /// <param name="step">步骤类型</param>
        /// <param name="expectedSkillDataPaths">
        ///   仅 EnemySetup 需要：本轮期望生成的 SkillData 路径集合。
        ///   用于检测将被清理的过期 SkillData。
        /// </param>
        public static ChangePlan Compute(
            CharacterKitTargetContext ctx,
            SetupPipeline.StepType step,
            HashSet<string> expectedSkillDataPaths = null)
        {
            var plan = new ChangePlan
            {
                targetContext = ctx,
                stepType = step,
            };

            string folder = ctx.resourceFolder;
            string name = ctx.displayName;

            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
            {
                plan.entries.Add(new Entry
                {
                    risk = RiskLevel.Error,
                    path = folder ?? "(空)",
                    description = "目标上下文不完整，无法计算变更计划"
                });
                return plan;
            }

            switch (step)
            {
                case SetupPipeline.StepType.CharacterSetup:
                    plan.AddCharacterSetupEntries(folder, name);
                    break;
                case SetupPipeline.StepType.EnemySetup:
                    plan.AddEnemySetupEntries(folder, name, expectedSkillDataPaths);
                    break;
                case SetupPipeline.StepType.OverrideSetup:
                    plan.entries.Add(new Entry
                    {
                        risk = RiskLevel.Overwrite,
                        path = $"{folder}/{name}_Override.overrideController",
                        description = "以 DefaultCharacterController 为逻辑骨架，建立角色动画映射",
                        exists = File.Exists($"{folder}/{name}_Override.overrideController")
                    });
                    break;
                case SetupPipeline.StepType.NetworkSetup:
                    plan.entries.Add(new Entry
                    {
                        risk = RiskLevel.Overwrite,
                        path = "Assets/_Game/Resources/NetworkPlayer.prefab",
                        description = "登记当前角色并生成 Resources 联机 Prefab",
                        exists = File.Exists("Assets/_Game/Resources/NetworkPlayer.prefab")
                    });
                    break;
                case SetupPipeline.StepType.PlayerConfig:
                    plan.AddConfigEntries(folder, name, "PlayerConfig");
                    break;
                case SetupPipeline.StepType.EnemyConfig:
                    plan.AddConfigEntries(folder, name, "EnemyConfig");
                    break;
                case SetupPipeline.StepType.RagdollSetup:
                case SetupPipeline.StepType.SpringBoneSetup:
                case SetupPipeline.StepType.WeaponSetup:
                case SetupPipeline.StepType.Diagnostic:
                    plan.entries.Add(new Entry
                    {
                        risk = RiskLevel.Safe,
                        path = ctx.prefabPath,
                        description = $"[{step}] 修改已有 Prefab，将自动备份后保存"
                    });
                    break;
            }

            return plan;
        }

        // ═══════════════════════════════════════════════════════════════
        // Character Setup 变更计算
        // ═══════════════════════════════════════════════════════════════

        private void AddCharacterSetupEntries(string folder, string name)
        {
            string controllerPath = $"{folder}/{name}_Locomotion.controller";
            string prefabPath = $"{folder}/{name}_Character.prefab";

            // Controller
            bool ctrlExists = AssetExists(controllerPath);
            entries.Add(new Entry
            {
                risk = ctrlExists ? RiskLevel.Overwrite : RiskLevel.Safe,
                path = controllerPath,
                exists = ctrlExists,
                description = ctrlExists
                    ? $"Animator Controller 将被覆盖（原文件已自动备份到 _Backups/）"
                    : "Animator Controller 将被新建"
            });

            // Prefab
            bool prefabExists = AssetExists(prefabPath);
            entries.Add(new Entry
            {
                risk = prefabExists ? RiskLevel.Overwrite : RiskLevel.Safe,
                path = prefabPath,
                exists = prefabExists,
                description = prefabExists
                    ? $"角色 Prefab 将被覆盖（原文件已自动备份到 _Backups/）"
                    : "角色 Prefab 将被新建"
            });

            // 列出 _Backups 目录（提示备份位置）
            string backupDir = $"{folder}/_Backups/";
            if (AssetDatabase.IsValidFolder(backupDir))
            {
                entries.Add(new Entry
                {
                    risk = RiskLevel.Safe,
                    path = backupDir,
                    exists = true,
                    description = "备份目录已存在，本次执行将创建新的时间戳备份"
                });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Enemy Setup 变更计算
        // ═══════════════════════════════════════════════════════════════

        private void AddEnemySetupEntries(
            string folder, string name, HashSet<string> expectedSkillDataPaths)
        {
            string controllerPath = $"{folder}/{name}_Enemy.controller";
            string prefabPath = $"{folder}/{name}_Enemy.prefab";
            string skillDataDir = $"{folder}/SkillData";

            // Controller
            bool ctrlExists = AssetExists(controllerPath);
            entries.Add(new Entry
            {
                risk = ctrlExists ? RiskLevel.Overwrite : RiskLevel.Safe,
                path = controllerPath,
                exists = ctrlExists,
                description = ctrlExists
                    ? $"Enemy Animator Controller 将被覆盖（原文件已自动备份到 _Backups/）"
                    : "Enemy Animator Controller 将被新建"
            });

            // Prefab
            bool prefabExists = AssetExists(prefabPath);
            entries.Add(new Entry
            {
                risk = prefabExists ? RiskLevel.Overwrite : RiskLevel.Safe,
                path = prefabPath,
                exists = prefabExists,
                description = prefabExists
                    ? $"敌人 Prefab 将被覆盖（原文件已自动备份到 _Backups/）"
                    : "敌人 Prefab 将被新建"
            });

            // 新生成的 SkillData
            if (expectedSkillDataPaths != null)
            {
                foreach (var skillPath in expectedSkillDataPaths)
                {
                    bool exists = AssetExists(skillPath);
                    entries.Add(new Entry
                    {
                        risk = exists ? RiskLevel.Overwrite : RiskLevel.Safe,
                        path = skillPath,
                        exists = exists,
                        description = exists
                            ? $"技能数据将被覆盖: {Path.GetFileName(skillPath)}"
                            : $"技能数据将被新建: {Path.GetFileName(skillPath)}"
                    });
                }
            }

            // 过期 SkillData 清理
            if (AssetDatabase.IsValidFolder(skillDataDir) && expectedSkillDataPaths != null)
            {
                string[] guids = AssetDatabase.FindAssets("t:SkillData", new[] { skillDataDir });
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    string fileName = Path.GetFileNameWithoutExtension(path);

                    // 判断是否由本 Enemy 生成
                    if (!fileName.StartsWith($"{name}_Enemy_",
                        System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!expectedSkillDataPaths.Contains(path))
                    {
                        entries.Add(new Entry
                        {
                            risk = RiskLevel.Delete,
                            path = path,
                            exists = true,
                            description = $"过期 SkillData 将被删除: {fileName}"
                        });
                    }
                }
            }

            // 备份目录
            string backupDir = $"{folder}/_Backups/";
            if (AssetDatabase.IsValidFolder(backupDir))
            {
                entries.Add(new Entry
                {
                    risk = RiskLevel.Safe,
                    path = backupDir,
                    exists = true,
                    description = "备份目录已存在，本次执行将创建新的时间戳备份"
                });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Config 步骤变更计算
        // ═══════════════════════════════════════════════════════════════

        private void AddConfigEntries(string folder, string name, string configType)
        {
            string configPath = $"{folder}/{name}_{configType}.asset";
            bool exists = AssetExists(configPath);

            entries.Add(new Entry
            {
                risk = exists ? RiskLevel.Overwrite : RiskLevel.Safe,
                path = configPath,
                exists = exists,
                description = exists
                    ? $"配置文件将被覆盖: {configPath}"
                    : $"配置文件将被新建: {configPath}"
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // 格式化为 UI 文本
        // ═══════════════════════════════════════════════════════════════

        /// <summary>生成变更计划的人类可读摘要。</summary>
        public string ToSummaryString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"变更计划 — {targetContext?.summary ?? "未知目标"}");
            sb.AppendLine($"步骤: {stepType}");
            sb.AppendLine(new string('─', 50));

            string currentRisk = "Safe";
            foreach (var e in entries)
            {
                string icon = e.risk switch
                {
                    RiskLevel.Safe => "  ＋",
                    RiskLevel.Overwrite => "  ↻",
                    RiskLevel.Delete => "  ✕",
                    RiskLevel.Error => "  ⊘",
                    _ => "  ?",
                };

                if (e.risk.ToString() != currentRisk)
                {
                    currentRisk = e.risk.ToString();
                    string header = e.risk switch
                    {
                        RiskLevel.Overwrite => "\n── 将被覆盖 ──",
                        RiskLevel.Delete => "\n── 将被删除 ──",
                        RiskLevel.Safe => "\n── 新建 ──",
                        _ => "",
                    };
                    if (!string.IsNullOrEmpty(header))
                        sb.AppendLine(header);
                }

                sb.AppendLine($"{icon} {e.description}");
            }

            sb.AppendLine(new string('─', 50));
            sb.AppendLine($"合计: {safeCount} 新建, {overwriteCount} 覆盖, {deleteCount} 删除");
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════
        // 预执行校验 — 整合 PreflightValidator
        // ═══════════════════════════════════════════════════════════════

        /// <summary>执行前综合检查结果。</summary>
        public class PreExecutionResult
        {
            public bool canProceed;                          // 是否可继续执行
            public List<PreflightValidator.Result> preflightResults; // 预校验结果
            public ChangePlan changePlan;                    // 变更计划
            public string summary;                           // 摘要文本

            /// <summary>阻断数量。</summary>
            public int blockCount => preflightResults?.FindAll(
                r => r.severity == PreflightValidator.Severity.Block).Count ?? 0;

            /// <summary>警告数量。</summary>
            public int warningCount => preflightResults?.FindAll(
                r => r.severity == PreflightValidator.Severity.Warning).Count ?? 0;
        }

        /// <summary>
        /// 执行前综合检查：运行 PreflightValidator + 计算 ChangePlan。
        /// 返回是否可继续执行。
        /// </summary>
        public static PreExecutionResult RunPreExecutionChecks(
            CharacterKitTargetContext ctx,
            SetupPipeline.StepType step,
            PreflightValidator.CharacterContext charCtx = null,
            PreflightValidator.EnemyContext enemyCtx = null,
            HashSet<string> expectedSkillDataPaths = null)
        {
            var result = new PreExecutionResult();

            // 1. 运行 PreflightValidator
            if (ctx.targetType == SetupPipeline.PipelineTarget.Character && charCtx != null)
                result.preflightResults = PreflightValidator.ValidateCharacter(charCtx);
            else if (ctx.targetType == SetupPipeline.PipelineTarget.Enemy && enemyCtx != null)
                result.preflightResults = PreflightValidator.ValidateEnemy(enemyCtx);
            else
                result.preflightResults = new List<PreflightValidator.Result>();

            // 2. 计算变更计划
            result.changePlan = Compute(ctx, step, expectedSkillDataPaths);

            // 3. 判断是否可继续
            bool preflightPass = !PreflightValidator.HasBlocks(result.preflightResults);
            result.canProceed = preflightPass;

            // 4. 生成摘要
            var sb = new StringBuilder();
            sb.AppendLine($"═══ 执行前检查 — {ctx.summary} ═══");
            sb.AppendLine();

            if (result.preflightResults.Count > 0)
            {
                sb.AppendLine("【预校验结果】");
                sb.Append(PreflightValidator.FormatResults(result.preflightResults));
                sb.AppendLine();
            }

            sb.AppendLine("【变更计划】");
            sb.Append(result.changePlan.ToSummaryString());

            if (!result.canProceed)
            {
                sb.AppendLine();
                sb.AppendLine("⊘ 存在阻断项，拒绝执行。请先修复以上问题。");
            }

            result.summary = sb.ToString();
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        // 内部工具
        // ═══════════════════════════════════════════════════════════════

        private static bool AssetExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            return File.Exists(Path.GetFullPath(assetPath).Replace('/', '\\'));
        }
    }
}
#endif
