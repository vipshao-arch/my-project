using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Game.Character.Editor
{
    /// <summary>
    /// Export Package — 动态扫描 Assets/_Game 一级目录，按实际结构勾选导出。
    ///
    /// 设计：
    ///   - 启动时扫描 _Game 一级子目录 + 一级文件，动态生成勾选列表（不写死）
    ///   - 已知目录附中文语义标签，未知目录显示原始目录名
    ///   - 勾选状态按路径用 EditorPrefs 持久化（目录增删不影响其他项的记忆）
    ///   - 额外自定义路径折叠区（用于 _Game 之外的特殊目录）
    ///
    /// 菜单：Tools > Export Package
    /// </summary>
    public class ExportPackageTool : EditorWindow
    {
        // ─────────────────────────────────────────────────────────────────
        // 已知目录的语义标签（未匹配的目录显示原名）
        // ─────────────────────────────────────────────────────────────────

        static readonly Dictionary<string, string> KnownLabels = new Dictionary<string, string>
        {
            { "Scripts",    "核心脚本 (运行时 + Editor 工具)" },
            { "character",  "角色资产 (Prefab/Controller/动画/Mask)" },
            { "Resources",  "运行时资源 (Resources.Load 依赖)" },
            { "Animations", "通用动作动画 (梯子/翻越等)" },
            { "VFX",        "VFX 特效 (材质/Prefab)" },
            { "Weapons",    "武器 (数据/Prefab)" },
            { "TestScene",  "测试场景与关卡原型 (调试用)" },
            { "HUD",        "HUD 界面资源" },
            { "Materials",  "共享材质" },
            { "Meshes",     "共享网格" },
            { "Docs",       "项目文档 (搭建指南等，不进游戏包)" },
            { "Editor",     "编辑器工具" },
        };

        // 默认不勾选的目录（调试用内容 / 纯文档）
        static readonly HashSet<string> DefaultOffDirs = new HashSet<string> { "TestScene", "Docs" };

        const string GameRoot = "Assets/_Game";

        // ─────────────────────────────────────────────────────────────────
        // 动态项
        // ─────────────────────────────────────────────────────────────────

        class ExportItem
        {
            public string Path;      // Assets/ 相对路径
            public string Label;     // 显示标签
            public bool   IsFile;    // 一级散文件
        }

        List<ExportItem> _items;
        bool[] _itemOn;
        List<string> _extraPaths;
        bool _includeDepends = true;
        bool _showExtraPaths;
        Dictionary<string, int> _fileCountCache;

        const string PrefsPrefix = "ExportPackage.Item.";
        const string PrefsExtra  = "ExportPackage.ExtraPaths";
        const string PrefsDeps   = "ExportPackage.IncludeDepends";

        // ─────────────────────────────────────────────────────────────────
        // 菜单入口
        // ─────────────────────────────────────────────────────────────────

        [MenuItem("Tools/Export Package", false, 500)]
        static void Open()
        {
            var win = GetWindow<ExportPackageTool>("Export Package");
            win.minSize = new Vector2(520, 520);
        }

        // ─────────────────────────────────────────────────────────────────
        // EditorWindow
        // ─────────────────────────────────────────────────────────────────

        void OnEnable()
        {
            _includeDepends = EditorPrefs.GetBool(PrefsDeps, true);
            string extra = EditorPrefs.GetString(PrefsExtra, "");
            _extraPaths = string.IsNullOrEmpty(extra)
                ? new List<string>()
                : extra.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            _fileCountCache = new Dictionary<string, int>();
            RescanDirectories();
        }

        /// <summary>扫描 _Game 一级目录结构，动态生成勾选列表。</summary>
        void RescanDirectories()
        {
            string absRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../", GameRoot));
            _items = new List<ExportItem>();

            if (Directory.Exists(absRoot))
            {
                // 一级子目录
                foreach (var dir in Directory.GetDirectories(absRoot).OrderBy(d => d))
                {
                    string name = Path.GetFileName(dir);
                    _items.Add(new ExportItem
                    {
                        Path = $"{GameRoot}/{name}",
                        Label = KnownLabels.TryGetValue(name, out var label) ? label : name,
                        IsFile = false,
                    });
                }
                // 一级散文件（如 SkillAvatarMask.mask 这类根目录资产）
                foreach (var file in Directory.GetFiles(absRoot).OrderBy(f => f))
                {
                    if (file.EndsWith(".meta")) continue;
                    string name = Path.GetFileName(file);
                    _items.Add(new ExportItem
                    {
                        Path = $"{GameRoot}/{name}",
                        Label = name + " (根目录文件)",
                        IsFile = true,
                    });
                }
            }

            // 恢复勾选状态：以路径为 key 记忆；新出现的项用默认规则
            _itemOn = new bool[_items.Count];
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                bool defaultOn = item.IsFile || !DefaultOffDirs.Contains(Path.GetFileName(item.Path));
                _itemOn[i] = EditorPrefs.GetBool(PrefsPrefix + item.Path, defaultOn);
            }
        }

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Export Package", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻ 重扫目录", EditorStyles.miniButton, GUILayout.Width(80)))
                RescanDirectories();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox($"按 {GameRoot} 实际目录结构动态呈现，勾选要导出的内容。勾选状态自动保存。", MessageType.Info);
            EditorGUILayout.HelpBox("联机内容已默认包含：Scripts(Net 联机层) + Resources(联机 prefab 四件+技能目录)。\n导出时自动生成 manifest.test09.json(已剔除本地路径依赖) 与 IMPORT_README.txt —— 导入方须先合并包依赖(NGO 1.9.1)再导包。", MessageType.None);

            EditorGUILayout.Space(6);

            // ── 动态勾选项 ──
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (_items.Count == 0)
            {
                EditorGUILayout.LabelField("（未扫描到目录，请检查 Assets/_Game 是否存在）", EditorStyles.miniLabel);
            }
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                bool exists = CheckPathExists(item.Path);
                int count = exists ? GetFileCount(item.Path) : 0;

                EditorGUILayout.BeginHorizontal();

                var oldColor = GUI.color;
                if (!exists) GUI.color = new Color(1f, 0.55f, 0.55f);
                bool newOn = EditorGUILayout.ToggleLeft(item.Label, _itemOn[i], EditorStyles.boldLabel);
                GUI.color = oldColor;

                if (newOn != _itemOn[i])
                {
                    _itemOn[i] = newOn;
                    EditorPrefs.SetBool(PrefsPrefix + item.Path, newOn);
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(exists ? $"{count} 文件" : "不存在", EditorStyles.miniLabel, GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField($"    {item.Path}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();

            // 全选 / 全不选
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(60)))
                SetAllItems(true);
            if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(60)))
                SetAllItems(false);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ── 额外自定义路径（折叠区）──
            _showExtraPaths = EditorGUILayout.Foldout(_showExtraPaths, $"额外路径（_Game 之外，{_extraPaths.Count} 条）", true);
            if (_showExtraPaths)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                for (int i = _extraPaths.Count - 1; i >= 0; i--)
                {
                    EditorGUILayout.BeginHorizontal();
                    bool exists = CheckPathExists(_extraPaths[i]);
                    var oldColor = GUI.color;
                    GUI.color = exists ? Color.white : new Color(1f, 0.55f, 0.55f);
                    _extraPaths[i] = EditorGUILayout.TextField(_extraPaths[i]);
                    GUI.color = oldColor;
                    GUILayout.Label(exists ? "✓" : "✗", GUILayout.Width(18));
                    if (GUILayout.Button("✕", GUILayout.Width(24)))
                    {
                        _extraPaths.RemoveAt(i);
                        SaveExtraPaths();
                    }
                    EditorGUILayout.EndHorizontal();
                }

                // ── 拖放区：将 Project 中的文件夹/资产拖入即添加 ──
                DrawDropZone();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(6);

            // ── 选项 ──
            bool newDeps = EditorGUILayout.Toggle("包含依赖项 (IncludeDependencies)", _includeDepends);
            if (newDeps != _includeDepends)
            {
                _includeDepends = newDeps;
                EditorPrefs.SetBool(PrefsDeps, newDeps);
            }

            // ── 统计 ──
            int onCount = _itemOn.Count(b => b);
            EditorGUILayout.LabelField($"已选 {onCount}/{_items.Count} 项 + {_extraPaths.Count} 条额外路径", EditorStyles.miniLabel);

            EditorGUILayout.Space(8);

            // ── 导出按钮 ──
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(onCount == 0 && _extraPaths.Count == 0))
            {
                if (GUILayout.Button("🚀  选择输出路径并导出", GUILayout.Height(34)))
                {
                    string defaultDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Export"));
                    string outputPath = EditorUtility.SaveFilePanel(
                        "保存 Unity Package", defaultDir, "CharacterSystem", "unitypackage");
                    if (!string.IsNullOrEmpty(outputPath))
                        ExportToPath(CollectSelectedPaths(), outputPath, _includeDepends);
                }

                if (GUILayout.Button("⚡  静默导出", GUILayout.Height(34)))
                {
                    string outputPath = Path.GetFullPath(
                        Path.Combine(Application.dataPath, "../Export/CharacterSystem.unitypackage"));
                    ExportToPath(CollectSelectedPaths(), outputPath, _includeDepends);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─────────────────────────────────────────────────────────────────
        // 内部逻辑
        // ─────────────────────────────────────────────────────────────────

        void SetAllItems(bool on)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                _itemOn[i] = on;
                EditorPrefs.SetBool(PrefsPrefix + _items[i].Path, on);
            }
        }

        void SaveExtraPaths()
        {
            EditorPrefs.SetString(PrefsExtra, string.Join(";", _extraPaths));
        }

        /// <summary>绘制拖放区：接受从 Project 窗口拖入的文件夹/资产，支持多选。</summary>
        void DrawDropZone()
        {
            var dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            var evt = Event.current;
            bool hovering = dropRect.Contains(evt.mousePosition)
                && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform);

            var prevBg = GUI.backgroundColor;
            if (hovering) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
            GUI.Box(dropRect, hovering
                ? "松开以添加所选资产"
                : "⬇  将文件夹 / 资产拖到此处添加（支持多选）", EditorStyles.helpBox);
            GUI.backgroundColor = prevBg;

            if (!hovering && evt.type != EventType.DragPerform) return;
            if (evt.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                DragAndDrop.AcceptDrag();
                int added = 0;
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    string p = AssetDatabase.GetAssetPath(obj);
                    if (string.IsNullOrEmpty(p) || _extraPaths.Contains(p)) continue;
                    _extraPaths.Add(p);
                    added++;
                }
                if (added > 0) SaveExtraPaths();
                evt.Use();
            }
        }

        List<string> CollectSelectedPaths()
        {
            var paths = new List<string>();
            for (int i = 0; i < _items.Count; i++)
                if (_itemOn[i]) paths.Add(_items[i].Path);
            paths.AddRange(_extraPaths);
            return paths;
        }

        int GetFileCount(string assetPath)
        {
            if (_fileCountCache.TryGetValue(assetPath, out int cached)) return cached;
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "../", assetPath));
            int count;
            if (File.Exists(abs))
                count = 1;
            else if (Directory.Exists(abs))
                count = Directory.GetFiles(abs, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta"));
            else
                count = 0;
            _fileCountCache[assetPath] = count;
            return count;
        }

        static void ExportToPath(IEnumerable<string> paths, string outputPath, bool includeDepends)
        {
            var valid = new List<string>();
            foreach (var p in paths)
            {
                if (CheckPathExists(p))
                    valid.Add(p);
                else
                    Debug.LogWarning($"[ExportPackage] 路径不存在，跳过: {p}");
            }

            // 强制包含包依赖说明文件：无论勾选状态如何，都确保导入方拿到 Package 依赖指引
            // （NGO / AI Navigation 是 Package 依赖，unitypackage 无法包含，必须靠此文件告知）。
            const string depsFile = "Assets/_Game/IMPORT_DEPENDENCIES.txt";
            if (CheckPathExists(depsFile) && !valid.Contains(depsFile))
            {
                valid.Add(depsFile);
                Debug.Log("[ExportPackage] 已强制包含依赖说明: " + depsFile);
            }

            if (valid.Count == 0)
            {
                Debug.LogError("[ExportPackage] 没有有效的导出路径，请检查勾选配置。");
                EditorUtility.DisplayDialog("导出失败", "没有可导出的有效路径。", "OK");
                return;
            }

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var opts = ExportPackageOptions.Recurse;
            if (includeDepends) opts |= ExportPackageOptions.IncludeDependencies;

            Debug.Log($"[ExportPackage] 开始导出 {valid.Count} 个路径 → {outputPath}");
            AssetDatabase.ExportPackage(valid.ToArray(), outputPath, opts);

            // 顺带产出依赖清单+导入指引(联机时代:NGO 包不装=Net 脚本编译失败+prefab 组件 Missing)
            CopyManifestAlongside(outputPath);
            WriteImportReadme(outputPath);

            Debug.Log($"[ExportPackage] ✓ 导出完成：{outputPath}");
            EditorUtility.RevealInFinder(outputPath);
        }

        /// <summary>
        /// 生成过滤版 manifest(2026-07-31 联机适配):剔除 file: 本地路径依赖(如 unity-mcp,
        /// 导入方路径必然无效),输出 manifest.test09.json 供合并,不直接覆盖对方 manifest。
        /// </summary>
        static void CopyManifestAlongside(string outputPath)
        {
            try
            {
                string src = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/manifest.json"));
                string dst = Path.Combine(Path.GetDirectoryName(outputPath), "manifest.test09.json");
                if (!File.Exists(src) || src == dst) return;

                var sb = new System.Text.StringBuilder();
                foreach (var line in File.ReadAllLines(src))
                {
                    if (line.Contains("\"file:")) continue;   // 本地路径依赖(开发工具),剔除
                    sb.AppendLine(line);
                }
                File.WriteAllText(dst, sb.ToString());
                Debug.Log($"[ExportPackage] ✓ manifest.test09.json 已生成(已剔除 file: 本地依赖,供导入方合并)");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ExportPackage] manifest 生成失败（不影响导出）: {e.Message}");
            }
        }

        /// <summary>生成 IMPORT_README.txt 到输出目录:导入顺序/联机依赖/验证步骤。</summary>
        static void WriteImportReadme(string outputPath)
        {
            try
            {
                string dst = Path.Combine(Path.GetDirectoryName(outputPath), "IMPORT_README.txt");
                File.WriteAllText(dst,
@"test09 导出包导入指引
============================================================

0. Unity 版本:2022.3.62f2(其他版本未验证)

1. 【关键!先对齐包依赖】把 manifest.test09.json 的 dependencies 合并到
   你项目的 Packages/manifest.json(不要整文件替换,按条目合并):
     联机必需:com.unity.netcode.gameobjects 1.9.1(自动带 transport)
             com.unity.ai.navigation 1.1.7(NavMesh 敌人/场景烘焙)
   不装包直接导 unitypackage 的后果:
     Net 目录脚本编译失败 + 角色 prefab 上联机组件全部 Missing Script。

2. 导入 .unitypackage(Assets → Import Package → Custom Package…)

3. 场景:默认未导出 TestScene;若勾选导出过,需手动把场景加入
   File → Build Settings(建议顺序:test_scene, pvp_arena)。

4. 联机功能验证:
     双实例冒烟 — Tools/Net/Debug Start Host + Debug Start Client,
     控制台出现 ""[Net] 客户端接入:id=1 在线=2"" 即通;
     一键打包 — Tools/Build/Build Dev Player (Windowed);
     联机 Resources 如需重建 — Tools/Net/Maintenance/Build Player Resources Entry。

5. 说明:com.coplaydev.unity-mcp 为本地开发工具(file 路径依赖),已剔除,
   不影响游戏运行;层/Tag 为默认(Default 层承载碰撞),无需 TagManager 对齐。
");
                Debug.Log($"[ExportPackage] ✓ IMPORT_README.txt 已生成");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ExportPackage] README 生成失败（不影响导出）: {e.Message}");
            }
        }

        static bool CheckPathExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "../", assetPath));
            return Directory.Exists(abs) || File.Exists(abs);
        }
    }
}
