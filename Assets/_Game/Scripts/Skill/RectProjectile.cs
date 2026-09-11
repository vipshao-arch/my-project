using UnityEngine;
using System.Collections.Generic;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 矩形投射物——散弹枪/冲击波/扇形弹幕/穿透激光/追踪弹的统一基类。
    ///
    /// 工作原理：
    ///   每帧沿 forward 方向移动，BoxCollider 作为触发器检测目标。
    ///   命中模式 / 形状 / 追踪 等参数由调用方通过 Initialize() 显式传入
    ///   （来自 graphData 的 RectShotData 节点），不再依赖 SkillData 旧字段：
    ///     - Stop       : 命中第一个目标后停(默认,典型散弹/手枪)
    ///     - Pierce     : 穿透,最多打 pierceCount 个目标(穿透激光/链式闪电)
    ///     - PierceAll  : 穿透射程内全部目标(扫射/范围冲击)
    ///   超出 maxRange 或 projectileLifetime 到达时自动销毁。
    ///
    /// 形状（由 graphData RectShotData.shape 决定）:
    ///   - Rect  : 矩形 box(默认,散弹/冲击波)
    ///   - Line  : 极窄长条 box(穿透激光,宽度固定 0.3m)
    ///   - Cone  : 锥形 box,代码内做水平扇形裁剪(典型冲击波/光束)
    ///
    /// 追踪（由 graphData RectShotData.homingEnabled 决定）:
    ///   - true  : 每帧寻找 homingSearchRadius 范围内最近 IDamageable,
    ///             以 homingTurnRate 度/秒 的角速度调整 forward(简易追踪弹)
    ///   - false : 直线弹(默认)
    ///
    /// 配套使用：
    ///   - 自动添加 RectShotVFX 组件负责视觉表现
    ///   - 由 HitDetector.DoRectShot 在命中帧实例化并调用 Initialize
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    [AddComponentMenu("Game/Skill System/Rect Projectile")]
    public class RectProjectile : MonoBehaviour
    {
        [Header("Shot Geometry")]
        [Tooltip("矩形宽度（米）。Rect 模式 = 横向判定宽度;Line 模式 = 固定细线;Cone 模式 = 末端最大宽度。")]
        public float shotWidth  = 2f;
        [Tooltip("矩形高度（米）。一般等于或略大于角色高度")]
        public float shotHeight = 1.8f;

        [Header("Travel")]
        [Tooltip("飞行速度（米/秒）")]
        public float speed    = 18f;
        [Tooltip("最大射程（米）。超出后自动销毁")]
        public float maxRange = 6f;

        [Header("Damage")]
        [Tooltip("命中伤害。可由 Initialize() 覆盖")]
        public float damage = 15f;
        [Tooltip("可命中的 Layer Mask")]
        public LayerMask hitMask = ~0;
        [Tooltip("命中模式:Stop / Pierce / PierceAll。由 RectShotData.hitMode 通过 Initialize() 注入。")]
        public ShotHitMode hitMode = ShotHitMode.Stop;
        [Tooltip("穿透数(仅 Pierce 时生效)。0=不穿透,等价 Stop。")]
        public int pierceCount = 0;

        [Header("Homing (追踪弹)")]
        [Tooltip("是否启用追踪。true=每帧找最近 IDamageable 调整 forward。")]
        public bool homingEnabled = false;
        [Tooltip("追踪弹最大旋转角速度(度/秒)。")]
        public float homingTurnRate = 540f;
        [Tooltip("追踪目标检测半径(米)。")]
        public float homingSearchRadius = 10f;

        [Header("Projectile Prefab（飞行弹体,RectShot 中段表现）")]
        [Tooltip("飞行弹体 prefab（裸 GameObject,如 ETFX BulletSmallBlue）。\n" +
                 "由 Initialize() 注入,跟随 RectProjectile 飞,飞完一起销毁。\n" +
                 "留空走 RectShotVFX 的 Scene 视图 Gizmos 绘制（Game 视图不显示）。\n" +
                 "一般从 SkillData.projectilePrefab 传入,不要手动改。")]
        public GameObject projectilePrefab;

        [Tooltip("弹体本地位置偏移(米),从 SkillData 传入")]
        public Vector3 projectileLocalOffset = Vector3.zero;

        [Tooltip("弹体本地欧拉角偏移(度),从 SkillData 传入")]
        public Vector3 projectileLocalEulerOffset = Vector3.zero;

        [Tooltip("弹体最大存活时间(秒),从 SkillData 传入。\n" +
                 "超过此时间无论是否命中都强制销毁 RectProjectile(连带拆子物体)。\n" +
                 "0 = 不设上限。")]
        public float projectileLifetime = 0f;

        [Tooltip("弹体 prefab 的 transform.localScale 倍率(运行时实例),从 SkillData 传入。\n" +
                 "1=原始大小(默认),0.5=缩小一半,2=放大一倍。\n" +
                 "ETFX missile 默认偏大,推荐 0.4~0.7。\n" +
                 "不影响 prefab 资源本身。")]
        public float projectileLocalScale = 1f;

        [Header("Obstacle Blocking (掩体格挡)")]
        [Tooltip("飞行中是否被掩体拦截(true=撞掩体即停在接触点并销毁)。由 RectShotData.SetObstacleBlocking 注入,默认 false 保持旧行为。")]
        public bool checkObstacle = false;
        [Tooltip("掩体所在层(默认 Default)。")]
        public LayerMask obstacleMask = 1;
        [Tooltip("掩体检测截面垂直半高上限(米)。\n" +
                 "shotHeight 较大(如矩形判定高度 5m)时,掩体 BoxCast 的 halfExtents.y 若直接取 shotHeight*0.2f 会把截面\n" +
                 "底部压到地面以下,导致地面(Default 层)被误判为掩体、弹幕刚生成即 SelfDestroy。\n" +
                 "这里把掩体检测的半高 clamp 到此上限(默认 0.9m),避免扫到地面。")]
        public float obstacleCastHalfHeight = 0.9f;
        [Tooltip("掩体检测截面离地间隙(米)。截面底部至少保留此间隙,确保不把地面当成掩体。")]
        public float obstacleCastGroundClearance = 0.1f;

        [Header("Debug")]
        public bool debugDraw = false;

        // ── 运行时 ────────────────────────────────────────────────────
        private float      _traveledDist = 0f;
        private GameObject _source;
        private HitDetector _ownerHitDetector;
        private SkillData   _skillData;
        private HashSet<GameObject> _hitTargets = new HashSet<GameObject>();
        private BoxCollider _box;
        private RectShotVFX _vfx;
        private bool _destroyed = false;
        private Transform _homingTarget;
        private float _homingRetargetCooldown = 0f;
        private ShotShape _shape = ShotShape.Rect;
        private bool _vfxInitialized = false;  // 标记外部 Initialize 是否已调过 VFX.Init,避免 Start 重复

        /// <summary>
        /// 命中目标时触发（参数：目标 Transform + 命中点）。供外部挂接命中 VFX/SFX。
        /// 在 OnTriggerEnter 之后、SelfDestroy 之前触发。
        /// </summary>
        public event System.Action<Transform, Vector3> OnTargetHit;

        // ── 生命周期 ─────────────────────────────────────────────────

        void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;

            // 自动挂 RectShotVFX
            _vfx = GetComponent<RectShotVFX>();
            if (_vfx == null) _vfx = gameObject.AddComponent<RectShotVFX>();
        }

        void Start()
        {
            // 同步 BoxCollider 尺寸
            ApplyBoxGeometry();

            // ★ 修复:外部 Initialize() 已经调过 VFX 初始化时,Start 不再重复。
            //   原因:DoRectShot() 的顺序是 AddComponent → Initialize(),但 Start 是
            //   在第一次 Update 之前才跑,期间 projectilePrefab 字段已被 Initialize() 赋值。
            //   不加这个标志会导致 prefab 子物体被挂两次。
            if (!_vfxInitialized)
            {
                if (_vfx != null)
                {
                    if (projectilePrefab != null)
                        _vfx.InitializeWithPrefab(shotWidth, shotHeight, maxRange, speed,
                                                  projectilePrefab, projectileLocalOffset, projectileLocalEulerOffset,
                                                  projectileLocalScale);
                    else
                        _vfx.Initialize(shotWidth, shotHeight, maxRange, speed);
                }
            }

            // projectileLifetime 兜底:超过时间强制销毁(防 ETFX looping 残留)
            if (projectileLifetime > 0f)
            {
                Invoke(nameof(SelfDestroy), projectileLifetime);
            }
        }

        /// <summary>
        /// 根据 _shape/shotWidth/shotHeight 同步 BoxCollider 尺寸。
        /// Rect: 宽 shotWidth, 高 shotHeight, 厚 0.3m(每帧扫过的体积)
        /// Line: 极窄长条, 宽 0.3m, 高 shotHeight, 厚=maxRange(因为它要一路贯通判定)
        /// Cone: 用 Rect 同尺寸(代码内裁剪)
        /// </summary>
        private void ApplyBoxGeometry()
        {
            if (_box == null) return;
            if (_shape == ShotShape.Line)
            {
                _box.size   = new Vector3(0.3f, shotHeight, maxRange);
                _box.center = new Vector3(0f, shotHeight * 0.5f - 0.1f, maxRange * 0.5f);
            }
            else
            {
                _box.size   = new Vector3(shotWidth, shotHeight, 0.3f);
                _box.center = new Vector3(0f, shotHeight * 0.5f - 0.1f, 0.15f);
            }
        }

        /// <summary>
        /// 掩体拦截注入(独立于 Initialize 签名,旧调用方 SkillAnimPlayer/HitDetector 等不受影响)。
        /// </summary>
        public void SetObstacleBlocking(bool check, LayerMask mask)
        {
            checkObstacle = check;
            if (mask.value != 0) obstacleMask = mask;
        }

        // 远端表现标记(S2-5b):visualOnly 弹体照常飞行/撞掩体/播命中 VFX,但不落地伤害
        private bool _visualOnly;
        public void SetVisualOnly(bool v) => _visualOnly = v;

        /// <summary>
        /// 掩体拦截 BoxCast(判定单点真源):沿飞行方向投射弹体截面盒,
        /// 命中 obstacleMask 层、非自身、非角色(IDamageable)的碰撞体视为撞掩体。
        /// TR-4.1 格挡矩阵回归工具复用此入口,保证工具与运行时判定一致。
        /// </summary>
        public static bool BoxCastObstacle(Vector3 pos, Quaternion rot, Vector3 halfExtents,
            float distance, LayerMask mask, GameObject source, out RaycastHit hit)
        {
            if (Physics.BoxCast(pos, halfExtents, rot * Vector3.forward, out hit, rot,
                    distance, mask, QueryTriggerInteraction.Ignore))
            {
                bool isSelf  = source != null && hit.collider.transform.IsChildOf(source.transform);
                bool isActor = hit.collider.GetComponentInParent<IDamageable>() != null;
                if (!isSelf && !isActor) return true;
            }
            return false;
        }

        /// <summary>
        /// Initialize:所有参数显式传入。ShotNodes / EnemyAI 等调用方直接传入 graphData 节点值。
        /// </summary>
        public void Initialize(float dmg, GameObject source, LayerMask mask, SkillData skill, GameObject projectile,
                               Vector3 localOffset, Vector3 localEulerOffset, float lifetime, float localScale,
                               ShotShape shape, ShotHitMode mode, int pierce, bool homing, float turnRate, float searchRadius)
        {
            damage                       = dmg;
            _source                      = source;
            hitMask                      = mask;
            _skillData                   = skill;
            projectilePrefab             = projectile;
            projectileLocalOffset        = localOffset;
            projectileLocalEulerOffset   = localEulerOffset;
            projectileLifetime           = lifetime;
            projectileLocalScale         = localScale;
            _shape                       = shape;
            hitMode                      = mode;
            pierceCount                  = pierce;
            homingEnabled                = homing;
            homingTurnRate               = turnRate;
            homingSearchRadius           = searchRadius;
            _ownerHitDetector = source != null ? source.GetComponent<HitDetector>() : null;
            _hitTargets.Clear();
            _traveledDist = 0f;

            // 同步 BoxCollider 尺寸（Initialize 可能在 Awake 之后）
            ApplyBoxGeometry();
            if (_vfx != null)
            {
                if (projectile != null)
                    _vfx.InitializeWithPrefab(shotWidth, shotHeight, maxRange, speed,
                                              projectile, localOffset, localEulerOffset, localScale);
                else
                    _vfx.Initialize(shotWidth, shotHeight, maxRange, speed);
                _vfxInitialized = true;
            }
        }

        void Update()
        {
            if (_destroyed) return;

            // ── 追踪:定期(0.1s)重新选目标,持续调整 forward ──
            if (homingEnabled)
            {
                _homingRetargetCooldown -= Time.deltaTime;
                if (_homingRetargetCooldown <= 0f)
                {
                    _homingTarget = AcquireHomingTarget();
                    _homingRetargetCooldown = 0.1f;
                }
                if (_homingTarget != null)
                {
                    Vector3 toTarget = _homingTarget.position - transform.position;
                    if (toTarget.sqrMagnitude > 0.0001f)
                    {
                        Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                        // 同时考虑初始 pitch 偏置
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, homingTurnRate * Time.deltaTime);
                    }
                }
            }

            float step = speed * Time.deltaTime;

            // ── 掩体拦截:BoxCast 预判本步是否撞掩体(弹体截面×0.4),命中即停在接触点销毁 ──
            if (checkObstacle && obstacleMask.value != 0)
            {
                // ★ 修复(掩体误判地面):原 halfExtents.y = shotHeight*0.2f,当 shotHeight 较大
                //   (如矩形判定高度 5m)时截面底部会低于地面,把地面(Default=obstacleMask)误判成掩体,
                //   弹幕刚生成即 SelfDestroy(表现为"不可见/打不到")。
                //   现将 BoxCast 中心抬升到弹体飞行带,并对垂直半高做上限 clamp,底部留离地间隙。
                float castHalfHeight = Mathf.Min(shotHeight * 0.5f, obstacleCastHalfHeight);
                Vector3 castCenter = transform.position
                                   + transform.up * (castHalfHeight + obstacleCastGroundClearance);
                Vector3 halfExtents = new Vector3(shotWidth * 0.2f, castHalfHeight, 0.05f);
                if (BoxCastObstacle(castCenter, transform.rotation, halfExtents, step,
                        obstacleMask, _source, out RaycastHit wallHit))
                {
                    transform.position = wallHit.point - transform.forward * 0.05f;
                    if (debugDraw)
                        Debug.Log($"[RectProjectile] 撞掩体 {wallHit.collider.name} dist={_traveledDist:F2}m");
                    SelfDestroy();
                    return;
                }
            }

            transform.position += transform.forward * step;
            _traveledDist += step;

            if (_traveledDist >= maxRange)
            {
                SelfDestroy();
                return;
            }

            if (debugDraw)
            {
                // Scene 视图辅助：画矩形轮廓
                DrawDebugRect();
            }
        }

        /// <summary>
        /// 在 homingSearchRadius 范围内找最近的 IDamageable(忽略 _source 自身)。
        /// </summary>
        private Transform AcquireHomingTarget()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, homingSearchRadius, hitMask);
            float bestDist = float.MaxValue;
            Transform best = null;
            foreach (var c in hits)
            {
                if (_source != null && c.transform.IsChildOf(_source.transform)) continue;
                if (c.gameObject == _source) continue;
                var d = c.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;
                float dist = (c.transform.position - transform.position).sqrMagnitude;
                if (dist < bestDist) { bestDist = dist; best = c.transform; }
            }
            return best;
        }

        void OnTriggerEnter(Collider other)
        {
            Debug.Log($"[RectProjectile.OnTriggerEnter] 命中 {other.name}(layer={LayerMask.LayerToName(other.gameObject.layer)})");
            if (_destroyed) return;
            // 排除来源
            if (_source != null && other.transform.IsChildOf(_source.transform)) return;
            if (other.gameObject == _source) return;
            if (_hitTargets.Contains(other.gameObject)) return;

            // 层检测
            if ((hitMask.value & (1 << other.gameObject.layer)) == 0) return;

            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null || damageable.isDead) return;

            _hitTargets.Add(other.gameObject);

            Vector3 hitDir = transform.forward;
            if (!_visualOnly)
                damageable.TakeDamage(damage, _source, hitDir);   // visualOnly:命中 VFX 照播,伤害不落地

            // 触发命中 VFX/SFX：取目标最近点
            Vector3 hitPoint = other.ClosestPoint(transform.position);
            OnTargetHit?.Invoke(other.transform, hitPoint);
            // P8:从 graphData HitVFXData 读取命中 VFX/SFX(含 sfxPitchRandomPercent)
            if (_skillData != null)
            {
                var (hfxPrefab, hfxSfx, _, _, _, _, hfxScale, _, hfxPitchRand) =
                    SkillData.GetHitVFXFromGraph(_skillData);
                if (hfxPrefab != null)
                {
                    GameObject hitFx = GameObject.Instantiate(hfxPrefab, hitPoint,
                        Quaternion.LookRotation(hitDir));
                    if (hfxScale != 1f)
                        hitFx.transform.localScale = hitFx.transform.localScale * hfxScale;
                }
                if (hfxSfx != null)
                    PlaySfx(hfxSfx, hfxPitchRand, hitPoint);
            }

            if (debugDraw)
                Debug.Log($"[RectProjectile] Hit {other.gameObject.name} dist={_traveledDist:F2}m dmg={damage} mode={hitMode} hits={_hitTargets.Count}");

            // 按命中模式决定是否停止
            switch (hitMode)
            {
                case ShotHitMode.Stop:
                    SelfDestroy();
                    break;
                case ShotHitMode.Pierce:
                    if (_hitTargets.Count >= Mathf.Max(1, pierceCount))
                        SelfDestroy();
                    break;
                case ShotHitMode.PierceAll:
                    // 不停,继续飞
                    break;
            }
        }

        /// <summary>
        /// 在指定位置播放带音调随机化的 AudioClip。挂临时 AudioSource，播放完毕后销毁。
        /// </summary>
        private static void PlaySfx(AudioClip clip, float pitchRandomPercent, Vector3 worldPos)
        {
            if (clip == null) return;
            var go = new GameObject($"[SFX]_{clip.name}");
            go.transform.position = worldPos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 1f;            // 3D 音效
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.minDistance  = 5f;
            src.maxDistance  = 30f;
            if (pitchRandomPercent > 0f)
            {
                float r = pitchRandomPercent / 100f;
                src.pitch = 1f + Random.Range(-r, r);
            }
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        private void SelfDestroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            // 通知 VFX 开始消散（而非硬切）
            if (_vfx != null) _vfx.StartFadeOut();
            // 禁用碰撞体，VFX 淡出后自行销毁 GameObject
            if (_box != null) _box.enabled = false;
            // 兜底：0.3s 后强制销毁（防 VFX 淡出逻辑出错时残留）
            Destroy(gameObject, 0.35f);
        }

        private void DrawDebugRect()
        {
            float hw = shotWidth  * 0.5f;
            float hh = shotHeight;
            Vector3 p  = transform.position;
            Vector3 r  = transform.right;
            Vector3 u  = Vector3.up;
            Vector3 tl = p - r * hw + u * hh;
            Vector3 tr = p + r * hw + u * hh;
            Vector3 bl = p - r * hw;
            Vector3 br = p + r * hw;
            Debug.DrawLine(tl, tr, Color.yellow);
            Debug.DrawLine(bl, br, Color.yellow);
            Debug.DrawLine(tl, bl, Color.yellow);
            Debug.DrawLine(tr, br, Color.yellow);
        }
    }
}
