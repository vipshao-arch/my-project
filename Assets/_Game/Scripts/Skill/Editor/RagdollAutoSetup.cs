using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Game.Character.Editor
{
    /// <summary>
    /// Ragdoll 自动配置工具
    /// 
    /// 为 Humanoid 角色自动创建完整的 Ragdoll 物理骨骼配置：
    /// - 在关键骨骼上添加 Rigidbody + CapsuleCollider/BoxCollider + CharacterJoint
    /// - 参数基于标准人形体型自动计算
    /// - 兼容 vRagdoll 组件需求
    ///
    /// 调用方式：由 Character Kit 装配流水线「③ Ragdoll 布娃娃设置」步骤调用
    /// （CharacterKitPanel.Ragdoll.cs → ExecuteRagdollSetup → 本类.Execute()）。
    /// </summary>
    public class RagdollAutoSetup
    {
        public static void Execute()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选中一个角色 GameObject", "OK");
                return;
            }

            try
            {
                int created = Execute(go);
                EditorUtility.DisplayDialog("完成",
                    $"Ragdoll 配置完成！\n\n" +
                    $"• 配置了 {created} 个骨骼的物理组件\n" +
                    $"• Rigidbody + Collider + CharacterJoint\n\n" +
                    "注意：已设置所有 Rigidbody 为 isKinematic=true（运行时控制）",
                    "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex, go);
                EditorUtility.DisplayDialog("Ragdoll 配置失败", ex.Message, "OK");
            }
        }

        /// <summary>返回 Ragdoll 自动装配前置检查结果；空字符串表示通过。</summary>
        public static string ValidateTarget(GameObject go)
        {
            if (go == null) return "Ragdoll 目标角色为空";
            var animator = go.GetComponent<Animator>();
            if (animator == null) return $"角色 {go.name} 没有 Animator 组件";
            if (!animator.isHuman) return $"角色 {go.name} 不是 Humanoid Avatar";

            var missing = new List<string>();
            foreach (var bone in new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Head
            })
            {
                if (animator.GetBoneTransform(bone) == null)
                    missing.Add(bone.ToString());
            }
            return missing.Count == 0
                ? string.Empty
                : $"Humanoid Avatar 缺少 Ragdoll 必需骨骼：{string.Join("、", missing)}";
        }

        /// <summary>为指定角色执行装配，不依赖 Selection，供 Character Kit 直接调用。</summary>
        public static int Execute(GameObject go)
        {
            string validation = ValidateTarget(go);
            if (!string.IsNullOrEmpty(validation))
                throw new System.InvalidOperationException(validation);

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                throw new System.InvalidOperationException($"角色 {go.name} 没有 Animator 组件");

            Undo.RegisterFullObjectHierarchyUndo(go, "Auto Ragdoll Setup");
            int created = SetupRagdoll(animator, go);
            EditorUtility.SetDirty(go);
            return created;
        }

        // ═══════════════════════════════════════════════════════════════
        // Ragdoll 骨骼配置数据
        // ═══════════════════════════════════════════════════════════════

        struct BoneConfig
        {
            public HumanBodyBones bone;
            public HumanBodyBones? connectedBone; // null = root (connected to character collider)
            public float mass;
            public ColliderType colliderType;
            public Vector3 colliderSize;   // capsule: (radius, height, 0), box: (x,y,z)
            public Vector3 colliderCenter;
            public int colliderDirection;   // capsule direction: 0=X, 1=Y, 2=Z
            // Joint limits
            public float lowTwist, highTwist;
            public float swing1, swing2;
        }

        enum ColliderType { Capsule, Box }

        static BoneConfig[] GetBoneConfigs()
        {
            return new BoneConfig[]
            {
                // Hips (root of ragdoll)
                new BoneConfig {
                    bone = HumanBodyBones.Hips,
                    connectedBone = null,
                    mass = 3.5f,
                    colliderType = ColliderType.Box,
                    colliderSize = new Vector3(0.25f, 0.15f, 0.2f),
                    colliderCenter = Vector3.zero,
                    lowTwist = 0, highTwist = 0, swing1 = 0, swing2 = 0,
                },
                // Spine
                new BoneConfig {
                    bone = HumanBodyBones.Spine,
                    connectedBone = HumanBodyBones.Hips,
                    mass = 3f,
                    colliderType = ColliderType.Box,
                    colliderSize = new Vector3(0.22f, 0.18f, 0.18f),
                    colliderCenter = new Vector3(0, 0.09f, 0),
                    lowTwist = -20, highTwist = 20, swing1 = 10, swing2 = 15,
                },
                // Chest
                new BoneConfig {
                    bone = HumanBodyBones.Chest,
                    connectedBone = HumanBodyBones.Spine,
                    mass = 2.5f,
                    colliderType = ColliderType.Box,
                    colliderSize = new Vector3(0.25f, 0.2f, 0.18f),
                    colliderCenter = new Vector3(0, 0.1f, 0),
                    lowTwist = -20, highTwist = 20, swing1 = 10, swing2 = 15,
                },
                // Head
                new BoneConfig {
                    bone = HumanBodyBones.Head,
                    connectedBone = HumanBodyBones.Chest,
                    mass = 1f,
                    colliderType = ColliderType.Capsule,
                    colliderSize = new Vector3(0.08f, 0.16f, 0),
                    colliderCenter = new Vector3(0, 0.08f, 0),
                    colliderDirection = 1,
                    lowTwist = -40, highTwist = 40, swing1 = 30, swing2 = 30,
                },
                // Left Upper Arm
                new BoneConfig {
                    bone = HumanBodyBones.LeftUpperArm,
                    connectedBone = HumanBodyBones.Chest,
                    mass = 1.2f,
                    colliderType = ColliderType.Capsule,
                    colliderSize = new Vector3(0.04f, 0.22f, 0),
                    colliderCenter = new Vector3(0, -0.11f, 0),
                    colliderDirection = 1,
                    lowTwist = -70, highTwist = 10, swing1 = 80, swing2 = 50,
                },
                // Left Lower Arm
                new BoneConfig {
                    bone = HumanBodyBones.LeftLowerArm,
                    connectedBone = HumanBodyBones.LeftUpperArm,
                    mass = 0.8f,
                    colliderType = ColliderType.Capsule,
                    colliderSize = new Vector3(0.035f, 0.2f, 0),
                    colliderCenter = new Vector3(0, -0.1f, 0),
                    colliderDirection = 1,
                    lowTwist = -90, highTwist = 0, swing1 = 5, swing2 = 5,
                },
                // Right Upper Arm
                new BoneConfig {
                    bone = HumanBodyBones.RightUpperArm,
                    connectedBone = HumanBodyBones.Chest,
                    mass = 1.2f,
                    colliderType = ColliderType.Capsule,
                    colliderSize = new Vector3(0.04f, 0.22f, 0),
                    colliderCenter = new Vector3(0, -0.11f, 0),
                    colliderDirection = 1,
                    lowTwist = -70, highTwist = 10, swing1 = 80, swing2 = 50,
                },
                // Right Lower Arm
                new BoneConfig {
                    bone = HumanBodyBones.RightLowerArm,
                    connectedBone = HumanBodyBones.RightUpperArm,
                    mass = 0.8f,
                    colliderType = ColliderType.Capsule,
                    colliderSize = new Vector3(0.035f, 0.2f, 0),
                    colliderCenter = new Vector3(0, -0.1f, 0),
                    colliderDirection = 1,
                    lowTwist = -90, highTwist = 0, swing1 = 5, swing2 = 5,
                },
                // Left Upper Leg
                new BoneConfig {
                    bone = HumanBodyBones.LeftUpperLeg,
                    connectedBone = HumanBodyBones.Hips,
                    mass = 2f,
                    colliderType = ColliderType.Capsule,
                    colliderSize = new Vector3(0.06f, 0.35f, 0),
                    colliderCenter = new Vector3(0, -0.18f, 0),
                    colliderDirection = 1,
                    lowTwist = -20, highTwist = 70, swing1 = 30, swing2 = 30,
                },
                // Left Lower Leg
                new BoneConfig {
                    bone = HumanBodyBones.LeftLowerLeg,
                    connectedBone = HumanBodyBones.LeftUpperLeg,
                    mass = 1.2f,
                    colliderType = ColliderType.Capsule,
                    colliderSize = new Vector3(0.05f, 0.3f, 0),
                    colliderCenter = new Vector3(0, -0.15f, 0),
                    colliderDirection = 1,
                    lowTwist = -80, highTwist = 0, swing1 = 5, swing2 = 5,
                },
                // Right Upper Leg
                new BoneConfig {
                    bone = HumanBodyBones.RightUpperLeg,
                    connectedBone = HumanBodyBones.Hips,
                    mass = 2f,
                    colliderType = ColliderType.Capsule,
                    colliderSize = new Vector3(0.06f, 0.35f, 0),
                    colliderCenter = new Vector3(0, -0.18f, 0),
                    colliderDirection = 1,
                    lowTwist = -20, highTwist = 70, swing1 = 30, swing2 = 30,
                },
                // Right Lower Leg
                new BoneConfig {
                    bone = HumanBodyBones.RightLowerLeg,
                    connectedBone = HumanBodyBones.RightUpperLeg,
                    mass = 1.2f,
                    colliderType = ColliderType.Capsule,
                    colliderSize = new Vector3(0.05f, 0.3f, 0),
                    colliderCenter = new Vector3(0, -0.15f, 0),
                    colliderDirection = 1,
                    lowTwist = -80, highTwist = 0, swing1 = 5, swing2 = 5,
                },
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // 配置逻辑
        // ═══════════════════════════════════════════════════════════════

        static int SetupRagdoll(Animator animator, GameObject root)
        {
            var configs = GetBoneConfigs();
            int count = 0;

            // 计算缩放因子（基于角色实际身高适配碰撞体大小）
            float scale = EstimateCharacterScale(animator);

            // 存储已配置的骨骼 Rigidbody 用于 Joint 连接
            var rigidbodies = new Dictionary<HumanBodyBones, Rigidbody>();

            foreach (var config in configs)
            {
                Transform boneTransform = animator.GetBoneTransform(config.bone);
                if (boneTransform == null)
                {
                    Debug.LogWarning($"[Ragdoll] 找不到骨骼: {config.bone}，跳过");
                    continue;
                }

                // Ragdoll 骨骼碰撞体不能留在 Default 层，否则 CharacterMotor 的
                // Ground/SphereCast、点击移动和战斗射线会命中角色自身。
                int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
                if (ignoreRaycastLayer >= 0)
                    boneTransform.gameObject.layer = ignoreRaycastLayer;

                // 已有配置的骨骼 — 更新关键参数确保性能优化
                var existingRb = boneTransform.GetComponent<Rigidbody>();
                var existingCol = boneTransform.GetComponent<Collider>();
                if (existingRb != null && existingCol != null)
                {
                    existingRb.isKinematic = true;
                    existingRb.detectCollisions = false;
                    existingRb.useGravity = false;
                    existingCol.isTrigger = true;
                    rigidbodies[config.bone] = existingRb;
                    count++;
                    continue;
                }

                // --- Rigidbody ---
                var rb = boneTransform.GetComponent<Rigidbody>();
                if (rb == null) rb = boneTransform.gameObject.AddComponent<Rigidbody>();
                rb.mass = config.mass;
                rb.isKinematic = true;  // vRagdoll 在运行时控制
                rb.useGravity = false;  // kinematic 状态下不需要重力，激活时 vRagdoll 会处理
                rb.drag = 0.1f;
                rb.angularDrag = 0.5f;
                rb.detectCollisions = false; // 默认关闭碰撞检测，激活 ragdoll 时再开启
                rigidbodies[config.bone] = rb;

                // --- Collider ---
                // 默认设为 Trigger（不参与物理碰撞），vRagdoll 激活时会切换
                if (config.colliderType == ColliderType.Capsule)
                {
                    var col = boneTransform.GetComponent<CapsuleCollider>();
                    if (col == null) col = boneTransform.gameObject.AddComponent<CapsuleCollider>();
                    col.radius = config.colliderSize.x * scale;
                    col.height = config.colliderSize.y * scale;
                    col.center = config.colliderCenter * scale;
                    col.direction = config.colliderDirection;
                    col.isTrigger = true;
                }
                else
                {
                    var col = boneTransform.GetComponent<BoxCollider>();
                    if (col == null) col = boneTransform.gameObject.AddComponent<BoxCollider>();
                    col.size = config.colliderSize * scale;
                    col.center = config.colliderCenter * scale;
                    col.isTrigger = true;
                }

                // --- CharacterJoint (所有非 root 骨骼) ---
                if (config.connectedBone.HasValue)
                {
                    var joint = boneTransform.GetComponent<CharacterJoint>();
                    if (joint == null) joint = boneTransform.gameObject.AddComponent<CharacterJoint>();

                    if (rigidbodies.TryGetValue(config.connectedBone.Value, out Rigidbody connectedRb))
                        joint.connectedBody = connectedRb;

                    // 设置关节限制
                    var lowTwist = joint.lowTwistLimit;
                    lowTwist.limit = config.lowTwist;
                    joint.lowTwistLimit = lowTwist;

                    var highTwist = joint.highTwistLimit;
                    highTwist.limit = config.highTwist;
                    joint.highTwistLimit = highTwist;

                    var swing1 = joint.swing1Limit;
                    swing1.limit = config.swing1;
                    joint.swing1Limit = swing1;

                    var swing2 = joint.swing2Limit;
                    swing2.limit = config.swing2;
                    joint.swing2Limit = swing2;

                    joint.enablePreprocessing = false;
                }

                count++;
            }

            // 确保角色根 CapsuleCollider 忽略 ragdoll collider
            SetupCollisionIgnore(root);

            return count;
        }

        /// <summary>
        /// 估算角色缩放比例（基于 Hips 到 Head 的距离）
        /// </summary>
        static float EstimateCharacterScale(Animator animator)
        {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);

            if (hips == null || head == null) return 1f;

            float height = Vector3.Distance(hips.position, head.position);
            // 标准人形 hips-to-head 约 0.7m，以此为基准
            float scale = height / 0.7f;
            return Mathf.Clamp(scale, 0.3f, 3f);
        }

        /// <summary>
        /// 设置角色根 Collider 忽略 Ragdoll 内部碰撞
        /// </summary>
        static void SetupCollisionIgnore(GameObject root)
        {
            var rootCollider = root.GetComponent<Collider>();
            if (rootCollider == null) return;

            var allColliders = root.GetComponentsInChildren<Collider>(true);
            foreach (var col in allColliders)
            {
                if (col != rootCollider)
                    Physics.IgnoreCollision(rootCollider, col, true);
            }
        }
    }
}

