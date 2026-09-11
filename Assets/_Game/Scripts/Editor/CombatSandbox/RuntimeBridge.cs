#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Game.Character;
using Game.SkillSystem;

namespace Game.EditorTools.CombatSandbox
{
    /// <summary>
    /// 运行时数据桥接层 — 安全地从 Editor 代码读取 Play Mode 下的战斗组件状态。
    ///
    /// 所有 GetXxx 方法在非 Play Mode 下返回 null / default，
    /// 不会抛出 NullReferenceException。
    /// </summary>
    public static class RuntimeBridge
    {
        /// <summary>当前是否在 Play Mode 下。</summary>
        public static bool IsPlaying => EditorApplication.isPlaying;

        // ═══════════════════════════════════════════════════════════
        // 查询
        // ═══════════════════════════════════════════════════════════

        /// <summary>获取场景中所有玩家 CharacterMotor。</summary>
        public static CharacterMotor[] GetPlayerMotors()
        {
            if (!IsPlaying) return new CharacterMotor[0];
            return Object.FindObjectsOfType<CharacterMotor>();
        }

        /// <summary>获取场景中所有 EnemyMotor。</summary>
        public static EnemyMotor[] GetEnemyMotors()
        {
            if (!IsPlaying) return new Object[0] as EnemyMotor[];
            return Object.FindObjectsOfType<EnemyMotor>();
        }

        /// <summary>获取 EnemyMotor 关联的 EnemyAI。</summary>
        public static EnemyAI GetEnemyAI(EnemyMotor motor)
        {
            if (motor == null) return null;
            return motor.GetComponent<EnemyAI>();
        }

        /// <summary>获取 CharacterMotor 关联的 SkillController。</summary>
        public static SkillController GetSkillController(CharacterMotor motor)
        {
            if (motor == null) return null;
            return motor.GetComponent<SkillController>();
        }

        /// <summary>获取 CharacterMotor 关联的 WeaponSwitcher。</summary>
        public static WeaponSwitcher GetWeaponSwitcher(CharacterMotor motor)
        {
            if (motor == null) return null;
            return motor.GetComponent<WeaponSwitcher>();
        }

        // ═══════════════════════════════════════════════════════════
        // 格式化
        // ═══════════════════════════════════════════════════════════

        /// <summary>HP 条文本： "80 / 100 (80%)"</summary>
        public static string FormatHP(float current, float max)
        {
            return $"{current:F0} / {max:F0} ({current / Mathf.Max(max, 1f) * 100f:F0}%)";
        }

        /// <summary>HP 百分比 0-1。</summary>
        public static float HPPercent(float current, float max)
        {
            return Mathf.Clamp01(current / Mathf.Max(max, 1f));
        }

        /// <summary>AI 状态的可读中文名。</summary>
        public static string GetAIStateLabel(EnemyAI.State state)
        {
            return state switch
            {
                EnemyAI.State.Idle   => "空闲",
                EnemyAI.State.Patrol => "巡逻",
                EnemyAI.State.Chase  => "追踪",
                EnemyAI.State.Attack => "攻击",
                EnemyAI.State.Return => "返回",
                EnemyAI.State.Dead   => "死亡",
                _ => state.ToString(),
            };
        }

        /// <summary>AI 状态对应的颜色。</summary>
        public static Color GetAIStateColor(EnemyAI.State state)
        {
            return state switch
            {
                EnemyAI.State.Idle   => new Color(0.5f, 0.5f, 0.5f),
                EnemyAI.State.Patrol => new Color(0.3f, 0.6f, 0.3f),
                EnemyAI.State.Chase  => new Color(0.9f, 0.7f, 0.2f),
                EnemyAI.State.Attack => new Color(0.9f, 0.2f, 0.2f),
                EnemyAI.State.Return => new Color(0.3f, 0.5f, 0.8f),
                EnemyAI.State.Dead   => new Color(0.6f, 0.1f, 0.1f),
                _ => Color.gray,
            };
        }
    }

    /// <summary>
    /// 伤害日志条目 — 由 Live Test Tab 收集并展示。
    /// </summary>
    public class DamageLogEntry
    {
        public float timestamp;
        public string sourceName;
        public string targetName;
        public float damage;
        public string skillName;
        public bool isCrit;
        public bool isHeal;
    }
}
#endif
