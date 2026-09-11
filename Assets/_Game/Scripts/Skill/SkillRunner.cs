using System.Collections.Generic;
using UnityEngine;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 技能执行者：负责驱动 SkillData.graphData 在动画生命周期内的三阶段回调。
    /// 由 HitDetector / SkillAnimPlayer / EnemyAI 在合适时机调用。
    ///
    /// 一个 SkillData 对应一个运行时 Runner 实例（挂在角色身上，由 SkillController 持有）。
    ///
    /// 2026-07-21 执行层统一重构:
    ///   graphData 直接携带行为(SkillNodeData.OnCast/OnTick/OnHit/OnEnd),
    ///   不再经 GraphAdapter 反射建虚拟 ScriptableObject 节点 ——
    ///   消除每次施法 CreateInstance/DestroyImmediate 的 GC 与映射表维护成本。
    ///   效果落地差异由 ctx.sink 屏蔽(运行时=RuntimeSkillEffectSink)。
    /// </summary>
    public class SkillRunner
    {
        private readonly NodeContext _ctx = new NodeContext();
        private SkillData _skill;
        private float _castStartTime;
        private float _animDuration;
        private float _channelAccum;       // 累计 channeled tick 时间
        private float _channelTickInterval;
        private float _channelDuration;
        private ChanneledData _channeled;
        private bool _running;

        // 已按时间轴触发过的命中型节点索引。VFX 节点仍由各自 OnTick 管理，
        // HitVFX 节点只能由真实命中后的 BroadcastHit 触发。
        private readonly HashSet<int> _timelineImpactTriggered = new HashSet<int>();

        // Channeled 期间需要的 hit.targets 缓冲
        private readonly List<GameObject> _channelTargets = new List<GameObject>();

        /// <summary>是否正在执行某个技能。</summary>
        public bool IsRunning => _running;
        public SkillData Current => _skill;

        /// <summary>当前执行上下文（供节点外访问，如 HitDetector 把节点 OnHit 调度时传 ctx）。</summary>
        public NodeContext Ctx => _ctx;

        // ── 生命周期 ─────────────────────────────────────────────

        public void StartCast(SkillData data, GameObject source, float damage, float range, LayerMask hitMask, bool debugDraw, float animDuration, bool visualOnly = false)
        {
            _skill = data;
            _animDuration = animDuration;
            _castStartTime = Time.time;
            _running = true;
            _channelAccum = 0f;

            _ctx.skill       = data;
            _ctx.source      = source;
            _ctx.caster      = source != null ? source.transform : null;
            _ctx.hitMask     = hitMask;
            _ctx.damage      = damage;
            _ctx.range       = range;
            _ctx.animTime    = 0f;
            _ctx.debugDraw   = debugDraw;
            _ctx.isPreview   = false;
            _ctx.visualOnly  = visualOnly;   // S2-5b:远端表现模式
            _ctx.sink        = RuntimeSkillEffectSink.Instance;
            _ctx.ResetPerCast();
            _timelineImpactTriggered.Clear();

            if (data == null || data.graphData == null || data.graphData.Count == 0)
            {
                Debug.LogWarning($"[SkillRunner] graphData empty or null — no VFX/impact nodes will fire! data={data?.name}", source);
            }

            // 找 Channeled 节点（最多一个）
            _channeled = null;
            _channelDuration = 0f;
            _channelTickInterval = 0f;
            if (data != null && data.graphData != null)
            {
                for (int i = 0; i < data.graphData.Count; i++)
                {
                    if (data.graphData[i] is ChanneledData cd)
                    {
                        _channeled = cd;
                        _channelDuration = cd.GetDuration(data);
                        _channelTickInterval = cd.GetTickInterval();
                        break;
                    }
                }
            }

            // OnCast: 全部节点
            ForEachNode(n => n.OnCast(_ctx));
        }

        public void Tick(float animTime)
        {
            if (!_running || _skill == null) return;
            _ctx.animTime = animTime;
            ForEachNode(n => n.OnTick(_ctx));
            TriggerTimelineImpactNodes(animTime);

            // Channeled: 按 tick 周期重复 OnHit（无目标走最近一次缓存）
            if (_channeled != null && _channelTickInterval > 0f)
            {
                _channelAccum += Time.deltaTime;
                while (_channelAccum >= _channelTickInterval)
                {
                    _channelAccum -= _channelTickInterval;
                    // 重入 OnHit：复用上次的 targets
                    if (_channelTargets.Count > 0)
                    {
                        var hit = HitInfo.Many(_channelTargets, _ctx.caster.position, _ctx.caster.forward);
                        _channeled.OnHit(_ctx, hit);
                    }
                }
            }
        }

        public void Hit(HitInfo hit)
        {
            if (!_running || _skill == null) return;
            // 缓存给 channeled 周期使用
            if (hit.targets != null)
            {
                _channelTargets.Clear();
                _channelTargets.AddRange(hit.targets);
            }
            ForEachNode(n => n.OnHit(_ctx, hit));
        }

        private void TriggerTimelineImpactNodes(float animTime)
        {
            if (_skill == null || _skill.graphData == null) return;

            for (int i = 0; i < _skill.graphData.Count; i++)
            {
                if (_timelineImpactTriggered.Contains(i)) continue;
                var node = _skill.graphData[i];
                if (!IsTimelineImpactNode(node)) continue;
                if (animTime < node.GetTriggerTime(_skill, _animDuration)) continue;

                _timelineImpactTriggered.Add(i);
                _ctx.currentNodeIndex = i;
                node.OnHit(_ctx, default);
            }
            _ctx.currentNodeIndex = -1;
        }

        private static bool IsTimelineImpactNode(SkillNodeData node)
        {
            return node is MeleeSwingData
                || node is RectShotData
                || node is BeamData
                || node is ChainBounceData
                || node is CurvedProjectileData
                || node is AOECircularData;
        }

        public void End()
        {
            if (!_running) return;
            ForEachNode(n => n.OnEnd(_ctx));
            _running = false;
            _skill = null;
            _channeled = null;
            _channelTargets.Clear();
            _ctx.currentNodeIndex = -1;
        }

        // ── 工具方法 ─────────────────────────────────────────────

        private void ForEachNode(System.Action<SkillNodeData> act)
        {
            if (_skill == null || _skill.graphData == null) return;
            for (int i = 0; i < _skill.graphData.Count; i++)
            {
                var n = _skill.graphData[i];
                if (n == null) continue;
                _ctx.currentNodeIndex = i;
                act(n);
            }
            _ctx.currentNodeIndex = -1;
        }

        /// <summary>
        /// 由判定节点（MeleeSwing/Beam/AOE）调用，把命中结果广播给同 SkillData 上的所有 HitVFXData。
        /// 2026-07-21:实现迁移至 NodeContext.BroadcastHit(直接遍历 graphData,不再重建虚拟节点)。
        /// 保留本静态入口以兼容既有调用点。
        /// </summary>
        public static void NotifyHit(NodeContext ctx, HitInfo hit)
        {
            if (ctx == null) return;
            ctx.BroadcastHit(hit);
        }
    }
}
