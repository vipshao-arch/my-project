using UnityEngine;
using Game.SkillSystem;

namespace Game.Character
{
    /// <summary>
    /// 输入处理器。复刻 Invector vThirdPersonInput。
    ///
    /// 按键映射：
    ///   WASD / 方向键 — 移动
    ///   Space         — 跳跃
    ///   Q             — 翻滚
    ///   LeftShift     — 冲刺（按住）
    ///   C             — 蹲下（切换）
    ///   Tab           — Strafe 切换
    ///   鼠标右键      — 点击地面移动（Click & Move）
    ///   滚轮          — 缩放摄像机（由 TopdownCameraController 处理）
    ///
    /// 执行时序：
    ///   LateUpdate → InputHandle
    ///   FixedUpdate → AirControl + CameraInput + 点击移动
    /// </summary>
    [AddComponentMenu("Game/Character System/Character Input Handler")]
    public class CharacterInputHandler : MonoBehaviour
    {
        [Header("Lock")]
        public bool lockInput;
        public bool lockCamera;

        [Header("Keys")]
        public KeyCode jumpKey   = KeyCode.Space;
        public KeyCode rollKey   = KeyCode.Q;
        public KeyCode sprintKey = KeyCode.LeftShift;
        public KeyCode crouchKey = KeyCode.C;
        public KeyCode strafeKey = KeyCode.Tab;

        [Header("Click To Move")]
        public int       clickMoveMouseButton  = 1;
        public LayerMask clickMoveLayer        = 1 << 0;
        public float     clickMoveArrivalDist  = 0.5f;
        public bool      clickMoveAutoSprint   = false;
        public float     clickMoveSprintDist   = 8f;
        [Tooltip("拖拽判定阈值(像素):按下后鼠标移动超过此值视为拖拽旋转,松手不触发点击移动;低于此值松手=点击移动。")]
        public float     clickMoveDragThreshold = 6f;

        // 内部引用
        private CharacterMotor           _motor;
        private SkillController          _skillController;
        private TopdownCameraController  _tpCamera;
        private Camera                   _mainCamera;  // 缓存主摄像机，避免每帧 Camera.main 查找
        private Vector2                  _lastLoggedInput = new Vector2(float.MinValue, float.MinValue); // 诊断去抖

        // 点击移动状态
        private bool    _isClickMoving;
        private Vector3 _clickMoveTarget;
        private bool    _savedRotateByWorld;

        // 右键「拖拽旋转 vs 单击移动」判定状态(2026-09-08)
        private bool    _clickMovePressed;
        private Vector2 _clickMovePressPos;
        private bool    _clickMoveDragged;

        private ICharacterInputSource _inputSource;   // 输入源(S2-0 收口):本地=LocalInputSource,远端=NullInputSource

        void Awake()
        {
            _motor           = GetComponent<CharacterMotor>();
            _skillController = GetComponent<SkillController>();
            _mainCamera      = Camera.main;
            _inputSource     = CharacterInputSourceResolver.Resolve(this) ?? LocalInputSource.Shared;
        }

        private ICharacterInputSource InputSource
            => _inputSource ?? (_inputSource = LocalInputSource.Shared);

        void Start()
        {
            if (_tpCamera == null)
            {
                _tpCamera = FindObjectOfType<TopdownCameraController>();
                if (_tpCamera != null && _tpCamera.target != transform)
                    _tpCamera.SetTarget(transform);
            }
        }

        void LateUpdate()
        {
            if (_motor == null || lockInput || Time.timeScale == 0) return;

            // lockMovement 只拦截移动/蹲下/冲刺/视角，不拦截跳跃和翻滚
            bool allowCombatMove = _skillController != null && _skillController.IsMovingAttackActive;
            if (!_motor.lockMovement || allowCombatMove)
            {
                MoveCharacter();
                SprintInput();
                StrafeInput();
            }

            // Crouch 是姿态切换，不应被普通移动 lockMovement（如攻击收招/落地缓冲）吞掉；
            // CharacterMotor 自身仍会拒绝翻越、梯子、翻滚和离地状态。
            CrouchInput();
            JumpInput();
            RollInput();
        }

        void FixedUpdate()
        {
            if (_motor == null) return;
            // AirControl 已移入 CharacterMotor.FixedUpdate，此处不再调用
            CameraInput();
            if (_isClickMoving) MoveToPoint();
        }

        void Update()
        {
            HandleClickMoveInput();
        }

        // 右键：按住拖拽=旋转(相机处理)；按下→松手(未超过阈值)=点击移动。
        private void HandleClickMoveInput()
        {
            if (InputSource.GetMouseButtonDown(clickMoveMouseButton))
            {
                _clickMovePressed  = true;
                _clickMoveDragged  = false;
                _clickMovePressPos = InputSource.MousePosition;
            }

            if (_clickMovePressed && InputSource.GetMouseButton(clickMoveMouseButton))
            {
                if (!_clickMoveDragged &&
                    ((Vector2)InputSource.MousePosition - _clickMovePressPos).magnitude >= clickMoveDragThreshold)
                    _clickMoveDragged = true;
            }

            if (InputSource.GetMouseButtonUp(clickMoveMouseButton))
            {
                _clickMovePressed = false;
                if (!_clickMoveDragged &&
                    (_skillController == null || !_skillController.InCombatMode))
                    TryStartClickMove();
            }
        }

        private void InputHandle()
        {
            MoveCharacter();
            SprintInput();
            CrouchInput();
            StrafeInput();
            JumpInput();
            RollInput();
        }

        private void MoveCharacter()
        {
            if (_isClickMoving)
            {
                float h = InputSource.MoveAxesRaw.x;
                float v = InputSource.MoveAxesRaw.y;
                if (Mathf.Abs(h) > 0.05f || Mathf.Abs(v) > 0.05f)
                {
                    CancelClickMove();
                    // CancelClickMove 后立刻写入 WASD 输入，不 return
                    _motor.input = new Vector2(h, v);
                }
                return;
            }

            if (_skillController != null && _skillController.InCombatMode)
                RemapCombatInput();
            else
                ReadRawInput();
        }

        private void ReadRawInput()
        {
            float h = InputSource.MoveAxesRaw.x;
            float v = InputSource.MoveAxesRaw.y;
            _motor.input = new Vector2(h, v);
        }

        private void RemapCombatInput()
        {
            // 攻击朝向系移动(W跟枪口):_motor.input 是机体坐标。
            //   input.y=+1 → 沿机体 forward(枪口/瞄准方向)前进 = 追击
            //   input.y=-1 → 后退(背离枪口) = 边退边打
            //   input.x=±1 → 侧移
            // 移动方向与射击方向始终一致;根朝向由 SkillController 瞬切对准瞄准点(快速面向攻击方向),
            // 下半身走位动画 InputVertical/InputHorizontal 直接取 input.y/x 并用极短阻尼实时响应,
            // 输入与攻击同向(W)=追击、反向(S)=边退边打,融合状态快速切换。
            float dh = InputSource.MoveAxesRaw.x;
            float dv = InputSource.MoveAxesRaw.y;

            _motor.input = new Vector2(
                Mathf.Clamp(dh, -1f, 1f),
                Mathf.Clamp(dv, -1f, 1f));

            if (_skillController.debugAttackFacing && _motor.input != _lastLoggedInput)
            {
                _lastLoggedInput = _motor.input;
                Debug.Log($"[AttackFacing] 攻击朝向系直接映射: raw=({dh},{dv}) → motor.input={_motor.input}");
            }
        }

        private void SprintInput()
        {
            // 用 GetKey 持续检测而非 GetKeyDown/Up：
            // 避免松键那帧恰好被 lockMovement 拦截导致 GetKeyUp 永远错过、isSprinting 卡死。
            _motor.Sprint(InputSource.GetKey(sprintKey));
        }

        private void CrouchInput()
        {
            if (InputSource.GetKeyDown(crouchKey)) _motor.Crouch();
        }

        private void StrafeInput()
        {
            if (InputSource.GetKeyDown(strafeKey)) _motor.isStrafing = !_motor.isStrafing;
        }

        private void JumpInput()
        {
            if (InputSource.GetKeyDown(jumpKey))
            {
                if (_isClickMoving) CancelClickMove();
                _skillController?.InterruptForAction();
                _motor.Jump();
            }
        }

        private void RollInput()
        {
            if (InputSource.GetKeyDown(rollKey))
            {
                if (_isClickMoving) CancelClickMove();
                _skillController?.InterruptForAction();
                _motor.Roll();
            }
        }

        private void CameraInput()
        {
            var cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return;
            _motor.UpdateTargetDirection(cam.transform);
            RotateWithCamera(cam.transform);
        }

        private void RotateWithCamera(Transform cameraTransform)
        {
            if (_motor.isStrafing && !_motor.actions && !_motor.lockMovement && !_motor.lockRotation)
            {
                if (_motor.input != Vector2.zero)
                {
                    var newRotation = new Vector3(
                        transform.eulerAngles.x,
                        cameraTransform.eulerAngles.y,
                        transform.eulerAngles.z);
                    transform.rotation = Quaternion.Lerp(
                        transform.rotation,
                        Quaternion.Euler(newRotation),
                        _motor.strafeRotationSpeed * Time.fixedDeltaTime);
                }
            }
        }

        public void TryStartClickMove()
        {
            var cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null || _motor == null) return;

            Ray ray = cam.ScreenPointToRay(InputSource.MousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, clickMoveLayer))
            {
                _clickMoveTarget = hit.point;
                if (!_isClickMoving)
                {
                    _isClickMoving       = true;
                    _savedRotateByWorld  = _motor.rotateByWorld;
                    _motor.rotateByWorld = true;
                }
                if (_skillController != null && _skillController.InCombatMode)
                    _skillController.ExitCombatMode();
            }
        }

        public void CancelClickMove()
        {
            if (!_isClickMoving) return;
            _isClickMoving       = false;
            _motor.rotateByWorld = _savedRotateByWorld;
            _motor.Sprint(false);
        }

        private void MoveToPoint()
        {
            Vector3 dir = _clickMoveTarget - transform.position;
            dir.y = 0f;

            float dist = new Vector3(
                _clickMoveTarget.x - transform.position.x, 0,
                _clickMoveTarget.z - transform.position.z).magnitude;

            if (dist <= clickMoveArrivalDist)
            {
                _motor.input = Vector2.Lerp(_motor.input, Vector2.zero, 20f * Time.deltaTime);
                CancelClickMove();
                return;
            }

            dir.Normalize();
            _motor.input = new Vector2(dir.x, dir.z);

            if (clickMoveAutoSprint && dist > clickMoveSprintDist)
                _motor.Sprint(true);
            else
                _motor.Sprint(false);
        }

        public bool IsClickMoving => _isClickMoving;
    }
}
