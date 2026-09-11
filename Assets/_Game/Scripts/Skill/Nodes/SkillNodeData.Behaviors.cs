// =====================================================================
//  SkillNodeData.Behaviors —— 节点行为(2026-07-21 执行层统一重构)
//
//  从 ScriptableObject SkillNode 双轨体系迁移:
//    - 行为直接挂在 POCO 上,SkillRunner(运行时)与 SkillPreviewRuntime(预览)
//      共用同一份实现
//    - 差异通过 NodeContext.isPreview / ctx.sink 屏蔽:
//        isPreview=true → 跳过 Physics 查询与伤害,只经 sink 播表现/画 Gizmo
//    - per-cast 状态(_fired)走 ctx.firedOnce(POCO 是共享资产,禁止存运行时状态)
//
//  原 SO 节点类(Nodes/*.cs)保留用于旧 Node_*.asset 反序列化兼容,
//  不再参与运行时执行。
// =====================================================================

using System.Collections.Generic;
using UnityEngine;
using Game.Character;

namespace Game.SkillSystem
{
    // ═══════════════════════════════════════════════════════════════
    //  ① VFX 节点行为
    // ═══════════════════════════════════════════════════════════════

    public partial class CastVFXData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnTick(NodeContext ctx)
        {
            if (ctx.animTime < triggerTime) return;
            if (!ctx.MarkFiredOnce()) return;
            Fire(ctx);
        }

        public override void OnPreviewTrigger(NodeContext ctx) => Fire(ctx);

        private void Fire(NodeContext ctx)
        {
            SkillNodeBehaviorUtil.SpawnNodeVFX(ctx, prefab, spawnBone, worldSpace,
                positionOffset, rotationOffset, scale, destroyAfterSeconds);
            if (sfx != null)
                ctx.sink?.PlaySFX(sfx, ctx.caster.position, sfxPitchRandomPercent);
        }
    }

    public partial class MidVFXData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnTick(NodeContext ctx)
        {
            if (ctx.animTime < triggerTime) return;
            if (!ctx.MarkFiredOnce()) return;
            Fire(ctx);
        }

        public override void OnPreviewTrigger(NodeContext ctx) => Fire(ctx);

        private void Fire(NodeContext ctx)
        {
            SkillNodeBehaviorUtil.SpawnNodeVFX(ctx, prefab, spawnBone, worldSpace,
                positionOffset, rotationOffset, scale, destroyAfterSeconds);
        }
    }

    public partial class HitVFXData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (ctx.isPreview)
            {
                // 预览:有受击目标(2026-08-03 预览靶子)则逐个挂到目标位置,
                // 无目标回退施法者位置展示 + gizmo 兜底
                var targets = ctx.previewHitTargets;
                if (targets != null && targets.Count > 0)
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        var t = targets[i];
                        if (t == null) continue;
                        Vector3 dir = (t.position - ctx.caster.position).normalized;
                        FireAt(ctx, t.position, dir.sqrMagnitude > 0.001f ? dir : ctx.caster.forward, t);
                    }
                }
                else
                {
                    FireAt(ctx, ctx.caster.position, ctx.caster.forward);
                    ctx.sink?.EmitHitGizmo(this, ctx.caster.position, 0.25f, 360f, 0.25f);
                }
                return;
            }
            if (hit.targets == null || hit.targets.Count == 0) return;

            for (int i = 0; i < hit.targets.Count; i++)
            {
                var t = hit.targets[i];
                if (t == null) continue;
                Vector3 hitDir = (t.transform.position - ctx.caster.position).normalized;
                FireAt(ctx, t.transform.position,
                    hitDir.sqrMagnitude > 0.001f ? hitDir : ctx.caster.forward, t.transform);
            }
        }

        /// <summary>
        /// target 非空且 spawnBone 已填:在目标身上解析挂点骨骼(命中身体部位),
        /// 特效挂为骨骼子物体(跟随移动/动画);spawnBone 留空=目标根位置不跟随(旧行为)。
        /// </summary>
        private void FireAt(NodeContext ctx, Vector3 point, Vector3 dir, Transform target = null)
        {
            if (prefab != null)
            {
                Transform bone = null;
                if (target != null && !string.IsNullOrEmpty(spawnBone))
                {
                    bone = FindChildRecursive(target, spawnBone);
                    if (bone != null) point = bone.position;
                }
                var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(rotationOffset);
                var go = ctx.sink?.SpawnVFX(prefab, point, rot, scale, bone, destroyAfterSeconds);
                if (go != null) ctx.spawnedThisCast.Add(go);
            }
            if (sfx != null)
                ctx.sink?.PlaySFX(sfx, point, sfxPitchRandomPercent);
        }

        /// <summary>递归按名查找子节点(骨骼名匹配,大小写敏感与 Unity 层级一致)。</summary>
        private static Transform FindChildRecursive(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindChildRecursive(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ② 近战节点行为
    // ═══════════════════════════════════════════════════════════════

    public partial class MeleeSwingData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (ctx?.caster == null) return;
            Vector3 origin  = ctx.caster.position + Vector3.up * originHeight
                            + ctx.caster.forward * originForwardOffset;
            Vector3 forward = ctx.caster.forward;

            if (ctx.isPreview)
            {
                ctx.sink?.EmitHitGizmo(this, origin, hitRadius, hitAngle, 0.5f);
                return;
            }

            // 判定体:OverlapBox(长=range 宽=hitRadius×2 高=verticalRange),
            // verticalRange=0 回退 hitRadius(近似旧胶囊纵向行为,兼容旧资产)。
            float vRange = verticalRange > 0f ? verticalRange : hitRadius;
            Vector3 halfExtents = new Vector3(hitRadius, vRange * 0.5f, range * 0.5f);
            Vector3 boxCenter   = origin + forward * (range * 0.5f);
            Collider[] hits = Physics.OverlapBox(boxCenter, halfExtents,
                Quaternion.LookRotation(forward), ctx.hitMask);

            var hitList = new List<GameObject>();
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i];
                if (SkillNodeBehaviorUtil.IsSelfCollider(ctx, col)) continue;
                if (hitList.Contains(col.gameObject)) continue;

                var d = col.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;

                Vector3 closest  = col.ClosestPoint(origin);
                Vector3 toTarget = closest - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f) toTarget = forward;
                float angle = Vector3.Angle(forward, toTarget.normalized);
                if (angle > hitAngle) continue;

                // 掩体格挡:攻击眼位(originHeight 胸口)到受击点的视线被挡则跳过
                if (checkObstacle && SkillNodeBehaviorUtil.IsBlockedByObstacle(
                        origin, col, obstacleMask, ctx.source)) continue;

                float dmg = damage > 0f ? damage : ctx.damage;
                SkillNodeBehaviorUtil.ApplyDamage(ctx, d, dmg, toTarget.normalized);
                hitList.Add(col.gameObject);
            }

            ctx.BroadcastHit(HitInfo.Many(hitList, origin, forward));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ③ 弹体 / 光束 / 链式 / 抛物线节点行为
    // ═══════════════════════════════════════════════════════════════

    public partial class RectShotData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (ctx.isPreview)
            {
                var center = ctx.caster.position + Vector3.up * spawnHeight;
                ctx.sink?.EmitHitGizmo(this, center,
                    Mathf.Max(width, height) * 0.5f, 360f, 0.3f);
                return;
            }

            int   c    = Mathf.Max(1, count);
            float half = c > 1 ? spreadAngle * 0.5f : 0f;

            for (int i = 0; i < c; i++)
            {
                float spreadYaw = 0f;
                if (c > 1) spreadYaw = Mathf.Lerp(-half, half, (float)i / (c - 1));
                float totalYaw = spreadYaw + initialYawOffset;
                Quaternion yawRot   = Quaternion.AngleAxis(totalYaw, Vector3.up);
                Quaternion pitchRot = Quaternion.AngleAxis(initialPitchOffset, Vector3.right);
                Vector3 shotDir = (yawRot * pitchRot) * ctx.caster.forward;

                Vector3 spawnPos = ctx.caster.position + shotDir * forwardOffset;
                spawnPos.y = ctx.caster.position.y + spawnHeight;

                var go = new GameObject(c > 1
                    ? $"[RectShot]_{ctx.source.name}_{i}"
                    : $"[RectShot]_{ctx.source.name}");
                go.transform.position = spawnPos;
                go.transform.rotation = Quaternion.LookRotation(shotDir);

                var rect = go.AddComponent<RectProjectile>();
                rect.shotWidth  = width;
                rect.shotHeight = height;
                rect.speed      = speed;
                rect.maxRange   = maxRange;

                float dmg = damage > 0f ? damage : ctx.damage;
                rect.Initialize(dmg, ctx.source, ctx.hitMask, ctx.skill, projectilePrefab,
                                projectileLocalOffset, projectileLocalEulerOffset,
                                projectileLifetime, projectileLocalScale,
                                shape, hitMode, pierceCount, homingEnabled, homingTurnRate, homingSearchRadius);
                // 掩体拦截注入(独立于 Initialize 签名,旧调用方不受影响)
                rect.SetObstacleBlocking(checkObstacle, obstacleMask);
                // 远端表现模式(S2-5b):弹体真实生成但伤害不落地
                if (ctx.visualOnly) rect.SetVisualOnly(true);
            }
        }
    }

    public partial class BeamData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            Vector3 origin  = ctx.caster.position + Vector3.up * originHeight;
            Vector3 forward = ctx.caster.forward;

            if (ctx.isPreview)
            {
                var end = origin + forward * range;
                // 中段长条 + 终点圆标(对齐原预览 TriggerNode 表现)
                ctx.sink?.EmitHitGizmo(this, (origin + end) * 0.5f, range * 0.5f, 15f, 0.4f);
                ctx.sink?.EmitHitGizmo(this, end, beamWidth, 360f, 0.3f);
                return;
            }

            RaycastHit[] hits = Physics.SphereCastAll(origin, beamWidth, forward, range, ctx.hitMask);

            var hitList = new List<GameObject>();
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i].collider;
                if (SkillNodeBehaviorUtil.IsSelfCollider(ctx, col)) continue;
                if (hitList.Contains(col.gameObject)) continue;
                var d = col.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;
                // 掩体格挡:照射眼位(originHeight)到受击点被挡则跳过
                if (checkObstacle && SkillNodeBehaviorUtil.IsBlockedByObstacle(
                        origin, col, obstacleMask, ctx.source)) continue;
                float dmg = damage > 0f ? damage : ctx.damage;
                SkillNodeBehaviorUtil.ApplyDamage(ctx, d, dmg, forward);
                hitList.Add(col.gameObject);
            }

            ctx.BroadcastHit(HitInfo.Many(hitList, origin, forward));
        }
    }

    public partial class ChainBounceData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (ctx.isPreview)
            {
                var center = ctx.caster.position;
                var fwd = ctx.caster.forward;
                // 首跳搜索圈 + 最大范围外环 + 模拟多跳方向标记
                ctx.sink?.EmitHitGizmo(this, center, searchRadius, 360f, 0.5f);
                if (maxBounceRange > searchRadius + 0.1f)
                    ctx.sink?.EmitHitGizmo(this, center, maxBounceRange, 360f, 0.2f);
                int marks = Mathf.Min(bounceCount, 6);
                for (int b = 0; b < marks; b++)
                {
                    float ang = b * 360f / Mathf.Max(bounceCount, 1);
                    var dir = Quaternion.Euler(0f, ang, 0f) * fwd;
                    ctx.sink?.EmitHitGizmo(this, center + dir * searchRadius, 0.3f, 360f, 0.25f);
                }
                return;
            }

            var chain = new List<GameObject>();
            var first = FindClosestTarget(ctx.caster.position, chain, ctx);
            if (first != null)
            {
                chain.Add(first);
                var d0 = first.GetComponentInParent<IDamageable>();
                float currentDmg = damage > 0f ? damage : ctx.damage;
                if (d0 != null)
                    SkillNodeBehaviorUtil.ApplyDamage(ctx, d0, currentDmg,
                        (first.transform.position - ctx.caster.position).normalized);

                Vector3 lastPos = first.transform.position;
                for (int i = 1; i < bounceCount; i++)
                {
                    var next = FindClosestTarget(lastPos, chain, ctx);
                    if (next == null) break;
                    chain.Add(next);
                    currentDmg *= damageFalloff;
                    var d = next.GetComponentInParent<IDamageable>();
                    if (d == null) break;
                    SkillNodeBehaviorUtil.ApplyDamage(ctx, d, currentDmg,
                        (next.transform.position - lastPos).normalized);
                    lastPos = next.transform.position;
                    if (maxBounceRange > 0f &&
                        Vector3.Distance(ctx.caster.position, lastPos) > maxBounceRange) break;
                }
            }

            ctx.BroadcastHit(HitInfo.Many(chain, ctx.caster.position, ctx.caster.forward));
        }

        private GameObject FindClosestTarget(Vector3 from, List<GameObject> exclude, NodeContext ctx)
        {
            Collider[] hits = Physics.OverlapSphere(from, searchRadius, ctx.hitMask);
            float best = float.MaxValue;
            GameObject bestGo = null;
            // 掩体格挡眼位:弹射点抬高 0.5m,避免贴地射线被 L0 台阶误挡(闪电链不穿墙)
            Vector3 eye = from + Vector3.up * 0.5f;
            for (int i = 0; i < hits.Length; i++)
            {
                var go = hits[i].gameObject;
                if (SkillNodeBehaviorUtil.IsSelfCollider(ctx, hits[i])) continue;
                if (exclude.Contains(go)) continue;
                var d = go.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;
                if (checkObstacle && SkillNodeBehaviorUtil.IsBlockedByObstacle(
                        eye, hits[i], obstacleMask, ctx.source)) continue;
                float dist = (go.transform.position - from).sqrMagnitude;
                if (dist < best) { best = dist; bestGo = go; }
            }
            return bestGo;
        }
    }

    public partial class CurvedProjectileData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (ctx.isPreview)
            {
                var center = ctx.caster.position + Vector3.up * spawnHeight;
                ctx.sink?.EmitHitGizmo(this, center,
                    Mathf.Max(width, height) * 0.5f, 360f, 0.3f);
                return;
            }

            Quaternion pitchRot = Quaternion.AngleAxis(launchPitch, ctx.caster.right);
            Vector3 shotDir = (pitchRot * ctx.caster.forward).normalized;

            Vector3 spawnPos = ctx.caster.position + shotDir * forwardOffset;
            spawnPos.y = ctx.caster.position.y + spawnHeight;

            var go = new GameObject($"[CurvedShot]_{ctx.source.name}");
            go.transform.position = spawnPos;
            go.transform.rotation = Quaternion.LookRotation(shotDir);

            var parabolic = go.AddComponent<ParabolicProjectile>();
            if (ctx.visualOnly) parabolic.SetVisualOnly(true);   // S2-5b:远端表现模式
            parabolic.Initialize(this, ctx);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ④ 生成 / AOE / 陷阱 / 墙体 / 召唤节点行为
    // ═══════════════════════════════════════════════════════════════

    public partial class AOECircularData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            Vector3 center = centerIsTargetPoint
                ? ctx.caster.position + ctx.caster.forward * ctx.range
                : ctx.caster.position;

            if (ctx.isPreview)
            {
                ctx.sink?.EmitHitGizmo(this, center, radius, 360f, 0.5f);
                return;
            }

            // 立体判定(2026-07-31):height>0=圆柱(半径 radius × 高度 height,自爆心平面上延),
            // 高台/坡道可控;height=0=旧行为完整球体(向上半球全覆盖,立体空间不可控)。
            Collider[] hits = height > 0f
                ? Physics.OverlapCapsule(center, center + Vector3.up * height, radius, ctx.hitMask)
                : Physics.OverlapSphere(center, radius, ctx.hitMask);
            var hitList = new List<GameObject>();
            // 掩体格挡眼位:爆心抬高 0.5m,避免贴地射线被 L0 台阶误挡
            Vector3 eye = center + Vector3.up * 0.5f;
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (SkillNodeBehaviorUtil.IsSelfCollider(ctx, c)) continue;
                var d = c.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;
                if (checkObstacle && SkillNodeBehaviorUtil.IsBlockedByObstacle(
                        eye, c, obstacleMask, ctx.source)) continue;
                float dmg = damage > 0f ? damage : ctx.damage;
                SkillNodeBehaviorUtil.ApplyDamage(ctx, d, dmg, (c.transform.position - center).normalized);
                hitList.Add(c.gameObject);
            }
            ctx.BroadcastHit(HitInfo.Many(hitList, center, ctx.caster.forward));
        }
    }

    public partial class TrapData
    {
        // 无 triggerTime 字段:与运行时命中帧(前摇结束)同语义
        public override float GetTriggerTime(SkillData skill, float clipLength)
            => SkillData.FrontSwingSeconds(clipLength, skill);

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            Vector3 pos = ctx.caster.position + ctx.caster.forward * spawnDistance;

            if (ctx.isPreview)
            {
                ctx.sink?.EmitHitGizmo(this, pos, 0.4f, 360f, 0.5f);
                if (trapPrefab != null)
                    ctx.sink.SpawnVFX(trapPrefab, pos, ctx.caster.rotation, 1f, null, 0f);
                return;
            }

            if (trapPrefab == null) return;
            var go = ctx.sink != null
                ? ctx.sink.SpawnVFX(trapPrefab, pos, ctx.caster.rotation, 1f, null, lifetime)
                : null;
            if (go != null) ctx.spawnedThisCast.Add(go);
        }
    }

    public partial class WallData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength)
            => SkillData.FrontSwingSeconds(clipLength, skill);

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            Vector3 pos = ctx.caster.position + ctx.caster.forward * spawnDistance;

            if (ctx.isPreview)
            {
                // 中心跨度弧 + 高度示意点 + prefab 实例展示
                ctx.sink?.EmitHitGizmo(this, pos, width * 0.5f, 60f, 0.5f);
                ctx.sink?.EmitHitGizmo(this, pos + Vector3.up * height * 0.5f, 0.2f, 360f, 0.4f);
                if (wallPrefab != null)
                {
                    var pv = ctx.sink.SpawnVFX(wallPrefab, pos, ctx.caster.rotation, 1f, null, 0f);
                    if (pv != null) pv.transform.localScale = new Vector3(width, height, 1f);
                }
                return;
            }

            if (wallPrefab == null) return;
            var go = ctx.sink != null
                ? ctx.sink.SpawnVFX(wallPrefab, pos, ctx.caster.rotation, 1f, null, lifetime)
                : null;
            if (go != null)
            {
                go.transform.localScale = new Vector3(width, height, 1f);
                ctx.spawnedThisCast.Add(go);
            }
        }
    }

    public partial class SummonData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength)
            => SkillData.FrontSwingSeconds(clipLength, skill);

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (ctx.isPreview)
            {
                var basePos = ctx.caster.position;
                var fwd = ctx.caster.forward;
                int marks = Mathf.Min(count, 4);
                for (int s = 0; s < marks; s++)
                {
                    float ang = s * 360f / Mathf.Max(count, 1);
                    var dir = Quaternion.Euler(0f, ang, 0f) * fwd;
                    ctx.sink?.EmitHitGizmo(this, basePos + dir * spawnDistance, 0.3f, 360f, 0.4f);
                }
                if (summonPrefab != null)
                    ctx.sink.SpawnVFX(summonPrefab, basePos + fwd * spawnDistance,
                        ctx.caster.rotation, 1f, null, 0f);
                return;
            }

            if (summonPrefab == null) return;
            for (int i = 0; i < count; i++)
            {
                float angle = (count > 1) ? Mathf.Lerp(-30f, 30f, (float)i / (count - 1)) : 0f;
                Quaternion rot = Quaternion.AngleAxis(angle, Vector3.up) * ctx.caster.rotation;
                Vector3 pos = ctx.caster.position + rot * Vector3.forward * spawnDistance;
                var go = ctx.sink != null
                    ? ctx.sink.SpawnVFX(summonPrefab, pos, rot, 1f, null, lifetime)
                    : null;
                if (go != null) ctx.spawnedThisCast.Add(go);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ⑤ 持续施法 / 移动策略 / 状态效果节点行为
    // ═══════════════════════════════════════════════════════════════

    public partial class ChanneledData
    {
        public float GetDuration(SkillData data)
            => durationOverride > 0f ? durationOverride : (data != null ? data.channelDuration : 0f);

        public float GetTickInterval() => Mathf.Max(0.05f, tickInterval);

        public override float GetTriggerTime(SkillData skill, float clipLength)
            => SkillData.FrontSwingSeconds(clipLength, skill);

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (ctx.isPreview)
            {
                // 范围环 + tick 节奏点(对齐原预览 TriggerNode 表现)
                var center = ctx.caster.position + Vector3.up * 0.3f;
                ctx.sink?.EmitHitGizmo(this, center, 3f, 360f, 0.5f);
                float dur = durationOverride > 0f ? durationOverride : 2f;
                int totalTicks = Mathf.Clamp(
                    Mathf.RoundToInt(dur / Mathf.Max(tickInterval, 0.1f)), 1, 8);
                for (int ti = 0; ti < totalTicks; ti++)
                {
                    float ang = ti * 360f / totalTicks;
                    var dir = Quaternion.Euler(0f, ang, 0f) * ctx.caster.forward;
                    ctx.sink?.EmitHitGizmo(this, center + dir * 3f, 0.15f, 360f, 0.15f);
                }
                return;
            }

            // 周期伤害:由 SkillRunner 按 tickInterval 反复调本方法
            // damage(可选覆盖) > tickAmount(默认); ctx.damage 不参与(用节点自己配置)
            if (hit.targets == null) return;
            float amount = damage > 0f ? damage : tickAmount;
            for (int i = 0; i < hit.targets.Count; i++)
            {
                var d = hit.targets[i] != null
                    ? hit.targets[i].GetComponentInParent<IDamageable>() : null;
                if (d == null || d.isDead) continue;
                SkillNodeBehaviorUtil.ApplyDamage(ctx, d, amount, ctx.caster.forward);
            }
        }
    }

    public partial class MovementData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnCast(NodeContext ctx)
        {
            if (ctx.isPreview) return;
            if (ctx.source == null) return;

            // 移动策略:SkillController/EnemyAI 在施法期间自行读取节点配置,
            // 恢复由各自超时逻辑处理(与原 MovementNode 一致,无显式恢复体)

            // 前冲
            if (dashOnCast)
            {
                var motor = ctx.source.GetComponent<ICharacterMotor>();
                if (motor != null)
                {
                    var rb = ctx.source.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic)
                        rb.AddForce(ctx.caster.forward * dashForce, ForceMode.VelocityChange);
                }
            }
        }
    }

    public partial class StatusEffectData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnHit(NodeContext ctx, HitInfo hit)
        {
            if (ctx.isPreview)
            {
                // 头顶状态标记:增益上方/减益下方
                float dir = applyToSelf ? 1f : -1f;
                var center = ctx.caster.position + Vector3.up * 2.2f;
                ctx.sink?.EmitHitGizmo(this, center, 0.25f, 360f, 0.4f);
                ctx.sink?.EmitHitGizmo(this, center + Vector3.up * dir * 0.3f, 0.12f, 360f, 0.3f);
                return;
            }

            if (effect == null) return;

            if (applyToSelf)
            {
                var mgr = ctx.source != null ? ctx.source.GetComponent<StatusEffectManager>() : null;
                if (mgr != null) mgr.Apply(effect);
            }
            else if (hit.targets != null)
            {
                int count = applyPerTarget ? hit.targets.Count : 1;
                for (int i = 0; i < count && i < hit.targets.Count; i++)
                {
                    var target = hit.targets[i];
                    if (target == null) continue;
                    var mgr = target.GetComponent<StatusEffectManager>();
                    if (mgr != null) mgr.Apply(effect);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ⑥ 时间轴层(动画片段层 / 多段层)
    //  运行时由 SkillAnimPlayer 直接消费,不参与 Runner 三阶段;
    //  预览侧 MultiStageLayerData 需要起手 VFX/SFX 展示。
    // ═══════════════════════════════════════════════════════════════

    public partial class AnimClipLayerData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;
    }

    public partial class MultiStageLayerData
    {
        public override float GetTriggerTime(SkillData skill, float clipLength) => triggerTime;

        public override void OnPreviewTrigger(NodeContext ctx)
        {
            SkillNodeBehaviorUtil.SpawnNodeVFX(ctx, vfxOnCast, spawnBone, worldSpace,
                positionOffset, rotationOffset, scale, destroyAfterSeconds);
            if (sfxOnCast != null)
                ctx.sink?.PlaySFX(sfxOnCast, ctx.caster.position, sfxPitchRandomPercent);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  共享工具:节点 VFX 生成(骨骼解析 + 偏移/旋转/缩放/挂接)
    //  被 CastVFX / MidVFX / MultiStageLayer 复用。
    // ═══════════════════════════════════════════════════════════════
    public static class SkillNodeBehaviorUtil
    {
        // ═══════════════════════════════════════════════════════════════
        //  掩体格挡判定(2026-07-30 原生实现,对齐《攻击与掩体格挡逻辑标准》)
        //
        //  判定式:被格挡 ⇔ Linecast(攻击眼位, 目标受击点) 命中 obstacleMask
        //  层非 Trigger 碰撞体。受击点 = 目标 Collider.bounds.center。
        //  角色(IDamageable)不充当掩体;施法者自身与目标自身不算遮挡。
        // ═══════════════════════════════════════════════════════════════
        /// <summary>
        /// 判断碰撞体是否属于施法者自身（含 Ragdoll 骨骼等子物体上的碰撞体）。
        /// 命中判定必须用它排除自身：只比较 col.gameObject == ctx.source 会漏掉子物体碰撞体，
        /// 导致近战/AOE 判定命中自身 Ragdoll 骨骼 → 自己伤害自己。
        /// </summary>
        public static bool IsSelfCollider(NodeContext ctx, Collider col)
        {
            if (ctx == null || col == null) return false;
            if (col.gameObject == ctx.source) return true;
            return ctx.caster != null && col.transform.IsChildOf(ctx.caster);
        }

        /// <summary>
        /// 伤害收口(S2-5b):远端表现模式(ctx.visualOnly)与预览模式一律不落地伤害;
        /// 否则正常 TakeDamage。节点内所有直接伤害调用统一走此入口。
        /// </summary>
        public static void ApplyDamage(NodeContext ctx, IDamageable d, float dmg, Vector3 dir)
        {
            if (ctx == null || ctx.visualOnly || ctx.isPreview) return;
            if (d == null) return;
            d.TakeDamage(dmg, ctx.source, dir);
        }

        public static bool IsBlockedByObstacle(Vector3 eyePos, Collider target,
            LayerMask obstacleMask, GameObject source)
        {
            if (target == null || obstacleMask.value == 0) return false;
            Vector3 targetPoint = target.bounds.center;
            if ((targetPoint - eyePos).sqrMagnitude < 0.0001f) return false;

            if (!Physics.Linecast(eyePos, targetPoint, out RaycastHit hit,
                    obstacleMask, QueryTriggerInteraction.Ignore))
                return false;

            if (hit.collider == target) return false;
            if (source != null && hit.collider.transform.IsChildOf(source.transform)) return false;
            if (hit.collider.GetComponentInParent<IDamageable>() != null) return false;
            return true;
        }

        public static GameObject SpawnNodeVFX(NodeContext ctx, GameObject prefab,
            string spawnBone, bool worldSpace, Vector3 positionOffset,
            Vector3 rotationOffset, float scale, float destroyAfterSeconds)
        {
            if (prefab == null || ctx == null || ctx.caster == null) return null;

            Transform bone = ctx.sink != null
                ? ctx.sink.ResolveSpawnBone(ctx, spawnBone)
                : ctx.caster;
            if (bone == null) bone = ctx.caster;

            Vector3 pos = bone.position + bone.TransformDirection(positionOffset);
            Quaternion rot = worldSpace
                ? Quaternion.LookRotation(ctx.caster.forward, Vector3.up) * Quaternion.Euler(rotationOffset)
                : bone.rotation * Quaternion.Euler(rotationOffset);

            var go = ctx.sink != null
                ? ctx.sink.SpawnVFX(prefab, pos, rot, scale, worldSpace ? null : bone, destroyAfterSeconds)
                : null;
            if (go != null) ctx.spawnedThisCast.Add(go);
            return go;
        }
    }
}
