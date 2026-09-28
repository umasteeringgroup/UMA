using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    /// <summary>Build-local crops; never modifies slots, overlay assets, or source UVs.</summary>
    public static class UMASourceUVCropping
    {
        public enum SlotMode { Automatic, Disabled }
        public static readonly Rect FullRect = new Rect(0, 0, 1, 1);

        public static Rect ExpandAtlasRegion(Rect packed, Rect crop)
        {
            if (crop.width <= 0 || crop.height <= 0 || crop == FullRect) return packed;
            var size = new Vector2(packed.width / crop.width, packed.height / crop.height);
            return new Rect(packed.position - Vector2.Scale(crop.position, size), size);
        }

        private sealed class Region
        {
            public bool valid = true, hasBounds;
            public Rect bounds;
            public int width = int.MaxValue, height = int.MaxValue;
        }

        public static void Prepare(UMAData.GeneratedMaterial material, UMAGeneratorBase generator, UMAData data)
        {
            foreach (var fragment in material.materialFragments) fragment.sourceUVRect = FullRect;
            if (!generator.enableSourceUVCropping || material.umaMaterial == null ||
                material.umaMaterial.materialType != UMAMaterial.MaterialType.Atlas ||
                data.AtlasUpdated != null && data.AtlasUpdated.HasListeners ||
                !UMAResourceReuse.CanUseEarlyAtlasLookup(generator.textureMerge)) return;

            var regions = new Dictionary<UMAData.MaterialFragment, Region>();
            foreach (var fragment in material.materialFragments)
            {
                var owner = fragment.isRectShared ? fragment.rectFragment : fragment;
                if (owner == null) continue;
                if (!regions.TryGetValue(owner, out var region)) regions.Add(owner, region = new Region());
                if (!TryBounds(fragment, region, generator.textureMerge, out var bounds)) { region.valid = false; continue; }
                region.bounds = region.hasBounds ? Rect.MinMaxRect(Mathf.Min(region.bounds.xMin, bounds.xMin),
                    Mathf.Min(region.bounds.yMin, bounds.yMin), Mathf.Max(region.bounds.xMax, bounds.xMax),
                    Mathf.Max(region.bounds.yMax, bounds.yMax)) : bounds;
                region.hasBounds = true;
            }
            foreach (var fragment in material.materialFragments)
            {
                var owner = fragment.isRectShared ? fragment.rectFragment : fragment;
                if (owner == null || !regions.TryGetValue(owner, out var region) || !region.valid || !region.hasBounds ||
                    region.width == int.MaxValue || region.height == int.MaxValue) continue;
                fragment.sourceUVRect = Pad(region.bounds, region.width, region.height, generator.sourceUVCropPadding);
            }
        }

        public static Rect Pad(Rect bounds, int width, int height, int padding)
        {
            if (width <= 0 || height <= 0) return FullRect;
            padding = Mathf.Max(4, padding);
            return Rect.MinMaxRect(Mathf.Max(0, Mathf.Floor(bounds.xMin * width) - padding) / width,
                Mathf.Max(0, Mathf.Floor(bounds.yMin * height) - padding) / height,
                Mathf.Min(width, Mathf.Ceil(bounds.xMax * width) + padding) / width,
                Mathf.Min(height, Mathf.Ceil(bounds.yMax * height) + padding) / height);
        }

        private static bool TryBounds(UMAData.MaterialFragment fragment, Region region, TextureMerge merge, out Rect bounds)
        {
            bounds = default;
            var slot = fragment.slotData;
            if (slot?.asset == null || slot.asset.sourceUVCropping == SlotMode.Disabled || slot.UVRemapped ||
                slot.useAtlasOverlay || slot.meshModifiers != null && slot.meshModifiers.Count != 0 || fragment.isNoTextures ||
                UMATextureEvent.HasAnyListeners(slot.asset.SlotAtlassed) ||
                UMATextureEvent.HasAnyListeners(slot.asset.SlotBeginProcessing) ||
                UMATextureEvent.HasAnyListeners(slot.asset.SlotProcessed) ||
                !SafeMaterial(fragment.umaMaterial ?? slot.material) ||
                !SafeCompositor(merge, fragment) ||
                !UMAMeshPreparation.TryGet(slot.asset.meshData, out var prepared) ||
                !prepared.TryGetSourceUVBounds(slot.asset.subMeshIndex, out bounds) || fragment.overlayData == null ||
                fragment.overlayData.Length == 0) return false;
            if (fragment.overlayData.Length > 1 && (fragment.AdditionalOverlays == null ||
                fragment.AdditionalOverlays.Length < fragment.overlayData.Length - 1)) return false;
            for (int i = 0; i < fragment.overlayData.Length; i++)
            {
                var overlay = fragment.overlayData[i];
                if (overlay?.asset == null || !overlay.asset.allowSourceUVCropping || overlay.instanceTransformed ||
                    overlay.asset.overlayType != OverlayDataAsset.OverlayType.Normal) return false;
                var rect = overlay.rect;
                if (rect.width < 0 || rect.height < 0 || !float.IsFinite(rect.x) || !float.IsFinite(rect.y) ||
                    !float.IsFinite(rect.width) || !float.IsFinite(rect.height)) return false;
                var properties = overlay.colorData?.PropertyBlock?.shaderProperties;
                if (properties != null)
                    foreach (var property in properties)
                        if (property != null && (!(property is UMAColorProperty) ||
                            property.name != null && property.name.EndsWith("_ST", System.StringComparison.Ordinal))) return false;
                var resolved = i == 0 ? fragment.baseOverlay : fragment.AdditionalOverlays?[i - 1];
                var textures = resolved?.textureList;
                if (textures == null) return false;
                for (int c = 0; c < textures.Length; c++)
                {
                    if (overlay.GetOverlayBlend(c) != OverlayDataAsset.OverlayBlend.Normal) return false;
                    var texture = textures[c];
                    if (texture == null) continue;
                    region.width = Mathf.Min(region.width, texture.width);
                    region.height = Mathf.Min(region.height, texture.height);
                }
                var alpha = resolved.alphaTexture;
                if (alpha != null) { region.width = Mathf.Min(region.width, alpha.width); region.height = Mathf.Min(region.height, alpha.height); }
            }
            return true;
        }

        private static bool SafeMaterial(UMAMaterial material)
        {
            if (material == null || material.materialType != UMAMaterial.MaterialType.Atlas || material.channels == null ||
                !SafeSampling(material.material, material.supportsSourceUVCropping) ||
                material.secondPass != null && !SafeSampling(material.secondPass, material.supportsSourceUVCropping)) return false;
            foreach (var channel in material.channels)
                if (channel.channelType != UMAMaterial.ChannelType.DiffuseTexture && channel.channelType != UMAMaterial.ChannelType.NormalMap &&
                    channel.channelType != UMAMaterial.ChannelType.DetailNormalMap && channel.channelType != UMAMaterial.ChannelType.Texture) return false;
            return true;
        }

        private static bool SafeSampling(Material material, bool explicitlySupported)
        {
            if (material == null || material.shader == null) return false;
            string name = material.shader.name;
            if (!explicitlySupported && name != "Standard" && name != "Standard (Specular setup)" &&
                name != "Universal Render Pipeline/Lit" && name != "Universal Render Pipeline/Simple Lit" &&
                name != "Universal Render Pipeline/Unlit") return false;
            if (material.IsKeywordEnabled("_PARALLAXMAP")) return false;
            if (material.HasProperty("_UVSec") && material.GetFloat("_UVSec") != 0) return false;
            var shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
                if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                {
                    int id = shader.GetPropertyNameId(i);
                    if (material.GetTextureScale(id) != Vector2.one || material.GetTextureOffset(id) != Vector2.zero) return false;
                }
            return true;
        }

        private static bool SafeCompositor(TextureMerge merge, UMAData.MaterialFragment fragment)
        {
            var material = fragment.umaMaterial ?? fragment.slotData.material;
            foreach (var channel in material.channels)
            {
                Shader shader;
                List<TextureMerge.BlendModeShaders> blends;
                List<UMAPostProcess> post;
                switch (channel.channelType)
                {
                    case UMAMaterial.ChannelType.DiffuseTexture: shader = merge.diffuseShader; blends = merge.DiffuseBlendModeShaders; post = merge.diffusePostProcesses; break;
                    case UMAMaterial.ChannelType.NormalMap: shader = merge.normalShader; blends = merge.NormalBlendModeShaders; post = merge.normalPostProcesses; break;
                    case UMAMaterial.ChannelType.DetailNormalMap: shader = merge.detailNormalShader; blends = null; post = merge.detailNormalPostProcesses; break;
                    default: shader = merge.dataShader; blends = merge.DataBlendModeShaders; post = merge.dataPostProcesses; break;
                }
                if (!UMAResourceReuse.IsStandardAtlasShader(shader) || !SafePostProcesses(post) ||
                    channel.channelType != UMAMaterial.ChannelType.DetailNormalMap &&
                    fragment.AdditionalOverlays != null && fragment.AdditionalOverlays.Length > 0 && !SafeBlend(blends)) return false;
            }
            return merge.transparentPrefillShader == null || merge.transparentPrefillShader.name == "Hidden/UMA/TransparentPrefill";
        }

        private static bool SafeBlend(List<TextureMerge.BlendModeShaders> shaders) => shaders != null && shaders.Count > 0 &&
            UMAResourceReuse.IsStandardAtlasShader(shaders[0].Combiner);

        private static bool SafePostProcesses(List<UMAPostProcess> processes)
        {
            if (processes == null) return false;
            foreach (var process in processes)
                if (process == null || process.GetType() != typeof(UMAPostProcess) || process.shader == null ||
                    process.shader.name != "UMA/NormalSwizzleShader") return false;
            return true;
        }

        // Destination coordinates have top-left origin; source texture UVs have bottom-left origin.
        public static bool ClipDraw(Rect original, Rect clip, out Rect destination, out Rect source)
        {
            destination = Rect.MinMaxRect(Mathf.Max(original.xMin, clip.xMin), Mathf.Max(original.yMin, clip.yMin),
                Mathf.Min(original.xMax, clip.xMax), Mathf.Min(original.yMax, clip.yMax));
            source = FullRect;
            if (original.width <= 0 || original.height <= 0 || destination.width <= 0 || destination.height <= 0) return false;
            source = new Rect((destination.xMin - original.xMin) / original.width,
                (original.yMax - destination.yMax) / original.height, destination.width / original.width, destination.height / original.height);
            return true;
        }
    }
}
