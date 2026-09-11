// =====================================================================
//  SkillPreviewRuntime —— 事件驱动层(V3 计划 §2.3 / §4.3)
//
//  核心职责:
//   1. 每帧由主 wizard OnGUI 末尾调 Tick(t, dt):
//      - 按 [lastT, t] 区间遍历 graphData 节点,找到 triggerTime 命中的节点
//      - 对命中节点调 TriggerNode:触发 VFX 实例化 / SFX 静音播放 / 命中框
//   2. VFX 实例池:HideAndDontSave + 30s 自动回收
//   3. SFX 池:临时 AudioSource(go.HideAndDontSave)+ SFXMute 标记
//   4. 命中框池:0.5s 淡出,DrawHitGizmos 走 Handles API
//   5. 反射缓存:启动期按 Type → FieldInfo[] 建索引,运行时零 GC
//
//  注意:
//   - 所有反射读字段都允许"字段不存在" = 静默返回,适配节点类型差异
//   - 反射读 graphData 节点 triggerTime / DamageNode.radius 走缓存
//   - 技能配置走 _workingCopy.Working(双轨下可能与 _editingTarget 不同)
// =====================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Game.SkillSystem.EditorTools
{
    public class SkillPreviewRuntime
    {
        // ── 公共状态 ──
        public GameObject PreviewInstance;
        public WorkingCopySkillData Working;
        public double CurrentT { get; private set; }
        public double LastTickT { get; private set; }
        public bool IsPlaying { get; set; }
        // P1 时间单位收敛:预览侧也需要 clipLength 来转换归一化 frontSwing → 秒。
        // 由 SkillBuilderWizard.StartPreviewPlayback() 在预览启动时设置。
        public float PreviewClipLength = 1f;
        public bool SFXMute = true;     // 主 wizard 注入,默认静音

        // ── 事件总线 ──
        public event Action<SkillVFXEvent>  OnVFXTriggered;
        public event Action<SkillSFXEvent>  OnSFXTriggered;
        public event Action<SkillHitEvent>  OnHitTriggered;

        // ── 实例池 ──
        private const float VFX_AUTO_DESTROY = 30f;
        private readonly List<VFXHandle>      _liveVFX        = new List<VFXHandle>();
        private readonly List<SFXHandle>      _liveSFX        = new List<SFXHandle>();
        private readonly List<HitGizmoHandle> _liveHits       = new List<HitGizmoHandle>();
        private const float HIT_FADE_DURATION = 0.5f;

        // ── 反射缓存 ──
        private static readonly Dictionary<Type, FieldInfo[]> _fieldCache = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<string, FieldInfo>  _fieldByNameCache = new Dictionary<string, FieldInfo>();

        // ── 执行层统一(2026-07-21):预览侧 NodeContext 与 Sink ──
        // 节点触发直接调 SkillNodeData.OnPreviewTrigger(_ctx),
        // 效果落地(VFX 池/静音 SFX/命中 Gizmo)由 PreviewSkillEffectSink 回到本类池方法。
        private NodeContext _ctx;
        private PreviewSkillEffectSink _sink;

        // ── 句柄类型 ──
        struct SFXHandle
        {
            public GameObject Go;
            public AudioSource Src;
            public double ExpireT;
        }
        struct HitGizmoHandle
        {
            public SkillHitEvent Event;
            public double T0;
        }
        // P1-6: VFXHandle 记录 destroyAfterSeconds,让 TickVFX 按配置时间销毁(不再硬编码 30s)
        struct VFXHandle
        {
            public GameObject Go;
            public double SpawnWallTime;     // EditorApplication.timeSinceStartup 出生时间
            public double SpawnPreviewTime;  // 预览时间 t(用于动画时间基准的销毁)
            public float AutoDestroySec;     // >0=按此秒数从 SpawnPreviewTime 算销毁, <=0=用 VFX_AUTO_DESTROY(30s) 兜底
        }

        // P2 VFX 收敛:legacy 顶层 VFX/SFX 已移除 —— 所有 VFX/SFX 现在仅从 graphData 节点触发。
        //   移除原因:graphData 节点路径(TriggerNode)已覆盖 VFX+SFX,legacy 路径会造成同一特效播放两次。

        // P2-1: FindBone 缓存,避免每次 VFX 触发全量 GetComponentsInChildren
        private Dictionary<string, Transform> _boneCache;
        private GameObject _cachedBoneRoot;

        // ════════════════════════════════════════════════════════════
        // 生命周期
        // ════════════════════════════════════════════════════════════
        public void Init(GameObject previewInstance, WorkingCopySkillData copy)
        {
            PreviewInstance = previewInstance;
            Working = copy;

            // 2026-07-21 执行层统一：初始化 NodeContext 与 Sink，
            // 使 Tick() 中 _ctx != null 守卫通过，节点 OnPreviewTrigger 正常触发 VFX。
            if (_ctx == null)
            {
                _ctx = new NodeContext();
                _sink = new PreviewSkillEffectSink(this);
            }
            _ctx.isPreview = true;
            _ctx.sink    = _sink;
            _ctx.caster  = (previewInstance != null && previewInstance) ? previewInstance.transform : null;
            _ctx.source  = (previewInstance != null && previewInstance) ? previewInstance : null;
            _ctx.ResetPerCast();
        }

        /// <summary>同步预览受击目标(2026-08-03):HitVFX 等"命中目标"语义节点的预览落点。</summary>
        public void SetPreviewHitTargets(List<GameObject> targets)
        {
            if (_ctx == null) return;
            if (_ctx.previewHitTargets == null)
                _ctx.previewHitTargets = new List<Transform>();
            _ctx.previewHitTargets.Clear();
            if (targets == null) return;
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null)
                    _ctx.previewHitTargets.Add(targets[i].transform);
        }

        public void Dispose()
        {
            // 停 SFX
            for (int i = 0; i < _liveSFX.Count; i++)
            {
                if (_liveSFX[i].Src != null) _liveSFX[i].Src.Stop();
                if (_liveSFX[i].Go != null) UnityEngine.Object.DestroyImmediate(_liveSFX[i].Go);
            }
            _liveSFX.Clear();
            // 销毁 VFX
            for (int i = 0; i < _liveVFX.Count; i++)
                if (_liveVFX[i].Go != null) UnityEngine.Object.DestroyImmediate(_liveVFX[i].Go);
            _liveVFX.Clear();
            _liveHits.Clear();
            // P2-1: 清空骨骼缓存(实例已销毁或即将销毁)
            _boneCache?.Clear();
            _cachedBoneRoot = null;
        }

        public void Reset()
        {
            Dispose();
            // P1-6 v5: LastTickT 初始化为 -0.001,确保 triggerTime=0 的 VFX 首帧
            // (fireT=0) 能穿越区间 >lastT(-0.001) && <=t(0),对齐游戏侧 _animTimer>=0 触发。
            LastTickT = -0.001;
            CurrentT = 0;
        }

        public void Seek(double t)
        {
            // 不再 Dispose VFX 池,避免拖拽播头时 VFX 瞬间消失。
            // 旧 VFX 保留,由粒子自身生命周期自然消亡;
            // 前进拖拽时 Tick 的区间判定只触发新事件,不会重复生成。
            LastTickT = t;
            CurrentT = t;
            // 保留 _topLevelXxxFired 不重置,防止回拖后前进时重复生成 Cast VFX/SFX
        }

        // V3.1.6:离屏场景粒子系统需要手动 Simulate(Editor 不会自动推进 ParticleSystem)
        // P1-6 fix 1: withChildren=false — GetComponentsInChildren 已遍历全层级.
        // P1-6 fix 2: 去掉 isPlaying 检查 — Unity 的 Simulate(t) 在模拟 t 秒后会
        //  暂停 PS(isPlaying→false),若检查 isPlaying 则第二帧起不再推进,PS 永远停在
        //  第一帧模拟后的时间点。直接用无条件 Simulate 逐帧累进推进。
        // P1-6 fix 3: 结合 OnPreviewVFX 用 Simulate(0,restart) 代替 Play() 初始化,
        //  避免 Unity Editor 内部自动推进(PreviewRenderUtility 下 Play() 仍可能被
        //  Editor 循环驱动).
        public void SimulateVFX(float dt)
        {
            for (int i = _liveVFX.Count - 1; i >= 0; i--)
            {
                var go = _liveVFX[i].Go;
                if (go == null) { _liveVFX.RemoveAt(i); continue; }
                var pss = go.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in pss)
                {
                    // P4:withChildren=true 确保子发射器/拖尾也同步模拟(与运行时内置循环一致)
                    ps.Simulate(dt, true, false);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        // 核心 Tick
        // ════════════════════════════════════════════════════════════
        public void Tick(double t, double dt)
        {
            if (Working == null || Working.Working == null) return;
            var data = Working.Working;
            if (data == null) return;

            double lastT = LastTickT;

            // P1-2 防御:时间倒退(t < lastT)走 Seek 语义——只更新位置,不触发 VFX。
            // 正常的区间判定 nodeT > lastT && nodeT <= t 在倒退时永远为空,
            // 强行 Tick 只会污染 LastTickT(设成更小的 t)导致后续帧错位。
            // 典型触发场景:Animator 路径暂停→继续时 StartAnimatorPlayback 重置
            // _previewAnimElapsed=0 但 Runtime 未 Reset(已在上层修复,此处兜底)。
            if (t < lastT - 0.0001)
            {
                Seek(t);
                TickVFX(dt);
                TickSFX(dt);
                TickHitGizmos(dt);
                return;
            }

            LastTickT = t;
            CurrentT = t;

            // P2 VFX 收敛:所有 VFX/SFX 仅从 graphData 节点触发,不再读取 legacy 顶层字段
            // (vfxOnCast/vfxOnHit/sfxOnCast/sfxOnHit/vfxEntries),避免同一特效播放两次。

            // graphData 节点 — 2026-07-21 执行层统一:
            // 触发时刻由节点 GetTriggerTime(skill, clipLength) 提供,
            // 触发行为由节点 OnPreviewTrigger(ctx) 执行(与运行时同一份行为代码)。
            if (data.graphData != null && _ctx != null)
            {
                _ctx.skill = data;
                // 每次 Tick 前刷新 caster/source（previewInstance 可能在 Init 后才被 RebuildPreviewInstance 创建）
                if (PreviewInstance != null && PreviewInstance)
                {
                    _ctx.caster = PreviewInstance.transform;
                    _ctx.source = PreviewInstance;
                }
                if (_ctx.caster != null)
                {
                    for (int i = 0; i < data.graphData.Count; i++)
                    {
                        var node = data.graphData[i];
                        if (node == null) continue;
                        double nodeT = node.GetTriggerTime(data, PreviewClipLength);
                        // 区间判定:(lastT, t] — 防 dt=0 漏触发,也防倒退重发
                        if (nodeT > lastT && nodeT <= t)
                        {
                            _ctx.currentNodeIndex = i;
                            _ctx.animTime = (float)t;
                            node.OnPreviewTrigger(_ctx);
                        }
                    }
                    _ctx.currentNodeIndex = -1;
                }
            }

            // 池推进
            TickVFX(dt);
            TickSFX(dt);
            TickHitGizmos(dt);
        }

        // ════════════════════════════════════════════════════════════
        // 池方法 — 供 PreviewSkillEffectSink 回调(2026-07-21 执行层统一)
        //
        // 节点行为(SkillNodeData.Behaviors)经 ctx.sink 调用到这里。
        // 原 TriggerNode 字符串 switch / EmitVFXFromNode 已删除:
        // 触发逻辑与运行时共用同一份节点行为代码,此处只保留效果落地。
        // ════════════════════════════════════════════════════════════

        /// <summary>解析预览实例的挂点骨骼(带缓存),找不到退回预览根。</summary>
        internal Transform ResolveBone(string boneName)
        {
            if (PreviewInstance == null) return null;
            if (string.IsNullOrEmpty(boneName)) return PreviewInstance.transform;
            var found = FindBone(PreviewInstance.transform, boneName);
            return found != null ? found : PreviewInstance.transform;
        }

        /// <summary>离屏池生成 VFX(HideAndDontSave + 手动 Simulate 注册)。</summary>
        internal GameObject PooledSpawnVFX(GameObject prefab, Vector3 pos, Quaternion rot,
                                           float scale, Transform parent, float destroyAfter)
        {
            if (PreviewInstance == null || prefab == null) return null;
            var go = (GameObject)UnityEngine.Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.name = $"VFXNode_{prefab.name}_{go.GetHashCode()}";
            go.transform.position = pos;
            go.transform.rotation = rot;
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: true);
            if (scale != 1f) go.transform.localScale = go.transform.localScale * scale;

            double t = CurrentT;
            _liveVFX.Add(new VFXHandle {
                Go = go,
                SpawnWallTime = EditorApplication.timeSinceStartup,
                SpawnPreviewTime = t,
                AutoDestroySec = destroyAfter
            });
            OnVFXTriggered?.Invoke(new SkillVFXEvent(
                t, prefab, go, pos, rot, scale, "", destroyAfter,
                parent == null, parent != null ? parent : PreviewInstance.transform));
            return go;
        }

        /// <summary>静音池播放 SFX(保留时长推进语义)。</summary>
        internal void PooledPlaySFX(AudioClip clip, float pitchRandomPercent)
        {
            EmitSFX(CurrentT, clip, 1f, pitchRandomPercent);
        }

        /// <summary>命中判定 Gizmo 入池(0.5s 淡出,Handles 绘制)。</summary>
        internal void PooledEmitHitGizmo(SkillNodeData node, Vector3 center, float radius,
                                         float angleDeg, float durationHint)
        {
            EmitHit(CurrentT, node, center, radius, angleDeg, true, 1f, durationHint);
        }

        void EmitHit(double t, SkillNodeData node, Vector3 center, float radius, float angle,
                     bool dealsDamage, float multiplier, float durationHint)
        {
            var e = new SkillHitEvent(t, node, center, radius, angle, dealsDamage, multiplier, durationHint);
            _liveHits.Add(new HitGizmoHandle { Event = e, T0 = t });
            OnHitTriggered?.Invoke(e);
        }

        void EmitSFX(double t, AudioClip clip, float volume, float pitchRandomPercent)
        {
            if (clip == null) return;
            var go = new GameObject("PreviewSFX_" + clip.name);
            go.hideFlags = HideFlags.HideAndDontSave;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = 1f + UnityEngine.Random.Range(-pitchRandomPercent, pitchRandomPercent) * 0.01f;
            src.spatialBlend = 0f;
            src.mute = SFXMute;
            src.Play();
            _liveSFX.Add(new SFXHandle { Go = go, Src = src, ExpireT = CurrentT + clip.length + 0.1 });
            OnSFXTriggered?.Invoke(new SkillSFXEvent(t, clip, volume, pitchRandomPercent));
        }

        void TickVFX(double dt)
        {
            double now = EditorApplication.timeSinceStartup;
            for (int i = _liveVFX.Count - 1; i >= 0; i--)
            {
                var h = _liveVFX[i];
                if (h.Go == null) { _liveVFX.RemoveAt(i); continue; }
                // P1-6: 按 destroyAfterSeconds 配置时间销毁(有配置用配置,无配置用 30s 兜底)
                float destroySec = h.AutoDestroySec > 0f ? h.AutoDestroySec : VFX_AUTO_DESTROY;
                if ((now - h.SpawnWallTime) > destroySec)
                {
                    UnityEngine.Object.DestroyImmediate(h.Go);
                    _liveVFX.RemoveAt(i);
                }
            }
        }

        void TickSFX(double dt)
        {
            for (int i = _liveSFX.Count - 1; i >= 0; i--)
            {
                var h = _liveSFX[i];
                if (h.Src == null || !h.Src.isPlaying)
                {
                    if (h.Go != null) UnityEngine.Object.DestroyImmediate(h.Go);
                    _liveSFX.RemoveAt(i);
                }
            }
        }

        void TickHitGizmos(double dt)
        {
            for (int i = _liveHits.Count - 1; i >= 0; i--)
            {
                float age = (float)(CurrentT - _liveHits[i].T0);
                if (age > HIT_FADE_DURATION) _liveHits.RemoveAt(i);
            }
        }

        // ════════════════════════════════════════════════════════════
        // 命中框 Gizmo 绘制(由主 wizard 在 DrawPreviewView 末尾调)
        // ════════════════════════════════════════════════════════════
        public void DrawHitGizmos()
        {
            for (int i = 0; i < _liveHits.Count; i++)
            {
                var h = _liveHits[i];
                float age = (float)(CurrentT - h.T0);
                float alpha = Mathf.Clamp01(1f - age / HIT_FADE_DURATION);
                Handles.color = new Color(1f, 0.3f, 0.3f, alpha);
                Handles.DrawWireDisc(h.Event.Center, Vector3.up, h.Event.Radius);
                if (h.Event.Angle < 360f)
                {
                    Vector3 fwd = PreviewInstance != null ? PreviewInstance.transform.forward : Vector3.forward;
                    Handles.DrawSolidArc(h.Event.Center, Vector3.up, fwd, h.Event.Angle, h.Event.Radius);
                    Handles.DrawSolidArc(h.Event.Center, Vector3.up, fwd, -h.Event.Angle, h.Event.Radius);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        // 反射辅助
        // ════════════════════════════════════════════════════════════
        public static double GetNodeTriggerTime(SkillNodeData node)
        {
            if (node == null) return 0;
            // 优先 triggerTime 字段
            float t = GetFloatField(node, "triggerTime", 0f);
            return t;
        }

        public static float GetFloatField(object obj, string name, float defVal = 0f)
        {
            if (obj == null) return defVal;
            var fi = GetFieldInfo(obj.GetType(), name);
            if (fi == null) return defVal;
            try { return Convert.ToSingle(fi.GetValue(obj)); } catch { return defVal; }
        }

        public static int GetIntField(object obj, string name, int defVal = 0)
        {
            if (obj == null) return defVal;
            var fi = GetFieldInfo(obj.GetType(), name);
            if (fi == null) return defVal;
            try { return Convert.ToInt32(fi.GetValue(obj)); } catch { return defVal; }
        }

        public static bool GetBoolField(object obj, string name, bool defVal = false)
        {
            if (obj == null) return defVal;
            var fi = GetFieldInfo(obj.GetType(), name);
            if (fi == null) return defVal;
            try { return Convert.ToBoolean(fi.GetValue(obj)); } catch { return defVal; }
        }

        // V4 修复2:补 string 字段反射读取(节点 spawnBone 用)
        public static string GetStringField(object obj, string name, string defVal = "")
        {
            if (obj == null) return defVal;
            var fi = GetFieldInfo(obj.GetType(), name);
            if (fi == null) return defVal;
            try { return fi.GetValue(obj) as string ?? defVal; } catch { return defVal; }
        }

        public static Vector3 GetVector3Field(object obj, string name)
        {
            if (obj == null) return Vector3.zero;
            var fi = GetFieldInfo(obj.GetType(), name);
            if (fi == null) return Vector3.zero;
            try { return (Vector3)fi.GetValue(obj); } catch { return Vector3.zero; }
        }

        public static T GetObjectField<T>(object obj, string name) where T : class
        {
            if (obj == null) return null;
            var fi = GetFieldInfo(obj.GetType(), name);
            if (fi == null) return null;
            try { return fi.GetValue(obj) as T; } catch { return null; }
        }

        static FieldInfo GetFieldInfo(Type t, string name)
        {
            string key = t.FullName + "::" + name;
            if (_fieldByNameCache.TryGetValue(key, out var cached)) return cached;
            // 沿继承链找
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                var fi = cur.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                {
                    _fieldByNameCache[key] = fi;
                    return fi;
                }
            }
            return null;
        }

        Vector3 GetPreviewInstancePos()
            => PreviewInstance != null ? PreviewInstance.transform.position : Vector3.zero;

        Vector3 GetForwardDir()
            => PreviewInstance != null ? PreviewInstance.transform.forward : Vector3.forward;

        Transform FindBone(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            // P1-6 v5: 对齐游戏侧 ResolveSpawnBone — HumanBodyBones 枚举优先,再用名称查找
            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.isHuman &&
                System.Enum.TryParse<HumanBodyBones>(name, true, out HumanBodyBones hbb))
            {
                var t = animator.GetBoneTransform(hbb);
                if (t != null) return t;
            }
            // P2-1: 实例级缓存 — 首次调用或实例变化时重建索引,后续直接查表
            // 注:这是静态方法但通过 _boneCache 实例字段访问,调用链保证 this 可达
            var rt = root.gameObject;
            if (_boneCache == null || _cachedBoneRoot != rt)
            {
                _boneCache = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
                _cachedBoneRoot = rt;
                var all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    string n = all[i].name;
                    if (!_boneCache.ContainsKey(n))
                        _boneCache[n] = all[i];
                }
            }
            // 精确匹配
            if (_boneCache.TryGetValue(name, out var exact)) return exact;

            // 与运行时 CastVFXNode / MidVFXNode 保持相同优先级：角色骨骼找不到时，
            // 再查实际装备武器子级的 VFX 挂点（VFX_Muzzle / VFX_BladeTip 等）。
            // 预览实例不依赖 WeaponHolder 的 Awake/Start，直接从已挂载的预览武器层级查找。
            var previewWeapons = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < previewWeapons.Length; i++)
            {
                var candidate = previewWeapons[i];
                if (candidate.name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && IsPreviewWeaponDescendant(candidate))
                    return candidate;
            }
            return null;
        }

        static bool IsPreviewWeaponDescendant(Transform candidate)
        {
            for (var current = candidate; current != null; current = current.parent)
            {
                if (current.name.StartsWith("PreviewWeapon_", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
