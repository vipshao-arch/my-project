#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Game.EditorTools.CombatSandbox
{
    /// <summary>
    /// Phase 4.4：三 Kit 交叉集成 — 跨 Kit 跳转工具方法。
    /// </summary>
    public static class CrossKitBridge
    {
        // ═══════════════════════════════════════════════════════════
        // 菜单入口（可绑定到按钮）
        // ═══════════════════════════════════════════════════════════

        /// <summary>打开 Character Kit 编辑器窗口。</summary>
        public static void OpenCharacterKit()
        {
            EditorApplication.ExecuteMenuItem("Tools/Character Kit");
        }

        /// <summary>打开 Level Kit 编辑器窗口。</summary>
        public static void OpenLevelKit()
        {
            EditorApplication.ExecuteMenuItem("Tools/Level Kit/Open Level Kit");
        }

        /// <summary>打开 Combat Sandbox 窗口。</summary>
        public static void OpenCombatSandbox()
        {
            EditorApplication.ExecuteMenuItem("Tools/Combat Sandbox");
        }

        /// <summary>打开 Skill Builder Wizard。</summary>
        public static void OpenSkillBuilder()
        {
            EditorApplication.ExecuteMenuItem("Tools/Skill Builder Wizard");
        }

        // ═══════════════════════════════════════════════════════════
        // 便捷绘制方法
        // ═══════════════════════════════════════════════════════════

        /// <summary>绘制一个「打开 X Kit」按钮。</summary>
        public static bool DrawKitJumpButton(string label, string kitName, float height = 24f)
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new UnityEngine.Color(0.3f, 0.5f, 0.8f);
            bool clicked = UnityEngine.GUILayout.Button(
                $"🔗 {label}", UnityEngine.GUILayout.Height(height));
            UnityEngine.GUI.backgroundColor = prevBg;

            if (clicked)
            {
                switch (kitName)
                {
                    case "CharacterKit": OpenCharacterKit(); break;
                    case "LevelKit":     OpenLevelKit();     break;
                    case "CombatSandbox": OpenCombatSandbox(); break;
                    case "SkillBuilder": OpenSkillBuilder(); break;
                }
            }

            return clicked;
        }
    }
}
#endif
