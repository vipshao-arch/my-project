using UnityEngine;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 通用拾取系统。挂在可拾取物体上，玩家接触时自动拾取。
    ///
    /// 拾取类型：
    ///   - Health: 恢复生命值
    ///   - Currency: 增加货币（需要外部系统读取）
    ///   - Custom: 触发自定义事件
    /// </summary>
    public class PickupItem : MonoBehaviour
    {
        public enum PickupType
        {
            Health,
            Currency,
            Custom
        }

        [Header("Type")]
        public PickupType pickupType = PickupType.Health;

        [Header("Values")]
        [Tooltip("生命恢复量（Health 类型有效）")]
        public float healAmount = 20f;
        [Tooltip("货币数量（Currency 类型有效）")]
        public int currencyAmount = 10;

        [Header("Pickup Settings")]
        [Tooltip("拾取范围（米）。玩家进入即拾取。")]
        public float pickupRange = 1.5f;
        [Tooltip("可拾取的目标 Layer")]
        public LayerMask targetLayer = 1 << 8; // Layer 8 = Player
        [Tooltip("拾取后是否销毁自身")]
        public bool destroyOnPickup = true;
        [Tooltip("拾取特效")]
        public GameObject pickupVFX;

        [Header("Float Animation")]
        public bool floatAnim = true;
        public float floatSpeed = 2f;
        public float floatHeight = 0.2f;
        public bool rotateAnim = true;
        public float rotateSpeed = 90f;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent OnPickupEvent;

        // 浮动动画的"视觉目标子节点"（prefab 下挂视觉模型的子物体）。
        // 浮动和旋转只作用于它，不动根 transform —— 否则会无视物理直接覆盖位置，
        // 导致掉落物被强制拉回初始 spawn 点（看起来"卡在地面"或"穿过地面"）。
        private Transform _visual;

        void Start()
        {
            // 找一个 Renderer 子节点做视觉子节点。找不到则浮动退化为空。
            // 注意：PickupItem 自身有 Collider（多数情况），不算 visual。
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != null && r.transform != transform)
                {
                    _visual = r.transform;
                    break;
                }
            }
        }

        void Update()
        {
            // 浮动+旋转动画：只动 _visual 子节点，不覆盖根 transform.position
            //   （旧实现 transform.position = _startPos + yOffset 会无视物理，
            //    导致 Rigidbody 把物品落到地面后又被拉回 spawn 点，视觉上"穿地/卡地"）
            if (_visual != null)
            {
                if (floatAnim)
                {
                    float yOffset = Mathf.Sin(Time.time * floatSpeed) * floatHeight;
                    Vector3 lp = _visual.localPosition;
                    _visual.localPosition = new Vector3(lp.x, yOffset, lp.z);
                }
                if (rotateAnim)
                {
                    _visual.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
                }
            }
            else if (floatAnim || rotateAnim)
            {
                // 没找到 visual 子节点（prefab 结构特殊）：不强行覆盖根 transform，
                // 退化为只做旋转；浮动关闭。
                if (rotateAnim) transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime);
            }

            // 检测玩家拾取
            Collider[] hits = Physics.OverlapSphere(transform.position, pickupRange, targetLayer);
            if (hits.Length > 0)
            {
                DoPickup(hits[0].gameObject);
            }
        }

        private void DoPickup(GameObject collector)
        {
            switch (pickupType)
            {
                case PickupType.Health:
                    var damageable = collector.GetComponent<IDamageable>();
                    if (damageable != null && !damageable.isDead)
                        damageable.Heal(healAmount);
                    else
                        return; // 无法拾取
                    break;

                case PickupType.Currency:
                    // 货币系统后续接入，这里先 Log
                    Debug.Log($"[PickupItem] {collector.name} picked up {currencyAmount} currency");
                    break;

                case PickupType.Custom:
                    // 由 OnPickupEvent 处理
                    break;
            }

            // 拾取特效
            if (pickupVFX != null)
                Instantiate(pickupVFX, transform.position, Quaternion.identity);

            // 事件
            OnPickupEvent?.Invoke();

            // 销毁
            if (destroyOnPickup)
                Destroy(gameObject);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, pickupRange);
        }
#endif
    }
}
