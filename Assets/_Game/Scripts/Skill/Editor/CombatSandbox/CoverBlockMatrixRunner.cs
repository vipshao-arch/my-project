#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Game.Character;

namespace Game.SkillSystem.EditorTools
{
    /// <summary>
    /// TR-4.1 格挡矩阵回归(P3,2026-07-30 建立)。
    ///
    /// 在远离场景的空旷坐标(500,0,500)临时搭建标准判定场,对
    /// "6 攻击形式 × 4 掩体级(L0~L3)"全部组合调用<b>真实运行时代码路径</b>:
    ///   LoS 系(近战/射线/闪电链/AOE)→ SkillNodeBehaviorUtil.IsBlockedByObstacle
    ///   矩形投射物                  → RectProjectile.BoxCastObstacle(判定单点真源)
    ///   抛物线                      → 设计豁免(反射断言无 checkObstacle 字段)
    /// 运行结束销毁判定场,不留场景脏数据。
    ///
    /// 期望矩阵(几何推出,与《攻击与掩体格挡逻辑标准》对齐):
    ///   L0(0.4m):任何形态不格挡;
    ///   L1(0.9m):高眼位(1.0m 射线系)站立命中/蹲下格挡 ← 蹲伏格挡闭环;
    ///            低眼位(近战0.8/链0.5/AOE爆心0.5)站立蹲下均格挡;
    ///   L2(1.8m)/L3(3.5m):全格挡(抛物线豁免越顶)。
    /// 标准几何:攻击方 z=-2.5,掩体 z=0(厚0.6m),目标 z=+1;
    /// 站立胶囊高1.8(中心0.9),蹲胶囊高1.2(中心0.6,对齐 ControlCapsuleHeight /1.5)。
    /// </summary>
    public static class CoverBlockMatrixRunner
    {
        private static readonly Vector3 ArenaOrigin = new Vector3(500f, 0f, 500f);
        private static readonly (string name, float h)[] Levels =
        {
            ("L0(0.4m)", 0.4f), ("L1(0.9m)", 0.9f), ("L2(1.8m)", 1.8f), ("L3(3.5m)", 3.5f),
        };
        private static readonly LayerMask ObstacleMask = 1; // Default 层

        private struct Cell
        {
            public string form, level, stance;
            public bool expectedBlocked, actualBlocked;
            public string note;
        }

        [MenuItem("Tools/Combat Sandbox/Run Cover Block Matrix")]
        public static void RunMenu() => RunBatch();

        /// <summary>CI/batchmode 入口:-executeMethod ...CoverBlockMatrixRunner.RunBatchCLI,失败退出码 1。</summary>
        public static void RunBatchCLI()
        {
            int fail = RunBatch();
            if (fail > 0 && Application.isBatchMode)
                EditorApplication.Exit(1);
        }

        /// <summary>跑全部矩阵用例,返回失败数。</summary>
        public static int RunBatch()
        {
            var cells = new List<Cell>();
            GameObject arena = null;
            GameObject cover = null;
            try
            {
                arena = new GameObject("_CoverMatrixArena");
                arena.transform.position = ArenaOrigin;

                // 目标胶囊(站立 1.8m / 蹲下 1.2m,中心随缩放升降,与 Motor 蹲伏同构)。
                // 必须挂 IDamageable:两胶囊同位会互为遮挡,且按游戏语义"角色不充当掩体",
                // 无 IDamageable 的胶囊会被 LoS/BoxCast 计为掩体导致假格挡。
                var targetStand  = CreateCapsule("Target_Stand",  new Vector3(0f, 0.9f, 1f), new Vector3(0.7f, 0.9f, 0.7f), arena.transform);
                var targetCrouch = CreateCapsule("Target_Crouch", new Vector3(0f, 0.6f, 1f), new Vector3(0.47f, 0.6f, 0.47f), arena.transform);
                targetStand.AddComponent<DummyDamageable>();
                targetCrouch.AddComponent<DummyDamageable>();

                // LoS 行:形态 / 眼位(与节点代码一致) / 期望(站立,蹲下)
                var losRows = new[]
                {
                    new LosRow("MeleeSwing", new Vector3(0f, 0.8f, -2.5f),
                        new[]{false,true,true,true}, new[]{false,true,true,true}),
                    new LosRow("Beam",       new Vector3(0f, 1.0f, -2.5f),
                        new[]{false,false,true,true}, new[]{false,true,true,true}),
                    new LosRow("Chain",      new Vector3(0f, 0.5f, -2.5f),
                        new[]{false,true,true,true}, new[]{false,true,true,true}),
                    new LosRow("AOE",        new Vector3(0f, 0.5f, -0.5f),
                        new[]{false,true,true,true}, new[]{false,true,true,true}),
                };

                // 矩形投射物截面默认值(读真实组件,避免魔法数漂移)
                var tmpGo = new GameObject("_tmp_rect");
                var tmpRect = tmpGo.AddComponent<RectProjectile>();
                Vector3 halfExtents = new Vector3(tmpRect.shotWidth * 0.2f, tmpRect.shotHeight * 0.2f, 0.05f);
                Object.DestroyImmediate(tmpGo);
                var rectExpected = new[]{false,true,true,true}; // 飞行1.0m 截面0.72m:L0 越过,L1+ 拦截

                for (int idx = 0; idx < Levels.Length; idx++)
                {
                    cover = CreateBox($"Cover_{Levels[idx].name}",
                        Vector3.zero, Levels[idx].h, arena.transform);
                    Physics.SyncTransforms();
                    foreach (var row in losRows)
                    {
                        bool standBlocked = SkillNodeBehaviorUtil.IsBlockedByObstacle(
                            ArenaOrigin + row.eye, targetStand.GetComponent<Collider>(), ObstacleMask, null);
                        cells.Add(new Cell { form = row.form, level = Levels[idx].name, stance = "站立",
                            expectedBlocked = row.standBlocked[idx], actualBlocked = standBlocked });

                        bool crouchBlocked = SkillNodeBehaviorUtil.IsBlockedByObstacle(
                            ArenaOrigin + row.eye, targetCrouch.GetComponent<Collider>(), ObstacleMask, null);
                        cells.Add(new Cell { form = row.form, level = Levels[idx].name, stance = "蹲下",
                            expectedBlocked = row.crouchBlocked[idx], actualBlocked = crouchBlocked,
                            note = (row.form == "Beam" && idx == 1) ? "蹲伏格挡闭环" : "" });
                    }

                    // RectProjectile 拦截(真实单点入口,飞行高 1.0m 水平直射)
                    bool intercepted = RectProjectile.BoxCastObstacle(
                        ArenaOrigin + new Vector3(0f, 1.0f, -2.5f), Quaternion.LookRotation(Vector3.forward),
                        halfExtents, 4.0f, ObstacleMask, null, out _);
                    cells.Add(new Cell { form = "RectProjectile", level = Levels[idx].name, stance = "-",
                        expectedBlocked = rectExpected[idx], actualBlocked = intercepted });

                    Object.DestroyImmediate(cover);
                    cover = null;
                }

                // 抛物线:设计豁免(无 checkObstacle 字段),任何掩体级不拦截
                bool curvedExempt =
                    typeof(CurvedProjectileData).GetField("checkObstacle") == null &&
                    typeof(ParabolicProjectile).GetField("checkObstacle") == null;
                foreach (var lv in Levels)
                    cells.Add(new Cell { form = "CurvedProjectile", level = lv.name, stance = "-",
                        expectedBlocked = false, actualBlocked = !curvedExempt, note = "设计豁免:抛物线越顶" });

                // 角色不充当掩体:无掩体,中间站一个 IDamageable → 不格挡
                var dummy = CreateCapsule("DummyActor", new Vector3(0f, 0.9f, -1f), new Vector3(0.7f, 0.9f, 0.7f), arena.transform);
                dummy.AddComponent<DummyDamageable>();
                Physics.SyncTransforms();
                bool actorBlocks = SkillNodeBehaviorUtil.IsBlockedByObstacle(
                    ArenaOrigin + new Vector3(0f, 1f, -2.5f), targetStand.GetComponent<Collider>(), ObstacleMask, null);
                cells.Add(new Cell { form = "LoS(角色挡线)", level = "无掩体", stance = "站立",
                    expectedBlocked = false, actualBlocked = actorBlocks, note = "IDamageable 不充当掩体" });
            }
            finally
            {
                if (cover != null) Object.DestroyImmediate(cover);
                if (arena != null) Object.DestroyImmediate(arena);
            }

            return Report(cells);
        }

        private static int Report(List<Cell> cells)
        {
            int fail = 0;
            var sb = new StringBuilder();
            sb.AppendLine("══ TR-4.1 格挡矩阵回归 ══");
            foreach (var c in cells)
            {
                bool ok = c.expectedBlocked == c.actualBlocked;
                string line = $"  {(ok ? "PASS" : "FAIL")} {c.form,-16} {c.level,-9} {c.stance,-2} " +
                              $"期望={(c.expectedBlocked ? "格挡" : "命中")} 实际={(c.actualBlocked ? "格挡" : "命中")}" +
                              (string.IsNullOrEmpty(c.note) ? "" : $"  ({c.note})");
                sb.AppendLine(line);
                if (!ok)
                {
                    fail++;
                    // 失败逐行单独输出:多行整表在部分日志管道会被截断,单行保证可见
                    Debug.LogError($"[CoverMatrix] FAIL {c.form} {c.level} {c.stance} " +
                        $"期望={(c.expectedBlocked ? "格挡" : "命中")} 实际={(c.actualBlocked ? "格挡" : "命中")}");
                }
            }
            sb.Append($"══ 结果:{cells.Count - fail}/{cells.Count} 通过 ══");
            string text = sb.ToString();
            if (fail > 0) Debug.LogError(text);
            else Debug.Log(text);
            return fail;
        }

        private struct LosRow
        {
            public string form;
            public Vector3 eye;
            public bool[] standBlocked, crouchBlocked;
            public LosRow(string f, Vector3 e, bool[] s, bool[] c) { form = f; eye = e; standBlocked = s; crouchBlocked = c; }
        }

        private static GameObject CreateBox(string name, Vector3 pos, float height, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0f, height * 0.5f, 0f);
            go.transform.localScale = new Vector3(2f, height, 0.6f);
            go.layer = 0; // Default 层非 Trigger(掩体格挡前提)
            return go;
        }

        private static GameObject CreateCapsule(string name, Vector3 pos, Vector3 scale, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.layer = 0;
            return go;
        }

        /// <summary>占位 IDamageable:验证"角色不充当掩体"语义。</summary>
        private class DummyDamageable : MonoBehaviour, IDamageable
        {
            public float currentHealth => 1f;
            public float maxHealth => 1f;
            public bool isDead => false;
#pragma warning disable 67
            public event System.Action<float> OnHealthChanged;
            public event System.Action OnDeath;
#pragma warning restore 67
            public void TakeDamage(float damage, GameObject source, Vector3 hitDir) { }
            public void Heal(float amount) { }
        }
    }
}
#endif
