#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Game.EditorTools.Shared;
using Game.EditorTools.CombatSandbox;
using Game.Character;

namespace Game.EditorTools.LevelKit
{
    /// <summary>
    /// Level Kit 面板 — 关卡搭建工具主面板。
    ///
    /// 三步 Tab：
    ///   Compose  — Action 调色板（拖拽放置场景交互元素）
    ///   Encounter — 遭遇战编排（波次编辑器 + EncounterData）
    ///   Validate — 关卡校验（场景完整性/正确性检查）
    ///
    /// 设计原则：
    ///   - 复用 Character Kit 打磨成熟的 Sub Tab 按钮模式 + 双列布局
    ///   - 命名空间 Game.EditorTools.LevelKit（与 Character Kit 平级）
    ///   - 颜色方案统一使用 EditorSkinPalette
    /// </summary>
    public class LevelKitPanel
    {
        /// <summary>调色板中的单个 Action 条目。</summary>
        private class ActionEntry
        {
            public string assetPath;      // "Assets/_Game/TestScene/Prefabs/Actions/climbup.prefab"
            public string displayName;    // "climbup"
            public string category;       // "移动交互" / "环境物件"
            public string categoryKey;    // "Actions" / "Environment"（用于分组排序）
            public GameObject prefab;     // 已加载的 Prefab 引用（懒加载）
        }
        private enum Tab { Compose, Encounter, Validate }
        private Tab _tab = Tab.Compose;

        private Vector2 _scroll;

        // ── Compose Tab 状态 ──────────────────────────────────────
        private string _composeSearchFilter = "";
        private List<ActionEntry> _actionPalette = new List<ActionEntry>();
        private bool _paletteScanned = false;
        private ActionEntry _selectedAction = null;   // 当前选中的 Action（用于放置模式）
        private Dictionary<string, bool> _categoryFoldouts = new Dictionary<string, bool>();

        // ── Encounter Tab 状态 ────────────────────────────────────
        private Vector2 _encounterScroll;
        private EncounterData _encounterData;              // 当前编辑的 EncounterData
        private SerializedObject _encounterDataSO;         // 用于 Inspector 属性绑定
        private int _selectedWaveIndex = -1;               // 当前展开的波次索引
        private bool _encounterDirty = false;
        private string _encounterSavePath = "Assets/_Game/";
        private List<GameObject> _enemyPrefabCache;        // 缓存的敌人 Prefab

        // ── Validate Tab 状态 ─────────────────────────────────────
        private Vector2 _validateScroll;
        private bool _validateRan = false;
        private List<Game.EditorTools.Shared.ValidationResult> _validateResults;

        public void OnGUI()
        {
            DrawSetupQuickBar();
            DrawSubTabs();
            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
            switch (_tab)
            {
                case Tab.Compose:   DrawComposeTab();   break;
                case Tab.Encounter: DrawEncounterTab();  break;
                case Tab.Validate:  DrawValidateTab();   break;
            }
            }
            finally { EditorGUILayout.EndScrollView(); }

            // ── 跨 Kit 跳转 (Phase 4.4) ─────────────────────────
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("快速跳转", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            CrossKitBridge.DrawKitJumpButton("Character Kit - 编辑角色", "CharacterKit");
            CrossKitBridge.DrawKitJumpButton("Combat Sandbox - 测试遭遇", "CombatSandbox");
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>绘制顶部场景装配快捷条：打开独立的场景装配工具窗口。</summary>
        void DrawSetupQuickBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("场景装配:", EditorStyles.miniBoldLabel, GUILayout.Width(56));

            if (GUILayout.Button("Ladder Setup", EditorStyles.miniButton, GUILayout.Height(18)))
                EditorApplication.ExecuteMenuItem("Tools/Level Kit/Ladder Setup");
            if (GUILayout.Button("Action Trigger", EditorStyles.miniButton, GUILayout.Height(18)))
                EditorApplication.ExecuteMenuItem("Tools/Level Kit/Action Trigger Setup");
            if (GUILayout.Button("Trap Hazard", EditorStyles.miniButton, GUILayout.Height(18)))
                EditorApplication.ExecuteMenuItem("Tools/Level Kit/Trap Hazard Setup");

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("玩法对象校验", EditorStyles.miniButton, GUILayout.Height(18)))
                EditorApplication.ExecuteMenuItem("Tools/Level Kit/Scene Validator (Gameplay)");

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        /// <summary>绘制顶部子标签栏（搭建 Compose / 遭遇 Encounter / 校验 Validate）。</summary>
        void DrawSubTabs()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(4);
            DrawSubTabButton(Tab.Compose,   "\u2692 搭建 Compose");
            DrawSubTabButton(Tab.Encounter, "\u2694 遭遇 Encounter");
            DrawSubTabButton(Tab.Validate,  "\u2714 校验 Validate");
            EditorGUILayout.Space(4);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>绘制单个子标签按钮，激活时蓝色高亮（与 Character Kit 一致）。</summary>
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
                    if (t == Tab.Validate) _validateRan = false;
                }
            }
            GUI.backgroundColor = prev;
        }

        // ═══════════════════════════════════════════════════════════════
        // Compose Tab — Action 调色板
        // ═══════════════════════════════════════════════════════════════

        void DrawComposeTab()
        {
            EditorGUILayout.LabelField("Action 调色板", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // ── 搜索栏 + 刷新 ──────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜索", GUILayout.Width(40));
            _composeSearchFilter = EditorGUILayout.TextField(_composeSearchFilter);
            if (GUILayout.Button("清除", GUILayout.Width(50)))
                _composeSearchFilter = "";
            if (GUILayout.Button("⟳ 刷新", GUILayout.Width(56)))
                ScanActionPalette();
            EditorGUILayout.EndHorizontal();

            // ── 选中提示 ───────────────────────────────────────────
            if (_selectedAction != null)
            {
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = EditorSkinPalette.Highlight;
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"  ▶ 准备放置: {_selectedAction.displayName}",
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_selectedAction.assetPath, EditorStyles.miniLabel);
                if (GUILayout.Button("✕ 取消", GUILayout.Width(60)))
                    _selectedAction = null;
                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = prevBg;
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space(4);

            // ── 首次自动扫描 ───────────────────────────────────────
            if (!_paletteScanned)
                ScanActionPalette();

            // ── 调色板卡片 ─────────────────────────────────────────
            var filter = _composeSearchFilter.Trim().ToLowerInvariant();
            int totalShown = 0;

            // 按 categoryKey 分组
            var grouped = _actionPalette
                .Where(e => string.IsNullOrEmpty(filter) || e.displayName.ToLowerInvariant().Contains(filter))
                .GroupBy(e => e.categoryKey)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                var catLabel = group.First().category;
                var catKey = group.Key;

                if (!_categoryFoldouts.ContainsKey(catKey))
                    _categoryFoldouts[catKey] = true;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // 折叠标题行
                EditorGUILayout.BeginHorizontal();
                _categoryFoldouts[catKey] = EditorGUILayout.Foldout(_categoryFoldouts[catKey],
                    $"  {catLabel}  ({group.Count()})", true, EditorStyles.foldoutHeader);
                EditorGUILayout.EndHorizontal();

                if (_categoryFoldouts[catKey])
                {
                    foreach (var entry in group.OrderBy(e => e.displayName))
                    {
                        DrawActionCard(entry);
                        totalShown++;
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(
                $"共 {_actionPalette.Count} 个 Prefab" +
                (string.IsNullOrEmpty(filter) ? "" : $"（显示 {totalShown} 个匹配项）"),
                EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "提示：点击「放置」将 Prefab 实例化到场景原点附近。" +
                "也可先在 Scene View 中选中放置位置，然后点击「放置」到选中物体位置。",
                MessageType.None);
        }

        /// <summary>绘制单个 Action 卡片：预览名 + 路径 + 放置按钮</summary>
        void DrawActionCard(ActionEntry entry)
        {
            bool isSelected = _selectedAction == entry;
            var prevBg = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // 图标/名称
            var prevColor = GUI.color;
            GUI.color = EditorSkinPalette.TextPrimary;
            EditorGUILayout.LabelField(entry.displayName, EditorStyles.boldLabel, GUILayout.Width(160));
            GUI.color = EditorSkinPalette.TextTertiary;
            EditorGUILayout.LabelField(
                System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(entry.assetPath)),
                EditorStyles.miniLabel, GUILayout.Width(80));
            GUI.color = prevColor;

            // 选中按钮
            if (isSelected)
            {
                GUI.backgroundColor = EditorSkinPalette.Highlight;
                if (GUILayout.Button("已选中 ✓", GUILayout.Width(80)))
                    _selectedAction = entry;
            }
            else
            {
                GUI.backgroundColor = prevBg;
                if (GUILayout.Button("选中", GUILayout.Width(60)))
                    _selectedAction = entry;
            }

            // 放置按钮
            GUI.backgroundColor = EditorSkinPalette.Success;
            if (GUILayout.Button("放置", GUILayout.Width(50)))
            {
                PlaceActionPrefab(entry);
            }

            GUI.backgroundColor = prevBg;
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>将 Action Prefab 实例化到当前场景中</summary>
        void PlaceActionPrefab(ActionEntry entry)
        {
            if (entry.prefab == null)
                entry.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.assetPath);

            if (entry.prefab == null)
            {
                Debug.LogError($"[LevelKit] 无法加载 Prefab: {entry.assetPath}");
                return;
            }

            // 确定放置位置：优先使用选中物体的位置，否则使用 Scene View 相机焦点
            Vector3 spawnPos = Vector3.zero;
            if (Selection.activeGameObject != null)
            {
                spawnPos = Selection.activeGameObject.transform.position + Vector3.forward * 2f;
            }
            else if (SceneView.lastActiveSceneView != null)
            {
                var cam = SceneView.lastActiveSceneView.camera;
                if (cam != null)
                {
                    // 在相机前方 5 米处放置
                    spawnPos = cam.transform.position + cam.transform.forward * 5f;
                    // 向下投射找到地面
                    if (Physics.Raycast(spawnPos + Vector3.up * 10f, Vector3.down, out var hit, 50f))
                        spawnPos = hit.point;
                }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab);
            instance.transform.position = spawnPos;
            instance.name = entry.prefab.name; // 去掉 "(Clone)" 后缀

            Undo.RegisterCreatedObjectUndo(instance, $"放置 {entry.displayName}");
            Selection.activeGameObject = instance;

            Debug.Log($"[LevelKit] 已放置 {entry.displayName} → 位置 {spawnPos}");
        }

        /// <summary>扫描 Actions 和 Environment 目录，构建调色板条目列表</summary>
        void ScanActionPalette()
        {
            _actionPalette.Clear();
            _paletteScanned = true;

            var scanDirs = new (string dir, string category, string categoryKey)[]
            {
                ("Assets/_Game/TestScene/Prefabs/Actions",     "移动交互", "Actions"),
                ("Assets/_Game/TestScene/Prefabs/Environment",  "环境物件", "Environment"),
            };

            foreach (var (dir, category, categoryKey) in scanDirs)
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;

                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { dir });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);

                    _actionPalette.Add(new ActionEntry
                    {
                        assetPath = path,
                        displayName = name,
                        category = category,
                        categoryKey = categoryKey,
                        prefab = null  // 懒加载
                    });
                }
            }

            Debug.Log($"[LevelKit] 已扫描 {_actionPalette.Count} 个 Action Prefab" +
                      $" (Actions: {_actionPalette.Count(e => e.categoryKey == "Actions")}," +
                      $" Environment: {_actionPalette.Count(e => e.categoryKey == "Environment")})");
        }

        // ═══════════════════════════════════════════════════════════════
        // Encounter Tab — 遭遇战波次编辑器
        // ═══════════════════════════════════════════════════════════════

        void DrawEncounterTab()
        {
            EditorGUILayout.LabelField("遭遇战编排", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // ── 顶部工具栏：加载/新建/保存 ──────────────────────
            DrawEncounterToolbar();
            EditorGUILayout.Space(6);

            // ── 遭遇战概览 ──────────────────────────────────────
            if (_encounterData == null)
            {
                EditorGUILayout.HelpBox(
                    "点击「新建」创建空白 EncounterData，或点击「加载」打开已有资产。\n\n" +
                    "数据资产：EncounterData : ScriptableObject（Wave × EnemySpawnEntry + TriggerCondition）。",
                    MessageType.Info);
                return;
            }

            // ── 遭遇战基本属性 ──────────────────────────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("名称", GUILayout.Width(40));
            var newName = EditorGUILayout.TextField(_encounterData.encounterName);
            if (newName != _encounterData.encounterName)
            {
                _encounterData.encounterName = newName;
                _encounterDirty = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField($"波次: {_encounterData.WaveCount}  |  总敌人: {_encounterData.TotalEnemyCount}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            // ── 波次列表 ────────────────────────────────────────
            _encounterScroll = EditorGUILayout.BeginScrollView(_encounterScroll);
            try
            {

            EditorGUILayout.LabelField("波次列表", EditorStyles.miniBoldLabel);
            for (int i = 0; i < _encounterData.waves.Count; i++)
            {
                DrawWaveCard(_encounterData.waves[i], i);
                EditorGUILayout.Space(2);
            }

            // ── 添加波次按钮 ────────────────────────────────────
            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ 添加波次", GUILayout.Height(30)))
                AddWave();

            }
            finally { EditorGUILayout.EndScrollView(); }

            EditorGUILayout.Space(8);

            // ── 保存提示 ────────────────────────────────────────
            if (_encounterDirty)
            {
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = EditorSkinPalette.Warning;
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField("⚠ 有未保存的修改", GUILayout.Width(140));
                if (GUILayout.Button("立即保存", GUILayout.Width(80)))
                    SaveEncounterData();
                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = prevBg;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "提示：编排完成后点击「保存」写入 .asset 文件，运行时由 EncounterManager 读取。",
                MessageType.None);
        }

        /// <summary>顶部工具栏：新建/加载/保存 EncounterData。</summary>
        void DrawEncounterToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建", GUILayout.Width(60), GUILayout.Height(26)))
                NewEncounterData();
            if (GUILayout.Button("加载...", GUILayout.Width(60), GUILayout.Height(26)))
                LoadEncounterData();
            if (_encounterData != null)
            {
                GUI.backgroundColor = EditorSkinPalette.Success;
                if (GUILayout.Button("保存", GUILayout.Width(60), GUILayout.Height(26)))
                    SaveEncounterData();
                GUI.backgroundColor = Color.white;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>创建空白 EncounterData。</summary>
        void NewEncounterData()
        {
            _encounterData = EncounterData.CreateDefault();
            _encounterDataSO = new SerializedObject(_encounterData);
            _selectedWaveIndex = 0;
            _encounterDirty = true;
            _encounterSavePath = "Assets/_Game/";
        }

        /// <summary>从磁盘加载已有 EncounterData。</summary>
        void LoadEncounterData()
        {
            var path = EditorUtility.OpenFilePanel("选择 EncounterData", "Assets/_Game/", "asset");
            if (string.IsNullOrEmpty(path)) return;

            // 转成相对路径
            var dataPath = Application.dataPath;
            if (path.StartsWith(dataPath))
            {
                path = "Assets" + path.Substring(dataPath.Length);
                var asset = AssetDatabase.LoadAssetAtPath<EncounterData>(path);
                if (asset != null)
                {
                    _encounterData = asset;
                    _encounterDataSO = new SerializedObject(_encounterData);
                    _selectedWaveIndex = _encounterData.waves.Count > 0 ? 0 : -1;
                    _encounterDirty = false;
                    _encounterSavePath = System.IO.Path.GetDirectoryName(path);
                    Debug.Log($"[LevelKit] 已加载 EncounterData: {path}");
                    return;
                }
            }
            Debug.LogWarning($"[LevelKit] 无法加载 EncounterData: {path}");
        }

        /// <summary>保存 EncounterData 到磁盘。</summary>
        void SaveEncounterData()
        {
            if (_encounterData == null) return;

            string assetPath;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(_encounterData)))
            {
                // 新资产：弹出保存对话框
                var savePath = EditorUtility.SaveFilePanelInProject(
                    "保存 EncounterData", _encounterData.encounterName, "asset",
                    "选择保存位置", _encounterSavePath);
                if (string.IsNullOrEmpty(savePath)) return;

                AssetDatabase.CreateAsset(_encounterData, savePath);
                assetPath = savePath;
                _encounterSavePath = System.IO.Path.GetDirectoryName(savePath);
            }
            else
            {
                assetPath = AssetDatabase.GetAssetPath(_encounterData);
            }

            EditorUtility.SetDirty(_encounterData);
            AssetDatabase.SaveAssets();
            _encounterDirty = false;

            Debug.Log($"[LevelKit] EncounterData 已保存: {assetPath}");
            EditorGUIUtility.PingObject(_encounterData);
        }

        /// <summary>添加新波次。</summary>
        void AddWave()
        {
            if (_encounterData == null) return;
            var wave = new WaveData
            {
                waveName = $"Wave {_encounterData.waves.Count + 1}",
                delayBeforeSpawn = 2f,
                triggerCondition = TriggerConditionType.PreviousWaveCleared,
                entries = new List<EnemySpawnEntry>
                {
                    new EnemySpawnEntry { count = 1, spawnInterval = 0.5f, positionMode = SpawnPositionMode.SpawnPoint }
                }
            };
            _encounterData.waves.Add(wave);
            _selectedWaveIndex = _encounterData.waves.Count - 1;
            _encounterDirty = true;
        }

        void RemoveWave(int index)
        {
            if (_encounterData == null || index < 0 || index >= _encounterData.waves.Count) return;
            _encounterData.waves.RemoveAt(index);
            if (_selectedWaveIndex >= _encounterData.waves.Count)
                _selectedWaveIndex = _encounterData.waves.Count - 1;
            _encounterDirty = true;
        }

        void MoveWave(int from, int to)
        {
            if (_encounterData == null || from < 0 || from >= _encounterData.waves.Count ||
                to < 0 || to >= _encounterData.waves.Count) return;
            var wave = _encounterData.waves[from];
            _encounterData.waves.RemoveAt(from);
            _encounterData.waves.Insert(to, wave);
            _selectedWaveIndex = to;
            _encounterDirty = true;
        }

        void AddSpawnEntry(int waveIdx)
        {
            if (_encounterData == null || waveIdx < 0 || waveIdx >= _encounterData.waves.Count) return;
            _encounterData.waves[waveIdx].entries.Add(new EnemySpawnEntry
            {
                count = 1,
                spawnInterval = 0.5f,
                positionMode = SpawnPositionMode.SpawnPoint,
            });
            _encounterDirty = true;
        }

        void RemoveSpawnEntry(int waveIdx, int entryIdx)
        {
            if (_encounterData == null || waveIdx < 0 || waveIdx >= _encounterData.waves.Count) return;
            var entries = _encounterData.waves[waveIdx].entries;
            if (entryIdx < 0 || entryIdx >= entries.Count) return;
            entries.RemoveAt(entryIdx);
            _encounterDirty = true;
        }

        /// <summary>绘制单张波次卡片（可折叠）。</summary>
        void DrawWaveCard(WaveData wave, int index)
        {
            bool isExpanded = _selectedWaveIndex == index;
            var prevBg = GUI.backgroundColor;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── 标题行 ──────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            // 折叠箭头
            string foldIcon = isExpanded ? "▼" : "▶";
            if (GUILayout.Button($"{foldIcon} {wave.waveName}", EditorStyles.boldLabel))
                _selectedWaveIndex = isExpanded ? -1 : index;

            GUILayout.FlexibleSpace();

            // 敌人数量徽章
            int enemyCount = wave.entries.Sum(e => e.count);
            EditorGUILayout.LabelField($"×{enemyCount}", EditorStyles.miniLabel, GUILayout.Width(30));

            // 上移 / 下移 / 删除
            GUI.enabled = index > 0;
            if (GUILayout.Button("▲", GUILayout.Width(24)))
                MoveWave(index, index - 1);
            GUI.enabled = index < _encounterData.waves.Count - 1;
            if (GUILayout.Button("▼", GUILayout.Width(24)))
                MoveWave(index, index + 1);
            GUI.enabled = true;

            GUI.backgroundColor = EditorSkinPalette.Danger;
            if (GUILayout.Button("✕", GUILayout.Width(24)))
                RemoveWave(index);
            GUI.backgroundColor = prevBg;

            EditorGUILayout.EndHorizontal();

            if (!isExpanded)
            {
                // 收起时显示摘要
                EditorGUILayout.LabelField(
                    $"  触发: {GetTriggerConditionLabel(wave.triggerCondition, wave.triggerParam)}  |  " +
                    $"开始延迟: {wave.delayBeforeSpawn}s  |  " +
                    $"{wave.entries.Count} 组敌人",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space(4);

            // ── 波次属性编辑 ────────────────────────────────────
            EditorGUI.indentLevel++;
            // 名称
            wave.waveName = EditorGUILayout.TextField("名称", wave.waveName);
            // 延迟
            wave.delayBeforeSpawn = EditorGUILayout.FloatField("开始前延迟 (s)", wave.delayBeforeSpawn);
            // 触发条件
            wave.triggerCondition = (TriggerConditionType)EditorGUILayout.EnumPopup("触发条件", wave.triggerCondition);
            if (wave.triggerCondition == TriggerConditionType.OnTimer)
            {
                wave.triggerParam = EditorGUILayout.FloatField("  → 秒数", wave.triggerParam);
            }
            else if (wave.triggerCondition == TriggerConditionType.OnObjective)
            {
                EditorGUILayout.LabelField($"  → 目标 ID: {wave.triggerParam:F0}");
            }

            EditorGUILayout.Space(4);

            // ── 敌人出生条目 ─────────────────────────────────────
            EditorGUILayout.LabelField("敌人出生列表", EditorStyles.miniBoldLabel);

            for (int ei = 0; ei < wave.entries.Count; ei++)
            {
                DrawSpawnEntryEditor(wave.entries[ei], index, ei);
            }

            if (GUILayout.Button("+ 添加敌人生成组", GUILayout.Height(24)))
                AddSpawnEntry(index);

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        /// <summary>绘制单条敌人生成条目的编辑器。</summary>
        void DrawSpawnEntryEditor(EnemySpawnEntry entry, int waveIdx, int entryIdx)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"生成组 {entryIdx + 1}", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", GUILayout.Width(22)))
            {
                RemoveSpawnEntry(waveIdx, entryIdx);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            // Prefab 选择
            entry.enemyPrefab = (GameObject)EditorGUILayout.ObjectField(
                "敌人 Prefab", entry.enemyPrefab, typeof(GameObject), false);

            EditorGUILayout.BeginHorizontal();
            entry.count = EditorGUILayout.IntField("数量", entry.count);
            if (entry.count < 1) entry.count = 1;
            entry.spawnInterval = EditorGUILayout.FloatField("间隔 (s)", entry.spawnInterval);
            if (entry.spawnInterval < 0f) entry.spawnInterval = 0f;
            EditorGUILayout.EndHorizontal();

            entry.positionMode = (SpawnPositionMode)EditorGUILayout.EnumPopup("出生模式", entry.positionMode);

            EditorGUILayout.EndVertical();
        }

        /// <summary>返回触发条件的可读文本。</summary>
        static string GetTriggerConditionLabel(TriggerConditionType type, float param)
        {
            return type switch
            {
                TriggerConditionType.OnTriggerEnter      => "进入区域",
                TriggerConditionType.PreviousWaveCleared  => "前置波次清完",
                TriggerConditionType.OnTimer             => $"开始后 {param}s",
                TriggerConditionType.OnObjective         => $"目标 {param:F0}",
                _ => type.ToString(),
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // Validate Tab — 关卡完整性校验（基于 AssetValidator）
        // ═══════════════════════════════════════════════════════════════

        void DrawValidateTab()
        {
            EditorGUILayout.LabelField("关卡校验", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                "一键检查关卡场景的完整性和正确性。\n" +
                "涵盖：必需对象、NavMesh、碰撞、资产引用、性能指标。",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // ── 运行校验按钮 ────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = EditorSkinPalette.Success;
            if (GUILayout.Button("▶ 运行全部校验", GUILayout.Height(34), GUILayout.Width(150)))
            {
                _validateResults = AssetValidator.RunAllSceneChecks();
                _validateRan = true;
            }
            GUI.backgroundColor = prevBg;
            if (GUILayout.Button("仅检查快速项", GUILayout.Height(34), GUILayout.Width(150)))
            {
                _validateResults = RunQuickChecks();
                _validateRan = true;
            }
            if (_validateRan && GUILayout.Button("清除结果", GUILayout.Height(34), GUILayout.Width(80)))
            {
                _validateRan = false;
                _validateResults = null;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            if (!_validateRan || _validateResults == null)
            {
                DrawValidateChecklistPreview();
            }
            else
            {
                DrawValidateResults();
            }
        }

        /// <summary>仅运行快速检查项（跳过三角面数等耗时计算）。</summary>
        List<Game.EditorTools.Shared.ValidationResult> RunQuickChecks()
        {
            var results = new List<Game.EditorTools.Shared.ValidationResult>();
            results.Add(AssetValidator.CheckMainCamera());
            results.Add(AssetValidator.CheckRequiredObject("PlayerSpawn"));
            results.Add(AssetValidator.CheckNavMeshBaked());

            var grounds = GameObject.FindGameObjectsWithTag("Ground");
            if (grounds.Length == 0)
            {
                results.Add(new Game.EditorTools.Shared.ValidationResult
                {
                    category = "碰撞",
                    description = "地面 (Tag=Ground) 是否存在",
                    severity = Game.EditorTools.Shared.ValidationSeverity.Error,
                    passed = false,
                    fixHint = "请确保场景中存在 Tag=Ground 的地面对象",
                });
            }
            else
            {
                results.Add(AssetValidator.CheckColliderPresent(grounds[0], "Ground"));
            }

            return results;
        }

        void DrawValidateChecklistPreview()
        {
            EditorGUILayout.LabelField("检查项（点击上方按钮执行）", EditorStyles.miniBoldLabel);

            _validateScroll = EditorGUILayout.BeginScrollView(_validateScroll, GUILayout.Height(260));
            try
            {

            DrawCheckItem("必需对象", "PlayerSpawn 点是否存在", "error");
            DrawCheckItem("必需对象", "主摄像机是否存在", "error");
            DrawCheckItem("NavMesh", "NavMeshSurface 是否已烘焙", "error");
            DrawCheckItem("碰撞", "地面是否有 Collider", "error");
            DrawCheckItem("性能", "场景三角面数（完整检查）", "info");
            DrawCheckItem("性能", "Light 数量（完整检查）", "info");

            }
            finally { EditorGUILayout.EndScrollView(); }
        }

        void DrawCheckItem(string category, string description, string severity)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"  ○ [{category}] {description}", GUILayout.Width(380));
            var prevColor = GUI.color;
            GUI.color = severity switch
            {
                "error"   => new Color(0.9f, 0.3f, 0.3f),
                "warning" => new Color(0.9f, 0.7f, 0.2f),
                _         => new Color(0.5f, 0.5f, 0.5f),
            };
            EditorGUILayout.LabelField(severity switch { "error" => "Error", "warning" => "Warning", _ => "Info" },
                EditorStyles.miniLabel, GUILayout.Width(60));
            GUI.color = prevColor;
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>显示真实校验结果（由 AssetValidator 生成）。</summary>
        void DrawValidateResults()
        {
            EditorGUILayout.LabelField("校验结果", EditorStyles.miniBoldLabel);

            var green  = new Color(0.4f, 0.85f, 0.4f);
            var yellow = new Color(0.9f, 0.7f, 0.2f);
            var red    = new Color(0.9f, 0.3f, 0.3f);

            _validateScroll = EditorGUILayout.BeginScrollView(_validateScroll, GUILayout.Height(260));
            try
            {

            foreach (var result in _validateResults)
            {
                string icon = result.passed ? "✔" : "✘";
                Color color = result.passed ? green
                    : result.severity == Game.EditorTools.Shared.ValidationSeverity.Error ? red : yellow;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var prev = GUI.color;
                GUI.color = color;
                EditorGUILayout.LabelField($"  {icon}  [{result.category}] {result.description}");
                GUI.color = prev;

                if (!result.passed && !string.IsNullOrEmpty(result.fixHint))
                {
                    GUI.color = EditorSkinPalette.TextTertiary;
                    EditorGUILayout.LabelField($"      💡 {result.fixHint}", EditorStyles.miniLabel);
                    GUI.color = prev;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(1);
            }

            }
            finally { EditorGUILayout.EndScrollView(); }

            EditorGUILayout.Space(4);

            // ── 汇总 + 操作 ────────────────────────────────────
            var (passed, failed, warnings) = AssetValidator.Summarize(_validateResults);
            int total = _validateResults.Count;

            EditorGUILayout.BeginHorizontal();
            var sumStyle = new GUIStyle(EditorStyles.miniLabel);
            sumStyle.normal.textColor = failed > 0 ? red : green;
            EditorGUILayout.LabelField(
                $"通过: {passed}/{total}  |  警告: {warnings}  |  错误: {failed}",
                sumStyle);
            GUILayout.FlexibleSpace();

            GUI.enabled = failed == 0 && warnings == 0;
            // "一键修复" 占位（未来接入自动修复逻辑）
            if (GUILayout.Button("一键修复", GUILayout.Width(80)))
            {
                Debug.Log("[LevelKit] 一键修复功能尚未实现，当前仅供 UI 预览。");
            }
            GUI.enabled = true;

            if (GUILayout.Button("导出报告", GUILayout.Width(80)))
            {
                ExportValidationReport();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>将校验结果导出为 Markdown 报告文件。</summary>
        void ExportValidationReport()
        {
            if (_validateResults == null || _validateResults.Count == 0) return;

            var savePath = EditorUtility.SaveFilePanel(
                "导出校验报告", Application.dataPath, "LevelValidationReport.md", "md");
            if (string.IsNullOrEmpty(savePath)) return;

            var (passed, failed, warnings) = AssetValidator.Summarize(_validateResults);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# 关卡校验报告");
            sb.AppendLine();
            sb.AppendLine($"- 生成时间: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- 场景: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            sb.AppendLine($"- 总计: {_validateResults.Count} 项 | 通过: {passed} | 警告: {warnings} | 错误: {failed}");
            sb.AppendLine();
            sb.AppendLine("## 详细结果");
            sb.AppendLine();
            sb.AppendLine("| 状态 | 类别 | 描述 | 修复建议 |");
            sb.AppendLine("|------|------|------|----------|");

            foreach (var r in _validateResults)
            {
                string status = r.passed ? "✅" : r.severity == Game.EditorTools.Shared.ValidationSeverity.Error ? "❌" : "⚠️";
                sb.AppendLine($"| {status} | {r.category} | {r.description} | {r.fixHint ?? "-"} |");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"*由 Level Kit Validate Tab 自动生成*");

            System.IO.File.WriteAllText(savePath, sb.ToString());
            EditorUtility.RevealInFinder(savePath);
            Debug.Log($"[LevelKit] 校验报告已导出: {savePath}");
        }
    }
}
#endif
