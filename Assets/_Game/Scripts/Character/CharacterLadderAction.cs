using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 梯子动作控制器（简化版）。
    ///
    /// ─── 设计（2026-08-25 简化：去掉 Enter/Exit 衔接动画，直接进入爬梯）───
    ///   状态机只有两态：Idle ↔ Climbing，不再有 Entering / Exiting。
    ///   • 进入：碰到入口触发器（朝向 + 移动方向正确）→ 直接吸附到挂梯轴、
    ///           摆正朝向、播 ClimbLadder，立即进入爬梯。
    ///   • 爬梯：Y = InputVertical × climbSpeed × dt；XZ 沿上下 matchTarget 插值锁定。
    ///   • 退出：到达顶部/底部边界，或按退梯键 → 直接放到落点（触发器位置）、回 Idle。
    ///
    ///   攀爬朝向唯一权威 = 梯子根节点 forward（GetLadderFacing），记作 F；
    ///   全部由 matchTargetTop/matchTargetBottom 世界坐标 + 梯子根节点朝向推导，
    ///   不写死坐标/方向，适配任意摆放位置/朝向/高度的梯子。
    ///
    /// ─── 触发器结构 ───
    ///   ladder (root)
    ///     ├─ ExitLadderTop     (trigger, exitAnimation=ExitLadderTop, matchTargetTop 子节点)
    ///     └─ EnterLadderBottom (trigger, exitAnimation=ExitLadderBottom, matchTargetBottom 子节点)
    /// </summary>
    [AddComponentMenu("Game/Character System/Character Ladder Action")]
    [DefaultExecutionOrder(200)] // 晚于 Motor(100)，确保 InputVertical 不被 Motor 覆盖
    public class CharacterLadderAction : MonoBehaviour, IHittable
    {
        // ─── Inspector ──────────────────────────────────────────────────

        [Header("Input Keys")]
        public KeyCode enterInput = KeyCode.E;
        public KeyCode exitInput  = KeyCode.Space;

        [Header("Settings")]
        public string ladderTag = "LadderTrigger";
        public bool   debugMode = false;

        [Header("Climb Drive")]
        [Tooltip("攀爬速度(m/s)，InputVertical(-1~1) × 此值 = 实际 Y 移动速度。")]
        public float climbSpeed = 2.5f;
        [Tooltip("进梯过渡时长(s)：位置/朝向从当前点平滑插值到挂梯轴，消除进梯瞬移跳变。")]
        public float enterTransitionDuration = 0.13f;
        [Tooltip("出梯过渡时长(s)：位置从挂梯点平滑插值到落点，消除出梯瞬移跳变。")]
        public float exitTransitionDuration = 0.15f;

        [Header("Component Hooks")]
        [Tooltip("输入参考系：指定后 motor.input 按该参考系转世界方向；留空=回退主相机。")]
        public Transform inputReferenceFrame;
        [Tooltip("勾选后 motor.input 视为世界系方向，不做相机变换（AI 驱动用）。")]
        public bool inputIsWorldSpace = false;

        [Header("Debug")]
        public string debugState    = "Idle";
        public string debugTrigger  = "none";

        // ─── 内部状态 ────────────────────────────────────────────────────

        private enum LadderState { Idle, Entering, Climbing, Exiting }

        private LadderState _state = LadderState.Idle;

        // 当前接触/选中的触发器；Climbing 期间保持不变。
        private CharacterLadderTrigger _activeTrigger;
        // 退梯后屏蔽同一个触发器，直到角色真正离开其 Collider + 松开纵向输入。
        private CharacterLadderTrigger _blockedReentryTrigger;
        private float _reentryBlockTimer;
        private bool  _mustReleaseVerticalInput;
        // 当前梯子根节点；边界/落点触发器必须限定在同一梯子内。
        private Transform _activeLadderRoot;
        // 退梯后冷却，防止立即重新进梯
        private float _exitCooldown;

        // ── 攀爬锁定 ──
        private Vector3 _lockedXZ;          // 无锚点时的兜底 XZ
        private Vector3 _ladderBottomAnchor;
        private Vector3 _ladderTopAnchor;
        private bool    _hasLadderAnchors;
        private Quaternion _lockedLadderRotation;
        private bool    _hasLockedLadderRotation;
        private float   _climbTime;         // 进入 Climbing 后累积时间

        // ── Y 轴边界（防止爬出梯子范围）──
        private float _ladderTopY    = float.MaxValue;
        private float _ladderBottomY = float.MinValue;
        private bool  _hasYBounds    = false;

        // ── 输入 ──
        private float _climbInput;          // 当前攀爬输入 (-1~1)

        // ── 进/出梯过渡（平滑插值，消除瞬移跳变）──
        private Vector3   _transitionStartPos;
        private Vector3   _transitionTargetPos;
        private Quaternion _transitionStartRot;
        private Quaternion _transitionTargetRot;
        private float     _transitionTimer;
        private float     _transitionDuration;
        private bool      _transitionHasRotation;

        // ── 物理状态快照 ──
        private bool  _wasKinematic;
        private bool  _physicsCaptured;

        // ── 组件缓存 ──
        private Animator              _anim;
        private CharacterMotor        _motor;
        private CharacterInputHandler _input;
        private Rigidbody             _rb;
        private CapsuleCollider       _col;
        private Camera                _mainCamera;
        private ICharacterInputSource _inputSource;

        // ── Animator 状态路径 ──
        private const string LadderClimbPath = "NormalState.Actions.Ladder.UsingLadder.ClimbLadder";
        private const string LocomotionDefaultPath = "NormalState.Locomotion.Free Locomotion.Free Locomotion";

        // Animator 参数
        private static readonly int H_InputVertical    = AnimatorParams.InputVertical;
        private static readonly int H_InputHorizontal  = AnimatorParams.InputHorizontal;
        private static readonly int H_VerticalVelocity = AnimatorParams.VerticalVelocity;
        private static readonly int H_ActionState      = AnimatorParams.ActionState;
        private static readonly int H_Falling          = AnimatorParams.Falling;

        // ─── Unity 生命周期 ─────────────────────────────────────────────

        void Awake()
        {
            _anim         = GetComponent<Animator>();
            _motor        = GetComponent<CharacterMotor>();
            _input        = GetComponent<CharacterInputHandler>();
            _rb           = GetComponent<Rigidbody>();
            _col          = GetComponent<CapsuleCollider>();
            _mainCamera   = Camera.main;
            _inputSource  = CharacterInputSourceResolver.Resolve(this);
        }

        void Update()
        {
            // GetKeyDown 只在 Update 有效，提前捕获按键进梯
            if (_state == LadderState.Idle)
                TryEnterByKey();
        }

        void FixedUpdate()
        {
            // 物理帧持续锁定 kinematic，防止 FixedUpdate↔LateUpdate 间隙被重力拉落
            if (_state != LadderState.Idle && _rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity  = false;
            }
        }

        void LateUpdate()
        {
            if (_exitCooldown > 0f)
                _exitCooldown -= Time.deltaTime;
            if (_reentryBlockTimer > 0f)
                _reentryBlockTimer -= Time.deltaTime;

            Vector2 currentAxes = GetMoveAxes();
            if (_reentryBlockTimer <= 0f)
            {
                _blockedReentryTrigger = null;
                _mustReleaseVerticalInput = false;
            }
            _climbInput = Mathf.Clamp(currentAxes.y, -1f, 1f);

            switch (_state)
            {
                case LadderState.Idle:
                    TryAutoEnter();
                    break;
                case LadderState.Entering:
                    TickEntering();
                    break;
                case LadderState.Climbing:
                    TickClimbing();
                    break;
                case LadderState.Exiting:
                    TickExiting();
                    break;
            }

            UpdateDebugInfo();
        }

        // ─── 触发器检测 ─────────────────────────────────────────────────

        private bool TryGetLadderTrigger(Collider other, out CharacterLadderTrigger trigger)
        {
            trigger = null;
            if (other == null) return false;
            trigger = other.GetComponent<CharacterLadderTrigger>()
                   ?? other.GetComponentInParent<CharacterLadderTrigger>();
            return trigger != null;
        }

        private bool IsEntryContact(CharacterLadderTrigger trigger, Collider other)
        {
            if (trigger == null || other == null) return false;
            string childName = other.transform.name;
            if (childName == "stair_end")
                return trigger.playAnimation == "EnterLadderTop";
            if (childName == "stair_start")
                return trigger.playAnimation == "EnterLadderBottom";
            return trigger.isEntryTrigger;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!TryGetLadderTrigger(other, out var t)) return;
            if (t == _blockedReentryTrigger)
            {
                // 顶部退出后平台下梯的方向是向梯子/向下；底部退出后重新上梯方向是向上。
                if (_reentryBlockTimer > 0f) return;
                _blockedReentryTrigger = null;
                _mustReleaseVerticalInput = false;
            }
            if (_exitCooldown > 0f) return;
            if (_state != LadderState.Idle) return;
            if (!IsEntryContact(t, other)) return;
            if (t.activeFromForward && !IsFacingCorrect(t)) return;
            if (_activeTrigger != null && _activeTrigger != t) return;
            _activeTrigger = t;
            if (_activeLadderRoot == null)
                _activeLadderRoot = t.transform.root;
            if (_motor != null) _motor.inLadderZone = true;
        }

        void OnTriggerStay(Collider other)
        {
            if (!TryGetLadderTrigger(other, out var t)) return;
            if (t == _blockedReentryTrigger)
            {
                if (_reentryBlockTimer > 0f) return;
                _blockedReentryTrigger = null;
                _mustReleaseVerticalInput = false;
            }
            if (_exitCooldown > 0f) return;
            if (_state != LadderState.Idle) return;
            if (!IsEntryContact(t, other)) return;
            if (t.activeFromForward && !IsFacingCorrect(t)) return;
            if (_activeTrigger != null && _activeTrigger != t) return;
            _activeTrigger = t;
            if (_activeLadderRoot == null)
                _activeLadderRoot = t.transform.root;
            if (_motor != null) _motor.inLadderZone = true;
        }

        void OnTriggerExit(Collider other)
        {
            if (!TryGetLadderTrigger(other, out var t)) return;
            if (t == _blockedReentryTrigger)
            {
                _blockedReentryTrigger = null;
                _mustReleaseVerticalInput = false;
            }
            if (_state != LadderState.Idle) return;
            if (_activeTrigger == t)
            {
                _activeTrigger.OnPlayerExit.Invoke();
                _activeTrigger = null;
                if (_motor != null)
                    _motor.inLadderZone = false;
            }
        }

        // ─── 朝向检查 ───────────────────────────────────────────────────

        /// <summary>
        /// 判断角色朝向是否正确面朝梯子。
        /// fallback 基准必须是梯子根节点 forward（GetLadderFacing），不能用触发器自身
        /// transform.forward —— 触发器网格/Collider 摆放朝向由美术资产决定，可能与攀爬朝向相反。
        /// </summary>
        private bool IsFacingCorrect(CharacterLadderTrigger t)
        {
            var expected = t.enterForward.sqrMagnitude > 0.001f
                ? t.enterForward
                : GetLadderFacing(t);
            if (t.reverseEnterForward) expected = -expected;
            expected.y = 0f;

            float angle = Vector3.Angle(transform.forward, expected);
            bool ok = angle < 60f;

            if (debugMode)
                Debug.Log($"[Ladder:FaceCheck] '{t.name}' reverse={t.reverseEnterForward} " +
                          $"angle={angle:F1} ok={ok}");

            return ok;
        }

        // ─── 进梯 ───────────────────────────────────────────────────────

        /// <summary>按键进梯（Update 中调用）。</summary>
        private void TryEnterByKey()
        {
            if (_input == null) return;               // AI 驱动不读按键
            if (_exitCooldown > 0f) return;
            if (_activeTrigger == null) return;
            if (_activeTrigger.autoAction) return;     // autoAction 触发器不走按键
            if (_motor != null && _motor.customAction) return;
            if (_inputSource != null && _inputSource.GetKeyDown(enterInput))
                StartClimbing(_activeTrigger);
        }

        /// <summary>自动进梯（LateUpdate Idle 状态调用）。</summary>
        private void TryAutoEnter()
        {
            if (_exitCooldown > 0f || _mustReleaseVerticalInput) return;
            if (_activeTrigger == null || !_activeTrigger.autoAction) return;
            // 出梯保留的出口触发器仅在角色附近有效（防止远距离朝梯子推杆被误吸附）
            if (Vector3.Distance(transform.position, _activeTrigger.transform.position) > 2.5f)
            {
                _activeTrigger = null;
                _activeLadderRoot = null;
                return;
            }
            if (_motor == null || _motor.actions) return;
            Vector2 moveAxes = GetMoveAxes();
            if (moveAxes.sqrMagnitude < 0.0001f) return;
            if (_anim.IsInTransition(0)) return;

            // Falling 状态不进梯
            if (_anim.GetCurrentAnimatorStateInfo(0).shortNameHash == H_Falling) return;

            var inputDir = GetWorldInputDirection();
            if (inputDir.sqrMagnitude < 0.0001f) return;
            inputDir.y = 0f;
            inputDir.Normalize();

            Collider triggerCollider = _activeTrigger.GetComponent<Collider>();
            Vector3 approach = triggerCollider != null
                ? triggerCollider.ClosestPoint(transform.position) - transform.position
                : GetEntryFacing(_activeTrigger);
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.0001f)
                approach = GetEntryFacing(_activeTrigger);
            approach.Normalize();

            if (Vector3.Dot(inputDir, approach) > 0.2f)
                StartClimbing(_activeTrigger);
        }

        /// <summary>程序化进梯 API（AI/NavMesh Link 等外部驱动调用）。</summary>
        public void TryEnterLadder()
        {
            if (_activeTrigger != null) StartClimbing(_activeTrigger);
        }

        // ─── 状态切换：进入爬梯 ─────────────────────────────────────────

        private void StartClimbing(CharacterLadderTrigger trigger)
        {
            if (trigger == null || _state != LadderState.Idle) return;
            if (_exitCooldown > 0f || _mustReleaseVerticalInput) return;

            _activeTrigger = trigger;
            _activeLadderRoot = trigger.transform.root;
            _state = LadderState.Entering;
            _climbTime = 0f;
            _hasLockedLadderRotation = false;

            // 1. 计算上下挂梯锚点（matchTarget）与 Y 边界。
            ComputeYBounds();

            // 2. 计算挂梯目标位置：XZ 锁定锚点 XZ，Y 保持当前（clamp 到范围）。
            Vector3 pos = _rb != null ? _rb.position : transform.position;
            Vector3 axisXZ = _hasLadderAnchors
                ? new Vector3(_ladderBottomAnchor.x, 0f, _ladderBottomAnchor.z)
                : pos;
            Vector3 targetPos = pos;
            targetPos.x = axisXZ.x;
            targetPos.z = axisXZ.z;
            if (_hasYBounds)
                targetPos.y = Mathf.Clamp(targetPos.y, _ladderBottomY, _ladderTopY);
            _lockedXZ = new Vector3(targetPos.x, 0f, targetPos.z);

            // 3. 计算攀爬目标朝向 F。
            Vector3 facing = GetLadderFacing(trigger);
            _transitionHasRotation = facing.sqrMagnitude > 0.001f;
            Quaternion targetRot = _transitionHasRotation
                ? Quaternion.LookRotation(facing)
                : transform.rotation;
            _lockedLadderRotation = targetRot;
            _hasLockedLadderRotation = true;

            // 4. Motor 接管 + 清移动参数。
            if (_motor != null)
            {
                _motor.customAction = true;
                _motor.inLadderZone  = false;
                _motor.ForceGroundedAnimParams();
            }
            _anim.SetFloat(H_InputHorizontal, 0f);
            _anim.SetFloat(H_InputVertical,   0f);
            _anim.SetFloat(H_VerticalVelocity, 0f);
            _anim.SetInteger(H_ActionState, 1);

            // 5. 物理锁定 kinematic。
            CapturePhysics();
            if (_rb != null)
            {
                if (!_rb.isKinematic)
                {
                    _rb.velocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic   = true;
                _rb.useGravity    = false;
                _rb.interpolation  = RigidbodyInterpolation.None;
            }

            // 6. 播放爬梯动画（Play 直接激活子状态机状态；CrossFade 无法跨未激活子状态机）。
            //    位置/朝向由 TickEntering 平滑插值，消除瞬移跳变。
            _anim.updateMode = AnimatorUpdateMode.Normal;
            _anim.applyRootMotion = false;
            _anim.speed = 1f;
            PlayLadderState("ClimbLadder");
            _anim.Update(0f);

            _transitionStartPos  = pos;
            _transitionTargetPos = targetPos;
            _transitionStartRot  = transform.rotation;
            _transitionTargetRot = targetRot;
            _transitionTimer     = 0f;
            _transitionDuration  = Mathf.Max(0.01f, enterTransitionDuration);

            if (debugMode)
                Debug.Log($"[Ladder] StartClimbing: trigger='{trigger.name}' from={pos} to={targetPos}");
        }

        // ─── Tick: Entering（进梯过渡）──────────────────────────────────

        private void TickEntering()
        {
            LockPhysics();
            _transitionTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_transitionTimer / _transitionDuration);
            float e = t * t * (3f - 2f * t); // SmoothStep 缓动

            SetPosition(Vector3.Lerp(_transitionStartPos, _transitionTargetPos, e));

            if (_transitionHasRotation)
            {
                Quaternion r = Quaternion.Slerp(_transitionStartRot, _transitionTargetRot, e);
                transform.rotation = r;
                if (_rb != null) _rb.rotation = r;
            }

            if (t >= 1f)
            {
                SetPosition(_transitionTargetPos);
                if (_transitionHasRotation)
                {
                    transform.rotation = _transitionTargetRot;
                    if (_rb != null) _rb.rotation = _transitionTargetRot;
                }
                _lockedLadderRotation = _transitionTargetRot;
                _hasLockedLadderRotation = true;
                _climbTime = 0f;
                _state = LadderState.Climbing;
            }
        }

        // ─── Tick: Climbing ─────────────────────────────────────────────

        private void TickClimbing()
        {
            LockPhysics();
            if (_motor != null) _motor.ForceGroundedAnimParams();

            _climbTime += Time.deltaTime;

            // 攀爬朝向固定 = F。
            if (_hasLockedLadderRotation)
            {
                transform.rotation = _lockedLadderRotation;
                if (_rb != null) _rb.rotation = _lockedLadderRotation;
            }

            Vector3 currentPos = _rb != null ? _rb.position : transform.position;
            float nextY = currentPos.y + _climbInput * climbSpeed * Time.deltaTime;

            // 到达顶部/底部边界且继续向对应方向 → 直接退梯。
            if (_hasYBounds && _climbTime > 0.3f)
            {
                if (_climbInput > 0.05f && nextY >= _ladderTopY)
                {
                    ExitLadder(top: true);
                    return;
                }
                if (_climbInput < -0.05f && nextY <= _ladderBottomY)
                {
                    ExitLadder(top: false);
                    return;
                }
            }

            Vector3 pos = currentPos;
            pos.y = _hasYBounds
                ? Mathf.Clamp(nextY, _ladderBottomY, _ladderTopY)
                : nextY;
            if (_hasLadderAnchors)
            {
                float t = Mathf.InverseLerp(_ladderBottomAnchor.y, _ladderTopAnchor.y, pos.y);
                Vector3 axis = Vector3.Lerp(_ladderBottomAnchor, _ladderTopAnchor, t);
                pos.x = axis.x;
                pos.z = axis.z;
            }
            else
            {
                pos.x = _lockedXZ.x;
                pos.z = _lockedXZ.z;
            }

            _anim.SetFloat(H_InputVertical, _climbInput);
            SetPosition(pos);

            // 退梯键（Space）：按当前高度就近选择落点触发器。
            if (GetExitInputDown() && _climbTime > 0.3f)
            {
                var exitTrigger = FindNearestExitTrigger();
                if (exitTrigger != null)
                    ExitLadderTo(exitTrigger);
                else
                    ForceExitLadder();
                return;
            }

            if (debugMode && Mathf.Abs(_climbInput) > 0.01f)
                Debug.Log($"[Ladder:Climb] Y={pos.y:F3} input={_climbInput:F2} climbTime={_climbTime:F1}");
        }

        // ─── 退出爬梯 ───────────────────────────────────────────────────

        /// <summary>到达顶部(top=true)/底部(top=false)边界退梯。</summary>
        private void ExitLadder(bool top)
        {
            var t = FindTriggerByExitAnim(top ? "ExitLadderTop" : "ExitLadderBottom");
            if (t != null)
            {
                ExitLadderTo(t);
                return;
            }
            ForceExitLadder();
        }

        /// <summary>退到指定触发器的落点（触发器自身位置 = 平台/地面侧）。</summary>
        private void ExitLadderTo(CharacterLadderTrigger trigger)
        {
            Vector3 landing = trigger != null
                ? GetLandingAnchor(trigger)
                : (_rb != null ? _rb.position : transform.position);

            _blockedReentryTrigger = trigger;

            // 启动出梯过渡：位置平滑插值到落点；动画先淡出，结束后 ResetPlayerSettings 恢复物理。
            _transitionStartPos  = _rb != null ? _rb.position : transform.position;
            _transitionTargetPos = landing;
            _transitionTimer     = 0f;
            _transitionDuration  = Mathf.Max(0.01f, exitTransitionDuration);
            _state = LadderState.Exiting;
        }

        /// <summary>无触发器时强制退出梯子。</summary>
        private void ForceExitLadder()
        {
            _blockedReentryTrigger = _activeTrigger;
            ResetPlayerSettings();
        }

        // ─── Tick: Exiting（出梯过渡）──────────────────────────────────

        private void TickExiting()
        {
            LockPhysics();
            _transitionTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_transitionTimer / _transitionDuration);
            float e = t * t * (3f - 2f * t); // SmoothStep 缓动

            SetPosition(Vector3.Lerp(_transitionStartPos, _transitionTargetPos, e));

            if (t >= 1f)
                ResetPlayerSettings(keepTrigger: true);
        }

        // ─── 锚点 / 落点 ─────────────────────────────────────────────────

        private Vector3 GetMatchAnchor(CharacterLadderTrigger trigger)
        {
            if (trigger == null)
                return _rb != null ? _rb.position : transform.position;

            Vector3 anchor = trigger.matchTarget != null
                ? trigger.matchTarget.position
                : trigger.transform.position;
            anchor.y += trigger.endpointHeightOffset;
            return anchor;
        }

        /// <summary>落点锚点：出梯后角色的站位。
        /// XZ 取挂梯锚点(matchTarget)的 XZ（挂梯侧），Y 取触发器（平台/地面高度）。
        /// 这样退梯落点与爬梯 XZ 一致，消除退梯瞬间的水平跳变。</summary>
        private Vector3 GetLandingAnchor(CharacterLadderTrigger trigger)
        {
            if (trigger == null)
                return _rb != null ? _rb.position : transform.position;

            Vector3 anchor = trigger.matchTarget != null
                ? trigger.matchTarget.position
                : trigger.transform.position;
            anchor.y = trigger.transform.position.y + trigger.endpointHeightOffset;
            return anchor;
        }

        /// <summary>按当前梯子根节点下的 exitAnimation 查找触发器。</summary>
        private CharacterLadderTrigger FindTriggerByExitAnim(string exitAnim)
        {
            if (_activeLadderRoot == null) return null;
            CharacterLadderTrigger best = null;
            float bestDistance = float.MaxValue;
            Vector3 pos = _rb != null ? _rb.position : transform.position;
            foreach (var t in _activeLadderRoot.GetComponentsInChildren<CharacterLadderTrigger>(true))
            {
                if (t == null || t.exitAnimation != exitAnim) continue;
                Collider col = t.GetComponent<Collider>();
                Vector3 point = col != null ? col.ClosestPoint(pos) : t.transform.position;
                float distance = (point - pos).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>按当前高度就近选择出口触发器（退梯键用）。</summary>
        private CharacterLadderTrigger FindNearestExitTrigger()
        {
            if (_activeLadderRoot == null) return _activeTrigger;
            CharacterLadderTrigger best = null;
            float bestDist = float.MaxValue;
            float posY = _rb != null ? _rb.position.y : transform.position.y;
            foreach (var t in _activeLadderRoot.GetComponentsInChildren<CharacterLadderTrigger>(true))
            {
                if (t == null || !t.isExitTrigger) continue;
                float dist = Mathf.Abs(GetMatchAnchor(t).y - posY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = t;
                }
            }
            return best ?? _activeTrigger;
        }

        /// <summary>从当前梯子根节点下的上下 matchTarget 高度轴推导边界。</summary>
        private void ComputeYBounds()
        {
            if (_activeLadderRoot == null)
            {
                _hasYBounds = false;
                _hasLadderAnchors = false;
                return;
            }

            float topY = float.MinValue;
            float botY = float.MaxValue;
            CharacterLadderTrigger topTrigger = null;
            CharacterLadderTrigger bottomTrigger = null;
            foreach (var t in _activeLadderRoot.GetComponentsInChildren<CharacterLadderTrigger>(true))
            {
                if (t == null) continue;
                float y = GetMatchAnchor(t).y;
                if (t.isExitTrigger && t.exitAnimation == "ExitLadderTop" && y > topY)
                {
                    topY = y;
                    topTrigger = t;
                }
                else if (t.isExitTrigger && t.exitAnimation == "ExitLadderBottom" && y < botY)
                {
                    botY = y;
                    bottomTrigger = t;
                }
            }

            _hasLadderAnchors = topTrigger != null && bottomTrigger != null;
            _hasYBounds = _hasLadderAnchors && topY > botY + 0.01f;
            if (_hasLadderAnchors)
            {
                _ladderTopAnchor = GetMatchAnchor(topTrigger);
                _ladderBottomAnchor = GetMatchAnchor(bottomTrigger);
                _ladderTopY = _ladderTopAnchor.y;
                _ladderBottomY = _ladderBottomAnchor.y;
                _lockedXZ = new Vector3(_ladderBottomAnchor.x, 0f, _ladderBottomAnchor.z);
                if (debugMode)
                    Debug.Log($"[Ladder:Anchors] bottom={_ladderBottomAnchor} top={_ladderTopAnchor}");
            }
        }

        // ─── 重置 ───────────────────────────────────────────────────────

        private void ResetPlayerSettings(bool keepTrigger = false)
        {
            _state = LadderState.Idle;
            _exitCooldown = 0.3f;
            _mustReleaseVerticalInput = true;
            _climbTime = 0f;
            _hasYBounds = false;
            _hasLadderAnchors = false;
            _hasLockedLadderRotation = false;
            _reentryBlockTimer = 0.25f;

            // Animator 恢复
            _anim.speed = 1f;
            _anim.applyRootMotion = false;
            _anim.updateMode = AnimatorUpdateMode.AnimatePhysics;
            _anim.SetInteger(H_ActionState, 0);
            PlayLocomotionDefault();

            // Collider 恢复
            if (_col != null) _col.enabled = true;

            // Rigidbody 恢复：先恢复非 kinematic，再设置 velocity
            if (_rb != null)
            {
                bool wasKinematicOriginal = _physicsCaptured ? _wasKinematic : false;
                _rb.isKinematic = wasKinematicOriginal;
                _rb.useGravity = true;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
                _physicsCaptured = false;

                if (!wasKinematicOriginal)
                {
                    if (_motor == null || !_motor.isKnockedBack)
                        _rb.velocity = Vector3.zero;
                }
            }

            // Motor 恢复
            if (_motor != null)
            {
                _motor.customAction = false;
                _motor.inLadderZone  = false;
                _motor.input = Vector2.zero;  // 清除残留输入，防止退梯后 stale input 触发循环重进
            }

            // 输入恢复
            if (_input != null) _input.enabled = true;

            // 朝向归正
            transform.eulerAngles = new Vector3(0f, transform.eulerAngles.y, 0f);

            // 清理触发器引用（出梯路径保留出口触发器：冷却后玩家朝梯子推杆可直接重新进梯，
            // 不依赖"出梯落点与入口 Collider 物理重叠"这一脆弱条件）
            if (keepTrigger && _activeTrigger != null)
            {
                _activeLadderRoot = _activeTrigger.transform.root;
            }
            else
            {
                _activeTrigger?.OnPlayerExit.Invoke();
                _activeTrigger = null;
                _activeLadderRoot = null;
            }

            if (debugMode) Debug.Log("[Ladder] ResetPlayerSettings → Idle");
        }

        // ─── 辅助方法 ───────────────────────────────────────────────────

        private void CapturePhysics()
        {
            if (_rb == null || _physicsCaptured) return;
            _wasKinematic = _rb.isKinematic;
            _physicsCaptured = true;
        }

        private void LockPhysics()
        {
            if (_motor != null && !_motor.customAction)
                _motor.customAction = true;
            if (_rb != null)
            {
                if (!_physicsCaptured) CapturePhysics();
                _rb.isKinematic = true;
                _rb.useGravity  = false;
                _rb.interpolation = RigidbodyInterpolation.None;
            }
        }

        private void SetPosition(Vector3 pos)
        {
            if (_rb != null)
            {
                _rb.position = pos;
                transform.position = pos;
            }
            else
            {
                transform.position = pos;
            }
        }

        /// <summary>入口所需朝向。</summary>
        private Vector3 GetEntryFacing(CharacterLadderTrigger t)
        {
            if (t == null) return transform.forward;
            Vector3 fwd = t.enterForward.sqrMagnitude > 0.001f
                ? t.enterForward
                : GetLadderFacing(t);
            if (t.reverseEnterForward) fwd = -fwd;
            fwd.y = 0f;
            return fwd.sqrMagnitude > 0.001f ? fwd.normalized : transform.forward;
        }

        /// <summary>
        /// 攀爬朝向 = 从挂梯锚点(matchTarget)指向梯子竖杆/横杆中心(触发器父节点)的水平方向。
        /// 这是几何确定的真相：玩家身体贴在挂梯点上、面向横杆，方向 = 横杆中心 - 挂梯点。
        /// </summary>
        private Vector3 GetLadderFacing(CharacterLadderTrigger trigger)
        {
            if (trigger == null) return transform.forward;
            Vector3 anchor = GetMatchAnchor(trigger);
            Vector3 center = trigger.transform.parent != null
                ? trigger.transform.parent.position
                : trigger.transform.position;
            Vector3 facing = center - anchor;
            facing.y = 0f;
            return facing.sqrMagnitude > 0.001f ? facing.normalized : transform.forward;
        }

        private void UpdateDebugInfo()
        {
            debugState   = _state.ToString();
            debugTrigger = _activeTrigger != null ? _activeTrigger.name : "none";
        }

        private bool PlayLadderState(string stateName)
        {
            string path = stateName switch
            {
                "ClimbLadder" => LadderClimbPath,
                _ => null
            };
            if (string.IsNullOrEmpty(path)) return false;

            int hash = Animator.StringToHash(path);
            if (_anim.HasState(0, hash))
            {
                _anim.Play(hash, 0, 0f);
                return true;
            }

            int shortHash = Animator.StringToHash(stateName);
            if (_anim.HasState(0, shortHash))
            {
                _anim.Play(shortHash, 0, 0f);
                return true;
            }

            Debug.LogError($"[Ladder] Animator 状态不存在: {path} / {stateName}");
            return false;
        }

        /// <summary>出梯时强制回到根状态机默认状态（Free Locomotion），绕开失效的 exit transition。</summary>
        private void PlayLocomotionDefault()
        {
            if (_anim == null) return;
            int hash = Animator.StringToHash(LocomotionDefaultPath);
            if (_anim.HasState(0, hash))
            {
                _anim.CrossFadeInFixedTime(hash, exitTransitionDuration, 0);
                return;
            }
            int shortHash = Animator.StringToHash("Free Locomotion");
            if (_anim.HasState(0, shortHash))
            {
                _anim.CrossFadeInFixedTime(shortHash, exitTransitionDuration, 0);
                return;
            }
            Debug.LogWarning("[Ladder] 未找到 Free Locomotion 状态，出梯动画回退失败");
        }

        // ─── 输入 ───────────────────────────────────────────────────────

        private Vector2 GetMoveAxes()
        {
            if (_inputSource != null)
                return _inputSource.MoveAxes;
            return _motor != null ? _motor.input : Vector2.zero;
        }

        /// <summary>输入方向 → 世界系。</summary>
        protected virtual Vector3 GetWorldInputDirection()
        {
            Vector2 axes = GetMoveAxes();
            Vector3 local = new Vector3(axes.x, 0f, axes.y);
            if (inputIsWorldSpace)
                return local;
            if (inputReferenceFrame != null)
            {
                var d = inputReferenceFrame.TransformDirection(local);
                d.y = 0f;
                return d;
            }
            var cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return local;
            var dir = cam.transform.TransformDirection(local);
            dir.y = 0f;
            return dir;
        }

        /// <summary>攀爬输入(垂直)。可重写以接入 AI/网络驱动。</summary>
        protected virtual float GetClimbInput() => _inputSource != null ? _inputSource.MoveAxes.y : 0f;

        /// <summary>退梯按键。可重写以接入 AI/网络驱动。</summary>
        protected virtual bool GetExitInputDown() => _inputSource != null && _inputSource.GetKeyDown(exitInput);

        // ─── 公共 API ───────────────────────────────────────────────────

        public bool IsUsingLadder => _state != LadderState.Idle;

        /// <summary>外部击打时强制退出梯子，恢复正常物理，再施加冲击力。</summary>
        public void ForceExitWithKnockback(Vector3 impulse)
        {
            if (_state == LadderState.Idle) return;
            string fallingState = _motor != null ? _motor.StateNames.falling : "Falling";
            _anim.CrossFadeInFixedTime(fallingState, 0.1f, 0);
            ResetPlayerSettings();
            _motor?.ApplyKnockbackImpulse(impulse, 0.6f);
        }

        // IHittable：梯子状态下走 ForceExit，否则委托 Motor
        public void ReceiveKnockback(Vector3 impulse, float duration = 0.5f)
        {
            if (_state != LadderState.Idle)
                ForceExitWithKnockback(impulse);
            else
                _motor?.ApplyKnockbackImpulse(impulse, duration);
        }
    }
}
