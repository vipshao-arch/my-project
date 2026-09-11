#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Game.EditorTools.CombatSandbox
{
    /// <summary>
    /// Combat Sandbox — AI与战斗测试独立窗口(Live Test / Batch Sim / Replay，Phase 3 待开发)。
    /// 拆分说明见 CharacterKitWindow 头部注释。
    ///
    /// 菜单：Tools &gt; Combat Sandbox &gt; Open Combat Sandbox
    /// (与 Level Kit 的 Open Level Kit 同风格;一级路径与子菜单冲突会被 Unity 吞掉)
    /// </summary>
    public class CombatSandboxWindow : EditorWindow
    {
        private CombatSandboxPanel _panel;

        [MenuItem("Tools/Combat Sandbox/Open Combat Sandbox", false, 400)]
        static void Open()
        {
            var win = GetWindow<CombatSandboxWindow>("Combat Sandbox");
            win.minSize = new Vector2(640, 480);
        }

        void OnEnable()
        {
            _panel = new CombatSandboxPanel();
        }

        void OnGUI()
        {
            _panel.OnGUI();
        }
    }
}
#endif
