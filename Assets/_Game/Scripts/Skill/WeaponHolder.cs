using UnityEngine;
using System.Collections.Generic;

namespace Game.SkillSystem
{
    /// <summary>
    /// 武器挂载管理器 — 挂在角色 GameObject 上
    /// 
    /// 职责：
    /// - 管理多个武器槽位的装备/卸下
    /// - 在战斗位和收纳位之间切换武器模型
    /// - 提供当前武器的战斗属性修正给 SkillController
    ///
    /// 使用方式：
    /// 1. 在 Inspector 中配置 mountPoints（挂载点列表）
    /// 2. 运行时调用 Equip(weaponData) 装备武器
    /// 3. SkillController 通过 GetCombatModifiers() 获取战斗修正
    /// </summary>
    [AddComponentMenu("Game/Skill System/Weapon Holder")]
    public class WeaponHolder : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════
        // 配置
        // ═══════════════════════════════════════════════════════════════

        [Header("挂载点配置")]
        [Tooltip("定义每个槽位对应的骨骼节点")]
        public WeaponMountPoint[] mountPoints = new WeaponMountPoint[]
        {
            new WeaponMountPoint { slotType = WeaponSlotType.MainHand, bone = HumanBodyBones.RightHand },
            new WeaponMountPoint { slotType = WeaponSlotType.OffHand,  bone = HumanBodyBones.LeftHand },
            new WeaponMountPoint { slotType = WeaponSlotType.Back,     bone = HumanBodyBones.Spine },
            new WeaponMountPoint { slotType = WeaponSlotType.Hip,      bone = HumanBodyBones.Hips },
        };

        [Header("初始装备")]
        [Tooltip("场景开始时自动装备的武器（可留空）")]
        public WeaponData initialMainHandWeapon;
        public WeaponData initialOffHandWeapon;

        // ═══════════════════════════════════════════════════════════════
        // 运行时状态
        // ═══════════════════════════════════════════════════════════════

        private Animator _animator;
        private Dictionary<WeaponSlotType, Transform> _resolvedMountPoints = new();
        private Dictionary<WeaponSlotType, EquippedWeaponState> _equipped = new();

        /// <summary>单个槽位的装备状态</summary>
        private class EquippedWeaponState
        {
            public WeaponData data;
            public GameObject instance;
            public bool isInHolster; // true = 收纳位，false = 战斗位
        }

        // ═══════════════════════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════════════════════

        void Awake()
        {
            _animator = GetComponent<Animator>();
            ResolveMountPoints();
        }

        void Start()
        {
            // 自动装备初始武器
            if (initialMainHandWeapon != null)
                Equip(initialMainHandWeapon, WeaponSlotType.MainHand);
            if (initialOffHandWeapon != null)
                Equip(initialOffHandWeapon, WeaponSlotType.OffHand);
        }

        // ═══════════════════════════════════════════════════════════════
        // 公开 API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 装备武器到指定槽位。如果槽位已有武器，先卸下旧武器。
        /// </summary>
        /// <param name="weapon">武器数据</param>
        /// <param name="slot">目标槽位（默认使用武器的 defaultSlot）</param>
        /// <param name="startInHolster">是否以收纳状态开始</param>
        public void Equip(WeaponData weapon, WeaponSlotType? slot = null, bool startInHolster = false)
        {
            if (weapon == null || weapon.prefab == null)
            {
                Debug.LogWarning("[WeaponHolder] 武器数据或 Prefab 为空，无法装备");
                return;
            }

            WeaponSlotType targetSlot = slot ?? weapon.defaultSlot;

            // 卸下当前槽位的旧武器
            if (_equipped.ContainsKey(targetSlot))
                Unequip(targetSlot);

            // 确定挂载骨骼
            Transform mountBone = GetMountTransform(startInHolster ? weapon.holsterSlot : targetSlot);
            if (mountBone == null)
            {
                Debug.LogWarning($"[WeaponHolder] 找不到槽位 {targetSlot} 的挂载骨骼");
                mountBone = transform;
            }

            // 实例化武器模型
            GameObject instance = Instantiate(weapon.prefab, mountBone);
            instance.name = $"Weapon_{weapon.weaponName}";

            // 设置偏移
            if (startInHolster)
            {
                instance.transform.localPosition = weapon.holsterPositionOffset;
                instance.transform.localRotation = Quaternion.Euler(weapon.holsterRotationOffset);
            }
            else
            {
                instance.transform.localPosition = weapon.positionOffset;
                instance.transform.localRotation = Quaternion.Euler(weapon.rotationOffset);
            }
            instance.transform.localScale = weapon.scale;

            // 记录状态
            _equipped[targetSlot] = new EquippedWeaponState
            {
                data = weapon,
                instance = instance,
                isInHolster = startInHolster,
            };

            Debug.Log($"[WeaponHolder] 装备 [{weapon.weaponName}] → {targetSlot}");
        }

        /// <summary>
        /// 卸下指定槽位的武器（销毁模型）
        /// </summary>
        public void Unequip(WeaponSlotType slot)
        {
            if (!_equipped.TryGetValue(slot, out var state)) return;

            if (state.instance != null)
                Destroy(state.instance);

            _equipped.Remove(slot);
            Debug.Log($"[WeaponHolder] 卸下 {slot} 的武器");
        }

        /// <summary>
        /// 将武器切换到收纳位（非战斗状态）
        /// </summary>
        public void SwitchToHolster(WeaponSlotType slot)
        {
            if (!_equipped.TryGetValue(slot, out var state) || state.isInHolster) return;

            Transform holsterBone = GetMountTransform(state.data.holsterSlot);
            if (holsterBone == null) return;

            state.instance.transform.SetParent(holsterBone);
            state.instance.transform.localPosition = state.data.holsterPositionOffset;
            state.instance.transform.localRotation = Quaternion.Euler(state.data.holsterRotationOffset);
            state.isInHolster = true;
        }

        /// <summary>
        /// 将武器切换到战斗位（从收纳位拿出来）
        /// </summary>
        public void SwitchToCombat(WeaponSlotType slot)
        {
            if (!_equipped.TryGetValue(slot, out var state) || !state.isInHolster) return;

            Transform combatBone = GetMountTransform(slot);
            if (combatBone == null) return;

            state.instance.transform.SetParent(combatBone);
            state.instance.transform.localPosition = state.data.positionOffset;
            state.instance.transform.localRotation = Quaternion.Euler(state.data.rotationOffset);
            state.isInHolster = false;
        }

        /// <summary>
        /// 获取指定槽位当前装备的武器数据（null = 无装备）
        /// </summary>
        public WeaponData GetEquippedWeapon(WeaponSlotType slot)
        {
            if (_equipped.TryGetValue(slot, out var state))
                return state.data;
            return null;
        }

        /// <summary>
        /// 获取主手武器的战斗属性修正。无武器时返回 Default（无修正）。
        /// </summary>
        public WeaponCombatModifiers GetCombatModifiers()
        {
            if (_equipped.TryGetValue(WeaponSlotType.MainHand, out var state) && state.data != null)
                return state.data.GetModifiers();
            return WeaponCombatModifiers.Default;
        }

        /// <summary>
        /// 检查指定槽位是否有武器装备
        /// </summary>
        public bool HasWeapon(WeaponSlotType slot)
        {
            return _equipped.ContainsKey(slot) && _equipped[slot].data != null;
        }

        /// <summary>
        /// 获取武器实例 GameObject（用于 VFX 挂载等）
        /// </summary>
        public GameObject GetWeaponInstance(WeaponSlotType slot)
        {
            if (_equipped.TryGetValue(slot, out var state))
                return state.instance;
            return null;
        }

        /// <summary>
        /// 在主手武器实例的子级中查找命名节点（VFX 挂载点）。
        /// 用于方案 A：武器 prefab 内预埋 VFX_BladeTip / VFX_Muzzle 等空节点。
        /// 查找顺序：主手 → 副手 → 返回 null。
        /// </summary>
        /// <param name="name">节点名（如 "VFX_BladeTip"）</param>
        /// <returns>匹配的 Transform，找不到返回 null</returns>
        public Transform GetWeaponMountPoint(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // 主手武器子级
            Transform t = FindInWeaponChildren(WeaponSlotType.MainHand, name);
            if (t != null) return t;

            // 副手武器子级
            t = FindInWeaponChildren(WeaponSlotType.OffHand, name);
            if (t != null) return t;

            return null;
        }

        private Transform FindInWeaponChildren(WeaponSlotType slot, string name)
        {
            if (!_equipped.TryGetValue(slot, out var state)) return null;
            if (state.instance == null) return null;
            return FindChildRecursive(state.instance.transform, name);
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // 内部方法
        // ═══════════════════════════════════════════════════════════════

        private void ResolveMountPoints()
        {
            _resolvedMountPoints.Clear();
            foreach (var mp in mountPoints)
            {
                Transform t = mp.Resolve(_animator, transform);
                _resolvedMountPoints[mp.slotType] = t;
                Debug.Log($"[WeaponHolder] 挂载点 {mp.slotType} → {t.name} (bone={mp.bone}, custom='{mp.customBoneName}', isHuman={(_animator != null && _animator.isHuman)})");
            }
        }

        private Transform GetMountTransform(WeaponSlotType slot)
        {
            if (_resolvedMountPoints.TryGetValue(slot, out Transform t))
                return t;

            // 如果没有预配置的挂载点，尝试用默认映射
            HumanBodyBones defaultBone = slot switch
            {
                WeaponSlotType.MainHand => HumanBodyBones.RightHand,
                WeaponSlotType.OffHand => HumanBodyBones.LeftHand,
                WeaponSlotType.Back => HumanBodyBones.Spine,
                WeaponSlotType.Hip => HumanBodyBones.Hips,
                _ => HumanBodyBones.RightHand,
            };

            if (_animator != null && _animator.isHuman)
            {
                Transform bone = _animator.GetBoneTransform(defaultBone);
                if (bone != null) return bone;
            }

            return transform;
        }
    }
}
