#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Game.EditorTools.Shared;
using Game.Character;
using Game.SkillSystem;

namespace Game.EditorTools.CombatSandbox
{
    /// <summary>
    /// Combat Sandbox 主面板 — AI 与战斗调试工具。
    ///
    /// 三步 Tab：
    ///   Live Test — 实时对战调试（数据面板 + 参数热调 + AI 控制）
    ///   Batch     — 批量模拟统计（Phase 3 实施）
    ///   Replay    — AI 决策回放（Phase 3 实施）
    ///
    /// 设计原则：
    ///   - 复用 Character Kit / Level Kit 的 Sub Tab 按钮模式
    ///   - 通过 RuntimeBridge 安全访问 Play Mode 运行时数据
    ///   - 颜色统一使用 EditorSkinPalette
    /// </summary>
    public class CombatSandboxPanel
    {
        private enum Tab { LiveTest, Batch, Replay }
        private Tab _tab = Tab.LiveTest;
        private Vector2 _scroll;
        private bool _foldoutPlayer = true;
        private bool _foldoutEnemies = true;
        private bool _foldoutParams = true;
        private bool _foldoutLog = true;

        // ── Live Test 状态 ──────────────────────────────────────
        private bool _aiPaused = false;
        private bool _playerInvincible = false;
        private bool _enemyInvincible = false;
        private List<DamageLogEntry> _damageLog = new List<DamageLogEntry>();
        private const int MaxDamageLogEntries = 30;

        // 热调参数缓存
        private Dictionary<CharacterMotor, float> _cachedSprintSpeed = new Dictionary<CharacterMotor, float>();
        private Dictionary<CharacterMotor, float> _cachedFreeRunSpeed = new Dictionary<CharacterMotor, float>();
        private Dictionary<EnemyAI, float> _cachedAttackCD = new Dictionary<EnemyAI, float>();
        private Dictionary<EnemyAI, float> _cachedAttackDmg = new Dictionary<EnemyAI, float>();
        private Dictionary<EnemyAI, float> _cachedSpeedMulti = new Dictionary<EnemyAI, float>();
        private Dictionary<EnemyMotor, float> _cachedMoveSpeed = new Dictionary<EnemyMotor, float>();
        private bool _paramsInitialised = false;

        // ── Batch Tab 状态 ──────────────────────────────────────
        private BatchSimConfig _batchConfig;
        private BatchSimResult _batchResult;
        private bool _batchRunning = false;
        private int _batchProgress = 0;
        private int _batchTotal = 0;
        private bool _batchFoldoutConfig = true;
        private bool _batchFoldoutDetail = true;
        private Vector2 _batchDetailScroll;

        // ── Replay Tab 状态 ─────────────────────────────────────
        private CombatReplayData _replayData;
#pragma warning disable CS0414
        private bool _replayIsRecording = false;
#pragma warning restore CS0414
        private bool _replayIsPlaying = false;
        private float _replayCurrentTime = 0f;
        private float _replaySpeed = 1f;
        private int _replaySelectedEvent = -1;
        private Vector2 _replayEventScroll;
        private bool _replayFoldoutEvents = true;

        // ═══════════════════════════════════════════════════════════
        // P6：GUIStyle 静态缓存，避免 OnGUI 每帧 new GUIStyle 产生 GC
        // ═══════════════════════════════════════════════════════════
        static readonly GUIStyle s_chipStyleActive = new GUIStyle(EditorStyles.miniButton) { normal = { textColor = Color.white } };
        static readonly GUIStyle s_chipStyleDimmed = new GUIStyle(EditorStyles.miniButton) { normal = { textColor = EditorSkinPalette.TextTertiary } };
        static readonly GUIStyle s_centeredWhiteStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = Color.white } };
        static readonly GUIStyle s_centeredGreyStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        static readonly GUIStyle s_logStyleBase = new GUIStyle(EditorStyles.miniLabel);
        static readonly GUIStyle s_winRateStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 18, normal = { textColor = Color.white } };
        static readonly GUIStyle s_difficultyStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
        static readonly GUIStyle s_resultStyle = new GUIStyle(EditorStyles.boldLabel);
        static readonly GUIStyle s_timeDisplayStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight };
        static readonly GUIStyle s_badgeStyle = new GUIStyle(EditorStyles.miniButton) { normal = { textColor = Color.white } };

        // ═══════════════════════════════════════════════════════════
        // 入口
        // ═══════════════════════════════════════════════════════════

        public void OnGUI()
        {
            DrawSubTabs();
            EditorGUILayout.Space(6);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
            switch (_tab)
            {
                case Tab.LiveTest: DrawLiveTestTab(); break;
                case Tab.Batch:    DrawBatchTab();    break;
                case Tab.Replay:   DrawReplayTab();   break;
            }
            }
            finally { EditorGUILayout.EndScrollView(); }

            // ── 跨 Kit 跳转 (Phase 4.4) ─────────────────────────
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("快速跳转", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            CrossKitBridge.DrawKitJumpButton("Character Kit - 调整角色参数", "CharacterKit");
            CrossKitBridge.DrawKitJumpButton("Level Kit - 编辑关卡", "LevelKit");
            EditorGUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════
        // Tab 路由
        // ═══════════════════════════════════════════════════════════

        void DrawSubTabs()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(4);
            DrawSubTabButton(Tab.LiveTest, "\u25B6 实时对战 Live Test");
            DrawSubTabButton(Tab.Batch,    "\u2261 批量模拟 Batch");
            DrawSubTabButton(Tab.Replay,   "\u21BA 回放 Replay");
            EditorGUILayout.Space(4);
            EditorGUILayout.EndHorizontal();
        }

        void DrawSubTabButton(Tab t, string label)
        {
            bool active = _tab == t;
            var prev = GUI.backgroundColor;
            if (active) GUI.backgroundColor = new Color(0.35f, 0.55f, 0.9f);
            if (GUILayout.Button(label, GUILayout.Height(26)))
            {
                if (_tab != t)
                {
                    _tab = t;
                    _paramsInitialised = false;
                }
            }
            GUI.backgroundColor = prev;
        }

        // ═══════════════════════════════════════════════════════════
        // Live Test Tab
        // ═══════════════════════════════════════════════════════════

        void DrawLiveTestTab()
        {
            if (!RuntimeBridge.IsPlaying)
            {
                EditorGUILayout.HelpBox(
                    "请先进入 Play Mode 以使用 Live Test 功能。\n\n" +
                    "进入 Play Mode 后，此面板将实时显示：\n" +
                    "  • 玩家 HP / 体力 / 法力 / DPS\n" +
                    "  • 敌人 AI 状态 / HP / 攻击参数\n" +
                    "  • 参数热调滑块（攻击力/速度/CD）\n" +
                    "  • 伤害日志",
                    MessageType.Info);
                _paramsInitialised = false;
                return;
            }

            var players = RuntimeBridge.GetPlayerMotors();
            var enemies = RuntimeBridge.GetEnemyMotors();

            if (players.Length == 0 && enemies.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Play Mode 运行中，但未检测到 CharacterMotor 或 EnemyMotor。\n" +
                    "请确保场景中有玩家和敌人角色。",
                    MessageType.Warning);
                return;
            }

            // ── 控制按钮栏 ──────────────────────────────────────
            DrawLiveTestControls();

            EditorGUILayout.Space(6);

            // ── 玩家面板 ─────────────────────────────────────────
            if (players.Length > 0)
                DrawPlayerPanel(players);

            EditorGUILayout.Space(6);

            // ── 敌人面板 ─────────────────────────────────────────
            if (enemies.Length > 0)
                DrawEnemyPanels(enemies);

            EditorGUILayout.Space(6);

            // ── 参数热调 ────────────────────────────────────────
            DrawParamSliders(players, enemies);

            EditorGUILayout.Space(6);

            // ── 伤害日志 ────────────────────────────────────────
            DrawDamageLog();
        }

        // ── 控制按钮 ───────────────────────────────────────────────

        void DrawLiveTestControls()
        {
            EditorGUILayout.BeginHorizontal();

            var prevBg = GUI.backgroundColor;

            // AI 暂停/恢复
            GUI.backgroundColor = _aiPaused ? EditorSkinPalette.Success : EditorSkinPalette.Warning;
            if (GUILayout.Button(_aiPaused ? "▶ 恢复 AI" : "⏸ 暂停 AI", GUILayout.Height(28), GUILayout.Width(100)))
            {
                _aiPaused = !_aiPaused;
                ApplyAIPause();
            }

            GUI.backgroundColor = _playerInvincible ? EditorSkinPalette.Warning : Color.white;
            if (GUILayout.Button("玩家无敌", GUILayout.Height(28), GUILayout.Width(80)))
            {
                _playerInvincible = !_playerInvincible;
            }

            GUI.backgroundColor = _enemyInvincible ? EditorSkinPalette.Warning : Color.white;
            if (GUILayout.Button("敌人无敌", GUILayout.Height(28), GUILayout.Width(80)))
            {
                _enemyInvincible = !_enemyInvincible;
            }

            GUI.backgroundColor = prevBg;

            GUILayout.FlexibleSpace();

            // 重置
            if (GUILayout.Button("重置参数", GUILayout.Height(28), GUILayout.Width(80)))
            {
                _paramsInitialised = false;
                _damageLog.Clear();
                _cachedSprintSpeed.Clear();
                _cachedFreeRunSpeed.Clear();
                _cachedAttackCD.Clear();
                _cachedAttackDmg.Clear();
                _cachedSpeedMulti.Clear();
                _cachedMoveSpeed.Clear();
            }

            if (GUILayout.Button("清空日志", GUILayout.Height(28), GUILayout.Width(80)))
                _damageLog.Clear();

            EditorGUILayout.EndHorizontal();
        }

        void ApplyAIPause()
        {
            var enemies = RuntimeBridge.GetEnemyMotors();
            foreach (var em in enemies)
            {
                var ai = RuntimeBridge.GetEnemyAI(em);
                if (ai != null) ai.enabled = !_aiPaused;
            }
        }

        // ── 玩家面板 ──────────────────────────────────────────────

        void DrawPlayerPanel(CharacterMotor[] players)
        {
            _foldoutPlayer = EditorGUILayout.Foldout(_foldoutPlayer, "玩家状态", true);

            if (!_foldoutPlayer) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            foreach (var motor in players)
            {
                if (motor == null) continue;

                // 名称
                EditorGUILayout.LabelField(motor.name, EditorStyles.boldLabel);

                // HP Bar
                float hpPct = RuntimeBridge.HPPercent(motor.currentHealth, motor.maxHealth);
                DrawHealthBar("HP", hpPct, motor.currentHealth, motor.maxHealth, Color.red);

                // Stamina Bar
                float stamPct = Mathf.Clamp01(motor.currentStamina / Mathf.Max(motor.maxStamina, 1f));
                DrawHealthBar("体力", stamPct, motor.currentStamina, motor.maxStamina, Color.green);

                // Mana Bar
                float manaPct = Mathf.Clamp01(motor.currentMana / Mathf.Max(motor.maxMana, 1f));
                DrawHealthBar("法力", manaPct, motor.currentMana, motor.maxMana, new Color(0.3f, 0.5f, 1f));

                // 状态行
                EditorGUILayout.BeginHorizontal();
                DrawStatusChip(motor.isGrounded, "着地");
                DrawStatusChip(motor.isSprinting, "冲刺");
                DrawStatusChip(motor.isCrouching, "蹲下");
                DrawStatusChip(motor.isJumping, "跳跃");
                DrawStatusChip(motor.isStrafing, "横移");
                DrawStatusChip(motor.isDead, "死亡", true);
                EditorGUILayout.LabelField($"速度: {motor.CurrentVelocity:F1} m/s", EditorStyles.miniLabel, GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();

                // 武器
                var weaponSwitcher = RuntimeBridge.GetWeaponSwitcher(motor);
                if (weaponSwitcher != null && weaponSwitcher.CurrentWeapon != null)
                {
                    EditorGUILayout.LabelField($"武器: {weaponSwitcher.CurrentWeapon.weaponName}",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(4);
            }

            EditorGUILayout.EndVertical();
        }

        void DrawStatusChip(bool active, string label, bool danger = false)
        {
            var prev = GUI.color;
            if (active)
            {
                GUI.color = danger ? new Color(0.9f, 0.2f, 0.2f) : new Color(0.3f, 0.7f, 0.3f);
                GUILayout.Button(label, s_chipStyleActive, GUILayout.Width(50), GUILayout.Height(18));
            }
            else
            {
                GUI.color = new Color(0.4f, 0.4f, 0.4f);
                GUILayout.Button(label, s_chipStyleDimmed, GUILayout.Width(50), GUILayout.Height(18));
            }
            GUI.color = prev;
        }

        void DrawHealthBar(string label, float pct, float current, float max, Color barColor)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(30));

            // 背景
            var rect = GUILayoutUtility.GetRect(200, 16);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));

            // 填充
            var fillRect = new Rect(rect.x, rect.y, rect.width * pct, rect.height);
            EditorGUI.DrawRect(fillRect, barColor);

            // 文本
            GUI.Label(rect, $"{current:F0} / {max:F0} ({pct * 100f:F0}%)",
                s_centeredWhiteStyle);

            EditorGUILayout.EndHorizontal();
        }

        // ── 敌人面板 ──────────────────────────────────────────────

        void DrawEnemyPanels(EnemyMotor[] enemies)
        {
            _foldoutEnemies = EditorGUILayout.Foldout(_foldoutEnemies, $"敌人列表 ({enemies.Length})", true);

            if (!_foldoutEnemies) return;

            foreach (var motor in enemies)
            {
                if (motor == null) continue;

                var ai = RuntimeBridge.GetEnemyAI(motor);
                var stateColor = ai != null
                    ? RuntimeBridge.GetAIStateColor(ai.CurrentState)
                    : Color.gray;
                var stateLabel = ai != null
                    ? RuntimeBridge.GetAIStateLabel(ai.CurrentState)
                    : "?";

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // 标题行
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(motor.name, EditorStyles.boldLabel);

                // AI 状态徽章
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = stateColor;
                GUILayout.Button(stateLabel, s_badgeStyle, GUILayout.Width(60), GUILayout.Height(20));
                GUI.backgroundColor = prevBg;

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    $"Pos: {motor.transform.position.ToString("F1")}",
                    EditorStyles.miniLabel, GUILayout.Width(160));
                EditorGUILayout.EndHorizontal();

                // HP
                float hpPct = RuntimeBridge.HPPercent(motor.currentHealth, motor.maxHealth);
                DrawHealthBar("HP", hpPct, motor.currentHealth, motor.maxHealth,
                    hpPct > 0.3f ? Color.red : new Color(0.6f, 0.1f, 0.1f));

                // 硬直
                if (motor.IsStunned)
                {
                    GUI.color = EditorSkinPalette.Warning;
                    EditorGUILayout.LabelField("   ⚡ 硬直中", EditorStyles.miniBoldLabel);
                    GUI.color = Color.white;
                }

                // AI 参数摘要
                if (ai != null)
                {
                    EditorGUILayout.LabelField(
                        $"攻击力: {ai.attackDamage:F0}  |  范围: {ai.attackRange:F1}m  |  CD: {ai.attackCooldown:F1}s  |  速度倍率: {ai.chaseSpeedMultiplier:F1}x",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
        }

        // ── 参数热调滑块 ──────────────────────────────────────────

        void DrawParamSliders(CharacterMotor[] players, EnemyMotor[] enemies)
        {
            _foldoutParams = EditorGUILayout.Foldout(_foldoutParams, "参数热调", true);

            if (!_foldoutParams) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (!_paramsInitialised)
            {
                InitParamCache(players, enemies);
                _paramsInitialised = true;
            }

            // ── 玩家参数 ──────────────────────────────────────
            if (players.Length > 0)
            {
                EditorGUILayout.LabelField("玩家", EditorStyles.miniBoldLabel);
                var motor = players[0];

                if (_cachedSprintSpeed.ContainsKey(motor))
                {
                    var newVal = EditorGUILayout.Slider("冲刺速度", _cachedSprintSpeed[motor], 1f, 12f);
                    if (Mathf.Abs(newVal - motor.freeSprintSpeed) > 0.01f)
                    {
                        motor.freeSprintSpeed = newVal;
                        _cachedSprintSpeed[motor] = newVal;
                    }
                }

                if (_cachedFreeRunSpeed.ContainsKey(motor))
                {
                    var newVal = EditorGUILayout.Slider("跑步速度", _cachedFreeRunSpeed[motor], 0.5f, 8f);
                    if (Mathf.Abs(newVal - motor.freeRunSpeed) > 0.01f)
                    {
                        motor.freeRunSpeed = newVal;
                        _cachedFreeRunSpeed[motor] = newVal;
                    }
                }
            }

            EditorGUILayout.Space(4);

            // ── 敌人参数 ──────────────────────────────────────
            if (enemies.Length > 0)
            {
                EditorGUILayout.LabelField("敌人", EditorStyles.miniBoldLabel);

                for (int i = 0; i < enemies.Length; i++)
                {
                    var motor = enemies[i];
                    var ai = RuntimeBridge.GetEnemyAI(motor);
                    if (ai == null) continue;

                    EditorGUILayout.LabelField($"  {motor.name}", EditorStyles.boldLabel);

                    // 攻击力
                    if (_cachedAttackDmg.ContainsKey(ai))
                    {
                        var newVal = EditorGUILayout.Slider("  攻击力", _cachedAttackDmg[ai], 1f, 100f);
                        if (Mathf.Abs(newVal - ai.attackDamage) > 0.01f)
                        {
                            ai.attackDamage = newVal;
                            _cachedAttackDmg[ai] = newVal;
                        }
                    }

                    // 攻击 CD
                    if (_cachedAttackCD.ContainsKey(ai))
                    {
                        var newVal = EditorGUILayout.Slider("  攻击间隔 (s)", _cachedAttackCD[ai], 0.1f, 10f);
                        if (Mathf.Abs(newVal - ai.attackCooldown) > 0.01f)
                        {
                            ai.attackCooldown = newVal;
                            _cachedAttackCD[ai] = newVal;
                        }
                    }

                    // 追踪速度倍率
                    if (_cachedSpeedMulti.ContainsKey(ai))
                    {
                        var newVal = EditorGUILayout.Slider("  追踪速度倍率", _cachedSpeedMulti[ai], 0.2f, 5f);
                        if (Mathf.Abs(newVal - ai.chaseSpeedMultiplier) > 0.01f)
                        {
                            ai.chaseSpeedMultiplier = newVal;
                            _cachedSpeedMulti[ai] = newVal;
                        }
                    }

                    // 移动速度
                    if (_cachedMoveSpeed.ContainsKey(motor))
                    {
                        var newVal = EditorGUILayout.Slider("  移动速度", _cachedMoveSpeed[motor], 0.5f, 8f);
                        if (Mathf.Abs(newVal - motor.moveSpeed) > 0.01f)
                        {
                            motor.moveSpeed = newVal;
                            _cachedMoveSpeed[motor] = newVal;
                        }
                    }

                    EditorGUILayout.Space(2);
                }
            }

            EditorGUILayout.EndVertical();
        }

        void InitParamCache(CharacterMotor[] players, EnemyMotor[] enemies)
        {
            foreach (var motor in players)
            {
                if (motor == null) continue;
                _cachedSprintSpeed[motor] = motor.freeSprintSpeed;
                _cachedFreeRunSpeed[motor] = motor.freeRunSpeed;
            }

            foreach (var motor in enemies)
            {
                if (motor == null) continue;
                _cachedMoveSpeed[motor] = motor.moveSpeed;

                var ai = RuntimeBridge.GetEnemyAI(motor);
                if (ai != null)
                {
                    _cachedAttackDmg[ai] = ai.attackDamage;
                    _cachedAttackCD[ai] = ai.attackCooldown;
                    _cachedSpeedMulti[ai] = ai.chaseSpeedMultiplier;
                }
            }
        }

        // ── 伤害日志 ───────────────────────────────────────────────

        void DrawDamageLog()
        {
            _foldoutLog = EditorGUILayout.Foldout(_foldoutLog,
                $"伤害日志 ({_damageLog.Count})", true);

            if (!_foldoutLog) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_damageLog.Count == 0)
            {
                EditorGUILayout.LabelField("  暂无伤害记录",
                    s_centeredGreyStyle);
            }

            // 反转显示（最新在前）
            int start = Mathf.Max(0, _damageLog.Count - MaxDamageLogEntries);
            for (int i = _damageLog.Count - 1; i >= start; i--)
            {
                var entry = _damageLog[i];

                // 颜色：治疗绿色 / 暴击橙色 / 普通白色
                if (entry.isHeal)
                    s_logStyleBase.normal.textColor = new Color(0.3f, 0.9f, 0.3f);
                else if (entry.isCrit)
                    s_logStyleBase.normal.textColor = new Color(1f, 0.6f, 0.1f);
                else
                    s_logStyleBase.normal.textColor = Color.white;
                var logStyle = s_logStyleBase;

                string icon = entry.isHeal ? "+" : entry.isCrit ? "💥" : "-";
                string skillStr = string.IsNullOrEmpty(entry.skillName) ? "" : $" [{entry.skillName}]";
                EditorGUILayout.LabelField(
                    $"  {icon} {entry.sourceName} → {entry.targetName}: {entry.damage:F0}{skillStr}",
                    logStyle);
            }

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════
        // Batch Tab — 占位（Phase 3 实施）
        // ═══════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════
        // Batch Tab — 批量模拟统计 (C2)
        // ═══════════════════════════════════════════════════════════

        void DrawBatchTab()
        {
            EditorGUILayout.LabelField("批量模拟", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // ── 配置资产 ──────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            _batchConfig = (BatchSimConfig)EditorGUILayout.ObjectField(
                "模拟配置", _batchConfig, typeof(BatchSimConfig), false);

            if (_batchConfig == null && GUILayout.Button("新建", GUILayout.Width(50)))
            {
                var path = EditorUtility.SaveFilePanelInProject(
                    "新建 BatchSimConfig", "BatchSimConfig", "asset",
                    "选择保存位置", "Assets/_Game/");
                if (!string.IsNullOrEmpty(path))
                {
                    var cfg = ScriptableObject.CreateInstance<BatchSimConfig>();
                    AssetDatabase.CreateAsset(cfg, path);
                    AssetDatabase.SaveAssets();
                    _batchConfig = cfg;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_batchConfig == null)
            {
                EditorGUILayout.HelpBox("请创建或拖入一个 BatchSimConfig 资产。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(6);

            // ── 配置编辑 ──────────────────────────────────────
            _batchFoldoutConfig = EditorGUILayout.Foldout(_batchFoldoutConfig, "模拟参数", true);
            if (_batchFoldoutConfig)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUI.BeginChangeCheck();
                _batchConfig.playerPrefab = (GameObject)EditorGUILayout.ObjectField(
                    "玩家 Prefab", _batchConfig.playerPrefab, typeof(GameObject), false);

                // 敌人条目编辑
                EditorGUILayout.LabelField($"敌人配置 ({_batchConfig.enemyEntries.Count})",
                    EditorStyles.miniBoldLabel);
                for (int i = 0; i < _batchConfig.enemyEntries.Count; i++)
                {
                    var entry = _batchConfig.enemyEntries[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(24));
                    entry.enemyPrefab = (GameObject)EditorGUILayout.ObjectField(
                        entry.enemyPrefab, typeof(GameObject), false, GUILayout.Width(160));
                    EditorGUILayout.LabelField("×", GUILayout.Width(12));
                    entry.count = EditorGUILayout.IntField(entry.count, GUILayout.Width(40));
                    entry.displayName = EditorGUILayout.TextField(entry.displayName ?? "",
                        GUILayout.Width(80));

                    if (GUILayout.Button("✕", GUILayout.Width(24)))
                    {
                        _batchConfig.enemyEntries.RemoveAt(i);
                        i--;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ 添加敌人条目", GUILayout.Width(120)))
                    _batchConfig.enemyEntries.Add(new EnemyBatchEntry());
                EditorGUILayout.EndHorizontal();

                _batchConfig.simulationCount = EditorGUILayout.IntField(
                    "模拟次数", _batchConfig.simulationCount);
                _batchConfig.maxDurationPerRun = EditorGUILayout.FloatField(
                    "最长单局 (s)", _batchConfig.maxDurationPerRun);
                _batchConfig.fixedRandomSeed = EditorGUILayout.IntField(
                    "固定随机种子 (0=随机)", _batchConfig.fixedRandomSeed);
                _batchConfig.easyThreshold = EditorGUILayout.Slider(
                    "「简单」阈值", _batchConfig.easyThreshold, 0f, 1f);
                _batchConfig.hardThreshold = EditorGUILayout.Slider(
                    "「困难」阈值", _batchConfig.hardThreshold, 0f, 1f);

                if (EditorGUI.EndChangeCheck() && _batchConfig != null)
                    EditorUtility.SetDirty(_batchConfig);

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(8);

            // ── 执行按钮 ──────────────────────────────────────
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = _batchConfig.IsValid
                ? EditorSkinPalette.Success
                : EditorSkinPalette.TextTertiary;
            GUI.enabled = _batchConfig.IsValid && !_batchRunning;

            if (GUILayout.Button(
                _batchRunning
                    ? $"⏳ 模拟中... {_batchProgress}/{_batchTotal}"
                    : $"▶ 开始批量模拟 ({_batchConfig.simulationCount} 次 × {_batchConfig.enemyEntries.Count} 种 = {_batchConfig.simulationCount * _batchConfig.enemyEntries.Count} 局)",
                GUILayout.Height(36)))
            {
                RunBatchSimulation();
            }

            GUI.enabled = true;
            GUI.backgroundColor = prevBg;

            if (_batchRunning)
            {
                EditorGUILayout.Space(4);
                var progressRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.Height(20));
                EditorGUI.DrawRect(progressRect, new Color(0.15f, 0.15f, 0.15f));
                float pct = _batchTotal > 0 ? (float)_batchProgress / _batchTotal : 0;
                var fillRect = new Rect(progressRect.x, progressRect.y,
                    progressRect.width * pct, progressRect.height);
                EditorGUI.DrawRect(fillRect, EditorSkinPalette.Highlight);
                GUI.Label(progressRect,
                    $"  {_batchProgress}/{_batchTotal}  ({pct * 100f:F0}%)",
                    s_centeredWhiteStyle);
            }

            // ── 统计结果 ──────────────────────────────────────
            if (_batchResult != null)
            {
                EditorGUILayout.Space(8);
                DrawBatchResult();
            }
        }

        void RunBatchSimulation()
        {
            if (_batchConfig == null || !_batchConfig.IsValid) return;

            _batchRunning = true;
            _batchResult = null;
            _batchProgress = 0;
            _batchTotal = _batchConfig.simulationCount * _batchConfig.enemyEntries.Count;

            var runner = new BatchSimRunner(_batchConfig);
            runner.OnProgress += (i, total) =>
            {
                _batchProgress = i;
                EditorApplication.delayCall += () => { }; // 强制重绘
            };

            try
            {
                _batchResult = runner.Execute();
            }
            finally
            {
                _batchRunning = false;
            }
        }

        void DrawBatchResult()
        {
            var r = _batchResult;
            if (r == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模拟结果", EditorStyles.boldLabel);

            // 胜率仪表盘
            EditorGUILayout.BeginHorizontal();

            // 圆形进度简化版 — 矩形条
            var gaugeRect = GUILayoutUtility.GetRect(80, 80, GUILayout.Width(80));
            EditorGUI.DrawRect(gaugeRect, new Color(0.1f, 0.1f, 0.1f));
            var fillY = gaugeRect.y + gaugeRect.height * (1f - r.winRate);
            var fillH = gaugeRect.height * r.winRate;
            var fillR = new Rect(gaugeRect.x, fillY, gaugeRect.width, fillH);
            EditorGUI.DrawRect(fillR,
                r.winRate > 0.5f ? new Color(0.3f, 0.7f, 0.3f) : new Color(0.9f, 0.3f, 0.3f));
            GUI.Label(gaugeRect, $"{r.winRate * 100f:F0}%",
                s_winRateStyle);

            EditorGUILayout.Space(12);

            // 统计数字
            EditorGUILayout.BeginVertical();
            var diffColor = r.difficultyRating switch
            {
                "简单" => EditorSkinPalette.Success,
                "困难" => new Color(0.9f, 0.3f, 0.3f),
                _ => EditorSkinPalette.Warning,
            };
            GUI.color = diffColor;
            EditorGUILayout.LabelField($"难度评级: {r.difficultyRating}",
                s_difficultyStyle);
            GUI.color = Color.white;

            EditorGUILayout.LabelField($"W: {r.wins}   L: {r.losses}   ({r.totalRuns} 局)");
            EditorGUILayout.LabelField($"平均剩余 HP: {r.avgRemainingHPPercent * 100f:F1}%");
            EditorGUILayout.LabelField($"平均耗时: {r.avgDuration:F1}s");
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            // ── 详细数据表 ──────────────────────────────────
            EditorGUILayout.Space(4);
            _batchFoldoutDetail = EditorGUILayout.Foldout(
                _batchFoldoutDetail, $"详细数据 ({r.runs.Count} 局)", true);
            if (_batchFoldoutDetail)
            {
                _batchDetailScroll = EditorGUILayout.BeginScrollView(
                    _batchDetailScroll, GUILayout.MaxHeight(200));
                try
                {

                // 表头
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("#", GUILayout.Width(30));
                EditorGUILayout.LabelField("敌人", GUILayout.Width(100));
                EditorGUILayout.LabelField("结果", GUILayout.Width(50));
                EditorGUILayout.LabelField("耗时", GUILayout.Width(50));
                EditorGUILayout.LabelField("剩余HP%", GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);

                foreach (var sim in r.runs)
                {
                    GUI.backgroundColor = sim.playerWon
                        ? new Color(0.15f, 0.35f, 0.15f)
                        : new Color(0.35f, 0.15f, 0.15f);
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    GUI.backgroundColor = Color.white;

                    EditorGUILayout.LabelField($"{sim.simIndex}", GUILayout.Width(30));
                    EditorGUILayout.LabelField(sim.entryName, GUILayout.Width(100));
                    GUI.color = sim.playerWon
                        ? EditorSkinPalette.Success
                        : new Color(0.9f, 0.3f, 0.3f);
                    EditorGUILayout.LabelField(sim.playerWon ? "胜" : "负",
                        s_resultStyle, GUILayout.Width(50));
                    GUI.color = Color.white;
                    EditorGUILayout.LabelField($"{sim.duration:F1}s", GUILayout.Width(50));
                    EditorGUILayout.LabelField($"{sim.remainingPlayerHPPercent * 100f:F1}%",
                        GUILayout.Width(60));
                    EditorGUILayout.EndHorizontal();
                }
                }
                finally { EditorGUILayout.EndScrollView(); }
            }

            // ── 导出 / 反馈按钮 ─────────────────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("📋 复制摘要", GUILayout.Width(100)))
            {
                var summary = $"BatchSim: {r.winRate:P0} 胜率 ({r.wins}W/{r.losses}L), " +
                              $"难度={r.difficultyRating}, 平均HP={r.avgRemainingHPPercent:P0}, " +
                              $"种子={r.seedBase}";
                GUIUtility.systemCopyBuffer = summary;
            }

            // 反馈：如果某敌人类型过强，提示跳转 Character Kit
            if (r.winRate < r.config.hardThreshold)
            {
                GUI.backgroundColor = EditorSkinPalette.Warning;
                if (GUILayout.Button("⚙ 打开 Character Kit 调整参数", GUILayout.Width(200)))
                {
                    EditorApplication.ExecuteMenuItem("Tools/Character Kit");
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════
        // Replay Tab — AI 决策回放 (C3)
        // ═══════════════════════════════════════════════════════════

        void DrawReplayTab()
        {
            EditorGUILayout.LabelField("AI 决策回放", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // ── 录制控制 ──────────────────────────────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("录制控制", EditorStyles.miniBoldLabel);

            bool isPlaying = Application.isPlaying;

            if (!isPlaying)
            {
                EditorGUILayout.HelpBox("请先进入 Play Mode 再开始录制。", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = isPlaying && !CombatReplayRecorder.IsRecording;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = EditorSkinPalette.Success;
            if (GUILayout.Button("⏺ 开始录制", GUILayout.Height(30)))
            {
                CombatReplayRecorder.BeginRecording();
                _replayIsRecording = true;
                _replayData = null;
            }
            GUI.backgroundColor = prevBg;

            GUI.enabled = isPlaying && CombatReplayRecorder.IsRecording;
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("⏹ 停止录制", GUILayout.Height(30)))
            {
                _replayData = CombatReplayRecorder.EndRecording();
                _replayIsRecording = false;
                _replayCurrentTime = 0f;
                _replayIsPlaying = false;

                // 提示保存
                if (_replayData != null)
                {
                    var savePath = EditorUtility.SaveFilePanelInProject(
                        "保存回放数据", "CombatReplay", "asset",
                        "选择保存位置", "Assets/_Game/");
                    if (!string.IsNullOrEmpty(savePath))
                    {
                        AssetDatabase.CreateAsset(_replayData, savePath);
                        AssetDatabase.SaveAssets();
                        EditorGUIUtility.PingObject(_replayData);
                    }
                }
            }
            GUI.backgroundColor = prevBg;
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // 录制状态
            if (CombatReplayRecorder.IsRecording)
            {
                GUI.color = EditorSkinPalette.Success;
                EditorGUILayout.LabelField($"⏺ 录制中... {CombatReplayRecorder.FrameCount} 帧, " +
                    $"{CombatReplayRecorder.EventCount} 事件", EditorStyles.miniBoldLabel);
                GUI.color = Color.white;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // ── 回放数据选择 ──────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            _replayData = (CombatReplayData)EditorGUILayout.ObjectField(
                "回放数据", _replayData, typeof(CombatReplayData), false);

            if (_replayData == null && GUILayout.Button("浏览库", GUILayout.Width(60)))
            {
                var guids = AssetDatabase.FindAssets("t:CombatReplayData");
                if (guids.Length > 0)
                {
                    var menu = new GenericMenu();
                    foreach (var g in guids)
                    {
                        var p = AssetDatabase.GUIDToAssetPath(g);
                        var rd = AssetDatabase.LoadAssetAtPath<CombatReplayData>(p);
                        var captured = rd;
                        menu.AddItem(new GUIContent($"{rd.recordedAt} - {rd.totalDuration:F1}s"),
                            false, () => _replayData = captured);
                    }
                    menu.ShowAsContext();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_replayData == null)
            {
                EditorGUILayout.HelpBox(
                    "录制一段战斗或拖入已保存的 CombatReplayData 资产。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(6);

            // ── 回放播放器 ───────────────────────────────────
            DrawReplayPlayer();
        }

        void DrawReplayPlayer()
        {
            if (_replayData == null) return;

            var data = _replayData;
            float totalDur = data.totalDuration;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── 元信息 ──────────────────────────────────────
            EditorGUILayout.LabelField(
                $"录制时间: {data.recordedAt}  |  时长: {totalDur:F1}s  |  " +
                $"{data.frames.Count} 帧 · {data.events.Count} 事件",
                EditorStyles.miniLabel);

            // 参与者
            if (data.participants.Count > 0)
            {
                EditorGUILayout.LabelField(
                    "参与者: " + string.Join(", ", data.participants),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6);

            // ── 播放控制 ────────────────────────────────────
            EditorGUILayout.BeginHorizontal();

            // 跳转开头
            if (GUILayout.Button("◀◀", GUILayout.Width(36), GUILayout.Height(30)))
            {
                _replayCurrentTime = 0f;
                _replaySelectedEvent = -1;
            }

            // 播放/暂停
            var playIcon = _replayIsPlaying ? "⏸" : "▶";
            GUI.backgroundColor = _replayIsPlaying
                ? EditorSkinPalette.Warning
                : EditorSkinPalette.Success;
            if (GUILayout.Button(playIcon, GUILayout.Width(36), GUILayout.Height(30)))
            {
                _replayIsPlaying = !_replayIsPlaying;
            }
            GUI.backgroundColor = Color.white;

            // 逐帧
            if (GUILayout.Button("⏭", GUILayout.Width(36), GUILayout.Height(30)))
            {
                StepToNextFrame();
            }

            // 倍速切换
            if (GUILayout.Button($"{_replaySpeed}x", GUILayout.Width(36), GUILayout.Height(30)))
            {
                _replaySpeed = _replaySpeed switch
                {
                    0.5f => 1f,
                    1f => 2f,
                    2f => 4f,
                    4f => 0.25f,
                    _ => 1f,
                };
            }

            // 跳转结尾
            if (GUILayout.Button("▶▶", GUILayout.Width(36), GUILayout.Height(30)))
            {
                _replayCurrentTime = totalDur;
            }

            GUILayout.FlexibleSpace();

            // 时间显示
            EditorGUILayout.LabelField(
                $"{_replayCurrentTime:F1}s / {totalDur:F1}s",
                s_timeDisplayStyle,
                GUILayout.Width(120));

            EditorGUILayout.EndHorizontal();

            // ── 时间轴 ──────────────────────────────────────
            var trackRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(24));
            EditorGUI.DrawRect(trackRect, new Color(0.12f, 0.12f, 0.12f));

            // 进度填充
            float progressPct = totalDur > 0 ? _replayCurrentTime / totalDur : 0;
            var progressFill = new Rect(trackRect.x, trackRect.y,
                trackRect.width * progressPct, trackRect.height);
            EditorGUI.DrawRect(progressFill, new Color(0.3f, 0.6f, 0.9f, 0.5f));

            // 帧标记
            foreach (var frame in data.frames)
            {
                if (totalDur <= 0) break;
                float fx = trackRect.x + (frame.timestamp / totalDur) * trackRect.width;
                var fRect = new Rect(fx, trackRect.y + trackRect.height * 0.3f,
                    2, trackRect.height * 0.4f);
                EditorGUI.DrawRect(fRect, new Color(0.5f, 0.8f, 0.5f));
            }

            // 事件标记
            foreach (var evt in data.events)
            {
                if (totalDur <= 0) break;
                float ex = trackRect.x + (evt.timestamp / totalDur) * trackRect.width;
                var eRect = new Rect(ex, trackRect.y + 2, 3, trackRect.height - 4);
                var eColor = evt.eventType switch
                {
                    ReplayEventType.DamageDealt => new Color(0.9f, 0.3f, 0.3f),
                    ReplayEventType.SkillUsed => new Color(0.3f, 0.5f, 0.9f),
                    ReplayEventType.Death => new Color(0.9f, 0.1f, 0.1f),
                    ReplayEventType.Healed => new Color(0.3f, 0.9f, 0.3f),
                    _ => new Color(0.7f, 0.7f, 0.3f),
                };
                EditorGUI.DrawRect(eRect, eColor);
            }

            // 当前游标
            var cursorX = trackRect.x + trackRect.width * progressPct;
            var cursorRect = new Rect(cursorX - 1, trackRect.y, 3, trackRect.height);
            EditorGUI.DrawRect(cursorRect, Color.white);

            // 可拖拽时间轴
            if (Event.current.type == EventType.MouseDown && trackRect.Contains(Event.current.mousePosition))
            {
                _replayCurrentTime = ((Event.current.mousePosition.x - trackRect.x) / trackRect.width) * totalDur;
                _replayCurrentTime = Mathf.Clamp(_replayCurrentTime, 0, totalDur);
                RepaintWindow();
            }

            EditorGUILayout.Space(6);

            // ── 事件列表 ────────────────────────────────────
            _replayFoldoutEvents = EditorGUILayout.Foldout(
                _replayFoldoutEvents, $"事件列表 ({data.events.Count})", true);
            if (_replayFoldoutEvents && data.events.Count > 0)
            {
                _replayEventScroll = EditorGUILayout.BeginScrollView(
                    _replayEventScroll, GUILayout.MaxHeight(180));
                try
                {

                for (int i = 0; i < data.events.Count; i++)
                {
                    var evt = data.events[i];
                    bool isSelected = _replaySelectedEvent == i;

                    var style = isSelected
                        ? new GUIStyle(EditorStyles.helpBox)
                        { normal = { background = MakeColorTex(EditorSkinPalette.Highlight) } }
                        : EditorStyles.helpBox;

                    EditorGUILayout.BeginHorizontal(style);

                    // 时间标签
                    GUI.color = new Color(0.6f, 0.8f, 1f);
                    EditorGUILayout.LabelField($"T+{evt.timestamp:F1}s", GUILayout.Width(70));
                    GUI.color = Color.white;

                    // 类型徽章
                    string typeLabel = evt.eventType switch
                    {
                        ReplayEventType.DamageDealt => "伤害",
                        ReplayEventType.Healed => "治疗",
                        ReplayEventType.SkillUsed => "技能",
                        ReplayEventType.Death => "死亡",
                        ReplayEventType.AIStateChange => "AI",
                        ReplayEventType.Spawn => "生成",
                        ReplayEventType.CombatStart => "开始",
                        ReplayEventType.CombatEnd => "结束",
                        _ => evt.eventType.ToString(),
                    };
                    GUI.backgroundColor = evt.eventType switch
                    {
                        ReplayEventType.DamageDealt => new Color(0.9f, 0.3f, 0.3f),
                        ReplayEventType.Death => new Color(0.7f, 0.1f, 0.1f),
                        ReplayEventType.Healed => new Color(0.3f, 0.7f, 0.3f),
                        _ => new Color(0.4f, 0.4f, 0.4f),
                    };
                    var badgeStyle = s_badgeStyle;
                    GUILayout.Button(typeLabel, badgeStyle, GUILayout.Width(40), GUILayout.Height(18));
                    GUI.backgroundColor = Color.white;

                    EditorGUILayout.LabelField(evt.description, EditorStyles.miniLabel);

                    GUILayout.FlexibleSpace();

                    // 点击跳转
                    if (GUILayout.Button("⏱", GUILayout.Width(20), GUILayout.Height(18)))
                    {
                        _replayCurrentTime = evt.timestamp;
                        _replaySelectedEvent = i;
                    }

                    EditorGUILayout.EndHorizontal();
                }
                }
                finally { EditorGUILayout.EndScrollView(); }
            }

            EditorGUILayout.EndVertical();

            // ── 自动播放驱动 ───────────────────────────────
            if (_replayIsPlaying)
            {
                EditorApplication.delayCall += AdvanceReplayTime;
            }
        }

        void AdvanceReplayTime()
        {
            if (!_replayIsPlaying || _replayData == null) return;

            float dt = 0.016f * _replaySpeed;
            _replayCurrentTime += dt;
            if (_replayCurrentTime >= _replayData.totalDuration)
            {
                _replayCurrentTime = _replayData.totalDuration;
                _replayIsPlaying = false;
            }
            RepaintWindow();
        }

        void StepToNextFrame()
        {
            if (_replayData == null || _replayData.frames.Count == 0) return;
            _replayIsPlaying = false;

            for (int i = 0; i < _replayData.frames.Count; i++)
            {
                if (_replayData.frames[i].timestamp > _replayCurrentTime + 0.01f)
                {
                    _replayCurrentTime = _replayData.frames[i].timestamp;
                    return;
                }
            }
            _replayCurrentTime = _replayData.totalDuration;
        }

        void RepaintWindow()
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var w in windows)
            {
                if (w.GetType().Name.Contains("Combat") || w.GetType().Name.Contains("Sandbox"))
                    w.Repaint();
            }
        }

        Texture2D MakeColorTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
#endif
