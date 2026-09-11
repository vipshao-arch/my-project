#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using System.Text;
using Game.Character;

namespace Game.Character.Editor
{
    /// <summary>
    /// 敌人 AI 运行时诊断工具
    ///
    /// 在 Play 模式下实时显示 EnemyAI 状态机内部状态：
    ///   - 当前大状态（Idle/Chase/Attack/Return/Dead）
    ///   - 攻击子阶段（Windup/Hit/Recovery/Done）+ 累计计时
    ///   - 位置锁定标志 + 锁定坐标
    ///   - NavMeshAgent 状态（enabled/onNavMesh/velocity/desiredVelocity）
    ///   - Animator 参数（Speed / Attack trigger / AttackIndex / IsDead）
    ///   - 到玩家的距离 vs attackRange/detectionRange
    ///
    /// 菜单：Tools > Character System > Diagnostics > Enemy AI Diagnostic
    /// </summary>
    public class EnemyAIDiagnostic : EditorWindow
    {
        // ─────────────────────────────────────────────────────────────────
        // 字段
        // ─────────────────────────────────────────────────────────────────

        private EnemyAI   _targetAI;
        private EnemyMotor _targetMotor;
        private Vector2   _scroll;
        private bool      _autoRefresh   = true;
        private float     _refreshRate   = 0.1f;   // 每 0.1s 刷新一次
        private double    _lastRefreshTime;

        // 反射获取 EnemyAI 私有字段
        private System.Reflection.FieldInfo _fi_attackPhase;
        private System.Reflection.FieldInfo _fi_attackElapsed;
        private System.Reflection.FieldInfo _fi_attackLockActive;
        private System.Reflection.FieldInfo _fi_attackLockedPos;
        private System.Reflection.FieldInfo _fi_recoveryStartPos;
        private System.Reflection.FieldInfo _fi_currentAttackIndex;
        private System.Reflection.FieldInfo _fi_attackTimer;
        private System.Reflection.FieldInfo _fi_target;

        // EnemyMotor 私有字段
        private System.Reflection.FieldInfo _mfi_hitStunTimer;
        private System.Reflection.FieldInfo _mfi_isKnockedBack;
        private System.Reflection.FieldInfo _mfi_suppressSpeed;
        private System.Reflection.FieldInfo _mfi_suppressTimer;

        // ─────────────────────────────────────────────────────────────────

        // 菜单入口已移除：由 Character Kit 流水线步骤⑥ 通过 GetWindow 直接打开本窗口。
        static void Open()
        {
            var win = GetWindow<EnemyAIDiagnostic>("Enemy AI Diagnostic");
            win.minSize = new Vector2(400, 580);
        }

        void OnEnable()
        {
            CacheReflectionFields();
        }

        void Update()
        {
            // 自动刷新：每 0.1s Repaint 一次（仅 Play 模式）
            if (_autoRefresh && Application.isPlaying)
            {
                if (EditorApplication.timeSinceStartup - _lastRefreshTime >= _refreshRate)
                {
                    _lastRefreshTime = EditorApplication.timeSinceStartup;
                    Repaint();
                }
            }
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Enemy AI 运行时诊断", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "需在 Play 模式下使用。选择场景中的 EnemyAI 或拖入 Inspector。\n" +
                "实时显示状态机内部字段，帮助定位攻击/移动切换问题。",
                Application.isPlaying ? MessageType.Info : MessageType.Warning);

            EditorGUILayout.Space(6);

            // 目标选择
            var newAI = (EnemyAI)EditorGUILayout.ObjectField(
                "目标 EnemyAI", _targetAI, typeof(EnemyAI), true);
            if (newAI != _targetAI)
            {
                _targetAI    = newAI;
                _targetMotor = newAI != null ? newAI.GetComponent<EnemyMotor>() : null;
            }

            // 自动查找
            if (_targetAI == null && Application.isPlaying)
            {
                _targetAI    = FindObjectOfType<EnemyAI>();
                _targetMotor = _targetAI != null ? _targetAI.GetComponent<EnemyMotor>() : null;
            }

            // 刷新控制
            EditorGUILayout.BeginHorizontal();
            _autoRefresh = EditorGUILayout.Toggle("自动刷新", _autoRefresh, GUILayout.Width(100));
            _refreshRate = EditorGUILayout.Slider(_refreshRate, 0.05f, 1f);
            EditorGUILayout.LabelField("s/次", GUILayout.Width(28));
            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("请进入 Play 模式后查看实时状态。", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (_targetAI == null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("场景中未找到 EnemyAI，请手动拖入。", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {

            DrawStateSection();
            EditorGUILayout.Space(6);
            DrawAttackSection();
            EditorGUILayout.Space(6);
            DrawNavMeshSection();
            EditorGUILayout.Space(6);
            DrawAnimatorSection();
            EditorGUILayout.Space(6);
            DrawMotorSection();
            EditorGUILayout.Space(6);
            DrawDistanceSection();
            EditorGUILayout.Space(6);
            DrawManualTestSection();

            }
            finally { EditorGUILayout.EndScrollView(); }
        }

        // ─────────────────────────────────────────────────────────────────
        // 各分区绘制
        // ─────────────────────────────────────────────────────────────────

        private void DrawStateSection()
        {
            EditorGUILayout.LabelField("── 大状态 ──────────────────────────", EditorStyles.boldLabel);

            var state = _targetAI.CurrentState;
            var stateColor = state switch
            {
                EnemyAI.State.Attack => Color.red,
                EnemyAI.State.Chase  => new Color(1f, 0.6f, 0f),
                EnemyAI.State.Dead   => Color.gray,
                _                   => Color.green,
            };

            var oldColor = GUI.contentColor;
            GUI.contentColor = stateColor;
            EditorGUILayout.LabelField("CurrentState", state.ToString(), EditorStyles.boldLabel);
            GUI.contentColor = oldColor;
        }

        private void DrawAttackSection()
        {
            EditorGUILayout.LabelField("── 攻击阶段 ─────────────────────────", EditorStyles.boldLabel);

            if (_fi_attackPhase == null) { EditorGUILayout.HelpBox("反射字段未找到（字段名已更改？）", MessageType.Warning); return; }

            var phase        = _fi_attackPhase.GetValue(_targetAI);
            float elapsed    = (float)(_fi_attackElapsed?.GetValue(_targetAI) ?? 0f);
            bool lockActive  = (bool)(_fi_attackLockActive?.GetValue(_targetAI) ?? false);
            Vector3 lockedPos= (Vector3)(_fi_attackLockedPos?.GetValue(_targetAI) ?? Vector3.zero);
            Vector3 recPos   = (Vector3)(_fi_recoveryStartPos?.GetValue(_targetAI) ?? Vector3.zero);
            int atkIdx       = (int)(_fi_currentAttackIndex?.GetValue(_targetAI) ?? 0);
            float atkTimer   = (float)(_fi_attackTimer?.GetValue(_targetAI) ?? 0f);

            // 攻击阶段颜色
            var phaseStr  = phase?.ToString() ?? "?";
            var phaseColor = phaseStr switch
            {
                "Windup"   => new Color(1f, 0.8f, 0f),
                "Hit"      => Color.red,
                "Recovery" => new Color(1f, 0.5f, 0f),
                "Done"     => Color.cyan,
                _          => Color.white,
            };

            var oldColor = GUI.contentColor;
            GUI.contentColor = phaseColor;
            EditorGUILayout.LabelField("AttackPhase", phaseStr, EditorStyles.boldLabel);
            GUI.contentColor = oldColor;

            EditorGUILayout.LabelField("_attackElapsed",       $"{elapsed:F3} s");
            EditorGUILayout.LabelField("_attackTimer (CD剩余)", $"{atkTimer:F3} s");
            EditorGUILayout.LabelField("_attackLockActive",    lockActive.ToString());
            if (lockActive)
                EditorGUILayout.LabelField("_attackLockedPos",     lockedPos.ToString("F3"));
            EditorGUILayout.LabelField("_recoveryStartPos",    recPos.ToString("F3"));
            EditorGUILayout.LabelField("_currentAttackIndex",  atkIdx.ToString());

            // 攻击参数速查
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField($"参数：startup={_targetAI.attackStartUpTime:F2}  hit={_targetAI.attackHitDelay:F2}  recover={_targetAI.attackRecoverTime:F2}  lockRatio={_targetAI.lockRecoveryRatio:F2}",
                EditorStyles.miniLabel);
            float freeThreshold = _targetAI.attackStartUpTime + _targetAI.attackHitDelay +
                                  _targetAI.attackRecoverTime * Mathf.Clamp01(_targetAI.lockRecoveryRatio);
            EditorGUILayout.LabelField($"free window 起点：{freeThreshold:F3} s", EditorStyles.miniLabel);
        }

        private void DrawNavMeshSection()
        {
            EditorGUILayout.LabelField("── NavMeshAgent ─────────────────────", EditorStyles.boldLabel);

            var agent = _targetAI.GetComponent<NavMeshAgent>();
            if (agent == null) { EditorGUILayout.LabelField("无 NavMeshAgent"); return; }

            EditorGUILayout.LabelField("enabled",          agent.enabled.ToString());
            EditorGUILayout.LabelField("isOnNavMesh",      agent.isOnNavMesh.ToString());
            EditorGUILayout.LabelField("isStopped",        agent.enabled ? agent.isStopped.ToString() : "N/A（disabled）");
            EditorGUILayout.LabelField("velocity",         agent.enabled ? agent.velocity.ToString("F3") : "N/A");
            EditorGUILayout.LabelField("desiredVelocity",  agent.enabled && agent.isOnNavMesh ? agent.desiredVelocity.ToString("F3") : "N/A");
            EditorGUILayout.LabelField("speed",            $"{agent.speed:F2}");
            EditorGUILayout.LabelField("nextPosition",     agent.enabled ? agent.nextPosition.ToString("F3") : "N/A");
        }

        private void DrawAnimatorSection()
        {
            EditorGUILayout.LabelField("── Animator 参数 ──────────────────────", EditorStyles.boldLabel);

            var anim = _targetAI.GetComponentInChildren<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null)
            {
                EditorGUILayout.LabelField("无 Animator / 未绑定 Controller");
                return;
            }

            TryShowFloat(anim,  "Speed");
            TryShowBool(anim,   "IsDead");
            TryShowInt(anim,    "AttackIndex");
            TryShowTrigger(anim,"Attack");
            TryShowTrigger(anim,"Hit");

            // 当前状态名
            if (anim.layerCount > 0)
            {
                var si = anim.GetCurrentAnimatorStateInfo(0);
                EditorGUILayout.LabelField("当前状态 hash", si.shortNameHash.ToString());
                EditorGUILayout.LabelField("normalizedTime", $"{si.normalizedTime:F3}");
            }
        }

        private void DrawMotorSection()
        {
            EditorGUILayout.LabelField("── EnemyMotor ────────────────────────", EditorStyles.boldLabel);

            if (_targetMotor == null) { EditorGUILayout.LabelField("无 EnemyMotor"); return; }

            EditorGUILayout.LabelField("currentHealth",  $"{_targetMotor.currentHealth:F1} / {_targetMotor.maxHealth:F1}");
            EditorGUILayout.LabelField("isDead",         _targetMotor.isDead.ToString());
            EditorGUILayout.LabelField("IsStunned",      _targetMotor.IsStunned.ToString());

            if (_mfi_hitStunTimer != null)
                EditorGUILayout.LabelField("_hitStunTimer",  $"{(float)_mfi_hitStunTimer.GetValue(_targetMotor):F3} s");
            if (_mfi_isKnockedBack != null)
                EditorGUILayout.LabelField("_isKnockedBack", _mfi_isKnockedBack.GetValue(_targetMotor).ToString());
            if (_mfi_suppressSpeed != null)
                EditorGUILayout.LabelField("_suppressSpeedUpdate", _mfi_suppressSpeed.GetValue(_targetMotor).ToString());
            if (_mfi_suppressTimer != null)
                EditorGUILayout.LabelField("_suppressSpeedTimer", $"{(float)_mfi_suppressTimer.GetValue(_targetMotor):F3} s");
        }

        private void DrawDistanceSection()
        {
            EditorGUILayout.LabelField("── 距离检测 ──────────────────────────", EditorStyles.boldLabel);

            var targetTf = _fi_target?.GetValue(_targetAI) as Transform;
            if (targetTf == null)
            {
                EditorGUILayout.LabelField("_target", "(null，未检测到玩家)");
                return;
            }

            float dist = Vector3.Distance(_targetAI.transform.position, targetTf.position);
            EditorGUILayout.LabelField("_target",          targetTf.name);
            EditorGUILayout.LabelField("距离",             $"{dist:F3} m");
            EditorGUILayout.LabelField("attackRange",      $"{_targetAI.attackRange:F2} m   (1.2x = {_targetAI.attackRange * 1.2f:F2} m)");
            EditorGUILayout.LabelField("detectionRange",   $"{_targetAI.detectionRange:F2} m");
            EditorGUILayout.LabelField("loseTargetRange",  $"{_targetAI.loseTargetRange:F2} m");

            // 距离状态可视化
            string status;
            if (dist <= _targetAI.attackRange)              status = "✅ 攻击范围内";
            else if (dist <= _targetAI.detectionRange)      status = "🔍 检测范围内（追踪）";
            else if (dist <= _targetAI.loseTargetRange)     status = "⚠ 超出检测但未脱仇恨";
            else                                             status = "❌ 已脱仇恨范围";
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void DrawManualTestSection()
        {
            EditorGUILayout.LabelField("── 手动测试 ──────────────────────────", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("施加 25 伤害", GUILayout.Height(26)) && _targetMotor != null)
                _targetMotor.TakeDamage(25f, null, Vector3.forward);

            if (GUILayout.Button("施加 200 伤害（击杀）", GUILayout.Height(26)) && _targetMotor != null)
                _targetMotor.TakeDamage(200f, null, Vector3.forward);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("ReceiveKnockback（向后）", GUILayout.Height(26)) && _targetMotor != null)
                (_targetMotor as IHittable)?.ReceiveKnockback(Vector3.back * 5f, 0.4f);
        }

        // ─────────────────────────────────────────────────────────────────
        // 工具方法
        // ─────────────────────────────────────────────────────────────────

        private void CacheReflectionFields()
        {
            var aiType = typeof(EnemyAI);
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            _fi_attackPhase        = aiType.GetField("_attackPhase",        bf);
            _fi_attackElapsed      = aiType.GetField("_attackElapsed",      bf);
            _fi_attackLockActive   = aiType.GetField("_attackLockActive",   bf);
            _fi_attackLockedPos    = aiType.GetField("_attackLockedPos",    bf);
            _fi_recoveryStartPos   = aiType.GetField("_recoveryStartPos",   bf);
            _fi_currentAttackIndex = aiType.GetField("_currentAttackIndex", bf);
            _fi_attackTimer        = aiType.GetField("_attackTimer",        bf);
            _fi_target             = aiType.GetField("_target",             bf);

            var motorType = typeof(EnemyMotor);
            _mfi_hitStunTimer  = motorType.GetField("_hitStunTimer",        bf);
            _mfi_isKnockedBack = motorType.GetField("_isKnockedBack",       bf);
            _mfi_suppressSpeed = motorType.GetField("_suppressSpeedUpdate", bf);
            _mfi_suppressTimer = motorType.GetField("_suppressSpeedTimer",  bf);
        }

        private static void TryShowFloat(Animator anim, string param)
        {
            try { EditorGUILayout.LabelField(param, $"{anim.GetFloat(param):F3}"); }
            catch { EditorGUILayout.LabelField(param, "(参数不存在)"); }
        }

        private static void TryShowBool(Animator anim, string param)
        {
            try { EditorGUILayout.LabelField(param, anim.GetBool(param).ToString()); }
            catch { EditorGUILayout.LabelField(param, "(参数不存在)"); }
        }

        private static void TryShowInt(Animator anim, string param)
        {
            try { EditorGUILayout.LabelField(param, anim.GetInteger(param).ToString()); }
            catch { EditorGUILayout.LabelField(param, "(参数不存在)"); }
        }

        private static void TryShowTrigger(Animator anim, string param)
        {
            // Trigger 无法直接查 GetTrigger，只能显示 hash 是否存在
            int hash = Animator.StringToHash(param);
            bool hasParam = false;
            foreach (var p in anim.parameters)
            {
                if (p.nameHash == hash) { hasParam = true; break; }
            }
            EditorGUILayout.LabelField($"Trigger [{param}]", hasParam ? "存在" : "(未配置)");
        }
    }
}
#endif
