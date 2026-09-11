#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.SkillSystem.EditorTools
{
    /// <summary>
    /// 技能目录生成器(S2-5b 配套,2026-07-30)。
    /// 扫描全项目 SkillData,写入 Assets/_Game/Resources/SkillCatalog.asset。
    /// 新增/重命名技能资产后重跑一次;构建独立包前必须跑(真机无编辑器兜底)。
    /// </summary>
    public static class SkillCatalogBuilder
    {
        private const string OutputPath = "Assets/_Game/Resources/SkillCatalog.asset";

        [MenuItem("Tools/Combat Sandbox/Rebuild Skill Catalog")]
        public static void Rebuild()
        {
            Directory.CreateDirectory("Assets/_Game/Resources");

            var asset = AssetDatabase.LoadAssetAtPath<SkillCatalogAsset>(OutputPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SkillCatalogAsset>();
                AssetDatabase.CreateAsset(asset, OutputPath);
            }

            asset.skills.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:SkillData"))
            {
                var d = AssetDatabase.LoadAssetAtPath<SkillData>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null) asset.skills.Add(d);
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SkillCatalog] 目录已重建:{asset.skills.Count} 个 SkillData → {OutputPath}");
        }
    }
}
#endif
