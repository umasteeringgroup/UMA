using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed class TexturePaintExportPlanEntry
    {
        public TextureSet set;
        public TexturePaintMaterialChannelCapability materialChannel;
        public TexturePaintLogicalTarget logicalTarget;
        public TexturePaintLogicalTargetMember member;
        public OverlayDataAsset sourceOverlay;
        public string targetName;
        public int tileNumber;
        public int materialChannelIndex;
        public int resolution;
        public string materialProperty;
        public string path;
        public string DisplayName => targetName + (tileNumber > 0 ? " / " + tileNumber : string.Empty) +
            " / " + TexturePaintExporter.MaterialChannelName(materialChannel);
    }

    public sealed class TexturePaintOverlayPlanEntry
    {
        public TextureSet set;
        public TexturePaintLogicalTarget logicalTarget;
        public TexturePaintLogicalTargetMember member;
        public OverlayDataAsset sourceOverlay;
        public string targetName;
        public int tileNumber;
        public string path;
        public string alphaMaskPath;
        public int alphaMaskResolution;
        public readonly List<TexturePaintExportPlanEntry> textures = new List<TexturePaintExportPlanEntry>();
        public string DisplayName => targetName + (tileNumber > 0 ? " / " + tileNumber : string.Empty) + " / Overlay";
    }

    public sealed class TexturePaintExportPlan
    {
        public string identifier;
        public readonly List<TexturePaintExportPlanEntry> entries = new List<TexturePaintExportPlanEntry>();
        public readonly List<TexturePaintOverlayPlanEntry> overlays = new List<TexturePaintOverlayPlanEntry>();
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public bool IsValid => entries.Count > 0 && overlays.Count > 0 && errors.Count == 0;
    }

    [Serializable]
    public sealed class TexturePaintExportResultSet
    {
        public string targetName;
        public int tileNumber;
        public string overlayPath;
        public string alphaMaskPath;
        public readonly List<string> texturePaths = new List<string>();
    }

    public sealed class TexturePaintExportResult
    {
        public string identifier;
        public readonly List<string> texturePaths = new List<string>();
        public readonly List<string> alphaMaskPaths = new List<string>();
        public readonly List<string> overlayPaths = new List<string>();
        // Retained for source compatibility. Phase 4 never creates material overrides.
        public readonly List<string> materialPaths = new List<string>();
        public readonly List<TexturePaintExportResultSet> resultSets = new List<TexturePaintExportResultSet>();
        public int TextureCount => texturePaths.Count;
    }

    public static class TexturePaintExporter
    {
        private sealed class MemberBinding
        {
            public TexturePaintLogicalTarget target;
            public TexturePaintLogicalTargetMember member;
            public OverlayDataAsset sourceOverlay;
            public string targetName;
            public int tileNumber;
        }

        private sealed class EncodedOutput
        {
            public TexturePaintExportPlanEntry entry;
            public TexturePaintOverlayPlanEntry overlay;
            public string path;
            public string displayName;
            public string stagingPath;
            public bool IsAlphaMask => overlay != null;
        }

        private sealed class ObjectSnapshot
        {
            public UnityEngine.Object target;
            public string json;
        }

        private sealed class FileSnapshot
        {
            public string assetPath;
            public byte[] bytes;
            public string importerJson;
        }

        private sealed class AuthoredOverlayBakeContext : IDisposable
        {
            private const string ShaderPath =
                "Assets/UMA/OverlayPainter/Shaders/ExportOverlayPack.shader";
            private readonly TextureSet set;
            private readonly Material material;
            private readonly Dictionary<TexturePaintChannel, RenderTexture> logical =
                new Dictionary<TexturePaintChannel, RenderTexture>();
            public RenderTexture Coverage { get; }

            public AuthoredOverlayBakeContext(TextureSet set, int coverageResolution)
            {
                this.set = set ?? throw new ArgumentNullException(nameof(set));
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
                if (shader == null) throw new InvalidOperationException(
                    "Overlay-only export shader is missing: " + ShaderPath);
                if (set.compositor?.IsAvailable != true) throw new InvalidOperationException(
                    set.Name + " cannot export authored layers because GPU layer compositing is unavailable.");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

                foreach (KeyValuePair<TexturePaintChannel, TextureChannelTarget> pair in set.channels)
                {
                    TextureChannelTarget target = pair.Value;
                    if (target?.editable?.Front == null ||
                        !HasVisibleAuthoredContribution(set, pair.Key)) continue;
                    RenderTexture composite = EditableTextureTarget.Create(
                        set.Name + " " + pair.Key + " Authored Overlay",
                        target.editable.Width, target.editable.Height, target.format);
                    set.compositor.ComposeAuthoredLayers(set, pair.Key, composite);
                    logical.Add(pair.Key, composite);
                }
                if (logical.TryGetValue(TexturePaintChannel.NormalControl,
                        out RenderTexture authoredControl))
                {
                    if (set.normalControlStrength <= 0.00001f)
                    {
                        Destroy(authoredControl);
                        logical.Remove(TexturePaintChannel.NormalControl);
                    }
                    else
                    {
                        TextureChannelTarget normalTarget = set.GetChannel(TexturePaintChannel.Normal);
                        if (normalTarget?.editable?.Front == null) throw new InvalidOperationException(
                            set.Name + " has Normal Control content but no normal target.");
                        bool ownsFlatBase = !logical.TryGetValue(TexturePaintChannel.Normal,
                            out RenderTexture authoredNormal);
                        if (ownsFlatBase)
                        {
                            authoredNormal = EditableTextureTarget.Create(set.Name + " Authored Flat Normal",
                                normalTarget.editable.Width, normalTarget.editable.Height, normalTarget.format);
                            Clear(authoredNormal, new Color(0.5f, 0.5f, 1f, 0f));
                        }
                        RenderTexture effective = EditableTextureTarget.Create(
                            set.Name + " Authored Effective Normal", normalTarget.editable.Width,
                            normalTarget.editable.Height, normalTarget.format);
                        if (!set.ApplyNormalControl(authoredNormal, authoredControl, effective, true))
                        {
                            Destroy(effective);
                            if (ownsFlatBase) Destroy(authoredNormal);
                            throw new InvalidOperationException(
                                set.Name + " could not derive an authored Normal Control overlay.");
                        }
                        Destroy(authoredNormal);
                        logical[TexturePaintChannel.Normal] = effective;
                    }
                }
                if (logical.Count == 0) throw new InvalidOperationException(
                    set.Name + " has no visible authored layer content to export.");

                Coverage = EditableTextureTarget.Create(set.Name + " Authored Overlay Coverage",
                    coverageResolution, coverageResolution, RenderTextureFormat.ARGB32);
                Clear(Coverage, Color.clear);
                foreach (RenderTexture source in logical.Values)
                    Graphics.Blit(source, Coverage, material, 1);
            }

            public Texture2D Bake(TexturePaintExportPlanEntry entry, TexturePaintExportBitDepth bitDepth)
            {
                TexturePaintMaterialChannelCapability capability = entry.materialChannel;
                RenderTextureFormat format = bitDepth == TexturePaintExportBitDepth.HalfFloat
                    ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                RenderTexture packed = EditableTextureTarget.Create(
                    entry.DisplayName + " Authored Overlay", entry.resolution, entry.resolution, format);
                try
                {
                    Vector4 defaults = Vector4.zero;
                    Vector4 hasSource = Vector4.zero;
                    Vector4 sourceComponents = Vector4.zero;
                    Vector4 invert = Vector4.zero;
                    string[] names = { "_Red", "_Green", "_Blue", "_Alpha" };
                    for (int component = 0; component < 4; component++)
                    {
                        TexturePaintPhysicalComponentCapability mapping = capability.Components[component];
                        defaults[component] = mapping?.neutralValue ?? 0f;
                        RenderTexture source = null;
                        if (mapping?.editable == true)
                            logical.TryGetValue(mapping.logicalChannel, out source);
                        if (source != null)
                        {
                            hasSource[component] = 1f;
                            sourceComponents[component] = IsVectorChannel(mapping.logicalChannel)
                                ? component : 0;
                            invert[component] = mapping.invert ? 1f : 0f;
                        }
                        material.SetTexture(names[component], source != null
                            ? (Texture)source : Texture2D.blackTexture);
                    }
                    material.SetVector("_Defaults", defaults);
                    material.SetVector("_HasSource", hasSource);
                    material.SetVector("_SourceComponent", sourceComponents);
                    material.SetVector("_Invert", invert);
                    material.SetTexture("_Coverage", Coverage);
                    material.SetInt("_AlphaFromCoverage", CanStoreCoverageInPhysicalAlpha(capability) ? 1 : 0);
                    Graphics.Blit(Texture2D.blackTexture, packed, material, 0);
                    bool linear = capability.output.colorSpace !=
                        UMAMaterial.TextureChannelColorSpace.SRGB;
                    return TexturePaintBaker.BakeRenderTexture(packed, entry.DisplayName,
                        0, bitDepth, linear);
                }
                finally { Destroy(packed); }
            }

            public Texture2D BakeCoverage(string name)
            {
                return TexturePaintBaker.BakeRenderTexture(Coverage, name, 0,
                    TexturePaintExportBitDepth.Eight, true);
            }

            public void Dispose()
            {
                foreach (RenderTexture texture in logical.Values) Destroy(texture);
                logical.Clear();
                Destroy(Coverage);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }

            private static bool IsVectorChannel(TexturePaintChannel channel)
            {
                return TexturePaintChannelUtility.IsVector(channel);
            }

            private static bool CanStoreCoverageInPhysicalAlpha(
                TexturePaintMaterialChannelCapability capability)
            {
                if (!ContainsLogicalChannel(capability, TexturePaintChannel.Albedo)) return false;
                UMAMaterial.TextureChannelUsage usage = capability.layout.alpha;
                return usage == UMAMaterial.TextureChannelUsage.Unused ||
                       usage == UMAMaterial.TextureChannelUsage.Opacity;
            }

            private static void Clear(RenderTexture texture, Color color)
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = texture;
                GL.Clear(false, true, color);
                RenderTexture.active = previous;
            }

            private static void Destroy(RenderTexture texture)
            {
                if (texture == null) return;
                if (RenderTexture.active == texture) RenderTexture.active = null;
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        public static TexturePaintExportPlan BuildPlan(TextureStore store, TextureSet current,
            string contextName, TexturePaintExportTemplate template)
        {
            return BuildPlan(store, current, contextName, template, "Export", null);
        }

        public static TexturePaintExportPlan BuildPlan(TextureStore store, TextureSet current,
            string contextName, TexturePaintExportTemplate template, string identifier,
            TexturePaintLogicalTargetCatalog targets)
        {
            TexturePaintExportPlan plan = new TexturePaintExportPlan();
            if (store == null) { plan.errors.Add("No texture store is available."); return plan; }
            if (template == null) { plan.errors.Add("Select an export template."); return plan; }
            template.Migrate();
            if (template.content == TexturePaintExportContent.AuthoredOverlay &&
                template.overwriteSourceOverlay)
            {
                plan.errors.Add("Authored Overlay export cannot overwrite a source overlay. " +
                    "Choose a new/versioned output so the character's base textures remain intact.");
                return plan;
            }
            if (string.IsNullOrWhiteSpace(identifier))
            {
                plan.errors.Add("Enter an Export Identifier.");
                return plan;
            }

            plan.identifier = Sanitize(identifier);
            if (!string.Equals(plan.identifier, identifier.Trim(), StringComparison.Ordinal))
                plan.warnings.Add($"The Export Identifier will be sanitized to '{plan.identifier}'.");
            string folder = NormalizeAssetFolder(template.outputFolder);
            List<TextureSet> sets = SelectSets(store, current, template, targets, plan);
            HashSet<string> reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int setIndex = 0; setIndex < sets.Count; setIndex++)
            {
                TextureSet set = sets[setIndex];
                TexturePaintMaterialCapabilityDescriptor descriptor = set.materialCapability;
                if (descriptor == null)
                {
                    plan.errors.Add(set.Name + " has no compiled UMAMaterial channel descriptor.");
                    continue;
                }
                AppendDiagnostics(plan, set, descriptor);
                if (!descriptor.IsSupported || set.umaMaterial?.channels == null) continue;

                List<MemberBinding> bindings = ResolveMemberBindings(set, targets);
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    MemberBinding binding = bindings[bindingIndex];
                    TexturePaintOverlayPlanEntry overlay = new TexturePaintOverlayPlanEntry
                    {
                        set = set,
                        logicalTarget = binding.target,
                        member = binding.member,
                        sourceOverlay = binding.sourceOverlay,
                        targetName = binding.targetName,
                        tileNumber = binding.tileNumber
                    };
                    for (int channelIndex = 0; channelIndex < descriptor.Channels.Count; channelIndex++)
                    {
                        TexturePaintMaterialChannelCapability channel = descriptor.Channels[channelIndex];
                        if (!channel.isTexture) continue;
                        if (template.content == TexturePaintExportContent.AuthoredOverlay &&
                            !HasVisibleAuthoredContribution(set, channel)) continue;
                        TexturePaintExportPlanEntry entry = BuildTextureEntry(plan, reserved, folder, template,
                            set, binding, channel);
                        if (entry == null) continue;
                        plan.entries.Add(entry);
                        overlay.textures.Add(entry);
                    }
                    if (overlay.textures.Count == 0)
                    {
                        plan.errors.Add(template.content == TexturePaintExportContent.AuthoredOverlay
                            ? $"{binding.targetName} has no visible authored layer content to export. " +
                              "Runtime Overlay export excludes the reconstructed base texture and direct base painting."
                            : $"{binding.targetName} has no physical UMAMaterial texture channels to export.");
                        continue;
                    }
                    overlay.path = ResolveOverlayPath(plan, reserved, folder, template, binding);
                    if (!string.IsNullOrEmpty(overlay.path))
                    {
                        if (template.content == TexturePaintExportContent.AuthoredOverlay)
                        {
                            overlay.alphaMaskResolution = ResolveOverlayMaskResolution(overlay, template);
                            overlay.alphaMaskPath = ResolveAlphaMaskPath(plan, reserved, folder, template,
                                binding);
                        }
                        PreflightOverlayIndex(plan, overlay.path);
                        if (template.content != TexturePaintExportContent.AuthoredOverlay ||
                            !string.IsNullOrEmpty(overlay.alphaMaskPath)) plan.overlays.Add(overlay);
                    }
                }
            }

            if (template.exportLogicalChannels || template.customPacking?.Exists(rule => rule != null && rule.enabled) == true)
                plan.warnings.Add("Logical/custom diagnostic outputs are ignored by the Phase 4 release export. " +
                    "Only descriptor-ordered physical UMAMaterial channels are written.");
            if (template.createMaterialOverride || template.updateRecipeReferences)
                plan.warnings.Add("Material overrides and implicit recipe/avatar updates are disabled for release export.");
            return plan;
        }

        public static TexturePaintExportResult Export(TextureStore store, TextureSet current,
            DynamicCharacterAvatar avatar, TexturePaintExportTemplate template, TexturePaintStageState state,
            TexturePaintOperationContext operation = default)
        {
            return Export(store, current, avatar, template, state, "Export", null, false, operation, null);
        }

        public static TexturePaintExportResult Export(TextureStore store, TextureSet current,
            DynamicCharacterAvatar avatar, TexturePaintExportTemplate template, TexturePaintStageState state,
            string identifier, TexturePaintLogicalTargetCatalog targets, bool overwriteConfirmed,
            TexturePaintOperationContext operation = default, Action<string, float> detailedProgress = null)
        {
            _ = state; // Export history intentionally does not live in the paint document/editor state.
            string contextName = avatar != null ? avatar.name : current?.surface?.slotName ?? "TexturePaint";
            TexturePaintExportPlan plan = BuildPlan(store, current, contextName, template, identifier, targets);
            if (!plan.IsValid) throw new InvalidOperationException(string.Join("\n", plan.errors));
            if (template.overwriteSourceOverlay && !overwriteConfirmed)
                throw new InvalidOperationException("Overwrite Source Overlay requires explicit confirmation.");

            operation.ThrowIfCancellationRequested();
            string stagingFolder = Path.Combine(Path.GetTempPath(), "UMA_TexturePaintExport",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingFolder);
            List<EncodedOutput> encoded = new List<EncodedOutput>();
            try
            {
                Encode(plan, template, stagingFolder, encoded, operation, detailedProgress);
                return Commit(plan, template, encoded, operation, detailedProgress);
            }
            finally
            {
                try { if (Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, true); }
                catch (Exception exception) { Debug.LogWarning("Texture export staging cleanup failed: " + exception.Message); }
            }
        }

        // Legacy entry points remain source-compatible and produce physical textures only.
        public static void ExportAll(TextureStore store, string folder, bool addressable, TexturePaintStageState state)
        {
            TexturePaintExportTemplate template = ScriptableObject.CreateInstance<TexturePaintExportTemplate>();
            template.outputFolder = folder;
            template.markAddressable = addressable;
            template.scope = TexturePaintExportScope.AllMaterials;
            try { Export(store, null, null, template, state); }
            finally { UnityEngine.Object.DestroyImmediate(template); }
        }

        public static void Export(TextureSet set, string folder, bool addressable, TexturePaintStageState state,
            bool refresh = true)
        {
            if (set == null) return;
            TextureStore view = new TextureStore();
            FieldInfo field = typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic);
            List<TextureSet> sets = (List<TextureSet>)field.GetValue(view);
            sets.Add(set);
            TexturePaintExportTemplate template = ScriptableObject.CreateInstance<TexturePaintExportTemplate>();
            template.outputFolder = folder;
            template.markAddressable = addressable;
            template.scope = TexturePaintExportScope.CurrentMaterial;
            try { Export(view, set, null, template, state); }
            finally { sets.Clear(); view.Dispose(); UnityEngine.Object.DestroyImmediate(template); }
        }

        public static string CreateDefaultIdentifier(string sessionName, DateTime now)
        {
            string prefix = Sanitize(string.IsNullOrWhiteSpace(sessionName) ? "TexturePaint" : sessionName);
            return prefix + "_" + now.ToString("yyyyMMdd_HHmmss");
        }

        internal static string MaterialChannelName(TexturePaintMaterialChannelCapability channel)
        {
            if (channel == null) return "Channel";
            string value = !string.IsNullOrWhiteSpace(channel.sourceTextureName)
                ? channel.sourceTextureName
                : channel.materialProperty;
            return string.IsNullOrWhiteSpace(value) ? "Channel" + channel.index : value.TrimStart('_');
        }

        private static List<TextureSet> SelectSets(TextureStore store, TextureSet current,
            TexturePaintExportTemplate template, TexturePaintLogicalTargetCatalog targets,
            TexturePaintExportPlan plan)
        {
            List<TextureSet> result = new List<TextureSet>();
            if (template.scope == TexturePaintExportScope.CurrentMaterial)
            {
                if (current == null)
                {
                    plan.errors.Add("No current material is selected.");
                    return result;
                }
                TexturePaintLogicalTarget logical = FindLogicalTarget(targets, current);
                if (logical?.isUdim == true)
                {
                    for (int memberIndex = 0; memberIndex < logical.members.Count; memberIndex++)
                    for (int setIndex = 0; setIndex < logical.members[memberIndex].textureSets.Count; setIndex++)
                    {
                        TextureSet memberSet = logical.members[memberIndex].textureSets[setIndex];
                        if (memberSet != null && ReferenceEquals(memberSet.umaMaterial, current.umaMaterial) &&
                            !result.Contains(memberSet)) result.Add(memberSet);
                    }
                }
                else
                {
                    string udimGroup = UdimGroupId(current);
                    if (!string.IsNullOrEmpty(udimGroup))
                    {
                        for (int i = 0; i < store.Sets.Count; i++)
                        {
                            TextureSet candidate = store.Sets[i];
                            if (candidate != null && ReferenceEquals(candidate.umaMaterial, current.umaMaterial) &&
                                string.Equals(UdimGroupId(candidate), udimGroup, StringComparison.Ordinal) &&
                                !result.Contains(candidate)) result.Add(candidate);
                        }
                    }
                }
                if (result.Count == 0) result.Add(current);
            }
            else
                for (int i = 0; i < store.Sets.Count; i++)
                    if (store.Sets[i] != null) result.Add(store.Sets[i]);
            return result;
        }

        private static TexturePaintLogicalTarget FindLogicalTarget(TexturePaintLogicalTargetCatalog targets,
            TextureSet set)
        {
            if (targets == null || set == null) return null;
            for (int targetIndex = 0; targetIndex < targets.Targets.Count; targetIndex++)
            {
                TexturePaintLogicalTarget target = targets.Targets[targetIndex];
                for (int memberIndex = 0; memberIndex < target.members.Count; memberIndex++)
                    if (target.members[memberIndex].textureSets.Contains(set)) return target;
            }
            return null;
        }

        private static string UdimGroupId(TextureSet set)
        {
            for (int i = 0; set?.surface?.slots != null && i < set.surface.slots.Count; i++)
            {
                SlotDataAsset asset = set.surface.slots[i]?.asset;
                if (asset?.IsUdimMember == true) return asset.udimGroupId;
            }
            return null;
        }

        private static void AppendDiagnostics(TexturePaintExportPlan plan, TextureSet set,
            TexturePaintMaterialCapabilityDescriptor descriptor)
        {
            for (int i = 0; i < descriptor.Diagnostics.Count; i++)
            {
                TexturePaintCapabilityDiagnostic diagnostic = descriptor.Diagnostics[i];
                string message = $"{set.Name} [{diagnostic.code}]: {diagnostic.message}";
                if (diagnostic.severity == TexturePaintCapabilitySeverity.Error) plan.errors.Add(message);
                else if (diagnostic.severity == TexturePaintCapabilitySeverity.Warning) plan.warnings.Add(message);
            }
        }

        private static List<MemberBinding> ResolveMemberBindings(TextureSet set,
            TexturePaintLogicalTargetCatalog catalog)
        {
            List<MemberBinding> result = new List<MemberBinding>();
            if (catalog != null)
            {
                for (int targetIndex = 0; targetIndex < catalog.Targets.Count; targetIndex++)
                {
                    TexturePaintLogicalTarget target = catalog.Targets[targetIndex];
                    for (int memberIndex = 0; memberIndex < target.members.Count; memberIndex++)
                    {
                        TexturePaintLogicalTargetMember member = target.members[memberIndex];
                        if (!member.textureSets.Contains(set)) continue;
                        result.Add(new MemberBinding
                        {
                            target = target,
                            member = member,
                            targetName = string.IsNullOrWhiteSpace(target.displayName) ? member.slotName : target.displayName,
                            tileNumber = target.isUdim ? member.udimTileNumber : 0,
                            sourceOverlay = ResolveSourceOverlay(set, member)
                        });
                    }
                }
            }
            if (result.Count > 0) return result;

            HashSet<string> slots = new HashSet<string>(StringComparer.Ordinal);
            if (set.surface?.slots != null)
            {
                for (int i = 0; i < set.surface.slots.Count; i++)
                {
                    SlotData slot = set.surface.slots[i];
                    if (slot == null || !slots.Add(slot.slotName)) continue;
                    SlotDataAsset asset = slot.asset;
                    bool udim = asset != null && asset.IsUdimMember;
                    result.Add(new MemberBinding
                    {
                        targetName = udim
                            ? (!string.IsNullOrWhiteSpace(asset.udimGroupName) ? asset.udimGroupName : asset.udimGroupId)
                            : slot.slotName,
                        tileNumber = udim ? asset.udimTileNumber : 0,
                        sourceOverlay = set.surface.standaloneSourceOverlay
                    });
                }
            }
            if (result.Count == 0)
                result.Add(new MemberBinding
                {
                    targetName = !string.IsNullOrWhiteSpace(set.surface?.slotName) ? set.surface.slotName : set.Name,
                    sourceOverlay = set.surface?.standaloneSourceOverlay
                });
            return result;
        }

        private static OverlayDataAsset ResolveSourceOverlay(TextureSet set,
            TexturePaintLogicalTargetMember member)
        {
            if (set.surface?.standaloneSourceOverlay != null) return set.surface.standaloneSourceOverlay;
            for (int i = 0; member?.sourceOverlays != null && i < member.sourceOverlays.Count; i++)
                if (member.sourceOverlays[i]?.asset != null) return member.sourceOverlays[i].asset;
            for (int i = 0; member?.destinationOverlays != null && i < member.destinationOverlays.Count; i++)
                if (member.destinationOverlays[i]?.asset != null) return member.destinationOverlays[i].asset;
            return null;
        }

        private static TexturePaintExportPlanEntry BuildTextureEntry(TexturePaintExportPlan plan,
            HashSet<string> reserved, string folder, TexturePaintExportTemplate template, TextureSet set,
            MemberBinding binding, TexturePaintMaterialChannelCapability channel)
        {
            if (channel.requiresPacking && (string.IsNullOrEmpty(channel.materialProperty) ||
                !set.physicalChannelGroups.ContainsKey(channel.materialProperty)))
            {
                plan.errors.Add($"{binding.targetName} material channel {channel.index} " +
                    $"('{MaterialChannelName(channel)}') requires physical packing, but its packed preview " +
                    "resource is unavailable.");
                return null;
            }
            int resolution = template.resolution > 0 ? template.resolution :
                channel.outputWidth > 0 ? Mathf.Max(channel.outputWidth, channel.outputHeight) :
                channel.workingWidth > 0 ? Mathf.Max(channel.workingWidth, channel.workingHeight) : FirstResolution(set);
            resolution = Mathf.Clamp(resolution, 1, 8192);
            string extension = channel.output.encoding == UMAMaterial.TextureChannelOutputEncoding.ExrHalf
                ? ".exr" : ".png";
            string path;
            if (template.overwriteSourceOverlay)
            {
                if (!TryGetPersistentSourceTexture(binding.sourceOverlay, channel.index, out Texture source,
                    out path))
                {
                    plan.errors.Add($"{binding.targetName} cannot overwrite material channel {channel.index}: " +
                        "the selected source overlay does not contain a persistent texture at that channel.");
                    return null;
                }
                if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                {
                    plan.errors.Add($"Cannot overwrite '{path}': the UMAMaterial requires {extension} output.");
                    return null;
                }
                if (!reserved.Add(path))
                {
                    plan.errors.Add("The overwrite source reuses one texture in multiple output channels: " + path);
                    return null;
                }
                _ = source;
            }
            else
            {
                string stem = PersistentTextureStem(channel.sourceTexture);
                if (string.IsNullOrEmpty(stem))
                    stem = Sanitize(binding.targetName) + "_" + Sanitize(MaterialChannelName(channel));
                string filename = stem + "_" + plan.identifier +
                    (binding.tileNumber > 0 ? "_" + binding.tileNumber : string.Empty) + extension;
                path = ResolveOutputPath(folder + "/" + filename, template.overwritePolicy, reserved,
                    plan.errors);
                if (string.IsNullOrEmpty(path)) return null;
                reserved.Add(path);
            }
            return new TexturePaintExportPlanEntry
            {
                set = set,
                materialChannel = channel,
                logicalTarget = binding.target,
                member = binding.member,
                sourceOverlay = binding.sourceOverlay,
                targetName = binding.targetName,
                tileNumber = binding.tileNumber,
                materialChannelIndex = channel.index,
                resolution = resolution,
                materialProperty = channel.materialProperty,
                path = path
            };
        }

        private static string ResolveOverlayPath(TexturePaintExportPlan plan, HashSet<string> reserved,
            string folder, TexturePaintExportTemplate template, MemberBinding binding)
        {
            if (template.overwriteSourceOverlay)
            {
                string sourcePath = AssetDatabase.GetAssetPath(binding.sourceOverlay);
                if (binding.sourceOverlay == null || string.IsNullOrEmpty(sourcePath))
                {
                    plan.errors.Add(binding.targetName +
                        " cannot use Overwrite Source Overlay because its source overlay is not a persistent asset.");
                    return null;
                }
                if (!reserved.Add(sourcePath))
                {
                    plan.errors.Add("The same source overlay is assigned to more than one export member: " + sourcePath);
                    return null;
                }
                return sourcePath;
            }

            string filename = Sanitize(binding.targetName) + "_" + plan.identifier +
                (binding.tileNumber > 0 ? "_" + binding.tileNumber : string.Empty) + "_Overlay.asset";
            string path = ResolveOverlayOutputPath(folder + "/" + filename, template.overwritePolicy,
                reserved, plan.errors);
            if (string.IsNullOrEmpty(path)) return null;
            reserved.Add(path);
            return path;
        }

        private static string ResolveAlphaMaskPath(TexturePaintExportPlan plan,
            HashSet<string> reserved, string folder, TexturePaintExportTemplate template,
            MemberBinding binding)
        {
            string filename = Sanitize(binding.targetName) + "_" + plan.identifier +
                (binding.tileNumber > 0 ? "_" + binding.tileNumber : string.Empty) + "_AlphaMask.png";
            string path = ResolveOutputPath(folder + "/" + filename, template.overwritePolicy,
                reserved, plan.errors);
            if (string.IsNullOrEmpty(path)) return null;
            reserved.Add(path);
            return path;
        }

        private static int ResolveOverlayMaskResolution(TexturePaintOverlayPlanEntry overlay,
            TexturePaintExportTemplate template)
        {
            if (template.resolution > 0) return Mathf.Clamp(template.resolution, 1, 8192);
            int resolution = 0;
            for (int i = 0; i < overlay.textures.Count; i++)
                resolution = Mathf.Max(resolution, overlay.textures[i].resolution);
            return Mathf.Clamp(resolution > 0 ? resolution : FirstResolution(overlay.set), 1, 8192);
        }

        private static bool HasVisibleAuthoredContribution(TextureSet set,
            TexturePaintMaterialChannelCapability capability)
        {
            for (int i = 0; i < capability.LogicalChannels.Count; i++)
            {
                TexturePaintChannel logical = capability.LogicalChannels[i];
                if (HasVisibleAuthoredContribution(set, logical)) return true;
                if (logical == TexturePaintChannel.Normal && set.normalControlStrength > 0.00001f &&
                    HasVisibleAuthoredContribution(set, TexturePaintChannel.NormalControl)) return true;
            }
            return false;
        }

        private static bool HasVisibleAuthoredContribution(TextureSet set, TexturePaintChannel channel)
        {
            if (set == null) return false;
            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer layer = set.layers[i];
                if (layer == null || layer.kind == TexturePaintLayerKind.Group || !layer.visible ||
                    layer.opacity <= 0f || !layer.channels.ContainsKey(channel)) continue;
                TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(channel, false);
                if (settings != null && (!settings.enabled || settings.opacity <= 0f)) continue;
                if (VisibleThroughParents(set, layer)) return true;
            }
            return false;
        }

        private static bool VisibleThroughParents(TextureSet set, TexturePaintLayer layer)
        {
            string parentId = layer.parentId;
            int guard = 0;
            while (!string.IsNullOrEmpty(parentId) && guard++ < set.layers.Count)
            {
                TexturePaintLayer parent = null;
                for (int i = 0; i < set.layers.Count; i++)
                    if (string.Equals(set.layers[i]?.id, parentId, StringComparison.Ordinal))
                    {
                        parent = set.layers[i];
                        break;
                    }
                if (parent == null) break;
                if (!parent.visible || parent.opacity <= 0f) return false;
                parentId = parent.parentId;
            }
            return true;
        }

        private static void PreflightOverlayIndex(TexturePaintExportPlan plan, string path)
        {
            OverlayDataAsset asset = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(path);
            string name = asset != null ? asset.overlayName : Path.GetFileNameWithoutExtension(path);
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            AssetItem existing = indexer?.GetAssetItem<OverlayDataAsset>(name);
            if (existing != null && (asset == null || !ReferenceEquals(existing.Item, asset)))
                plan.errors.Add($"UMA library already contains a different OverlayDataAsset named '{name}'. " +
                    "Choose another Export Identifier or resolve the library conflict before exporting.");
        }

        private static string ResolveOverlayOutputPath(string requested,
            TexturePaintOverwritePolicy policy, HashSet<string> reserved, List<string> errors)
        {
            string directory = Path.GetDirectoryName(requested)?.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(requested);
            string extension = Path.GetExtension(requested);
            for (int version = 0; version < 10000; version++)
            {
                string candidate = version == 0 ? requested : directory + "/" + name + "_v" +
                    version.ToString("D3") + extension;
                OverlayDataAsset asset = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(candidate);
                string candidateName = asset != null ? asset.overlayName :
                    Path.GetFileNameWithoutExtension(candidate);
                AssetItem indexed = UMAAssetIndexer.Instance?.GetAssetItem<OverlayDataAsset>(candidateName);
                bool indexConflict = indexed != null && (asset == null || !ReferenceEquals(indexed.Item, asset));
                bool occupied = File.Exists(Path.GetFullPath(candidate)) || asset != null ||
                                reserved.Contains(candidate) || indexConflict;
                if (!occupied || policy == TexturePaintOverwritePolicy.Overwrite && version == 0)
                    return candidate;
                if (policy == TexturePaintOverwritePolicy.Fail)
                {
                    errors.Add(indexConflict
                        ? "UMA library overlay name already exists: " + candidateName
                        : "Output already exists: " + candidate);
                    return null;
                }
            }
            errors.Add("Could not allocate a versioned overlay path for " + requested);
            return null;
        }

        private static bool TryGetPersistentSourceTexture(OverlayDataAsset overlay, int channelIndex,
            out Texture texture, out string path)
        {
            texture = overlay?.textureList != null && channelIndex >= 0 && channelIndex < overlay.textureList.Length
                ? overlay.textureList[channelIndex] : null;
            path = texture != null ? AssetDatabase.GetAssetPath(texture) : string.Empty;
            return texture != null && !string.IsNullOrEmpty(path);
        }

        private static void Encode(TexturePaintExportPlan plan, TexturePaintExportTemplate template,
            string stagingFolder, List<EncodedOutput> outputs, TexturePaintOperationContext operation,
            Action<string, float> detailedProgress)
        {
            Dictionary<TextureSet, AuthoredOverlayBakeContext> overlayContexts =
                new Dictionary<TextureSet, AuthoredOverlayBakeContext>();
            try
            {
                if (template.content == TexturePaintExportContent.AuthoredOverlay)
                {
                    for (int i = 0; i < plan.overlays.Count; i++)
                    {
                        TexturePaintOverlayPlanEntry overlay = plan.overlays[i];
                        if (!overlayContexts.ContainsKey(overlay.set))
                            overlayContexts.Add(overlay.set,
                                new AuthoredOverlayBakeContext(overlay.set, overlay.alphaMaskResolution));
                    }
                }

                for (int i = 0; i < plan.entries.Count; i++)
                {
                    operation.ThrowIfCancellationRequested();
                    TexturePaintExportPlanEntry entry = plan.entries[i];
                    float entryStart = 0.45f * i / Mathf.Max(1, plan.entries.Count);
                    float entrySpan = 0.45f / Mathf.Max(1, plan.entries.Count);
                    Report(operation, detailedProgress,
                        $"Baking {entry.DisplayName} at {entry.resolution} x {entry.resolution}", entryStart);
                    TexturePaintExportBitDepth bitDepth = ToBitDepth(entry.materialChannel.output.encoding);
                    Texture2D texture = template.content == TexturePaintExportContent.AuthoredOverlay
                        ? overlayContexts[entry.set].Bake(entry, bitDepth)
                        : TexturePaintBaker.Bake(entry.set, entry.materialChannel,
                            entry.resolution, bitDepth);
                    if (texture == null)
                        throw new InvalidOperationException("Could not bake " + entry.DisplayName + ".");
                    try
                    {
                        Report(operation, detailedProgress, $"Preparing {entry.DisplayName}",
                            entryStart + entrySpan * 0.12f);
                        bool invertNormalGreen = IsNormalChannel(entry.materialChannel) &&
                            entry.materialChannel.output.normalConvention ==
                            UMAMaterial.TextureChannelNormalConvention.DirectX;
                        bool linear = entry.materialChannel.output.colorSpace !=
                            UMAMaterial.TextureChannelColorSpace.SRGB;
                        ApplyDeclaredOutputTransform(texture, entry.materialChannel, invertNormalGreen, linear,
                            operation, template.content == TexturePaintExportContent.AuthoredOverlay);
                        if (ContainsLogicalChannel(entry.materialChannel, TexturePaintChannel.Albedo) &&
                            template.padding > 0)
                        {
                            DilateTransparent(texture, template.padding, linear, operation, (pass, passCount) =>
                                Report(operation, detailedProgress,
                                    $"Padding {entry.DisplayName} ({pass}/{passCount})",
                                    entryStart + entrySpan * (0.18f + 0.54f * pass / Mathf.Max(1f, passCount))));
                        }
                        Report(operation, detailedProgress, $"Encoding {entry.DisplayName}",
                            entryStart + entrySpan * 0.78f);
                        byte[] bytes = bitDepth == TexturePaintExportBitDepth.HalfFloat
                            ? texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP)
                            : texture.EncodeToPNG();
                        operation.ThrowIfCancellationRequested();
                        string stagingPath = Path.Combine(stagingFolder, i.ToString("D4") +
                            Path.GetExtension(entry.path));
                        File.WriteAllBytes(stagingPath, bytes);
                        outputs.Add(new EncodedOutput
                        {
                            entry = entry,
                            path = entry.path,
                            displayName = entry.DisplayName,
                            stagingPath = stagingPath
                        });
                    }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                    Report(operation, detailedProgress, $"Prepared {entry.DisplayName}", entryStart + entrySpan);
                }

                if (template.content != TexturePaintExportContent.AuthoredOverlay) return;
                for (int i = 0; i < plan.overlays.Count; i++)
                {
                    operation.ThrowIfCancellationRequested();
                    TexturePaintOverlayPlanEntry overlay = plan.overlays[i];
                    string displayName = overlay.targetName +
                        (overlay.tileNumber > 0 ? " / " + overlay.tileNumber : string.Empty) +
                        " / Alpha Mask";
                    Report(operation, detailedProgress, "Generating " + displayName,
                        0.45f + 0.05f * i / Mathf.Max(1, plan.overlays.Count));
                    Texture2D mask = overlayContexts[overlay.set].BakeCoverage(displayName);
                    try
                    {
                        string stagingPath = Path.Combine(stagingFolder,
                            (plan.entries.Count + i).ToString("D4") + ".png");
                        File.WriteAllBytes(stagingPath, mask.EncodeToPNG());
                        outputs.Add(new EncodedOutput
                        {
                            overlay = overlay,
                            path = overlay.alphaMaskPath,
                            displayName = displayName,
                            stagingPath = stagingPath
                        });
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mask); }
                }
            }
            finally
            {
                foreach (AuthoredOverlayBakeContext context in overlayContexts.Values) context.Dispose();
            }
        }

        private static TexturePaintExportResult Commit(TexturePaintExportPlan plan,
            TexturePaintExportTemplate template, List<EncodedOutput> encoded,
            TexturePaintOperationContext operation, Action<string, float> detailedProgress)
        {
            string outputFolder = NormalizeAssetFolder(template.outputFolder);
            bool outputFolderExisted = template.overwriteSourceOverlay ||
                                       AssetDatabase.IsValidFolder(outputFolder);
            List<FileSnapshot> replacedFiles = new List<FileSnapshot>();
            List<string> newFiles = new List<string>();
            List<string> newAssets = new List<string>();
            List<ObjectSnapshot> objectSnapshots = new List<ObjectSnapshot>();
            List<string> registeredOverlayNames = new List<string>();
            List<string> newAddressableGuids = new List<string>();
            TexturePaintExportResult result = new TexturePaintExportResult { identifier = plan.identifier };
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            try
            {
                if (!template.overwriteSourceOverlay) EnsureFolder(outputFolder);
                for (int i = 0; i < encoded.Count; i++)
                {
                    operation.ThrowIfCancellationRequested();
                    string path = encoded[i].path;
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        replacedFiles.Add(new FileSnapshot
                        {
                            assetPath = path,
                            bytes = File.ReadAllBytes(fullPath),
                            importerJson = importer != null ? EditorJsonUtility.ToJson(importer) : null
                        });
                    }
                    else newFiles.Add(path);
                    File.Copy(encoded[i].stagingPath, fullPath, true);
                    if (encoded[i].IsAlphaMask) result.alphaMaskPaths.Add(path);
                    else result.texturePaths.Add(path);
                    Report(operation, detailedProgress, $"Writing {encoded[i].displayName}",
                        0.5f + 0.12f * ((i + 1f) / encoded.Count));
                }

                Report(operation, detailedProgress, "Importing exported textures", 0.62f);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                for (int i = 0; i < plan.entries.Count; i++)
                {
                    operation.ThrowIfCancellationRequested();
                    ConfigureImporter(plan.entries[i].path, plan.entries[i]);
                    AssetDatabase.WriteImportSettingsIfDirty(plan.entries[i].path);
                    AssetDatabase.ImportAsset(plan.entries[i].path,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    Report(operation, detailedProgress, $"Configuring {plan.entries[i].DisplayName}",
                        0.62f + 0.12f * ((i + 1f) / plan.entries.Count));
                }
                for (int i = 0; i < plan.overlays.Count; i++)
                {
                    TexturePaintOverlayPlanEntry overlay = plan.overlays[i];
                    if (string.IsNullOrEmpty(overlay.alphaMaskPath)) continue;
                    ConfigureAlphaMaskImporter(overlay.alphaMaskPath);
                    AssetDatabase.WriteImportSettingsIfDirty(overlay.alphaMaskPath);
                    AssetDatabase.ImportAsset(overlay.alphaMaskPath,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                }

                for (int i = 0; i < plan.overlays.Count; i++)
                {
                    operation.ThrowIfCancellationRequested();
                    TexturePaintOverlayPlanEntry overlayPlan = plan.overlays[i];
                    OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(overlayPlan.path);
                    if (overlay == null)
                    {
                        overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
                        overlay.name = Path.GetFileNameWithoutExtension(overlayPlan.path);
                        AssetDatabase.CreateAsset(overlay, overlayPlan.path);
                        newAssets.Add(overlayPlan.path);
                    }
                    else
                    {
                        objectSnapshots.Add(new ObjectSnapshot
                        {
                            target = overlay,
                            json = EditorJsonUtility.ToJson(overlay)
                        });
                        if (template.overwriteSourceOverlay)
                            Undo.RecordObject(overlay, "Overwrite Overlay Painter Source Overlay");
                    }
                    ConfigureOverlay(overlay, overlayPlan);
                    EditorUtility.SetDirty(overlay);
                    RegisterOverlay(indexer, overlay, registeredOverlayNames);
                    result.overlayPaths.Add(overlayPlan.path);
                    TexturePaintExportResultSet resultSet = new TexturePaintExportResultSet
                    {
                        targetName = overlayPlan.targetName,
                        tileNumber = overlayPlan.tileNumber,
                        overlayPath = overlayPlan.path,
                        alphaMaskPath = overlayPlan.alphaMaskPath
                    };
                    for (int entryIndex = 0; entryIndex < overlayPlan.textures.Count; entryIndex++)
                        resultSet.texturePaths.Add(overlayPlan.textures[entryIndex].path);
                    result.resultSets.Add(resultSet);
                    Report(operation, detailedProgress, $"Creating {overlayPlan.DisplayName}",
                        0.74f + 0.14f * ((i + 1f) / plan.overlays.Count));
                }

                if (template.markAddressable)
                {
                    for (int i = 0; i < result.texturePaths.Count; i++)
                        if (MarkAddressable(result.texturePaths[i]))
                            newAddressableGuids.Add(AssetDatabase.AssetPathToGUID(result.texturePaths[i]));
                    for (int i = 0; i < result.alphaMaskPaths.Count; i++)
                        if (MarkAddressable(result.alphaMaskPaths[i]))
                            newAddressableGuids.Add(AssetDatabase.AssetPathToGUID(result.alphaMaskPaths[i]));
                    for (int i = 0; i < result.overlayPaths.Count; i++)
                        if (MarkAddressable(result.overlayPaths[i]))
                            newAddressableGuids.Add(AssetDatabase.AssetPathToGUID(result.overlayPaths[i]));
                }

                operation.ThrowIfCancellationRequested();
                for (int i = 0; i < result.overlayPaths.Count; i++)
                {
                    OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(
                        result.overlayPaths[i]);
                    if (overlay != null) AssetDatabase.SaveAssetIfDirty(overlay);
                }
                EditorUtility.SetDirty(indexer);
                AssetDatabase.SaveAssetIfDirty(indexer);
                Report(operation, detailedProgress, "Verifying exported assets", 0.94f);
                VerifyResults(indexer, plan, result);
                Report(operation, detailedProgress, "Texture export complete", 1f);
                return result;
            }
            catch
            {
                Rollback(indexer, outputFolder, outputFolderExisted, replacedFiles, newFiles, newAssets,
                    objectSnapshots, registeredOverlayNames, newAddressableGuids);
                throw;
            }
        }

        private static void ConfigureOverlay(OverlayDataAsset overlay,
            TexturePaintOverlayPlanEntry plan)
        {
            OverlayDataAsset source = plan.sourceOverlay;
            bool authoredOverlay = !string.IsNullOrEmpty(plan.alphaMaskPath);
            if (source != null && !ReferenceEquals(source, overlay))
            {
                overlay.overlayType = source.overlayType;
                overlay.dontMergeDuplicates = source.dontMergeDuplicates;
                overlay.tags = source.tags != null ? (string[])source.tags.Clone() : null;
                overlay.overlayGroup = source.overlayGroup;
                overlay.forceKeep = source.forceKeep;
                overlay.noAutoAdd = source.noAutoAdd;
            }
            if (authoredOverlay) overlay.overlayType = OverlayDataAsset.OverlayType.Normal;
            int count = Mathf.Max(1, plan.set.umaMaterial?.channels?.Length ?? 0);
            Texture[] textures = new Texture[count];
            string[] names = new string[count];
            for (int i = 0; i < plan.textures.Count; i++)
            {
                TexturePaintExportPlanEntry entry = plan.textures[i];
                if (entry.materialChannelIndex < 0 || entry.materialChannelIndex >= count) continue;
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.path);
                if (texture == null) throw new InvalidDataException("Exported texture did not import: " + entry.path);
                textures[entry.materialChannelIndex] = texture;
                names[entry.materialChannelIndex] = texture.name;
            }
            overlay.material = plan.set.umaMaterial;
            overlay.materialName = plan.set.umaMaterial != null ? plan.set.umaMaterial.name : string.Empty;
            overlay.textureList = textures;
            overlay.textureNames = names;
            OverlayDataAsset.OverlayBlend[] blends = new OverlayDataAsset.OverlayBlend[count];
            if (!authoredOverlay && source?.overlayBlend != null)
                Array.Copy(source.overlayBlend, blends, Mathf.Min(source.overlayBlend.Length, blends.Length));
            overlay.overlayBlend = blends;
            int width = 0, height = 0;
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] == null) continue;
                width = textures[i].width;
                height = textures[i].height;
                break;
            }
            if (width <= 0 || height <= 0) width = height = FirstResolution(plan.set);
            overlay.rect = new Rect(0f, 0f, width, height);
            overlay.alphaMask = string.IsNullOrEmpty(plan.alphaMaskPath) ? null :
                AssetDatabase.LoadAssetAtPath<Texture2D>(plan.alphaMaskPath);
            if (!string.IsNullOrEmpty(plan.alphaMaskPath) && overlay.alphaMask == null)
                throw new InvalidDataException("Exported overlay alpha mask did not import: " +
                    plan.alphaMaskPath);
        }

        private static void RegisterOverlay(UMAAssetIndexer indexer, OverlayDataAsset overlay,
            List<string> newlyRegistered)
        {
            if (indexer == null) throw new InvalidOperationException("UMAAssetIndexer is not available.");
            AssetItem existing = indexer.GetAssetItem<OverlayDataAsset>(overlay.overlayName);
            if (existing != null)
            {
                if (!ReferenceEquals(existing.Item, overlay))
                    throw new InvalidOperationException($"UMA library already contains a different overlay named " +
                        $"'{overlay.overlayName}'. Choose another Export Identifier.");
                return;
            }
            if (!indexer.EvilAddAsset(typeof(OverlayDataAsset), overlay))
                throw new InvalidOperationException("UMA library rejected overlay '" + overlay.overlayName + "'.");
            newlyRegistered.Add(overlay.overlayName);
        }

        private static void VerifyResults(UMAAssetIndexer indexer, TexturePaintExportPlan plan,
            TexturePaintExportResult result)
        {
            for (int i = 0; i < plan.overlays.Count; i++)
            {
                TexturePaintOverlayPlanEntry expected = plan.overlays[i];
                OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(expected.path);
                if (overlay == null) throw new InvalidDataException("Overlay did not persist: " + expected.path);
                AssetItem item = indexer.GetAssetItem<OverlayDataAsset>(overlay.overlayName);
                if (item == null || !ReferenceEquals(item.Item, overlay))
                    throw new InvalidDataException("UMA library lookup failed for overlay '" + overlay.overlayName + "'.");
                if (!string.IsNullOrEmpty(expected.alphaMaskPath) &&
                    (overlay.alphaMask == null || !string.Equals(
                        AssetDatabase.GetAssetPath(overlay.alphaMask), expected.alphaMaskPath,
                        StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Overlay alpha mask was not assigned correctly: " +
                        expected.alphaMaskPath);
                if (!string.IsNullOrEmpty(expected.alphaMaskPath) &&
                    !result.alphaMaskPaths.Exists(path => string.Equals(path, expected.alphaMaskPath,
                        StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Overlay alpha mask is missing from the export result: " +
                        expected.alphaMaskPath);
            }
            int alphaMaskCount = 0;
            for (int i = 0; i < plan.overlays.Count; i++)
                if (!string.IsNullOrEmpty(plan.overlays[i].alphaMaskPath)) alphaMaskCount++;
            if (result.overlayPaths.Count != plan.overlays.Count ||
                result.texturePaths.Count != plan.entries.Count ||
                result.alphaMaskPaths.Count != alphaMaskCount)
                throw new InvalidDataException("The committed export result does not match its preflight plan.");
        }

        private static void Rollback(UMAAssetIndexer indexer, string outputFolder, bool outputFolderExisted,
            List<FileSnapshot> replacedFiles, List<string> newFiles, List<string> newAssets,
            List<ObjectSnapshot> objectSnapshots, List<string> registeredOverlayNames,
            List<string> newAddressableGuids)
        {
            for (int i = 0; i < newAddressableGuids.Count; i++) RemoveAddressable(newAddressableGuids[i]);
            if (indexer != null)
            {
                for (int i = 0; i < registeredOverlayNames.Count; i++)
                    indexer.RemoveAsset(typeof(OverlayDataAsset), registeredOverlayNames[i], false);
                indexer.RebuildIndex();
                EditorUtility.SetDirty(indexer);
            }
            for (int i = 0; i < objectSnapshots.Count; i++)
            {
                ObjectSnapshot snapshot = objectSnapshots[i];
                if (snapshot.target == null) continue;
                EditorJsonUtility.FromJsonOverwrite(snapshot.json, snapshot.target);
                EditorUtility.SetDirty(snapshot.target);
            }
            for (int i = 0; i < newAssets.Count; i++) AssetDatabase.DeleteAsset(newAssets[i]);
            for (int i = 0; i < replacedFiles.Count; i++)
                File.WriteAllBytes(Path.GetFullPath(replacedFiles[i].assetPath), replacedFiles[i].bytes);
            for (int i = 0; i < newFiles.Count; i++)
            {
                string fullPath = Path.GetFullPath(newFiles[i]);
                if (!File.Exists(fullPath)) continue;
                if (!AssetDatabase.DeleteAsset(newFiles[i])) File.Delete(fullPath);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < replacedFiles.Count; i++)
            {
                TextureImporter importer = AssetImporter.GetAtPath(replacedFiles[i].assetPath) as TextureImporter;
                if (importer != null && !string.IsNullOrEmpty(replacedFiles[i].importerJson))
                {
                    EditorJsonUtility.FromJsonOverwrite(replacedFiles[i].importerJson, importer);
                    AssetDatabase.WriteImportSettingsIfDirty(replacedFiles[i].assetPath);
                }
                AssetDatabase.ImportAsset(replacedFiles[i].assetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            for (int i = 0; i < objectSnapshots.Count; i++)
                if (objectSnapshots[i].target != null)
                    AssetDatabase.SaveAssetIfDirty(objectSnapshots[i].target);
            if (indexer != null) AssetDatabase.SaveAssetIfDirty(indexer);
            if (!outputFolderExisted && AssetDatabase.IsValidFolder(outputFolder))
                AssetDatabase.DeleteAsset(outputFolder);
        }

        private static void ConfigureImporter(string path, TexturePaintExportPlanEntry entry)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidDataException("No TextureImporter was created for " + path);
            UMAMaterial.TextureChannelOutputSettings output = entry.materialChannel.output;
            importer.textureType = output.importerType == UMAMaterial.TextureChannelImporterType.NormalMap
                ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = output.colorSpace == UMAMaterial.TextureChannelColorSpace.SRGB;
            importer.mipmapEnabled = output.generateMipMaps;
            importer.textureCompression = ToImporterCompression(output.compression);
            importer.alphaSource = output.alphaSource == UMAMaterial.TextureChannelAlphaSource.FromInput
                ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.filterMode = output.filterMode;
            importer.anisoLevel = output.anisoLevel;
            importer.maxTextureSize = output.maxTextureSize;
            importer.npotScale = TextureImporterNPOTScale.None;
            UMAMaterial.TextureChannelPlatformOverrideSettings[] overrides = output.platformOverrides;
            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                UMAMaterial.TextureChannelPlatformOverrideSettings configured = overrides[i];
                if (string.IsNullOrWhiteSpace(configured.platformName)) continue;
                TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings(configured.platformName);
                platform.name = configured.platformName;
                platform.overridden = configured.enabled;
                platform.maxTextureSize = configured.maxTextureSize;
                platform.textureCompression = ToImporterCompression(configured.compression);
                importer.SetPlatformTextureSettings(platform);
            }
        }

        private static void ConfigureAlphaMaskImporter(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidDataException(
                "No TextureImporter was created for overlay alpha mask " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
        }

        private static TextureImporterCompression ToImporterCompression(
            UMAMaterial.TextureChannelImportCompression compression)
        {
            return compression == UMAMaterial.TextureChannelImportCompression.HighQuality
                ? TextureImporterCompression.CompressedHQ
                : compression == UMAMaterial.TextureChannelImportCompression.Compressed
                    ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
        }

        private static TexturePaintExportBitDepth ToBitDepth(UMAMaterial.TextureChannelOutputEncoding encoding)
        {
            return encoding == UMAMaterial.TextureChannelOutputEncoding.ExrHalf
                ? TexturePaintExportBitDepth.HalfFloat
                : encoding == UMAMaterial.TextureChannelOutputEncoding.Png16
                    ? TexturePaintExportBitDepth.Sixteen : TexturePaintExportBitDepth.Eight;
        }

        private static bool IsNormalChannel(TexturePaintMaterialChannelCapability channel)
        {
            return channel.layout.red == UMAMaterial.TextureChannelUsage.Normal &&
                   channel.layout.green == UMAMaterial.TextureChannelUsage.Normal &&
                   channel.layout.blue == UMAMaterial.TextureChannelUsage.Normal;
        }

        private static bool ContainsLogicalChannel(TexturePaintMaterialChannelCapability capability,
            TexturePaintChannel channel)
        {
            for (int i = 0; i < capability.LogicalChannels.Count; i++)
                if (capability.LogicalChannels[i] == channel) return true;
            return false;
        }

        private static string PersistentTextureStem(Texture texture)
        {
            if (texture == null) return null;
            string path = AssetDatabase.GetAssetPath(texture);
            return string.IsNullOrEmpty(path) ? null : Sanitize(Path.GetFileNameWithoutExtension(path));
        }

        private static string ResolveOutputPath(string requested, TexturePaintOverwritePolicy policy,
            HashSet<string> reserved, List<string> errors)
        {
            bool exists = File.Exists(Path.GetFullPath(requested)) ||
                          AssetDatabase.LoadMainAssetAtPath(requested) != null || reserved.Contains(requested);
            if (!exists || policy == TexturePaintOverwritePolicy.Overwrite) return requested;
            if (policy == TexturePaintOverwritePolicy.Fail)
            {
                errors.Add("Output already exists: " + requested);
                return null;
            }
            string directory = Path.GetDirectoryName(requested)?.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(requested);
            string extension = Path.GetExtension(requested);
            for (int version = 1; version < 10000; version++)
            {
                string candidate = directory + "/" + name + "_v" + version.ToString("D3") + extension;
                if (!File.Exists(Path.GetFullPath(candidate)) &&
                    AssetDatabase.LoadMainAssetAtPath(candidate) == null && !reserved.Contains(candidate))
                    return candidate;
            }
            errors.Add("Could not allocate a versioned path for " + requested);
            return null;
        }

        private static int FirstResolution(TextureSet set)
        {
            foreach (TextureChannelTarget channel in set.channels.Values)
                if (channel?.Texture != null) return Mathf.Max(channel.Texture.width, channel.Texture.height);
            return 2048;
        }

        private static bool MarkAddressable(string path)
        {
#if UMA_ADDRESSABLES
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return false;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (settings.FindAssetEntry(guid) != null) return false;
            settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            return true;
#else
            return false;
#endif
        }

        private static void RemoveAddressable(string guid)
        {
#if UMA_ADDRESSABLES
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            settings?.RemoveAssetEntry(guid);
#endif
        }

        private static void ApplyDeclaredOutputTransform(Texture2D texture,
            TexturePaintMaterialChannelCapability channel, bool invertNormalGreen,
            bool linear, TexturePaintOperationContext operation,
            bool preserveAuthoredOverlayAlpha = false)
        {
            Vector4 replace = Vector4.zero;
            Vector4 neutral = Vector4.zero;
            bool any = false;
            for (int component = 0; component < 4; component++)
            {
                TexturePaintPhysicalComponentCapability mapping = channel.Components[component];
                bool replaceComponent = mapping == null ||
                    mapping.usage == UMAMaterial.TextureChannelUsage.Unused;
                if (preserveAuthoredOverlayAlpha && component == 3 &&
                    ContainsLogicalChannel(channel, TexturePaintChannel.Albedo))
                    replaceComponent = false;
                replace[component] = replaceComponent ? 1f : 0f;
                neutral[component] = mapping?.neutralValue ?? 0f;
                any |= replaceComponent;
            }
            if (!any && !invertNormalGreen) return;
            operation.ThrowIfCancellationRequested();
            Shader shader = LoadExportShader();
            RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                ExportRenderTextureFormat(texture), linear
                    ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture previous = RenderTexture.active;
            try
            {
                material.SetVector("_ReplaceMask", replace);
                material.SetVector("_NeutralValues", neutral);
                material.SetFloat("_InvertGreen", invertNormalGreen ? 1f : 0f);
                material.SetTexture("_MainTex", texture);
                Graphics.Blit(texture, temporary, material, 3);
                RenderTexture.active = temporary;
                texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                texture.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(material);
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static void DilateTransparent(Texture2D texture, int padding, bool linear,
            TexturePaintOperationContext operation, Action<int, int> progress)
        {
            if (texture == null || padding <= 0) return;
            Shader shader = LoadExportShader();
            RenderTextureFormat colorFormat = ExportRenderTextureFormat(texture);
            RenderTextureFormat validityFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
                ? RenderTextureFormat.R8 : RenderTextureFormat.RHalf;
            RenderTextureReadWrite colorSpace = linear
                ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            RenderTexture colorA = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                colorFormat, colorSpace);
            RenderTexture colorB = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                colorFormat, colorSpace);
            RenderTexture validA = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                validityFormat, RenderTextureReadWrite.Linear);
            RenderTexture validB = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                validityFormat, RenderTextureReadWrite.Linear);
            Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            colorA.filterMode = colorB.filterMode = FilterMode.Point;
            validA.filterMode = validB.filterMode = FilterMode.Point;
            colorA.wrapMode = colorB.wrapMode = validA.wrapMode = validB.wrapMode = TextureWrapMode.Clamp;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, colorA);
                material.SetTexture("_MainTex", texture);
                Graphics.Blit(texture, validA, material, 2);
                for (int pass = 0; pass < padding; pass++)
                {
                    operation.ThrowIfCancellationRequested();
                    material.SetTexture("_MainTex", colorA);
                    material.SetTexture("_ValidityTex", validA);
                    Graphics.Blit(colorA, colorB, material, 0);
                    material.SetTexture("_MainTex", validA);
                    Graphics.Blit(validA, validB, material, 1);
                    (colorA, colorB) = (colorB, colorA);
                    (validA, validB) = (validB, validA);
                    progress?.Invoke(pass + 1, padding);
                }
                operation.ThrowIfCancellationRequested();
                RenderTexture.active = colorA;
                texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                texture.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(material);
                RenderTexture.ReleaseTemporary(colorA);
                RenderTexture.ReleaseTemporary(colorB);
                RenderTexture.ReleaseTemporary(validA);
                RenderTexture.ReleaseTemporary(validB);
            }
        }

        private static Shader LoadExportShader()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/UMA/OverlayPainter/Shaders/ExportDilate.shader");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException(
                    "The Overlay Painter export post-process shader is missing or unsupported.");
            return shader;
        }

        private static RenderTextureFormat ExportRenderTextureFormat(Texture2D texture)
        {
            RenderTextureFormat format = texture.format switch
            {
                TextureFormat.RGBA64 => RenderTextureFormat.ARGB64,
                TextureFormat.RGBAHalf => RenderTextureFormat.ARGBHalf,
                TextureFormat.RGBAFloat => RenderTextureFormat.ARGBFloat,
                _ => RenderTextureFormat.ARGB32
            };
            if (SystemInfo.SupportsRenderTextureFormat(format)) return format;
            return SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf;
        }

        private static void Report(TexturePaintOperationContext operation,
            Action<string, float> detailedProgress, string message, float value)
        {
            value = Mathf.Clamp01(value);
            operation.Report(value);
            detailedProgress?.Invoke(message, value);
            operation.ThrowIfCancellationRequested();
        }

        private static string NormalizeAssetFolder(string folder)
        {
            folder = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            return folder.StartsWith("Assets/", StringComparison.Ordinal)
                ? folder : "Assets/UMA/OverlayPainter/Generated";
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string Sanitize(string value)
        {
            value = (value ?? string.Empty).Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            value = value.Replace('/', '_').Replace('\\', '_');
            return string.IsNullOrWhiteSpace(value) ? "TexturePaint" : value;
        }
    }
}
