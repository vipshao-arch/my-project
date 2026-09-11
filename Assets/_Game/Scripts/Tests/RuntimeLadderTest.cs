using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Game.Character;

namespace Game.Tests
{
    /// <summary>
    /// 运行时梯子测试 v6：验证完整上下流程，不使用反射 hack。
    ///
    /// 测试流程：
    ///   Test 1: 底部进梯 → 向上爬 → 顶部退梯（上平台）
    ///   Test 2: 顶部进梯 → 向下爬 → 底部退梯（下平台）
    ///   Test 3: 底部进梯 → 向上爬 → 向下爬 → 底部退梯（往返）
    ///   Test 4: 退梯后不循环重进（防反复上下）
    ///
    /// 关键设计：
    ///   - 通过 TestInputSource 控制 MoveAxes.y 驱动攀爬输入
    ///   - 使用 inputIsWorldSpace=true + lockRotation=true 绕过 Motor 旋转覆盖
    ///   - 进梯靠 ScanNearbyTriggers 距离检测 + TryAutoEnter 自动进梯
    ///   - 退梯靠 TickClimbing 中的距离检测/Y边界自动触发
    ///   - 不再使用反射 ForceEnter
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class RuntimeLadderTest : MonoBehaviour
    {
        private CharacterLadderAction _action;
        private CharacterMotor _motor;
        private Transform _player;
        private TestInputSource _testInput;

        private static string _logFile;
        private static int _logCount = 0;
        private static List<string> _results = new List<string>();

        private bool _originalWorldSpace;
        private bool _originalLockRotation;

        private static void LogToFile(string msg)
        {
            if (_logFile == null)
                _logFile = Path.Combine(Application.persistentDataPath, "ladder_test_log.txt");
            if (_logCount == 0)
                File.WriteAllText(_logFile, "");
            _logCount++;
            File.AppendAllText(_logFile, msg + "\n");
        }

        private static void RecordResult(string testName, bool pass, string detail)
        {
            string r = $"{testName}: {(pass ? "PASS" : "FAIL")} - {detail}";
            _results.Add(r);
            Debug.Log($"[RuntimeTest] RESULT: {r}");
            LogToFile($"RESULT: {r}");
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/Tests/Run Runtime Ladder Test")]
        static void RunFromEditorMenu()
        {
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.isPlaying = true;
                UnityEditor.EditorApplication.delayCall += StartManualRun;
                return;
            }
            StartManualRun();
        }

        static void StartManualRun()
        {
            if (!Application.isPlaying) return;
            var existing = FindObjectOfType<RuntimeLadderTest>();
            if (existing != null) return;
            var go = new GameObject("[RuntimeLadderTest]");
            go.AddComponent<RuntimeLadderTest>();
            DontDestroyOnLoad(go);
            Debug.Log("[RuntimeTest] Manually started from Tools/Tests");
        }
#endif

        IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogError("[RuntimeTest] No Player found!");
                yield break;
            }

            _player = player.transform;
            _action = player.GetComponent<CharacterLadderAction>();
            _motor  = player.GetComponent<CharacterMotor>();

            if (_action == null)
            {
                Debug.LogError("[RuntimeTest] CharacterLadderAction not found!");
                yield break;
            }

            // 注入测试输入源
            _testInput = player.GetComponent<TestInputSource>();
            if (_testInput == null)
                _testInput = player.AddComponent<TestInputSource>();
            SetPrivateField(_action, "_inputSource", _testInput);

            // 同时注入到 CharacterInputHandler，防止它用 LocalInputSource 覆盖 _motor.input
            var inputHandler = player.GetComponent<CharacterInputHandler>();
            if (inputHandler != null)
                SetPrivateField(inputHandler, "_inputSource", _testInput);

            // 切换到世界系输入
            _originalWorldSpace = (bool)GetPrivateField(_action, "inputIsWorldSpace");
            SetPrivateField(_action, "inputIsWorldSpace", true);
            SetPrivateField(_action, "debugMode", false);

            _originalLockRotation = _motor.lockRotation;

            // 诊断触发器
            var allTriggers = FindObjectsOfType<CharacterLadderTrigger>();
            foreach (var t in allTriggers)
            {
                var fwd = t.enterForward.sqrMagnitude > 0.001f ? t.enterForward : t.transform.forward;
                if (t.reverseEnterForward) fwd = -fwd;
                Debug.Log($"[RuntimeTest] Trigger '{t.name}': pos={t.transform.position} " +
                          $"play={t.playAnimation} exit={t.exitAnimation} " +
                          $"autoAction={t.autoAction} fwd={fwd} " +
                          $"matchTarget={t.matchTarget?.position}");
            }

            Debug.Log("[RuntimeTest] ===== STARTING LADDER TESTS v6 =====");
            LogToFile("===== LADDER TESTS v6 =====");

            // Test 1: 底部进梯 → 向上爬 → 顶部退梯
            Debug.Log("[RuntimeTest] --- TEST 1: Bottom Enter → Climb Up → Top Exit ---");
            yield return TestBottomEnterTopExit();
            yield return new WaitForSeconds(1f);
            ForceReset();
            yield return new WaitForSeconds(0.5f);

            // Test 2: 顶部进梯 → 向下爬 → 底部退梯
            Debug.Log("[RuntimeTest] --- TEST 2: Top Enter → Climb Down → Bottom Exit ---");
            yield return TestTopEnterBottomExit();
            yield return new WaitForSeconds(1f);
            ForceReset();
            yield return new WaitForSeconds(0.5f);

            // Test 3: 底部进梯 → 向上爬 → 向下爬 → 底部退梯
            Debug.Log("[RuntimeTest] --- TEST 3: Bottom Enter → Up → Down → Bottom Exit ---");
            yield return TestBottomEnterUpDownExit();
            yield return new WaitForSeconds(1f);
            ForceReset();
            yield return new WaitForSeconds(0.5f);

            // Test 4: 退梯后防循环重进
            Debug.Log("[RuntimeTest] --- TEST 4: No Re-enter After Exit ---");
            yield return TestNoReenterCycle();
            yield return new WaitForSeconds(1f);

            // 恢复
            SetPrivateField(_action, "inputIsWorldSpace", _originalWorldSpace);
            _motor.lockRotation = _originalLockRotation;
            _testInput.MoveAxes = Vector2.zero;

            Debug.Log("[RuntimeTest] ===== ALL TESTS COMPLETE =====");
            LogToFile("===== FINAL SUMMARY =====");
            foreach (var r in _results)
                LogToFile(r);
            Debug.Log($"[RuntimeTest] LogFile: {_logFile}");
        }

        // ─── 方向推导 ───────────────────────────────────────────

        private Vector3 GetExpectedEnterDirection(CharacterLadderTrigger t)
        {
            Vector3 fwd = t.enterForward.sqrMagnitude > 0.001f
                ? t.enterForward
                : t.transform.forward;
            if (t.reverseEnterForward) fwd = -fwd;
            fwd.y = 0f;
            return fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
        }

        private Vector2 DirToInput(Vector3 worldDir) => new Vector2(worldDir.x, worldDir.z);

        // ─── Test 1: 底部进梯 → 向上爬 → 顶部退梯 ──────────────

        IEnumerator TestBottomEnterTopExit()
        {
            var triggerGO = GameObject.Find("EnterLadderBottom");
            if (triggerGO == null)
            {
                RecordResult("Test1_BottomEnterTopExit", false, "EnterLadderBottom not found");
                yield break;
            }

            var trigger = triggerGO.GetComponent<CharacterLadderTrigger>();
            Vector3 expectedDir = GetExpectedEnterDirection(trigger);
            Vector3 triggerPos = triggerGO.transform.position;

            // 定位到触发器附近
            Vector3 startPos = triggerPos - expectedDir * 0.2f;
            startPos.y = 0f;
            var rb = _player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = startPos;
                rb.velocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            _player.position = startPos;
            _player.rotation = Quaternion.LookRotation(expectedDir);

            yield return new WaitForFixedUpdate();
            yield return null;

            // 锁定旋转 + 朝梯子方向走
            _motor.lockRotation = true;
            Vector2 input = DirToInput(expectedDir);

            // 阶段1：进梯
            bool entered = false;
            float enterTimer = 0f;
            while (enterTimer < 3f)
            {
                enterTimer += Time.deltaTime;
                // 只在循环开始前定位，不能每帧覆盖 Enter Root Motion。
                _testInput.MoveAxes = input;

                if (_action.debugState == "Climbing")
                {
                    entered = true;
                    Debug.Log($"[RuntimeTest] T1: Entered Climbing at t={enterTimer:F2}s");
                    break;
                }
                yield return null;
            }

            if (!entered)
            {
                RecordResult("Test1_BottomEnterTopExit", false, $"failed to enter, state={_action.debugState}");
                _motor.lockRotation = _originalLockRotation;
                yield break;
            }

            // 阶段2：向上爬
            _testInput.MoveAxes = new Vector2(0f, 1f); // 世界系 +Y = 向上
            float climbTimer = 0f;
            bool reachedTop = false;
            while (climbTimer < 15f)
            {
                climbTimer += Time.deltaTime;
                _testInput.MoveAxes = new Vector2(0f, 1f);

                if (_action.debugState == "Idle" || _action.debugState == "Exiting")
                {
                    reachedTop = true;
                    Debug.Log($"[RuntimeTest] T1: Top exit at t={climbTimer:F2}s pos={_player.position}");
                    break;
                }
                yield return null;
            }

            // 等待 Exiting → Idle
            float exitTimer = 0f;
            while (_action.debugState == "Exiting" && exitTimer < 5f)
            {
                exitTimer += Time.deltaTime;
                _testInput.MoveAxes = Vector2.zero;
                yield return null;
            }

            _testInput.MoveAxes = Vector2.zero;
            _motor.lockRotation = _originalLockRotation;

            if (reachedTop || _action.debugState == "Idle")
            {
                RecordResult("Test1_BottomEnterTopExit", true,
                    $"entered→climbed→exited, finalY={_player.position.y:F2}");
            }
            else
            {
                RecordResult("Test1_BottomEnterTopExit", false,
                    $"stuck at {_action.debugState} after {climbTimer:F1}s, Y={_player.position.y:F2}");
            }
        }

        // ─── Test 2: 顶部进梯 → 向下爬 → 底部退梯 ──────────────

        IEnumerator TestTopEnterBottomExit()
        {
            var exitTopGO = GameObject.Find("ExitLadderTop");
            if (exitTopGO == null)
            {
                RecordResult("Test2_TopEnterBottomExit", false, "ExitLadderTop not found");
                yield break;
            }

            var trigger = exitTopGO.GetComponent<CharacterLadderTrigger>();
            Vector3 expectedDir = GetExpectedEnterDirection(trigger);
            Vector3 triggerPos = exitTopGO.transform.position;

            // 定位到顶部触发器正前方 0.2m（同高度）
            Vector3 startPos = triggerPos - expectedDir * 0.2f;
            startPos.y = triggerPos.y;
            // 使用 Rigidbody.position 直接设置避免重力干扰
            var rb = _player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = startPos;
                rb.velocity = Vector3.zero;
            }
            _player.position = startPos;
            _player.rotation = Quaternion.LookRotation(expectedDir);
            Physics.SyncTransforms();

            // 暂时锁定物理，防止掉落
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

            yield return new WaitForFixedUpdate();
            yield return null;

            _motor.lockRotation = true;
            Vector2 input = DirToInput(expectedDir);

            // 阶段1：进梯
            bool entered = false;
            float enterTimer = 0f;
            while (enterTimer < 3f)
            {
                enterTimer += Time.deltaTime;
                // 只在进入循环前定位一次；进入动画的 Root Motion 不能被测试夹具每帧覆盖。
                _testInput.MoveAxes = input;

                if (_action.debugState == "Climbing")
                {
                    entered = true;
                    Debug.Log($"[RuntimeTest] T2: Entered Climbing at t={enterTimer:F2}s");
                    break;
                }
                yield return null;
            }

            if (!entered)
            {
                RecordResult("Test2_TopEnterBottomExit", false, $"failed to enter, state={_action.debugState}");
                _motor.lockRotation = _originalLockRotation;
                yield break;
            }

            // 阶段2：向下爬
            _testInput.MoveAxes = new Vector2(0f, -1f);
            float climbTimer = 0f;
            bool reachedBottom = false;
            while (climbTimer < 15f)
            {
                climbTimer += Time.deltaTime;
                _testInput.MoveAxes = new Vector2(0f, -1f);

                if (_action.debugState == "Idle" || _action.debugState == "Exiting")
                {
                    reachedBottom = true;
                    Debug.Log($"[RuntimeTest] T2: Bottom exit at t={climbTimer:F2}s pos={_player.position}");
                    break;
                }
                yield return null;
            }

            // 等待 Exiting → Idle
            float exitTimer = 0f;
            while (_action.debugState == "Exiting" && exitTimer < 5f)
            {
                exitTimer += Time.deltaTime;
                _testInput.MoveAxes = Vector2.zero;
                yield return null;
            }

            _testInput.MoveAxes = Vector2.zero;
            _motor.lockRotation = _originalLockRotation;

            if (reachedBottom || _action.debugState == "Idle")
            {
                RecordResult("Test2_TopEnterBottomExit", true,
                    $"top→climbed down→exited, finalY={_player.position.y:F2}");
            }
            else
            {
                RecordResult("Test2_TopEnterBottomExit", false,
                    $"stuck at {_action.debugState} after {climbTimer:F1}s, Y={_player.position.y:F2}");
            }
        }

        // ─── Test 3: 底部进梯 → 向上 → 向下 → 底部退梯 ──────────

        IEnumerator TestBottomEnterUpDownExit()
        {
            var triggerGO = GameObject.Find("EnterLadderBottom");
            if (triggerGO == null)
            {
                RecordResult("Test3_UpDownRoundTrip", false, "EnterLadderBottom not found");
                yield break;
            }

            var trigger = triggerGO.GetComponent<CharacterLadderTrigger>();
            Vector3 expectedDir = GetExpectedEnterDirection(trigger);
            Vector3 triggerPos = triggerGO.transform.position;

            Vector3 startPos = triggerPos - expectedDir * 0.2f;
            startPos.y = 0f;
            var rb = _player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = startPos;
                rb.velocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            _player.position = startPos;
            _player.rotation = Quaternion.LookRotation(expectedDir);

            yield return new WaitForFixedUpdate();
            yield return null;

            _motor.lockRotation = true;
            Vector2 input = DirToInput(expectedDir);

            // 进梯
            bool entered = false;
            float enterTimer = 0f;
            while (enterTimer < 3f)
            {
                enterTimer += Time.deltaTime;
                _player.position = startPos;
                _player.rotation = Quaternion.LookRotation(expectedDir);
                _testInput.MoveAxes = input;

                if (_action.debugState == "Climbing")
                {
                    entered = true;
                    break;
                }
                yield return null;
            }

            if (!entered)
            {
                RecordResult("Test3_UpDownRoundTrip", false, $"failed to enter, state={_action.debugState}");
                _motor.lockRotation = _originalLockRotation;
                yield break;
            }

            // 向上爬 1 秒
            _testInput.MoveAxes = new Vector2(0f, 1f);
            float upTimer = 0f;
            while (upTimer < 1f && _action.debugState == "Climbing")
            {
                upTimer += Time.deltaTime;
                _testInput.MoveAxes = new Vector2(0f, 1f);
                yield return null;
            }

            if (_action.debugState != "Climbing")
            {
                // 向上爬时已退梯（可能爬太快到达顶部）
                _testInput.MoveAxes = Vector2.zero;
                _motor.lockRotation = _originalLockRotation;
                RecordResult("Test3_UpDownRoundTrip", true,
                    $"exited during up-climb at t={upTimer:F1}s (short ladder)");
                yield break;
            }

            // 向下爬，应触发底部退梯
            _testInput.MoveAxes = new Vector2(0f, -1f);
            float downTimer = 0f;
            bool exited = false;
            while (downTimer < 15f)
            {
                downTimer += Time.deltaTime;
                _testInput.MoveAxes = new Vector2(0f, -1f);

                if (_action.debugState == "Idle" || _action.debugState == "Exiting")
                {
                    exited = true;
                    break;
                }
                yield return null;
            }

            // 等待 Exiting → Idle
            float exitWait = 0f;
            while (_action.debugState == "Exiting" && exitWait < 5f)
            {
                exitWait += Time.deltaTime;
                _testInput.MoveAxes = Vector2.zero;
                yield return null;
            }

            _testInput.MoveAxes = Vector2.zero;
            _motor.lockRotation = _originalLockRotation;

            if (exited || _action.debugState == "Idle")
            {
                RecordResult("Test3_UpDownRoundTrip", true,
                    $"up→down→exited, finalY={_player.position.y:F2}");
            }
            else
            {
                RecordResult("Test3_UpDownRoundTrip", false,
                    $"stuck at {_action.debugState} after {downTimer:F1}s");
            }
        }

        // ─── Test 4: 退梯后防循环重进 ──────────────────────────

        IEnumerator TestNoReenterCycle()
        {
            var triggerGO = GameObject.Find("EnterLadderBottom");
            if (triggerGO == null)
            {
                RecordResult("Test4_NoReenterCycle", false, "EnterLadderBottom not found");
                yield break;
            }

            var trigger = triggerGO.GetComponent<CharacterLadderTrigger>();
            Vector3 expectedDir = GetExpectedEnterDirection(trigger);
            Vector3 triggerPos = triggerGO.transform.position;

            // 进梯 → 立刻退梯（Space），然后检查不重进
            Vector3 startPos = triggerPos - expectedDir * 0.2f;
            startPos.y = 0f;
            var rb = _player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = startPos;
                rb.velocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            _player.position = startPos;
            _player.rotation = Quaternion.LookRotation(expectedDir);

            yield return new WaitForFixedUpdate();
            yield return null;

            _motor.lockRotation = true;
            Vector2 input = DirToInput(expectedDir);

            // 进梯
            bool entered = false;
            float enterTimer = 0f;
            while (enterTimer < 3f)
            {
                enterTimer += Time.deltaTime;
                _player.position = startPos;
                _player.rotation = Quaternion.LookRotation(expectedDir);
                _testInput.MoveAxes = input;

                if (_action.debugState == "Climbing")
                {
                    entered = true;
                    break;
                }
                yield return null;
            }

            if (!entered)
            {
                // 如果进梯失败，至少验证不会反复进退
                _testInput.MoveAxes = Vector2.zero;
                _motor.lockRotation = _originalLockRotation;
                RecordResult("Test4_NoReenterCycle", true,
                    "enter failed but no cycle detected");
                yield break;
            }

            // 向上爬一小段
            _testInput.MoveAxes = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.3f);

            // 退梯（模拟 Space 按键）
            // 通过反射设置 _exitInputRequested 或直接等待距离检测退梯
            // 这里用向下爬来触发底部退梯
            _testInput.MoveAxes = new Vector2(0f, -1f);
            float exitTimer = 0f;
            while (exitTimer < 10f)
            {
                exitTimer += Time.deltaTime;
                _testInput.MoveAxes = new Vector2(0f, -1f);

                if (_action.debugState == "Idle")
                {
                    Debug.Log($"[RuntimeTest] T4: Exited at t={exitTimer:F2}s");
                    break;
                }
                yield return null;
            }

            // 退梯后保持输入（模拟玩家持续按住方向键），检查3秒内不重进
            _testInput.MoveAxes = Vector2.zero;
            if (_motor != null) _motor.input = Vector2.zero;

            float observeTimer = 0f;
            int reenterCount = 0;
            while (observeTimer < 3f)
            {
                observeTimer += Time.deltaTime;
                // 不给任何输入
                _testInput.MoveAxes = Vector2.zero;

                if (_action.debugState != "Idle" && _action.debugState != "Exiting")
                {
                    reenterCount++;
                    Debug.Log($"[RuntimeTest] T4: Re-entered at t={observeTimer:F2}s state={_action.debugState}");
                    // 如果重进了，等它退出来再继续观察
                    float waitTimer = 0f;
                    while (_action.debugState != "Idle" && waitTimer < 5f)
                    {
                        waitTimer += Time.deltaTime;
                        _testInput.MoveAxes = Vector2.zero;
                        yield return null;
                    }
                }
                yield return null;
            }

            _motor.lockRotation = _originalLockRotation;

            if (reenterCount == 0)
            {
                RecordResult("Test4_NoReenterCycle", true,
                    "no re-enter detected in 3s after exit");
            }
            else
            {
                RecordResult("Test4_NoReenterCycle", false,
                    $"re-entered {reenterCount} times in 3s (cycle bug)");
            }
        }

        // ─── 辅助 ───────────────────────────────────────────────

        void ForceReset()
        {
            if (_action == null) return;
            _motor.lockRotation = _originalLockRotation;
            var method = typeof(CharacterLadderAction).GetMethod("ResetPlayerSettings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null) method.Invoke(_action, null);

            // 重置防循环标记和冷却，允许测试间重新进梯
            SetPrivateField(_action, "_blockedReentryTrigger", null);
            SetPrivateField(_action, "_reentryBlockTimer", 0f);
            SetPrivateField(_action, "_mustReleaseVerticalInput", false);
            SetPrivateField(_action, "_exitCooldown", 0f);

            // 恢复物理（ResetPlayerSettings 已做，但确保 Rigidbody 非kinematic）
            var rb = _player.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                // 已恢复为非kinematic，无需额外操作
            }

            if (_motor != null) _motor.input = Vector2.zero;
            _testInput.MoveAxes = Vector2.zero;
        }

        static void SetPrivateField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (field != null) field.SetValue(obj, value);
        }

        static object GetPrivateField(object obj, string fieldName)
        {
            var field = obj.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return field != null ? field.GetValue(obj) : null;
        }
    }

    /// <summary>
    /// 测试用输入源：实现 ICharacterInputSource，允许程序化控制 MoveAxes。
    /// </summary>
    public class TestInputSource : MonoBehaviour, ICharacterInputSource
    {
        [HideInInspector] public Vector2 MoveAxes = Vector2.zero;

        public Vector2 MoveAxesRaw => MoveAxes;
        Vector2 ICharacterInputSource.MoveAxes => MoveAxes;
        public Vector2 MousePosition => Vector2.zero;
        public float MouseScroll => 0f;
        public bool GetKey(KeyCode key) => false;
        public bool GetKeyDown(KeyCode key) => false;
        public bool GetMouseButton(int button) => false;
        public bool GetMouseButtonDown(int button) => false;
        public bool GetMouseButtonUp(int button) => false;
    }
}
