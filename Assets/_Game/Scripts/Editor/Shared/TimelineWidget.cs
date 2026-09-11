using UnityEngine;
using UnityEditor;

namespace Game.EditorTools.Shared
{
    /// <summary>
    /// 可复用的时间轴标尺/网格绘制组件（Phase 0 基础设施）。
    ///
    /// 抽取自 SkillBuilderWizard 时间轴渲染中与"技能节点"无关的通用部分
    /// （秒数标尺、网格线、播放头绘制），供 Level Kit 的 Encounter Editor（波次编排时间轴）
    /// 和 Combat Sandbox 的 AIDecisionRecorder（状态回放时间轴）复用。
    ///
    /// 注意：SkillBuilderWizard 本体的时间轴实现（节点拖拽/多态序列化等业务逻辑）保持不变，
    /// 不依赖本类，避免牵动已验证稳定的技能编辑工作流。
    /// </summary>
    public static class TimelineWidget
    {
        /// <summary>
        /// 绘制时间轴标尺（秒数刻度 + 网格线），自适应刻度间隔。
        /// </summary>
        /// <param name="rect">标尺绘制区域</param>
        /// <param name="minT">时间轴起始时间(秒)</param>
        /// <param name="maxT">时间轴结束时间(秒)</param>
        /// <param name="pixelsPerSecond">每秒对应的像素数(缩放级别)</param>
        /// <param name="scrollX">水平滚动偏移(像素)</param>
        public static void DrawRuler(Rect rect, float minT, float maxT, float pixelsPerSecond, float scrollX)
        {
            EditorGUI.DrawRect(rect, EditorSkinPalette.BgHeader);
            if (pixelsPerSecond <= 0.01f) return;

            // 自适应刻度间隔:根据缩放级别选 0.1/0.5/1/2/5/10 秒
            float[] steps = { 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f };
            float step = steps[steps.Length - 1];
            foreach (var s in steps)
            {
                if (s * pixelsPerSecond >= 40f) { step = s; break; }
            }

            int startIdx = Mathf.FloorToInt(minT / step);
            int endIdx = Mathf.CeilToInt(maxT / step);
            var lineCol = EditorSkinPalette.Divider;
            var textStyle = EditorSkinPalette.TipStyle();

            for (int i = startIdx; i <= endIdx; i++)
            {
                float t = i * step;
                float x = rect.x + t * pixelsPerSecond - scrollX;
                if (x < rect.x - 20f || x > rect.xMax + 20f) continue;

                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), lineCol);
                GUI.Label(new Rect(x + 2f, rect.y + 1f, 60f, rect.height - 2f), $"{t:0.##}s", textStyle);
            }
        }

        /// <summary>绘制播放头竖线(跨轨道区域)</summary>
        public static void DrawPlayhead(Rect trackAreaRect, float playheadT, float pixelsPerSecond, float scrollX, Color color)
        {
            float x = trackAreaRect.x + playheadT * pixelsPerSecond - scrollX;
            if (x < trackAreaRect.x - 2f || x > trackAreaRect.xMax + 2f) return;
            EditorGUI.DrawRect(new Rect(x - 0.5f, trackAreaRect.y, 1.5f, trackAreaRect.height), color);
        }

        /// <summary>绘制轨道行交替底色(斑马纹，提升多行可读性)</summary>
        public static void DrawRowBackground(Rect rowRect, int rowIndex)
        {
            var col = (rowIndex % 2 == 0) ? EditorSkinPalette.BgRowAlt1 : EditorSkinPalette.BgRowAlt2;
            EditorGUI.DrawRect(rowRect, col);
        }
    }
}
