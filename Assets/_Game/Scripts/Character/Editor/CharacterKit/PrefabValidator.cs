#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// 执行后 Prefab 校验器 — 验证 Setup 流程产出的 Prefab 完整性。
    ///
    /// 解决问题：
    ///   - Enemy 原本有 ValidateEnemyPrefab 但 Character 没有（验证不对称）
    ///   - 统一为 SetupPipeline.PipelineTarget 驱动的单一入口
    ///
    /// 使用方式：
    ///   // Setup 执行完毕后
    ///   var issues = PrefabValidator.Validate(targetType, prefab, controller);
    ///   foreach (var issue in issues)
    ///       SetupLog($"  {issue.severity} {issue.message}");
    /// </summary>
    public static class PrefabValidator
    {
        public enum Severity { Error, Warning, Info }

        public struct Issue
        {
            public Severity severity;
            public string component;  // 涉及的组件/引用
            public string message;
        }

        /// <summary>
        /// 验证 Setup 产出的 Prefab 完整性。
        /// </summary>
        /// <param name="targetType">Character 或 Enemy</param>
        /// <param name="prefab">装配完成的 Prefab</param>
        /// <param name="controller">绑定到 Animator 的 Controller</param>
        /// <returns>问题列表（空 = 全部通过）</returns>
        public static List<Issue> Validate(
            SetupPipeline.PipelineTarget targetType,
            GameObject prefab,
            RuntimeAnimatorController controller)
        {
            return targetType == SetupPipeline.PipelineTarget.Character
                ? ValidateCharacterPrefab(prefab, controller)
                : ValidateEnemyPrefab(prefab, controller);
        }

        /// <summary>
        /// 验证 Character Prefab 所需的全部组件和引用。
        /// 组件清单来自 AssembleCharSetupPrefab() 中的实际装配逻辑。
        /// </summary>
        public static List<Issue> ValidateCharacterPrefab(GameObject prefab, RuntimeAnimatorController controller)
        {
            var issues = new List<Issue>();

            if (prefab == null)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "Prefab",
                    message = "Prefab 为 null，无法验证"
                });
                return issues;
            }

            // ── 运行时角色身份 ───────────────────────────────────────
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0 && prefab.layer != playerLayer)
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "GameObject.layer",
                    message = $"角色根节点必须位于 Player 层（当前: {prefab.layer}）"
                });
            if (!string.Equals(prefab.tag, "Player", System.StringComparison.Ordinal))
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "GameObject.tag",
                    message = $"角色根节点必须使用 Player Tag（当前: {prefab.tag}）"
                });

            // ── Animator ────────────────────────────────────────────
            var animator = prefab.GetComponent<Animator>();
            if (animator == null)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "Animator",
                    message = "缺少 Animator 组件"
                });
            }
            else
            {
                if (animator.avatar == null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = "Animator.avatar",
                        message = "Avatar 引用为空"
                    });

                if (animator.runtimeAnimatorController != controller && controller != null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = "Animator.Controller",
                        message = $"RuntimeAnimatorController 与预期不一致（预期: {controller.name}）"
                    });
                else if (animator.runtimeAnimatorController == null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = "Animator.Controller",
                        message = "RuntimeAnimatorController 引用为空"
                    });
            }

            // ── 物理组件 ────────────────────────────────────────────
            if (prefab.GetComponent<Rigidbody>() == null)
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "Rigidbody",
                    message = "缺少 Rigidbody 组件"
                });

            var rootCollider = prefab.GetComponent<CapsuleCollider>();
            if (rootCollider == null)
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "CapsuleCollider",
                    message = "缺少 CapsuleCollider 组件"
                });

            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            foreach (var childCollider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (childCollider == null || childCollider == rootCollider) continue;
                if (!childCollider.isTrigger || (ignoreRaycastLayer >= 0 && childCollider.gameObject.layer != ignoreRaycastLayer))
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = $"Collider:{childCollider.name}",
                        message = $"子级 Ragdoll 碰撞体必须是 Ignore Raycast 层 Trigger（当前 layer={LayerMask.LayerToName(childCollider.gameObject.layer)}, trigger={childCollider.isTrigger}），否则移动/点击/战斗射线会命中角色自身"
                    });
                    break;
                }
            }

            // ── 角色核心组件 ────────────────────────────────────────
            var requiredComponents = new (System.Type type, string name)[]
            {
                (typeof(CharacterMotor), "CharacterMotor"),
                (typeof(CharacterInputHandler), "CharacterInputHandler"),
                (typeof(Game.SkillSystem.SkillController), "SkillController"),
                (typeof(Game.SkillSystem.SkillAnimPlayer), "SkillAnimPlayer"),
                (typeof(Game.SkillSystem.HitDetector), "HitDetector"),
                (typeof(Game.SkillSystem.SkillMovementController), "SkillMovementController"),
                (typeof(Game.SkillSystem.WeaponHolder), "WeaponHolder"),
                (typeof(CharacterActionHandler), "CharacterActionHandler"),
                (typeof(CharacterLadderAction), "CharacterLadderAction"),
                (typeof(Unity.Netcode.NetworkObject), "NetworkObject"),
                (typeof(Game.Net.ClientNetworkTransform), "ClientNetworkTransform"),
                (typeof(Unity.Netcode.Components.NetworkAnimator), "NetworkAnimator"),
                (typeof(Game.Net.NetworkCharacterSetup), "NetworkCharacterSetup"),
                (typeof(Game.Net.NetworkCastRelay), "NetworkCastRelay"),
                (typeof(CharacterStats), "CharacterStats"),
            };

            foreach (var (type, name) in requiredComponents)
            {
                if (prefab.GetComponent(type) == null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = name,
                        message = $"缺少 {name} 组件"
                    });
            }

            var networkAnimator = prefab.GetComponent<Unity.Netcode.Components.NetworkAnimator>();
            if (networkAnimator != null)
            {
                var networkAnimatorSO = new SerializedObject(networkAnimator);
                var animatorProp = networkAnimatorSO.FindProperty("m_Animator");
                if (animatorProp == null || animatorProp.objectReferenceValue != prefab.GetComponent<Animator>())
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = "NetworkAnimator.m_Animator",
                        message = "NetworkAnimator 未绑定同根 Animator"
                    });
                }
            }

            // ── 技能系统引用完整性 ──────────────────────────────────
            var skillCtrl = prefab.GetComponent<Game.SkillSystem.SkillController>();
            if (skillCtrl != null)
            {
                if (skillCtrl.animPlayer == null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Warning,
                        component = "SkillController.animPlayer",
                        message = "SkillController.animPlayer 引用为空"
                    });

                if (skillCtrl.movementController == null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Warning,
                        component = "SkillController.movementController",
                        message = "SkillController.movementController 引用为空"
                    });

                if (skillCtrl.weaponHolder == null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Warning,
                        component = "SkillController.weaponHolder",
                        message = "SkillController.weaponHolder 引用为空，后续 Weapon Setup 步骤将设置此项"
                    });

                if (skillCtrl.inputHandler == null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Warning,
                        component = "SkillController.inputHandler",
                        message = "SkillController.inputHandler 引用为空"
                    });

                int attackCount = 0;
                if (skillCtrl.attackSlots != null)
                    foreach (var data in skillCtrl.attackSlots)
                        if (data != null) attackCount++;
                int skillCount = 0;
                if (skillCtrl.skillSlots != null)
                    foreach (var data in skillCtrl.skillSlots)
                        if (data != null) skillCount++;
                if (attackCount == 0)
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = "SkillController.attackSlots",
                        message = "没有绑定任何普攻 SkillData，运行时鼠标攻击将失效"
                    });
                if (skillCount == 0)
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = "SkillController.skillSlots",
                        message = "没有绑定任何技能 SkillData，运行时技能按键将失效"
                    });
                if (skillCtrl.hitDetector == null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = "SkillController.hitDetector",
                        message = "HitDetector 引用为空，运行时命中/伤害节点不会执行"
                    });
            }

            return issues;
        }

        /// <summary>
        /// 验证 Enemy Prefab 所需的全部组件和引用。
        /// 从原有 ValidateEnemyPrefab 抽取并增强。
        /// </summary>
        public static List<Issue> ValidateEnemyPrefab(GameObject prefab, RuntimeAnimatorController controller)
        {
            var issues = new List<Issue>();

            if (prefab == null)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "Prefab",
                    message = "Prefab 为 null，无法验证"
                });
                return issues;
            }

            // ── Animator ────────────────────────────────────────────
            var animator = prefab.GetComponent<Animator>();
            if (animator == null)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "Animator",
                    message = "缺少 Animator 组件"
                });
            }
            else
            {
                if (animator.runtimeAnimatorController != controller && controller != null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = "Animator.Controller",
                        message = $"RuntimeAnimatorController 与预期不一致（预期: {controller.name}）"
                    });
                else if (animator.runtimeAnimatorController == null)
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        component = "Animator.Controller",
                        message = "RuntimeAnimatorController 引用为空"
                    });
            }

            // ── 物理组件 ────────────────────────────────────────────
            if (prefab.GetComponent<Rigidbody>() == null)
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "Rigidbody",
                    message = "缺少 Rigidbody 组件"
                });

            if (prefab.GetComponent<CapsuleCollider>() == null)
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "CapsuleCollider",
                    message = "缺少 CapsuleCollider 组件"
                });

            // ── 敌人核心组件 ────────────────────────────────────────
            if (prefab.GetComponent<EnemyMotor>() == null)
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "EnemyMotor",
                    message = "缺少 EnemyMotor 组件"
                });

            if (prefab.GetComponent<EnemyAI>() == null)
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "EnemyAI",
                    message = "缺少 EnemyAI 组件"
                });

            if (prefab.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "NavMeshAgent",
                    message = "缺少 NavMeshAgent 组件"
                });

            if (prefab.GetComponent<Game.SkillSystem.HitDetector>() == null)
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    component = "HitDetector",
                    message = "缺少 HitDetector 组件"
                });

            // ── EnemyAI 引用完整性 ──────────────────────────────────
            var enemyAI = prefab.GetComponent<EnemyAI>();
            if (enemyAI != null)
            {
                if (enemyAI.attackClips == null || enemyAI.attackClips.Length == 0)
                    issues.Add(new Issue
                    {
                        severity = Severity.Info,
                        component = "EnemyAI.attackClips",
                        message = "attackClips 为空（可能没有配置攻击动画）"
                    });
            }

            return issues;
        }

        // ═══════════════════════════════════════════════════════════════
        // 辅助
        // ═══════════════════════════════════════════════════════════════

        /// <summary>是否有 Error 级别问题。</summary>
        public static bool HasErrors(List<Issue> issues)
        {
            return issues.Exists(i => i.severity == Severity.Error);
        }

        /// <summary>格式化问题列表为日志友好的字符串。</summary>
        public static string FormatIssues(List<Issue> issues)
        {
            if (issues.Count == 0) return "✔ 全部通过";

            var sb = new StringBuilder();
            foreach (var issue in issues)
            {
                string icon = issue.severity == Severity.Error ? "❌" :
                              issue.severity == Severity.Warning ? "⚠" : "ℹ";
                sb.AppendLine($"  {icon} [{issue.component}] {issue.message}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
#endif
