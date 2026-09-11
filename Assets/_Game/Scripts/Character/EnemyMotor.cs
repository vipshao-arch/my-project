using UnityEngine;
using UnityEngine.AI;
using System;

namespace Game.Character
{
    /// <summary>
    /// 敌人移动与生命值组件。轻量级实现，不依赖 CharacterMotor。
    ///
    /// 功能：
    ///   - Rigidbody 物理移动（由 EnemyAI 驱动）
    ///   - 实现 IDamageable（受击→扣血→死亡）
    ///   - 实现 IHittable（击退冲量）
    ///   - 受击硬直（hitStunDuration）
    ///   - 死亡处理（禁用碰撞/导航/动画淡出）
    ///   - 死亡掉落事件
    ///
    /// 挂载要求：Rigidbody + CapsuleCollider + Animator
    /// </summary>
    [AddComponentMenu("Game/Character System/Enemy Motor")]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class EnemyMotor : MonoBehaviour, IDamageable, IHittable
    {
        // ═══════════════════════════════════════════════════════════════
        // Inspector 配置
        // ═══════════════════════════════════════════════════════════════

        [Header("Health")]
        public float maxHealth = 50f;
        public float currentHealth { get; private set; }
        public bool isDead { get; private set; }

        [Header("Movement")]
        public float moveSpeed = 2.5f;
        public float rotationSpeed = 8f;

        [Header("Hit Response")]
        [Tooltip("受击硬直时间（秒）。期间无法移动/攻击。")]
        public float hitStunDuration = 0.3f;
        [Tooltip("受击后闪红持续时间")]
        public float hitFlashDuration = 0.15f;

        [Header("Death")]
        [Tooltip("死亡后销毁延迟（秒）。0=不自动销毁。")]
        public float destroyAfterDeath = 3f;
        [Tooltip("死亡时是否禁用 Rigidbody 物理")]
        public bool disablePhysicsOnDeath = true;

        [Header("Events")]
        public Action<float> OnHealthChanged;
        public Action OnDeath;
        public Action<GameObject> OnEnemyDied; // 传递自身，供掉落系统使用

        // ═══════════════════════════════════════════════════════════════
        // 事件桥接（IDamageable 显式实现）
        // ═══════════════════════════════════════════════════════════════

        event Action<float> IDamageable.OnHealthChanged
        {
            add => OnHealthChanged += value;
            remove => OnHealthChanged -= value;
        }
        event Action IDamageable.OnDeath
        {
            add => OnDeath += value;
            remove => OnDeath -= value;
        }
        float IDamageable.maxHealth => maxHealth;

        // ═══════════════════════════════════════════════════════════════
        // 运行时状态
        // ═══════════════════════════════════════════════════════════════

        private Rigidbody _rb;
        private CapsuleCollider _col;
        private Animator _anim;
        private NavMeshAgent _navAgent;
        private Renderer[] _renderers;

        private float _hitStunTimer = 0f;
        private float _hitFlashTimer = 0f;
        private bool _isKnockedBack = false;
        private float _knockbackTimer = 0f;
        private float _deathNavY = 0f; // 死亡瞬间的 NavMesh Y，避免浮空

        // Speed 平滑（防止 attack→locomotion 切换时 Speed 在一帧内归零再复活造成 Idle 插帧）
        //   攻击锁位期间：保持最后一次移动 Speed，不让 agent.velocity=0 把它清零
        //   切出攻击时：CrossFadeToLocomotion 设 Speed=0.5，本字段确保下帧 Update 不会立刻覆盖回 0
        private bool _suppressSpeedUpdate = false;
        private float _suppressSpeedTimer = 0f;
        private const float SUPPRESS_SPEED_DURATION = 0.18f; // 比 crossfade 时长(0.12)多一点点的保护窗

        // Animator 参数 hash
        // 参数名统一走 AnimatorParams 契约(2026-07-30)
        private static readonly int H_Speed = AnimatorParams.Speed;
        private static readonly int H_IsDead = AnimatorParams.IsDead;
        private static readonly int H_Hit = AnimatorParams.Hit;
        private static readonly int H_Attack = AnimatorParams.Attack;
        // 多套普攻：EnemyAI 随机选一段普攻下标 → SetInteger(AttackIndex, idx) → 配套 controller 按条件路由到 Attack_1/Attack_2/...
        //   - 留空 attackClips 时 EnemyAI 传 0；controller 可以只配 Attack 单状态 + Attack Trigger（兼容旧版）
        //   - 有 attackClips 时 controller 必须有 AttackIndex int 参数 + Attack_1/Attack_2/... 状态 + AnyState→各 Attack 条件分支
        private static readonly int H_AttackIndex = AnimatorParams.AttackIndex;

        // ═══════════════════════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════════════════════

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _col = GetComponent<CapsuleCollider>();
            _anim = GetComponent<Animator>();
            _navAgent = GetComponent<NavMeshAgent>();
            _renderers = GetComponentsInChildren<Renderer>();

            // 如果根物体 Animator 没有 Avatar，尝试从子物体的 Animator 获取
            if (_anim != null && _anim.avatar == null)
            {
                var childAnimators = GetComponentsInChildren<Animator>();
                foreach (var ca in childAnimators)
                {
                    if (ca != _anim && ca.avatar != null)
                    {
                        _anim.avatar = ca.avatar;
                        Debug.Log($"[EnemyMotor] Copied Avatar from child Animator: {ca.avatar.name}");
                        break;
                    }
                }
            }

            // 如果根物体 Animator 没有 Controller，尝试从子物体的 Animator 获取
            if (_anim != null && _anim.runtimeAnimatorController == null)
            {
                var childAnimators = GetComponentsInChildren<Animator>();
                foreach (var ca in childAnimators)
                {
                    if (ca != _anim && ca.runtimeAnimatorController != null)
                    {
                        _anim.runtimeAnimatorController = ca.runtimeAnimatorController;
                        Debug.Log($"[EnemyMotor] Copied AnimatorController from child Animator: {ca.runtimeAnimatorController.name}");
                        break;
                    }
                }
            }

            // 敌人由 NavMeshAgent 驱动位移，不由 RootMotion 驱动（攻击中位置由 EnemyAI.LateUpdate 硬锁）
            // 不在这里强制设 applyRootMotion：EnemyPrefabSetup.BuildPrefab 已经按需设过，
            // 如果反过来强制 false 会与未来的 RootMotion 需求冲突。

            // NavMeshAgent 驱动移动时，Rigidbody 设为 kinematic 避免冲突
            if (_navAgent != null && _rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
        }

        void Start()
        {
            currentHealth = maxHealth;
        }

        void FixedUpdate()
        {
            if (isDead) return;

            // 击退计时
            if (_isKnockedBack)
            {
                _knockbackTimer -= Time.fixedDeltaTime;
                if (_knockbackTimer <= 0f)
                    _isKnockedBack = false;
            }

            // 硬直计时
            if (_hitStunTimer > 0f)
                _hitStunTimer -= Time.fixedDeltaTime;
        }

        void Update()
        {
            if (isDead) return;

            // 闪红效果
            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_hitFlashTimer / hitFlashDuration);
                SetHitFlash(t);
            }
            else
            {
                SetHitFlash(0f);
            }

            // Speed 压制计时（用于 attack→locomotion 过渡保护）
            if (_suppressSpeedUpdate)
            {
                _suppressSpeedTimer -= Time.deltaTime;
                if (_suppressSpeedTimer <= 0f)
                    _suppressSpeedUpdate = false;
            }

            // Animator 参数更新
            //   攻击锁位期间（suppressSpeed）跳过 Speed 更新，保持 CrossFadeToLocomotion 设的 0.5，
            //   防止 agent.velocity=0 把 Speed 拉回 0 导致 Walk→Idle 闪一帧。
            if (!_suppressSpeedUpdate && _anim != null && _anim.runtimeAnimatorController != null)
            {
                float speed = _navAgent != null
                    ? _navAgent.velocity.magnitude
                    : (_rb != null ? new Vector3(_rb.velocity.x, 0f, _rb.velocity.z).magnitude : 0f);
                _anim.SetFloat(H_Speed, speed);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 移动（由 EnemyAI 调用）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>移动到目标方向。由 EnemyAI FallbackMove 调用（无 NavMesh 时）。</summary>
        public void MoveTo(Vector3 direction, float speedMultiplier = 1f)
        {
            if (isDead || _hitStunTimer > 0f || _isKnockedBack) return;
            if (direction.sqrMagnitude < 0.01f) return;

            // 直接移动 transform（NavMeshAgent 由 EnemyAI 直接驱动，不经过此处）
            Vector3 move = direction.normalized * moveSpeed * speedMultiplier * Time.deltaTime;
            move.y = 0f;
            transform.position += move;
        }

        /// <summary>朝向目标点。</summary>
        public void RotateTowards(Vector3 targetPosition)
        {
            if (isDead || _hitStunTimer > 0f) return;

            Vector3 dir = targetPosition - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;

            Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 播放攻击动画。
        ///   idx 范围 0~N-1：EnemyAI 在 attackClips 中随机选中的下标。
        ///   - 先 SetInteger(AttackIndex, idx)（供 controller 条件路由到 Attack_1/Attack_2/...）
        ///   - 再 SetTrigger(Attack)（触发状态切换）
        ///   注意：SetInteger 与 SetTrigger 顺序敏感——必须先 SetInteger 再 SetTrigger，
        ///         否则 controller 收到 Trigger 时 AttackIndex 还是上一帧的值。
        /// </summary>
        public void PlayAttackAnimation(int attackIndex = 0)
        {
            if (_anim != null && _anim.runtimeAnimatorController != null)
            {
                _anim.SetInteger(H_AttackIndex, attackIndex);
                _anim.SetTrigger(H_Attack);
            }
        }

        // 过渡目标状态名（按优先级匹配：先 Walk 再 Locomotion 再 Run）。缓存以省 HasState 开销。
        private static readonly string[] s_LocomotionStateNames = { "Walk", "Locomotion", "Run" };
        private int _cachedLocomotionStateHash = 0;

        /// <summary>
        /// 把 Animator 显式 CrossFade 到移动状态（Walk/Locomotion/Run），用于"攻击→移动"的过渡表现。
        ///   - 离开 Attack 时 EnemyAI.ChangeState 会调用本方法；
        ///   - 同时 ResetTrigger(Attack) 防止攻击 trigger 残留导致下一帧又跳回 Attack 状态。
        ///   - 默认过渡时长 0.12s：覆盖攻击尾段（~0.05s），又能跟 UpdateChase 下一帧 SetFloat(Speed,…) 衔接上。
        ///   - 找不到移动状态时静默 no-op（兼容没有 Walk 状态的旧 controller）。
        ///   - 调用后启动 Speed 压制计时，保护 crossfade 期间 Speed 不被 agent.velocity=0 拉回 0。
        /// </summary>
        public void CrossFadeToLocomotion(float fadeDuration = 0.12f, int layer = 0)
        {
            if (_anim == null || _anim.runtimeAnimatorController == null) return;

            // 找到第一个存在的移动状态
            int targetHash = _cachedLocomotionStateHash;
            if (targetHash == 0 || !_anim.HasState(layer, targetHash))
            {
                for (int i = 0; i < s_LocomotionStateNames.Length; i++)
                {
                    int h = Animator.StringToHash(s_LocomotionStateNames[i]);
                    if (_anim.HasState(layer, h))
                    {
                        targetHash = h;
                        _cachedLocomotionStateHash = h;
                        break;
                    }
                }
            }
            if (targetHash == 0) return;

            // 重置 Attack trigger，防止下一帧 Animator 又跳回 Attack 状态（trigger 残留经典坑）
            _anim.ResetTrigger(H_Attack);
            // Speed 设个小正数（>0.05）让 Walk 状态机内部 "Speed > 0" 的过渡条件满足，避免 crossfade 后又立刻掉回 Idle
            _anim.SetFloat(H_Speed, Mathf.Max(0.5f, _anim.GetFloat(H_Speed)));
            // CrossFade：固定时长，attack 状态 0.12s 内淡出，Walk 状态 0.12s 内淡入
            _anim.CrossFadeInFixedTime(targetHash, Mathf.Max(0.01f, fadeDuration), layer);

            // 启动 Speed 压制窗口：在 crossfade 完成前不让 Update 里的 agent.velocity→Speed 覆盖上面设的 0.5
            _suppressSpeedUpdate = true;
            _suppressSpeedTimer = Mathf.Max(fadeDuration, SUPPRESS_SPEED_DURATION);
        }

        /// <summary>停止移动。</summary>
        public void StopMoving()
        {
            if (_navAgent != null && _navAgent.isOnNavMesh)
            {
                _navAgent.isStopped = true;
                _navAgent.velocity = Vector3.zero;
            }
            else if (_rb != null && !_rb.isKinematic)
            {
                Vector3 v = _rb.velocity;
                v.x = 0f;
                v.z = 0f;
                _rb.velocity = v;
            }
            // Fallback：无 NavMesh 也无 Rigidbody 时无需额外操作（transform 直接驱动的移动是逐帧的）
        }

        /// <summary>是否在硬直中（不能移动/攻击）。</summary>
        public bool IsStunned => _hitStunTimer > 0f || _isKnockedBack;

        /// <summary>联机(S2-4):Client 端由 NetworkEnemySetup 镜像 Host 血量(供血条/表现)。</summary>
        public void SetHealthFromNetwork(float hp)
        {
            currentHealth = Mathf.Max(0f, hp);
            OnHealthChanged?.Invoke(currentHealth);
        }

        /// <summary>
        /// 联机(A2):Client 端尸体表现(由 NetworkEnemySetup 镜像 Host 死亡状态)。
        /// 只做表现层:尸体可穿过/物理冻结/死亡动画;不含事件与掉落(掉落是 Host 权威)。
        /// </summary>
        public void SetDeadFromNetwork()
        {
            if (isDead) return;
            isDead = true;
            if (_navAgent != null) _navAgent.enabled = false;
            if (_rb != null) { _rb.isKinematic = true; _rb.useGravity = false; }
            if (_col != null) _col.enabled = false;
            if (_anim != null)
            {
                _anim.SetBool(H_IsDead, true);
                _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // IDamageable
        // ═══════════════════════════════════════════════════════════════

        public void TakeDamage(float damage, GameObject source, Vector3 hitDir)
        {
            if (isDead) return;

            // 联机(S2-4):客户端命中的伤害路由到 Host 复核落地;单机/Host 直接本地落地
            if (Game.Net.NetworkCombatRelay.TryRouteDamage(gameObject, damage, source, hitDir))
                return;

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            OnHealthChanged?.Invoke(currentHealth);

            // 受击硬直
            _hitStunTimer = Mathf.Max(_hitStunTimer, hitStunDuration);
            // 受击闪红
            _hitFlashTimer = hitFlashDuration;

            // 伤害数字飘字
            ShowDamageNumber(damage);

            if (currentHealth <= 0f)
                Die();
        }

        public void Heal(float amount)
        {
            if (isDead) return;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            OnHealthChanged?.Invoke(currentHealth);
        }

        // ═══════════════════════════════════════════════════════════════
        // IHittable
        // ═══════════════════════════════════════════════════════════════

        public void ReceiveKnockback(Vector3 impulse, float duration = 0.5f)
        {
            if (isDead) return;
            _isKnockedBack = true;
            _knockbackTimer = duration;

            // 击退：disable agent → 直接位移 → Warp 对齐 → enable
            //   关键：之前 disable→改 transform→enable 的"裸位移"会让 agent 内部 nextPosition 残留旧坐标，
            //   enable 后下一帧 agent 会把 transform 拉回 nextPosition 出现"瞬移一步"。先 Warp 对齐再 enable 就消除了。
            if (_navAgent != null && _navAgent.isOnNavMesh)
            {
                _navAgent.enabled = false;
                transform.position += impulse * duration * 0.3f;
                _navAgent.enabled = true;
                _navAgent.Warp(transform.position);
                _navAgent.velocity = Vector3.zero;
            }
            else
            {
                // 无 NavMesh：直接改 transform
                transform.position += impulse * duration * 0.3f;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 伤害数字飘字
        // ═══════════════════════════════════════════════════════════════

        private static DamageNumberSpawner _sharedDamageSpawner;

        private void ShowDamageNumber(float damage)
        {
            if (damage <= 0f) return;

            if (_sharedDamageSpawner == null)
            {
                var spawnerObj = FindObjectOfType<DamageNumberSpawner>();
                if (spawnerObj == null)
                {
                    var go = new GameObject("DamageNumberSpawner");
                    spawnerObj = go.AddComponent<DamageNumberSpawner>();
                }
                _sharedDamageSpawner = spawnerObj;
            }

            if (_sharedDamageSpawner != null)
            {
                Vector3 pos = transform.position + Vector3.up * 1.5f;
                _sharedDamageSpawner.ShowDamage(pos, damage);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 死亡
        // ═══════════════════════════════════════════════════════════════

        private void Die()
        {
            if (isDead) return;
            isDead = true;

            // 记录死亡前的 NavMesh 位置 Y（用于死亡后保持贴地）
            _deathNavY = transform.position.y;
            NavMeshHit navHit;
            if (_navAgent != null && _navAgent.isOnNavMesh)
            {
                _deathNavY = transform.position.y;
            }
            else if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out navHit, 2f, NavMesh.AllAreas))
            {
                _deathNavY = navHit.position.y;
            }

            // 停止移动
            StopMoving();

            // 禁用 NavMeshAgent（保留组件引用，避免空引用）
            if (_navAgent != null)
                _navAgent.enabled = false;

            // 禁用物理
            if (_rb != null)
            {
                // 注意：先 zero velocity/angularVelocity，再 set kinematic
                // 否则 kinematic body 上设 velocity 会抛异常
                if (!_rb.isKinematic)
                {
                    _rb.velocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }

            // 把敌人贴回 NavMesh 表面（避免浮空/沉地）
            // 关键：不要简单归零 Y，那会让站立在 NavMesh 上的敌人沉到地面以下
            Vector3 pos = transform.position;
            transform.position = new Vector3(pos.x, _deathNavY, pos.z);

            // 死亡后保持水平旋转，不要让 rigidbody 锁住旋转
            if (_rb != null)
                _rb.constraints = RigidbodyConstraints.FreezeRotation;

            // 禁用碰撞体（让玩家可以穿过尸体）
            if (_col != null)
                _col.enabled = false;

            // 播放死亡动画
            if (_anim != null)
            {
                _anim.SetBool(H_IsDead, true);
                _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            // 通知掉落系统
            OnEnemyDied?.Invoke(gameObject);

            // 触发事件
            OnDeath?.Invoke();

            // 自动销毁:联机时走 NetworkObject.Despawn(各端同步销毁尸体);单机照旧 Destroy
            if (destroyAfterDeath > 0f)
            {
                var netObj = GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                    StartCoroutine(DespawnAfterDelay(netObj, destroyAfterDeath));
                else
                    Destroy(gameObject, destroyAfterDeath);
            }
        }

        /// <summary>联机(A2):延迟 Despawn(Host 调用,NGO 自动同步销毁到各端)。</summary>
        private System.Collections.IEnumerator DespawnAfterDelay(Unity.Netcode.NetworkObject netObj, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn();
        }

        // ═══════════════════════════════════════════════════════════════
        // 受击闪红
        // ═══════════════════════════════════════════════════════════════

        private void SetHitFlash(float intensity)
        {
            if (_renderers == null) return;
            Color flashColor = Color.red * intensity;
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                // 使用 MaterialPropertyBlock 避免创建材质实例
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", flashColor);
                mpb.SetFloat("_EmissionIntensity", intensity);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
