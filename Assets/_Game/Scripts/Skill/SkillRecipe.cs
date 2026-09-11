// 2026-07-21:BuildNodes 仅作 Editor 调试/老资产兼容,有意引用废弃 SO 节点,豁免 CS0618。
#pragma warning disable 0618

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Game.SkillSystem
{
    /// <summary>
    /// 技能配方 — 描述一个技能的分类特征组合。
    ///
    /// 设计理念：
    ///   技能 = 分类特征 × 节点组合
    ///   用户通过逐层选择分类特征（射程/范围/弹体/多段/Buff），
    ///   SkillBuilderWizard 根据这些特征自动生成对应的 SkillNodeData 组合写入 SkillData.graphData。
    ///
    /// 5 个分类维度：
    ///   ① 射程类型   — 近程(Melee) / 远程(Ranged)
    ///   ② 范围类型   — 单体(Single) / 范围(AOE) / 扇形(Cone) / 直线(Line)
    ///   ③ 弹体类型   — 无(Instant) / 直线飞行(Projectile) / 抛物线(Curved) / 光束(Beam) / 链式(Chain)
    ///   ④ 多段释放   — 否(Single) / 是(MultiHit, 多段伤害) / 持续(Channel, DOT/HOT)
    ///   ⑤ Buff 效果  — 无(None) / 自身增益(SelfBuff) / 目标减益(Debuff)
    /// </summary>

    #region 分类枚举

    /// <summary>① 射程类型</summary>
    public enum SkillRange
    {
        /// <summary>近程：角色前方近距离判定（0~3m）</summary>
        Melee,
        /// <summary>远程：发射弹体或光束到达远处（3m+）</summary>
        Ranged,
    }

    /// <summary>② 范围类型</summary>
    public enum SkillTargeting
    {
        /// <summary>单体：只命中一个目标</summary>
        Single,
        /// <summary>范围：圆形 AOE 命中范围内所有目标</summary>
        AOE,
        /// <summary>扇形：前方扇形角度内所有目标</summary>
        Cone,
        /// <summary>直线：前方窄条/矩形内所有目标</summary>
        Line,
    }

    /// <summary>③ 弹体类型</summary>
    public enum SkillProjectile
    {
        /// <summary>即时：无飞行体，前摇结束瞬间判定</summary>
        Instant,
        /// <summary>直线飞行：投射物沿直线飞出</summary>
        Projectile,
        /// <summary>抛物线：受重力影响的弧线弹体</summary>
        Curved,
        /// <summary>光束：瞬时射线扫掠</summary>
        Beam,
        /// <summary>链式：命中后弹射到下一个目标</summary>
        Chain,
    }

    /// <summary>④ 多段释放</summary>
    public enum SkillMultiHit
    {
        /// <summary>单次：一次命中结算</summary>
        Single,
        /// <summary>多段：连续 N 次命中，每次有间隔</summary>
        MultiHit,
        /// <summary>持续：按住持续施法，按周期 tick</summary>
        Channel,
    }

    /// <summary>⑤ Buff 效果</summary>
    public enum SkillBuffType
    {
        /// <summary>无 Buff</summary>
        None,
        /// <summary>自身增益（攻击力提升/护盾/加速等）</summary>
        SelfBuff,
        /// <summary>目标减益（中毒/减速/破甲等）</summary>
        Debuff,
    }

    #endregion

    /// <summary>
    /// 技能配方：用户在向导中选择的分类特征组合。
    /// 向导根据此配方生成 SkillNodeData POCO 组合。
    /// </summary>
    [Serializable]
    public class SkillRecipe
    {
        [Header("0 技能分类(决定走 Q/W/E/R 槽还是普攻槽)")]
        [Tooltip("• Skill = 主动技能(Q/W/E/R,受冷却约束)\n• BasicAttack = 普攻(左键/自动攻击)")]
        public SkillCategory category = SkillCategory.Skill;

        [Header("① 射程类型")]
        public SkillRange range = SkillRange.Melee;

        [Header("② 范围类型")]
        public SkillTargeting targeting = SkillTargeting.Single;

        [Header("③ 弹体类型")]
        [Tooltip("近程 + 即时 = 近战挥击\n远程 + Projectile = 直线弹体\n远程 + Curved = 抛物线\n远程 + Beam = 光束\n远程 + Chain = 链式弹射")]
        public SkillProjectile projectile = SkillProjectile.Instant;

        [Header("④ 多段释放")]
        [Tooltip("Single=单次命中\nMultiHit=连续多段（N次伤害间隔触发）\nChannel=持续施法（按周期tick伤害）")]
        public SkillMultiHit multiHit = SkillMultiHit.Single;

        [Header("⑤ Buff 效果")]
        [Tooltip("None=无\nSelfBuff=给自己加增益\nDebuff=给目标加减益")]
        public SkillBuffType buff = SkillBuffType.None;

        // ── 数值参数（向导第 3 步填写） ──

        [Header("基础信息(对应 SkillData ①)")]
        public int skillId = 0;
        public Sprite icon;

        [Header("时间参数(对应 SkillData ②)")]
        public float cooldown = 5f;
        [Tooltip("施法吟唱时间(秒),0 = 瞬发")]
        public float castTime = 0f;

        [Header("动画窗口(对应 SkillData ③)")]
        public float frontSwing = 0.35f;
        public float backSwing = 0.3f;

        [Header("动画融合(对应 SkillData ④)")]
        public float fadeInDuration = 0.1f;
        public float fadeOutDuration = 0.25f;

        [Header("数值参数(写入 graphData 判定节点)")]
        public float damage = 15f;
        public float rangeMeters = 2f;
        public float hitRadius = 1.5f;
        public float hitAngle = 60f;

        [Header("弹体参数（远程类用）")]
        public float projectileSpeed = 15f;
        public int projectileCount = 1;
        public float projectileSpread = 0f;
        public float projectileLifetime = 2f;
        public GameObject projectilePrefab;

        [Header("多段参数（MultiHit/Channel 用）")]
        [Tooltip("MultiHit 模式：段数\nChannel 模式：tick 次数")]
        public int hitCount = 3;
        [Tooltip("MultiHit 模式：每段间隔（秒）\nChannel 模式：tick 间隔（秒）")]
        public float hitInterval = 0.3f;

        [Header("Buff 参数")]
        [Tooltip("Buff 效果资产（StatusEffect SO）。2026-07-21 修复:此前配方生成的 StatusEffectData.effect 永远为 null,运行时必跳过。")]
        public StatusEffect buffEffect;
        [Tooltip("Buff 持续时间（秒）— 参考值,实际时长以 buffEffect 资产为准")]
        public float buffDuration = 5f;
        [Tooltip("Buff 数值（如攻击力+20%填20）— 参考值,实际数值以 buffEffect 资产为准")]
        public float buffValue = 20f;

        [Header("VFX")]
        public GameObject vfxOnCast;
        public GameObject vfxOnHit;
        public string vfxCastBone = "VFX_BladeTip";
        public AudioClip sfxOnCast;
        public AudioClip sfxOnHit;

        [Header("动画")]
        public string animTrigger = "SkillQ";
        public string animClipName = "Skill_Q";
        public AnimationClip animClip;
        public int animStateId = 1;

        [Header("多段动画与表现")]
        [Tooltip("MultiHit/Channel 每段独立配置。段数变化时由 Step2 自动补齐。")]
        public List<SkillMultiStageSegment> stages = new List<SkillMultiStageSegment>();

        [Header("输出")]
        [Tooltip("生成的 .asset 文件名（不含扩展名）")]
        public string skillName = "New Skill";
        [Tooltip("输出目录（相对 Assets/）")]
        public string outputFolder = "_Game/SkillData";
    }

    /// <summary>
    /// 配方 → 节点组合生成器。
    /// 根据 SkillRecipe 的分类特征选择，生成对应的 SkillNodeData 组合。
    /// </summary>
    public static class SkillRecipeBuilder
    {
        /// <summary>
        /// 根据 recipe 生成完整的 SkillNodeData POCO 列表（新格式 graphData 用）。
        /// 与 BuildNodes 对齐但直接生成 POCO,无需 ScriptableObject 中转,没有子资产。
        /// 生成顺序：CastVFX → 判定节点( Melee/Shot/Beam/Chain/Curved ) → HitVFX → Channeled → Buff → Movement
        /// </summary>
        public static List<SkillNodeData> BuildDataList(SkillRecipe recipe)
        {
            var datas = new List<SkillNodeData>();
            bool isMultiHit = recipe.multiHit == SkillMultiHit.MultiHit;

            // ── 起手 VFX 节点 ──
            // 多段开启时，全局 VFX/SFX 合并到第一段 MultiStageLayerData，不独立生成全局节点
            if (!isMultiHit && (recipe.vfxOnCast != null || recipe.sfxOnCast != null))
            {
                var d = new CastVFXData
                {
                    prefab     = recipe.vfxOnCast,
                    sfx        = recipe.sfxOnCast,
                    spawnBone  = recipe.vfxCastBone,
                    worldSpace = false,
                    triggerTime = 0f,
                };
                datas.Add(d);
            }

            // ── 判定节点（根据射程+弹体+范围组合选择） ──
            var hitData = BuildHitData(recipe);
            if (hitData != null)
            {
                datas.Add(hitData);

                // 2026-07-21 修复:MultiHit(连续多段)此前无节点产物。
                // 生成方式:复制判定节点 N-1 份,triggerTime 按 hitInterval 递增
                // (相对偏移,前摇秒数由 GenerateSkillData 统一回写)。
                // SkillRunner 的 timeline impact 按节点索引独立触发,天然支持多段。
                if (isMultiHit && recipe.hitCount > 1)
                {
                    for (int seg = 1; seg < recipe.hitCount; seg++)
                    {
                        var dup = DuplicateHitData(hitData, seg * recipe.hitInterval);
                        if (dup != null) datas.Add(dup);
                    }
                }
            }

            // ── 命中 VFX 节点 ──
            // 多段开启时，全局命中 VFX/SFX 合并到第一段 HitVFXData，不独立生成
            if (!isMultiHit && (recipe.vfxOnHit != null || recipe.sfxOnHit != null))
            {
                datas.Add(new HitVFXData
                {
                    prefab = recipe.vfxOnHit,
                    sfx    = recipe.sfxOnHit,
                });
            }

            // ── 多段动画与表现层 ──
            if (isMultiHit && recipe.stages != null)
            {
                for (int i = 0; i < recipe.stages.Count; i++)
                {
                    var stage = recipe.stages[i];
                    float stageTime = i * Mathf.Max(0f, recipe.hitInterval);

                    // 第一段：合并全局 VFX/SFX（若段未单独配置）
                    GameObject stageVfxOnCast = stage.vfxOnCast;
                    AudioClip  stageSfxOnCast = stage.sfxOnCast;
                    GameObject stageVfxOnHit  = stage.vfxOnHit;
                    AudioClip  stageSfxOnHit  = stage.sfxOnHit;
                    if (i == 0)
                    {
                        if (stageVfxOnCast == null) stageVfxOnCast = recipe.vfxOnCast;
                        if (stageSfxOnCast == null) stageSfxOnCast = recipe.sfxOnCast;
                        if (stageVfxOnHit  == null) stageVfxOnHit  = recipe.vfxOnHit;
                        if (stageSfxOnHit  == null) stageSfxOnHit  = recipe.sfxOnHit;
                    }

                    if (stage.animClip != null)
                    {
                        datas.Add(new MultiStageLayerData
                        {
                            stageName = string.IsNullOrEmpty(stage.stageName) ? $"第{i + 1}段" : stage.stageName,
                            animClip = stage.animClip,
                            triggerTime = stageTime,
                            animSpeed = Mathf.Clamp(stage.animSpeed, 0.1f, 4f),
                            vfxOnCast = stageVfxOnCast,
                            sfxOnCast = stageSfxOnCast,
                            hitAtNormalized = stage.hitAtNormalized,
                            dealsDamage = stage.dealsDamage,
                            damageMultiplier = stage.damageMultiplier,
                        });
                    }
                    if (stageVfxOnHit != null || stageSfxOnHit != null)
                    {
                        datas.Add(new HitVFXData
                        {
                            prefab = stageVfxOnHit,
                            sfx = stageSfxOnHit,
                            triggerTime = stageTime,
                        });
                    }
                }
            }

            // ── 持续施法表现层（Channel 模式） ──
            if (recipe.multiHit == SkillMultiHit.Channel && recipe.stages != null)
            {
                for (int i = 0; i < recipe.stages.Count; i++)
                {
                    var stage = recipe.stages[i];
                    if (stage.animClip == null) continue;
                    datas.Add(new MultiStageLayerData
                    {
                        stageName = string.IsNullOrEmpty(stage.stageName) ? $"第{i + 1}段" : stage.stageName,
                        animClip = stage.animClip,
                        triggerTime = i * Mathf.Max(0f, recipe.hitInterval),
                        animSpeed = Mathf.Clamp(stage.animSpeed, 0.1f, 4f),
                        vfxOnCast = stage.vfxOnCast,
                        sfxOnCast = stage.sfxOnCast,
                    });
                }
            }

            // ── 持续施法节点（Channel 模式） ──
            if (recipe.multiHit == SkillMultiHit.Channel)
            {
                datas.Add(new ChanneledData
                {
                    tickInterval     = recipe.hitInterval,
                    tickAmount       = recipe.damage,
                    durationOverride = recipe.hitInterval * recipe.hitCount,
                    lockMovement     = false,
                });
            }

            // ── Buff 节点（StatusEffectData） ──
            if (recipe.buff != SkillBuffType.None)
            {
                datas.Add(new StatusEffectData
                {
                    effect         = recipe.buffEffect,   // 2026-07-21 修复:此前未写入,运行时必跳过
                    applyToSelf   = recipe.buff == SkillBuffType.SelfBuff,
                    applyPerTarget = recipe.buff == SkillBuffType.Debuff,
                });
            }

            // ── 移动策略节点 ──
            datas.Add(new MovementData
            {
                policy         = recipe.range == SkillRange.Ranged ? MovementPolicy.SlowMove : MovementPolicy.FullMove,
                speedMultiplier = recipe.range == SkillRange.Ranged ? 0.5f : 1f,
                facingMode     = FacingMode.Cursor,
            });

            return datas;
        }

        /// <summary>
        /// 根据 recipe 生成完整的 SkillNodeData POCO 列表(主路径,BuildDataList 是它)。
        /// 保留 BuildNodes 仅作 Editor 调试/兼容老 .asset 显示用,运行时不再使用。
        /// 生成顺序：CastVFX → 判定节点( Melee/Shot/Beam/Chain/Curved ) → HitVFX → Channeled → Buff(预留)
        /// </summary>
        public static List<SkillNode> BuildNodes(SkillRecipe recipe)
        {
            var nodes = new List<SkillNode>();

            // ── 起手 VFX 节点 ──
            if (recipe.vfxOnCast != null || recipe.sfxOnCast != null)
            {
                var castVfx = ScriptableObject.CreateInstance<CastVFXNode>();
                castVfx.prefab = recipe.vfxOnCast;
                castVfx.sfx = recipe.sfxOnCast;
                castVfx.spawnBone = recipe.vfxCastBone;
                castVfx.worldSpace = false;
                castVfx.triggerTime = 0f;
                nodes.Add(castVfx);
            }

            // ── 判定节点（根据射程+弹体+范围组合选择） ──
            SkillNode hitNode = BuildHitNode(recipe);
            if (hitNode != null)
            {
                nodes.Add(hitNode);
            }

            // ── 命中 VFX 节点 ──
            if (recipe.vfxOnHit != null || recipe.sfxOnHit != null)
            {
                var hitVfx = ScriptableObject.CreateInstance<HitVFXNode>();
                hitVfx.prefab = recipe.vfxOnHit;
                hitVfx.sfx = recipe.sfxOnHit;
                nodes.Add(hitVfx);
            }

            // ── 持续施法节点（Channel 模式） ──
            if (recipe.multiHit == SkillMultiHit.Channel)
            {
                var channeled = ScriptableObject.CreateInstance<ChanneledNode>();
                channeled.tickInterval = recipe.hitInterval;
                channeled.tickAmount = recipe.damage;
                channeled.durationOverride = recipe.hitInterval * recipe.hitCount;
                channeled.lockMovement = false;
                nodes.Add(channeled);
            }

            // ── Buff 节点（StatusEffectNode） ──
            if (recipe.buff != SkillBuffType.None)
            {
                var buffNode = ScriptableObject.CreateInstance<StatusEffectNode>();
                buffNode.effect = recipe.buffEffect;        // D7: 此前漏写,effect=null 运行时静默失效
                buffNode.applyToSelf = recipe.buff == SkillBuffType.SelfBuff;
                buffNode.applyPerTarget = recipe.buff == SkillBuffType.Debuff;
                nodes.Add(buffNode);
            }

            // ── 移动策略节点 ──
            var moveNode = ScriptableObject.CreateInstance<MovementNode>();
            moveNode.policy = recipe.range == SkillRange.Ranged
                ? MovementPolicy.SlowMove : MovementPolicy.FullMove;
            moveNode.speedMultiplier = recipe.range == SkillRange.Ranged ? 0.5f : 1f;
            moveNode.facingMode = FacingMode.Cursor;
            nodes.Add(moveNode);

            return nodes;
        }

        /// <summary>
        /// 根据射程+弹体+范围组合，选择正确的判定节点 POCO 类型。
        /// </summary>
        private static SkillNodeData BuildHitData(SkillRecipe r)
        {
            // 近程 + 即时 = 近战挥击
            if (r.range == SkillRange.Melee && r.projectile == SkillProjectile.Instant)
            {
                return BuildMeleeData(r);
            }

            // 远程类：根据弹体类型选择
            switch (r.projectile)
            {
                case SkillProjectile.Projectile: return BuildProjectileData(r);
                case SkillProjectile.Curved:    return BuildCurvedData(r);
                case SkillProjectile.Beam:      return BuildBeamData(r);
                case SkillProjectile.Chain:     return BuildChainData(r);
                // 远程 + 即时 = AOE 在目标点爆炸
                case SkillProjectile.Instant:   return BuildAOEData(r);
            }
            return null;
        }

        // ── 近战挥击 (POCO) ──
        private static MeleeSwingData BuildMeleeData(SkillRecipe r) => new MeleeSwingData
        {
            range = r.rangeMeters,
            hitRadius = r.hitRadius,
            hitAngle = r.targeting switch
            {
                SkillTargeting.Single => 45f,
                SkillTargeting.Cone   => r.hitAngle,
                SkillTargeting.AOE    => 180f,
                SkillTargeting.Line   => 15f,
                _ => 60f,
            },
            damage = r.damage,
            originHeight = 0.8f,
        };

        // ── 直线弹体 (POCO) ──
        private static RectShotData BuildProjectileData(SkillRecipe r) => new RectShotData
        {
            shape = r.targeting switch
            {
                SkillTargeting.Line => ShotShape.Line,
                SkillTargeting.Cone => ShotShape.Cone,
                _ => ShotShape.Rect,
            },
            width = r.targeting == SkillTargeting.Line ? 0.3f : r.hitRadius,
            height = 1.8f,
            speed = r.projectileSpeed,
            maxRange = r.rangeMeters,
            count = r.projectileCount,
            spreadAngle = r.projectileSpread,
            hitMode = r.targeting == SkillTargeting.AOE ? ShotHitMode.PierceAll : ShotHitMode.Stop,
            spawnHeight = 0.9f,
            forwardOffset = 0.3f,
            projectilePrefab = r.projectilePrefab,
            projectileLifetime = r.projectileLifetime,
            damage = r.damage,          // P0-A1: 此前漏写，伤害输入被静默丢弃
        };

        // ── 抛物线弹体 (POCO) ──
        private static CurvedProjectileData BuildCurvedData(SkillRecipe r) => new CurvedProjectileData
        {
            width = r.hitRadius,
            height = 1.8f,
            speed = r.projectileSpeed,
            maxRange = r.rangeMeters,
            gravity = 12f,
            groundSnapY = 0.2f,
            launchPitch = 30f,
            spawnHeight = 0.9f,
            forwardOffset = 0.3f,
            projectilePrefab = r.projectilePrefab,
            projectileLifetime = r.projectileLifetime,
            damage = r.damage,          // P0-A2: 此前漏写，抛物线技能零伤害
        };

        // ── 光束 (POCO) ──
        private static BeamData BuildBeamData(SkillRecipe r) => new BeamData
        {
            beamWidth = r.targeting == SkillTargeting.Line ? 0.15f : r.hitRadius,
            range = r.rangeMeters,
            damage = r.damage,
            originHeight = 1f,
        };

        // ── 链式弹射 (POCO) ──
        private static ChainBounceData BuildChainData(SkillRecipe r) => new ChainBounceData
        {
            bounceCount = r.projectileCount > 1 ? r.projectileCount : 4,
            searchRadius = r.rangeMeters,
            damageFalloff = 0.7f,
            maxBounceRange = r.rangeMeters * 2f,
            damage = r.damage,
        };

        // ── AOE (POCO) ──
        private static AOECircularData BuildAOEData(SkillRecipe r) => new AOECircularData
        {
            radius = r.hitRadius,
            centerIsTargetPoint = r.range == SkillRange.Ranged,
            damage = r.damage,
        };

        /// <summary>
        /// 复制判定节点并把 triggerTime 偏移 offset 秒(MultiHit 多段用,2026-07-21)。
        /// JsonUtility 深拷贝 POCO;仅支持带 triggerTime 字段的判定类节点。
        /// </summary>
        private static SkillNodeData DuplicateHitData(SkillNodeData src, float triggerTimeOffset)
        {
            if (src == null) return null;
            var json = JsonUtility.ToJson(src);
            SkillNodeData dup = JsonUtility.FromJson(json, src.GetType()) as SkillNodeData;
            if (dup == null) return null;
            switch (dup)
            {
                case MeleeSwingData m:        m.triggerTime += triggerTimeOffset; break;
                case RectShotData s:          s.triggerTime += triggerTimeOffset; break;
                case BeamData b:              b.triggerTime += triggerTimeOffset; break;
                case ChainBounceData c:       c.triggerTime += triggerTimeOffset; break;
                case CurvedProjectileData v:  v.triggerTime += triggerTimeOffset; break;
                case AOECircularData a:       a.triggerTime += triggerTimeOffset; break;
                default: return null; // 非判定类节点不多段复制
            }
            return dup;
        }

        /// <summary>
        /// 根据射程+弹体+范围组合，选择正确的判定节点类型。
        /// </summary>
        private static SkillNode BuildHitNode(SkillRecipe r)
        {
            // 近程 + 即时 = 近战挥击
            if (r.range == SkillRange.Melee && r.projectile == SkillProjectile.Instant)
            {
                return BuildMeleeNode(r);
            }

            // 远程类：根据弹体类型选择
            switch (r.projectile)
            {
                case SkillProjectile.Projectile:
                    return BuildProjectileNode(r);

                case SkillProjectile.Curved:
                    return BuildCurvedNode(r);

                case SkillProjectile.Beam:
                    return BuildBeamNode(r);

                case SkillProjectile.Chain:
                    return BuildChainNode(r);

                // 远程 + 即时 = AOE 在目标点爆炸
                case SkillProjectile.Instant:
                    return BuildAOENode(r);
            }
            return null;
        }

        // ── 近战挥击 ──
        private static SkillNode BuildMeleeNode(SkillRecipe r)
        {
            var n = ScriptableObject.CreateInstance<MeleeSwingNode>();
            n.range = r.rangeMeters;
            n.hitRadius = r.hitRadius;
            n.hitAngle = r.targeting switch
            {
                SkillTargeting.Single => 45f,    // 单体窄角
                SkillTargeting.Cone   => r.hitAngle, // 扇形用配置角度
                SkillTargeting.AOE    => 180f,   // AOE 全方向
                SkillTargeting.Line   => 15f,    // 直线极窄
                _ => 60f,
            };
            n.damage = r.damage;
            n.originHeight = 0.8f;
            return n;
        }

        // ── 直线弹体（矩形投射物） ──
        private static SkillNode BuildProjectileNode(SkillRecipe r)
        {
            var n = ScriptableObject.CreateInstance<RectShotNode>();
            n.shape = r.targeting switch
            {
                SkillTargeting.Line => ShotShape.Line,
                SkillTargeting.Cone => ShotShape.Cone,
                _ => ShotShape.Rect,
            };
            n.width = r.targeting == SkillTargeting.Line ? 0.3f : r.hitRadius;
            n.height = 1.8f;
            n.speed = r.projectileSpeed;
            n.maxRange = r.rangeMeters;
            n.count = r.projectileCount;
            n.spreadAngle = r.projectileSpread;
            n.hitMode = r.targeting == SkillTargeting.AOE ? ShotHitMode.PierceAll : ShotHitMode.Stop;
            n.spawnHeight = 0.9f;
            n.forwardOffset = 0.3f;
            n.projectilePrefab = r.projectilePrefab;
            n.projectileLifetime = r.projectileLifetime;
            return n;
        }

        // ── 抛物线弹体 ──
        private static SkillNode BuildCurvedNode(SkillRecipe r)
        {
            var n = ScriptableObject.CreateInstance<CurvedProjectileNode>();
            n.width = r.hitRadius;
            n.height = 1.8f;
            n.speed = r.projectileSpeed;
            n.maxRange = r.rangeMeters;
            n.gravity = 12f;
            n.groundSnapY = 0.2f;
            n.launchPitch = 30f;
            n.spawnHeight = 0.9f;
            n.forwardOffset = 0.3f;
            n.projectilePrefab = r.projectilePrefab;
            n.projectileLifetime = r.projectileLifetime;
            return n;
        }

        // ── 光束 ──
        private static SkillNode BuildBeamNode(SkillRecipe r)
        {
            var n = ScriptableObject.CreateInstance<BeamNode>();
            n.beamWidth = r.targeting == SkillTargeting.Line ? 0.15f : r.hitRadius;
            n.range = r.rangeMeters;
            n.damage = r.damage;
            n.originHeight = 1f;
            return n;
        }

        // ── 链式弹射 ──
        private static SkillNode BuildChainNode(SkillRecipe r)
        {
            var n = ScriptableObject.CreateInstance<ChainBounceNode>();
            n.bounceCount = r.projectileCount > 1 ? r.projectileCount : 4;
            n.searchRadius = r.rangeMeters;
            n.damageFalloff = 0.7f;
            n.maxBounceRange = r.rangeMeters * 2f;
            n.damage = r.damage;
            return n;
        }

        // ── AOE（目标点/脚下爆炸） ──
        private static SkillNode BuildAOENode(SkillRecipe r)
        {
            var n = ScriptableObject.CreateInstance<AOECircularNode>();
            n.radius = r.hitRadius;
            n.centerIsTargetPoint = r.range == SkillRange.Ranged;
            n.damage = r.damage;
            return n;
        }


        /// <summary>
        /// 生成 SkillData 的公共字段值（从 recipe 映射）。
        /// 方案 C：仅写入 graphData 中不内含的顶层身份/动画字段。
        /// </summary>
        public static void ApplyToSkillData(SkillData data, SkillRecipe r)
        {
            if (data == null || r == null) return;
            data.skillName = r.skillName;
            data.skillId = r.skillId;
            data.icon = r.icon;
            data.category = r.category;
            data.cooldown = r.cooldown;
            data.castTime = r.castTime;
            data.frontSwing = r.frontSwing;
            data.backSwing = r.backSwing;
            data.fadeInDuration = r.fadeInDuration;
            data.fadeOutDuration = r.fadeOutDuration;
            data.animTrigger = r.animTrigger;
            data.animStateId = r.animStateId;
            data.animClipName = r.animClipName;
            bool isMultiHit = r.multiHit == SkillMultiHit.MultiHit && r.stages != null && r.stages.Count > 0;
            // 多段开启时，全局 animClip 合并到第一段（若未配置）
            if (isMultiHit && r.animClip != null && r.stages[0].animClip == null)
            {
                r.stages[0].animClip = r.animClip;
                r.animClip = null;
            }
            data.animClips = r.animClip != null ? new[] { r.animClip } : new AnimationClip[0];
            // 2026-07-28(test07 对齐):补"动画层"时间轴轨道 —— AnimClipLayerData 无运行时行为(纯可视化层),
            // 有 animClips 但无该节点时自动注入,让时间轴出现"动画"轨道。
            if (data.animClips.Length > 0 && data.animClips[0] != null
                && !data.graphData.Exists(n => n is AnimClipLayerData))
                data.graphData.Insert(0, new AnimClipLayerData { animClip = data.animClips[0], triggerTime = 0f, visualOnly = true });
            data.channelDuration = r.multiHit == SkillMultiHit.Channel
                ? Mathf.Max(0f, r.hitCount * r.hitInterval) : 0f;
            data.multiStage = new SkillMultiStageConfig
            {
                enabled = isMultiHit,
                stageCount = r.stages != null ? r.stages.Count : 0,
                stageInterval = r.hitInterval,
                segments = r.stages != null ? new List<SkillMultiStageSegment>(r.stages) : new List<SkillMultiStageSegment>()
            };
            // 多段开启时，动画由各段 MultiStageLayerData 驱动，清空顶层 animClips 避免冲突
            if (isMultiHit) data.animClips = new AnimationClip[0];
        }

        /// <summary>
        /// 从现有 SkillData 反向生成配方，用于编辑现有技能时让 Step1/Step2
        /// 显示当前资产的实际配置。graphData 是唯一行为来源，兼容字段不参与读取。
        /// </summary>
        public static SkillRecipe FromSkillData(SkillData data)
        {
            var r = new SkillRecipe();
            if (data == null) return r;

            r.category = data.category;
            r.skillId = data.skillId;
            r.icon = data.icon;
            r.cooldown = data.cooldown;
            r.castTime = data.castTime;
            r.frontSwing = data.frontSwing;
            r.backSwing = data.backSwing;
            r.fadeInDuration = data.fadeInDuration;
            r.fadeOutDuration = data.fadeOutDuration;
            r.animTrigger = data.animTrigger;
            r.animStateId = data.animStateId;
            r.animClipName = data.animClipName;
            r.animClip = FirstClip(data);
            r.skillName = data.skillName;
            if (data.multiStage != null && data.multiStage.segments != null)
            {
                r.stages = new List<SkillMultiStageSegment>(data.multiStage.segments);
                r.hitCount = r.stages.Count;
                r.hitInterval = data.multiStage.stageInterval;
                if (data.multiStage.enabled && r.stages.Count > 0)
                    r.multiHit = SkillMultiHit.MultiHit;
            }

            if (data.graphData == null) return r;
            if ((r.stages == null || r.stages.Count == 0))
            {
                r.stages = new List<SkillMultiStageSegment>();
                for (int i = 0; i < data.graphData.Count; i++)
                {
                    if (data.graphData[i] is MultiStageLayerData stage)
                    {
                        r.stages.Add(new SkillMultiStageSegment
                        {
                            stageName = stage.stageName,
                            animClip = stage.animClip,
                            animSpeed = stage.animSpeed,
                            vfxOnCast = stage.vfxOnCast,
                            sfxOnCast = stage.sfxOnCast
                        });
                    }
                }
                if (r.stages.Count > 0)
                {
                    r.hitCount = r.stages.Count;
                    r.multiHit = SkillMultiHit.MultiHit;
                }
            }
            int hitCount = 0;
            int animationStageCount = 0;
            bool hasChannel = false;
            float firstHitTime = -1f;
            float hitInterval = 0f;
            SkillNodeData firstHit = null;
            for (int i = 0; i < data.graphData.Count; i++)
            {
                var node = data.graphData[i];
                if (node == null) continue;
                if (node is CastVFXData cast)
                {
                    r.vfxOnCast = cast.prefab;
                    r.sfxOnCast = cast.sfx;
                    r.vfxCastBone = cast.spawnBone;
                }
                else if (node is HitVFXData hitVfx)
                {
                    r.vfxOnHit = hitVfx.prefab;
                    r.sfxOnHit = hitVfx.sfx;
                }
                else if (node is StatusEffectData status)
                {
                    r.buff = status.applyToSelf ? SkillBuffType.SelfBuff : SkillBuffType.Debuff;
                    r.buffEffect = status.effect;
                }
                else if (node is MultiStageLayerData stage)
                {
                    animationStageCount++;
                    if (r.animClip == null && stage.animClip != null) r.animClip = stage.animClip;
                }
                else if (node is ChanneledData channel)
                {
                    hasChannel = true;
                    r.multiHit = SkillMultiHit.Channel;
                    r.hitInterval = channel.tickInterval;
                    r.hitCount = Mathf.Max(1, Mathf.RoundToInt(channel.GetDuration(data) / Mathf.Max(channel.tickInterval, 0.01f)));
                }
                else if (node is SkillNodeData hitNode && IsHitNode(hitNode))
                {
                    hitCount++;
                    if (firstHit == null) firstHit = hitNode;
                    float t = GetHitTriggerTime(hitNode);
                    if (firstHitTime < 0f) firstHitTime = t;
                    else if (hitInterval <= 0f) hitInterval = Mathf.Max(0f, t - firstHitTime);
                }
            }

            if (!hasChannel && (hitCount > 1 || animationStageCount > 1))
            {
                r.multiHit = SkillMultiHit.MultiHit;
                r.hitCount = Mathf.Max(hitCount, animationStageCount);
                r.hitInterval = hitInterval;
            }
            if (firstHit != null) ReadHitNode(firstHit, r);
            return r;
        }

        private static AnimationClip FirstClip(SkillData data)
        {
            if (data.animClips != null)
                for (int i = 0; i < data.animClips.Length; i++)
                    if (data.animClips[i] != null) return data.animClips[i];
            if (data.graphData != null)
                for (int i = 0; i < data.graphData.Count; i++)
                {
                    if (data.graphData[i] is AnimClipLayerData a && a.animClip != null) return a.animClip;
                    if (data.graphData[i] is MultiStageLayerData m && m.animClip != null) return m.animClip;
                }
            return null;
        }

        private static bool IsHitNode(SkillNodeData node)
            => node is MeleeSwingData || node is RectShotData || node is CurvedProjectileData
            || node is BeamData || node is ChainBounceData || node is AOECircularData;

        private static float GetHitTriggerTime(SkillNodeData node)
        {
            if (node is MeleeSwingData m) return m.triggerTime;
            if (node is RectShotData s) return s.triggerTime;
            if (node is CurvedProjectileData c) return c.triggerTime;
            if (node is BeamData b) return b.triggerTime;
            if (node is ChainBounceData l) return l.triggerTime;
            if (node is AOECircularData a) return a.triggerTime;
            return 0f;
        }

        private static void ReadHitNode(SkillNodeData node, SkillRecipe r)
        {
            if (node is MeleeSwingData m)
            {
                r.range = SkillRange.Melee;
                r.projectile = SkillProjectile.Instant;
                r.rangeMeters = m.range;
                r.hitRadius = m.hitRadius;
                r.hitAngle = m.hitAngle;
                r.damage = m.damage;
                return;
            }
            r.range = SkillRange.Ranged;
            if (node is RectShotData s)
            {
                r.projectile = SkillProjectile.Projectile;
                r.damage = s.damage; r.rangeMeters = s.maxRange; r.hitRadius = s.width;
                r.projectileSpeed = s.speed; r.projectileCount = s.count;
                r.projectileSpread = s.spreadAngle; r.projectileLifetime = s.projectileLifetime;
            }
            else if (node is CurvedProjectileData c)
            {
                r.projectile = SkillProjectile.Curved;
                r.damage = c.damage; r.rangeMeters = c.maxRange; r.hitRadius = c.width;
                r.projectileSpeed = c.speed; r.projectileLifetime = c.projectileLifetime;
            }
            else if (node is BeamData b)
            {
                r.projectile = SkillProjectile.Beam;
                r.damage = b.damage; r.rangeMeters = b.range; r.hitRadius = b.beamWidth;
            }
            else if (node is ChainBounceData l)
            {
                r.projectile = SkillProjectile.Chain;
                r.damage = l.damage; r.rangeMeters = l.searchRadius; r.projectileCount = l.bounceCount;
            }
            else if (node is AOECircularData a)
            {
                r.projectile = SkillProjectile.Instant;
                r.targeting = SkillTargeting.AOE;
                // D8: radius 是 AOE 范围不是射程,rangeMeters 应为 0(与 ReadNumericsFromHitNode 一致)
                r.damage = a.damage; r.rangeMeters = 0f; r.hitRadius = a.radius;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 节点预设库
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 技能预设 — 预配置的 SkillRecipe，一键加载常用技能模板。
    /// </summary>
    public static class SkillPresets
    {
        public static string[] PresetNames = new[]
        {
            "近战刀光（Melee Swing）",
            "散弹枪（Shotgun Blast）",
            "弓箭单发（Single Arrow）",
            "闪电链（Chain Lightning）",
            "榴弹抛物线（Grenade）",
            "激光光束（Laser Beam）",
            "持续施法 DOT（Channel）",
            "范围 AOE 爆炸（AOE Explosion）",
        };

        /// <summary>按索引获取预设配方。</summary>
        public static SkillRecipe GetPreset(int index)
        {
            return index switch
            {
                0 => MeleeSwing(),
                1 => ShotgunBlast(),
                2 => SingleArrow(),
                3 => ChainLightning(),
                4 => Grenade(),
                5 => LaserBeam(),
                6 => ChannelDOT(),
                7 => AOEExplosion(),
                _ => MeleeSwing(),
            };
        }

        static SkillRecipe MeleeSwing() => new SkillRecipe
        {
            skillName = "New_MeleeSwing",
            range = SkillRange.Melee,
            targeting = SkillTargeting.Cone,
            projectile = SkillProjectile.Instant,
            multiHit = SkillMultiHit.Single,
            buff = SkillBuffType.None,
            damage = 12f, rangeMeters = 2.5f, hitRadius = 1.5f, hitAngle = 60f,
            cooldown = 2f, frontSwing = 0.35f, backSwing = 0.3f,
            animTrigger = "Attack", animClipName = "", animStateId = 1,
            vfxCastBone = "VFX_BladeTip",
        };

        static SkillRecipe ShotgunBlast() => new SkillRecipe
        {
            skillName = "New_Shotgun",
            range = SkillRange.Ranged,
            targeting = SkillTargeting.Cone,
            projectile = SkillProjectile.Projectile,
            multiHit = SkillMultiHit.Single,
            buff = SkillBuffType.None,
            damage = 8f, rangeMeters = 8f, hitRadius = 1.5f, hitAngle = 30f,
            cooldown = 3f, frontSwing = 0.2f, backSwing = 0.4f,
            projectileSpeed = 16f, projectileCount = 5, projectileSpread = 30f,
            projectileLifetime = 1f,
            animTrigger = "SkillQ", animClipName = "Skill_Q", animStateId = 1,
            vfxCastBone = "VFX_Muzzle",
        };

        static SkillRecipe SingleArrow() => new SkillRecipe
        {
            skillName = "New_Arrow",
            range = SkillRange.Ranged,
            targeting = SkillTargeting.Single,
            projectile = SkillProjectile.Projectile,
            multiHit = SkillMultiHit.Single,
            buff = SkillBuffType.None,
            damage = 15f, rangeMeters = 12f, hitRadius = 0.4f, hitAngle = 10f,
            cooldown = 1.5f, frontSwing = 0.25f, backSwing = 0.3f,
            projectileSpeed = 25f, projectileCount = 1, projectileSpread = 0f,
            projectileLifetime = 2f,
            animTrigger = "SkillW", animClipName = "Skill_W", animStateId = 2,
            vfxCastBone = "VFX_Muzzle",
        };

        static SkillRecipe ChainLightning() => new SkillRecipe
        {
            skillName = "New_ChainLightning",
            range = SkillRange.Ranged,
            targeting = SkillTargeting.Single,
            projectile = SkillProjectile.Chain,
            multiHit = SkillMultiHit.Single,
            buff = SkillBuffType.None,
            damage = 10f, rangeMeters = 8f, hitRadius = 1f, hitAngle = 60f,
            cooldown = 5f, frontSwing = 0.2f, backSwing = 0.3f,
            projectileCount = 4,
            animTrigger = "SkillE", animClipName = "Skill_E", animStateId = 3,
            vfxCastBone = "RightHand",
        };

        static SkillRecipe Grenade() => new SkillRecipe
        {
            skillName = "New_Grenade",
            range = SkillRange.Ranged,
            targeting = SkillTargeting.AOE,
            projectile = SkillProjectile.Curved,
            multiHit = SkillMultiHit.Single,
            buff = SkillBuffType.None,
            damage = 30f, rangeMeters = 10f, hitRadius = 3f, hitAngle = 180f,
            cooldown = 8f, frontSwing = 0.3f, backSwing = 0.3f,
            projectileSpeed = 12f, projectileCount = 1, projectileLifetime = 3f,
            animTrigger = "SkillR", animClipName = "Skill_R", animStateId = 4,
            vfxCastBone = "RightHand",
        };

        static SkillRecipe LaserBeam() => new SkillRecipe
        {
            skillName = "New_LaserBeam",
            range = SkillRange.Ranged,
            targeting = SkillTargeting.Line,
            projectile = SkillProjectile.Beam,
            multiHit = SkillMultiHit.Single,
            buff = SkillBuffType.None,
            damage = 6f, rangeMeters = 15f, hitRadius = 0.5f, hitAngle = 10f,
            cooldown = 6f, frontSwing = 0.15f, backSwing = 0.4f,
            animTrigger = "SkillQ", animClipName = "Skill_Q", animStateId = 1,
            vfxCastBone = "VFX_Muzzle",
        };

        static SkillRecipe ChannelDOT() => new SkillRecipe
        {
            skillName = "New_ChannelDOT",
            range = SkillRange.Ranged,
            targeting = SkillTargeting.Single,
            projectile = SkillProjectile.Beam,
            multiHit = SkillMultiHit.Channel,
            buff = SkillBuffType.None,
            damage = 5f, rangeMeters = 10f, hitRadius = 0.5f, hitAngle = 30f,
            cooldown = 10f, frontSwing = 0.2f, backSwing = 0.3f,
            hitCount = 6, hitInterval = 0.5f,
            animTrigger = "SkillW", animClipName = "Skill_W", animStateId = 2,
            vfxCastBone = "VFX_Muzzle",
        };

        static SkillRecipe AOEExplosion() => new SkillRecipe
        {
            skillName = "New_AOEExplosion",
            range = SkillRange.Ranged,
            targeting = SkillTargeting.AOE,
            projectile = SkillProjectile.Instant,
            multiHit = SkillMultiHit.Single,
            buff = SkillBuffType.None,
            damage = 40f, rangeMeters = 8f, hitRadius = 4f, hitAngle = 180f,
            cooldown = 12f, frontSwing = 0.4f, backSwing = 0.4f,
            animTrigger = "SkillE", animClipName = "Skill_E", animStateId = 3,
            vfxCastBone = "Spine",
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 反向推导：SkillData + graphData → SkillRecipe
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 从已有的 SkillData.asset 反向推导出 SkillRecipe。
    /// 
    /// 用途：
    ///   1. "编辑现有技能" — 加载 .asset 后，提取 Recipe 让用户修改分类特征，重新生成节点
    ///   2. 技能复制/变体 — 提取 A→修改分类→生成 B
    ///   3. 预设导出 — 将现有技能保存为可复用的 Recipe
    /// 
    /// 数据流：
    ///   SkillData.graphData (List&lt;SkillNodeData&gt;) → 识别判定节点类型 → 反推 5 分类维度
    ///   + SkillData 顶层字段 (skillName/cooldown/anim 等) → Recipe 字段
    /// </summary>
    public static class SkillRecipeExtractor
    {
        /// <summary>
        /// 从 SkillData 提取 Recipe。
        /// 优先从 graphData 推断分类特征；graphData 为空时回退到顶层字段（兼容老 .asset）。
        /// </summary>
        public static SkillRecipe Extract(SkillData data)
        {
            if (data == null) return new SkillRecipe();

            var r = new SkillRecipe();

            // ── 第一步：从 graphData 识别判定节点类型 → 推断分类特征 ──
            SkillNodeData hitNode = FindHitNode(data);
            if (hitNode != null)
            {
                InferRangeAndProjectile(hitNode, r);
                InferTargeting(hitNode, r);
                ReadNumericsFromHitNode(hitNode, r);
            }

            // ── 第二步：从 graphData 识别 Channeled → multiHit ──
            bool hasChanneled = TryFindNodeOfType<ChanneledData>(data, out ChanneledData ch);
            r.multiHit = hasChanneled ? SkillMultiHit.Channel : SkillMultiHit.Single;
            if (hasChanneled)
            {
                r.hitCount = Mathf.RoundToInt(ch.durationOverride / Mathf.Max(ch.tickInterval, 0.001f));
                r.hitInterval = ch.tickInterval;
            }

            // ── 第三步：从 graphData 识别 StatusEffectData → buff ──
            // 注意：applyToSelf / applyPerTarget 是 StatusEffectData 节点自身的配置字段，
            // 而 duration / isDOT / tickAmount / damageMultiplier 来自引用的 StatusEffect 资源。
            bool hasStatusEffect = TryFindNodeOfType<StatusEffectData>(data, out StatusEffectData se);
            if (hasStatusEffect)
            {
                r.buff = se.applyToSelf ? SkillBuffType.SelfBuff
                       : se.applyPerTarget ? SkillBuffType.Debuff
                       : SkillBuffType.None;
                r.buffDuration = se.effect != null ? se.effect.duration : 5f;
                r.buffValue = se.effect != null
                    ? (se.effect.isDOT ? se.effect.tickAmount : se.effect.damageMultiplier)
                    : 20f;
            }

            // ── 第四步：从 graphData 读 VFX/SFX ──
            if (TryFindNodeOfType<CastVFXData>(data, out CastVFXData castVfx))
            {
                r.vfxOnCast = castVfx.prefab;
                r.sfxOnCast = castVfx.sfx;
                r.vfxCastBone = castVfx.spawnBone ?? "VFX_BladeTip";
            }
            if (TryFindNodeOfType<HitVFXData>(data, out HitVFXData hitVfx))
            {
                r.vfxOnHit = hitVfx.prefab;
                r.sfxOnHit = hitVfx.sfx;
            }

            // ── 第五步：从 graphData 读 MovementData → movementPolicy 推导射程 ──
            if (hitNode == null && TryFindNodeOfType<MovementData>(data, out MovementData moveData))
            {
                r.range = moveData.policy == MovementPolicy.SlowMove
                    ? SkillRange.Ranged : SkillRange.Melee;
            }

            // ── 第六步：从 graphData 读动画层信息 ──
            if (TryFindNodeOfType<AnimClipLayerData>(data, out AnimClipLayerData animLayer))
            {
                if (animLayer.animClip != null)
                    r.animClipName = animLayer.animClip.name;
            }

            // ── 第七步：SkillData 顶层字段 → Recipe ──
            ReadTopLevelFields(data, r);

            return r;
        }

        // ── 节点查找 ─────────────────────────────────────────────

        /// <summary>在 graphData 中找到第一个判定节点（近战/弹体/光束/AOE）。</summary>
        private static SkillNodeData FindHitNode(SkillData data)
        {
            if (data.graphData == null) return null;
            foreach (var node in data.graphData)
            {
                if (node is MeleeSwingData || node is RectShotData || node is CurvedProjectileData
                    || node is BeamData || node is ChainBounceData || node is AOECircularData)
                    return node;
            }
            return null;
        }

        /// <summary>在 graphData 中查找指定类型的第一个节点。</summary>
        private static bool TryFindNodeOfType<T>(SkillData data, out T result) where T : SkillNodeData
        {
            result = null;
            if (data.graphData == null) return false;
            foreach (var node in data.graphData)
            {
                if (node is T t)
                {
                    result = t;
                    return true;
                }
            }
            return false;
        }

        // ── 分类特征推断 ─────────────────────────────────────────

        /// <summary>从判定节点类型推断 range + projectile。</summary>
        private static void InferRangeAndProjectile(SkillNodeData node, SkillRecipe r)
        {
            switch (node)
            {
                case MeleeSwingData _:
                    r.range = SkillRange.Melee;
                    r.projectile = SkillProjectile.Instant;
                    break;
                case RectShotData _:
                    r.range = SkillRange.Ranged;
                    r.projectile = SkillProjectile.Projectile;
                    break;
                case CurvedProjectileData _:
                    r.range = SkillRange.Ranged;
                    r.projectile = SkillProjectile.Curved;
                    break;
                case BeamData _:
                    r.range = SkillRange.Ranged;
                    r.projectile = SkillProjectile.Beam;
                    break;
                case ChainBounceData _:
                    r.range = SkillRange.Ranged;
                    r.projectile = SkillProjectile.Chain;
                    break;
                case AOECircularData _:
                    r.range = SkillRange.Ranged;
                    r.projectile = SkillProjectile.Instant;
                    break;
            }
        }

        /// <summary>从判定节点空间参数推断 targeting。</summary>
        private static void InferTargeting(SkillNodeData node, SkillRecipe r)
        {
            switch (node)
            {
                case MeleeSwingData m:
                    r.hitAngle = m.hitAngle;
                    if (m.hitAngle <= 20f)          r.targeting = SkillTargeting.Line;
                    else if (m.hitAngle <= 50f)      r.targeting = SkillTargeting.Single;
                    else if (m.hitAngle <= 120f)     r.targeting = SkillTargeting.Cone;
                    else                             r.targeting = SkillTargeting.AOE;
                    break;

                case RectShotData shot:
                    r.targeting = shot.shape switch
                    {
                        ShotShape.Line => SkillTargeting.Line,
                        ShotShape.Cone => SkillTargeting.Cone,
                        _ => shot.width < 0.5f ? SkillTargeting.Line
                           : shot.hitMode == ShotHitMode.PierceAll ? SkillTargeting.AOE
                           : SkillTargeting.Single,
                    };
                    r.projectileCount = shot.count;
                    r.projectileSpeed = shot.speed;
                    r.projectileSpread = shot.spreadAngle;
                    r.projectileLifetime = shot.projectileLifetime;
                    r.projectilePrefab = shot.projectilePrefab;
                    break;

                case CurvedProjectileData cur:
                    r.targeting = SkillTargeting.AOE;
                    r.projectileSpeed = cur.speed;
                    r.projectileLifetime = cur.projectileLifetime;
                    r.projectilePrefab = cur.projectilePrefab;
                    break;

                case BeamData bm:
                    r.targeting = bm.beamWidth < 0.3f ? SkillTargeting.Line : SkillTargeting.Cone;
                    break;

                case ChainBounceData ch:
                    r.targeting = SkillTargeting.Single;
                    r.projectileCount = ch.bounceCount;
                    break;

                case AOECircularData aoe:
                    r.targeting = SkillTargeting.AOE;
                    break;
            }
        }

        /// <summary>从判定节点读取通用数值参数。</summary>
        private static void ReadNumericsFromHitNode(SkillNodeData node, SkillRecipe r)
        {
            switch (node)
            {
                case MeleeSwingData m:
                    r.damage = m.damage;
                    r.rangeMeters = m.range;
                    r.hitRadius = m.hitRadius;
                    break;
                case RectShotData s:
                    r.damage = s.damage;
                    r.rangeMeters = s.maxRange;
                    r.hitRadius = s.width;
                    break;
                case CurvedProjectileData c:
                    r.damage = c.damage;
                    r.rangeMeters = c.maxRange;
                    r.hitRadius = c.width;
                    break;
                case BeamData b:
                    r.damage = b.damage;
                    r.rangeMeters = b.range;
                    r.hitRadius = b.beamWidth;
                    break;
                case ChainBounceData ch:
                    r.damage = ch.damage;
                    r.rangeMeters = ch.searchRadius;
                    r.hitRadius = 1f;
                    break;
                case AOECircularData a:
                    r.damage = a.damage;
                    r.rangeMeters = 0f; // AOE 通常以目标点为中心
                    r.hitRadius = a.radius;
                    break;
            }
        }

        /// <summary>从 SkillData 顶层字段读取 Recipe 字段。</summary>
        private static void ReadTopLevelFields(SkillData data, SkillRecipe r)
        {
            r.skillName     = data.animClipName ?? "New Skill";
            r.category      = data.category;
            r.cooldown      = data.cooldown;
            r.frontSwing    = data.frontSwing;
            r.backSwing     = data.backSwing;
            r.animTrigger   = data.animTrigger ?? "SkillQ";
            r.animStateId   = data.animStateId;
            r.animClipName  = data.animClipName ?? "";

            // outputFolder 保持默认
            r.outputFolder = "_Game/SkillData";
        }
    }
}
