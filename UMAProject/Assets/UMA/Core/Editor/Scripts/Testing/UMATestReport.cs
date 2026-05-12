#if UNITY_EDITOR

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UMA.Editors
{
    public enum UMATestSeverity
    {
        Pass,
        Info,
        Warning,
        Error
    }

    public sealed class UMATestMessage
    {
        public UMATestMessage(UMATestSeverity severity, string category, string message, Object context = null)
        {
            Severity = severity;
            Category = category ?? string.Empty;
            Message = message ?? string.Empty;
            Context = context;
        }

        public UMATestSeverity Severity { get; }
        public string Category { get; }
        public string Message { get; }
        public Object Context { get; }

        public string ToInspectorString()
        {
            switch (Severity)
            {
                case UMATestSeverity.Error:
                    return "Error: " + Message;
                case UMATestSeverity.Warning:
                    return "Warning: " + Message;
                default:
                    return "Info: " + Message;
            }
        }

        public override string ToString()
        {
            string prefix = string.IsNullOrEmpty(Category) ? Severity.ToString() : Severity + " / " + Category;
            return prefix + ": " + Message;
        }
    }

    public sealed class UMATestReport
    {
        private readonly List<UMATestMessage> messages = new List<UMATestMessage>();

        public UMATestReport(string title, RaceData race = null)
        {
            Title = title ?? "UMA Test";
            Race = race;
        }

        public string Title { get; }
        public RaceData Race { get; }
        public IReadOnlyList<UMATestMessage> Messages => messages;
        public bool HasErrors => ErrorCount > 0;
        public bool HasWarnings => WarningCount > 0;

        public int PassCount => CountSeverity(UMATestSeverity.Pass);
        public int InfoCount => CountSeverity(UMATestSeverity.Info);
        public int WarningCount => CountSeverity(UMATestSeverity.Warning);
        public int ErrorCount => CountSeverity(UMATestSeverity.Error);

        public void Add(UMATestSeverity severity, string category, string message, Object context = null)
        {
            messages.Add(new UMATestMessage(severity, category, message, context));
        }

        public void AddPass(string category, string message, Object context = null)
        {
            Add(UMATestSeverity.Pass, category, message, context);
        }

        public void AddInfo(string category, string message, Object context = null)
        {
            Add(UMATestSeverity.Info, category, message, context);
        }

        public void AddWarning(string category, string message, Object context = null)
        {
            Add(UMATestSeverity.Warning, category, message, context);
        }

        public void AddError(string category, string message, Object context = null)
        {
            Add(UMATestSeverity.Error, category, message, context);
        }

        public void AddRange(IEnumerable<UMATestMessage> items)
        {
            if (items == null)
            {
                return;
            }

            foreach (UMATestMessage item in items)
            {
                if (item != null)
                {
                    messages.Add(item);
                }
            }
        }

        public string ToLogString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(Title);
            if (Race != null)
            {
                builder.Append(" for '").Append(Race.raceName).Append("'");
            }

            builder.Append(" - ")
                .Append(ErrorCount).Append(" error(s), ")
                .Append(WarningCount).Append(" warning(s), ")
                .Append(PassCount).Append(" pass(es)")
                .AppendLine();

            for (int i = 0; i < messages.Count; i++)
            {
                builder.Append("- ").Append(messages[i]).AppendLine();
            }

            return builder.ToString();
        }

        private int CountSeverity(UMATestSeverity severity)
        {
            int count = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].Severity == severity)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public sealed class UMARaceSmokeTestOptions
    {
        public bool ValidateBaseRecipe = true;
        public bool GenerateTemporaryAvatar = true;
        public bool IncludePassMessages = true;

        public static UMARaceSmokeTestOptions Default => new UMARaceSmokeTestOptions();
    }
}

#endif