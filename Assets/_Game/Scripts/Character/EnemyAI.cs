using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Game.Character;
using Game.SkillSystem;

namespace Game.Character
{
    /// <summary>
    /// 敌人 AI 状态机。控制敌人在场景中的行为。
    ///
    /// 状态流：
    ///   Idle (待机)
    ///     ↓ 检测到玩家
    ///   Chase (追踪)
    ///     ↓ 进入攻击范围
    ///   Attack (攻击)
    ///     ↓ 玩家离开范围
    ///   Chase ...
    ///     ↓ 玩家脱离仇恨范围 / 死亡
    ///   Return (返回出生点)
    ///     ↓ 到达出生点
    ///   Idle ...
    ///
    /// 依赖：EnemyMotor（移动+生命值）、NavMeshAgent（寻路）
    /// </summary>
    [RequireComponent(typeof(EnemyMotor))]
    [RequireComponent(typeof(NavMeshAgent))]
    [AddComponentMenu("Game/Character System/Enemy AI")]
    public class EnemyAI : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════
        // 状态枚举
        // ═══════════════════════════════════════════════════════════════

        public enum State
        {
            Idle,       // 待机（原地不动）
            Patrol,     // 巡逻（在出生点附近游走）
            Chase,      // 追踪玩家
            Attack,     // 攻击玩家
            Return,     // 返回出生点
            Dead        // 死亡
        }

        // ═══════════════════════════════════════════════════════════════
        // Inspector 配置
        // ═══════════════════════════════════════════════════════════════

        [Header("═══ 数据资产(可选,拖入后 Awake 时覆盖下方全部字段) ═══")]
        [Tooltip("敌人配方资产(行为参数 + 技能绑定 + 武器配置 + 掉落表,一体化)。\n" +
                 "优先级高于 behaviorData。\n" +
                 "留空 = 完全使用下方字段当前值(向后兼容,不影响任何现有 Prefab)。\n" +
                 "拖入资产 = Awake() 时用资产值覆盖下方字段,实现多个敌人共享同一套配置。\n" +
                 "Character Kit → Enemy Tab 提供可视化编辑 + 一键从当前值生成资产。")]
        public EnemyArchetype archetype;

        [Tooltip("敌人行为配置资产(感知/攻击/移动/血条/技能释放参数)。\n" +
                 "留空 = 完全使用下方字段当前值(向后兼容,不影响任何现有 Prefab)。\n" +
                 "拖入资产 = Awake() 时用资产值覆盖下方字段,实现多个敌人共享同一套配置。\n" +
                 "Character Kit → Enemy Tab 提供可视化编辑 + 一键从当前值生成资产。\n\n" +
                 "注意: archetype 字段优先级更高,两者都填时只用 archetype。")]
        public EnemyBehaviorData behaviorData;

        [Header("Detection")]
        [Tooltip("视野范围（米）")]
        public float detectionRange = 8f;
        [Tooltip("视野角度（度，-1=全方向感知）\n180 = 前方半圆（推荐），120 = 前方扇形偏小，360 = 全方向")]
        public float detectionAngle = 180f;
        [Tooltip("可检测的目标 Layer")]
        public LayerMask targetLayer = 1 << 8; // Layer 8 = Player

        [Header("Combat")]
        [Tooltip("攻击范围（米）")]
        public float attackRange = 1.5f;
        [Tooltip("攻击扇形半角（度）。实际判定角度 = hitAngle * 2。\n90 = 前方 180°（宽泛），60 = 前方 120°（默认），45 = 前方 90°（精准刺击）")]
        [Range(10f, 180f)]
        public float attackHitAngle = 90f;
        [Tooltip("攻击间隔（秒）。建议 ≥ attackStartUpTime + attackHitDelay + attackRecoverTime，否则下次攻击会延后。")]
        public float attackCooldown = 1.5f;
        [Tooltip("攻击伤害")]
        public float attackDamage = 10f;
        [Tooltip("攻击击退力度")]
        public float attackKnockback = 3f;
        [Tooltip("攻击前摇（秒）。期间播放攻击动画+蓄力，**位置锁**，**不可被打断**（玩家远离仍会继续挥完）。")]
        public float attackStartUpTime = 0.15f;
        [Tooltip("命中判定延时（秒）。从 PerformAttack 开始计时，命中帧时进行 OverlapSphere 判定。\n该值即 DelayedDamage 的等待时长，应与攻击动画的“挥到”帧对齐。\n命中帧期间**位置锁**，**不可被打断**。")]
        public float attackHitDelay = 0.3f;
        [Tooltip("攻击后摇（秒）。命中后到攻击完全结束之间的时间。**前半段（lockRecoveryRatio 范围内）位置锁**——这是“打击点到后摇的衔接部分”，避免命中帧与后摇边界出现位置回弹；**后半段可被玩家移动打断**（玩家远离立刻切回 Chase）。")]
        public float attackRecoverTime = 0.4f;
        [Tooltip("后摇前段锁位比例（0~1）。0 = 后摇全程不锁，1 = 后摇全程锁（等同于把后摇并入命中帧）。\n" +
                 "默认 0.35 = 后摇前 35% 时间仍保持位置锁（衔接部分），后 65% 才是可打断/可位移的自由段。\n" +
                 "锁位期间 NavMeshAgent 保持 disable、LateUpdate 仍把 transform 拉回锁定位置；\n" +
                 "锁位结束才允许被玩家距离/位移信号打断回 Chase。")]
        [Range(0f, 1f)] public float lockRecoveryRatio = 0.35f;

        [Header("Attack Animations (多个普攻随机)")]
        [Tooltip("普攻动画片段列表（Attack_1/Attack_2/...）。\n留空则退化为单套攻击（只触发 'Attack' Trigger，与旧版兼容）。\n有内容时 EnemyAI 在 PerformAttack 时随机抽一个下标，\n并 SetInteger(AttackIndex, idx) + SetTrigger(Attack)；\n配套的 AnimatorController 需有 Attack_1/Attack_2/... 状态并按 AttackIndex 条件路由。")]
        public AnimationClip[] attackClips = new AnimationClip[0];

        [Header("Movement")]
        [Tooltip("追踪速度倍率（相对 EnemyMotor.moveSpeed）")]
        public float chaseSpeedMultiplier = 1.2f;
        [Tooltip("巡逻半径（米）")]
        public float patrolRadius = 3f;
        [Tooltip("巡逻间隔（秒）")]
        public float patrolInterval = 3f;
        [Tooltip("脱离战斗后返回出生点的距离阈值")]
        public float loseTargetRange = 12f;

        [Header("Health Bar")]
        [Tooltip("是否显示头顶血条")]
        public bool showHealthBar = true;
        [Tooltip("血条宽度")]
        public float healthBarWidth = 80f;
        [Tooltip("血条高度")]
        public float healthBarHeight = 8f;
        [Tooltip("血条距头顶高度")]
        public float healthBarHeightOffset = 2.2f;
        [Tooltip("血条整体缩放")]
        public float healthBarScale = 0.01f;

        [Header("Debug")]
        public bool debugDraw = false;

        [Header("═══ 攻击技能（可选,留空用旧版资源加载逻辑） ═══")]
        [Tooltip("敌人普攻对应的 SkillData 资产数组。与 attackClips 一一对应:\n" +
                 "  · attackClips[0] 播 attackSkillDatas[0] 的动画/VFX/伤害\n" +
                 "  · attackClips[1] 播 attackSkillDatas[1] 的动画/VFX/伤害\n" +
                 "数组留空或元素不足时退回 attackSkillData 单字段(向后兼容)。\n" +
                 "两个都为空则退回 EnemyAI.attackRange/attackDamage 旧逻辑。")]
        public SkillData[] attackSkillDatas = new SkillData[0];

        [Tooltip("旧版单字段普攻 SkillData（兼容）。attackSkillDatas[] 为空时用此字段。")]
        public SkillData attackSkillData;

        [Header("═══ 主动技能槽（可选,敌人可释放 Q/W/E/R 类技能） ═══")]
        [Tooltip("敌人可释放的主动技能列表。每个技能有独立冷却。\n" +
                 "AI 在 Chase 状态下根据距离/冷却决定释放哪个技能。\n" +
                 "留空则敌人只会普攻（向后兼容）。")]
        public SkillData[] enemySkills = new SkillData[0];

        [Tooltip("技能释放概率（0~1）。每次攻击冷却结束时,有此概率释放技能而非普攻。\n" +
                 "0 = 只普攻,0.5 = 一半概率放技能,1 = 优先放技能。")]
        [Range(0f, 1f)]
        public float skillCastChance = 0.3f;

        [Tooltip("技能释放距离（米）。玩家在此距离内才考虑放技能。\n" +
                 "应大于 attackRange,小于 detectionRange。")]
        public float skillCastRange = 5f;

        // ═══════════════════════════════════════════════════════════════
        // 运行时状态
        // ═══════════════════════════════════════════════════════════════

        public State CurrentState { get; private set; } = State.Idle;

        private EnemyMotor _motor;
        private NavMeshAgent _agent;
        private Transform _target;
        private Vector3 _spawnPosition;

        private float _attackTimer = 0f;
        private float _patrolTimer = 0f;
        private Vector3 _patrolTarget;

        // 敌人技能冷却计时（与 enemySkills[] 一一对应）
        private float[] _skillCooldowns;

        // Attack 状态时锁定 transform（解决 Animator 在 agent 禁用后仍把 root 拉向 nextPosition 导致的滑步）
        private Vector3 _attackLockedPos;
        private bool _attackLockActive = false;

        // 本次攻击选中的动画下标（attackClips 中的 index；空列表时保持 0）
        private int _currentAttackIndex = 0;

        // 后摇（Recovery）阶段起点位置：用于判定"被强制位移后还在挥刀"→ 切回 Chase 播移动动画
        private Vector3 _recoveryStartPos;

        // 统一行为上下文：ExecuteSkillGraph 创建，DelayedDamage / DelayedSkillDamage 用于 HitVFX 广播
        private NodeContext _activeSkillContext;

        // 攻击阶段时间窗（解决"播放着攻击动画往前位移"/"后半段可被打断"问题）
        //   Idle     - 不在攻击中
        //   Windup   - 前摇（命中帧之前）→ 可被玩家移动打断，取消位置锁
        //   Hit      - 命中帧（PerformAttack 触发，DelayedDamage 等待 attackHitDelay）→ 锁定位置
        //   Recovery - 后摇（命中判定后到攻击完全结束）→ 锁定位置
        //   Done     - 本次攻击完成（位置已解锁），等 _attackTimer 归零后再触发下一次
        private enum AttackPhase { Idle, Windup, Hit, Recovery, Done }
        private AttackPhase _attackPhase = AttackPhase.Idle;
        // 攻击开始时累计计时（每帧 += Time.deltaTime），用于阶段推进 + 命中判定
        private float _attackElapsed = 0f;

        // GL 可视化 ID（每个敌人独立注册感知范围 + 攻击范围）
        private int  _glDetectId;
        private int  _glAttackId;
        private bool _glRegistered = false;

        // ═══════════════════════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════════════════════

        void Awake()
        {
            _motor = GetComponent<EnemyMotor>();
            _agent = GetComponent<NavMeshAgent>();
            _glDetectId = GetInstanceID();
            _glAttackId = GetInstanceID() + 1;

            // Character Kit Phase 2 数据化:archetype 优先级高于 behaviorData
            // (留空则完全不受影响,保持向后兼容)
            if (archetype != null)
                archetype.ApplyTo(this);
            else if (behaviorData != null)
                behaviorData.ApplyTo(this);
        }

        void Start()
        {
            _spawnPosition = transform.position;

            // 配置 NavMeshAgent
            if (_agent != null)
            {
                _agent.updateRotation = true;
                _agent.updatePosition = true;
                _agent.speed = _motor.moveSpeed;
                _agent.baseOffset = 0f; // 避免浮空
            }

            // 武器装备由 WeaponHolder 自身的 initialMainHandWeapon 字段驱动，
            // 不再在 EnemyAI 中重复同步。
            // WeaponHolder.Awake() 自动 ResolveMountPoints，
            // WeaponHolder.Start() 读自己的 initialMainHandWeapon 装备。

            // 初始化技能冷却数组
            if (enemySkills != null && enemySkills.Length > 0)
            {
                _skillCooldowns = new float[enemySkills.Length];
                for (int i = 0; i < _skillCooldowns.Length; i++)
                    _skillCooldowns[i] = 0f; // 开局所有技能可用
            }

            // 自动添加 EnemyHealthBar（如果不存在）
            AutoSetupHealthBar();

            // 监听死亡
            _motor.OnDeath += OnMotorDeath;
        }

        void OnDestroy()
        {
            if (_motor != null)
                _motor.OnDeath -= OnMotorDeath;

            // 注销 GL 常驻注册
            if (_glRegistered)
            {
                CombatRangeDebugDraw.UnregisterPersistent(_glDetectId);
                CombatRangeDebugDraw.UnregisterPersistent(_glAttackId);
                _glRegistered = false;
            }
        }

        /// <summary>自动添加血量条组件（如果不存在）。</summary>
        private void AutoSetupHealthBar()
        {
            if (!showHealthBar) return;

            var healthBar = GetComponent<EnemyHealthBar>();
            if (healthBar == null)
            {
                healthBar = gameObject.AddComponent<EnemyHealthBar>();
                Debug.Log("[EnemyAI] Auto-added EnemyHealthBar component");
            }

            // 同步配置
            healthBar.barWidth = healthBarWidth;
            healthBar.barHeight = healthBarHeight;
            healthBar.heightOffset = healthBarHeightOffset;
            healthBar.canvasScale = healthBarScale;
        }

        void Update()
        {
            if (CurrentState == State.Dead) return;
            if (_motor == null || _motor.isDead) return;

            // ── GL Game 视图常驻可视化（debugDraw=true 时） ──────────────
            if (debugDraw)
            {
                Vector3 pos = transform.position;
                Vector3 fwd = transform.forward;

                if (!_glRegistered)
                {
                    // 感知扇形（橙色）
                    CombatRangeDebugDraw.RegisterPersistent(_glDetectId,
                        new CombatRangeDebugDraw.PersistentShape
                        {
                            origin     = pos, forward   = fwd,
                            range      = detectionRange, halfAngle = detectionAngle * 0.5f,
                            yOffset    = 0.03f,
                            color      = new Color(1f, 0.55f, 0f, 0.5f),
                            drawCircle = detectionAngle >= 359f,
                        });
                    // 攻击扇形（红色）
                    CombatRangeDebugDraw.RegisterPersistent(_glAttackId,
                        new CombatRangeDebugDraw.PersistentShape
                        {
                            origin     = pos, forward   = fwd,
                            range      = attackRange, halfAngle = attackHitAngle,
                            yOffset    = 0.06f,
                            color      = new Color(1f, 0.1f, 0.1f, 0.7f),
                            drawCircle = false,
                        });
                    _glRegistered = true;
                }
                else
                {
                    CombatRangeDebugDraw.UpdatePersistent(_glDetectId, pos, fwd);
                    CombatRangeDebugDraw.UpdatePersistent(_glAttackId, pos, fwd);
                }
            }
            else if (_glRegistered)
            {
                CombatRangeDebugDraw.UnregisterPersistent(_glDetectId);
                CombatRangeDebugDraw.UnregisterPersistent(_glAttackId);
                _glRegistered = false;
            }

            // 实时同步血条参数到 EnemyHealthBar（让 Inspector 拖动立即生效）
            if (showHealthBar)
            {
                var hb = GetComponent<EnemyHealthBar>();
                if (hb == null)
                {
                    AutoSetupHealthBar();
                    hb = GetComponent<EnemyHealthBar>();
                }
                if (hb != null)
                {
                    if (hb.barWidth != healthBarWidth)         hb.barWidth = healthBarWidth;
                    if (hb.barHeight != healthBarHeight)       hb.barHeight = healthBarHeight;
                    if (hb.heightOffset != healthBarHeightOffset) hb.heightOffset = healthBarHeightOffset;
                    if (hb.canvasScale != healthBarScale)      hb.canvasScale = healthBarScale;
                }
            }

            // 硬直期间不执行 AI
            if (_motor.IsStunned)
            {
                // 硬直/击退期间强制 disable NavMeshAgent + Warp 对齐
                //   原因：受击瞬间 agent 还在 updatePosition=true 状态，
                //   _isKnockedBack=true 时 EnemyMotor.ReceiveKnockback 已经 disable→位移→Warp→enable，
                //   但 FixedUpdate 中 _knockbackTimer 归零之前 agent 一直会基于旧 desiredVelocity 推 transform → 滑步
                //   这里用 disable + Warp 兜底，防御性最强。
                if (_agent != null && _agent.enabled)
                {
                    if (_agent.isOnNavMesh) _agent.Warp(transform.position);
                    _agent.isStopped = true;
                    _agent.velocity = Vector3.zero;
                    _agent.enabled = false;
                }
                return;
            }
            // 硬直结束 → 恢复 agent（如果之前被 disable 了）
            if (_agent != null && !_agent.enabled && !_motor.IsStunned && CurrentState != State.Attack && CurrentState != State.Dead)
            {
                _agent.enabled = true;
                if (_agent.isOnNavMesh) _agent.Warp(transform.position);
                _agent.isStopped = false;
            }

            switch (CurrentState)
            {
                case State.Idle:     UpdateIdle();     break;
                case State.Patrol:   UpdatePatrol();   break;
                case State.Chase:    UpdateChase();    break;
                case State.Attack:   UpdateAttack();   break;
                case State.Return:   UpdateReturn();   break;
            }

            // 攻击冷却
            if (_attackTimer > 0f)
                _attackTimer -= Time.deltaTime;

            // 技能冷却递减
            if (_skillCooldowns != null)
            {
                for (int i = 0; i < _skillCooldowns.Length; i++)
                {
                    if (_skillCooldowns[i] > 0f)
                        _skillCooldowns[i] -= Time.deltaTime;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 状态逻辑
        // ═══════════════════════════════════════════════════════════════

        private void UpdateIdle()
        {
            // 停止移动
            _motor.StopMoving();

            // 检测玩家
            if (TryDetectTarget())
            {
                ChangeState(State.Chase);
                return;
            }

            // 一定时间后开始巡逻
            _patrolTimer += Time.deltaTime;
            if (_patrolTimer >= patrolInterval)
            {
                _patrolTimer = 0f;
                _patrolTarget = GetRandomPatrolPoint();
                ChangeState(State.Patrol);
            }
        }

        private void UpdatePatrol()
        {
            // 移动到巡逻点
            MoveAlongNavMesh(_patrolTarget, 0.6f);

            // 检测玩家
            if (TryDetectTarget())
            {
                ChangeState(State.Chase);
                return;
            }

            // 到达巡逻点
            float dist = Vector3.Distance(transform.position, _patrolTarget);
            if (dist < 0.5f)
            {
                ChangeState(State.Idle);
            }
        }

        private void UpdateChase()
        {
            if (_target == null)
            {
                ChangeState(State.Return);
                return;
            }

            float distToTarget = Vector3.Distance(transform.position, _target.position);

            // ── 技能释放决策：在技能射程内且冷却完毕时，有概率释放技能 ──
            if (distToTarget <= skillCastRange && _attackTimer <= 0f)
            {
                int skillIdx = PickReadySkill();
                if (skillIdx >= 0 && Random.value <= skillCastChance)
                {
                    PerformSkill(skillIdx);
                    return;
                }
            }

            // 进入攻击范围
            if (distToTarget <= attackRange)
            {
                ChangeState(State.Attack);
                return;
            }

            // 脱离仇恨范围
            if (distToTarget > loseTargetRange)
            {
                _target = null;
                ChangeState(State.Return);
                return;
            }

            // 追踪
            MoveAlongNavMesh(_target.position, chaseSpeedMultiplier);
        }

        private void UpdateAttack()
        {
            if (_target == null)
            {
                ChangeState(State.Return);
                return;
            }

            float distToTarget = Vector3.Distance(transform.position, _target.position);

            // ────────────────────────────────────────────────────────────
            // 修复：攻击结束(或尚未开始)且玩家已超出攻击范围 → 切回 Chase 追击
            //   根因：原逻辑只在 Recovery 后段(free window)检测「玩家远离」，攻击一轮走完
            //   (Done→Idle) 后若玩家已拉开到 attackRange*1.2 之外、但仍在仇恨范围内，
            //   敌人会卡死在 Attack 状态原地发呆，不再追击。
            //   这里在 Idle / Done(已挥完、等待冷却) 阶段补一个常态距离检测：
            //   只要玩家超出 attackRange*1.2 就切回 Chase；超远距离(>loseTargetRange)
            //   由 UpdateChase 内已有的脱战判定继续处理，链路完整。
            //   注意：Windup / Hit / Recovery 前段刻意不检测，保证挥刀到命中帧不被距离打断。
            // ────────────────────────────────────────────────────────────
            if ((_attackPhase == AttackPhase.Idle || _attackPhase == AttackPhase.Done)
                && distToTarget > attackRange * 1.2f)
            {
                ChangeState(State.Chase);
                return;
            }

            // 朝向玩家
            _motor.RotateTowards(_target.position);
            // 注意：lock window 期间不调 StopMoving()，让 EnemyMotor.Update 里的 Speed 压制正常工作
            // （StopMoving 会把 agent.velocity 清零，进而让 Speed=0 → Animator 跳回 Idle）
            // free window 阶段由下方逻辑控制 agent，不需要 StopMoving

            // 兜底：lock window 期间确认 NavMeshAgent 处于 disable 状态 + 内部 nextPosition 与 transform 对齐
            //   滑步根因：NavMeshAgent 的 updatePosition=true 会无视 LateUpdate 的硬锁把 transform 拉回 nextPosition；
            //   关闭 agent 之前先 Warp 到当前 transform，可避免"agent 内部 nextPosition 与 transform 不一致"，
            //   下次恢复时直接以当前位置为起点，零瞬移。
            //   注意：进入 Recovery 的 free window 后要"放"agent 重新接管移动（位置解锁 + 允许打断），
            //         此时本兜底段不再 disable agent（让 free window 真正生效）。
            float lockRecoveryEndTime = attackStartUpTime + attackHitDelay + attackRecoverTime * Mathf.Clamp01(lockRecoveryRatio);
            bool  inLockWindow = _attackLockActive || _attackPhase != AttackPhase.Recovery ||
                                  _attackElapsed < lockRecoveryEndTime;
            if (_agent != null && inLockWindow)
            {
                if (_agent.enabled)
                {
                    // agent 还开着 → 先 Warp 对齐 → 再 disable（防御性兜底：每帧确保一次）
                    if (_agent.isOnNavMesh) _agent.Warp(transform.position);
                    _agent.isStopped = true;
                    _agent.velocity = Vector3.zero;
                    _agent.enabled = false;
                }
                else if (_agent.isOnNavMesh)
                {
                    // agent 已 disable 但 nextPosition 残留 → 再 Warp 一次保险（多次 Warp 不会出错）
                    _agent.Warp(transform.position);
                }
            }

            // ────────────────────────────────────────────────────────────
            // 攻击时间窗阶段推进
            //   Windup   (0~attackStartUpTime)                          → 位置锁，不可被打断
            //   Hit      (attackStartUpTime~+hit)                       → 位置锁，不可被打断（命中帧必须跑完）
            //   Recovery (attackStartUpTime+hit~+recover)
            //      · 前段 (lock window, [0, lockRecoveryRatio])         → 位置锁，不可被打断
            //                = "打击点到后摇的衔接部分"，防止 Animator Attack→Walk 切换过早与命中帧尾巴重叠
            //      · 后段 (free window, [lockRecoveryRatio, 1])         → 解锁，可被打断（玩家远离/被强制位移即切回 Chase）
            //   Done     (累计时长 ≥ startup+hit+recover)               → 等 _attackTimer 归零再触发下次 PerformAttack
            // ────────────────────────────────────────────────────────────
            _attackElapsed += Time.deltaTime;

            // 阶段推进：Windup → Hit → Recovery → Done
            //   Windup 进入瞬间：记录起点 + 启位置锁（防攻击动画的 Root 推送）
            if (_attackPhase == AttackPhase.Idle && _attackTimer <= 0f && distToTarget <= attackRange * 1.2f)
            {
                _attackPhase    = AttackPhase.Windup;
                _attackElapsed  = 0f;
                // 前摇起始：立即记录锁定位置 + 启位置锁
                _attackLockedPos = transform.position;
                _attackLockActive = true;
                PerformAttack();
                _attackTimer = attackCooldown;
            }
            if (_attackPhase == AttackPhase.Windup && _attackElapsed >= attackStartUpTime)
            {
                _attackPhase    = AttackPhase.Hit;
                // 命中帧开始：刷新一次锁定位置（防 Animator 上一帧已轻微推过起点）
                _attackLockedPos = transform.position;
                _attackLockActive = true;
            }
            if (_attackPhase == AttackPhase.Hit && _attackElapsed >= attackStartUpTime + attackHitDelay)
            {
                _attackPhase    = AttackPhase.Recovery;
                // 命中帧结束 → 进入后摇；
                // 位置锁在前段（lockRecoveryRatio 范围内）继续生效，这是"打击点到后摇的衔接部分不能打断不能位移"的实现点；
                // 后段才允许被打断（_attackElapsed 越过 lockRecoveryEndTime 时下面中断判定才生效）。
                // 注意：避免与上方兜底段（line ~394）中的 lockRecoveryEndTime 局部变量重名导致 CS0136，
                //       此处用 lockRecoveryEndTimeHit 表示"Hit→Recovery 切瞬间算出的后摇锁位截止时间"。
                float lockRecoveryEndTimeHit = attackStartUpTime + attackHitDelay + attackRecoverTime * Mathf.Clamp01(lockRecoveryRatio);
                _attackLockActive = (_attackElapsed < lockRecoveryEndTimeHit);
                // 记录后摇起点位置，用来判定"被强制位移"打断（仅在后段生效）
                _recoveryStartPos = transform.position;
            }
            if (_attackPhase == AttackPhase.Recovery && _attackElapsed >= attackStartUpTime + attackHitDelay + attackRecoverTime)
            {
                _attackPhase    = AttackPhase.Done;
                // 后摇结束，冗余防御：确保位置锁是关的
                _attackLockActive = false;
            }

            // Done 阶段：等冷却归零后重置为 Idle，允许触发下一次攻击
            // 关键：缺少此步会导致 _attackPhase 永远停留在 Done，
            // 触发条件 (phase==Idle) 永不满足 → 敌人攻击后原地停顿不再攻击
            if (_attackPhase == AttackPhase.Done && _attackTimer <= 0f)
            {
                _attackPhase   = AttackPhase.Idle;
                _attackElapsed = 0f;
            }

            // 后摇（Recovery）阶段：可被玩家移动打断 → 取消攻击，切回 Chase（自然播移动动画）
            //   打断窗口：仅在后摇"后段"（free window）生效。
            //     锁位窗口（lock window）= 后摇前段（lockRecoveryRatio 范围内），与"打击点到后摇的衔接部分"语义对齐：
            //       - 这一段不能被玩家移动打断，位置锁继续生效，agent 保持 disable；
            //       - 用于解决"刚进入后摇立即切回移动"导致 Animator Attack→Walk 过渡与命中帧尾巴重叠的视觉异常。
            //   free window 起点：elapsed ≥ attackStartUpTime + attackHitDelay + attackRecoverTime * lockRecoveryRatio
            //   打断条件（任一满足即切回）：
            //     1) 玩家远离：distToTarget > attackRange * 1.2f
            //     2) 产生位移：相对后摇起点 _recoveryStartPos 位移 > 0.05m（被强制位移后还在挥刀会很怪）
            //   切到 Chase 后，UpdateChase → MoveAlongNavMesh → _anim.SetFloat(Speed, …)
            //   Animator 看到 Speed>0 自动从 Attack 状态过渡到 Walk。
            //   此外，离开 Attack 时 EnemyAI.ChangeState 会调用 _motor.CrossFadeToLocomotion(...)
            //   显式 CrossFade 到 Walk 状态，固定过渡时长，避免"切回移动时卡一下攻击尾帧"的问题。
            float recoveryFreeThreshold = attackStartUpTime + attackHitDelay + attackRecoverTime * Mathf.Clamp01(lockRecoveryRatio);
            bool  inRecoveryFreeWindow  = _attackPhase == AttackPhase.Recovery && _attackElapsed >= recoveryFreeThreshold;
            if (inRecoveryFreeWindow)
            {
                // 第一次进入 free window：关掉位置锁、让 NavMeshAgent 重新接管 X/Z（不切状态）
                if (_attackLockActive)
                {
                    _attackLockActive = false;
                    // 后摇后段开始：把 agent 重新 enable，让它接管移动
                    //   顺序：Warp 对齐 → enable → velocity=0 → SetDestination(自身) 兜底
                    //   避免 agent 启用瞬间还带着攻击前 warp 时的旧 desiredVelocity 推一帧
                    if (_agent != null)
                    {
                        if (_agent.isOnNavMesh) _agent.Warp(transform.position);
                        if (!_agent.enabled) _agent.enabled = true;
                        _agent.velocity = Vector3.zero;
                        _agent.SetDestination(transform.position);
                        _agent.isStopped = false;
                    }
                }
                float movedDistance = Vector3.Distance(transform.position, _recoveryStartPos);
                bool targetOutOfRange = distToTarget > attackRange * 1.2f;
                bool hasDisplacement  = movedDistance > 0.05f;
                if (targetOutOfRange || hasDisplacement)
                {
                    _attackPhase    = AttackPhase.Idle;
                    _attackLockActive = false;
                    // 切到 Chase 前再 Warp 一次保险（确保 agent 内部 nextPosition 与 transform 完全对齐）
                    if (_agent != null && _agent.enabled)
                    {
                        if (_agent.isOnNavMesh) _agent.Warp(transform.position);
                        _agent.velocity = Vector3.zero;
                    }
                    ChangeState(State.Chase);
                    return;
                }
            }

            // Windup / Hit：忽略距离变化，必须挥到命中帧
            //   Windup（前摇）：玩家即使走远也要把蓄力挥完 → 敌人一定要砍你一刀
            //   Hit（命中帧）：强制把命中判定跑完，避免挥到一半人跑了伤害没结算

            // 攻击触发：见上方"阶段推进"块首条（Idle + _attackTimer<=0 + 距离足够）→ 启动新一次攻击（Windup）
            //   这样保证：每次攻击的 PerformAttack 只触发一次，命中判定只发一次。
        }

        private void UpdateReturn()
        {
            float distToSpawn = Vector3.Distance(transform.position, _spawnPosition);
            if (distToSpawn < 0.5f)
            {
                ChangeState(State.Idle);
                return;
            }

            MoveAlongNavMesh(_spawnPosition, 0.8f);

            // 返回途中检测到玩家
            if (TryDetectTarget())
            {
                ChangeState(State.Chase);
                return;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 核心行为
        // ═══════════════════════════════════════════════════════════════

        /// <summary>尝试检测玩家目标。</summary>
        private bool TryDetectTarget()
        {
            // 如果 targetLayer 未配置（=0），自动检测带 IDamageable 的对象，
            // 但排除其他敌人（EnemyAI）——敌人默认只攻击玩家，不攻击同类，避免互殴。
            Collider[] targets;
            if (targetLayer == 0)
            {
                // Fallback: 检测所有层，然后过滤 IDamageable 并排除其他敌人
                targets = Physics.OverlapSphere(transform.position, detectionRange);
                var filtered = new System.Collections.Generic.List<Collider>();
                foreach (var c in targets)
                {
                    if (c.transform.root == transform.root) continue;               // 排除自身
                    if (c.GetComponentInParent<IDamageable>() == null) continue;    // 只认可受伤对象
                    if (c.GetComponentInParent<EnemyAI>() != null) continue;        // 排除其他敌人，避免互殴
                    filtered.Add(c);
                }
                targets = filtered.ToArray();
            }
            else
            {
                targets = Physics.OverlapSphere(transform.position, detectionRange, targetLayer);
            }

            if (targets.Length == 0) return false;

            foreach (var col in targets)
            {
                // 排除自身
                if (col.transform.root == transform.root) continue;

                // 角度检测
                if (detectionAngle > 0f && detectionAngle < 360f)
                {
                    Vector3 toTarget = col.transform.position - transform.position;
                    toTarget.y = 0f;
                    if (toTarget.sqrMagnitude < 0.01f) continue;
                    float angle = Vector3.Angle(transform.forward, toTarget.normalized);
                    if (angle > detectionAngle * 0.5f) continue;
                }

                _target = col.transform;
                return true;
            }

            return false;
        }

        /// <summary>使用 NavMeshAgent 寻路并移动。</summary>
        private void MoveAlongNavMesh(Vector3 destination, float speedMultiplier)
        {
            // NavMeshAgent 不可用时，直接朝目标移动
            if (_agent == null)
            {
                FallbackMove(destination, speedMultiplier);
                return;
            }

            // agent 被 disable（攻击期间）→ 先 re-enable + Warp 对齐，再正常寻路
            if (!_agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.Warp(transform.position);
                _agent.enabled = true;
                // 上方已有 Warp，这里补一次确认
                if (_agent.isOnNavMesh) _agent.Warp(transform.position);
                _agent.velocity = Vector3.zero;
            }

            if (!_agent.isOnNavMesh)
            {
                FallbackMove(destination, speedMultiplier);
                return;
            }

            // NavMeshAgent 直接驱动移动（updatePosition=true）
            _agent.isStopped = false;
            _agent.speed = _motor.moveSpeed * speedMultiplier;
            _agent.SetDestination(destination);
        }

        /// <summary>无 NavMesh 时的 fallback 移动：直接朝目标方向走。</summary>
        private void FallbackMove(Vector3 destination, float speedMultiplier)
        {
            Vector3 toDest = destination - transform.position;
            toDest.y = 0f;
            float dist = toDest.magnitude;
            if (dist < 0.1f)
            {
                _motor.StopMoving();
                return;
            }

            Vector3 dir = toDest.normalized;
            _motor.MoveTo(dir, speedMultiplier);
            _motor.RotateTowards(transform.position + dir);
        }

        /// <summary>执行攻击。</summary>
        private void PerformAttack()
        {
            if (_target == null) return;

            // 只从非空攻击槽位中随机选择，保留原始槽位索引以匹配 Animator 和 SkillData。
            _currentAttackIndex = SelectAttackSlot();

            // 播放攻击动画（_currentAttackIndex 决定走 Animator 哪一段 Attack 状态）
            _motor.PlayAttackAnimation(_currentAttackIndex);

            // 当前普攻段对应的 SkillData（用于 VFX/SFX/伤害参数）
            var curSkill = GetCurrentAttackSkillData();

            // 攻击特效：统一行为体系驱动（VFX 从 graphData 节点读取）
            ExecuteSkillGraph(curSkill);

            // 命中判定延时：优先用 SkillData.frontSwing * clipLength，退回 attackHitDelay
            float hitDelay = attackHitDelay;
            if (curSkill != null)
            {
                float clipLen = GetClipLength(curSkill.animClipName);
                if (clipLen > 0.1f)
                    hitDelay = Mathf.Max(0.1f, curSkill.frontSwing * clipLen);
            }
            StartCoroutine(DelayedDamage(hitDelay));

            if (debugDraw)
                Debug.Log($"[EnemyAI] {name} attacked {_target.name} (idx={_currentAttackIndex}) for {(curSkill != null ? SkillData.GetDamageFromGraph(curSkill) : attackDamage)} damage");
        }

        private int SelectAttackSlot()
        {
            if (attackClips == null || attackClips.Length == 0)
                return 0;

            int validCount = 0;
            for (int i = 0; i < attackClips.Length; i++)
            {
                if (attackClips[i] != null)
                    validCount++;
            }

            if (validCount == 0)
                return 0;

            int selected = Random.Range(0, validCount);
            for (int i = 0; i < attackClips.Length; i++)
            {
                if (attackClips[i] == null) continue;
                if (selected-- == 0) return i;
            }

            return 0;
        }

        /// <summary>
        /// 获取当前普攻段对应的 SkillData。
        /// 查找优先级：attackSkillDatas[idx] → attackSkillData(单字段) → null
        /// </summary>
        private SkillData GetCurrentAttackSkillData()
        {
            // 1. 数组优先：按当前攻击下标取
            if (attackSkillDatas != null && attackSkillDatas.Length > 0)
            {
                int idx = Mathf.Clamp(_currentAttackIndex, 0, attackSkillDatas.Length - 1);
                if (attackSkillDatas[idx] != null)
                    return attackSkillDatas[idx];
            }
            // 2. 单字段兼容
            return attackSkillData;
        }

        // ═══════════════════════════════════════════════════════════════
        // 技能释放（enemySkills[]）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 从 enemySkills[] 中找一个冷却完毕的技能。
        /// 优先选冷却最久没用的（简单轮询，避免总放第一个）。
        /// 返回 -1 表示没有可用技能。
        /// </summary>
        private int PickReadySkill()
        {
            if (enemySkills == null || enemySkills.Length == 0) return -1;
            if (_skillCooldowns == null) return -1;

            int best = -1;
            float bestCooldown = float.MaxValue;
            for (int i = 0; i < enemySkills.Length; i++)
            {
                if (enemySkills[i] == null) continue;
                if (_skillCooldowns[i] > 0f) continue;
                // 选冷却剩余最少（其实都是 0，取第一个即可，但保留扩展性）
                if (_skillCooldowns[i] < bestCooldown)
                {
                    bestCooldown = _skillCooldowns[i];
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// 释放指定槽位的技能。
        /// 复用 PerformAttack 的时间窗逻辑（前摇→命中→后摇），
        /// 但伤害/范围/VFX 从 SkillData 读取，命中判定走 DelayedSkillDamage。
        /// </summary>
        private void PerformSkill(int skillIdx)
        {
            if (_target == null) return;
            var skill = enemySkills[skillIdx];
            if (skill == null) return;

            // 朝向目标
            _motor.RotateTowards(_target.position);

            // 播放技能动画（用 SkillData 的 animTrigger）
            var animator = GetComponent<Animator>();
            if (animator != null && !string.IsNullOrEmpty(skill.animTrigger))
                animator.SetTrigger(skill.animTrigger);

            // 起手 VFX（统一行为体系驱动）
            ExecuteSkillGraph(skill);

            // 命中判定延时（用技能的 frontSwing * clipLength 近似命中帧时机）
            float clipLen = GetClipLength(skill.animClipName);
            float hitDelay = Mathf.Max(0.1f, skill.frontSwing * clipLen);
            StartCoroutine(DelayedSkillDamage(hitDelay, skill));

            // 设置冷却
            _skillCooldowns[skillIdx] = skill.cooldown;
            _attackTimer = hitDelay + skill.backSwing * clipLen + 0.3f; // 技能后摇期间不普攻

            if (debugDraw)
                Debug.Log($"[EnemyAI] {name} cast skill [{skill.skillName}] idx={skillIdx} dmg={SkillData.GetDamageFromGraph(skill)} range={SkillData.GetMeleeParamsFromGraph(skill).range}");
        }

        /// <summary>技能延迟伤害判定，参数从 graphData 读取（方案 C 驱动）。</summary>
        private System.Collections.IEnumerator DelayedSkillDamage(float delay, SkillData skill)
        {
            yield return new WaitForSeconds(delay);

            if (_target == null || _motor == null || _motor.isDead) yield break;
            if (_motor.IsStunned) yield break;

            // 方案 C：从 graphData 读参数（MeleeSwingData / RectShotData / BeamData）
            float effectiveDmg = SkillData.GetDamageFromGraph(skill);
            var (range, hitRadius, hitAngle) = SkillData.GetMeleeParamsFromGraph(skill);
            float halfAngle = hitAngle;

            Vector3 origin = transform.position + Vector3.up * 0.8f;
            Vector3 fwd = transform.forward;

            // 检查 graphData 中的节点类型来决定判定方式
            RectShotData rectShot = null;
            BeamData beam = null;
            if (skill.graphData != null)
            {
                for (int i = 0; i < skill.graphData.Count; i++)
                {
                    if (rectShot == null && skill.graphData[i] is RectShotData rs) rectShot = rs;
                    if (beam == null && skill.graphData[i] is BeamData b) beam = b;
                    if (rectShot != null && beam != null) break;
                }
            }

            var hitTargets = new HashSet<GameObject>();

            // ── RectShot 路径：生成 RectProjectile ─────────────────
            if (rectShot != null)
            {
                var go = new GameObject($"[EnemySkill]_{skill.skillName}");
                go.transform.position = transform.position + fwd * rectShot.forwardOffset + Vector3.up * rectShot.spawnHeight;
                go.transform.rotation = Quaternion.LookRotation(fwd);
                var rp = go.AddComponent<RectProjectile>();
                rp.shotWidth = rectShot.width;
                rp.shotHeight = rectShot.height;
                rp.speed = rectShot.speed;
                rp.maxRange = rectShot.maxRange;
                // 使用最完整 Initialize 重载，参数全从 graphData 读取
                rp.Initialize(effectiveDmg, gameObject, targetLayer, skill, rectShot.projectilePrefab,
                    rectShot.projectileLocalOffset, rectShot.projectileLocalEulerOffset,
                    rectShot.projectileLifetime, rectShot.projectileLocalScale,
                    rectShot.shape, rectShot.hitMode, rectShot.pierceCount,
                    rectShot.homingEnabled, rectShot.homingTurnRate, rectShot.homingSearchRadius);
                yield break;
            }

            // ── Beam 路径：SphereCastAll ───────────────────────────
            if (beam != null)
            {
                float beamRange = beam.range > 0f ? beam.range : range;
                float beamWidth = 0.25f;
                RaycastHit[] beamHits = Physics.SphereCastAll(origin, beamWidth, fwd, beamRange, targetLayer);
                foreach (var bh in beamHits)
                {
                    if (bh.collider.transform == transform) continue;
                    if (hitTargets.Contains(bh.collider.gameObject)) continue;
                    var d = bh.collider.GetComponentInParent<IDamageable>();
                    if (d == null || d.isDead) continue;
                    d.TakeDamage(effectiveDmg, gameObject, fwd);
                    hitTargets.Add(bh.collider.gameObject);
                }
                // 统一 HitVFX 广播
                if (_activeSkillContext != null && hitTargets.Count > 0)
                    _activeSkillContext.BroadcastHit(HitInfo.Many(new List<GameObject>(hitTargets), origin, fwd));
                yield break;
            }

            // ── 默认：Melee / AOE 路径（OverlapCapsule + 扇形过滤）───
            float effectiveRange = range;
            float effectiveRadius = hitRadius;
            Vector3 capsuleTip = origin + fwd * effectiveRange;

            Collider[] hits = Physics.OverlapCapsule(
                origin, capsuleTip, effectiveRadius,
                targetLayer, QueryTriggerInteraction.Collide);

            foreach (var h in hits)
            {
                if (h == null) continue;
                if (h.transform == transform || h.transform.IsChildOf(transform)) continue;
                if (hitTargets.Contains(h.gameObject)) continue;

                var d = h.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;

                Vector3 closest = h.ClosestPoint(origin);
                Vector3 toTarget = closest - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f) toTarget = fwd;
                float angle = Vector3.Angle(fwd, toTarget.normalized);
                if (angle > halfAngle) continue;

                d.TakeDamage(effectiveDmg, gameObject, toTarget.normalized);
                hitTargets.Add(h.gameObject);

                // 统一 HitVFX 收集（广播在循环外）

                if (debugDraw)
                    Debug.Log($"[EnemyAI] {name} skill hit {h.name} angle={angle:F1}° dmg={effectiveDmg}");
            }

            // ── 统一 HitVFX 广播（替代 SpawnSkillHitVFX）──
            if (_activeSkillContext != null && hitTargets.Count > 0)
                _activeSkillContext.BroadcastHit(HitInfo.Many(new List<GameObject>(hitTargets), origin, fwd));
        }

        /// <summary>从 Animator 获取 Clip 长度（缓存）。</summary>
        private float GetClipLength(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return 0.5f;
            var animator = GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return 0.5f;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && clip.name == clipName)
                    return clip.length;
            }
            return 0.5f;
        }

        // ═══════════════════════════════════════════════════════════════
        // 统一行为接入（2026-07-21 执行层统一）
        // 替代 SpawnAttackVFX / SpawnSkillVFX: 遍历 graphData 对每个节点调 OnTick
        // ctx 存到 _activeSkillContext 供后续 BroadcastHit 复用
        // ═══════════════════════════════════════════════════════════════
        private void ExecuteSkillGraph(SkillData skill)
        {
            if (skill?.graphData == null || skill.graphData.Count == 0) return;

            float clipLen = GetClipLength(skill.animClipName);
            if (clipLen <= 0f) clipLen = 1f;

            var ctx = new NodeContext
            {
                skill      = skill,
                source     = gameObject,
                caster     = transform,
                hitMask    = targetLayer,
                damage     = attackDamage,
                range      = attackRange,
                animTime   = 0f,
                isPreview  = false,
                sink       = RuntimeSkillEffectSink.Instance,
                debugDraw  = debugDraw,
            };
            ctx.ResetPerCast();
            _activeSkillContext = ctx;

            for (int i = 0; i < skill.graphData.Count; i++)
            {
                var node = skill.graphData[i];
                if (node == null) continue;

                float triggerT = node.GetTriggerTime(skill, clipLen);
                if (triggerT > 0f)
                    StartCoroutine(DelayedNodeTick(node, ctx, triggerT, i));
                else
                    FireNodeNow(node, ctx, i);
            }
        }

        private void FireNodeNow(SkillNodeData node, NodeContext ctx, int nodeIndex)
        {
            ctx.currentNodeIndex = nodeIndex;
            ctx.animTime = 0.001f; // >0 以通过 triggerTime = 0 守卫
            node.OnTick(ctx);
            ctx.currentNodeIndex = -1;
        }

        private System.Collections.IEnumerator DelayedNodeTick(SkillNodeData node, NodeContext ctx, float delay, int nodeIndex)
        {
            yield return new WaitForSeconds(delay);
            ctx.currentNodeIndex = nodeIndex;
            ctx.animTime = delay + 0.001f; // >= triggerTime 以通过守卫
            node.OnTick(ctx);
            ctx.currentNodeIndex = -1;
        }

        /// <summary>
        /// 延迟造成伤害（等待攻击动画命中帧）。
        /// 与主角攻击流程对齐：优先从 WeaponHolder 拿主手武器实例 + WeaponData 的战斗修正；
        /// 角色没装备武器时退回 EnemyAI 自身的 attackRange / attackDamage 旧逻辑（向后兼容）。
        ///
        /// 修复项：
        ///   1) OverlapSphere → OverlapCapsule，避免覆盖身后区域
        ///   2) ClosestPoint 扇形过滤（attackHitAngle），精度优于轴心
        ///   3) 加 isKnockedBack 检查：敌人被击飞/硬直中不触发命中
        ///   4) hitMask 改用 targetLayer（Player 层），不再全层检测
        ///   5) _hitTargets HashSet 去重，同一次攻击不重复命中同一目标
        /// </summary>
        private System.Collections.IEnumerator DelayedDamage(float delay)
        {
            yield return new WaitForSeconds(delay);

            // 敌人已死亡 / 被击退中 → 取消本次命中
            if (_target == null || _motor == null || _motor.isDead) yield break;
            if (_motor.IsStunned) yield break;

            // ── 武器修正 ─────────────────────────────────────────────────
            var weaponHolder  = GetComponent<WeaponHolder>();
            var weaponInstance = weaponHolder != null ? weaponHolder.GetWeaponInstance(WeaponSlotType.MainHand) : null;
            var weaponMods    = weaponHolder != null ? weaponHolder.GetCombatModifiers() : WeaponCombatModifiers.Default;

            Transform attackT       = weaponInstance != null ? weaponInstance.transform : transform;
            // 优先从 graphData 读参数，其次 SkillData legacy，最后用 EnemyAI 自身字段
            var curSkill = GetCurrentAttackSkillData();
            float baseDamage;
            float baseRange;
            float baseHitAngle;
            if (curSkill != null)
            {
                baseDamage = SkillData.GetDamageFromGraph(curSkill);
                var (r, _, a) = SkillData.GetMeleeParamsFromGraph(curSkill);
                baseRange = r;
                baseHitAngle = a;
            }
            else
            {
                baseDamage = 0f;
                baseRange = 0f;
                baseHitAngle = 0f;
            }
            // 当 graphData 为空或未配置时，退化到 EnemyAI 自身字段
            if (baseDamage <= 0f) baseDamage = attackDamage;
            if (baseRange <= 0f)  baseRange  = attackRange;
            if (baseHitAngle <= 0f) baseHitAngle = attackHitAngle;
            float effectiveRange    = baseRange + weaponMods.rangeBonus;
            float effectiveRadius   = 0.3f + weaponMods.hitRadiusBonus;
            float effectiveDmg      = baseDamage * Mathf.Max(0.01f, weaponMods.damageMultiplier);
            float effectiveKnock    = attackKnockback;
            float halfAngle         = baseHitAngle;

            // ── OverlapCapsule：沿前方延伸，覆盖挥刀弧线 ────────────────
            Vector3 origin     = attackT.position + Vector3.up * 0.5f; // 腰部高度
            Vector3 fwd        = attackT.forward;
            Vector3 capsuleTip = origin + fwd * effectiveRange;

            // 本次攻击命中缓存（防同帧多 Collider 指向同一目标重复结算）
            var hitTargets = new HashSet<GameObject>();

            Collider[] hits = Physics.OverlapCapsule(
                origin, capsuleTip, effectiveRadius,
                targetLayer, QueryTriggerInteraction.Collide);

            bool damaged = false;
            foreach (var h in hits)
            {
                if (h == null) continue;
                if (h.transform == transform || h.transform.IsChildOf(transform)) continue;
                if (hitTargets.Contains(h.gameObject)) continue;

                var d = h.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;

                // ── 扇形过滤：ClosestPoint 精度优于轴心 ──────────────────
                Vector3 closest  = h.ClosestPoint(origin);
                Vector3 toTarget = closest - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f) toTarget = fwd;

                float angle = Vector3.Angle(fwd, toTarget.normalized);
                if (angle > halfAngle) continue;

                // ── 结算伤害 ─────────────────────────────────────────────
                Vector3 hitDir = toTarget.normalized;
                d.TakeDamage(effectiveDmg, gameObject, hitDir * effectiveKnock);
                hitTargets.Add(h.gameObject);
                damaged = true;

                if (debugDraw)
                    Debug.Log($"[EnemyAI] {name} hit {h.name} angle={angle:F1}° dmg={effectiveDmg:F1}" +
                              (weaponInstance != null ? " (weapon)" : " (bare)"));
            }

            // ── 统一 HitVFX 广播（替代内联 FindFirstNode<HitVFXData>）──
            if (_activeSkillContext != null && hitTargets.Count > 0)
                _activeSkillContext.BroadcastHit(HitInfo.Many(new List<GameObject>(hitTargets), origin, fwd));

            if (!damaged && debugDraw)
                Debug.Log($"[EnemyAI] {name} attack missed (no target in range/angle)");

            // 命中帧可视化（运行时 DrawLine，配合 debugDraw）
            if (debugDraw)
            {
                Quaternion lRot = Quaternion.AngleAxis(-halfAngle, Vector3.up);
                Quaternion rRot = Quaternion.AngleAxis( halfAngle, Vector3.up);
                float drawDur = 0.2f;
                Debug.DrawRay(origin, lRot * fwd * effectiveRange, Color.red,    drawDur);
                Debug.DrawRay(origin, rRot * fwd * effectiveRange, Color.red,    drawDur);
                Debug.DrawRay(origin, fwd  * effectiveRange,       Color.yellow, drawDur);
            }
        }

        /// <summary>获取巡逻点。</summary>
        private Vector3 GetRandomPatrolPoint()
        {
            Vector2 random = Random.insideUnitCircle * patrolRadius;
            return _spawnPosition + new Vector3(random.x, 0f, random.y);
        }

        /// <summary>状态切换。</summary>
        private void ChangeState(State newState)
        {
            if (CurrentState == newState) return;

            // ────────────────────────────────────────────────────────────
            // 退出旧状态时的清理（关键：解决攻击滑步）
            // ────────────────────────────────────────────────────────────
            if (CurrentState == State.Attack && newState != State.Attack)
            {
                // 解除 Attack 位置硬锁
                _attackLockActive = false;
                // 重置攻击阶段机（避免状态污染）
                _attackPhase   = AttackPhase.Idle;
                _attackElapsed = 0f;

                // 离开 Attack 时播"攻击→移动"过渡：
                //   ResetTrigger(Attack)  +  CrossFade 到 Walk 状态，固定过渡时长。
                //   - 不依赖 Speed>0 自然过渡（攻击末段 Speed 经常是 0，trigger 自然 fade 容易卡一下）
                //   - 强制 0.12s 切到 Walk，命中帧刚结束就能切到跑动，视觉上"敌人把刀收起来小跑过来"
                //   下一帧 UpdateChase/UpdateReturn 会通过 _anim.SetFloat(Speed, …) 接管 Walk 的快慢
                if (_motor != null)
                {
                    _motor.CrossFadeToLocomotion(0.12f);
                }

                // 离开 Attack → 恢复 NavMeshAgent：
                //   顺序必须是 Warp → enabled=true → isStopped=false
                //   关键：Warp 之后立即设一个"原地"destination，让 agent 重新规划、velocity=0，
                //         避免它带着恢复瞬间的旧 desiredVelocity 移动一帧（"切回移动闪一下"）
                //   真正的 target 会在下一帧 UpdateChase/UpdateReturn 里通过 SetDestination 覆盖。
                if (_agent != null)
                {
                    // 先 Warp 对齐（无论 agent 当前是否 enabled，都做一次更稳）
                    if (_agent.isOnNavMesh) _agent.Warp(transform.position);
                    if (!_agent.enabled) _agent.enabled = true;
                    _agent.velocity = Vector3.zero;
                    // 关键：把 destination 设到当前 transform（原地），让 agent 内部 desiredVelocity=0
                    // 下游 UpdateChase/UpdateReturn 会立即覆盖成真实目标
                    _agent.SetDestination(transform.position);
                    _agent.isStopped = false;
                }
            }

            if (debugDraw)
                Debug.Log($"[EnemyAI] {name}: {CurrentState} → {newState}");

            CurrentState = newState;

            // ────────────────────────────────────────────────────────────
            // 进入新状态时的初始化
            // ────────────────────────────────────────────────────────────
            if (newState == State.Attack)
            {
                // 重置攻击时间窗：阶段从 Idle 起，下一帧 UpdateAttack 的阶段机会把它推到 Windup。
                //   关键：进入 Attack 不立刻 _attackLockActive=true，
                //   真正启用位置锁是在 UpdateAttack 内部从 Idle 触发新攻击时（Windup 起点），
                //   命中帧结束时（Hit→Recovery）再解锁（与新语义：前摇 + 命中帧锁、后摇不锁）。
                _attackPhase   = AttackPhase.Idle;
                _attackElapsed = 0f;
                _attackLockActive = false;

                // 进入 Attack → 同步 disable NavMeshAgent
                //   顺序：Warp 对齐 → velocity 清零 → disable
                //   关键：Warp 必须在 disable 之前，让 agent 内部 nextPosition 与 transform 一致；
                //   下次恢复（leave Attack）时 Warp 又会对齐到离开时的位置，实现"对齐-锁定-对齐"完整闭环。
                //   applyRootMotion=false 由 EnemyPrefabSetup 设好，攻击中 transform 不会被 Animator 推。
                if (_agent != null)
                {
                    if (_agent.enabled)
                    {
                        if (_agent.isOnNavMesh) _agent.Warp(transform.position);
                        _agent.isStopped = true;
                        _agent.velocity = Vector3.zero;
                        _agent.enabled = false;
                    }
                }
            }
            else if (newState == State.Idle || newState == State.Return)
            {
                if (_agent != null && _agent.isOnNavMesh)
                    _agent.isStopped = true;
                _motor.StopMoving();
            }

            _patrolTimer = 0f;
        }

        private void OnMotorDeath()
        {
            ChangeState(State.Dead);
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.enabled = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // LateUpdate：硬锁 Attack 状态时的 transform 位置
        //
        // 根因分析（针对"敌人攻击/移动切换滑步"）：
        //   1) NavMeshAgent 的 nextPosition 与 transform 不同步：agent 的 updatePosition=true
        //      会无视 LateUpdate 的位置修改把 transform 拉回 nextPosition。修复方式是
        //      在 disable/enable agent 之前先 Warp(transform.position) 对齐。
        //   2) Animator 的 Root Transform 推送：Humanoid Animator 即便 applyRootMotion=false，
        //      仍会每帧把 transform 投影到 bodyPosition，攻击动画 body 仍会小幅漂移。
        //      修复：BuildPrefab 把 applyRootMotion 显式设为 false（最关键），LateUpdate
        //      锁作为最后一道防线。
        //   3) 攻击/移动切换时 agent 的 desiredVelocity 残留：恢复时 agent 会按旧
        //      velocity 移动一帧。修复：Warp 完立即 SetDestination(transform.position)，
        //      让 agent 重新规划（原地）、velocity=0。
        //
        // 现在这一道 LateUpdate 锁：
        //   配合 applyRootMotion=false + 完整 Warp 闭环，理论上 transform 不会被任何系统推。
        //   保留它作为最后一道防御，处理"老 prefab 未应用新 applyRootMotion=false"或"未来
        //   引入新的根变换源"的情况。
        // ═══════════════════════════════════════════════════════════════

        void LateUpdate()
        {
            if (_attackLockActive)
            {
                Vector3 p = transform.position;
                transform.position = new Vector3(_attackLockedPos.x, p.y, _attackLockedPos.z);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ═══════════════════════════════════════════════════════════════
        // 调试可视化
        // ═══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 pos = transform.position;
            Vector3 fwd = transform.forward;

            // ── 感知范围（橙色球 + 视野扇形） ──────────────────────────
            UnityEditor.Handles.color = new Color(1f, 0.6f, 0f, 0.12f);
            UnityEditor.Handles.DrawSolidArc(pos, Vector3.up,
                Quaternion.AngleAxis(-detectionAngle * 0.5f, Vector3.up) * fwd,
                detectionAngle, detectionRange);
            UnityEditor.Handles.color = new Color(1f, 0.6f, 0f, 0.5f);
            UnityEditor.Handles.DrawWireArc(pos, Vector3.up,
                Quaternion.AngleAxis(-detectionAngle * 0.5f, Vector3.up) * fwd,
                detectionAngle, detectionRange);

            // ── 攻击范围（红色胶囊 + 扇形） ────────────────────────────
            float   halfAngle = attackHitAngle;
            Vector3 origin    = pos + Vector3.up * 0.5f;
            Vector3 tip       = origin + fwd * attackRange;

            // 扇形填充
            UnityEditor.Handles.color = new Color(1f, 0.1f, 0.1f, 0.1f);
            UnityEditor.Handles.DrawSolidArc(origin, Vector3.up,
                Quaternion.AngleAxis(-halfAngle, Vector3.up) * fwd,
                halfAngle * 2f, attackRange);

            // 扇形边界线
            UnityEditor.Handles.color = new Color(1f, 0.1f, 0.1f, 0.8f);
            Quaternion lRot = Quaternion.AngleAxis(-halfAngle, Vector3.up);
            Quaternion rRot = Quaternion.AngleAxis( halfAngle, Vector3.up);
            UnityEditor.Handles.DrawLine(origin, origin + lRot * fwd * attackRange);
            UnityEditor.Handles.DrawLine(origin, origin + rRot * fwd * attackRange);
            UnityEditor.Handles.DrawWireArc(origin, Vector3.up,
                lRot * fwd, halfAngle * 2f, attackRange);

            // 胶囊截面圆（首/尾）
            UnityEditor.Handles.color = new Color(1f, 0.1f, 0.1f, 0.4f);
            UnityEditor.Handles.DrawWireDisc(origin, Vector3.up, 0.3f);
            UnityEditor.Handles.DrawWireDisc(tip,    Vector3.up, 0.3f);

            // 攻击范围文字标注
            UnityEditor.Handles.Label(tip + Vector3.up * 0.3f,
                $"ATK {attackRange:F1}m / ±{halfAngle:F0}°",
                new GUIStyle { normal = { textColor = new Color(1f, 0.4f, 0.4f) }, fontSize = 10 });

            // ── 脱战范围（蓝色虚线圆） ─────────────────────────────────
            UnityEditor.Handles.color = new Color(0.2f, 0.4f, 1f, 0.35f);
            UnityEditor.Handles.DrawWireArc(pos, Vector3.up, fwd, 360f, loseTargetRange);

            // ── 巡逻范围（绿色，仅运行时可见出生点） ───────────────────
            if (Application.isPlaying)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.15f);
                Gizmos.DrawWireSphere(_spawnPosition, patrolRadius);

                // 当前 NavMesh 目标连线
                if (CurrentState == State.Chase && _target != null)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(pos, _target.position);
                }
            }
        }
#endif
    }
}
