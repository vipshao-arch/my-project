#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Game.Character;

namespace Game.Character.Editor
{
    /// <summary>
    /// Action Trigger 可视化配置工具
    ///
    /// 为场景中 CharacterActionTrigger 提供可读的参数界面，
    /// 并支持自动添加 Collider、预览对齐目标位置。
    ///
    /// 菜单：Tools > Level Kit > Action Trigger Setup
    /// </summary>
    public class ActionTriggerSetupTool : EditorWindow
    {
        private CharacterActionTrigger _trigger;
        private SerializedObject _so;
        private Vector2 _scroll;

        // 折叠状态
        private bool _foldBasic    = true;
        private bool _foldMatch    = true;
        private bool _foldMove     = true;
        private bool _foldEvents   = false;

        [MenuItem("Tools/Level Kit/Action Trigger Setup", false, 313)]
        static void Open()
        {
            var win = GetWindow<ActionTriggerSetupTool>("Action Trigger Setup");
            win.minSize = new Vector2(440, 520);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Action Trigger 配置工具", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // 目标选择
            var newTrigger = (CharacterActionTrigger)EditorGUILayout.ObjectField(
                "目标 Trigger", _trigger, typeof(CharacterActionTrigger), true);

            if (newTrigger != _trigger)
            {
                _trigger = newTrigger;
                _so = _trigger != null ? new SerializedObject(_trigger) : null;
            }

            // 跟随 Hierarchy 选择
            if (_trigger == null && Selection.activeGameObject != null)
            {
                var t = Selection.activeGameObject.GetComponent<CharacterActionTrigger>();
                if (t != null) { _trigger = t; _so = new SerializedObject(_trigger); }
            }

            if (_trigger == null)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.HelpBox(
                    "选择场景中挂有 CharacterActionTrigger 的物体，\n或点击下方按钮为选中物体添加组件。",
                    MessageType.Info);

                if (Selection.activeGameObject != null &&
                    GUILayout.Button($"为 [{Selection.activeGameObject.name}] 添加 ActionTrigger", GUILayout.Height(30)))
                {
                    Undo.AddComponent<CharacterActionTrigger>(Selection.activeGameObject);
                    _trigger = Selection.activeGameObject.GetComponent<CharacterActionTrigger>();
                    _so = new SerializedObject(_trigger);
                }
                return;
            }

            _so.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {

            DrawColliderStatus();
            EditorGUILayout.Space(6);

            // ── 基础参数 ──────────────────────────────────────────────────
            _foldBasic = EditorGUILayout.BeginFoldoutHeaderGroup(_foldBasic, "基础参数");
            if (_foldBasic)
            {
                EditorGUI.indentLevel++;

                DrawProp("playAnimation",   "动画状态名");
                DrawProp("autoAction",      "自动触发（进入即执行）");
                DrawProp("destroyAfter",    "执行后销毁");
                DrawPropConditional("destroyDelay", "destroyAfter", "  销毁延迟（秒）");
                DrawProp("onDoActionDelay", "触发延迟（秒）");
                DrawProp("resetPlayerSettings", "恢复角色设置");
                DrawProp("disableCollision","触发时关闭角色碰撞体");
                DrawProp("disableGravity",  "触发时关闭重力");

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // ── 对齐目标 ──────────────────────────────────────────────────
            _foldMatch = EditorGUILayout.BeginFoldoutHeaderGroup(_foldMatch, "Match Target 对齐");
            if (_foldMatch)
            {
                EditorGUI.indentLevel++;

                DrawProp("matchTarget",      "对齐目标 Transform");
                DrawProp("avatarTarget",     "骨骼对齐点（AvatarTarget）");
                DrawProp("matchTargetMask",  "轴向遮罩（xyz=1启用）");
                DrawProp("startMatchTarget", "对齐开始时间（0~1）");
                DrawProp("endMatchTarget",   "对齐结束时间（0~1）");
                DrawProp("endExitTimeAnimation", "动画退出时间（0~1）");

                // 可视化预览
                if (_trigger.matchTarget != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("  目标位置：", EditorStyles.miniLabel, GUILayout.Width(80));
                    EditorGUILayout.LabelField(_trigger.matchTarget.position.ToString("F3"), EditorStyles.miniLabel);
                    if (GUILayout.Button("聚焦", GUILayout.Width(42), GUILayout.Height(16)))
                        SceneView.lastActiveSceneView?.LookAt(_trigger.matchTarget.position);
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // ── 进入朝向/位移 ────────────────────────────────────────────
            _foldMove = EditorGUILayout.BeginFoldoutHeaderGroup(_foldMove, "朝向 & 退出速度");
            if (_foldMove)
            {
                EditorGUI.indentLevel++;
                DrawProp("activeFromForward",   "限定正面进入（面向触发器）");
                DrawProp("useTriggerRotation",  "使用触发器朝向对齐角色");
                DrawProp("snapPosition",        "进入时瞬移到的位置");
                DrawProp("exitSpeed",           "退出后初始速度（m/s）");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // ── 事件 ──────────────────────────────────────────────────────
            _foldEvents = EditorGUILayout.BeginFoldoutHeaderGroup(_foldEvents, "UnityEvents（可选）");
            if (_foldEvents)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_so.FindProperty("OnDoAction"),    new GUIContent("OnDoAction"));
                EditorGUILayout.PropertyField(_so.FindProperty("OnPlayerEnter"), new GUIContent("OnPlayerEnter"));
                EditorGUILayout.PropertyField(_so.FindProperty("OnPlayerStay"),  new GUIContent("OnPlayerStay"));
                EditorGUILayout.PropertyField(_so.FindProperty("OnPlayerExit"),  new GUIContent("OnPlayerExit"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(10);

            // ── 快速预设 ──────────────────────────────────────────────────
            EditorGUILayout.LabelField("快速预设", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("StepUp", GUILayout.Height(28)))
                ApplyPreset_StepUp();
            if (GUILayout.Button("JumpOver", GUILayout.Height(28)))
                ApplyPreset_JumpOver();
            if (GUILayout.Button("ClimbUp", GUILayout.Height(28)))
                ApplyPreset_ClimbUp();
            if (GUILayout.Button("重置", GUILayout.Height(28)))
                ApplyPreset_Reset();

            EditorGUILayout.EndHorizontal();

            }
            finally { EditorGUILayout.EndScrollView(); }

            if (_so.ApplyModifiedProperties())
                EditorUtility.SetDirty(_trigger);
        }

        // ─────────────────────────────────────────────────────────────────
        // Collider 状态行
        // ─────────────────────────────────────────────────────────────────

        private void DrawColliderStatus()
        {
            bool hasCol = _trigger.GetComponent<Collider>() != null;
            bool isTrig = hasCol && _trigger.GetComponent<Collider>().isTrigger;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                hasCol ? (isTrig ? "✓  Collider (trigger)" : "⚠  Collider（非 trigger）") : "✗  无 Collider",
                hasCol && isTrig ? EditorStyles.boldLabel : EditorStyles.label);

            if (!hasCol && GUILayout.Button("添加 BoxCollider", GUILayout.Width(110), GUILayout.Height(18)))
            {
                var col = Undo.AddComponent<BoxCollider>(_trigger.gameObject);
                col.isTrigger = true;
            }
            else if (hasCol && !isTrig && GUILayout.Button("设为 Trigger", GUILayout.Width(90), GUILayout.Height(18)))
            {
                Undo.RecordObject(_trigger.GetComponent<Collider>(), "Set Trigger");
                _trigger.GetComponent<Collider>().isTrigger = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─────────────────────────────────────────────────────────────────
        // 快速预设
        // ─────────────────────────────────────────────────────────────────

        private void ApplyPreset_StepUp()
        {
            Undo.RecordObject(_trigger, "Preset StepUp");
            _trigger.playAnimation      = "StepUp";
            _trigger.autoAction         = false;
            _trigger.activeFromForward  = true;
            _trigger.useTriggerRotation = true;
            _trigger.avatarTarget       = AvatarTarget.LeftHand;
            _trigger.matchTargetMask    = new Vector3(0f, 1f, 1f);
            _trigger.startMatchTarget   = 0.2f;
            _trigger.endMatchTarget     = 0.6f;
            _trigger.endExitTimeAnimation = 0.95f;
            _trigger.resetPlayerSettings  = true;
            _trigger.disableCollision     = true;
            _trigger.disableGravity       = true;
            _trigger.exitSpeed            = 0f;
            EditorUtility.SetDirty(_trigger);
            _so = new SerializedObject(_trigger);
        }

        private void ApplyPreset_JumpOver()
        {
            Undo.RecordObject(_trigger, "Preset JumpOver");
            _trigger.playAnimation      = "JumpOver";
            _trigger.autoAction         = false;
            _trigger.activeFromForward  = true;
            _trigger.useTriggerRotation = true;
            _trigger.avatarTarget       = AvatarTarget.LeftHand;
            _trigger.matchTargetMask    = new Vector3(0f, 1f, 1f);
            _trigger.startMatchTarget   = 0f;
            _trigger.endMatchTarget     = 0.3f;
            _trigger.endExitTimeAnimation = 0.95f;
            _trigger.resetPlayerSettings  = true;
            _trigger.disableCollision     = true;
            _trigger.disableGravity       = true;
            _trigger.exitSpeed            = 0f;
            EditorUtility.SetDirty(_trigger);
            _so = new SerializedObject(_trigger);
        }

        private void ApplyPreset_ClimbUp()
        {
            Undo.RecordObject(_trigger, "Preset ClimbUp");
            _trigger.playAnimation      = "ClimbUp";
            _trigger.autoAction         = false;
            _trigger.activeFromForward  = true;
            _trigger.useTriggerRotation = true;
            _trigger.avatarTarget       = AvatarTarget.LeftHand;
            _trigger.matchTargetMask    = new Vector3(0f, 1f, 1f);
            _trigger.startMatchTarget   = 0f;
            _trigger.endMatchTarget     = 0.3f;
            _trigger.endExitTimeAnimation = 0.95f;
            _trigger.resetPlayerSettings  = true;
            _trigger.disableCollision     = true;
            _trigger.disableGravity       = true;
            _trigger.exitSpeed            = 0f;
            EditorUtility.SetDirty(_trigger);
            _so = new SerializedObject(_trigger);
        }

        private void ApplyPreset_Reset()
        {
            Undo.RecordObject(_trigger, "Preset Reset");
            var fresh = ScriptableObject.CreateInstance<ScriptableObject>();
            // 仅重置常用参数
            _trigger.playAnimation        = "";
            _trigger.autoAction           = false;
            _trigger.activeFromForward    = false;
            _trigger.useTriggerRotation   = false;
            _trigger.matchTarget          = null;
            _trigger.startMatchTarget     = 0f;
            _trigger.endMatchTarget       = 1f;
            _trigger.endExitTimeAnimation = 0.8f;
            _trigger.exitSpeed            = 0f;
            _trigger.snapPosition         = null;
            _trigger.resetPlayerSettings  = true;
            _trigger.disableCollision     = true;
            _trigger.disableGravity       = true;
            DestroyImmediate(fresh);
            EditorUtility.SetDirty(_trigger);
            _so = new SerializedObject(_trigger);
        }

        // ─────────────────────────────────────────────────────────────────
        // 工具方法
        // ─────────────────────────────────────────────────────────────────

        private void DrawProp(string propName, string label)
        {
            var prop = _so.FindProperty(propName);
            if (prop != null)
                EditorGUILayout.PropertyField(prop, new GUIContent(label));
        }

        private void DrawPropConditional(string propName, string condPropName, string label)
        {
            var cond = _so.FindProperty(condPropName);
            if (cond != null && cond.boolValue)
                DrawProp(propName, label);
        }

        void OnSelectionChange()
        {
            if (Selection.activeGameObject != null)
            {
                var t = Selection.activeGameObject.GetComponent<CharacterActionTrigger>();
                if (t != null && t != _trigger)
                {
                    _trigger = t;
                    _so = new SerializedObject(_trigger);
                    Repaint();
                }
            }
        }
    }
}
#endif
