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
    /// <summary>
    /// Character Kit 面板（Phase 3）— 流水线概览 + 点击步骤打开独立子面板。
    ///
    /// 当前布局：
    ///   - 顶部 Sub Tab：装配 Setup / 技能 Skill
    ///   - Setup Tab：左侧流水线步骤列表（可拖拽分隔条），右侧选中步骤的独立子面板
    ///     · Player Config — 主角手感配置（模板+覆盖模式，编辑后保存到角色目录）
    ///     · Enemy Config — 敌人配置（Archetype 模式 + BehaviorData 兼容模式）
    ///     · Character Setup — 主角 Prefab 装配（基础组装 + 动画绑定）
    ///     · Override Setup — 主角动画 Override 骨架与映射校验
    ///     · Network Setup — 主角联机组件、玩家入口与 Resources prefab
    ///     · Enemy Setup — 敌人 Prefab 装配
    ///     · Ragdoll Setup — 布娃娃物理骨骼
    ///     · SpringBone Setup — 动态骨骼/飘带
    ///     · Weapon Setup — 武器挂载 + 切换系统
    ///     · Diagnostic — 完整性诊断 / Enemy AI 诊断
    ///   - Skill Tab：打开 Skill Builder Wizard 统一入口
    ///
    /// 关键设计：
    ///   - 流水线按目标类型（Character / Enemy）切换步骤列表
    ///   - Config 步骤采用"模板+覆盖"模式：自动加载默认预设 → 编辑 → 保存到角色目录
    ///   - 共享步骤（Ragdoll / SpringBone / Weapon）在 Character ↔ Enemy 切换时保留数据
    ///   - 动画集（CharacterAnimSetAsset）支持多段动画 + 衔接（起手/收招）+ 通用过渡
    /// </summary>
    public partial class CharacterKitPanel
    {
        private enum Tab { Skill, Setup }
        private Tab _tab = Tab.Setup;
        private bool _sessionLoaded = false;

        // ── Player Config 状态（Setup Tab 第 1 步）──────────────────
        private GameObject _playerTarget;
        private CharacterMotor _playerMotor;
        private PlayerConfigData _playerTemplateConfig;   // 拖入的已有配置（替换默认），null=用默认预设
        private PlayerConfigData _playerEditConfig;       // 当前正在编辑的配置（来自默认预设或拖入模板的副本）
        private SerializedObject _playerEditConfigSO;     // 可编辑的 SerializedObject
        private bool _playerConfigDirty = false;          // 是否有未保存修改
        private string _playerResourceFolder;             // 用户拖入的资源目录（_playerTarget 为空时使用）
        // P4.1：缓存资源目录的 DefaultAsset，避免 OnGUI 每帧 AssetDatabase.LoadAssetAtPath
        private DefaultAsset _cachedPlayerFolderAsset;
        private string _cachedPlayerFolderPath;

        // ── Enemy Config 状态（Setup Tab 第 0 步）───────────────────
        private GameObject _enemyTarget;
        private EnemyAI _enemyAI;
        private bool _enemyUseArchetype = true;            // true=用 Archetype, false=用 BehaviorData
        private EnemyArchetype _enemyTemplateArch;          // 拖入的已有配方（替换默认），null=用默认预设
        private EnemyArchetype _enemyEditArch;             // 当前正在编辑的配方（来自默认预设或拖入模板的副本）
        private SerializedObject _enemyEditArchSO;         // 可编辑的 SerializedObject
        private bool _enemyConfigDirty = false;            // 是否有未保存修改
        private string _enemyResourceFolder;               // 用户拖入的资源目录（保存路径）
        private DefaultAsset _cachedEnemyFolderAsset;
        private string _cachedEnemyFolderPath;

        // ── Skill Tab 状态 (CK-07) ────────────────────────────────────
        private string _currentCharFolder = "";            // 当前角色目录
        private List<SkillData> _charSkillList;            // 缓存的技能资产列表
        private Vector2 _skillListScroll;
        private bool _skillListScanned = false;
        private string _skillSearchFilter = "";
        private bool _skillShowBasicAttacks = true;

        // ── Setup Tab 状态 ───────────────────────────────────────────
        private SetupPipeline _pipeline;
        private SetupPipeline.PipelineTarget _setupTarget = SetupPipeline.PipelineTarget.Character;
        private bool _pipelineBuilt = false;
        private bool _needsPipelineStateRefresh = false;
        private Vector2 _setupScroll;         // 流水线步骤列表的滚动
        private int _selectedStepIndex = -1;   // 当前选中的步骤索引，-1=未选中任何步骤
        private float _splitterPos = 250f;     // 分隔条位置（左侧面板宽度）
        private float _pendingSplitterPos = -1f; // 拖拽中的暂存值，Repaint 时同步到 _splitterPos
        private bool _isDraggingSplitter = false;

        private Vector2 _scroll;

        // ── 内嵌 Character Setup 工具状态 ──────────────────────────────
        private AnimatorController _charSetupSourceController;
        private DefaultAsset _charSetupFolder;
        private AnimationClip _charSetupIdleClip;
        private AnimationClip _charSetupRunClip;
        private AnimationClip _charSetupRunFastClip;
        private string _charSetupFolderPath;
        private string _charSetupName;
        private string _charSetupPrefix;
        private string _charSetupModelFBXPath;
        private string _charSetupControllerPath;
        private string _charSetupOverridePath;
        private string _charSetupPrefabPath;
        private string _charSetupAnimSetPath;
        private string _charSetupLoadedFolderPath;
        // 动画集（模板+覆盖模式）
        private CharacterAnimSetAsset _charSetupTemplateAnimSet;  // 拖入的已有动画集（替换默认），null=用默认预设
        private CharacterAnimSetAsset _charSetupEditAnimSet;     // 当前正在编辑的动画集（从模板克隆的副本）
        private bool _charSetupAnimDirty = false;                // 动画集是否有未保存修改
        private List<string> _charSetupLogMessages = new List<string>();

        // ── 内嵌 Enemy Setup 工具状态 ──────────────────────────────
        private AnimatorController _enemySetupSourceController;
        private DefaultAsset _enemySetupFolder;
        private AnimationClip _enemySetupIdleClip;
        private AnimationClip _enemySetupWalkClip;
        private AnimationClip _enemySetupHitClip;
        private AnimationClip _enemySetupDeathClip;
        private AnimationClip[] _enemySetupAttackClips = new AnimationClip[4];
        private int _enemySetupAttackCount = 1;
        private EnemyAnimSetAsset _enemySetupAnimSet;
        // 2026-07-21: 角色模板选择（-1=自定义, 0=默认Melee, ...）
        private int _selectedEnemyTemplate = 0;
        private string _enemySetupFolderPath;
        private string _enemySetupLastScannedFolder; // 跟踪上次扫描的文件夹，用于检测变更触发自动扫描
        private string _enemySetupName;
        private string _enemySetupPrefix;
        private string _enemySetupModelFBXPath;
        private string _enemySetupControllerPath;
        private string _enemySetupPrefabPath;
        private List<string> _enemySetupLogMessages = new List<string>();

        // ── 内嵌 Ragdoll Setup 状态 ──────────────────────────────
        private GameObject _ragdollTarget;
        private List<string> _ragdollLogMessages = new List<string>();
        private Vector2 _ragdollLogScroll;
#pragma warning disable CS0414
        private bool _ragdollLastSuccess = false;
#pragma warning restore CS0414
        // Ragdoll 参数（独立执行时使用，修改后实时生效）
        private float _ragdollGlobalMassScale = 1.0f;          // 全局质量倍率（0.1~3.0）
        private float _ragdollGlobalDrag = 0.1f;               // Rigidbody Drag
        private float _ragdollGlobalAngularDrag = 0.5f;        // Rigidbody Angular Drag
        private bool _ragdollCollidersAsTrigger = true;        // Collider 是否设为 Trigger（默认 true，运行时由 vRagdoll 控制）
        private bool _ragdollDetectCollisions = false;         // Rigidbody 是否启用碰撞检测（默认 false）
        private bool _ragdollShowAdvancedParams = false;       // 高级参数折叠

        // ── 内嵌 SpringBone Setup 状态 ──────────────────────────────
        // 目标角色
        private GameObject _springBoneTarget;
        // 摆动骨骼链（按 (rootName, chainLength, stiffness, damping, gravityX, gravityY, gravityZ, radius) 持久化）
        private List<SpringBoneChainState> _springBoneChains = new List<SpringBoneChainState>();
        // 球形碰撞体（按 (boneName, radius) 持久化）
        private List<SpringBoneColliderState> _springBoneColliders = new List<SpringBoneColliderState>();
        // 全局参数
        private float _springBoneGlobalStiffness = 0.3f;
        private float _springBoneGlobalDamping = 0.15f;
        private Vector3 _springBoneGlobalGravity = new Vector3(0f, -0.003f, 0f);
        private float _springBoneGlobalRadius = 0.03f;
        // 添加字段
        private Transform _springBonePendingRoot;
        private Transform _springBonePendingCollider;
        // UI 折叠
        private bool _springBoneShowChains = true;
        private bool _springBoneShowColliders = true;
        private bool _springBoneShowParams = true;
        // 日志
        private List<string> _springBoneLogMessages = new List<string>();
        private Vector2 _springBoneLogScroll;
        private Vector2 _springBonePanelScroll;

        [System.Serializable]
        private class SpringBoneChainState
        {
            public Transform root;
            public int chainLength;
            public float stiffness;
            public float damping;
            public Vector3 gravity;
            public float radius;
            public bool foldout = true;
        }
        [System.Serializable]
        private class SpringBoneColliderState
        {
            public Transform bone;
            public float radius;
        }

        // ── 内嵌 Weapon Switcher Setup 状态 ──────────────────────────────
        private GameObject _weaponTarget;
        private WeaponSwitcher _weaponSwitcher;
        private WeaponHolder _weaponHolder;
        private SerializedObject _weaponSwitcherSO;
        private SerializedObject _weaponHolderSO;
        private SerializedProperty _weaponListProp;
        private SerializedProperty _weaponStartIndexProp;
        private SerializedProperty _weaponNextKeyProp;
        private SerializedProperty _weaponPrevKeyProp;
        private SerializedProperty _weaponUseScrollProp;
        private SerializedProperty _weaponSwitchAnimProp;
        private SerializedProperty _weaponSwitchDurationProp;
        private SerializedProperty _weaponAllowCombatProp;
        private SerializedProperty _weaponHolderMountProp;
        private SerializedProperty _weaponHolderInitMainProp;
        private SerializedProperty _weaponHolderInitOffProp;
        private Vector2 _weaponPanelScroll;
        private List<string> _weaponLogMessages = new List<string>();
        private Vector2 _weaponLogScroll;

        // ── 内嵌 Diagnostic 状态 ──────────────────────────────
        private GameObject _diagTarget;
        private List<string> _diagLogMessages = new List<string>();
        private Vector2 _diagLogScroll;
        private bool _diagLastSuccess = false;

        // ── CK-02：目标来源追踪（标记共享步骤目标是否来自流水线自动传播）─
        private bool _ragdollTargetAuto = false;
        private bool _springTargetAuto = false;
        private bool _weaponTargetAuto = false;
        private bool _diagTargetAuto = false;

        // ── Session 持久化追踪 ──────────────────────────────────────
        private int _lastSavedTabIndex = -1;
        private float _lastSavedSplitterPos = -1f;
        private int _lastSavedPipelineTarget = -1;
        private int _lastSavedStepIndex = -2;
        private float _lastSavedSetupScrollY = -1f;
        private string _lastSavedCharFolderPath = null;
        private string _lastSavedEnemyFolderPath = null;

        public void OnGUI()
        {
            // ── P2.2：首次加载会话持久化状态 ──────────────────────
            if (!_sessionLoaded)
            {
                var session = CharacterKitSession.instance;
                _tab = (Tab)session.selectedTabIndex;
                _splitterPos = session.splitterPos;
                _setupTarget = (SetupPipeline.PipelineTarget)session.pipelineTargetIndex;
                _selectedStepIndex = session.selectedStepIndex;
                _setupScroll.y = session.setupScrollY;
                _lastSavedTabIndex = session.selectedTabIndex;
                _lastSavedSplitterPos = session.splitterPos;
                _lastSavedPipelineTarget = session.pipelineTargetIndex;
                _lastSavedStepIndex = session.selectedStepIndex;
                _lastSavedSetupScrollY = session.setupScrollY;
                // 恢复装配步骤拖入的资源文件夹（编译重载后私有字段被清空，从会话还原）
                _charSetupFolderPath = session.charSetupFolderPath;
                _enemySetupFolderPath = session.enemySetupFolderPath;
                if (_charSetupFolder == null && !string.IsNullOrEmpty(_charSetupFolderPath))
                    _charSetupFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_charSetupFolderPath);
                if (_enemySetupFolder == null && !string.IsNullOrEmpty(_enemySetupFolderPath))
                    _enemySetupFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_enemySetupFolderPath);
                _lastSavedCharFolderPath = session.charSetupFolderPath;
                _lastSavedEnemyFolderPath = session.enemySetupFolderPath;
                _sessionLoaded = true;
            }

            DrawSubTabs();
            EditorGUILayout.Space(6);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                switch (_tab)
                {
                    case Tab.Skill: DrawSkillTab(); break;
                    case Tab.Setup: DrawSetupTab(); break;
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndScrollView());
            }

            // ── P2.2+P3.3：有变化时持久化到 ScriptableSingleton ─────────
            // P3.1：仅在 Repaint 事件中持久化（值在 Layout 后确定，避免每帧多次写入）
            if (Event.current.type == EventType.Repaint)
            {
                int currentTabIndex = (int)_tab;
                int currentPipelineTarget = (int)_setupTarget;
                float currentSetupScrollY = _setupScroll.y;
                string currentCharFolderPath = _charSetupFolder != null
                    ? AssetDatabase.GetAssetPath(_charSetupFolder) : (_charSetupFolderPath ?? "");
                string currentEnemyFolderPath = _enemySetupFolder != null
                    ? AssetDatabase.GetAssetPath(_enemySetupFolder) : (_enemySetupFolderPath ?? "");
                if (currentTabIndex != _lastSavedTabIndex
                    || !Mathf.Approximately(_splitterPos, _lastSavedSplitterPos)
                    || currentPipelineTarget != _lastSavedPipelineTarget
                    || _selectedStepIndex != _lastSavedStepIndex
                    || !Mathf.Approximately(currentSetupScrollY, _lastSavedSetupScrollY)
                    || currentCharFolderPath != _lastSavedCharFolderPath
                    || currentEnemyFolderPath != _lastSavedEnemyFolderPath)
                {
                    var session = CharacterKitSession.instance;
                    session.selectedTabIndex = currentTabIndex;
                    session.splitterPos = _splitterPos;
                    session.pipelineTargetIndex = currentPipelineTarget;
                    session.selectedStepIndex = _selectedStepIndex;
                    session.setupScrollY = currentSetupScrollY;
                    session.charSetupFolderPath = currentCharFolderPath;
                    session.enemySetupFolderPath = currentEnemyFolderPath;
                    session.Save();
                    _lastSavedTabIndex = currentTabIndex;
                    _lastSavedSplitterPos = _splitterPos;
                    _lastSavedPipelineTarget = currentPipelineTarget;
                    _lastSavedStepIndex = _selectedStepIndex;
                    _lastSavedSetupScrollY = currentSetupScrollY;
                    _lastSavedCharFolderPath = currentCharFolderPath;
                    _lastSavedEnemyFolderPath = currentEnemyFolderPath;
                }
            }
        }

        /// <summary>绘制顶部子标签栏（装配 Setup / 技能 Skill）。</summary>
        void DrawSubTabs()
        {
            EditorGUILayout.BeginHorizontal();
            try
            {
                DrawSubTabButton(Tab.Setup, "装配 Setup");
                DrawSubTabButton(Tab.Skill, "技能 Skill");
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }
        }

        /// <summary>绘制单个子标签按钮，激活时高亮显示。</summary>
        void DrawSubTabButton(Tab t, string label)
        {
            bool active = _tab == t;
            var prev = GUI.backgroundColor;
            if (active) GUI.backgroundColor = new Color(0.35f, 0.55f, 0.9f);
            if (GUILayout.Button(label, GUILayout.Height(24)))
                _tab = t;
            GUI.backgroundColor = prev;
        }

        /// <summary>绘制 EnemyArchetype 配方的速览信息卡片。</summary>
        void DrawArchetypePreview(EnemyArchetype arch)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("配方速览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"名称: {arch.archetypeName}");
            EditorGUILayout.LabelField($"分类: {arch.enemyClass}");
            EditorGUILayout.LabelField($"感知范围: {arch.detectionRange:F1}m / 角度: {arch.detectionAngle:F0}°");
            EditorGUILayout.LabelField($"攻击范围: {arch.attackRange:F1}m / 伤害: {arch.attackDamage:F0} / CD: {arch.attackCooldown:F1}s");
            EditorGUILayout.LabelField($"掉落: {(arch.dropTable != null ? arch.dropTable.Length : 0)} 项");
        }

        /// <summary>从当前 EnemyAI 提取参数生成 EnemyArchetype 资产文件。</summary>
        void ExtractEnemyArchetype()
        {
            if (_enemyAI == null) return;

            var data = ScriptableObject.CreateInstance<EnemyArchetype>();
            data.archetypeName = _enemyTarget.name + " 配方";
            data.CopyFrom(_enemyAI);

            string dir = "Assets/_Game/character/Common/EnemyArchetypes";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets/_Game/character/Common", "EnemyArchetypes");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/Archetype_{_enemyTarget.name}.asset");
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(data);
            Debug.Log($"[CharacterKit] 已生成 EnemyArchetype: {path}");
        }

        /// <summary>将已关联的 EnemyArchetype 应用到 EnemyAI 组件。</summary>
        void ApplyEnemyArchetype()
        {
            if (_enemyAI == null) return;

            var archetype = _enemyAI.archetype;
            if (archetype == null) return;

            Undo.RecordObject(_enemyAI, "Apply EnemyArchetype");
            archetype.ApplyTo(_enemyAI);
            EditorUtility.SetDirty(_enemyAI);
            Debug.Log($"[CharacterKit] 已应用 EnemyArchetype '{archetype.archetypeName}' 到 {_enemyAI.name}");
        }

        /// <summary>从当前 EnemyAI 提取参数生成 EnemyBehaviorData 资产文件（兼容模式）。</summary>
        void ExtractEnemyBehavior()
        {
            var data = ScriptableObject.CreateInstance<EnemyBehaviorData>();
            data.CopyFrom(_enemyAI);
            string dir = "Assets/_Game/character/Common/EnemyBehaviors";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets/_Game/character/Common", "EnemyBehaviors");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/EnemyBehavior_{_enemyTarget.name}.asset");
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(data);
            Debug.Log($"[CharacterKit] 已生成 {path}");
        }

        /// <summary>将已关联的 EnemyBehaviorData 应用到 EnemyAI 组件（兼容模式）。</summary>
        void ApplyEnemyBehavior()
        {
            var so = new SerializedObject(_enemyAI);
            var data = so.FindProperty("behaviorData").objectReferenceValue as EnemyBehaviorData;
            if (data == null) return;
            Undo.RecordObject(_enemyAI, "Apply EnemyBehaviorData");
            data.ApplyTo(_enemyAI);
            EditorUtility.SetDirty(_enemyAI);
        }

    }
}
#endif