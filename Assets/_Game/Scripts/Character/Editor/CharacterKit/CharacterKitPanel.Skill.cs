#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Game.Character;
using Game.SkillSystem;
using Game.SkillSystem.EditorTools;
using Game.EditorTools.Shared;
using Game.EditorTools.CombatSandbox;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.Character.EditorTools.CharacterKit
{
    public partial class CharacterKitPanel
    {
        void DrawSkillTab()
        {
            EditorGUILayout.LabelField("角色技能库", EditorStyles.boldLabel);

            // ── 角色目录探测 ──────────────────────────────────
            DetectCurrentCharacterFolder();

            EditorGUILayout.Space(8);

            // ── 角色技能库扫描 ────────────────────────────────
            if (string.IsNullOrEmpty(_currentCharFolder))
            {
                EditorGUILayout.HelpBox(
                    "未检测到当前编辑角色的目录。请在 Setup Tab 中拖入 AnimSet 或 Config 资产，" +
                    "系统会自动探测角色目录并显示其技能列表。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField($"角色目录: {_currentCharFolder}",
                    EditorStyles.miniLabel);
                if (GUILayout.Button("刷新", GUILayout.Width(50)))
                {
                    _skillListScanned = false;
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            // ── 搜索过滤 ──────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            try
            {
                _skillSearchFilter = EditorGUILayout.TextField("搜索", _skillSearchFilter,
                    GUILayout.Width(200));
                _skillShowBasicAttacks = EditorGUILayout.Toggle("显示普攻", _skillShowBasicAttacks,
                    GUILayout.Width(100));
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }

            // ── 技能列表 ──────────────────────────────────────
            var skills = GetCurrentCharacterSkills();
            if (skills.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"在 {_currentCharFolder}/SkillData/ 下未找到 SkillData 资产。\n" +
                    "在 Skill Builder Wizard 中创建技能并保存到该目录。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"技能列表 ({skills.Count})", EditorStyles.miniBoldLabel);

            _skillListScroll = EditorGUILayout.BeginScrollView(
                _skillListScroll, GUILayout.MaxHeight(400));
            try
            {
                // 表头
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                try
                {
                    EditorGUILayout.LabelField("图标", GUILayout.Width(36));
                    EditorGUILayout.LabelField("名称", GUILayout.Width(140));
                    EditorGUILayout.LabelField("类型", GUILayout.Width(60));
                    EditorGUILayout.LabelField("ID", GUILayout.Width(40));
                    EditorGUILayout.LabelField("冷却", GUILayout.Width(50));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("操作", GUILayout.Width(160));
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                }

                foreach (var sd in skills)
                {
                    if (sd == null) continue;

                    bool isAttack = sd.category == SkillCategory.BasicAttack;

                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    try
                    {
                        // Icon
                        if (sd.icon != null)
                            GUILayout.Box(sd.icon.texture, GUILayout.Width(32), GUILayout.Height(32));
                        else
                            GUILayout.Box("", GUILayout.Width(32), GUILayout.Height(32));

                        // Name
                        EditorGUILayout.LabelField(sd.skillName, EditorStyles.boldLabel,
                            GUILayout.Width(140));

                        // Category badge
                        GUI.backgroundColor = isAttack
                            ? new Color(0.5f, 0.5f, 0.5f)
                            : new Color(0.3f, 0.6f, 0.3f);
                        GUILayout.Button(isAttack ? "普攻" : "技能", StyleSkillBadge,
                            GUILayout.Width(44), GUILayout.Height(18));
                        GUI.backgroundColor = Color.white;

                        // ID
                        EditorGUILayout.LabelField(sd.skillId.ToString(), EditorStyles.miniLabel,
                            GUILayout.Width(40));

                        // Cooldown
                        EditorGUILayout.LabelField($"{sd.cooldown:F1}s", EditorStyles.miniLabel,
                            GUILayout.Width(50));

                        GUILayout.FlexibleSpace();

                        // 操作按钮
                        if (GUILayout.Button("编辑", GUILayout.Width(40), GUILayout.Height(18)))
                        {
                            // 通过 Project 选中该资产，方便在 Inspector 中编辑
                            Selection.activeObject = sd;
                            EditorGUIUtility.PingObject(sd);
                        }

                        if (GUILayout.Button("SkillWiz", GUILayout.Width(56), GUILayout.Height(18)))
                        {
                            // P1-4: 直接传参定位,替代 ExecuteMenuItem 无参打开
                            SkillBuilderWizard.EditSkill(sd);
                        }

                        GUI.backgroundColor = EditorSkinPalette.Success;
                        if (GUILayout.Button(isAttack ? "绑定普攻" : "绑定技能",
                            GUILayout.Width(56), GUILayout.Height(18)))
                        {
                            BindSkillToCurrentCharacter(sd);
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    finally
                    {
                        SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                    }
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndScrollView());
            }

            // ── 跨 Kit 跳转 ──────────────────────────────────
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("快速跳转", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            try
            {
                CrossKitBridge.DrawKitJumpButton("Level Kit - 在关卡中使用此角色", "LevelKit", 24);
                CrossKitBridge.DrawKitJumpButton("Combat Sandbox - 测试此角色", "CombatSandbox", 24);
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }
        }

        // ── Skill Tab 辅助方法 ────────────────────────────────────

        /// <summary>探测当前角色目录（优先装配步骤拖入的资源路径，其次从模板资产路径推断）。</summary>
        void DetectCurrentCharacterFolder()
        {
            // ① 装配步骤① 拖入的"资源路径"文件夹（最权威来源，随拖拽实时更新）
            // ② 回退：从 AnimSet/Config 模板资产路径推断（Common 共享目录除外）
            string folder = GetActiveSetupFolder()
                         ?? TryInferCharRoot(_charSetupTemplateAnimSet)
                         ?? TryInferCharRoot(_charSetupEditAnimSet)
                         ?? TryInferCharRoot(_playerTemplateConfig)
                         ?? TryInferCharRoot(_playerEditConfig);

            if (folder != null && folder != _currentCharFolder)
            {
                _currentCharFolder = folder;
                _skillListScanned = false; // 目录变化 → 强制重扫
            }
        }

        /// <summary>取当前流水线目标在装配步骤中拖入的资源文件夹路径。</summary>
        string GetActiveSetupFolder()
        {
            // 优先当前流水线目标（Enemy / Character）的文件夹，回退到另一个
            if (_setupTarget == SetupPipeline.PipelineTarget.Enemy)
                return GetFolderPath(_enemySetupFolder, _enemySetupFolderPath)
                    ?? GetFolderPath(_charSetupFolder, _charSetupFolderPath);
            return GetFolderPath(_charSetupFolder, _charSetupFolderPath)
                ?? GetFolderPath(_enemySetupFolder, _enemySetupFolderPath);
        }

        static string GetFolderPath(DefaultAsset folderAsset, string cachedPath)
        {
            if (folderAsset != null)
            {
                string p = AssetDatabase.GetAssetPath(folderAsset);
                if (!string.IsNullOrEmpty(p)) return p.Replace("\\", "/");
            }
            return string.IsNullOrEmpty(cachedPath) ? null : cachedPath;
        }

        /// <summary>从资产路径推断角色根目录（character/ 下第一级子目录；Common 返回 null）。</summary>
        static string TryInferCharRoot(Object asset)
        {
            if (asset == null) return null;
            string norm = AssetDatabase.GetAssetPath(asset)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(norm)) return null;
            int idx = norm.IndexOf("/character/", System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            string rootName = norm.Substring(idx + "/character/".Length).Split('/')[0];
            if (string.IsNullOrEmpty(rootName) || rootName == "Common") return null;
            return $"Assets/_Game/character/{rootName}";
        }

        /// <summary>获取当前角色目录下的所有技能。</summary>
        List<SkillData> GetCurrentCharacterSkills()
        {
            if (_skillListScanned && _charSkillList != null)
                return FilterSkills(_charSkillList);

            _charSkillList = new List<SkillData>();
            if (string.IsNullOrEmpty(_currentCharFolder)) return _charSkillList;

            string skillFolder = $"{_currentCharFolder}/SkillData";
            if (AssetDatabase.IsValidFolder(skillFolder))
            {
                var guids = AssetDatabase.FindAssets("t:SkillData", new[] { skillFolder });
                foreach (var g in guids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    var sd = AssetDatabase.LoadAssetAtPath<SkillData>(p);
                    if (sd != null) _charSkillList.Add(sd);
                }
                _charSkillList.Sort((a, b) =>
                {
                    // 技能优先，普攻其次
                    int catCmp = a.category.CompareTo(b.category);
                    if (catCmp != 0) return catCmp;
                    return string.Compare(a.skillName, b.skillName,
                        System.StringComparison.OrdinalIgnoreCase);
                });
            }

            _skillListScanned = true;
            return FilterSkills(_charSkillList);
        }

        List<SkillData> FilterSkills(List<SkillData> source)
        {
            if (source == null) return new List<SkillData>();
            var filtered = new List<SkillData>();
            foreach (var sd in source)
            {
                if (sd == null) continue;
                if (!_skillShowBasicAttacks && sd.category == SkillCategory.BasicAttack)
                    continue;
                if (!string.IsNullOrEmpty(_skillSearchFilter))
                {
                    if (!sd.skillName.ToLower().Contains(_skillSearchFilter.ToLower())
                        && sd.skillId.ToString() != _skillSearchFilter)
                        continue;
                }
                filtered.Add(sd);
            }
            return filtered;
        }

        /// <summary>
        /// 将技能绑定到当前角色 Prefab 的 SkillController 空槽中。
        /// 找到角色目录下第一个有 SkillController 的 Prefab 执行绑定。
        /// </summary>
        void BindSkillToCurrentCharacter(SkillData sd)
        {
            if (sd == null) return;
            if (string.IsNullOrEmpty(_currentCharFolder))
            {
                Debug.LogWarning("[CharacterKit] 无法确定角色目录。");
                return;
            }

            // 查找角色 Prefab
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { _currentCharFolder });
            GameObject prefab = null;
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null && go.GetComponentInChildren<Animator>(true) != null
                    && go.GetComponent<SkillController>() != null)
                {
                    prefab = go;
                    break;
                }
            }

            if (prefab == null)
            {
                Debug.LogWarning($"[CharacterKit] 在 {_currentCharFolder} 下未找到带 SkillController 的 Prefab。");
                return;
            }

            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var sc = instance.GetComponent<SkillController>();

            if (sc == null)
            {
                Object.DestroyImmediate(instance);
                Debug.LogWarning("[CharacterKit] Prefab 上没有 SkillController 组件。");
                return;
            }

            bool isAttack = sd.category == SkillCategory.BasicAttack;
            var slots = isAttack ? sc.attackSlots : sc.skillSlots;
            if (slots == null)
                slots = new SkillData[8];

            // 检查是否已存在
            int emptyIdx = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == sd)
                {
                    Debug.Log($"[CharacterKit] 「{sd.skillName}」已绑定，跳过。");
                    Object.DestroyImmediate(instance);
                    return;
                }
                if (slots[i] == null && emptyIdx < 0)
                    emptyIdx = i;
            }

            if (emptyIdx < 0)
            {
                Debug.LogWarning("[CharacterKit] 所有槽位已满。");
                Object.DestroyImmediate(instance);
                return;
            }

            slots[emptyIdx] = sd;
            if (isAttack) sc.attackSlots = slots; else sc.skillSlots = slots;

            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);

            Debug.Log($"[CharacterKit] ✅ 「{sd.skillName}」已绑定到槽位 {emptyIdx + 1}。");
        }
    }
}
#endif