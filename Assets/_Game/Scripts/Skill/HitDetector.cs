using UnityEngine;
using System.Collections.Generic;
using Game.Character;
using Game.SkillSystem;

namespace Game.SkillSystem
{
    /// <summary>
    /// 技能命中检测器。挂在角色上，监听 SkillAnimPlayer 的 OnHitFrame 事件，
    /// 将命中帧分发到 graphData 中的各 SkillNode 执行。
    ///
    /// 方案 C（全 graphData）：所有判定/伤害/VFX 均由节点驱动，HitDetector 只做分发。
    ///
    /// 命中过滤：
    ///   - 不伤害自身
    ///   - 通过 hitMask 指定可命中的 Layer
    ///   - 同一次攻击同一目标只命中一次（_hitTargets 缓存）
    /// </summary>
    [RequireComponent(typeof(SkillAnimPlayer))]
    [AddComponentMenu("Game/Skill System/Hit Detector")]
    public class HitDetector : MonoBehaviour
    {
        [Header("Hit Settings")]
        [Tooltip("可被命中的 Layer Mask。默认包含 Enemy 层。")]
        public LayerMask hitMask = 0;

        [Tooltip("是否在 Start 时自动设置 hitMask 为全层(不再排除自身层,PvP 需命中同层玩家)")]
        public bool autoSetupHitMask = true;

        [Header("Debug")]
        public bool debugDraw = false;

        // ── 运行时缓存 ──────────────────────────────────────────────────
        private HashSet<GameObject> _hitTargets = new HashSet<GameObject>();
        private SkillData _currentSkillData;

        private SkillAnimPlayer _animPlayer;
        private SkillController _skillController;

        // ── 生命周期 ────────────────────────────────────────────────────

        void Awake()
        {
            _animPlayer      = GetComponent<SkillAnimPlayer>();
            _skillController = GetComponent<SkillController>();
        }

        void Start()
        {
            if (hitMask == 0)
            {
                // 2026-09-08 PvP 修复：默认全层，不再"排除自身层"。
                // 原逻辑 hitMask = ~0 & ~(1 << selfLayer) 会整体排除 Player 层(Layer 8)，
                // 而所有玩家都在 Layer 8 → 联机 PvP 玩家之间命中检测被过滤、无法互伤。
                // 防自伤已由 SkillNodeBehaviorUtil.IsSelfCollider / 投射物 source 排除兜底。
                hitMask = ~0;
            }
        }

        void OnEnable()
        {
            if (_animPlayer != null)
                _animPlayer.OnHitFrame += OnHitFrame;
        }

        void OnDisable()
        {
            if (_animPlayer != null)
                _animPlayer.OnHitFrame -= OnHitFrame;
        }

        // ── 攻击参数设置 ────────────────────────────────────────────────

        /// <summary>由 SkillController 在开始攻击/技能时调用，启动 SkillRunner。</summary>
        public void SetupAttack(SkillData data)
        {
            _currentSkillData = data;
            _hitTargets.Clear();

            // 节点启动：PlayAnimation 已经把 _animPlayer 标为 pending,
            // 这里拿到完整参数后启动 runner
            if (_animPlayer != null && _animPlayer.IsPendingStart)
            {
                _animPlayer.ClearPendingStart();
                _animPlayer.Runner.StartCast(
                    data, gameObject, 0f, 0f,
                    hitMask, debugDraw, _animPlayer.AnimDuration);
            }
        }

        // ── 命中帧分发 ──────────────────────────────────────────────────

        /// <summary>
        /// 遍历 SkillData.graphData，将命中帧分发到各节点的 OnHit 方法。
        /// 有 triggerTime 的伤害/投射/AOE 节点由 SkillRunner 按时间轴触发，
        /// 命中帧只保留不具备独立时间轴语义的兼容节点。
        /// </summary>
        private void DispatchByNode(SkillData data)
        {
            if (data.graphData == null || data.graphData.Count == 0) return;
            // 2026-07-21:直接遍历 graphData 调行为,不再经 GraphAdapter 重建虚拟节点。
            var ctx = _animPlayer.Runner.Ctx;
            for (int i = 0; i < data.graphData.Count; i++)
            {
                var n = data.graphData[i];
                if (n == null) continue;
                if (n is MeleeSwingData
                    || n is RectShotData
                    || n is BeamData
                    || n is ChainBounceData
                    || n is CurvedProjectileData
                    || n is AOECircularData)
                    continue;
                ctx.currentNodeIndex = i;
                n.OnHit(ctx, default);
            }
            ctx.currentNodeIndex = -1;
        }

        private void OnHitFrame(SkillData data)
        {
            if (data == null) return;
            SetupAttack(data);

            // 方案 C：始终走 graphData 节点路径
            DispatchByNode(data);
        }

        // ── 投射物回调 ──────────────────────────────────────────────────

        /// <summary>供 RectProjectile / VFXProjectile 调用：投射物击中目标时结算伤害。</summary>
        public void OnProjectileHit(GameObject target, float damage)
        {
            if (target == null || target == gameObject) return;
            if (_hitTargets.Contains(target)) return;

            var damageable = target.GetComponentInParent<IDamageable>();
            if (damageable == null || damageable.isDead) return;

            Vector3 hitDir = (target.transform.position - transform.position).normalized;
            damageable.TakeDamage(damage, gameObject, hitDir);
            _hitTargets.Add(target);
        }
    }
}
