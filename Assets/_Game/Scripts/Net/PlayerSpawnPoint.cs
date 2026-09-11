using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// 联机出生点(2026-09-08):场景中显式放置的出生点标记。
    ///
    /// NetworkSpawnPlacer 会收集场景里所有 PlayerSpawnPoint,按 index 升序排序后
    /// 以 clientId 取模轮询分配,让每个玩家落到各自干净的出生位,取代旧的
    /// "单点 PlayerSpawn + 环形错位"方案(错位点可能压进阻挡)。
    ///
    /// 摆放:空 GameObject + 本组件即可;Scene 视图有绿色 Gizmo 便于定位。
    /// 也可用 Tools/Net/Place Spawn Points 菜单在场景中批量生成(自动贴地)。
    /// </summary>
    public class PlayerSpawnPoint : MonoBehaviour
    {
        [Tooltip("分配顺序:数字小的优先;相同时按名字排序")]
        public int index;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.95f, 0.4f, 0.95f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.25f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.35f, transform.position + Vector3.down * 2f);

            // 角色占位示意(约 0.9m 宽 / 2m 高)
            Gizmos.color = new Color(0.2f, 0.95f, 0.4f, 0.28f);
            Gizmos.DrawCube(transform.position + Vector3.up * 1f, new Vector3(0.9f, 2f, 0.9f));
        }
    }
}
