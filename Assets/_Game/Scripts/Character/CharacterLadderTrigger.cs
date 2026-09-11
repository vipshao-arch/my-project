using UnityEngine;
using UnityEngine.Events;

namespace Game.Character
{
    /// <summary>
    /// 梯子触发器。挂载在梯子进出节点上，配合 CharacterLadderAction 使用。
    /// 场景配置：BoxCollider(isTrigger=true) + Tag="LadderTrigger"
    ///
    /// 层级结构（每个梯子 prefab）：
    ///   ladder (root)
    ///     ├─ ExitLadderTop     ← 本组件，BoxCollider(trigger)
    ///     │    ├─ matchTargetTop    ← 纯 Transform 标记（不应有 Collider）
    ///     │    └─ stair_end         ← 顶部入口 Trigger（只负责进入梯子）
    ///     └─ EnterLadderBottom ← 本组件，BoxCollider(trigger)
    ///          ├─ matchTargetBottom ← 纯 Transform 标记（不应有 Collider）
    ///          └─ stair_start       ← 底部入口/视觉台阶 Trigger
    /// </summary>
    [AddComponentMenu("Game/Character System/Character Ladder Trigger")]
    public class CharacterLadderTrigger : MonoBehaviour
    {
        [Header("Ladder Animations")]
        [Tooltip("进梯动画状态名（EnterLadderBottom / EnterLadderTop）")]
        public string playAnimation = "EnterLadderBottom";
        [Tooltip("退梯动画状态名（ExitLadderTop / ExitLadderBottom）")]
        public string exitAnimation = "ExitLadderTop";

        [Header("Match Target")]
        [Tooltip("角色在梯子上的实际挂梯锚点。应作为本触发器的子节点，并沿其局部 Y 轴调整高度")]
        public Transform matchTarget;
        [Tooltip("角色根节点相对触发器 Transform 高度的修正值。触发器 Y 是梯子端点高度，角色根节点需要偏移时在这里设置。")]
        public float endpointHeightOffset = 0f;
        [Tooltip("该触发器是否用于从平台/地面进入梯子。顶部入口应挂在 stair_end 上。")]
        public bool isEntryTrigger = true;
        [Tooltip("该触发器是否用于从梯子离开到平台/地面。ExitLadderTop 仅用于顶部出口。")]
        public bool isExitTrigger = true;
        public float startMatchTarget = 0f;
        public float endMatchTarget   = 0.5f;

        [Header("Options")]
        [Tooltip("角色接触即自动进入（无需按键）")]
        public bool autoAction = false;
        [Tooltip("需要角色面朝梯子才能触发")]
        public bool activeFromForward = true;
        [Tooltip("触发时将角色旋转到与梯子一致的朝向")]
        public bool useTriggerRotation = true;
        [Tooltip("进梯时角色 Snap 到的朝向（世界空间）。留 (0,0,0) 则使用触发器自身 forward")]
        public Vector3 enterForward = Vector3.zero;
        [Tooltip("将计算出的 enterForward 取反（从顶部进梯时勾选此项）")]
        public bool reverseEnterForward = false;

        [Header("Events")]
        public UnityEvent OnDoAction;
        public UnityEvent OnPlayerEnter;
        public UnityEvent OnPlayerStay;
        public UnityEvent OnPlayerExit;

        void Start()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

#if UNITY_EDITOR
        /// <summary>标记 OnValidate 是否已排队延迟修复，防止重复入队。</summary>
        private bool _fixQueued;

        void OnValidate()
        {
            if (_fixQueued) return;
            _fixQueued = true;

            var self = this;
            var go   = gameObject;

            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (self == null || go == null) return;
                self._fixQueued = false;
                AutoFix(go, self);
            };
        }

        private static void AutoFix(GameObject go, CharacterLadderTrigger self)
        {
            bool changed = false;

            // 1. Tag → "LadderTrigger"
            if (!go.CompareTag("LadderTrigger"))
            {
                go.tag = "LadderTrigger";
                changed = true;
            }

            // 2. Collider → isTrigger=true
            var col = go.GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
                changed = true;
            }

            // 3. 删除重复 CharacterLadderTrigger（保留第一个）
            var triggers = go.GetComponents<CharacterLadderTrigger>();
            for (int i = 1; i < triggers.Length; i++)
            {
                Debug.LogWarning($"[LadderTrigger] 自动删除 '{go.name}' 上重复的 CharacterLadderTrigger (#{i + 1})", triggers[i]);
                UnityEditor.Undo.DestroyObjectImmediate(triggers[i]);
                changed = true;
            }

            // 4. 删除 matchTarget 上的所有 Collider
            if (self.matchTarget != null)
            {
                var mtGO = self.matchTarget.gameObject;
                var mtCols = mtGO.GetComponents<Collider>();
                foreach (var mtCol in mtCols)
                {
                    Debug.LogWarning($"[LadderTrigger] 自动删除 matchTarget '{mtGO.name}' 上的 {mtCol.GetType().Name}", mtCol);
                    UnityEditor.Undo.DestroyObjectImmediate(mtCol);
                    changed = true;
                }
            }

            // 5. stair_start / stair_end 是实际进出区域：保留并确保为 Trigger。
            // CharacterLadderAction 通过父级 CharacterLadderTrigger 接收子 Collider 事件，
            // 不再把父节点小盒子当作顶部入口。
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i);
                if (child.name != "stair_start" && child.name != "stair_end") continue;
                var childCols = child.GetComponents<Collider>();
                foreach (var cc in childCols)
                {
                    // 只有 primitive Collider 可以安全作为 Trigger；
                    // 凹 MeshCollider 不允许 isTrigger，保持其禁用状态。
                    if (!(cc is BoxCollider) && !(cc is SphereCollider) && !(cc is CapsuleCollider))
                        continue;
                    if (!cc.isTrigger)
                    {
                        UnityEditor.Undo.RecordObject(cc, "Enable stair trigger");
                        cc.isTrigger = true;
                        changed = true;
                    }
                    if (!cc.enabled)
                    {
                        UnityEditor.Undo.RecordObject(cc, "Enable stair trigger collider");
                        cc.enabled = true;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                UnityEditor.EditorUtility.SetDirty(go);
                Debug.Log($"[LadderTrigger] '{go.name}' 自动修复完成");
            }
        }
#endif
    }
}
