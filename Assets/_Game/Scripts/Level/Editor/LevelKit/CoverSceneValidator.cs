#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Game.Character;
using Game.SkillSystem;
using UnityEditor;
using UnityEngine;

namespace Game.Level.EditorTools
{
    /// <summary>
    /// L0~L3 掩体场景规范校验(TR-3.1,2026-07-30 建立)。
    ///
    /// 规则(与《攻击与掩体格挡逻辑标准》一致):
    ///  1. 交互触发器类型与其服务的实体碰撞体高度档位必须匹配:
    ///     StepUp ≤0.5m / JumpOver 0.5~1.2m / ClimbUp 1.2~2.5m;
    ///  2. 掩体实体碰撞体必须在 Default 层且非 Trigger(否则 LoS 格挡失效);
    ///  3. 触发器碰撞体必须 isTrigger(否则误挡攻击)。
    /// </summary>
    public static class CoverSceneValidator
    {
        private class Band
        {
            public string keyword;
            public float min, max;
            public Band(string k, float lo, float hi) { keyword = k; min = lo; max = hi; }
        }

        // 档位定义(与 L0~L3 一致)
        private static readonly Band[] Bands =
        {
            new Band("StepUp",   0.05f, 0.5f),
            new Band("JumpOver", 0.5f,  1.2f),
            new Band("ClimbUp",  1.2f,  2.5f),
        };

        [MenuItem("Tools/Level Kit/Validate Cover Rules (L0-L3)")]
        public static void Validate()
        {
            var triggers = Object.FindObjectsOfType<CharacterActionTrigger>(true);
            _errors.Clear();
            _warnings.Clear();
            int checkedCount = 0;

            foreach (var trig in triggers)
            {
                if (string.IsNullOrEmpty(trig.playAnimation)) continue;
                Band band = null;
                foreach (var b in Bands)
                    if (trig.playAnimation.Contains(b.keyword)) { band = b; break; }
                if (band == null) continue; // 梯子等非档位触发器跳过

                checkedCount++;
                string path = GetPath(trig.transform);

                // 触发器自身 collider 必须 isTrigger
                var selfCol = trig.GetComponent<Collider>();
                if (selfCol == null)
                    _errors.Add($"{path}: 无 Collider");
                else if (!selfCol.isTrigger)
                    _errors.Add($"{path}: 触发器 Collider 未设 isTrigger(会误挡攻击)");

                // 找服务的实体碰撞体:父链或同父兄弟中 isTrigger=false 的 Collider
                var solid = FindSolidCollider(trig.transform);
                if (solid == null)
                {
                    _warnings.Add($"{path}: 未找到关联实体碰撞体(跳过高度检查)");
                    continue;
                }

                if (solid.gameObject.layer != 0)
                    _errors.Add($"{path}: 实体碰撞体在 {LayerMask.LayerToName(solid.gameObject.layer)} 层,必须在 Default 层(否则掩体格挡失效)");

                float h = solid.bounds.size.y;
                if (h < band.min || h > band.max)
                    _errors.Add($"{path}: {band.keyword} 触发器服务的碰撞体高 {h:F2}m,超出档位 [{band.min}~{band.max}]m");
            }

            Debug.Log($"[CoverValidator] 档位触发器 {checkedCount} 个,错误 {_errors.Count},警告 {_warnings.Count}" +
                      (checkedCount == 0 ? " — 场景中无 StepUp/JumpOver/ClimbUp 触发器" :
                       _errors.Count == 0 ? " — ✅ 全部通过" : ""));

            // 逐条独立输出(便于控制台过滤/点击定位)
            foreach (var err in _errors)
                Debug.LogError($"[CoverValidator] ❌ {err}");
            foreach (var warn in _warnings)
                Debug.LogWarning($"[CoverValidator] ⚠ {warn}");
        }

        private static readonly List<string> _errors = new List<string>();
        private static readonly List<string> _warnings = new List<string>();

        /// <summary>
        /// 找触发器服务的实体碰撞体:仅沿父链向上找 isTrigger=false 的 Collider。
        /// 场景规范:触发器 prefab 必须作为所服务掩体的子级放置(脚手架/装配工具照此生成)。
        /// 父链找不到 = 结构不规范 → 警告(不猜配对,避免误报)。
        /// </summary>
        private static Collider FindSolidCollider(Transform t)
        {
            var p = t.parent;
            while (p != null)
            {
                var cols = p.GetComponents<Collider>();
                foreach (var c in cols)
                    if (!c.isTrigger) return c;
                p = p.parent;
            }
            return null;
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        // ─────────────────────────────────────────────────────────────
        //  技能掩体配置审计(TR-2.4 静态检查)
        // ─────────────────────────────────────────────────────────────

        [MenuItem("Tools/Level Kit/Audit Skill Obstacle Config")]
        public static void AuditSkillObstacleConfig()
        {
            var guids = AssetDatabase.FindAssets("t:SkillData");
            var sb = new StringBuilder();
            int total = 0, enabled = 0, disabled = 0;
            var disabledList = new List<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var sd = AssetDatabase.LoadAssetAtPath<SkillData>(path);
                if (sd == null || sd.graphData == null || sd.graphData.Count == 0) continue;

                int on = 0, off = 0;
                foreach (var node in sd.graphData)
                {
                    bool? check = GetNodeCheckObstacle(node);
                    if (!check.HasValue) continue;
                    if (check.Value) on++; else off++;
                }
                if (on + off == 0) continue;

                total++;
                enabled += on;
                disabled += off;
                if (off > 0) disabledList.Add($"    - {sd.name} ({path}): {off} 个节点未开格挡");
            }

            sb.AppendLine($"[SkillObstacleAudit] 含格挡节点的技能 {total} 个");
            sb.AppendLine($"  节点开启格挡 {enabled},未开启 {disabled}");
            if (disabledList.Count > 0)
            {
                sb.AppendLine("  ⚠ 以下技能存在未开格挡的节点(将无视掩体,请确认是否刻意):");
                foreach (var line in disabledList) sb.AppendLine(line);
                sb.AppendLine("  提示:旧资产默认 false(保持旧行为),需在 Skill Builder 中手动开启。");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>读节点的 checkObstacle(无该字段的节点类型返回 null,如抛物线按设计不参与)。</summary>
        private static bool? GetNodeCheckObstacle(SkillNodeData node)
        {
            switch (node)
            {
                case MeleeSwingData m:  return m.checkObstacle;
                case RectShotData r:    return r.checkObstacle;
                case BeamData b:        return b.checkObstacle;
                case AOECircularData a: return a.checkObstacle;
                case ChainBounceData c: return c.checkObstacle;
                default: return null;
            }
        }
    }
}
#endif
