#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace Game.EditorTools.CombatSandbox
{
    /// <summary>
    /// 战斗回放数据 — 录制 AI 决策帧与关键事件。
    /// CreateAssetMenu: Game/Combat System/Combat Replay Data
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Combat System/Combat Replay Data", fileName = "CombatReplay")]
    public class CombatReplayData : ScriptableObject
    {
        [Header("回放元信息")]
        [Tooltip("录制时间（编辑器时间戳）。")]
        public string recordedAt;

        [Tooltip("这局战斗的总时长（秒）。")]
        public float totalDuration;

        [Tooltip("参与录制的角色名列表。")]
        public List<string> participants = new List<string>();

        [Header("帧数据")]
        [Tooltip("按时间排序的帧快照列表。")]
        public List<ReplayFrame> frames = new List<ReplayFrame>();

        [Header("事件")]
        [Tooltip("按时间排序的战斗事件列表。")]
        public List<ReplayEvent> events = new List<ReplayEvent>();
    }

    /// <summary>
    /// 单帧快照 — 记录某一时刻所有角色的状态。
    /// </summary>
    [System.Serializable]
    public class ReplayFrame
    {
        [Tooltip("距战斗开始的时间（秒）。")]
        public float timestamp;

        [Tooltip("玩家 HP。")]
        public float playerHP;

        [Tooltip("玩家位置。")]
        public Vector3 playerPosition;

        [Tooltip("所有敌人的 HP。")]
        public List<float> enemyHPs = new List<float>();

        [Tooltip("所有敌人的位置。")]
        public List<Vector3> enemyPositions = new List<Vector3>();

        [Tooltip("所有敌人的 AI 状态索引。")]
        public List<int> enemyAIStates = new List<int>();
    }

    /// <summary>
    /// 战斗事件 — 关键节点（状态变更 / 伤害 / 技能 / 死亡）。
    /// </summary>
    [System.Serializable]
    public class ReplayEvent
    {
        public float timestamp;
        public ReplayEventType eventType;
        public string description;
        public string sourceName;
        public string targetName;
        public float value; // 伤害量 / 治疗量 等
    }

    public enum ReplayEventType
    {
        AIStateChange,
        DamageDealt,
        Healed,
        SkillUsed,
        Death,
        Spawn,
        CombatStart,
        CombatEnd,
    }
}
#endif
