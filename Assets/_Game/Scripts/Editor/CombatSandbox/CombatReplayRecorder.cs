#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Game.Character;

namespace Game.EditorTools.CombatSandbox
{
    /// <summary>
    /// 战斗回放录制器 — 在 Play Mode 下定期采集帧快照和事件。
    ///
    /// 用法：
    ///   CombatReplayRecorder.BeginRecording();
    ///   // ... 战斗进行中 ...
    ///   var replay = CombatReplayRecorder.EndRecording();
    /// </summary>
    public static class CombatReplayRecorder
    {
        private static bool _isRecording = false;
        private static float _startTime;
        private static float _lastFrameTime;
        private static float _frameInterval = 0.5f; // 每 0.5 秒一个快照
        private static List<ReplayFrame> _frames;
        private static List<ReplayEvent> _events;
        private static List<string> _participants;
        private static Dictionary<EnemyMotor, EnemyAI.State> _prevAIStates;

        /// <summary>是否正在录制。</summary>
        public static bool IsRecording => _isRecording;

        /// <summary>当前已录制的帧数。</summary>
        public static int FrameCount => _frames != null ? _frames.Count : 0;

        /// <summary>当前已录制的事件数。</summary>
        public static int EventCount => _events != null ? _events.Count : 0;

        /// <summary>开始录制。</summary>
        public static void BeginRecording()
        {
            if (!Application.isPlaying) return;

            _isRecording = true;
            _startTime = Time.time;
            _lastFrameTime = 0f;
            _frames = new List<ReplayFrame>();
            _events = new List<ReplayEvent>();
            _participants = new List<string>();
            _prevAIStates = new Dictionary<EnemyMotor, EnemyAI.State>();

            // 记录参与者
            var players = Object.FindObjectsOfType<CharacterMotor>();
            foreach (var p in players)
                _participants.Add($"[Player] {p.name}");

            var enemies = Object.FindObjectsOfType<EnemyMotor>();
            foreach (var e in enemies)
            {
                _participants.Add($"[Enemy] {e.name}");
                var ai = e.GetComponent<EnemyAI>();
                if (ai != null)
                    _prevAIStates[e] = ai.CurrentState;
            }

            RecordEvent(ReplayEventType.CombatStart, "", "", 0, "战斗开始");

            // 立即记录第 0 帧
            RecordFrame();

            EditorApplication.update += OnEditorUpdate;
            Debug.Log("[CombatReplay] 开始录制...");
        }

        /// <summary>结束录制，返回回放数据。</summary>
        public static CombatReplayData EndRecording()
        {
            EditorApplication.update -= OnEditorUpdate;
            _isRecording = false;

            if (!Application.isPlaying || _frames == null)
                return null;

            // 记录最后一帧
            RecordFrame();

            RecordEvent(ReplayEventType.CombatEnd, "", "", 0, "战斗结束");

            // 创建 ScriptableObject
            var data = ScriptableObject.CreateInstance<CombatReplayData>();
            data.recordedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            data.totalDuration = Time.time - _startTime;
            data.participants = _participants;
            data.frames = _frames;
            data.events = _events;

            Debug.Log($"[CombatReplay] 录制完成: {data.frames.Count} 帧, {data.events.Count} 事件, " +
                      $"时长 {data.totalDuration:F1}s");

            return data;
        }

        /// <summary>录制关键事件（外部可调用）。</summary>
        public static void RecordEvent(ReplayEventType type, string source, string target,
            float value, string description)
        {
            if (!_isRecording || _events == null) return;

            _events.Add(new ReplayEvent
            {
                timestamp = Time.time - _startTime,
                eventType = type,
                sourceName = source,
                targetName = target,
                value = value,
                description = description,
            });
        }

        static void OnEditorUpdate()
        {
            if (!_isRecording || !Application.isPlaying) return;

            float elapsed = Time.time - _startTime;

            // 按固定间隔录制帧
            if (elapsed - _lastFrameTime >= _frameInterval)
            {
                RecordFrame();
                _lastFrameTime = elapsed;
            }

            // 检测 AI 状态变更
            CheckAIStateChanges();
        }

        static void RecordFrame()
        {
            var frame = new ReplayFrame
            {
                timestamp = Time.time - _startTime,
            };

            // Player
            var players = Object.FindObjectsOfType<CharacterMotor>();
            if (players.Length > 0)
            {
                frame.playerHP = players[0].currentHealth;
                frame.playerPosition = players[0].transform.position;
            }

            // Enemies
            var enemies = Object.FindObjectsOfType<EnemyMotor>();
            foreach (var e in enemies)
            {
                frame.enemyHPs.Add(e.currentHealth);
                frame.enemyPositions.Add(e.transform.position);

                var ai = e.GetComponent<EnemyAI>();
                frame.enemyAIStates.Add(ai != null ? (int)ai.CurrentState : -1);
            }

            _frames.Add(frame);
        }

        static void CheckAIStateChanges()
        {
            var enemies = Object.FindObjectsOfType<EnemyMotor>();
            foreach (var e in enemies)
            {
                var ai = e.GetComponent<EnemyAI>();
                if (ai == null) continue;

                if (_prevAIStates.TryGetValue(e, out var prev) && prev != ai.CurrentState)
                {
                    RecordEvent(ReplayEventType.AIStateChange, e.name, "",
                        0, $"{RuntimeBridge.GetAIStateLabel(prev)} → {RuntimeBridge.GetAIStateLabel(ai.CurrentState)}");
                    _prevAIStates[e] = ai.CurrentState;
                }
            }
        }
    }
}
#endif
