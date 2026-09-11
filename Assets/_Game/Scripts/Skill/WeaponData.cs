using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// 武器数据 ScriptableObject
    /// 
    /// 配置武器的模型、挂载偏移、收纳位置、战斗属性修正。
    /// 通过 WeaponHolder 在运行时装备到角色骨骼上。
    ///
    /// 创建：Project 面板右键 → Create → Game → Weapon Data
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeapon", menuName = "Game/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        // ═══════════════════════════════════════════════════════════════
        // 基本信息
        // ═══════════════════════════════════════════════════════════════

        [Header("基本信息")]
        [Tooltip("武器名称（显示用）")]
        public string weaponName = "New Weapon";

        [Tooltip("武器图标（UI 用）")]
        public Sprite icon;

        [Tooltip("武器模型 Prefab（实例化挂到骨骼上）")]
        public GameObject prefab;

        // ═══════════════════════════════════════════════════════════════
        // 战斗位挂载配置
        // ═══════════════════════════════════════════════════════════════

        [Header("战斗位挂载")]
        [Tooltip("默认装备槽位（决定挂载到哪个骨骼）")]
        public WeaponSlotType defaultSlot = WeaponSlotType.MainHand;

        [Tooltip("相对骨骼的位置偏移")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("相对骨骼的旋转偏移（欧拉角）")]
        public Vector3 rotationOffset = Vector3.zero;

        [Tooltip("武器缩放")]
        public Vector3 scale = Vector3.one;

        // ═══════════════════════════════════════════════════════════════
        // 收纳位配置（非战斗状态 / 切换武器时）
        // ═══════════════════════════════════════════════════════════════

        [Header("收纳位配置")]
        [Tooltip("收纳时挂载的槽位（如背部、腰部）")]
        public WeaponSlotType holsterSlot = WeaponSlotType.Back;

        [Tooltip("收纳位的位置偏移")]
        public Vector3 holsterPositionOffset = Vector3.zero;

        [Tooltip("收纳位的旋转偏移（欧拉角）")]
        public Vector3 holsterRotationOffset = Vector3.zero;

        // ═══════════════════════════════════════════════════════════════
        // 战斗属性修正
        // ═══════════════════════════════════════════════════════════════

        [Header("战斗属性修正")]
        [Tooltip("伤害倍率（乘算叠加到 SkillData.damage 上）\n1.0 = 不修改，1.5 = +50%伤害")]
        public float damageMultiplier = 1f;

        [Tooltip("攻击范围加成（加算到 SkillData.range 上）\n0 = 不加成，2.0 = +2m范围")]
        public float rangeBonus = 0f;

        [Tooltip("攻击速度倍率（影响 backSwing 缩放）\n1.0 = 正常，0.8 = 加速20%，1.2 = 减速20%")]
        public float attackSpeedMultiplier = 1f;

        [Tooltip("判定半径加成（加算到 SkillData.hitRadius 上）\n0 = 不加成")]
        public float hitRadiusBonus = 0f;

        // ═══════════════════════════════════════════════════════════════
        // 工具方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 获取此武器的战斗修正值
        /// </summary>
        public WeaponCombatModifiers GetModifiers()
        {
            return new WeaponCombatModifiers
            {
                damageMultiplier = damageMultiplier,
                rangeBonus = rangeBonus,
                attackSpeedMultiplier = attackSpeedMultiplier,
                hitRadiusBonus = hitRadiusBonus,
            };
        }
    }
}
