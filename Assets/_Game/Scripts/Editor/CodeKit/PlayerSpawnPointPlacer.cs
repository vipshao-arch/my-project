#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Net;

namespace Game.EditorTools
{
    /// <summary>
    /// 出生点批量生成器(2026-09-08):在场景中生成多个 PlayerSpawnPoint,
    /// 自动向下贴地,取代单一 PlayerSpawn 标记 + 环形错位(错位可能压进阻挡)。
    ///
    /// 锚点优先级:PlayerSpawn_A/B(对战双区) → PlayerSpawn(合作) → 世界原点。
    /// 贴地用向下 Raycast(从 +50m 下落);无屋顶的开阔测试场景适用。
    /// 生成的出生点统一挂到 _PlayerSpawnPoints 根节点下,便于整体删除/调整。
    /// </summary>
    public static class PlayerSpawnPointPlacer
    {
        private const string RootName = "_PlayerSpawnPoints";
        private const float RowSpacing = 3f;
        private const float GroundRayHeight = 50f;

        [MenuItem("Tools/Net/Place Spawn Points (4)")]
        public static void PlaceFour() => Place(4);

        [MenuItem("Tools/Net/Place Spawn Points (6)")]
        public static void PlaceSix() => Place(6);

        public static void Place(int count)
        {
            if (!SceneManager.GetActiveScene().IsValid())
            {
                Debug.LogError("[SpawnPlacer] 当前没有打开的场景。");
                return;
            }

            // 清理旧生成(幂等)
            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Place Spawn Points");

            var positions = BuildPositions(ResolveAnchors(), count);

            int placed = 0;
            for (int i = 0; i < positions.Count && i < count; i++)
            {
                var go = new GameObject($"PlayerSpawnPoint_{i:D2}");
                go.transform.SetParent(root.transform);
                go.transform.position = SnapToGround(positions[i]);
                go.AddComponent<PlayerSpawnPoint>().index = i;
                Undo.RegisterCreatedObjectUndo(go, "Place Spawn Points");
                placed++;
            }

            EditorUtility.SetDirty(root);
            Debug.Log($"[SpawnPlacer] 已生成 {placed} 个出生点(根节点 {RootName})。");
        }

        private static List<Vector3> ResolveAnchors()
        {
            var list = new List<Vector3>();
            var a = GameObject.Find("PlayerSpawn_A");
            var b = GameObject.Find("PlayerSpawn_B");
            if (a != null && b != null)
            {
                list.Add(a.transform.position);
                list.Add(b.transform.position);
                return list;
            }

            var m = GameObject.Find("PlayerSpawn");
            if (m != null) { list.Add(m.transform.position); return list; }

            list.Add(Vector3.zero);
            return list;
        }

        private static List<Vector3> BuildPositions(List<Vector3> anchors, int count)
        {
            var result = new List<Vector3>();
            if (anchors.Count >= 2)
            {
                // 双锚点(PvP A/B):各分一半
                int half = Mathf.CeilToInt(count / 2f);
                AppendRow(result, anchors[0], half);
                AppendRow(result, anchors[1], count - half);
            }
            else
            {
                AppendRow(result, anchors[0], count);
            }
            return result;
        }

        private static void AppendRow(List<Vector3> result, Vector3 anchor, int n)
        {
            // 沿 X 轴一字排开,anchor 居中
            float start = -(n - 1) * 0.5f * RowSpacing;
            for (int i = 0; i < n; i++)
                result.Add(anchor + new Vector3(start + i * RowSpacing, 0f, 0f));
        }

        private static Vector3 SnapToGround(Vector3 pos)
        {
            if (Physics.Raycast(pos + Vector3.up * GroundRayHeight, Vector3.down,
                    out RaycastHit hit, GroundRayHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.25f;
            return pos + Vector3.up * 0.25f;
        }
    }
}
#endif
