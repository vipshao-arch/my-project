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
        static readonly GUIContent GCLRag_MassScale        = new GUIContent("质量倍率", "全局骨骼质量倍率，1.0=标准，重型角色适当调高");
        static readonly GUIContent GCLRag_LinearDrag       = new GUIContent("线性阻力", "Rigidbody.drag，骨骼运动阻力");
        static readonly GUIContent GCLRag_AngularDrag      = new GUIContent("角阻力", "Rigidbody.angularDrag，骨骼旋转阻力");
        static readonly GUIContent GCLRag_Trigger          = new GUIContent("Collider 设为 Trigger", "勾选=Collider.isTrigger=true，运行时由 vRagdoll 接管碰撞");
        static readonly GUIContent GCLRag_DetectCollisions = new GUIContent("启用碰撞检测", "Rigidbody.detectCollisions（默认关闭以提升性能）");

        /// <summary>绘制 Ragdoll Setup 独立面板：目标选择 + 参数编辑 + 执行/移除布娃娃系统。</summary>
        void DrawRagdollSetupPanel(SetupPipeline.StepState state)
        {
            EditorGUILayout.LabelField("⑤ Ragdoll 布娃娃设置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "为角色 Prefab 自动生成 Ragdoll（布娃娃）物理骨骼系统。\n" +
                "可独立使用：选择任意 Humanoid 角色 GameObject → 调整参数 → 执行。\n\n" +
                "默认参数为标准人形体型自动计算，可在「高级参数」中微调。",
                MessageType.Info);
            EditorGUILayout.Space(6);

            // ── 目标选择 ────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField("目标角色", GUILayout.Width(70));
                var newTarget = (GameObject)EditorGUILayout.ObjectField(
                    _ragdollTarget, typeof(GameObject), true);
                if (newTarget != _ragdollTarget)
                {
                    _ragdollTarget = newTarget;
                    OnSharedTargetManuallyChanged(newTarget, ref _ragdollTargetAuto);
                }
                if (GUILayout.Button("→", GUILayout.Width(28)))
                {
                    _ragdollTarget = Selection.activeGameObject;
                    OnSharedTargetManuallyChanged(Selection.activeGameObject, ref _ragdollTargetAuto);
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            // ── CK-02：自动来源指示器 ──
            if (_ragdollTarget != null && _ragdollTargetAuto)
            {
                EditorGUILayout.LabelField("  ⟳ 自动绑定（来自流水线前置步骤）", EditorStyles.miniLabel);
            }

            // ── 目标状态检查 ────────────────────────────────────────
            if (_ragdollTarget != null)
            {
                string targetValidation = Game.Character.Editor.RagdollAutoSetup.ValidateTarget(_ragdollTarget);
                if (!string.IsNullOrEmpty(targetValidation))
                {
                    EditorGUILayout.HelpBox("✗ " + targetValidation, MessageType.Error);
                }
                else
                {
                    EditorGUILayout.HelpBox("✓ 目标有效：Animator (Humanoid) - " + _ragdollTarget.name, MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.LabelField("  └─ 拖入 Hierarchy / Project 中的角色 GameObject", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6);

            // ── 高级参数（独立执行时可调）─────────────────────────────
            _ragdollShowAdvancedParams = EditorGUILayout.BeginFoldoutHeaderGroup(
                _ragdollShowAdvancedParams, "⚙  高级参数（可选调节）");
            try
            {
                if (_ragdollShowAdvancedParams)
                {
                    EditorGUI.indentLevel++;
                    _ragdollGlobalMassScale = EditorGUILayout.Slider(
                        GCLRag_MassScale,
                        _ragdollGlobalMassScale, 0.1f, 5.0f);
                    _ragdollGlobalDrag = EditorGUILayout.Slider(
                        GCLRag_LinearDrag,
                        _ragdollGlobalDrag, 0f, 5f);
                    _ragdollGlobalAngularDrag = EditorGUILayout.Slider(
                        GCLRag_AngularDrag,
                        _ragdollGlobalAngularDrag, 0f, 5f);
                    _ragdollCollidersAsTrigger = EditorGUILayout.Toggle(
                        GCLRag_Trigger,
                        _ragdollCollidersAsTrigger);
                    _ragdollDetectCollisions = EditorGUILayout.Toggle(
                        GCLRag_DetectCollisions,
                        _ragdollDetectCollisions);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.HelpBox(
                        "修改后点击「确认执行」生效。参数仅作用于本次生成，已存在的 Ragdoll 组件不会自动更新。",
                        MessageType.None);
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndFoldoutHeaderGroup());
            }

            EditorGUILayout.Space(6);

            // ── 执行按钮 ────────────────────────────────────────────
            GUILayout.FlexibleSpace();
            var btnStyle = StyleBoldButton13;

            bool ready = _ragdollTarget != null
                && string.IsNullOrEmpty(Game.Character.Editor.RagdollAutoSetup.ValidateTarget(_ragdollTarget));
            GUI.enabled = ready;
            if (GUILayout.Button("✔ 确认执行 — Ragdoll Auto Setup", btnStyle, GUILayout.Height(34)))
            {
                ExecuteRagdollSetup(state);
            }
            GUI.enabled = true;

            // ── 移除按钮（危险操作） ────────────────────────────────
            EditorGUILayout.Space(4);
            GUI.enabled = _ragdollTarget != null;
            if (GUILayout.Button("🗑 移除 Ragdoll 组件", StyleRemoveButton, GUILayout.Height(22)))
            {
                if (_ragdollTarget != null)
                {
                    var rb = _ragdollTarget.GetComponent<Rigidbody>();
                    var col = _ragdollTarget.GetComponent<Collider>();
                    int joints = _ragdollTarget.GetComponentsInChildren<CharacterJoint>(true).Length;
                    int rbs = 0; foreach (var x in _ragdollTarget.GetComponentsInChildren<Rigidbody>(true)) if (x != rb) rbs++;
                    int cols = 0; foreach (var x in _ragdollTarget.GetComponentsInChildren<Collider>(true)) if (x != col) cols++;
                    if (EditorUtility.DisplayDialog("确认移除 Ragdoll",
                        $"将移除 {joints} Joint + {rbs} Rigidbody + {cols} Collider\n" +
                        "（保留角色根的 Rigidbody + CapsuleCollider）\n\n继续？",
                        "移除", "取消"))
                    {
                        RemoveRagdoll(_ragdollTarget);
                        _ragdollLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] 已移除 Ragdoll: {joints} Joint + {rbs} Rigidbody + {cols} Collider");
                    }
                }
            }
            GUI.enabled = true;

            // ── 执行日志 ────────────────────────────────────────────
            if (_ragdollLogMessages.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("执行日志", EditorStyles.boldLabel);
                float h = Mathf.Min(_ragdollLogMessages.Count * 20 + 20, 120);
                _ragdollLogScroll = EditorGUILayout.BeginScrollView(_ragdollLogScroll, GUILayout.Height(h));
                try
                {
                    foreach (var msg in _ragdollLogMessages)
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
        }

        /// <summary>执行 Ragdoll 自动装配，将目标设为 Selection 后调用 RagdollAutoSetup.Execute()。</summary>
        void ExecuteRagdollSetup(SetupPipeline.StepState state)
        {
            _ragdollLogMessages.Clear();
            try
            {
                state.status = SetupPipeline.StepStatus.Running;
                state.progress = 0.1f;
                SetupPipeline.ShowStepProgress("Ragdoll 布娃娃设置", 0.1f, "准备装配骨骼...");

                if (_ragdollTarget == null)
                    throw new System.InvalidOperationException("Ragdoll 目标为空，请重新选择当前角色 Prefab");

                // Project 中的 Prefab 资产不能直接作为层级对象修改；必须进入 Prefab Contents。
                string targetPath = AssetDatabase.GetAssetPath(_ragdollTarget);
                bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(_ragdollTarget);
                GameObject workingTarget = _ragdollTarget;
                bool loadedPrefabContents = false;
                try
                {
                    if (isPrefabAsset)
                    {
                        if (string.IsNullOrEmpty(targetPath))
                            throw new System.InvalidOperationException($"无法解析 Prefab 路径：{_ragdollTarget.name}");
                        workingTarget = PrefabUtility.LoadPrefabContents(targetPath);
                        loadedPrefabContents = workingTarget != null;
                        if (!loadedPrefabContents)
                            throw new System.InvalidOperationException($"无法加载 Prefab 内容：{targetPath}");

                        UnpackNestedModelPrefabForRagdoll(workingTarget);
                    }

                    SetupPipeline.ShowStepProgress("Ragdoll 布娃娃设置", 0.3f, "生成 Collider + Rigidbody + Joint...");
                    int configured = Game.Character.Editor.RagdollAutoSetup.Execute(workingTarget);

                    SetupPipeline.ShowStepProgress("Ragdoll 布娃娃设置", 0.7f, "应用自定义参数...");
                    ApplyRagdollCustomParams(workingTarget);

                    if (loadedPrefabContents)
                    {
                        PrefabUtility.SaveAsPrefabAsset(workingTarget, targetPath);
                        AssetDatabase.SaveAssets();
                    }

                    state.status = SetupPipeline.StepStatus.Completed;
                    state.message = $"Ragdoll 设置完成（{configured} 个骨骼）";
                    _ragdollLastSuccess = true;
                    _ragdollLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] Ragdoll 设置完成: {_ragdollTarget.name}（{configured} 个骨骼）");
                }
                finally
                {
                    if (loadedPrefabContents && workingTarget != null)
                        PrefabUtility.UnloadPrefabContents(workingTarget);
                }
                _ragdollLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}]    质量x{_ragdollGlobalMassScale:F2} drag={_ragdollGlobalDrag:F2} angularDrag={_ragdollGlobalAngularDrag:F2} trigger={_ragdollCollidersAsTrigger} detectCollisions={_ragdollDetectCollisions}");
            }
            catch (System.Exception ex)
            {
                state.status = SetupPipeline.StepStatus.Failed;
                string detail = ex.GetBaseException().Message;
                state.message = $"执行失败: {detail}";
                _ragdollLastSuccess = false;
                _ragdollLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ❌ 执行失败: {ex.GetType().Name}: {detail}");
                Debug.LogException(ex, _ragdollTarget);
            }
            state.progress = 1f;
            SetupPipeline.ClearStepProgress();
            RepaintCharacterKitWindow();
        }

        /// <summary>解包模型生成的嵌套 Prefab 实例，使 Humanoid 骨骼可以挂载 Rigidbody/Collider/Joint。</summary>
        static void UnpackNestedModelPrefabForRagdoll(GameObject root)
        {
            if (root == null) return;
            var animator = root.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return;

            var bones = new[]
            {
                animator.GetBoneTransform(HumanBodyBones.Hips),
                animator.GetBoneTransform(HumanBodyBones.Spine),
                animator.GetBoneTransform(HumanBodyBones.Head)
            };
            foreach (var bone in bones)
            {
                if (bone == null) continue;
                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(bone.gameObject);
                if (instanceRoot != null && instanceRoot != root && PrefabUtility.IsPartOfPrefabInstance(instanceRoot))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        instanceRoot,
                        PrefabUnpackMode.OutermostRoot,
                        InteractionMode.AutomatedAction);
                    return;
                }
            }
        }

        /// <summary>将用户在高级参数中配置的全局参数应用到 Ragdoll 已生成的 Rigidbody / Collider 组件上。</summary>
        void ApplyRagdollCustomParams(GameObject go)
        {
            if (go == null) return;
            var rootRb = go.GetComponent<Rigidbody>();
            Undo.RegisterFullObjectHierarchyUndo(go, "Apply Ragdoll Custom Params");

            // 应用到所有 Ragdoll 骨骼 Rigidbody
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == rootRb) continue; // 跳过角色根 Rigidbody
                rb.drag = _ragdollGlobalDrag;
                rb.angularDrag = _ragdollGlobalAngularDrag;
                rb.detectCollisions = _ragdollDetectCollisions;
                // 质量倍率生效（保留相对比例）
                if (rb.mass > 0f && !Mathf.Approximately(_ragdollGlobalMassScale, 1.0f))
                    rb.mass *= _ragdollGlobalMassScale;
            }

            // 应用 Trigger 设置到所有 Ragdoll 骨骼 Collider
            if (_ragdollCollidersAsTrigger)
            {
                var rootCol = go.GetComponent<Collider>();
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                {
                    if (col == rootCol) continue;
                    col.isTrigger = true;
                }
            }
        }

        /// <summary>移除角色上的所有 Ragdoll 物理组件（保留根 Rigidbody 和 CapsuleCollider）。</summary>
        static void RemoveRagdoll(GameObject go)
        {
            var rootRb = go.GetComponent<Rigidbody>();
            var rootCol = go.GetComponent<Collider>();
            Undo.RegisterFullObjectHierarchyUndo(go, "Remove Ragdoll");
            foreach (var j in go.GetComponentsInChildren<CharacterJoint>(true))
            {
                try { Object.DestroyImmediate(j); } catch { }
            }
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == rootRb) continue;
                try { Object.DestroyImmediate(rb); } catch { }
            }
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
            {
                if (col == rootCol) continue;
                try { Object.DestroyImmediate(col); } catch { }
            }
            EditorUtility.SetDirty(go);
        }
    }
}
#endif