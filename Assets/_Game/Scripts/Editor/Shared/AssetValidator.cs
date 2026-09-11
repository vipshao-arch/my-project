using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using System.Collections.Generic;

namespace Game.EditorTools.Shared
{
    /// <summary>校验严重级别。</summary>
    public enum ValidationSeverity { Info, Warning, Error }

    /// <summary>单条校验结果。</summary>
    public struct ValidationResult
    {
        public string category;       // "必需对象" / "NavMesh" / "碰撞" / "引用完整性" / "性能"
        public string description;    // 人类可读描述
        public ValidationSeverity severity;
        public bool passed;
        public string fixHint;        // 修复建议（可为空）

        public override string ToString()
            => $"[{severity}] [{category}] {description} — {(passed ? "✓" : "✗")}";
    }

    /// <summary>
    /// 静态资产校验器 — 供 Character Kit / Level Kit / Combat Sandbox 的
    /// Diagnostic / Validate 面板统一调用。
    ///
    /// 覆盖：场景必需对象、NavMesh、碰撞、Prefab 引用完整性、性能指标。
    /// 使用 RunAllSceneChecks() 批量执行，或逐个调用 Check* 静态方法。
    /// </summary>
    public static class AssetValidator
    {
        // ── 场景必需对象 ──────────────────────────────────────

        /// <summary>检查场景中是否存在指定名称的 GameObject。</summary>
        public static ValidationResult CheckRequiredObject(string objectName)
        {
            var go = GameObject.Find(objectName);
            return new ValidationResult
            {
                category = "必需对象",
                description = $"是否存在: {objectName}",
                severity = ValidationSeverity.Error,
                passed = go != null,
                fixHint = go == null ? $"请在场景中添加一个名为 \"{objectName}\" 的 GameObject" : null,
            };
        }

        /// <summary>检查主摄像机是否启用。</summary>
        public static ValidationResult CheckMainCamera()
        {
            var cam = Camera.main;
            return new ValidationResult
            {
                category = "必需对象",
                description = "主摄像机是否存在并启用",
                severity = ValidationSeverity.Error,
                passed = cam != null && cam.isActiveAndEnabled,
                fixHint = cam == null ? "请确保场景中有 Tag=MainCamera 的 Camera" : null,
            };
        }

        // ── NavMesh ───────────────────────────────────────────

        /// <summary>检查 NavMesh Surface 是否已烘焙。</summary>
        public static ValidationResult CheckNavMeshBaked()
        {
            bool baked = NavMesh.GetSettingsByIndex(0).agentTypeID != -1;
            // 更可靠的检查：尝试采样任意位置
            NavMesh.SamplePosition(Vector3.zero, out var hit, 0.1f, NavMesh.AllAreas);
            return new ValidationResult
            {
                category = "NavMesh",
                description = "NavMesh 是否已烘焙",
                severity = ValidationSeverity.Error,
                passed = hit.hit,
                fixHint = hit.hit ? null : "请在 Navigation 窗口中烘焙 NavMesh",
            };
        }

        /// <summary>检查指定位置是否在 NavMesh 上。</summary>
        public static ValidationResult CheckNavMeshPlacement(Vector3 position, string label)
        {
            NavMesh.SamplePosition(position, out var hit, 5f, NavMesh.AllAreas);
            return new ValidationResult
            {
                category = "NavMesh",
                description = $"位置 [{label}] 是否在 NavMesh 上",
                severity = ValidationSeverity.Warning,
                passed = hit.hit,
                fixHint = hit.hit ? null : $"请将 [{label}] 移至 NavMesh 可到达区域",
            };
        }

        // ── 碰撞 ──────────────────────────────────────────────

        /// <summary>检查 GameObject 是否有 Collider。</summary>
        public static ValidationResult CheckColliderPresent(GameObject go, string label = "地面")
        {
            bool hasCollider = go != null && go.GetComponentInChildren<Collider>(true) != null;
            return new ValidationResult
            {
                category = "碰撞",
                description = $"{label} 是否有 Collider",
                severity = ValidationSeverity.Error,
                passed = hasCollider,
                fixHint = hasCollider ? null : $"请为 [{label}] 添加 Collider 组件",
            };
        }

        /// <summary>检查 GameObject 是否非 Static（动态物体不应该是 Static）。</summary>
        public static ValidationResult CheckNotStatic(GameObject go, string label)
        {
            bool isStatic = go != null && go.isStatic;
            return new ValidationResult
            {
                category = "碰撞",
                description = $"{label} 是否非 Static",
                severity = ValidationSeverity.Info,
                passed = !isStatic,
                fixHint = isStatic ? $"请取消 [{label}] 的 Static 标记（动态 Prefab 不应为 Static）" : null,
            };
        }

        // ── 引用完整性 ────────────────────────────────────────

        /// <summary>检查 Prefab 引用是否有效（非 null 且资产存在）。</summary>
        public static ValidationResult CheckPrefabReference(GameObject prefab, string label)
        {
            if (prefab == null)
            {
                return new ValidationResult
                {
                    category = "引用完整性",
                    description = $"Prefab 引用 [{label}] 是否有效",
                    severity = ValidationSeverity.Error,
                    passed = false,
                    fixHint = $"请为 [{label}] 指定有效 Prefab",
                };
            }

            // 检查是否为磁盘资产（而非场景实例）
            if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(path))
                {
                    return new ValidationResult
                    {
                        category = "引用完整性",
                        description = $"Prefab 引用 [{label}] 是否有效",
                        severity = ValidationSeverity.Error,
                        passed = false,
                        fixHint = $"[{label}] 引用的不是有效的 Prefab 资产",
                    };
                }
            }

            return new ValidationResult
            {
                category = "引用完整性",
                description = $"Prefab 引用 [{label}] 是否有效",
                severity = ValidationSeverity.Error,
                passed = true,
            };
        }

        // ── 性能 ──────────────────────────────────────────────

        /// <summary>检查场景三角面数是否在阈值内。</summary>
        public static ValidationResult CheckTriangleCount(int maxTriangles = 10000)
        {
            int triCount = CountTrianglesInScene();
            bool ok = triCount <= maxTriangles;
            return new ValidationResult
            {
                category = "性能",
                description = $"场景三角面数: {triCount:N0}（阈值 ≤ {maxTriangles:N0}）",
                severity = ok ? ValidationSeverity.Info : ValidationSeverity.Warning,
                passed = ok,
                fixHint = ok ? null : $"面数过高，建议简化模型或启用 LOD",
            };
        }

        /// <summary>检查场景中 Light 数量是否在阈值内。</summary>
        public static ValidationResult CheckLightCount(int maxLights = 10)
        {
            int count = Object.FindObjectsOfType<Light>().Length;
            bool ok = count <= maxLights;
            return new ValidationResult
            {
                category = "性能",
                description = $"Light 数量: {count}（阈值 ≤ {maxLights}）",
                severity = ok ? ValidationSeverity.Info : ValidationSeverity.Warning,
                passed = ok,
                fixHint = ok ? null : $"Light 数量过多 ({count})，建议合并或使用烘焙",
            };
        }

        // ── 批量校验 ──────────────────────────────────────────

        /// <summary>运行全套场景校验，返回结果列表。</summary>
        public static List<ValidationResult> RunAllSceneChecks()
        {
            var results = new List<ValidationResult>();

            // 必需对象
            results.Add(CheckMainCamera());
            results.Add(CheckRequiredObject("PlayerSpawn"));

            // NavMesh
            results.Add(CheckNavMeshBaked());

            // 碰撞 — 检查 Tag=Ground 的物体
            var grounds = GameObject.FindGameObjectsWithTag("Ground");
            if (grounds.Length == 0)
            {
                results.Add(new ValidationResult
                {
                    category = "碰撞",
                    description = "地面 (Tag=Ground) 是否存在",
                    severity = ValidationSeverity.Error,
                    passed = false,
                    fixHint = "请确保场景中存在 Tag=Ground 的地面对象",
                });
            }
            else
            {
                results.Add(CheckColliderPresent(grounds[0], "Ground"));
            }

            // 性能
            results.Add(CheckTriangleCount());
            results.Add(CheckLightCount());

            return results;
        }

        /// <summary>计算汇总：通过数、失败数、警告数。</summary>
        public static (int passed, int failed, int warnings) Summarize(List<ValidationResult> results)
        {
            int p = 0, f = 0, w = 0;
            foreach (var r in results)
            {
                if (r.passed)
                {
                    if (r.severity == ValidationSeverity.Error) p++;
                    else p++;
                }
                else
                {
                    if (r.severity == ValidationSeverity.Error) f++;
                    else w++;
                }
            }
            return (p, f, w);
        }

        // ═══════════════════════════════════════════════════════════════
        // 内部辅助
        // ═══════════════════════════════════════════════════════════════

        /// <summary>粗略计算场景中所有 MeshRenderer 的总三角面数。</summary>
        static int CountTrianglesInScene()
        {
            int total = 0;
            var renderers = Object.FindObjectsOfType<MeshRenderer>();
            foreach (var mr in renderers)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                total += mf.sharedMesh.triangles.Length / 3;
            }
            var skinnedRenderers = Object.FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMesh == null) continue;
                total += smr.sharedMesh.triangles.Length / 3;
            }
            return total;
        }
    }
}
