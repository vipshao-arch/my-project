using UnityEngine;
using System.Collections.Generic;

namespace Game.Character
{
    /// <summary>敌人出生位置模式。</summary>
    public enum SpawnPositionMode
    {
        /// <summary>场景中预设的出生点</summary>
        SpawnPoint,
        /// <summary>相对于遭遇战区域中心的随机环形分布</summary>
        AroundCenter,
        /// <summary>在玩家周围随机生成</summary>
        AroundPlayer,
    }

    /// <summary>遭遇战触发条件类型。</summary>
    public enum TriggerConditionType
    {
        /// <summary>进入触发器区域</summary>
        OnTriggerEnter,
        /// <summary>前置波次全部清完</summary>
        PreviousWaveCleared,
        /// <summary>关卡开始后 N 秒</summary>
        OnTimer,
        /// <summary>完成特定目标</summary>
        OnObjective,
    }

    /// <summary>单条敌人出生条目：指定敌人类型、数量、出生间隔。</summary>
    [System.Serializable]
    public class EnemySpawnEntry
    {
        [Tooltip("敌人 Prefab")]
        public GameObject enemyPrefab;

        [Tooltip("出生数量")]
        [Min(1)]
        public int count = 1;

        [Tooltip("同一批次内每条敌人的出生间隔（秒）")]
        [Min(0f)]
        public float spawnInterval = 0.5f;

        [Tooltip("出生位置模式")]
        public SpawnPositionMode positionMode = SpawnPositionMode.SpawnPoint;
    }

    /// <summary>一波敌人的完整配置。</summary>
    [System.Serializable]
    public class WaveData
    {
        [Tooltip("波次名称（如 Wave 1 / 增援 / Boss 波）")]
        public string waveName = "Wave 1";

        [Tooltip("波次开始前延迟（秒）")]
        [Min(0f)]
        public float delayBeforeSpawn = 2f;

        [Tooltip("本波敌人列表")]
        public List<EnemySpawnEntry> entries = new List<EnemySpawnEntry>();

        [Tooltip("触发条件")]
        public TriggerConditionType triggerCondition = TriggerConditionType.PreviousWaveCleared;

        [Tooltip("触发参数（OnTimer=秒数, OnObjective=目标ID, 其他忽略）")]
        public float triggerParam;
    }

    /// <summary>
    /// 遭遇战配置数据资产 — 定义一组遭遇战的波次编排。
    ///
    /// 策划在 Level Kit → Encounter Tab 中可视化编辑，保存为 .asset
    /// 运行时由 EncounterManager 读取并驱动波次逻辑。
    /// </summary>
    [CreateAssetMenu(fileName = "NewEncounterData", menuName = "Game/Level/Encounter Data")]
    public class EncounterData : ScriptableObject
    {
        [Tooltip("遭遇战名称")]
        public string encounterName = "未命名遭遇战";

        [Tooltip("遭遇战描述")]
        [TextArea(2, 4)]
        public string description = "";

        [Tooltip("波次列表")]
        public List<WaveData> waves = new List<WaveData>();

        /// <summary>获取总敌人数量。</summary>
        public int TotalEnemyCount
        {
            get
            {
                int count = 0;
                foreach (var wave in waves)
                    foreach (var entry in wave.entries)
                        count += entry.count;
                return count;
            }
        }

        /// <summary>获取波次数量。</summary>
        public int WaveCount => waves.Count;

        /// <summary>快速创建带默认波次的 EncounterData。</summary>
        public static EncounterData CreateDefault()
        {
            var data = CreateInstance<EncounterData>();
            data.waves.Add(new WaveData
            {
                waveName = "Wave 1",
                delayBeforeSpawn = 0f,
                triggerCondition = TriggerConditionType.OnTriggerEnter,
                entries = new List<EnemySpawnEntry>
                {
                    new EnemySpawnEntry { count = 2, spawnInterval = 0.5f },
                }
            });
            return data;
        }
    }
}
