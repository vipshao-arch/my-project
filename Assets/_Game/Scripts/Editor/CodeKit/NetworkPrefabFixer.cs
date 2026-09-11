#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 联机 prefab 修复器(S2 修补,2026-07-30)。
    ///
    /// headless(MCP/脚本)给 prefab 添加 NetworkAnimator 时,其 m_Animator 序列化引用
    /// 不会自动赋值,运行时 Awake 报 UnassignedReferenceException。
    /// 本工具扫描全部带 NetworkAnimator 的 prefab,把 m_Animator 指向同根 Animator。
    /// 幂等,可反复执行;新增联机 prefab 后跑一次即可。
    /// </summary>
    public static class NetworkPrefabFixer
    {
        private const string DefaultPlayerPrefabPath = "Assets/_Game/character/graves/graves_Character.prefab";
        private const string EnemyPrefabPath  = "Assets/_Game/character/M_WangLingDaoBing/M_WangLingDaoBing_Enemy.prefab";
        private const string ResourcesPlayerPath = "Assets/_Game/Resources/NetworkPlayer.prefab";
        private const string ResourcesEnemyPath  = "Assets/_Game/Resources/NetworkEnemy.prefab";

        /// <summary>
        /// 手动恢复入口：生成 Resources/NetworkPlayer.prefab 与 NetworkEnemy.prefab(prefab 变体)。
        /// 正常角色创建由 Character Kit → Setup → Network Setup 自动调用。
        /// 独立包无 AssetDatabase,联机 prefab 必须经 Resources 加载;删除原 prefab 后需重建。
        /// </summary>
        [MenuItem("Tools/Net/Maintenance/Build Player Resources Entry")]
        public static void BuildPlayerResourcesEntry()
        {
            System.IO.Directory.CreateDirectory("Assets/_Game/Resources");
            string playerPrefabPath = ResolvePlayerPrefabPath();
            CreateVariant(playerPrefabPath, ResourcesPlayerPath, "玩家");
            BuildCharacterPlayerEntries();   // 为全部主角生成多角色联机入口(角色选择用)
            CreateVariant(EnemyPrefabPath, ResourcesEnemyPath, "敌人");
            CreateRelayPrefab();
            CreateMatchManagerPrefab();
            Fix();   // 顺带兜底:全部联机 prefab 的 NetworkAnimator.m_Animator 引用扫描修复
        }

        /// <summary>手动重建多角色联机入口(角色选择流程用)。</summary>
        [MenuItem("Tools/Net/Maintenance/Build Character Player Entries")]
        private static void BuildCharacterPlayerEntriesMenu()
        {
            BuildCharacterPlayerEntries();
            Fix();
        }

        /// <summary>
        /// 为 Assets/_Game/character 下每个主角(目录内含 &lt;目录名&gt;_Character.prefab)
        /// 生成联机入口变体 Resources/NetworkPlayer_&lt;目录名&gt;.prefab,写入 GlobalObjectIdHash。
        /// 供角色选择流程(NetworkGameManager + CharacterRoster)注册使用。
        /// </summary>
        public static void BuildCharacterPlayerEntries()
        {
            System.IO.Directory.CreateDirectory("Assets/_Game/Resources");
            string charactersDir = "Assets/_Game/character";
            if (!System.IO.Directory.Exists(charactersDir)) return;

            int count = 0;
            foreach (var dir in System.IO.Directory.GetDirectories(charactersDir))
            {
                string dirName = System.IO.Path.GetFileName(dir);
                if (dirName == "Common") continue;   // 公共资源目录,非角色
                string characterPrefab = $"{dir}/{dirName}_Character.prefab";
                if (!System.IO.File.Exists(characterPrefab)) continue;
                string outPath = $"Assets/_Game/Resources/NetworkPlayer_{dirName}.prefab";
                CreateVariant(characterPrefab, outPath, $"角色 {dirName}");
                count++;
            }
            if (count == 0)
                Debug.LogWarning("[NetFixer] 未在 Assets/_Game/character 下发现任何 *_Character.prefab");
        }

        /// <summary>
        /// 解析当前玩家入口：优先 Character Kit 记录路径，其次当前选中角色，最后回退 graves 默认。
        /// </summary>
        private static string ResolvePlayerPrefabPath()
        {
            string configured = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "");
            if (IsValidCharacterPrefab(configured)) return configured;

            var selected = Selection.activeObject as GameObject;
            string selectedPath = selected != null ? AssetDatabase.GetAssetPath(selected) : "";
            if (IsValidCharacterPrefab(selectedPath))
            {
                EditorPrefs.SetString("Test09.CharacterKit.PlayerPrefabPath", selectedPath);
                return selectedPath;
            }

            if (IsValidCharacterPrefab(DefaultPlayerPrefabPath)) return DefaultPlayerPrefabPath;
            Debug.LogError("[PrefabFixer] 未找到可用玩家 prefab，请在 Character Kit 设置中指定玩家入口。");
            return DefaultPlayerPrefabPath;
        }

        private static bool IsValidCharacterPrefab(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) return false;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null && prefab.GetComponent<Animator>() != null
                && prefab.GetComponent<Game.Character.CharacterMotor>() != null;
        }

        /// <summary>
        /// 生成 Resources/NetworkCombatRelay.prefab(NetworkObject + NetworkCombatRelay)。
        /// 运行时 new GameObject + AddComponent&lt;NetworkObject&gt; 的 Spawn 会带 hash=0,
        /// 客户端无法实例化——必须是注册过的 prefab 资产。
        /// </summary>
        private static void CreateRelayPrefab()
        {
            var go = new GameObject("NetworkCombatRelay");
            try
            {
                go.AddComponent<Unity.Netcode.NetworkObject>();
                go.AddComponent<Game.Net.NetworkCombatRelay>();
                PrefabUtility.SaveAsPrefabAsset(go, "Assets/_Game/Resources/NetworkCombatRelay.prefab");
                WriteGlobalObjectIdHash("Assets/_Game/Resources/NetworkCombatRelay.prefab");
                Debug.Log("[NetFixer] 战斗路由 prefab 已生成:Assets/_Game/Resources/NetworkCombatRelay.prefab(含 GlobalObjectIdHash)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>生成 Resources/NetworkMatchManager.prefab(对战模式管理,阶段三)。</summary>
        private static void CreateMatchManagerPrefab()
        {
            var go = new GameObject("NetworkMatchManager");
            try
            {
                go.AddComponent<Unity.Netcode.NetworkObject>();
                go.AddComponent<Game.Net.NetworkMatchManager>();
                PrefabUtility.SaveAsPrefabAsset(go, "Assets/_Game/Resources/NetworkMatchManager.prefab");
                WriteGlobalObjectIdHash("Assets/_Game/Resources/NetworkMatchManager.prefab");
                Debug.Log("[NetFixer] 对战管理 prefab 已生成:Assets/_Game/Resources/NetworkMatchManager.prefab(含 GlobalObjectIdHash)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void CreateVariant(string srcPath, string outPath, string label)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
            if (src == null) { Debug.LogError($"[NetFixer] {label} prefab 未找到:{srcPath}"); return; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(inst, outPath);
                WriteGlobalObjectIdHash(outPath);
                Debug.Log($"[NetFixer] {label}联机入口已生成:{outPath}(含 GlobalObjectIdHash)");
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        /// <summary>
        /// 把 XXHash32(GlobalObjectId 字符串) 写入 prefab 的 NetworkObject.GlobalObjectIdHash。
        /// headless 添加的 NetworkObject 该字段为 0(OnValidate 不跑),客户端无法映射 prefab。
        /// 算法与 Unity.Netcode.XXHash.Hash32(internal)逐位一致。
        /// </summary>
        private static void WriteGlobalObjectIdHash(string prefabPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) return;
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString();
            uint hash = XXHash32(System.Text.Encoding.UTF8.GetBytes(globalId));

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var no = root.GetComponent<Unity.Netcode.NetworkObject>();
                if (no == null) return;
                var so = new SerializedObject(no);
                var prop = so.FindProperty("GlobalObjectIdHash");
                if (prop == null) return;
                prop.longValue = hash;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // XXHash32(seed=0),与 Unity.Netcode.XXHash 实现一致(小端)
        private static uint XXHash32(byte[] data)
        {
            unchecked
            {
                const uint p1 = 2654435761u, p2 = 2246822519u, p3 = 3266489917u, p4 = 0668265263u, p5 = 0374761393u;
                uint hash = p5;
                int length = data.Length;
                int offset = 0;

                if (length >= 16)
                {
                    uint v0 = p1 + p2, v1 = p2, v2 = 0u, v3 = 0u - p1;
                    int count = length >> 4;
                    for (int i = 0; i < count; i++)
                    {
                        v0 += System.BitConverter.ToUInt32(data, offset + 0)  * p2; v0 = RotL(v0, 13) * p1;
                        v1 += System.BitConverter.ToUInt32(data, offset + 4)  * p2; v1 = RotL(v1, 13) * p1;
                        v2 += System.BitConverter.ToUInt32(data, offset + 8)  * p2; v2 = RotL(v2, 13) * p1;
                        v3 += System.BitConverter.ToUInt32(data, offset + 12) * p2; v3 = RotL(v3, 13) * p1;
                        offset += 16;
                    }
                    hash = RotL(v0, 1) + RotL(v1, 7) + RotL(v2, 12) + RotL(v3, 18);
                }

                hash += (uint)length;
                length &= 15;
                while (length >= 4)
                {
                    hash += System.BitConverter.ToUInt32(data, offset) * p3;
                    hash = RotL(hash, 17) * p4;
                    offset += 4; length -= 4;
                }
                while (length > 0)
                {
                    hash += data[offset] * p5;
                    hash = RotL(hash, 11) * p1;
                    offset++; length--;
                }

                hash ^= hash >> 15; hash *= p2;
                hash ^= hash >> 13; hash *= p3;
                hash ^= hash >> 16;
                return hash;
            }
        }

        private static uint RotL(uint v, int b) => (v << b) | (v >> (32 - b));

        /// <summary>扫全部带 NetworkAnimator 的 prefab,把 m_Animator 绑到同根 Animator(由 Build 入口自动调用,不再单设菜单)。</summary>
        public static void Fix()
        {
            int fixedCount = 0, okCount = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                try
                {
                    var animators = root.GetComponentsInChildren<Unity.Netcode.Components.NetworkAnimator>(true);
                    if (animators.Length == 0) continue;
                    foreach (var na in animators)
                    {
                        var so = new SerializedObject(na);
                        var prop = so.FindProperty("m_Animator");
                        if (prop == null) continue;
                        if (prop.objectReferenceValue != null) { okCount++; continue; }
                        var anim = na.GetComponent<Animator>();
                        if (anim == null) anim = na.GetComponentInChildren<Animator>();
                        if (anim == null)
                        {
                            Debug.LogWarning($"[PrefabFixer] {path}: {na.name} 无 Animator 可绑");
                            continue;
                        }
                        prop.objectReferenceValue = anim;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        dirty = true;
                        fixedCount++;
                    }
                    if (dirty)
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Debug.Log($"[PrefabFixer] NetworkAnimator 引用修复:修复 {fixedCount},已合规 {okCount}");
        }
    }
}
#endif
