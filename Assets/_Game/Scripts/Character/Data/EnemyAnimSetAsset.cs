using System.Collections.Generic;
using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 敌人动画序列集合资产（ScriptableObject）。
    ///
    /// 用途：把敌人的攻击动画 + 主动技能动画 + 过渡动画集中管理，
    /// 供 Enemy Setup 流水线 Part 2 使用。
    ///
    /// 存放约定：
    ///   - 通用模板：Assets/_Game/character/Common/DefaultEnemyAnimSet.asset
    ///   - 敌人覆盖：Assets/_Game/character/{敌人名}/AnimSet_{敌人名}.asset
    ///
    /// 使用方式：
    ///   1. Character Kit → Enemy Setup → Part 2 自动加载 DefaultEnemyAnimSet
    ///   2. 拖入自己的 Enemy Anim Set 资产替换模板
    ///   3. 修改后保存到敌人目录
    ///   4. ExecuteEnemySetupPart2 从本资产读取动画并构建 AnimatorController
    /// </summary>
    [CreateAssetMenu(fileName = "NewEnemyAnimSet", menuName = "Game/Character System/Enemy Anim Set")]
    public class EnemyAnimSetAsset : ScriptableObject
    {
        [Header("攻击动画")]
        [Tooltip("攻击动画列表（Attack_1~Attack_6），索引 i 对应 Animator 中的 Attack_{i+1} 状态名。\n" +
                 "留空的条目在 Part 2 执行时会被跳过。")]
        public List<AnimationClip> attackClips = new List<AnimationClip>();

        [Header("主动技能动画")]
        [Tooltip("敌人主动技能动画组（Skill_Q / W / E / R / 5~8），每组可含多段 + 衔接。")]
        public List<CharacterAnimSequence> skills = new List<CharacterAnimSequence>();

        [Header("通用过渡动画组")]
        [Tooltip("跨状态过渡定义（如 Idle↔Move、任意状态→Idle）。")]
        public List<BlendTransitionData> blendTransitions = new List<BlendTransitionData>();

        // ═══════════════════════════════════════════════════════════════
        // 查询辅助
        // ═══════════════════════════════════════════════════════════════

        /// <summary>是否有任何非空 Clip。</summary>
        public bool HasAnyClip()
        {
            if (attackClips != null)
                foreach (var c in attackClips)
                    if (c != null) return true;
            if (skills != null)
                foreach (var s in skills)
                    if (s != null && s.HasAnySegment()) return true;
            return false;
        }

        /// <summary>统计已填充的 Clip 总数（攻击 + 技能主段）。</summary>
        public int CountFilledClips()
        {
            int count = 0;
            if (attackClips != null)
                foreach (var c in attackClips)
                    if (c != null) count++;
            if (skills != null)
                foreach (var s in skills)
                    if (s?.segments != null)
                        foreach (var seg in s.segments)
                            if (seg != null) count++;
            return count;
        }

        /// <summary>统计已填充的过渡数。</summary>
        public int CountFilledTransitions()
        {
            int count = 0;
            if (blendTransitions != null)
                foreach (var t in blendTransitions)
                    if (t != null && !string.IsNullOrEmpty(t.toState)) count++;
            return count;
        }

        // ═══════════════════════════════════════════════════════════════
        // 深拷贝
        // ═══════════════════════════════════════════════════════════════

        /// <summary>从另一个 EnemyAnimSetAsset 浅拷贝所有引用。</summary>
        public void CopyFrom(EnemyAnimSetAsset template)
        {
            if (template == null) return;
            name = template.name;
            attackClips = new List<AnimationClip>(template.attackClips ?? new List<AnimationClip>());

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
        }
    }
}
