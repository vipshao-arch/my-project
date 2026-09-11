#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace Game.Level.EditorTools
{
    /// <summary>
    /// 掩体测试脚手架(TR-3 建立;2026-07-31 收敛:去菜单化,仅作布局库)。
    ///
    /// 仅供 CoverRegressionSceneBuilder 等场景生成器复用的静态布局:
    ///   L0 台阶×2(stepup) / L1 低掩体×2(jumpover) / L2 高掩体(climbup 双面) /
    ///   双层平台+27.5° 斜坡(stepup×2+climbup×3) / L3 墙+梯子 / PlayerSpawn。
    ///   合计 12 触发器,均为所服务掩体的子级(场景铁律);实体碰撞体 Default 层非 Trigger;
    ///   L1/L2/L3 盒挂 NavMeshModifier(Not Walkable,防孤岛——敌人不攀爬)。
    /// </summary>
    public static class CoverTestScaffold
    {
        private const string ActionPrefabDir = "Assets/_Game/TestScene/Prefabs/Actions";

        /// <summary>在当前场景原点附近生成全套掩体布局(根节点 _CoverScaffold)。</summary>
        public static void Spawn()
        {
            var root = new GameObject("_CoverScaffold");

            // ── 出生点 ──
            CreateMarker("PlayerSpawn", new Vector3(0f, 0f, -4f), root.transform);

            // ── L0 台阶×2(0.5m,stepup) ──
            var s0a = CreateBox("L0_A", new Vector3(-2f, 0.25f, 2f), new Vector3(1.5f, 0.5f, 1.5f), root.transform);
            SpawnTrigger("stepup.prefab", new Vector3(-2f, 0f, 1f), s0a.transform);
            var s0b = CreateBox("L0_B", new Vector3(2f, 0.25f, 2f), new Vector3(1.5f, 0.5f, 1.5f), root.transform);
            SpawnTrigger("stepup.prefab", new Vector3(2f, 0f, 1f), s0b.transform);

            // ── L1 低掩体×2(0.9m,jumpover) ──
            var l1a = CreateBox("L1_A", new Vector3(-4f, 0.45f, 7f), new Vector3(2f, 0.9f, 0.6f), root.transform);
            SpawnTrigger("jumpover.prefab", new Vector3(-4f, 0f, 6.3f), l1a.transform);
            MarkNotNavigable(l1a);
            var l1b = CreateBox("L1_B", new Vector3(4f, 0.45f, 7f), new Vector3(2f, 0.9f, 0.6f), root.transform);
            SpawnTrigger("jumpover.prefab", new Vector3(4f, 0f, 6.3f), l1b.transform);
            MarkNotNavigable(l1b);

            // ── L2 高掩体(1.8m,climbup 双面) ──
            var l2 = CreateBox("L2", new Vector3(0f, 0.9f, 11f), new Vector3(2f, 1.8f, 1f), root.transform);
            SpawnTrigger("climbup.prefab", new Vector3(0f, 0f, 10.3f), l2.transform);
            SpawnTrigger("climbup.prefab", new Vector3(0f, 0f, 11.7f), l2.transform);
            MarkNotNavigable(l2);

            // ── 双层平台(3m)+ 27.5° 斜坡(经斜坡+stepup 上台;薄板不配 climbup——档位 1.2~2.5m 校验不过) ──
            var plat = CreateBox("Platform", new Vector3(-7f, 2.85f, 15f), new Vector3(6f, 0.3f, 6f), root.transform);
            var ramp = CreateBox("Ramp", new Vector3(-7f, 1.5f, 10.4f), new Vector3(3f, 0.2f, 7f), root.transform);
            ramp.transform.rotation = Quaternion.Euler(-27.5f, 0f, 0f);
            SpawnTrigger("stepup.prefab", new Vector3(-7f, 0f, 6.7f), plat.transform);

            // ── L3 高墙(3.5m)+ 梯子 ──
            var l3 = CreateBox("L3_Wall", new Vector3(6f, 1.75f, 15f), new Vector3(4f, 3.5f, 0.5f), root.transform);
            SpawnTrigger("ladder.prefab", new Vector3(6f, 0f, 14.6f), l3.transform);
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
                Debug.LogWarning($"[CoverScaffold] 触发器 prefab 未找到:{path}");
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
