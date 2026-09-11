using UnityEngine;
using System.Collections.Generic;

namespace Game.SkillSystem
{
    /// <summary>
    /// Game 视图运行时攻击/感知范围 GL 叠加渲染。
    ///
    /// 用法：
    ///   // 注册一个"持续型"扇形（感知范围，每帧刷新）
    ///   CombatRangeDebugDraw.RegisterPersistent(id, origin, forward, range, halfAngle, color);
    ///   // 取消注册
    ///   CombatRangeDebugDraw.UnregisterPersistent(id);
    ///
    ///   // 注册一个"闪现型"扇形（命中帧，duration 秒后自动消失）
    ///   CombatRangeDebugDraw.Flash(origin, forward, range, halfAngle, color, duration);
    ///
    /// 所有绘制均在 OnRenderObject 中通过 GL 执行，仅在 Game 视图可见。
    /// debugDraw 为 false 时自动跳过，不影响正式包性能。
    /// </summary>
    [AddComponentMenu("")]   // 不出现在 Add Component 菜单，由代码自动创建
    public class CombatRangeDebugDraw : MonoBehaviour
    {
        // ── 单例 ────────────────────────────────────────────────────────
        private static CombatRangeDebugDraw _instance;

        /// <summary>
        /// 全局总开关：控制攻击/感知范围的 OnRenderObject GL 叠加是否在 Game 视图显示。
        /// 默认 true（与 EnemyAI.debugDraw 行为并联）。设为 false 可一键关掉所有扇形/圆形绘制，
        /// 不影响 EnemyAI.debugDraw 自身行为，下次调试时设回 true 即可恢复。
        /// 用法（Console 或任意脚本）：
        ///     CombatRangeDebugDraw.ShowGameViewDraw = false;
        /// </summary>
        public static bool ShowGameViewDraw = true;

        public static CombatRangeDebugDraw Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[CombatRangeDebugDraw]");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CombatRangeDebugDraw>();
                }
                return _instance;
            }
        }

        // ── 数据结构 ────────────────────────────────────────────────────

        /// <summary>持续型扇形（每帧调用方负责更新 origin/forward）。</summary>
        public struct PersistentShape
        {
            public Vector3 origin;
            public Vector3 forward;
            public float   range;
            public float   halfAngle;   // 度
            public float   yOffset;     // 地面偏移（避免 z-fighting）
            public Color   color;
            public bool    drawCircle;  // 是否额外画全圆（感知范围用）
        }

        /// <summary>闪现型（有生命周期，自动销毁）。</summary>
        private struct FlashShape
        {
            public Vector3 origin;
            public Vector3 forward;
            public float   range;
            public float   halfAngle;
            public float   yOffset;
            public Color   color;
            public float   expireTime;
        }

        private static Dictionary<int, PersistentShape> _persistent = new Dictionary<int, PersistentShape>();
        private static List<FlashShape>                 _flashes    = new List<FlashShape>();

        private static Material _mat;
        private const  int      kArcSegments = 32;

        // ── 公共 API ────────────────────────────────────────────────────

        /// <summary>注册持续型扇形。id 由调用方管理（推荐用 GetInstanceID()）。</summary>
        public static void RegisterPersistent(int id, PersistentShape shape)
        {
            Instance._EnsureInit();
            _persistent[id] = shape;
        }

        /// <summary>更新持续型扇形的 origin/forward（每帧调用）。</summary>
        public static void UpdatePersistent(int id, Vector3 origin, Vector3 forward)
        {
            if (!_persistent.ContainsKey(id)) return;
            var s = _persistent[id];
            s.origin  = origin;
            s.forward = forward;
            _persistent[id] = s;
        }

        /// <summary>取消注册持续型扇形。</summary>
        public static void UnregisterPersistent(int id)
        {
            _persistent.Remove(id);
        }

        /// <summary>注册一个命中帧闪现扇形，duration 秒后自动消失。</summary>
        public static void Flash(Vector3 origin, Vector3 forward,
                                 float range, float halfAngle,
                                 Color color, float duration = 0.15f,
                                 float yOffset = 0.05f)
        {
            Instance._EnsureInit();
            _flashes.Add(new FlashShape
            {
                origin     = origin,
                forward    = forward,
                range      = range,
                halfAngle  = halfAngle,
                yOffset    = yOffset,
                color      = color,
                expireTime = Time.time + duration,
            });
        }

        // ── 生命周期 ────────────────────────────────────────────────────

        void Awake()   { _EnsureInit(); }
        void OnDestroy() { if (_instance == this) _instance = null; }

        void Update()
        {
            // 清理过期闪现
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                if (Time.time >= _flashes[i].expireTime)
                    _flashes.RemoveAt(i);
            }
        }

        private void _EnsureInit()
        {
            if (_mat != null) return;
            // 使用 Unity 内置 UI Unlit 着色器，支持颜色混合
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull",      (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite",    0);
            _mat.SetInt("_ZTest",     (int)UnityEngine.Rendering.CompareFunction.Always); // 透过地形显示
        }

        // ── GL 渲染 ─────────────────────────────────────────────────────

        void OnRenderObject()
        {
            // 总开关:关闭后不画任何 GL 扇形/圆
            if (!ShowGameViewDraw) return;
            if (_mat == null) return;
            if (_persistent.Count == 0 && _flashes.Count == 0) return;

            _mat.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);

            // 持续型
            foreach (var kv in _persistent)
                DrawShape(kv.Value.origin, kv.Value.forward,
                          kv.Value.range,  kv.Value.halfAngle,
                          kv.Value.yOffset, kv.Value.color,
                          kv.Value.drawCircle);

            // 闪现型
            foreach (var f in _flashes)
            {
                // 生命周期越短颜色越淡（淡出效果）
                float alpha = Mathf.Clamp01((f.expireTime - Time.time) / 0.08f);
                Color c = new Color(f.color.r, f.color.g, f.color.b, f.color.a * alpha);
                DrawShape(f.origin, f.forward, f.range, f.halfAngle, f.yOffset, c, false);
            }

            GL.PopMatrix();
        }

        /// <summary>
        /// 在 GL LINES 模式下画扇形：
        ///   - 填充色（TRIANGLES，半透明）
        ///   - 边界线（LINES，不透明）
        /// </summary>
        private void DrawShape(Vector3 origin, Vector3 forward,
                               float range, float halfAngle,
                               float yOffset, Color color, bool drawFullCircle)
        {
            Vector3 o = origin + Vector3.up * yOffset;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            float startAngle = -halfAngle;
            float endAngle   =  halfAngle;
            if (drawFullCircle) { startAngle = 0f; endAngle = 360f; }

            int   segs      = drawFullCircle ? kArcSegments : Mathf.Max(4, Mathf.RoundToInt(kArcSegments * halfAngle * 2f / 360f));
            float angleDeg  = endAngle - startAngle;
            float segAngle  = angleDeg / segs;

            // ── 填充三角形 ──────────────────────────────────────────────
            Color fill = new Color(color.r, color.g, color.b, color.a * 0.18f);
            GL.Begin(GL.TRIANGLES);
            GL.Color(fill);
            for (int i = 0; i < segs; i++)
            {
                float a0 = startAngle + segAngle * i;
                float a1 = a0 + segAngle;
                Vector3 p0 = o + Quaternion.AngleAxis(a0, Vector3.up) * forward * range;
                Vector3 p1 = o + Quaternion.AngleAxis(a1, Vector3.up) * forward * range;
                GL.Vertex(o);
                GL.Vertex(p0);
                GL.Vertex(p1);
            }
            GL.End();

            // ── 边界线 ──────────────────────────────────────────────────
            Color line = new Color(color.r, color.g, color.b, color.a);
            GL.Begin(GL.LINES);
            GL.Color(line);

            // 弧线
            for (int i = 0; i < segs; i++)
            {
                float a0 = startAngle + segAngle * i;
                float a1 = a0 + segAngle;
                Vector3 p0 = o + Quaternion.AngleAxis(a0, Vector3.up) * forward * range;
                Vector3 p1 = o + Quaternion.AngleAxis(a1, Vector3.up) * forward * range;
                GL.Vertex(p0);
                GL.Vertex(p1);
            }

            // 两条半径线（非全圆时）
            if (!drawFullCircle)
            {
                Vector3 left  = o + Quaternion.AngleAxis(startAngle, Vector3.up) * forward * range;
                Vector3 right = o + Quaternion.AngleAxis(endAngle,   Vector3.up) * forward * range;
                GL.Vertex(o); GL.Vertex(left);
                GL.Vertex(o); GL.Vertex(right);
                // 中轴线（前方方向）
                GL.Color(new Color(1f, 1f, 0f, color.a * 0.6f));
                GL.Vertex(o); GL.Vertex(o + forward * range);
            }

            GL.End();
        }
    }
}
