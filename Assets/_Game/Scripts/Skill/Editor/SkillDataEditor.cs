// =====================================================================
//  SkillDataEditor —— 按 effectType 自动收拢 Inspector 字段
// ---------------------------------------------------------------------
//  目的:
//    SkillData.cs 把 11 种技能类型(melee / rectShot / chainBounce /
//    curvedProjectile / beam / AOE / trap / wall / summon / channeled /
//    selfBuff) 的兼容字段全堆在一个 ScriptableObject 里,Inspector
//    动辄 60+ 字段,大多数是当前技能用不到的"无效字段"。
//
//  本 Editor 在 Inspector 渲染时,按 effectType 跳过与当前技能类型
//    无关的字段,只显示:
//      • ①~⑤ + ⑤.1(所有技能通用的身份/动画/多段字段)
//      • ⑥ graphData 列表(完全保留,核心数据)
//      • ⑦~⑮ 中属于"当前 effectType 用得到的那一组"
//
//  安全性:
//    1. 字段名 0 改动、字段类型 0 改动 → .asset YAML / EditorJsonUtility
//       / WorkingCopySkillData 全部兼容
//    2. 序列化数据 0 损失(隐藏的字段仍在 SO 里,运行时仍可读,只是
//       Inspector 不画)
//    3. 不影响 Build(本文件在 Editor/ 目录下,不会进入运行时)
//
//  字段→技能类型 的对应关系(每行 = 1 个 effectType 用得到的字段组):
//    AreaOfEffect    → range / hitRadius / hitAngle / damage
//    RectShot        → rectShot* 全部(12 个)+ projectile* 全部(5 个)
//    ChainBounce     → chainBounce* 全部(4 个)
//    CurvedProjectile→ curveGravity / curveGroundSnapY
//    Beam            →(暂未建独立字段,跟随 RectShot 的 projectile 参数)
//    AOECircular     → aoeRadius / aoeCenterIsTargetPoint
//    Trap            → trap* 全部(3 个)
//    Wall            → wall* 全部(4 个)
//    Summon          → summon* 全部(4 个)
//    Channeled       → channel* 全部(4 个)
//    SelfBuff / None → 只显示共用字段(⑦ VFX/SFX 仍保留,通用)
//    MovementPolicy / facingMode / speedMultiplier → 隐藏
//      (因为这些是"角色行为"而不是"技能效果",MovementNode 才是正路,
//       老兼容字段已无运行时引用,见 HitDetector.cs:240+ 注释)
//
//  所有技能都保留 ⑦ VFX/SFX 通用字段,因为它是"身份级"通用起手/命中
//  反馈,任何技能都用得到(只是运行时优先取 graphData 里的 CastVFXNode/
//  HitVFXNode,老字段是 fallback)。
// =====================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.SkillSystem;

namespace Game.SkillSystem.EditorTools
{
    [CustomEditor(typeof(SkillData))]
    public class SkillDataEditor : UnityEditor.Editor
    {
        // ── 缓存(避免每帧重算)────────────────────────────────
        // ⑦~⑭ 兼容字段已全部从 Inspector 隐藏(2026-07-16)。
        // 运行时代码 100% 走 graphData 节点驱动,这些字段仅用于:
        //   1. Unity YAML 已完成 v2 迁移
        //   2. 运行时统一使用 graphData，不再保留旧字段读取。
        private static readonly HashSet<string> _alwaysVisible = new HashSet<string>
        {
            // ① 基础信息
            "skillName", "skillId", "icon", "category",
            // ② 时间参数
            "cooldown", "castTime", "channelDuration",
            // ③ 动画窗口
            "frontSwing", "backSwing",
            // ④ 动画融合
            "fadeInDuration", "fadeOutDuration",
            // ⑤ 动画身份
            "animTrigger", "animStateId", "animClipName", "animClips",
            // ⑥ 行为节点(核心)
            "graphData",
        };

        // ⑧ ⑮ 已被 HitDetector 走"老路径兼容"分支(8 个 .asset 已全部迁移,
        // 走 graphData 优先,所以这里也藏起来)
        private static readonly HashSet<string> _neverVisible = new HashSet<string>
        {
            "movementPolicy", "speedMultiplier", "facingMode",
        };

        // effectType → 该类型专属字段名集合(2026-07-16:全部置空,字段已从 Inspector 隐藏)
        // 保留字典结构以兼容未来若需要再次显示某些字段
        private static readonly Dictionary<EffectType, HashSet<string>> _byType =
            new Dictionary<EffectType, HashSet<string>>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var data = (SkillData)target;
            // 行为类型只由 graphData 节点决定；Legacy effectType 不参与 Inspector 逻辑。
            var effectType = EffectType.None;

            // ① 基础信息 — 始终先画(注意:effectType 是它的一部分,改它会触发重画)
            DrawHeader("① 基础信息");
            DrawProperty("skillName");
            DrawProperty("skillId");
            DrawProperty("icon");
            DrawProperty("category");

            // 当前行为类型由 graphData 决定，不再显示废弃 effectType。
            if (GUI.changed)
            {
                serializedObject.ApplyModifiedProperties();
                // 不写盘,只是内存改 effectType,让用户看到字段收拢
                Repaint();
                return;
            }

            EditorGUILayout.Space(8);

            // ② 时间参数
            DrawHeader("② 时间参数(秒)");
            DrawProperty("cooldown");
            DrawProperty("castTime");
            DrawProperty("channelDuration");

            EditorGUILayout.Space(4);

            // ③ 动画窗口
            DrawHeader("③ 动画窗口(归一化时间 0~1)");
            DrawProperty("frontSwing");
            DrawProperty("backSwing");

            EditorGUILayout.Space(4);

            // ④ 动画融合
            DrawHeader("④ 动画融合(秒)");
            DrawProperty("fadeInDuration");
            DrawProperty("fadeOutDuration");

            EditorGUILayout.Space(4);

            // ⑤ 动画身份
            DrawHeader("⑤ 动画身份");
            DrawProperty("animTrigger");
            DrawProperty("animStateId");
            DrawProperty("animClipName");
            // 多段开启时，animClips 由各段 MultiStageLayerData 驱动，禁用全局编辑
            bool isMulti = data.multiStage != null && data.multiStage.enabled
                           && data.multiStage.segments != null && data.multiStage.segments.Count > 0;
            if (isMulti)
            {
                EditorGUILayout.HelpBox("多段已启用：动画 Clip / VFX / SFX 由各段配置（见下方 multiStage.segments），全局字段已被接管。", MessageType.Info);
                using (new EditorGUI.DisabledScope(true))
                    DrawProperty("animClips");
            }
            else
            {
                DrawProperty("animClips");
            }
            // ⑤.1 多段配置：与 graphData 的 MultiStageLayerData 同步展示，
            // 便于已有 SkillData 直接编辑每段动画/VFX/SFX。
            DrawProperty("multiStage");

            EditorGUILayout.Space(8);

            // ⑥ graphData(核心,始终画完整列表)
            DrawHeader("⑥ 行为节点(graphData)");
            DrawGraphData(effectType);

            EditorGUILayout.Space(8);

            // graphData 是唯一技能行为配置源。
            EditorGUILayout.HelpBox(
                "当前 SkillData 已完成 v2 迁移。所有行为、动画表现、VFX/SFX 和命中参数均来自 graphData。",
                MessageType.None);

            // ⑮ 角色行为(已废弃,MovementNode 才是正路)
            // 整段不画,对应 _neverVisible

            serializedObject.ApplyModifiedProperties();
        }

        // ── 辅助:画单个属性(用 SerializedProperty 才能正确处理 Undo/多对象/嵌套)─
        private void DrawProperty(string name)
        {
            var prop = serializedObject.FindProperty(name);
            if (prop == null) return;
            EditorGUI.indentLevel++;
            // 2026-07-28(test07 对齐):中文字段标签
            EditorGUILayout.PropertyField(prop, new GUIContent(CnLabel(name)), true);
            EditorGUI.indentLevel--;
        }

        // 字段名 → 中文显示名(未收录的字段回退 Unity 默认显示)
        private static string CnLabel(string name)
        {
            switch (name)
            {
                case "skillName":        return "技能名称";
                case "skillId":          return "技能 ID";
                case "icon":             return "图标";
                case "category":         return "分类";
                case "cooldown":         return "冷却时间 (秒)";
                case "castTime":         return "吟唱时间 (秒)";
                case "channelDuration":  return "持续时长 (秒)";
                case "frontSwing":       return "前摇 (归一化 0~1)";
                case "backSwing":        return "后摇 (归一化 0~1)";
                case "fadeInDuration":   return "淡入时间 (秒)";
                case "fadeOutDuration":  return "淡出时间 (秒)";
                case "animTrigger":      return "动画 Trigger";
                case "animStateId":      return "状态 ID";
                case "animClipName":     return "状态名称";
                case "animClips":        return "AnimationClips";
                default:                 return ObjectNames.NicifyVariableName(name);
            }
        }

        // ── 辅助:画分组标题(用粗体大字号,与原 Header 风格一致)──
        private static void DrawHeader(string text)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 6, 4),
            };
            EditorGUILayout.LabelField(text, style);
        }

        // ── 辅助:画 graphData(整段保留,但展示当前 effectType 推荐节点)──
        private void DrawGraphData(EffectType effectType)
        {
            var prop = serializedObject.FindProperty("graphData");
            if (prop == null) return;

            EditorGUILayout.PropertyField(prop, true);

            // 推荐节点提示
            string hint = GetGraphDataHint(effectType);
            if (!string.IsNullOrEmpty(hint))
            {
                EditorGUILayout.HelpBox(hint, MessageType.None);
            }
        }

        private static string GetTypeHeader(EffectType t)
        {
            switch (t)
            {
                case EffectType.AreaOfEffect:     return "⑧ 近战挥击";
                case EffectType.RectShot:         return "⑨ 矩形弹幕";
                case EffectType.Beam:             return "⑨ 光束(沿用 RectShot 字段)";
                case EffectType.ChainBounce:      return "⑩ 链式弹射";
                case EffectType.CurvedProjectile: return "⑪ 抛物线";
                case EffectType.AOECircular:      return "⑫ 圆形 AOE";
                case EffectType.Trap:             return "⑬ 陷阱";
                case EffectType.Wall:             return "⑬ 墙体";
                case EffectType.Summon:           return "⑬ 召唤";
                case EffectType.Channeled:        return "⑭ 持续施法";
                default:                          return "⑧~⑭ 兼容字段";
            }
        }

        private static string GetNodeNameHint(EffectType t)
        {
            switch (t)
            {
                case EffectType.AreaOfEffect:     return "MeleeSwingNode";
                case EffectType.RectShot:         return "RectShotNode";
                case EffectType.Beam:             return "BeamNode";
                case EffectType.ChainBounce:      return "ChainBounceNode";
                case EffectType.CurvedProjectile: return "CurvedProjectileNode";
                case EffectType.AOECircular:      return "AOECircularNode";
                case EffectType.Trap:             return "TrapNode";
                case EffectType.Wall:             return "WallNode";
                case EffectType.Summon:           return "SummonNode";
                case EffectType.Channeled:        return "ChanneledNode";
                case EffectType.SelfBuff:         return "StatusEffectNode(applyToSelf=true)";
                default:                          return "对应节点";
            }
        }

        private static string GetGraphDataHint(EffectType t)
        {
            switch (t)
            {
                case EffectType.AreaOfEffect:
                    return "推荐:1 个 MeleeSwingNode(范围/半径/角度/伤害)";
                case EffectType.RectShot:
                    return "推荐:1 个 RectShotNode(形状/弹数/速度/穿透/追踪)";
                case EffectType.Beam:
                    return "推荐:1 个 BeamNode(宽度/射程/伤害)";
                case EffectType.ChainBounce:
                    return "推荐:1 个 ChainBounceNode(弹射次数/搜索半径/伤害衰减)";
                case EffectType.CurvedProjectile:
                    return "推荐:1 个 CurvedProjectileNode(宽/高/速度/重力/俯仰角)";
                case EffectType.AOECircular:
                    return "推荐:1 个 AOECircularNode(半径/中心/伤害)";
                case EffectType.Trap:
                    return "推荐:1 个 TrapNode(prefab/存活/触发冷却)";
                case EffectType.Wall:
                    return "推荐:1 个 WallNode(prefab/宽/高/存活)";
                case EffectType.Summon:
                    return "推荐:1 个 SummonNode(prefab/数量/存活/距离)";
                case EffectType.Channeled:
                    return "推荐:1 个 ChanneledNode(tick 间隔/量/时长覆盖/锁移动)";
                case EffectType.SelfBuff:
                    return "推荐:1 个 StatusEffectNode(applyToSelf=true)";
                default:
                    return "";
            }
        }
    }
}
