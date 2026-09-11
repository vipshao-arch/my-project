#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Game.Character;
using Game.SkillSystem;
using Game.SkillSystem.EditorTools;
using Game.EditorTools.Shared;
using Game.EditorTools.CombatSandbox;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.Character.EditorTools.CharacterKit
{
    public partial class CharacterKitPanel
    {
        // ═══════════════════════════════════════════════════════════════
        // Setup Tab — 双列布局：左侧流水线步骤列表 + 右侧选中步骤的独立子面板
        //
        // 支持 Character / Enemy 两种目标类型切换，流水线步骤列表随之变化。
        // 每步点击后右侧展示对应的配置或操作面板。
        // 分隔条可拖拽调整左右比例。
        // ═══════════════════════════════════════════════════════════════
        /// <summary>绘制装配 Setup Tab 主布局（流水线步骤列表 + 独立子面板）。</summary>
        void DrawSetupTab()
        {
            EditorGUILayout.LabelField("角色装配流水线", EditorStyles.boldLabel);

            // ── 顶部：装配目标选择 ────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField("装配目标", EditorStyles.miniLabel, GUILayout.Width(70));
                var prevTarget = _setupTarget;
                _setupTarget = (SetupPipeline.PipelineTarget)EditorGUILayout.EnumPopup(_setupTarget);
                if (_setupTarget != prevTarget)
                {
                    _pipelineBuilt = false;
                    _pipeline = null;
                    _selectedStepIndex = -1;

                    // ── 重置目标专有字段（切换 Character ↔ Enemy 时）─────
                    if (_setupTarget == SetupPipeline.PipelineTarget.Character)
                    {
                        // 切到 Character：清空 Enemy 专有数据
                        _enemyTarget = null;
                        _enemyAI = null;
                        _enemyEditArch = null;
                        _enemyEditArchSO = null;
                        _enemyTemplateArch = null;
                        _enemyConfigDirty = false;
                        _enemyResourceFolder = null;

                        // Enemy Setup 专有数据
                        _enemySetupSourceController = null;
                        _enemySetupFolder = null;
                        _enemySetupIdleClip = null;
                        _enemySetupWalkClip = null;
                        _enemySetupHitClip = null;
                        _enemySetupDeathClip = null;
                        _enemySetupAttackClips = new AnimationClip[4];
                        _enemySetupAttackCount = 1;
                        _enemySetupAnimSet = null;
                        _enemySetupFolderPath = null;
                        _enemySetupLastScannedFolder = null;
                        _enemySetupName = null;
                        _enemySetupPrefix = null;
                        _enemySetupModelFBXPath = null;
                        _enemySetupControllerPath = null;
                        _enemySetupPrefabPath = null;
                        _enemySetupLogMessages.Clear();
                    }
                    else // Enemy
                    {
                        // 切到 Enemy：清空 Character 专有数据
                        _playerTarget = null;
                        _playerMotor = null;
                        _playerTemplateConfig = null;
                        _playerEditConfig = null;
                        _playerEditConfigSO = null;
                        _playerConfigDirty = false;
                        _playerResourceFolder = null;

                        // Character Setup 专有数据
                        _charSetupSourceController = null;
                        _charSetupFolder = null;
                        _charSetupIdleClip = null;
                        _charSetupRunClip = null;
                        _charSetupRunFastClip = null;
                        _charSetupFolderPath = null;
                        _charSetupName = null;
                        _charSetupPrefix = null;
                        _charSetupModelFBXPath = null;
                        _charSetupControllerPath = null;
                        _charSetupOverridePath = null;
                        _charSetupPrefabPath = null;
                        _charSetupAnimSetPath = null;
                        _charSetupLoadedFolderPath = null;
                        _charSetupTemplateAnimSet = null;
                        _charSetupEditAnimSet = null;
                        _charSetupAnimDirty = false;
                        _charSetupLogMessages.Clear();
                    }

                    // ── 共享步骤字段保留不清空 ──
                    // Ragdoll: _ragdollTarget, _ragdollLogMessages, _ragdollLastSuccess
                    // SpringBone: _springBoneTarget, _springBoneChains, _springBoneColliders, etc.
                    // Weapon: _weaponTarget, _weaponSwitcher, _weaponHolder, etc.

                    // Diagnostic 切换目标时清空诊断结果
                    _diagLogMessages.Clear();
                    _diagLastSuccess = false;
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            EditorGUILayout.Space(6);

            // ── 构建/重建流水线（含 callback 注入）
            // 所有事件类型中一致执行 Build + LoadPipelineState，
            // 确保 Layout 和 Repaint 事件间管线结构和步骤状态完全一致。
            if (!_pipelineBuilt || _pipeline == null)
            {
                _pipeline = new SetupPipeline();
                _pipeline.Build(_setupTarget);
                RestorePipelineTargets(); // 先恢复目标，再按目标身份读取步骤状态
                LoadPipelineState();
                InjectConfigCallbacks();
                _pipeline.onStepChanged += OnPipelineStepChanged;
                _pipelineBuilt = true;
                _needsPipelineStateRefresh = true;

                // 首次打开默认选中第一步
                if (_selectedStepIndex < 0)
                    _selectedStepIndex = 0;
            }

            if (_needsPipelineStateRefresh)
            {
                RefreshPipelineState();
                _needsPipelineStateRefresh = false;
            }
            else if (Event.current.type == EventType.Repaint)
            {
                // 资产可能由维护菜单、另一个面板或 Unity 导入器刚刚写入；
                // 不应等到重开窗口才更新绿色状态。
                RefreshPipelineState();
            }

            EditorGUILayout.Space(8);

            // ── 双列布局：左=流水线步骤列表，右=选中步骤的独立面板 ──
            // 分隔条可拖拽调整左右比例
            EditorGUILayout.BeginHorizontal();
            try
            {
                float splitterWidth = 6f;
                float minLeftWidth = 150f;
                float maxLeftRatio = 0.6f;

                // ── 左侧：流水线步骤列表 ─────────────────────────────────
                float leftWidth = _splitterPos;
                EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
                try
                {
                    EditorGUILayout.LabelField("流水线步骤（点击打开）", EditorStyles.boldLabel);
                    var leftScrollVec = EditorGUILayout.BeginScrollView(_setupScroll, GUILayout.ExpandHeight(true));
                    try
                    {
                        _setupScroll = leftScrollVec;
                        if (_pipeline != null)
                        {
                            for (int i = 0; i < _pipeline.steps.Count; i++)
                            {
                                var state = _pipeline.steps[i];
                                DrawPipelineStepButton(state, i);
                            }
                        }
                    }
                    finally
                    {
                        SafeEndLayout(() => EditorGUILayout.EndScrollView());
                    }

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(_pipeline != null ? _pipeline.GetSummary() : "(管线未初始化)", EditorStyles.miniLabel);
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndVertical());
                }

                // ── 可拖拽分隔条 ─────────────────────────────────────────
                EditorGUILayout.BeginVertical(GUILayout.Width(splitterWidth), GUILayout.ExpandHeight(true));
                try
                {
                    Rect splitterRect = GUILayoutUtility.GetRect(splitterWidth, splitterWidth, GUILayout.ExpandHeight(true));
                    EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
                    HandleSplitterDrag(splitterRect, minLeftWidth, maxLeftRatio);
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndVertical());
                }

                // ── 右侧：选中步骤的独立面板 ─────────────────────────────
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
                try
                {
                    DrawStepDetailPanel();
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndVertical());
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            // 同步暂存值到 _splitterPos（所有事件类型都同步，避免 Layout/Repaint 事件间布局参数不一致）
            // 关键：必须在 Layout 事件中也同步，否则下一帧的 Layout 会用旧值而 Repaint 用新值
            if (_pendingSplitterPos >= 0f)
            {
                _splitterPos = _pendingSplitterPos;
                _pendingSplitterPos = -1f;
            }

            // ── 持久化当前流水线状态（P3.1: 仅在 Repaint 事件中写入，减少 EditorPrefs IO）─
            if (Event.current.type == EventType.Repaint)
                SavePipelineState();
        }

        /// <summary>处理分隔条拖拽，动态调整左右面板宽度比例。</summary>
        /// <remarks>
        /// 拖拽中不直接修改 _splitterPos，而是写入 _pendingSplitterPos，
        /// 由 DrawSetupTab 在所有事件结束时统一同步。这避免了 Layout/Repaint 事件间
        /// 布局参数不一致导致的 EndLayoutGroup 报错。
        /// </remarks>
        void HandleSplitterDrag(Rect splitterRect, float minLeftWidth, float maxLeftRatio)
        {
            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        _isDraggingSplitter = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_isDraggingSplitter && GUIUtility.hotControl == controlId)
                    {
                        float totalWidth = EditorGUIUtility.currentViewWidth;
                        float maxLeftWidth = totalWidth * maxLeftRatio;
                        _pendingSplitterPos = Mathf.Clamp(e.mousePosition.x, minLeftWidth, maxLeftWidth);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (_isDraggingSplitter && GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        _isDraggingSplitter = false;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    // 绘制分隔条视觉样式
                    float x = splitterRect.x + splitterRect.width * 0.25f;
                    float y = splitterRect.y;
                    float w = splitterRect.width * 0.5f;
                    float h = splitterRect.height;
                    Color col = _isDraggingSplitter
                        ? new Color(0.4f, 0.55f, 0.85f, 0.8f)
                        : new Color(0.35f, 0.35f, 0.35f, 0.5f);
                    EditorGUI.DrawRect(new Rect(x, y, w, h), col);
                    break;
            }
        }

        static void PersistPlayerConfigCompletion(string prefabPath)
        {
            if (!IsValidPrefabPath(prefabPath)) return;
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(guid)) return;
            EditorPrefs.SetString(
                $"CharacterKit_Pipeline_{SetupPipeline.PipelineTarget.Character}_{guid}_{SetupPipeline.StepType.PlayerConfig}",
                SetupPipeline.StepStatus.Completed.ToString());
        }

        bool IsPlayerConfigReady()
        {
            var target = ResolveCharacterPrefabPath();
            var prefab = IsValidPrefabPath(target)
                ? AssetDatabase.LoadAssetAtPath<GameObject>(target)
                : null;
            var motor = prefab != null
                ? prefab.GetComponentInChildren<CharacterMotor>(true)
                : _playerMotor;
            if (motor == null) return false;
            var so = new SerializedObject(motor);
            var prop = so.FindProperty("configData");
            return prop != null && prop.objectReferenceValue != null;
        }

        static bool HasEnemyBehaviorData(EnemyAI ai)
        {
            if (ai == null) return false;
            var so = new SerializedObject(ai);
            var prop = so.FindProperty("behaviorData");
            return prop != null && prop.objectReferenceValue != null;
        }

        /// <summary>检查路径是否指向有效 Prefab。</summary>
        static bool IsValidPrefabPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        }

        /// <summary>在指定目录中查找包含目标组件的 Prefab。</summary>
        static string FindPrefabPathWithComponent<T>(string folder) where T : Component
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponent<T>() != null)
                    return path;
            }
            return null;
        }

        /// <summary>解析当前主角 Prefab；优先使用当前 Setup 目录，避免回退到其他角色。</summary>
        string ResolveCharacterPrefabPath()
        {
            if (IsValidPrefabPath(_charSetupPrefabPath)) return _charSetupPrefabPath;
            if (_playerTarget != null)
            {
                string selectedPath = AssetDatabase.GetAssetPath(_playerTarget);
                if (IsValidPrefabPath(selectedPath)) return selectedPath;
            }
            string folder = _charSetupFolderPath;
            if (string.IsNullOrEmpty(folder) && _charSetupFolder != null)
                folder = AssetDatabase.GetAssetPath(_charSetupFolder);
            string inFolder = FindPrefabPathWithComponent<CharacterMotor>(folder);
            if (IsValidPrefabPath(inFolder)) return inFolder;
            string configured = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "");
            return IsValidPrefabPath(configured) ? configured : null;
        }

        /// <summary>解析当前敌人 Prefab；优先使用当前 Setup 目录。</summary>
        string ResolveEnemyPrefabPath()
        {
            if (IsValidPrefabPath(_enemySetupPrefabPath)) return _enemySetupPrefabPath;
            if (_enemyTarget != null)
            {
                string selectedPath = AssetDatabase.GetAssetPath(_enemyTarget);
                if (IsValidPrefabPath(selectedPath)) return selectedPath;
            }
            string folder = _enemySetupFolderPath;
            if (string.IsNullOrEmpty(folder) && _enemySetupFolder != null)
                folder = AssetDatabase.GetAssetPath(_enemySetupFolder);
            return FindPrefabPathWithComponent<EnemyAI>(folder);
        }

        /// <summary>从会话目录和当前玩家入口恢复流水线目标对象。</summary>
        void RestorePipelineTargets()
        {
            if (_setupTarget == SetupPipeline.PipelineTarget.Character)
            {
                string path = ResolveCharacterPrefabPath();
                var prefab = IsValidPrefabPath(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                if (prefab != null)
                {
                    _charSetupPrefabPath = path;
                    _playerTarget = prefab;
                    _playerMotor = prefab.GetComponentInChildren<CharacterMotor>(true);
                    TryPropagateTargetToSharedSteps(prefab);
                }
            }
            else
            {
                string path = ResolveEnemyPrefabPath();
                var prefab = IsValidPrefabPath(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                if (prefab != null)
                {
                    _enemySetupPrefabPath = path;
                    _enemyTarget = prefab;
                    _enemyAI = prefab.GetComponent<EnemyAI>();
                    TryPropagateTargetToSharedSteps(prefab);
                }
            }
        }

        /// <summary>按当前目标 Prefab GUID 隔离流水线状态，避免不同角色共用同一组绿色标记。</summary>
        string GetPipelineStatePrefix()
        {
            string path = _setupTarget == SetupPipeline.PipelineTarget.Character
                ? ResolveCharacterPrefabPath() : ResolveEnemyPrefabPath();
            string guid = IsValidPrefabPath(path) ? AssetDatabase.AssetPathToGUID(path) : "NoTarget";
            return $"CharacterKit_Pipeline_{_setupTarget}_{guid}_";
        }

        /// <summary>
        /// 刷新流水线步骤状态 — 根据当前目标 Prefab 的实际组件/资产状态推断完成情况。
        /// 调用时机：首次构建流水线、目标变更、每次打开 Setup Tab。
        /// </summary>
        void RefreshPipelineState()
        {
            if (_pipeline == null) return;
            RestorePipelineTargets();

            foreach (var s in _pipeline.steps)
            {
                // 重新检测所有步骤状态（包括已完成步骤，确保目标变更后状态正确）
                bool detected = s.step.type switch
                {
                    SetupPipeline.StepType.PlayerConfig =>
                        IsPlayerConfigReady(),

                    SetupPipeline.StepType.EnemyConfig =>
                        _enemyAI != null && (_enemyAI.archetype != null || HasEnemyBehaviorData(_enemyAI)),

                    SetupPipeline.StepType.CharacterSetup =>
                        _playerTarget != null &&
                        FindCharacterAnimator(_playerTarget) != null &&
                        FindCharacterAnimator(_playerTarget)?.runtimeAnimatorController != null &&
                        FindCharacterAnimator(_playerTarget)?.avatar != null,

                    SetupPipeline.StepType.OverrideSetup =>
                        IsCharacterOverrideSetupReady(),

                    SetupPipeline.StepType.NetworkSetup =>
                        IsCharacterNetworkSetupReady(),

                    SetupPipeline.StepType.EnemySetup =>
                        _enemyTarget != null &&
                        _enemyTarget.GetComponent<Animator>() != null &&
                        _enemyTarget.GetComponent<UnityEngine.AI.NavMeshAgent>() != null,

                    SetupPipeline.StepType.RagdollSetup =>
                        _ragdollLastSuccess || CheckRagdollPresent(_setupTarget == SetupPipeline.PipelineTarget.Character
                            ? _playerTarget : _enemyTarget),

                    SetupPipeline.StepType.SpringBoneSetup =>
                        CheckSpringBonePresent(_setupTarget == SetupPipeline.PipelineTarget.Character
                            ? _playerTarget : _enemyTarget),

                    SetupPipeline.StepType.WeaponSetup =>
                        _weaponTarget != null && _weaponTarget.GetComponent<WeaponSwitcher>() != null,

                    // 诊断没有可由资产完整推导的唯一完成条件，保留当前目标的手动完成标记。
                    SetupPipeline.StepType.Diagnostic => s.status == SetupPipeline.StepStatus.Completed || _diagLastSuccess,

                    _ => false
                };

                if (detected)
                {
                    s.status = SetupPipeline.StepStatus.Completed;
                    s.message = "(检测到已完成)";
                }
                else if (s.status == SetupPipeline.StepStatus.Completed)
                {
                    // 之前标记 Complete 但现在检测不到 → 可能更换了目标，重置为 Pending
                    s.status = SetupPipeline.StepStatus.Pending;
                    s.message = "";
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 流水线状态持久化（EditorPrefs）
        // 解决的问题：步骤完成标记（✔/✘）在窗口关闭/重开后不会丢失。
        // Key 格式：CharacterKit_Pipeline_{Character/Enemy}_{PrefabGUID}_{StepType}
        // ═══════════════════════════════════════════════════════════════

        /// <summary>从 EditorPrefs 恢复上次会话的流水线步骤状态。</summary>
        void LoadPipelineState()
        {
            if (_pipeline == null) return;
            string prefix = GetPipelineStatePrefix();
            foreach (var s in _pipeline.steps)
            {
                string key = prefix + s.step.type;
                if (EditorPrefs.HasKey(key)
                    && System.Enum.TryParse<SetupPipeline.StepStatus>(EditorPrefs.GetString(key), out var savedStatus))
                {
                    s.status = savedStatus;
                }
            }
        }

        /// <summary>将当前流水线步骤状态写入 EditorPrefs。</summary>
        void SavePipelineState()
        {
            if (_pipeline == null) return;
            string prefix = GetPipelineStatePrefix();
            foreach (var s in _pipeline.steps)
            {
                EditorPrefs.SetString(prefix + s.step.type, s.status.ToString());
            }
        }

        bool CheckRagdollPresent(GameObject go)
            => go != null && go.GetComponentInChildren<CharacterJoint>(true) != null;

        bool CheckSpringBonePresent(GameObject go)
            => go != null && go.GetComponentsInChildren<MonoBehaviour>(true)
                .Any(c => c.GetType().Name.Contains("SpringBone"));

        /// <summary>绘制流水线步骤按钮（左侧列表，每步可点击打开独立面板）。</summary>
        void DrawPipelineStepButton(SetupPipeline.StepState state, int index)
        {
            bool isSelected = _selectedStepIndex == index;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            try
            {
                // ── 状态图标 ──────────────────────────────────────────────
                string icon;
                Color iconColor;
                switch (state.status)
                {
                    case SetupPipeline.StepStatus.Completed:
                        icon = "✔"; iconColor = Color.green; break;
                    case SetupPipeline.StepStatus.Running:
                        icon = "⏳"; iconColor = Color.yellow; break;
                    case SetupPipeline.StepStatus.Failed:
                        icon = "✘"; iconColor = Color.red; break;
                    case SetupPipeline.StepStatus.Skipped:
                        icon = "→"; iconColor = Color.gray; break;
                    default:
                        icon = "○"; iconColor = Color.gray; break;
                }

                var prevColor = GUI.color;
                GUI.color = iconColor;
                EditorGUILayout.LabelField(icon, GUILayout.Width(16));
                GUI.color = prevColor;

                // ── 步骤名（点击选中，高亮当前选中项）─────────────────────
                var labelStyle = isSelected ? StyleStepLabelSelected : StyleStepLabel;
                if (GUILayout.Button(state.step.label, labelStyle))
                {
                    _selectedStepIndex = index;
                }
                GUI.enabled = true;

                // ── 进度 ──────────────────────────────────────────────────
                if (state.status == SetupPipeline.StepStatus.Running)
                {
                    Rect r = GUILayoutUtility.GetRect(50, 14);
                    EditorGUI.ProgressBar(r, state.progress, "");
                }
                else
                {
                    GUILayout.Space(54);
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }
        }

        /// <summary>右侧详情面板 — 根据 _selectedStepIndex 显示对应步骤的独立面板。</summary>
        void DrawStepDetailPanel()
        {
            if (_selectedStepIndex < 0 || _selectedStepIndex >= _pipeline.steps.Count)
            {
                EditorGUILayout.HelpBox(
                    "← 点击左侧流水线步骤打开对应面板。\n\n" +
                    "每步独立操作：\n" +
                    "• Config 步骤：编辑配置参数 → 保存\n" +
                    "• Setup 步骤：装配角色 Prefab 组件\n" +
                    "• Diagnostic 步骤：完整性诊断检查",
                    MessageType.Info);
                return;
            }

            var state = _pipeline.steps[_selectedStepIndex];

            // ── 面板标题栏 ────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField($"▎{state.step.label}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(state.message, EditorStyles.miniLabel);
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }
            EditorGUILayout.Space(4);

            // ── 前置必需步骤校验 ───────────────────────────────────────
            var missing = _pipeline.GetUnsatisfiedPrerequisites(state);
            if (missing.Count > 0)
            {
                var sb = new System.Text.StringBuilder(
                    state.step.allowStandalone
                        ? "ℹ 以下步骤尚未完成（独立模式：仍可继续执行此步骤）：\n"
                        : "⚠ 以下必需步骤尚未完成，建议先执行：\n");
                foreach (var m in missing)
                    sb.AppendLine($"  • {m.step.label}");
                EditorGUILayout.HelpBox(sb.ToString().TrimEnd(),
                    state.step.allowStandalone ? MessageType.Info : MessageType.Warning);
                EditorGUILayout.Space(4);
            }
            // 独立可用的步骤即使前置未完成也允许操作
            bool prerequisitesBlocked = !state.step.allowStandalone && missing.Count > 0;

            // ── 独立模式提示徽章 ──────────────────────────────────────
            if (state.step.allowStandalone)
            {
                EditorGUILayout.LabelField("🔓 独立可用 — 不依赖前置步骤", StyleStandaloneBadge);
                EditorGUILayout.Space(2);
            }

            // ── 根据步骤类型分发到对应独立面板 ────────────────────────
            EditorGUI.BeginDisabledGroup(prerequisitesBlocked);
            try
            {
                switch (state.step.type)
                {
                    case SetupPipeline.StepType.PlayerConfig:
                        DrawPlayerConfigPanel(state);
                        break;
                    case SetupPipeline.StepType.EnemyConfig:
                        DrawEnemyConfigPanel(state);
                        break;
                    case SetupPipeline.StepType.CharacterSetup:
                        DrawCharacterSetupPanel(state);
                        break;
                    case SetupPipeline.StepType.OverrideSetup:
                        DrawOverrideSetupPanel(state);
                        break;
                    case SetupPipeline.StepType.NetworkSetup:
                        DrawNetworkSetupPanel(state);
                        break;
                    case SetupPipeline.StepType.EnemySetup:
                        DrawEnemySetupPanel(state);
                        break;
                    case SetupPipeline.StepType.RagdollSetup:
                        DrawRagdollSetupPanel(state);
                        break;
                    case SetupPipeline.StepType.SpringBoneSetup:
                        DrawSpringBoneSetupPanel(state);
                        break;
                    case SetupPipeline.StepType.WeaponSetup:
                        DrawWeaponSetupPanel(state);
                        break;
                    case SetupPipeline.StepType.Diagnostic:
                        DrawDiagnosticPanel(state);
                        break;
                    default:
                        EditorGUILayout.HelpBox($"未实现的步骤类型: {state.step.type}", MessageType.Warning);
                        break;
                }
            }
            finally
            {
                EditorGUI.EndDisabledGroup();
            }
        }

        /// <summary>给 SetupPipeline 的 Config 步骤注入当前面板的提取/应用回调。</summary>
        void InjectConfigCallbacks()
        {
            if (_pipeline == null) return;
            foreach (var state in _pipeline.steps)
            {
                if (state.step.type == SetupPipeline.StepType.PlayerConfig)
                    state.step.callback = ExecutePlayerConfigStep;
                else if (state.step.type == SetupPipeline.StepType.EnemyConfig)
                    state.step.callback = ExecuteEnemyConfigStep;
            }
        }

        /// <summary>执行 Player Config 步骤：自动保存未保存修改，然后将配置应用到 CharacterMotor。</summary>
        void ExecutePlayerConfigStep()
        {
            // 如果有未保存的修改，自动保存
            if (_playerConfigDirty && _playerEditConfig != null)
                SavePlayerConfigToCharacterDir();

            if (_playerMotor != null)
            {
                var so = new SerializedObject(_playerMotor);
                var configProp = so.FindProperty("configData");
                var data = configProp != null ? configProp.objectReferenceValue as PlayerConfigData : null;
                if (data != null)
                {
                    Undo.RecordObject(_playerMotor, "Apply PlayerConfigData");
                    data.ApplyTo(_playerMotor);
                    EditorUtility.SetDirty(_playerMotor);
                    Debug.Log($"[CharacterKit] 已应用 PlayerConfig '{data.name}' 到 {_playerMotor.name}");
                }
                else
                {
                    Debug.LogWarning("[CharacterKit] PlayerConfig 步骤：未找到 configData，请先确认并保存配置。");
                }
            }
        }

        /// <summary>执行 Enemy Config 步骤：根据模式（Archetype / BehaviorData）应用配置到 EnemyAI。</summary>
        void ExecuteEnemyConfigStep()
        {
            if (_enemyAI != null)
            {
                if (_enemyUseArchetype)
                {
                    // 如果有未保存的修改，自动保存
                    if (_enemyConfigDirty && _enemyEditArch != null)
                        SaveEnemyArchetypeToEnemyDir();

                    var archetype = _enemyAI.archetype;
                    if (archetype != null)
                    {
                        Undo.RecordObject(_enemyAI, "Apply EnemyArchetype");
                        archetype.ApplyTo(_enemyAI);
                        EditorUtility.SetDirty(_enemyAI);
                        Debug.Log($"[CharacterKit] 已应用 EnemyArchetype '{archetype.archetypeName}' 到 {_enemyAI.name}");
                    }
                    else
                    {
                        Debug.LogWarning("[CharacterKit] EnemyConfig 步骤：未找到 archetype，请先保存配置。");
                    }
                }
                else
                {
                    var so = new SerializedObject(_enemyAI);
                    var data = so.FindProperty("behaviorData").objectReferenceValue as EnemyBehaviorData;
                    if (data != null)
                    {
                        Undo.RecordObject(_enemyAI, "Apply EnemyBehaviorData");
                        data.ApplyTo(_enemyAI);
                        EditorUtility.SetDirty(_enemyAI);
                        Debug.Log($"[CharacterKit] 已应用 EnemyBehaviorData '{data.name}' 到 {_enemyAI.name}");
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Player Config 独立面板
        //
        // 流程：
        //   ① 自动加载默认预设 → 编辑参数
        //   ② 拖入目标角色（决定保存路径 + 自动关联）
        //   ③ 保存到角色目录
        //   辅助：拖入已有配置文件替换默认值，修改后另存
        // ═══════════════════════════════════════════════════════════════
        /// <summary>绘制 Player Config 独立面板：编辑参数 → 拖入角色目录 → 保存配置。</summary>
        void DrawPlayerConfigPanel(SetupPipeline.StepState state)
        {
            EditorGUILayout.LabelField("② 主角手感配置（PlayerConfigData）", EditorStyles.boldLabel);

            // ── 初始化编辑缓冲区（首次打开自动加载默认预设；Prefab 已由上一步生成）─────────
            if (_playerEditConfig == null)
                InitPlayerEditConfig();

            // ── 参数来源：默认预设 或 拖入已有配置替换 ───────────────
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField("参数来源", EditorStyles.miniLabel, GUILayout.Width(60));
                var newTemplate = (PlayerConfigData)EditorGUILayout.ObjectField(_playerTemplateConfig,
                    typeof(PlayerConfigData), false);
                if (newTemplate != _playerTemplateConfig)
                {
                    _playerTemplateConfig = newTemplate;
                    if (_playerTemplateConfig != null)
                    {
                        // 用拖入的配置替换编辑缓冲区
                        _playerEditConfig.CopyFromTemplate(_playerTemplateConfig);
                        _playerEditConfigSO.Update();
                        _playerConfigDirty = true;
                    }
                }
                if (_playerTemplateConfig == null)
                {
                    EditorGUILayout.LabelField("← 使用默认预设", EditorStyles.miniLabel, GUILayout.Width(100));
                }
                else
                {
                    if (GUILayout.Button("恢复默认", GUILayout.Width(70), GUILayout.Height(18)))
                    {
                        _playerTemplateConfig = null;
                        InitPlayerEditConfig();
                    }
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            // ── 保存路径（拖入资源目录指定保存位置）───────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField("资源目录", EditorStyles.miniLabel, GUILayout.Width(60));
                var folderObj = EditorGUILayout.ObjectField(
                    GetCachedPlayerFolderAsset(),
                    typeof(DefaultAsset), false);
                if (folderObj != null)
                {
                    string p = AssetDatabase.GetAssetPath(folderObj);
                    if (AssetDatabase.IsValidFolder(p))
                        _playerResourceFolder = p;
                }
                if (!string.IsNullOrEmpty(_playerResourceFolder))
                {
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                        _playerResourceFolder = null;
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            string saveHint = GetPlayerSaveHint();
            EditorGUILayout.LabelField(saveHint, GetMiniLabelColor(ResolvePlayerSaveTarget() != null ? new Color(0.5f, 0.8f, 0.5f) : Color.gray));

            // ── 参数编辑器（自适应窗口高度，最大化显示参数）──────────
            if (_playerEditConfigSO != null)
            {
                _playerEditConfigSO.Update();
                var it = _playerEditConfigSO.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.name == "m_Script") continue;
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(it);
                    if (EditorGUI.EndChangeCheck())
                        _playerConfigDirty = true;
                }
                _playerEditConfigSO.ApplyModifiedProperties();
            }

            // ── 底部确认按钮（自适应窗口宽度）─────────────────────────
            EditorGUILayout.Space(6);
            GUILayout.FlexibleSpace();
            var confirmStyle = _playerConfigDirty ? StyleConfirmDirtyButton13 : StyleBoldButton13;
            if (GUILayout.Button(_playerConfigDirty ? "✔ 确认参数（已修改）" : "✔ 确认参数", confirmStyle, GUILayout.Height(34)))
            {
                bool linked = EnsurePlayerConfigLinked();
                if (linked)
                {
                    state.status = SetupPipeline.StepStatus.Completed;
                    state.message = "参数已确认并关联当前角色";
                    state.progress = 1f;
                    _needsPipelineStateRefresh = true;
                }
                else
                {
                    state.status = SetupPipeline.StepStatus.Failed;
                    state.message = "确认失败：未能将 PlayerConfig 关联到当前角色 Prefab";
                    state.progress = 1f;
                }
                RepaintCharacterKitWindow();
            }
        }

        /// <summary>生成 Player Config 保存路径的提示文本。</summary>
        string GetPlayerSaveHint()
        {
            string target = ResolvePlayerSaveTarget();
            if (target == null)
                return "↳ 拖入资源目录（folder）以确定保存路径（将存入该目录下）";

            return $"↳ 将保存到: {target}/PlayerConfig_{Path.GetFileName(target)}.asset";
        }

        // ═══════════════════════════════════════════════════════════════
        // 统一保存路径解析（消除 ResolvePlayer / ResolveEnemy / ResolveCharAnim 三份重复）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 统一解析保存路径：有指定目录时用目录名作为配置名、目录路径作为保存路径；
        /// 否则使用兜底目录 + 兜底名称，并确保兜底目录已创建。
        /// </summary>
        /// <param name="folder">主目录（可为 null）</param>
        /// <param name="fallbackDir">兜底资产目录</param>
        /// <param name="fallbackName">兜底配置名前缀</param>
        /// <param name="name">输出的配置名称</param>
        /// <param name="dir">输出的保存目录</param>
        /// <returns>true 表示使用了主目录，false 表示使用了兜底目录</returns>
        static bool ResolveSaveTarget(string folder, string fallbackDir, string fallbackName,
            out string name, out string dir)
        {
            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
            {
                name = Path.GetFileName(folder);
                dir = folder;
                return true;
            }
            name = fallbackName;
            dir = fallbackDir;
            EnsureDir(dir);
            return false;
        }

        /// <summary>解析 Player Config 当前生效的保存目录。</summary>
        string ResolvePlayerSaveTarget()
        {
            if (!string.IsNullOrEmpty(_playerResourceFolder) && AssetDatabase.IsValidFolder(_playerResourceFolder))
                return _playerResourceFolder;

            // 未手动拖入目录时，直接使用当前角色 Prefab 所在目录，避免配置被保存到
            // Common/NewCharacter 后却没有回写到当前角色，导致第一步始终不变绿。
            string prefabPath = ResolveCharacterPrefabPath();
            if (IsValidPrefabPath(prefabPath))
            {
                string prefabFolder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(prefabFolder)) return prefabFolder;
            }
            return null;
        }

        /// <summary>设置主角目标对象，同时自动获取 CharacterMotor 组件，并传播到后续共享步骤。</summary>
        void SetPlayerTarget(GameObject go)
        {
            _playerTarget = go;
            _playerMotor = _playerTarget != null ? _playerTarget.GetComponent<CharacterMotor>() : null;
            _needsPipelineStateRefresh = true;

            // ── CK-02：自动传播目标到后续共享步骤 ──
            if (go != null)
            {
                TryPropagateTargetToSharedSteps(go);
            }
        }

        /// <summary>从 DefaultPlayerConfig 克隆一份到编辑缓冲区，并自动指认参数来源。</summary>
        void InitPlayerEditConfig()
        {
            var template = DefaultConfigCreator.GetOrCreateDefaultPlayerConfig();
            if (template == null)
            {
                Debug.LogError("[CharacterKit] 无法加载 DefaultPlayerConfig 预设");
                return;
            }

            // 深拷贝：创建临时 ScriptableObject 并从模板复制所有值
            _playerEditConfig = ScriptableObject.CreateInstance<PlayerConfigData>();
            _playerEditConfig.CopyFromTemplate(template);
            _playerEditConfigSO = new SerializedObject(_playerEditConfig);
            _playerConfigDirty = false;

            // 自动指认参数来源：让 ObjectField 默认显示 DefaultPlayerConfig
            if (_playerTemplateConfig == null)
                _playerTemplateConfig = template;
        }

        /// <summary>从当前 CharacterMotor 提取值到编辑缓冲区。</summary>
        void ExtractPlayerConfigFromMotor()
        {
            if (_playerMotor == null) return;
            if (_playerEditConfig == null)
                InitPlayerEditConfig();
            _playerEditConfig.CopyFrom(_playerMotor);
            _playerEditConfigSO.Update();
            _playerConfigDirty = true;
        }

        /// <summary>确保当前角色有 PlayerConfig，并将编辑缓冲区写入 Prefab。</summary>
        bool EnsurePlayerConfigLinked()
        {
            string prefabPath = ResolveCharacterPrefabPath();
            if (!IsValidPrefabPath(prefabPath))
            {
                Debug.LogError("[CharacterKit] 当前没有有效的角色 Prefab，无法确认 PlayerConfig。");
                return false;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var motor = prefab != null ? prefab.GetComponentInChildren<CharacterMotor>(true) : null;
            if (motor == null)
            {
                Debug.LogError($"[CharacterKit] 当前角色缺少 CharacterMotor：{prefabPath}");
                return false;
            }

            var motorSO = new SerializedObject(motor);
            var configProp = motorSO.FindProperty("configData");
            if (configProp != null && configProp.objectReferenceValue != null)
            {
                PersistPlayerConfigCompletion(prefabPath);
                _playerTarget = prefab;
                _playerMotor = prefab.GetComponentInChildren<CharacterMotor>(true);
                return true;
            }

            if (_playerEditConfig == null)
                InitPlayerEditConfig();
            if (_playerEditConfig == null) return false;

            string targetPath = ResolvePlayerSaveTarget();
            if (string.IsNullOrEmpty(targetPath) || !AssetDatabase.IsValidFolder(targetPath))
            {
                Debug.LogError("[CharacterKit] 没有可用的角色配置保存目录。");
                return false;
            }

            string configName = Path.GetFileName(targetPath);
            string savePath = $"{targetPath}/PlayerConfig_{configName}.asset";
            var data = AssetDatabase.LoadAssetAtPath<PlayerConfigData>(savePath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<PlayerConfigData>();
                data.CopyFromTemplate(_playerEditConfig);
                savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);
                AssetDatabase.CreateAsset(data, savePath);
                AssetDatabase.SaveAssets();
            }

            if (!AssignPlayerConfigToCharacterPrefab(data))
            {
                Debug.LogError($"[CharacterKit] PlayerConfig 创建成功但回写 Prefab 失败：{savePath}");
                return false;
            }

            _playerConfigDirty = false;
            PersistPlayerConfigCompletion(prefabPath);
            Debug.Log($"[CharacterKit] 已确认并关联主角配置：{savePath}");
            return true;
        }

        /// <summary>将编辑好的配置保存到角色目录，并避免重复生成无关配置资产。</summary>
        void SavePlayerConfigToCharacterDir()
        {
            EnsurePlayerConfigLinked();
        }

        [MenuItem("Tools/Character Kit/Maintenance/Repair Current Player Config")]
        static void RepairCurrentPlayerConfig()
        {
            var panel = new CharacterKitPanel();
            string prefabPath = panel.ResolveCharacterPrefabPath();
            if (!IsValidPrefabPath(prefabPath))
            {
                Debug.LogError("[CharacterKit] 当前玩家入口无效，无法修复 Player Config。");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var motor = prefab != null ? prefab.GetComponent<CharacterMotor>() : null;
            if (motor == null)
            {
                Debug.LogError($"[CharacterKit] Prefab 缺少 CharacterMotor：{prefabPath}");
                return;
            }
            var currentSO = new SerializedObject(motor);
            var currentProp = currentSO.FindProperty("configData");
            if (currentProp != null && currentProp.objectReferenceValue != null)
            {
                PersistPlayerConfigCompletion(prefabPath);
                Debug.Log($"[CharacterKit] 当前角色已有 Player Config：{currentProp.objectReferenceValue.name}");
                return;
            }

            var template = DefaultConfigCreator.GetOrCreateDefaultPlayerConfig();
            if (template == null)
            {
                Debug.LogError("[CharacterKit] 找不到 DefaultPlayerConfig。");
                return;
            }
            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogError($"[CharacterKit] 角色目录无效：{folder}");
                return;
            }
            var data = ScriptableObject.CreateInstance<PlayerConfigData>();
            data.CopyFromTemplate(template);
            string configPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{folder}/PlayerConfig_{Path.GetFileName(folder)}.asset");
            AssetDatabase.CreateAsset(data, configPath);
            AssetDatabase.SaveAssets();

            if (panel.AssignPlayerConfigToCharacterPrefab(data))
            {
                PersistPlayerConfigCompletion(prefabPath);
                RepaintCharacterKitWindow();
                Debug.Log($"[CharacterKit] 已修复并关联当前玩家 Player Config：{configPath}");
            }
            else
                Debug.LogError($"[CharacterKit] Player Config 资产已创建，但回写 Prefab 失败：{configPath}");
        }

        bool AssignPlayerConfigToCharacterPrefab(PlayerConfigData data)
        {
            if (data == null) return false;
            string prefabPath = ResolveCharacterPrefabPath();
            if (!IsValidPrefabPath(prefabPath)) return false;

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null) return false;
                var motor = root.GetComponentInChildren<CharacterMotor>(true);
                if (motor == null) return false;

                var so = new SerializedObject(motor);
                var configProp = so.FindProperty("configData");
                if (configProp == null) return false;
                configProp.objectReferenceValue = data;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(motor);

                if (!PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saveSuccess) || !saveSuccess)
                    return false;
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var savedMotor = savedPrefab != null ? savedPrefab.GetComponent<CharacterMotor>() : null;
            var savedSO = savedMotor != null ? new SerializedObject(savedMotor) : null;
            var savedProp = savedSO?.FindProperty("configData");
            bool linked = savedProp != null && savedProp.objectReferenceValue == data;
            if (linked)
            {
                _playerTarget = savedPrefab;
                _playerMotor = savedMotor;
                _needsPipelineStateRefresh = true;
            }
            return linked;
        }

        // ═══════════════════════════════════════════════════════════════
        // Character AnimSet 初始化 / 保存（模板+覆盖模式，与 PlayerConfig 同逻辑）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>从 DefaultCharacterAnimSet 克隆一份到编辑缓冲区。</summary>
        void InitCharSetupAnimSet()
        {
            var template = DefaultConfigCreator.GetOrCreateDefaultAnimSet();
            if (template == null)
            {
                Debug.LogError("[CharacterKit] 无法加载 DefaultCharacterAnimSet 预设");
                return;
            }

            _charSetupEditAnimSet = ScriptableObject.CreateInstance<CharacterAnimSetAsset>();
            _charSetupEditAnimSet.CopyFrom(template);
            _charSetupEditAnimSet.name = "AnimSet_EditBuffer";
            _charSetupAnimDirty = false;

            if (_charSetupTemplateAnimSet == null)
                _charSetupTemplateAnimSet = template;
        }

        /// <summary>用拖入的动画集模板替换编辑缓冲区。</summary>
        void ApplyCharSetupAnimSetTemplate(CharacterAnimSetAsset newTemplate)
        {
            if (newTemplate == null) return;
            _charSetupTemplateAnimSet = newTemplate;
            _charSetupEditAnimSet = ScriptableObject.CreateInstance<CharacterAnimSetAsset>();
            _charSetupEditAnimSet.CopyFrom(newTemplate);
            _charSetupEditAnimSet.name = "AnimSet_EditBuffer";
            _charSetupAnimDirty = true;
        }

        /// <summary>解析动画集的保存目录：_playerResourceFolder → 角色模型文件夹 → Common 兜底。</summary>
        string ResolveCharAnimSetSaveDir()
        {
            // 优先用 _playerResourceFolder
            if (!string.IsNullOrEmpty(_playerResourceFolder) && AssetDatabase.IsValidFolder(_playerResourceFolder))
                return _playerResourceFolder;

            // 其次用角色模型文件夹的父目录
            if (_charSetupFolder != null)
            {
                string p = AssetDatabase.GetAssetPath(_charSetupFolder);
                if (!string.IsNullOrEmpty(p)) return p;
            }

            return null;
        }

        /// <summary>将编辑好的动画集保存到角色目录（或 Common 兜底）。</summary>
        void SaveCharAnimSetToCharacterDir()
        {
            if (_charSetupEditAnimSet == null) return;

            string targetPath = _charSetupFolderPath;
            string configName = !string.IsNullOrEmpty(targetPath)
                ? Path.GetFileName(targetPath)
                : null;
            if (string.IsNullOrEmpty(targetPath) || !AssetDatabase.IsValidFolder(targetPath))
            {
                ResolveSaveTarget(ResolveCharAnimSetSaveDir(),
                    "Assets/_Game/character/Common", "NewCharacter",
                    out configName, out targetPath);
            }

            string savePath = $"{targetPath}/AnimSet_{configName}.asset";
            var data = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(savePath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<CharacterAnimSetAsset>();
                AssetDatabase.CreateAsset(data, savePath);
            }
            data.CopyFrom(_charSetupEditAnimSet);
            data.name = Path.GetFileNameWithoutExtension(savePath);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            _charSetupAnimSetPath = savePath;

            EditorGUIUtility.PingObject(data);
            _charSetupAnimDirty = false;
            Debug.Log($"[CharacterKit] 已保存动画集（稳定路径）: {savePath}");
        }

        // ═══════════════════════════════════════════════════════════════
        // Enemy Config 独立面板
        //
        // 流程：
        //   ① 自动加载默认预设 → 编辑配方
        //   ② 拖入目标敌人（决定保存路径 + 自动关联）
        //   ③ 保存到敌人目录
        //   辅助：拖入已有配方替换默认值 / 切换 BehaviorData 兼容模式
        // ═══════════════════════════════════════════════════════════════
        /// <summary>绘制 Enemy Config 独立面板：编辑敌人配方 → 拖入资源目录 → 保存。</summary>
        void DrawEnemyConfigPanel(SetupPipeline.StepState state)
        {
            EditorGUILayout.LabelField("① 敌人配置", EditorStyles.boldLabel);

            if (!_enemyUseArchetype)
            {
                // BehaviorData 兼容模式 — 建议切换到 Archetype
                EditorGUILayout.HelpBox(
                    "当前使用旧版 BehaviorData 模式。建议切换到 Archetype 模式以获得更完整的参数编辑体验。",
                    MessageType.Info);
                if (GUILayout.Button("切换到 Archetype 模式（推荐）", GUILayout.Height(24)))
                {
                    _enemyUseArchetype = true;
                    _enemyEditArch = null;
                    _enemyEditArchSO = null;
                    _enemyConfigDirty = false;
                }
                return;
            }

            // ── 初始化编辑缓冲区（首次打开自动加载默认预设；Prefab 已由上一步生成）─────────
            if (_enemyEditArch == null)
                InitEnemyEditArchetype();

            // ── 参数来源：默认预设 或 拖入已有配方替换 ───────────────
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField("配方来源", EditorStyles.miniLabel, GUILayout.Width(60));
                var newTemplate = (EnemyArchetype)EditorGUILayout.ObjectField(_enemyTemplateArch,
                    typeof(EnemyArchetype), false);
                if (newTemplate != _enemyTemplateArch)
                {
                    _enemyTemplateArch = newTemplate;
                    if (_enemyTemplateArch != null)
                    {
                        _enemyEditArch.CopyFromTemplate(_enemyTemplateArch);
                        _enemyEditArchSO.Update();
                        _enemyConfigDirty = true;
                    }
                }
                if (_enemyTemplateArch == null)
                {
                    EditorGUILayout.LabelField("← 使用默认预设", EditorStyles.miniLabel, GUILayout.Width(100));
                }
                else
                {
                    if (GUILayout.Button("恢复默认", GUILayout.Width(70), GUILayout.Height(18)))
                    {
                        _enemyTemplateArch = null;
                        InitEnemyEditArchetype();
                    }
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            // ── 保存路径（拖入资源目录指定保存位置）───────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField("资源目录", EditorStyles.miniLabel, GUILayout.Width(60));
                var folderObj = EditorGUILayout.ObjectField(
                    GetCachedEnemyFolderAsset(),
                    typeof(DefaultAsset), false);
                if (folderObj != null)
                {
                    string p = AssetDatabase.GetAssetPath(folderObj);
                    if (AssetDatabase.IsValidFolder(p))
                        _enemyResourceFolder = p;
                }
                if (!string.IsNullOrEmpty(_enemyResourceFolder))
                {
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                        _enemyResourceFolder = null;
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            string saveHint = GetEnemySaveHint();
            EditorGUILayout.LabelField(saveHint, GetMiniLabelColor(ResolveEnemySaveTarget() != null ? new Color(0.5f, 0.8f, 0.5f) : Color.gray));

            // ── 参数编辑器 ──────────────────────────────────────────────
            if (_enemyEditArchSO != null)
            {
                _enemyEditArchSO.Update();

                var it = _enemyEditArchSO.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.name == "m_Script") continue;
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(it);
                    if (EditorGUI.EndChangeCheck())
                        _enemyConfigDirty = true;
                }
                _enemyEditArchSO.ApplyModifiedProperties();
            }

            // ── 底部确认按钮（自适应窗口宽度）─────────────────────────
            EditorGUILayout.Space(6);
            GUILayout.FlexibleSpace();
            var confirmStyle = _enemyConfigDirty ? StyleConfirmDirtyButton13 : StyleBoldButton13;
            if (GUILayout.Button(_enemyConfigDirty ? "✔ 确认参数（已修改）" : "✔ 确认参数", confirmStyle, GUILayout.Height(34)))
            {
                if (_enemyConfigDirty)
                {
                    SaveEnemyArchetypeToEnemyDir();
                    _enemyConfigDirty = false;
                }
                state.status = SetupPipeline.StepStatus.Completed;
                state.message = "参数已确认";
                state.progress = 1f;
            }

            // ── BehaviorData 兼容入口（小字链接）────────────────────
            EditorGUILayout.Space(2);
            if (GUILayout.Button("切换到 BehaviorData 兼容模式", EditorStyles.linkLabel))
            {
                _enemyUseArchetype = false;
                _enemyEditArch = null;
                _enemyEditArchSO = null;
                _enemyConfigDirty = false;
            }
        }

        /// <summary>生成 Enemy Config 保存路径的提示文本。</summary>
        string GetEnemySaveHint()
        {
            string target = ResolveEnemySaveTarget();
            if (target == null)
                return "↳ 拖入资源目录（folder）以确定保存路径（将存入该目录下）";

            return $"↳ 将保存到: {target}/Archetype_{Path.GetFileName(target)}.asset";
        }

        /// <summary>解析敌人配方当前生效的保存目录：_enemyResourceFolder → 返回 null 表示未指定。</summary>
        string ResolveEnemySaveTarget()
        {
            if (!string.IsNullOrEmpty(_enemyResourceFolder) && AssetDatabase.IsValidFolder(_enemyResourceFolder))
                return _enemyResourceFolder;
            return null;
        }

        /// <summary>设置敌人目标对象，同时自动获取 EnemyAI 组件，并传播到后续共享步骤。</summary>
        void SetEnemyTarget(GameObject go)
        {
            _enemyTarget = go;
            _enemyAI = _enemyTarget != null ? _enemyTarget.GetComponent<EnemyAI>() : null;
            _needsPipelineStateRefresh = true;

            // ── CK-02：自动传播目标到后续共享步骤 ──
            if (go != null)
            {
                TryPropagateTargetToSharedSteps(go);
            }
        }

        /// <summary>
        /// CK-02：将前序步骤选定的目标自动传播到后续共享步骤。
        /// 只对尚未手动指定的共享步骤生效（保留用户手动覆盖的能力）。
        /// </summary>
        void TryPropagateTargetToSharedSteps(GameObject target)
        {
            if (target == null) return;

            int propagated = 0;

            if (_ragdollTarget == null || _ragdollTargetAuto)
            {
                _ragdollTarget = target;
                _ragdollTargetAuto = true;
                propagated++;
            }
            if (_springBoneTarget == null || _springTargetAuto)
            {
                _springBoneTarget = target;
                _springTargetAuto = true;
                propagated++;
            }
            if (_weaponTarget == null || _weaponTargetAuto)
            {
                _weaponTarget = target;
                _weaponTargetAuto = true;
                propagated++;
            }
            if (_diagTarget == null || _diagTargetAuto)
            {
                _diagTarget = target;
                _diagTargetAuto = true;
                propagated++;
            }

            if (propagated > 0)
            {
                Debug.Log($"[CharacterKit] 自动传播目标 '{target.name}' → {propagated} 个共享步骤" +
                          "(Ragdoll/SpringBone/Weapon/Diagnostic)");
                _needsPipelineStateRefresh = true;
            }
        }

        /// <summary>
        /// CK-02：当用户在共享步骤中手动拖入不同目标时，清除自动标记。
        /// </summary>
        void OnSharedTargetManuallyChanged(GameObject newTarget, ref bool autoFlag)
        {
            if (newTarget != null)
            {
                autoFlag = false; // 用户手动覆盖，断开自动绑定
            }
        }

        // ── Enemy Archetype 编辑缓冲区操作 ──────────────────────────

        /// <summary>从 DefaultEnemyArchetype 克隆一份到编辑缓冲区，并自动指认参数来源。</summary>
        void InitEnemyEditArchetype()
        {
            var template = DefaultConfigCreator.GetOrCreateDefaultEnemyArchetype();
            if (template == null)
            {
                Debug.LogError("[CharacterKit] 无法加载 DefaultEnemyArchetype 预设");
                return;
            }

            _enemyEditArch = ScriptableObject.CreateInstance<EnemyArchetype>();
            _enemyEditArch.CopyFromTemplate(template);
            _enemyEditArchSO = new SerializedObject(_enemyEditArch);
            _enemyConfigDirty = false;

            // 自动指认参数来源：让 ObjectField 默认显示 DefaultEnemyArchetype
            if (_enemyTemplateArch == null)
                _enemyTemplateArch = template;
        }

        /// <summary>从当前 EnemyAI 提取值到编辑缓冲区。</summary>
        void ExtractEnemyArchetypeFromAI()
        {
            if (_enemyAI == null) return;
            if (_enemyEditArch == null)
                InitEnemyEditArchetype();
            _enemyEditArch.CopyFrom(_enemyAI);
            _enemyEditArchSO.Update();
            _enemyConfigDirty = true;
        }

        /// <summary>将编辑好的配方保存到资源目录（或 Common 兜底）。</summary>
        void SaveEnemyArchetypeToEnemyDir()
        {
            if (_enemyEditArch == null) return;

            ResolveSaveTarget(_enemyResourceFolder,
                "Assets/_Game/character/Common/EnemyArchetypes", "NewEnemy",
                out string archName, out string targetPath);

            var data = ScriptableObject.CreateInstance<EnemyArchetype>();
            data.CopyFromTemplate(_enemyEditArch);
            data.archetypeName = archName + " 配方";

            string savePath = AssetDatabase.GenerateUniqueAssetPath($"{targetPath}/Archetype_{archName}.asset");
            AssetDatabase.CreateAsset(data, savePath);
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(data);
            _enemyConfigDirty = false;
            Debug.Log($"[CharacterKit] 已保存敌人配方: {savePath}");
        }

        // ═══════════════════════════════════════════════════════════════
        // 动画序列 & 过渡编辑器（Character/Enemy 共用）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>绘制单个动画序列编辑器（折叠面板，含多段 + 衔接）</summary>
        void DrawCharAnimSequenceEditor(CharacterAnimSequence seq, string displayLabel, System.Action onChanged = null)
        {
            EditorGUILayout.BeginVertical(StyleHelpBox);
            try
            {
                // ── 标题行（ID + 多段开关）────────────────────────────────
                EditorGUILayout.BeginHorizontal();
                try
                {
                    EditorGUILayout.LabelField($"── {displayLabel} ──", EditorStyles.boldLabel, GUILayout.Width(120));
                    var newId = EditorGUILayout.TextField(seq.id, GUILayout.Width(110));
                    if (newId != seq.id) { seq.id = newId; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                    GUILayout.FlexibleSpace();
                    bool newMulti = EditorGUILayout.ToggleLeft("多段动画", seq.useMultiSegment, GUILayout.Width(85));
                    if (newMulti != seq.useMultiSegment) { seq.useMultiSegment = newMulti; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                }

                EditorGUI.indentLevel++;

                if (!seq.useMultiSegment)
                {
                    // ── 单段模式：只显示一个主片段 ────────────────────────
                    var newClip = (AnimationClip)EditorGUILayout.ObjectField(
                        "主片段", seq.segments.Count > 0 ? seq.segments[0] : null, typeof(AnimationClip), false);
                    if (newClip != (seq.segments.Count > 0 ? seq.segments[0] : null))
                    {
                        if (seq.segments.Count == 0) seq.segments.Add(newClip);
                        else seq.segments[0] = newClip;
                        if (onChanged != null) onChanged(); else _charSetupAnimDirty = true;
                    }
                }
                else
                {
                    // ── 衔接：起手（播放主动作前的过渡动画，如拔刀、蓄力等）─
                    var newLeadIn = (AnimationClip)EditorGUILayout.ObjectField(
                        "↳ 衔接-起手（可选）", seq.leadIn, typeof(AnimationClip), false);
                    if (newLeadIn != seq.leadIn) { seq.leadIn = newLeadIn; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                    // ── 多段主体（连击的每一段攻击/技能动画）──────────────
                    int newSegCount = EditorGUILayout.IntSlider("主动作段数", Mathf.Max(seq.segments.Count, 1), 1, 6);
                    while (seq.segments.Count < newSegCount) { seq.segments.Add(null); if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }
                    while (seq.segments.Count > newSegCount) { seq.segments.RemoveAt(seq.segments.Count - 1); if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                    for (int s = 0; s < seq.segments.Count; s++)
                    {
                        var newClip = (AnimationClip)EditorGUILayout.ObjectField(
                            $"  段 {s + 1}", seq.segments[s], typeof(AnimationClip), false);
                        if (newClip != seq.segments[s]) { seq.segments[s] = newClip; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }
                    }

                    // ── 衔接：收招（主动作播完后的过渡动画，如收刀、后摇等）─
                    var newLeadOut = (AnimationClip)EditorGUILayout.ObjectField(
                        "↳ 衔接-收招（可选）", seq.leadOut, typeof(AnimationClip), false);
                    if (newLeadOut != seq.leadOut) { seq.leadOut = newLeadOut; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                    // ── 过渡参数 ────────────────────────────────────────────
                    if (seq.leadIn != null || seq.leadOut != null || seq.segments.Count > 1)
                    {
                        EditorGUILayout.Space(2);
                        float newLeadDur = EditorGUILayout.Slider("衔接过渡(秒)", seq.leadTransitionDuration, 0f, 0.5f);
                        if (!Mathf.Approximately(newLeadDur, seq.leadTransitionDuration))
                        { seq.leadTransitionDuration = newLeadDur; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                        if (seq.segments.Count > 1)
                        {
                            float newSegDur = EditorGUILayout.Slider("段间过渡(秒)", seq.segmentTransitionDuration, 0f, 0.5f);
                            if (!Mathf.Approximately(newSegDur, seq.segmentTransitionDuration))
                            { seq.segmentTransitionDuration = newSegDur; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }
                        }

                        bool newInterrupt = EditorGUILayout.Toggle("允许连击打断", seq.canInterruptSelf);
                        if (newInterrupt != seq.canInterruptSelf) { seq.canInterruptSelf = newInterrupt; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel--;
                SafeEndLayout(() => EditorGUILayout.EndVertical());
            }
            EditorGUILayout.Space(2);
        }

        /// <summary>绘制单条通用过渡编辑器。</summary>
        void DrawBlendTransitionEditor(BlendTransitionData trans, int index, System.Action onChanged = null)
        {
            EditorGUILayout.BeginVertical(StyleHelpBox);
            try
            {
                // ── 标题行（类型选择）────────────────────────────────────
                EditorGUILayout.BeginHorizontal();
                try
                {
                    EditorGUILayout.LabelField($"── 过渡 #{index + 1} ──", EditorStyles.boldLabel, GUILayout.Width(100));

                    var newType = (BlendTransitionData.TransitionType)EditorGUILayout.EnumPopup(trans.type, GUILayout.Width(140));
                    if (newType != trans.type) { trans.type = newType; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                }

                EditorGUI.indentLevel++;

                switch (trans.type)
                {
                    case BlendTransitionData.TransitionType.OneWay:
                    case BlendTransitionData.TransitionType.TwoWay:
                        // From / To 状态动画（状态名自动从 clip 名派生）
                        var newFromClip = (AnimationClip)EditorGUILayout.ObjectField("源状态动画", trans.fromClip, typeof(AnimationClip), false);
                        if (newFromClip != trans.fromClip) { trans.fromClip = newFromClip; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                        var newToClip = (AnimationClip)EditorGUILayout.ObjectField("目标状态动画", trans.toClip, typeof(AnimationClip), false);
                        if (newToClip != trans.toClip) { trans.toClip = newToClip; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                        // 过渡动画片段
                        var newTransClip = (AnimationClip)EditorGUILayout.ObjectField("过渡动画（独立片段）", trans.transitionClip, typeof(AnimationClip), false);
                        if (newTransClip != trans.transitionClip) { trans.transitionClip = newTransClip; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                        if (trans.type == BlendTransitionData.TransitionType.TwoWay)
                        {
                            EditorGUILayout.LabelField("（双向：同时生成 From→To 和 To→From 过渡）", EditorStyles.miniLabel);
                        }
                        break;

                    case BlendTransitionData.TransitionType.AnyStateToTarget:
                        var newAnyClip = (AnimationClip)EditorGUILayout.ObjectField("目标状态动画", trans.toClip, typeof(AnimationClip), false);
                        if (newAnyClip != trans.toClip) { trans.toClip = newAnyClip; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                        var newAnyTransClip = (AnimationClip)EditorGUILayout.ObjectField("过渡动画（独立片段）", trans.transitionClip, typeof(AnimationClip), false);
                        if (newAnyTransClip != trans.transitionClip) { trans.transitionClip = newAnyTransClip; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                        EditorGUILayout.LabelField("（所有攻击/技能子状态自动添加 → 回退目标的过渡）", EditorStyles.miniLabel);
                        break;
                }

                // ── 过渡 Blend 参数（通用）────────────────────────────────
                EditorGUILayout.Space(2);
                float newBlendIn = EditorGUILayout.Slider("切入 Blend(秒)  源→过渡", trans.blendInDuration, 0f, 0.5f);
                if (!Mathf.Approximately(newBlendIn, trans.blendInDuration)) { trans.blendInDuration = newBlendIn; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }

                float newBlendOut = EditorGUILayout.Slider("切出 Blend(秒)  过渡→目标", trans.blendOutDuration, 0f, 0.5f);
                if (!Mathf.Approximately(newBlendOut, trans.blendOutDuration)) { trans.blendOutDuration = newBlendOut; if (onChanged != null) onChanged(); else _charSetupAnimDirty = true; }
            }
            finally
            {
                EditorGUI.indentLevel--;
                SafeEndLayout(() => EditorGUILayout.EndVertical());
            }
            EditorGUILayout.Space(2);
        }

        // ═══════════════════════════════════════════════════════════════
        // 共享工具：Part 1 就绪判定 + 执行按钮绘制（Character/Enemy 共用）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>统计 Part 1 就绪条件数（通用实现）。</summary>
        int CountSetupPart1Ready(Object controller, Object folder, bool hasAnyAnim)
        {
            int count = 0;
            if (controller != null) count++;
            if (folder != null) count++;
            if (hasAnyAnim) count++;
            return count;
        }

        /// <summary>
        /// 绘制 Part 1 执行按钮（统一 Character / Enemy 两侧）。
        /// 就绪条件：Controller + 文件夹 + 至少一个动画（3 项）。
        /// customLabel：可选，覆盖默认按钮文字（如 "确认执行"）。
        /// </summary>
        bool DrawSetupPart1ExecuteButton(int ready, string animDesc, System.Action onExecute, string customLabel = null)
        {
            bool canRun = ready >= 3;
            string btnLabel;
            if (!string.IsNullOrEmpty(customLabel))
            {
                btnLabel = canRun
                    ? $"▶ {customLabel}：生成 Controller + Prefab + SkillData"
                    : $"▶ {customLabel}（{ready}/3 已就绪）";
            }
            else
            {
                btnLabel = canRun
                    ? "▶ 执行 Part 1：生成 Controller + Prefab"
                    : $"▶ 执行 Part 1（{ready}/3 已就绪）";
            }

            var btnStyle = StyleBoldButton12;
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = canRun ? new Color(0.40f, 0.70f, 0.45f) : new Color(0.55f, 0.55f, 0.55f);

            if (GUILayout.Button(btnLabel, btnStyle, GUILayout.Height(34)))
            {
                if (!canRun)
                {
                    EditorUtility.DisplayDialog("未就绪",
                        $"请先完成所有分组：\n  ① 模板 Animator Controller\n  ② 角色模型文件夹\n  ③ 至少一个{animDesc}动画",
                        "好的");
                }
                else
                {
                    // 延迟到下一编辑器帧执行，避免 AssetDatabase.Refresh / SaveAssets
                    // 在 OnGUI 内同步调用破坏 IMGUI 全局布局栈，导致 EndLayoutGroup 错误。
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            onExecute();
                        }
                        catch (System.Exception ex) when (!(ex is UnityEngine.ExitGUIException))
                        {
                            Debug.LogError($"[CharacterKit] 执行操作时发生异常: {ex}");
                            EditorUtility.DisplayDialog("执行错误", $"操作失败:\n{ex.Message}\n\n详见 Console", "确定");
                        }
                        RepaintCharacterKitWindow();
                    };
                }
            }
            GUI.backgroundColor = prevBg;
            return canRun;
        }

        /// <summary>
        /// 绘制 Part 1 完成状态展示（统一 Character / Enemy 两侧）。
        /// </summary>
        void DrawSetupPart1CompleteBox(string controllerPath, string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(StylePart1CompleteBox);
            try
            {
                var prevC = GUI.contentColor;
                GUI.contentColor = new Color(0.4f, 0.75f, 0.45f);
                EditorGUILayout.LabelField("✅ Part 1 完成", EditorStyles.boldLabel);
                GUI.contentColor = prevC;
                if (!string.IsNullOrEmpty(controllerPath))
                    EditorGUILayout.LabelField($"  Controller: {controllerPath}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"  Prefab:     {prefabPath}", EditorStyles.miniLabel);
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndVertical());
            }
        }

        /// <summary>
        /// 绘制 Enemy Setup 完成状态展示（一站式：Controller + Prefab + SkillData）。
        /// </summary>
        void DrawEnemySetupCompleteBox()
        {
            if (string.IsNullOrEmpty(_enemySetupPrefabPath)) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(StyleHelpBox);
            try
            {
                var prevC = GUI.contentColor;
                GUI.contentColor = new Color(0.4f, 0.75f, 0.45f);
                EditorGUILayout.LabelField("✅ Enemy Setup 完成", EditorStyles.boldLabel);
                GUI.contentColor = prevC;
                if (!string.IsNullOrEmpty(_enemySetupControllerPath))
                    EditorGUILayout.LabelField($"  Controller: {_enemySetupControllerPath}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"  Prefab:     {_enemySetupPrefabPath}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(_enemySetupName))
                    EditorGUILayout.LabelField($"  SkillData:  {_enemySetupFolderPath}/SkillData/", EditorStyles.miniLabel);

                // P1-5: EnemySetup → SkillBuilderWizard 快捷跳转
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                try
                {
                    // 找到第一个生成的 SkillData 作为默认打开目标
                    string skillDataDir = $"{_enemySetupFolderPath}/SkillData";
                    if (AssetDatabase.IsValidFolder(skillDataDir))
                    {
                        var guids = AssetDatabase.FindAssets("t:SkillData", new[] { skillDataDir });
                        if (guids.Length > 0)
                        {
                            var firstSd = AssetDatabase.LoadAssetAtPath<SkillData>(
                                AssetDatabase.GUIDToAssetPath(guids[0]));
                            if (firstSd != null && GUILayout.Button("在技能编辑器中打开", GUILayout.Height(22)))
                            {
                                SkillBuilderWizard.EditSkill(firstSd);
                            }
                        }
                    }
                    if (GUILayout.Button("刷新 SkillData 列表", GUILayout.Height(22), GUILayout.Width(120)))
                    {
                        AssetDatabase.Refresh();
                        RepaintCharacterKitWindow();
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndVertical());
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 共享方法：Character/Enemy Setup 通用 UI 辅助 (P2.4)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>绘制一个带标题的分组折叠区域（Character / Enemy 共用）。</summary>
        void DrawSetupSection(string title, System.Action body)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.BeginVertical(StyleHelpBox);
            try
            {
                EditorGUILayout.LabelField(title, StyleTitle12);
                EditorGUILayout.Space(2);
                body();
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndVertical());
            }
        }

        /// <summary>绘制单个 AnimationClip 的 ObjectField（Character / Enemy 共用）。</summary>
        AnimationClip DrawSetupClipField(string label, AnimationClip clip)
        {
            return (AnimationClip)EditorGUILayout.ObjectField($"  {label}", clip, typeof(AnimationClip), false);
        }

        /// <summary>通用 Setup 日志记录（Character / Enemy 共用）。</summary>
        void SetupLog(List<string> logTarget, string prefix, string msg)
        {
            logTarget.Add(msg);
            Debug.Log($"{prefix} {msg}");
        }

        // ═══════════════════════════════════════════════════════════════
        // P4.1：缓存 AssetDatabase.LoadAssetAtPath 结果，避免 OnGUI 每帧 IO
        // ═══════════════════════════════════════════════════════════════

        /// <summary>获取缓存的 Player 资源目录 DefaultAsset（仅在路径变更时重新加载）。</summary>
        DefaultAsset GetCachedPlayerFolderAsset()
        {
            if (_cachedPlayerFolderPath != _playerResourceFolder)
            {
                _cachedPlayerFolderPath = _playerResourceFolder;
                _cachedPlayerFolderAsset = string.IsNullOrEmpty(_playerResourceFolder)
                    ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(_playerResourceFolder);
            }
            return _cachedPlayerFolderAsset;
        }

        /// <summary>获取缓存的 Enemy 资源目录 DefaultAsset（仅在路径变更时重新加载）。</summary>
        DefaultAsset GetCachedEnemyFolderAsset()
        {
            if (_cachedEnemyFolderPath != _enemyResourceFolder)
            {
                _cachedEnemyFolderPath = _enemyResourceFolder;
                _cachedEnemyFolderAsset = string.IsNullOrEmpty(_enemyResourceFolder)
                    ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(_enemyResourceFolder);
            }
            return _cachedEnemyFolderAsset;
        }

    }
}
#endif