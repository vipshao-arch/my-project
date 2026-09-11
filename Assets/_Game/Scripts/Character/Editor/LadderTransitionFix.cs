using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace Game.Character.Editor
{
    /// <summary>
    /// 梯子状态诊断工具。
    /// 梯子动画已经存在于 NormalState/Actions/Ladder 状态机中，
    /// 不再创建根层代理状态；代理状态会破坏原有 Actions 状态机的过渡并导致反复上下。
    /// </summary>
    [InitializeOnLoad]
    public static class LadderTransitionFix
    {
        private const string ControllerPath = "Assets/_Game/character/Common/DefaultCharacterController.controller";

        private static readonly string[] ProxyStates =
        {
            "LadderEnterBot", "LadderEnterTop", "LadderClimb",
            "LadderExitBot", "LadderExitTop"
        };

        private static readonly string[] SourceStates =
        {
            "EnterLadderBottom", "EnterLadderTop", "ClimbLadder",
            "ExitLadderBottom", "ExitLadderTop"
        };

        private static readonly string[] Triggers =
        {
            "DoEnterLadderBottom", "DoEnterLadderTop", "DoClimbLadder",
            "DoExitLadderBottom", "DoExitLadderTop"
        };

        static LadderTransitionFix()
        {
            EditorApplication.delayCall += () =>
            {
                try { CleanupProxyStates(silent: true); }
                catch (System.Exception e) { Debug.LogError($"[LadderFix] Cleanup failed: {e}"); }
            };
        }

        [MenuItem("Tools/Ladder/Remove Proxy States")]
        public static void RemoveProxyStatesMenu()
        {
            CleanupProxyStates(silent: false);
        }

        private static void CleanupProxyStates(bool silent)
        {
            var ctrl = LoadController();
            if (ctrl == null) return;
            var sm = ctrl.layers[0].stateMachine;
            int removed = 0;
            foreach (var child in sm.states.ToArray())
            {
                if (!ProxyStates.Contains(child.state.name)) continue;
                sm.RemoveState(child.state);
                removed++;
            }
            if (removed > 0)
            {
                EditorUtility.SetDirty(ctrl);
                AssetDatabase.SaveAssets();
            }
            if (!silent)
                Debug.Log($"[LadderFix] Removed proxy states: {removed}; use Actions/Ladder states only.");
        }

        [MenuItem("Tools/Ladder/Diagnose Transitions")]
        public static void Diagnose()
        {
            var ctrl = LoadController();
            if (ctrl == null) return;

            var sm = ctrl.layers[0].stateMachine;
            Debug.Log($"[LadderDiag] Root SM: {sm.name}, states: {sm.states.Length}, anyStateTrans: {sm.anyStateTransitions.Length}");

            foreach (var s in sm.states)
                Debug.Log($"[LadderDiag] Root state: '{s.state.name}', motion={s.state.motion?.name ?? "null"}");

            // Find source states recursively
            var found = new Dictionary<string, AnimatorState>();
            FindStatesRecursive(sm, found);
            foreach (var name in SourceStates)
                Debug.Log($"[LadderDiag] Source '{name}': {(found.ContainsKey(name) ? $"FOUND, motion={found[name].motion?.name ?? "null"}" : "MISSING")}");
        }

        [MenuItem("Tools/Ladder/Fix Transitions")]
        public static void FixTransitionsMenu()
        {
            // 兼容旧菜单路径，但不再创建代理状态；只清理历史代理。
            try { CleanupProxyStates(silent: false); }
            catch (System.Exception e) { Debug.LogError($"[LadderFix] Cleanup failed: {e}"); }
        }

        private static AnimatorController LoadController()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl == null)
                Debug.LogError($"[LadderFix] Cannot load {ControllerPath}");
            return ctrl;
        }

        private static void FixTransitions(bool silent)
        {
            // 保留旧 API 以兼容已有调用方，但绝不再修改/添加 Animator 状态。
            CleanupProxyStates(silent);
        }

        private static void AddProxyTransition(
            Dictionary<string, AnimatorState> proxies,
            string from, string to,
            bool hasExitTime = false, float exitTime = 0f, float duration = 0.1f,
            string trigger = null)
        {
            if (!proxies.ContainsKey(from) || !proxies.ContainsKey(to)) return;

            // Check if transition already exists
            foreach (var t in proxies[from].transitions)
            {
                if (t.destinationState == proxies[to])
                    return; // Already exists
            }

            var tr = proxies[from].AddTransition(proxies[to]);
            if (trigger != null)
            {
                tr.AddCondition(AnimatorConditionMode.If, 0, trigger);
                tr.hasExitTime = false;
            }
            else
            {
                tr.hasExitTime = hasExitTime;
                tr.exitTime = exitTime;
            }
            tr.duration = duration;
            tr.hasFixedDuration = true;
        }

        private static void AddTransitionToTarget(
            Dictionary<string, AnimatorState> proxies,
            string from, AnimatorState target,
            bool hasExitTime, float exitTime, float duration)
        {
            if (!proxies.ContainsKey(from)) return;

            foreach (var t in proxies[from].transitions)
            {
                if (t.destinationState == target)
                    return;
            }

            var tr = proxies[from].AddTransition(target);
            tr.hasExitTime = hasExitTime;
            tr.exitTime = exitTime;
            tr.duration = duration;
            tr.hasFixedDuration = true;
        }

        private static void FindStatesRecursive(AnimatorStateMachine sm, Dictionary<string, AnimatorState> found)
        {
            foreach (var s in sm.states)
                foreach (var target in SourceStates)
                    if (s.state.name == target && !found.ContainsKey(target))
                        found[target] = s.state;

            foreach (var child in sm.stateMachines)
                FindStatesRecursive(child.stateMachine, found);
        }
    }
}
