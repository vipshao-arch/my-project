#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// Character Kit — 角色工具独立窗口（主角 + 敌人 + 技能编辑 + 装配）。
    ///
    /// 当前状态（2026-07-09）：
    ///   - CharacterKitPanel 已迭代至 Phase 3：流水线概览 + 点击步骤打开独立子面板
    ///   - 支持 Character / Enemy 两种目标类型切换
    ///   - Character 流水线步骤：Character Setup → Player Config → Override Setup → Network Setup →
    ///     Ragdoll Setup → SpringBone Setup → Weapon Setup → Diagnostic；Enemy 流水线另含 Enemy Config/Setup
    ///
    /// 菜单：Tools &gt; Character Kit &gt; Open Character Kit
    /// (与 Level Kit 的 Open Level Kit 同风格;一级路径与子菜单冲突会被 Unity 吞掉)
    /// </summary>
    public class CharacterKitWindow : EditorWindow
    {
        private CharacterKitPanel _panel;

        [MenuItem("Tools/Character Kit/Open Character Kit", false, 100)]
        static void Open()
        {
            var win = GetWindow<CharacterKitWindow>("Character Kit");
            win.minSize = new Vector2(640, 480);
        }

        void OnEnable()
        {
            _panel = new CharacterKitPanel();
        }

        void OnGUI()
        {
            _panel.OnGUI();
        }
    }
}
#endif
