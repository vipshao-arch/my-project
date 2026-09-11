using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// 联机可选角色表(2026-09-08)。
    ///
    /// 每个角色对应一个 Resources 下的联机入口 prefab(NetworkPlayer_&lt;角色名&gt;),
    /// 由 Character Kit → Network Setup / NetworkPrefabFixer.BuildCharacterPlayerEntries()
    /// 从各角色 *_Character.prefab 生成变体并写入 GlobalObjectIdHash。
    ///
    /// id 与角色目录名一致(graves / h_ezreal),用于 ConnectionData 序列化;
    /// resourcesPrefab 是 Resources.Load 的相对名(不含扩展名)。
    /// </summary>
    [System.Serializable]
    public struct CharacterDefinition
    {
        public string id;              // 角色标识(目录名,连接数据里传输用)
        public string displayName;     // 界面显示名
        public string resourcesPrefab; // Resources 下的联机入口名
    }

    public static class CharacterRoster
    {
        public static readonly CharacterDefinition[] All =
        {
            new CharacterDefinition { id = "graves",   displayName = "格雷夫斯",   resourcesPrefab = "NetworkPlayer_graves" },
            new CharacterDefinition { id = "h_ezreal", displayName = "伊泽瑞尔",   resourcesPrefab = "NetworkPlayer_h_ezreal" },
        };

        public static int Count => All.Length;

        /// <summary>按 id 找下标;找不到回退 0。</summary>
        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].id == id) return i;
            return 0;
        }

        /// <summary>加载下标对应角色的联机入口 prefab(可能为 null,调用方需兜底)。</summary>
        public static GameObject LoadPrefab(int index)
        {
            if (index < 0 || index >= All.Length) return null;
            return Resources.Load<GameObject>(All[index].resourcesPrefab);
        }
    }
}
