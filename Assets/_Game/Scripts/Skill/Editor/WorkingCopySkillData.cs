// =====================================================================
//  WorkingCopySkillData —— 预览用的内存副本(V3 计划 §2.3 / §4.1)
//
//  设计要点:
//   1. Source = 用户选的源 .asset(只读,Confirm 时被覆盖)
//   2. Working = ScriptableObject.CreateInstance<SkillData>() 内存深拷贝
//      - 走 EditorJsonUtility.ToJson/FromJsonOverwrite,对 [SerializeReference] 完整支持
//   3. 任何编辑只动 Working,Confirm 才把 Working 写回 Source + SaveAssetIfDirty
//   4. Undo 栈:深 32,存 JSON 快照,主 wizard 拦截 Ctrl+Z 优先吃这里
//   5. Working 用 HideFlags.HideAndDontSave,关掉编辑器=丢弃(预期)
// =====================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.SkillSystem.EditorTools
{
    /// <summary>预览用的内存副本 — 所有 Edit 落这里,Confirm 才覆盖原 .asset。</summary>
    public class WorkingCopySkillData
    {
        public SkillData Source { get; private set; }
        public SkillData Working { get; private set; }
        public bool IsDirty { get; private set; }
        public bool HasRedo => _redoStack.Count > 0;

        private const int MAX_UNDO = 32;
        private readonly List<string> _undoStack = new List<string>(MAX_UNDO);
        private readonly List<string> _redoStack = new List<string>(MAX_UNDO);

        public WorkingCopySkillData(SkillData source)
        {
            Source = source;
            Working = ScriptableObject.CreateInstance<SkillData>();
            // 用 HideInHierarchy 而非 HideAndDontSave:
            //   HideAndDontSave 包含 NotEditable,会让 SerializedObject 渲染的字段全部灰置。
            //   Working 是用 CreateInstance 创建的内存对象,本来就不会被 Unity 持久化,
            //   只需要 HideInHierarchy 阻止在 Hierarchy 窗口显示即可,Editor 关闭时会被 GC。
            Working.hideFlags = HideFlags.HideInHierarchy;
            RebuildFromSource();
        }

        /// <summary>重新从 Source 深拷贝一份到 Working(清空 Undo 栈)。</summary>
        public void RebuildFromSource()
        {
            if (Source == null || Working == null)
            {
                IsDirty = false;
                _undoStack.Clear();
                return;
            }
            var json = EditorJsonUtility.ToJson(Source);
            EditorJsonUtility.FromJsonOverwrite(json, Working);
            Working.name = Source.name + " (Working Copy)";
            // 关键:不要用 HideAndDontSave,否则 SerializedObject 字段会全部灰置。
            Working.hideFlags = HideFlags.HideInHierarchy;
            IsDirty = false;
            _undoStack.Clear();
            _redoStack.Clear();
        }

        /// <summary>把 Working 写回 Source + 落盘。返回是否执行了保存。</summary>
        public bool Confirm()
        {
            ConfirmWithoutSave();
            if (Source == null) return false;
            AssetDatabase.SaveAssetIfDirty(Source);
            return true;
        }

        /// <summary>把 Working 写回 Source(仅内存,不落盘)。用于 _paramsDirty→RebuildFromSource 前
        /// 保存面板编辑到 Source,避免 RebuildFromSource 全量覆盖丢失面板编辑。</summary>
        public void ConfirmWithoutSave()
        {
            if (Source == null || Working == null) return;
            UnityEditor.Undo.RecordObject(Source, "Confirm SkillData Preview");
            var originalName = Source.name;
            var json = EditorJsonUtility.ToJson(Working);
            EditorJsonUtility.FromJsonOverwrite(json, Source);
            Source.name = originalName;
            EditorUtility.SetDirty(Source);
            IsDirty = false;
        }

        /// <summary>丢弃 Working 的改动,重新从 Source 深拷贝。</summary>
        public void Revert() => RebuildFromSource();

        /// <summary>推入当前 Working 快照(在用户做改动的 BeginChangeCheck 末尾调)。</summary>
        public void SnapshotForUndo()
        {
            if (Working == null) return;
            _undoStack.Add(EditorJsonUtility.ToJson(Working));
            if (_undoStack.Count > MAX_UNDO) _undoStack.RemoveAt(0);
            _redoStack.Clear();   // 新操作会导致 redo 分支失效
        }

        /// <summary>弹出最近一份快照恢复 Working。返回是否真做了 Undo。</summary>
        public bool Undo()
        {
            if (_undoStack.Count == 0 || Working == null) return false;
            // 把当前状态推入 redo 栈
            _redoStack.Add(EditorJsonUtility.ToJson(Working));
            if (_redoStack.Count > MAX_UNDO) _redoStack.RemoveAt(0);
            var json = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            EditorJsonUtility.FromJsonOverwrite(json, Working);
            IsDirty = true;   // 撤回也算"与 Source 不一致",等下次 Save
            return true;
        }

        /// <summary>重做最近一次 Undo。返回是否真做了 Redo。</summary>
        public bool Redo()
        {
            if (_redoStack.Count == 0 || Working == null) return false;
            // 把当前状态推回 undo 栈
            _undoStack.Add(EditorJsonUtility.ToJson(Working));
            if (_undoStack.Count > MAX_UNDO) _undoStack.RemoveAt(0);
            var json = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            EditorJsonUtility.FromJsonOverwrite(json, Working);
            IsDirty = true;
            return true;
        }

        public void Dispose()
        {
            if (Working != null) Object.DestroyImmediate(Working);
            Working = null;
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
