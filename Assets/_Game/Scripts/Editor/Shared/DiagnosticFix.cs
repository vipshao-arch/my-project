#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;

namespace Game.EditorTools.Shared
{
    /// <summary>
    /// 诊断问题的可自动修复类型。
    /// </summary>
    public enum DiagnosticFixType
    {
        /// <summary>不可自动修复，仅显示提示。</summary>
        None,

        /// <summary>添加指定组件到目标 GameObject。</summary>
        AddComponent,

        /// <summary>设置目标组件上的属性（如 isKinematic = true）。</summary>
        SetProperty,

        /// <summary>设置 GameObject 的 Tag。</summary>
        SetTag,

        /// <summary>设置 GameObject 的 Layer。</summary>
        SetLayer,

        /// <summary>拖入资产引用（如 AnimatorController / Avatar），需手动操作。</summary>
        ManualAsset,
    }

    /// <summary>
    /// 诊断问题条目 — 包含可执行修复信息。
    /// </summary>
    public class DiagnosticIssue
    {
        public string description;
        public string fixHint;
        public DiagnosticFixType fixType = DiagnosticFixType.None;
        public GameObject target;
        public Type componentType;          // AddComponent 时使用
        public string propertyName;         // SetProperty 时使用
        public object propertyValue;        // SetProperty 时使用

        /// <summary>此问题是否可以自动修复。</summary>
        public bool CanAutoFix => fixType != DiagnosticFixType.None && fixType != DiagnosticFixType.ManualAsset;

        /// <summary>执行修复操作。</summary>
        public bool ExecuteFix()
        {
            if (!CanAutoFix || target == null) return false;

            switch (fixType)
            {
                case DiagnosticFixType.AddComponent:
                    if (componentType != null && target.GetComponent(componentType) == null)
                    {
                        Undo.RecordObject(target, $"添加 {componentType.Name}");
                        target.AddComponent(componentType);
                        UnityEditor.EditorUtility.SetDirty(target);
                        return true;
                    }
                    break;

                case DiagnosticFixType.SetProperty:
                    if (!string.IsNullOrEmpty(propertyName))
                    {
                        // 遍历 target 上的所有组件查找属性
                        foreach (var comp in target.GetComponents<Component>())
                        {
                            var prop = comp.GetType().GetProperty(propertyName);
                            if (prop != null && prop.CanWrite)
                            {
                                Undo.RecordObject(comp, $"设置 {propertyName}");
                                prop.SetValue(comp, propertyValue);
                                UnityEditor.EditorUtility.SetDirty(comp);
                                return true;
                            }
                            var field = comp.GetType().GetField(propertyName);
                            if (field != null)
                            {
                                Undo.RecordObject(comp, $"设置 {propertyName}");
                                field.SetValue(comp, propertyValue);
                                UnityEditor.EditorUtility.SetDirty(comp);
                                return true;
                            }
                        }
                    }
                    break;

                case DiagnosticFixType.SetTag:
                    Undo.RecordObject(target, "设置 Tag");
                    target.tag = propertyValue as string ?? target.tag;
                    UnityEditor.EditorUtility.SetDirty(target);
                    return true;

                case DiagnosticFixType.SetLayer:
                    Undo.RecordObject(target, "设置 Layer");
                    target.layer = (int)propertyValue;
                    UnityEditor.EditorUtility.SetDirty(target);
                    return true;
            }

            return false;
        }

        // ── 工厂方法 ──────────────────────────────────────────

        public static DiagnosticIssue MissingComponent(GameObject target, Type componentType,
            string label, string fixHint)
        {
            return new DiagnosticIssue
            {
                description = $"缺少 {label} 组件",
                fixHint = fixHint,
                fixType = DiagnosticFixType.AddComponent,
                target = target,
                componentType = componentType,
            };
        }

        public static DiagnosticIssue ManualAsset(GameObject target, string description, string fixHint)
        {
            return new DiagnosticIssue
            {
                description = description,
                fixHint = fixHint,
                fixType = DiagnosticFixType.ManualAsset,
                target = target,
            };
        }

        public static DiagnosticIssue MissingCollider(GameObject target)
        {
            return new DiagnosticIssue
            {
                description = "缺少 Collider 组件",
                fixHint = "自动添加 CapsuleCollider (高2m,半径0.3m)",
                fixType = DiagnosticFixType.AddComponent,
                target = target,
                componentType = typeof(CapsuleCollider),
            };
        }
    }
}
#endif
