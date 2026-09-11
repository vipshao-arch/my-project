// 2026-07-21:StatusEffectNode 已废弃(仅旧资产反序列化兼容),豁免 CS0618。
#pragma warning disable 0618

using UnityEngine;
using System;
using System.Collections.Generic;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 状态效果数据 — Buff/Debuff 的配置资产。
    ///
    /// 设计理念（对标 GAS GameplayEffect）：
    ///   - StatusEffect 是独立 SO，不侵入 SkillData
    ///   - 技能通过 StatusEffectNode 引用 StatusEffect
    ///   - 命中时由 StatusEffectNode 把 Effect 施加到目标身上
    ///   - StatusEffectManager（挂在角色上）负责管理已激活的 Effect 实例
    ///
    /// 创建：Project > Create > Game > Status Effect
    /// </summary>
    [CreateAssetMenu(fileName = "NewStatusEffect", menuName = "Game/Status Effect")]
    public class StatusEffect : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("效果名称（显示用）")]
        public string effectName = "New Effect";

        [Tooltip("效果图标")]
        public Sprite icon;

        [Tooltip("效果类型")]
        public EffectType type = EffectType.Buff;

        [Tooltip("持续时间（秒）。0=永久，-1=瞬发（无持续）")]
        public float duration = 5f;

        [Tooltip("叠加层数上限")]
        [Range(1, 99)]
        public int maxStacks = 1;

        [Header("数值修正")]
        [Tooltip("攻击力百分比修正（20=+20%，-20=-20%）")]
        public float damageMultiplier = 0f;

        [Tooltip("防御力百分比修正")]
        public float defenseMultiplier = 0f;

        [Tooltip("移动速度百分比修正")]
        public float moveSpeedMultiplier = 0f;

        [Tooltip("额外伤害减免（0=无，0.3=减伤30%）")]
        [Range(0f, 0.95f)]
        public float damageReduction = 0f;

        [Header("DOT/HOT")]
        [Tooltip("是否为持续伤害/治疗")]
        public bool isDOT = false;

        [Tooltip("DOT/HOT 间隔（秒）")]
        public float tickInterval = 1f;

        [Tooltip("单次 tick 数值（正数=伤害，负数=治疗）")]
        public float tickAmount = 5f;

        [Header("视觉")]
        [Tooltip("激活时挂在角色身上的 VFX Prefab")]
        public GameObject vfxPrefab;

        [Tooltip("VFX 挂载骨骼名（留空=角色根）")]
        public string vfxBone = "Spine";

        [Header("动画")]
        [Tooltip("激活时触发的 Animator Trigger（留空=不触发）")]
        public string animTrigger = "";

        /// <summary>效果类型枚举</summary>
        public enum EffectType
        {
            /// <summary>增益（攻击力提升/护盾/加速）</summary>
            Buff,
            /// <summary>减益（中毒/减速/破甲）</summary>
            Debuff,
            /// <summary>控制（眩晕/冰冻/定身）</summary>
            CrowdControl,
            /// <summary>护盾（吸收伤害）</summary>
            Shield,
        }
    }

    /// <summary>
    /// 运行时状态效果实例（挂在角色上的活跃 Effect）。
    /// 由 StatusEffectManager 管理生命周期。
    /// </summary>
    [Serializable]
    public class StatusEffectInstance
    {
        public StatusEffect data;
        public float remainingTime;
        public int stacks;
        public float tickTimer;
        public GameObject vfxInstance;

        public bool IsExpired => data.duration > 0f && remainingTime <= 0f;
    }

    /// <summary>
    /// 状态效果管理器 — 挂在角色身上，管理所有活跃的 Buff/Debuff。
    /// </summary>
    [AddComponentMenu("Game/Skill System/Status Effect Manager")]
    public class StatusEffectManager : MonoBehaviour
    {
        [Header("当前效果（运行时）")]
        [SerializeField] private List<StatusEffectInstance> _activeEffects = new();

        private IDamageable _damageable;
        private Animator _animator;

        /// <summary>当前攻击力乘数（所有效果累加）</summary>
        public float TotalDamageMultiplier
        {
            get
            {
                float total = 0f;
                foreach (var e in _activeEffects)
                    total += e.data.damageMultiplier * 0.01f * e.stacks;
                return total;
            }
        }

        /// <summary>当前移动速度乘数</summary>
        public float TotalMoveSpeedMultiplier
        {
            get
            {
                float total = 0f;
                foreach (var e in _activeEffects)
                    total += e.data.moveSpeedMultiplier * 0.01f * e.stacks;
                return total;
            }
        }

        /// <summary>当前伤害减免</summary>
        public float TotalDamageReduction
        {
            get
            {
                float total = 0f;
                foreach (var e in _activeEffects)
                    total = Mathf.Max(total, e.data.damageReduction);
                return total;
            }
        }

        /// <summary>是否处于控制状态（眩晕/冰冻）</summary>
        public bool IsCrowdControlled
        {
            get
            {
                foreach (var e in _activeEffects)
                    if (e.data.type == StatusEffect.EffectType.CrowdControl && !e.IsExpired)
                        return true;
                return false;
            }
        }

        void Awake()
        {
            _damageable = GetComponent<IDamageable>();
            _animator = GetComponent<Animator>();
        }

        void Update()
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var inst = _activeEffects[i];

                // 持续时间倒计时
                if (inst.data.duration > 0f)
                {
                    inst.remainingTime -= Time.deltaTime;
                    if (inst.IsExpired)
                    {
                        RemoveEffect(i);
                        continue;
                    }
                }

                // DOT/HOT tick
                if (inst.data.isDOT)
                {
                    inst.tickTimer -= Time.deltaTime;
                    if (inst.tickTimer <= 0f)
                    {
                        inst.tickTimer = inst.data.tickInterval;
                        if (_damageable != null && !(_damageable.isDead))
                        {
                            if (inst.data.tickAmount > 0f)
                                _damageable.TakeDamage(inst.data.tickAmount, gameObject, Vector3.zero);
                            else
                                _damageable.Heal(-inst.data.tickAmount);
                        }
                    }
                }
            }
        }

        /// <summary>施加一个状态效果。</summary>
        public void Apply(StatusEffect effect)
        {
            if (effect == null) return;

            // 检查是否已有同类型效果
            var existing = _activeEffects.Find(e => e.data == effect);
            if (existing != null)
            {
                if (existing.stacks < existing.data.maxStacks)
                    existing.stacks++;
                existing.remainingTime = effect.duration;
                return;
            }

            // 新增效果
            var inst = new StatusEffectInstance
            {
                data = effect,
                remainingTime = effect.duration,
                stacks = 1,
                tickTimer = effect.tickInterval,
            };

            // 生成 VFX
            if (effect.vfxPrefab != null)
            {
                Transform bone = string.IsNullOrEmpty(effect.vfxBone)
                    ? transform
                    : FindChild(transform, effect.vfxBone) ?? transform;
                inst.vfxInstance = Instantiate(effect.vfxPrefab, bone);
            }

            // 触发动画
            if (_animator != null && !string.IsNullOrEmpty(effect.animTrigger))
                _animator.SetTrigger(effect.animTrigger);

            _activeEffects.Add(inst);
        }

        /// <summary>移除指定索引的效果。</summary>
        public void RemoveEffect(int index)
        {
            if (index < 0 || index >= _activeEffects.Count) return;
            var inst = _activeEffects[index];
            if (inst.vfxInstance != null)
                Destroy(inst.vfxInstance);
            _activeEffects.RemoveAt(index);
        }

        /// <summary>清除所有效果。</summary>
        public void ClearAll()
        {
            foreach (var e in _activeEffects)
                if (e.vfxInstance != null) Destroy(e.vfxInstance);
            _activeEffects.Clear();
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform c in parent)
            {
                if (c.name == name) return c;
                var f = FindChild(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }

    /// <summary>
    /// 状态效果节点 — 技能命中时施加 Buff/Debuff 到目标。
    /// 放在 SkillData.graphData 中，OnHit 时触发。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Skill Node/Status Effect", fileName = "Node_StatusEffect")]
    public class StatusEffectNode : SkillNode
    {
        [Header("状态效果")]
        [Tooltip("要施加的 StatusEffect 资产")]
        public StatusEffect effect;

        [Tooltip("施加目标：true=施法者自身（增益），false=被命中目标（减益）")]
        public bool applyToSelf = false;

        [Tooltip("命中多少个目标就施加多少次（false=只施加一次）")]
        public bool applyPerTarget = true;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (effect == null) return;

            if (applyToSelf)
            {
                // 自身增益
                var mgr = ctx.source != null ? ctx.source.GetComponent<StatusEffectManager>() : null;
                if (mgr != null)
                    mgr.Apply(effect);
            }
            else if (hit.targets != null)
            {
                // 目标减益
                int count = applyPerTarget ? hit.targets.Count : 1;
                for (int i = 0; i < count && i < hit.targets.Count; i++)
                {
                    var target = hit.targets[i];
                    if (target == null) continue;
                    var mgr = target.GetComponent<StatusEffectManager>();
                    if (mgr != null)
                        mgr.Apply(effect);
                }
            }
        }
    }
}
