#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Level.EditorTools
{
    /// <summary>
    /// NavMesh 层间可通行校验(TR-3.3,2026-07-30 建立)。
    ///
    /// 检测多层场景 NavMesh 烘焙后的连通性:从 NavMesh 三角网格均匀采样,
    /// 以首个采样点为基准 CalculatePath 到其余采样点,统计不可达比例。
    /// 用于发现"二层平台未通过楼梯/斜坡连上 NavMesh"(敌人无法跨层)的场景缺陷。
    /// </summary>
    public static class NavMeshConnectivityValidator
    {
        private const int MaxSamples = 300;

        [MenuItem("Tools/Level Kit/Validate NavMesh Connectivity")]
        public static void Validate()
        {
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
            {
                Debug.LogWarning("[NavMeshValidator] 场景中无 NavMesh 烘焙数据。请先在 Navigation 窗口烘焙。");
                return;
            }

            // 均匀采样
            var verts = tri.vertices;
            int step = Mathf.Max(1, verts.Length / MaxSamples);
            var samples = new List<Vector3>();
            for (int i = 0; i < verts.Length; i += step)
                samples.Add(verts[i]);

            // 基准点:取 y 最低的采样点附近(主地面层)
            Vector3 reference = samples[0];
            float minY = float.MaxValue;
            foreach (var s in samples)
                if (s.y < minY) { minY = s.y; reference = s; }

            var path = new NavMeshPath();
            var unreachable = new List<Vector3>();
            foreach (var s in samples)
            {
                if (NavMesh.CalculatePath(reference, s, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete)
                    continue;
                unreachable.Add(s);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[NavMeshValidator] 三角网格顶点 {verts.Length},采样 {samples.Count}");
            sb.AppendLine($"  基准点(最低地面) {reference}");
            sb.AppendLine($"  NavMeshLink 数量: {Object.FindObjectsOfType<NavMeshLink>().Length}");
            float ratio = samples.Count > 0 ? (float)unreachable.Count / samples.Count : 0f;
            if (unreachable.Count == 0)
            {
                sb.AppendLine("  ✅ 全部采样点连通,NavMesh 无孤岛。");
            }
            else
            {
                sb.AppendLine($"  ❌ 不可达采样 {unreachable.Count}/{samples.Count} ({ratio:P0}):");
                int show = Mathf.Min(10, unreachable.Count);
                for (int i = 0; i < show; i++)
                    sb.AppendLine($"    - {unreachable[i]}  (y={unreachable[i].y:F2})");
                if (unreachable.Count > show)
                    sb.AppendLine($"    ... 其余 {unreachable.Count - show} 个略");
                sb.AppendLine("  提示:y 值不同的不可达点通常意味着该高度层缺少楼梯/斜坡连接,或梯子被误烘进 NavMesh(玩家专属梯子不应烘焙)。");
            }
            Debug.Log(sb.ToString());
            // 单行结论(多行整表在部分日志管道只显示首行,单行保证可见)
            if (unreachable.Count == 0)
                Debug.Log($"[NavMeshValidator] ✅ 连通 {samples.Count}/{samples.Count},无孤岛");
            else
                Debug.LogError($"[NavMeshValidator] ❌ 不可达 {unreachable.Count}/{samples.Count} ({ratio:P0})");
        }
    }
}
#endif
