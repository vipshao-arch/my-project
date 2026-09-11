#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEngine.SceneManagement;

namespace Game.Level.EditorTools
{
    /// <summary>
    /// 对称对战场景构建器(阶段三 3.3,2026-07-31 建立)。
    ///
    /// 一键生成 `Assets/_Game/TestScene/pvp_arena.unity`:绕 Z 轴镜像的 FFA 对称竞技场——
    ///   中央 L2 主掩体(双面 climbup) + 每侧 L1 对(jumpover)/L2(climbup)/二层平台+斜坡/L3 墙+梯子
    ///   + PlayerSpawn_A/B 双出生区(clientId 奇偶分配)。
    /// 全部实体碰撞体 Default 层非 Trigger;触发器挂为所服务掩体的子级(场景铁律);
    /// L1/L2/L3 盒带 NavMeshModifier(Not Walkable,防孤岛——敌人不攀爬)。
    /// 对战场景不生成敌人(无 EnemySpawn 标记,SpawnSceneEnemies 自动跳过)。
    /// </summary>
    public static class PvpArenaSceneBuilder
    {
        private const string ScenePath = "Assets/_Game/TestScene/pvp_arena.unity";
        private const string ActionPrefabDir = "Assets/_Game/TestScene/Prefabs/Actions";

        [MenuItem("Tools/Level Kit/Build PvP Arena Scene")]
        public static void Build()
        {
            Scene returnScene = SceneManager.GetActiveScene();
            string returnPath = returnScene.path;
            try
            {
                if (returnScene.isDirty && !string.IsNullOrEmpty(returnPath))
                {
                    EditorSceneManager.SaveScene(returnScene);
                    Debug.Log($"[PvpArena] 已保护性保存当前场景:{returnPath}");
                }

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                // ── 地面 40×40(中心原点) ──
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.position = Vector3.zero;
                ground.transform.localScale = new Vector3(4f, 1f, 4f);
                ground.layer = 0;

                // ── 方向光 + 相机 ──
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.AddComponent<Game.Character.TopdownCameraController>();
                camGo.transform.position = new Vector3(0f, 12f, -6f);
                camGo.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.15f, 0.16f, 0.18f);

                var root = new GameObject("_PvpArena");

                // ── 出生区(A/B,NetworkSpawnPlacer 奇偶分配) ──
                CreateMarker("PlayerSpawn_A", new Vector3(0f, 0f, -18f), root.transform);
                CreateMarker("PlayerSpawn_B", new Vector3(0f, 0f, 18f), root.transform);

                // ── 中央 L2 主掩体(3×1.8×2,双面可攀) ──
                var center = CreateBox("Center_L2", new Vector3(0f, 0.9f, 0f), new Vector3(3f, 1.8f, 2f), root.transform);
                SpawnTrigger("climbup.prefab", new Vector3(0f, 0f, -1.4f), center.transform);
                SpawnTrigger("climbup.prefab", new Vector3(0f, 0f, 1.4f), center.transform);
                MarkNotNavigable(center);

                // ── 每侧镜像内容(s=±1:A 侧 -Z / B 侧 +Z) ──
                BuildSide(root.transform, -1);
                BuildSide(root.transform, +1);

                // ── NavMesh 烘焙(对战场景虽无敌人,保持可烘防后续增敌) ──
                var navGo = new GameObject("NavMesh");
                var surface = navGo.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.All;
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.BuildNavMesh();

                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log($"[PvpArena] 对战场景已生成:{ScenePath}\n" +
                          "布局:中央 L2 主掩体 + 每侧 L1对/L2/二层平台+斜坡/L3墙+梯子,绕 Z 镜像;\n" +
                          "出生区 PlayerSpawn_A/B(clientId 奇偶分配)。建议跑 Validate Cover Rules + NavMesh Connectivity 复核。");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PvpArena] 构建失败:{e}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(returnPath))
                    EditorSceneManager.OpenScene(returnPath);
            }
        }

        /// <summary>构建一侧内容(s=-1 → -Z 侧;s=+1 → +Z 侧,绕原点镜像)。</summary>
        private static void BuildSide(Transform root, int s)
        {
            string tag = s < 0 ? "A" : "B";

            // L1 低掩体对(0.9m,蹲下即安全);触发器放面向中线一侧
            var l1a = CreateBox($"L1_{tag}_1", new Vector3(-3f, 0.45f, 8f * s), new Vector3(2f, 0.9f, 0.6f), root);
            var l1b = CreateBox($"L1_{tag}_2", new Vector3(3f, 0.45f, 8f * s), new Vector3(2f, 0.9f, 0.6f), root);
            SpawnTrigger("jumpover.prefab", new Vector3(-3f, 0f, 7.6f * s), l1a.transform);
            SpawnTrigger("jumpover.prefab", new Vector3(3f, 0f, 7.6f * s), l1b.transform);
            MarkNotNavigable(l1a);
            MarkNotNavigable(l1b);

            // L2 高掩体(1.8m,可攀顶)
            var l2 = CreateBox($"L2_{tag}", new Vector3(5f, 0.9f, 12f * s), new Vector3(2f, 1.8f, 1f), root);
            SpawnTrigger("climbup.prefab", new Vector3(5f, 0f, 11.3f * s), l2.transform);
            MarkNotNavigable(l2);

            // 二层平台(3m)+ 27.5° 斜坡(朝中线升)
            CreateBox($"Platform_{tag}", new Vector3(-6f, 2.85f, 13f * s), new Vector3(6f, 0.3f, 5f), root);
            var ramp = CreateBox($"Ramp_{tag}", new Vector3(-6f, 1.5f, 8.5f * s), new Vector3(3f, 0.2f, 6.5f), root);
            ramp.transform.rotation = Quaternion.Euler(-27.5f * s, 0f, 0f);

            // L3 高墙(3.5m)+ 梯子(玩家专属捷径)
            var l3 = CreateBox($"L3_{tag}", new Vector3(-9f, 1.75f, 13f * s), new Vector3(3f, 3.5f, 0.5f), root);
            SpawnTrigger("ladder.prefab", new Vector3(-9f, 0f, 12.6f * s), l3.transform);
            MarkNotNavigable(l3);
        }

        private static GameObject CreateBox(string name, Vector3 pos, Vector3 size, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.layer = 0;
            return go;
        }

        private static void CreateMarker(string name, Vector3 pos, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
        }

        private static void MarkNotNavigable(GameObject go)
        {
            var mod = go.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = NavMesh.GetAreaFromName("Not Walkable");
        }

        private static void SpawnTrigger(string prefabName, Vector3 pos, Transform parent)
        {
            string path = $"{ActionPrefabDir}/{prefabName}";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[PvpArena] 触发器 prefab 未找到:{path}");
                return;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = prefabName.Replace(".prefab", "");
            inst.transform.SetParent(parent, false);
            inst.transform.position = pos;
        }
    }
}
#endif
