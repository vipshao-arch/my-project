#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Game.Character;
using Game.SkillSystem;
using Game.SkillSystem.EditorTools;
using Game.EditorTools.Shared;
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
        static readonly GUIContent GCLChar_CtrlTemplate = new GUIContent("Controller 模板", "作为模板的 Animator Controller，复制后替换动画");
        static readonly GUIContent GCLChar_AnimSetTemplate = new GUIContent("AnimSet 模板", "角色动画集 ScriptableObject 资产。\n默认会自动填充 DefaultCharacterAnimSet。\n可拖入自己的 AnimSet 资产替换。");
        static readonly GUIContent GCLChar_ResourcePath = new GUIContent("资源路径", "包含角色主模型 FBX（无 @ 前缀）和所有 @动画 FBX（@Idle、@Run 等）的文件夹。\n拖入后点击下方扫描按钮自动填充动画字段。");

        /// <summary>绘制 Character Setup 独立面板：5 段单步完成（不再分 Part1/Part2）。</summary>
        /// <remarks>
        /// 5 段流程：
        ///   ① 配置三件套（Controller 模板 / AnimSet 模板 / 资源路径）
        ///   ② 基础移动动画（Idle / Run / RunFast）
        ///   ③ 攻击动画组（Attack 1~N，每组支持多段 + 衔接）
        ///   ④ 技能动画组（Skill Q / W / E / R / 5~8，每组支持多段 + 衔接）
        ///   ⑤ 通用过渡动画组
        ///   ──── 单按钮「确认执行」一步完成：保存 AnimSet → 复制 Controller → 替换 Locomotion/攻击/技能/过渡动画 → 装配 Prefab
        ///   ──── 路径自动继承（资源路径仅需设置一次）
        /// </remarks>
        void DrawCharacterSetupPanel(SetupPipeline.StepState state)
        {
            // ── 首次打开自动初始化动画集编辑缓冲区 ──────────────────────
            if (_charSetupEditAnimSet == null)
                InitCharSetupAnimSet();

            // ── 首次打开自动指认默认 Animator Controller ───────────────
            if (_charSetupSourceController == null)
                _charSetupSourceController = DefaultConfigCreator.GetOrCreateDefaultCharacterController();

            EditorGUILayout.LabelField("① 主角 Prefab 装配（5 段单步完成）", StyleTitle14);
            EditorGUILayout.Space(4);

            // ═══════════════════════════════════════════════════════════
            //  ① 配置三件套：Controller 模板 / AnimSet 模板 / 资源路径
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("① 配置三件套（Controller 模板 / AnimSet 模板 / 资源路径）", () =>
            {
                // ── 1.1 Controller 模板 ──────────────────────────────────
                _charSetupSourceController = (AnimatorController)EditorGUILayout.ObjectField(
                    GCLChar_CtrlTemplate,
                    _charSetupSourceController, typeof(AnimatorController), false);

                // ── 1.2 AnimSet 模板 ────────────────────────────────────
                var newTemplate = (CharacterAnimSetAsset)EditorGUILayout.ObjectField(
                    GCLChar_AnimSetTemplate,
                    _charSetupTemplateAnimSet, typeof(CharacterAnimSetAsset), false);
                if (newTemplate != _charSetupTemplateAnimSet)
                {
                    _charSetupTemplateAnimSet = newTemplate;
                    if (_charSetupTemplateAnimSet != null)
                        ApplyCharSetupAnimSetTemplate(_charSetupTemplateAnimSet);
                }
                if (_charSetupTemplateAnimSet == null)
                {
                    EditorGUILayout.LabelField("← 使用默认预设", EditorStyles.miniLabel);
                }
                else
                {
                    if (GUILayout.Button("恢复默认", GUILayout.Width(70), GUILayout.Height(18)))
                    {
                        _charSetupTemplateAnimSet = null;
                        InitCharSetupAnimSet();
                    }
                }

                // ── 保存路径提示 ──────────────────────────────────────
                string saveDir = ResolveCharAnimSetSaveDir();
                if (!string.IsNullOrEmpty(saveDir))
                {
                    EditorGUILayout.LabelField($"↳ AnimSet 保存到: {saveDir}/AnimSet_{Path.GetFileName(saveDir)}.asset", GetMiniLabelColor(new Color(0.5f, 0.8f, 0.5f)));
                }

                // ── 1.3 资源路径（拖入后自动扫描 @动画 FBX 并填充）─────
                _charSetupFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    GCLChar_ResourcePath,
                    _charSetupFolder, typeof(DefaultAsset), false);
                SyncSelectedCharacterFolder();
                if (_charSetupFolder != null)
                {
                    if (GUILayout.Button("🔍 扫描文件夹并自动填充动画", GUILayout.Height(20)))
                        AutoFillCharSetupFromFolder();
                }

                // ── 当前角色状态条（路径自动从资源路径继承）───────────
                if (!string.IsNullOrEmpty(_charSetupName))
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.BeginVertical(StyleHelpBox);
                    try
                    {
                        var prevC = GUI.contentColor;
                        GUI.contentColor = new Color(0.7f, 0.85f, 1.0f);
                        EditorGUILayout.LabelField($"📌 当前角色：{_charSetupName}", EditorStyles.boldLabel);
                        GUI.contentColor = prevC;
                        EditorGUILayout.LabelField($"  Controller: {(_charSetupControllerPath ?? "<未生成>")}", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField($"  Override:   {(_charSetupOverridePath ?? "<未生成>")}", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField($"  Prefab:     {(_charSetupPrefabPath ?? "<未生成>")}", EditorStyles.miniLabel);
                    }
                    finally
                    {
                        SafeEndLayout(() => EditorGUILayout.EndVertical());
                    }
                    EditorGUILayout.Space(2);
                }
            });

            // ═══════════════════════════════════════════════════════════
            //  ② 基础移动动画（Idle / Run / RunFast）
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("② 基础移动动画（Idle / Run / RunFast）", () =>
            {
                EditorGUILayout.LabelField("  留空则使用模板默认动画", EditorStyles.miniLabel);
                _charSetupIdleClip = DrawSetupClipField("Idle（待机）", _charSetupIdleClip);
                _charSetupRunClip = DrawSetupClipField("Run（跑步）", _charSetupRunClip);
                _charSetupRunFastClip = DrawSetupClipField("Run Fast（快跑）", _charSetupRunFastClip);
            });

            // ── 动画集检查 ────────────────────────────────────────────
            if (_charSetupEditAnimSet == null)
            {
                EditorGUILayout.HelpBox("动画集未初始化，请刷新面板。", MessageType.Warning);
                return;
            }

            // ═══════════════════════════════════════════════════════════
            //  ③ 攻击动画组（Attack 1~N，每组支持多段 + 衔接）
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("③ 攻击动画组（Attack 1~N，每组支持多段 + 衔接）", () =>
            {
                int newAttackCount = EditorGUILayout.IntSlider("攻击组数", _charSetupEditAnimSet.attacks.Count, 1, 8);
                while (_charSetupEditAnimSet.attacks.Count < newAttackCount)
                {
                    _charSetupEditAnimSet.attacks.Add(new CharacterAnimSequence
                    {
                        id = AnimStateNaming.AttackState(_charSetupEditAnimSet.attacks.Count + 1),
                        segments = new List<AnimationClip> { null }
                    });
                    _charSetupAnimDirty = true;
                }
                while (_charSetupEditAnimSet.attacks.Count > newAttackCount)
                {
                    _charSetupEditAnimSet.attacks.RemoveAt(_charSetupEditAnimSet.attacks.Count - 1);
                    _charSetupAnimDirty = true;
                }

                for (int i = 0; i < _charSetupEditAnimSet.attacks.Count; i++)
                {
                    DrawCharAnimSequenceEditor(_charSetupEditAnimSet.attacks[i], $"Attack {i + 1}", () => _charSetupAnimDirty = true);
                }
            });

            // ═══════════════════════════════════════════════════════════
            //  ④ 技能动画组（Skill Q / W / E / R / 5~8，每组支持多段 + 衔接）
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("④ 技能动画组（Skill Q / W / E / R / 5~8，每组支持多段 + 衔接）", () =>
            {
                int newSkillCount = EditorGUILayout.IntSlider("技能组数", _charSetupEditAnimSet.skills.Count, 1, 8);
                while (_charSetupEditAnimSet.skills.Count < newSkillCount)
                {
                    int idx = _charSetupEditAnimSet.skills.Count;
                    string sid = AnimStateNaming.SkillSuffix(idx);
                    _charSetupEditAnimSet.skills.Add(new CharacterAnimSequence
                    {
                        id = AnimStateNaming.SkillState(sid),
                        segments = new List<AnimationClip> { null }
                    });
                    _charSetupAnimDirty = true;
                }
                while (_charSetupEditAnimSet.skills.Count > newSkillCount)
                {
                    _charSetupEditAnimSet.skills.RemoveAt(_charSetupEditAnimSet.skills.Count - 1);
                    _charSetupAnimDirty = true;
                }

                for (int i = 0; i < _charSetupEditAnimSet.skills.Count; i++)
                {
                    string label = AnimStateNaming.SkillLabel(i);
                    DrawCharAnimSequenceEditor(_charSetupEditAnimSet.skills[i], label, () => _charSetupAnimDirty = true);
                }
            });

            // ═══════════════════════════════════════════════════════════
            //  ⑤ 通用过渡动画组
            // ═══════════════════════════════════════════════════════════
            DrawSetupSection("⑤ 通用过渡动画组（Idle↔Move、多段回待机等）", () =>
            {
                int transCount = _charSetupEditAnimSet.blendTransitions.Count;
                int newTransCount = EditorGUILayout.IntSlider("过渡组数", transCount, 0, 8);
                while (_charSetupEditAnimSet.blendTransitions.Count < newTransCount)
                {
                    _charSetupEditAnimSet.blendTransitions.Add(new BlendTransitionData());
                    _charSetupAnimDirty = true;
                }
                while (_charSetupEditAnimSet.blendTransitions.Count > newTransCount)
                {
                    _charSetupEditAnimSet.blendTransitions.RemoveAt(_charSetupEditAnimSet.blendTransitions.Count - 1);
                    _charSetupAnimDirty = true;
                }

                for (int i = 0; i < _charSetupEditAnimSet.blendTransitions.Count; i++)
                {
                    DrawBlendTransitionEditor(_charSetupEditAnimSet.blendTransitions[i], i, () => _charSetupAnimDirty = true);
                }
            });

            // ═══════════════════════════════════════════════════════════
            //  单按钮一站式完成：保存 AnimSet → 复制 Controller → 替换动画 → 装配 Prefab
            // ═══════════════════════════════════════════════════════════
            EditorGUILayout.Space(8);
            int ready = CountSetupPart1Ready(_charSetupSourceController, _charSetupFolder,
                _charSetupIdleClip != null || _charSetupRunClip != null || _charSetupRunFastClip != null);
            DrawSetupPart1ExecuteButton(ready,
                "Idle/Run/RunFast 任一动画",
                () =>
                {
                    _charSetupLogMessages.Clear();
                    ExecuteCharacterSetup();
                },
                "确认执行");

            // ── 完成状态 ───────────────────────────────────────────────
            DrawSetupPart1CompleteBox(_charSetupControllerPath, _charSetupPrefabPath);

            // ── Override / 联机装配状态（随上方一键装配自动完成，此处仅展示）──
            if (!string.IsNullOrEmpty(_charSetupPrefabPath))
            {
                EditorGUILayout.Space(2);
                bool overrideReady = IsCharacterOverrideSetupReady();
                bool networkReady = IsCharacterNetworkSetupReady();
                EditorGUILayout.LabelField(
                    $"  Animation Override: {(overrideReady ? "✔ 已就绪" : "✘ 未就绪")}    Network 联机入口: {(networkReady ? "✔ 已就绪" : "✘ 未就绪")}",
                    GetMiniLabelColor(overrideReady && networkReady ? new Color(0.5f, 0.8f, 0.5f) : new Color(0.9f, 0.7f, 0.3f)));
            }

            // ═══════════════════════════════════════════════════════════
            //  执行日志（已在 DrawSetupTab 外层 ScrollView 中，无需嵌套）
            // ═══════════════════════════════════════════════════════════
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("执行日志", EditorStyles.boldLabel);
            if (_charSetupLogMessages.Count == 0)
            {
                EditorGUILayout.LabelField("  （暂无日志）", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var msg in _charSetupLogMessages)
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

        // ═══════════════════════════════════════════════════════════════
        // Character Setup 一站式执行逻辑
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 一站式执行：保存 AnimSet → 复制 Controller → 替换 Locomotion → 装配 Prefab →
        /// 绑定攻击/技能/过渡动画 → 自动传播到共享步骤。
        /// </summary>
        void ExecuteCharacterSetup()
        {
            CharSetupLog("══════════════════");
            CharSetupLog("   主角 Prefab 一站式装配");
            CharSetupLog("══════════════════");

            // 先固定当前角色目录，后续 AnimSet/SkillData 生成全部只允许使用该目录的已保存资产。
            _charSetupFolderPath = AssetDatabase.GetAssetPath(_charSetupFolder);
            _charSetupName = Path.GetFileName(_charSetupFolderPath);
            if (string.IsNullOrEmpty(_charSetupPrefix))
                _charSetupPrefix = _charSetupName;

            // 首次创建兜底：编辑缓冲与磁盘都没有有效 AnimSet 时，先按 FBX 文件名自动扫描填充，
            // 避免“第一次创建必失败、必须手动扫描后再创建第二次”。
            if (_charSetupEditAnimSet == null || !_charSetupEditAnimSet.HasAnyClip())
            {
                CharSetupLog("  ⏳ 动画集为空，自动扫描角色 FBX 填充...");
                AutoFillCharSetupFromFolder();
                if (_charSetupEditAnimSet != null && _charSetupEditAnimSet.HasAnyClip())
                {
                    SaveCharAnimSetToCharacterDir();
                    _charSetupAnimDirty = false;
                    CharSetupLog("  ✔ 自动扫描完成并已保存动画集");
                }
            }

            // 在 Preflight 前就固定 AnimSet 来源，避免空的编辑缓冲先触发错误校验。
            if (_charSetupAnimDirty)
                SaveCharAnimSetToCharacterDir();
            RestoreSavedCharacterAnimSetForSkillData(forceReload: true);
            if (_charSetupEditAnimSet == null || !_charSetupEditAnimSet.HasAnyClip())
            {
                CharSetupLog("❌ 当前角色 AnimSet 为空，已中止生成，避免清空已有 SkillData。请检查动画 FBX 命名（<角色>@attackNN / @spellNN）后重试。");
                return;
            }

            // ── P0-1：Preflight 前置校验 ──────────────────────────
            {
                var ctx = new PreflightValidator.CharacterContext
                {
                    sourceController = _charSetupSourceController,
                    controllerPath = $"{AssetDatabase.GetAssetPath(_charSetupFolder)}/{_charSetupName}_Locomotion.controller",
                    folderPath = AssetDatabase.GetAssetPath(_charSetupFolder),
                    characterName = Path.GetFileName(AssetDatabase.GetAssetPath(_charSetupFolder)),
                    modelFBXPath = _charSetupModelFBXPath,
                    animSet = _charSetupEditAnimSet,
                    folderAsset = _charSetupFolder,
                };
                var results = PreflightValidator.ValidateCharacter(ctx);
                foreach (var r in results)
                {
                    string icon = r.severity == PreflightValidator.Severity.Block ? "❌" :
                                  r.severity == PreflightValidator.Severity.Warning ? "⚠" : "";
                    CharSetupLog($"  {icon} [{r.check}] {r.detail}");
                }
                if (PreflightValidator.HasBlocks(results))
                {
                    CharSetupLog("  ⛔ 前置校验未通过，已中止执行");
                    return;
                }
                CharSetupLog("  ✔ 前置校验通过");

                // ── P0 A3：变更计划预览 ──────────────────────────
                var targetCtx = BuildCharacterTargetContext();
                if (targetCtx != null)
                {
                    ShowChangePlanForStep(targetCtx,
                        SetupPipeline.StepType.CharacterSetup);
                }
            }

            // ── 阶段 1：生成 Controller + 替换 Locomotion ──────────

            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { _charSetupFolderPath });
            var fbxPaths = fbxGuids.Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".FBX", System.StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            _charSetupModelFBXPath = null;
            foreach (var path in fbxPaths)
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!fileName.Contains("@"))
                {
                    _charSetupModelFBXPath = path;
                    _charSetupPrefix = fileName;
                    break;
                }
            }

            if (string.IsNullOrEmpty(_charSetupModelFBXPath))
            {
                CharSetupLog("❌ 找不到主模型 FBX（不含 @ 的文件）！");
                return;
            }

            CharSetupLog($"  模型: {_charSetupModelFBXPath}");
            CharSetupLog($"  角色名: {_charSetupName} | 前缀: {_charSetupPrefix}");

            var importer = AssetImporter.GetAtPath(_charSetupModelFBXPath) as ModelImporter;
            if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            {
                CharSetupLog("  设置 Humanoid Avatar...");
                importer.animationType = ModelImporterAnimationType.Human;
                importer.SaveAndReimport();
            }

            // ── 2026-08-03 装配疏漏⑥:材质提取(内嵌材质 → materials/ 下 .mat 资产,自动重定向) ──
            ExtractModelMaterials(importer);

            // ── 2026-08-03 装配疏漏②:动画 FBX 批量导入配置(Humanoid+共享模型 Avatar+RootMotion 节点) ──
            ConfigureAnimationFbxImporters();

            // FBX Reimport 可能使旧 AnimSet 中的 AnimationClip 引用失效；
            // Controller/SkillData 必须使用导入完成后重新从磁盘读取的 AnimSet。
            RestoreSavedCharacterAnimSetForSkillData(forceReload: true);
            RebindAnimSetClipsFromAnimationFiles();
            if (_charSetupEditAnimSet == null || !_charSetupEditAnimSet.HasAnyClip())
            {
                CharSetupLog("❌ 动画 FBX 重导入后 AnimSet 引用失效/为空，已中止生成，未覆盖 SkillData");
                return;
            }
            SaveCharAnimSetToCharacterDir();

            _charSetupControllerPath = $"{_charSetupFolderPath}/{_charSetupName}_Locomotion.controller";

            if (_charSetupSourceController == null)
            {
                CharSetupLog("❌ 未指定模板 Animator Controller，请先拖入 Controller 模板");
                return;
            }

            string sourceControllerPath = AssetDatabase.GetAssetPath(_charSetupSourceController);
            // ── P0-2：DeleteAsset 前创建备份 ──────────────────────────
            AssetBackupService.BackupBeforeOverwrite(_charSetupControllerPath);
            if (System.IO.File.Exists(Path.GetFullPath(_charSetupControllerPath).Replace('/', '\\')))
                AssetDatabase.DeleteAsset(_charSetupControllerPath);

            if (!AssetDatabase.CopyAsset(sourceControllerPath, _charSetupControllerPath))
            {
                CharSetupLog($"❌ 复制 Controller 失败（源：{sourceControllerPath}）");
                return;
            }
            AssetDatabase.ImportAsset(_charSetupControllerPath, ImportAssetOptions.ForceUpdate);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(_charSetupControllerPath);
            if (controller == null)
            {
                CharSetupLog("❌ 无法加载 Controller");
                return;
            }

            int replaced = 0;
            for (int li = 0; li < controller.layers.Length; li++)
            {
                var layer = controller.layers[li];
                bool isStrafe = layer.name == "CombatState";
                replaced += ReplaceLocomotionClipsInSM(layer.stateMachine, isStrafe);
            }
            EditorUtility.SetDirty(controller);

            CharSetupLog($"  Locomotion 替换: {replaced} 个");
            if (_charSetupIdleClip != null) CharSetupLog($"    Idle → {_charSetupIdleClip.name}");
            if (_charSetupRunClip != null) CharSetupLog($"    Run → {_charSetupRunClip.name}");
            if (_charSetupRunFastClip != null) CharSetupLog($"    Sprint → {_charSetupRunFastClip.name}");

            // ── 阶段 3：装配 Prefab ────────────────────────────────
            _charSetupPrefabPath = AssembleCharSetupPrefab();
            if (string.IsNullOrEmpty(_charSetupPrefabPath)) return;

            AssetDatabase.SaveAssets();

            // ── 阶段 4：绑定攻击 / 技能 / 过渡动画 ─────────────────
            if (_charSetupEditAnimSet == null)
            {
                CharSetupLog("⚠ 动画集未初始化，跳过动画绑定");
            }
            else
            {
                var animSet = _charSetupEditAnimSet;
                int totalBound = 0;

                // ── 绑定攻击动画组 ──────────────────────────────────
                CharSetupLog($"\n  绑定 {animSet.attacks.Count} 组攻击动画...");
                foreach (var seq in animSet.attacks)
                {
                    if (!seq.HasAnySegment())
                    {
                        CharSetupLog($"  {seq.id}: (空 — 跳过)");
                        continue;
                    }
                    BindAnimSequenceToController(controller, seq);
                    CharSetupLog($"  {seq.id}: {seq.segments.Count} 段" +
                        (seq.leadIn != null ? " + 起手" : "") +
                        (seq.leadOut != null ? " + 收招" : ""));
                    totalBound++;
                }

                // ── 绑定技能动画组 ──────────────────────────────────
                CharSetupLog($"\n  绑定 {animSet.skills.Count} 组技能动画...");
                foreach (var seq in animSet.skills)
                {
                    if (!seq.HasAnySegment())
                    {
                        CharSetupLog($"  {seq.id}: (空 — 跳过)");
                        continue;
                    }
                    BindAnimSequenceToController(controller, seq);
                    CharSetupLog($"  {seq.id}: {seq.segments.Count} 段" +
                        (seq.leadIn != null ? " + 起手" : "") +
                        (seq.leadOut != null ? " + 收招" : ""));
                    totalBound++;
                }

                // ── 清除超出范围的旧状态 ───────────────────────────
                CharSetupLog("\n  清除超出范围的旧状态...");
                int maxAttackSlots = AnimStateNaming.AttackIndexMax;
                int maxSpellSlots = AnimStateNaming.AttackIndexMax;

                for (int i = animSet.attacks.Count; i < maxAttackSlots; i++)
                {
                    string stateName = AnimStateNaming.AttackState(i + 1);
                    if (ClearCharSetupMotionByName(controller, stateName))
                        CharSetupLog($"  清除 {stateName}");
                }

                for (int i = animSet.skills.Count; i < maxSpellSlots; i++)
                {
                    string label = AnimStateNaming.SkillSuffix(i);
                    string stateName = AnimStateNaming.SkillState(label);
                    if (ClearCharSetupMotionByName(controller, stateName))
                        CharSetupLog($"  清除 {stateName}");
                }

                // ── 绑定通用过渡动画 ────────────────────────────────
                CharSetupLog($"\n  绑定 {animSet.blendTransitions.Count} 组通用过渡...");
                int totalTrans = 0;
                foreach (var trans in animSet.blendTransitions)
                {
                    if (trans == null) continue;
                    BindBlendTransitionToController(controller, trans);
                    totalTrans++;
                    string fromLabel = trans.fromClip != null ? trans.fromClip.name : "?";
                    string toLabel = trans.toClip != null ? trans.toClip.name : "?";
                    CharSetupLog($"  {trans.type}: {fromLabel} → {toLabel}");
                }

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                CharSetupLog($"═══════════════════");
                CharSetupLog($"  动画绑定完成！{totalBound} 组动画序列 + {totalTrans} 组过渡已绑定");
            }

            // ── 2026-08-03 装配疏漏⑤:Controller → Override 自动转换(P1-5 工作流) ──
            ConvertCharControllerToOverride(controller);

            // ── 运行时 SkillData 生成（只使用已保存的 AnimSet） ──
            // 生成前关闭可能持有旧 WorkingCopy 的 SkillBuilder 窗口，并禁止其 OnDisable 回写。
            // 否则首次创建生成完 SkillData 后，旧编辑快照会把 animClips/graphData 清空。
            CloseSkillBuilderWindowsBeforeExternalWrite();
            // 生成前再做一次强制重载+按 FBX 回绑：上面的 Controller 复制/Refresh、Prefab 装配等
            // 步骤可能触发资产刷新，使缓冲里的 Clip 引用失效（首次创建时尤其明显）。
            RestoreSavedCharacterAnimSetForSkillData(forceReload: true);
            RebindAnimSetClipsFromAnimationFiles();
            GenerateStarterSkillDataAssets();
            if (!ValidateGeneratedSkillDataAssets())
            {
                CharSetupLog("❌ SkillData 生成后校验失败，已中止完成标记");
                return;
            }
            BindGeneratedSkillDataToCharacterPrefab();

            // ── 阶段 5：自动传播到共享步骤 ─────────────────────────
            var charPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(_charSetupPrefabPath);
            if (charPrefab != null)
            {
                _playerTarget = charPrefab;
                _playerMotor = charPrefab.GetComponentInChildren<CharacterMotor>(true);
                TryPropagateTargetToSharedSteps(charPrefab);
                CharSetupLog("  ✅ 已自动传播角色 Prefab → Ragdoll / SpringBone / Weapon / Diagnostic");
            }

            // ── P0-3：执行后 Prefab 完整性校验 ───────────────────────
            {
                CharSetupLog("\n── Prefab 完整性校验 ────");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_charSetupPrefabPath);
                var ctrl = prefab != null ? prefab.GetComponent<Animator>()?.runtimeAnimatorController : null;
                var issues = PrefabValidator.Validate(
                    SetupPipeline.PipelineTarget.Character, prefab, ctrl);
                CharSetupLog(PrefabValidator.FormatIssues(issues));
                if (PrefabValidator.HasErrors(issues))
                {
                    CharSetupLog("  ❌ 存在 Error 级别问题，已阻止 Character Setup 完成标记");
                    return;
                }
            }

            // ── 流水线整合：一键装配同时完成 ②PlayerConfig 关联 与 ④联机装配 ──
            // ③Override 已在装配过程中生成（ConvertCharControllerToOverride）。
            // 后续 ②③④ 步骤仅作为调整/重新校验入口，不再是必须手动执行的独立步骤。
            try
            {
                if (EnsurePlayerConfigLinked())
                    CharSetupLog("  ✔ ② PlayerConfig 已自动创建/关联");
                else
                    CharSetupLog("  ⚠ ② PlayerConfig 自动关联失败，可在 Player Config 步骤手动确认");

                // 联机步骤已合并进一键装配，不再出现在流水线列表中，
                // 这里构造独立状态对象执行，不依赖流水线步骤实例。
                CharSetupLog("  ⏳ ④ 联机装配...");
                var networkStep = new SetupPipeline.StepState
                {
                    step = new SetupPipeline.Step { type = SetupPipeline.StepType.NetworkSetup, label = "Network Setup" }
                };
                ExecuteCharacterNetworkSetup(networkStep);
            }
            catch (System.Exception integrateEx)
            {
                CharSetupLog($"  ⚠ 整合步骤（PlayerConfig/Network）执行异常: {integrateEx.Message}（角色 Prefab 本身已完成，可后续手动执行对应步骤）");
            }

            CharSetupLog("══════════════════");
            CharSetupLog("  一站式装配完成！（Prefab + 动画绑定 + SkillData + PlayerConfig + 联机入口）");
            CharSetupLog("══════════════════");

            // ── P0 A3：标记步骤完成 + 保存指纹 ──────────────────
            var setupCtx = BuildCharacterTargetContext();
            if (setupCtx != null)
            {
                MarkCurrentStepComplete(setupCtx,
                    SetupPipeline.StepType.CharacterSetup);
                CharSetupLog($"  ✔ 步骤完成标记已保存 (GUID: {setupCtx.prefabGuid})");
            }

            RepaintCharacterKitWindow();
        }

        // ═══════════════════════════════════════════════════════════════
        // 2026-08-03 装配流水线自动化补全(用户报 6 项流程疏漏,②④⑤⑥ 为本节新增)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 装配疏漏②:对角色目录全部 @动画 FBX 统一导入配置——
        /// AnimationType=Humanoid + AvatarSetup=CopyFromOther(共享主模型 Avatar,跨 FBX 骨骼一致)
        /// + RootMotionNode("&lt;Root Transform&gt;")。幂等:已合规的跳过。
        /// </summary>
        void ConfigureAnimationFbxImporters()
        {
            // 主模型 Avatar(动画 FBX 的 CopyFromOther 目标)
            Avatar modelAvatar = null;
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(_charSetupModelFBXPath))
                if (sub is Avatar a) { modelAvatar = a; break; }

            int configured = 0;
            var guids = AssetDatabase.FindAssets("t:Model", new[] { _charSetupFolderPath });
            foreach (var guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!Path.GetFileNameWithoutExtension(p).Contains("@")) continue;   // 只处理动画 FBX
                var imp = AssetImporter.GetAtPath(p) as ModelImporter;
                if (imp == null) continue;

                bool dirty = false;
                if (imp.animationType != ModelImporterAnimationType.Human)
                { imp.animationType = ModelImporterAnimationType.Human; dirty = true; }
                if (modelAvatar != null && (imp.avatarSetup != ModelImporterAvatarSetup.CopyFromOther
                                            || imp.sourceAvatar != modelAvatar))
                { imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; imp.sourceAvatar = modelAvatar; dirty = true; }
                if (string.IsNullOrEmpty(imp.motionNodeName))
                { imp.motionNodeName = "Hips"; dirty = true; }

                if (dirty) { imp.SaveAndReimport(); configured++; }
            }
            CharSetupLog($"  动画 FBX 导入配置: {configured} 个(Humanoid + 共享 Avatar + RootMotion 节点)");
        }

        /// <summary>
        /// 装配疏漏⑥:主模型材质切为外部模式(External)——Reimport 后 Unity 自动把内嵌材质
        /// 提取为 .mat 资产并重定向,避免 prefab 引用 FBX 内嵌材质(不可编辑/不可复用)。幂等。
        /// </summary>
        void ExtractModelMaterials(ModelImporter modelImporter)
        {
            if (modelImporter == null) return;
            if (modelImporter.materialLocation != ModelImporterMaterialLocation.External)
            {
                modelImporter.materialLocation = ModelImporterMaterialLocation.External;
                modelImporter.SaveAndReimport();
                CharSetupLog("  材质: 已切换 External 外部材质模式(自动提取为 .mat 资产)");
            }
        }

        AnimatorController ResolveCharacterSourceController(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return null;
            string folder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            string backupFolder = $"{folder}/_Backups";
            if (!AssetDatabase.IsValidFolder(backupFolder)) return null;

            string preferredName = Path.GetFileNameWithoutExtension(prefabPath).Replace("_Character", "") + "_Locomotion";
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { backupFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (candidate != null && string.Equals(candidate.name, preferredName, System.StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { backupFolder }))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null) return candidate;
            }
            return null;
        }

        int SyncOverrideFromCharacterController(AnimatorOverrideController ov, string prefabPath, AnimatorController generatedController = null)
        {
            // _Backups 只在覆盖既有文件时存在；首次创建没有备份目录时，
            // 必须直接使用本次刚复制并替换 Locomotion 的 generatedController，
            // 不能把 Override 的 Locomotion 映射留空，导致 Prefab 首次挂载信息为空。
            var source = ResolveCharacterSourceController(prefabPath) ?? generatedController;
            if (source == null)
            {
                CharSetupLog("  ⚠ 未找到当前角色源 Controller，无法自动补齐 Locomotion 映射");
                return 0;
            }

            int changed = OverrideControllerTool.SyncFromController(ov, source);
            AssetDatabase.SaveAssets();
            CharSetupLog($"  Controller 状态/BlendTree → Override 自动补齐: {changed} 条映射");
            return changed;
        }

        int SyncOverrideFromAnimationDirectories(AnimatorOverrideController ov, string prefabPath)
        {
            string characterFolder = string.IsNullOrEmpty(prefabPath)
                ? null
                : Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            int changed = OverrideControllerTool.SyncFromAnimationDirectories(ov, characterFolder);
            AssetDatabase.SaveAssets();
            CharSetupLog($"  共享/角色动画目录 → Override 自动补齐: {changed} 条映射");
            return changed;
        }

        CharacterAnimSetAsset ResolveCharacterAnimSetForOverride(string prefabPath)
        {
            if (!string.IsNullOrEmpty(_charSetupAnimSetPath))
            {
                var saved = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(_charSetupAnimSetPath);
                if (saved != null) return saved;
            }

            string folder = string.IsNullOrEmpty(prefabPath) ? null : Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
            {
                string folderName = Path.GetFileName(folder);
                string exactPath = $"{folder}/AnimSet_{folderName}.asset";
                var exact = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(exactPath);
                if (exact != null)
                {
                    _charSetupAnimSetPath = exactPath;
                    return exact;
                }

                foreach (var guid in AssetDatabase.FindAssets("t:CharacterAnimSetAsset", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var candidate = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(path);
                    if (candidate != null)
                    {
                        _charSetupAnimSetPath = path;
                        return candidate;
                    }
                }
            }
            return null;
        }

        int SyncOverrideFromCharacterAnimSet(AnimatorOverrideController ov, string prefabPath)
        {
            var animSet = ResolveCharacterAnimSetForOverride(prefabPath);
            if (animSet == null)
            {
                CharSetupLog("  ⚠ 未找到当前角色 AnimSet，Override 保留已有映射");
                return 0;
            }

            int changed = OverrideControllerTool.SyncFromAnimSet(ov, animSet);
            AssetDatabase.SaveAssets();
            CharSetupLog($"  AnimSet → Override 同步: {changed} 条映射");
            return changed;
        }

        /// <summary>
        /// 装配疏漏⑤:复制版 Controller → OverrideController 自动转换(P1-5 工作流)。
        /// 主骨架=_charSetupSourceController(模板);生成 {name}_Override.overrideController 挂 prefab;
        /// 中间复制版 controller 归档 _Backups(保留可查,不再是 prefab 引用)。
        /// </summary>
        void ConvertCharControllerToOverride(AnimatorController sourceCtrl)
        {
            var master = _charSetupSourceController as AnimatorController;
            DefaultConfigCreator.EnsureCharacterControllerParameters(master);
            DefaultConfigCreator.EnsureCharacterControllerParameters(sourceCtrl);
            if (master == null || sourceCtrl == null)
            {
                CharSetupLog("⚠ 模板不是 AnimatorController,跳过 Override 转换(prefab 仍挂复制版)");
                return;
            }

            string report;
            var ov = OverrideControllerTool.CreateOverrideFromExisting(
                master, sourceCtrl, _charSetupFolderPath, $"{_charSetupName}_Override", out report);
            if (ov == null)
            {
                CharSetupLog($"⚠ Override 生成失败:{report}(prefab 仍挂复制版)");
                return;
            }
            _charSetupOverridePath = AssetDatabase.GetAssetPath(ov);
            SyncOverrideFromCharacterController(ov, _charSetupPrefabPath, sourceCtrl);
            SyncOverrideFromAnimationDirectories(ov, _charSetupPrefabPath);
            SyncOverrideFromCharacterAnimSet(ov, _charSetupPrefabPath);
            // 权威兜底：攻击/技能状态按状态名显式映射，防止通用名匹配对错锚
            if (_charSetupEditAnimSet != null)
            {
                int skillMapped = OverrideControllerTool.SyncSkillStatesFromAnimSet(ov, _charSetupEditAnimSet);
                if (skillMapped > 0)
                    CharSetupLog($"  ✔ 攻击/技能状态显式映射: {skillMapped} 条");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_charSetupPrefabPath);
            if (prefab != null)
            {
                var anim = prefab.GetComponent<Animator>();
                if (anim != null)
                {
                    anim.runtimeAnimatorController = ov;
                    EditorUtility.SetDirty(prefab);
                    PrefabUtility.SavePrefabAsset(prefab);
                }
            }

            // 中间复制版归档 _Backups
            string backupDir = $"{_charSetupFolderPath}/_Backups";
            EnsureDirectoryExists(backupDir);
            string bakPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{backupDir}/{_charSetupName}_Locomotion.controller");
            AssetDatabase.MoveAsset(_charSetupControllerPath, bakPath);
            _charSetupControllerPath = bakPath;

            CharSetupLog($"  Override 转换: {_charSetupName}_Override.overrideController 已挂 prefab");
            CharSetupLog($"  中间版 controller 已归档 → {bakPath}");
        }

        static void UnpackNestedCharacterPrefabs(GameObject root)
        {
            if (root == null) return;
            var instanceRoots = new HashSet<GameObject>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(transform.gameObject);
                if (instanceRoot != null && instanceRoot != root)
                    instanceRoots.Add(instanceRoot);
            }
            foreach (var instanceRoot in instanceRoots)
            {
                if (instanceRoot != null && PrefabUtility.IsPartOfPrefabInstance(instanceRoot))
                    PrefabUtility.UnpackPrefabInstance(instanceRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
        }

        static void NormalizePlayerRootIdentity(GameObject root)
        {
            if (root == null) return;
            int playerLayer = LayerMask.NameToLayer("Player");
            root.layer = playerLayer >= 0 ? playerLayer : 8;
            try { root.tag = "Player"; }
            catch (UnityException) { CharSetupLogStatic("  ⚠ 项目未定义 Player Tag，保留当前 Tag"); }
        }

        static void CharSetupLogStatic(string message)
        {
            Debug.Log($"[CharacterKit] {message}");
        }

        static void EnsureRuntimeRootAnimator(GameObject root)
        {
            if (root == null) return;
            var rootAnimator = root.GetComponent<Animator>();
            if (rootAnimator == null)
                rootAnimator = root.AddComponent<Animator>();

            var nested = root.GetComponentsInChildren<Animator>(true);
            foreach (var animator in nested)
            {
                if (animator == rootAnimator) continue;
                if (rootAnimator.avatar == null) rootAnimator.avatar = animator.avatar;
                if (rootAnimator.runtimeAnimatorController == null)
                    rootAnimator.runtimeAnimatorController = animator.runtimeAnimatorController;
                animator.enabled = false;
            }
            rootAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(rootAnimator);
        }

        static void NormalizeRagdollPhysics(GameObject root)
        {
            if (root == null) return;
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer < 0) ignoreRaycastLayer = 2;
            var rootCollider = root.GetComponent<Collider>();
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider == rootCollider) continue;
                collider.isTrigger = true;
                collider.gameObject.layer = ignoreRaycastLayer;
                EditorUtility.SetDirty(collider);
                var rigidbody = collider.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    rigidbody.isKinematic = true;
                    rigidbody.useGravity = false;
                    rigidbody.detectCollisions = false;
                    EditorUtility.SetDirty(rigidbody);
                }
            }
        }

        [MenuItem("Tools/Character Kit/Maintenance/Rebuild h_ezreal From Folder")]
        static void RebuildEzrealFromFolder()
        {
            const string folder = "Assets/_Game/character/h_ezreal";
            const string model = folder + "/model/h_ezreal.FBX";
            var folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
            var source = DefaultConfigCreator.GetOrCreateDefaultCharacterController();
            if (folderAsset == null || !(AssetImporter.GetAtPath(model) is ModelImporter))
            {
                Debug.LogError($"[CharacterKit] h_ezreal 资源目录或模型不存在: {folder}");
                return;
            }

            var panel = new CharacterKitPanel
            {
                _charSetupFolder = folderAsset,
                _charSetupFolderPath = folder,
                _charSetupName = "h_ezreal",
                _charSetupPrefix = "h_ezreal",
                _charSetupSourceController = source
            };
            panel.InitCharSetupAnimSet();
            panel.AutoFillCharSetupFromFolder();
            panel.ExecuteCharacterSetup();
        }

        [MenuItem("Tools/Character Kit/Maintenance/Rebuild Current Player As Standalone Prefab")]
        static void RebuildCurrentPlayerAsStandalonePrefab()
        {
            string prefabPath = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "");
            if (!IsValidPrefabPath(prefabPath))
            {
                Debug.LogError("[CharacterKit] 当前玩家 Prefab 无效，无法重建独立 Prefab。");
                return;
            }

            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            string characterName = Path.GetFileName(folder);
            string modelPath = $"{folder}/model/{characterName}.FBX";
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter))
            {
                Debug.LogError($"[CharacterKit] 找不到当前角色主模型 FBX：{modelPath}");
                return;
            }

            var master = AssetDatabase.LoadAssetAtPath<AnimatorController>(OverrideControllerTool.MasterControllerPath);
            var animSet = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>($"{folder}/AnimSet_{characterName}.asset");
            if (master == null || animSet == null || !animSet.HasAnyClip())
            {
                Debug.LogError("[CharacterKit] Default Controller 或当前角色 AnimSet 无效，已中止独立 Prefab 重建。");
                return;
            }

            var panel = new CharacterKitPanel
            {
                _charSetupFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder),
                _charSetupFolderPath = folder,
                _charSetupName = characterName,
                _charSetupPrefix = characterName,
                _charSetupModelFBXPath = modelPath,
                _charSetupSourceController = master,
                _charSetupAnimSetPath = AssetDatabase.GetAssetPath(animSet),
                _charSetupEditAnimSet = ScriptableObject.CreateInstance<CharacterAnimSetAsset>()
            };
            panel._charSetupEditAnimSet.CopyFrom(animSet);
            panel._charSetupEditAnimSet.name = "AnimSet_EditBuffer";
            panel.ExecuteCharacterSetup();
            Debug.Log($"[CharacterKit] 已按独立 Prefab 流程重建当前角色：{prefabPath}");
        }

        [MenuItem("Tools/Character Kit/Maintenance/Repair Current Player Runtime Assembly")]
        static void RepairCurrentPlayerRuntimeAssembly()
        {
            string prefabPath = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "");
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError("[CharacterKit] 当前玩家入口为空，无法修复运行时装配。");
                return;
            }
            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            if (!IsValidPrefabPath(prefabPath) || string.IsNullOrEmpty(folder))
            {
                Debug.LogError($"[CharacterKit] 当前玩家 Prefab 无效：{prefabPath}");
                return;
            }
            var panel = new CharacterKitPanel
            {
                _charSetupPrefabPath = prefabPath,
                _charSetupFolderPath = folder,
                _charSetupName = Path.GetFileName(folder),
                _charSetupPrefix = Path.GetFileName(folder)
            };
            panel.BindGeneratedSkillDataToCharacterPrefab();
            Debug.Log($"[CharacterKit] 当前玩家运行时装配修复完成：{prefabPath}");
        }

        List<SkillData> LoadGeneratedSkillDataAssets(string skillFolder)
        {
            var result = new List<SkillData>();
            var seen = new HashSet<SkillData>();
            string prefix = string.IsNullOrEmpty(_charSetupPrefix) ? _charSetupName : _charSetupPrefix;

            // 首次创建时新建资产可能尚未进入 FindAssets 索引；按规范路径直接加载，
            // 让“文件刚创建但索引还没刷新”不会导致校验/绑定漏掉 Attack/Skill。
            for (int i = 1; i <= AnimStateNaming.AttackIndexMax; i++)
            {
                string path = $"{skillFolder}/{prefix}@attack{i:D2}.asset";
                var data = AssetDatabase.LoadAssetAtPath<SkillData>(path);
                if (data != null && seen.Add(data)) result.Add(data);
            }
            for (int i = 1; i <= 4; i++)
            {
                string path = $"{skillFolder}/{prefix}@spell{i:D2}.asset";
                var data = AssetDatabase.LoadAssetAtPath<SkillData>(path);
                if (data != null && seen.Add(data)) result.Add(data);
            }

            // 兼容旧角色自定义命名资产。
            foreach (var guid in AssetDatabase.FindAssets("t:SkillData", new[] { skillFolder }))
            {
                var data = AssetDatabase.LoadAssetAtPath<SkillData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && seen.Add(data)) result.Add(data);
            }
            return result;
        }

        bool ValidateGeneratedSkillDataAssets()
        {
            if (string.IsNullOrEmpty(_charSetupFolderPath)) return false;
            string skillFolder = $"{_charSetupFolderPath}/SkillData";
            if (!AssetDatabase.IsValidFolder(skillFolder)) return false;

            int expectedAttack = _charSetupEditAnimSet != null ? _charSetupEditAnimSet.attacks.Count : 0;
            int expectedSkill = _charSetupEditAnimSet != null ? Mathf.Min(_charSetupEditAnimSet.skills.Count, 4) : 0;
            // 防止"空转绿"：AnimSet 没有任何攻击/技能序列时必须失败，
            // 否则 expected=0 会让校验恒真、后续绑定出全空槽位。
            if (expectedAttack + expectedSkill == 0)
            {
                CharSetupLog("  ❌ SkillData 校验: AnimSet 没有任何攻击/技能序列，请检查动画 FBX 命名（<角色>@attackNN / @spellNN）并重新扫描");
                return false;
            }
            int validAttack = 0;
            int validSkill = 0;
            var invalid = new List<string>();
            foreach (var data in LoadGeneratedSkillDataAssets(skillFolder))
            {
                if (data == null) continue;
                string path = AssetDatabase.GetAssetPath(data);
                bool hasClip = data.animClips != null && data.animClips.Length > 0 && data.animClips[0] != null;
                bool hasAnimNode = data.graphData != null
                    && data.graphData.Any(node => node is AnimClipLayerData anim && anim.animClip != null);
                if (!hasClip || !hasAnimNode)
                {
                    invalid.Add(Path.GetFileName(path));
                    continue;
                }
                if (data.category == SkillCategory.BasicAttack) validAttack++;
                else if (data.category == SkillCategory.Skill) validSkill++;
            }

            bool valid = invalid.Count == 0
                && validAttack >= expectedAttack
                && validSkill >= expectedSkill;
            if (!valid)
                CharSetupLog($"  ❌ SkillData 校验: Attack={validAttack}/{expectedAttack}, Skill={validSkill}/{expectedSkill}, 空节点={string.Join(", ", invalid)}");
            else
                CharSetupLog($"  ✅ SkillData 校验: Attack={validAttack}, Skill={validSkill}, 动画节点完整");
            return valid;
        }

        void BindGeneratedSkillDataToCharacterPrefab()
        {
            if (string.IsNullOrEmpty(_charSetupPrefabPath) || string.IsNullOrEmpty(_charSetupFolderPath))
                return;

            string skillFolder = $"{_charSetupFolderPath}/SkillData";
            if (!AssetDatabase.IsValidFolder(skillFolder))
            {
                CharSetupLog($"  ⚠ SkillData 目录不存在，跳过运行时技能绑定: {skillFolder}");
                return;
            }

            var attacks = new List<SkillData>();
            var skills = new List<SkillData>();
            foreach (var data in LoadGeneratedSkillDataAssets(skillFolder))
            {
                if (data == null) continue;
                if (data.category == SkillCategory.BasicAttack) attacks.Add(data);
                else if (data.category == SkillCategory.Skill) skills.Add(data);
            }
            attacks = attacks.OrderBy(data => data.name, System.StringComparer.OrdinalIgnoreCase).ToList();
            skills = skills.OrderBy(data => data.name, System.StringComparer.OrdinalIgnoreCase).ToList();

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(_charSetupPrefabPath);
                if (root == null) return;

                UnpackNestedCharacterPrefabs(root);
                NormalizePlayerRootIdentity(root);
                EnsureRuntimeRootAnimator(root);
                NormalizeRagdollPhysics(root);

                var skillController = root.GetComponent<Game.SkillSystem.SkillController>();
                if (skillController == null)
                    skillController = root.AddComponent<Game.SkillSystem.SkillController>();
                var hitDetector = root.GetComponent<Game.SkillSystem.HitDetector>();
                if (hitDetector == null)
                    hitDetector = root.AddComponent<Game.SkillSystem.HitDetector>();
                var animPlayer = root.GetComponent<Game.SkillSystem.SkillAnimPlayer>();
                if (animPlayer == null)
                    animPlayer = root.AddComponent<Game.SkillSystem.SkillAnimPlayer>();
                var movementController = root.GetComponent<Game.SkillSystem.SkillMovementController>();
                if (movementController == null)
                    movementController = root.AddComponent<Game.SkillSystem.SkillMovementController>();
                var weaponHolder = root.GetComponent<Game.SkillSystem.WeaponHolder>();
                if (weaponHolder == null)
                    weaponHolder = root.AddComponent<Game.SkillSystem.WeaponHolder>();
                var inputHandler = root.GetComponent<CharacterInputHandler>();
                if (inputHandler == null)
                    inputHandler = root.AddComponent<CharacterInputHandler>();

                skillController.skillSlots = new SkillData[8];
                skillController.attackSlots = new SkillData[8];
                for (int i = 0; i < skills.Count && i < skillController.skillSlots.Length; i++)
                    skillController.skillSlots[i] = skills[i];
                for (int i = 0; i < attacks.Count && i < skillController.attackSlots.Length; i++)
                    skillController.attackSlots[i] = attacks[i];

                skillController.animPlayer = animPlayer;
                skillController.hitDetector = hitDetector;
                skillController.movementController = movementController;
                skillController.weaponHolder = weaponHolder;
                skillController.inputHandler = inputHandler;
                hitDetector.autoSetupHitMask = true;

                EditorUtility.SetDirty(skillController);
                EditorUtility.SetDirty(hitDetector);
                PrefabUtility.SaveAsPrefabAsset(root, _charSetupPrefabPath);
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.ImportAsset(_charSetupPrefabPath, ImportAssetOptions.ForceUpdate);
            var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(_charSetupPrefabPath);
            var savedController = savedPrefab != null
                ? savedPrefab.GetComponent<Game.SkillSystem.SkillController>() : null;
            var savedHitDetector = savedPrefab != null
                ? savedPrefab.GetComponent<Game.SkillSystem.HitDetector>() : null;
            int savedAttacks = savedController != null && savedController.attackSlots != null
                ? savedController.attackSlots.Count(data => data != null) : 0;
            int savedSkills = savedController != null && savedController.skillSlots != null
                ? savedController.skillSlots.Count(data => data != null) : 0;
            bool wired = savedController != null && savedHitDetector != null
                && savedController.hitDetector == savedHitDetector
                && savedAttacks == Mathf.Min(attacks.Count, 8)
                && savedSkills == Mathf.Min(skills.Count, 8);
            CharSetupLog(wired
                ? $"  ✅ 运行时技能已绑定: Attack={savedAttacks}, Skill={savedSkills}, HitDetector=已绑定"
                : $"  ❌ 运行时技能绑定校验失败: Attack={savedAttacks}/{Mathf.Min(attacks.Count, 8)}, Skill={savedSkills}/{Mathf.Min(skills.Count, 8)}, HitDetector={(savedHitDetector != null ? "存在" : "缺失")}");
        }

        CharacterAnimSetAsset LoadSavedCharacterAnimSet()
        {
            if (string.IsNullOrEmpty(_charSetupFolderPath)) return null;
            string path = $"{_charSetupFolderPath}/AnimSet_{Path.GetFileName(_charSetupFolderPath)}.asset";
            var saved = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(path);
            if (saved != null && saved.HasAnyClip()) return saved;

            var candidates = AssetDatabase.FindAssets("t:CharacterAnimSetAsset", new[] { _charSetupFolderPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>)
                .Where(asset => asset != null && asset.HasAnyClip())
                .OrderByDescending(asset => AssetDatabase.GetAssetPath(asset) == path)
                .ThenByDescending(asset => AssetDatabase.GetAssetPath(asset), System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            return candidates.FirstOrDefault();
        }

        void RestoreSavedCharacterAnimSetForSkillData(bool forceReload = false)
        {
            var saved = LoadSavedCharacterAnimSet();
            if (saved == null)
            {
                CharSetupLog($"  ⚠ 未找到有效的已保存 AnimSet：{_charSetupFolderPath}");
                return;
            }

            if (forceReload || _charSetupEditAnimSet == null || !_charSetupEditAnimSet.HasAnyClip())
            {
                _charSetupEditAnimSet = ScriptableObject.CreateInstance<CharacterAnimSetAsset>();
                _charSetupEditAnimSet.CopyFrom(saved);
                _charSetupEditAnimSet.name = "AnimSet_EditBuffer";
                _charSetupAnimSetPath = AssetDatabase.GetAssetPath(saved);
                CharSetupLog($"  已强制加载角色 AnimSet：{_charSetupAnimSetPath}（Attack={saved.attacks.Count}, Skill={saved.skills.Count}）");
            }
        }

        /// <summary>
        /// 装配疏漏④:按 AnimSet 攻击/技能组生成初版 SkillData；
        /// 普攻 h_xxx@attackNN(BasicAttack,skillId=100+N,trigger=Attack);
        /// 技能 h_xxx@spellNN(Skill,skillId=200+N,trigger=SkillQ/W/E/R)。
        /// 自动注入动画层节点(visualOnly,Timeline 轨道可见)。
        /// </summary>
        static void CloseSkillBuilderWindowsBeforeExternalWrite()
        {
            const string suppressKey = "Test09.CharacterKit.SuppressSkillBuilderAutoSave";
            // Close() 触发 OnDisable 的时机可能晚于当前调用栈；不能在 finally 立即清除开关，
            // 否则旧 WorkingCopy 仍会在本帧末尾回写并覆盖刚生成的 SkillData。
            EditorPrefs.SetBool(suppressKey, true);
            foreach (var window in Resources.FindObjectsOfTypeAll<SkillBuilderWizard>())
            {
                if (window != null)
                    window.Close();
            }
            EditorApplication.delayCall += () => EditorPrefs.SetBool(suppressKey, false);
        }

        static bool EnsureAssetDatabaseFolder(string assetFolder)
        {
            assetFolder = assetFolder?.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(assetFolder)) return false;
            if (AssetDatabase.IsValidFolder(assetFolder)) return true;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string name = Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return false;
            if (!AssetDatabase.IsValidFolder(parent) && !EnsureAssetDatabaseFolder(parent)) return false;

            // 用 AssetDatabase.CreateFolder 注册新目录，不触发全局 Refresh/旧窗口 OnDisable。
            string guid = AssetDatabase.CreateFolder(parent, name);
            return !string.IsNullOrEmpty(guid) && AssetDatabase.IsValidFolder(assetFolder);
        }

        void GenerateStarterSkillDataAssets()
        {
            var animSet = _charSetupEditAnimSet;
            if (animSet == null || string.IsNullOrEmpty(_charSetupFolderPath)) return;
            string dir = $"{_charSetupFolderPath}/SkillData";
            if (!EnsureAssetDatabaseFolder(dir))
            {
                CharSetupLog($"  ❌ 无法注册 SkillData 目录: {dir}");
                return;
            }
            CharSetupLog($"  SkillData 生成源：{_charSetupAnimSetPath} | Attack={animSet.attacks.Count} | Skill={animSet.skills.Count}");
            int created = 0;

            for (int i = 0; i < animSet.attacks.Count && i < AnimStateNaming.AttackIndexMax; i++)
            {
                int slot = i + 1;
                var clip = ResolveSequenceClip(animSet.attacks[i], $"attack{slot:D2}");
                if (clip == null)
                {
                    CharSetupLog($"  ⚠ 缺少 Attack {slot} 动画 Clip，跳过 SkillData");
                    continue;
                }
                string path = $"{dir}/{_charSetupPrefix}@attack{slot:D2}.asset";
                var sd = CreateOrGetSkillData(path, $"{_charSetupPrefix}@attack{slot:D2}", SkillCategory.BasicAttack);
                bool changed = false;
                if (sd.skillName != $"{_charSetupPrefix}@attack{slot:D2}") { sd.skillName = $"{_charSetupPrefix}@attack{slot:D2}"; changed = true; }
                if (sd.category != SkillCategory.BasicAttack) { sd.category = SkillCategory.BasicAttack; changed = true; }
                if (sd.animTrigger != "Attack") { sd.animTrigger = "Attack"; changed = true; }
                if (sd.animStateId != slot) { sd.animStateId = slot; changed = true; }
                if (sd.skillId != 100 + slot) { sd.skillId = 100 + slot; changed = true; }
                if (sd.cooldown != 1f) { sd.cooldown = 1f; changed = true; }
                if (sd.animClips == null || sd.animClips.Length != 1 || sd.animClips[0] != clip)
                {
                    sd.animClips = new[] { clip };
                    changed = true;
                }
                if (EnsureAnimLayerNode(sd, clip)) changed = true;
                if (EnsureStarterExecutableNodes(sd, clip, isAttack: true)) changed = true;
                if (changed)
                {
                    EditorUtility.SetDirty(sd);
                    created++;
                }
            }

            for (int i = 0; i < animSet.skills.Count && i < 4; i++)
            {
                int slot = i + 1;
                string suffix = AnimStateNaming.SkillSuffix(i);   // Q/W/E/R
                var clip = ResolveSequenceClip(animSet.skills[i], $"spell{slot:D2}");
                if (clip == null)
                {
                    CharSetupLog($"  ⚠ 缺少 Skill {suffix} 动画 Clip，跳过 SkillData");
                    continue;
                }
                string path = $"{dir}/{_charSetupPrefix}@spell{slot:D2}.asset";
                var sd = CreateOrGetSkillData(path, $"{_charSetupPrefix}@spell{slot:D2}", SkillCategory.Skill);
                bool changed = false;
                string trigger = "Skill" + suffix;
                if (sd.skillName != $"{_charSetupPrefix}@spell{slot:D2}") { sd.skillName = $"{_charSetupPrefix}@spell{slot:D2}"; changed = true; }
                if (sd.category != SkillCategory.Skill) { sd.category = SkillCategory.Skill; changed = true; }
                if (sd.animTrigger != trigger) { sd.animTrigger = trigger; changed = true; }
                if (sd.animStateId != slot) { sd.animStateId = slot; changed = true; }
                if (sd.skillId != 200 + slot) { sd.skillId = 200 + slot; changed = true; }
                if (sd.cooldown != 5f + i * 2f) { sd.cooldown = 5f + i * 2f; changed = true; }
                if (sd.animClips == null || sd.animClips.Length != 1 || sd.animClips[0] != clip)
                {
                    sd.animClips = new[] { clip };
                    changed = true;
                }
                if (EnsureAnimLayerNode(sd, clip)) changed = true;
                if (EnsureStarterExecutableNodes(sd, clip, isAttack: false)) changed = true;
                if (changed)
                {
                    EditorUtility.SetDirty(sd);
                    created++;
                }
            }

            if (created > 0)
            {
                // 不调用 AssetDatabase.Refresh()：它会触发 SkillBuilderWizard.OnDisable，
                // 向导中的旧 WorkingCopy 可能把刚生成的 Clip/graphData 覆盖为空。
                // SaveAssets 后必须把当前缓存对象卸载再导入：首次 CreateAsset 时，
                // AssetDatabase 可能仍返回创建前的空对象，导致本次校验看到 0 Clip/0 graphData，
                // 第二次创建才读到磁盘内容。
                AssetDatabase.SaveAssets();
                ForceReloadGeneratedSkillDataAssets(dir);
                CharSetupLog($"  初版/修复 SkillData: {created} 个 → SkillData/(含动画层轨道 visualOnly)");
            }
        }

        void ForceReloadGeneratedSkillDataAssets(string skillFolder)
        {
            var paths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            string prefix = string.IsNullOrEmpty(_charSetupPrefix) ? _charSetupName : _charSetupPrefix;
            for (int i = 1; i <= AnimStateNaming.AttackIndexMax; i++)
                paths.Add($"{skillFolder}/{prefix}@attack{i:D2}.asset");
            for (int i = 1; i <= 4; i++)
                paths.Add($"{skillFolder}/{prefix}@spell{i:D2}.asset");
            foreach (var guid in AssetDatabase.FindAssets("t:SkillData", new[] { skillFolder }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            foreach (var path in paths)
            {
                var loaded = AssetDatabase.LoadAssetAtPath<SkillData>(path);
                if (loaded != null)
                    Resources.UnloadAsset(loaded);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        [MenuItem("Tools/Character Kit/Maintenance/Repair Current Player SkillData Animations")]
        static void RepairCurrentPlayerSkillDataAnimations()
        {
            string prefabPath = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "");
            if (!IsValidPrefabPath(prefabPath))
            {
                Debug.LogError("[CharacterKit] 当前玩家入口无效，无法修复 SkillData。");
                return;
            }

            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            var panel = new CharacterKitPanel
            {
                _charSetupFolderPath = folder,
                _charSetupName = Path.GetFileName(folder),
                _charSetupPrefix = Path.GetFileName(folder)
            };
            string animSetPath = $"{folder}/AnimSet_{Path.GetFileName(folder)}.asset";
            var animSet = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(animSetPath);
            if (animSet == null)
            {
                string guid = AssetDatabase.FindAssets("t:CharacterAnimSetAsset", new[] { folder }).FirstOrDefault();
                animSet = string.IsNullOrEmpty(guid)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(AssetDatabase.GUIDToAssetPath(guid));
            }
            if (animSet == null)
            {
                Debug.LogError($"[CharacterKit] 未找到当前角色 AnimSet：{folder}");
                return;
            }

            panel._charSetupAnimSetPath = AssetDatabase.GetAssetPath(animSet);
            panel._charSetupEditAnimSet = ScriptableObject.CreateInstance<CharacterAnimSetAsset>();
            panel._charSetupEditAnimSet.CopyFrom(animSet);
            panel.RebindAnimSetClipsFromAnimationFiles();
            panel.SaveCharAnimSetToCharacterDir();
            panel.GenerateStarterSkillDataAssets();
            Debug.Log($"[CharacterKit] 已修复当前角色 SkillData 动画引用：{folder}/SkillData");
        }

        AnimationClip ResolveSequenceClip(CharacterAnimSequence sequence, string fileSuffix)
        {
            var clip = FirstClipOfSequence(sequence);
            if (clip != null) return clip;
            if (string.IsNullOrEmpty(_charSetupFolderPath) || string.IsNullOrEmpty(fileSuffix)) return null;

            string animationFolder = $"{_charSetupFolderPath}/animations";
            if (!AssetDatabase.IsValidFolder(animationFolder)) animationFolder = _charSetupFolderPath;
            string prefix = string.IsNullOrEmpty(_charSetupPrefix) ? _charSetupName : _charSetupPrefix;
            string expectedName = $"{prefix}@{fileSuffix}";

            // 首次创建时 FBX 可能已写入磁盘但尚未进入 FindAssets 索引；
            // 直接按规范路径读取一次，避免必须第二次创建才能得到 Clip。
            string[] directPaths =
            {
                $"{animationFolder}/{expectedName}.FBX",
                $"{animationFolder}/{expectedName}.fbx",
                $"{_charSetupFolderPath}/{expectedName}.FBX",
                $"{_charSetupFolderPath}/{expectedName}.fbx"
            };
            foreach (var directPath in directPaths)
            {
                if (!File.Exists(directPath)) continue;
                clip = LoadFirstClipFromFBX(directPath);
                if (clip != null)
                {
                    if (sequence != null)
                    {
                        if (sequence.segments == null) sequence.segments = new List<AnimationClip>();
                        if (sequence.segments.Count == 0) sequence.segments.Add(clip);
                        else sequence.segments[0] = clip;
                    }
                    CharSetupLog($"  已从 FBX 恢复 {expectedName}: {clip.name}");
                    return clip;
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { animationFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), expectedName, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                clip = LoadFirstClipFromFBX(path);
                if (clip != null)
                {
                    if (sequence != null)
                    {
                        if (sequence.segments == null) sequence.segments = new List<AnimationClip>();
                        if (sequence.segments.Count == 0) sequence.segments.Add(clip);
                        else sequence.segments[0] = clip;
                    }
                    CharSetupLog($"  已从 FBX 恢复 {expectedName}: {clip.name}");
                    return clip;
                }
            }
            return null;
        }

        void RebindAnimSetClipsFromAnimationFiles()
        {
            if (_charSetupEditAnimSet == null) return;
            for (int i = 0; i < _charSetupEditAnimSet.attacks.Count; i++)
                ResolveSequenceClip(_charSetupEditAnimSet.attacks[i], $"attack{i + 1:D2}");
            for (int i = 0; i < _charSetupEditAnimSet.skills.Count && i < 4; i++)
                ResolveSequenceClip(_charSetupEditAnimSet.skills[i], $"spell{i + 1:D2}");
        }

        /// <summary>取动画序列的首个 clip(首段 &gt; 起手 &gt; 收招)。</summary>
        static AnimationClip FirstClipOfSequence(CharacterAnimSequence seq)
        {
            if (seq == null) return null;
            if (seq.segments != null)
                foreach (var c in seq.segments)
                    if (c != null) return c;
            if (seq.leadIn != null) return seq.leadIn;
            return seq.leadOut;
        }

        void SyncSelectedCharacterFolder()
        {
            if (_charSetupFolder == null) return;
            string folderPath = AssetDatabase.GetAssetPath(_charSetupFolder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath)) return;
            if (string.Equals(folderPath, _charSetupLoadedFolderPath, System.StringComparison.OrdinalIgnoreCase)) return;

            _charSetupLoadedFolderPath = folderPath;
            _charSetupFolderPath = folderPath;
            _charSetupName = Path.GetFileName(folderPath);
            _charSetupPrefix = _charSetupName;

            var saved = LoadSavedCharacterAnimSet();
            if (saved != null)
            {
                _charSetupEditAnimSet = ScriptableObject.CreateInstance<CharacterAnimSetAsset>();
                _charSetupEditAnimSet.CopyFrom(saved);
                _charSetupEditAnimSet.name = "AnimSet_EditBuffer";
                _charSetupAnimSetPath = AssetDatabase.GetAssetPath(saved);
                _charSetupAnimDirty = false;
                CharSetupLog($"  已加载当前角色 AnimSet：{_charSetupAnimSetPath}");
            }
            else if (_charSetupEditAnimSet == null || !_charSetupEditAnimSet.HasAnyClip())
            {
                InitCharSetupAnimSet();
            }
        }

        /// <summary>向 SkillData 注入动画层节点(Timeline 轨道;visualOnly=纯可视化,运行时不调度)。</summary>
        static bool EnsureAnimLayerNode(SkillData sd, AnimationClip clip)
        {
            if (sd == null || clip == null) return false;
            if (sd.graphData == null)
                sd.graphData = new List<SkillNodeData>();

            AnimClipLayerData animationNode = null;
            foreach (var node in sd.graphData)
            {
                if (node is AnimClipLayerData animLayer)
                {
                    animationNode = animLayer;
                    break;
                }
            }

            bool changed = false;
            if (animationNode == null)
            {
                animationNode = new AnimClipLayerData();
                sd.graphData.Insert(0, animationNode);
                changed = true;
            }
            if (animationNode.animClip != clip) { animationNode.animClip = clip; changed = true; }
            if (animationNode.triggerTime != 0f) { animationNode.triggerTime = 0f; changed = true; }
            if (!animationNode.visualOnly) { animationNode.visualOnly = true; changed = true; }
            return changed;
        }

        static bool InjectAnimLayerNode(SkillData sd, AnimationClip clip)
        {
            return EnsureAnimLayerNode(sd, clip);
        }

        /// <summary>
        /// 为生成的 SkillData 注入默认可执行节点（移动策略 + 命中）。
        /// 仅在对应类别节点缺失时注入——用户在技能编辑器中自定义的节点不会被覆盖。
        /// 对照 graves 基准：普攻=SlowMove(0.5)+Cursor 面向+6m 远程矩形弹幕；技能=SlowMove(1.0)+Cursor 面向+8m 近战命中。
        /// 缺 MovementData 时运行时攻击期间不做任何移动/朝向控制，原地攻击与移动攻击表现会异常。
        /// </summary>
        static bool EnsureStarterExecutableNodes(SkillData sd, AnimationClip clip, bool isAttack)
        {
            if (sd == null) return false;
            if (sd.graphData == null)
                sd.graphData = new List<SkillNodeData>();

            bool changed = false;

            // 迁移：普攻历史模板曾误用近战挥击（2m 贴脸），现统一改为远程弹幕。
            // 重建时移除旧的默认近战节点，让下方重新注入 RectShotData（不影响技能/自定义节点）。
            if (isAttack)
            {
                for (int i = sd.graphData.Count - 1; i >= 0; i--)
                {
                    if (sd.graphData[i] is MeleeSwingData m && Mathf.Approximately(m.range, 2f))
                    {
                        sd.graphData.RemoveAt(i);
                        changed = true;
                    }
                }
            }

            bool hasMovement = false;
            bool hasDamage = false;
            foreach (var node in sd.graphData)
            {
                if (node == null) continue;
                if (node.Category == SkillNodeCategory.Movement) hasMovement = true;
                if (node.Category == SkillNodeCategory.Melee || node.Category == SkillNodeCategory.Shot
                    || node.Category == SkillNodeCategory.Spawn || node.Category == SkillNodeCategory.Channeled)
                    hasDamage = true;
            }

            if (!hasMovement)
            {
                sd.graphData.Add(new MovementData
                {
                    visualOnly = false,
                    triggerTime = 0f,
                    policy = MovementPolicy.SlowMove,
                    speedMultiplier = isAttack ? 0.5f : 1f,
                    facingMode = FacingMode.Cursor,
                    dashOnCast = false,
                });
                changed = true;
            }

            if (!hasDamage)
            {
                // 命中时机对齐前摇结束点（frontSwing 为归一化时间，换算成秒）
                float hitTime = 0f;
                if (clip != null && sd.frontSwing > 0f && sd.frontSwing < 1f)
                    hitTime = clip.length * sd.frontSwing;

                if (isAttack)
                {
                    // 普攻 = 远程矩形弹幕（对照 graves h_graves@attack01 基准：6m 远程射击）。
                    // 近战挥击只用于技能模板，避免远程角色（法师/枪手）普攻退化成 2m 贴脸挥砍。
                    sd.graphData.Add(new RectShotData
                    {
                        visualOnly = false,
                        triggerTime = hitTime,
                        damage = 5f,
                        shape = ShotShape.Rect,
                        width = 2f,
                        height = 5f,
                        speed = 18f,
                        maxRange = 6f,
                        count = 1,
                        spreadAngle = 0f,
                        hitMode = ShotHitMode.Stop,
                        pierceCount = 0,
                        homingEnabled = false,
                        homingTurnRate = 540f,
                        homingSearchRadius = 10f,
                        initialYawOffset = 0f,
                        initialPitchOffset = 0f,
                        spawnHeight = 0.9f,
                        forwardOffset = 0.3f,
                        checkObstacle = true,
                        obstacleMask = 1,
                    });
                }
                else
                {
                    sd.graphData.Add(new MeleeSwingData
                    {
                        visualOnly = false,
                        triggerTime = hitTime,
                        range = 8f,
                        hitRadius = 2f,
                        hitAngle = 60f,
                        damage = 30f,
                        originHeight = 0.8f,
                        verticalRange = 2f,
                        checkObstacle = true,
                        obstacleMask = 1,
                    });
                }
                changed = true;
            }
            return changed;
        }

        /// <summary>扫描角色文件夹中的 @动画 FBX，自动填充 Idle / Run / RunFast / 攻击组 / 技能组动画字段。</summary>
        void AutoFillCharSetupFromFolder()
        {
            if (_charSetupFolder == null) return;
            if (_charSetupEditAnimSet == null) InitCharSetupAnimSet();
            if (_charSetupEditAnimSet == null)
            {
                CharSetupLog("  ❌ 动画集未初始化，无法填充攻击/技能组");
                return;
            }
            string folderPath = AssetDatabase.GetAssetPath(_charSetupFolder);

            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { folderPath });
            var fbxPaths = fbxGuids.Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".FBX", System.StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase));

            var unmatched = new List<string>();
            foreach (var path in fbxPaths)
            {
                string fileName = Path.GetFileNameWithoutExtension(path).ToLower();
                if (!fileName.Contains("@")) continue;

                string animName = fileName.Substring(fileName.IndexOf('@') + 1);

                if (animName.Contains("idle") && _charSetupIdleClip == null)
                    _charSetupIdleClip = LoadFirstClipFromFBX(path);
                else if (animName == "run" && _charSetupRunClip == null)
                    _charSetupRunClip = LoadFirstClipFromFBX(path);
                else if ((animName == "run_fast" || animName == "sprint") && _charSetupRunFastClip == null)
                    _charSetupRunFastClip = LoadFirstClipFromFBX(path);
                else if (TryParseCharAttackIndex(animName, out int atkIdx))
                    FillCharAttackFromScan(atkIdx, path);
                else if (TryParseCharSkillIndex(animName, out int sklIdx, out bool isEnd))
                    FillCharSkillFromScan(sklIdx, isEnd, path);
                else
                    unmatched.Add(animName);
            }

            CharSetupLog("  自动填充完成");
            if (_charSetupIdleClip != null) CharSetupLog($"  Idle: {_charSetupIdleClip.name}");
            if (_charSetupRunClip != null) CharSetupLog($"  Run: {_charSetupRunClip.name}");
            if (_charSetupRunFastClip != null) CharSetupLog($"  Run Fast: {_charSetupRunFastClip.name}");
            for (int i = 0; i < _charSetupEditAnimSet.attacks.Count; i++)
            {
                var c = _charSetupEditAnimSet.attacks[i]?.GetMainSegment(0);
                if (c != null) CharSetupLog($"  Attack {i + 1}: {c.name}");
            }
            for (int i = 0; i < _charSetupEditAnimSet.skills.Count; i++)
            {
                var seq = _charSetupEditAnimSet.skills[i];
                var c = seq?.GetMainSegment(0);
                if (c != null)
                {
                    string tail = seq.leadOut != null ? $" (+收招: {seq.leadOut.name})" : "";
                    CharSetupLog($"  {seq.id}: {c.name}{tail}");
                }
            }
            if (unmatched.Count > 0)
                CharSetupLog($"  未匹配（已跳过）: {string.Join(", ", unmatched)}");
            RepaintCharacterKitWindow();
        }

        /// <summary>填充攻击组主片段：attack01 → attacks[0]（仅填空槽，不覆盖手动配置）。</summary>
        void FillCharAttackFromScan(int atkIdx, string fbxPath)
        {
            var seq = EnsureCharSequence(_charSetupEditAnimSet.attacks, atkIdx, false);
            if (seq == null || seq.GetMainSegment(0) != null) return;
            var clip = LoadFirstClipFromFBX(fbxPath);
            if (clip == null) return;
            if (seq.segments.Count == 0) seq.segments.Add(clip);
            else seq.segments[0] = clip;
            _charSetupAnimDirty = true;
        }

        /// <summary>填充技能组主片段/收招段：spell01 → skills[0]（Skill_Q），spell04_end → skills[3].leadOut。</summary>
        void FillCharSkillFromScan(int sklIdx, bool isEnd, string fbxPath)
        {
            var seq = EnsureCharSequence(_charSetupEditAnimSet.skills, sklIdx, true);
            if (seq == null) return;
            var clip = LoadFirstClipFromFBX(fbxPath);
            if (clip == null) return;
            if (isEnd)
            {
                if (seq.leadOut == null) { seq.leadOut = clip; _charSetupAnimDirty = true; }
                return;
            }
            if (seq.GetMainSegment(0) != null) return;
            if (seq.segments.Count == 0) seq.segments.Add(clip);
            else seq.segments[0] = clip;
            _charSetupAnimDirty = true;
        }

        /// <summary>确保序列组列表有 idx 槽位（不够则按命名规范新建），返回该槽位序列。</summary>
        CharacterAnimSequence EnsureCharSequence(List<CharacterAnimSequence> list, int idx, bool isSkill)
        {
            while (list.Count <= idx)
            {
                int c = list.Count;
                string newId = isSkill
                    ? AnimStateNaming.SkillState(AnimStateNaming.SkillSuffix(c))
                    : AnimStateNaming.AttackState(c + 1);
                list.Add(new CharacterAnimSequence { id = newId, segments = new List<AnimationClip> { null } });
                _charSetupAnimDirty = true;
            }
            return list[idx];
        }

        /// <summary>解析攻击动画名下标：attack01 / attack_1 / attack1 → 0-based index。</summary>
        static bool TryParseCharAttackIndex(string animName, out int idx)
        {
            idx = -1;
            var lower = animName.ToLowerInvariant();
            if (!lower.StartsWith("attack")) return false;
            return TryParseCharTrailingNumber(lower.Substring("attack".Length), out idx);
        }

        /// <summary>解析技能动画名下标：spell01 / skill01 → 0-based index；并检测 _end 收招后缀。</summary>
        static bool TryParseCharSkillIndex(string animName, out int idx, out bool isEnd)
        {
            idx = -1; isEnd = false;
            var lower = animName.ToLowerInvariant();

            const string endSuffix = "_end";
            if (lower.EndsWith(endSuffix))
            {
                isEnd = true;
                lower = lower.Substring(0, lower.Length - endSuffix.Length);
            }

            string digits = null;
            if (lower.StartsWith("spell")) digits = lower.Substring("spell".Length);
            else if (lower.StartsWith("skill")) digits = lower.Substring("skill".Length);
            else return false;

            return TryParseCharTrailingNumber(digits, out idx);
        }

        /// <summary>解析尾部数字（"_01"→1，"1"→1），返回 0-based index，范围 0~7。</summary>
        static bool TryParseCharTrailingNumber(string s, out int idx)
        {
            idx = -1;
            s = s.TrimStart('_');
            if (string.IsNullOrEmpty(s)) return false;
            if (!int.TryParse(s, out int n) || n < 1 || n > 8) return false;
            idx = n - 1;
            return true;
        }

        /// <summary>装配 Character Prefab：挂载 Animator / Rigidbody / CapsuleCollider / CharacterMotor / 技能系统组件。</summary>
        string AssembleCharSetupPrefab()
        {
            CharSetupLog("\n  装配 Character Prefab...");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(_charSetupControllerPath);
            var avatar = EnsureHumanoidAvatar(_charSetupModelFBXPath, CharSetupLog);
            if (avatar == null) return null;

            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(_charSetupModelFBXPath);
            if (modelPrefab == null)
            {
                CharSetupLog("  无法加载模型 FBX！");
                return null;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            // Graves 是独立层级 Prefab；新角色也必须解包 FBX 嵌套实例，
            // 否则根节点 Layer/Tag/Animator/组件引用会停留在 stripped Prefab 修改记录中，
            // 运行时 GetComponent、网络同步和敌人 Layer 检测都可能取不到。
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = $"{_charSetupName}_Character";
            NormalizePlayerRootIdentity(root);
            NormalizeRagdollPhysics(root);

            // CharacterMotor / SkillController / NetworkAnimator 都挂在角色根节点，
            // 根 Animator 必须是唯一运行时动画入口；不能把 FBX 子节点 Animator 当作主 Animator，
            // 否则根 Animator 没有 Avatar/Controller，移动与技能动画会全部失效。
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var nestedAnimators = root.GetComponentsInChildren<Animator>(true);
            foreach (var nestedAnimator in nestedAnimators)
            {
                if (nestedAnimator != animator)
                    nestedAnimator.enabled = false;
            }

            var rb = root.GetComponent<Rigidbody>();
            if (rb == null) rb = root.AddComponent<Rigidbody>();
            rb.mass = 50f;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var col = root.GetComponent<CapsuleCollider>();
            if (col == null) col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.9f, 0);
            col.radius = 0.3f;
            col.height = 1.8f;

            var motor = root.GetComponent<CharacterMotor>();
            if (motor == null) motor = root.AddComponent<CharacterMotor>();

            var inputHandler = root.GetComponent<CharacterInputHandler>();
            if (inputHandler == null) inputHandler = root.AddComponent<CharacterInputHandler>();

            var skillController = root.GetComponent<Game.SkillSystem.SkillController>();
            if (skillController == null) skillController = root.AddComponent<Game.SkillSystem.SkillController>();
            skillController.combatModeTimeout = 3f;
            skillController.combatLayerFadeOutDuration = 0.5f;

            var animPlayer = root.GetComponent<Game.SkillSystem.SkillAnimPlayer>();
            if (animPlayer == null) animPlayer = root.AddComponent<Game.SkillSystem.SkillAnimPlayer>();
            animPlayer.fadeInDuration = 0.1f;
            animPlayer.fadeOutDuration = 0.2f;
            skillController.animPlayer = animPlayer;

            var hitDetector = root.GetComponent<Game.SkillSystem.HitDetector>();
            if (hitDetector == null) hitDetector = root.AddComponent<Game.SkillSystem.HitDetector>();
            hitDetector.autoSetupHitMask = true;
            hitDetector.debugDraw = false;

            var moveCtrl = root.GetComponent<Game.SkillSystem.SkillMovementController>();
            if (moveCtrl == null) moveCtrl = root.AddComponent<Game.SkillSystem.SkillMovementController>();
            skillController.movementController = moveCtrl;

            var weaponHolder = root.GetComponent<Game.SkillSystem.WeaponHolder>();
            if (weaponHolder == null) weaponHolder = root.AddComponent<Game.SkillSystem.WeaponHolder>();
            skillController.weaponHolder = weaponHolder;

            skillController.inputHandler = inputHandler;

            var actionHandler = root.GetComponent<CharacterActionHandler>();
            if (actionHandler == null) root.AddComponent<CharacterActionHandler>();

            var ladderAction = root.GetComponent<CharacterLadderAction>();
            if (ladderAction == null) ladderAction = root.AddComponent<CharacterLadderAction>();
            ladderAction.debugMode = false;
            ladderAction.enterInput = KeyCode.E;
            ladderAction.exitInput = KeyCode.S;

            // ── 2026-08-04 联机装配闭环：新主角 prefab 直接具备标准联机组件 ──
            ConfigureCharacterNetworkComponents(root);

            SetupCharSetupCamera(root);

            var invectorComponents = root.GetComponents<Component>()
                .Where(c => c != null && c.GetType().Name.StartsWith("v"))
                .ToArray();
            if (invectorComponents.Length > 0)
            {
                foreach (var comp in invectorComponents)
                    Object.DestroyImmediate(comp);
                CharSetupLog($"  移除了 {invectorComponents.Length} 个 Invector 组件");
            }

            string prefabPath = $"{_charSetupFolderPath}/{_charSetupName}_Character.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            if (!EnsureCharacterPrefabAvatar(prefabPath, _charSetupModelFBXPath))
            {
                CharSetupLog("  ❌ Prefab 保存后 Avatar 回写校验失败");
                return null;
            }

            // 供 Character Kit Network Setup 及 Net 维护入口解析新主角；不再默认硬编码 graves。
            EditorPrefs.SetString("Test09.CharacterKit.PlayerPrefabPath", prefabPath);
            CharSetupLog($"  玩家联机入口已记录: {prefabPath}");
            CharSetupLog($"  Prefab: {prefabPath}");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return prefabPath;
        }

        static Animator FindCharacterAnimator(GameObject root)
        {
            if (root == null) return null;
            var animator = root.GetComponent<Animator>();
            if (animator != null) return animator;
            return root.GetComponentInChildren<Animator>(true);
        }

        [MenuItem("Tools/Character Kit/Maintenance/Repair Current Player Avatar")]
        static void RepairCurrentPlayerAvatar()
        {
            string prefabPath = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "");
            var prefab = string.IsNullOrEmpty(prefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("[CharacterKit] 当前玩家入口无效，无法修复 Avatar。");
                return;
            }

            string modelPath = null;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (source != null)
                modelPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(modelPath) || !(AssetImporter.GetAtPath(modelPath) is ModelImporter))
            {
                string folder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
                string modelName = Path.GetFileNameWithoutExtension(prefabPath)
                    .Replace("_Character", string.Empty);
                modelPath = $"{folder}/model/{modelName}.FBX";
            }

            var panel = new CharacterKitPanel();
            bool repaired = panel.EnsureCharacterPrefabAvatar(prefabPath, modelPath);
            if (repaired)
                Debug.Log($"[CharacterKit] 当前玩家 Avatar 修复完成：{prefabPath}");
            else
                Debug.LogError($"[CharacterKit] 当前玩家 Avatar 修复失败：{modelPath}");
        }

        /// <summary>将模型 Avatar 显式写入生成后的 Character Prefab，并在保存后重新加载校验。</summary>
        bool EnsureCharacterPrefabAvatar(string prefabPath, string modelPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || string.IsNullOrEmpty(modelPath)) return false;
            var avatar = EnsureHumanoidAvatar(modelPath, CharSetupLog);
            if (avatar == null) return false;

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                CharSetupLog($"  ❌ 无法加载 Prefab 进行 Avatar 回写: {prefabPath}");
                return false;
            }

            try
            {
                var animator = FindCharacterAnimator(root);
                if (animator == null)
                {
                    CharSetupLog("  ❌ 生成 Prefab 层级缺少 Animator，无法写入 Avatar");
                    return false;
                }
                animator.avatar = avatar;
                if (animator.avatar != avatar)
                {
                    CharSetupLog($"  ❌ Animator.avatar 写入未生效：{animator.name}");
                    return false;
                }
                EditorUtility.SetDirty(animator);
                if (!PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saveSuccess) || !saveSuccess)
                {
                    CharSetupLog($"  ❌ Prefab 保存失败，Avatar 未提交：{prefabPath}");
                    return false;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var savedAnimator = FindCharacterAnimator(savedPrefab);
            bool valid = savedAnimator != null
                && savedAnimator.avatar != null
                && savedAnimator.avatar.isHuman
                && savedAnimator.avatar.isValid;
            CharSetupLog(valid
                ? $"  ✅ Avatar 已写入 Prefab: {savedAnimator.avatar.name}（持久化引用）"
                : $"  ❌ Prefab 重新加载后 Avatar 仍为空或无效：{prefabPath}");
            return valid;
        }

        /// <summary>新主角联机标准组件：NetworkObject/ClientNetworkTransform/NetworkAnimator/NetworkCharacterSetup/NetworkCastRelay。</summary>
        static void ConfigureCharacterNetworkComponents(GameObject root)
        {
            if (root.GetComponent<Unity.Netcode.NetworkObject>() == null)
                root.AddComponent<Unity.Netcode.NetworkObject>();
            if (root.GetComponent<Game.Net.ClientNetworkTransform>() == null)
                root.AddComponent<Game.Net.ClientNetworkTransform>();
            var networkAnimator = root.GetComponent<Unity.Netcode.Components.NetworkAnimator>();
            if (networkAnimator == null)
                networkAnimator = root.AddComponent<Unity.Netcode.Components.NetworkAnimator>();
            // headless AddComponent 不会序列化 m_Animator，装配时直接绑定，避免新 prefab 首次联机 NRE。
            var animatorSO = new SerializedObject(networkAnimator);
            var animatorProp = animatorSO.FindProperty("m_Animator");
            if (animatorProp != null) { animatorProp.objectReferenceValue = root.GetComponent<Animator>(); animatorSO.ApplyModifiedPropertiesWithoutUndo(); }
            if (root.GetComponent<Game.Net.NetworkCharacterSetup>() == null)
                root.AddComponent<Game.Net.NetworkCharacterSetup>();
            if (root.GetComponent<Game.Net.NetworkCastRelay>() == null)
                root.AddComponent<Game.Net.NetworkCastRelay>();
            if (root.GetComponent<CharacterStats>() == null)
                root.AddComponent<CharacterStats>();
        }

        /// <summary>解析当前 Character Prefab 的动画 Override 状态。</summary>
        bool IsCharacterOverrideSetupReady()
        {
            string path = ResolveCharacterPrefabPath();
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = prefab != null ? prefab.GetComponent<Animator>() : null;
            var ov = animator != null ? animator.runtimeAnimatorController as AnimatorOverrideController : null;
            return ov != null && OverrideControllerTool.ValidateOverride(ov, out _);
        }

        /// <summary>绘制主角动画 Override 步骤；独立菜单仅保留为全局修复入口。</summary>
        void DrawOverrideSetupPanel(SetupPipeline.StepState state)
        {
            EditorGUILayout.LabelField("③ 动画 Override 装配（必需）", StyleTitle14);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "主角必须使用 DefaultCharacterController 作为逻辑骨架，并通过 AnimatorOverrideController 映射当前角色动画。\n" +
                "本步骤自动扫描角色专属 animations 与 Assets/_Game/Animations 共享目录（专属优先、共享兜底），再同步 AnimSet；未通过时不能进入 Network Setup。",
                MessageType.Info);

            string path = ResolveCharacterPrefabPath();
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                EditorGUILayout.HelpBox("请先完成 Character Prefab Setup，生成主角 Prefab。", MessageType.Warning);
                return;
            }

            var animator = prefab.GetComponent<Animator>();
            var ov = animator != null ? animator.runtimeAnimatorController as AnimatorOverrideController : null;
            string report = ov == null ? "当前 Animator 未使用 AnimatorOverrideController" : "";
            bool ready = ov != null && OverrideControllerTool.ValidateOverride(ov, out report);
            DrawNetworkComponentStatus(prefab, "AnimatorOverrideController", ready);
            EditorGUILayout.LabelField($"目标 Prefab: {path}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"状态: {report}", ready ? GetMiniLabelColor(new Color(0.4f, 0.85f, 0.45f)) : GetMiniLabelColor(new Color(0.95f, 0.55f, 0.4f)));

            EditorGUILayout.Space(6);
            if (GUILayout.Button(ready ? "✔ 重新校验 Override" : "▶ 执行 Override 校验 / 修复", StyleBoldButton13, GUILayout.Height(34)))
                EditorApplication.delayCall += () => ExecuteCharacterOverrideSetup(state, path);
        }

        /// <summary>校验或把旧的复制版 Controller 迁移为当前角色 Override。</summary>
        void ExecuteCharacterOverrideSetup(SetupPipeline.StepState state, string prefabPath)
        {
            state.status = SetupPipeline.StepStatus.Running;
            state.progress = 0.1f;
            SetupPipeline.ShowStepProgress("动画 Override 装配", 0.1f, "读取角色 Prefab...");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var animator = prefab != null ? prefab.GetComponent<Animator>() : null;
                var existing = animator != null ? animator.runtimeAnimatorController as AnimatorOverrideController : null;
                if (existing != null)
                {
                    SyncOverrideFromCharacterController(existing, prefabPath);
                    SyncOverrideFromAnimationDirectories(existing, prefabPath);
                    SyncOverrideFromCharacterAnimSet(existing, prefabPath);
                    if (OverrideControllerTool.ValidateOverride(existing, out var existingReport))
                    {
                        state.status = SetupPipeline.StepStatus.Completed;
                        state.message = existingReport;
                        state.progress = 1f;
                        return;
                    }
                }

                var source = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
                var master = AssetDatabase.LoadAssetAtPath<AnimatorController>(OverrideControllerTool.MasterControllerPath);
                if (source == null || master == null)
                    throw new System.InvalidOperationException("当前角色没有可迁移的 AnimatorController，或 DefaultCharacterController 不存在");

                string folder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(prefabPath) + "_Override";
                string report;
                var ov = OverrideControllerTool.CreateOverrideFromExisting(master, source, folder, name, out report);
                if (ov == null)
                    throw new System.InvalidOperationException(report);
                _charSetupOverridePath = AssetDatabase.GetAssetPath(ov);
                SyncOverrideFromCharacterController(ov, prefabPath);
                SyncOverrideFromCharacterAnimSet(ov, prefabPath);

                SetupPipeline.ShowStepProgress("动画 Override 装配", 0.65f, "写入角色 Prefab...");
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var rootAnimator = root.GetComponent<Animator>();
                    if (rootAnimator == null) throw new System.InvalidOperationException("Prefab 根节点缺少 Animator");
                    rootAnimator.runtimeAnimatorController = ov;
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var finalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var finalOv = finalPrefab != null ? finalPrefab.GetComponent<Animator>()?.runtimeAnimatorController as AnimatorOverrideController : null;
                string finalReport = "";
                bool finalValid = finalOv != null && OverrideControllerTool.ValidateOverride(finalOv, out finalReport);
                if (!finalValid)
                    throw new System.InvalidOperationException("Override 写入后强校验失败");

                _charSetupPrefabPath = prefabPath;
                _playerTarget = finalPrefab;
                _playerMotor = finalPrefab != null ? finalPrefab.GetComponent<CharacterMotor>() : null;
                state.status = SetupPipeline.StepStatus.Completed;
                state.message = finalReport;
                state.progress = 1f;
                CharSetupLog($"  ✔ Override Setup 完成: {prefabPath}");
                _needsPipelineStateRefresh = true;
                RepaintCharacterKitWindow();
            }
            catch (System.Exception ex)
            {
                state.status = SetupPipeline.StepStatus.Failed;
                state.message = $"执行失败: {ex.Message}";
                CharSetupLog($"  ❌ Override Setup 失败: {ex.Message}");
            }
            finally
            {
                state.progress = 1f;
                SetupPipeline.ClearStepProgress();
            }
        }

        /// <summary>获取 Character Setup 生成的玩家 Prefab 路径，兼容窗口重载后的 EditorPrefs 状态。</summary>
        string ResolveCharacterNetworkPrefabPath()
        {
            return ResolveCharacterPrefabPath();
        }

        /// <summary>校验主角联机组件、NetworkAnimator 引用、当前玩家入口和 Resources 入口。</summary>
        bool IsCharacterNetworkSetupReady()
        {
            string path = ResolveCharacterNetworkPrefabPath();
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return false;

            var networkAnimator = prefab.GetComponent<Unity.Netcode.Components.NetworkAnimator>();
            var animator = prefab.GetComponent<Animator>();
            bool animatorBound = false;
            if (networkAnimator != null)
            {
                var so = new SerializedObject(networkAnimator);
                var prop = so.FindProperty("m_Animator");
                animatorBound = prop != null && prop.objectReferenceValue == animator;
            }

            bool componentsReady = prefab.GetComponent<Unity.Netcode.NetworkObject>() != null
                && prefab.GetComponent<Game.Net.ClientNetworkTransform>() != null
                && networkAnimator != null
                && animatorBound
                && prefab.GetComponent<Game.Net.NetworkCharacterSetup>() != null
                && prefab.GetComponent<Game.Net.NetworkCastRelay>() != null
                && prefab.GetComponent<CharacterStats>() != null;
            bool playerEntryReady = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "") == path;
            var resourcesPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Resources/NetworkPlayer.prefab");
            bool resourcesReady = resourcesPrefab != null
                && resourcesPrefab.GetComponent<Unity.Netcode.NetworkObject>() != null;
            return componentsReady && playerEntryReady && resourcesReady;
        }

        /// <summary>绘制 Character 专用 Network Setup 步骤：联机装配是主角 Setup 的必需步骤。</summary>
        void DrawNetworkSetupPanel(SetupPipeline.StepState state)
        {
            EditorGUILayout.LabelField("④ 主角联机装配（必需）", StyleTitle14);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "该步骤将校验/修复 NetworkObject、ClientNetworkTransform、NetworkAnimator、NetworkCharacterSetup、NetworkCastRelay、CharacterStats，\n" +
                "并把当前角色写入玩家入口，生成 Resources/NetworkPlayer.prefab。未完成时角色只能作为单机 Prefab 使用。",
                MessageType.Info);

            string path = ResolveCharacterNetworkPrefabPath();
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                EditorGUILayout.HelpBox("请先完成 Character Prefab Setup，生成主角 Prefab。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField($"目标 Prefab: {path}", EditorStyles.miniLabel);
            DrawNetworkComponentStatus(prefab, "NetworkObject", prefab.GetComponent<Unity.Netcode.NetworkObject>() != null);
            DrawNetworkComponentStatus(prefab, "Game.Net.ClientNetworkTransform", prefab.GetComponent<Game.Net.ClientNetworkTransform>() != null);
            var networkAnimator = prefab.GetComponent<Unity.Netcode.Components.NetworkAnimator>();
            bool animatorBound = IsNetworkAnimatorBound(prefab, networkAnimator);
            DrawNetworkComponentStatus(prefab, "NetworkAnimator + Animator 引用", networkAnimator != null && animatorBound);
            DrawNetworkComponentStatus(prefab, "NetworkCharacterSetup", prefab.GetComponent<Game.Net.NetworkCharacterSetup>() != null);
            DrawNetworkComponentStatus(prefab, "NetworkCastRelay", prefab.GetComponent<Game.Net.NetworkCastRelay>() != null);
            DrawNetworkComponentStatus(prefab, "CharacterStats", prefab.GetComponent<CharacterStats>() != null);

            string configuredPath = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "");
            DrawNetworkComponentStatus(prefab, "当前玩家入口", configuredPath == path);
            bool resourcesReady = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Resources/NetworkPlayer.prefab") != null;
            DrawNetworkComponentStatus(prefab, "Resources/NetworkPlayer.prefab", resourcesReady);

            EditorGUILayout.Space(6);
            if (GUILayout.Button(IsCharacterNetworkSetupReady() ? "✔ 重新校验并更新联机入口" : "▶ 执行联机装配 / 修复", StyleBoldButton13, GUILayout.Height(34)))
            {
                EditorApplication.delayCall += () => ExecuteCharacterNetworkSetup(state);
            }
        }

        static void DrawNetworkComponentStatus(GameObject prefab, string label, bool ready)
        {
            var oldColor = GUI.contentColor;
            GUI.contentColor = ready ? new Color(0.4f, 0.85f, 0.45f) : new Color(0.95f, 0.55f, 0.4f);
            EditorGUILayout.LabelField(ready ? $"  ✔ {label}" : $"  ✘ {label}", EditorStyles.miniLabel);
            GUI.contentColor = oldColor;
        }

        static bool IsNetworkAnimatorBound(GameObject prefab, Unity.Netcode.Components.NetworkAnimator networkAnimator)
        {
            if (networkAnimator == null) return false;
            var so = new SerializedObject(networkAnimator);
            var prop = so.FindProperty("m_Animator");
            return prop != null && prop.objectReferenceValue == prefab.GetComponent<Animator>();
        }

        /// <summary>执行 Network Setup：补齐组件、绑定 Animator、登记玩家入口并生成 Resources 联机入口。</summary>
        void ExecuteCharacterNetworkSetup(SetupPipeline.StepState state)
        {
            state.status = SetupPipeline.StepStatus.Running;
            state.progress = 0.1f;
            SetupPipeline.ShowStepProgress("主角联机装配", 0.1f, "加载角色 Prefab...");
            string path = ResolveCharacterNetworkPrefabPath();
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                state.status = SetupPipeline.StepStatus.Failed;
                state.message = "未找到 Character Prefab";
                state.progress = 1f;
                SetupPipeline.ClearStepProgress();
                return;
            }

            GameObject root = null;
            try
            {
                if (!string.IsNullOrEmpty(_charSetupModelFBXPath)
                    && !EnsureCharacterPrefabAvatar(path, _charSetupModelFBXPath))
                    throw new System.InvalidOperationException("生成后的 Character Prefab 缺少有效 Avatar");

                root = PrefabUtility.LoadPrefabContents(path);
                ConfigureCharacterNetworkComponents(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                EditorPrefs.SetString("Test09.CharacterKit.PlayerPrefabPath", path);
                state.progress = 0.55f;
                SetupPipeline.ShowStepProgress("主角联机装配", 0.55f, "生成 Resources/NetworkPlayer.prefab...");

                Game.EditorTools.NetworkPrefabFixer.BuildPlayerResourcesEntry();
                // 不 Refresh：NetworkPrefabFixer 已保存资源，Refresh 会触发 SkillBuilderWizard.OnDisable，
                // 旧 WorkingCopy 可能覆盖刚生成的 SkillData。
                AssetDatabase.SaveAssets();

                if (!IsCharacterNetworkSetupReady())
                    throw new System.InvalidOperationException("联机组件或 Resources/NetworkPlayer.prefab 校验未通过");

                state.status = SetupPipeline.StepStatus.Completed;
                state.message = "联机组件与玩家入口已完成";
                state.progress = 1f;
                CharSetupLog($"  ✔ Network Setup 完成: {path}");
                _needsPipelineStateRefresh = true;
                RepaintCharacterKitWindow();
            }
            catch (System.Exception ex)
            {
                state.status = SetupPipeline.StepStatus.Failed;
                state.message = $"执行失败: {ex.Message}";
                CharSetupLog($"  ❌ Network Setup 失败: {ex.Message}");
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
                state.progress = 1f;
                SetupPipeline.ClearStepProgress();
            }
        }

        /// <summary>设置 TopdownCameraController 并绑定到角色根。</summary>
        void SetupCharSetupCamera(GameObject characterRoot)
        {
            var mainCamGO = GameObject.Find("MainCamera");
            if (mainCamGO == null)
            {
                var camComp = Camera.main;
                if (camComp != null) mainCamGO = camComp.gameObject;
            }

            if (mainCamGO == null)
            {
                CharSetupLog("  未找到 MainCamera，跳过摄像机设置");
                return;
            }

            var camCtrl = mainCamGO.GetComponent<Game.Character.TopdownCameraController>();
            if (camCtrl == null)
                camCtrl = mainCamGO.AddComponent<Game.Character.TopdownCameraController>();

            camCtrl.SetTarget(characterRoot.transform);
            CharSetupLog($"  摄像机已绑定到 {characterRoot.name}");
        }
    }
}
#endif
