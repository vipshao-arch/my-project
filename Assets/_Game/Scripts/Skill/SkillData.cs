using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.SkillSystem
{
    [CreateAssetMenu(fileName = "NewSkill", menuName = "Game/Skill Data")]
    public class SkillData : ScriptableObject
    {
        [HideInInspector] public int assetVersion = 3;

        [Header("基础信息")]
        [Tooltip("技能名称")]
        public string skillName = "New Skill";
        [Tooltip("技能 ID（唯一标识，用于技能系统索引）")]
        public int skillId;
        [Tooltip("技能图标（UI 显示用）")]
        public Sprite icon;
        [Tooltip("技能分类：基础攻击 / 主动技能")]
        public SkillCategory category = SkillCategory.Skill;

        [Header("时间参数")]
        [Tooltip("冷却时间（秒）")]
        public float cooldown = 5f;
        [Tooltip("施法前摇时间（秒）。技能从按下到生效的等待时间")]
        public float castTime;
        [Tooltip("持续施法时长（秒）。仅持续施法类技能使用")]
        public float channelDuration;

        [Header("动画窗口")]
        [Range(0f, 1f)][Tooltip("前摇比例（0~1）。技能动画开始到伤害判定的时间比例")]
        public float frontSwing = 0.1f;
        [Range(0f, 1f)][Tooltip("后摇比例（0~1）。伤害判定到动画结束的时间比例")]
        public float backSwing = 0.3f;

        [Header("动画融合")]
        [Tooltip("动画淡入时间（秒）。切换到技能动画时的过渡时长")]
        public float fadeInDuration = 0.1f;
        [Tooltip("动画淡出时间（秒）。技能动画结束回到待机时的过渡时长")]
        public float fadeOutDuration = 0.3f;

        [Header("动画身份")]
        [Tooltip("Animator 触发参数名（如 SkillQ/W/E/R/Attack）")]
        public string animTrigger = "SkillQ";
        [Tooltip("动画状态 ID（备用，通常由 animTrigger 自动推导）")]
        public int animStateId = 1;
        [Tooltip("动画 Clip 名称（保持为空则自动推导）")]
        public string animClipName = "";
        [Tooltip("动画 Clip 数组。优先级最高，直接指定播放哪个 Clip")]
        public AnimationClip[] animClips = Array.Empty<AnimationClip>();

        [Header("多段配置")]
        [Tooltip("多段技能配置。每段可独立配置动画/VFX/SFX/伤害")]
        public SkillMultiStageConfig multiStage = new SkillMultiStageConfig();

        [Header("行为节点")]
        [Tooltip("技能行为节点链。技能的所有判定/伤害/VFX/SFX/移动行为由此驱动")]
        [SerializeReference] public List<SkillNodeData> graphData = new List<SkillNodeData>();

        [Header("已知动画")]
        [Tooltip("已知的动画信息（由 EnemySetup 面板自动填充）")]
        public List<KnownAnimationEntry> knownAnimations = new List<KnownAnimationEntry>();

        public SkillValidationReport ValidateForPreview() => Validate(false);
        public SkillValidationReport ValidateForRuntime() => Validate(true);
        public SkillValidationReport ValidateFull() => Validate(true);

        private SkillValidationReport Validate(bool runtime)
        {
            var report = new SkillValidationReport();
            if (string.IsNullOrWhiteSpace(skillName)) report.AddWarning("技能名称为空", "skillName");
            if (graphData == null || graphData.Count == 0)
                report.AddWarning("graphData 为空，技能不会产生行为", "graphData");
            bool hasClip = animClips != null && animClips.Length > 0 && animClips[0] != null;
            if (!hasClip && string.IsNullOrWhiteSpace(animTrigger) && string.IsNullOrWhiteSpace(animClipName))
            {
                if (runtime) report.AddError("未设置动画来源", "animClips");
                else report.AddWarning("未设置动画来源", "animClips");
            }
            if (cooldown < 0f) report.AddWarning("冷却时间不能为负数", "cooldown");
            if (frontSwing < 0f || frontSwing > 1f) report.AddWarning("前摇比例超出 0~1", "frontSwing");
            if (backSwing < 0f || backSwing > 1f) report.AddWarning("后摇比例超出 0~1", "backSwing");
            return report;
        }

        public static SkillData CreateBasicAttack()
        {
            var data = CreateInstance<SkillData>();
            data.category = SkillCategory.BasicAttack;
            return data;
        }

        public static SkillData CreateActiveSkill()
        {
            var data = CreateInstance<SkillData>();
            data.category = SkillCategory.Skill;
            return data;
        }

        public static float FrontSwingSeconds(float clipLength, SkillData data)
        {
            return data == null || clipLength <= 0f ? 0f : clipLength * Mathf.Clamp01(data.frontSwing);
        }

        public static float BackSwingStartSeconds(float clipLength, SkillData data, float frontSwingSeconds)
        {
            if (data == null || clipLength <= 0f) return clipLength * 0.7f;
            return Mathf.Max(clipLength - clipLength * Mathf.Clamp01(data.backSwing), frontSwingSeconds + 0.05f);
        }

        public static float GetDamageFromGraph(SkillData data)
        {
            if (data?.graphData == null) return 0f;
            foreach (var node in data.graphData)
            {
                float damage = GetNodeDamage(node);
                if (damage > 0f) return damage;
            }
            return 0f;
        }

        public static (GameObject prefab, AudioClip sfx, string spawnBone, bool worldSpace,
                       Vector3 positionOffset, Vector3 rotationOffset, float scale, float destroyAfterSeconds,
                       float sfxPitchRandomPercent) GetHitVFXFromGraph(SkillData data)
        {
            if (data?.graphData == null) return default;
            foreach (var node in data.graphData)
            {
                if (node is HitVFXData hit)
                    return (hit.prefab, hit.sfx, hit.spawnBone, hit.worldSpace, hit.positionOffset,
                            hit.rotationOffset, hit.scale, hit.destroyAfterSeconds, hit.sfxPitchRandomPercent);
            }
            return default;
        }

        public static (float range, float hitRadius, float hitAngle) GetMeleeParamsFromGraph(SkillData data)
        {
            if (data?.graphData != null)
                foreach (var node in data.graphData)
                    if (node is MeleeSwingData melee) return (melee.range, melee.hitRadius, melee.hitAngle);
            return (0f, 0f, 0f);
        }

        private static float GetNodeDamage(SkillNodeData node)
        {
            if (node is MeleeSwingData melee) return melee.damage;
            if (node is RectShotData shot) return shot.damage;
            if (node is BeamData beam) return beam.damage;
            if (node is ChainBounceData chain) return chain.damage;
            if (node is CurvedProjectileData curved) return curved.damage;
            if (node is AOECircularData aoe) return aoe.damage;
            if (node is ChanneledData channel) return channel.tickAmount; // D6: 生成器写入 tickAmount 而非 damage
            return 0f;
        }
    }

    public enum SkillCategory { Skill, BasicAttack }
    public enum ShotShape { Rect, Line, Cone }
    public enum ShotHitMode { Stop, Pierce, PierceAll }
    public enum MovementPolicy { FullMove, SlowMove, Locked }
    public enum FacingMode { Free, Cursor, CursorDirection = 1, Skill = 2 }
    public enum EffectType { None, AreaOfEffect, Projectile, RectShot, ChainBounce, CurvedProjectile, Beam, AOECircular, Trap, Wall, Summon, Channeled, SelfBuff }

    [Serializable]
    public class VFXEntry
    {
        [Tooltip("VFX Prefab")]
        public GameObject prefab;
        [Tooltip("触发时间（秒）")]
        public float triggerTime;
        [Tooltip("挂点骨骼名（如 RightHand / Bip001 Spine）")]
        public string spawnBone;
        [Tooltip("是否在世界空间播放")]
        public bool worldSpace = true;
        [Tooltip("位置偏移")]
        public Vector3 positionOffset;
        [Tooltip("旋转偏移")]
        public Vector3 rotationOffset;
        [Tooltip("缩放倍率")]
        public float scale = 1f;
        [Tooltip("自动销毁时间（秒）。0 = 不自动销毁")]
        public float destroyAfterSeconds;
        [NonSerialized] internal bool fired;
    }

    [Serializable]
    public class SkillMultiStageSegment
    {
        [Tooltip("阶段名称（便于识别）")]
        public string stageName = "Stage";
        [Tooltip("该段的动画 Clip")]
        public AnimationClip animClip;
        [Range(0.1f, 4f)][Tooltip("该段的动画播放速度倍率")]
        public float animSpeed = 1f;
        [Tooltip("该段的释放 VFX")]
        public GameObject vfxOnCast;
        [Tooltip("该段的释放音效")]
        public AudioClip sfxOnCast;
        [Tooltip("该段的命中 VFX")]
        public GameObject vfxOnHit;
        [Tooltip("该段的命中音效")]
        public AudioClip sfxOnHit;
        [Range(0f, 1f)][Tooltip("命中判定时机（归一化时间 0~1）")]
        public float hitAtNormalized;
        [Tooltip("该段是否造成伤害")]
        public bool dealsDamage = true;
        [Tooltip("该段的伤害倍率（相对于基础伤害）")]
        public float damageMultiplier = 1f;
    }

    [Serializable]
    public class SkillMultiStageConfig
    {
        [Tooltip("是否启用多段技能")]
        public bool enabled;
        [Range(1, 16)][Tooltip("总段数")]
        public int stageCount = 1;
        [Tooltip("段间间隔（秒）")]
        public float stageInterval = 0.3f;
        [Tooltip("各段配置列表")]
        public List<SkillMultiStageSegment> segments = new List<SkillMultiStageSegment>();
    }

    [Serializable]
    public struct KnownAnimationEntry
    {
        [Tooltip("动画分类（如 Idle/Walk/Attack/Skill）")]
        public string category;
        [Tooltip("子标识（如 Attack01/SkillQ）")]
        public string subId;
        [Tooltip("显示名称")]
        public string displayName;
        [Tooltip("动画 Clip 引用")]
        public AnimationClip clip;
    }
}
