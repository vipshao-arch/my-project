using UnityEngine;
using UnityEditor;

namespace Game.EditorTools.Shared
{
    /// <summary>
    /// 可复用的拖拽分隔条组件（Phase 0 基础设施）。
    ///
    /// 抽取自 SkillBuilderWizard 的 DrawVSplitHandle/DrawHSplitHandle 实现模式
    /// （增量偏移法：记录拖拽起始鼠标位置 + 起始值，用 delta 计算新值，避免反馈环闪烁）。
    /// SkillBuilderWizard 本体未做改动，本类供 Level Kit（Encounter Editor 时间轴）
    /// 和 GameDevSuiteWindow 的面板布局复用。
    ///
    /// 用法：
    ///   private SplitterState _vSplit = new SplitterState();
    ///   ...
    ///   _panelWidth = SplitterWidget.DrawVertical(rect, _vSplit, _panelWidth);
    /// </summary>
    public static class SplitterWidget
    {
        /// <summary>单个分隔条的拖拽状态（每个分隔条实例需要独立持有一份）</summary>
        public class SplitterState
        {
            public bool dragging;
            public float dragStartMouse;
            public float dragStartValue;
            public readonly int ctrlId = System.Guid.NewGuid().GetHashCode();
        }

        /// <summary>绘制一条垂直分隔条(左右布局用)，返回拖拽后的新宽度值</summary>
        public static float DrawVertical(Rect r, SplitterState state, float currentValue)
        {
            EditorGUI.DrawRect(r, EditorSkinPalette.BgSplit);
            EditorGUI.DrawRect(new Rect(r.x + r.width * 0.5f - 0.5f, r.y, 1f, r.height),
                new Color(EditorSkinPalette.SplitLine.r, EditorSkinPalette.SplitLine.g,
                    EditorSkinPalette.SplitLine.b, state.dragging ? 0.9f : 0.35f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.SplitResizeLeftRight);

            var e = Event.current;
            float result = currentValue;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                state.dragging = true;
                state.dragStartMouse = e.mousePosition.x;
                state.dragStartValue = currentValue;
                GUIUtility.hotControl = state.ctrlId;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == state.ctrlId && state.dragging)
            {
                float delta = e.mousePosition.x - state.dragStartMouse;
                result = state.dragStartValue + delta;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == state.ctrlId)
            {
                state.dragging = false;
                GUIUtility.hotControl = 0;
                e.Use();
            }
            return result;
        }

        /// <summary>绘制一条水平分隔条(上下布局用)，返回拖拽后的新高度值</summary>
        public static float DrawHorizontal(Rect r, SplitterState state, float currentValue, bool invert = true)
        {
            EditorGUI.DrawRect(r, EditorSkinPalette.BgSplit);
            EditorGUI.DrawRect(new Rect(r.x, r.y + r.height * 0.5f - 0.5f, r.width, 1f),
                new Color(EditorSkinPalette.SplitLine.r, EditorSkinPalette.SplitLine.g,
                    EditorSkinPalette.SplitLine.b, state.dragging ? 0.9f : 0.35f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.SplitResizeUpDown);

            var e = Event.current;
            float result = currentValue;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                state.dragging = true;
                state.dragStartMouse = e.mousePosition.y;
                state.dragStartValue = currentValue;
                GUIUtility.hotControl = state.ctrlId;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == state.ctrlId && state.dragging)
            {
                float delta = e.mousePosition.y - state.dragStartMouse;
                result = invert ? state.dragStartValue - delta : state.dragStartValue + delta;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == state.ctrlId)
            {
                state.dragging = false;
                GUIUtility.hotControl = 0;
                e.Use();
            }
            return result;
        }
    }
}
