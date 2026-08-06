#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint
{
    public enum TexturePaintMaterialPipeline
    {
        Unsupported,
        Universal,
        HighDefinition
    }

    public enum TexturePaintCapabilitySeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class TexturePaintCapabilityDiagnostic
    {
        public TexturePaintCapabilitySeverity severity { get; internal set; }
        public string code { get; internal set; }
        public string message { get; internal set; }
        public int materialChannelIndex { get; internal set; } = -1;
    }

    public sealed class TexturePaintPhysicalComponentCapability
    {
        public int component { get; internal set; }
        public UMAMaterial.TextureChannelUsage usage { get; internal set; }
        public bool editable { get; internal set; }
        public TexturePaintChannel logicalChannel { get; internal set; }
        public bool invert { get; internal set; }
        public float neutralValue { get; internal set; }
    }

    public sealed class TexturePaintMaterialChannelCapability
    {
        private readonly TexturePaintPhysicalComponentCapability[] components =
            new TexturePaintPhysicalComponentCapability[4];
        private readonly List<TexturePaintCapabilityDiagnostic> diagnostics =
            new List<TexturePaintCapabilityDiagnostic>();
        private readonly List<TexturePaintChannel> logicalChannels = new List<TexturePaintChannel>();
        private readonly ReadOnlyCollection<TexturePaintPhysicalComponentCapability> componentView;
        private readonly ReadOnlyCollection<TexturePaintCapabilityDiagnostic> diagnosticView;
        private readonly ReadOnlyCollection<TexturePaintChannel> logicalChannelView;
        private UMAMaterial.MaterialChannel definitionValue;
        private UMAMaterial.TextureChannelOutputSettings outputValue;

        internal TexturePaintMaterialChannelCapability()
        {
            componentView = Array.AsReadOnly(components);
            diagnosticView = diagnostics.AsReadOnly();
            logicalChannelView = logicalChannels.AsReadOnly();
        }

        public int index { get; internal set; }
        public UMAMaterial.MaterialChannel definition
        {
            get => CloneDefinition(definitionValue);
            internal set => definitionValue = CloneDefinition(value);
        }
        public string materialProperty { get; internal set; }
        public string sourceTextureName { get; internal set; }
        public Texture sourceTexture { get; internal set; }
        public int width { get; internal set; }
        public int height { get; internal set; }
        public int workingWidth { get; internal set; }
        public int workingHeight { get; internal set; }
        public int outputWidth { get; internal set; }
        public int outputHeight { get; internal set; }
        public RenderTextureFormat workingFormat { get; internal set; }
        public UMAMaterial.TextureChannelLayout layout { get; internal set; }
        public UMAMaterial.TextureChannelOutputSettings output
        {
            get => CloneOutput(value: outputValue);
            internal set => outputValue = CloneOutput(value);
        }
        public bool isTexture { get; internal set; }
        public bool requiresPacking { get; internal set; }
        public IReadOnlyList<TexturePaintPhysicalComponentCapability> Components => componentView;
        public IReadOnlyList<TexturePaintCapabilityDiagnostic> Diagnostics => diagnosticView;
        public IReadOnlyList<TexturePaintChannel> LogicalChannels => logicalChannelView;
        public bool HasErrors => diagnostics.Exists(item => item.severity == TexturePaintCapabilitySeverity.Error);

        internal void SetComponent(int component, TexturePaintPhysicalComponentCapability value)
        {
            components[component] = value;
        }

        internal void AddDiagnostic(TexturePaintCapabilityDiagnostic diagnostic)
        {
            diagnostics.Add(diagnostic);
        }

        internal void AddLogicalChannel(TexturePaintChannel channel)
        {
            if (!logicalChannels.Contains(channel)) logicalChannels.Add(channel);
        }

        private static UMAMaterial.MaterialChannel CloneDefinition(UMAMaterial.MaterialChannel value)
        {
            value.textureChannelOutput = CloneOutput(value.textureChannelOutput);
            return value;
        }

        private static UMAMaterial.TextureChannelOutputSettings CloneOutput(
            UMAMaterial.TextureChannelOutputSettings value)
        {
            value.platformOverrides = value.platformOverrides == null || value.platformOverrides.Length == 0
                ? Array.Empty<UMAMaterial.TextureChannelPlatformOverrideSettings>()
                : (UMAMaterial.TextureChannelPlatformOverrideSettings[])value.platformOverrides.Clone();
            return value;
        }
    }

    public sealed class TexturePaintMaterialCapabilityDescriptor
    {
        private readonly List<TexturePaintMaterialChannelCapability> channels =
            new List<TexturePaintMaterialChannelCapability>();
        private readonly List<TexturePaintCapabilityDiagnostic> diagnostics =
            new List<TexturePaintCapabilityDiagnostic>();
        private readonly ReadOnlyCollection<TexturePaintMaterialChannelCapability> channelView;
        private readonly ReadOnlyCollection<TexturePaintCapabilityDiagnostic> diagnosticView;

        internal TexturePaintMaterialCapabilityDescriptor()
        {
            channelView = channels.AsReadOnly();
            diagnosticView = diagnostics.AsReadOnly();
        }

        public UMAMaterial umaMaterial { get; internal set; }
        public Material material { get; internal set; }
        public Shader shader => material != null ? material.shader : null;
        public TexturePaintMaterialPipeline pipeline { get; internal set; }
        public IReadOnlyList<TexturePaintMaterialChannelCapability> Channels => channelView;
        public IReadOnlyList<TexturePaintCapabilityDiagnostic> Diagnostics => diagnosticView;
        public bool IsSupported => diagnostics.FindIndex(item =>
            item.severity == TexturePaintCapabilitySeverity.Error) < 0;

        public TexturePaintMaterialChannelCapability GetChannel(int index)
        {
            return index >= 0 && index < channels.Count ? channels[index] : null;
        }

        public TexturePaintMaterialChannelCapability FindChannel(string materialProperty)
        {
            if (string.IsNullOrEmpty(materialProperty)) return null;
            for (int i = 0; i < channels.Count; i++)
                if (string.Equals(channels[i].materialProperty, materialProperty,
                    StringComparison.Ordinal)) return channels[i];
            return null;
        }

        public string FailureSummary()
        {
            List<string> failures = new List<string>();
            for (int i = 0; i < diagnostics.Count; i++)
                if (diagnostics[i].severity == TexturePaintCapabilitySeverity.Error)
                    failures.Add(diagnostics[i].message);
            return string.Join("\n", failures);
        }

        internal void AddChannel(TexturePaintMaterialChannelCapability channel)
        {
            channels.Add(channel);
            for (int i = 0; i < channel.Diagnostics.Count; i++)
                diagnostics.Add(channel.Diagnostics[i]);
        }

        internal void AddDiagnostic(TexturePaintCapabilityDiagnostic diagnostic)
        {
            diagnostics.Add(diagnostic);
        }
    }

    public static class TexturePaintMaterialCapabilityService
    {
        private const UMAMaterial.TextureChannelUsage EditableUsageMask =
            UMAMaterial.TextureChannelUsage.Albedo |
            UMAMaterial.TextureChannelUsage.Normal |
            UMAMaterial.TextureChannelUsage.Metallic |
            UMAMaterial.TextureChannelUsage.Smoothness |
            UMAMaterial.TextureChannelUsage.Roughness |
            UMAMaterial.TextureChannelUsage.AmbientOcclusion |
            UMAMaterial.TextureChannelUsage.Emission |
            UMAMaterial.TextureChannelUsage.Custom;

        private const UMAMaterial.TextureChannelUsage ColorUsageMask =
            UMAMaterial.TextureChannelUsage.Albedo |
            UMAMaterial.TextureChannelUsage.Emission |
            UMAMaterial.TextureChannelUsage.DetailAlbedo;

        public static TexturePaintMaterialCapabilityDescriptor Compile(UMAMaterial umaMaterial,
            Material activeMaterial, IReadOnlyList<Texture> channelSources = null,
            bool allowMissingTextures = false)
        {
            TexturePaintMaterialCapabilityDescriptor result = new TexturePaintMaterialCapabilityDescriptor
            {
                umaMaterial = umaMaterial,
                material = activeMaterial,
                pipeline = DetectPipeline(activeMaterial)
            };

            if (umaMaterial == null)
            {
                Add(result, TexturePaintCapabilitySeverity.Error, "MAT001",
                    "No UMAMaterial is assigned to this generated surface.");
                return result;
            }
            if (activeMaterial == null || activeMaterial.shader == null)
            {
                Add(result, TexturePaintCapabilitySeverity.Error, "MAT002",
                    $"UMA Material '{umaMaterial.name}' has no active render-pipeline material and shader.");
                return result;
            }

            Material configured = umaMaterial.material;
            if (configured == null || configured.shader == null)
                Add(result, TexturePaintCapabilitySeverity.Error, "MAT003",
                    $"UMA Material '{umaMaterial.name}' does not resolve an active material through UMAMaterial.material.");
            else if (configured.shader != activeMaterial.shader)
                Add(result, TexturePaintCapabilitySeverity.Error, "MAT004",
                    $"Generated material shader '{activeMaterial.shader.name}' does not match the active shader " +
                    $"'{configured.shader.name}' selected by UMA Material '{umaMaterial.name}'.");

            TexturePaintMaterialPipeline currentPipeline = DetectCurrentPipeline();
            if (result.pipeline == TexturePaintMaterialPipeline.Unsupported)
                Add(result, TexturePaintCapabilitySeverity.Error, "MAT005",
                    $"Shader '{activeMaterial.shader.name}' is not tagged for URP or HDRP. " +
                    "Overlay Painter release workflows support URP and HDRP only.");
            else if (currentPipeline == TexturePaintMaterialPipeline.Unsupported)
                Add(result, TexturePaintCapabilitySeverity.Error, "MAT006",
                    "The active project render pipeline is neither URP nor HDRP.");
            else if (currentPipeline != result.pipeline)
                Add(result, TexturePaintCapabilitySeverity.Error, "MAT007",
                    $"Shader '{activeMaterial.shader.name}' targets {PipelineName(result.pipeline)}, but the active " +
                    $"project pipeline is {PipelineName(currentPipeline)}.");

            if (umaMaterial.channels == null || umaMaterial.channels.Length == 0)
            {
                Add(result, TexturePaintCapabilitySeverity.Error, "MAT008",
                    $"UMA Material '{umaMaterial.name}' does not declare any material channels.");
                return result;
            }

            Dictionary<string, int> properties = new Dictionary<string, int>(StringComparer.Ordinal);
            bool requiresComputePacking = false;
            for (int channelIndex = 0; channelIndex < umaMaterial.channels.Length; channelIndex++)
            {
                UMAMaterial.MaterialChannel definition = umaMaterial.channels[channelIndex];
                Texture source = channelSources != null && channelIndex < channelSources.Count
                    ? channelSources[channelIndex]
                    : GetTexture(activeMaterial, definition.materialPropertyName);
                TexturePaintMaterialChannelCapability channel = CompileChannel(result, definition,
                    channelIndex, source, allowMissingTextures);
                if (channel.requiresPacking) requiresComputePacking = true;

                if (channel.isTexture && !string.IsNullOrEmpty(channel.materialProperty))
                {
                    if (properties.TryGetValue(channel.materialProperty, out int prior))
                        Add(channel, TexturePaintCapabilitySeverity.Error, "CHN009",
                            $"Material channels {prior} and {channelIndex} both target '{channel.materialProperty}'. " +
                            "Each physical shader texture must have one deterministic channel definition.");
                    else properties.Add(channel.materialProperty, channelIndex);
                }
                result.AddChannel(channel);
            }

            if (requiresComputePacking && !SystemInfo.supportsComputeShaders)
                Add(result, TexturePaintCapabilitySeverity.Error, "GPU001",
                    "This material requires component packing, but compute shaders are unavailable.");
            if (requiresComputePacking &&
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGB32))
                Add(result, TexturePaintCapabilitySeverity.Error, "GPU002",
                    "This material requires component packing, but ARGB32 random-write render textures are unavailable.");
            return result;
        }

        public static bool TryResolveUsage(UMAMaterial.TextureChannelUsage usage,
            out TexturePaintChannel channel, out bool invert)
        {
            invert = false;
            UMAMaterial.TextureChannelUsage supported = usage & EditableUsageMask;
            if ((usage & ~EditableUsageMask) != 0 || CountFlags(supported) != 1)
            {
                channel = TexturePaintChannel.Custom;
                return false;
            }
            if ((supported & UMAMaterial.TextureChannelUsage.Albedo) != 0) channel = TexturePaintChannel.Albedo;
            else if ((supported & UMAMaterial.TextureChannelUsage.Normal) != 0) channel = TexturePaintChannel.Normal;
            else if ((supported & UMAMaterial.TextureChannelUsage.Metallic) != 0) channel = TexturePaintChannel.Metallic;
            else if ((supported & UMAMaterial.TextureChannelUsage.Smoothness) != 0)
            {
                channel = TexturePaintChannel.Roughness;
                invert = true;
            }
            else if ((supported & UMAMaterial.TextureChannelUsage.Roughness) != 0) channel = TexturePaintChannel.Roughness;
            else if ((supported & UMAMaterial.TextureChannelUsage.AmbientOcclusion) != 0) channel = TexturePaintChannel.AmbientOcclusion;
            else if ((supported & UMAMaterial.TextureChannelUsage.Emission) != 0) channel = TexturePaintChannel.Emission;
            else channel = TexturePaintChannel.Custom;
            return true;
        }

        private static TexturePaintMaterialChannelCapability CompileChannel(
            TexturePaintMaterialCapabilityDescriptor descriptor, UMAMaterial.MaterialChannel definition,
            int channelIndex, Texture source, bool allowMissingTextures)
        {
            bool isTexture = definition.channelType != UMAMaterial.ChannelType.MaterialColor;
            TexturePaintMaterialChannelCapability result = new TexturePaintMaterialChannelCapability
            {
                index = channelIndex,
                definition = definition,
                materialProperty = (definition.materialPropertyName ?? string.Empty).Trim(),
                sourceTextureName = (definition.sourceTextureName ?? string.Empty).Trim(),
                sourceTexture = source,
                width = source != null ? source.width : 0,
                height = source != null ? source.height : 0,
                workingWidth = source != null ? source.width : 0,
                workingHeight = source != null ? source.height : 0,
                workingFormat = UMAMaterial.GetCompatibleChannelTextureFormat(definition.textureFormat),
                layout = UMAMaterial.GetTextureChannelLayout(definition, descriptor.material),
                output = UMAMaterial.GetTextureChannelOutputSettings(definition, descriptor.material,
                    descriptor.umaMaterial),
                isTexture = isTexture
            };
            if (result.width > 0 && result.height > 0)
            {
                float outputScale = Mathf.Min(1f, result.output.maxTextureSize /
                    (float)Mathf.Max(result.width, result.height));
                result.outputWidth = Mathf.Max(1, Mathf.RoundToInt(result.width * outputScale));
                result.outputHeight = Mathf.Max(1, Mathf.RoundToInt(result.height * outputScale));
            }

            if (!isTexture) return result;
            if (!definition.NonShaderTexture)
            {
                if (string.IsNullOrEmpty(result.materialProperty))
                    Add(result, TexturePaintCapabilitySeverity.Error, "CHN001",
                        $"Material channel {channelIndex} has no shader property name.");
                else if (!descriptor.material.HasProperty(result.materialProperty))
                    Add(result, TexturePaintCapabilitySeverity.Error, "CHN002",
                        $"Shader '{descriptor.shader.name}' does not contain texture property " +
                        $"'{result.materialProperty}' for material channel {channelIndex}.");
                else
                {
                    int propertyIndex = descriptor.shader.FindPropertyIndex(result.materialProperty);
                    if (propertyIndex >= 0 && descriptor.shader.GetPropertyType(propertyIndex) != ShaderPropertyType.Texture)
                        Add(result, TexturePaintCapabilitySeverity.Error, "CHN003",
                            $"Shader property '{result.materialProperty}' is not a texture property.");
                }
            }
            if (source == null)
                Add(result, allowMissingTextures ? TexturePaintCapabilitySeverity.Warning :
                    TexturePaintCapabilitySeverity.Error, "CHN004",
                    $"Material channel {channelIndex} ('{DisplayProperty(result)}') has no source texture. " +
                    (allowMissingTextures ? "A semantic-neutral source must be created before painting." :
                        "Generate the UMA material or assign a compatible source texture before opening the stage."));
            if (!UMAMaterial.IsSupportedChannelTextureFormat(definition.textureFormat))
                Add(result, TexturePaintCapabilitySeverity.Warning, "CHN005",
                    $"Material channel {channelIndex} requests unsupported working format {definition.textureFormat}; " +
                    $"Overlay Painter will use {result.workingFormat}.");

            HashSet<TexturePaintChannel> logicalChannels = new HashSet<TexturePaintChannel>();
            for (int component = 0; component < 4; component++)
            {
                UMAMaterial.TextureChannelUsage usage = result.layout.GetComponent(component);
                UMAMaterial.TextureChannelUsage supported = usage & EditableUsageMask;
                UMAMaterial.TextureChannelUsage unsupported = usage & ~EditableUsageMask;
                int supportedCount = CountFlags(supported);
                if (supportedCount > 1 || (supported != 0 && unsupported != 0))
                    Add(result, TexturePaintCapabilitySeverity.Error, "CHN006",
                        $"{ComponentName(component)} of '{DisplayProperty(result)}' has conflicting meanings " +
                        $"({usage}). Split the meanings or author a safe Custom mapping.");
                else if (unsupported != 0)
                    Add(result, TexturePaintCapabilitySeverity.Warning, "CHN007",
                        $"{ComponentName(component)} of '{DisplayProperty(result)}' uses {unsupported}, which is " +
                        "preserved from the source but is not currently editable.");

                bool editable = TryResolveUsage(usage, out TexturePaintChannel logical, out bool invert);
                if (editable)
                {
                    logicalChannels.Add(logical);
                    result.AddLogicalChannel(logical);
                }
                result.SetComponent(component, new TexturePaintPhysicalComponentCapability
                {
                    component = component,
                    usage = usage,
                    editable = editable,
                    logicalChannel = logical,
                    invert = invert,
                    neutralValue = NeutralValue(usage, component)
                });
            }

            result.requiresPacking = RequiresPacking(result, logicalChannels);
            ValidateColorSpace(result);
            ValidateCustomOutput(result);
            return result;
        }

        private static bool RequiresPacking(TexturePaintMaterialChannelCapability channel,
            HashSet<TexturePaintChannel> logicalChannels)
        {
            if (logicalChannels.Count == 0) return false;
            if (logicalChannels.Count > 1) return true;
            foreach (TexturePaintChannel logical in logicalChannels)
            {
                if (logical == TexturePaintChannel.Albedo || logical == TexturePaintChannel.Normal ||
                    logical == TexturePaintChannel.Emission || logical == TexturePaintChannel.Custom)
                {
                    bool red = false, green = false, blue = false;
                    for (int i = 0; i < 4; i++)
                    {
                        TexturePaintPhysicalComponentCapability component = channel.Components[i];
                        if (component == null || !component.editable || component.logicalChannel != logical) continue;
                        if (component.invert) return true;
                        if (i == 0) red = true;
                        else if (i == 1) green = true;
                        else if (i == 2) blue = true;
                    }
                    return !(red && green && blue);
                }
                int count = 0;
                for (int i = 0; i < 4; i++)
                {
                    TexturePaintPhysicalComponentCapability component = channel.Components[i];
                    if (component == null || !component.editable || component.logicalChannel != logical) continue;
                    count++;
                    if (i != 0 || component.invert) return true;
                }
                return count != 1;
            }
            return false;
        }

        private static void ValidateColorSpace(TexturePaintMaterialChannelCapability channel)
        {
            UMAMaterial.TextureChannelUsage rgb = channel.layout.red | channel.layout.green | channel.layout.blue;
            bool hasColor = (rgb & ColorUsageMask) != 0;
            bool hasData = (rgb & ~(ColorUsageMask | UMAMaterial.TextureChannelUsage.Unused)) != 0;
            bool hdrpDetail = string.Equals(channel.materialProperty, "_DetailMap", StringComparison.Ordinal);
            if (hasColor && hasData && !hdrpDetail)
                Add(channel, TexturePaintCapabilitySeverity.Error, "CHN008",
                    $"'{DisplayProperty(channel)}' mixes color and data meanings in RGB. One importer color-space " +
                    "setting cannot represent that layout safely; use separate physical textures or a custom adapter.");

            if (channel.sourceTexture is Texture2D texture)
            {
                bool expectedSrgb = channel.output.colorSpace == UMAMaterial.TextureChannelColorSpace.SRGB;
                if (texture.isDataSRGB != expectedSrgb)
                    Add(channel, TexturePaintCapabilitySeverity.Warning, "IMP001",
                        $"Source texture '{texture.name}' is {(texture.isDataSRGB ? "sRGB" : "Linear")}, but " +
                        $"'{DisplayProperty(channel)}' is configured for {(expectedSrgb ? "sRGB" : "Linear")} output.");
            }
        }

        private static void ValidateCustomOutput(TexturePaintMaterialChannelCapability channel)
        {
            if (channel.definition.textureChannelOutput.mode != UMAMaterial.TextureChannelOutputMode.Custom) return;
            bool normalLayout = channel.layout.red == UMAMaterial.TextureChannelUsage.Normal &&
                                channel.layout.green == UMAMaterial.TextureChannelUsage.Normal &&
                                channel.layout.blue == UMAMaterial.TextureChannelUsage.Normal;
            if (channel.output.importerType == UMAMaterial.TextureChannelImporterType.NormalMap && !normalLayout)
                Add(channel, TexturePaintCapabilitySeverity.Error, "IMP002",
                    $"'{DisplayProperty(channel)}' uses the Normal Map importer but RGB is not a complete normal map.");
            if (channel.output.importerType == UMAMaterial.TextureChannelImporterType.NormalMap &&
                channel.output.colorSpace == UMAMaterial.TextureChannelColorSpace.SRGB)
                Add(channel, TexturePaintCapabilitySeverity.Error, "IMP003",
                    $"'{DisplayProperty(channel)}' cannot use both the Normal Map importer and sRGB color space.");

            HashSet<string> platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            UMAMaterial.TextureChannelPlatformOverrideSettings[] overrides = channel.output.platformOverrides;
            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                if (!overrides[i].enabled) continue;
                if (string.IsNullOrWhiteSpace(overrides[i].platformName))
                    Add(channel, TexturePaintCapabilitySeverity.Error, "IMP004",
                        $"'{DisplayProperty(channel)}' has an enabled platform override without a platform name.");
                else if (!platforms.Add(overrides[i].platformName))
                    Add(channel, TexturePaintCapabilitySeverity.Error, "IMP005",
                        $"'{DisplayProperty(channel)}' declares platform '{overrides[i].platformName}' more than once.");
            }
        }

        private static TexturePaintMaterialPipeline DetectPipeline(Material material)
        {
            if (material == null || material.shader == null) return TexturePaintMaterialPipeline.Unsupported;
            string tag = material.GetTag("RenderPipeline", false, string.Empty) ?? string.Empty;
            TexturePaintMaterialPipeline detected = PipelineFromTag(tag);
            if (detected != TexturePaintMaterialPipeline.Unsupported) return detected;

            // Material.GetTag depends on the currently selected SubShader. That can return an
            // empty value under the null graphics device used by batch-mode validation, and it
            // can also hide a valid declaration when the project is currently using the other
            // SRP. Read every authored SubShader tag so setup and CI reach the same decision.
            ShaderTagId renderPipeline = new ShaderTagId("RenderPipeline");
            for (int subshaderIndex = 0; subshaderIndex < material.shader.subshaderCount; subshaderIndex++)
            {
                detected = PipelineFromTag(material.shader.FindSubshaderTagValue(
                    subshaderIndex, renderPipeline).name);
                if (detected != TexturePaintMaterialPipeline.Unsupported) return detected;
            }
            return TexturePaintMaterialPipeline.Unsupported;
        }

        private static TexturePaintMaterialPipeline PipelineFromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return TexturePaintMaterialPipeline.Unsupported;
            if (tag.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
                return TexturePaintMaterialPipeline.Universal;
            if (tag.IndexOf("HDRender", StringComparison.OrdinalIgnoreCase) >= 0 ||
                tag.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0)
                return TexturePaintMaterialPipeline.HighDefinition;
            return TexturePaintMaterialPipeline.Unsupported;
        }

        private static TexturePaintMaterialPipeline DetectCurrentPipeline()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) return TexturePaintMaterialPipeline.Unsupported;
            string typeName = pipeline.GetType().FullName ?? pipeline.GetType().Name;
            if (typeName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
                return TexturePaintMaterialPipeline.Universal;
            if (typeName.IndexOf("HDRender", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0)
                return TexturePaintMaterialPipeline.HighDefinition;
            return TexturePaintMaterialPipeline.Unsupported;
        }

        private static Texture GetTexture(Material material, string property)
        {
            return material != null && !string.IsNullOrEmpty(property) && material.HasProperty(property)
                ? material.GetTexture(property)
                : null;
        }

        private static int CountFlags(UMAMaterial.TextureChannelUsage value)
        {
            uint bits = (uint)value;
            int count = 0;
            while (bits != 0)
            {
                bits &= bits - 1;
                count++;
            }
            return count;
        }

        private static float NeutralValue(UMAMaterial.TextureChannelUsage usage, int component)
        {
            if ((usage & UMAMaterial.TextureChannelUsage.Normal) != 0)
                return component == 2 ? 1f : component < 2 ? 0.5f : 1f;
            if ((usage & (UMAMaterial.TextureChannelUsage.Albedo | UMAMaterial.TextureChannelUsage.Opacity |
                          UMAMaterial.TextureChannelUsage.AmbientOcclusion |
                          UMAMaterial.TextureChannelUsage.Roughness)) != 0) return 1f;
            if ((usage & UMAMaterial.TextureChannelUsage.Smoothness) != 0) return 0f;
            if ((usage & (UMAMaterial.TextureChannelUsage.DetailNormalX |
                          UMAMaterial.TextureChannelUsage.DetailNormalY |
                          UMAMaterial.TextureChannelUsage.DetailAlbedo |
                          UMAMaterial.TextureChannelUsage.DetailSmoothness)) != 0) return 0.5f;
            return 0f;
        }

        private static string ComponentName(int component)
        {
            return component == 0 ? "R" : component == 1 ? "G" : component == 2 ? "B" : "A";
        }

        private static string DisplayProperty(TexturePaintMaterialChannelCapability channel)
        {
            return string.IsNullOrEmpty(channel.materialProperty) ? $"Channel {channel.index}" :
                channel.materialProperty;
        }

        private static string PipelineName(TexturePaintMaterialPipeline pipeline)
        {
            return pipeline == TexturePaintMaterialPipeline.Universal ? "URP" :
                pipeline == TexturePaintMaterialPipeline.HighDefinition ? "HDRP" : "Unsupported";
        }

        private static void Add(TexturePaintMaterialCapabilityDescriptor target,
            TexturePaintCapabilitySeverity severity, string code, string message)
        {
            target.AddDiagnostic(new TexturePaintCapabilityDiagnostic
            {
                severity = severity,
                code = code,
                message = message
            });
        }

        private static void Add(TexturePaintMaterialChannelCapability target,
            TexturePaintCapabilitySeverity severity, string code, string message)
        {
            target.AddDiagnostic(new TexturePaintCapabilityDiagnostic
            {
                severity = severity,
                code = code,
                message = message,
                materialChannelIndex = target.index
            });
        }
    }
}
#endif
