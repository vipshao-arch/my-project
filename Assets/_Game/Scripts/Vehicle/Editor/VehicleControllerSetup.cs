using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Vehicle.Editor
{
    /// <summary>
    /// 一键把 VehicleController 挂载到目标车辆:
    ///   1) 预制体本身;
    ///   2) 已加载场景里非预制体实例的独立摆放对象(预制体实例会自动继承预制体上的组件)。
    ///
    /// 通用化设计:要挂载新车,只需把名字加到 <see cref="TargetVehicles"/> 列表,无需改其它代码。
    ///
    /// 菜单:Tools → Vehicle → Setup Target Vehicles
    /// </summary>
    public static class VehicleControllerSetup
    {
        private const string PrefabDir = "Assets/PolygonApocalypseWasteland/Prefabs/Vehicles/";

        /// <summary>需要挂载驾驶功能的车辆(按需增删,幂等:已挂载的自动跳过)。</summary>
        private static readonly string[] TargetVehicles =
        {
            "SM_Veh_Tumbler_01",
            "SM_Veh_Reaper_01",
            "SM_Veh_Greaser_01",
        };

        [MenuItem("Tools/Vehicle/Setup Target Vehicles")]
        public static void Setup()
        {
            int added = 0;

            // 1) 预制体
            foreach (var name in TargetVehicles)
            {
                string path = PrefabDir + name + ".prefab";
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                {
                    Debug.LogWarning($"[VehicleSetup] 未找到预制体:{path}");
                    continue;
                }

                var contents = PrefabUtility.LoadPrefabContents(path);
                if (contents.GetComponent<VehicleController>() == null)
                {
                    contents.AddComponent<VehicleController>();
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    added++;
                    Debug.Log($"[VehicleSetup] 已给预制体 {name} 添加 VehicleController。");
                }
                else
                {
                    Debug.Log($"[VehicleSetup] 预制体 {name} 已存在 VehicleController,跳过。");
                }
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // 2) 已加载场景里的独立摆放对象
            var targetSet = new HashSet<string>(TargetVehicles);
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (!targetSet.Contains(t.name)) continue;
                        // 预制体实例自动继承预制体上的组件,跳过,避免产生 override
                        if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) continue;

                        if (t.GetComponent<VehicleController>() == null)
                        {
                            Undo.AddComponent<VehicleController>(t.gameObject);
                            added++;
                            Debug.Log($"[VehicleSetup] 已给场景对象 '{t.name}' 添加 VehicleController。", t);
                        }
                    }
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log(added > 0
                ? $"[VehicleSetup] 完成,共新增 {added} 个 VehicleController。"
                : "[VehicleSetup] 无需新增(可能已配置完成)。");
        }
    }
}
