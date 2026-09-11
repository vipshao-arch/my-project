using UnityEngine;
using System.Collections.Generic;

namespace Game.Character
{
    /// <summary>
    /// SpringBone — Verlet 积分，参考 VRMSpringBone 0.x 原始实现。
    ///
    /// 根因修复：
    ///   披风骨骼无动画 key，动画系统每帧不会覆盖 bone.rotation。
    ///   上帧 spring 写回的 bone.rotation 残留，导致下帧 bindDir / restTail 错误。
    ///   解决：在每帧 SimulateAll 开头，先把每根骨骼恢复到 bind localRotation，
    ///         再叠加"父骨骼本帧旋转变化"（即还原到动画驱动的姿势），
    ///         之后读 bone.rotation 作为 bindDir 才是正确的。
    ///
    ///   具体做法：每帧模拟前，对链上每根骨骼：
    ///     bone.localRotation = initLocalRot
    ///   这样 bone.rotation = parent.rotation * initLocalRot = 动画驱动值（父骨骼由动画驱动，无污染）
    ///   然后再做 Verlet + 旋转写回。
    /// </summary>
    [AddComponentMenu("Game/Character System/Spring Bone Component")]
    public class SpringBoneComponent : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        // 数据
        // ─────────────────────────────────────────────

        [System.Serializable]
        public class SpringChain
        {
            [Tooltip("链根骨骼（随动画走，不参与模拟）")]
            public Transform root;

            [Tooltip("链长，0 = 自动到末端")]
            public int chainLength = 0;

            [Header("物理")]
            [Range(0f, 1f), Tooltip("刚性：越大越快回原位。披风 0.02~0.08，头发 0.05~0.15")]
            public float stiffness = 0.07f;

            [Range(0f, 1f), Tooltip("阻尼：越大摆动越快停。披风 0.3~0.6，头发 0.5~0.8")]
            public float damping = 0.5f;

            [Tooltip("重力（世界 m/s²）。Y=-2 标准披风，-0.5 轻飘，-4 沉重")]
            public Vector3 gravity = new Vector3(0f, -2f, 0f);

            [Tooltip("骨骼碰撞半径")]
            public float radius = 0.03f;

            [Header("碰撞（可选）")]
            public List<SphereColliderDef> sphereColliders = new List<SphereColliderDef>();
        }

        [System.Serializable]
        public class SphereColliderDef
        {
            public Transform bone;
            public float radius = 0.08f;
        }

        // ─────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────

        [Header("骨骼链")]
        public List<SpringChain> springChains = new List<SpringChain>();

        [Header("全局")]
        [Range(0f, 2f)] public float globalStiffness = 1f;
        [Range(0f, 2f)] public float globalGravity   = 1f;

        // ─────────────────────────────────────────────
        // 运行时
        // ─────────────────────────────────────────────

        private class SpringNode
        {
            public Transform  bone;
            public SpringNode parent;      // null = 根节点

            // Verlet：模拟骨骼"子端点"（tail）
            public Vector3 currentTail;   // 当前帧 tail 世界位置
            public Vector3 prevTail;      // 上一帧 tail 世界位置

            // 初始化时记录，运行中不变
            public Quaternion initLocalRot;   // bind pose localRotation
            public Vector3    localChildDir;  // 本骨骼 local 空间下指向子骨骼的方向
            public float      boneLength;     // 骨骼自身长度（本骨骼 → 子骨骼距离）
            public float      radius;
        }

        private List<List<SpringNode>> _chains = new List<List<SpringNode>>();
        private bool _initialized;

        // ─────────────────────────────────────────────
        // 生命周期
        // ─────────────────────────────────────────────

        void OnEnable()  => Initialize();

        void OnDisable()
        {
            // 还原所有骨骼到 bind pose
            RestoreAll();
        }

        void LateUpdate()
        {
            if (!_initialized) Initialize();
            SimulateAll();
        }

        // ─────────────────────────────────────────────
        // 初始化
        // ─────────────────────────────────────────────

        private void Initialize()
        {
            _chains.Clear();
            foreach (var def in springChains)
            {
                if (def.root == null) continue;
                var chain = BuildChain(def);
                if (chain.Count >= 2) _chains.Add(chain);
            }
            _initialized = true;
        }

        private List<SpringNode> BuildChain(SpringChain def)
        {
            // 收集线性骨骼列表
            var bones = new List<Transform>();
            var cur = def.root;
            int limit = def.chainLength > 0 ? def.chainLength : 64;
            while (cur != null && bones.Count < limit)
            {
                bones.Add(cur);
                if (cur.childCount == 0) break;
                cur = cur.GetChild(0);
            }

            var nodes = new List<SpringNode>();
            for (int i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];

                // ── localChildDir：本骨骼 local 空间下指向子骨骼 ──
                Vector3 lcd;
                if (i < bones.Count - 1)
                {
                    // 有真实子骨骼：用 InverseTransformPoint 在 bind pose 下算
                    lcd = bone.InverseTransformPoint(bones[i + 1].position);
                    if (lcd.sqrMagnitude > 0.0001f) lcd = lcd.normalized;
                    else lcd = Vector3.forward;
                }
                else
                {
                    // 末端：沿父→末端方向延伸
                    lcd = i > 0
                        ? bone.InverseTransformDirection((bone.position - bones[i - 1].position).normalized)
                        : Vector3.forward;
                    if (lcd.sqrMagnitude < 0.0001f) lcd = Vector3.forward;
                    lcd = lcd.normalized;
                }

                // ── boneLength ──
                float boneLen = i > 0
                    ? Vector3.Distance(bone.position, bones[i - 1].position)
                    : 0.1f; // 根节点给一个默认值，不参与模拟

                // ── tail 初始位置 ──
                Vector3 initTail = bone.position + bone.rotation * lcd * boneLen;

                nodes.Add(new SpringNode
                {
                    bone          = bone,
                    parent        = i > 0 ? nodes[i - 1] : null,
                    initLocalRot  = bone.localRotation,
                    localChildDir = lcd,
                    boneLength    = boneLen,
                    currentTail   = initTail,
                    prevTail      = initTail,
                    radius        = def.radius,
                });
            }
            return nodes;
        }

        // ─────────────────────────────────────────────
        // 模拟
        // ─────────────────────────────────────────────

        private void SimulateAll()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            for (int ci = 0; ci < _chains.Count && ci < springChains.Count; ci++)
            {
                var nodes = _chains[ci];
                var def   = springChains[ci];
                if (nodes.Count < 2) continue;

                float   stiff = Mathf.Clamp01(def.stiffness * globalStiffness);
                float   damp  = Mathf.Clamp01(def.damping);
                Vector3 grav  = def.gravity * globalGravity;

                // ════════════════════════════════════════
                // 步骤 A：还原到 bind pose（清除上帧 spring 污染）
                //         只还原 spring 管辖的骨骼（index >= 1）
                //         根节点 (index 0) 由动画驱动，不还原
                // ════════════════════════════════════════
                for (int i = 1; i < nodes.Count; i++)
                    if (nodes[i].bone != null)
                        nodes[i].bone.localRotation = nodes[i].initLocalRot;

                // ════════════════════════════════════════
                // 步骤 B：对每根骨骼做 Verlet + 约束 + 旋转写回
                // ════════════════════════════════════════
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node.bone == null) continue;

                    // 根节点不做 Verlet，只同步 tail
                    if (node.parent == null)
                    {
                        // 根节点 tail 每帧从骨骼当前旋转重算（跟随动画）
                        node.currentTail = node.bone.position
                                         + node.bone.rotation * node.localChildDir * node.boneLength;
                        node.prevTail    = node.currentTail;
                        continue;
                    }

                    // ── 步骤 A 后 bone.rotation = parent.rotation * initLocalRot（无污染）──
                    // restTail = 本骨骼在 bind 姿势下的子端点世界位置
                    Vector3 restTail = node.bone.position
                                     + node.bone.rotation * node.localChildDir * node.boneLength;

                    // ── Verlet ──
                    Vector3 vel      = (node.currentTail - node.prevTail) * (1f - damp);
                    Vector3 nextTail = node.currentTail + vel + grav * (dt * dt);

                    // ── stiffness：向 restTail 拉 ──
                    nextTail = Vector3.Lerp(nextTail, restTail, stiff);

                    // ── 长度约束：子端点锁定在本骨骼长度处 ──
                    Vector3 toTail = nextTail - node.bone.position;
                    if (toTail.sqrMagnitude < 0.000001f)
                        toTail = node.bone.rotation * node.localChildDir;
                    nextTail = node.bone.position + toTail.normalized * node.boneLength;

                    // ── 碰撞 ──
                    foreach (var col in def.sphereColliders)
                    {
                        if (col.bone == null) continue;
                        Vector3 diff = nextTail - col.bone.position;
                        float   minD = col.radius + node.radius;
                        if (diff.sqrMagnitude < minD * minD)
                        {
                            float d = diff.magnitude;
                            Vector3 n2 = d > 0.0001f ? diff / d : Vector3.up;
                            nextTail = col.bone.position + n2 * minD;
                            // 重新约束长度
                            toTail = nextTail - node.bone.position;
                            if (toTail.sqrMagnitude < 0.000001f)
                                toTail = node.bone.rotation * node.localChildDir;
                            nextTail = node.bone.position + toTail.normalized * node.boneLength;
                        }
                    }

                    // ── 保存 tail ──
                    node.prevTail    = node.currentTail;
                    node.currentTail = nextTail;

                    // ── 旋转写回 ──
                    // bindDir：步骤 A 还原后的骨骼朝向子端点方向（无污染）
                    Vector3 bindDir = node.bone.rotation * node.localChildDir;
                    // simDir：本帧模拟的子端点方向
                    Vector3 simDir  = nextTail - node.bone.position;

                    if (bindDir.sqrMagnitude < 0.000001f || simDir.sqrMagnitude < 0.000001f) continue;

                    // rot: bindDir → simDir
                    Quaternion rot = Quaternion.FromToRotation(bindDir.normalized, simDir.normalized);
                    // 叠加到骨骼（在 bind pose 基础上偏转）
                    node.bone.rotation = rot * node.bone.rotation;
                }
            }
        }

        // ─────────────────────────────────────────────
        // 辅助
        // ─────────────────────────────────────────────

        private void RestoreAll()
        {
            foreach (var chain in _chains)
                foreach (var n in chain)
                    if (n.bone) n.bone.localRotation = n.initLocalRot;
        }

        // ─────────────────────────────────────────────
        // 公开 API
        // ─────────────────────────────────────────────

        [ContextMenu("Reset to Bind Pose")]
        public void ResetToBind()
        {
            RestoreAll();
            foreach (var chain in _chains)
                foreach (var n in chain)
                    if (n.bone)
                    {
                        n.currentTail = n.bone.position + n.bone.rotation * n.localChildDir * n.boneLength;
                        n.prevTail    = n.currentTail;
                    }
        }

        public void Reinitialize()
        {
            _initialized = false;
        }

        // ─────────────────────────────────────────────
        // Gizmos
        // ─────────────────────────────────────────────

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!_initialized) return;
            for (int ci = 0; ci < _chains.Count && ci < springChains.Count; ci++)
            {
                var nodes = _chains[ci];
                var def   = springChains[ci];

                Gizmos.color = Color.cyan;
                for (int i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    if (n.bone == null) continue;
                    Gizmos.DrawWireSphere(n.currentTail, def.radius);
                    Gizmos.DrawLine(n.bone.position, n.currentTail);
                }

                Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
                foreach (var col in def.sphereColliders)
                    if (col.bone) Gizmos.DrawWireSphere(col.bone.position, col.radius);
            }
        }
#endif
    }
}
