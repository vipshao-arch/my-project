using System.Collections.Generic;
using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// 技能名 → SkillData 运行时索引(S2-5b,2026-07-30)。
    ///
    /// 联机广播只传技能名(字符串),远端经本目录解析同一资产:
    ///   构建/真机:读 Resources/SkillCatalog(由 Tools/Combat Sandbox/Rebuild Skill Catalog 生成);
    ///   编辑器兜底:目录缺失时 AssetDatabase 直扫(零配置)。
    /// </summary>
    public static class SkillCatalog
    {
        private static Dictionary<string, SkillData> _byName;

        public static SkillData Find(string skillName)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(skillName) && _byName.TryGetValue(skillName, out var d) ? d : null;
        }

        private static void EnsureBuilt()
        {
            if (_byName != null) return;
            _byName = new Dictionary<string, SkillData>();

            var catalog = Resources.Load<SkillCatalogAsset>("SkillCatalog");
            if (catalog != null)
                foreach (var d in catalog.skills)
                    if (d != null) _byName[d.name] = d;

#if UNITY_EDITOR
            if (_byName.Count == 0)   // 目录资产未生成:编辑器内兜底直扫
            {
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:SkillData"))
                {
                    var d = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillData>(
                        UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (d != null) _byName[d.name] = d;
                }
            }
#endif
        }
    }
}
