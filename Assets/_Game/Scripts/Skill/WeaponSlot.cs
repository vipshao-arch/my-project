using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// 武器槽位类型
    /// </summary>
    public enum WeaponSlotType
    {
        MainHand,   // 主手（右手）— 攻击武器
        OffHand,    // 副手（左手）— 盾牌/副武器
        Back,       // 背部 — 收纳大型武器
        Hip,        // 腰部 — 收纳小型武器
    }

    /// <summary>
    /// 武器挂载点配置 — 定义每个槽位对应的骨骼节点
    /// </summary>
    [System.Serializable]
    public class WeaponMountPoint
    {
        [Tooltip("此挂载点对应的槽位类型")]
        public WeaponSlotType slotType = WeaponSlotType.MainHand;

        [Tooltip("Humanoid 标准骨骼枚举（优先使用）")]
        public HumanBodyBones bone = HumanBodyBones.RightHand;

        [Tooltip("自定义骨骼节点名（当 bone 找不到时使用，支持非标准骨骼如 'Bip001 R Hand'）")]
        public string customBoneName = "";

        /// <summary>
        /// 解析挂载点对应的 Transform
        /// </summary>
        public Transform Resolve(Animator animator, Transform root)
        {
            // 优先：Humanoid 标准骨骼映射
            if (animator != null && animator.isHuman)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t != null) return t;
            }

            // Fallback 1：通过自定义骨骼名查找
            if (!string.IsNullOrEmpty(customBoneName))
            {
                Transform found = FindChildByName(root, customBoneName);
                if (found != null) return found;
            }

            // Fallback 2：通过常见骨骼名模式自动查找
            string[] fallbackNames = GetFallbackBoneNames(bone);
            foreach (var name in fallbackNames)
            {
                Transform found = FindChildByName(root, name);
                if (found != null) return found;
            }

            // 最终 fallback: 使用 root
            return root;
        }

        /// <summary>
        /// 根据 HumanBodyBones 枚举返回常见的骨骼名匹配列表
        /// </summary>
        private static string[] GetFallbackBoneNames(HumanBodyBones bone)
        {
            return bone switch
            {
                HumanBodyBones.RightHand => new[] {
                    "Bip001 R Hand", "R Hand", "RightHand", "Right_Hand",
                    "hand_R", "hand.R", "mixamorig:RightHand",
                    "Bip001 R Hand_scale"
                },
                HumanBodyBones.LeftHand => new[] {
                    "Bip001 L Hand", "L Hand", "LeftHand", "Left_Hand",
                    "hand_L", "hand.L", "mixamorig:LeftHand",
                    "Bip001 L Hand_scale"
                },
                HumanBodyBones.Spine => new[] {
                    "Bip001 Spine", "Spine", "spine", "Bip001 Spine_scale",
                    "mixamorig:Spine"
                },
                HumanBodyBones.Hips => new[] {
                    "Bip001 Pelvis", "Hips", "hips", "Pelvis",
                    "mixamorig:Hips", "Bip001"
                },
                _ => new string[0],
            };
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindChildByName(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }

    /// <summary>
    /// 武器战斗属性修正值（从 WeaponData 计算后传出）
    /// </summary>
    public struct WeaponCombatModifiers
    {
        public float damageMultiplier;      // 伤害倍率（乘算）
        public float rangeBonus;            // 攻击范围加成（加算）
        public float attackSpeedMultiplier; // 攻速倍率（乘算）
        public float hitRadiusBonus;        // 判定范围加成（加算）

        /// <summary>无修正的默认值</summary>
        public static WeaponCombatModifiers Default => new WeaponCombatModifiers
        {
            damageMultiplier = 1f,
            rangeBonus = 0f,
            attackSpeedMultiplier = 1f,
            hitRadiusBonus = 0f,
        };
    }
}
