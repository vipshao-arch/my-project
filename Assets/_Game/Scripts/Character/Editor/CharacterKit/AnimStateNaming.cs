#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// Animator State 命名协定 — 集中管理装配工具和技能编辑之间共享的状态名、Trigger 名。
    ///
    /// 解决问题：
    ///   - 消除隐式字符串共识（Attack_1 / Skill_Q / Attack01 等散落在多处）
    ///   - 改名时编译期报错（而非静默失效）
    ///   - SkillBuilderWizard / CharacterKitPanel / 旧 Setup 工具之间共享同一套命名规则
    ///
    /// 使用方式：
    ///   - 生成状态名：AnimStateNaming.AttackState(1) → "Attack_1"
    ///   - 解析下标：   AnimStateNaming.TryParseAttackIndex("Attack_3", out int idx) → 3
    ///   - 常量引用：   AnimStateNaming.StateIdle → "Idle"
    /// </summary>
    public static class AnimStateNaming
    {
        // ═══════════════════════════════════════════════════════════════
        // 基础状态名（Locomotion / 通用层）
        // ═══════════════════════════════════════════════════════════════

        public const string StateIdle    = "Idle";
        public const string StateWalk    = "Walk";
        public const string StateRun     = "Run";
        public const string StateSprint  = "Sprint";
        public const string StateStand   = "Stand";
        public const string StateHit     = "Hit";
        public const string StateDeath   = "Death";
        public const string StateDead    = "Dead";

        // ═══════════════════════════════════════════════════════════════
        // 攻击状态名（Attack_1, Attack_2, ... Attack_8）
        // ═══════════════════════════════════════════════════════════════

        public const string AttackPrefix = "Attack_";
        public const int    AttackIndexMin = 1;
        public const int    AttackIndexMax = 8;

        /// <summary>生成攻击状态名：Attack_{index}（1-based）。</summary>
        public static string AttackState(int index)
            => $"{AttackPrefix}{index}";

        /// <summary>尝试从状态名解析攻击下标（1-based）。</summary>
        public static bool TryParseAttackIndex(string stateName, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(stateName)) return false;
            if (!stateName.StartsWith(AttackPrefix, StringComparison.Ordinal)) return false;
            if (!int.TryParse(stateName.Substring(AttackPrefix.Length), out index))
            {
                index = -1;
                return false;
            }

            if (index < AttackIndexMin || index > AttackIndexMax)
            {
                index = -1;
                return false;
            }

            return true;
        }

        /// <summary>生成旧格式攻击状态名：Attack01（两位数补零，用于旧敌人 Controller 兼容清除）。</summary>
        public static string AttackStateLegacy(int index)
            => $"Attack{index:00}";

        // ═══════════════════════════════════════════════════════════════
        // 技能状态名（Skill_Q, Skill_W, ...）
        // ═══════════════════════════════════════════════════════════════

        public const string SkillPrefix   = "Skill_";
        public static readonly string[] DefaultSkillLabels = { "Q", "W", "E", "R" };

        /// <summary>生成技能状态名：Skill_{label}（如 Skill_Q）。</summary>
        public static string SkillState(string label)
            => $"{SkillPrefix}{label}";

        /// <summary>生成技能 UI 标签：Skill Q, Skill W...</summary>
        public static string SkillLabel(int index)
        {
            if (index >= 0 && index < DefaultSkillLabels.Length)
                return $"Skill {DefaultSkillLabels[index]}";
            return $"Skill {index + 1}";
        }

        /// <summary>获取技能后缀（Q, W, E, R, 5, ... 或数字回退）。</summary>
        public static string SkillSuffix(int index)
        {
            string[] labels = { "Q", "W", "E", "R", "5", "6", "7", "8" };
            return index < labels.Length ? labels[index] : $"{index + 1}";
        }

        // ═══════════════════════════════════════════════════════════════
        // FBX 动画 Clip 名匹配模式（大小写不敏感）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>判断 clip 名是否为 idle/stand 动画。</summary>
        public static bool IsIdleClip(string clipName)
        {
            var lower = clipName.ToLowerInvariant();
            return lower.Contains("idle") || lower.Contains("stand");
        }

        /// <summary>判断 clip 名是否为 walk/run 动画。</summary>
        public static bool IsMoveClip(string clipName)
        {
            var lower = clipName.ToLowerInvariant();
            return lower.Contains("walk") || lower.Contains("run");
        }

        /// <summary>判断 clip 名是否为 hit 动画。</summary>
        public static bool IsHitClip(string clipName)
        {
            var lower = clipName.ToLowerInvariant();
            return lower.Contains("hit");
        }

        /// <summary>判断 clip 名是否为 death/dead 动画。</summary>
        public static bool IsDeathClip(string clipName)
        {
            var lower = clipName.ToLowerInvariant();
            return lower.Contains("dead") || lower.Contains("death");
        }

        /// <summary>判断 clip 名是否匹配某个攻击段（attack01/attack_1/attack1）。</summary>
        public static bool IsAttackClip(string clipName, int segmentIndex)
        {
            var lower = clipName.ToLowerInvariant();
            return lower.Contains($"attack0{segmentIndex + 1}")
                || lower.Contains($"attack_{segmentIndex + 1}")
                || lower.Contains($"attack{segmentIndex + 1}");
        }

        /// <summary>判断 clip 名是否包含 attack 字样。</summary>
        public static bool IsAttackClip(string clipName)
        {
            return clipName.ToLowerInvariant().Contains("attack");
        }

        /// <summary>判断 state 名是否为 idle/stand 状态。</summary>
        public static bool IsIdleState(string stateName)
            => stateName == StateIdle || stateName.Contains(StateStand);

        /// <summary>判断 state 名是否为 walk/run 状态。</summary>
        public static bool IsMoveState(string stateName)
            => stateName == StateWalk || stateName == StateRun;

        /// <summary>判断 state 名是否为受击状态。</summary>
        public static bool IsHitState(string stateName)
            => stateName == StateHit;

        /// <summary>判断 state 名是否为死亡状态。</summary>
        public static bool IsDeathState(string stateName)
            => stateName == StateDeath || stateName == StateDead;
    }
}
#endif
