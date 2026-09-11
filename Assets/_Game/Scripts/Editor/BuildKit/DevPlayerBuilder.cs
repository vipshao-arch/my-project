#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 一键开发版打包(2026-07-31,联机/阵营时代打包流程收口)。
    ///
    /// 前置链(顺序执行,替代人工记忆):
    ///   1. Build Player Resources Entry——联机 Resources 四件(玩家/敌人变体+relay+match)
    ///      + 全部 NetworkAnimator 引用兜底扫描;
    ///   2. Rebuild Skill Catalog——远端技能名解析依赖(独立包无编辑器兜底);
    ///   3. 构建场景=test_scene + pvp_arena(写回 EditorBuildSettings);
    ///   4. 窗口化可拖拽(Windowed + resizableWindow);
    ///   5. BuildPipeline.BuildPlayer(Development)→ Builds/test09_dev.exe。
    ///
    /// 报告单行输出(MCP 控制台多行只显首行坑)。
    /// </summary>
    public static class DevPlayerBuilder
    {
        private const string OutputPath = "Builds/test09_dev.exe";
        private static readonly string[] ScenePaths =
        {
            "Assets/_Game/TestScene/test_scene.unity",
            "Assets/_Game/TestScene/pvp_arena.unity",
        };

        [MenuItem("Tools/Build/Build Dev Player (Windowed)", false, 600)]
        public static void Build()
        {
            // 1. 联机 Resources 入口(内含 NetworkAnimator 引用兜底 Fix)
            NetworkPrefabFixer.BuildPlayerResourcesEntry();

            // 2. 技能目录(远端技能解析)
            Game.SkillSystem.EditorTools.SkillCatalogBuilder.Rebuild();

            // 3. 构建场景列表
            var scenes = new EditorBuildSettingsScene[ScenePaths.Length];
            for (int i = 0; i < ScenePaths.Length; i++)
                scenes[i] = new EditorBuildSettingsScene(ScenePaths[i], true);
            EditorBuildSettings.scenes = scenes;

            // 4. 窗口化可拖拽
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            // 5. 构建
            var result = BuildPipeline.BuildPlayer(scenes, OutputPath,
                BuildTarget.StandaloneWindows64, BuildOptions.Development);

            var summary = result.summary;
            Debug.Log($"[Build] {summary.result} errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                      $"size={summary.totalSize / (1024f * 1024f):F1}MB time={summary.totalTime.TotalSeconds:F0}s → {OutputPath}");
            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorUtility.RevealInFinder(OutputPath);
        }
    }
}
#endif
