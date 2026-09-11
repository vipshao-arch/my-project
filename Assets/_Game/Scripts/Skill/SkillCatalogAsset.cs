using System.Collections.Generic;
using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// 技能目录资产(Resources/SkillCatalog):构建期可用的全量 SkillData 索引。
    /// 注意:ScriptableObject 类必须与文件同名,否则资产脚本绑定失败。
    /// </summary>
    public class SkillCatalogAsset : ScriptableObject
    {
        public List<SkillData> skills = new List<SkillData>();
    }
}
