#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace Game.EditorTools.CombatSandbox
{
    /// <summary>
    /// 批量模拟配置 — 定义对阵双方与运行参数。
    /// CreateAssetMenu: Game/Combat System/Batch Sim Config
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Combat System/Batch Sim Config", fileName = "BatchSimConfig")]
    public class BatchSimConfig : ScriptableObject
    {
        [Header("对阵双方")]
        [Tooltip("玩家 Prefab（必须挂载 CharacterMotor）。")]
        public GameObject playerPrefab;

        [Tooltip("敌人条目列表 — 每个条目定义一个敌人类型及其出场参数。")]
        public List<EnemyBatchEntry> enemyEntries = new List<EnemyBatchEntry>();

        [Header("模拟参数")]
        [Tooltip("每种组合重复模拟次数。")]
        [Min(1)] public int simulationCount = 100;

        [Tooltip("单局最长运行时间（秒），超时自动终止。")]
        [Min(5f)] public float maxDurationPerRun = 60f;

        [Tooltip("使用固定随机种子以保证可复现性。0 = 随机种子。")]
        public int fixedRandomSeed = 0;

        [Header("统计阈值")]
        [Tooltip("胜率高于此值视为「容易」。")]
        [Range(0, 1)] public float easyThreshold = 0.8f;

        [Tooltip("胜率低于此值视为「困难」。")]
        [Range(0, 1)] public float hardThreshold = 0.3f;

        /// <summary>验证配置是否有效。</summary>
        public bool IsValid => playerPrefab != null && enemyEntries.Count > 0 && simulationCount > 0;
    }

    /// <summary>
    /// 批量模拟中的单个敌人条目 — 描述一个敌人类型及其多样性参数。
    /// </summary>
    [System.Serializable]
    public class EnemyBatchEntry
    {
        [Tooltip("敌人 Prefab（必须挂载 EnemyMotor）。")]
        public GameObject enemyPrefab;

        [Tooltip("该类型敌人在单局中出场的数量。")]
        [Min(1)] public int count = 1;

        [Tooltip("该敌人类型的外观名称（用于统计报告）。")]
        public string displayName;

        /// <summary>自动推断显示名称。</summary>
        public string DisplayName =>
            !string.IsNullOrEmpty(displayName) ? displayName
            : enemyPrefab != null ? enemyPrefab.name : "???";
    }
}
#endif
