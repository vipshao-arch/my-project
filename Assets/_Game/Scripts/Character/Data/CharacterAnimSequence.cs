using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 单个动作的动画序列描述：支持多段主动作 + 衔接(起手/收招) + 过渡参数。
    /// 与 <see cref="CharacterAnimSetAsset"/> 配合使用，构成一个角色的完整动画配置。
    /// </summary>
    [Serializable]
    public class CharacterAnimSequence
    {
        [Tooltip("动作 ID（也是 Animator State 名），例如 Attack_1, Skill_Q。留空时自动生成")]
        public string id = "";

        [Header("衔接动画（可选）")]
        [Tooltip("进入主动作前播放的起手衔接动画")]
        public AnimationClip leadIn;

        [Tooltip("主动作结束后播放的收招衔接动画")]
        public AnimationClip leadOut;

        [Tooltip("衔接动画(起手/收招)与主体之间的过渡时长（秒）")]
        [Range(0f, 0.5f)]
        public float leadTransitionDuration = 0.05f;

        [Header("主动作多段动画")]
        [Tooltip("是否启用多段主动画（关闭时只使用第一段）")]
        public bool useMultiSegment = false;

        [Tooltip("主动作的多段动画片段，按顺序播放，段间自动插入 Transition")]
        public List<AnimationClip> segments = new List<AnimationClip>();

        [Tooltip("主动作多段之间的过渡时长（秒）")]
        [Range(0f, 0.5f)]
        public float segmentTransitionDuration = 0.08f;

        [Tooltip("是否允许被 AnyState 从自身打断（用于连击）")]
        public bool canInterruptSelf = true;

        // ═══════════════════════════════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>获取指定索引的主段动画片段。</summary>
        public AnimationClip GetMainSegment(int idx)
            => (segments != null && idx >= 0 && idx < segments.Count) ? segments[idx] : null;

        /// <summary>是否有任何主段动画。</summary>
        public bool HasAnySegment()
        {
            if (segments == null) return false;
            for (int i = 0; i < segments.Count; i++)
                if (segments[i] != null) return true;
            return false;
        }

        /// <summary>第一个非空主段动画（兜底用）。</summary>
        public AnimationClip GetFirstValidSegment()
        {
            if (segments == null) return null;
            for (int i = 0; i < segments.Count; i++)
                if (segments[i] != null) return segments[i];
            return null;
        }

        /// <summary>从另一个序列深拷贝所有字段。</summary>
        public void CopyFrom(CharacterAnimSequence other)
        {
            if (other == null) return;
            id = other.id;
            leadIn = other.leadIn;
            leadOut = other.leadOut;
            leadTransitionDuration = other.leadTransitionDuration;
            useMultiSegment = other.useMultiSegment;
            segmentTransitionDuration = other.segmentTransitionDuration;
            canInterruptSelf = other.canInterruptSelf;

            segments = new List<AnimationClip>();
            if (other.segments != null)
            {
                foreach (var s in other.segments)
                    segments.Add(s);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // 通用过渡动画数据
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 一条跨状态过渡定义（如 Idle↔Move）。
    /// 根据过渡类型在 AnimatorController 中生成对应的 Blend Tree 或 Transition。
    /// </summary>
    [Serializable]
    public class BlendTransitionData
    {
        public enum TransitionType
        {
            /// <summary>单向：From → To（如 Move → Idle）</summary>
            OneWay,
            /// <summary>双向：From ↔ To 均生成过渡</summary>
            TwoWay,
            /// <summary>任意状态 → To（多段技能每段结束后回待机）</summary>
            AnyStateToTarget
        }

        [Tooltip("过渡类型")]
        public TransitionType type = TransitionType.OneWay;

        [Tooltip("源状态名（如 Move，OneWay/TwoWay 时必填）")]
        public string fromState;

        [Tooltip("目标状态名（如 Idle）")]
        public string toState;

        [Tooltip("源状态使用的动画片段")]
        public AnimationClip fromClip;

        [Tooltip("目标状态使用的动画片段")]
        public AnimationClip toClip;

        [Tooltip("过渡动画片段（独立的过渡动画，如专门的待机→移动过渡动画）")]
        public AnimationClip transitionClip;

        [Tooltip("切入过渡动画的 Blend 时长（秒）——源状态 → 过渡动画")]
        [Range(0f, 0.5f)]
        public float blendInDuration = 0.05f;

        [Tooltip("切出过渡动画的 Blend 时长（秒）——过渡动画 → 目标状态")]
        [Range(0f, 0.5f)]
        public float blendOutDuration = 0.1f;

        [Tooltip("是否有退出时间条件")]
        public bool hasExitTime = true;

        [Tooltip("退出时间归一化值（0~1）")]
        [Range(0f, 1f)]
        public float exitTime = 0.95f;

        /// <summary>生成 AnimatorState 名称（用于查找/创建）。</summary>
        public string FromStateName => string.IsNullOrEmpty(fromState) ? "From" : fromState;
        public string ToStateName => string.IsNullOrEmpty(toState) ? "To" : toState;
    }
}

