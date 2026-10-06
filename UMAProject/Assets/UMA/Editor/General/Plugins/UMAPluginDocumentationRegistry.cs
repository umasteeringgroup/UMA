using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [FilePath("ProjectSettings/UMAPluginDocumentation.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class UMAPluginDocumentationRegistry : ScriptableSingleton<UMAPluginDocumentationRegistry>
    {
        public const string DescriptorFileName = "UMAPluginDocumentation.json";

        [Serializable]
        public sealed class Registration
        {
            public string pluginId;
            public string displayName;
            public string documentationGuid;
            public string descriptorGuid;
            public string DocumentationPath => AssetDatabase.GUIDToAssetPath(documentationGuid);
        }

        [Serializable]
        private sealed class Descriptor
        {
            public string pluginId;
            public string displayName;
            public string documentationFolder;
        }

        [SerializeField] private List<Registration> registrations = new();
        public static event Action Changed;
        public static IReadOnlyList<Registration> Registrations => instance.registrations
            .OrderBy(r => r.displayName, StringComparer.OrdinalIgnoreCase).ToArray();

        public static bool RegisterDescriptor(string assetPath)
        {
            if (!string.Equals(Path.GetFileName(assetPath), DescriptorFileName, StringComparison.Ordinal)) return false;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset == null) return false;
            var descriptor = JsonUtility.FromJson<Descriptor>(asset.text);
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.pluginId) ||
                descriptor.pluginId == "uma" || string.IsNullOrWhiteSpace(descriptor.displayName))
                throw new InvalidDataException("Plugin documentation needs a unique pluginId and displayName.");
            string relative = (descriptor.documentationFolder ?? "").Replace('\\', '/');
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) ||
                relative.Split('/').Any(s => s == ".." || s == "." || s.Length == 0))
                throw new InvalidDataException("documentationFolder must name a folder within the plugin.");
            string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/') + "/" + relative;
            if (!AssetDatabase.IsValidFolder(folder))
                throw new InvalidDataException("Plugin documentation folder is missing: " + folder);
            string sourceGuid = AssetDatabase.AssetPathToGUID(assetPath);
            string folderGuid = AssetDatabase.AssetPathToGUID(folder);
            var entries = instance.registrations;
            var current = entries.Find(r => r.pluginId == descriptor.pluginId);
            if (current != null && current.descriptorGuid != sourceGuid &&
                !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(current.descriptorGuid)))
                throw new InvalidDataException("Duplicate plugin documentation id: " + descriptor.pluginId);
            if (current != null && current.displayName == descriptor.displayName &&
                current.documentationGuid == folderGuid && current.descriptorGuid == sourceGuid) return false;
            entries.RemoveAll(r => r.pluginId == descriptor.pluginId || r.descriptorGuid == sourceGuid);
            entries.Add(new Registration { pluginId = descriptor.pluginId, displayName = descriptor.displayName,
                documentationGuid = folderGuid, descriptorGuid = sourceGuid });
            Persist();
            return true;
        }

        public static void Unregister(string pluginId)
        {
            if (instance.registrations.RemoveAll(r => r.pluginId == pluginId) > 0) Persist();
        }

        public static void PruneMissing()
        {
            if (instance.registrations.RemoveAll(r =>
                AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(r.descriptorGuid)) == null ||
                !AssetDatabase.IsValidFolder(r.DocumentationPath)) > 0) Persist();
        }

        public static bool IsPluginDocument(string path) => instance.registrations.Any(r =>
            !string.IsNullOrEmpty(r.DocumentationPath) &&
            path.StartsWith(r.DocumentationPath + "/", StringComparison.OrdinalIgnoreCase));

        private static void Persist()
        {
            instance.Save(true);
            Changed?.Invoke();
        }
    }

    [InitializeOnLoad]
    internal sealed class UMAPluginDocumentationImports : AssetPostprocessor
    {
        private static readonly HashSet<string> pending = new(StringComparer.Ordinal);
        private const string PendingKey = "UMA.Documentation.PendingImports";
        static UMAPluginDocumentationImports() { EditorApplication.update += Process; }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            foreach (string path in imported.Concat(moved))
                if (Path.GetFileName(path) == UMAPluginDocumentationRegistry.DescriptorFileName) pending.Add(path);
            foreach (string path in SessionState.GetString(PendingKey, "").Split('\n'))
                if (!string.IsNullOrEmpty(path)) pending.Add(path);
            SessionState.SetString(PendingKey, string.Join("\n", pending));
            EditorApplication.update -= Process;
            EditorApplication.update += Process;
        }

        private static void Process()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= Process;
            UMAPluginDocumentationRegistry.PruneMissing();
            foreach (string path in SessionState.GetString(PendingKey, "").Split('\n'))
                if (!string.IsNullOrEmpty(path)) pending.Add(path);
            var paths = pending.ToArray(); pending.Clear();
            SessionState.EraseString(PendingKey);
            foreach (string path in paths)
            {
                try { UMAPluginDocumentationRegistry.RegisterDescriptor(path); }
                catch (Exception e) { Debug.LogWarning("[UMA Documentation] " + path + ": " + e.Message); }
            }
        }
    }
}
