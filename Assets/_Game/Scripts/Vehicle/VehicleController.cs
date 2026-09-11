using System.Collections.Generic;
using UnityEngine;
using Game.Character;
using Game.SkillSystem;

namespace Game.Vehicle
{
    /// <summary>
    /// 载具控制器(顶视角 ARCADE 车辆)。
    ///
    /// ─── 功能(2026-09-10) ───
    ///   角色在载具周围按 E → 上车:控制权从角色切换到载具,镜头跟随载具。
    ///   驾驶时:
    ///     • 只能移动,不能攻击(角色 SkillController 被禁用)。
    ///     • 真实转弯(自行车模型):W/S 沿车头前进/后退,A/D 打方向盘带动前轮转向,
    ///       车头随「车速 × 前轮转角」偏转——只有前进/后退时才转向,静止时不原地打方向。
    ///     • 长按位移键逐步加速(带加速度曲线),最高达 driveSpeed × maxSpeedMultiplier(默认 2 倍速);
    ///       松手逐步减速;撞墙/撞陡坡后速度归零,需松手或反向后重新提速。
    ///     • 车轮随车速滚动,前轮随方向盘转向。
    ///     • 多点贴地:前后左右四处采样地表高度,车身随地形俯仰/侧倾,上下坡不悬空、不穿地。
    ///     • 坡度阻挡:前进/倒车方向坡度超过 maxClimbAngle 的陡坡无法开过去。
    ///   再按 E → 下车:角色在载具侧方恢复,镜头切回角色。
    ///
    /// ─── 设计要点 ───
    ///   • 上车/下车用「距离判定」而非 Trigger 碰撞体:免去在预制体里手工加 Trigger,
    ///     也避免 OnTriggerEnter/Exit 生命周期在角色被隐藏/禁用时失效。
    ///   • 上车时对角色做「快照 + 禁用」:禁用输入/技能/移动/交互组件,隐藏渲染,
    ///     冻结刚体,下车时按快照原样恢复,不污染角色状态。
    ///   • 载具用 Kinematic Rigidbody + MovePosition/MoveRotation 驱动:
    ///     既保证与静态墙体碰撞(不会穿墙),又完全由脚本控制运动。
    ///   • 贴地用「前后左右四点」向下射线检测地表(排除载具自身碰撞体),每物理步
    ///     同时校正高度(y)与俯仰/侧倾姿态(pitch/roll),适配不平地面与上下坡。
    ///
    /// ─── 已知边界 ───
    ///   • 本组件为本地单机逻辑;联机同步(载具位置/驾驶权)暂未实现。
    ///   • 下车点取载具侧方,若侧方贴墙可能落点不佳,后续可加落点探测。
    /// </summary>
    [AddComponentMenu("Game/Vehicle System/Vehicle Controller")]
    public class VehicleController : MonoBehaviour
    {
        [Header("Interaction")]
        [Tooltip("上车/下车按键。")]
        public KeyCode interactKey = KeyCode.E;
        [Tooltip("上车交互半径(米)。角色进入此距离内按 E 才生效。")]
        public float interactRadius = 3f;

        [Header("Movement")]
        [Tooltip("基础驾驶速度(m/s,默认 12 ≈ 43km/h)。")]
        public float driveSpeed = 12f;
        [Tooltip("上车时是否从角色 CharacterMotor.freeSprintSpeed 同步基础速度(建议关闭,让载具用真实汽车速度)。")]
        public bool syncSpeedFromCharacter = false;
        [Tooltip("最高速度倍率(长按位移键逐步加速到 driveSpeed × 此值,默认 2 倍 ≈ 86km/h)。")]
        public float maxSpeedMultiplier = 2f;
        [Tooltip("加速率(m/s²,真实汽车约 3~5,街机取 6 更跟手)。")]
        public float acceleration = 6f;
        [Tooltip("松手/刹车减速率(m/s²,真实汽车约 8~10)。")]
        public float brakeDeceleration = 12f;

        [Header("Nitro")]
        [Tooltip("氮气加速按键(按住期间提升最高速度与加速度)。")]
        public KeyCode nitroKey = KeyCode.LeftShift;
        [Tooltip("氮气时最高速度额外倍率(叠加在 maxSpeedMultiplier 上,默认 1.5 → 最高 3 倍速)。")]
        public float nitroSpeedMultiplier = 1.5f;
        [Tooltip("氮气时加速度额外倍率(推背感更强)。")]
        public float nitroAccelerationMultiplier = 1.8f;

        [Header("Steering")]
        [Tooltip("前轮最大转向角(度)。越大转弯半径越小,真实汽车约 30~40。")]
        public float maxSteerAngle = 35f;
        [Tooltip("前轮转向角平滑速度(度/秒)。越大打方向越跟手,真实车约 200~300。")]
        public float steerSmoothSpeed = 220f;
        [Tooltip("转弯灵敏度倍率(1 = 物理自行车模型,嫌转得慢可调大)。")]
        public float turnSharpness = 1f;
        [Tooltip("轴距(前后轮距离,米)。0 = 自动按车轮位置计算。")]
        public float wheelbase = 0f;

        [Header("Obstacle")]
        [Tooltip("可翻越障碍的最大高度差(米)。车底需要抬升的高度 ≤ 此值 → 碾过去;超过 → 撞停。默认 0.8 ≈ 车轮直径,能翻越小石头/碎石。")]
        public float maxStepOverHeight = 0.8f;
        [Tooltip("越障抬升余量(米)。碾过时车底抬高到障碍顶部 + 此值,给跨越留缓冲。")]
        public float stepUpClearance = 0.05f;

        [Header("Wheels")]
        [Tooltip("前轮(转向 + 滚动)。留空则运行时按名字自动查找。")]
        public Transform[] frontWheels;
        [Tooltip("后轮(仅滚动)。留空则运行时按名字自动查找。")]
        public Transform[] rearWheels;
        [Tooltip("轮子滚动方向反转(若轮子滚动方向看起来反了就勾上)。")]
        public bool invertWheelSpin = false;

        [Header("Ground Snap")]
        [Tooltip("地面检测射线起点相对载具中心的高度(米)。")]
        public float groundCheckHeight = 3f;
        [Tooltip("载具贴地后的高度补偿(米)。pivot 高于地表时填正值。")]
        public float groundOffset = 0f;
        [Tooltip("地面检测层(-1 = 所有层)。")]
        public LayerMask groundLayerMask = ~0;
        [Tooltip("最大可爬坡角度(度)。前进/后退方向的坡度超过此角度时阻挡,无法开过去。")]
        public float maxClimbAngle = 35f;
        [Tooltip("地面采样球半径(米)。用轮子半径大小的球体向下采样,可「骑」过细小缝隙/裂缝而不落入。0=自动取轮子平均半径。")]
        public float groundSampleRadius = 0f;
        [Tooltip("贴地高度/姿态平滑速度(1/秒)。值越大越跟手,越小越平滑(抗缝隙单帧跳变)。")]
        public float groundHeightSmooth = 12f;
        [Tooltip("贴地兜底探测距离(米)。四点常规采样都采不到地面时(开上高处/悬崖边),用此距离向下探测下方地表并落回,避免空中开车。")]
        public float groundFallbackDistance = 100f;

        [Tooltip("下车点相对载具中心的侧向偏移(米)。")]
        public float exitOffset = 2.5f;

        [Header("Debug")]
        public bool debugLog = false;

        [Header("Debug - Read Only")]
        public bool isDriving;
        public bool playerInRange;

        // ── 玩家角色引用 ──
        private Transform _player;
        private CharacterMotor _playerMotor;
        private CharacterInputHandler _playerInput;
        private SkillController _playerSkill;
        private CharacterActionHandler _playerAction;
        private CharacterLadderAction _playerLadder;
        private CharacterHUD _playerHud;
        private Rigidbody _playerRb;

        // ── 上车快照(用于下车原样恢复) ──
        private MonoBehaviour[] _controlComponents;
        private bool[] _controlStates;
        private Renderer[] _renderers;
        private bool[] _rendererStates;
        private Collider[] _colliders;
        private bool[] _colliderStates;
        private bool _playerRbWasKinematic;

        // ── 场景依赖 ──
        private TopdownCameraController _cameraController;
        private Rigidbody _rb;

        // ── 车轮/转向运行时状态 ──
        private Transform[] _wheels;
        private bool[] _isFrontWheel;
        private float[] _wheelRollAccum;
        private float[] _wheelRadii;
        private float _wheelbase;
        private float _trackWidth;
        private float _currentSteerAngle;
        private float _yaw;
        private float _currentSpeed;    // 有符号瞬时速度(正=前进,负=倒车)
        private bool _collisionStopped;  // 撞墙后保持停止,直到松手或反向输入
        private int _collisionBlockDir;  // 撞墙时的输入方向(1=前进,-1=倒车)

        // ── 贴地采样运行时状态 ──
        private float _groundSampleRadius; // 实际采样球半径(=groundSampleRadius 或轮子平均半径)
        private bool _groundSmoothInit;    // 贴地平滑是否已初始化(上车时重置)
        private float _smoothedGroundY;    // 平滑后的地表高度
        private float _smoothedPitch;      // 平滑后的俯仰角
        private float _smoothedRoll;       // 平滑后的侧倾角

        // ── 越障状态 ──
        private Collider[] _carColliders;   // 载具所有碰撞体(用于算包围盒与越障)
        private float _pivotToBottom;       // pivot 到最低碰撞点(轮子底)的垂直距离
        private Collider _stepUpObstacle;   // 正在越过的低矮障碍(持续抬升直到越过)
        private float _stepUpHeight;        // 越障目标底盘高度(-∞ 表示未越障)

        void Awake()
        {
            // 载具原预制体无 Rigidbody,运行时补齐为 Kinematic,便于与静态墙体碰撞。
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.constraints = RigidbodyConstraints.FreezeRotation; // 朝向由 MoveRotation 手动控制

            ResolveWheels();

            // 采样球半径:groundSampleRadius<=0 时自动取轮子平均半径。
            if (_wheelRadii != null && _wheelRadii.Length > 0)
            {
                float rsum = 0f;
                for (int i = 0; i < _wheelRadii.Length; i++) rsum += _wheelRadii[i];
                _groundSampleRadius = groundSampleRadius > 0f ? groundSampleRadius : (rsum / _wheelRadii.Length);
            }
            else
                _groundSampleRadius = groundSampleRadius > 0f ? groundSampleRadius : 0.45f;

            _yaw = transform.eulerAngles.y;

            // 收集载具所有碰撞体,算 pivot 到最低碰撞点(轮子底)的垂直距离。
            // 越障抬升要以「车底越过障碍顶」为目标,而非「车 pivot 越过障碍顶」,
            // 否则车底仍会陷在障碍里被物理卡住(历史 bug)。
            _carColliders = GetComponentsInChildren<Collider>(true);
            _pivotToBottom = 0f;
            if (_carColliders != null && _carColliders.Length > 0)
            {
                Bounds b = new Bounds(transform.position, Vector3.zero);
                bool first = true;
                for (int i = 0; i < _carColliders.Length; i++)
                {
                    var c = _carColliders[i];
                    if (c == null || c.isTrigger) continue;
                    if (first) { b = c.bounds; first = false; }
                    else b.Encapsulate(c.bounds);
                }
                if (!first)
                    _pivotToBottom = transform.position.y - b.min.y;
            }
            _stepUpHeight = float.NegativeInfinity;
        }

        void Start()
        {
            _cameraController = FindObjectOfType<TopdownCameraController>();
            ResolvePlayer();
        }

        void Update()
        {
            // 按键判定必须放 Update(GetKeyDown 在物理帧不可靠)
            if (_player == null)
            {
                ResolvePlayer();
                if (_player == null) return;
            }

            if (isDriving)
            {
                if (LocalInputSource.Shared.GetKeyDown(interactKey))
                    ExitVehicle();
            }
            else
            {
                playerInRange = Vector3.Distance(_player.position, transform.position) <= interactRadius;
                if (playerInRange && LocalInputSource.Shared.GetKeyDown(interactKey))
                    EnterVehicle();
            }
        }

        void FixedUpdate()
        {
            if (!isDriving) return;

            Vector2 axes = LocalInputSource.Shared.MoveAxesRaw;
            float throttle = axes.y; // W=1 前进, S=-1 后退
            float steer = axes.x;    // A=-1 左转, D=1 右转
            bool nitro = LocalInputSource.Shared.GetKey(nitroKey);

            // 1) 前轮转向角:平滑过渡到目标角度(A/D 控制),松手自动回正。
            float targetSteer = steer * maxSteerAngle;
            _currentSteerAngle = Mathf.MoveTowards(
                _currentSteerAngle, targetSteer, steerSmoothSpeed * Time.fixedDeltaTime);

            Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 fwd = yawRot * Vector3.forward;

            // 2) 坡度阻挡:采样「当前朝向」下的地面前后坡度,超过 maxClimbAngle 的陡坡禁止通过。
            //    pitchDeg > 0 = 上坡(车头高)。前进时坡度 > 阈值 → 挡住无法上坡;
            //    倒车时坡度 < -阈值(车尾朝上坡)→ 挡住无法倒上坡。下坡不阻挡。
            bool blocked = false;
            if (Mathf.Abs(throttle) > 0.01f &&
                SampleGroundPlane(_rb.position, yawRot,
                    out float _, out float curPitchDeg, out float _))
            {
                if (throttle > 0f && curPitchDeg > maxClimbAngle) blocked = true;
                else if (throttle < 0f && curPitchDeg < -maxClimbAngle) blocked = true;
            }

            // 3) 撞停防抖:撞墙后保持停止,直到松手或反向输入才解除,避免顶着墙反复微加速抖动。
            if (_collisionStopped)
            {
                int inputDir = Mathf.Abs(throttle) < 0.01f ? 0 : (throttle > 0f ? 1 : -1);
                if (inputDir != _collisionBlockDir)
                    _collisionStopped = false; // 松手或反向 → 解除撞停
            }

            // 4) 速度状态机:长按位移键逐步加速到最高速度(driveSpeed × maxSpeedMultiplier),
            //    松手逐步减速;撞墙/撞陡坡后速度归零,需松手或反向重新提速。
            //    按氮气键时,最高速度 × nitroSpeedMultiplier、加速度 × nitroAccelerationMultiplier。
            float topMultiplier = maxSpeedMultiplier * (nitro ? nitroSpeedMultiplier : 1f);
            float accel = acceleration * (nitro ? nitroAccelerationMultiplier : 1f);

            float targetSpeed = 0f;
            if (!_collisionStopped && Mathf.Abs(throttle) > 0.01f && !blocked)
                targetSpeed = throttle * driveSpeed * topMultiplier;

            if (blocked)
                _currentSpeed = 0f; // 陡坡阻挡:立即撞停
            else if (Mathf.Abs(targetSpeed) > Mathf.Abs(_currentSpeed))
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, accel * Time.fixedDeltaTime);
            else
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, brakeDeceleration * Time.fixedDeltaTime);

            // 5) 真实转弯(自行车模型):车头朝向随「车速 × 前轮转角」偏转,只有前进/后退时才转向。
            //    yawRate = (v / L) * tan(δ):前进时右打方向车头右转,倒车时车头反向偏转(车尾甩向转向侧)。
            if (Mathf.Abs(_currentSpeed) > 0.001f && Mathf.Abs(_currentSteerAngle) > 0.01f)
            {
                float yawRate = (_currentSpeed / _wheelbase) * Mathf.Tan(_currentSteerAngle * Mathf.Deg2Rad);
                float yawDeg = yawRate * turnSharpness * Time.fixedDeltaTime * Mathf.Rad2Deg;
                _yaw += yawDeg;
                yawRot = Quaternion.Euler(0f, _yaw, 0f);
                fwd = yawRot * Vector3.forward;
            }

            // 6) 撞墙/越障检测:用刚体 SweepTest 沿「实际移动方向」扫描。
            //    • 命中竖直面(法线 y<0.5)才继续判定(法线近竖直 → 地面/坡面,交给坡度阻挡);
            //    • 算出「车底需要抬升的高度差」= 障碍顶部 - 车当前底盘高度:
            //        ≤ maxStepOverHeight → 可翻越,进入「越障」状态,持续把车底抬到障碍顶之上碾过去;
            //        >  maxStepOverHeight → 高障碍,撞停。
            //    (关键改进:不再按「障碍碰撞体总高 bounds.size.y」一刀切——大石头的低矮边缘、
            //     半埋的石头也能按「实际需要抬升多少」正确判定,而不是被整块总高误判为高障碍。)
            float signedMove = _currentSpeed * Time.fixedDeltaTime;
            if (Mathf.Abs(_currentSpeed) > 0.001f)
            {
                Vector3 moveDir = signedMove >= 0f ? fwd : -fwd;
                float sweepDist = Mathf.Abs(signedMove) + 0.05f; // 加 5cm skin 提前量
                if (_rb.SweepTest(moveDir, out RaycastHit hit, sweepDist))
                {
                    if (hit.normal.y < 0.5f)
                    {
                        // 车底当前高度 = pivot 到最低碰撞点距离,越障需求 = 障碍顶 - 车底。
                        float carBottomY = _rb.position.y - _pivotToBottom;
                        float stepUpRequired = hit.collider.bounds.max.y - carBottomY;

                        if (stepUpRequired <= maxStepOverHeight)
                        {
                            // 进入/刷新越障状态:目标 pivot 高度 = 障碍顶 + pivot 到底盘距离 + 余量,
                            // 保证「车底」真正越过障碍顶(而非只把 pivot 抬上去)。
                            _stepUpObstacle = hit.collider;
                            _stepUpHeight = hit.collider.bounds.max.y + _pivotToBottom + stepUpClearance;
                            if (debugLog)
                                Debug.Log($"[Vehicle] 越障({hit.collider.name}) 抬升={stepUpRequired:F2}m");
                        }
                        else
                        {
                            // 高障碍 → 撞停,并清除越障状态。
                            _stepUpObstacle = null;
                            _stepUpHeight = float.NegativeInfinity;
                            _collisionBlockDir = _currentSpeed > 0f ? 1 : -1; // 记录实际运动方向(而非输入)
                            _currentSpeed = 0f;
                            _collisionStopped = true;
                            signedMove = 0f;
                            if (debugLog)
                                Debug.Log($"[Vehicle] 撞墙({hit.collider.name}),需松手/反向重新提速");
                        }
                    }
                }
            }

            // 6.5) 越障状态维护:正在越障时检查车是否已水平越过障碍(水平投影不再重叠)。
            //      已越过 → 清除状态回落贴地;未越过 → 本帧继续抬升,避免「单帧抬升→下帧回落→穿透卡死」。
            if (_stepUpObstacle != null && !CarOverlapsObstacleXZ(_stepUpObstacle))
            {
                _stepUpObstacle = null;
                _stepUpHeight = float.NegativeInfinity;
                _groundSmoothInit = false; // 回落贴地时重新初始化平滑,避免高度跳变
            }

            // 7) 计算目标位置 + 多点贴地:采样前后左右四处地表高度,计算俯仰/侧倾与中心高度。
            Vector3 targetPos = _rb.position + fwd * signedMove;
            Vector3 newPos = targetPos;
            Quaternion newRot = yawRot;

            bool steppingUp = _stepUpObstacle != null;
            if (steppingUp)
            {
                // 越障期间:跳过地面采样,锁定高度到障碍顶之上、姿态保持水平(仅 yaw)。
                //   原因:此时车底悬在障碍上方,地面采样球会命中障碍本身、把障碍顶误当「地面」,
                //   导致贴地高度与 pitch/roll 来回跳变、车身在障碍上抖动甚至被拉回。
                newPos.y = _stepUpHeight;
                newRot = yawRot;
            }
            else if (SampleGroundPlane(targetPos, yawRot, out float groundY, out float pitchDeg, out float rollDeg))
            {
                // 高度/姿态做帧率无关的指数平滑,进一步吸收缝隙边缘的单帧跳变。
                if (!_groundSmoothInit)
                {
                    _smoothedGroundY = groundY;
                    _smoothedPitch = pitchDeg;
                    _smoothedRoll = rollDeg;
                    _groundSmoothInit = true;
                }
                float t = 1f - Mathf.Exp(-groundHeightSmooth * Time.fixedDeltaTime);
                _smoothedGroundY = Mathf.Lerp(_smoothedGroundY, groundY, t);
                _smoothedPitch = Mathf.Lerp(_smoothedPitch, pitchDeg, t);
                _smoothedRoll = Mathf.Lerp(_smoothedRoll, rollDeg, t);

                newPos.y = _smoothedGroundY + groundOffset;
                newRot = yawRot * Quaternion.Euler(_smoothedPitch, 0f, _smoothedRoll);
            }

            // 越障抬升兜底:即使未进入 steppingUp(如越障状态刚被刷新),也确保高度不低于越障目标。
            if (_stepUpHeight > newPos.y)
                newPos.y = _stepUpHeight;

            _rb.MovePosition(newPos);
            _rb.MoveRotation(newRot);

            // 8) 轮子视觉:滚动 + 前轮转向。
            UpdateWheelVisuals(signedMove, _currentSteerAngle);
        }

        // ─── 上车 ───────────────────────────────────────────────────────

        private void EnterVehicle()
        {
            if (_player == null) return;
            isDriving = true;

            // 重置驾驶状态(避免上次驾驶残留瞬时速度/撞停/越障标记)
            _currentSpeed = 0f;
            _collisionStopped = false;
            _groundSmoothInit = false; // 重新上车时贴地平滑从头初始化,避免高度跳变
            _stepUpObstacle = null;
            _stepUpHeight = float.NegativeInfinity;

            if (syncSpeedFromCharacter && _playerMotor != null)
                driveSpeed = _playerMotor.freeSprintSpeed;

            // 清理角色当前状态,避免下车后残留战斗/点击移动
            if (_playerInput != null && _playerInput.IsClickMoving)
                _playerInput.CancelClickMove();
            if (_playerSkill != null && _playerSkill.InCombatMode)
                _playerSkill.ExitCombatMode();

            // 1) 快照并禁用角色控制组件(输入/技能/移动/交互)
            var controls = new List<MonoBehaviour>();
            if (_playerInput != null) controls.Add(_playerInput);
            if (_playerSkill != null) controls.Add(_playerSkill);
            if (_playerMotor != null) controls.Add(_playerMotor);
            if (_playerAction != null) controls.Add(_playerAction);
            if (_playerLadder != null) controls.Add(_playerLadder);
            _controlComponents = controls.ToArray();
            _controlStates = new bool[_controlComponents.Length];
            for (int i = 0; i < _controlComponents.Length; i++)
            {
                _controlStates[i] = _controlComponents[i].enabled;
                _controlComponents[i].enabled = false;
            }

            // 2) 隐藏角色渲染
            _renderers = _player.GetComponentsInChildren<Renderer>(true);
            _rendererStates = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _rendererStates[i] = _renderers[i].enabled;
                _renderers[i].enabled = false;
            }

            // 2.5) 隐藏角色头顶 HUD(血条),避免隐形角色上空还飘着血条
            if (_playerHud != null)
                _playerHud.SetVisible(false);

            // 3) 禁用角色碰撞体(避免隐形身体挡住载具)
            _colliders = _player.GetComponentsInChildren<Collider>(true);
            _colliderStates = new bool[_colliders.Length];
            for (int i = 0; i < _colliders.Length; i++)
            {
                _colliderStates[i] = _colliders[i].enabled;
                _colliders[i].enabled = false;
            }

            // 4) 冻结角色刚体
            if (_playerRb != null)
            {
                _playerRbWasKinematic = _playerRb.isKinematic;
                _playerRb.isKinematic = true;
                _playerRb.velocity = Vector3.zero;
                _playerRb.angularVelocity = Vector3.zero;
            }

            // 5) 镜头切到载具
            if (_cameraController != null)
                _cameraController.SetTarget(transform);

            if (debugLog)
                Debug.Log($"[Vehicle] Enter '{name}' driveSpeed={driveSpeed:F1}");
        }

        // ─── 下车 ───────────────────────────────────────────────────────

        private void ExitVehicle()
        {
            if (!isDriving) return;
            isDriving = false;

            // 下车点:载具侧方,保持高度(贴地)
            Vector3 exit = transform.position - transform.right * exitOffset;
            exit.y = transform.position.y;

            // 1) 放置角色(此时刚体仍 kinematic,teleport 安全)
            if (_player != null)
            {
                _player.position = exit;
                if (_playerRb != null) _playerRb.position = exit;
                // 面朝车外
                Vector3 outward = (exit - transform.position);
                outward.y = 0f;
                if (outward.sqrMagnitude > 0.001f)
                    _player.rotation = Quaternion.LookRotation(outward.normalized);
            }

            // 2) 恢复刚体
            if (_playerRb != null)
            {
                _playerRb.isKinematic = _playerRbWasKinematic;
                _playerRb.velocity = Vector3.zero;
                _playerRb.angularVelocity = Vector3.zero;
            }

            // 3) 恢复碰撞体
            if (_colliders != null)
                for (int i = 0; i < _colliders.Length; i++)
                    if (_colliders[i] != null) _colliders[i].enabled = _colliderStates[i];

            // 4) 恢复渲染
            if (_renderers != null)
                for (int i = 0; i < _renderers.Length; i++)
                    if (_renderers[i] != null) _renderers[i].enabled = _rendererStates[i];

            // 4.5) 恢复角色头顶 HUD
            if (_playerHud != null)
                _playerHud.SetVisible(true);

            // 5) 恢复控制组件
            if (_controlComponents != null)
                for (int i = 0; i < _controlComponents.Length; i++)
                    if (_controlComponents[i] != null) _controlComponents[i].enabled = _controlStates[i];

            // 6) 清残留输入,角色从 idle 起步
            if (_playerMotor != null)
                _playerMotor.input = Vector2.zero;

            // 7) 镜头切回角色
            if (_cameraController != null)
                _cameraController.SetTarget(_player);

            if (debugLog)
                Debug.Log($"[Vehicle] Exit '{name}' at {exit}");
        }

        // ─── 辅助 ───────────────────────────────────────────────────────

        /// <summary>定位玩家角色(以 CharacterInputHandler 标识)。</summary>
        private void ResolvePlayer()
        {
            var input = FindObjectOfType<CharacterInputHandler>();
            if (input == null) return;

            _player = input.transform;
            _playerInput = input;
            _playerMotor = input.GetComponent<CharacterMotor>();
            _playerSkill = input.GetComponent<SkillController>();
            _playerAction = input.GetComponent<CharacterActionHandler>();
            _playerLadder = input.GetComponent<CharacterLadderAction>();
            _playerHud = input.GetComponentInChildren<CharacterHUD>(true);
            _playerRb = input.GetComponent<Rigidbody>();
        }

        /// <summary>
        /// 解析车轮节点(通用,适配 4 轮车 / 多轴车 / 大小写后缀 / 方向盘节点):
        ///   1. 优先用 Inspector 手动配置的 frontWheels / rearWheels;
        ///   2. 未配置则自动查找:名字含 "wheel"(排除含 "steering" 的方向盘)的节点,
        ///      按「局部 z 坐标」自动分轴——z 最大的为前轴(转向 + 滚动),
        ///      其余(中间轴/后轴)只滚动不转向。这样无论轮子叫什么名字、有几根轴,
        ///      都能正确识别,不再依赖 fl/fr/rl/rr 等固定后缀。
        ///   记录每个轮子滚动半径(SphereCollider.radius)、轴距(最前轴到最后一轴 z 距离)、
        ///   轮距(左右轮 |x| 平均 × 2)。
        /// </summary>
        private void ResolveWheels()
        {
            var front = new List<Transform>();
            var rear = new List<Transform>();

            if (frontWheels != null)
                foreach (var t in frontWheels) if (t != null) front.Add(t);
            if (rearWheels != null)
                foreach (var t in rearWheels) if (t != null) rear.Add(t);

            float autoFrontZ = 0f, autoRearZ = 0f;

            // 未手动配置则自动查找
            if (front.Count == 0 && rear.Count == 0)
            {
                var candidates = new List<Transform>();
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLower();
                    if (!n.Contains("wheel")) continue;    // 只认轮子
                    if (n.Contains("steering")) continue;  // 排除方向盘(SteeringWheel/SteeringW)
                    candidates.Add(t);
                }

                if (candidates.Count > 0)
                {
                    float minZ = float.MaxValue, maxZ = float.MinValue;
                    foreach (var t in candidates)
                    {
                        float z = t.localPosition.z;
                        if (z < minZ) minZ = z;
                        if (z > maxZ) maxZ = z;
                    }
                    autoFrontZ = maxZ; // 最前轴
                    autoRearZ = minZ;  // 最后一轴

                    // 同一根轴上的左右轮 localZ 几乎相等,容差取轴距的 15%(至少 0.1m)。
                    float axisTol = Mathf.Max(0.1f, (maxZ - minZ) * 0.15f);

                    foreach (var t in candidates)
                    {
                        float z = t.localPosition.z;
                        if (maxZ - z <= axisTol) front.Add(t); // 前轴:转向
                        else rear.Add(t);                       // 中间/后轴:仅滚动
                    }
                }
            }

            var wheels = new List<Transform>();
            var isFront = new List<bool>();
            foreach (var t in front) { wheels.Add(t); isFront.Add(true); }
            foreach (var t in rear)  { wheels.Add(t); isFront.Add(false); }

            _wheels = wheels.ToArray();
            _isFrontWheel = isFront.ToArray();
            _wheelRollAccum = new float[_wheels.Length];
            _wheelRadii = new float[_wheels.Length];

            for (int i = 0; i < _wheels.Length; i++)
            {
                var sc = _wheels[i].GetComponent<SphereCollider>();
                _wheelRadii[i] = sc != null ? sc.radius : 0.45f;
            }

            // 轴距:优先公开字段;否则自动查找时用「最前轴到最后一轴」距离,手动配置时用前后平均 z 距离。
            if (wheelbase > 0f)
                _wheelbase = wheelbase;
            else if (autoFrontZ != autoRearZ)
                _wheelbase = Mathf.Abs(autoFrontZ - autoRearZ);
            else if (front.Count > 0 && rear.Count > 0)
                _wheelbase = Mathf.Abs(AverageLocalZ(front) - AverageLocalZ(rear));
            else
                _wheelbase = 3.3f;

            if (_wheelbase < 0.001f) _wheelbase = 3.3f;

            // 轮距:所有轮子 |localPosition.x| 平均值 × 2(用于左右贴地采样)。
            float sumAbsX = 0f;
            int xCount = 0;
            foreach (var w in wheels)
            {
                sumAbsX += Mathf.Abs(w.localPosition.x);
                xCount++;
            }
            _trackWidth = xCount > 0 ? (sumAbsX / xCount) * 2f : 1.5f;
        }

        /// <summary>
        /// 更新车轮视觉:所有轮子按行驶距离滚动,前轮额外绕 Y 轴打方向。
        /// 滚动角 = 行驶距离 / 半径(弧度),前进正转、后退反转。
        /// </summary>
        private void UpdateWheelVisuals(float signedMoveDistance, float steerAngle)
        {
            if (_wheels == null || _wheels.Length == 0) return;

            float spinSign = invertWheelSpin ? -1f : 1f;

            for (int i = 0; i < _wheels.Length; i++)
            {
                if (_wheels[i] == null) continue;

                float r = (_wheelRadii != null && i < _wheelRadii.Length) ? _wheelRadii[i] : 0.45f;
                float rollDeg = (signedMoveDistance / r) * Mathf.Rad2Deg * spinSign;
                _wheelRollAccum[i] = (_wheelRollAccum[i] + rollDeg) % 360f;

                if (_isFrontWheel != null && i < _isFrontWheel.Length && _isFrontWheel[i])
                {
                    // 前轮:先绕车辆 Y 轴转向,再绕轮子自身 X 轴滚动。
                    _wheels[i].localRotation =
                        Quaternion.Euler(0f, steerAngle, 0f) *
                        Quaternion.Euler(_wheelRollAccum[i], 0f, 0f);
                }
                else
                {
                    _wheels[i].localRotation = Quaternion.Euler(_wheelRollAccum[i], 0f, 0f);
                }
            }
        }

        private static float AverageLocalZ(List<Transform> wheels)
        {
            if (wheels == null || wheels.Count == 0) return 0f;
            float sum = 0f;
            foreach (var w in wheels) sum += w.localPosition.z;
            return sum / wheels.Count;
        }

        /// <summary>
        /// 多点地面采样:在载具「前左/前右/后左/后右」四个角的正上方分别向下射线,
        /// 得到四处地表高度,据此计算:
        ///   • groundY —— 载具中心应贴的地表高度(四点平均);
        ///   • pitchDeg —— 前后俯仰角(正值 → 车头上抬,上坡;负值 → 车头下压,下坡);
        ///   • rollDeg —— 左右侧倾角(右高左低 → 车右倾)。
        /// 让车身在不平地面/上下坡上表现出前后左右的高低差异。
        /// </summary>
        private bool SampleGroundPlane(Vector3 centerPos, Quaternion yawRot,
            out float groundY, out float pitchDeg, out float rollDeg)
        {
            groundY = centerPos.y;
            pitchDeg = 0f;
            rollDeg = 0f;

            float halfWheelbase = _wheelbase * 0.5f;
            float halfTrack = _trackWidth * 0.5f;

            // 四个采样点(载具局部水平偏移):FL / FR / RL / RR
            Vector3[] localPts = new Vector3[]
            {
                new Vector3(-halfTrack, 0f,  halfWheelbase),
                new Vector3( halfTrack, 0f,  halfWheelbase),
                new Vector3(-halfTrack, 0f, -halfWheelbase),
                new Vector3( halfTrack, 0f, -halfWheelbase),
            };

            float[] gY = new float[4];
            bool[] ok = new bool[4];
            int foundCount = 0;

            for (int i = 0; i < 4; i++)
            {
                Vector3 world = centerPos + yawRot * localPts[i];
                ok[i] = SampleGroundHeight(world, out gY[i]);
                if (ok[i]) foundCount++;
            }

            if (foundCount == 0)
            {
                // 四点均未采到地面:可能车身被抬高、或开上高处/悬崖边,地面骤降到常规采样范围之外。
                // 用长距离中心探测兜底,尽量让车身落回下方地表,避免「在空中开车」。
                Vector3 fallbackOrigin = centerPos + Vector3.up * groundCheckHeight;
                RaycastHit[] fbHits = Physics.SphereCastAll(
                    fallbackOrigin, _groundSampleRadius, Vector3.down, groundFallbackDistance,
                    groundLayerMask, QueryTriggerInteraction.Ignore);

                float fbBestY = float.NegativeInfinity;
                bool fbFound = false;
                for (int i = 0; i < fbHits.Length; i++)
                {
                    var h = fbHits[i];
                    if (h.collider == null) continue;
                    if (h.collider.transform == transform || h.collider.transform.IsChildOf(transform)) continue;
                    float sy = h.point.y; // hit.point 即球底接触点,其 y 就是地表高度
                    if (sy > fbBestY) { fbBestY = sy; fbFound = true; }
                }

                if (fbFound)
                {
                    groundY = fbBestY;
                    pitchDeg = 0f;
                    rollDeg = 0f;
                    return true;
                }
                return false;
            }

            // 用「中位数」抗离群:即使某个采样点球体部分落入细小缝隙,导致该点高度偏低,
            // 也不影响整体结果。中位数天然抗离群,比简单平均更稳。
            float[] sorted = { gY[0], gY[1], gY[2], gY[3] };
            System.Array.Sort(sorted);
            float median = (sorted[1] + sorted[2]) * 0.5f;

            // 缺失(掉坑采不到)或「比中位数低很多」(明显落入缝隙)的点,用中位数补齐,
            // 避免车身被单点缝隙拖下去或姿态跳变。只压低点、不压高点(凸起石头是真实地形)。
            float dropTolerance = _groundSampleRadius * 2f + 0.05f;
            for (int i = 0; i < 4; i++)
            {
                if (!ok[i] || median - gY[i] > dropTolerance)
                    gY[i] = median;
            }

            groundY = (gY[0] + gY[1] + gY[2] + gY[3]) * 0.25f;

            float frontY = (gY[0] + gY[1]) * 0.5f; // FL + FR
            float rearY  = (gY[2] + gY[3]) * 0.5f; // RL + RR
            float leftY  = (gY[0] + gY[2]) * 0.5f; // FL + RL
            float rightY = (gY[1] + gY[3]) * 0.5f; // FR + RR

            // 前高后低(上坡)→ 车头应上抬;但 Quaternion.Euler 绕 X 轴正角使 forward 朝下,
            // 故取 rearY - frontY,令「车头上抬」为正值(下坡为负,车头下压)。
            // 右高左低 → roll 正(车右倾),此方向与 Quaternion.Euler 绕 Z 轴正角一致,无需取反。
            pitchDeg = Mathf.Atan2(rearY - frontY, _wheelbase) * Mathf.Rad2Deg;
            rollDeg  = Mathf.Atan2(rightY - leftY, _trackWidth) * Mathf.Rad2Deg;

            return true;
        }

        /// <summary>
        /// 单点向下「球体扫描」检测地表高度。排除载具自身(车身/轮子等子碰撞体),
        /// 取最高命中点作为地表。
        ///
        /// 与旧版 RaycastAll 点采样相比,这里用半径 ≈ 轮子半径的球体向下扫描:
        ///   • 球体无法穿过宽度小于其直径的缝隙/裂缝 → 会「骑」在缝隙边缘,而不是掉进缝底,
        ///     从根本上避免车身被细小缝隙「吞」进去卡住或姿态剧烈跳变。
        ///   • 返回的是球底接触面的高度(hit.point.y - radius),语义等价于旧版点采样地表高度,
        ///     因此 newPos.y = groundY + groundOffset 的贴地公式无需改动。
        /// </summary>
        private bool SampleGroundHeight(Vector3 worldPos, out float groundY)
        {
            groundY = worldPos.y;
            Vector3 origin = worldPos + Vector3.up * groundCheckHeight;
            float radius = _groundSampleRadius;

            RaycastHit[] hits = Physics.SphereCastAll(
                origin, radius, Vector3.down, groundCheckHeight * 2f,
                groundLayerMask, QueryTriggerInteraction.Ignore);

            float bestY = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                // 排除载具自身碰撞体:根物体上的 MeshCollider(车身) + 轮子 SphereCollider 等子碰撞体
                if (h.collider.transform == transform || h.collider.transform.IsChildOf(transform)) continue;
                // SphereCast 的 hit.point 是「球表面与地面的接触点」(向下扫描即球底触地点),
                // 其 y 直接就是地表高度,无需再 ±radius。
                // (历史教训:曾误写 +radius 导致悬空、-radius 导致陷地,实际二者都不该加。)
                float surfaceY = h.point.y;
                if (surfaceY > bestY)
                {
                    bestY = surfaceY;
                    found = true;
                }
            }

            if (found) groundY = bestY;
            return found;
        }

        /// <summary>
        /// 判断载具是否在水平(XZ)投影上与障碍物包围盒重叠。
        /// 越障期间用于判断是否已越过障碍:重叠时持续抬升,水平脱离后回落。
        /// </summary>
        private bool CarOverlapsObstacleXZ(Collider obstacle)
        {
            if (obstacle == null) return false;
            Bounds ob = obstacle.bounds;
            Bounds car = GetCarBoundsXZ();
            return car.min.x < ob.max.x && car.max.x > ob.min.x &&
                   car.min.z < ob.max.z && car.max.z > ob.min.z;
        }

        /// <summary>
        /// 载具所有碰撞体合并后的轴对齐包围盒(用于水平重叠判断,仅用 x/z)。
        /// </summary>
        private Bounds GetCarBoundsXZ()
        {
            if (_carColliders == null || _carColliders.Length == 0)
                return new Bounds(_rb.position, Vector3.one);

            Bounds b = new Bounds(_rb.position, Vector3.zero);
            bool first = true;
            for (int i = 0; i < _carColliders.Length; i++)
            {
                var c = _carColliders[i];
                if (c == null || c.isTrigger) continue;
                if (first) { b = c.bounds; first = false; }
                else b.Encapsulate(c.bounds);
            }
            return first ? new Bounds(_rb.position, Vector3.one) : b;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0.8f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
