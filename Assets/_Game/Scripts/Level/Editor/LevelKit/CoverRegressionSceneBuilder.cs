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
    /// TR-4.2 立体对战回归场景构建器(P3 双层对战场景落地,2026-07-30 建立)。
    ///
    /// 一键生成 `Assets/_Game/TestScene/cover_regression.unity`:
    ///   地面 + 方向光 + 顶视角相机 + CoverTestScaffold 全套(L0~L3/双层平台/斜坡/L3墙+梯子)
    ///   + NavMeshSurface 现场烘焙 + 玩家(graves) + 双敌位(M_WangLingDaoBing×2)。
    ///
    /// 执行流程:保护性保存当前场景(若脏)→ 新场景构建保存 → 切回原场景。
    /// 触发器均为 Trigger 碰撞体,NavMesh 烘焙自动忽略(梯子不进 NavMesh 天然满足)。
    /// 全程无对话框,可在 MCP/batchmode 下执行。
    /// </summary>
    public static class CoverRegressionSceneBuilder
    {
        private const string ScenePath = "Assets/_Game/TestScene/cover_regression.unity";
        private const string PlayerPrefabPath = "Assets/_Game/character/graves/graves_Character.prefab";
        private const string EnemyPrefabPath  = "Assets/_Game/character/M_WangLingDaoBing/M_WangLingDaoBing_Enemy.prefab";

        [MenuItem("Tools/Level Kit/Build Cover Regression Scene")]
        public static void Build()
        {
            Scene returnScene = SceneManager.GetActiveScene();
            string returnPath = returnScene.path;
            try
            {
                if (returnScene.isDirty && !string.IsNullOrEmpty(returnPath))
                {
                    EditorSceneManager.SaveScene(returnScene);
                    Debug.Log($"[RegressionBuilder] 已保护性保存当前场景:{returnPath}");
                }

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                // ── 地面(覆盖 z 0~25 对战区) ──
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.position = new Vector3(0f, 0f, 12f);
                ground.transform.localScale = new Vector3(4f, 1f, 4f); // 40m×40m
                ground.layer = 0;

                // ── 方向光 ──
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                // ── 顶视角相机(CharacterInputHandler.Start 自动 SetTarget 到玩家) ──
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.AddComponent<Game.Character.TopdownCameraController>();
                camGo.transform.position = new Vector3(0f, 12f, -6f);
                camGo.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.15f, 0.16f, 0.18f);

                // ── 脚手架全套(L0~L3/双层+斜坡/L3墙+梯子/出生点) ──
                CoverTestScaffold.Spawn();

                // ── 玩家与双敌位 ──
                SpawnPrefab(PlayerPrefabPath, "Player", new Vector3(0f, 0.1f, 0f));
                SpawnPrefab(EnemyPrefabPath,  "Enemy_Melee",        new Vector3(4f, 0.1f, 14f));
                SpawnPrefab(EnemyPrefabPath,  "Enemy_Ranged_High", new Vector3(0f, 3.1f, 20f));

                // ── NavMesh 现场烘焙(斜坡 27.5° 可上行;触发器为 Trigger 自动忽略) ──
                var navGo = new GameObject("NavMesh");
                var surface = navGo.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.All;
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.BuildNavMesh();

                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log($"[RegressionBuilder] 回归场景已生成:{ScenePath}\n" +
                          "内容:地面/相机/脚手架(L0~L3+双层+斜坡+梯子)/玩家×1/敌×2/NavMesh 已烘焙。\n" +
                          "建议:Tools/Level Kit/Validate NavMesh Connectivity 验证连通性;Play 跑对局剧本。");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RegressionBuilder] 构建失败:{e}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(returnPath))
                    EditorSceneManager.OpenScene(returnPath);
            }
        }

        private static void SpawnPrefab(string path, string name, Vector3 pos)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[RegressionBuilder] prefab 未找到:{path}");
                return;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = name;
            inst.transform.position = pos;
        }
    }
}
#endif
