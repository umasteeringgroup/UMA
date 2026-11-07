using System;
using System.Collections.Generic;
using System.IO;
using UMA;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal static class TexturePaintMaterialPresetPackager
    {
        private sealed class PackagedObject
        {
            public UnityEngine.Object source;
            public UnityEngine.Object packaged;
        }

        private sealed class Context
        {
            public TexturePaintMaterialPreset preset;
            public int nameIndex;
            public readonly List<PackagedObject> objects = new List<PackagedObject>();
            public readonly List<TexturePaintMaterialPresetPackagedDependency> dependencies =
                new List<TexturePaintMaterialPresetPackagedDependency>();
            public readonly HashSet<string> externalDependencies =
                new HashSet<string>(StringComparer.Ordinal);
        }

        public static TexturePaintMaterialPreset Package(TexturePaintMaterialPreset source,
            string assetPath)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(assetPath) ||
                !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Packaged presets must be saved as an .asset below Assets.",
                    nameof(assetPath));
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                throw new IOException("An asset already exists at the selected package path.");

            TexturePaintMaterialPreset packaged =
                ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();
            try
            {
                EditorUtility.CopySerialized(source, packaged);
                packaged.name = Path.GetFileNameWithoutExtension(assetPath);
                packaged.schemaVersion = TexturePaintMaterialPreset.CurrentSchemaVersion;
                packaged.presetId = Guid.NewGuid().ToString("N");
                packaged.packaged = true;
                packaged.packagedFromPresetId = !string.IsNullOrEmpty(source.packagedFromPresetId)
                    ? source.packagedFromPresetId : source.presetId;
                packaged.packagedUtc = DateTime.UtcNow.ToString("O");
                packaged.modifiedUtc = packaged.packagedUtc;
                packaged.packagedDependencies =
                    new List<TexturePaintMaterialPresetPackagedDependency>();
                packaged.packagedExternalDependencies = new List<string>();
                InlinePixelPayloads(packaged);
                AssetDatabase.CreateAsset(packaged, assetPath);

                var context = new Context { preset = packaged };
                RewriteReferences(packaged, context);
                if (packaged.plugins != null)
                    for (int i = 0; i < packaged.plugins.Count; i++)
                    {
                        TexturePaintMaterialPresetPlugin plugin = packaged.plugins[i];
                        if (plugin == null || string.IsNullOrEmpty(plugin.pluginId)) continue;
                        context.externalDependencies.Add("Plugin: " + plugin.pluginId +
                            (string.IsNullOrEmpty(plugin.pluginVersion)
                                ? string.Empty : " " + plugin.pluginVersion));
                    }
                packaged.packagedDependencies = context.dependencies;
                packaged.packagedExternalDependencies.AddRange(context.externalDependencies);
                packaged.packagedExternalDependencies.Sort(StringComparer.Ordinal);
                packaged.Migrate();
                EditorUtility.SetDirty(packaged);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                return AssetDatabase.LoadAssetAtPath<TexturePaintMaterialPreset>(assetPath);
            }
            catch
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                    AssetDatabase.DeleteAsset(assetPath);
                else if (packaged != null) UnityEngine.Object.DestroyImmediate(packaged);
                throw;
            }
        }

        private static void RewriteReferences(UnityEngine.Object target, Context context)
        {
            if (target == null) return;
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = true;
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.objectReferenceValue == null || property.name == "m_Script") continue;
                UnityEngine.Object source = property.objectReferenceValue;
                UnityEngine.Object replacement = PackageReference(source, context);
                if (replacement != null && !ReferenceEquals(replacement, source))
                    property.objectReferenceValue = replacement;
                else if (replacement == null) RecordExternalDependency(source, context);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static UnityEngine.Object PackageReference(UnityEngine.Object source,
            Context context)
        {
            if (source == null) return null;
            UnityEngine.Object existing = FindPackaged(source, context);
            if (existing != null) return existing;
            if (source is Texture texture) return PackageTexture(texture, context);
            if (source is Sprite sprite) return PackageSprite(sprite, context);
            if (source is OverlayDataAsset overlay) return PackageScriptable(overlay, context);
            if (source is BrushPreset brush) return PackageScriptable(brush, context);
            if (source is UMAMaterial umaMaterial) return PackageScriptable(umaMaterial, context);
            if (source is Material material) return PackageMaterial(material, context);
            if (source is Font font) return PackageFont(font, context);
            return null;
        }

        private static Texture PackageTexture(Texture source, Context context)
        {
            Texture clone = UnityEngine.Object.Instantiate(source);
            AddEmbedded(source, clone, "Texture", context);
            return clone;
        }

        private static Sprite PackageSprite(Sprite source, Context context)
        {
            Texture2D texture = PackageReference(source.texture, context) as Texture2D;
            if (texture == null) throw new InvalidOperationException(
                $"Sprite '{source.name}' does not have a packageable Texture2D.");
            Rect rect = source.rect;
            Vector2 pivot = rect.width > 0f && rect.height > 0f
                ? new Vector2(source.pivot.x / rect.width, source.pivot.y / rect.height)
                : new Vector2(0.5f, 0.5f);
            Sprite clone = Sprite.Create(texture, rect, pivot, source.pixelsPerUnit, 0,
                SpriteMeshType.FullRect, source.border, false);
            try { clone.OverrideGeometry(source.vertices, source.triangles); }
            catch (Exception) { }
            AddEmbedded(source, clone, "Sprite", context);
            return clone;
        }

        private static T PackageScriptable<T>(T source, Context context)
            where T : ScriptableObject
        {
            T clone = UnityEngine.Object.Instantiate(source);
            AddEmbedded(source, clone, typeof(T).Name, context);
            RewriteReferences(clone, context);
            return clone;
        }

        private static Material PackageMaterial(Material source, Context context)
        {
            Material clone = new Material(source);
            AddEmbedded(source, clone, "Material", context);
            RewriteReferences(clone, context);
            string[] properties = source.GetTexturePropertyNames();
            for (int i = 0; i < properties.Length; i++)
            {
                Texture texture = source.GetTexture(properties[i]);
                if (texture == null) continue;
                clone.SetTexture(properties[i], PackageReference(texture, context) as Texture);
            }
            return clone;
        }

        private static Font PackageFont(Font source, Context context)
        {
            Font clone = UnityEngine.Object.Instantiate(source);
            AddEmbedded(source, clone, "Font", context);
            if (source.material != null)
                clone.material = PackageReference(source.material, context) as Material;
            RewriteReferences(clone, context);
            return clone;
        }

        private static void AddEmbedded(UnityEngine.Object source, UnityEngine.Object clone,
            string kind, Context context)
        {
            if (clone == null) throw new InvalidOperationException(
                $"Could not clone preset dependency '{source?.name}'.");
            clone.name = $"Embedded {++context.nameIndex:000} {CleanName(source.name)}";
            clone.hideFlags = HideFlags.HideInHierarchy;
            context.objects.Add(new PackagedObject { source = source, packaged = clone });
            AssetDatabase.AddObjectToAsset(clone, context.preset);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid,
                out long localId);
            context.dependencies.Add(
                new TexturePaintMaterialPresetPackagedDependency
                {
                    name = source.name,
                    type = kind,
                    sourceGuid = guid,
                    sourceLocalId = localId
                });
        }

        private static UnityEngine.Object FindPackaged(UnityEngine.Object source, Context context)
        {
            for (int i = 0; i < context.objects.Count; i++)
                if (ReferenceEquals(context.objects[i].source, source))
                    return context.objects[i].packaged;
            return null;
        }

        private static void RecordExternalDependency(UnityEngine.Object source, Context context)
        {
            if (source == null || source is MonoScript || source is TexturePaintMaterialPreset) return;
            string path = AssetDatabase.GetAssetPath(source);
            context.externalDependencies.Add(source.GetType().Name + ": " + source.name +
                (string.IsNullOrEmpty(path) ? string.Empty : " (" + path + ")"));
        }

        private static void InlinePixelPayloads(TexturePaintMaterialPreset preset)
        {
            if (preset?.layers == null) return;
            for (int layerIndex = 0; layerIndex < preset.layers.Count; layerIndex++)
            {
                TexturePaintDocumentLayer layer = preset.layers[layerIndex];
                if (layer == null) continue;
                InlinePixels(layer.maskPixels);
                if (layer.channels == null) continue;
                for (int channelIndex = 0; channelIndex < layer.channels.Count; channelIndex++)
                    InlinePixels(layer.channels[channelIndex]?.pixels);
            }
        }

        private static void InlinePixels(TexturePaintPixelData pixels)
        {
            if (pixels == null) return;
            byte[] bytes = pixels.GetCompressedBytes();
            pixels.compressedBytes = bytes == null ? null : (byte[])bytes.Clone();
            pixels.dataAsset = null;
            pixels.storageKey = null;
            pixels.recoveryBlobKey = null;
        }

        private static string CleanName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Dependency";
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }
    }
}
