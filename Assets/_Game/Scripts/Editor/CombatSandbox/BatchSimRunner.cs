#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Diagnostics;
using Game.Character;

namespace Game.EditorTools.CombatSandbox
{
    /// <summary>
    /// 批量模拟引擎 — 在 Editor 中驱动反复 Play/Stop 收集统计数据。
    ///
    /// 用法：
    ///   var runner = new BatchSimRunner(config);
    ///   runner.OnProgress += (i, total) => { ... };
    ///   var result = runner.Execute();
    /// </summary>
    public class BatchSimRunner
    {
        public BatchSimConfig config;
        public event System.Action<int, int> OnProgress;
        public event System.Action<SimRunResult> OnRunComplete;

        private List<SimRunResult> _results = new List<SimRunResult>();

        public BatchSimRunner(BatchSimConfig config)
        {
            this.config = config;
        }

        /// <summary>执行批量模拟，返回汇总结果。</summary>
        public BatchSimResult Execute()
        {
            if (config == null || !config.IsValid)
            {
                UnityEngine.Debug.LogError("[BatchSim] 配置无效，无法运行。");
                return new BatchSimResult { config = config, runs = new List<SimRunResult>() };
            }

            _results.Clear();
            int total = config.simulationCount * config.enemyEntries.Count;

            // 预生成随机种子序列以保证可复现性
            var seedBase = config.fixedRandomSeed > 0
                ? config.fixedRandomSeed
                : System.Environment.TickCount;
            var rng = new System.Random(seedBase);
            var seeds = new List<int>();
            for (int i = 0; i < config.simulationCount; i++)
                seeds.Add(rng.Next());

            int runIndex = 0;

            // 在非 Play Mode 下执行统计型模拟（基于配置数值推算）
            for (int entryIdx = 0; entryIdx < config.enemyEntries.Count; entryIdx++)
            {
                var entry = config.enemyEntries[entryIdx];

                for (int sim = 0; sim < config.simulationCount; sim++)
                {
                    var result = SimulateSingleRun(entry, sim, seeds[sim]);
                    _results.Add(result);

                    runIndex++;
                    OnProgress?.Invoke(runIndex, total);
                    OnRunComplete?.Invoke(result);
                }
            }

            // 汇总
            int totalWins = 0, totalLosses = 0;
            float totalHP = 0f, totalTime = 0f;
            foreach (var r in _results)
            {
                if (r.playerWon) totalWins++; else totalLosses++;
                totalHP += r.remainingPlayerHPPercent;
                totalTime += r.duration;
            }

            float winRate = _results.Count > 0 ? (float)totalWins / _results.Count : 0;
            string difficulty;
            if (winRate >= config.easyThreshold) difficulty = "简单";
            else if (winRate <= config.hardThreshold) difficulty = "困难";
            else difficulty = "适中";

            return new BatchSimResult
            {
                config = config,
                runs = _results,
                totalRuns = _results.Count,
                wins = totalWins,
                losses = totalLosses,
                winRate = winRate,
                avgRemainingHPPercent = _results.Count > 0 ? totalHP / _results.Count : 0,
                avgDuration = _results.Count > 0 ? totalTime / _results.Count : 0,
                difficultyRating = difficulty,
                seedBase = seedBase,
            };
        }

        /// <summary>
        /// 模拟单局战斗 — 基于配置数值做统计推演。
        ///
        /// 简化模型（不使用真实物理）：
        ///   - PlayerHP = playerMaxHP，EnemyHP = sum(enemyMaxHP × count)
        ///   - PlayerDPS = playerAtk × playerAtkSpeed
        ///   - EnemyDPS = sum(enemyAtk × count) / sum(enemyAtkInterval)
        ///   - 每 tick 双方互减 HP，加入随机波动（±15%）
        ///   - Player 拥有格挡/闪避修正因子（根据模拟次数微调）
        /// </summary>
        SimRunResult SimulateSingleRun(EnemyBatchEntry entry, int simIndex, int seed)
        {
            var sw = Stopwatch.StartNew();
            var rng = new System.Random(seed);

            // ── 提取数值参数 ──────────────────────────────────
            // Player
            float pHP = 100f, pAtk = 25f, pAtkSpeed = 1.5f;
            if (config.playerPrefab != null)
            {
                var pm = config.playerPrefab.GetComponent<CharacterMotor>();
                if (pm != null)
                {
                    pHP = Mathf.Max(pm.maxHealth, 1f);
                    pAtk = Mathf.Max(pm.maxHealth * 0.25f, 1f);
                }
            }

            // Enemy
            float totalEHP = 0f, totalEDPS = 0f;
            int enemyCount = entry.count;
            if (entry.enemyPrefab != null)
            {
                var em = entry.enemyPrefab.GetComponent<EnemyMotor>();
                if (em != null)
                {
                    totalEHP = Mathf.Max(em.maxHealth, 1f) * entry.count;
                }
                var ai = entry.enemyPrefab.GetComponent<EnemyAI>();
                if (ai != null)
                {
                    totalEDPS = ai.attackDamage * entry.count / Mathf.Max(ai.attackCooldown, 0.3f);
                }
            }

            if (totalEHP <= 0) totalEHP = 80f * entry.count;
            if (totalEDPS <= 0) totalEDPS = 8f * entry.count;

            // ── 战斗模拟 ──────────────────────────────────────
            float time = 0f;
            float playerHP = pHP;
            float enemyHP = totalEHP;
            const float tick = 0.1f;
            bool playerWon = false;
            float maxTime = config.maxDurationPerRun;

            while (time < maxTime && playerHP > 0 && enemyHP > 0)
            {
                // 随机波动因子
                float pf = 0.85f + (float)rng.NextDouble() * 0.3f;  // 0.85 ~ 1.15
                float ef = 0.85f + (float)rng.NextDouble() * 0.3f;  // 0.85 ~ 1.15

                // 玩家格挡概率 ~20%，闪避概率 ~10%
                float blockChance = 0.2f;
                float dodgeChance = 0.1f;

                // 敌人对玩家造成伤害
                float eDmg = totalEDPS * tick * ef;
                if (rng.NextDouble() > blockChance)
                    playerHP -= eDmg;

                // 玩家对敌人造成伤害
                float pDmg = pAtk * pAtkSpeed * tick * pf;
                if (rng.NextDouble() > dodgeChance * 0.5f) // 敌人闪避减半
                    enemyHP -= pDmg;

                // 随敌人数量减少而降低输出（敌人逐个死亡）
                if (enemyHP < totalEHP * 0.5f)
                    totalEDPS *= 0.7f; // 近似：一半敌人已阵亡

                time += tick;
            }

            playerWon = enemyHP <= 0 && playerHP > 0;
            if (!playerWon && playerHP <= 0) playerHP = 0;

            sw.Stop();

            return new SimRunResult
            {
                simIndex = simIndex,
                entryName = entry.DisplayName,
                playerWon = playerWon,
                duration = Mathf.Min(time, maxTime),
                remainingPlayerHPPercent = Mathf.Clamp01(playerHP / pHP),
                computeTimeMs = sw.ElapsedMilliseconds,
                seed = seed,
            };
        }
    }

    /// <summary>单局模拟结果。</summary>
    public class SimRunResult
    {
        public int simIndex;
        public string entryName;
        public bool playerWon;
        public float duration;
        public float remainingPlayerHPPercent;
        public long computeTimeMs;
        public int seed;
    }

    /// <summary>批量模拟汇总结果。</summary>
    public class BatchSimResult
    {
        public BatchSimConfig config;
        public List<SimRunResult> runs;
        public int totalRuns;
        public int wins;
        public int losses;
        public float winRate;
        public float avgRemainingHPPercent;
        public float avgDuration;
        public string difficultyRating;
        public int seedBase;
    }
}
#endif
