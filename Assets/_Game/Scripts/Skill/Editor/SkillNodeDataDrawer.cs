#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Game.SkillSystem
{
    /// <summary>
    /// SkillNodeData 自定义 PropertyDrawer。
    /// 
    /// 解决 [SerializeReference] 列表在 Inspector 中显示为黑盒(只看到类名)的问题。
    /// 按子类类型分组渲染关键字段,用折叠条 + 色带区分 Category。
    ///
    /// 注册:本类的 [CustomPropertyDrawer] 特性会让 Unity 自动用它渲染所有 SkillNodeData 子类。
    /// </summary>
    [CustomPropertyDrawer(typeof(SkillNodeData), true)]
    public class SkillNodeDataDrawer : PropertyDrawer
    {
        // ── Category → 色带颜色映射(与 Timeline 轨道色对齐) ──────────
        private static readonly Dictionary<SkillNodeCategory, Color> s_categoryColors = new Dictionary<SkillNodeCategory, Color>
        {
            { SkillNodeCategory.VFX,        new Color(0.3f, 0.85f, 0.3f)  },  // 绿
            { SkillNodeCategory.Melee,       new Color(0.85f, 0.3f, 0.3f) },  // 红
            { SkillNodeCategory.Shot,        new Color(0.3f, 0.6f, 0.85f) },  // 蓝
            { SkillNodeCategory.Spawn,       new Color(0.85f, 0.6f, 0.3f) },  // 橙
            { SkillNodeCategory.Channeled,   new Color(0.85f, 0.85f, 0.3f) }, // 黄
            { SkillNodeCategory.Movement,    new Color(0.6f, 0.3f, 0.85f) },  // 紫
            { SkillNodeCategory.Buff,        new Color(0.3f, 0.85f, 0.85f) }, // 青
            { SkillNodeCategory.AnimClip,    new Color(0.6f, 0.85f, 0.3f) },  // 黄绿
            { SkillNodeCategory.MultiStage,  new Color(0.85f, 0.3f, 0.85f) }, // 品红
        };

        // ── 缓存反射字段,避免每帧 GetFields ─────────────────────
        private static readonly Dictionary<Type, List<FieldInfo>> s_fieldCache = new Dictionary<Type, List<FieldInfo>>();

        // ── 折叠状态:用 foldoutCache Key = property.propertyPath ──
        private static readonly Dictionary<string, bool> s_foldoutCache = new Dictionary<string, bool>();

        private const int MAX_CACHE_ENTRIES = 500;

        /// <summary>限制静态缓存增长,防止长 Editor 会话内存膨胀。</summary>
        private static void LimitCache<TKey, TValue>(Dictionary<TKey, TValue> dict)
        {
            if (dict.Count > MAX_CACHE_ENTRIES)
                dict.Clear();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // 只处理 SkillNodeData 子类
            object target = GetManagedTarget(property);
            if (target == null) return EditorGUIUtility.singleLineHeight;

            var fields = GetVisibleFields(target.GetType());
            bool expanded = GetFoldout(property.propertyPath);

            // 折叠时只显示标题行;展开时每字段一行
            int lineCount = expanded ? 1 + fields.Count : 1;
            return EditorGUIUtility.singleLineHeight * lineCount + (expanded ? 4f : 0f);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            object target = GetManagedTarget(property);
            if (target == null)
            {
                EditorGUI.LabelField(position, label, new GUIContent("(null)"));
                return;
            }

            var targetType = target.GetType();
            var fields = GetVisibleFields(targetType);
            bool expanded = GetFoldout(property.propertyPath);

            // ── 折叠条:类别色带 + 类型名 ──
            var foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // 类别色带(左侧 4px)
            if (target is SkillNodeData snd)
            {
                Color catCol = GetCategoryColor(snd.Category);
                EditorGUI.DrawRect(new Rect(foldoutRect.x, foldoutRect.y + 2f, 4f, foldoutRect.height - 4f), catCol);
            }

            // foldout 带 DisplayName + 类型简称
            string title = target is SkillNodeData snd2 ? snd2.DisplayName : targetType.Name;
            string shortType = targetType.Name.Replace("Data", "");
            var foldoutContent = new GUIContent($"{title}  [{shortType}]");
            bool newExpanded = EditorGUI.Foldout(
                new Rect(foldoutRect.x + 6f, foldoutRect.y, foldoutRect.width - 6f, foldoutRect.height),
                expanded, foldoutContent, true);

            if (newExpanded != expanded)
                SetFoldout(property.propertyPath, newExpanded);

            if (!expanded) return;

            // ── 展开:逐字段渲染 ──
            float y = position.y + EditorGUIUtility.singleLineHeight + 2f;
            float indent = 20f;
            float fieldWidth = position.width - indent - 10f;

            // 用 SerializedProperty 子属性绘制(保证 Undo / Prefab override 正常工作)
            var childProp = property.Copy();
            int depth = property.depth;
            bool hasChild = childProp.NextVisible(true) && childProp.depth > depth;

            if (hasChild)
            {
                // 路径 A:用 serialized 子属性(可改的 GameObject/AudioClip 等 Unity Object 引用)
                int fieldIdx = 0;
                do
                {
                    if (childProp.depth != depth + 1) continue;

                    string fieldName = childProp.name;
                    // 跳过 category 和 DisplayName 属性(抽象属性无序列化值)
                    if (fieldName == "Category" || fieldName == "DisplayName") continue;
                    // 跳过 editorTrackRow / editorDuration(编辑器缓存,Inspector 不显示)
                    if (fieldName == "editorTrackRow" || fieldName == "editorDuration") continue;

                    var fieldRect = new Rect(position.x + indent, y, fieldWidth, EditorGUIUtility.singleLineHeight);
                    EditorGUI.PropertyField(fieldRect, childProp, new GUIContent(ObjectNames.NicifyVariableName(fieldName)), true);
                    y += EditorGUIUtility.singleLineHeight + 1f;
                    fieldIdx++;
                }
                while (childProp.NextVisible(false) && fieldIdx < fields.Count);
            }
            else
            {
                // 路径 B:fallback 反射绘制基本值(仅值类型,无 Unity Object 引用)
                foreach (var fi in fields)
                {
                    string friendlyName = ObjectNames.NicifyVariableName(fi.Name);
                    var fieldRect = new Rect(position.x + indent, y, fieldWidth, EditorGUIUtility.singleLineHeight);

                    object val = fi.GetValue(target);
                    DrawReflectedField(fieldRect, fi, val, target, property);

                    y += EditorGUIUtility.singleLineHeight + 1f;
                }
            }
        }

        // ── 辅助方法 ─────────────────────────────────────────

        /// <summary>从 [SerializeReference] 属性中取出托管对象引用。</summary>
        private object GetManagedTarget(SerializedProperty property)
        {
            return property.managedReferenceValue;
        }

        /// <summary>获取需要显示的可视化字段(排除基类缓存字段)。</summary>
        private List<FieldInfo> GetVisibleFields(Type type)
        {
            if (s_fieldCache.TryGetValue(type, out var cached))
                return cached;

            var all = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var result = new List<FieldInfo>();
            foreach (var fi in all)
            {
                // 排除抽象属性 backing(不会有)
                if (fi.Name == "editorTrackRow" || fi.Name == "editorDuration") continue;
                // 排除 [HideInInspector]
                if (fi.GetCustomAttribute<HideInInspector>() != null) continue;
                result.Add(fi);
            }
            s_fieldCache[type] = result;
            LimitCache(s_fieldCache);
            return result;
        }

        private bool GetFoldout(string path)
        {
            s_foldoutCache.TryGetValue(path, out bool v);
            return v;
        }
        private void SetFoldout(string path, bool v)
        {
            s_foldoutCache[path] = v;
            LimitCache(s_foldoutCache);
        }

        private Color GetCategoryColor(SkillNodeCategory cat)
        {
            if (s_categoryColors.TryGetValue(cat, out var c)) return c;
            return Color.gray;
        }

        /// <summary>fallback 反射字段绘制(基本类型 + Vector3 + GameObject + AudioClip 等)。</summary>
        private void DrawReflectedField(Rect rect, FieldInfo fi, object currentValue, object target, SerializedProperty property)
        {
            EditorGUI.BeginChangeCheck();
            object newValue = DoField(rect, fi.FieldType, currentValue, ObjectNames.NicifyVariableName(fi.Name));
            if (EditorGUI.EndChangeCheck())
            {
                // Undo 支持:记录所属 ScriptableObject 以便 Ctrl+Z 正常工作
                if (property.serializedObject?.targetObject != null)
                {
                    Undo.RecordObject(property.serializedObject.targetObject, "修改节点字段");
                    EditorUtility.SetDirty(property.serializedObject.targetObject);
                }
                fi.SetValue(target, newValue);
            }
        }

        private object DoField(Rect rect, Type fieldType, object value, string label)
        {
            if (fieldType == typeof(float))
                return EditorGUI.FloatField(rect, label, value != null ? (float)value : 0f);
            if (fieldType == typeof(int))
                return EditorGUI.IntField(rect, label, value != null ? (int)value : 0);
            if (fieldType == typeof(bool))
                return EditorGUI.Toggle(rect, label, value != null ? (bool)value : false);
            if (fieldType == typeof(string))
                return EditorGUI.TextField(rect, label, value as string ?? "");
            if (fieldType == typeof(Vector3))
                return EditorGUI.Vector3Field(rect, label, value != null ? (Vector3)value : Vector3.zero);
            if (fieldType == typeof(Vector2))
                return EditorGUI.Vector2Field(rect, label, value != null ? (Vector2)value : Vector2.zero);
            if (fieldType == typeof(Color))
                return EditorGUI.ColorField(rect, label, value != null ? (Color)value : Color.white);
            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                return EditorGUI.ObjectField(rect, label, (UnityEngine.Object)value, fieldType, false);
            if (fieldType.IsEnum)
                return EditorGUI.EnumPopup(rect, label, value as Enum ?? (Enum)Enum.ToObject(fieldType, 0));

            // fallback:显示只读字符串
            EditorGUI.LabelField(rect, label, value?.ToString() ?? "null");
            return value;
        }
    }
}
#endif
