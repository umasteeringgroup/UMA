using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    public static partial class UMAResourceReuse
    {
        private sealed class Dependency
        {
            internal readonly string Id = Guid.NewGuid().ToString("N");
            internal readonly long Number = ++nextDependencyNumber;
            internal long Revision;
            internal bool Registered;
            internal long AtlasSignatureToken;
            internal int AtlasSignatureStart, AtlasSignatureLength;
            internal bool AtlasSignatureInput;
#if UNITY_EDITOR
            internal int Epoch = -1, DirtyCount;
            internal Hash128 EditorHash;
#endif
        }
        private static long nextDependencyNumber;
        private static int textureInputEpoch;
        private static readonly ConditionalWeakTable<UnityEngine.Object, Dependency> dependencies = new ConditionalWeakTable<UnityEngine.Object, Dependency>();

        /// <summary>Call after changing hidden atlas dependencies. Existing leases stay valid.</summary>
        public static void InvalidateTextureInputs() { unchecked { textureInputEpoch++; } UMANPCBuildHandle.InvalidateAll(); }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void WatchTextureSourceEdits()
        {
            UnityEditor.EditorApplication.projectChanged -= InvalidateTextureInputs;
            UnityEditor.EditorApplication.projectChanged += InvalidateTextureInputs;
            UnityEditor.Undo.undoRedoPerformed -= InvalidateTextureInputs;
            UnityEditor.Undo.undoRedoPerformed += InvalidateTextureInputs;
        }
#endif

        private static Dependency GetDependency(UnityEngine.Object asset, Dependency cached = null)
        {
            var dependency = cached ?? dependencies.GetOrCreateValue(asset);
#if UNITY_EDITOR
            int dirty = UnityEditor.EditorUtility.GetDirtyCount(asset);
            if (dependency.Epoch != textureInputEpoch || dependency.DirtyCount != dirty)
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(asset);
                dependency.EditorHash = string.IsNullOrEmpty(path) ? default : UnityEditor.AssetDatabase.GetAssetDependencyHash(path);
                dependency.Epoch = textureInputEpoch; dependency.DirtyCount = dirty;
            }
#endif
            return dependency;
        }

        private static Hash128 TextureContentsHash(Texture texture)
        {
#if UNITY_EDITOR
            return texture.imageContentsHash;
#else
            // Player builds track the same object through its dependency number,
            // update count, and any caller-supplied revision.
            return default;
#endif
        }

        private static bool NeedsTextureRevision(Texture texture, Dependency dependency, Hash128 contents)
        {
            if (dependency.Registered || texture == Texture2D.whiteTexture ||
                texture == Texture2D.blackTexture || texture == Texture2D.grayTexture ||
                texture == Texture2D.normalTexture) return false;
            // GPU writes do not necessarily increment updateCount automatically.
            if (texture is RenderTexture) return true;
#if UNITY_EDITOR
            return contents == default;
#else
            return false;
#endif
        }

        private sealed class ShaderLayout
        {
            internal int Epoch = -1;
            internal int[] Ids;
            internal string[] Names;
            internal ShaderPropertyType[] Types;
            internal Dictionary<string, int> PropertyIds;
            internal Dictionary<string, ShaderPropertyType> PropertyTypes;
            internal bool StandardAtlas, StandardPostprocess;
        }
        private static readonly ConditionalWeakTable<Shader, ShaderLayout> shaderLayouts = new ConditionalWeakTable<Shader, ShaderLayout>();
        private static ShaderLayout GetShaderLayout(Shader shader)
        {
            var layout = shaderLayouts.GetOrCreateValue(shader);
            if (layout.Epoch == textureInputEpoch && layout.Ids != null) return layout;
            int count = shader.GetPropertyCount();
            string name = shader.name;
            layout.StandardAtlas = name == "UMA/Atlas/AtlasDiffuseShader" || name == "UMA/Atlas/AtlasShaderNormal" ||
                name == "UMA/Atlas/AtlasShaderNew" || name == "UMA/Atlas/AtlasShaderNormal32" || name == "UMA/AtlasDetailShaderNormal";
            layout.StandardPostprocess = name.StartsWith("UMA/Atlas", StringComparison.Ordinal) || name == "UMA/NormalSwizzleShader";
            layout.Ids = new int[count]; layout.Names = new string[count]; layout.Types = new ShaderPropertyType[count];
            layout.PropertyIds = new Dictionary<string, int>(count, StringComparer.Ordinal);
            layout.PropertyTypes = new Dictionary<string, ShaderPropertyType>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                layout.Ids[i] = shader.GetPropertyNameId(i);
                layout.Names[i] = shader.GetPropertyName(i);
                layout.PropertyIds[layout.Names[i]] = layout.Ids[i];
                if (layout.Names[i] != "_MainTex" && layout.Names[i] != "_ExtraTex" &&
                    layout.Names[i] != "_Color" && layout.Names[i] != "_AdditiveColor") layout.StandardAtlas = false;
                layout.Types[i] = shader.GetPropertyType(i);
                layout.PropertyTypes[layout.Names[i]] = layout.Types[i];
            }
            layout.Epoch = textureInputEpoch;
            return layout;
        }

        /// <summary>
        /// Register/invalidate runtime-written input textures or deterministic custom atlas shaders.
        /// Increment revision after every write or change to otherwise hidden shader dependencies.
        /// Source registration does NOT transfer ownership to UMA.
        /// </summary>
        public static void SetDependencyRevision(UnityEngine.Object source, long revision)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var dependency = dependencies.GetOrCreateValue(source);
            if (!dependency.Registered || dependency.Revision != revision) UMANPCBuildHandle.InvalidateAll();
            dependency.Revision = revision; dependency.Registered = true;
        }

        internal sealed partial class Description
        {
            internal void Asset(UnityEngine.Object asset, bool inputTexture = false, bool deterministicShader = false)
            {
                Value(asset != null);
                if (asset == null) return;
                var dependency = GetDependency(asset);
                Value(dependency.Id); Value(dependency.Revision);
                if (deterministicShader && asset is Shader shader && !dependency.Registered &&
                    !(shader.name.StartsWith("UMA/Atlas", StringComparison.Ordinal) || shader.name == "Hidden/UMA/TransparentPrefill" || shader.name == "UMA/NormalSwizzleShader"))
                    throw new NotSupportedException("Custom atlas shader needs a dependency revision: " + shader.name);
#if UNITY_EDITOR
                Value(dependency.EditorHash.ToString());
#endif
                if (asset is Texture texture)
                {
                    var contents = TextureContentsHash(texture);
                    if (inputTexture && NeedsTextureRevision(texture, dependency, contents))
                        throw new NotSupportedException("Runtime input texture needs a dependency revision: " + texture.name);
                    Value(texture.updateCount); Value(contents.ToString());
                    Value(texture.width); Value(texture.height); Value(texture.graphicsFormat);
                    Value(texture.wrapModeU); Value(texture.wrapModeV); Value(texture.wrapModeW);
                    Value(texture.filterMode); Value(texture.anisoLevel);
                    // Atlas drawing explicitly sets the source mip bias; the sharper-fit option
                    // is in the atlas key instead of this mutable scratch sampling value.
                }
            }

            internal void Material(Material material, bool atlasDraw = false, bool advancedBlend = false, UMAAtlasBinding[] atlases = null)
            {
                Value(material != null);
                if (material == null) return;
                Asset(material.shader, deterministicShader: atlasDraw);
                Value(material.renderQueue); Value(material.enableInstancing); Value(material.doubleSidedGI);
                Value(material.globalIlluminationFlags);
                Value(material.GetTag("RenderType", false)); Value(material.GetTag("Queue", false));
                Value(material.GetTag("DisableBatching", false)); Value(material.GetTag("ForceNoShadowCasting", false));
                Value(material.GetTag("IgnoreProjector", false)); Value(material.GetTag("RenderPipeline", false));
                Value(material.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal).ToArray());
                var shader = material.shader;
                var layout = GetShaderLayout(shader);
                Value(layout.Ids.Length);
                for (int i = 0; i < layout.Ids.Length; i++)
                {
                    string property = layout.Names[i];
                    int propertyId = layout.Ids[i];
                    Value(property);
                    // This is the temporary framebuffer copy created by DrawAllRects, not
                    // a source dependency. Its contents follow the preceding draw commands.
                    if (advancedBlend && property == "_BaseTex") continue;
                    switch (layout.Types[i])
                    {
                        case ShaderPropertyType.Color: Value(material.GetColor(propertyId)); break;
                        case ShaderPropertyType.Vector: Value(material.GetVector(propertyId)); break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range: Value(material.GetFloat(propertyId)); break;
                        case ShaderPropertyType.Int: Value(material.GetInteger(propertyId)); break;
                        case ShaderPropertyType.Texture:
                            var texture = material.GetTexture(propertyId);
                            UMAAtlasBinding binding = atlases?.FirstOrDefault(a => a != null && a.Property == property && a.Atlas.Texture == texture);
                            // The same logical image remains the same dependency during
                            // asynchronous RT -> Texture2D conversion.
                            Value(binding != null);
                            Asset(binding != null ? binding.Atlas : texture, atlasDraw);
                            Value(material.GetTextureScale(propertyId)); Value(material.GetTextureOffset(propertyId)); break;
                        default: throw new NotSupportedException("Unsupported shader property: " + property);
                    }
                }
                for (int i = 0; i < material.passCount; i++)
                {
                    string pass = material.GetPassName(i); Value(pass); Value(material.GetShaderPassEnabled(pass));
                }
            }

            internal void MaterialInputs(UMAData.GeneratedMaterial generated)
            {
                Value(generated.materialFragments.Count);
                foreach (var fragment in generated.materialFragments)
                {
                    Value(fragment.overlayData?.Length ?? 0);
                    if (fragment.overlayData == null) continue;
                    foreach (var overlay in fragment.overlayData)
                    {
                        var block = overlay?.colorData?.PropertyBlock;
                        Value(block?.shaderProperties?.Count ?? 0);
                        if (block?.shaderProperties == null) continue;
                        foreach (var property in block.shaderProperties)
                        {
                            var type = property?.GetType();
                            if (property == null) { Value(null); continue; }
                            if (type == typeof(UMAFloatProperty) || type == typeof(UMAColorProperty) ||
                                type == typeof(UMAVectorProperty) || type == typeof(UMAVectorArrayProperty) ||
                                type == typeof(UMAFloatArrayProperty) || type == typeof(UMAIntProperty) ||
                                type == typeof(UMAMatrixProperty) || type == typeof(UMAMatrixArrayProperty) ||
                                type == typeof(UMAOverlayTransformProperty)) Value(property);
                            else if (type == typeof(UMATextureProperty))
                            {
                                Value(type.FullName); Value(property.name);
                                Asset(((UMATextureProperty)property).Value);
                            }
                            else throw new NotSupportedException("Custom/buffer material properties require private material instances.");
                        }
                    }
                }
            }
        }

        internal static UMAGeneratedResourceKey DescribeAtlas(UMAData data, TextureMerge merge,
            UMAMaterial material, int channel, UMAGeneratorBase generator, int width, int height,
            int outputWidth, int outputHeight, RenderTextureFormat format, bool convert, Color background)
        {
            // AtlasUpdated callbacks can stamp arbitrary per-avatar data into the destination.
            // Do not skip or replay them on somebody else's immutable atlas.
            // Serialized UnityEvents can exist with no listeners (also after unsubscription).
            if (data.AtlasUpdated != null && data.AtlasUpdated.HasListeners)
                throw new NotSupportedException("AtlasUpdated callbacks require private atlases.");
            using (var d = new Description())
            {
                d.Value(width); d.Value(height); d.Value(outputWidth); d.Value(outputHeight); d.Value(format);
                d.Value(convert); d.Value(generator.qualityTextures.Mips(convert ? generator.convertMipMaps : material.generateMipMaps));
                d.Value(generator.qualityTextures.EffectiveCompression(convert, format, outputWidth, outputHeight));
                d.Value(generator.useAsyncConversion); d.Value(generator.SharperFitTextures); d.Value(background);
                d.Value(QualitySettings.activeColorSpace); d.Value(SystemInfo.graphicsDeviceType);
                d.Value(generator.qualityTextures.Anisotropy(material)); d.Value(generator.qualityTextures.MipBias(material)); d.Value(generator.qualityTextures.Filter(material));
                d.Value(material.channels[channel].channelType);
                d.Value(merge.CacheRectCount);
                var rects = merge.GetPreviewRects();
                for (int i = 0; i < merge.CacheRectCount; i++)
                {
                    var rect = rects[i];
                    d.Asset(rect.tex, true); d.Material(rect.mat, true, rect.advancedBlending);
                    d.Value(rect.rect); d.Value(rect.transform); d.Value(rect.rotation); d.Value(rect.scale); d.Value(rect.position);
                    d.Value(rect.sourceCropped); if (rect.sourceCropped) d.Value(rect.sourceClipRect);
                    d.Value(rect.advancedBlending); d.Value(rect.transparentPrefill); d.Value(rect.transparentPrefillColor);
                    if (rect.transparentPrefill) d.Asset(merge.transparentPrefillShader, deterministicShader: true);
                }
                List<UMAPostProcess> processes;
                switch (material.channels[channel].channelType)
                {
                    case UMAMaterial.ChannelType.DiffuseTexture: processes = merge.diffusePostProcesses; break;
                    case UMAMaterial.ChannelType.NormalMap: processes = merge.normalPostProcesses; break;
                    case UMAMaterial.ChannelType.DetailNormalMap: processes = merge.detailNormalPostProcesses; break;
                    default: processes = merge.dataPostProcesses; break;
                }
                d.Value(processes.Count);
                foreach (var process in processes)
                {
                    if (process != null && process.GetType() != typeof(UMAPostProcess)) throw new NotSupportedException("Custom atlas postprocess.");
                    d.Asset(process?.shader, deterministicShader: true);
                }
                return d.Key("UMA.Atlas.channel.v1");
            }
        }
    }
}
