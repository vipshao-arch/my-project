using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Game.Character;
using Game.SkillSystem;

namespace Game.Character.Editor
{
    /// <summary>
    /// 梯子场景配置工具
    /// 
    /// 功能：
    ///   1. 为场景中所有 Tag = "LadderTrigger" 的物件补挂 CharacterLadderTrigger
    ///   2. 将上下触发器自身 Transform 作为梯子端点（支持任意平台高度）
    ///   3. 为角色 GameObject 补挂 CharacterLadderAction（若未挂载）
    ///   4. 修复角色身上的 CharacterLadderAction 引用
    /// </summary>
    public class LadderSetupTool : UnityEditor.EditorWindow
    {
        [MenuItem("Tools/Level Kit/Ladder Setup", false, 311)]
        public static void ShowWindow()
        {
            GetWindow<LadderSetupTool>("Ladder Setup");
        }

        [MenuItem("Tools/Level Kit/Quick Setup Ladders &l", false, 312)]
        public static void QuickSetup()
        {
            int ladderCount    = 0;
            int characterCount = 0;

            // ── 1. 处理所有梯子触发器节点 ──
            // ladder prefab 结构：
            //   ladder (root)
            //     └─ stair_start (子节点)
            //           ├─ ExitLadderTop     (fileID 498026 → matchTargetTop 的父)
            //           └─ EnterLadderBottom (fileID 422116 → matchTargetBottom 的父)
            //
            // 我们在 ladder root 上添加 CharacterLadderTrigger，
            // 再在 ExitLadderTop / EnterLadderBottom 子节点上也各加一个，
            // 分别设置 playAnimation / exitAnimation / matchTarget。

            GameObject[] allGOs = FindObjectsOfType<GameObject>(true);
            foreach (var go in allGOs)
            {
                // 找到名叫 "EnterLadderBottom" 或 "EnterLadderTop" 的节点
                if (go.name == "EnterLadderBottom" || go.name == "EnterLadderTop")
                {
                    SetupLadderTriggerNode(go);
                    ladderCount++;
                }
            }

            // ── 2. 处理角色 ──
            GameObject character = FindCharacter();
            if (character != null)
            {
                var la = character.GetComponent<CharacterLadderAction>();
                if (la == null)
                {
                    la = character.AddComponent<CharacterLadderAction>();
                    characterCount++;
                    Debug.Log($"[LadderSetup] 已为角色 '{character.name}' 添加 CharacterLadderAction");
                }
                else
                {
                    Debug.Log($"[LadderSetup] 角色 '{character.name}' 已有 CharacterLadderAction，跳过");
                }
            }
            else
            {
                Debug.LogWarning("[LadderSetup] 未找到角色 GameObject（需要挂载 CharacterMotor）");
            }

            Debug.Log($"[LadderSetup] 完成：处理了 {ladderCount} 个梯子触发节点，{characterCount} 个角色");
        }

        /// <summary>
        /// 上下梯闭环校验（2026-08-27 新增）：
        /// 校验每个梯子（含场景独立搭建对象与 prefab 实例）的"上→下→再上"闭环可用性。
        /// 检查项源自实际踩坑：
        ///   1. 出口触发器 isEntryTrigger 必须为 true（出梯保留触发器机制 + 二次进梯前提）
        ///   2. autoAction 必须为 true（自动进梯）
        ///   3. 触发器 Collider 存在且为 isTrigger
        ///   4. matchTargetTop/Bottom XZ 完全一致（挂梯轴铁律）
        ///   5. 出梯落点与出口 Collider 的重叠厚度（薄重叠物理事件不可靠；
        ///      重叠不足时代码会兜底保留触发器，但建议加厚 Collider）
        /// 输出为多条单行日志（MCP 控制台兼容）。
        /// </summary>
        [MenuItem("Tools/Level Kit/Ladder Roundtrip Check", false, 313)]
        public static void RoundtripCheck()
        {
            var allTriggers = FindObjectsOfType<CharacterLadderTrigger>(true);

            // 按梯子根分组（兼容独立对象 / prefab 实例两种搭建方式）
            var groups = new Dictionary<Transform, List<CharacterLadderTrigger>>();
            foreach (var t in allTriggers)
            {
                if (t == null) continue;
                var root = t.transform.root;
                if (!groups.TryGetValue(root, out var list))
                {
                    list = new List<CharacterLadderTrigger>();
                    groups[root] = list;
                }
                list.Add(t);
            }

            int err = 0, warn = 0;
            foreach (var kv in groups)
            {
                string ladderName = kv.Key.name;

                foreach (var t in kv.Value)
                {
                    string id = $"{ladderName}/{t.name}";

                    var col = t.GetComponent<Collider>();
                    if (col == null)
                    {
                        Debug.LogError($"[LadderCheck] ✗ {id}: 缺少 Collider，无法触发进梯");
                        err++;
                        continue;
                    }
                    if (!col.isTrigger)
                    {
                        Debug.LogError($"[LadderCheck] ✗ {id}: Collider 不是 Trigger");
                        err++;
                    }
                    if (!t.isEntryTrigger)
                    {
                        Debug.LogError($"[LadderCheck] ✗ {id}: isEntryTrigger=false，出梯后无法二次进梯（出梯保留机制要求出口可作入口）");
                        err++;
                    }
                    if (!t.autoAction)
                    {
                        Debug.LogWarning($"[LadderCheck] ! {id}: autoAction=false，无法自动进梯");
                        warn++;
                    }
                }

                // matchTargetTop/Bottom XZ 一致性
                var top = kv.Value.Find(x => x.exitAnimation == "ExitLadderTop");
                var bottom = kv.Value.Find(x => x.exitAnimation == "ExitLadderBottom");
                if (top == null || bottom == null)
                {
                    Debug.LogWarning($"[LadderCheck] ! {ladderName}: 缺少 ExitLadderTop/ExitLadderBottom 触发器（闭环不完整）");
                    warn++;
                    continue;
                }

                Vector3 topPos = top.matchTarget != null ? top.matchTarget.position : top.transform.position;
                Vector3 botPos = bottom.matchTarget != null ? bottom.matchTarget.position : bottom.transform.position;
                Vector2 dxz = new Vector2(topPos.x - botPos.x, topPos.z - botPos.z);
                if (dxz.magnitude > 0.05f)
                {
                    Debug.LogError($"[LadderCheck] ✗ {ladderName}: matchTargetTop/Bottom XZ 偏差 {dxz.magnitude:F3}m（须完全一致，否则挂梯轴歪斜/爬梯漂移）");
                    err++;
                }

                // 出梯落点与出口 Collider 的重叠厚度
                if (top.matchTarget != null)
                {
                    float landingY = top.transform.position.y + top.endpointHeightOffset;
                    Bounds b = top.GetComponent<Collider>().bounds;
                    // 胶囊体自落点向上延伸；与 Collider 的有效重叠
                    float overlapMin = Mathf.Max(landingY, b.min.y);
                    float overlap = b.max.y - overlapMin;
                    if (overlap < 0.05f)
                    {
                        Debug.LogWarning($"[LadderCheck] ! {ladderName}/ExitLadderTop: 出梯落点 Y={landingY:F2} 在出口 Collider(Y∈[{b.min.y:F2},{b.max.y:F2}]) 之上无重叠——二次进梯将依赖代码保留触发器兜底（可用但建议加厚/上移 Collider 覆盖平台站立区）");
                        warn++;
                    }
                    else
                    {
                        Debug.Log($"[LadderCheck] ✓ {ladderName}: 落点与出口 Collider 重叠 {overlap:F2}m，闭环物理检测正常");
                    }
                }
            }

            Debug.Log($"[LadderCheck] 校验完成：{groups.Count} 个梯子，{err} 错误 / {warn} 警告");
        }

        // ── 对单个 EnterLadderBottom / EnterLadderTop 节点配置 CharacterLadderTrigger ──
        private static void SetupLadderTriggerNode(GameObject node)
        {
            Undo.RecordObject(node, "Setup Ladder Trigger");

            // 1. Tag
            node.tag = "LadderTrigger";

            // 2. BoxCollider (isTrigger)
            var col = node.GetComponent<BoxCollider>();
            if (col == null)
                col = Undo.AddComponent<BoxCollider>(node);
            col.isTrigger = true;
            col.center = Vector3.zero;

            // 3. CharacterLadderTrigger（防止重复添加）
            var triggers = node.GetComponents<CharacterLadderTrigger>();
            CharacterLadderTrigger trigger;
            if (triggers.Length == 0)
            {
                trigger = Undo.AddComponent<CharacterLadderTrigger>(node);
            }
            else
            {
                trigger = triggers[0];
                // 删除多余的重复组件
                for (int i = 1; i < triggers.Length; i++)
                {
                    Debug.LogWarning($"[LadderSetup] 删除 '{node.name}' 上的重复 CharacterLadderTrigger (#{i})");
                    Undo.DestroyObjectImmediate(triggers[i]);
                }
            }

            bool isBottom = node.name == "EnterLadderBottom";

            trigger.playAnimation    = isBottom ? "EnterLadderBottom" : "EnterLadderTop";
            trigger.exitAnimation    = isBottom ? "ExitLadderBottom"  : "ExitLadderTop";
            trigger.startMatchTarget = isBottom ? 0.2f : 0.2f;
            trigger.endMatchTarget   = isBottom ? 0.60f : 0.85f;
            trigger.autoAction       = true;
            // 底部进梯：autoAction 已通过输入方向判定进入，不需要朝向前置检查。
            // 且梯子根节点 Y=-90° (forward=-X) 与玩家面向 +Z 成 90°，
            // activeFromForward=true 会导致 IsFacingCorrect 永远失败（angle=90 > threshold=60）。
            trigger.activeFromForward = !isBottom;
            trigger.useTriggerRotation = true;

            // 4. 触发器负责检测，matchTarget 负责角色实际挂梯位置。
            // 保留其 X/Z 挂梯偏移，仅把局部 Y 作为可调的高度轴；默认与触发器同高。
            Transform ladderRoot = FindLadderRoot(node.transform);
            string targetName = isBottom ? "matchTargetBottom" : "matchTargetTop";
            Transform matchTarget = FindDeepChild(ladderRoot, targetName);
            if (matchTarget != null)
            {
                Undo.RecordObject(matchTarget, "Align ladder match target height");
                Vector3 local = matchTarget.localPosition;
                matchTarget.localPosition = new Vector3(local.x, 0f, local.z);
                trigger.matchTarget = matchTarget;
            }
            else
            {
                trigger.matchTarget = node.transform;
            }
            trigger.endpointHeightOffset = 0f;

            // 触发器碰撞体必须与挂梯锚点重合；触发器 Transform 的位置只保留
            // 梯子方向/缩放，Collider center 按 matchTarget 的局部坐标自动对齐。
            var triggerCollider = node.GetComponent<BoxCollider>();
            if (triggerCollider != null && matchTarget != null)
            {
                Undo.RecordObject(triggerCollider, "Align ladder trigger to match target");
                triggerCollider.center = node.transform.InverseTransformPoint(matchTarget.position);
            }

            // 5. stair_start / stair_end 是实际进出区域，必须保留为 Trigger。
            for (int i = 0; i < node.transform.childCount; i++)
            {
                var child = node.transform.GetChild(i);
                if (child.name != "stair_start" && child.name != "stair_end") continue;
                foreach (var cc in child.GetComponents<Collider>())
                {
                    if (!(cc is BoxCollider) && !(cc is SphereCollider) && !(cc is CapsuleCollider))
                        continue;
                    Undo.RecordObject(cc, "Enable stair trigger");
                    cc.isTrigger = true;
                    cc.enabled = true;
                }
            }

            EditorUtility.SetDirty(node);
            Debug.Log($"[LadderSetup] 已配置 {node.name} → play={trigger.playAnimation}, exit={trigger.exitAnimation}");
        }

        // ── 向上查找梯子根节点（名叫 "ladder" 或 "ladder (1)" 的那个）──
        private static Transform FindLadderRoot(Transform t)
        {
            Transform cur = t;
            while (cur != null)
            {
                if (cur.name.StartsWith("ladder"))
                    return cur;
                cur = cur.parent;
            }
            return t.root;
        }

        // ── 递归查找子节点 ──
        private static Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindDeepChild(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ── 查找角色（挂有 CharacterMotor 的 GameObject）──
        private static GameObject FindCharacter()
        {
            var motor = FindObjectOfType<CharacterMotor>();
            return motor != null ? motor.gameObject : null;
        }

        // ──────────────── GUI ────────────────

        void OnGUI()
        {
            GUILayout.Label("梯子场景配置工具", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "自动为场景中 EnterLadderBottom / EnterLadderTop 节点\n" +
                "添加 CharacterLadderTrigger，节点自身就是高度端点。\n\n" +
                "同时为角色（挂有 CharacterMotor）添加 CharacterLadderAction。",
                MessageType.Info);

            EditorGUILayout.Space();

            if (GUILayout.Button("一键配置所有梯子", GUILayout.Height(40)))
                QuickSetup();

            if (GUILayout.Button("上下梯闭环校验（Roundtrip Check）", GUILayout.Height(30)))
                RoundtripCheck();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("单独操作", EditorStyles.boldLabel);

            if (GUILayout.Button("只配置梯子触发器"))
            {
                int count = 0;
                foreach (var go in FindObjectsOfType<GameObject>(true))
                {
                    if (go.name == "EnterLadderBottom" || go.name == "EnterLadderTop")
                    {
                        SetupLadderTriggerNode(go);
                        count++;
                    }
                }
                EditorUtility.DisplayDialog("Done", $"已处理 {count} 个节点", "OK");
            }

            if (GUILayout.Button("只添加角色 LadderAction 组件"))
            {
                var character = FindCharacter();
                if (character == null)
                {
                    EditorUtility.DisplayDialog("Error", "未找到角色（需要挂载 CharacterMotor）", "OK");
                    return;
                }
                if (character.GetComponent<CharacterLadderAction>() == null)
                {
                    Undo.AddComponent<CharacterLadderAction>(character);
                    EditorUtility.DisplayDialog("Done", $"已为 '{character.name}' 添加 CharacterLadderAction", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Info", "角色已有 CharacterLadderAction，无需重复添加", "OK");
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "快捷键：Alt + L  →  一键配置（Quick Setup）",
                MessageType.None);
        }
    }
}



