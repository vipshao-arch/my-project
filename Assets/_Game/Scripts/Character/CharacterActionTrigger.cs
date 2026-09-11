using UnityEngine;
using System.Collections;

namespace Game.Character
{
    /// <summary>
    /// 通用动作触发器。挂载在场景交互物体上，角色进入后按键触发动画。
    /// 替代 Invector vTriggerGenericAction。
    /// </summary>
    [AddComponentMenu("Game/Character System/Character Action Trigger")]
    public class CharacterActionTrigger : MonoBehaviour
    {
        [Header("Action Settings")]
        [Tooltip("自动执行动作（无需按键）")]
        public bool autoAction;
        [Tooltip("禁用角色碰撞")]
        public bool disableCollision = true;
        [Tooltip("禁用角色重力")]
        public bool disableGravity = true;
        [Tooltip("动画结束后恢复设置")]
        public bool resetPlayerSettings = true;
        [Tooltip("动画状态名称（必须与 Animator Controller 中的状态名完全一致）")]
        public string playAnimation;
        [Tooltip("动画退出时间（normalizedTime）")]
        public float endExitTimeAnimation = 0.8f;

        [Header("Match Target")]
        public AvatarTarget avatarTarget;
        public Vector3 matchTargetMask;
        public Transform matchTarget;
        public float startMatchTarget;
        public float endMatchTarget;

        [Header("Rotation / Position Snap")]
        [Tooltip("触发时旋转角色朝向触发器")]
        public bool useTriggerRotation;
        [Tooltip("需要角色面向触发器前方（点积检测）")]
        public bool activeFromForward;
        [Tooltip("触发时将角色 XZ 对齐到此 Transform（无 matchTarget 时生效）。留空则不对齐。")]
        public Transform snapPosition;
        [Tooltip("动画结束后给予的前冲速度（m/s）。0=静止恢复（与旧版一致）。翻墙建议 2-3。")]
        public float exitSpeed = 0f;

        [Header("On Do Action Delay")]
        [Tooltip("执行 OnDoAction 事件前的延迟（秒）")]
        public float onDoActionDelay;

        [Header("Destroy")]
        public bool destroyAfter;
        public float destroyDelay;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent OnDoAction;
        public UnityEngine.Events.UnityEvent OnPlayerEnter;
        public UnityEngine.Events.UnityEvent OnPlayerStay;
        public UnityEngine.Events.UnityEvent OnPlayerExit;

        void Start()
        {
            // 运行时不再强制覆盖 Tag/Layer，避免破坏场景作者设置。
            // 触发器语义由 CharacterActionHandler 以组件识别，Tag 不作为门控。
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        public virtual IEnumerator OnDoActionDelay(GameObject obj)
        {
            yield return new WaitForSeconds(onDoActionDelay);
            OnDoAction.Invoke();
        }
    }
}
