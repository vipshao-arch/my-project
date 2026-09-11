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
        // ═══════════════════════════════════════════════════════════════
        // P4.2：GUIContent 静态缓存，避免 OnGUI 每帧 new GUIContent 产生 GC
        // ═══════════════════════════════════════════════════════════════
        static readonly GUIContent GCLSB_Stiffness     = new GUIContent("刚性 Stiffness", "骨骼回弹力度，值越大越僵硬");
        static readonly GUIContent GCLSB_Damping       = new GUIContent("阻尼 Damping", "运动衰减速度，值越大摆动越快停止");
        static readonly GUIContent GCLSB_Gravity       = new GUIContent("重力方向力", "每帧施加的额外加速度（通常 Y 轴负值模拟下垂）");
        static readonly GUIContent GCLSB_Radius        = new GUIContent("碰撞半径", "每节骨骼的球形碰撞体半径");
        static readonly GUIContent GCLSB_RootBone      = new GUIContent("根骨骼", "摆动链的起始骨骼节点");
        static readonly GUIContent GCLSB_ChainLength   = new GUIContent("链长（0=自动）", "骨骼链长度，0 表示自动追踪到末端骨骼");

        /// <summary>绘制 SpringBone Setup 独立面板：摆动骨骼链 + 球形碰撞体 + 全局物理参数。</summary>
        void DrawSpringBoneSetupPanel(SetupPipeline.StepState state)
        {
            EditorGUILayout.LabelField("④ SpringBone 动态骨骼", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "为角色 Prefab 设置 SpringBone（动态骨骼/飘带）组件，\n" +
                "用于头发、裙摆等柔性部件的物理模拟。",
                MessageType.Info);
            EditorGUILayout.Space(6);

            _springBonePanelScroll = EditorGUILayout.BeginScrollView(_springBonePanelScroll, GUILayout.ExpandHeight(true));
            try
            {

                // ── 依赖检查：SpringBoneComponent 是否存在 ─────────────────
                if (!HasSpringBoneRuntimeType())
                {
                    EditorGUILayout.HelpBox(
                        "⚠ 找不到 SpringBoneComponent 运行时脚本（Game.Character.SpringBoneComponent）。\n\n" +
                        "此工具需要项目中存在该脚本才能正常工作。\n\n" +
                        "解决方案：\n" +
                        "  1. 在 Assets/_Game/Scripts/Character/ 下新建 SpringBoneComponent.cs\n" +
                        "     （参考模板：定义 SpringChain / SphereColliderDef 子类，\n" +
                        "      公开 springChains 列表 + Reinitialize() 方法）\n" +
                        "  2. 或从其他项目导入已有 SpringBone 实现",
                        MessageType.Error);
                    EditorGUILayout.Space(4);
                    if (GUILayout.Button("📄 一键创建 SpringBoneComponent 模板脚本", GUILayout.Height(30)))
                    {
                        CreateSpringBoneTemplateScript();
                    }
                    return;
                }

                EditorGUILayout.Space(4);

                // ── 目标角色 ────────────────────────────────────────────────
                EditorGUILayout.BeginHorizontal();
                try
                {
                    EditorGUILayout.LabelField("目标角色", GUILayout.Width(70));
                    var newTarget = (GameObject)EditorGUILayout.ObjectField(
                        _springBoneTarget, typeof(GameObject), true);
                    if (newTarget != _springBoneTarget)
                    {
                        _springBoneTarget = newTarget;
                        OnSharedTargetManuallyChanged(newTarget, ref _springTargetAuto);
                        if (_springBoneTarget != null) SpringBoneReadFromComponent();
                        else SpringBoneClearAll();
                    }
                    if (GUILayout.Button("→", GUILayout.Width(28)))
                    {
                        _springBoneTarget = Selection.activeGameObject;
                        OnSharedTargetManuallyChanged(Selection.activeGameObject, ref _springTargetAuto);
                        if (_springBoneTarget != null) SpringBoneReadFromComponent();
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                }

                // ── CK-02：自动来源指示器 ──
                if (_springBoneTarget != null && _springTargetAuto)
                {
                    EditorGUILayout.LabelField("  ⟳ 自动绑定（来自流水线前置步骤）", EditorStyles.miniLabel);
                }

                if (_springBoneTarget == null)
                {
                    EditorGUILayout.LabelField("  └─ 拖入 Hierarchy 中的角色 GameObject", EditorStyles.miniLabel);
                    return;
                }

                EditorGUILayout.Space(6);

                // ── 摆动骨骼链 Section ────────────────────────────────────
                _springBoneShowChains = EditorGUILayout.BeginFoldoutHeaderGroup(
                    _springBoneShowChains, $"摆动骨骼链  ({_springBoneChains.Count})");
                try
                {
                    if (_springBoneShowChains)
                    {
                        for (int i = _springBoneChains.Count - 1; i >= 0; i--)
                            DrawSpringBoneChainEntry(i);

                        EditorGUILayout.Space(4);
                        DrawSpringBoneAddChainRow();
                        EditorGUILayout.Space(2);
                        DrawSpringBoneChainButtons();
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndFoldoutHeaderGroup());
                }

                EditorGUILayout.Space(6);

                // ── 球形碰撞体 Section ────────────────────────────────────
                _springBoneShowColliders = EditorGUILayout.BeginFoldoutHeaderGroup(
                    _springBoneShowColliders, $"球形碰撞体  ({_springBoneColliders.Count})");
                try
                {
                    if (_springBoneShowColliders)
                    {
                        for (int i = _springBoneColliders.Count - 1; i >= 0; i--)
                        {
                            var c = _springBoneColliders[i];
                            EditorGUILayout.BeginHorizontal();
                            try
                            {
                                c.bone = (Transform)EditorGUILayout.ObjectField(
                                    c.bone, typeof(Transform), true, GUILayout.MinWidth(120));
                                c.radius = EditorGUILayout.FloatField(c.radius, GUILayout.Width(52));
                                EditorGUILayout.LabelField("r", GUILayout.Width(10));
                                if (GUILayout.Button("✕", GUILayout.Width(24)))
                                    _springBoneColliders.RemoveAt(i);
                            }
                            finally
                            {
                                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                            }
                        }

                        EditorGUILayout.Space(3);
                        EditorGUILayout.BeginHorizontal();
                        try
                        {
                            _springBonePendingCollider = (Transform)EditorGUILayout.ObjectField(
                                "添加碰撞体骨骼", _springBonePendingCollider, typeof(Transform), true);
                            bool alreadyInCollider = _springBonePendingCollider != null &&
                                _springBoneColliders.Any(c => c.bone == _springBonePendingCollider);
                            GUI.enabled = _springBonePendingCollider != null && !alreadyInCollider;
                            if (GUILayout.Button("+ 添加", GUILayout.Width(60)))
                            {
                                _springBoneColliders.Add(new SpringBoneColliderState
                                {
                                    bone = _springBonePendingCollider,
                                    radius = 0.08f
                                });
                                _springBonePendingCollider = null;
                            }
                            GUI.enabled = true;
                        }
                        finally
                        {
                            SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                        }
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndFoldoutHeaderGroup());
                }

                EditorGUILayout.Space(6);

                // ── 全局参数 Section ──────────────────────────────────────
                _springBoneShowParams = EditorGUILayout.BeginFoldoutHeaderGroup(
                    _springBoneShowParams, "全局物理参数（统一应用用）");
                try
                {
                    if (_springBoneShowParams)
                    {
                        EditorGUI.indentLevel++;
                        _springBoneGlobalStiffness = EditorGUILayout.Slider(
                            GCLSB_Stiffness, _springBoneGlobalStiffness, 0f, 1f);
                        _springBoneGlobalDamping = EditorGUILayout.Slider(
                            GCLSB_Damping, _springBoneGlobalDamping, 0f, 1f);
                        _springBoneGlobalGravity = EditorGUILayout.Vector3Field(
                            GCLSB_Gravity, _springBoneGlobalGravity);
                        _springBoneGlobalRadius = EditorGUILayout.Slider(
                            GCLSB_Radius, _springBoneGlobalRadius, 0.001f, 0.2f);
                        EditorGUI.indentLevel--;
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndFoldoutHeaderGroup());
                }

                EditorGUILayout.Space(10);

                // ── 操作按钮 ──────────────────────────────────────────────
                EditorGUILayout.BeginHorizontal();
                try
                {
                    GUI.enabled = _springBoneChains.Count > 0;
                    if (GUILayout.Button("✅  应用配置", StyleApplyButton, GUILayout.Height(30)))
                    {
                        ExecuteSpringBoneApply(state);
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("↺ 重新读取", GUILayout.Height(30), GUILayout.Width(100)))
                    {
                        SpringBoneReadFromComponent();
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndScrollView());
            }

            // ── 日志 ──────────────────────────────────────────────────
            if (_springBoneLogMessages.Count > 0)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("执行日志", EditorStyles.boldLabel);
                float h = Mathf.Min(_springBoneLogMessages.Count * 20 + 20, 100);
                _springBoneLogScroll = EditorGUILayout.BeginScrollView(_springBoneLogScroll, GUILayout.Height(h));
                try
                {
                    foreach (var msg in _springBoneLogMessages)
                    {
                        if (msg.StartsWith("❌"))
                            EditorGUILayout.HelpBox(msg, MessageType.Error);
                        else if (msg.StartsWith("⚠"))
                            EditorGUILayout.HelpBox(msg, MessageType.Warning);
                        else
                            EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndScrollView());
                }
            }
        }

        // ── SpringBone 关键词（自动扫描用） ─────────────────────────
        private static readonly string[] _springBoneKeywords = new string[]
        {
            "hair","Hair","kami","bangs","Bangs","ponytail","Ponytail",
            "braid","Braid","ahoge","sideburn","fringe",
            "cloth","Cloth","skirt","Skirt","coat","Coat","cape","Cape",
            "cloak","ribbon","Ribbon","scarf","Scarf","tassel","Tassel",
            "dynamic","Dynamic","swing","Swing","spring","Spring",
            "phys","Phys","tail","Tail","ear","Ear","wing","Wing",
            "chain","Chain","pendant","acc_","_dyn",
        };

        /// <summary>反射获取 SpringBoneComponent 运行时类型（缓存）。</summary>
        static System.Type _springBoneTypeCache;
        static System.Type GetSpringBoneComponentType()
        {
            if (_springBoneTypeCache != null) return _springBoneTypeCache;
            _springBoneTypeCache =
                System.Type.GetType("Game.Character.SpringBoneComponent, Assembly-CSharp") ??
                System.Type.GetType("Game.Character.SpringBoneComponent, Assembly-CSharp-firstpass");
            return _springBoneTypeCache;
        }
        /// <summary>检查项目中是否存在 SpringBoneComponent 运行时脚本。</summary>
        static bool HasSpringBoneRuntimeType() => GetSpringBoneComponentType() != null;

        /// <summary>绘制单条摆动骨骼链的折叠编辑面板。</summary>
        void DrawSpringBoneChainEntry(int i)
        {
            var e = _springBoneChains[i];
            string tail = SpringBoneGetChainTailName(e.root);
            int depth = SpringBoneCountDepth(e.root);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            try
            {
                EditorGUILayout.BeginHorizontal();
                try
                {
                    e.foldout = EditorGUILayout.Foldout(e.foldout,
                        $"{(e.root != null ? e.root.name : "(空)")}  →  {tail}  [{depth} 节]", true);
                    if (GUILayout.Button("✕", GUILayout.Width(24)))
                    {
                        _springBoneChains.RemoveAt(i);
                        return;
                    }
                }
                finally
                {
                    SafeEndLayout(() => EditorGUILayout.EndHorizontal());
                }

                if (e.foldout)
                {
                    EditorGUI.indentLevel++;
                    e.root = (Transform)EditorGUILayout.ObjectField(
                        GCLSB_RootBone, e.root, typeof(Transform), true);
                    e.chainLength = EditorGUILayout.IntField(
                        GCLSB_ChainLength, e.chainLength);
                    e.stiffness = EditorGUILayout.Slider(
                        GCLSB_Stiffness, e.stiffness, 0f, 1f);
                    e.damping = EditorGUILayout.Slider(
                        GCLSB_Damping, e.damping, 0f, 1f);
                    e.gravity = EditorGUILayout.Vector3Field(
                        GCLSB_Gravity, e.gravity);
                    e.radius = EditorGUILayout.Slider(
                        GCLSB_Radius, e.radius, 0.001f, 0.2f);
                    EditorGUI.indentLevel--;
                }
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndVertical());
            }
        }

        /// <summary>绘制添加摆动骨骼链的行（选择根骨骼 + 添加按钮）。</summary>
        void DrawSpringBoneAddChainRow()
        {
            EditorGUILayout.BeginHorizontal();
            try
            {
                _springBonePendingRoot = (Transform)EditorGUILayout.ObjectField(
                    "添加根骨骼", _springBonePendingRoot, typeof(Transform), true);
                bool alreadyIn = _springBonePendingRoot != null &&
                    _springBoneChains.Any(c => c.root == _springBonePendingRoot);
                GUI.enabled = _springBonePendingRoot != null && !alreadyIn;
                if (GUILayout.Button("+ 添加", GUILayout.Width(60)))
                {
                    _springBoneChains.Add(SpringBoneMakeChainEntry(_springBonePendingRoot));
                    _springBonePendingRoot = null;
                }
                GUI.enabled = true;
                if (alreadyIn)
                    EditorGUILayout.HelpBox("该骨骼已在列表中。", MessageType.Warning);
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }
        }

        /// <summary>绘制 SpringBone 骨骼链批量操作按钮（自动扫描/统一参数/清空）。</summary>
        void DrawSpringBoneChainButtons()
        {
            EditorGUILayout.BeginHorizontal();
            try
            {
                if (GUILayout.Button("自动扫描添加", GUILayout.Height(22)))
                    SpringBoneAutoScan();
                if (GUILayout.Button("统一参数到全局", GUILayout.Height(22)))
                    SpringBoneApplyGlobalParams();
                GUI.enabled = _springBoneChains.Count > 0;
                if (GUILayout.Button("清空", GUILayout.Width(44), GUILayout.Height(22)))
                    _springBoneChains.Clear();
                GUI.enabled = true;
            }
            finally
            {
                SafeEndLayout(() => EditorGUILayout.EndHorizontal());
            }
        }

        /// <summary>用当前全局参数创建一条新的骨骼链条目。</summary>
        SpringBoneChainState SpringBoneMakeChainEntry(Transform root) => new SpringBoneChainState
        {
            root = root,
            stiffness = _springBoneGlobalStiffness,
            damping = _springBoneGlobalDamping,
            gravity = _springBoneGlobalGravity,
            radius = _springBoneGlobalRadius,
        };

        /// <summary>清空所有摆动骨骼链和碰撞体数据。</summary>
        void SpringBoneClearAll()
        {
            _springBoneChains.Clear();
            _springBoneColliders.Clear();
        }

        /// <summary>从目标角色上的 SpringBoneComponent 反射读取骨骼链和碰撞体配置。</summary>
        void SpringBoneReadFromComponent()
        {
            _springBoneChains.Clear();
            _springBoneColliders.Clear();
            _springBoneLogMessages.Clear();

            if (_springBoneTarget == null) return;

            var compType = GetSpringBoneComponentType();
            if (compType == null) return;

            var comp = _springBoneTarget.GetComponentInChildren(compType, true) as Component;
            if (comp == null) return;

            var chainsField = compType.GetField("springChains",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (chainsField == null) return;

            var chainList = chainsField.GetValue(comp) as System.Collections.IList;
            if (chainList == null) return;

            bool first = true;
            foreach (var sc in chainList)
            {
                if (sc == null) continue;
                var scType = sc.GetType();

                T Get<T>(string name)
                {
                    var f = scType.GetField(name,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    return f != null ? (T)f.GetValue(sc) : default;
                }

                var entry = new SpringBoneChainState
                {
                    root = Get<Transform>("root"),
                    chainLength = Get<int>("chainLength"),
                    stiffness = Get<float>("stiffness"),
                    damping = Get<float>("damping"),
                    gravity = Get<Vector3>("gravity"),
                    radius = Get<float>("radius"),
                };
                _springBoneChains.Add(entry);

                if (first)
                {
                    _springBoneGlobalStiffness = entry.stiffness;
                    _springBoneGlobalDamping = entry.damping;
                    _springBoneGlobalGravity = entry.gravity;
                    _springBoneGlobalRadius = entry.radius;
                    first = false;
                }
            }

            _springBoneLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] 已从组件读取 {_springBoneChains.Count} 条链");
        }

        /// <summary>自动扫描目标角色中匹配关键词的骨骼节点，添加到摆动链列表。</summary>
        void SpringBoneAutoScan()
        {
            if (_springBoneTarget == null) return;
            int added = 0;
            foreach (var t in _springBoneTarget.GetComponentsInChildren<Transform>(true))
            {
                if (t.childCount == 0) continue;
                if (_springBoneChains.Any(c => c.root == t)) continue;
                if (!_springBoneKeywords.Any(kw => t.name.Contains(kw))) continue;
                if (_springBoneChains.Any(c => c.root != null && t.IsChildOf(c.root))) continue;

                _springBoneChains.Add(SpringBoneMakeChainEntry(t));
                added++;
            }
            if (added == 0)
            {
                _springBoneLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ⚠ 未发现新的摆动骨骼（无关键词匹配）");
            }
            else
            {
                _springBoneLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] 自动扫描添加 {added} 条链");
            }
        }

        /// <summary>将全局物理参数统一应用到所有骨骼链。</summary>
        void SpringBoneApplyGlobalParams()
        {
            foreach (var e in _springBoneChains)
            {
                e.stiffness = _springBoneGlobalStiffness;
                e.damping = _springBoneGlobalDamping;
                e.gravity = _springBoneGlobalGravity;
                e.radius = _springBoneGlobalRadius;
            }
            _springBoneLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] 已将全局参数应用到 {_springBoneChains.Count} 条链");
        }

        /// <summary>执行 SpringBone 应用：反射创建 SpringChain 列表并写入 SpringBoneComponent。</summary>
        void ExecuteSpringBoneApply(SetupPipeline.StepState state)
        {
            _springBoneLogMessages.Clear();
            if (_springBoneTarget == null || _springBoneChains.Count == 0) return;

            state.status = SetupPipeline.StepStatus.Running;
            state.progress = 0.1f;
            SetupPipeline.ShowStepProgress("SpringBone 动态骨骼", 0.1f, "创建 SpringBoneComponent...");

            var compType = GetSpringBoneComponentType();
            if (compType == null)
            {
                _springBoneLogMessages.Add("❌ 找不到 SpringBoneComponent 运行时脚本，无法应用");
                state.status = SetupPipeline.StepStatus.Failed;
                state.message = "缺少 SpringBoneComponent 脚本";
                state.progress = 1f;
                SetupPipeline.ClearStepProgress();
                RepaintCharacterKitWindow();
                return;
            }

            try
            {
                SetupPipeline.ShowStepProgress("SpringBone 动态骨骼", 0.3f, "添加/查找 SpringBoneComponent...");
                // 找到或添加组件
                var comp = _springBoneTarget.GetComponentInChildren(compType, true) as Component;
                if (comp == null)
                {
                    Undo.RegisterFullObjectHierarchyUndo(_springBoneTarget, "Add SpringBoneComponent");
                    comp = Undo.AddComponent(_springBoneTarget, compType) as Component;
                }
                else
                {
                    Undo.RegisterFullObjectHierarchyUndo(_springBoneTarget, "SpringBone Setup");
                }

                var chainsField = compType.GetField("springChains",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (chainsField == null)
                {
                    _springBoneLogMessages.Add("❌ SpringBoneComponent 缺少 public List<SpringChain> springChains 字段");
                    state.status = SetupPipeline.StepStatus.Failed;
                    state.message = "SpringBoneComponent 字段缺失";
                    SetupPipeline.ClearStepProgress();
                    return;
                }

                var chainType = compType.GetNestedType("SpringChain");
                if (chainType == null)
                {
                    _springBoneLogMessages.Add("❌ SpringBoneComponent 缺少嵌套类 SpringChain");
                    state.status = SetupPipeline.StepStatus.Failed;
                    state.message = "SpringChain 类缺失";
                    SetupPipeline.ClearStepProgress();
                    return;
                }

                SetupPipeline.ShowStepProgress("SpringBone 动态骨骼", 0.5f,
                    $"配置 {_springBoneChains.Count} 条骨骼链...");

                var listType = typeof(List<>).MakeGenericType(chainType);
                var chainList = System.Activator.CreateInstance(listType) as System.Collections.IList;

                for (int i = 0; i < _springBoneChains.Count; i++)
                {
                    var e = _springBoneChains[i];
                    if (e.root == null) continue;
                    var sc = System.Activator.CreateInstance(chainType);
                    void Set(string name, object val)
                    {
                        var f = chainType.GetField(name,
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        f?.SetValue(sc, val);
                    }
                    Set("root", e.root);
                    Set("chainLength", e.chainLength);
                    Set("stiffness", e.stiffness);
                    Set("damping", e.damping);
                    Set("gravity", e.gravity);
                    Set("radius", e.radius);
                    chainList.Add(sc);

                    if (i % 5 == 0)
                        SetupPipeline.ShowStepProgress("SpringBone 动态骨骼",
                            0.5f + 0.4f * ((float)(i + 1) / _springBoneChains.Count),
                            $"配置链 {i + 1}/{_springBoneChains.Count}...");
                }

                chainsField.SetValue(comp, chainList);

                SetupPipeline.ShowStepProgress("SpringBone 动态骨骼", 0.95f, "Reinitialize...");
                // 调用 Reinitialize
                var reinit = compType.GetMethod("Reinitialize",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                reinit?.Invoke(comp, null);

                EditorUtility.SetDirty(comp);

                state.status = SetupPipeline.StepStatus.Completed;
                state.message = "SpringBone 已应用";
                _springBoneLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ✓ SpringBoneComponent 配置已应用：{_springBoneChains.Count} 条链");
            }
            catch (System.Exception ex)
            {
                state.status = SetupPipeline.StepStatus.Failed;
                state.message = $"应用失败: {ex.Message}";
                _springBoneLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ❌ 应用失败: {ex.Message}");
            }
            state.progress = 1f;
            SetupPipeline.ClearStepProgress();
            RepaintCharacterKitWindow();
        }

        /// <summary>一键创建 SpringBoneComponent 模板脚本到项目中。</summary>
        void CreateSpringBoneTemplateScript()
        {
            const string outPath = "Assets/_Game/Scripts/Character/SpringBoneComponent.cs";
            const string content = @"using UnityEngine;
using System.Collections.Generic;

namespace Game.Character
{
    [AddComponentMenu(""Game/Character System/Spring Bone Component"")]
    public class SpringBoneComponent : MonoBehaviour
    {
        [System.Serializable]
        public class SphereColliderDef
        {
            public Transform bone;
            public float     radius = 0.08f;
        }

        [System.Serializable]
        public class SpringChain
        {
            public Transform              root;
            [Tooltip(""0 = 自动追踪到末端骨骼"")]
            public int                    chainLength  = 0;
            [Range(0f, 1f)] public float  stiffness    = 0.3f;
            [Range(0f, 1f)] public float  damping      = 0.15f;
            public Vector3                gravity      = new Vector3(0f, -0.003f, 0f);
            public float                  radius       = 0.03f;
            public List<SphereColliderDef> sphereColliders = new List<SphereColliderDef>();
            [System.NonSerialized] public Transform[] bones;
            [System.NonSerialized] public Vector3[]   prevPositions;
            [System.NonSerialized] public Vector3[]   currentPositions;
        }

        public List<SpringChain> springChains = new List<SpringChain>();

        public void Reinitialize()
        {
            foreach (var chain in springChains)
            {
                if (chain.root == null) continue;
                var boneList = new List<Transform>();
                CollectBones(chain.root, chain.chainLength, boneList);
                chain.bones           = boneList.ToArray();
                chain.prevPositions   = new Vector3[chain.bones.Length];
                chain.currentPositions = new Vector3[chain.bones.Length];
                for (int i = 0; i < chain.bones.Length; i++)
                    chain.prevPositions[i] = chain.currentPositions[i] = chain.bones[i].position;
            }
        }

        private void Start() => Reinitialize();

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            foreach (var chain in springChains)
                UpdateChain(chain, dt);
        }

        private static void CollectBones(Transform t, int limit, List<Transform> list)
        {
            list.Add(t);
            if (limit > 0 && list.Count >= limit) return;
            if (t.childCount > 0) CollectBones(t.GetChild(0), limit, list);
        }

        private static void UpdateChain(SpringChain chain, float dt)
        {
            if (chain.bones == null || chain.bones.Length < 2) return;
            float stiff = Mathf.Clamp01(chain.stiffness);
            float damp  = Mathf.Clamp01(chain.damping);
            for (int i = 1; i < chain.bones.Length; i++)
            {
                var bone = chain.bones[i];
                if (bone == null) continue;
                Vector3 restPos = chain.bones[i - 1].TransformPoint(
                    chain.bones[i - 1].InverseTransformPoint(bone.position));
                Vector3 velocity = (chain.currentPositions[i] - chain.prevPositions[i]) * (1f - damp);
                Vector3 newPos   = chain.currentPositions[i] + velocity + chain.gravity * dt;
                newPos = Vector3.Lerp(newPos, restPos, stiff);
                float len = (bone.position - chain.bones[i - 1].position).magnitude;
                if (len > 0f)
                    newPos = chain.currentPositions[i - 1] + (newPos - chain.currentPositions[i - 1]).normalized * len;
                chain.prevPositions[i]    = chain.currentPositions[i];
                chain.currentPositions[i] = newPos;
                bone.position             = newPos;
            }
        }
    }
}
";
            var dir = System.IO.Path.GetDirectoryName(
                System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../", outPath)));
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(
                System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../", outPath)),
                content, System.Text.Encoding.UTF8);

            AssetDatabase.Refresh();
            _springBoneLogMessages.Add($"[{System.DateTime.Now:HH:mm:ss}] ✓ 已创建模板：{outPath}");
            _springBoneTypeCache = null; // 重新查询
            Debug.Log($"[SpringBoneSetup] 已创建模板：{outPath}");
            EditorUtility.DisplayDialog("完成",
                $"已创建 SpringBoneComponent 模板脚本：\n{outPath}\n\n等 Unity 编译完成后重新点击目标即可。",
                "OK");
        }

        /// <summary>计算从根骨骼沿第一个子节点到末端的深度。</summary>
        static int SpringBoneCountDepth(Transform root)
        {
            if (root == null) return 0;
            int d = 0;
            Transform cur = root;
            while (cur.childCount > 0) { cur = cur.GetChild(0); d++; }
            return d;
        }

        /// <summary>获取骨骼链末端节点的名称（沿第一个子节点遍历到叶子）。</summary>
        static string SpringBoneGetChainTailName(Transform root)
        {
            if (root == null) return "-";
            Transform cur = root;
            while (cur.childCount > 0) cur = cur.GetChild(0);
            return cur.name;
        }

    }
}
#endif