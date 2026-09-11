using UnityEngine;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 敌人掉落系统。挂在敌人上，监听 EnemyMotor.OnEnemyDied 事件，
    /// 死亡时按配置掉落物品。
    ///
    /// 用法：挂在敌人 GameObject 上（与 EnemyMotor 同级）。
    /// </summary>
    public class EnemyDrop : MonoBehaviour
    {
        [System.Serializable]
        public class DropEntry
        {
            [Tooltip("掉落物 prefab（需含 PickupItem 组件）")]
            public GameObject prefab;

            [Tooltip("掉落概率 (0~1)")]
            [Range(0f, 1f)]
            public float dropChance = 1f;

            [Tooltip("掉落数量范围")]
            public int minCount = 1;
            public int maxCount = 1;
        }

        [Header("Drop Table")]
        public DropEntry[] dropTable = new DropEntry[0];

        [Header("Scatter Settings")]
        [Tooltip("掉落物散布半径")]
        public float scatterRadius = 0.5f;
        [Tooltip("掉落物初始高度偏移")]
        public float spawnHeight = 0.5f;
        [Tooltip("掉落时是否给予一个向上的弹跳力")]
        public bool popUp = true;
        public float popUpForce = 3f;

        private EnemyMotor _motor;

        void Start()
        {
            _motor = GetComponent<EnemyMotor>();
            if (_motor != null)
                _motor.OnEnemyDied += OnEnemyDied;
        }

        void OnDestroy()
        {
            if (_motor != null)
                _motor.OnEnemyDied -= OnEnemyDied;
        }

        private void OnEnemyDied(GameObject enemy)
        {
            if (dropTable == null || dropTable.Length == 0) return;

            foreach (var entry in dropTable)
            {
                if (entry.prefab == null) continue;

                // 概率检查
                if (Random.value > entry.dropChance) continue;

                // 数量
                int count = Random.Range(entry.minCount, entry.maxCount + 1);
                for (int i = 0; i < count; i++)
                {
                    SpawnDrop(entry.prefab);
                }
            }
        }

        private void SpawnDrop(GameObject prefab)
        {
            // 计算 XZ 散布偏移（不参与 Y 计算）
            Vector2 xz = Random.insideUnitCircle * scatterRadius;
            Vector3 anchor = transform.position + new Vector3(xz.x, 0f, xz.y);

            // 用 Raycast 找地面高度：从敌人位置上方 3m 朝下打 6m
            //  - 找不到地面（敌人站在空洞上方）则用敌人自身 Y + spawnHeight
            //  - 找到则贴地后再 +spawnHeight（让物品浮在地面之上，物理落体更自然）
            float groundY;
            if (Physics.Raycast(anchor + Vector3.up * 3f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                groundY = hit.point.y;
            else
                groundY = transform.position.y;

            Vector3 spawnPos = new Vector3(anchor.x, groundY + spawnHeight, anchor.z);

            GameObject go = Instantiate(prefab, spawnPos, Random.rotation);

            // 防御：prefab 自身没有 collider 时 Rigidbody 物理会"穿地"
            //   自动补一个 SphereCollider，picker 还是能正常拾取（OverlapSphere 走 trigger 也行）
            if (go.GetComponentInChildren<Collider>() == null)
            {
                var sc = go.AddComponent<SphereCollider>();
                sc.radius = 0.25f;
                sc.center = Vector3.zero;
            }

            // 弹跳力
            if (popUp)
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) rb = go.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.AddForce(Vector3.up * popUpForce + Random.insideUnitSphere * 1f, ForceMode.Impulse);
            }
        }
    }
}
