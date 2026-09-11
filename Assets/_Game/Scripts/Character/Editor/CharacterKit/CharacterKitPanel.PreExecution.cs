#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// 执行前校验 + 变更计划 + 执行后状态标记 — 集成层。
    ///
    /// 在 ExecuteCharacterSetup / ExecuteEnemySetup 的现有 Preflight 块中调用，
    /// 提供统一的 ChangePlan 展示和 CharacterKitTargetContext 步骤完成标记。
    /// </summary>
    public partial class CharacterKitPanel
    {
        // ═══════════════════════════════════════════════════════════════
        // ChangePlan 展示（在执行前 Preflight 块内调用）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 计算并记录变更计划到日志。如果包含覆盖/删除项，生成警告日志。
        /// 返回 false 表示用户需要关注高风险变更（不阻断，仅提示）。
        /// </summary>
        bool ShowChangePlanForStep(
            CharacterKitTargetContext ctx,
            SetupPipeline.StepType step,
            HashSet<string> expectedSkillDataPaths = null)
        {
            var plan = ChangePlan.Compute(ctx, step, expectedSkillDataPaths);

            if (plan.overwriteCount > 0 || plan.deleteCount > 0)
            {
                // 高风险变更：输出到日志
                LogToCurrentSetup($"  ── 变更计划 ──");
                foreach (var entry in plan.entries)
                {
                    string icon = entry.risk switch
                    {
                        ChangePlan.RiskLevel.Overwrite => "↻",
                        ChangePlan.RiskLevel.Delete => "✕",
                        ChangePlan.RiskLevel.Safe => "＋",
                        _ => "?",
                    };
                    LogToCurrentSetup($"  {icon} {entry.description}");
                }
                LogToCurrentSetup($"  ── 合计: {plan.overwriteCount} 覆盖, {plan.deleteCount} 删除 ──");
            }

            return true; // 当前不阻断，仅记录
        }

        // ═══════════════════════════════════════════════════════════════
        // CharacterKitTargetContext 构建
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 从当前 Character Setup 状态构建目标上下文。
        /// 应在 _charSetupFolderPath 和 _charSetupName 已设置后调用。
        /// </summary>
        CharacterKitTargetContext BuildCharacterTargetContext()
        {
            string expectedPrefabPath = $"{_charSetupFolderPath}/{_charSetupName}_Character.prefab";

            return CharacterKitTargetContext.FromFolder(
                _charSetupFolderPath,
                _charSetupName,
                SetupPipeline.PipelineTarget.Character,
                expectedPrefabPath);
        }

        /// <summary>
        /// 从当前 Enemy Setup 状态构建目标上下文。
        /// 应在 _enemySetupFolderPath 和 _enemySetupName 已设置后调用。
        /// </summary>
        CharacterKitTargetContext BuildEnemyTargetContext()
        {
            string expectedPrefabPath = $"{_enemySetupFolderPath}/{_enemySetupName}_Enemy.prefab";

            return CharacterKitTargetContext.FromFolder(
                _enemySetupFolderPath,
                _enemySetupName,
                SetupPipeline.PipelineTarget.Enemy,
                expectedPrefabPath);
        }

        // ═══════════════════════════════════════════════════════════════
        // 步骤完成标记（执行成功后调用）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 标记当前步骤完成，保存指纹。完成后自动更新流水线状态。
        /// </summary>
        void MarkCurrentStepComplete(
            CharacterKitTargetContext ctx, SetupPipeline.StepType step)
        {
            if (ctx == null) return;

            ctx.MarkStepComplete(step);
            ctx.ComputeAndSaveFingerprint();

            // 同步到现有流水线状态
            if (_pipeline != null)
            {
                foreach (var s in _pipeline.steps)
                {
                    if (s.step.type == step)
                    {
                        s.status = SetupPipeline.StepStatus.Completed;
                        _needsPipelineStateRefresh = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 检查 CharacterKitTargetContext 中步骤是否已完成（新体系）。
        /// 用于在加载流水线状态时与新体系双向同步。
        /// </summary>
        bool IsStepCompleteInContext(
            CharacterKitTargetContext ctx, SetupPipeline.StepType step)
        {
            if (ctx == null) return false;
            return ctx.IsStepComplete(step);
        }

        // ═══════════════════════════════════════════════════════════════
        // 日志兼容（写入当前装配的日志列表）
        // ═══════════════════════════════════════════════════════════════

        private void LogToCurrentSetup(string message)
        {
            // 判断当前上下文：Character 还是 Enemy
            if (_charSetupLogMessages != null)
                _charSetupLogMessages.Add(message);
            else if (_enemySetupLogMessages != null)
                _enemySetupLogMessages.Add(message);
        }
    }
}
#endif
