#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Game.Character;

namespace Game.Character.Editor
{
    /// <summary>
    /// Trap / Hazard 配置工具
    ///
    /// 一键为选中物体配置摆锤/机关击退系统：
    ///   - 根节点：Rigidbody (kinematic) + vPendulum
    ///   - 锤头子节点：BoxCollider (trigger) + PendulumHitForwarder
    ///
    /// 菜单：Tools > Level Kit > Trap Hazard Setup
    /// </summary>
    public class TrapHazardSetupTool : EditorWindow
    {
        // ── 参数 ──────────────────────────────────────────────────────────
        private GameObject _target;
        private GameObject _hammerChild;   // 锤头子节点（可自动查找）
        private bool       _autoFindHammer = true;

        private float _knockbackForce    = 15f;
        private float _knockbackUpForce  = 3f;
        private float _knockbackDuration = 0.6f;
        private float _hitCooldown       = 0.8f;

        private Vector3 _colliderCenter = new Vector3(0, 0, 0);
        private Vector3 _colliderSize   = new Vector3(0.5f, 0.5f, 0.5f);

        private Vector2 _scroll;

        // ─────────────────────────────────────────────────────────────────

        [MenuItem("Tools/Level Kit/Trap Hazard Setup", false, 314)]
        static void Open()
        {
            var win = GetWindow<TrapHazardSetupTool>("Trap Hazard Setup");
            win.minSize = new Vector2(400, 480);
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {

            EditorGUILayout.LabelField("Trap / Hazard 配置工具", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "为选中的摆锤/机关物体自动配置 vPendulum + PendulumHitForwarder。\n" +
                "根节点添加击退逻辑，锤头子节点添加碰撞检测。",
                MessageType.Info);
            EditorGUILayout.Space(8);

            // ── 目标选择 ──
            EditorGUILayout.LabelField("目标物体", EditorStyles.boldLabel);
            _target = (GameObject)EditorGUILayout.ObjectField("根节点", _target, typeof(GameObject), true);

            if (_target == null)
            {
                // 跟随 Hierarchy 选择
                if (Selection.activeGameObject != null)
                    _target = Selection.activeGameObject;
            }

            EditorGUILayout.Space(6);

            // ── 锤头子节点 ──
            EditorGUILayout.LabelField("锤头节点（碰撞检测）", EditorStyles.boldLabel);
            _autoFindHammer = EditorGUILayout.Toggle("自动查找锤头", _autoFindHammer);

            if (_autoFindHammer)
            {
                EditorGUI.BeginDisabledGroup(true);
                string hint = _target != null ? AutoFindHammerHint(_target) : "—";
                EditorGUILayout.TextField("候选节点", hint);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.HelpBox("自动查找：优先选用名含 'head'/'hammer'/'hit'/'bob' 的子节点；找不到则用末端子节点。", MessageType.None);
            }
            else
            {
                _hammerChild = (GameObject)EditorGUILayout.ObjectField("锤头节点", _hammerChild, typeof(GameObject), true);
            }

            EditorGUILayout.Space(8);

            // ── 击退参数 ──
            EditorGUILayout.LabelField("击退参数", EditorStyles.boldLabel);
            _knockbackForce    = EditorGUILayout.FloatField("击退力  knockbackForce",    _knockbackForce);
            _knockbackUpForce  = EditorGUILayout.FloatField("上弹力  knockbackUpForce",  _knockbackUpForce);
            _knockbackDuration = EditorGUILayout.FloatField("持续时间 knockbackDuration", _knockbackDuration);
            _hitCooldown       = EditorGUILayout.FloatField("冷却时间 hitCooldown",       _hitCooldown);

            EditorGUILayout.Space(8);

            // ── 锤头碰撞体 ──
            EditorGUILayout.LabelField("锤头 BoxCollider 参数", EditorStyles.boldLabel);
            _colliderCenter = EditorGUILayout.Vector3Field("Center", _colliderCenter);
            _colliderSize   = EditorGUILayout.Vector3Field("Size",   _colliderSize);

            EditorGUILayout.Space(12);

            // ── 组件状态预览 ──
            if (_target != null)
            {
                DrawStatusSection();
                EditorGUILayout.Space(8);
            }

            GUI.enabled = _target != null;
            if (GUILayout.Button("⚡  一键配置 Trap / Hazard", GUILayout.Height(38)))
                Execute();
            GUI.enabled = true;

            EditorGUILayout.Space(4);
            if (_target != null && GUILayout.Button("清除配置（移除组件）", GUILayout.Height(24)))
                RemoveComponents();

            }
            finally { EditorGUILayout.EndScrollView(); }
        }

        // ─────────────────────────────────────────────────────────────────
        // 执行
        // ─────────────────────────────────────────────────────────────────

        private void Execute()
        {
            if (_target == null) return;

            Undo.RegisterFullObjectHierarchyUndo(_target, "Setup Trap Hazard");

            // 1. 根节点：Rigidbody（kinematic，摆锤用动画驱动）
            var rb = _target.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(_target);
            rb.isKinematic = true;
            rb.useGravity  = false;

            // 2. 根节点：vPendulum
            var pendulum = _target.GetComponent<vPendulum>();
            if (pendulum == null) pendulum = Undo.AddComponent<vPendulum>(_target);
            pendulum.knockbackForce    = _knockbackForce;
            pendulum.knockbackUpForce  = _knockbackUpForce;
            pendulum.knockbackDuration = _knockbackDuration;
            pendulum.hitCooldown       = _hitCooldown;

            // 3. 找到锤头子节点
            GameObject hammerGO = GetOrFindHammer();
            if (hammerGO == null)
            {
                EditorUtility.DisplayDialog("错误",
                    "未找到锤头子节点。\n请手动指定或确保根节点有子节点。", "OK");
                return;
            }

            // 4. 锤头：BoxCollider (trigger)
            var col = hammerGO.GetComponent<BoxCollider>();
            if (col == null) col = Undo.AddComponent<BoxCollider>(hammerGO);
            col.isTrigger = true;
            col.center    = _colliderCenter;
            col.size      = _colliderSize;

            // 5. 锤头：PendulumHitForwarder
            var fwd = hammerGO.GetComponent<PendulumHitForwarder>();
            if (fwd == null) fwd = Undo.AddComponent<PendulumHitForwarder>(hammerGO);

            // 6. PendulumHitForwarder 关联根节点 vPendulum
            var fwdSo = new SerializedObject(fwd);
            var pendulumProp = fwdSo.FindProperty("pendulum");
            if (pendulumProp != null)
            {
                pendulumProp.objectReferenceValue = pendulum;
                fwdSo.ApplyModifiedProperties();
            }

            // 7. 确保锤头 Layer 与根节点一致，避免触发器失效
            hammerGO.layer = _target.layer;

            EditorUtility.SetDirty(_target);
            EditorUtility.SetDirty(hammerGO);

            Debug.Log($"[TrapHazardSetup] 配置完成：根={_target.name}，锤头={hammerGO.name}");
            EditorUtility.DisplayDialog("完成",
                $"Trap 配置完成！\n\n" +
                $"根节点：{_target.name}\n" +
                $"  + Rigidbody (kinematic)\n" +
                $"  + vPendulum (force={_knockbackForce}, cd={_hitCooldown}s)\n\n" +
                $"锤头节点：{hammerGO.name}\n" +
                $"  + BoxCollider (trigger)\n" +
                $"  + PendulumHitForwarder",
                "OK");
        }

        private void RemoveComponents()
        {
            if (_target == null) return;
            if (!EditorUtility.DisplayDialog("确认",
                "将移除根节点上的 vPendulum + Rigidbody，\n以及锤头节点上的 PendulumHitForwarder + BoxCollider。",
                "确认", "取消")) return;

            Undo.RegisterFullObjectHierarchyUndo(_target, "Remove Trap Components");

            TryDestroy(_target.GetComponent<vPendulum>());
            TryDestroy(_target.GetComponent<Rigidbody>());

            var hammer = GetOrFindHammer();
            if (hammer != null)
            {
                TryDestroy(hammer.GetComponent<PendulumHitForwarder>());
                TryDestroy(hammer.GetComponent<BoxCollider>());
            }

            EditorUtility.SetDirty(_target);
            Debug.Log("[TrapHazardSetup] 已移除 Trap 组件");
        }

        // ─────────────────────────────────────────────────────────────────
        // 状态预览
        // ─────────────────────────────────────────────────────────────────

        private void DrawStatusSection()
        {
            EditorGUILayout.LabelField("当前组件状态", EditorStyles.boldLabel);

            bool hasPendulum  = _target.GetComponent<vPendulum>()  != null;
            bool hasRb        = _target.GetComponent<Rigidbody>()   != null;
            var ham = GetOrFindHammer();
            bool hasForwarder = ham != null && ham.GetComponent<PendulumHitForwarder>() != null;
            bool hasCollider  = ham != null && ham.GetComponent<BoxCollider>()          != null;

            DrawRow("vPendulum",             hasPendulum);
            DrawRow("Rigidbody (kinematic)", hasRb);
            DrawRow("PendulumHitForwarder",  hasForwarder);
            DrawRow("BoxCollider (trigger)", hasCollider);
        }

        private static void DrawRow(string label, bool ok)
        {
            EditorGUILayout.BeginHorizontal();
            var style = ok ? EditorStyles.boldLabel : EditorStyles.label;
            EditorGUILayout.LabelField(ok ? "✓" : "✗", style, GUILayout.Width(18));
            EditorGUILayout.LabelField(label);
            EditorGUILayout.EndHorizontal();
        }

        // ─────────────────────────────────────────────────────────────────
        // 辅助
        // ─────────────────────────────────────────────────────────────────

        private GameObject GetOrFindHammer()
        {
            if (!_autoFindHammer) return _hammerChild;
            if (_target == null)  return null;
            return AutoFindHammer(_target);
        }

        private static string AutoFindHammerHint(GameObject root)
        {
            var go = AutoFindHammer(root);
            return go != null ? go.name : "未找到";
        }

        private static GameObject AutoFindHammer(GameObject root)
        {
            string[] keywords = { "head", "hammer", "hit", "bob", "ball", "weight", "strike" };
            return FindByKeyword(root.transform, keywords)?.gameObject
                ?? FindDeepestChild(root.transform)?.gameObject;
        }

        private static Transform FindByKeyword(Transform t, string[] kws)
        {
            foreach (Transform child in t)
            {
                string n = child.name.ToLower();
                foreach (var kw in kws)
                    if (n.Contains(kw)) return child;
                var found = FindByKeyword(child, kws);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindDeepestChild(Transform t)
        {
            if (t.childCount == 0) return t;
            // 找层级最深的那条链的末端
            Transform deepest = null;
            int maxDepth = -1;
            FindDeepest(t, 0, ref deepest, ref maxDepth);
            return deepest == t ? null : deepest; // 排除根本身
        }

        private static void FindDeepest(Transform t, int depth, ref Transform deepest, ref int maxDepth)
        {
            if (t.childCount == 0)
            {
                if (depth > maxDepth) { maxDepth = depth; deepest = t; }
                return;
            }
            foreach (Transform c in t)
                FindDeepest(c, depth + 1, ref deepest, ref maxDepth);
        }

        private static void TryDestroy(Component c)
        {
            if (c != null) try { Undo.DestroyObjectImmediate(c); } catch { }
        }

        void OnSelectionChange()
        {
            if (_target == null && Selection.activeGameObject != null)
            {
                _target = Selection.activeGameObject;
                Repaint();
            }
        }
    }
}
#endif
