using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// 散弹枪矩形扩散视觉效果。
    ///
    /// 挂在 RectProjectile 同一 GameObject 上，由 RectProjectile 自动添加并初始化。
    /// 用 Gizmos 在 Scene 视图绘制矩形扩散动画（与敌方范围 Gizmos 一致，Game 视图不显示）：
    ///
    ///   阶段 1 — 扩散（TravelPhase）：
    ///     矩形从发射点向前推进，同时宽度从 0 扩张到 shotWidth（视觉上"散开"）
    ///     颜色为高饱和青白色，边框闪烁
    ///
    ///   阶段 2 — 消散（FadeOut）：
    ///     命中/超出范围后调用 StartFadeOut()
    ///     矩形原地淡出（alpha 0.3s 内降到 0），完成后销毁 GameObject
    ///
    /// Inspector 可调参数：
    ///   lineColor       矩形边框颜色
    ///   fillColor       矩形填充颜色（半透明）
    ///   glowLayers      视觉线宽层数（Gizmos 下不再叠加，保留字段兼容）
    ///   fadeOutDuration 消散时长（秒）
    ///   widthCurve      宽度扩散曲线（0=发射时，1=到达最远处）
    /// </summary>
    [AddComponentMenu("")]   // 不直接显示在 Add Component，由 RectProjectile 自动添加
    public class RectShotVFX : MonoBehaviour
    {
        [Header("Projectile Prefab (RectShot 中段飞行弹体)")]
        [Tooltip("飞行弹体 prefab（裸 GameObject,如 ETFX BulletSmallBlue）。\n" +
                 "挂到 RectProjectile 上作为子物体,跟随弹体一起飞,飞完一起销毁。\n" +
                 "配了这个就不画 Gizmos 矩形(避免叠加);留空走 Scene 视图 Gizmos 绘制。\n" +
                 "由 RectProjectile.Initialize() 注入,不要手动改。")]
        public GameObject projectilePrefab;

        [Tooltip("相对 RectProjectile 的子物体位置偏移")]
        public Vector3 projectileLocalOffset = Vector3.zero;

        [Tooltip("相对 RectProjectile 的子物体欧拉角偏移")]
        public Vector3 projectileLocalEulerOffset = Vector3.zero;

        [Tooltip("消散阶段(prefab 模式下):prefab 子物体淡出后延迟多少秒销毁 RectProjectile")]
        public float projectileDestroyDelay = 0.3f;

        [Tooltip("飞行弹体 prefab 的 transform.localScale 倍率(运行时实例)。\n" +
                 "1=原始大小,0.5=缩小一半,2=放大一倍。\n" +
                 "由 InitializeWithPrefab 从 SkillData.projectileLocalScale 传入,不要手动改。")]
        public float projectileLocalScale = 1f;

        [Header("Visual (仅在 projectilePrefab 为空时生效)")]
        [Tooltip("矩形边框颜色")]
        public Color lineColor = new Color(0.4f, 0.9f, 1f, 1f);
        [Tooltip("矩形填充颜色（半透明）")]
        public Color fillColor = new Color(0.3f, 0.8f, 1f, 0.12f);
        [Tooltip("消散时长（秒）")]
        public float fadeOutDuration = 0.25f;
        [Tooltip("宽度扩散曲线：X=飞行进度(0~1)，Y=宽度比例(0~1)。\n默认：发射瞬间宽度=20%，到达时=100%")]
        public AnimationCurve widthCurve = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f);
        [Tooltip("额外描边层数（1=单线，3=模拟粗线）")]
        [Range(1, 5)]
        public int glowLayers = 3;

        // ── 运行时 ────────────────────────────────────────────────────
        private float _shotWidth;
        private float _shotHeight;
        private float _maxRange;
        private float _speed;

        private float _travelProgress;   // 0~1，飞行进度
        private bool  _isFadingOut;
        private float _fadeTimer;
        private bool  _initialized;

        private GameObject _spawnedProjectile;  // prefab 模式下挂载的子物体实例
        private ParticleSystem[] _childPS;      // prefab 模式下的所有粒子系统,用于消散时停止

        // ── 初始化 ────────────────────────────────────────────────────

        /// <summary>由 RectProjectile 调用，传入几何参数。</summary>
        public void Initialize(float width, float height, float maxRange, float speed)
        {
            _shotWidth  = width;
            _shotHeight = height;
            _maxRange   = maxRange;
            _speed      = speed;
            _initialized = true;
        }

        /// <summary>由 RectProjectile 调用,在原 Initialize 基础上额外注入飞行弹体 prefab。
        /// prefab 模式下:挂 prefab 为子物体,关闭 Gizmos 绘制。
        /// prefab 为空:走 Scene 视图 Gizmos 绘制,行为不变。
        /// localOffset/localEulerOffset/localScale 由 RectProjectile 从 SkillData 传入(权威源,这里只接收)。</summary>
        public void InitializeWithPrefab(float width, float height, float maxRange, float speed,
                                          GameObject prefab, Vector3 localOffset, Vector3 localEulerOffset,
                                          float localScale = 1f)
        {
            Initialize(width, height, maxRange, speed);
            projectilePrefab = prefab;
            projectileLocalScale = localScale;
            if (prefab != null)
            {
                _spawnedProjectile = Instantiate(prefab, transform);
                _spawnedProjectile.transform.localPosition = localOffset;
                _spawnedProjectile.transform.localEulerAngles = localEulerOffset;
                if (localScale != 1f)
                    _spawnedProjectile.transform.localScale = _spawnedProjectile.transform.localScale * localScale;
                // 收集所有粒子系统,用于 StartFadeOut 时停止发射
                _childPS = _spawnedProjectile.GetComponentsInChildren<ParticleSystem>(true);
                Debug.Log($"[RectShotVFX] prefab={prefab.name} 实例化为子物体 name={_spawnedProjectile.name} " +
                          $"localPos={_spawnedProjectile.transform.localPosition} " +
                          $"localScale={_spawnedProjectile.transform.localScale} " +
                          $"childPS={( _childPS != null ? _childPS.Length : 0 )}个");
            }
        }

        // ── 生命周期 ─────────────────────────────────────────────────

        void Update()
        {
            if (!_initialized) return;

            if (_isFadingOut)
            {
                _fadeTimer -= Time.deltaTime;
                if (_fadeTimer <= 0f)
                    Destroy(gameObject);
                return;
            }

            // 根据父物体的飞行距离更新进度（RectProjectile 每帧移动，这里跟随）
            _travelProgress += (_speed * Time.deltaTime) / Mathf.Max(_maxRange, 0.001f);
            _travelProgress  = Mathf.Clamp01(_travelProgress);
        }

        public void StartFadeOut()
        {
            _isFadingOut = true;
            _fadeTimer   = fadeOutDuration;

            // prefab 模式:强制清粒子(ETFX missile looping=1,StopEmitting 不会清残留)
            if (_spawnedProjectile != null && _childPS != null)
            {
                foreach (var ps in _childPS)
                {
                    if (ps != null)
                    {
                        // Stop + ClearWithChildren 立即清空所有粒子,不留残影
                        ps.Stop(true, UnityEngine.ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
                // 关闭所有 Light(ETFX missile 自带 Light 组件),强制关掉避免残留照明
                var lights = _spawnedProjectile.GetComponentsInChildren<Light>(true);
                foreach (var l in lights)
                {
                    if (l != null) l.enabled = false;
                }
                // 关闭 TrailRenderer(ETFX missile 有 TrailModule + 可能 TrailRenderer 组件)
                var trails = _spawnedProjectile.GetComponentsInChildren<TrailRenderer>(true);
                foreach (var t in trails)
                {
                    if (t != null) t.emitting = false;
                }
            }
        }

        // ── Gizmos 渲染（仅 Scene 视图，与敌方范围一致） ──────────────

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // prefab 模式:不画 Gizmos,避免与 prefab 视觉叠加
            if (_spawnedProjectile != null) return;
            if (!_initialized) return;
            if (!_isFadingOut && _travelProgress <= 0f) return;

            // 当前帧的宽度（根据扩散曲线）
            float curWidth  = _shotWidth * widthCurve.Evaluate(_travelProgress);
            float curHeight = _shotHeight;

            // 消散阶段 alpha 递减
            float alpha = 1f;
            if (_isFadingOut)
                alpha = Mathf.Clamp01(_fadeTimer / Mathf.Max(fadeOutDuration, 0.001f));

            float hw = curWidth * 0.5f;

            // 矩形站在 transform 正前方 0 处，宽/高展开（用局部坐标系）
            Vector3 center = new Vector3(0f, curHeight * 0.5f, 0f);
            Vector3 size   = new Vector3(curWidth, curHeight, 0.02f);

            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            // ── 填充面 ────────────────────────────────────────────────
            Gizmos.color = new Color(fillColor.r, fillColor.g, fillColor.b, fillColor.a * alpha);
            Gizmos.DrawCube(center, size);

            // ── 边框 ──────────────────────────────────────────────────
            Gizmos.color = new Color(lineColor.r, lineColor.g, lineColor.b, alpha);
            Gizmos.DrawWireCube(center, size);

            // ── 扫描线（从底部往上的水平扫线，增强"能量冲击"感） ────
            int   scanLines = 4;
            Gizmos.color = new Color(lineColor.r, lineColor.g, lineColor.b, alpha * 0.35f);
            for (int i = 1; i < scanLines; i++)
            {
                float t = (float)i / scanLines;
                float y = curHeight * t;
                // 扫线随时间轻微抖动（增加动感）
                float yt = y + Mathf.Sin(Time.time * 12f + i) * 0.03f;
                Gizmos.DrawLine(new Vector3(-hw * 0.9f, yt, 0f),
                                new Vector3( hw * 0.9f, yt, 0f));
            }

            Gizmos.matrix = old;
        }
#endif
    }
}
