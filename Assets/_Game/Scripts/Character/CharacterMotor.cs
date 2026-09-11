using UnityEngine;

namespace Game.Character
{










    /// <summary>










    /// 閼奉亞鐖虹憴鎺曞缁夎濮╅幒褍鍩楅崳銊ｂ偓鍌氱節鐎圭偛顦查崚?Invector vThirdPersonMotor/Animator/Controller 閺嶇绺鹃柅鏄忕帆閿?










    ///   - PhysicMaterial 娑撳鈧礁鍨忛幑顫礄閹解晜鎽?/ 閺堚偓婢堆勬噰閹?/ 濠婃垶绨婚敍?










    ///   - 閸欏苯鍘滅痪?+ SphereCast 閸︿即娼扮捄婵堫瀲濡偓濞?










    ///   - StepOffset 閸欎即妯佹潏鍛И










    ///   - 闁害鍒嗘€傞敍鍧礱lk/run/sprint/crouch閿涘鈧俺绻?OnAnimatorMove + ControlSpeed 妞瑰崬濮?










    ///   - 鐠哄疇绌敍鍧杣mpTimer + jumpHeight Rigidbody.velocity.y閿?    ///   - 缂堢粯绮撮敍鍦setState trigger + CrossFade閿?    ///   - 闊弓绗呴敍鍫ｅ厡閸ュ﹣缍嬮崝銊︹偓浣虹級閺€?+ 婢舵挳銆?SphereCast 濡偓濞村绱?










    ///   - Animator 閸欏倹鏆熼崗銊ユ倱濮濄儻绱橧nputMagnitude / InputHorizontal / InputVertical /










    ///     IsGrounded / IsStrafing / IsCrouching / VerticalVelocity / GroundDistance閿?    /// </summary>
    [AddComponentMenu("Game/Character System/Character Motor")]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Animator))]
    public class CharacterMotor : MonoBehaviour, ICharacterMotor, IHittable, IDamageable
    {










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // Inspector 闁板秶鐤?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        // ═══════════════════════════════════════════════════════════════
        // Character Kit Phase 1 数据化：可选的手感配置资产。
        // 留空 = 完全使用下方字段当前值（向后兼容）。
        // 拖入资产 = Awake() 时用资产值覆盖下方字段，支持多套手感快速切换对比。
        // ═══════════════════════════════════════════════════════════════
        [Header("═══ 手感配置(可选,拖入后 Awake 时覆盖下方全部字段) ═══")]
        public PlayerConfigData configData;

        [Header("═══ 动画集(可选,提供运动状态名覆盖) ═══")]
        [Tooltip("挂接 CharacterAnimSetAsset 后,运动状态名(Idle/Jump/Roll 等)取自其 locomotionStates;留空使用内置默认名。")]
        public CharacterAnimSetAsset animSetAsset;

        [Header("Locomotion")]
        public bool useRootMotion = false;










        // speedMultiplier 閺?ICharacterMotor 閹恒儱褰涚仦鐐粹偓褝绱濇惔蹇撳灙閸栨牕鐡у▓闈涘綗鐎?        [Range(0f, 2f)]
        [SerializeField] private float _speedMultiplierField = 1f;
        public float speedMultiplier
        {
            get => _speedMultiplierField;
            set => _speedMultiplierField = value;
        }
        public bool rotateByWorld { get; set; } = false;
        public bool turnOnSpotAnim { get; set; } = false;

        [Header("Free Speed")]
        public float freeWalkSpeed   = 2f;
        public float freeRunSpeed    = 3f;
        public float freeSprintSpeed = 5f;
        public float freeCrouchSpeed = 1.5f;
        public float freeRotationSpeed = 10f;

        [Header("Strafe Speed")]
        public float strafeWalkSpeed   = 2f;
        public float strafeRunSpeed    = 3f;
        public float strafeSprintSpeed = 4.5f;
        public float strafeCrouchSpeed = 1.5f;
        public float strafeRotationSpeed = 10f;

        [Header("Jump")]
        public float jumpHeight  = 4f;
        public float jumpForward = 3f;
        public float jumpTimer   = 0.3f;
        public bool  jumpAirControl = true;
        public float coyoteTime  = 0.15f;  // Coyote Time
        [Tooltip("落地前提前按跳跃，落地瞬间自动触发的缓冲时间（Jump Buffer）")]
        public float jumpBufferTime = 0.12f;

        [Header("Knockback")]
        [Tooltip("受击击退的默认持续时间（秒），可被施加者覆盖")]
        public float knockbackDuration = 0.5f;

        [Tooltip("高落差落地速度阈值（m/s）。落地前下落速度超过该值才播放 LandHigh 落地动画")]
        public float landHighSpeedThreshold = 3.5f;

        [Header("Speed Smoothing")]
        [Tooltip("Free 模式水平速度平滑系数。越大越跟手（响应快），越小越有惯性（起步/急停/变速更柔和）")]
        public float speedLerpRate = 12f;

        [Header("Roll")]
        public bool rollControl = false;

        [Header("Ground")]
        public LayerMask groundLayer     = 1 << 0;
        public LayerMask autoCrouchLayer = 1 << 0;
        public float groundMinDistance = 0.2f;
        public float groundMaxDistance = 0.5f;
        public float slopeLimit        = 45f;
        public float extraGravity      = -10f;
        public float stepOffsetEnd     = 0.45f;
        public float stepOffsetStart   = 0.05f;
        public float stepSmooth        = 4f;

        [Header("Crouch")]
        public float headDetect = 0.95f;

        [Header("Health")]
        public float maxHealth            = 100f;
        public float healthRecovery       = 0f;       // 濮ｅ繒顫楅崶鐐额攨闁插骏绱?=娑撳秴娲栫悰鈧?
        public float healthRecoveryDelay  = 3f;       // 閸欐ぞ婵€閸氬骸顦跨亸鎴狀潡閹靛秴绱戞慨瀣礀鐞涒偓

        [Header("Stamina")]
        public float maxStamina           = 100f;
        public float staminaRecovery      = 20f;

        [Header("Mana")]
        public float maxMana              = 100f;
        public float manaRecovery         = 0.3f;










     // 濮ｅ繒顫楅崶鐐扮秼閸?
     public float sprintStaminaCost    = 25f;










      // 濮ｅ繒顫楀☉鍫ｂ偓?
      public float jumpStaminaCost      = 15f;










      // 鐠哄疇绌崡鏇燁偧濞戝牐鈧?
      public float rollStaminaCost      = 15f;










      // 缂堢粯绮撮崡鏇燁偧濞戝牐鈧?
        [Header("Ragdoll")]
        public float ragdollVelocity      = -50f;     // 垂直速度低于此€时触发 Ragdoll?=禁用

        [Header("Random Idle")]
        public float randomIdleTime       = 0f;       // 闂呭繑婧€ Idle 鐟欙箑褰傞梻鎾閿涘牏顫楅敍澶涚礉0=缁備胶鏁?

        [Header("Debug")]
        public bool debugMode             = false;    // 鍚时输出运行时诊断信息
        private float _rootMotionTraceTimer;











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // ICharacterMotor 閹恒儱褰?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        public ICharacterMotor.LocomotionType locomotionType { get; set; } = ICharacterMotor.LocomotionType.OnlyFree;
        public bool isStrafing   { get; set; } = false;
        public bool lockRotation { get; set; } = false;
        // 战斗攻击朝向(世界空间水平单位向量)。SkillController 在 FixedUpdate(物理步内,执行顺序 -10 先于本脚本)
        // 写入当帧值;Strafe 物理速度方向直接以此为基准,不再读 transform.rotation(LateUpdate 才更新)，
        // 消除「根在 LateUpdate 瞬切、物理却用上一帧朝向」的方向切换滞后。
        public Vector3 CombatFacing { get; set; } = Vector3.forward;
        public bool isDead       { get; set; } = false;
        public Vector2 input     { get; set; } = Vector2.zero;
        public bool isInAction   { get; private set; } = false;

        public bool isGrounded   { get; private set; } = true;
        public bool isCrouching  { get; private set; } = false;
        public bool isSprinting  { get; private set; } = false;
        public bool isJumping    { get; private set; } = false;
        // 状态识别 hash 走 _stateNames(可配置,2026-07-30)
        public bool isRolling    => _baseLayerInfo.shortNameHash == _stateNames.RollHash;
        public bool landHigh     => _baseLayerInfo.shortNameHash == _stateNames.LandHighHash;
        public bool quickStop    => _baseLayerInfo.shortNameHash == _stateNames.QuickStopHash;
        public bool actions      => isRolling || quickStop || landHigh || customAction;
        private bool _customAction = false;
        /// <summary>
        /// 自定义动作（梯子/翻越/交互）开关。
        /// 关键：进入 customAction 时必须临时开启 applyRootMotion，
        /// 否则 OnAnimatorMove 里的 _anim.rootPosition / deltaPosition 全为 0，
        /// 导致进梯/翻越动画无位移（上不了梯、原地播放、重复触发）。
        /// 退出时恢复 applyRootMotion=false，普通移动继续走 Rigidbody 速度驱动。
        /// </summary>
        public bool customAction
        {
            get => _customAction;
            set
            {
                if (_customAction == value) return;
                _customAction = value;
                if (_anim != null)
                {
                    _anim.applyRootMotion = value;
                    if (debugMode)
                        Debug.Log($"[Motor] customAction={value} -> applyRootMotion={_anim.applyRootMotion}");
                }
            }
        }
        /// <summary>被外力击退期间，跳过 Motor 的速度控制，让物理自然飞出</summary>
        public bool isKnockedBack { get; set; } = false;

        /// <summary>
        /// 跳过下一帧 OnAnimatorMove 的 deltaPosition 应用。
        /// 当 CharacterLadderAction 从 Climbing(applyRootMotion=false) 切换到
        /// Exiting(applyRootMotion=true) 时，Animator 的 rootPosition 基准已陈旧
        /// （停滞在进梯动画结束时的近地面位置），首帧 deltaPosition 是从地面到
        /// 当前高度的大跳变。设置此标志可安全丢弃该帧的 delta，避免视觉弹回地面。
        /// </summary>
        public bool skipNextRootDelta { get; set; } = false;

        /// <summary>
        /// 在梯子 Enter/Exit 切换 Root Motion 前同步 Animator 的根基准，
        /// 防止 applyRootMotion=false 期间 rootPosition 停在旧的地面高度，
        /// 下一帧重新开启时产生“从地面跳到当前高度”的陈旧 delta。
        /// </summary>
        public void PrepareAnimatorRootMotion(Vector3 position, Quaternion rotation)
        {
            if (_anim == null) return;
            _anim.applyRootMotion = false;
            _anim.rootPosition = position;
            _anim.rootRotation = rotation;
            skipNextRootDelta = true;
        }

        private float _knockbackTimer = 0f;
        private Vector3 _pendingKnockbackImpulse = Vector3.zero;

        /// <summary>外部调用：在下一个 FixedUpdate 开头施加冲量并保护 velocity 不被覆盖</summary>
        public void ActivateKnockback(float duration = -1f)
        {
            isKnockedBack = true;
            _knockbackTimer = duration > 0f ? duration : knockbackDuration;
        }

        public void ApplyKnockbackImpulse(Vector3 impulse, float duration = -1f)
        {
            _pendingKnockbackImpulse = impulse;
            isKnockedBack = true;
            _knockbackTimer = duration > 0f ? duration : knockbackDuration;
            // 播放受击/坠落动画
            if (_anim != null)
                _anim.CrossFadeInFixedTime(H_Falling, 0.1f, _normalStateLayerIndex >= 0 ? _normalStateLayerIndex : 0);
        }

        // IHittable 接口实现
        public void ReceiveKnockback(Vector3 impulse, float duration = 0.5f)
            => ApplyKnockbackImpulse(impulse, duration);
        /// <summary>角色在梯子触发器范围内时由 CharacterLadderAction 设置，CheckGround 视为 grounded，阻断 Falling</summary>
        public bool inLadderZone { get; set; } = false;
        public bool lockMovement { get; set; } = false;
        public bool ragdolled    { get; private set; } = false;
        public bool isSliding    => _isSliding;











        // Health(数值持有已拆到 CharacterStats,2026-07-30;此处为兼容转发)
        private CharacterStats _stats;
        private CharacterStats Stats
        {
            get
            {
                // 惰性兜底:外部代码在本组件 Awake 前访问也能拿到可用实例
                if (_stats == null)
                {
                    _stats = GetComponent<CharacterStats>();
                    if (_stats == null) _stats = gameObject.AddComponent<CharacterStats>();
                    _stats.InitFrom(this);
                }
                return _stats;
            }
        }
        /// <summary>属性组件(HP/Stamina/Mana 唯一运行时持有者,联机数值同步落点)。</summary>
        public CharacterStats stats => Stats;
        public float currentHealth => Stats.currentHealth;











        // Stamina(转发 CharacterStats)
        public float currentStamina => Stats.currentStamina;

        // Mana(转发 CharacterStats)
        public float currentMana => Stats.currentMana;











        // Random Idle
        private float _idleTimer;











        // 娴滃娆?
        // 数值变化事件转发到 CharacterStats(2026-07-30);订阅方(HUD 等)无感知
        public event System.Action<float> onHealthChanged
        {
            add    => Stats.onHealthChanged += value;
            remove => Stats.onHealthChanged -= value;
        }










    // 閸欏倹鏆熼敍姘秼閸撳秷顢呴柌?
    public event System.Action<float> onStaminaChanged
    {
        add    => Stats.onStaminaChanged += value;
        remove => Stats.onStaminaChanged -= value;
    }
    public event System.Action<float> onManaChanged
    {
        add    => Stats.onManaChanged += value;
        remove => Stats.onManaChanged -= value;
    }
   // 閸欏倹鏆熼敍姘秼閸撳秳缍嬮崝?
   public System.Action        onDead;
        public System.Action        onActiveRagdoll;
        public System.Action        onResetRagdoll;











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // 閸愬懘鍎寸紒鍕










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private Rigidbody        _rb;
        private CapsuleCollider  _col;
        private Animator         _anim;
        private Camera           _mainCamera;  // 缓存主摄像机，避免每?Camera.main 鏌ユ壘











        // 绾扮増鎸掓担鎾冲斧婵鈧》绱欓煫韫瑓/缂堢粯绮撮弮鍓佺級閺€鎾呯礆
        private Vector3 _colCenter;
        private float   _colRadius;
        private float   _colHeight;











        // PhysicMaterial
        private PhysicMaterial _frictionPhys;
        private PhysicMaterial _maxFrictionPhys;
        private PhysicMaterial _slippyPhys;











        // 地面
        private float      _groundDistance;
        private RaycastHit _groundHit;
        private float      _verticalVelocity;

        // LandHigh 超时保险：防?lockMovement 鍗℃
        private float _landHighTimer = 0f;
        private const float LandHighMaxDuration = 1.5f;











        // 鐠哄疇绌?
        private float _jumpCounter;
        private float _coyoteCounter;   // Coyote Time
        private float _jumpBufferCounter;  // Jump Buffer 倒计时：>0 表示有缓冲中的跳跃请求
        private bool  _isSliding;       // 闄″潯婊戣惤涓紙鍖哄埆浜庣湡姝ｇ地，不触?Falling锛?

        private bool  _rollRequested;    // set in LateUpdate, consumed in FixedUpdate










        // 缂堢粯绮?闊弓绗呮潏鍛И
        private bool _autoCrouch;
        private bool _inCrouchArea;











        // 闁害锛圫trafe €崇础娑擃參妫块崣姗€鍣洪敍?
        private float _speed;
        private float _direction;
        private float _strafeInput;











        // Animator 鐏炲倷淇婇幁顖ょ礄濮ｅ繐鎶?LayerControl 閺囧瓨鏌婇敍?
        private AnimatorStateInfo _baseLayerInfo;
        private AnimatorStateInfo _fullBodyInfo;











        // Animator hash
        // 参数名统一走 AnimatorParams 契约(2026-07-30)
        private static readonly int H_InputMagnitude   = AnimatorParams.InputMagnitude;
        private static readonly int H_InputHorizontal  = AnimatorParams.InputHorizontal;
        private static readonly int H_InputVertical    = AnimatorParams.InputVertical;
        private static readonly int H_IsGrounded       = AnimatorParams.IsGrounded;
        private static readonly int H_IsStrafing       = AnimatorParams.IsStrafing;
        private static readonly int H_IsCrouching      = AnimatorParams.IsCrouching;
        private static readonly int H_Crouch           = AnimatorParams.Crouch;
        private static readonly int H_IsCustomAction   = AnimatorParams.IsCustomAction;
        private static readonly int H_VerticalVelocity = AnimatorParams.VerticalVelocity;
        private static readonly int H_GroundDistance   = AnimatorParams.GroundDistance;
        private static readonly int H_ActionState      = AnimatorParams.ActionState;
        private static readonly int H_Falling          = AnimatorParams.Falling;










        // 閻樿埖鈧胶鐓崥?hash閿涘牏鏁?shortNameHash 閸栧綊鍘ら敍宀勪缉閸忓秴鐡欓悩鑸碘偓浣规簚鐠侯垰绶為梻顕€顣介敍?
        // 运动状态名(可配置,默认与现有 Controller 一致);Roll/LandHigh/QuickStop 识别 hash 由其派生
        private LocomotionStateNames _stateNames = new LocomotionStateNames();
        /// <summary>运动状态名配置(供 LadderAction 等地形交互组件读取)。</summary>
        public LocomotionStateNames StateNames => _stateNames;

        private static readonly int H_ResetState       = AnimatorParams.ResetState;
        private static readonly int H_isDead           = AnimatorParams.IsDead;











        // 层索引缓?
        private int _actionStateLayerIndex = -1;
        private int _normalStateLayerIndex = -1;











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // 閻㈢喎鎳￠崨銊︽埂










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private ICharacterInputSource _inputSource;   // 输入源(S2-0 收口):本地=LocalInputSource,远端=NullInputSource

        void Awake()
        {
            _rb          = GetComponent<Rigidbody>();
            _col         = GetComponent<CapsuleCollider>();
            _anim        = GetComponent<Animator>();
            _mainCamera  = Camera.main;
            _inputSource = CharacterInputSourceResolver.Resolve(this);

            // Character Kit Phase 1 数据化:若绑定了 PlayerConfigData,用资产值覆盖上方字段
            // (留空则完全不受影响,保持向后兼容)
            if (configData != null)
                configData.ApplyTo(this);

            // 运动状态名:AnimSet 资产提供则覆盖默认名(2026-07-30)
            if (animSetAsset != null && animSetAsset.locomotionStates != null)
                _stateNames.CopyFrom(animSetAsset.locomotionStates);

            // 自动挂头顶 HUD（玩家/敌人共用 CharacterHUD）
            EnsureHUD();











            // 娣囨繂鐡ㄧ喊鐗堟寬娴ｆ挸鍨垫慨瀣偓?
            _colCenter = _col.center;
            _colRadius = _col.radius;
            _colHeight = _col.height;











            // Rigidbody 閰嶇疆
            // isKinematic 蹇呴』涓?false，否则无法缃?velocity（物理驱动移級
            _rb.isKinematic            = false;
            _rb.useGravity             = true;
            _rb.constraints            = RigidbodyConstraints.FreezeRotation;
            _rb.interpolation          = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;











            // 瀵搫鍩楅柌宥囩枂绾扮増鎸掓担鎾跺Ц閹緤绱欓梼鍙夘剾娑撳﹥顐兼潻鎰攽濞堝鏆€ isTrigger=true閿?
            _col.isTrigger = false;











            // 鐎靛綊缍堥崢鐔哄 vThirdPersonMotor.Init()










            // applyRootMotion=true閿涙艾绱╅幙搴ゎ吀缁?root motion 閺佺増宓侀敍鍧塭ltaPosition/rootRotation 閺堝鈧》绱?










            // 娴ｅ棙婀?OnAnimatorMove閿涘本澧嶆禒銉ョ穿閹垮簼绗夐懛顏勫З鎼存梻鏁ら敍宀€鏁?OnAnimatorMove 鐎瑰苯鍙忛幒褍鍩?










            // AnimatePhysics閿涙瓌nAnimatorMove 闂呭繒澧块悶鍡橆劄鐠嬪啰鏁ら敍灞肩瑢 FixedUpdate 閸氬矂顣?
            _anim.applyRootMotion = false;
            _anim.updateMode      = AnimatorUpdateMode.AnimatePhysics;











            // PhysicMaterial
            _frictionPhys = new PhysicMaterial { name = "frictionPhys",
                staticFriction = .25f, dynamicFriction = .25f,
                frictionCombine = PhysicMaterialCombine.Multiply };
            _maxFrictionPhys = new PhysicMaterial { name = "maxFrictionPhys",
                staticFriction = 1f, dynamicFriction = 1f,
                frictionCombine = PhysicMaterialCombine.Maximum };
            _slippyPhys = new PhysicMaterial { name = "slippyPhys",
                staticFriction = 0f, dynamicFriction = 0f,
                frictionCombine = PhysicMaterialCombine.Minimum };











            // 鐎涙劗顫幘鐐扮秼韫囩晫鏆?
            var allCols = GetComponentsInChildren<Collider>();
            foreach (var c in allCols)
                if (c != _col) Physics.IgnoreCollision(_col, c);











            // ActionState 鐏?
            _actionStateLayerIndex = GetLayerIndex(_anim, "ActionState");
            _normalStateLayerIndex = GetLayerIndex(_anim, "NormalState");
            if (_normalStateLayerIndex < 0) _normalStateLayerIndex = 0;











            // 閸掓繂顫愰崠?Health / Stamina
            // 属性组件初始化:读取(可能已被 configData 覆盖的)最新配置字段(2026-07-30)
            _stats = GetComponent<CharacterStats>();
            if (_stats == null) _stats = gameObject.AddComponent<CharacterStats>();
            _stats.InitFrom(this);
            _stats.onDeadChanged += OnDeadStateChanged;   // 订阅网络死亡状态(远端驱动死亡/复活动画)
            _idleTimer = randomIdleTime > 0f ? randomIdleTime : float.MaxValue;
        }











        void OnDisable()
        {
            // 保底：脚本被禁用时恢复碰撞体，防止击退期间 GameObject 被禁用导致永久穿透
            if (_col != null) _col.enabled = true;
            isKnockedBack = false;
            if (_stats != null) _stats.onDeadChanged -= OnDeadStateChanged;
        }

        void Update()
        {
            if (isDead) return;
            LayerControl();
            UpdateActionState();
            UpdateAnimator();
            ControlCapsuleHeight();
            AutoCrouch();
            CheckStopMove();
            HealthRecovery();
            StaminaRecovery();
            ManaRecovery();
            CheckRandomIdle();
        }

        void LateUpdate()
        {
            // customAction 期间（瀛?交互），覆盖 Animator 参数防触发 Falling
            if (customAction && _anim != null)
            {
                _anim.SetBool(H_IsGrounded,       true);
                _anim.SetBool(H_IsCustomAction,   true);
                _anim.SetFloat(H_GroundDistance,  -1f);
                _anim.SetFloat(H_VerticalVelocity, 0f);
            }
        }

        /// <summary>外部系统（Ladder/ActionHandler）在同帧立即调用，强制屏?Falling 参数</summary>
        public void ForceGroundedAnimParams()
        {
            if (_anim == null) return;
            _anim.SetBool(H_IsGrounded,       true);
            _anim.SetBool(H_IsCustomAction,   true);
            _anim.SetFloat(H_GroundDistance,  -1f);
            _anim.SetFloat(H_VerticalVelocity, 0f);
        }

        void FixedUpdate()
        {
            if (isDead) return;

            // 击退冲量：在所有速度控制之前施加，确保不被覆盖
            if (_pendingKnockbackImpulse != Vector3.zero)
            {
                _rb.velocity = _pendingKnockbackImpulse;
                isGrounded = false;
                if (_col != null) _col.enabled = false;
                _pendingKnockbackImpulse = Vector3.zero;
            }

            if (isKnockedBack)
            {
                _knockbackTimer -= Time.fixedDeltaTime;
                if (_knockbackTimer <= 0f)
                {
                    isKnockedBack = false;
                    if (_col != null) _col.enabled = true;
                }
            }
            CheckGroundDistance();
            CheckGround();
            // Coyote Time：接地时重置，地后倒鏃?
            if (isGrounded && !isJumping)
                _coyoteCounter = coyoteTime;
            else if (_coyoteCounter > 0f)
                _coyoteCounter -= Time.fixedDeltaTime;
            // Jump Buffer 倒计时（在 ExecuteJump 消费前递减）
            if (_jumpBufferCounter > 0f)
                _jumpBufferCounter -= Time.fixedDeltaTime;
            CheckRagdoll();
            ControlJumpBehaviour();
            AirControl();   // 由 Motor 自身驱动，不再依赖 InputHandler.FixedUpdate 调用
            ControlLocomotion();
            // isCrouching 涓?groundDistance 很小时（入口坡面帧间抖动），仍驱动€熷害
            bool groundedOrCrouchNearGround = isGrounded
                || (isCrouching && !isJumping && _groundDistance < groundMaxDistance * 2f);
            // 与 ControlLocomotion 的 (lockMovement && !_combatMovementOverride) 保持一致:
            // 战斗移动覆盖(_combatMovementOverride)期间即使动画带 LockMovement 标签也要写物理速度,
            // 否则方向切换时身体会沿用旧 _rb.velocity 惯性滑行(用户反馈「实际移动方向滞后」)。
            if (groundedOrCrouchNearGround && (!lockMovement || _combatMovementOverride) && !customAction && !isKnockedBack) ControlSpeedFixedUpdate();
            // Consume buffered input requests (recorded in LateUpdate, executed here with fresh physics state)
            ExecuteJump();
            ExecuteRoll();
        }











        // OnAnimatorMove: AnimatePhysics 濡€崇础娑撳娈㈤悧鈺冩倞濮濄儴鐨熼悽?










        // applyRootMotion=true閿涘苯绱╅幙搴濈瑝閼奉亜濮╂惔鏃傛暏閿涘牆娲滄稉鐑樻箒 OnAnimatorMove閿涘绱濋悽杈劃閺傝纭剁€瑰苯鍙忛幒褍鍩楅敍?










        //   閺咁噣鈧氨些閸?閳?Rigidbody 閸旀盯鈹嶉崝?










        //   Action閿涘潏ustomAction=true閿涘鍟?rootPosition 缂佹繂顕崐纭风礄MatchTarget 娣囶喗鏁奸惃鍕Ц rootPosition閿?
        // customAction=true: rootPosition/deltaPosition 驱动（解?LadderAction 引用?
        void OnAnimatorMove()
        {
            if (isKnockedBack) return;

            // Enter/Exit 切换首帧只用于清空 Animator 的旧 root 基准。
            // 该帧不消费位移和旋转，下一帧再从当前 Transform/Rigidbody 姿态
            // 连续消费 Root Motion，避免首帧朝向和位置跳变。
            if (skipNextRootDelta)
            {
                skipNextRootDelta = false;
                return;
            }

            if (customAction)
            {
                // applyRootMotion=false：由 CharacterLadderAction.UseLadder 手动驱动位置
                if (!_anim.applyRootMotion)
                    return;

                // applyRootMotion=true（进梯/退梯阶段）：
                // 消费 deltaPosition 驱动位移。kinematic body 直接赋值穿透碰撞体，
                // 非 kinematic 用 MovePosition 让物理引擎处理。
                Vector3 delta = _anim.deltaPosition;
                if (debugMode)
                {
                    _rootMotionTraceTimer -= Time.deltaTime;
                    if (_rootMotionTraceTimer <= 0f)
                    {
                        _rootMotionTraceTimer = 0.1f;
                        Debug.Log($"[Motor:RootMotion] state={_anim.GetCurrentAnimatorStateInfo(0).shortNameHash} " +
                                  $"apply={_anim.applyRootMotion} delta={delta} " +
                                  $"deltaRot={_anim.deltaRotation.eulerAngles} pos={transform.position} rb={(_rb != null ? _rb.position : transform.position)}");
                    }
                }
                // Humanoid Animator 在切换 Enter/Exit 状态时可能产生跨旧 root 基准的巨大首帧 delta；
                // 该 delta 不是动作位移，会把角色从顶部瞬移到地面。正常梯子 Root Motion 单帧位移远小于 1m。
                if (Mathf.Abs(delta.y) > 1f || delta.sqrMagnitude > 4f)
                {
                    if (debugMode)
                        Debug.LogWarning($"[Motor] 丢弃异常 Root Motion delta={delta}");
                    delta = Vector3.zero;
                }
                if (delta.sqrMagnitude > 0.000001f)
                {
                    if (_rb != null)
                    {
                        if (_rb.isKinematic)
                            _rb.position += delta;   // kinematic 直接赋值，穿透碰撞体
                        else
                            _rb.MovePosition(_rb.position + delta);
                    }
                    else
                        transform.position += delta;
                }
                transform.rotation *= _anim.deltaRotation;
                if (_rb != null) _rb.rotation = transform.rotation;
                return;
            }

            // 旋转每帧都应避免空中时旋噺绉疮绐佸彉
            if (!lockRotation)
                transform.rotation *= _anim.deltaRotation;

            if (!isGrounded) return;
            // 位移?FixedUpdate -> ControlLocomotion 驱动
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?










        // 鐏炲倷淇婇幁?& 閸斻劋缍旈悩鑸碘偓?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private void LayerControl()
        {
            _baseLayerInfo = _anim.GetCurrentAnimatorStateInfo(_normalStateLayerIndex);

            int fb = GetLayerIndex(_anim, "FullBody");
            if (fb >= 0) _fullBodyInfo = _anim.GetCurrentAnimatorStateInfo(fb);











            // lockMovement: 閹绢厽鏂佺敮?"LockMovement" 閺嶅洨顒烽惃鍕З閻㈢粯妞傞柨浣哥暰
            // lockMovement: 鎾斁甯?"LockMovement" 标的动画时锁定
            bool tagLock = IsAnimatorTag("LockMovement");

            // LandHigh 瑙ｉ攣閫昏緫
            // LandHigh 鏄地缓冲动画，畬鍚?Animator 应自动过渡出去€?
            // 濡傛灉 Animator Controller 娌℃湁閰嶇疆閫€出过渡（或过渡条件不满足），
            // 角色会卡?LandHigh + LockMovement Tag，永远无法移动€?
            // 淇锛?
            //   姝ｅ父璺緞 鈹€ Animator 进入过渡?IsInTransition=true锛屾时提前解?tagLock
            //   蹇€熻矾寰?鈹€ isGrounded 涓?normalizedTime >= 0.9锛堥潪寰动画畬锛夌珛鍗宠В閿?
            //   鍏滃簳璺緞 鈹€ 瓒呰繃 LandHighMaxDuration(1.5s) 寮哄埗 CrossFade 鍒?Idle
            if (landHigh)
            {
                _landHighTimer += Time.deltaTime;

                bool inTransition  = _anim.IsInTransition(_normalStateLayerIndex);
                bool animNearEnd   = isGrounded && Mathf.Repeat(_baseLayerInfo.normalizedTime, 1f) >= 0.88f;
                bool timedOut      = _landHighTimer >= LandHighMaxDuration;

                if (inTransition || animNearEnd)
                {
                    // 姝ｅ父/蹇€路径：动画即将结束或已€濮嬭繃娓★紝瑙ｉ櫎 lockMovement
                    tagLock = false;
                }
                else if (timedOut)
                {
                    // 兜底：强制跳?
                    tagLock = false;
                    _anim.CrossFadeInFixedTime(_stateNames.idle, 0.15f);
                    if (debugMode)
                        Debug.LogWarning("[CharacterMotor] LandHigh timeout, force CrossFade to Idle");
                }
            }
            else
            {
                _landHighTimer = 0f;
            }

            lockMovement = tagLock;










            // 濞夈劍鍓伴敍姝漸stomAction 閻㈠崬顦婚柈銊ч兇缂佺噦绱橤ravesActionHandler / GravesLadderAction閿涘顔曠純顕嗙礉










            // 娑撳秳绮?Animator Tag 鐠囪褰囬敍宀勪缉閸忓秵鐦＄敮褑顩惄鏍モ偓?
            }

        private void UpdateActionState()
        {










            // 娴兼ê鍘涢悽?ActionState 鐏炲倹娼堥柌宥呭灲閺傤叏绱欐俊鍌涚亯鐏炲倸鐡ㄩ崷顭掔礆
            if (_actionStateLayerIndex >= 0)
            {
                isInAction = _anim.GetLayerWeight(_actionStateLayerIndex) > 0.01f;
            }
            else
            {










                // ActionState 鐏炲倷绗夌€涙ê婀弮璁圭礉閸ョ偤鈧偓閸?actions 鐏炵偞鈧?                // actions = isRolling || quickStop || landHigh || customAction
                isInAction = actions;
            }
        }

        private bool IsAnimatorTag(string tag)
        {
            if (_baseLayerInfo.IsTag(tag)) return true;
            if (_fullBodyInfo.IsTag(tag))  return true;
            return false;
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // 閸︿即娼板Λ鈧ù瀣剁礄閸?Raycast + SphereCast閿涘苯顦查崚璇插斧閻楀牞绱?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private void CheckGroundDistance()
        {
            if (_col == null) return;

            // 使用当前胶囊实际高度（蹲下时已缩小），避?ray1 起点偏高打到顶面
            float curHeight = _col.height;
            float radius = _col.radius * 0.9f;
            float dist   = 10f;
            Vector3 pos  = transform.position + Vector3.up * _col.radius;

            Ray ray1 = new Ray(transform.position + new Vector3(0, curHeight * 0.5f, 0), Vector3.down);
            Ray ray2 = new Ray(pos, Vector3.down);

            if (Physics.Raycast(ray1, out _groundHit, curHeight * 0.5f + 2f, groundLayer))
                dist = transform.position.y - _groundHit.point.y;

            if (Physics.SphereCast(ray2, radius, out var sphereHit, _col.radius + 2f, groundLayer))
            {
                float d2 = sphereHit.distance - _col.radius * 0.1f;
                if (dist > d2) { dist = d2; _groundHit = sphereHit; }
            }

            _groundDistance = (float)System.Math.Round(dist, 2);
        }

        private void CheckGround()
        {
            // 击退优先，不受 inLadderZone 影响
            if (isKnockedBack) { isGrounded = false; return; }
            if (isDead || customAction || inLadderZone) { isGrounded = true; return; }











            // PhysicMaterial 閸掑洦宕?
            float ga = GroundAngle();
            if (isGrounded && ga <= slopeLimit + 1f)
                _col.material = (input == Vector2.zero) ? _maxFrictionPhys : _frictionPhys;
            else
                _col.material = _slippyPhys;

            bool checkCond = !isRolling;
            float magVel   = Mathf.Clamp(new Vector3(_rb.velocity.x, 0, _rb.velocity.z).magnitude, 0, 1);
            float checkDist = magVel > 0.25f ? groundMaxDistance : groundMinDistance;

            if (!checkCond) return;

            bool onStep = StepOffset();

            if (_groundDistance <= 0.05f)
            {
                bool wasAirborne = !isGrounded;
                float impactSpeed = -_rb.velocity.y;
                isGrounded = true;
                // 高落差落地：直接 CrossFade 到 LandHigh 状态播落地动画。
                // 说明：Animator 里 Falling->LandHigh 的过渡依赖 bool 参数 LandHigh，
                // 但该参数既无代码写入、进入过渡又是孤儿(未挂到任何源状态)，故绕过参数直切状态。
                if (wasAirborne && impactSpeed >= landHighSpeedThreshold && !isRolling && !customAction && _anim != null)
                    _anim.CrossFadeInFixedTime(_stateNames.landHigh, 0.1f, _normalStateLayerIndex >= 0 ? _normalStateLayerIndex : 0);
                _verticalVelocity = 0f;   // 落地时立即归零，配合 UpdateAnimator 的阻尼产生平滑收?
                // 绌块€修正：_groundDistance < 0 说明角色陷入地面，直接修?Y 浣嶇疆
                if (_groundDistance < 0f && _groundHit.collider != null && !isKnockedBack)
                {
                    var p = _rb.position;
                    p.y -= _groundDistance;
                    _rb.position = p;
                    SetVelocity(new Vector3(_rb.velocity.x, 0f, _rb.velocity.z));
                }
                Sliding();
            }
            else if (_groundDistance >= checkDist)
            {
                isGrounded = false;
                _verticalVelocity = _rb.velocity.y;
                // 闄愬埗鏈€大下落€度，防止触?ragdoll 或穿透地?
                if (_rb.velocity.y < -15f)
                    SetVelocity(new Vector3(_rb.velocity.x, -15f, _rb.velocity.z));
                if (!onStep && !isJumping)
                    _rb.AddForce(Vector3.up * extraGravity * Time.fixedDeltaTime, ForceMode.VelocityChange);
            }
            else if (!onStep && !isJumping)
            {
                _rb.AddForce(Vector3.up * (extraGravity * 2f * Time.fixedDeltaTime), ForceMode.VelocityChange);
            }
        }

        private float GroundAngle()
            => Vector3.Angle(_groundHit.normal, Vector3.up);

        private void Sliding()
        {
            bool onStep = StepOffset();
            RaycastHit hit2;
            float ga2 = 0f;
            if (Physics.Raycast(new Ray(transform.position, Vector3.down), out hit2, 1f, groundLayer))
                ga2 = Vector3.Angle(Vector3.up, hit2.normal);

            float ga = GroundAngle();
            if (ga > slopeLimit + 1f && ga <= 85f && ga2 > slopeLimit + 1f && ga2 <= 85f
                && _groundDistance <= 0.05f && !onStep)
            {
                // 闄″潯婊戣惤锛氫繚鎸?isGrounded=true（贴地），不触发 Falling
                // 鐢?_isSliding 标志区分，供外部查
                _isSliding = true;
                isGrounded = true;
                float sv = Mathf.Clamp((ga - slopeLimit) * 2f, 0, 10);
                SetVelocity(new Vector3(_rb.velocity.x, -sv, _rb.velocity.z));
            }
            else
            {
                _isSliding = false;
                isGrounded = true;
            }
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // StepOffset 閸欎即妯佹潏鍛И閿涘牆顦查崚璇插斧閻楀牞绱?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private bool StepOffset()
        {
            if (input.sqrMagnitude < 0.1f || !isGrounded) return false;

            var moveDir = isStrafing && input.magnitude > 0
                ? (transform.right * input.x + transform.forward * input.y).normalized
                : transform.forward;

            Ray ray = new Ray(
                transform.position + new Vector3(0, stepOffsetEnd, 0)
                + moveDir * (_col.radius + 0.05f),
                Vector3.down);

            if (Physics.Raycast(ray, out RaycastHit hit, stepOffsetEnd - stepOffsetStart, groundLayer)
                && !hit.collider.isTrigger)
            {
                if (hit.point.y >= transform.position.y + 0.08f && hit.point.y <= transform.position.y + stepOffsetEnd)
                {
                    // 排除斜坡：台阶顶面法线接近 Vector3.up，斜坡法线倾斜。
                    // 斜坡上"前方略高"会被误判为台阶，stepSmooth=4 导致上坡加速。
                    if (Vector3.Angle(hit.normal, Vector3.up) > 15f) return false;

                    float sp = isStrafing ? Mathf.Clamp(input.magnitude, 0, 1) : _speed;
                    var vd = isStrafing
                        ? (hit.point - transform.position)
                        : (hit.point - transform.position).normalized;
                    float vel = Mathf.Max(GetCurrentVelocityMagnitude(), 1f);
                    // Free 模式 vd 已归一化，sp*vel 即正常移动速度，乘 stepSmooth=4 会造成上台阶 4 倍速
                    // （爬楼梯每级台阶、坡顶边缘都触发，表现为"爬梯/上坡加速"）。
                    // Strafing 模式 vd 是位移向量(量级<1)，需保留 stepSmooth 补偿才能爬上台。
                    float boost = isStrafing ? stepSmooth : 1f;
                    SetVelocity(vd * boost * (sp * vel));
                    return true;
                }
            }
            return false;
        }

        private float GetCurrentVelocityMagnitude()
        {
            if (isStrafing)
            {
                if (_strafeInput <= 0.5f) return strafeWalkSpeed;
                if (_strafeInput <= 1f)   return strafeRunSpeed;
                return strafeSprintSpeed;
            }
            if (_speed <= 0.5f) return freeWalkSpeed;
            if (_speed <= 1f)   return freeRunSpeed;
            return freeSprintSpeed;
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?










        // 缁夎濮╅幒褍鍩楅敍鍫濐槻閸?ControlLocomotion / FreeMovement / StrafeMovement閿?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private bool _stopMove;
        private bool _combatMovementOverride;

        public void SetCombatMovementOverride(bool active)
        {
            _combatMovementOverride = active;
        }

        private bool FreeLocomotionConditions =>
            locomotionType == ICharacterMotor.LocomotionType.OnlyFree
            || (!isStrafing && locomotionType != ICharacterMotor.LocomotionType.OnlyStrafe);

        private void ControlLocomotion()
        {
            if ((lockMovement && !_combatMovementOverride) || isDead || customAction) return;
            if (FreeLocomotionConditions)
                FreeMovement();
            else
                StrafeMovement();
        }

        private void StrafeMovement()
        {
            StrafeLimitSpeed(1f);
            if (isSprinting) _strafeInput += 0.5f;
            if (_stopMove)   _strafeInput = 0f;
            _anim.SetFloat(H_InputMagnitude, _strafeInput, 0.2f, Time.deltaTime);

            // 方向参数(InputVertical/InputHorizontal)驱动下半身 2D 八向融合树。Animator 为
            // AnimatePhysics 模式(物理步求值),这里在 FixedUpdate 内直接写,使当步求值即时生效,
            // 消除「Update 写参数 → 下一物理步才求值」的额外一帧延迟。
            // MoveAxesRaw=GetAxisRaw 无平滑且当帧稳定,比 input 字段(上一帧 LateUpdate 写入)更新鲜。
            // 战斗/Strafe 下 input 即 clamp(MoveAxesRaw),这里直读原始轴结果一致但零延迟。
            Vector2 raw = _inputSource.MoveAxesRaw;
            float v = _stopMove ? 0f : Mathf.Clamp(raw.y, -1f, 1f);
            float h = _stopMove ? 0f : Mathf.Clamp(raw.x, -1f, 1f);
            _anim.SetFloat(H_InputHorizontal, h, 0f, Time.deltaTime);
            _anim.SetFloat(H_InputVertical,   v, 0f, Time.deltaTime);
        }

        private void StrafeLimitSpeed(float maxVal)
        {
            // 直读当帧原始轴(MoveAxesRaw=GetAxisRaw 无平滑),而非 input 字段。
            // input 由 CharacterInputHandler.LateUpdate 写入,本帧 FixedUpdate 读到的是上一帧值,
            // 导致战斗 Strafe 物理速度方向慢一帧——越轴切换时动画已切、身体却还在旧方向滑行,
            // 连续操作时逐帧累积(用户反馈「操作越多越滞后」)。方向参数与物理速度同源、当帧一致。
            Vector2 raw = _inputSource.MoveAxesRaw;
            _speed      = Mathf.Clamp(raw.y, -1f, 1f);
            _direction  = Mathf.Clamp(raw.x, -1f, 1f);
            _strafeInput = Mathf.Clamp(new Vector2(_speed, _direction).magnitude, 0, maxVal);
        }

        private void FreeMovement()
        {
            _speed = Mathf.Clamp01(Mathf.Abs(input.x) + Mathf.Abs(input.y));
            // InputHorizontal 是公共控制器横向 BlendTree/镜像的权威参数。
            // FreeMovement 之前只更新 _speed，A/D 时 _direction 沿用旧值 0，
            // 导致移动攻击没有左右镜像；无论是否开启 strafing 都保留原始横向符号。
            _direction = Mathf.Clamp(input.x, -1f, 1f);
            if (isSprinting) _speed += 0.5f;
            if (_stopMove || lockMovement) 
            {
                _speed = 0f;
            }

            _anim.SetFloat(H_InputMagnitude, _speed, 0.2f, Time.deltaTime);

            bool canRotate = (input != Vector2.zero) && !lockRotation
                && (!actions || quickStop || (isRolling && rollControl));

            if (canRotate && (isGrounded || jumpAirControl))
            {
                Vector3 targetDir = GetMoveDirection().normalized;
                if (targetDir.sqrMagnitude > 0.01f)
                {
                    Quaternion freeRot = Quaternion.LookRotation(targetDir, Vector3.up);
                    transform.rotation = Quaternion.Lerp(
                        transform.rotation, freeRot,
                        freeRotationSpeed * Time.deltaTime);
                }
            }
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?










        // ControlSpeed閿涘牆婀?OnAnimatorMove 娑擃叀鐨熼悽顭掔礉婢跺秴鍩㈤崢鐔哄閿?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?










        // ControlSpeedFixedUpdate閿涙碍娅橀柅姘毙╅崝銊┾偓鐔峰閸掑棙銆傞敍灞芥躬 FixedUpdate 闁插矁鐨熼悽?
        private void ControlSpeedFixedUpdate()
        {
            if (isStrafing)
            {
                if (_strafeInput <= 0.5f)   ControlSpeed(strafeWalkSpeed);
                else if (_strafeInput <= 1f) ControlSpeed(strafeRunSpeed);
                else                         ControlSpeed(strafeSprintSpeed);
                if (isCrouching)             ControlSpeed(strafeCrouchSpeed);
            }
            else
            {
                if (_speed <= 0.5f)   ControlSpeed(freeWalkSpeed);
                else if (_speed <= 1f) ControlSpeed(freeRunSpeed);
                else                   ControlSpeed(freeSprintSpeed);
                if (isCrouching)       ControlSpeed(freeCrouchSpeed);
            }
        }











        // ControlSpeed：只在普通移动时调用（OnAnimatorMove ?applyRootMotion=false 时）
        /// <summary>
        /// 瀹夊叏璁剧疆 Rigidbody velocity銆?
        /// Kinematic 妯″紡涓?velocity 璧嬪€间細鎶ラ敊锛屾方法跳过?
        /// 鏍规湰淇鍦?Awake 涓紙_rb.isKinematic = false锛夛紝姝ゅ作为防御兜底?
        /// </summary>
        private void SetVelocity(Vector3 v)
        {
            if (_rb.isKinematic) return;
            _rb.velocity = v;
        }

        private void ControlSpeed(float velocity)
        {
            if (Time.deltaTime == 0) return;
            velocity *= speedMultiplier;

            if (_rb.isKinematic)
            {
                // Kinematic 妯″紡锛歷elocity 璧嬪€无效，改用 MovePosition
                Vector3 moveDir;
                if (isStrafing)
                    moveDir = transform.TransformDirection(new Vector3(input.x, 0, input.y)).normalized;
                else
                    moveDir = transform.forward * _speed;

                _rb.MovePosition(_rb.position + moveDir * velocity * Time.deltaTime);
                return;
            }

            if (isStrafing)
            {
                // 战斗 Strafe：直接硬切目标速度，任何方向切换（前后/左右/对角八向）都即时响应。
                // 不再走速度平滑——速度 Lerp 会让身体在旧方向上继续滑行一小段后才掉头，
                // 表现为八向切换滞后（用户反馈：不光是 W/S，八向都有）。视觉柔和度由
                // Animator 走位参数（InputVertical/InputHorizontal 低阻尼）与 BlendTree 融合承担。
                float slopeFactor = GetSlopeSpeedFactor();
                // 方向基准用 CombatFacing(当帧攻击朝向,SkillController 在物理步内先写入),
                // 而非 transform.TransformDirection(依赖 transform.rotation,后者 LateUpdate 才瞬切)。
                // 这样物理速度方向与攻击朝向、动画方向参数三者同帧一致,方向切换零相位差。
                Vector3 fwd = CombatFacing;
                if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 v = (fwd * _speed + right * _direction)
                    * (velocity > 0 ? velocity * slopeFactor : 1f);
                v.y = _rb.velocity.y;
                SetVelocity(v);
            }
            else
            {
                // Free 模式：Lerp 平滑水平速度，起步加速/急停减速/冲刺切换都有惯性过渡，
                // 消除原先硬赋值导致的速度瞬变。竖直分量保持物理（重力/跳跃）。
                // 上坡修正：斜坡上水平速度按 cos(坡度) 缩放，抵消 Rigidbody 沿斜坡放大速度（上坡加速）。
                float slopeFactor = GetSlopeSpeedFactor();
                Vector3 targetH = transform.forward * (velocity * _speed * slopeFactor);
                Vector3 curH    = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z);
                Vector3 newH    = Vector3.Lerp(curH, targetH, speedLerpRate * Time.deltaTime);
                SetVelocity(new Vector3(newH.x, _rb.velocity.y, newH.z));
            }
        }

        /// <summary>
        /// 斜坡速度因子：上坡时按 cos(坡度) 缩放水平速度，抵消 Rigidbody 沿斜坡放大速度（上坡加速）。
        /// 仅在移动方向含上坡分量时生效；下坡/平地返回 1。
        /// </summary>
        private float GetSlopeSpeedFactor()
        {
            if (!isGrounded || _groundHit.collider == null) return 1f;
            float ga = GroundAngle();
            if (ga <= 0.01f || ga >= slopeLimit) return 1f;

            Vector3 slopeDown = Vector3.ProjectOnPlane(Vector3.down, _groundHit.normal);
            if (slopeDown.sqrMagnitude < 0.0001f) return 1f;
            slopeDown.Normalize();

            // 移动方向在斜坡向上方向的分量 >0 表示上坡
            float uphill = Vector3.Dot(transform.forward, -slopeDown);
            if (uphill <= 0.1f) return 1f;

            return Mathf.Cos(ga * Mathf.Deg2Rad);
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // 鐠哄疇绌敍鍫濐槻閸掕甯悧鍫礆










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private void ControlJumpBehaviour()
        {
            if (!isJumping) return;
            _jumpCounter -= Time.fixedDeltaTime;
            if (_jumpCounter <= 0) { _jumpCounter = 0; isJumping = false; }
            var vel = _rb.velocity;
            vel.y = jumpHeight;
            SetVelocity(vel);
        }

        public void AirControl()
        {
            if (isGrounded || !JumpFwdCondition || isKnockedBack) return;

            if (jumpAirControl)
            {
                if (isStrafing)
                {
                    Vector3 vel = transform.forward * (jumpForward * _speed)
                                + transform.right   * (jumpForward * _direction);
                    SetVelocity(new Vector3(vel.x, _rb.velocity.y, vel.z));
                }
                else
                {
                    Vector3 vel = transform.forward * (jumpForward * _speed);
                    SetVelocity(new Vector3(vel.x, _rb.velocity.y, vel.z));
                }
            }
        }

        private bool JumpFwdCondition
        {
            get
            {
                Vector3 p1 = transform.position + _col.center + Vector3.up * (-_col.height * 0.5f);
                Vector3 p2 = p1 + Vector3.up * _col.height;
                // 鐢?CapsuleCast 替代 CapsuleCastAll，避免数组分?GC
                return !Physics.CapsuleCast(p1, p2, _col.radius * 0.5f, transform.forward, 0.6f, groundLayer);
            }
        }











        // 閸忣剙绱戦幒銉ュ經娓?GravesInputHandler 鐠嬪啰鏁?
        public void Jump()
        {
            // Called from LateUpdate - record into the jump buffer;
            // actual execution happens in FixedUpdate (Coyote + Buffer combined).
            if (!customAction)
                _jumpBufferCounter = jumpBufferTime;
        }

        private void ExecuteJump()
        {
            // Jump Buffer：缓冲窗口内持续尝试，落地（coyote）后立即触发
            if (_jumpBufferCounter <= 0f) return;
            if (customAction || inLadderZone) { _jumpBufferCounter = 0f; return; }  // 梯子触发器范围内禁止跳跃
            // landHigh allows jump to cancel the hard-landing; other actions block
            bool canJump = !isCrouching && (_coyoteCounter > 0f) && (!actions || landHigh) && !isJumping;
            if (!canJump) return;  // 不清零缓冲，等下一帧落地条件满足
            if (currentStamina < jumpStaminaCost) { _jumpBufferCounter = 0f; return; }

            _jumpBufferCounter = 0f;
            ReduceStamina(jumpStaminaCost);
            _jumpCounter   = jumpTimer;
            _coyoteCounter = 0f;
            isJumping      = true;

            float rawH = _inputSource.MoveAxesRaw.x;
            float rawV = _inputSource.MoveAxesRaw.y;
            bool hasInput = (rawH * rawH + rawV * rawV) > 0.01f;
            _anim.CrossFadeInFixedTime(hasInput ? _stateNames.jumpMove : _stateNames.jump, 0.1f);
        }










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // 缂堢粯绮撮敍鍫濐槻閸掕甯悧鍫礆










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        public void Roll()
        {
            // Called from LateUpdate - just record the request.
            _rollRequested = true;
        }

        private void ExecuteRoll()
        {
            if (!_rollRequested) return;
            _rollRequested = false;
            // landHigh allows roll to cancel hard-landing
            bool actionsOk = !actions || quickStop || landHigh;
            bool canRoll   = (input != Vector2.zero || _speed > 0.25f) && actionsOk && isGrounded && !isJumping;
            if (!canRoll || isRolling) return;
            if (currentStamina < rollStaminaCost) return;

            ReduceStamina(rollStaminaCost);
            _anim.SetTrigger(H_ResetState);
            _anim.CrossFadeInFixedTime(_stateNames.roll, 0.1f);
        }










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // 闊弓绗呴敍鍫濐槻閸掕甯悧鍫礆










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        public void Crouch()
        {
            // Crouch 是姿态切换，不应被 quickStop/landHigh 这类普通动画状态吞掉；
            // 只有翻越/梯子等 customAction 和翻滚期间禁止切换。
            bool nearGround = isGrounded || _groundDistance <= groundMaxDistance * 2f;
            if (!nearGround || customAction || isRolling) return;
            if (isCrouching && CanExitCrouch())
                isCrouching = false;
            else
                isCrouching = true;
        }

        private bool CanExitCrouch()
        {
            float radius = _col.radius * 0.9f;
            Vector3 pos  = transform.position + Vector3.up * ((_colHeight * 0.5f) - _colRadius);
            return !Physics.SphereCast(new Ray(pos, Vector3.up), radius, out _,
                headDetect - (_colRadius * 0.1f), autoCrouchLayer);
        }

        private void AutoCrouch()
        {
            if (_autoCrouch) isCrouching = true;
            if (_autoCrouch && !_inCrouchArea && CanExitCrouch())
            {
                _autoCrouch = false;
                isCrouching = false;
            }
        }

        public void SetAutoCrouch(bool inArea)
        {
            _autoCrouch  = inArea;
            _inCrouchArea = inArea;
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?










        // 閼宠泛娉担鎾冲З閹胶缂夐弨鎾呯礄闊弓绗?/ 缂堢粯绮?/ LandHigh閿?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private float _capsuleHeightVel = 0f;   // SmoothDamp 速度缓存
        private float _capsuleRadiusVel = 0f;

        private void ControlCapsuleHeight()
        {
            bool wantCrouch = isCrouching || isRolling || landHigh;
            float targetHeight = wantCrouch ? _colHeight / 1.5f : _colHeight;
            float targetRadius = wantCrouch ? _colRadius / 1.5f : _colRadius;

            // 蹲下/起立时平滑过渡（?0.12s锛夛紝涓?Animator CrossFade 时长同
            // 直接进矮洞强制蹲时也€要平滑，避免胶囊突变卡
            float smoothTime = 0.12f;
            float newHeight = Mathf.SmoothDamp(_col.height, targetHeight, ref _capsuleHeightVel, smoothTime);
            float newRadius = Mathf.SmoothDamp(_col.radius, targetRadius, ref _capsuleRadiusVel, smoothTime);

            _col.height = newHeight;
            _col.radius = newRadius;
            // center 闅?height 按比例调整（保持底部贴地?
            _col.center = _colCenter * (newHeight / _colHeight);
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // StopMove 濡偓濞村绱欓弬婊冩蒋 / 闂呮粎顣查悧鈺呮▎濮濄垻些閸旑煉绱?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        [Header("Stop Move")]
        public LayerMask stopMoveLayer;
        public float stopMoveHeight   = 0.65f;
        public float stopMoveDistance = 0.5f;

        private void CheckStopMove()
        {
            if (input.sqrMagnitude < 0.1f || !isGrounded) { _stopMove = false; return; }

            // 蹲下时用当前胶囊实际高度的一半作为射线高度，避免顶面Е鍙?
            float rayHeight = isCrouching ? (_col.height * 0.4f) : stopMoveHeight;

            Ray ray = new Ray(transform.position + new Vector3(0, rayHeight, 0),
                              GetMoveDirection().normalized);

            if (Physics.Raycast(ray, out RaycastHit hit, _col.radius + stopMoveDistance, stopMoveLayer))
            {
                float a = Vector3.Angle(Vector3.up, hit.normal);
                _stopMove = (hit.distance <= stopMoveDistance && a > 85f) || (a >= slopeLimit + 1f && a <= 85f);
            }
            else if (Physics.Raycast(ray, out hit, 1f, groundLayer))
            {
                float a = Vector3.Angle(Vector3.up, hit.normal);
                _stopMove = (a >= slopeLimit + 1f && a <= 85f);
            }
            else
                _stopMove = false;
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?










        // AutoCrouch Trigger 濡偓濞村绱欐径宥呭煝閸樼喓澧楅敍姝峚g = "AutoCrouch"閿涘本妫ら棁鈧崷鐑樻珯閼存碍婀伴敍?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("AutoCrouch"))
            {
                _autoCrouch  = true;
                _inCrouchArea = true;
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (other.CompareTag("AutoCrouch"))
            {
                _autoCrouch  = true;
                _inCrouchArea = true;
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("AutoCrouch"))
                _inCrouchArea = false;










            // _autoCrouch 閻?AutoCrouch() 閸?Update 娑擃厽鐗撮幑?CanExitCrouch 閼奉亜濮╁〒鍛存珟
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?










        // 缁備胶鏁?/ 閸氼垳鏁ら柌宥呭閸滃瞼顫幘鐑囩礄娓?GravesActionHandler 娴ｈ法鏁ら敍?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        public void DisableGravityAndCollision()
        {
            _anim.SetFloat(H_InputHorizontal,  0f);
            _anim.SetFloat(H_InputVertical,     0f);
            _anim.SetFloat(H_VerticalVelocity,  0f);
            _rb.useGravity = false;










            // 濞夈劍鍓伴敍姘瑝閺€?_col.isTrigger閿涘奔绻氶幐浣筋潡閼硅尙澧块悶鍡欘潾閹剧儑绱濋柆鍨帳閻潙娼?OnTrigger 濡偓濞?
            }

        public void EnableGravityAndCollision(float normalizedTime)
        {
            if (_baseLayerInfo.normalizedTime >= normalizedTime)
            {
                _rb.useGravity = true;
                // 鍚屾鎭㈠碰撞体（?DisableGravityAndCollision 瀵圭О锛?
                if (_col != null) _col.enabled = true;
            }
        }











        /// <summary>










        /// 鐎靛綊缍堥崢鐔哄 vThirdPersonAnimator.MatchTarget 閳?鐢箑鐣ㄩ崗銊︻梾閺屻儳娈?Animator.MatchTarget 鐏忎浇顥?










        /// </summary>
        public bool MatchTarget(Vector3 matchPosition, Quaternion matchRotation, AvatarTarget target,
            MatchTargetWeightMask weightMask, float normalisedStartTime, float normalisedEndTime)
        {
            if (_anim == null) return false;
            if (_anim.isMatchingTarget) return false;
            if (_anim.IsInTransition(_normalStateLayerIndex)) return false;

            float normalizeTime = Mathf.Repeat(_baseLayerInfo.normalizedTime, 1f);
            if (normalizeTime > normalisedEndTime) return false;

            _anim.MatchTarget(matchPosition, matchRotation, target, weightMask, normalisedStartTime, normalisedEndTime);
            return true;
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // Animator 閸欏倹鏆熼崥灞绢劄閿涘牆鐣弫鏉戭槻閸掍紮绱?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private void UpdateAnimator()
        {
            if (_anim == null) return;

            // customAction 期间强制 IsGrounded=true，防止进?交互动作触发 Falling
            bool animGrounded = isGrounded || customAction;
            _anim.SetBool(H_IsGrounded,     animGrounded);
            _anim.SetBool(H_IsCustomAction, customAction);
            _anim.SetBool(H_IsStrafing,     isStrafing);
            _anim.SetBool(H_IsCrouching, isCrouching);
            // Default Controller 的部分 Crouch 子状态机仍使用旧 Crouch 条件；
            // 双写保证 graves 与新创建角色都能进入同一套蹲伏过渡。
            _anim.SetBool(H_Crouch,      isCrouching);
            _anim.SetBool(H_isDead,      isDead);

            // GroundDistance / VerticalVelocity锛?
            //   customAction 时立即强制为安全值（?Falling），其余帧用阻尼平滑落地跳变
            if (customAction)
            {
                _anim.SetFloat(H_GroundDistance,   -1f);
                _anim.SetFloat(H_VerticalVelocity,  0f);
            }
            else
            {
                float targetVV = animGrounded ? 0f : _verticalVelocity;
                _anim.SetFloat(H_GroundDistance,   _groundDistance, 0.05f, Time.deltaTime);
                _anim.SetFloat(H_VerticalVelocity, targetVV,        0.08f, Time.deltaTime);
            }

            // 始终写入水平输入：移动攻击期间即使未开启 Strafe，也必须保留 A/D 的符号，
            // 供 Animator 的移动/镜像混合使用；旧逻辑仅在 isStrafing 时写入，首次按键攻击会沿用 0。
            bool movementUnlocked = !lockMovement || _combatMovementOverride;
            bool canMove = !_stopMove && movementUnlocked;

            // 走位方向参数(InputVertical/InputHorizontal)直接驱动下半身 2D 八向融合树。
            //   战斗/Strafe 下: input = clamp(MoveAxesRaw)，即 InputVertical=input.y、InputHorizontal=input.x。
            //   此前值来自 _speed/_direction，而它们由 FixedUpdate 的 StrafeLimitSpeed 计算，
            //   输入又由 InputHandler.LateUpdate 写入 input 字段——形成「LateUpdate 写 input →
            //   FixedUpdate 算 _speed/_direction → Update 写动画参数」的一帧延迟,八向切换慢半拍。
            //   这里当帧直读原始轴(MoveAxesRaw=GetAxisRaw,无平滑),绕开整条延迟链。
            float animatorVertical, animatorHorizontal;
            if (canMove && isStrafing)
            {
                Vector2 raw = _inputSource.MoveAxesRaw;
                animatorVertical   = Mathf.Clamp(raw.y, -1f, 1f);
                animatorHorizontal = Mathf.Clamp(raw.x, -1f, 1f);
            }
            else if (canMove)
            {
                animatorVertical   = _speed;
                animatorHorizontal = _direction;
            }
            else
            {
                animatorVertical   = 0f;
                animatorHorizontal = 0f;
            }

            // 零阻尼即时写入:方向切换(前/后/左/右/对角八向)当帧生效,不再被阻尼拖慢。
            // 停止时的「跑→待机」柔和过渡由 InputMagnitude(0.2s 阻尼)承担,此处不影响。
            _anim.SetFloat(H_InputHorizontal, animatorHorizontal, 0f, Time.deltaTime);
            // customAction 期间（梯子攀爬等）不覆盖 InputVertical，由 LadderAction 接管
            if (!customAction)
                _anim.SetFloat(H_InputVertical, animatorVertical, 0f, Time.deltaTime);
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?










        // 閻╊喗鐖ｉ弬鐟版倻閿涘牅绶?SkillController 娴ｈ法鏁ら敍?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        public void UpdateTargetDirection(Transform cam = null)
        {
            if (cam != null && !rotateByWorld)
            {
                Vector3 forward = cam.TransformDirection(Vector3.forward); forward.y = 0;
                Vector3 right   = cam.TransformDirection(Vector3.right);
                _targetDirection = input.x * right + input.y * forward;
            }
            else
                _targetDirection = new Vector3(input.x, 0, input.y);
        }

        private Vector3 _targetDirection;
        public  Vector3 TargetDirectionForMotor => _targetDirection;

        private Vector3 GetMoveDirection()
        {
            if (rotateByWorld || _targetDirection.sqrMagnitude < 0.01f)
                return new Vector3(input.x, 0, input.y).normalized;

            var cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return transform.forward;

            Vector3 cf = cam.transform.forward; cf.y = 0;
            Vector3 cr = cam.transform.right;   cr.y = 0;
            if (cf.sqrMagnitude < 0.01f) cf = Vector3.forward; else cf.Normalize();
            if (cr.sqrMagnitude < 0.01f) cr = Vector3.right;   else cr.Normalize();
            return (cf * input.y + cr * input.x).normalized;
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // ICharacterMotor 閹恒儱褰涢弬瑙勭《










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        public void Sprint(bool active)
        {
            if (active)
            {
                if (isGrounded && !isCrouching && input.sqrMagnitude > 0.1f && currentStamina > 0f)
                    isSprinting = true;
            }
            else
            {
                isSprinting = false;
            }

            // 体力耗尽姩鍋滄鍐插埡
            if (currentStamina <= 0f) isSprinting = false;
        }


        public void ForceStopMovement()
        {
            input         = Vector2.zero;
            _speed        = 0f;
            _direction    = 0f;
            _strafeInput  = 0f;
            SetVelocity(new Vector3(0, _rb.velocity.y, 0));
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?










        // Root motion 閹貉冨煑閹恒儱褰涢敍鍫滅箽閻ｆ瑱绱濋崥鎴濇倵閸忕厧顔愰敍?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        public void EnableRootMotionPosition()  { useRootMotion = true; }
        public void DisableRootMotionPosition() { useRootMotion = false; }
        public void EnableRootMotionRotation()  { }
        public void DisableRootMotionRotation() { }
        public bool IsUsingRootMotionPosition => useRootMotion;











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // 瀹搞儱鍙?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        public float CurrentVelocity => _rb != null ? _rb.velocity.magnitude : 0f;
        public bool  IsSprinting     => isSprinting;

        private static int GetLayerIndex(Animator anim, string name)
        {
            for (int i = 0; i < anim.layerCount; i++)
                if (anim.GetLayerName(i) == name) return i;
            return -1;
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // Health 缁崵绮?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        // ── IDamageable 接口事件 ──
        event System.Action<float> IDamageable.OnHealthChanged
        {
            add => onHealthChanged += value;
            remove => onHealthChanged -= value;
        }
        event System.Action IDamageable.OnDeath
        {
            add => onDead += value;
            remove => onDead -= value;
        }

        float IDamageable.currentHealth => currentHealth;
        float IDamageable.maxHealth => maxHealth;

        /// <summary>旧版 TakeDamage（向后兼容 ICharacterMotor 接口）</summary>
        public void TakeDamage(float damage)
        {
            TakeDamage(damage, null, Vector3.zero);
        }

        /// <summary>
        /// IDamageable.TakeDamage — 带伤害来源和击中方向。
        /// 扣血 → 受击硬直 → 击退 → 死亡判定。
        /// </summary>
        public void TakeDamage(float damage, GameObject source, Vector3 hitDir)
        {
            if (isDead || ragdolled) return;

            // 联机(S2-4):客户端命中的伤害路由到 Host 复核落地;单机/Host 直接本地落地
            if (Game.Net.NetworkCombatRelay.TryRouteDamage(gameObject, damage, source, hitDir))
                return;

            // 扣血与事件派发委托给 CharacterStats(2026-07-30)
            Stats.ApplyDamage(damage);

            // 伤害数字飘字
            ShowDamageNumber(damage);

            // 受击反馈：硬直 + 击退
            if (source != null && hitDir.sqrMagnitude > 0.01f)
            {
                float knockbackForce = Mathf.Min(damage * 0.5f, 8f);
                ReceiveKnockback(hitDir * knockbackForce + Vector3.up * 2f, 0.25f);
            }

            if (currentHealth <= 0f)
                Die();
        }

        // ── 伤害数字飘字（与 EnemyMotor 共享同一个 DamageNumberSpawner） ──
        // 注:此处 FindObjectOfType+自动创建是刻意的场景级共享服务(全局飘字池),
        // 非 per-character 依赖,多角色同场安全(2026-07-30 确认)。
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

        public void Heal(float amount)
        {
            Stats.Heal(amount);
        }

        private void Die()
        {
            if (isDead) return;
            EnterDeadState();
            Stats.SetDead(true);   // PvP(2026-09-08):Host 权威写网络变量,各端收到后做死亡表现
        }

        /// <summary>进入死亡状态(纯本地表现,不写网络)。Die() 与网络死亡事件共用。</summary>
        private void EnterDeadState()
        {
            if (isDead) return;
            isDead        = true;
            lockMovement  = true;
            customAction  = false;
            // 死亡时恢复碰撞体（击退期间可能已禁用）
            if (_col != null) _col.enabled = true;
            isKnockedBack = false;
            _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _anim.SetBool(H_isDead, true);
            onDead?.Invoke();
        }

        /// <summary>联机对战(3.2):重生——满血+解除死亡锁+死亡动画复位。</summary>
        public void Revive()
        {
            if (!isDead) return;
            ExitDeadState();
            Stats.SetDead(false);  // PvP(2026-09-08):Host 权威写网络变量,各端收到后做复活动画表现
        }

        /// <summary>退出死亡状态(纯本地表现,不写网络)。Revive() 与网络死亡事件共用。</summary>
        private void ExitDeadState()
        {
            if (!isDead) return;
            isDead       = false;
            lockMovement = false;
            Stats.ResetToMax();
            _anim.SetBool(H_isDead, false);
        }

        /// <summary>网络死亡状态回调(CharacterStats.onDeadChanged):远端驱动的死亡/复活动画。</summary>
        private void OnDeadStateChanged(bool dead)
        {
            if (dead) EnterDeadState();
            else ExitDeadState();
        }

        private void HealthRecovery()
        {
            Stats.TickHealthRecovery();
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // Stamina 缁崵绮?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        // 数值变更全部委托 CharacterStats(2026-07-30)
        public void ReduceStamina(float amount, bool perSecond = false) => Stats.ReduceStamina(amount, perSecond);

        public void ReduceMana(float amount) => Stats.ReduceMana(amount);

        public void RestoreMana(float amount) => Stats.RestoreMana(amount);

        public bool HasMana(float amount) => Stats.HasMana(amount);

        // 自动挂头顶 HUD（玩家/敌人共用 CharacterHUD）
        private void EnsureHUD()
        {
            var hud = GetComponent<CharacterHUD>();
            if (hud == null)
            {
                hud = gameObject.AddComponent<CharacterHUD>();
                hud.motor = this;
            }
            else if (hud.motor == null)
            {
                hud.motor = this;
            }
        }

        private void StaminaRecovery()
        {










            // Sprint 閹镐胶鐢诲☉鍫ｂ偓?
            if (isSprinting)
            {
                ReduceStamina(sprintStaminaCost, true);










                // 娴ｆ挸濮忛懓妤€鏁栭敍姘辩彌閸楀啿宸遍崚璺轰粻濮濄垹鍟块崚?
                if (currentStamina <= 0f)
                    isSprinting = false;
                return;
            }

            Stats.TickStaminaRecovery();
        }

        private void ManaRecovery()
        {
            Stats.TickManaRecovery();
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // Ragdoll 缁崵绮?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private void CheckRagdoll()
        {
            if (ragdollVelocity == 0f || ragdolled) return;
            // 鍙非跳跃状态落地且速度超过阈€时触发（排除普通跳跃落地）
            if (!isJumping && _verticalVelocity <= ragdollVelocity && _groundDistance <= 0.1f)
                EnableRagdoll();
        }

        public void EnableRagdoll()
        {
            if (ragdolled) return;
            _anim.SetFloat(H_InputHorizontal,  0f);
            _anim.SetFloat(H_InputVertical,     0f);
            _anim.SetFloat(H_VerticalVelocity,  0f);
            ragdolled    = true;
            lockMovement = true;
            _col.isTrigger      = true;
            _rb.useGravity      = false;
            _rb.isKinematic     = true;
            onActiveRagdoll?.Invoke();
        }

        public void ResetRagdoll()
        {
            ragdolled    = false;
            lockMovement = false;
            _rb.WakeUp();
            _rb.useGravity  = true;
            _rb.isKinematic = false;
            _col.isTrigger  = false;
            onResetRagdoll?.Invoke();
        }











        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?        // 闂呭繑婧€ Idle 閸斻劎鏁?










        // 閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡鎰ㄦ櫜閳烘劏鏅查埡?
        private static readonly int H_RandomIdle = AnimatorParams.RandomIdle;

        private void CheckRandomIdle()
        {
            if (randomIdleTime <= 0f) return;
            if (input.sqrMagnitude > 0.01f || !isGrounded || actions || isCrouching) { _idleTimer = randomIdleTime; return; }

            _idleTimer -= Time.deltaTime;
            if (_idleTimer <= 0f)
            {
                _anim.SetTrigger(H_RandomIdle);
                _idleTimer = randomIdleTime;
            }
        }
    }
}



