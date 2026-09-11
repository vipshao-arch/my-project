using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// 本端玩家的角色选择状态(2026-09-08)。
    ///
    /// 建房/加入前在界面选定角色;连接时:
    ///   - 房主(Host):建房前把 NetworkConfig.PlayerPrefab 设为所选角色(本地玩家不走审批);
    ///   - 加入方(Client):把角色 id 写入 NetworkConfig.ConnectionData,由 Host 的
    ///     ConnectionApprovalCallback 解析并指定 PlayerPrefabHash。
    /// </summary>
    public static class CharacterSelection
    {
        public static int SelectedIndex = 0;

        public static CharacterDefinition Selected
            => CharacterRoster.All[Mathf.Clamp(SelectedIndex, 0, CharacterRoster.All.Length - 1)];

        /// <summary>连接数据:角色 id 的 UTF-8 字节。</summary>
        public static byte[] BuildConnectionData()
            => System.Text.Encoding.UTF8.GetBytes(Selected.id);

        /// <summary>解析连接数据为角色 id;空/异常回退到第一个角色。</summary>
        public static string ParseConnectionData(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return CharacterRoster.All[0].id;
            try { return System.Text.Encoding.UTF8.GetString(payload); }
            catch { return CharacterRoster.All[0].id; }
        }
    }
}
