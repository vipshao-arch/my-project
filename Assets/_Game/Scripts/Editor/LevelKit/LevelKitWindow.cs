#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Game.EditorTools.LevelKit
{
    /// <summary>
    /// Level Kit — 关卡工具独立窗口(Composition / Encounter / Validate，Phase 2 待开发)。
    /// 拆分说明见 CharacterKitWindow 头部注释。
    ///
    /// 菜单：Tools &gt; Level Kit
    /// </summary>
    public class LevelKitWindow : EditorWindow
    {
        private LevelKitPanel _panel;

        [MenuItem("Tools/Level Kit/Open Level Kit", false, 300)]
        static void Open()
        {
            var win = GetWindow<LevelKitWindow>("Level Kit");
            win.minSize = new Vector2(640, 480);
        }

        void OnEnable()
        {
            _panel = new LevelKitPanel();
        }

        void OnGUI()
        {
            _panel.OnGUI();
        }
    }
}
#endif
