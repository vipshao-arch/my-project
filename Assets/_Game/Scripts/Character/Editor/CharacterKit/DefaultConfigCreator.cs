#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Game.Character;
using System.IO;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// 默认预设资产创建器 — 在 _Game/character/Common/ 下创建通用预设资产。
    ///
    /// 创建清单：
    ///   - DefaultPlayerConfig     — 主角手感配置模板（移动速度、旋转、跳跃等）
    ///   - DefaultEnemyArchetype    — 敌人行为配方模板（感知/攻击/技能/掉落）
    ///   - DefaultCharacterAnimSet   — 主角动画集模板（4 组攻击 + 4 组技能 + 2 组过渡）
    ///   - DefaultEnemyAnimSet       — 敌人动画集模板（6 个攻击动画空位 + 技能组 + 2 组过渡）
    ///   - DefaultCharacterController — 从 graves_Character 复制的 Animator Controller 模板
    ///
    /// 调用方式：由 GetOrCreate* 系列方法在资产缺失时自动调用（一次性引导，无需手动触发）。
    /// </summary>
    public static class DefaultConfigCreator
    {
        const string CommonDir = "Assets/_Game/character/Common";
        const string SourceControllerPath = "Assets/_Game/character/graves/graves_Character.controller";

        public static void CreateDefaultAssets()
        {
            EnsureDir(CommonDir);

            // ── DefaultPlayerConfig ────────────────────────────────────
            string playerPath = $"{CommonDir}/DefaultPlayerConfig.asset";
            if (!AssetDatabase.LoadAssetAtPath<PlayerConfigData>(playerPath))
            {
                var playerCfg = ScriptableObject.CreateInstance<PlayerConfigData>();
                playerCfg.name = "DefaultPlayerConfig";
                // 使用 PlayerConfigData 类中已有的默认值（无需额外设）
                AssetDatabase.CreateAsset(playerCfg, playerPath);
                Debug.Log($"[CharacterKit] 已创建通用主角配置预设: {playerPath}");
            }
            else
            {
                Debug.Log($"[CharacterKit] 通用主角配置预设已存在，跳过: {playerPath}");
            }

            // ── DefaultEnemyArchetype ──────────────────────────────────
            string enemyPath = $"{CommonDir}/DefaultEnemyArchetype.asset";
            if (!AssetDatabase.LoadAssetAtPath<EnemyArchetype>(enemyPath))
            {
                var enemyArch = ScriptableObject.CreateInstance<EnemyArchetype>();
                enemyArch.archetypeName = "默认敌人配方";
                enemyArch.description = "通用敌人配置预设模板。请在此基础上修改并保存到各自敌人目录。";
                // 使用 EnemyArchetype 类中已有的默认值
                AssetDatabase.CreateAsset(enemyArch, enemyPath);
                Debug.Log($"[CharacterKit] 已创建通用敌人配置预设: {enemyPath}");
            }
            else
            {
                Debug.Log($"[CharacterKit] 通用敌人配置预设已存在，跳过: {enemyPath}");
            }

            // ── DefaultEnemyAnimSet ──────────────────────────────────
            string enemyAnimSetPath = $"{CommonDir}/DefaultEnemyAnimSet.asset";
            if (!AssetDatabase.LoadAssetAtPath<EnemyAnimSetAsset>(enemyAnimSetPath))
            {
                var enemyAnimSet = ScriptableObject.CreateInstance<EnemyAnimSetAsset>();
                enemyAnimSet.name = "DefaultEnemyAnimSet";
                // 默认 6 个空位（对应 Attack_1~Attack_6）
                for (int i = 0; i < 6; i++)
                    enemyAnimSet.attackClips.Add(null);
                // 默认 2 个主动技能槽（Q / W，各 1 段空位）
                enemyAnimSet.skills.Add(new CharacterAnimSequence
                {
                    id = "Skill_Q",
                    segments = new System.Collections.Generic.List<AnimationClip> { null }
                });
                enemyAnimSet.skills.Add(new CharacterAnimSequence
                {
                    id = "Skill_W",
                    segments = new System.Collections.Generic.List<AnimationClip> { null }
                });
                // 默认 2 组通用过渡（与主角模板对齐）
                enemyAnimSet.blendTransitions.Add(new BlendTransitionData
                {
                    type = BlendTransitionData.TransitionType.TwoWay,
                    fromState = "Idle",
                    toState = "Move",
                    blendInDuration = 0.05f,
                    blendOutDuration = 0.1f
                });
                enemyAnimSet.blendTransitions.Add(new BlendTransitionData
                {
                    type = BlendTransitionData.TransitionType.AnyStateToTarget,
                    toState = "Idle",
                    blendInDuration = 0.05f,
                    blendOutDuration = 0.1f
                });
                AssetDatabase.CreateAsset(enemyAnimSet, enemyAnimSetPath);
                Debug.Log($"[CharacterKit] 已创建通用敌人动画集预设: {enemyAnimSetPath}");
            }
            else
            {
                Debug.Log($"[CharacterKit] 通用敌人动画集预设已存在，跳过: {enemyAnimSetPath}");
            }

            // ── DefaultCharacterAnimSet ──────────────────────────────────────
            string animSetPath = $"{CommonDir}/DefaultCharacterAnimSet.asset";
            if (!AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(animSetPath))
            {
                // 如果存在旧的 DefaultAnimSet.asset，迁移路径
                string oldAnimSetPath = $"{CommonDir}/DefaultAnimSet.asset";
                var oldAnimSet = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(oldAnimSetPath);
                if (oldAnimSet != null)
                {
                    AssetDatabase.MoveAsset(oldAnimSetPath, animSetPath);
                    oldAnimSet.name = "DefaultCharacterAnimSet";
                    Debug.Log($"[CharacterKit] 已将旧版动画集 DefaultAnimSet 重命名为 DefaultCharacterAnimSet: {animSetPath}");
                }
                else
                {
                    var animSet = ScriptableObject.CreateInstance<CharacterAnimSetAsset>();
                    animSet.name = "DefaultCharacterAnimSet";

                    // 默认 4 组攻击（每组 1 段空位）
                    for (int i = 1; i <= 4; i++)
                    {
                        animSet.attacks.Add(new CharacterAnimSequence
                        {
                            id = $"Attack_{i}",
                            segments = new System.Collections.Generic.List<AnimationClip> { null }
                        });
                    }

                    // 默认 4 组技能（Q/W/E/R，每组 1 段空位）
                    string[] defaultSkillIds = { "Q", "W", "E", "R" };
                    foreach (var sid in defaultSkillIds)
                    {
                        animSet.skills.Add(new CharacterAnimSequence
                        {
                            id = $"Skill_{sid}",
                            segments = new System.Collections.Generic.List<AnimationClip> { null }
                        });
                    }

                    // 默认 2 组通用过渡（Idle↔Move 双向，多段技能→Idle）
                    animSet.blendTransitions.Add(new BlendTransitionData
                    {
                        type = BlendTransitionData.TransitionType.TwoWay,
                        fromState = "Idle",
                        toState = "Move",
                        blendInDuration = 0.05f,
                        blendOutDuration = 0.1f
                    });
                    animSet.blendTransitions.Add(new BlendTransitionData
                    {
                        type = BlendTransitionData.TransitionType.AnyStateToTarget,
                        toState = "Idle",
                        blendInDuration = 0.05f,
                        blendOutDuration = 0.1f
                    });

                    AssetDatabase.CreateAsset(animSet, animSetPath);
                    Debug.Log($"[CharacterKit] 已创建通用主角动画集预设: {animSetPath}");
                }
            }
            else
            {
                Debug.Log($"[CharacterKit] 通用主角动画集预设已存在，跳过: {animSetPath}");
            }

            // ── DefaultCharacterController ──────────────────────────
            string ctrlPath = $"{CommonDir}/DefaultCharacterController.controller";
            if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath))
            {
                var srcCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(SourceControllerPath);
                if (srcCtrl != null)
                {
                    AssetDatabase.CopyAsset(SourceControllerPath, ctrlPath);
                    var copied = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
                    EnsureCharacterControllerParameters(copied);
                    Debug.Log($"[CharacterKit] 已从 graves_Character 复制默认控制器预设: {ctrlPath}");
                }
                else
                {
                    Debug.LogWarning($"[CharacterKit] 源控制器 {SourceControllerPath} 不存在，无法创建默认控制器预设");
                }
            }
            else
            {
                Debug.Log($"[CharacterKit] 通用控制器预设已存在，跳过: {ctrlPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>获取或创建默认主角配置（不存在则自动创建）。</summary>
        public static PlayerConfigData GetOrCreateDefaultPlayerConfig()
        {
            string path = $"{CommonDir}/DefaultPlayerConfig.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PlayerConfigData>(path);
            if (existing != null) return existing;

            CreateDefaultAssets();
            return AssetDatabase.LoadAssetAtPath<PlayerConfigData>(path);
        }

        /// <summary>获取或创建默认敌人配方（不存在则自动创建）。</summary>
        public static EnemyArchetype GetOrCreateDefaultEnemyArchetype()
        {
            string path = $"{CommonDir}/DefaultEnemyArchetype.asset";
            var existing = AssetDatabase.LoadAssetAtPath<EnemyArchetype>(path);
            if (existing != null) return existing;

            CreateDefaultAssets();
            return AssetDatabase.LoadAssetAtPath<EnemyArchetype>(path);
        }

        /// <summary>获取或创建默认主角动画集（不存在则自动创建）。</summary>
        public static CharacterAnimSetAsset GetOrCreateDefaultAnimSet()
        {
            // 优先新路径，fallback 旧路径
            string path = $"{CommonDir}/DefaultCharacterAnimSet.asset";
            var existing = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(path);
            if (existing != null) return existing;

            // 尝试旧路径迁移
            string oldPath = $"{CommonDir}/DefaultAnimSet.asset";
            var old = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(oldPath);
            if (old != null)
            {
                AssetDatabase.MoveAsset(oldPath, path);
                old.name = "DefaultCharacterAnimSet";
                Debug.Log($"[CharacterKit] 已将旧版 DefaultAnimSet 重命名为 DefaultCharacterAnimSet");
                return old;
            }

            CreateDefaultAssets();
            return AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>(path);
        }

        /// <summary>获取或创建默认敌人动画集（不存在则自动创建）。</summary>
        public static EnemyAnimSetAsset GetOrCreateDefaultEnemyAnimSet()
        {
            string path = $"{CommonDir}/DefaultEnemyAnimSet.asset";
            var existing = AssetDatabase.LoadAssetAtPath<EnemyAnimSetAsset>(path);
            if (existing != null) return existing;

            CreateDefaultAssets();
            return AssetDatabase.LoadAssetAtPath<EnemyAnimSetAsset>(path);
        }

        /// <summary>获取或创建默认 Animator Controller（不存在则从 graves_Character 复制）。</summary>
        public static AnimatorController GetOrCreateDefaultCharacterController()
        {
            string path = $"{CommonDir}/DefaultCharacterController.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null)
            {
                EnsureCharacterControllerParameters(existing);
                return existing;
            }

            CreateDefaultAssets();
            var created = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            EnsureCharacterControllerParameters(created);
            return created;
        }

        /// <summary>
        /// 兼容旧 Default Controller：部分子状态机使用 Crouch / LandHigh 条件，
        /// 但旧资产（graves_Character.controller）只声明 IsCrouching 等旧参数，
        /// 导致代码状态已进入而 Animator 永远不切状态（落地动画断链）。
        /// </summary>
        public static void EnsureCharacterControllerParameters(AnimatorController controller)
        {
            if (controller == null) return;
            bool changed = false;
            changed |= EnsureBoolParameter(controller, "Crouch");
            changed |= EnsureBoolParameter(controller, "LandHigh");
            if (changed)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>确保 Controller 存在指定 bool 参数，缺失则补上并返回 true。</summary>
        private static bool EnsureBoolParameter(AnimatorController controller, string name)
        {
            foreach (var parameter in controller.parameters)
                if (parameter.name == name) return false;
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
            return true;
        }

        /// <summary>重建干净的默认 Enemy Animator Controller，不复制任何现有 Controller。</summary>
        public static AnimatorController RebuildDefaultEnemyController()
        {
            string path = $"{CommonDir}/DefaultEnemyController.controller";
            EnsureDir(CommonDir);

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.Refresh();
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("AttackIndex", AnimatorControllerParameterType.Int);

            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            var hit = sm.AddState("Hit");
            var death = sm.AddState("Death");
            sm.defaultState = idle;

            AddConditionTransition(idle, walk, AnimatorConditionMode.Greater, 0.1f, "Speed");
            AddConditionTransition(walk, idle, AnimatorConditionMode.Less, 0.05f, "Speed");
            AddExitTransition(hit, idle, 0.85f, 0.15f);

            var anyHit = sm.AddAnyStateTransition(hit);
            anyHit.hasExitTime = false;
            anyHit.duration = 0.1f;
            anyHit.canTransitionToSelf = false;
            anyHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");

            var anyDeath = sm.AddAnyStateTransition(death);
            anyDeath.hasExitTime = false;
            anyDeath.duration = 0.15f;
            anyDeath.canTransitionToSelf = false;
            anyDeath.AddCondition(AnimatorConditionMode.If, 0, "IsDead");

            for (int i = 0; i < 6; i++)
            {
                var attack = sm.AddState($"Attack{(i + 1):00}");
                var anyAttack = sm.AddAnyStateTransition(attack);
                anyAttack.hasExitTime = false;
                anyAttack.duration = 0.1f;
                anyAttack.canTransitionToSelf = false;
                anyAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");
                anyAttack.AddCondition(AnimatorConditionMode.Equals, i, "AttackIndex");
                AddExitTransition(attack, idle, 0.85f, 0.2f);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CharacterKit] 已重建干净默认敌人控制器: {path}");
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }

        static void AddConditionTransition(AnimatorState from, AnimatorState to,
            AnimatorConditionMode mode, float threshold, string parameter)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.15f;
            transition.AddCondition(mode, threshold, parameter);
        }

        static void AddExitTransition(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = duration;
        }

        /// <summary>获取或创建默认 Enemy Animator Controller，并拒绝结构损坏的旧资产。</summary>
        public static AnimatorController GetOrCreateDefaultEnemyController()
        {
            string path = $"{CommonDir}/DefaultEnemyController.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null)
            {
                if (ValidateEnemyController(existing, out string reason))
                    return existing;

                Debug.LogWarning($"[CharacterKit] 默认敌人 Controller 校验失败，将自动重建: {reason}");
            }

            return RebuildDefaultEnemyController();
        }

        /// <summary>校验默认敌人 Controller 的参数、默认状态和 Transition 引用。</summary>
        public static bool ValidateEnemyController(AnimatorController controller, out string reason)
        {
            if (controller == null)
            {
                reason = "Controller 为空";
                return false;
            }

            if (controller.layers == null || controller.layers.Length == 0)
            {
                reason = "没有 Animator Layer";
                return false;
            }

            var parameters = new System.Collections.Generic.HashSet<string>();
            foreach (var parameter in controller.parameters)
            {
                if (parameter != null)
                    parameters.Add(parameter.name);
            }

            string[] requiredParameters = { "Speed", "Attack", "Hit", "IsDead", "AttackIndex" };
            foreach (var parameter in requiredParameters)
            {
                if (!parameters.Contains(parameter))
                {
                    reason = $"缺少参数 {parameter}";
                    return false;
                }
            }

            for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
            {
                var stateMachine = controller.layers[layerIndex].stateMachine;
                if (stateMachine == null)
                {
                    reason = $"Layer {layerIndex} 的 StateMachine 为空";
                    return false;
                }

                if (stateMachine.defaultState == null)
                {
                    reason = $"Layer {layerIndex} 没有默认状态";
                    return false;
                }

                if (!ValidateEnemyStateMachine(stateMachine, layerIndex, out reason))
                    return false;
            }

            reason = null;
            return true;
        }

        static bool ValidateEnemyStateMachine(AnimatorStateMachine stateMachine, int layerIndex, out string reason)
        {
            foreach (var childState in stateMachine.states)
            {
                if (childState.state == null)
                {
                    reason = $"Layer {layerIndex} 包含空 State 引用";
                    return false;
                }

                foreach (var transition in childState.state.transitions)
                {
                    if (transition == null || transition.isExit)
                        continue;
                    if (transition.destinationState == null && transition.destinationStateMachine == null)
                    {
                        reason = $"State {childState.state.name} 包含空目标 Transition";
                        return false;
                    }
                }
            }

            foreach (var transition in stateMachine.anyStateTransitions)
            {
                if (transition == null || transition.isExit)
                    continue;
                if (transition.destinationState == null && transition.destinationStateMachine == null)
                {
                    reason = $"AnyState 包含空目标 Transition";
                    return false;
                }
            }

            foreach (var transition in stateMachine.entryTransitions)
            {
                if (transition == null ||
                    (transition.destinationState == null && transition.destinationStateMachine == null))
                {
                    reason = $"Entry 包含空目标 Transition";
                    return false;
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                if (childStateMachine.stateMachine == null)
                {
                    reason = $"包含空子 StateMachine 引用";
                    return false;
                }

                if (!ValidateEnemyStateMachine(childStateMachine.stateMachine, layerIndex, out reason))
                    return false;
            }

            reason = null;
            return true;
        }

        static void EnsureDir(string dir)
        {
            var parts = dir.Split('/');
            string current = "";
            for (int i = 0; i < parts.Length; i++)
            {
                current = i == 0 ? parts[i] : $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(current))
                {
                    string parent = current.Substring(0, current.LastIndexOf('/'));
                    string folder = current.Substring(current.LastIndexOf('/') + 1);
                    AssetDatabase.CreateFolder(parent, folder);
                }
            }
        }
    }
}
#endif
