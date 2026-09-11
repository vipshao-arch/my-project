#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace Game.Character.EditorTools.CharacterKit
{
    public partial class CharacterKitPanel
    {
        /// <summary>
        /// 安全调用 EndLayoutGroup，当布局栈已污染时静默吞掉异常，防止级联报错。
        /// 用于 EditorGUILayout.EndHorizontal / EndVertical / EndScrollView 等。
        /// </summary>
        static void SafeEndLayout(System.Action endCall)
        {
            try { endCall(); }
            catch (System.Exception) { /* 布局栈已污染，静默跳过 */ }
        }

        static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            if (target == null) return null;
            var existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        /// <summary>流水线步骤状态变化回调，强制重绘窗口以更新 UI。</summary>
        void OnPipelineStepChanged(SetupPipeline.StepState state)
        {
            RepaintCharacterKitWindow();
        }

        /// <summary>强制重绘 Character Kit 窗口（流水线回调用）。</summary>
        static void RepaintCharacterKitWindow()
        {
            var win = EditorWindow.GetWindow<CharacterKitWindow>(false, null, false);
            if (win != null) win.Repaint();
        }

        /// <summary>递归创建目录。</summary>
        static void EnsureDir(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            var parts = dir.Split('/');
            string current = "";
            for (int i = 0; i < parts.Length; i++)
            {
                current = i == 0 ? parts[i] : $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(current))
                {
                    string parent = current.Substring(0, current.LastIndexOf('/'));
                    string folder = current.Substring(current.LastIndexOf('/') + 1);
                    AssetDatabase.CreateFolder(parent, folder);
                }
            }
        }

        /// <summary>从目标 Prefab/GameObject 的路径推导角色文件夹。</summary>
        static string DeriveCharacterFolder(GameObject target)
        {
            if (target == null) return null;
            string path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path)) return null;
            string dir = Path.GetDirectoryName(path).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder($"{dir}/SkillData"))
                return dir;
            string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder($"{parent}/SkillData"))
                return parent;
            return dir;
        }
    }
}
#endif
