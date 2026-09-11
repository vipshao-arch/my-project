#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Game.Character;

namespace Game.Character.Editor
{
    /// <summary>
    /// Action 资产快速搭建工具。
    ///
    /// 一键在场景中新建并设置 4 种交互资产：
    ///   • stepup   → CharacterActionTrigger, playAnimation = "StepUp"
    ///   • jumpover → CharacterActionTrigger, playAnimation = "JumpOver"
    ///   • climbup  → CharacterActionTrigger, playAnimation = "ClimbUp"
    ///   • ladder   → CharacterLadderTrigger（爬梯，配 ClimbLadder）
    ///
    /// 菜单：Tools/Level Kit/Create StepUp | JumpOver | ClimbUp | Ladder
    /// 窗口：Tools/Level Kit/Action Asset Setup
    /// </summary>
    public class ActionAssetSetupTool : EditorWindow
    {
        [MenuItem("Tools/Level Kit/Action Asset Setup", false, 310)]
        static void Open()
        {
            GetWindow<ActionAssetSetupTool>("Action Asset Setup");
        }

        [MenuItem("Tools/Level Kit/Create StepUp", false, 320)]
        static void CreateStepUp() => CreateActionAsset("stepup", "StepUp", 0.2f, 0.6f);

        [MenuItem("Tools/Level Kit/Create JumpOver", false, 321)]
        static void CreateJumpOver() => CreateActionAsset("jumpover", "JumpOver", 0f, 0.3f);

        [MenuItem("Tools/Level Kit/Create ClimbUp", false, 322)]
        static void CreateClimbUp() => CreateActionAsset("climbup", "ClimbUp", 0f, 0.3f);

        [MenuItem("Tools/Level Kit/Create Ladder", false, 323)]
        static void CreateLadder() => CreateLadderAsset();

        [MenuItem("Tools/Level Kit/Auto Create Actions From Selection", false, 330)]
        static void AutoCreateFromSelection() => AutoCreateActionsFromSelection();

        // ─── 通用 Action 资产（stepup / jumpover / climbup）───────────────

        static void CreateActionAsset(string name, string anim, float startMT, float endMT)
        {
            Vector3 pos = GetSpawnPosition();
            GameObject root = new GameObject(name);
            root.transform.position = pos;

            Undo.RegisterCreatedObjectUndo(root, "Create " + name);

            // 1. BoxCollider(trigger)
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(1f, 1f, 1f);

            // 2. CharacterActionTrigger
            var trigger = root.AddComponent<CharacterActionTrigger>();
            trigger.playAnimation = anim;
            trigger.autoAction = false;                    // 按键触发（E）
            trigger.disableCollision = true;               // 翻越时禁用角色胶囊体，避免撞障碍物（顿挫）
            trigger.disableGravity = true;
            trigger.resetPlayerSettings = true;
            trigger.endExitTimeAnimation = 0.95f;          // 让动画几乎播完再退出，避免翻越尾段被截断
            trigger.avatarTarget = AvatarTarget.LeftHand;  // 手撑点匹配
            trigger.matchTargetMask = new Vector3(0f, 1f, 1f);
            trigger.startMatchTarget = startMT;
            trigger.endMatchTarget = endMT;
            trigger.useTriggerRotation = true;
            trigger.activeFromForward = true;
            trigger.exitSpeed = 0f;

            // 3. matchTarget 子节点（手撑点 / 对齐目标）
            var target = new GameObject("target");
            target.transform.SetParent(root.transform, false);
            target.transform.localPosition = new Vector3(0f, 0.65f, 0.6f);
            trigger.matchTarget = target.transform;

            Selection.activeGameObject = root;
            Debug.Log($"[ActionAssetSetup] 已创建 '{name}'（playAnimation={anim}）。请按实际障碍物调整 BoxCollider 尺寸和 target 子节点位置。");
        }

        // ─── 梯子资产 ───────────────────────────────────────────────────

        static void CreateLadderAsset()
        {
            Vector3 pos = GetSpawnPosition();
            GameObject root = new GameObject("ladder");
            root.transform.position = pos;

            Undo.RegisterCreatedObjectUndo(root, "Create ladder");

            // 挂梯侧统一在局部 -Z（matchTarget 在 -Z，保证上下锚点 XZ 一致、挂梯轴垂直）
            const float climbZ = -0.2f;

            // ── 底部：EnterLadderBottom ──
            GameObject bottom = new GameObject("EnterLadderBottom");
            bottom.transform.SetParent(root.transform, false);
            bottom.transform.localPosition = Vector3.zero;
            SetupLadderNode(bottom, "EnterLadderBottom", "ExitLadderBottom", climbZ, reverseEnter: false);

            // ── 顶部：ExitLadderTop ──
            GameObject top = new GameObject("ExitLadderTop");
            top.transform.SetParent(root.transform, false);
            top.transform.localPosition = new Vector3(0f, 3f, 0f);
            SetupLadderNode(top, "EnterLadderTop", "ExitLadderTop", climbZ, reverseEnter: true);

            Selection.activeGameObject = root;
            Debug.Log($"[ActionAssetSetup] 已创建 'ladder'（底部 0m / 顶部 3m）。请按实际高度调整 ExitLadderTop 的 localPosition.y 和两个 matchTarget。");
        }

        static void SetupLadderNode(GameObject node, string playAnim, string exitAnim, float climbZ, bool reverseEnter)
        {
            node.tag = "LadderTrigger";

            // BoxCollider(trigger)
            var col = node.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(1f, 0.4f, 0.6f);

            // CharacterLadderTrigger
            var trigger = node.AddComponent<CharacterLadderTrigger>();
            trigger.playAnimation = playAnim;
            trigger.exitAnimation = exitAnim;
            trigger.autoAction = true;
            trigger.activeFromForward = true;
            trigger.useTriggerRotation = true;
            trigger.reverseEnterForward = reverseEnter;
            trigger.isEntryTrigger = true;
            trigger.isExitTrigger = true;
            trigger.endpointHeightOffset = 0f;

            // matchTarget 子节点（挂梯锚点，XZ 在挂梯侧 climbZ，保证上下锚点 XZ 一致）
            string mtName = node.name == "EnterLadderBottom" ? "matchTargetBottom" : "matchTargetTop";
            GameObject mt = new GameObject(mtName);
            mt.transform.SetParent(node.transform, false);
            // 底部：锚点=节点高度；顶部：锚点在节点下方 1m（挂梯位置低于顶部平台边缘）
            float mtY = node.name == "EnterLadderBottom" ? 0f : -1f;
            mt.transform.localPosition = new Vector3(0f, mtY, climbZ);
            trigger.matchTarget = mt.transform;

            // stair 子节点（实际进出触发区）
            string stairName = node.name == "EnterLadderBottom" ? "stair_start" : "stair_end";
            GameObject stair = new GameObject(stairName);
            stair.transform.SetParent(node.transform, false);
            stair.transform.localPosition = new Vector3(0f, 0f, -0.3f);
            var stairCol = stair.AddComponent<BoxCollider>();
            stairCol.isTrigger = true;
            stairCol.size = new Vector3(0.5f, 0.2f, 0.5f);
        }

        // ─── 从选中物体自动创建 Action（按高度/形态判定）─────────────────

        enum ActionKind { StepUp, JumpOver, ClimbUp, Ladder }

        /// <summary>
        /// 主入口：为当前 Hierarchy 选中的所有物体，按高度与形态自动创建 StepUp/JumpOver/ClimbUp/Ladder。
        /// 判定规则：
        ///   细长竖杆（高 &gt; 1.8m 且 高/宽 &gt; 2.5）→ Ladder
        ///   高 ≤ 0.6m  → StepUp（走上矮台阶/平台）
        ///   高 ≤ 1.5m  → JumpOver（翻越中障碍）
        ///   其余        → ClimbUp（攀爬高墙）
        /// </summary>
        static void AutoCreateActionsFromSelection()
        {
            GameObject[] selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
            {
                Debug.LogWarning("[ActionAssetSetup] 请先在 Hierarchy 选中一个或多个障碍物/平台物体。");
                return;
            }

            int created = 0;
            foreach (var src in selection)
            {
                if (src == null) continue;
                if (CreateActionForObject(src)) created++;
            }

            if (created == 0)
                Debug.LogWarning("[ActionAssetSetup] 未创建任何 Action：选中物体需要有 Collider 或 Renderer 来确定尺寸。");
            else
                Debug.Log($"[ActionAssetSetup] 已为 {created}/{selection.Length} 个物体自动创建 Action 资产（Undo 可撤销）。");
        }

        static bool CreateActionForObject(GameObject src)
        {
            Bounds b = GetObjectBounds(src);
            if (b.size.sqrMagnitude <= 0.0001f) return false;

            ActionKind kind = Classify(b);
            Debug.Log($"[ActionAssetSetup] '{src.name}' → {kind}（尺寸 {b.size.x:F2}×{b.size.y:F2}×{b.size.z:F2}）");

            if (kind == ActionKind.Ladder)
                CreateLadderFromBounds(b);
            else
                CreateActionFromBounds(b, kind);

            return true;
        }

        static Bounds GetObjectBounds(GameObject go)
        {
            bool has = false;
            Bounds total = new Bounds(go.transform.position, Vector3.zero);

            // 优先物理碰撞体（跳过 trigger，避免把已有 Action 的触发区算进去）
            var cols = go.GetComponentsInChildren<Collider>();
            foreach (var c in cols)
            {
                if (c.isTrigger) continue;
                if (!has) { total = c.bounds; has = true; }
                else total.Encapsulate(c.bounds);
            }

            // 无碰撞体时退回 Renderer 包围盒
            if (!has)
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (!has) { total = r.bounds; has = true; }
                    else total.Encapsulate(r.bounds);
                }
            }
            return total;
        }

        static ActionKind Classify(Bounds b)
        {
            float h = b.size.y;
            float w = Mathf.Max(b.size.x, b.size.z);

            // 细长竖杆/梯子形态
            if (h > 1.8f && w > 0.01f && h / w > 2.5f)
                return ActionKind.Ladder;

            if (h <= 0.6f)  return ActionKind.StepUp;
            if (h <= 1.5f)  return ActionKind.JumpOver;
            return ActionKind.ClimbUp;
        }

        /// <summary>从障碍物指向 SceneView 相机的水平方向（触发器将放在这一侧）。</summary>
        static Vector3 GetApproachDirection(Bounds b)
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
            {
                Vector3 toCam = view.camera.transform.position - b.center;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 0.001f) return toCam.normalized;
            }
            return Vector3.back;
        }

        static void CreateActionFromBounds(Bounds b, ActionKind kind)
        {
            string anim = kind switch
            {
                ActionKind.StepUp   => "StepUp",
                ActionKind.JumpOver => "JumpOver",
                _                   => "ClimbUp",
            };

            Vector3 approach = GetApproachDirection(b);
            Vector3 right = Vector3.Cross(Vector3.up, approach).normalized;

            // 障碍物在 approach 方向的半厚度（用于把触发器贴到朝向相机的表面外）
            float halfThick = Mathf.Abs(approach.x) * b.extents.x + Mathf.Abs(approach.z) * b.extents.z;
            // 正面宽度 = 障碍物在垂直于 approach 的水平方向上的投影宽度
            float faceWidth = Mathf.Abs(right.x) * b.size.x + Mathf.Abs(right.z) * b.size.z;

            Vector3 frontCenter = b.center - approach * (halfThick + 0.15f);
            frontCenter.y = b.min.y;

            GameObject root = new GameObject(kind.ToString().ToLower());
            root.transform.position = frontCenter;
            root.transform.rotation = Quaternion.LookRotation(-approach);   // forward 指向障碍物
            Undo.RegisterCreatedObjectUndo(root, "Auto Create " + kind);

            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(faceWidth, b.size.y, 0.6f);

            var trigger = root.AddComponent<CharacterActionTrigger>();
            trigger.playAnimation = anim;
            trigger.autoAction = false;
            trigger.disableCollision = true;
            trigger.disableGravity = true;
            trigger.resetPlayerSettings = true;
            trigger.endExitTimeAnimation = 0.95f;
            trigger.avatarTarget = AvatarTarget.LeftHand;
            trigger.matchTargetMask = new Vector3(0f, 1f, 1f);
            trigger.useTriggerRotation = true;
            trigger.activeFromForward = true;

            switch (kind)
            {
                case ActionKind.StepUp:
                    trigger.startMatchTarget = 0.2f;
                    trigger.endMatchTarget   = 0.6f;
                    trigger.exitSpeed        = 0f;     // 走上台阶后停下
                    break;
                case ActionKind.JumpOver:
                    trigger.startMatchTarget = 0f;
                    trigger.endMatchTarget   = 0.3f;
                    trigger.exitSpeed        = 2.5f;   // 翻越后前冲
                    break;
                default: // ClimbUp
                    trigger.startMatchTarget = 0f;
                    trigger.endMatchTarget   = 0.3f;
                    trigger.exitSpeed        = 2.5f;   // 爬上墙顶后前冲
                    break;
            }

            // matchTarget：障碍物顶部中心（手撑点/落点），用户可按需微调
            var target = new GameObject("target");
            target.transform.SetParent(root.transform, false);
            target.transform.position = new Vector3(b.center.x, b.max.y, b.center.z);
            trigger.matchTarget = target.transform;

            Selection.activeGameObject = root;
        }

        static void CreateLadderFromBounds(Bounds b)
        {
            Vector3 approach = GetApproachDirection(b);
            float halfThick = Mathf.Abs(approach.x) * b.extents.x + Mathf.Abs(approach.z) * b.extents.z;

            Vector3 frontCenter = b.center - approach * (halfThick + 0.15f);
            frontCenter.y = b.min.y;

            GameObject root = new GameObject("ladder");
            root.transform.position = frontCenter;
            root.transform.rotation = Quaternion.LookRotation(-approach);
            Undo.RegisterCreatedObjectUndo(root, "Auto Create Ladder");

            const float climbZ = -0.2f;

            GameObject bottom = new GameObject("EnterLadderBottom");
            bottom.transform.SetParent(root.transform, false);
            bottom.transform.localPosition = Vector3.zero;
            SetupLadderNode(bottom, "EnterLadderBottom", "ExitLadderBottom", climbZ, reverseEnter: false);

            GameObject top = new GameObject("ExitLadderTop");
            top.transform.SetParent(root.transform, false);
            top.transform.localPosition = new Vector3(0f, b.size.y, 0f);
            SetupLadderNode(top, "EnterLadderTop", "ExitLadderTop", climbZ, reverseEnter: true);

            Selection.activeGameObject = root;
        }

        // ─── 放置位置：优先场景相机前方，否则场景原点 ─────────────────────

        static Vector3 GetSpawnPosition()
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
            {
                var p = view.camera.transform.position;
                var f = view.camera.transform.forward;
                // 相机前方 3m、落到水平面（保留相机 y 稍下方作为障碍物基底）
                Vector3 target = p + f * 3f;
                return target;
            }
            return Vector3.zero;
        }

        // ─── GUI ───────────────────────────────────────────────────────

        void OnGUI()
        {
            GUILayout.Label("Action 资产快速搭建", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                "一键在场景相机前方新建并设置交互资产：\n" +
                "  StepUp / JumpOver / ClimbUp → CharacterActionTrigger（按键 E 触发）\n" +
                "  Ladder → CharacterLadderTrigger（进入自动爬梯）\n\n" +
                "创建后请按实际障碍物/平台调整 BoxCollider 尺寸、target/matchTarget 子节点位置。",
                MessageType.Info);

            EditorGUILayout.Space(8);

            if (GUILayout.Button("新建 StepUp", GUILayout.Height(30)))
                CreateStepUp();
            if (GUILayout.Button("新建 JumpOver", GUILayout.Height(30)))
                CreateJumpOver();
            if (GUILayout.Button("新建 ClimbUp", GUILayout.Height(30)))
                CreateClimbUp();
            if (GUILayout.Button("新建 Ladder", GUILayout.Height(30)))
                CreateLadder();

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox("或选中场景中的障碍物/平台物体后，按高度与形态自动创建对应 Action。", MessageType.Info);
            if (GUILayout.Button("从选中物体自动创建", GUILayout.Height(30)))
                AutoCreateActionsFromSelection();
        }
    }
}
#endif
