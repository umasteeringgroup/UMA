using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    public static class UMAShaderPackageUtility
    {
        public static readonly string[] MaterialSlots = { "_material", "_secondPass", "_HDRPMaterial", "_HDRPSecondPass" };
        public static readonly string[] MaterialSlotLabels = { "Default", "Second pass", "HDRP", "HDRP second pass" };

        public sealed class Usage
        {
            public UMAMaterial Owner;
            public readonly List<int> Slots = new List<int>();
        }

        public sealed class Package
        {
            public string Path;
            public Shader Shader;
            public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
            public readonly List<Material> Materials = new List<Material>();
            public readonly List<Usage> Uses = new List<Usage>();
        }

        public sealed class ChannelMapping
        {
            public string Property;
            public bool Unused;
        }

        public sealed class Replacement
        {
            public UMAMaterial Owner;
            public Shader SourceShader;
            public Shader Shader;
            public string OwnerSnapshot;
            public readonly List<int> Slots = new List<int>();
            public readonly List<ChannelMapping> Channels = new List<ChannelMapping>();
            public readonly List<Replacement> Shared = new List<Replacement>();
        }

        public static bool IsPackage(string path) => string.Equals(System.IO.Path.GetExtension(path), ".umashaderpack", StringComparison.OrdinalIgnoreCase);

        public static List<Package> Scan(string[] folders = null, Action<float, string> progress = null)
        {
            bool Included(string path) => folders == null || folders.Any(folder => path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal));
            var packages = AssetDatabase.GetAllAssetPaths().Where(path => IsPackage(path) && Included(path))
                .Select(path => new Package { Path = path, Shader = AssetDatabase.LoadAssetAtPath<Shader>(path) })
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Path, StringComparer.Ordinal).ToList();
            var byShader = new Dictionary<Shader, Package>();
            foreach (var package in packages) if (package.Shader != null) byShader[package.Shader] = package;
            var materials = new HashSet<Material>();
            var owners = new HashSet<UMAMaterial>();
            // Search the whole project for users, even when package paths are restricted.
            var materialGuids = AssetDatabase.FindAssets("t:Material");
            var ownerGuids = AssetDatabase.FindAssets("t:UMAMaterial");
            int total = Math.Max(1, materialGuids.Length + ownerGuids.Length);
            for (int i = 0; i < materialGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                if (i % 20 == 0) progress?.Invoke((float)i / total, path);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Material material) materials.Add(material);
            }
            for (int i = 0; i < ownerGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(ownerGuids[i]);
                if (i % 20 == 0) progress?.Invoke((float)(materialGuids.Length + i) / total, path);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is UMAMaterial owner) owners.Add(owner);
            }
            foreach (var owner in owners)
            {
                using var serialized = new SerializedObject(owner);
                var uses = new Dictionary<Package, Usage>();
                for (int slot = 0; slot < MaterialSlots.Length; slot++)
                {
                    var material = serialized.FindProperty(MaterialSlots[slot])?.objectReferenceValue as Material;
                    if (material == null) continue;
                    materials.Add(material);
                    if (material.shader == null || !byShader.TryGetValue(material.shader, out var package)) continue;
                    if (!uses.TryGetValue(package, out var use))
                    {
                        use = new Usage { Owner = owner };
                        uses.Add(package, use);
                        package.Uses.Add(use);
                    }
                    use.Slots.Add(slot);
                }
            }
            foreach (var material in materials)
                if (material != null && material.shader != null && byShader.TryGetValue(material.shader, out var package)) package.Materials.Add(material);
            foreach (var package in packages)
            {
                package.Materials.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(AssetDatabase.GetAssetPath(a) + a.name, AssetDatabase.GetAssetPath(b) + b.name));
                package.Uses.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(AssetDatabase.GetAssetPath(a.Owner) + a.Owner.name, AssetDatabase.GetAssetPath(b.Owner) + b.Owner.name));
            }
            return packages;
        }

        public static Material GetMaterial(UMAMaterial owner, int slot)
        {
            using var serialized = new SerializedObject(owner);
            return serialized.FindProperty(MaterialSlots[slot]).objectReferenceValue as Material;
        }

        public static List<string> Properties(Shader shader, UMAMaterial.MaterialChannel channel)
        {
            var result = new List<string>();
            if (shader == null) return result;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var type = shader.GetPropertyType(i);
                bool compatible = channel.channelType == UMAMaterial.ChannelType.MaterialColor
                    ? type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector
                    : type == ShaderPropertyType.Texture && shader.GetPropertyTextureDimension(i) == TextureDimension.Tex2D;
                if (compatible) result.Add(shader.GetPropertyName(i));
            }
            return result;
        }

        public static string SuggestProperty(Shader shader, UMAMaterial.MaterialChannel channel)
        {
            var choices = Properties(shader, channel);
            if (choices.Contains(channel.materialPropertyName)) return channel.materialPropertyName;
            string[][] aliases = { new[] { "_MainTex", "_BaseMap", "_BaseColorMap" }, new[] { "_BumpMap", "_NormalMap" }, new[] { "_Color", "_BaseColor" } };
            foreach (var group in aliases)
                if (group.Contains(channel.materialPropertyName))
                    foreach (var property in group) if (choices.Contains(property)) return property;
            return string.Empty;
        }

        private static string ChannelKey(UMAMaterial.MaterialChannel channel)
            => channel.materialPropertyName + "|" + (channel.channelType == UMAMaterial.ChannelType.MaterialColor) + "|" + channel.NonShaderTexture;

        public static List<Replacement> FindShared(Replacement request)
        {
            var materials = new HashSet<Material>(request.Slots.Select(slot => GetMaterial(request.Owner, slot)));
            materials.Remove(null);
            var result = new List<Replacement>();
            var seen = new HashSet<UMAMaterial> { request.Owner };
            foreach (string guid in AssetDatabase.FindAssets("t:UMAMaterial"))
                foreach (var owner in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<UMAMaterial>())
                {
                    if (!seen.Add(owner)) continue;
                    var shared = new Replacement { Owner = owner, SourceShader = request.SourceShader,
                        Shader = request.Shader, OwnerSnapshot = EditorJsonUtility.ToJson(owner) };
                    for (int slot = 0; slot < MaterialSlots.Length; slot++)
                        if (materials.Contains(GetMaterial(owner, slot))) shared.Slots.Add(slot);
                    if (shared.Slots.Count > 0) result.Add(shared);
                }
            return result.OrderBy(item => AssetDatabase.GetAssetPath(item.Owner), StringComparer.Ordinal).ToList();
        }

        public static void RefreshShared(Replacement request)
        {
            var mappings = new Dictionary<string, ChannelMapping>();
            foreach (var item in new[] { request }.Concat(request.Shared))
            {
                if (item.Owner == null || item.OwnerSnapshot != EditorJsonUtility.ToJson(item.Owner)) continue;
                for (int i = 0; i < item.Channels.Count; i++)
                    mappings.TryAdd(ChannelKey(item.Owner.channels[i]), item.Channels[i]);
            }
            request.Shared.Clear();
            request.Shared.AddRange(FindShared(request));
            foreach (var item in new[] { request }.Concat(request.Shared))
            {
                item.Channels.Clear();
                foreach (var channel in item.Owner.channels ?? Array.Empty<UMAMaterial.MaterialChannel>())
                {
                    string key = ChannelKey(channel);
                    if (!mappings.TryGetValue(key, out var mapping))
                    {
                        mapping = new ChannelMapping { Property = SuggestProperty(request.Shader, channel), Unused = channel.NonShaderTexture };
                        mappings.Add(key, mapping);
                    }
                    item.Channels.Add(mapping);
                }
            }
        }

        public static string ValidateUpdate(Replacement request, bool rescan = false)
        {
            string error = Validate(request, updateExisting: true);
            if (error != null) return error;
            if (rescan)
            {
                var current = FindShared(request);
                if (current.Count != request.Shared.Count || current.Any(item => !request.Shared.Any(old =>
                    old.Owner == item.Owner && old.OwnerSnapshot == item.OwnerSnapshot && old.Slots.SequenceEqual(item.Slots))))
                    return "Shared UMAMaterial usage changed. Reopen Replace to review all affected mappings.";
            }
            var items = new[] { request }.Concat(request.Shared).ToList();
            foreach (var item in request.Shared)
            {
                item.Shader = request.Shader;
                error = Validate(item, updateExisting: true);
                if (error != null) return (item.Owner != null ? item.Owner.name : "Shared UMAMaterial") + ": " + error;
            }
            foreach (var material in request.Slots.Select(slot => GetMaterial(request.Owner, slot)).Distinct())
            {
                var destinations = new Dictionary<string, string>();
                foreach (var item in items.Where(item => item.Slots.Any(slot => GetMaterial(item.Owner, slot) == material)))
                    for (int i = 0; i < item.Channels.Count; i++)
                    {
                        var mapping = item.Channels[i];
                        if (mapping.Unused) continue;
                        string key = ChannelKey(item.Owner.channels[i]);
                        if (destinations.TryGetValue(mapping.Property, out var oldKey) && oldKey != key)
                            return material.name + ": different source properties map to " + mapping.Property + ". Choose separate properties or mark one unused.";
                        destinations[mapping.Property] = key;
                    }
            }
            return null;
        }

        public static string Validate(Replacement request, bool checkEditable = true, bool updateExisting = false)
        {
            if (request?.Owner == null || request.SourceShader == null) return "The source was removed. Refresh the package list and reopen Replace.";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Exit Play Mode before replacing material assets.";
            if (request.Shader == null) return "Choose a replacement shader.";
            if (IsPackage(AssetDatabase.GetAssetPath(request.Shader))) return "Choose a shader that is not imported from a .umashaderpack.";
            if (ShaderUtil.ShaderHasError(request.Shader)) return "The replacement shader has compilation errors.";
            if (request.OwnerSnapshot != EditorJsonUtility.ToJson(request.Owner)) return "The UMAMaterial changed while this dialog was open. Reopen Replace to use its current settings.";
            if (request.Slots.Count == 0) return "Select at least one material slot to replace.";
            if (request.Slots.Distinct().Count() != request.Slots.Count || request.Slots.Any(i => i < 0 || i >= MaterialSlots.Length)) return "Invalid material slot selection.";
            var channels = request.Owner.channels ?? Array.Empty<UMAMaterial.MaterialChannel>();
            if (request.Channels.Count != channels.Length) return "The channel list changed. Reopen Replace.";
            string ownerPath = AssetDatabase.GetAssetPath(request.Owner);
            if (string.IsNullOrEmpty(ownerPath) || !ownerPath.StartsWith("Assets/", StringComparison.Ordinal)) return "The UMAMaterial must be a writable asset under Assets. Copy package content into Assets first.";
            if (checkEditable && !AssetDatabase.IsOpenForEdit(request.Owner, StatusQueryOptions.ForceUpdate)) return "The UMAMaterial is read-only or not checked out.";
            var destinations = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < channels.Length; i++)
            {
                var mapping = request.Channels[i];
                if (mapping == null) return "A channel mapping is missing.";
                if (mapping.Unused) continue;
                if (string.IsNullOrEmpty(mapping.Property)) return "Choose a replacement property for channel " + i + ", or mark it as not used by the shader.";
                if (!Properties(request.Shader, channels[i]).Contains(mapping.Property)) return "Channel " + i + " is not compatible with property " + mapping.Property + ".";
                if (!destinations.Add(mapping.Property)) return "More than one channel maps to " + mapping.Property + ". Choose separate properties or mark an extra channel as unused.";
            }
            for (int slot = 0; slot < MaterialSlots.Length; slot++)
            {
                var material = GetMaterial(request.Owner, slot);
                if (request.Slots.Contains(slot) || (updateExisting && material != null && request.Slots.Any(selected => GetMaterial(request.Owner, selected) == material)))
                {
                    if (material == null || material.shader != request.SourceShader) return MaterialSlotLabels[slot] + " changed. Refresh and reopen Replace.";
                    if (updateExisting)
                    {
                        string path = AssetDatabase.GetAssetPath(material);
                        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || !AssetDatabase.IsMainAsset(material))
                            return "Update requires standalone Material assets under Assets. Use Create copies and replace for imported materials.";
                        if (checkEditable && !AssetDatabase.IsOpenForEdit(material, StatusQueryOptions.ForceUpdate))
                            return "Material " + material.name + " is read-only or not checked out.";
                    }
                    continue;
                }
                if (material == null) continue;
                for (int i = 0; i < channels.Length; i++)
                {
                    var mapping = request.Channels[i];
                    bool changed = mapping.Unused != channels[i].NonShaderTexture || (!mapping.Unused && mapping.Property != channels[i].materialPropertyName);
                    if (changed && mapping.Unused && !channels[i].NonShaderTexture && material.HasProperty(channels[i].materialPropertyName))
                        return "Channel " + i + " is still used by " + MaterialSlotLabels[slot] + ". Replace that pass too before marking the channel unused.";
                    if (changed && !mapping.Unused && !Properties(material.shader, channels[i]).Contains(mapping.Property))
                        return "Channel " + i + " also applies to " + MaterialSlotLabels[slot] + ", whose shader does not support " + mapping.Property + ". Keep a compatible mapping or replace that pass too.";
                }
            }
            return null;
        }

        public static List<Material> Replace(Replacement request, string folder)
            => ApplyReplacement(request, folder, false);

        public static List<Material> Update(Replacement request)
            => ApplyReplacement(request, null, true);

        private static List<Material> ApplyReplacement(Replacement request, string folder, bool updateExisting)
        {
            string error = updateExisting ? ValidateUpdate(request, true) : Validate(request);
            if (error != null) throw new InvalidOperationException(error);
            folder = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (!updateExisting && (!(folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal)) || !AssetDatabase.IsValidFolder(folder)))
                throw new InvalidOperationException("Choose an existing folder under Assets for the material copies.");
            if (!updateExisting && !AssetDatabase.IsOpenForEdit(folder, StatusQueryOptions.ForceUpdate)) throw new InvalidOperationException("The output folder is read-only or not checked out.");
            var owners = updateExisting ? new[] { request }.Concat(request.Shared).ToList() : new List<Replacement> { request };
            var copies = new Dictionary<Material, Material>();
            var createdPaths = new List<string>();
            var temporaryMaterials = new List<Material>();
            string undoName = updateExisting ? "Update UMA shader package materials" : "Replace UMA shader package";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            try
            {
                foreach (int slot in request.Slots)
                {
                    var source = GetMaterial(request.Owner, slot);
                    if (copies.ContainsKey(source)) continue;
                    var copy = new Material(source) { name = source.name + " (" + request.Owner.name + ")", hideFlags = HideFlags.None };
                    try
                    {
                        copy.shader = request.Shader;
                        copy.shaderKeywords = source.shaderKeywords.Where(k => request.Shader.keywordSpace.FindKeyword(k).isValid).ToArray();
                        foreach (var item in owners.Where(item => item.Slots.Any(selected => GetMaterial(item.Owner, selected) == source)))
                        for (int i = 0; i < item.Channels.Count; i++)
                        {
                            var mapping = item.Channels[i];
                            var channel = item.Owner.channels[i];
                            string oldName = channel.materialPropertyName;
                            if (mapping.Unused || channel.NonShaderTexture || string.IsNullOrEmpty(oldName) || !source.HasProperty(oldName)) continue;
                            int oldIndex = source.shader.FindPropertyIndex(oldName);
                            var oldType = source.shader.GetPropertyType(oldIndex);
                            if (channel.channelType == UMAMaterial.ChannelType.MaterialColor)
                            {
                                if (oldType == ShaderPropertyType.Color || oldType == ShaderPropertyType.Vector)
                                    copy.SetVector(mapping.Property, source.GetVector(oldName));
                            }
                            else if (oldType == ShaderPropertyType.Texture)
                            {
                                copy.SetTexture(mapping.Property, source.GetTexture(oldName));
                                copy.SetTextureScale(mapping.Property, source.GetTextureScale(oldName));
                                copy.SetTextureOffset(mapping.Property, source.GetTextureOffset(oldName));
                            }
                        }
                        MaterialEditor.ApplyMaterialPropertyDrawers(copy);
                        var editor = Editor.CreateEditor(copy) as MaterialEditor;
                        try { editor?.customShaderGUI?.ValidateMaterial(copy); }
                        finally { if (editor != null) Object.DestroyImmediate(editor); }
                        if (updateExisting)
                        {
                            copy.name = source.name;
                            copy.hideFlags = source.hideFlags;
                            temporaryMaterials.Add(copy);
                            copies.Add(source, copy);
                            continue;
                        }
                        string filename = string.Concat(copy.name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\' ? '_' : c));
                        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + filename + ".mat");
                        AssetDatabase.CreateAsset(copy, path);
                        createdPaths.Add(path);
                        copies.Add(source, copy);
                    }
                    catch
                    {
                        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(copy))) Object.DestroyImmediate(copy);
                        throw;
                    }
                }
                if (updateExisting)
                {
                    foreach (var source in copies.Keys.ToList())
                    {
                        Undo.RegisterCompleteObjectUndo(source, undoName);
                        EditorUtility.CopySerialized(copies[source], source);
                        EditorUtility.SetDirty(source);
                        copies[source] = source;
                    }
                }
                foreach (var item in owners)
                {
                    Undo.RegisterCompleteObjectUndo(item.Owner, undoName);
                    using (var serialized = new SerializedObject(item.Owner))
                    {
                        foreach (int slot in item.Slots)
                        {
                            var property = serialized.FindProperty(MaterialSlots[slot]);
                            property.objectReferenceValue = copies[(Material)property.objectReferenceValue];
                        }
                        var channels = serialized.FindProperty("channels");
                        for (int i = 0; i < item.Channels.Count; i++)
                        {
                            var channel = channels.GetArrayElementAtIndex(i);
                            channel.FindPropertyRelative("materialPropertyName").stringValue = item.Channels[i].Unused ? string.Empty : item.Channels[i].Property;
                            channel.FindPropertyRelative("NonShaderTexture").boolValue = item.Channels[i].Unused;
                        }
                        serialized.ApplyModifiedProperties();
                    }
                    if (copies.Values.Contains(item.Owner.material))
                    {
                        item.Owner.MaterialName = item.Owner.material.name;
                        item.Owner.ShaderName = item.Owner.material.shader.name;
                    }
                    EditorUtility.SetDirty(item.Owner);
                }
                foreach (var copy in copies.Values) AssetDatabase.SaveAssetIfDirty(copy);
                foreach (var item in owners) AssetDatabase.SaveAssetIfDirty(item.Owner);
                Undo.CollapseUndoOperations(undoGroup);
                UMAResourceReuse.InvalidateTextureInputs();
                return copies.Values.ToList();
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                foreach (var path in createdPaths) AssetDatabase.DeleteAsset(path);
                if (updateExisting)
                    foreach (var source in copies.Keys) AssetDatabase.SaveAssetIfDirty(source);
                foreach (var item in owners)
                    if (item.Owner != null) AssetDatabase.SaveAssetIfDirty(item.Owner);
                throw;
            }
            finally
            {
                foreach (var material in temporaryMaterials) Object.DestroyImmediate(material);
            }
        }
    }
}
