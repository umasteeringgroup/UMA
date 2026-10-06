using System;
using UnityEditor;

namespace UMA.Editors
{
    /// <summary>Registers an editor action for an inspected item type.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class UMAPluginAttribute : Attribute
    {
        public Type TargetType { get; }
        public string Label { get; }
        public string Tooltip { get; set; }
        public int Order { get; set; }
        public bool IncludeDerived { get; set; } = true;
        public bool SupportsMultipleTargets { get; set; }
        public string ValidateMethod { get; set; }

        public UMAPluginAttribute(Type targetType, string label)
        {
            TargetType = targetType;
            Label = label;
        }
    }

    /// <summary>The editor launcher contract, independent of optional tool APIs.</summary>
    public static class UMAPluginApi
    {
        public static int Version => 1;
    }

    /// <summary>
    /// Implement on an Editor, or supply a window adapter to RegisterExplicitHost,
    /// when the host keeps working data outside its SerializedObject.
    /// </summary>
    public interface IUMAPluginHost
    {
        bool CanRunPluginActions { get; }
        bool TryPreparePluginAction();
        void RefreshAfterPluginAction();
    }
}
