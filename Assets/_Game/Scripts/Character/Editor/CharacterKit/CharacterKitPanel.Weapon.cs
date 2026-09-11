#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Game.Character;
using Game.SkillSystem;
using System.Collections.Generic;

namespace Game.Character.EditorTools.CharacterKit
{
    public partial class CharacterKitPanel
    {
        // ═══════════════════════════════════════════════════════════════
        // P4.2：GUIContent 静态缓存，避免 OnGUI 每帧 new GUIContent 产生 GC
        // ═══════════════════════════════════════════════════════════════
        static readonly GUIContent GCLWpn_StartIndex    = new GUIContent("起始武器索引");
        static readonly GUIContent GCLWpn_NextKey       = new GUIContent("切换下一武器键");
        static readonly GUIContent GCLWpn_PrevKey       = new GUIContent("切换上一武器键");
        static readonly GUIContent GCLWpn_UseScroll     = new GUIContent("鼠标滚轮切换");
        static readonly GUIContent GCLWpn_AllowCombat   = new GUIContent("战斗模式中允许换武器");
        static readonly GUIContent GCLWpn_SwitchAnim    = new GUIContent("Animator 触发器名");
        static readonly GUIContent GCLWpn_SwitchDuration= new GUIContent("切换持续时间（秒）");
        static readonly GUIContent GCLWpn_MountConfig   = new GUIContent("挂载点配置");
        static readonly GUIContent GCLWpn_InitMain      = new GUIContent("主手初始武器");
        static readonly GUIContent GCLWpn_InitOff       = new GUIContent("副手初始武器");

        /// <summary>绘制 Weapon Setup 独立面板：武器列表 + 输入配置 + 挂载点 + 运行时测试。</summary>
        void DrawWeaponSetupPanel(SetupPipeline.StepState state)
        {
            EditorGUILayout.LabelField("⑤ Weapon 武器挂载", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "为角色 Prefab 设置 WeaponSwitcher 武器挂载系统，\n" +
                "配置主手/副手武器槽位和切换逻辑。",
                MessageType.Info);
            EditorGUILayout.Space(6);

            // ── 目标选择 ────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField("目标角色", GUILayout.Width(70));
                var newTarget = (GameObject)EditorGUILayout.ObjectField(
                    _weaponTarget, typeof(GameObject), true);
                if (newTarget != _weaponTarget)
                {
                    _weaponTarget = newTarget;
                    OnSharedTargetManuallyChanged(newTarget, ref _weaponTargetAuto);
                    WeaponRefreshComponents();
                }
                if (GUILayout.Button("→", GUILayout.Width(28)))
                {
                    _weaponTarget = Selection.activeGameObject;
                    OnSharedTargetManuallyChanged(Selection.activeGameObject, ref _weaponTargetAuto);
                    WeaponRefreshComponents();
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            // ── CK-02：自动来源指示器 ──
            if (_weaponTarget != null && _weaponTargetAuto)
            {
                EditorGUILayout.LabelField("  ⟳ 自动绑定（来自流水线前置步骤）", EditorStyles.miniLabel);
            }

            if (_weaponTarget == null)
            {
                EditorGUILayout.LabelField("  └─ 拖入 Hierarchy 中的角色 GameObject", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.Space(6);

            // ── 自动添加组件 ────────────────────────────────────────
            if (_weaponHolder == null || _weaponSwitcher == null)
            {
                EditorGUILayout.HelpBox("角色缺少必要组件，点击下方按钮自动添加", MessageType.Warning);
                EditorGUILayout.Space(2);
                if (GUILayout.Button("⚡ 自动添加 WeaponHolder + WeaponSwitcher", GUILayout.Height(30)))
                {
                    Undo.RecordObject(_weaponTarget, "Add Weapon Switcher");
                    if (_weaponHolder == null)
                        _weaponHolder = Undo.AddComponent<WeaponHolder>(_weaponTarget);
                    if (_weaponSwitcher == null)
                        _weaponSwitcher = Undo.AddComponent<WeaponSwitcher>(_weaponTarget);
                    WeaponRefreshComponents();
                    EditorUtility.SetDirty(_weaponTarget);
                    _weaponLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ✓ 组件已添加");
                }
                EditorGUILayout.Space(4);
            }

            // ── 组件状态 ────────────────────────────────────────────
            WeaponDrawStatusRow("WeaponHolder", _weaponHolder != null);
            WeaponDrawStatusRow("WeaponSwitcher", _weaponSwitcher != null);
            var skillCtrl = _weaponTarget.GetComponent<SkillController>();
            WeaponDrawStatusRow("SkillController（可选，战斗模式联动）", skillCtrl != null);
            var animator = _weaponTarget.GetComponent<Animator>();
            WeaponDrawStatusRow("Animator（用于换武器动画）", animator != null);

            if (_weaponSwitcher == null)
            {
                EditorGUILayout.HelpBox("需要先有 WeaponSwitcher 组件才能编辑配置。", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(6);
            _weaponPanelScroll = EditorGUILayout.BeginScrollView(_weaponPanelScroll, GUILayout.ExpandHeight(true));
            try
            {

            // ── 武器列表 ────────────────────────────────────────────
            EditorGUILayout.LabelField(
                $"武器列表 ({(_weaponListProp != null ? _weaponListProp.arraySize : 0)} 把)",
                EditorStyles.boldLabel);

            if (_weaponListProp != null)
            {
                _weaponSwitcherSO.Update();
                EditorGUILayout.PropertyField(_weaponListProp, true);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            try
            {
                if (_weaponStartIndexProp != null)
                {
                    EditorGUILayout.PropertyField(_weaponStartIndexProp, GCLWpn_StartIndex);
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ 创建新 WeaponData（ScriptableObject）", GUILayout.Height(22)))
            {
                WeaponCreateNewData();
            }

            EditorGUILayout.Space(8);

            // ── 输入配置 ────────────────────────────────────────────
            EditorGUILayout.LabelField("输入配置", EditorStyles.boldLabel);
            if (_weaponNextKeyProp != null) EditorGUILayout.PropertyField(_weaponNextKeyProp, GCLWpn_NextKey);
            if (_weaponPrevKeyProp != null) EditorGUILayout.PropertyField(_weaponPrevKeyProp, GCLWpn_PrevKey);
            if (_weaponUseScrollProp != null) EditorGUILayout.PropertyField(_weaponUseScrollProp, GCLWpn_UseScroll);
            if (_weaponAllowCombatProp != null) EditorGUILayout.PropertyField(_weaponAllowCombatProp, GCLWpn_AllowCombat);

            EditorGUILayout.Space(6);

            // ── 换武器动画 ──────────────────────────────────────────
            EditorGUILayout.LabelField("换武器动画", EditorStyles.boldLabel);
            if (_weaponSwitchAnimProp != null) EditorGUILayout.PropertyField(_weaponSwitchAnimProp, GCLWpn_SwitchAnim);
            if (_weaponSwitchDurationProp != null) EditorGUILayout.PropertyField(_weaponSwitchDurationProp, GCLWpn_SwitchDuration);

            if (_weaponSwitchAnimProp != null && !string.IsNullOrEmpty(_weaponSwitchAnimProp.stringValue))
            {
                if (animator == null)
                {
                    EditorGUILayout.HelpBox("角色没有 Animator 组件，动画触发器无效", MessageType.Warning);
                }
            }

            // ── 挂载点（来自 WeaponHolder） ─────────────────────────
            if (_weaponHolder != null && _weaponHolderMountProp != null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("武器挂载点", EditorStyles.boldLabel);
                _weaponHolderSO.Update();
                EditorGUILayout.PropertyField(_weaponHolderMountProp, GCLWpn_MountConfig, true);
                if (_weaponHolderSO.ApplyModifiedProperties())
                    EditorUtility.SetDirty(_weaponHolder);
                EditorGUILayout.HelpBox(
                    "挂载点优先使用 Humanoid 标准骨骼映射。\n" +
                    "非标准骨骼（Bip001 / mixamorig 等）请填写 customBoneName。",
                    MessageType.Info);
            }

            // ── 初始装备（WeaponHolder.initialMainHandWeapon / initialOffHandWeapon）──
            if (_weaponHolder != null && _weaponHolderInitMainProp != null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("初始装备（场景开始自动装备）", EditorStyles.boldLabel);
                _weaponHolderSO.Update();
                EditorGUILayout.PropertyField(_weaponHolderInitMainProp, GCLWpn_InitMain);
                EditorGUILayout.PropertyField(_weaponHolderInitOffProp, GCLWpn_InitOff);
                if (_weaponHolderSO.ApplyModifiedProperties())
                    EditorUtility.SetDirty(_weaponHolder);
                EditorGUILayout.HelpBox(
                    "留空 = 角色空手生成（可运行时动态装备）。\n" +
                    "WeaponData 资产请通过 \"+ 创建新 WeaponData\" 按钮创建。",
                    MessageType.Info);
            }

            // 写入修改
            if (_weaponSwitcherSO != null && _weaponSwitcherSO.ApplyModifiedProperties())
                EditorUtility.SetDirty(_weaponTarget);
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndScrollView());
            }

            // ── 运行时测试 ──────────────────────────────────────────
            if (Application.isPlaying && _weaponSwitcher != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("运行时测试", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"当前武器：{_weaponSwitcher.CurrentIndex} — {_weaponSwitcher.CurrentWeapon?.weaponName ?? "无"}");
                EditorGUILayout.LabelField($"换武器中：{_weaponSwitcher.IsSwitching}");

                EditorGUILayout.BeginHorizontal();
                try
                {
                    if (GUILayout.Button("← 上一把"))
                        _weaponSwitcher.TrySwitchWeapon(-1);
                    if (GUILayout.Button("下一把 →"))
                        _weaponSwitcher.TrySwitchWeapon(1);
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                }

                if (_weaponSwitcher.CurrentWeapon != null)
                {
                    var w = _weaponSwitcher.CurrentWeapon;
                    EditorGUILayout.HelpBox(
                        $"武器：{w.weaponName}\n伤害倍率：{w.damageMultiplier:F2}  范围+{w.rangeBonus:F2}  攻速：{w.attackSpeedMultiplier:F2}",
                        MessageType.None);
                }
            }

            // ── 日志 ────────────────────────────────────────────────
            if (_weaponLogMessages.Count > 0)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("执行日志", EditorStyles.boldLabel);
                float h = Mathf.Min(_weaponLogMessages.Count * 20 + 20, 80);
                _weaponLogScroll = EditorGUILayout.BeginScrollView(_weaponLogScroll, GUILayout.Height(h));
                try
                {
                    foreach (var msg in _weaponLogMessages)
                    {
                        if (msg.StartsWith("❌"))
                            EditorGUILayout.HelpBox(msg, MessageType.Error);
                        else if (msg.StartsWith("⚠"))
                            EditorGUILayout.HelpBox(msg, MessageType.Warning);
                        else
                            EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndScrollView());
                }
            }

            // 标记完成
            if (_weaponHolder != null && _weaponSwitcher != null)
            {
                state.status = SetupPipeline.StepStatus.Completed;
                state.message = "武器组件就绪";
            }
        }

        /// <summary>刷新武器目标对象的组件引用和 SerializedProperty 缓存。</summary>
        void WeaponRefreshComponents()
        {
            _weaponSwitcher = null;
            _weaponHolder = null;
            _weaponSwitcherSO = null;
            _weaponHolderSO = null;
            _weaponListProp = null;
            _weaponStartIndexProp = null;
            _weaponNextKeyProp = null;
            _weaponPrevKeyProp = null;
            _weaponUseScrollProp = null;
            _weaponSwitchAnimProp = null;
            _weaponSwitchDurationProp = null;
            _weaponAllowCombatProp = null;
            _weaponHolderMountProp = null;
            _weaponHolderInitMainProp = null;
            _weaponHolderInitOffProp = null;

            if (_weaponTarget == null) return;

            _weaponHolder = _weaponTarget.GetComponent<WeaponHolder>();
            _weaponSwitcher = _weaponTarget.GetComponent<WeaponSwitcher>();

            if (_weaponSwitcher != null)
            {
                _weaponSwitcherSO = new SerializedObject(_weaponSwitcher);
                _weaponListProp = _weaponSwitcherSO.FindProperty("weaponList");
                _weaponStartIndexProp = _weaponSwitcherSO.FindProperty("startWeaponIndex");
                _weaponNextKeyProp = _weaponSwitcherSO.FindProperty("nextWeaponKey");
                _weaponPrevKeyProp = _weaponSwitcherSO.FindProperty("prevWeaponKey");
                _weaponUseScrollProp = _weaponSwitcherSO.FindProperty("useScrollWheel");
                _weaponSwitchAnimProp = _weaponSwitcherSO.FindProperty("switchAnimTrigger");
                _weaponSwitchDurationProp = _weaponSwitcherSO.FindProperty("switchDuration");
                _weaponAllowCombatProp = _weaponSwitcherSO.FindProperty("allowSwitchInCombat");
            }

            if (_weaponHolder != null)
            {
                _weaponHolderSO = new SerializedObject(_weaponHolder);
                _weaponHolderMountProp = _weaponHolderSO.FindProperty("mountPoints");
                _weaponHolderInitMainProp = _weaponHolderSO.FindProperty("initialMainHandWeapon");
                _weaponHolderInitOffProp = _weaponHolderSO.FindProperty("initialOffHandWeapon");
            }
        }

        /// <summary>绘制武器组件状态行（✓/✗ + 组件名）。</summary>
        void WeaponDrawStatusRow(string label, bool found)
        {
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField(found ? "✓" : "✗",
                    found ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.Width(18));
                EditorGUILayout.LabelField(label);
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }
        }

        /// <summary>创建新的 WeaponData ScriptableObject 资产。</summary>
        void WeaponCreateNewData()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "创建 WeaponData",
                "NewWeapon",
                "asset",
                "选择保存位置",
                "Assets/_Game");

            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance<WeaponData>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;
            _weaponLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ✓ 已创建 WeaponData：{path}");
        }
    }
}
#endif
