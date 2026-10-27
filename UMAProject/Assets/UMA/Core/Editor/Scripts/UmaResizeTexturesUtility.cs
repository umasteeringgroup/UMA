using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public static class UmaResizeTexturesUtility
    {
        public sealed class Entry
        {
            public string Guid, Path, Message;
            public Vector2Int CurrentSize;
            public bool CanResize;
        }

        private static List<TextureImporterPlatformSettings> LimitsToLower(TextureImporter importer, int maximumSize)
        {
            var result = new List<TextureImporterPlatformSettings>();
            var defaults = importer.GetDefaultPlatformTextureSettings();
            if (defaults.maxTextureSize > maximumSize) result.Add(defaults);
            using var serialized = new SerializedObject(importer);
            var platforms = serialized.FindProperty("m_PlatformSettings");
            var visited = new HashSet<string>(StringComparer.Ordinal) { defaults.name };
            for (int i = 0; i < platforms.arraySize; i++)
            {
                string name = platforms.GetArrayElementAtIndex(i).FindPropertyRelative("m_BuildTarget").stringValue;
                if (string.IsNullOrEmpty(name) || !visited.Add(name)) continue;
                var settings = importer.GetPlatformTextureSettings(name);
                if (settings.overridden && settings.maxTextureSize > maximumSize) result.Add(settings);
            }
            return result;
        }

        public static Entry Inspect(string guid, int maximumSize)
        {
            var entry = new Entry { Guid = guid, Path = AssetDatabase.GUIDToAssetPath(guid) };
            try
            {
                if (maximumSize < 32 || maximumSize > 16384 || !Mathf.IsPowerOfTwo(maximumSize))
                    throw new ArgumentOutOfRangeException(nameof(maximumSize), "Choose a power of two from 32 to 16384.");
                if (string.IsNullOrEmpty(entry.Path) || !entry.Path.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Only project-owned texture importers under Assets can be changed.");
                var importer = AssetImporter.GetAtPath(entry.Path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("This asset does not have a TextureImporter.");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Path);
                if (texture != null) entry.CurrentSize = new Vector2Int(texture.width, texture.height);
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                if (Math.Max(width, height) <= maximumSize)
                {
                    entry.Message = "Unchanged: source is already at or below the maximum.";
                    return entry;
                }
                var changes = LimitsToLower(importer, maximumSize);
                if (changes.Count == 0)
                {
                    entry.Message = "Unchanged: import limits are already at or below the maximum.";
                    return entry;
                }
                string meta = entry.Path + ".meta";
                if (!AssetDatabase.IsOpenForEdit(meta) || (File.GetAttributes(meta) & FileAttributes.ReadOnly) != 0)
                    throw new InvalidOperationException("Import settings are read-only or need to be checked out.");
                entry.CanResize = true;
                string defaultName = importer.GetDefaultPlatformTextureSettings().name;
                entry.Message = "Maximum Size: " + string.Join(", ", changes.Select(p =>
                    $"{(p.name == defaultName ? "Default" : p.name)} {p.maxTextureSize} → {maximumSize}"));
            }
            catch (Exception exception) { entry.Message = "Skipped: " + exception.Message; }
            return entry;
        }

        public static Entry Resize(string guid, int maximumSize)
        {
            var entry = Inspect(guid, maximumSize);
            if (!entry.CanResize) return entry;
            var importer = (TextureImporter)AssetImporter.GetAtPath(entry.Path);
            var changes = LimitsToLower(importer, maximumSize);
            Undo.RecordObject(importer, "Resize Selected Textures");
            foreach (var settings in changes)
            {
                settings.maxTextureSize = maximumSize;
                importer.SetPlatformTextureSettings(settings);
            }
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Path);
            if (texture != null) entry.CurrentSize = new Vector2Int(texture.width, texture.height);
            entry.Message = "Saved lower import limits and reimported. Source image unchanged.";
            return entry;
        }
    }
}
