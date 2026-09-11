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
        // P4.2：GUIContent 静态缓存，避免 OnGUI 每帧 new GUIContent 产生 GC
        // ═══════════════════════════════════════════════════════════════
        static readonly GUIContent GCLEnemy_CtrlTemplate = new GUIContent("Controller 模板", "作为模板的 Animator Controller，复制后替换动画");
        static readonly GUIContent GCLEnemy_AnimSetTemplate = new GUIContent("AnimSet 模板", "敌人动画集 ScriptableObject 资产。\n默认会自动填充 DefaultEnemyAnimSet。\n可拖入自己的 AnimSet 资产替换。");
        static readonly GUIContent GCLEnemy_ResourcePath = new GUIContent("资源路径", "包含敌人主模型 FBX（无 @ 前缀）和所有 @动画 FBX（@Idle、@Attack_1 等）的文件夹。\n拖入或切换文件夹后会自动扫描目录内所有 @动画 FBX，并填充到下方动画字段。");

        /// <summary>绘制 Enemy Setup 独立面板：5 段单步完成（不再分 Part1/Part2）。</summary>
        /// <remarks>
        /// 5 段流程：
        ///   ① 配置三件套（Controller 模板 / AnimSet 模板 / 资源路径）
        ///   ② 基础动画（Idle / Walk / Hit / Death）
        ///   ③ 攻击动画组（Attack 1~N）
        ///   ④ 技能动画组（Skill Q / W / E / R，各支持多段 + 衔接）
        ///   ⑤ 通用过渡动画组
        ///   ──── 单按钮「确认执行」一步完成：复制 Controller → 替换基础/攻击/技能/过渡动画 → 装配 Prefab
        ///   ──── 路径自动继承（资源路径仅需设置一次）
        /// </remarks>
        void DrawEnemySetupPanel(SetupPipeline.StepState state)
        {
            // ── 2026-07-21: 角色模板一键预填（Controller + AnimSet）──
            DrawEnemyTemplateSelector();

            // ── 首次打开自动指认默认 Animator Controller ───────────────
            if (_enemySetupSourceController == null)
                _enemySetupSourceController = DefaultConfigCreator.GetOrCreateDefaultEnemyController();

            // ── 首次打开自动指认默认 AnimSet 模板 ─────────────────────
            if (_enemySetupAnimSet == null)
                _enemySetupAnimSet = DefaultConfigCreator.GetOrCreateDefaultEnemyAnimSet();

            EditorGUILayout.LabelField("② 敌人 Prefab 装配（5 段单步完成）", StyleTitle14);
            EditorGUILayout.Space(4);

            // ═══════════════════════════════════════════════════════════
            //  ① 配置三件套：Controller 模板 / AnimSet 模板 / 资源路径
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("① 配置三件套（Controller 模板 / AnimSet 模板 / 资源路径）", () =>
            {
                // ── 1.1 Controller 模板 ──────────────────────────────────
                _enemySetupSourceController = (AnimatorController)EditorGUILayout.ObjectField(
                    GCLEnemy_CtrlTemplate,
                    _enemySetupSourceController, typeof(AnimatorController), false);

                // ── 1.2 AnimSet 模板（默认自动填充 DefaultEnemyAnimSet）──
                EditorGUI.BeginChangeCheck();
                _enemySetupAnimSet = (EnemyAnimSetAsset)EditorGUILayout.ObjectField(
                    GCLEnemy_AnimSetTemplate,
                    _enemySetupAnimSet, typeof(EnemyAnimSetAsset), false);
                if (EditorGUI.EndChangeCheck() && _enemySetupAnimSet == null)
                    _enemySetupAnimSet = DefaultConfigCreator.GetOrCreateDefaultEnemyAnimSet();

                // ── 1.3 资源路径（拖入后自动扫描 @动画 FBX 并填充）───────
                EditorGUI.BeginChangeCheck();
                _enemySetupFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    GCLEnemy_ResourcePath,
                    _enemySetupFolder, typeof(DefaultAsset), false);
                bool folderChanged = EditorGUI.EndChangeCheck();
                if (_enemySetupFolder != null)
                {
                    string folderPath = AssetDatabase.GetAssetPath(_enemySetupFolder);

                    // 首次拖入 / 切换文件夹 → 自动扫描填充
                    if (folderChanged || _enemySetupLastScannedFolder != folderPath)
                    {
                        AutoFillEnemySetupFromFolder();
                        _enemySetupLastScannedFolder = folderPath;
                    }

                    // 手动重新扫描按钮（保留入口，避免自动扫描漏掉新文件时无法重扫）
                    if (GUILayout.Button("🔍 重新扫描文件夹并刷新动画", GUILayout.Height(20)))
                    {
                        AutoFillEnemySetupFromFolder();
                        _enemySetupLastScannedFolder = folderPath;
                    }
                }

                // ── 当前角色状态条（路径自动从 _enemySetupFolder 继承） ──
                if (!string.IsNullOrEmpty(_enemySetupName))
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.BeginVertical(StyleHelpBox);
                    try
                    {
                        var prevC = GUI.contentColor;
                        GUI.contentColor = new Color(0.7f, 0.85f, 1.0f);
                        EditorGUILayout.LabelField($"📌 当前敌人：{_enemySetupName}", EditorStyles.boldLabel);
                        GUI.contentColor = prevC;
                        EditorGUILayout.LabelField($"  Controller: {(_enemySetupControllerPath ?? "<未生成>")}", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField($"  Prefab:     {(_enemySetupPrefabPath ?? "<未生成>")}", EditorStyles.miniLabel);
                    }
                    finally
                    {
                        SafeEndLayout(() => EditorGUILayout.EndVertical());
                    }
                    EditorGUILayout.Space(2);
                }
            });

            // ═══════════════════════════════════════════════════════════
            //  ③ 基础动画（Idle / Walk / Hit / Death）
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("② 基础动画（Idle / Walk / Hit / Death）", () =>
            {
                EditorGUILayout.LabelField("  留空则使用模板默认动画", EditorStyles.miniLabel);
                _enemySetupIdleClip = DrawSetupClipField("Idle（待机）", _enemySetupIdleClip);
                _enemySetupWalkClip = DrawSetupClipField("Walk（行走）", _enemySetupWalkClip);
                _enemySetupHitClip = DrawSetupClipField("Hit（受击）", _enemySetupHitClip);
                _enemySetupDeathClip = DrawSetupClipField("Death（死亡）", _enemySetupDeathClip);
            });

            // ═══════════════════════════════════════════════════════════
            //  ④ 攻击动画组（Attack 1~N，AnimSet.attackClips）
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("③ 攻击动画组（Attack 1~N，存到 AnimSet.attackClips）", () =>
            {
                System.Action markDirty = () => { if (_enemySetupAnimSet != null) EditorUtility.SetDirty(_enemySetupAnimSet); };

                if (_enemySetupAnimSet == null)
                {
                    EditorGUILayout.HelpBox("请先在 ① 中指定 AnimSet 模板。", MessageType.Warning);
                    return;
                }
                if (_enemySetupAnimSet.attackClips == null)
                    _enemySetupAnimSet.attackClips = new System.Collections.Generic.List<AnimationClip>();

                int atkSlots = _enemySetupAnimSet.attackClips.Count;
                int newAtkSlots = EditorGUILayout.IntSlider("攻击位数量", atkSlots, 0, 12);
                while (_enemySetupAnimSet.attackClips.Count < newAtkSlots)
                {
                    _enemySetupAnimSet.attackClips.Add(null);
                    markDirty();
                }
                while (_enemySetupAnimSet.attackClips.Count > newAtkSlots)
                {
                    _enemySetupAnimSet.attackClips.RemoveAt(_enemySetupAnimSet.attackClips.Count - 1);
                    markDirty();
                }

                for (int i = 0; i < _enemySetupAnimSet.attackClips.Count; i++)
                {
                    EditorGUI.BeginChangeCheck();
                    var newClip = (AnimationClip)EditorGUILayout.ObjectField(
                        $"  Attack {i + 1}", _enemySetupAnimSet.attackClips[i], typeof(AnimationClip), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _enemySetupAnimSet.attackClips[i] = newClip;
                        markDirty();
                    }
                }
            });

            // ═══════════════════════════════════════════════════════════
            //  ⑤ 技能动画组（Skill Q / W / E / R，各支持多段 + 衔接）
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("④ 技能动画组（Skill Q / W / E / R，存到 AnimSet.skills）", () =>
            {
                System.Action markDirty = () => { if (_enemySetupAnimSet != null) EditorUtility.SetDirty(_enemySetupAnimSet); };

                if (_enemySetupAnimSet == null)
                {
                    EditorGUILayout.HelpBox("请先在 ① 中指定 AnimSet 模板。", MessageType.Warning);
                    return;
                }
                if (_enemySetupAnimSet.skills == null)
                    _enemySetupAnimSet.skills = new System.Collections.Generic.List<CharacterAnimSequence>();

                int newSkillCount = EditorGUILayout.IntSlider("技能组数", _enemySetupAnimSet.skills.Count, 0, 8);
                while (_enemySetupAnimSet.skills.Count < newSkillCount)
                {
                    int idx = _enemySetupAnimSet.skills.Count;
                    string sid = AnimStateNaming.SkillSuffix(idx);
                    _enemySetupAnimSet.skills.Add(new CharacterAnimSequence
                    {
                        id = AnimStateNaming.SkillState(sid),
                        segments = new System.Collections.Generic.List<AnimationClip> { null }
                    });
                    markDirty();
                }
                while (_enemySetupAnimSet.skills.Count > newSkillCount)
                {
                    _enemySetupAnimSet.skills.RemoveAt(_enemySetupAnimSet.skills.Count - 1);
                    markDirty();
                }

                for (int i = 0; i < _enemySetupAnimSet.skills.Count; i++)
                {
                    string label = AnimStateNaming.SkillLabel(i);
                    DrawCharAnimSequenceEditor(_enemySetupAnimSet.skills[i], label, markDirty);
                }
            });

            // ═══════════════════════════════════════════════════════════
            //  ⑥ 通用过渡动画组
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("⑤ 通用过渡动画组（Idle↔Move、多段回待机等）", () =>
            {
                System.Action markDirty = () => { if (_enemySetupAnimSet != null) EditorUtility.SetDirty(_enemySetupAnimSet); };

                if (_enemySetupAnimSet == null)
                {
                    EditorGUILayout.HelpBox("请先在 ① 中指定 AnimSet 模板。", MessageType.Warning);
                    return;
                }
                if (_enemySetupAnimSet.blendTransitions == null)
                    _enemySetupAnimSet.blendTransitions = new System.Collections.Generic.List<BlendTransitionData>();

                int transCount = _enemySetupAnimSet.blendTransitions.Count;
                int newTransCount = EditorGUILayout.IntSlider("过渡组数", transCount, 0, 8);
                while (_enemySetupAnimSet.blendTransitions.Count < newTransCount)
                {
                    _enemySetupAnimSet.blendTransitions.Add(new BlendTransitionData());
                    markDirty();
                }
                while (_enemySetupAnimSet.blendTransitions.Count > newTransCount)
                {
                    _enemySetupAnimSet.blendTransitions.RemoveAt(_enemySetupAnimSet.blendTransitions.Count - 1);
                    markDirty();
                }

                for (int i = 0; i < _enemySetupAnimSet.blendTransitions.Count; i++)
                {
                    DrawBlendTransitionEditor(_enemySetupAnimSet.blendTransitions[i], i, markDirty);
                }
            });

            // ═══════════════════════════════════════════════════════════
            //  单按钮一站式完成：Controller → 替换全部动画 → Prefab → SkillData
            // ═══════════════════════════════════════════════════════════
            EditorGUILayout.Space(8);
            DrawSetupPart1ExecuteButton(
                CountEnemySetupPart1Ready(),
                "Source Controller + 角色文件夹 + 至少 1 个基础动画",
                () =>
                {
                    _enemySetupLogMessages.Clear();
                    ExecuteEnemySetup();
                },
                "确认执行");

            // ── 完成状态 ───────────────────────────────────────────────
            DrawEnemySetupCompleteBox();

            // ═══════════════════════════════════════════════════════════
            //  执行日志（已在 DrawSetupTab 外层 ScrollView 中，无需嵌套）
            // ═══════════════════════════════════════════════════════════
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("执行日志", EditorStyles.boldLabel);
            if (_enemySetupLogMessages.Count == 0)
            {
                EditorGUILayout.LabelField("  （暂无日志）", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var msg in _enemySetupLogMessages)
                {
                    if (msg.StartsWith("❌"))
                        EditorGUILayout.HelpBox(msg, MessageType.Error);
                    else if (msg.StartsWith("⚠"))
                        EditorGUILayout.HelpBox(msg, MessageType.Warning);
                    else
                        EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
                }
            }
        }

        /// <summary>统计 Enemy Setup Part 1 中已就绪的分组数量。</summary>
        int CountEnemySetupPart1Ready()
        {
            return CountSetupPart1Ready(_enemySetupSourceController, _enemySetupFolder,
                _enemySetupIdleClip != null || _enemySetupWalkClip != null ||
                _enemySetupHitClip != null || _enemySetupDeathClip != null);
        }

        // ═══════════════════════════════════════════════════════════════
        //  角色模板（2026-07-21: P2 简化实现）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>角色模板定义。</summary>
        static readonly (string name, string desc)[] _enemyTemplates = new[]
        {
            ("默认 (近战 Melee)", "DefaultEnemyController + DefaultEnemyAnimSet"),
        };

        /// <summary>渲染模板选择下拉框。选中模板后自动填充 Controller + AnimSet。</summary>
        void DrawEnemyTemplateSelector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("⚡ 角色模板（一键预填 Controller + AnimSet）", EditorStyles.boldLabel);

            var displayNames = new string[_enemyTemplates.Length + 1];
            displayNames[0] = "— 自定义 —";
            for (int i = 0; i < _enemyTemplates.Length; i++)
                displayNames[i + 1] = _enemyTemplates[i].name;

            int displayIndex = _selectedEnemyTemplate < 0 ? 0 : Mathf.Clamp(_selectedEnemyTemplate + 1, 0, displayNames.Length - 1);

            EditorGUI.BeginChangeCheck();
            int newDisplayIndex = EditorGUILayout.Popup("模板", displayIndex, displayNames);
            if (EditorGUI.EndChangeCheck() && newDisplayIndex != displayIndex)
            {
                _selectedEnemyTemplate = newDisplayIndex == 0 ? -1 : newDisplayIndex - 1;
                if (_selectedEnemyTemplate >= 0)
                {
                    ApplyEnemyTemplate(_selectedEnemyTemplate);
                    EnemySetupLog($"✨ 已应用角色模板: {_enemyTemplates[_selectedEnemyTemplate].name}");
                }
            }

            if (_selectedEnemyTemplate >= 0 && _selectedEnemyTemplate < _enemyTemplates.Length)
                EditorGUILayout.LabelField($"  {_enemyTemplates[_selectedEnemyTemplate].desc}", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        /// <summary>按模板索引自动填充 Controller + AnimSet。</summary>
        void ApplyEnemyTemplate(int templateIndex)
        {
            switch (templateIndex)
            {
                case 0: // 默认 Melee
                    _enemySetupSourceController = DefaultConfigCreator.GetOrCreateDefaultEnemyController();
                    _enemySetupAnimSet = DefaultConfigCreator.GetOrCreateDefaultEnemyAnimSet();
                    break;
            }
        }

        /// <summary>扫描敌人角色文件夹中的 @动画 FBX，自动填充动画字段。</summary>
        void AutoFillEnemySetupFromFolder()
        {
            if (_enemySetupFolder == null) return;
            if (_enemySetupAnimSet == null)
                _enemySetupAnimSet = DefaultConfigCreator.GetOrCreateDefaultEnemyAnimSet();
            if (_enemySetupAnimSet == null)
            {
                EnemySetupLog("❌ AnimSet 未加载，无法记录动画片段");
                return;
            }
            if (_enemySetupAnimSet.attackClips == null)
                _enemySetupAnimSet.attackClips = new System.Collections.Generic.List<AnimationClip>();

            string folderPath = AssetDatabase.GetAssetPath(_enemySetupFolder);

            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { folderPath });
            var fbxPaths = fbxGuids.Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".FBX", System.StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase));

            foreach (var path in fbxPaths)
            {
                string fileName = Path.GetFileNameWithoutExtension(path).ToLower();
                if (!fileName.Contains("@")) continue;

                string animName = fileName.Substring(fileName.IndexOf('@') + 1);

                if (AnimStateNaming.IsIdleClip(animName) && _enemySetupIdleClip == null)
                    _enemySetupIdleClip = LoadFirstClipFromFBX(path);
                else if (AnimStateNaming.IsMoveClip(animName) && _enemySetupWalkClip == null)
                    _enemySetupWalkClip = LoadFirstClipFromFBX(path);
                else if (AnimStateNaming.IsHitClip(animName) && _enemySetupHitClip == null)
                    _enemySetupHitClip = LoadFirstClipFromFBX(path);
                else if (AnimStateNaming.IsDeathClip(animName) && _enemySetupDeathClip == null)
                    _enemySetupDeathClip = LoadFirstClipFromFBX(path);
                else if (AnimStateNaming.IsAttackClip(animName))
                {
                    // 解析攻击下标，直接写入 AnimSet.attackClips
                    int atkIdx = TryParseEnemyAttackIndex(animName);
                    var atkClips = _enemySetupAnimSet?.attackClips;
                    if (atkClips == null) continue;
                    if (atkIdx < 0) atkIdx = atkClips.Count; // 追加到末尾
                    while (atkClips.Count <= atkIdx) atkClips.Add(null);
                    var scannedClip = LoadFirstClipFromFBX(path);
                    if (scannedClip != null)
                    {
                        // 扫描结果是事实来源：同一次扫描直接写入 AnimSet，不能因为默认
                        // AnimSet 预先有 null 槽位而跳过；这是首次扫描不记录、第二次才出现的根因。
                        atkClips[atkIdx] = scannedClip;
                        EditorUtility.SetDirty(_enemySetupAnimSet);
                    }
                }
            }

            EnemySetupLog("  自动填充完成");
            if (_enemySetupIdleClip != null) EnemySetupLog($"  Idle: {_enemySetupIdleClip.name}");
            if (_enemySetupWalkClip != null) EnemySetupLog($"  Walk: {_enemySetupWalkClip.name}");
            if (_enemySetupHitClip != null) EnemySetupLog($"  Hit: {_enemySetupHitClip.name}");
            if (_enemySetupDeathClip != null) EnemySetupLog($"  Death: {_enemySetupDeathClip.name}");
            if (_enemySetupAnimSet?.attackClips != null)
                for (int i = 0; i < _enemySetupAnimSet.attackClips.Count; i++)
                    if (_enemySetupAnimSet.attackClips[i] != null) EnemySetupLog($"  Attack {i + 1}: {_enemySetupAnimSet.attackClips[i].name}");
            RepaintCharacterKitWindow();
        }

        /// <summary>解析攻击动画名称中的数字下标。Attack01 / Attack_1 → 0 (index)。</summary>
        static int TryParseEnemyAttackIndex(string animName)
        {
            // 从末尾找连续数字
            int i = animName.Length - 1;
            while (i >= 0 && char.IsDigit(animName[i])) i--;
            if (i < 0 || i >= animName.Length - 1) return -1;
            string numStr = animName.Substring(i + 1);
            if (int.TryParse(numStr, out int n) && n > 0) return n - 1; // 0-indexed
            return -1;
        }

        // ── Enemy Setup AnimSet 加载辅助 ───────────────────────────────

        /// <summary>
        /// 从 _enemySetupAnimSet 的 attackClips 列表中加载到 _enemySetupAttackClips[]，
        /// 并自动设置攻击段数。
        /// </summary>
        void LoadEnemyClipsFromAnimSet()
        {
            if (_enemySetupAnimSet == null || _enemySetupAnimSet.attackClips == null) return;

            int validCount = 0;
            for (int i = 0; i < _enemySetupAnimSet.attackClips.Count && i < 6; i++)
                if (_enemySetupAnimSet.attackClips[i] != null) validCount = i + 1;

            if (validCount == 0)
            {
                EditorUtility.DisplayDialog("AnimSet 为空",
                    $"{_enemySetupAnimSet.name} 中没有已填充的攻击动画。\n请先在 AnimSet 资产中拖入 AnimationClip。",
                    "好的");
                return;
            }

            _enemySetupAttackCount = validCount;
            // 确保数组足够大
            if (_enemySetupAttackClips.Length < validCount)
                _enemySetupAttackClips = new AnimationClip[Mathf.Max(validCount, 4)];

            for (int i = 0; i < validCount; i++)
                _enemySetupAttackClips[i] = _enemySetupAnimSet.attackClips[i];

            EnemySetupLog($"从 AnimSet '{_enemySetupAnimSet.name}' 加载了 {validCount} 个攻击动画");
            RepaintCharacterKitWindow();
        }

        // ── Enemy Setup 日志辅助 ──────────────────────────────────────

        void EnemySetupLog(string msg) => SetupLog(_enemySetupLogMessages, "[EnemySetup]", msg);

        // ═══════════════════════════════════════════════════════════════
        // Enemy Setup 执行逻辑
        // ═══════════════════════════════════════════════════════════════

        /// <summary>统一执行 Enemy Setup：复制模板 Controller → 替换全部动画 → 装配 Prefab → 生成 SkillData。</summary>
        void ExecuteEnemySetup()
        {
            // 先建立目标上下文，后续 Preflight/变更计划不能依赖尚未初始化的名称和路径。
            _enemySetupFolderPath = _enemySetupFolder != null
                ? AssetDatabase.GetAssetPath(_enemySetupFolder) : null;
            _enemySetupName = string.IsNullOrEmpty(_enemySetupFolderPath)
                ? null : Path.GetFileName(_enemySetupFolderPath);
            if (string.IsNullOrEmpty(_enemySetupFolderPath) || string.IsNullOrEmpty(_enemySetupName))
            {
                EnemySetupLog("❌ 资源文件夹无效，无法执行 Enemy Setup");
                return;
            }

            EnemySetupLog("══════════════════════════════════");
            EnemySetupLog("  Enemy Setup：Controller + Prefab + SkillData 一站式生成");
            EnemySetupLog("══════════════════════════════════");

            try
            {
            EditorUtility.DisplayProgressBar("Enemy Setup", "步骤 1/7: 前置校验...", 0.05f);

            // ── P0-1：Preflight 前置校验 ──────────────────────────
            {
                var ctx = new PreflightValidator.EnemyContext
                {
                    sourceController = _enemySetupSourceController,
                    controllerPath = $"{AssetDatabase.GetAssetPath(_enemySetupFolder)}/{_enemySetupName}_Enemy.controller",
                    folderPath = AssetDatabase.GetAssetPath(_enemySetupFolder),
                    enemyName = Path.GetFileName(AssetDatabase.GetAssetPath(_enemySetupFolder)),
                    modelFBXPath = _enemySetupModelFBXPath,
                    animSet = _enemySetupAnimSet,
                    folderAsset = _enemySetupFolder,
                };
                var results = PreflightValidator.ValidateEnemy(ctx);
                foreach (var r in results)
                {
                    string icon = r.severity == PreflightValidator.Severity.Block ? "❌" :
                                  r.severity == PreflightValidator.Severity.Warning ? "⚠" : "";
                    EnemySetupLog($"  {icon} [{r.check}] {r.detail}");
                }
                if (PreflightValidator.HasBlocks(results))
                {
                    EnemySetupLog("  ⛔ 前置校验未通过，已中止执行");
                    return;
                }
                EnemySetupLog("  ✔ 前置校验通过");

                // ── P0 A3：变更计划预览 ──────────────────────────
                var targetCtx = BuildEnemyTargetContext();
                if (targetCtx != null)
                {
                    // 构建预期 SkillData 路径集合（用于过期检测）
                    var expectedPaths = new System.Collections.Generic.HashSet<string>();
                    string skillDataDir = $"{AssetDatabase.GetAssetPath(_enemySetupFolder)}/SkillData";
                    string enemyName = Path.GetFileName(
                        AssetDatabase.GetAssetPath(_enemySetupFolder));
                    for (int i = 1; i <= _enemySetupAttackCount; i++)
                    {
                        expectedPaths.Add(
                            $"{skillDataDir}/{enemyName}_Enemy_Attack_{i:D2}.asset");
                    }
                    ShowChangePlanForStep(targetCtx,
                        SetupPipeline.StepType.EnemySetup, expectedPaths);
                }
            }

            EditorUtility.DisplayProgressBar("Enemy Setup", "步骤 2/7: 扫描模型 FBX...", 0.15f);

            // ── 1. 查找主模型 ──────────────────────────────────────
            _enemySetupFolderPath = AssetDatabase.GetAssetPath(_enemySetupFolder);
            _enemySetupName = Path.GetFileName(_enemySetupFolderPath);

            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { _enemySetupFolderPath });
            var fbxPaths = fbxGuids.Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".FBX", System.StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            _enemySetupModelFBXPath = null;
            foreach (var path in fbxPaths)
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!fileName.Contains("@"))
                {
                    _enemySetupModelFBXPath = path;
                    _enemySetupPrefix = fileName;
                    break;
                }
            }

            if (string.IsNullOrEmpty(_enemySetupModelFBXPath))
            {
                EnemySetupLog("  找不到主模型 FBX（不含 @ 的文件）！");
                return;
            }

            EnemySetupLog($"  模型: {_enemySetupModelFBXPath}");
            EnemySetupLog($"  敌人名: {_enemySetupName} | 前缀: {_enemySetupPrefix}");

            EditorUtility.DisplayProgressBar("Enemy Setup", "步骤 3/7: 复制 Controller...", 0.30f);

            // ── 2. 设置 Humanoid ───────────────────────────────────
            var importer = AssetImporter.GetAtPath(_enemySetupModelFBXPath) as ModelImporter;
            if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            {
                EnemySetupLog("   设置 Humanoid Avatar...");
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }

            // ── 3. 复制 Controller ─────────────────────────────────
            _enemySetupControllerPath = $"{_enemySetupFolderPath}/{_enemySetupName}_Enemy.controller";

            if (_enemySetupSourceController == null)
            {
                EnemySetupLog("❌ 未指定模板 Animator Controller，请先拖入 Source Controller");
                return;
            }

            string sourceControllerPath = AssetDatabase.GetAssetPath(_enemySetupSourceController);
            // ── P0-2：DeleteAsset 前创建备份 ──────────────────────────
            AssetBackupService.BackupBeforeOverwrite(_enemySetupControllerPath);
            if (System.IO.File.Exists(Path.GetFullPath(_enemySetupControllerPath).Replace('/', '\\')))
                AssetDatabase.DeleteAsset(_enemySetupControllerPath);

            if (!AssetDatabase.CopyAsset(sourceControllerPath, _enemySetupControllerPath))
            {
                EnemySetupLog($"❌ 复制 Controller 失败（源：{sourceControllerPath}）");
                return;
            }
            AssetDatabase.Refresh();

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(_enemySetupControllerPath);
            if (controller == null)
            {
                EnemySetupLog("  无法加载 Controller");
                return;
            }

            if (!DefaultConfigCreator.ValidateEnemyController(controller, out string controllerReason))
            {
                EnemySetupLog($"  ❌ Controller 校验失败: {controllerReason}");
                EnemySetupLog("  删除损坏的模板资产后，系统会在下次装配时自动重建");
                return;
            }

            EditorUtility.DisplayProgressBar("Enemy Setup", "步骤 4/7: 绑定动画 Clip...", 0.45f);

            // ── 4. 替换全部动画（基础 + 攻击 + 技能，合并为一次遍历）────
            int totalBound = 0;
            for (int li = 0; li < controller.layers.Length; li++)
            {
                var layer = controller.layers[li];
                totalBound += BindEnemyClipsInSM(layer.stateMachine);
            }

            // 清除超出 AnimSet 范围的旧 Attack 状态
            int animSetAtkCount = GetEnemyAnimSetAttackCount();
            int maxAttackSlots = AnimStateNaming.AttackIndexMax;
            for (int i = animSetAtkCount; i < maxAttackSlots; i++)
            {
                string stateName = AnimStateNaming.AttackStateLegacy(i + 1);
                if (ClearEnemySetupMotionByName(controller, stateName))
                    EnemySetupLog($"  清除 {stateName}");
            }

            int removedInvalidTransitions = RemoveInvalidAnimatorTransitions(controller);
            if (removedInvalidTransitions > 0)
                EnemySetupLog($"  清理无效 Animator Transition: {removedInvalidTransitions} 条");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            EnemySetupLog($"  动画绑定: {totalBound} 个");
            if (_enemySetupIdleClip != null) EnemySetupLog($"    Idle → {_enemySetupIdleClip.name}");
            if (_enemySetupWalkClip != null) EnemySetupLog($"    Walk → {_enemySetupWalkClip.name}");
            if (_enemySetupHitClip != null) EnemySetupLog($"    Hit → {_enemySetupHitClip.name}");
            if (_enemySetupDeathClip != null) EnemySetupLog($"    Death → {_enemySetupDeathClip.name}");

            // ── 5. 装配 Prefab ───────────────────────────────────────
            EditorUtility.DisplayProgressBar("Enemy Setup", "步骤 5/7: 装配 Prefab...", 0.60f);

            _enemySetupPrefabPath = AssembleEnemySetupPrefab();
            if (string.IsNullOrEmpty(_enemySetupPrefabPath)) return;

            EditorUtility.DisplayProgressBar("Enemy Setup", "步骤 6/7: 生成 SkillData...", 0.75f);

            // ── 6. 生成 SkillData（含动画信息） ────────────────────
            // 先生成资产，再把对应引用写回 Prefab，确保 attackSkillDatas 可完整绑定。
            GenerateEnemySkillDataAssets();

            // ── 7. 同步 EnemyAI.attackClips / attackSkillDatas ───────
            if (!string.IsNullOrEmpty(_enemySetupPrefabPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_enemySetupPrefabPath);
                if (prefab != null)
                {
                    var enemyAI = prefab.GetComponent<EnemyAI>();
                    if (enemyAI != null)
                    {
                        var attackSlots = GetEnemyAnimSetAttackSlots();
                        enemyAI.attackClips = attackSlots.ToArray();
                        enemyAI.attackSkillDatas = LoadEnemyAttackSkillDatas();
                        EditorUtility.SetDirty(enemyAI);
                        PrefabUtility.SavePrefabAsset(prefab);
                        EnemySetupLog($"  同步 EnemyAI.attackClips: {attackSlots.Count} 个槽位（保留空槽位索引）");
                        EnemySetupLog($"  同步 EnemyAI.attackSkillDatas: {enemyAI.attackSkillDatas.Length} 个槽位");
                    }
                }
            }

            AssetDatabase.SaveAssets();
            // P2-3:移除 AssetDatabase.Refresh() — 它会触发 domain reload，
            // 如果 SkillBuilderWizard 窗口正开着，OnDisable → _workingCopy.Confirm()
            // 会用旧数据覆盖这里刚生成的新 SkillData。
            // SaveAssets 已将数据写入磁盘，LoadAssetAtPath 可立即读到最新内容。

            EnemySetupLog("══════════════════════════════════");
            EnemySetupLog("  全部完成！Controller + Prefab + SkillData 已生成");
            EnemySetupLog("══════════════════════════════════");

            // ── P0 A3：标记步骤完成 + 保存指纹 ──────────────────
            var setupCtx = BuildEnemyTargetContext();
            if (setupCtx != null)
            {
                MarkCurrentStepComplete(setupCtx,
                    SetupPipeline.StepType.EnemySetup);
                EnemySetupLog($"  ✔ 步骤完成标记已保存 (GUID: {setupCtx.prefabGuid})");
            }

            // ── CK-02：Enemy Setup 生成的 Prefab 自动传播到后续共享步骤 ──
            var enemyPrefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(_enemySetupPrefabPath);
            if (enemyPrefabGo != null)
            {
                TryPropagateTargetToSharedSteps(enemyPrefabGo);
                EnemySetupLog("  ✅ 已自动传播敌人 Prefab → Ragdoll / SpringBone / Weapon / Diagnostic");
            }

            EditorUtility.DisplayProgressBar("Enemy Setup", "步骤 7/7: 校验完成...", 0.90f);

            // ── P0-3：执行后 Prefab 完整性校验 ───────────────────────
            {
                EnemySetupLog("\n── Prefab 完整性校验 ────");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_enemySetupPrefabPath);
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(_enemySetupControllerPath);
                var issues = PrefabValidator.Validate(
                    SetupPipeline.PipelineTarget.Enemy, prefab, ctrl);
                EnemySetupLog(PrefabValidator.FormatIssues(issues));
                if (PrefabValidator.HasErrors(issues))
                    EnemySetupLog("  ⚠ 存在 Error 级别问题，请检查上述组件");
            }

            RepaintCharacterKitWindow();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        Avatar EnsureHumanoidAvatar(string modelPath, System.Action<string> log)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                log?.Invoke("  ❌ 模型路径为空，无法创建 Avatar");
                return null;
            }

            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                log?.Invoke($"  ❌ 无法获取 ModelImporter: {modelPath}");
                return null;
            }

            bool needsReimport = importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel;
            if (needsReimport)
            {
                log?.Invoke("  配置 Humanoid Avatar 并重新导入模型...");
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }

            // SaveAndReimport 后重新获取 importer，避免继续使用失效的导入器对象。
            importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;

            // CreateFromThisModel 的 Avatar 不通过 sourceAvatar 暴露；必须从 FBX 子资源中取得
            // 持久化 Avatar。sourceAvatar 只适用于 CopyFromOther，直接使用它会得到 null。
            Avatar avatar = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                var candidate = asset as Avatar;
                if (candidate != null && candidate.isHuman && candidate.isValid)
                {
                    avatar = candidate;
                    break;
                }
            }

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var modelAnimator = modelAsset != null ? modelAsset.GetComponent<Animator>() : null;
            if (avatar == null && modelAnimator != null && modelAnimator.avatar != null
                && modelAnimator.avatar.isHuman && modelAnimator.avatar.isValid)
                avatar = modelAnimator.avatar;

            if (avatar == null)
            {
                log?.Invoke("  ❌ FBX 重新导入后没有持久化 Humanoid Avatar，拒绝使用临时 Avatar");
                log?.Invoke($"  请确认 Model Import Settings > Rig > Animation Type=Humanoid、Avatar Definition=Create From This Model，并点击 Apply: {modelPath}");
                return null;
            }

            log?.Invoke($"  ✅ Humanoid Avatar（FBX 持久化子资源）: {avatar.name}");
            return avatar;
        }

        /// <summary>装配 Enemy Prefab：挂载 Animator / Rigidbody / CapsuleCollider / EnemyMotor / EnemyAI / NavMeshAgent 等组件。</summary>
        string AssembleEnemySetupPrefab()
        {
            EnemySetupLog("\n  装配 Enemy Prefab...");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(_enemySetupControllerPath);
            var avatar = EnsureHumanoidAvatar(_enemySetupModelFBXPath, EnemySetupLog);
            if (avatar == null) return null;

            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(_enemySetupModelFBXPath);
            if (modelPrefab == null)
            {
                EnemySetupLog("  无法加载模型 FBX！");
                return null;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            root.name = $"{_enemySetupName}_Enemy";

            // 设置 Enemy Layer 和 Tag
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0) root.layer = enemyLayer;
            try { root.tag = "Enemy"; } catch { }

            // 移除 Model 根上的 Animator（FBX 导入自带），使用根物体上的
            var modelAnim = root.GetComponent<Animator>();
            if (modelAnim != null) Object.DestroyImmediate(modelAnim);

            // 递归设置子物体 Layer
            SetLayerRecursivelyEnemy(root, enemyLayer >= 0 ? enemyLayer : 0);

            // ── Animator ────────────────────────────────────────────
            var animator = GetOrAddComponent<Animator>(root);
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // ── Rigidbody ────────────────────────────────────────────
            var rb = GetOrAddComponent<Rigidbody>(root);
            rb.mass = 80f;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // ── CapsuleCollider ──────────────────────────────────────
            var col = GetOrAddComponent<CapsuleCollider>(root);
            col.center = new Vector3(0, 1f, 0);
            col.radius = 0.35f;
            col.height = 2f;

            // ── EnemyMotor ───────────────────────────────────────────
            var motor = GetOrAddComponent<EnemyMotor>(root);
            motor.maxHealth = 80f;
            motor.moveSpeed = 2.5f;
            motor.rotationSpeed = 8f;

            // ── EnemyAI ──────────────────────────────────────────────
            var ai = GetOrAddComponent<EnemyAI>(root);
            ai.attackRange = 1.8f;
            ai.attackCooldown = 1.2f;
            ai.attackDamage = 12f;
            ai.attackKnockback = 3f;
            ai.detectionRange = 8f;
            ai.detectionAngle = 180f;
            ai.patrolRadius = 3f;
            ai.patrolInterval = 3f;
            ai.loseTargetRange = 12f;

            // ── NavMeshAgent ─────────────────────────────────────────
            // EnemyAI 声明了 RequireComponent(typeof(NavMeshAgent))，添加 EnemyAI 时
            // Unity 会自动补上 NavMeshAgent。先获取，避免重复 AddComponent 导致返回 null。
            var agent = GetOrAddComponent<UnityEngine.AI.NavMeshAgent>(root);

            if (agent != null)
            {
                agent.speed = 2.5f;
                agent.angularSpeed = 360f;
                agent.acceleration = 20f;
                agent.stoppingDistance = 0.5f;
                agent.radius = 0.3f;
                agent.height = 2f;
                agent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            }
            else
            {
                EnemySetupLog("  ❌ 无法创建或获取 NavMeshAgent。请检查 AI Navigation/NavMesh 模块及脚本编译状态。");
                Object.DestroyImmediate(root);
                return null;
            }

            // ── WeaponHolder ─────────────────────────────────────────
            // 注意：武器配置（WeaponHolder + WeaponSwitcher）统一在第 5 步 Weapon Setup 中添加
            // 此处不再自动添加 WeaponHolder 组件

            // ── HitDetector ──────────────────────────────────────────
            var hitDetector = GetOrAddComponent<Game.SkillSystem.HitDetector>(root);
            hitDetector.autoSetupHitMask = true;

            // ── 保存为 Prefab ────────────────────────────────────────
            string prefabPath = $"{_enemySetupFolderPath}/{_enemySetupName}_Enemy.prefab";
            bool saveSucceeded = false;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out saveSucceeded);
            Object.DestroyImmediate(root);

            if (!saveSucceeded)
            {
                EnemySetupLog($"  ❌ Prefab 保存失败: {prefabPath}");
                return null;
            }

            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!ValidateEnemyPrefab(savedPrefab, controller, out string prefabReason))
            {
                EnemySetupLog($"  ❌ Prefab 回读校验失败: {prefabReason}");
                return null;
            }

            EnemySetupLog($"  Prefab: {prefabPath}");
            Selection.activeObject = savedPrefab;
            return prefabPath;
        }

        bool ValidateEnemyPrefab(GameObject prefab, AnimatorController controller, out string reason)
        {
            if (prefab == null)
            {
                reason = "保存后无法重新加载 Prefab";
                return false;
            }

            var animator = prefab.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController != controller)
            {
                reason = "Animator 或 RuntimeAnimatorController 引用不正确";
                return false;
            }

            if (prefab.GetComponent<Rigidbody>() == null ||
                prefab.GetComponent<CapsuleCollider>() == null ||
                prefab.GetComponent<EnemyMotor>() == null ||
                prefab.GetComponent<EnemyAI>() == null ||
                prefab.GetComponent<UnityEngine.AI.NavMeshAgent>() == null ||
                prefab.GetComponent<Game.SkillSystem.HitDetector>() == null)
            {
                reason = "缺少 Enemy 必需组件";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>递归设置 Enemy Prefab 子物体的 Layer。</summary>
        static void SetLayerRecursivelyEnemy(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursivelyEnemy(child.gameObject, layer);
        }

        /// <summary>
        /// 为当前敌人自动生成 SkillData 资产并保存到 {角色文件夹}/SkillData/ 目录。
        /// 每份 SkillData 的 knownAnimations 记录从 AnimSet 中收集到的所有动画信息。
        /// </summary>
        void GenerateEnemySkillDataAssets()
        {
            if (string.IsNullOrEmpty(_enemySetupFolderPath)) return;

            // 目标路径：{角色文件夹}/SkillData/  （与 Controller/Prefab 同级之下的 SkillData 子目录）
            string skillDataDir = $"{_enemySetupFolderPath}/SkillData";
            EnsureDirectoryExists(skillDataDir);

            // 收集所有已知动画信息
            var knownAnims = CollectEnemyKnownAnimations();

            // 从已装配 Prefab 读取 EnemyAI 攻击参数，作为 SkillData 默认值（替代硬编码）
            float defaultRange = 1.8f, defaultHitAngle = 120f, defaultDamage = 10f;
            if (!string.IsNullOrEmpty(_enemySetupPrefabPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_enemySetupPrefabPath);
                var enemyAI = prefab != null ? prefab.GetComponent<EnemyAI>() : null;
                if (enemyAI != null)
                {
                    defaultRange = enemyAI.attackRange;
                    defaultHitAngle = enemyAI.attackHitAngle;
                    defaultDamage = enemyAI.attackDamage;
                }
            }

            int totalFiles = 0;
            var expectedSkillDataPaths = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);

            // ── 为每个攻击创建 SkillData（BasicAttack 类型）───────────
            // 保留 AnimSet 原始槽位索引，确保 Attack_3 不会被压缩成 Attack_2。
            var atkClips = _enemySetupAnimSet?.attackClips;
            if (atkClips != null && atkClips.Count > 0)
            {
                for (int i = 0; i < atkClips.Count && i < AnimStateNaming.AttackIndexMax; i++)
                {
                    var atkClip = atkClips[i];
                    if (atkClip == null) continue;
                    int slotIndex = i + 1;

                    string assetPath = $"{skillDataDir}/{_enemySetupName}_Enemy_Attack_{slotIndex:D2}.asset";
                    expectedSkillDataPaths.Add(assetPath);
                    var skillData = CreateOrGetSkillData(assetPath, $"{_enemySetupName}_Enemy_Attack_{slotIndex:D2}", SkillCategory.BasicAttack);
                    // P1-8.4: animTrigger 必须匹配 Animator Controller 的 Trigger 参数名。
                    // Enemy Controller 的 Trigger 参数统一叫 "Attack"（不是 "Attack_1"/"Attack_2"）。
                    // 配合 AttackIndex 整数参数路由到具体 Attack_N 状态。
                    // 普攻播动画由 EnemyMotor.SetTrigger("Attack") 负责,此字段仅用于技能路径和预览。
                    skillData.animTrigger = "Attack";
                    skillData.animStateId = slotIndex;
                    skillData.animClips = new AnimationClip[] { atkClip };
                    // animClipName 匹配 Animator Controller 中的 State 名称（统一规范：Attack_1/Attack_2/...）。
                    // 留空时 SkillAnimPlayer.GetAnimatorStateName 会自动推导 "Attack_" + animStateId。
                    skillData.animClipName = "";
                    skillData.cooldown = 1.0f;
                    skillData.knownAnimations = knownAnims;
                    // P2-2:生成 MeleeSwingData 节点填充 graphData，确保：
                    //   - 运行时 SkillData.GetDamageFromGraph / GetMeleeParamsFromGraph 返回有效值
                    //   - 预览 Timeline 有轨道节点（可拖拽+可视化）
                    //   - Attack 预览动画正常驱动播放
                    PopulateEnemyAttackGraphData(skillData, atkClip, slotIndex, defaultRange, defaultHitAngle, defaultDamage);
                    EditorUtility.SetDirty(skillData);
                    EnemySetupLog($"  SkillData: {_enemySetupName}_Enemy_Attack_{slotIndex:D2} → {assetPath}");
                    totalFiles++;
                }
            }

            // ── 为每个技能创建 SkillData（Skill 类型）─────────────────
            if (_enemySetupAnimSet?.skills != null)
            {
                for (int i = 0; i < _enemySetupAnimSet.skills.Count; i++)
                {
                    var seq = _enemySetupAnimSet.skills[i];
                    if (seq == null || seq.segments == null || seq.segments.Count == 0) continue;

                    string skillSuffix = AnimStateNaming.SkillSuffix(i); // Q/W/E/R/...
                    string label = AnimStateNaming.SkillLabel(i);
                    string assetPath = $"{skillDataDir}/{_enemySetupName}_Enemy_Skill_{skillSuffix}.asset";
                    expectedSkillDataPaths.Add(assetPath);

                    var skillData = CreateOrGetSkillData(assetPath, $"{_enemySetupName}_Enemy_Skill_{skillSuffix}", SkillCategory.Skill);
                    skillData.animTrigger = $"Skill{skillSuffix}";
                    skillData.animClips = seq.segments.ToArray();
                    skillData.animClipName = seq.segments.Count > 0 && seq.segments[0] != null
                        ? seq.segments[0].name : "";
                    skillData.cooldown = 3.0f;
                    skillData.knownAnimations = knownAnims;
                    // P1: 根据技能槽位自动匹配配方模板生成 graphData
                    PopulateEnemySkillGraphData(skillData, seq, i, defaultRange, defaultHitAngle, defaultDamage);
                    EditorUtility.SetDirty(skillData);
                    EnemySetupLog($"  SkillData: {_enemySetupName}_Enemy_Skill_{skillSuffix} ({label}) → {assetPath}");
                    totalFiles++;
                }
            }

            int staleDeleted = RemoveStaleGeneratedEnemySkillData(skillDataDir, expectedSkillDataPaths);
            if (staleDeleted > 0)
                EnemySetupLog($"  清理旧生成 SkillData: {staleDeleted} 份");

            EnemySetupLog($"  SkillData 生成完成：共 {totalFiles} 份 → {skillDataDir}/");

            // 2026-07-21 执行层统一: 自动验证生成的 SkillData
            ValidateGeneratedSkillData(skillDataDir, expectedSkillDataPaths);

            AssetDatabase.SaveAssets();
            // P2-3:移除 AssetDatabase.Refresh() — 它会触发 domain reload，
            // 如果 SkillBuilderWizard 窗口正开着，OnDisable → _workingCopy.Confirm()
            // 会用旧数据覆盖这里刚生成的新 SkillData。
        }

        int RemoveStaleGeneratedEnemySkillData(string skillDataDir,
            System.Collections.Generic.HashSet<string> expectedPaths)
        {
            if (!AssetDatabase.IsValidFolder(skillDataDir)) return 0;

            int removed = 0;
            string[] guids = AssetDatabase.FindAssets("t:SkillData", new[] { skillDataDir });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                bool generatedForEnemy = fileName.StartsWith(
                    $"{_enemySetupName}_Enemy_", System.StringComparison.OrdinalIgnoreCase);

                if (generatedForEnemy && !expectedPaths.Contains(path))
                {
                    if (AssetDatabase.DeleteAsset(path))
                        removed++;
                }
            }

            return removed;
        }

        /// <summary>2026-07-21: 验证 EnemySetup 生成的 SkillData 中 graphData 节点配置。仅 Error/Warning 输出日志。</summary>
        void ValidateGeneratedSkillData(string skillDataDir,
            System.Collections.Generic.HashSet<string> expectedPaths)
        {
            if (expectedPaths == null || expectedPaths.Count == 0) return;
            foreach (var path in expectedPaths)
            {
                var sd = AssetDatabase.LoadAssetAtPath<SkillData>(path);
                if (sd == null) continue;
                var issues = SkillDataValidator.Validate(sd, defaultClipLength: 1f);
                foreach (var issue in issues)
                {
                    if (issue.Level != SkillDataValidator.Issue.Severity.Info)
                        Debug.LogWarning(
                            $"[EnemySetup] 验证 {sd.name} — {issue.Level}: {issue.Message}",
                            sd);
                }
            }
        }

        /// <summary>从当前 AnimSet 收集所有已知动画条目。</summary>
        System.Collections.Generic.List<KnownAnimationEntry> CollectEnemyKnownAnimations()
        {
            var list = new System.Collections.Generic.List<KnownAnimationEntry>();

            void Add(string cat, string subId, string displayName, AnimationClip clip)
            {
                if (clip == null) return;
                list.Add(new KnownAnimationEntry
                {
                    category = cat,
                    subId = subId,
                    displayName = displayName,
                    clip = clip
                });
            }

            // 基础动画
            Add("Idle", "Idle", "待机", _enemySetupIdleClip);
            Add("Walk", "Walk", "行走", _enemySetupWalkClip);
            Add("Hit", "Hit", "受击", _enemySetupHitClip);
            Add("Death", "Death", "死亡", _enemySetupDeathClip);

            // 攻击动画
            if (_enemySetupAnimSet?.attackClips != null)
            {
                for (int i = 0; i < _enemySetupAnimSet.attackClips.Count; i++)
                {
                    var clip = _enemySetupAnimSet.attackClips[i];
                    if (clip != null)
                        Add("Attack", $"Attack{i + 1:D2}", $"攻击 {i + 1}", clip);
                }
            }

            // 技能动画（各段）
            if (_enemySetupAnimSet?.skills != null)
            {
                for (int i = 0; i < _enemySetupAnimSet.skills.Count; i++)
                {
                    var seq = _enemySetupAnimSet.skills[i];
                    if (seq?.segments == null) continue;
                    string sid = AnimStateNaming.SkillSuffix(i);
                    string label = AnimStateNaming.SkillLabel(i);
                    for (int s = 0; s < seq.segments.Count; s++)
                    {
                        var clip = seq.segments[s];
                        if (clip != null)
                            Add("Skill", $"Skill{sid}_Seg{s}", $"{label} 第{s + 1}段", clip);
                    }
                }
            }

            // 过渡动画
            if (_enemySetupAnimSet?.blendTransitions != null)
            {
                for (int i = 0; i < _enemySetupAnimSet.blendTransitions.Count; i++)
                {
                    var bt = _enemySetupAnimSet.blendTransitions[i];
                    if (bt?.fromClip != null)
                        Add("Transition", $"BlendTransition{i}_From", $"过渡 {i + 1} From", bt.fromClip);
                    if (bt?.toClip != null)
                        Add("Transition", $"BlendTransition{i}_To", $"过渡 {i + 1} To", bt.toClip);
                    if (bt?.transitionClip != null)
                        Add("Transition", $"BlendTransition{i}_Trans", $"过渡 {i + 1} Trans", bt.transitionClip);
                }
            }

            return list;
        }

        /// <summary>创建或加载指定路径的 SkillData 资产。</summary>
        SkillData CreateOrGetSkillData(string assetPath, string skillName, SkillCategory category)
        {
            var existing = AssetDatabase.LoadAssetAtPath<SkillData>(assetPath);
            if (existing != null)
            {
                existing.skillName = skillName;
                existing.name = skillName;   // P2-3:修复 m_Name 被 Confirm 污染为 "...(Working Copy)"
                existing.category = category;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var sd = ScriptableObject.CreateInstance<SkillData>();
            sd.skillName = skillName;
            sd.name = skillName;
            sd.category = category;
            AssetDatabase.CreateAsset(sd, assetPath);
            // 首次创建时立即注册并重新加载资产，避免后续绑定拿到 CreateAsset 前的空缓存对象。
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<SkillData>(assetPath) ?? sd;
        }

        /// <summary>
        /// 为敌人攻击 SkillData 的 graphData 填充 MeleeSwingData 节点。
        /// 重新生成时先清空旧 graphData，确保不会积累过期节点。
        /// </summary>
        void PopulateEnemyAttackGraphData(SkillData skillData, AnimationClip atkClip, int slotIndex,
            float defaultRange = 1.8f, float defaultHitAngle = 120f, float defaultDamage = 10f)
        {
            if (skillData == null) return;
            // 重新生成：清空旧 graphData
            if (skillData.graphData == null)
                skillData.graphData = new List<SkillNodeData>();
            else
                skillData.graphData.Clear();

            float clipLen = atkClip != null ? atkClip.length : 1.5f;
            var meleeNode = new MeleeSwingData
            {
                triggerTime = 0.2f,                    // 前摇约 0.2s 后触发命中判定
                range       = defaultRange,            // 从 EnemyAI.attackRange 读取
                hitRadius   = 0.5f,                    // 默认碰撞半径
                hitAngle    = defaultHitAngle,          // 从 EnemyAI.attackHitAngle 读取
                damage      = defaultDamage,           // 从 EnemyAI.attackDamage 读取
                originHeight = 0.8f,                   // 判定原点高度（胸部附近）
                originForwardOffset = 0f,
                editorDuration = clipLen               // 节点长度 = 动画时长
            };
            skillData.graphData.Add(meleeNode);
        }

        /// <summary>
        /// 为敌人技能 SkillData 自动匹配配方模板生成 graphData。
        /// 根据技能槽位 (Q/W/E/R) 和动画时长推断技能类型，生成对应判定节点。
        /// 用户可在 SkillBuilderWizard 中进一步调整。
        /// </summary>
        void PopulateEnemySkillGraphData(SkillData skillData,
            CharacterAnimSequence seq, int skillIndex,
            float defaultRange, float defaultHitAngle, float defaultDamage)
        {
            if (skillData == null) return;
            if (skillData.graphData == null)
                skillData.graphData = new List<SkillNodeData>();
            else
                skillData.graphData.Clear();

            float clipLen = seq?.segments != null && seq.segments.Count > 0 && seq.segments[0] != null
                ? seq.segments[0].length : 1.5f;

            // 根据槽位推断技能类型模板（可后续在 Wizard 中调整）
            // Q: 近战强化攻击 (MeleeSwing + 更高伤害)
            // W: 远程弹幕 (RectShot)
            // E: AOE 范围伤害 (AOECircular)
            // R: 大招/持续施法 (Channeled)
            switch (skillIndex)
            {
                case 0: // SkillQ — 近战强化
                    skillData.graphData.Add(new MeleeSwingData
                    {
                        triggerTime = 0.2f,
                        range = defaultRange,
                        hitRadius = 0.5f,
                        hitAngle = defaultHitAngle,
                        damage = defaultDamage * 1.5f,
                        originHeight = 0.8f,
                        originForwardOffset = 0f,
                        editorDuration = clipLen
                    });
                    break;

                case 1: // SkillW — 远程弹幕
                    skillData.graphData.Add(new RectShotData
                    {
                        triggerTime = 0.2f,
                        width = 0.5f,
                        height = 1.8f,
                        speed = 8f,
                        maxRange = defaultRange * 4f,
                        count = 1,
                        damage = defaultDamage,
                        spawnHeight = 0.9f,
                        forwardOffset = 0.3f,
                        editorDuration = clipLen
                    });
                    break;

                case 2: // SkillE — AOE 范围
                    skillData.graphData.Add(new AOECircularData
                    {
                        triggerTime = 0.3f,
                        radius = defaultRange * 1.5f,
                        damage = defaultDamage * 1.2f,
                        centerIsTargetPoint = false,
                        editorDuration = clipLen
                    });
                    break;

                default: // SkillR+ — 持续施法
                    skillData.graphData.Add(new ChanneledData
                    {
                        durationOverride = 1.5f,
                        tickInterval = 0.3f,
                        tickAmount = defaultDamage * 0.5f,
                        lockMovement = true,
                        editorDuration = clipLen
                    });
                    break;
            }
        }

        /// <summary>确保 Unity 资产目录存在（不存在则逐级创建，使用 AssetDatabase 以确保 .meta 文件生成）。</summary>
        void EnsureDirectoryExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            // 确保父目录存在
            string parent = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string folderName = System.IO.Path.GetFileName(assetPath);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureDirectoryExists(parent);
            }

            AssetDatabase.CreateFolder(parent, folderName);
            Debug.Log($"[CharacterKit] 自动创建目录: {assetPath}");
        }

        /// <summary>从 AnimSet 获取攻击槽位数，保留稀疏槽位的原始索引。</summary>
        int GetEnemyAnimSetAttackCount()
        {
            if (_enemySetupAnimSet?.attackClips == null) return 0;
            return Mathf.Min(_enemySetupAnimSet.attackClips.Count, AnimStateNaming.AttackIndexMax);
        }

        /// <summary>获取攻击槽位，保留空槽位以维持 Animator AttackIndex 对齐。</summary>
        List<AnimationClip> GetEnemyAnimSetAttackSlots()
        {
            var list = new List<AnimationClip>();
            if (_enemySetupAnimSet?.attackClips == null) return list;

            int count = Mathf.Min(_enemySetupAnimSet.attackClips.Count, AnimStateNaming.AttackIndexMax);
            for (int i = 0; i < count; i++)
                list.Add(_enemySetupAnimSet.attackClips[i]);
            return list;
        }

        SkillData[] LoadEnemyAttackSkillDatas()
        {
            int count = Mathf.Min(_enemySetupAnimSet?.attackClips?.Count ?? 0, AnimStateNaming.AttackIndexMax);
            var result = new SkillData[count];
            string skillDataDir = $"{_enemySetupFolderPath}/SkillData";

            for (int i = 0; i < count; i++)
            {
                if (_enemySetupAnimSet.attackClips[i] == null) continue;
                string path = $"{skillDataDir}/{_enemySetupName}_Enemy_Attack_{i + 1:D2}.asset";
                result[i] = AssetDatabase.LoadAssetAtPath<SkillData>(path);
            }

            return result;
        }

        /// <summary>从 AnimSet 获取所有非空攻击 Clip（保序）。</summary>
        List<AnimationClip> GetEnemyAnimSetValidAttackClips()
        {
            var list = new List<AnimationClip>();
            if (_enemySetupAnimSet?.attackClips == null) return list;
            foreach (var c in _enemySetupAnimSet.attackClips)
                if (c != null) list.Add(c);
            return list;
        }

        /// <summary>从 AnimSet 获取指定下标的攻击 Clip（null 表示空位）。</summary>
        AnimationClip GetEnemyAnimSetAttackClip(int idx)
        {
            if (_enemySetupAnimSet?.attackClips == null) return null;
            if (idx < 0 || idx >= _enemySetupAnimSet.attackClips.Count) return null;
            return _enemySetupAnimSet.attackClips[idx];
        }

        /// <summary>在状态机中绑定 Enemy 动画（按名称匹配状态 + 遍历 BlendTree 子 Clip）。</summary>
        int BindEnemyClipsInSM(AnimatorStateMachine sm)
        {
            int count = 0;
            foreach (var childState in sm.states)
            {
                string name = childState.state.name;
                AnimationClip clip = null;

                if (AnimStateNaming.IsIdleState(name) && _enemySetupIdleClip != null)
                    clip = _enemySetupIdleClip;
                else if (AnimStateNaming.IsMoveState(name) && _enemySetupWalkClip != null)
                    clip = _enemySetupWalkClip;
                else if (AnimStateNaming.IsHitState(name) && _enemySetupHitClip != null)
                    clip = _enemySetupHitClip;
                else if (AnimStateNaming.IsDeathState(name) && _enemySetupDeathClip != null)
                    clip = _enemySetupDeathClip;
                else if (name.StartsWith("Attack", System.StringComparison.OrdinalIgnoreCase))
                {
                    // 支持统一状态名 Attack_1，也兼容旧状态名 Attack1/Attack01。
                    // 原实现直接解析 "Attack_1" 的后缀 "_1"，永远解析失败，
                    // 导致 EnemySetup 日志显示执行完成，但 Attack_1 的 motion 始终为空，
                    // 运行时和预览最终都回到待机状态。
                    int atkIdx;
                    int parsed;
                    if (!AnimStateNaming.TryParseAttackIndex(name, out parsed))
                    {
                        string numPart = name.Substring("Attack".Length).TrimStart('_');
                        int.TryParse(numPart, out atkIdx);
                    }
                    else
                    {
                        atkIdx = parsed;
                    }
                    if (atkIdx > 0)
                        clip = GetEnemyAnimSetAttackClip(atkIdx - 1);
                }

                if (clip != null)
                {
                    childState.state.motion = clip;
                    count++;
                    EnemySetupLog($"  {name} → {clip.name}");
                }
                else if (childState.state.motion is BlendTree bt)
                {
                    count += BindEnemyClipsInBlendTree(bt);
                }
            }

            foreach (var childSM in sm.stateMachines)
                count += BindEnemyClipsInSM(childSM.stateMachine);

            return count;
        }

        /// <summary>在 Enemy BlendTree 中递归替换子 Clip（按名称匹配）。</summary>
        int BindEnemyClipsInBlendTree(BlendTree bt)
        {
            int count = 0;
            var children = bt.children;

            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];

                if (child.motion is BlendTree subBt)
                {
                    count += BindEnemyClipsInBlendTree(subBt);
                }
                else if (child.motion is AnimationClip existingClip)
                {
                    string clipName = existingClip.name.ToLower();
                    AnimationClip replacement = null;

                    if ((clipName.Contains("idle") || clipName.Contains("stand")) && _enemySetupIdleClip != null)
                        replacement = _enemySetupIdleClip;
                    else if ((clipName.Contains("walk") || clipName.Contains("run")) && _enemySetupWalkClip != null)
                        replacement = _enemySetupWalkClip;
                    else if (clipName.Contains("hit") && _enemySetupHitClip != null)
                        replacement = _enemySetupHitClip;
                    else if ((clipName.Contains("dead") || clipName.Contains("death")) && _enemySetupDeathClip != null)
                        replacement = _enemySetupDeathClip;
                    else if (clipName.Contains("attack"))
                    {
                        int animSetAtkCount = _enemySetupAnimSet?.attackClips?.Count ?? 0;
                        for (int a = 0; a < animSetAtkCount; a++)
                        {
                            var atkClip = GetEnemyAnimSetAttackClip(a);
                            if (atkClip != null && AnimStateNaming.IsAttackClip(clipName, a))
                            {
                                replacement = atkClip;
                                break;
                            }
                        }
                    }

                    if (replacement != null)
                    {
                        children[i].motion = replacement;
                        count++;
                        EnemySetupLog($"  [BT] {clipName} → {replacement.name}");
                    }
                }
            }

            if (count > 0)
                bt.children = children;

            return count;
        }

        /// <summary>清除 Controller 中指定名称的状态的 motion。</summary>
        bool ClearEnemySetupMotionByName(AnimatorController controller, string stateName)
        {
            bool cleared = false;
            foreach (var layer in controller.layers)
            {
                if (ClearEnemySetupMotionInSM(layer.stateMachine, stateName))
                    cleared = true;
            }
            return cleared;
        }

        bool ClearEnemySetupMotionInSM(AnimatorStateMachine sm, string targetName)
        {
            bool found = false;
            foreach (var childState in sm.states)
            {
                if (childState.state.name == targetName && childState.state.motion != null)
                {
                    childState.state.motion = null;
                    found = true;
                }
            }
            foreach (var childSM in sm.stateMachines)
            {
                if (ClearEnemySetupMotionInSM(childSM.stateMachine, targetName))
                    found = true;
            }
            return found;
        }

        /// <summary>清理 Animator 图表中指向空节点的无效过渡。</summary>
        int RemoveInvalidAnimatorTransitions(AnimatorController controller)
        {
            int removed = 0;
            foreach (var layer in controller.layers)
                removed += RemoveInvalidAnimatorTransitions(layer.stateMachine);
            return removed;
        }

        int RemoveInvalidAnimatorTransitions(AnimatorStateMachine sm)
        {
            int removed = 0;

            foreach (var state in sm.states)
            {
                if (state.state == null) continue;
                var transitions = state.state.transitions;
                foreach (var transition in transitions)
                {
                    if (transition == null) continue;
                    if (!transition.isExit && transition.destinationState == null)
                    {
                        state.state.RemoveTransition(transition);
                        removed++;
                    }
                }
            }

            foreach (var transition in sm.anyStateTransitions)
            {
                if (transition == null) continue;
                if (!transition.isExit && transition.destinationState == null &&
                    transition.destinationStateMachine == null)
                {
                    sm.RemoveAnyStateTransition(transition);
                    removed++;
                }
            }

            foreach (var transition in sm.entryTransitions)
            {
                if (transition == null || transition.destinationState != null ||
                    transition.destinationStateMachine != null)
                    continue;
                sm.RemoveEntryTransition(transition);
                removed++;
            }

            foreach (var childSM in sm.stateMachines)
            {
                if (childSM.stateMachine == null) continue;

                var stateMachineTransitions = sm.GetStateMachineTransitions(childSM.stateMachine);
                foreach (var transition in stateMachineTransitions)
                {
                    if (transition == null) continue;
                    if (!transition.isExit && transition.destinationStateMachine == null)
                    {
                        sm.RemoveStateMachineTransition(childSM.stateMachine, transition);
                        removed++;
                    }
                }

                removed += RemoveInvalidAnimatorTransitions(childSM.stateMachine);
            }

            return removed;
        }
    }
}
#endif