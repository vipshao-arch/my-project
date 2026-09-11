using UnityEditor;
using UnityEngine;

// WebGL build helper. Run from menu Tools/Build/WebGL, or via batchmode:
//   Unity.exe -batchmode -projectPath <path> -executeMethod WebGLBuildSetup.BuildWebGL -outputPath <dir> -quit
public static class WebGLBuildSetup
{
    private const string DefaultOutput = "Build/WebGL";

    [MenuItem("Tools/Build/WebGL")]
    public static void BuildWebGLMenu()
    {
        BuildWebGL(DefaultOutput);
    }

    public static void BuildWebGL(string outputPath)
    {
        // Ensure Demo is scene index 0
        var scenes = EditorBuildSettings.scenes;
        Debug.Log($"[WebGLBuild] {scenes.Length} scenes in build settings.");
        for (int i = 0; i < scenes.Length; i++)
        {
            Debug.Log($"[WebGLBuild]   [{i}] enabled={scenes[i].enabled} {scenes[i].path}");
        }

        string[] activeScenes = System.Array.ConvertAll(scenes, s => s.path);
        BuildOptions opts = BuildOptions.None;
#if UNITY_WEBGL
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.memorySize = 512;
#endif
        BuildPipeline.BuildPlayer(activeScenes, outputPath, BuildTarget.WebGL, opts);
        Debug.Log($"[WebGLBuild] Done -> {outputPath}");
    }
}
