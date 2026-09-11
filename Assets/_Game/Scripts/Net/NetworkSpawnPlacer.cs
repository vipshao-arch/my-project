using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// 联机出生点(TR-3.5 场景标记 v1,2026-07-30)。
    ///
    /// 场景中以 "PlayerSpawn" 命名的 GameObject 作为联机出生基点
    /// (脚手架/回归场景已带);无标记时回退世界原点。
    /// 多名玩家按 clientId 环形错位(半径 2m),房主(0)在基点。
    ///
    /// 位置写入由 Owner 客户端本地执行(NetworkCharacterSetup.OnNetworkSpawn),
    /// 再由客户端权威 NetworkTransform 广播——符合移动客户端权威架构。
    /// </summary>
    public static class NetworkSpawnPlacer
    {
        public static string markerName = "PlayerSpawn";
        public static float ringRadius = 2f;

        // ── 会话级随机出生点洗牌(host 权威,2026-09-08) ──
        // 房主在 StartHost 前调 PrepareRandomOrder 把显式出生点随机打乱,
        // ConnectionApproval 再按 clientId 顺序(0=房主,1/2/3=成员)轮询取点,
        // 经 response.Position 下发——既"随机"又保证每人一个、互不重叠。
        private static Vector3[] _sessionSlots;
        private static bool _sessionReady;

        /// <summary>host 建房时调用:收集显式出生点并随机洗牌。</summary>
        public static void PrepareRandomOrder()
        {
            _sessionSlots = null;
            _sessionReady = false;

            var points = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            if (points == null || points.Length == 0) return;

            System.Array.Sort(points, (x, y) => x.index.CompareTo(y.index));
            for (int i = points.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var tmp = points[i];
                points[i] = points[j];
                points[j] = tmp;
            }

            _sessionSlots = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
                _sessionSlots[i] = points[i].transform.position;
            _sessionReady = true;

            Debug.Log($"[Spawn] host 已随机洗牌 {points.Length} 个出生点:" +
                      string.Join(" / ", _sessionSlots));
        }

        /// <summary>
        /// host 端(ConnectionApproval)为第 clientIndex 个玩家取出生点。
        /// 有洗牌结果则按 clientId 轮询取洗牌点(随机);无显式出生点时回退旧确定性逻辑。
        /// </summary>
        public static Vector3 GetHostAssignedSpawnPos(int clientIndex, out bool isExplicit)
        {
            if (_sessionReady && _sessionSlots != null && _sessionSlots.Length > 0)
            {
                isExplicit = true;
                var p = _sessionSlots[clientIndex % _sessionSlots.Length];
                Debug.Log($"[Spawn] host 分配 client#{clientIndex} → 洗牌出生点#{clientIndex % _sessionSlots.Length} @ {p}");
                return p;
            }
            return GetSpawnPos((ulong)clientIndex, out isExplicit);
        }

        public static Vector3 GetSpawnPos(ulong clientId, out bool isExplicit)
        {
            // 1. 优先:显式出生点(PlayerSpawnPoint 组件),按 index 排序后轮询分配(2026-09-08)
            var points = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            if (points != null && points.Length > 0)
            {
                System.Array.Sort(points, (x, y) =>
                {
                    int c = x.index.CompareTo(y.index);
                    return c != 0 ? c : string.CompareOrdinal(x.name, y.name);
                });
                var p = points[(int)(clientId % (ulong)points.Length)];
                Debug.Log($"[Spawn] clientId={clientId} 找到 {points.Length} 个出生点,分配 {p.name}(index={p.index}) @ {p.transform.position}");
                isExplicit = true;
                return p.transform.position;
            }

            isExplicit = false;

            // 2. 对战场景:PlayerSpawn_A/B 双区,clientId 奇偶分配(3.3)
            var a = GameObject.Find("PlayerSpawn_A");
            var b = GameObject.Find("PlayerSpawn_B");
            if (a != null && b != null)
            {
                Debug.Log($"[Spawn] clientId={clientId} 无显式出生点,回退 PlayerSpawn_A/B");
                return (clientId % 2 == 0 ? a : b).transform.position;
            }

            // 3. 合作场景:PlayerSpawn 基点 + 环形错位
            var marker = GameObject.Find(markerName);
            Vector3 basePos = marker != null ? marker.transform.position : Vector3.zero;

            int idx = (int)(clientId % 8);
            if (idx == 0) return basePos;
            float angle = idx * (360f / 8f) * Mathf.Deg2Rad;
            return basePos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ringRadius;
        }
    }
}
