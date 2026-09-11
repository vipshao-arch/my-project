#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Game.Character;
using Game.EditorTools.Shared;
using Game.SkillSystem;
using System.Collections.Generic;
using System.Linq;

namespace Game.Character.EditorTools.CharacterKit
{
    public partial class CharacterKitPanel
    {
        /// <summary>当前诊断发现的可修复问题列表（供 UI 渲染一键修复按钮）。</summary>
        private List<DiagnosticIssue> _diagIssues;

        /// <summary>绘制 Diagnostic 独立面板：Character 完整性诊断 或 Enemy AI 诊断窗口。</summary>
        void DrawDiagnosticPanel(SetupPipeline.StepState state)
        {
            bool isEnemy = _setupTarget == SetupPipeline.PipelineTarget.Enemy;
            EditorGUILayout.LabelField(isEnemy ? "⑥ Enemy AI 诊断" : "⑥ 完整性诊断", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // ── 目标选择 ────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField("目标角色", GUILayout.Width(70));
                var newTarget = (GameObject)EditorGUILayout.ObjectField(
                    _diagTarget, typeof(GameObject), true);
                if (newTarget != _diagTarget)
                {
                    _diagTarget = newTarget;
                    OnSharedTargetManuallyChanged(newTarget, ref _diagTargetAuto);
                }
                if (GUILayout.Button("→", GUILayout.Width(28)))
                {
                    _diagTarget = Selection.activeGameObject;
                    OnSharedTargetManuallyChanged(Selection.activeGameObject, ref _diagTargetAuto);
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            // ── CK-02：自动来源指示器 ──
            if (_diagTarget != null && _diagTargetAuto)
            {
                EditorGUILayout.LabelField("  ⟳ 自动绑定（来自流水线前置步骤）", EditorStyles.miniLabel);
            }

            if (_diagTarget == null)
            {
                EditorGUILayout.LabelField("  └─ 拖入 Hierarchy 中的角色 GameObject", EditorStyles.miniLabel);
            }

            // ── 诊断结果（紧贴目标角色行下方，FlexSpace 占满剩余空间）──
            if (_diagLogMessages.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(
                    _diagLastSuccess ? "诊断结果  ✓" : "诊断结果  ⚠",
                    EditorStyles.boldLabel);
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginVertical(StyleDiagResultBox);
                try
                {
                    _diagLogScroll = EditorGUILayout.BeginScrollView(_diagLogScroll,
                        GUILayout.ExpandHeight(true));
                    try
                    {
                        var normalLabel = EditorStyles.miniLabel;

                        bool inFixSection = false;
                        foreach (var msg in _diagLogMessages)
                        {
                            if (msg == "============ 修复方案 ============")
                            {
                                EditorGUILayout.LabelField(msg, StyleDiagFixHeader);
                                inFixSection = true;
                            }
                            else if (msg.StartsWith("❌") || msg.StartsWith("ERROR"))
                            {
                                EditorGUILayout.LabelField(msg, StyleDiagErrorLabel);
                            }
                            else if (msg.StartsWith("⚠") || msg.StartsWith("WARN"))
                            {
                                EditorGUILayout.LabelField(msg, StyleDiagErrorLabel);
                            }
                            else if (inFixSection && msg.StartsWith("【"))
                            {
                                EditorGUILayout.LabelField(msg, StyleDiagFixHeader);
                            }
                            else if (inFixSection)
                            {
                                EditorGUILayout.LabelField(msg, StyleDiagFixItem);
                            }
                            else
                            {
                                EditorGUILayout.LabelField(msg, normalLabel);
                            }
                        }
                    }
                    finally
                    {
                        SafeEndLayout(() => EditorGUILayout.EndScrollView());
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndVertical());
                }
            }

            // ── 一键修复按钮 (CK-06) ────────────────────────────────
            if (_diagIssues != null && _diagIssues.Count > 0)
            {
                EditorGUILayout.Space(4);

                int autoFixable = _diagIssues.Count(i => i.CanAutoFix);
                if (autoFixable > 0)
                {
                    for (int i = 0; i < _diagIssues.Count; i++)
                    {
                        var di = _diagIssues[i];
                        if (!di.CanAutoFix) continue;

                        EditorGUILayout.BeginHorizontal();
                        try
                        {
                            EditorGUILayout.LabelField($"  {di.description}",
                                EditorStyles.miniLabel, GUILayout.Width(260));

                            var prevBg = GUI.backgroundColor;
                            GUI.backgroundColor = EditorSkinPalette.Success;
                            if (GUILayout.Button($"🔧 一键修复", GUILayout.Width(90), GUILayout.Height(20)))
                            {
                                if (di.ExecuteFix())
                                {
                                    Debug.Log($"[CharacterKit] ✅ 已修复: {di.description}");
                                    _diagIssues.RemoveAt(i);
                                    _needsPipelineStateRefresh = true;
                                }
                                else
                                {
                                    Debug.LogWarning($"[CharacterKit] ⚠ 修复失败: {di.description}");
                                }
                                break;
                            }
                            GUI.backgroundColor = prevBg;
                        }
                        finally
                        {
                            SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                        }
                    }

                    if (autoFixable > 1)
                    {
                        EditorGUILayout.Space(2);
                        GUI.backgroundColor = EditorSkinPalette.Highlight;
                        if (GUILayout.Button($"🔧 一键修复全部 ({autoFixable} 项)", GUILayout.Height(24)))
                        {
                            int fixedCount = 0;
                            var remaining = new List<DiagnosticIssue>();
                            foreach (var issue in _diagIssues)
                            {
                                if (issue.CanAutoFix && issue.ExecuteFix())
                                    fixedCount++;
                                else
                                    remaining.Add(issue);
                            }
                            _diagIssues = remaining;
                            _needsPipelineStateRefresh = true;
                            Debug.Log($"[CharacterKit] ✅ 一键修复完成: {fixedCount} 项已修复, {remaining.Count} 项需手动处理");
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }

                int manualCount = _diagIssues.Count(i => !i.CanAutoFix);
                if (manualCount > 0)
                {
                    EditorGUILayout.Space(2);
                    GUI.color = EditorSkinPalette.Warning;
                    EditorGUILayout.LabelField(
                        $"  ✋ {manualCount} 项需手动操作（如拖入资产引用）",
                        EditorStyles.miniLabel);
                    GUI.color = Color.white;
                }
            }

            // ── 执行按钮 ────────────────────────────────────────────
            EditorGUILayout.Space(6);
            var btnStyle = StyleDiagBoldButton;

            string btnText = isEnemy ? "✔ 打开 Enemy AI 诊断窗口" : "✔ 运行完整性诊断";
            if (GUILayout.Button(btnText, btnStyle, GUILayout.Height(34)))
            {
                ExecuteDiagnostic(state, isEnemy);
            }
        }

        /// <summary>执行诊断：Enemy 模式打开独立诊断窗口，Character 模式运行内联诊断。</summary>
        void ExecuteDiagnostic(SetupPipeline.StepState state, bool isEnemy)
        {
            _diagLogMessages.Clear();
            try
            {
                if (isEnemy)
                {
                    SetupPipeline.ShowStepProgress("Enemy AI 诊断", 0.5f, "打开诊断窗口...");
                    var win = EditorWindow.GetWindow<Game.Character.Editor.EnemyAIDiagnostic>(
                        "Enemy AI 诊断", true, typeof(CharacterKitWindow));
                    win.Show();
                    state.status = SetupPipeline.StepStatus.Completed;
                    state.message = "Enemy AI 诊断窗口已打开";
                    _diagLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] Enemy AI 诊断窗口已打开");
                }
                else
                {
                    SetupPipeline.ShowStepProgress("角色诊断", 0.3f,
                        $"检查 {(_diagTarget != null ? _diagTarget.name : "当前场景")}...");
                    RunCharacterDiagnosticInline();
                    state.status = SetupPipeline.StepStatus.Completed;
                    state.message = "诊断完成";
                }
                _diagLastSuccess = true;
            }
            catch (System.Exception ex)
            {
                state.status = SetupPipeline.StepStatus.Failed;
                state.message = $"执行失败: {ex.Message}";
                _diagLastSuccess = false;
                _diagLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ❌ 执行失败: {ex.Message}");
            }
            state.progress = 1f;
            SetupPipeline.ClearStepProgress();
            RepaintCharacterKitWindow();
        }

        /// <summary>运行内联角色诊断：读取 CharacterMotor / Animator / InputHandler / SkillController 状态并输出，缺失项附带修复建议。</summary>
        void RunCharacterDiagnosticInline()
        {
            _diagLastSuccess = true;
            var issues = new List<DiagnosticIssue>();
            var motor = _diagTarget != null
                ? _diagTarget.GetComponentInChildren<CharacterMotor>(true)
                : Object.FindObjectOfType<CharacterMotor>();
            if (motor == null)
            {
                _diagLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ❌ 未找到 CharacterMotor");
                issues.Add(new DiagnosticIssue
                {
                    description = "缺少 CharacterMotor 组件",
                    fixHint = "在角色 GameObject 上挂载 CharacterMotor 组件",
                    fixType = DiagnosticFixType.ManualAsset,
                });
                _diagLastSuccess = false;
                return;
            }

            _diagLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] 诊断对象: {motor.name}");

            // ── CharacterMotor ──────────────────────────────────────
            _diagLogMessages.Add("=== CharacterMotor ===");
            _diagLogMessages.Add($"  name: {motor.name}");
            _diagLogMessages.Add($"  input: {motor.input}");
            _diagLogMessages.Add($"  isGrounded: {motor.isGrounded}");
            _diagLogMessages.Add($"  isStrafing: {motor.isStrafing}");
            _diagLogMessages.Add($"  lockRotation: {motor.lockRotation}");
            _diagLogMessages.Add($"  rotateByWorld: {motor.rotateByWorld}");
            _diagLogMessages.Add($"  isInAction: {motor.isInAction}");
            _diagLogMessages.Add($"  isKnockedBack: {motor.isKnockedBack}");
            _diagLogMessages.Add($"  inLadderZone: {motor.inLadderZone}");
            _diagLogMessages.Add($"  speedMultiplier: {motor.speedMultiplier}");
            _diagLogMessages.Add($"  CurrentVelocity: {motor.CurrentVelocity:F3}");

            // ── Rigidbody ──────────────────────────────────────────
            _diagLogMessages.Add("=== Rigidbody ===");
            var rb = motor.GetComponent<Rigidbody>();
            if (rb != null)
            {
                _diagLogMessages.Add($"  velocity: {rb.velocity}");
                _diagLogMessages.Add($"  isKinematic: {rb.isKinematic}");
                _diagLogMessages.Add($"  useGravity: {rb.useGravity}");
                _diagLogMessages.Add($"  mass: {rb.mass}");
            }
            else
            {
                _diagLogMessages.Add("  ❌ 缺少 Rigidbody 组件");
                issues.Add(new DiagnosticIssue
                {
                    description = "缺少 Rigidbody 组件",
                    fixHint = "自动添加 Rigidbody (isKinematic=true, Continuous Speculative)",
                    fixType = DiagnosticFixType.AddComponent,
                    target = motor.gameObject,
                    componentType = typeof(Rigidbody),
                });
                _diagLastSuccess = false;
            }

            // ── Animator ───────────────────────────────────────────
            _diagLogMessages.Add("=== Animator ===");
            var animator = motor.GetComponent<Animator>();
            if (animator != null)
            {
                _diagLogMessages.Add($"  applyRootMotion: {animator.applyRootMotion}");
                _diagLogMessages.Add($"  updateMode: {animator.updateMode}");
                _diagLogMessages.Add($"  avatar: {(animator.avatar != null ? animator.avatar.name : "null")}");
                _diagLogMessages.Add($"  runtimeAnimatorController: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "null")}");
                if (animator.runtimeAnimatorController == null)
                {
                    _diagLogMessages.Add("  ❌ 未绑定 AnimatorController");
                    issues.Add(DiagnosticIssue.ManualAsset(motor.gameObject,
                        "Animator 未绑定 AnimatorController",
                        "将 Part 1 生成的 Controller 拖入 Animator.Controller 字段"));
                    _diagLastSuccess = false;
                }
                if (animator.avatar == null)
                {
                    _diagLogMessages.Add("  ❌ 未绑定 Avatar");
                    issues.Add(DiagnosticIssue.ManualAsset(motor.gameObject,
                        "Animator 未绑定 Avatar",
                        "在模型 Import Settings > Rig 设置 Animation Type = Humanoid 并 Apply\n" +
                        "  → 然后在 Animator 的 Avatar 字段拖入生成的 Avatar"));
                    _diagLastSuccess = false;
                }
                if (Application.isPlaying)
                {
                    _diagLogMessages.Add($"  InputHorizontal: {animator.GetFloat("InputHorizontal")}");
                    _diagLogMessages.Add($"  InputVertical: {animator.GetFloat("InputVertical")}");
                    _diagLogMessages.Add($"  InputMagnitude: {animator.GetFloat("InputMagnitude")}");
                    _diagLogMessages.Add($"  IsGrounded: {animator.GetBool("IsGrounded")}");
                    _diagLogMessages.Add($"  Speed: {animator.GetFloat("Speed")}");
                    _diagLogMessages.Add($"  Direction: {animator.GetFloat("Direction")}");
                }
                else
                {
                    _diagLogMessages.Add("  (Animator 参数仅在 Play 模式可读)");
                }
                for (int i = 0; i < animator.layerCount; i++)
                {
                    _diagLogMessages.Add($"  Layer[{i}] '{animator.GetLayerName(i)}' weight: {animator.GetLayerWeight(i):F3}");
                }
            }
            else
            {
                _diagLogMessages.Add("  ❌ 缺少 Animator 组件");
                issues.Add(DiagnosticIssue.MissingComponent(motor.gameObject, typeof(Animator),
                    "Animator",
                    "自动添加 Animator 组件（需手动绑定 Controller + Avatar）"));
                _diagLastSuccess = false;
            }

            // ── InputHandler ───────────────────────────────────────
            _diagLogMessages.Add("=== CharacterInputHandler ===");
            var inputHandler = motor.GetComponent<CharacterInputHandler>();
            if (inputHandler != null)
            {
                if (Application.isPlaying)
                    _diagLogMessages.Add($"  IsClickMoving: {inputHandler.IsClickMoving}");
                else
                    _diagLogMessages.Add("  IsClickMoving: (仅 Play 模式可读)");
            }
            else
            {
                _diagLogMessages.Add("  ⚠ 缺少 CharacterInputHandler 组件");
                issues.Add(DiagnosticIssue.MissingComponent(motor.gameObject, typeof(CharacterInputHandler),
                    "CharacterInputHandler（可选）",
                    "自动添加 CharacterInputHandler 组件"));
            }

            // ── SkillController ────────────────────────────────────
            _diagLogMessages.Add("=== SkillController ===");
            var skillCtrl = motor.GetComponent<SkillController>();
            if (skillCtrl != null)
            {
                if (Application.isPlaying)
                {
                    _diagLogMessages.Add($"  InCombatMode: {skillCtrl.InCombatMode}");
                    _diagLogMessages.Add($"  TargetDirection: {skillCtrl.TargetDirection}");
                }
                else
                {
                    _diagLogMessages.Add("  InCombatMode: (仅 Play 模式可读)");
                }
            }
            else
            {
                _diagLogMessages.Add("  ⚠ 缺少 SkillController 组件");
                issues.Add(DiagnosticIssue.MissingComponent(motor.gameObject, typeof(SkillController),
                    "SkillController（可选）",
                    "自动添加 SkillController 组件"));
            }

            // ── Camera ─────────────────────────────────────────────
            _diagLogMessages.Add("=== Camera ===");
            var cam = Camera.main;
            if (cam != null)
            {
                bool hasTopdown = cam.GetComponent<TopdownCameraController>() != null;
                _diagLogMessages.Add($"  Has TopdownCameraController: {hasTopdown}");
                if (!hasTopdown)
                {
                    _diagLogMessages.Add("  ⚠ 摄像机缺少 TopdownCameraController 组件");
                    issues.Add(DiagnosticIssue.MissingComponent(cam.gameObject, typeof(TopdownCameraController),
                        "TopdownCameraController",
                        "自动添加 TopdownCameraController 组件"));
                }
            }
            else
            {
                _diagLogMessages.Add("  ❌ 未找到 MainCamera");
                issues.Add(new DiagnosticIssue
                {
                    description = "未找到 MainCamera",
                    fixHint = "在场景中创建一个 Camera，Tag 设为 MainCamera",
                    fixType = DiagnosticFixType.ManualAsset,
                });
                _diagLastSuccess = false;
            }

            // ── Collider ───────────────────────────────────────────
            _diagLogMessages.Add("=== Collider ===");
            var col = motor.GetComponent<Collider>();
            if (col != null)
            {
                _diagLogMessages.Add($"  type: {col.GetType().Name}");
                _diagLogMessages.Add($"  isTrigger: {col.isTrigger}");
            }
            else
            {
                _diagLogMessages.Add("  ⚠ 缺少 Collider 组件");
                issues.Add(DiagnosticIssue.MissingCollider(motor.gameObject));
            }

            // ── 汇总 ───────────────────────────────────────────────
            _diagLogMessages.Add("================================");
            _diagLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ✓ 诊断完成");

            // ── 修复方案（仅在有问题时输出）────────────────────
            if (issues.Count > 0)
            {
                _diagLogMessages.Add("");
                _diagLogMessages.Add("============ 修复方案 ============");
                for (int i = 0; i < issues.Count; i++)
                {
                    _diagLogMessages.Add($"【{i + 1}】{issues[i].description}");
                    if (!string.IsNullOrEmpty(issues[i].fixHint))
                    {
                        foreach (var line in issues[i].fixHint.Split('\n'))
                        {
                            _diagLogMessages.Add($"    {line.TrimEnd('\r')}");
                        }
                    }
                    _diagLogMessages.Add(issues[i].CanAutoFix
                        ? "    🔧 可一键修复"
                        : "    ✋ 需手动操作");
                    _diagLogMessages.Add("");
                }
                _diagLastSuccess = false;
            }

            // ── 保存可修复问题列表供 UI 渲染修复按钮 ──────────
            _diagIssues = issues;
        }
    }
}
#endif
