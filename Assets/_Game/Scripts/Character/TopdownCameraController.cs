using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// Topdown 俯视角摄像机控制器。
    /// 俯角跟随 + 滚轮缩放 + 右键拖拽旋转(yaw)/俯仰(pitch)。
    /// </summary>
    [AddComponentMenu("Game/Character System/Topdown Camera Controller")]
    public class TopdownCameraController : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;

        [Header("Distance")]
        public float defaultDistance = 12f;
        public float minDistance     = 4f;
        public float maxDistance     = 15f;
        public float scrollSpeed     = 10f;

        [Header("Angle (初始俯角)")]
        [Range(10f, 85f)]
        [Tooltip("初始/默认俯仰角，0=水平，90=正上方俯视。运行时按住右键上下拖拽可动态调整(范围见 Mouse Drag Rotate 分区的 minPitch/maxPitch)。")]
        public float topdownAngle = 60f;

        [Header("Field of View (画面广角)")]
        [Tooltip("摄像机垂直视场角(度)。俯视角 ARPG 常用 40~60；越大越广角、越小越聚焦。")]
        [Range(20f, 100f)]
        public float fieldOfView = 60f;
        [Tooltip("滚轮缩放时是否联动 FOV：缩近聚焦、拉远广角。默认关，保持原手感。")]
        public bool scaleFovWithZoom = false;
        [Tooltip("距离最近(minDistance)时的 FOV(聚焦)")]
        [Range(20f, 100f)]
        public float fovAtMinDistance = 45f;
        [Tooltip("距离最远(maxDistance)时的 FOV(广角)")]
        [Range(20f, 100f)]
        public float fovAtMaxDistance = 70f;

        [Header("Follow")]
        public float smoothFollowSpeed = 10f;

        [Header("Target Offset (角色相对画面中心的偏移)")]
        [Tooltip("相对跟随焦点的世界空间偏移(米)。Y 增大 → 焦点上移 → 角色在画面中向下偏移(角色出现在画面中心下方),给角色上方留出更多视野。\n"
               + "X/Z 为世界水平偏移,会随镜头 yaw 旋转而摆动;若只需\"角色下移\"效果,仅调 Y 即可。")]
        public Vector3 targetOffset = Vector3.zero;

        [Header("Vertical Follow (立体场景,2026-07-30)")]
        [Tooltip("垂直跟随速度(独立于水平)。越小攀爬/上下楼时画面越稳;0=垂直高度锁定不跟随。")]
        public float verticalFollowSpeed = 3f;
        [Tooltip("垂直死区(米):目标高度差小于此值时垂直不跟随,消除台阶/斜坡引起的画面微抖。")]
        public float verticalDeadZone = 0.2f;

        [Header("Mouse Drag Rotate (鼠标拖拽旋转镜头,2026-09-08)")]
        [Tooltip("是否启用按住鼠标按键拖拽镜头(左右=水平旋转 yaw,上下=俯仰 pitch)。")]
        public bool enableMouseDragRotate = true;
        [Tooltip("拖拽镜头使用的鼠标按键:0=左键 1=右键 2=中键。")]
        public int rotateMouseButton = 1;
        [Tooltip("水平旋转速度(度/像素):左右拖动灵敏度,值越大转得越快(拖动 300 像素约转 90 度时约 0.3)。")]
        public float rotateSensitivity = 0.3f;
        [Tooltip("旋转平滑时间(秒):越小越跟手、越大越顺滑但越滞后。用于消除鼠标微颤与位置滞后造成的镜头轨迹抖动。")]
        public float rotateSmoothTime = 0.08f;
        [Tooltip("俯仰速度(度/像素):上下拖动灵敏度,值越大俯仰越快。")]
        public float pitchSensitivity = 0.3f;
        [Tooltip("俯仰角下限(度):0=水平视线,90=正上方俯视。向上拖动时俯角不会小于此值。")]
        [Range(0f, 90f)]
        public float minPitch = 10f;
        [Tooltip("俯仰角上限(度):0=水平视线,90=正上方俯视。向下拖动时俯角不会大于此值。")]
        [Range(0f, 90f)]
        public float maxPitch = 85f;
        [Tooltip("反转俯仰拖拽方向。默认关:鼠标上移=更俯视(俯角增大);勾选后方向相反。")]
        public bool invertPitch = false;
        [Tooltip("反转水平旋转方向。默认关;勾选后左右拖拽方向互换。")]
        public bool invertYaw = false;

        private float _yaw = 0f;              // 当前水平旋转角(度,平滑后)
        private float _targetYaw = 0f;        // 拖拽目标 yaw(度,平滑前)
        private float _yawVel = 0f;           // SmoothDampAngle 速度
        private float _lastRotateMouseX;       // 上一帧鼠标 X(拖拽旋转增量用)

        private float _pitch = 60f;           // 当前俯仰角(度,平滑后)
        private float _targetPitch = 60f;     // 拖拽目标俯仰角(度,平滑前)
        private float _pitchVel = 0f;         // SmoothDamp 速度
        private float _lastRotateMouseY;       // 上一帧鼠标 Y(拖拽俯仰增量用)

        private float   _currentDistance;
        private Vector3 _followPosition;      // 平滑跟随点(角色位置,水平/垂直分别平滑)
        private bool    _initialized = false;
        private Camera  _cam;

        // 运行时调试重置用默认值快照
        private float _defaultAngle;
        private float _defaultDistanceValue;
        private float _defaultFov;
        private Vector3 _defaultOffset;

        private Quaternion CamRotation => Quaternion.Euler(_pitch, _yaw, 0f);

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _currentDistance = defaultDistance;
            _defaultAngle         = topdownAngle;
            _defaultDistanceValue = defaultDistance;
            _defaultFov           = fieldOfView;
            _defaultOffset        = targetOffset;
            _pitch = _targetPitch = topdownAngle;
            ApplyFov();
            if (target != null) SnapToTarget();
        }

        void Start()
        {
            if (target == null)
            {
                // 显式注入优先;兜底只认"带输入组件的玩家角色"(2026-07-30),
                // 避免场中有多个 CharacterMotor(玩家+敌人)时随机抢目标。
                var motors = FindObjectsOfType<CharacterMotor>();
                for (int i = 0; i < motors.Length; i++)
                {
                    if (motors[i].GetComponent<CharacterInputHandler>() != null)
                    {
                        target = motors[i].transform;
                        break;
                    }
                }
                if (target == null)
                    Debug.LogWarning("[TopdownCamera] 未指定跟随目标,且场景中找不到玩家角色(CharacterInputHandler)。请在 Inspector 显式指定 target。");
            }
            if (target != null) SnapToTarget();
        }

        void LateUpdate()
        {
            UpdateMouseDragRotate();
            if (target == null) return;
            if (!_initialized) { SnapToTarget(); _initialized = true; return; }
            UpdateCamera();
        }

        public void Zoom(float scrollValue)
        {
            _currentDistance -= scrollValue * scrollSpeed;
            _currentDistance  = Mathf.Clamp(_currentDistance, minDistance, maxDistance);
            if (scaleFovWithZoom) ApplyFov();
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (newTarget != null) SnapToTarget();
        }

        private void UpdateMouseDragRotate()
        {
            if (!enableMouseDragRotate) return;

            // 相机旋转/俯仰属纯本地表现,与滚轮缩放一致,允许直读 Unity Input。
            if (Input.GetMouseButtonDown(rotateMouseButton))
            {
                _lastRotateMouseX = Input.mousePosition.x;
                _lastRotateMouseY = Input.mousePosition.y;
            }

            if (Input.GetMouseButton(rotateMouseButton))
            {
                float dx = Input.mousePosition.x - _lastRotateMouseX;
                float dy = Input.mousePosition.y - _lastRotateMouseY;

                _targetYaw   -= dx * rotateSensitivity * (invertYaw ? -1f : 1f);
                _targetPitch += dy * pitchSensitivity * (invertPitch ? -1f : 1f);
                _targetPitch  = Mathf.Clamp(_targetPitch, minPitch, maxPitch);

                _lastRotateMouseX = Input.mousePosition.x;
                _lastRotateMouseY = Input.mousePosition.y;
            }

            // 平滑 yaw/pitch:旋转与位置跟随同节奏收敛,消除瞬时旋转造成的镜头抖动。
            _yaw   = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVel, rotateSmoothTime);
            _pitch = Mathf.SmoothDamp(_pitch, _targetPitch, ref _pitchVel, rotateSmoothTime);
        }

        private void ApplyFov()
        {
            if (_cam == null) return;
            float fov = fieldOfView;
            if (scaleFovWithZoom)
            {
                float span = maxDistance - minDistance;
                float t = span > 0.001f ? Mathf.InverseLerp(minDistance, maxDistance, _currentDistance) : 0f;
                fov = Mathf.Lerp(fovAtMinDistance, fovAtMaxDistance, t);
            }
            _cam.fieldOfView = fov;
        }

        private Vector3 CamOffset => CamRotation * new Vector3(0f, 0f, -_currentDistance);

        // 跟随焦点 = 平滑跟随点 + 目标偏移。offset.y>0 时角色在画面中下移。
        private Vector3 FocusPoint => _followPosition + targetOffset;

        private void SnapToTarget()
        {
            if (target == null) return;
            _followPosition    = target.position;
            transform.position = FocusPoint + CamOffset;
            transform.rotation = CamRotation;
        }

        private void UpdateCamera()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f) Zoom(scroll);

            float dt = Time.deltaTime;

            // 水平跟随:平滑追赶 target 的 xz(保持操作跟手性)
            Vector3 targetFlat = new Vector3(target.position.x, _followPosition.y, target.position.z);
            _followPosition = Vector3.Lerp(_followPosition, targetFlat, smoothFollowSpeed * dt);

            // 垂直:独立慢速阻尼 + 死区(2026-07-30 立体场景)
            // 攀爬时 target.y 逐帧突变,统一 Lerp 会导致画面抽动;分离后垂直缓慢平滑跟随。
            float newY = _followPosition.y;
            if (verticalFollowSpeed > 0f)
            {
                float dy = target.position.y - _followPosition.y;
                if (Mathf.Abs(dy) > verticalDeadZone)
                {
                    float targetY = target.position.y - Mathf.Sign(dy) * verticalDeadZone;
                    newY = Mathf.Lerp(_followPosition.y, targetY, verticalFollowSpeed * dt);
                }
            }
            _followPosition.y = newY;

            // 位置 = 平滑跟随点 + 球面 offset。offset 只由 (pitch,yaw,distance) 决定,
            // 距离恒定;旋转始终围绕角色(平滑跟随点)稳定进行,不再因 Lerp 走弦而向中心漂移。
            // 目标偏移 targetOffset 叠加在焦点上,用于把角色在画面中整体平移(如下移)。
            transform.position = FocusPoint + CamOffset;
            transform.rotation = CamRotation;
        }

        /// <summary>
        /// 计算「从 fromPosition 指向鼠标屏幕点」的水平瞄准方向，使用硬贴摄像机位置
        /// (fromPosition + targetOffset + 旋转球面偏移)，不含 _followPosition 的 Lerp 位置平滑。
        ///
        /// 背景：渲染用摄像机位置 FocusPoint=_followPosition+targetOffset 会平滑跟随角色，
        /// 角色移动时摄像机水平位置滞后，导致 ScreenPointToRay 的起点漂移，鼠标地面点跟着漂，
        /// 攻击朝向（进而移动方向/根朝向/下半身八向）全部跟着漂——表现为「越操作越滞后、方向错乱」。
        /// 这里把射线起点换成硬贴位置，方向只由摄像机旋转 + 鼠标决定，与位置平滑无关。
        /// </summary>
        public Vector3 GetAimDirection(Vector3 fromPosition, Vector2 screenPos)
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) return Vector3.forward;

            // 攻击朝向用「拖拽目标旋转」(未平滑的 _targetYaw/_targetPitch) 重建射线，
            // 而非读摄像机 transform 的当前(已平滑)姿态，同时摆脱两大滞后源：
            //   1) 位置平滑(_followPosition 的 Lerp)——改用硬贴摄像机位置;
            //   2) 旋转平滑(rotateSmoothTime=0.08 的 SmoothDampAngle/SmoothDamp)——改用目标角直算。
            // 角色始终面向攻击方向、下半身/位移基于攻击方向+实时输入，上游零滞后是前提。
            Quaternion targetRot = Quaternion.Euler(_targetPitch, _targetYaw, 0f);

            // 硬贴摄像机位置(不含位置平滑)：角色 + targetOffset + 目标旋转球面偏移。
            Vector3 hardCamPos = (fromPosition + targetOffset) + targetRot * new Vector3(0f, 0f, -_currentDistance);

            // 射线方向：屏幕坐标 → NDC → 相机局部方向 → 目标旋转，等价于 ScreenPointToRay 但用目标角。
            Vector3 dir = ScreenPointToTargetDir(screenPos, targetRot);

            Plane plane = new Plane(Vector3.up, fromPosition);
            if (plane.Raycast(new Ray(hardCamPos, dir), out float dist))
            {
                Vector3 hit = hardCamPos + dir * dist;
                Vector3 d = hit - fromPosition;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f)
                    return d.normalized;
            }
            return Vector3.zero;
        }

        /// <summary>用给定旋转(而非摄像机当前姿态)重建屏幕点射线方向。</summary>
        private Vector3 ScreenPointToTargetDir(Vector2 screenPos, Quaternion targetRot)
        {
            if (_cam == null) return Vector3.forward;

            float ndcX = (screenPos.x / Screen.width)  * 2f - 1f;
            float ndcY = (screenPos.y / Screen.height) * 2f - 1f;
            float halfH = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * _cam.aspect;

            Vector3 camLocal = new Vector3(ndcX * halfW, ndcY * halfH, 1f);
            return (targetRot * camLocal).normalized;
        }

#if UNITY_EDITOR
        // ---- 运行时调试（仅 Play 模式生效，构建时剔除）----
        private void Update()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetKeyDown(KeyCode.F6)) ResetCameraToDefaults();
            if (!ctrl) return;

            if (Input.GetKeyDown(KeyCode.UpArrow))    { topdownAngle = Mathf.Clamp(topdownAngle + 5f, 10f, 85f); _pitch = _targetPitch = topdownAngle; }
            if (Input.GetKeyDown(KeyCode.DownArrow))  { topdownAngle = Mathf.Clamp(topdownAngle - 5f, 10f, 85f); _pitch = _targetPitch = topdownAngle; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { _currentDistance = Mathf.Clamp(_currentDistance + 1f, minDistance, maxDistance); if (scaleFovWithZoom) ApplyFov(); }
            if (Input.GetKeyDown(KeyCode.LeftArrow))  { _currentDistance = Mathf.Clamp(_currentDistance - 1f, minDistance, maxDistance); if (scaleFovWithZoom) ApplyFov(); }
            if (Input.GetKeyDown(KeyCode.RightBracket)) { fieldOfView = Mathf.Clamp(fieldOfView + 5f, 20f, 100f); ApplyFov(); }
            if (Input.GetKeyDown(KeyCode.LeftBracket))  { fieldOfView = Mathf.Clamp(fieldOfView - 5f, 20f, 100f); ApplyFov(); }
        }

        private void OnGUI()
        {
            float fov = _cam != null ? _cam.fieldOfView : 0f;
            GUI.Box(new Rect(10, 10, 480, 40),
                "Camera: 俯角=" + _pitch.ToString("F0") +
                "  yaw=" + _yaw.ToString("F0") +
                "  距=" + _currentDistance.ToString("F1") +
                "  FOV=" + fov.ToString("F0"));
            GUI.Label(new Rect(20, 32, 460, 16),
                "右键拖拽=旋转/俯仰 单击=移动  Ctrl+↑↓俯角 ←→距离 []FOV F6重置");
            GUI.Label(new Rect(20, 50, 460, 16),
                "目标偏移 Offset=" + targetOffset.ToString("F2") + "  (Y>0 → 角色下移)");
        }

        private void ResetCameraToDefaults()
        {
            topdownAngle     = _defaultAngle;
            _currentDistance = _defaultDistanceValue;
            fieldOfView      = _defaultFov;
            targetOffset     = _defaultOffset;
            _yaw             = 0f;
            _targetYaw       = 0f;
            _yawVel          = 0f;
            _pitch           = _defaultAngle;
            _targetPitch     = _defaultAngle;
            _pitchVel        = 0f;
            ApplyFov();
        }
#endif
    }
}
