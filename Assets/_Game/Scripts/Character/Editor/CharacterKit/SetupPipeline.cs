#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// 装配流水线步骤定义 — 把分散的 Setup 步骤串联成一个有序、可追踪的装配流程。
    ///
    /// 解决的问题：
    ///   - 原 Setup Tab 只是"转发菜单命令"占位实现，建一个角色要开多次窗口
    ///   - 各 Setup 工具零状态共享，无法获知前置步骤是否完成
    ///   - 策划不知道"装配顺序"该是什么
    ///
    /// 流水线步骤（按依赖顺序）：
    ///   ① Character Setup / Enemy Setup — 从模型创建 Prefab + 挂组件
    ///   ② Player Config / Enemy Config — 主角手感配置 / 敌人行为配方编辑
    ///   ③ Override Setup                 — 主角动画 Override 骨架与映射校验
    ///   ④ Network Setup                  — 主角联机组件 + 玩家入口
    ///   ⑤ Ragdoll Auto Setup             — 布娃娃物理系统
    ///   ⑥ SpringBone Setup               — 动态骨骼/飘带
    ///   ⑦ Weapon Switcher Setup          — 武器挂载 + 切换系统
    ///   ⑧ Diagnostic                     — 完整性诊断 / Enemy AI 诊断
    ///
    /// 使用方式：
    ///   - Character Kit → Setup Tab 按步骤逐一执行
    ///   - Character / Enemy 两种目标类型各自有不同的步骤列表
    /// </summary>
    public class SetupPipeline
    {
        // ═══════════════════════════════════════════════════════════════
        // 步骤定义
        // ═══════════════════════════════════════════════════════════════

        public enum StepType
        {
            PlayerConfig,       // 主角手感配置（提取/应用 PlayerConfigData）
            EnemyConfig,        // 敌人配置（提取/应用 EnemyArchetype / EnemyBehaviorData）
            CharacterSetup,     // 主角 Prefab 装配
            OverrideSetup,      // 主角动画 Override 骨架与映射校验
            EnemySetup,         // 敌人 Prefab 装配
            NetworkSetup,       // 主角联机组件 + 玩家入口
            RagdollSetup,       // 布娃娃
            SpringBoneSetup,    // 动态骨骼
            WeaponSetup,        // 武器挂载
            Diagnostic,         // 诊断
        }

        public class Step
        {
            public StepType type;
            public string label;
            public System.Action callback; // 手动执行回调（用于 PlayerConfig / EnemyConfig 等非菜单步骤）
            public bool isCharacterOnly;  // true=只对主角执行
            public bool isEnemyOnly;      // true=只对敌人执行
            public bool required;         // 是否必需步骤
            public bool allowStandalone;  // true=允许独立执行（不强制前置必需步骤完成），用于可单独使用的工具型步骤
        }

        // ═══════════════════════════════════════════════════════════════
        // 状态
        // ═══════════════════════════════════════════════════════════════

        public enum StepStatus { Pending, Running, Completed, Skipped, Failed }

        public class StepState
        {
            public Step step;
            public StepStatus status = StepStatus.Pending;
            public string message = "";
            public float progress = 0f; // 0~1
        }

        public enum PipelineTarget { Character, Enemy }

        public PipelineTarget targetType { get; private set; } = PipelineTarget.Character;
        public List<StepState> steps { get; private set; } = new List<StepState>();

        public Action<StepState> onStepChanged;

        // ═══════════════════════════════════════════════════════════════
        // 构建流水线
        // ═══════════════════════════════════════════════════════════════

        static readonly Step[] CharacterSteps =
        {
            new Step { type = StepType.CharacterSetup,  label = "Character Prefab Setup",  callback = null, isCharacterOnly = true,  required = true,  allowStandalone = false },
            new Step { type = StepType.PlayerConfig,    label = "Player Config",           callback = null, isCharacterOnly = true,  required = true,  allowStandalone = false },
            new Step { type = StepType.RagdollSetup,    label = "Ragdoll Auto Setup",      callback = null, isCharacterOnly = false, required = false, allowStandalone = true  },
            new Step { type = StepType.SpringBoneSetup, label = "SpringBone Setup",        callback = null, isCharacterOnly = false, required = false, allowStandalone = true  },
            new Step { type = StepType.WeaponSetup,     label = "Weapon Switcher Setup",   callback = null, isCharacterOnly = false, required = false, allowStandalone = true  },
            new Step { type = StepType.Diagnostic,      label = "Character Diagnostics",   callback = null, isCharacterOnly = true,  required = false, allowStandalone = true  },
        };

        static readonly Step[] EnemySteps =
        {
            new Step { type = StepType.EnemyConfig,     label = "Enemy Config",            callback = null, isEnemyOnly = true, required = false, allowStandalone = false },
            new Step { type = StepType.EnemySetup,      label = "Build Enemy Prefab",      callback = null, isEnemyOnly = true, required = true,  allowStandalone = false },
            new Step { type = StepType.RagdollSetup,    label = "Ragdoll Auto Setup",      callback = null, isCharacterOnly = false, required = false, allowStandalone = true  },
            new Step { type = StepType.SpringBoneSetup, label = "SpringBone Setup",        callback = null, isCharacterOnly = false, required = false, allowStandalone = true  },
            new Step { type = StepType.WeaponSetup,     label = "Weapon Switcher Setup",   callback = null, isCharacterOnly = false, required = false, allowStandalone = true  },
            new Step { type = StepType.Diagnostic,      label = "Enemy AI Diagnostic",     callback = null, isEnemyOnly = true, required = false, allowStandalone = true  },
        };

        /// <summary>根据目标类型（Character / Enemy）构建对应的流水线步骤列表。</summary>
        public void Build(PipelineTarget target)
        {
            targetType = target;
            steps.Clear();
            var template = target == PipelineTarget.Character ? CharacterSteps : EnemySteps;
            foreach (var s in template)
            {
                steps.Add(new StepState
                {
                    step = s,
                    status = StepStatus.Pending,
                    message = "",
                    progress = 0f,
                });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 工具方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>获取流水线摘要文本。</summary>
        public string GetSummary()
        {
            int done = 0, total = 0;
            foreach (var s in steps)
            {
                if (s.status == StepStatus.Skipped) continue;
                total++;
                if (s.status == StepStatus.Completed) done++;
            }
            return $"流水线: {done}/{total} 步完成";
        }

        /// <summary>
        /// 检查目标步骤的前置必需步骤是否全部完成。
        /// 返回未完成的前置步骤列表（空列表 = 全部满足）。
        /// allowStandalone=true 的步骤无前置限制（独立可用工具）。
        /// </summary>
        public List<StepState> GetUnsatisfiedPrerequisites(StepState target)
        {
            var result = new List<StepState>();
            if (target.step.allowStandalone) return result; // 独立步骤无前置限制
            int targetIndex = steps.IndexOf(target);
            if (targetIndex < 0) return result;

            for (int i = 0; i < targetIndex; i++)
            {
                var s = steps[i];
                if (s.step.required && s.status != StepStatus.Completed && s.status != StepStatus.Skipped)
                    result.Add(s);
            }
            return result;
        }

        /// <summary>目标步骤的所有必需前置步骤是否已完成。</summary>
        public bool ArePrerequisitesSatisfied(StepState target)
            => GetUnsatisfiedPrerequisites(target).Count == 0;

        // ═══════════════════════════════════════════════════════════════
        // CK-04：系统级进度条 — EditorUtility.DisplayProgressBar
        // ═══════════════════════════════════════════════════════════════

        /// <summary>在系统级进度条中显示当前步骤的进度。</summary>
        public static void ShowStepProgress(string stepName, float progress, string detail = "")
        {
            if (progress >= 1f)
            {
                EditorUtility.ClearProgressBar();
                return;
            }
            EditorUtility.DisplayProgressBar(
                "Character Kit — 执行步骤",
                string.IsNullOrEmpty(detail) ? stepName : $"{stepName}\n{detail}",
                Mathf.Clamp01(progress));
        }

        /// <summary>清除系统级进度条。</summary>
        public static void ClearStepProgress()
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
#endif
