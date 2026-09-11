using System.Collections.Generic;
using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 角色动画序列集合资产（ScriptableObject）。
    ///
    /// 用途：把一个角色的所有攻击动画组 + 技能动画组集中管理，
    /// 支持多段动画 + 衔接(起手/收招) + 过渡参数配置。
    ///
    /// 存放约定：
    ///   - 通用模板：Assets/_Game/character/Common/DefaultCharacterAnimSet.asset
    ///   - 角色覆盖：Assets/_Game/character/{角色名}/AnimSet_{角色名}.asset
    ///
    /// 使用方式：
    ///   1. Character Kit → Setup Tab → Step 1(Character Prefab Setup) 自动加载 DefaultCharacterAnimSet
    ///   2. 拖入自己的 AnimSet 资产替换模板
    ///   3. 修改后保存到角色目录
    ///   4. Character Prefab Setup 从本资产读取多段+衔接并构建 AnimatorController
    /// </summary>
    [CreateAssetMenu(fileName = "NewAnimSet", menuName = "Game/Character System/Character Anim Set")]
    public class CharacterAnimSetAsset : ScriptableObject
    {
        [Header("攻击动画组")]
        [Tooltip("攻击动作序列组（Attack_1 ~ Attack_N），每组可含多段 + 衔接")]
        public List<CharacterAnimSequence> attacks = new List<CharacterAnimSequence>();

        [Header("技能动画组")]
        [Tooltip("技能动作序列组（Skill_Q / W / E / R / 5~8），每组可含多段 + 衔接")]
        public List<CharacterAnimSequence> skills = new List<CharacterAnimSequence>();

        [Header("通用过渡动画组")]
        [Tooltip("跨状态过渡定义（如 Idle↔Move、任意状态→Idle）")]
        public List<BlendTransitionData> blendTransitions = new List<BlendTransitionData>();

        [Header("运动状态名配置")]
        [Tooltip("NormalState 层状态名（Idle/Jump/Roll 等）。改名必须与 Animator Controller 实际状态一致；默认名兼容现有 Controller。")]
        public LocomotionStateNames locomotionStates = new LocomotionStateNames();

        // ═══════════════════════════════════════════════════════════════
        // 深拷贝（从模板克隆）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>从另一个 CharacterAnimSetAsset 深拷贝所有字段。</summary>
        public void CopyFrom(CharacterAnimSetAsset template)
        {
            if (template == null) return;
            name = template.name;

            attacks = new List<CharacterAnimSequence>();
            if (template.attacks != null)
            {
                foreach (var src in template.attacks)
                {
                    if (src == null)
                    {
                        attacks.Add(null);
                        continue;
                    }

                    var clone = new CharacterAnimSequence();
                    clone.CopyFrom(src);
                    attacks.Add(clone);
                }
            }

            skills = new List<CharacterAnimSequence>();
            if (template.skills != null)
            {
                foreach (var src in template.skills)
                {
                    if (src == null)
                    {
                        skills.Add(null);
                        continue;
                    }

                    var clone = new CharacterAnimSequence();
                    clone.CopyFrom(src);
                    skills.Add(clone);
                }
            }

            blendTransitions = new List<BlendTransitionData>();
            if (template.blendTransitions != null)
            {
                foreach (var src in template.blendTransitions)
                {
                    if (src == null)
                    {
                        blendTransitions.Add(null);
                        continue;
                    }

                    blendTransitions.Add(new BlendTransitionData
                    {
                        type = src.type,
                        fromState = src.fromState,
                        toState = src.toState,
                        fromClip = src.fromClip,
                        toClip = src.toClip,
                        transitionClip = src.transitionClip,
                        blendInDuration = src.blendInDuration,
                        blendOutDuration = src.blendOutDuration,
                        hasExitTime = src.hasExitTime,
                        exitTime = src.exitTime
                    });
                }
            }

            if (template.locomotionStates != null)
                locomotionStates.CopyFrom(template.locomotionStates);
        }

        // ═══════════════════════════════════════════════════════════════
        // 查询辅助
        // ═══════════════════════════════════════════════════════════════

        /// <summary>攻击组 + 技能组中是否有任何非空 Clip。</summary>
        public bool HasAnyClip()
        {
            foreach (var seq in attacks)
                if (seq != null && seq.HasAnySegment()) return true;
            foreach (var seq in skills)
                if (seq != null && seq.HasAnySegment()) return true;
            return false;
        }

        /// <summary>统计所有已填充的 Clip 总数。</summary>
        public int CountFilledClips()
        {
            int count = 0;
            foreach (var seq in attacks)
            {
                if (seq?.segments == null) continue;
                foreach (var s in seq.segments) if (s != null) count++;
            }
            foreach (var seq in skills)
            {
                if (seq?.segments == null) continue;
                foreach (var s in seq.segments) if (s != null) count++;
            }
            return count;
        }

        /// <summary>统计所有已填充序列组数（含任意非空段的组）。</summary>
        public int CountFilledSequences()
        {
            int count = 0;
            foreach (var seq in attacks)
                if (seq != null && seq.HasAnySegment()) count++;
            foreach (var seq in skills)
                if (seq != null && seq.HasAnySegment()) count++;
            return count;
        }
    }
}
