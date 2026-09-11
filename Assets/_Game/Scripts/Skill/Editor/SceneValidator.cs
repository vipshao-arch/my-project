#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using System.Collections.Generic;
using Game.Character;
using Game.SkillSystem;

namespace Game.Character.Editor
{
    /// <summary>
    /// 场景配置验证器
    ///
    /// 编辑时检查整个场景的配置正确性，输出问题列表。
    /// 涵盖：角色组件、梯子触发器、机关配置、相机绑定、Action Trigger、
    ///       敌人系统（EnemyAI / EnemyMotor / NavMeshAgent / NavMesh 烘焙）。
    ///
    /// 菜单：Tools > Level Kit > Diagnostics > Scene Validator
    /// </summary>
    public class SceneValidator : EditorWindow
    {
        private Vector2 _scroll;
        private List<Issue> _issues = new List<Issue>();
        private bool _ran = false;

        // 分组折叠状态
        private bool _foldPlayer  = true;
        private bool _foldEnemy   = true;
        private bool _foldLadder  = true;
        private bool _foldTrap    = true;
        private bool _foldTrigger = true;
        private bool _foldCamera  = true;
        private bool _foldNet     = true;

        private enum Severity { Error, Warning, Info }

        private struct Issue
        {
            public Severity severity;
            public string   message;
            public Object   target;
            public string   group;
        }

        // ─────────────────────────────────────────────────────────────────

        [MenuItem("Tools/Level Kit/Scene Validator (Gameplay)", false, 325)]
        static void Open()
        {
            var win = GetWindow<SceneValidator>("Gameplay Validator");
            win.minSize = new Vector2(520, 500);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("玩法对象校验器（Gameplay Validator）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "检查场景中角色/敌人/梯子/机关/相机的配置完整性。\n" +
                "仅在编辑器内使用，不影响运行时。",
                MessageType.Info);
            EditorGUILayout.Space(6);

            if (GUILayout.Button("▶  运行验证", GUILayout.Height(34)))
            {
                RunValidation();
                _ran = true;
            }

            if (!_ran)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField("点击「运行验证」开始检查。", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            EditorGUILayout.Space(6);

            int errors = 0, warnings = 0, infos = 0;
            foreach (var i in _issues)
            {
                if      (i.severity == Severity.Error)   errors++;
                else if (i.severity == Severity.Warning) warnings++;
                else                                     infos++;
            }

            DrawSummaryBar(errors, warnings, infos);
            EditorGUILayout.Space(6);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
            DrawGroup("玩家角色",   "player",  ref _foldPlayer);
            DrawGroup("敌人系统",   "enemy",   ref _foldEnemy);
            DrawGroup("梯子系统",   "ladder",  ref _foldLadder);
            DrawGroup("机关/Trap", "trap",    ref _foldTrap);
            DrawGroup("ActionTrigger", "trigger", ref _foldTrigger);
            DrawGroup("相机",       "camera",  ref _foldCamera);
            DrawGroup("联机",       "net",     ref _foldNet);
            DrawGroup("其他",       "",        ref _foldPlayer);
            }
            finally { EditorGUILayout.EndScrollView(); }
        }

        // ─────────────────────────────────────────────────────────────────
        // 分组绘制
        // ─────────────────────────────────────────────────────────────────

        private void DrawGroup(string label, string group, ref bool foldout)
        {
            var groupIssues = new List<Issue>();
            foreach (var i in _issues)
            {
                bool match = group == "" ? (i.group == "" || i.group == null) : i.group == group;
                if (match) groupIssues.Add(i);
            }
            if (groupIssues.Count == 0) return;

            int e = 0, w = 0;
            foreach (var i in groupIssues)
            {
                if (i.severity == Severity.Error)   e++;
                if (i.severity == Severity.Warning) w++;
            }

            string suffix = e > 0 ? $"  ✗{e}" : (w > 0 ? $"  ⚠{w}" : "  ✓");
            foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, label + suffix);
            if (foldout)
            {
                EditorGUI.indentLevel++;
                foreach (var issue in groupIssues)
                    DrawIssueRow(issue);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ─────────────────────────────────────────────────────────────────
        // Summary Bar
        // ─────────────────────────────────────────────────────────────────

        private static void DrawSummaryBar(int errors, int warnings, int infos)
        {
            EditorGUILayout.BeginHorizontal();

            var eStyle = new GUIStyle(EditorStyles.helpBox);
            eStyle.normal.textColor = errors > 0 ? new Color(0.9f, 0.2f, 0.2f) : Color.gray;
            GUILayout.Label($"✗  {errors} 错误", eStyle);

            var wStyle = new GUIStyle(EditorStyles.helpBox);
            wStyle.normal.textColor = warnings > 0 ? new Color(0.9f, 0.7f, 0.1f) : Color.gray;
            GUILayout.Label($"⚠  {warnings} 警告", wStyle);

            var iStyle = new GUIStyle(EditorStyles.helpBox);
            iStyle.normal.textColor = Color.gray;
            GUILayout.Label($"ℹ  {infos} 提示", iStyle);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawIssueRow(Issue issue)
        {
            MessageType mt = issue.severity == Severity.Error   ? MessageType.Error
                           : issue.severity == Severity.Warning ? MessageType.Warning
                           :                                       MessageType.Info;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(issue.message, mt);
            if (issue.target != null && GUILayout.Button("选中", GUILayout.Width(42), GUILayout.Height(30)))
            {
                var go = issue.target as GameObject;
                if (go != null)
                {
                    Selection.activeGameObject = go;
                    EditorGUIUtility.PingObject(go);
                }
                else
                {
                    EditorGUIUtility.PingObject(issue.target);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─────────────────────────────────────────────────────────────────
        // 验证逻辑
        // ─────────────────────────────────────────────────────────────────

        private void RunValidation()
        {
            _issues.Clear();

            ValidatePlayer();
            ValidateEnemies();
            ValidateLadders();
            ValidateTraps();
            ValidateActionTriggers();
            ValidateCamera();
            ValidateNetworking();

            if (_issues.Count == 0)
                Add(Severity.Info, "✅ 场景验证全部通过！", null, "");

            Repaint();
        }

        // ── 联机(出生标记/预放实例,2026-07-31 阵营版配套) ────────────────

        private void ValidateNetworking()
        {
            // 出生标记:合作=PlayerSpawn;对战=PlayerSpawn_A/B 成对(按名字查找,与 NetworkSpawnPlacer 一致)
            var single = GameObject.Find("PlayerSpawn");
            var a      = GameObject.Find("PlayerSpawn_A");
            var b      = GameObject.Find("PlayerSpawn_B");
            if (single == null && a == null && b == null)
                Add(Severity.Warning, "联机出生标记缺失:无 PlayerSpawn 也无 PlayerSpawn_A/B(联机出生回退世界原点)。", null, "net");
            if ((a == null) != (b == null))
                Add(Severity.Error, "PlayerSpawn_A/B 必须成对存在(只找到一侧),团队/FFA 分边失效。", a != null ? a : b, "net");

            // 敌人出生标记:无标记=对战场景不刷敌(预期);有标记=Host 按标记动态生成
            var eMelee = GameObject.Find("EnemySpawn_Melee");
            var eHigh  = GameObject.Find("EnemySpawn_HighGround");
            if (eMelee == null && eHigh == null)
                Add(Severity.Info, "无 EnemySpawn_Melee/HighGround 标记:联机不刷敌(对战场景预期;合作场景需补齐)。", null, "net");

            // 场景预放玩家/敌人:联机时会被 CleanupScenePlayer 清除改由 Host 动态生成
            foreach (var m in FindObjectsOfType<CharacterMotor>())
                Add(Severity.Warning, $"[{m.name}] 场景预放玩家:联机建房时会被清理(Host 动态生成玩家),单机调试可保留,联机场景建议删除。", m.gameObject, "net");
            foreach (var e in FindObjectsOfType<EnemyMotor>(true))
                Add(Severity.Warning, $"[{e.gameObject.name}] 场景预放敌人:联机建房时会被清理(Host 按 EnemySpawn 标记生成),建议删除改用标记。", e.gameObject, "net");
        }

        // ── 玩家角色 ──────────────────────────────────────────────────────

        private void ValidatePlayer()
        {
            var motors = FindObjectsOfType<CharacterMotor>();
            if (motors.Length == 0)
            {
                Add(Severity.Error, "场景中没有 CharacterMotor，玩家系统不可用。", null, "player");
                return;
            }
            if (motors.Length > 1)
                Add(Severity.Warning, $"场景中有 {motors.Length} 个 CharacterMotor，多角色请确认。", null, "player");

            foreach (var motor in motors)
            {
                string n = motor.name;

                if (motor.GetComponent<Rigidbody>() == null)
                    Add(Severity.Error, $"[{n}] 缺少 Rigidbody", motor.gameObject, "player");

                var rb = motor.GetComponent<Rigidbody>();
                if (rb != null && rb.isKinematic)
                    Add(Severity.Warning, $"[{n}] Rigidbody.isKinematic=true，CharacterMotor 依赖物理位移", motor.gameObject, "player");

                if (motor.GetComponent<CapsuleCollider>() == null)
                    Add(Severity.Error, $"[{n}] 缺少 CapsuleCollider", motor.gameObject, "player");

                if (motor.GetComponent<Animator>() == null)
                    Add(Severity.Error, $"[{n}] 缺少 Animator", motor.gameObject, "player");

                if (motor.GetComponent<CharacterInputHandler>() == null)
                    Add(Severity.Warning, $"[{n}] 缺少 CharacterInputHandler（玩家控制失效）", motor.gameObject, "player");

                var anim = motor.GetComponent<Animator>();
                if (anim != null && anim.runtimeAnimatorController == null)
                    Add(Severity.Error, $"[{n}] Animator 没有绑定 AnimatorController", motor.gameObject, "player");

                // Layer 检查
                if (motor.gameObject.layer != 8)
                    Add(Severity.Warning, $"[{n}] gameObject.layer={motor.gameObject.layer}，期望 Layer 8 (Player)。敌人检测会失效。", motor.gameObject, "player");
            }
        }

        // ── 敌人系统 ──────────────────────────────────────────────────────

        private void ValidateEnemies()
        {
            var enemies = FindObjectsOfType<EnemyMotor>(true);
            if (enemies.Length == 0)
            {
                Add(Severity.Info, "场景中无 EnemyMotor（无敌人，如无战斗可忽略）。", null, "enemy");
                return;
            }

            Add(Severity.Info, $"场景中共有 {enemies.Length} 个敌人。", null, "enemy");

            // 检查 NavMesh 是否已烘焙（取一个采样点）
            bool hasNavMesh = false;
            foreach (var em in enemies)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(em.transform.position, out hit, 2f, NavMesh.AllAreas))
                {
                    hasNavMesh = true;
                    break;
                }
            }
            if (!hasNavMesh)
                Add(Severity.Error, "场景 NavMesh 未烘焙或覆盖不足，EnemyAI 无法寻路。请在 Navigation 窗口烘焙 NavMesh。", null, "enemy");

            foreach (var motor in enemies)
            {
                string n = motor.gameObject.name;

                // EnemyAI
                var ai = motor.GetComponent<EnemyAI>();
                if (ai == null)
                    Add(Severity.Error, $"[{n}] 缺少 EnemyAI 组件，敌人行为失效。", motor.gameObject, "enemy");

                // NavMeshAgent
                var agent = motor.GetComponent<NavMeshAgent>();
                if (agent == null)
                    Add(Severity.Error, $"[{n}] 缺少 NavMeshAgent，AI 无法寻路。", motor.gameObject, "enemy");
                else
                {
                    if (!agent.isOnNavMesh && Application.isPlaying)
                        Add(Severity.Warning, $"[{n}] NavMeshAgent 不在 NavMesh 上（运行时），可能站在 NavMesh 外。", motor.gameObject, "enemy");
                }

                // Rigidbody
                var rb = motor.GetComponent<Rigidbody>();
                if (rb == null)
                    Add(Severity.Error, $"[{n}] 缺少 Rigidbody。", motor.gameObject, "enemy");
                else if (!rb.isKinematic)
                    Add(Severity.Warning, $"[{n}] Rigidbody.isKinematic=false，有 NavMeshAgent 时应为 kinematic，否则物理与 NavMesh 冲突。", motor.gameObject, "enemy");

                // Collider
                if (motor.GetComponent<CapsuleCollider>() == null)
                    Add(Severity.Error, $"[{n}] 缺少 CapsuleCollider，命中检测失效。", motor.gameObject, "enemy");

                // Animator
                var anim = motor.GetComponentInChildren<Animator>();
                if (anim == null)
                    Add(Severity.Error, $"[{n}] 缺少 Animator（含子物体），死亡/攻击动画失效。", motor.gameObject, "enemy");
                else if (anim.runtimeAnimatorController == null)
                    Add(Severity.Error, $"[{n}] Animator 没有绑定 AnimatorController。", motor.gameObject, "enemy");

                // Layer
                if (motor.gameObject.layer != 9)
                    Add(Severity.Warning, $"[{n}] gameObject.layer={motor.gameObject.layer}，期望 Layer 9 (Enemy)。HitDetector 默认 hitMask=512(1<<9)，不在该层会漏检。", motor.gameObject, "enemy");

                // EnemyDrop
                var drop = motor.GetComponent<EnemyDrop>();
                if (drop == null)
                    Add(Severity.Info, $"[{n}] 没有 EnemyDrop 组件（无掉落物，如不需要可忽略）。", motor.gameObject, "enemy");

                // EnemyHealthBar
                var hb = motor.GetComponent<EnemyHealthBar>();
                if (hb == null)
                    Add(Severity.Info, $"[{n}] 没有 EnemyHealthBar（运行时会自动添加）。", motor.gameObject, "enemy");

                // AI 攻击参数检查
                if (ai != null)
                {
                    float totalAttackTime = ai.attackStartUpTime + ai.attackHitDelay + ai.attackRecoverTime;
                    if (totalAttackTime > ai.attackCooldown)
                        Add(Severity.Warning,
                            $"[{n}] 攻击时间窗总和 ({totalAttackTime:F2}s) > attackCooldown ({ai.attackCooldown:F2}s)，" +
                            "下次攻击会延后，可能造成 AI 攻击频率异常。",
                            motor.gameObject, "enemy");

                    if (ai.attackRange <= 0f)
                        Add(Severity.Error, $"[{n}] attackRange <= 0，敌人永远无法进入攻击状态。", motor.gameObject, "enemy");

                    if (ai.detectionRange <= ai.attackRange)
                        Add(Severity.Warning, $"[{n}] detectionRange({ai.detectionRange}) <= attackRange({ai.attackRange})，敌人可能来不及检测到玩家就已经在攻击范围内。", motor.gameObject, "enemy");
                }
            }
        }

        // ── 梯子 ──────────────────────────────────────────────────────────

        private void ValidateLadders()
        {
            var triggers = FindObjectsOfType<CharacterLadderTrigger>(true);
            if (triggers.Length == 0)
            {
                Add(Severity.Info, "场景中无梯子触发器（CharacterLadderTrigger），如无梯子可忽略。", null, "ladder");
                return;
            }

            foreach (var t in triggers)
            {
                string n = t.gameObject.name;

                if (t.matchTarget == null)
                    Add(Severity.Error, $"梯子触发器 [{n}] matchTarget 为空，进梯动作无法对齐", t.gameObject, "ladder");

                var col = t.GetComponent<Collider>();
                if (col == null)
                    Add(Severity.Error, $"梯子触发器 [{n}] 缺少 Collider", t.gameObject, "ladder");
                else if (!col.isTrigger)
                    Add(Severity.Error, $"梯子触发器 [{n}] Collider.isTrigger 必须为 true", t.gameObject, "ladder");

                if (string.IsNullOrEmpty(t.playAnimation))
                    Add(Severity.Warning, $"梯子触发器 [{n}] playAnimation 为空，进梯无动画", t.gameObject, "ladder");
            }

            // 检查角色是否有 LadderAction
            var motors = FindObjectsOfType<CharacterMotor>();
            foreach (var m in motors)
            {
                if (m.GetComponent<CharacterLadderAction>() == null)
                    Add(Severity.Warning, $"角色 [{m.name}] 缺少 CharacterLadderAction，无法与梯子交互", m.gameObject, "ladder");
            }
        }

        // ── 机关/Trap ──────────────────────────────────────────────────────

        private void ValidateTraps()
        {
            var pendulums = FindObjectsOfType<vPendulum>(true);
            if (pendulums.Length == 0) return;

            foreach (var p in pendulums)
            {
                string n = p.gameObject.name;

                var fwd = p.GetComponentInChildren<PendulumHitForwarder>(true);
                if (fwd == null)
                    Add(Severity.Error, $"vPendulum [{n}] 子节点中没有 PendulumHitForwarder，击中无法触发", p.gameObject, "trap");
                else
                {
                    var col = fwd.GetComponent<Collider>();
                    if (col == null)
                        Add(Severity.Error, $"PendulumHitForwarder [{fwd.name}] 缺少 Collider（需 BoxCollider isTrigger）", fwd.gameObject, "trap");
                    else if (!col.isTrigger)
                        Add(Severity.Error, $"PendulumHitForwarder [{fwd.name}] Collider.isTrigger 必须为 true", fwd.gameObject, "trap");
                }

                if (p.knockbackForce <= 0f)
                    Add(Severity.Warning, $"vPendulum [{n}] knockbackForce <= 0，击退无效果", p.gameObject, "trap");
            }
        }

        // ── Action Trigger ─────────────────────────────────────────────────

        private void ValidateActionTriggers()
        {
            var triggers = FindObjectsOfType<CharacterActionTrigger>(true);
            if (triggers.Length == 0) return;

            foreach (var t in triggers)
            {
                string n = t.gameObject.name;

                var col = t.GetComponent<Collider>();
                if (col == null)
                    Add(Severity.Error, $"ActionTrigger [{n}] 缺少 Collider", t.gameObject, "trigger");
                else if (!col.isTrigger)
                    Add(Severity.Warning, $"ActionTrigger [{n}] Collider.isTrigger 建议为 true", t.gameObject, "trigger");

                if (string.IsNullOrEmpty(t.playAnimation))
                    Add(Severity.Warning, $"ActionTrigger [{n}] playAnimation 为空，不会触发任何动作", t.gameObject, "trigger");
            }
        }

        // ── 相机 ──────────────────────────────────────────────────────────

        private void ValidateCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Add(Severity.Error, "场景中没有标记为 MainCamera 的摄像机", null, "camera");
                return;
            }

            // 尝试找 TopdownCameraController（用反射，避免硬依赖）
            var ctrlType = System.Type.GetType("Game.Character.TopdownCameraController, Assembly-CSharp") ??
                           System.Type.GetType("TopdownCameraController, Assembly-CSharp");

            if (ctrlType == null)
            {
                Add(Severity.Info, "未找到 TopdownCameraController 类型，跳过相机目标验证。", cam.gameObject, "camera");
                return;
            }

            var ctrl = cam.GetComponent(ctrlType);
            if (ctrl == null)
            {
                Add(Severity.Warning, "MainCamera 没有 TopdownCameraController 组件", cam.gameObject, "camera");
                return;
            }

            var targetField = ctrlType.GetField("target",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (targetField != null)
            {
                var targetVal = targetField.GetValue(ctrl) as Transform;
                if (targetVal == null)
                    Add(Severity.Warning, "TopdownCameraController.target 未绑定，相机不会跟随角色", cam.gameObject, "camera");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 工具方法
        // ─────────────────────────────────────────────────────────────────

        private void Add(Severity sv, string msg, Object target, string group)
        {
            _issues.Add(new Issue { severity = sv, message = msg, target = target, group = group });
        }
    }
}
#endif
