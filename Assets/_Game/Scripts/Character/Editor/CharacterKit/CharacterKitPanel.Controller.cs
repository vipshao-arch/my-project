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
        // ---- Controller binding helpers ----


        /// <summary>把一个 CharacterAnimSequence 绑定到 AnimatorController（主层 Base Layer）。</summary>
        void BindAnimSequenceToController(AnimatorController controller, CharacterAnimSequence seq)
        {
            if (seq == null || !seq.HasAnySegment()) return;
            if (controller.layers.Length == 0) return;

            var sm = controller.layers[0].stateMachine;
            string baseId = string.IsNullOrEmpty(seq.id) ? "Anim" : seq.id;

            // ── 1) 主体状态（多段或单段）─────────────────────────────
            if (seq.segments.Count == 1)
            {
                // 单段：直接设置 motion
                var mainClip = seq.GetFirstValidSegment();
                if (mainClip != null)
                    ReplaceCharSetupMotionByName(controller, baseId, mainClip);
            }
            else
            {
                // 多段：用 sub state machine 把多段串起来
                var subSm = EnsureOrCreateSubStateMachine(sm, baseId + "_Combo");
                // 清除旧的多段子状态（避免残留）
                var toRemove = subSm.states
                    .Where(s => s.state.name.StartsWith(baseId + "_"))
                    .Select(s => s.state)
                    .ToList();
                foreach (var st in toRemove)
                {
                    // 先清除 transition 再移除 state
                    subSm.RemoveState(st);
                }
                // 重新构建
                RebuildMultiSegmentSM(subSm, seq, baseId);
            }

            // ── 2) 衔接：起手 → 主体 ─────────────────────────────────
            if (seq.leadIn != null)
            {
                var leadInState = EnsureOrCreateState(sm, baseId + "_LeadIn", seq.leadIn);
                var mainState = FindStateByName(sm, baseId);
                if (mainState != null)
                {
                    var t = leadInState.AddTransition(mainState);
                    t.hasExitTime = true;
                    t.exitTime = 0.95f;
                    t.duration = seq.leadTransitionDuration;
                }
            }

            // ── 3) 衔接：主体 → 收招 ─────────────────────────────────
            if (seq.leadOut != null)
            {
                var leadOutState = EnsureOrCreateState(sm, baseId + "_LeadOut", seq.leadOut);
                var mainState = FindStateByName(sm, baseId);
                if (mainState != null)
                {
                    var t = mainState.AddTransition(leadOutState);
                    t.hasExitTime = true;
                    t.exitTime = 0.95f;
                    t.duration = seq.leadTransitionDuration;
                }
            }

            // ── 4) AnyState → 起手（或主体），支持连击打断 ──────────
            var entryState = seq.leadIn != null
                ? FindStateByName(sm, baseId + "_LeadIn")
                : FindStateByName(sm, baseId);
            if (entryState != null)
            {
                var anyTransition = sm.AddAnyStateTransition(entryState);
                anyTransition.hasExitTime = false;
                anyTransition.duration = 0.05f;
                anyTransition.canTransitionToSelf = seq.canInterruptSelf;
            }
        }

        /// <summary>从 AnimationClip 名称派生状态名。</summary>
        static string StateNameFromClip(AnimationClip clip)
        {
            return clip != null ? clip.name : "Unnamed";
        }

        /// <summary>把一条 BlendTransitionData 绑定到 AnimatorController。</summary>
        void BindBlendTransitionToController(AnimatorController controller, BlendTransitionData trans)
        {
            if (controller.layers.Length == 0) return;
            var sm = controller.layers[0].stateMachine;

            switch (trans.type)
            {
                case BlendTransitionData.TransitionType.OneWay:
                    {
                        string fromName = StateNameFromClip(trans.fromClip);
                        string toName = StateNameFromClip(trans.toClip);
                        ApplyOneWayTransition(sm, trans, fromName, toName);
                    }
                    break;

                case BlendTransitionData.TransitionType.TwoWay:
                    {
                        string fromName = StateNameFromClip(trans.fromClip);
                        string toName = StateNameFromClip(trans.toClip);
                        ApplyOneWayTransition(sm, trans, fromName, toName);
                        ApplyOneWayTransition(sm, trans, toName, fromName);
                    }
                    break;

                case BlendTransitionData.TransitionType.AnyStateToTarget:
                    ApplyAnyStateToTarget(sm, trans);
                    break;
            }
        }

        void ApplyOneWayTransition(AnimatorStateMachine sm, BlendTransitionData trans, string fromName, string toName)
        {
            if (string.IsNullOrEmpty(fromName) || string.IsNullOrEmpty(toName)) return;
            if (trans.fromClip == null || trans.toClip == null) return;

            // 确保源和目标状态存在
            var fromState = EnsureOrCreateState(sm, fromName, trans.fromClip);
            var toState = EnsureOrCreateState(sm, toName, trans.toClip);

            // 移除已有的同名过渡
            foreach (var existing in fromState.transitions)
            {
                if (existing.destinationState == toState)
                    fromState.RemoveTransition(existing);
            }

            if (trans.transitionClip != null)
            {
                // ── 有独立过渡动画：创建中间过渡状态 ──────────────────
                string transStateName = $"{fromName}To{toName}_Transition";
                var transState = EnsureOrCreateState(sm, transStateName, trans.transitionClip);

                // 移除已有的过渡
                foreach (var existing in fromState.transitions)
                {
                    if (existing.destinationState == transState)
                        fromState.RemoveTransition(existing);
                }
                foreach (var existing in transState.transitions)
                {
                    if (existing.destinationState == toState)
                        transState.RemoveTransition(existing);
                }

                // From → TransitionState（切入 Blend）
                var t1 = fromState.AddTransition(transState);
                // 无条件过渡必须使用 Exit Time，否则 Unity 会判定为无效过渡并忽略。
                t1.hasExitTime = true;
                t1.exitTime = 0f;
                t1.duration = trans.blendInDuration;

                // TransitionState → To（动画播完后自动切，切出 Blend）
                var t2 = transState.AddTransition(toState);
                t2.hasExitTime = true;
                t2.exitTime = 0.95f;
                t2.duration = trans.blendOutDuration;
            }
            else
            {
                // ── 无独立过渡动画：直接过渡 ─────────────────────────
                var t = fromState.AddTransition(toState);
                // 无条件过渡使用零 Exit Time，避免被 Unity 判定为无效并忽略。
                t.hasExitTime = true;
                t.exitTime = 0f;
                t.duration = trans.blendOutDuration;
            }
        }

        void ApplyAnyStateToTarget(AnimatorStateMachine sm, BlendTransitionData trans)
        {
            if (trans.toClip == null) return;
            string toName = StateNameFromClip(trans.toClip);

            // 确保目标状态存在
            var toState = EnsureOrCreateState(sm, toName, trans.toClip);

            if (trans.transitionClip != null)
            {
                // ── 有独立过渡动画：AnyState → TransitionState → To ──
                string transStateName = $"AnyTo{toName}_Transition";
                var transState = EnsureOrCreateState(sm, transStateName, trans.transitionClip);

                // AnyState → TransitionState（切入 Blend）
                var anyT = sm.AddAnyStateTransition(transState);
                anyT.hasExitTime = false;
                anyT.duration = trans.blendInDuration;
                anyT.canTransitionToSelf = false;

                // TransitionState → To（动画播完自动切，切出 Blend）
                foreach (var existing in transState.transitions)
                {
                    if (existing.destinationState == toState)
                        transState.RemoveTransition(existing);
                }
                var t2 = transState.AddTransition(toState);
                t2.hasExitTime = true;
                t2.exitTime = 0.95f;
                t2.duration = trans.blendOutDuration;
            }
            else
            {
                // ── 无独立过渡动画：直接 AnyState → To ────────────────
                var anyT = sm.AddAnyStateTransition(toState);
                anyT.hasExitTime = false;
                anyT.duration = trans.blendOutDuration;
                anyT.canTransitionToSelf = false;
            }
        }

        /// <summary>在子状态机中构建多段动画链。</summary>
        void RebuildMultiSegmentSM(AnimatorStateMachine subSm, CharacterAnimSequence seq, string baseId)
        {
            var validSegments = seq.segments.Where(s => s != null).ToList();
            if (validSegments.Count == 0) return;

            for (int i = 0; i < validSegments.Count; i++)
            {
                string stateName = $"{baseId}_{i + 1}";
                var state = EnsureOrCreateState(subSm, stateName, validSegments[i]);

                // 段间 transition（上一段 → 当前段）
                if (i > 0)
                {
                    var prevState = FindStateByName(subSm, $"{baseId}_{i}");
                    if (prevState != null)
                    {
                        var t = prevState.AddTransition(state);
                        t.hasExitTime = true;
                        t.exitTime = 0.95f;
                        t.duration = seq.segmentTransitionDuration;
                        t.canTransitionToSelf = false;
                    }
                }
            }

            // 设置第一个状态为默认状态
            var firstState = FindStateByName(subSm, $"{baseId}_1");
            if (firstState != null)
                subSm.defaultState = firstState;
        }

        // ── StateMachine 构建辅助 ────────────────────────────────────

        AnimatorState EnsureOrCreateState(AnimatorStateMachine sm, string stateName, AnimationClip clip)
        {
            var existing = FindStateByName(sm, stateName);
            if (existing != null)
            {
                existing.motion = clip;
                return existing;
            }
            var state = sm.AddState(stateName);
            state.motion = clip;
            return state;
        }

        AnimatorStateMachine EnsureOrCreateSubStateMachine(AnimatorStateMachine parentSm, string smName)
        {
            foreach (var childSm in parentSm.stateMachines)
            {
                if (childSm.stateMachine.name == smName)
                    return childSm.stateMachine;
            }
            return parentSm.AddStateMachine(smName);
        }

        AnimatorState FindStateByName(AnimatorStateMachine sm, string stateName)
        {
            foreach (var childState in sm.states)
            {
                if (childState.state.name == stateName)
                    return childState.state;
            }
            foreach (var childSm in sm.stateMachines)
            {
                var found = FindStateByName(childSm.stateMachine, stateName);
                if (found != null) return found;
            }
            return null;
        }

        // ── 旧辅助方法（保留兼容）────────────────────────────────────

        int ReplaceLocomotionClipsInSM(AnimatorStateMachine sm, bool isStrafeLayer = false)
        {
            int count = 0;
            foreach (var childState in sm.states)
            {
                string name = childState.state.name;
                AnimationClip directClip = null;
                if (name == AnimStateNaming.StateIdle && _charSetupIdleClip != null)
                    directClip = _charSetupIdleClip;
                else if (!isStrafeLayer && name == AnimStateNaming.StateRun && _charSetupRunClip != null)
                    directClip = _charSetupRunClip;
                else if (!isStrafeLayer && name == AnimStateNaming.StateSprint && _charSetupRunFastClip != null)
                    directClip = _charSetupRunFastClip;

                if (directClip != null)
                {
                    childState.state.motion = directClip;
                    count++;
                }
                else
                {
                    if (childState.state.motion is BlendTree bt)
                        count += ReplaceClipsInBlendTree(bt, isStrafeLayer);
                }
            }

            foreach (var childSM in sm.stateMachines)
                count += ReplaceLocomotionClipsInSM(childSM.stateMachine, isStrafeLayer);

            return count;
        }

        int ReplaceClipsInBlendTree(BlendTree bt, bool isStrafeLayer = false)
        {
            int count = 0;
            var children = bt.children;

            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];

                if (child.motion is BlendTree subBt)
                {
                    string subName = subBt.name;
                    AnimationClip clip = null;

                    if (subName == AnimStateNaming.StateIdle && _charSetupIdleClip != null)
                        clip = _charSetupIdleClip;
                    else if (!isStrafeLayer && (subName == AnimStateNaming.StateRun || subName == AnimStateNaming.StateWalk) && _charSetupRunClip != null)
                        clip = _charSetupRunClip;
                    else if (!isStrafeLayer && subName == AnimStateNaming.StateSprint && _charSetupRunFastClip != null)
                        clip = _charSetupRunFastClip;

                    if (clip != null)
                    {
                        children[i].motion = clip;
                        count++;
                    }
                    else
                    {
                        count += ReplaceClipsInBlendTree(subBt, isStrafeLayer);
                    }
                }
                else if (child.motion is AnimationClip existingClip)
                {
                    string clipName = existingClip.name.ToLower();
                    string assetPath = AssetDatabase.GetAssetPath(existingClip).ToLower();
                    AnimationClip replacement = null;

                    if ((clipName.Contains("idle") || assetPath.Contains("idle")) && _charSetupIdleClip != null)
                        replacement = _charSetupIdleClip;
                    else if (!isStrafeLayer)
                    {
                        if ((clipName.Contains("sprint") || clipName.Contains("run_fast") ||
                             assetPath.Contains("sprint") || assetPath.Contains("run_fast")) && _charSetupRunFastClip != null)
                            replacement = _charSetupRunFastClip;
                        else if ((clipName.Contains("run") || assetPath.Contains("run.")) && _charSetupRunClip != null)
                            replacement = _charSetupRunClip;
                    }

                    if (replacement != null)
                    {
                        children[i].motion = replacement;
                        count++;
                    }
                }
            }

            if (count > 0)
                bt.children = children;

            return count;
        }

        void ReplaceCharSetupMotionByName(AnimatorController controller, string stateName, AnimationClip clip)
        {
            foreach (var layer in controller.layers)
                ReplaceCharSetupMotionInSM(layer.stateMachine, stateName, clip);
        }

        void ReplaceCharSetupMotionInSM(AnimatorStateMachine sm, string targetName, AnimationClip clip)
        {
            foreach (var childState in sm.states)
            {
                if (childState.state.name == targetName)
                    childState.state.motion = clip;
            }
            foreach (var childSM in sm.stateMachines)
                ReplaceCharSetupMotionInSM(childSM.stateMachine, targetName, clip);
        }

        bool ClearCharSetupMotionByName(AnimatorController controller, string stateName)
        {
            bool cleared = false;
            foreach (var layer in controller.layers)
            {
                if (ClearCharSetupMotionInSM(layer.stateMachine, stateName))
                    cleared = true;
            }
            return cleared;
        }

        bool ClearCharSetupMotionInSM(AnimatorStateMachine sm, string targetName)
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
                if (ClearCharSetupMotionInSM(childSM.stateMachine, targetName))
                    found = true;
            }
            return found;
        }

        AnimationClip LoadFirstClipFromFBX(string fbxPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            return null;
        }

        void CharSetupLog(string msg) => SetupLog(_charSetupLogMessages, "[CharSetup]", msg);
    }
}
#endif