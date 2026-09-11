#if UNITY_EDITOR
using UnityEditor;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>
    /// Character Kit 编辑器会话持久化存储。
    /// 使用 ScriptableSingleton 在编译/重启后保持窗口状态。
    /// 与 EditorPrefs 不同：ScriptableSingleton 不会因编译重载而丢失数据。
    /// </summary>
    [FilePath("ProjectSettings/CharacterKitSession.asset", FilePathAttribute.Location.ProjectFolder)]
    public class CharacterKitSession : ScriptableSingleton<CharacterKitSession>
    {
        /// <summary>Tab 选中索引：0=Skill, 1=Setup。默认 1（Setup）。</summary>
        public int selectedTabIndex = 1;

        /// <summary>分隔条位置（左侧面板宽度，像素）。默认 250。</summary>
        public float splitterPos = 250f;

        /// <summary>流水线装配目标：0=Character, 1=Enemy。默认 0。</summary>
        public int pipelineTargetIndex = 0;

        /// <summary>Setup Tab — 选中的步骤索引（-1=未选中）。默认 -1。</summary>
        public int selectedStepIndex = -1;

        /// <summary>Setup Tab — 流水线步骤列表的垂直滚动位置。默认 0。</summary>
        public float setupScrollY = 0f;

        /// <summary>Setup Tab — Character 装配拖入的资源文件夹路径。默认空。</summary>
        public string charSetupFolderPath = "";

        /// <summary>Setup Tab — Enemy 装配拖入的资源文件夹路径。默认空。</summary>
        public string enemySetupFolderPath = "";

        public void Save() => Save(true);
    }
}
#endif
