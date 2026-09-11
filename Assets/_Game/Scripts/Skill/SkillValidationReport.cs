using System.Collections.Generic;

namespace Game.SkillSystem
{
    /// <summary>
    /// SkillData 校验报告 — 供 ValidateForPreview / ValidateForRuntime / ValidateFull 使用。
    /// 收集错误、警告、信息条目，调用方可据此决定是否阻止预览或技能释放。
    /// </summary>
    public class SkillValidationReport
    {
        public enum Severity { Info, Warning, Error }

        public struct Entry
        {
            public Severity Severity;
            public string Message;
            public string FieldName;

            public Entry(Severity severity, string message, string fieldName)
            {
                Severity = severity;
                Message = message;
                FieldName = fieldName;
            }
        }

        public readonly List<Entry> Entries = new List<Entry>();

        /// <summary>是否存在 Error 级别条目。</summary>
        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Entries.Count; i++)
                    if (Entries[i].Severity == Severity.Error) return true;
                return false;
            }
        }

        /// <summary>是否存在 Warning 级别条目。</summary>
        public bool HasWarnings
        {
            get
            {
                for (int i = 0; i < Entries.Count; i++)
                    if (Entries[i].Severity == Severity.Warning) return true;
                return false;
            }
        }

        public void AddInfo(string message, string fieldName = null)
            => Entries.Add(new Entry(Severity.Info, message, fieldName));

        public void AddWarning(string message, string fieldName = null)
            => Entries.Add(new Entry(Severity.Warning, message, fieldName));

        public void AddError(string message, string fieldName = null)
            => Entries.Add(new Entry(Severity.Error, message, fieldName));

        /// <summary>将另一个报告的条目合并到当前报告。</summary>
        public void Merge(SkillValidationReport other)
        {
            if (other == null || other.Entries.Count == 0) return;
            Entries.AddRange(other.Entries);
        }
    }
}
