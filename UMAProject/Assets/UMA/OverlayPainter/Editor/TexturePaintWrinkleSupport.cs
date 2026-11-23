using System;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    /// <summary>
    /// Expression wrinkle services for Overlay Painter, built on UMA's wrinkle baker so painting and the baker use the
    /// same tangent frames: the per-texel slope frame of a surface's slot mesh, and the height a wrinkle map was made
    /// from (the "_height" texture kept next to the map by the baker, the source import and the wrinkle export).
    /// </summary>
    [InitializeOnLoad]
    internal sealed class TexturePaintWrinkleSupport : ITexturePaintWrinkleSupport
    {
        static TexturePaintWrinkleSupport()
        {
            TexturePaintWrinkles.Support = new TexturePaintWrinkleSupport();
        }

        public bool TryBuildSlopeFrame(TextureSet set, int width, int height, out Texture2D frame, out Texture2D coverage,
            out string error)
        {
            frame = null; coverage = null; error = null;
            if (width != height || width < 4)
            {
                error = $"Wrinkle Height needs a square wrinkle map; this one is {width} x {height}.";
                return false;
            }
            SlotDataAsset slot = FindSlot(set);
            if (slot == null)
            {
                error = "Wrinkle Height needs the slot's mesh data to work out its tangent frames, and this surface has none.";
                return false;
            }
            if (!CarriesWrinkleMap(set, slot))
            {
                // Saves the frame textures (tens of MB) on UDIM tiles such as the UMA 3 torso that have no wrinkle map.
                error = $"Tile {slot.udimTileNumber} ({slot.slotName}) has no wrinkle map, so it has no Wrinkle Height. " +
                    "Paint wrinkles on the tile that carries the wrinkle overlay (the UMA 3 head is UMA30_Body_UDIM1001).";
                return false;
            }

            int size = width, n = size * size;
            UMAWrinkleSurfaceMap map = UMAWrinkleMapBaker.BuildSurface(slot, size);
            float[] coefficients = UMAWrinkleSourceMaps.BuildSlopeFrame(map);
            Color[] framePixels = new Color[n];
            Color[] coveragePixels = new Color[n];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // The baker's rows start at the top and its second axis points down; GPU rows start at the bottom.
                int i = y * size + x, gpu = (size - 1 - y) * size + x;
                framePixels[gpu] = new Color(coefficients[i * 4], -coefficients[i * 4 + 1],
                    coefficients[i * 4 + 2], -coefficients[i * 4 + 3]);
                if (map.Covered[i]) coveragePixels[gpu] = new Color(1f, 0f, 0f, 1f);
                else if (map.Source[i] >= 0)
                {
                    int source = map.Source[i];
                    coveragePixels[gpu] = new Color(0f, source % size, size - 1 - source / size, 1f);
                }
            }
            frame = CreateData(set.Name + " Wrinkle Slope Frame", size, framePixels);
            coverage = CreateData(set.Name + " Wrinkle Coverage", size, coveragePixels);
            return true;
        }

        public Texture ResolveHeightSource(TextureSet set, TexturePaintMaterialChannelCapability wrinkleChannel)
        {
            if (wrinkleChannel == null) return null;
            Texture2D height = UMAWrinkleMapBaker.FindHeightSource(TexturePaintWrinkles.Original(wrinkleChannel.sourceTexture));
            if (height != null) return height;
            // A generated character reconstructs its sources; find the overlay that supplies the wrinkle map.
            for (int i = 0; set?.sources != null && i < set.sources.Count; i++)
            {
                Texture[] textures = set.sources[i]?.overlay?.asset?.textureList;
                if (textures == null || wrinkleChannel.index >= textures.Length) continue;
                height = UMAWrinkleMapBaker.FindHeightSource(textures[wrinkleChannel.index]);
                if (height != null) return height;
            }
            if (set?.surface?.standaloneSourceOverlay?.textureList is Texture[] standalone &&
                wrinkleChannel.index < standalone.Length)
                return UMAWrinkleMapBaker.FindHeightSource(standalone[wrinkleChannel.index]);
            return null;
        }

        public Texture ResolveLosslessSource(ReconstructedSurface surface, int channelIndex, Texture current)
        {
            Texture original = current != null && AssetDatabase.Contains(current) ? current : null;
            // A generated character's sources are reconstructed composites; use the overlay texture behind them.
            if (original == null && surface?.standaloneSourceOverlay?.textureList is Texture[] standalone &&
                channelIndex < standalone.Length) original = standalone[channelIndex];
            for (int f = 0; original == null && surface?.generatedMaterial?.materialFragments != null &&
                 f < surface.generatedMaterial.materialFragments.Count; f++)
            {
                UMAData.MaterialFragment fragment = surface.generatedMaterial.materialFragments[f];
                if (fragment?.slotData != null && !surface.ContainsSlot(fragment.slotData.slotName)) continue;
                for (int o = 0; original == null && fragment?.overlayList != null && o < fragment.overlayList.Count; o++)
                {
                    Texture[] textures = fragment.overlayList[o]?.asset?.textureList;
                    if (textures != null && channelIndex < textures.Length && textures[channelIndex] != null)
                        original = textures[channelIndex];
                }
            }
            string path = original != null ? AssetDatabase.GetAssetPath(original) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
            {
                name = original.name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            // 8-bit PNGs load exactly; the data is linear (zone ids, slopes), so no sRGB conversion either.
            if (!ImageConversion.LoadImage(copy, bytes, false) || copy.width != original.width || copy.height != original.height)
            {
                UnityEngine.Object.DestroyImmediate(copy);
                return null;
            }
            TexturePaintWrinkles.RegisterLosslessCopy(copy, original);
            return copy;
        }

        /// <summary>The head slot of a surface: the UMA 3 head (or its runtime-baked copy) first, else the only slot with a mesh.</summary>
        internal static SlotDataAsset FindSlot(TextureSet set)
        {
            SlotDataAsset first = null;
            int withMesh = 0;
            for (int i = 0; set?.surface?.slots != null && i < set.surface.slots.Count; i++)
            {
                SlotDataAsset asset = set.surface.slots[i]?.asset;
                if (asset?.meshData == null) continue;
                withMesh++;
                first ??= asset;
                string name = asset.slotName ?? string.Empty;
                if (name == UMAWrinkleZones.UMA3HeadSlotName ||
                    name.StartsWith(UMAWrinkleZones.UMA3HeadSlotName + "_baked_", StringComparison.Ordinal))
                    return asset;
            }
            return withMesh >= 1 ? first : null;
        }

        /// <summary>
        /// True for the UMA 3 head, a slot whose wrinkle map is a real texture (an asset or an overlay of a generated
        /// character), and a single slot that is not part of a UDIM group (painting a new wrinkle map from scratch).
        /// </summary>
        internal static bool CarriesWrinkleMap(TextureSet set, SlotDataAsset slot)
        {
            string name = slot.slotName ?? string.Empty;
            if (name == UMAWrinkleZones.UMA3HeadSlotName ||
                name.StartsWith(UMAWrinkleZones.UMA3HeadSlotName + "_baked_", StringComparison.Ordinal)) return true;
            TexturePaintMaterialCapabilityDescriptor descriptor = set.materialCapability;
            for (int c = 0; descriptor != null && c < descriptor.Channels.Count; c++)
            {
                TexturePaintMaterialChannelCapability channel = descriptor.Channels[c];
                if (!TexturePaintMaterialCapabilityService.HasWrinkleSlope(channel)) continue;
                Texture original = TexturePaintWrinkles.Original(channel.sourceTexture);
                if (original != null && AssetDatabase.Contains(original)) return true;
                for (int i = 0; set.sources != null && i < set.sources.Count; i++)
                {
                    Texture[] textures = set.sources[i]?.overlay?.asset?.textureList;
                    if (textures != null && channel.index < textures.Length && textures[channel.index] != null) return true;
                }
            }
            return !slot.IsUdimMember;
        }

        /// <summary>True when any surface in the store has expression wrinkle channels (for export defaults and help).</summary>
        internal static bool HasWrinkleSurfaces(TextureStore store)
        {
            for (int i = 0; store?.Sets != null && i < store.Sets.Count; i++)
            {
                TexturePaintMaterialCapabilityDescriptor descriptor = store.Sets[i]?.materialCapability;
                for (int c = 0; descriptor != null && c < descriptor.Channels.Count; c++)
                    if (TexturePaintMaterialCapabilityService.IsWrinkleChannel(descriptor.Channels[c])) return true;
            }
            return false;
        }

        /// <summary>
        /// True when a set was opened from a wrinkle overlay: its source overlay has textures only in the wrinkle channels
        /// (like UMA30_ExpressionWrinkles_UDIM1001). Such a session should export Wrinkle Maps Only.
        /// </summary>
        internal static bool IsWrinkleOverlaySession(TextureSet set)
        {
            OverlayDataAsset overlay = set?.surface?.standaloneSourceOverlay;
            TexturePaintMaterialCapabilityDescriptor descriptor = set?.materialCapability;
            if (overlay?.textureList == null || descriptor == null) return false;
            bool wrinkle = false;
            for (int c = 0; c < descriptor.Channels.Count; c++)
            {
                TexturePaintMaterialChannelCapability channel = descriptor.Channels[c];
                bool hasTexture = channel.index < overlay.textureList.Length && overlay.textureList[channel.index] != null;
                if (!hasTexture) continue;
                if (!TexturePaintMaterialCapabilityService.IsWrinkleChannel(channel)) return false;
                wrinkle = true;
            }
            return wrinkle;
        }

        /// <summary>
        /// Makes an avatar use an exported wrinkle overlay: sets (or adds) its UMA Expression Wrinkles component's
        /// overlay, with Undo. Returns a short report.
        /// </summary>
        internal static string UseOnAvatar(UMA.CharacterSystem.DynamicCharacterAvatar avatar, OverlayDataAsset overlay)
        {
            if (avatar == null || overlay == null) return null;
            UMAExpressionWrinkles component = avatar.GetComponent<UMAExpressionWrinkles>();
            if (component == null)
            {
                component = Undo.AddComponent<UMAExpressionWrinkles>(avatar.gameObject);
                if (component.profile == null)
                    component.profile = AssetDatabase.LoadAssetAtPath<UMAWrinkleProfile>(UMAWrinkleZones.UMA3ProfilePath);
            }
            else Undo.RecordObject(component, "Use Exported Wrinkle Maps");
            component.wrinkleOverlay = overlay;
            EditorUtility.SetDirty(component);
            bool transient = (component.hideFlags & HideFlags.DontSave) != 0;
            return $"{avatar.name} now uses '{overlay.overlayName}' for expression wrinkles" +
                (transient ? " until it is rebuilt (its wrinkles come from UMA Settings: All Avatars)." : ". Rebuild the avatar to see it.");
        }

        /// <summary>
        /// Makes every avatar that gets wrinkles from the project setting (All Avatars) use an exported wrinkle overlay.
        /// Saves UMA Settings only when it had no unsaved edits of its own.
        /// </summary>
        internal static string UseForAllAvatars(OverlayDataAsset overlay)
        {
            if (overlay == null) return null;
            UMASettings settings = UMASettings.GetSettings();
            if (settings == null) return "UMA Settings was not found, so the wrinkle overlay was not set there.";
            if (EditorUtility.IsDirty(settings))
                return "UMA Settings has unsaved changes, so the wrinkle overlay was not set there. Set 'Wrinkle Overlay' in UMA Settings yourself.";
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("wrinkleOverlay").objectReferenceValue = overlay;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(settings);
            return $"UMA Settings now uses '{overlay.overlayName}' as the wrinkle overlay" +
                (settings.expressionWrinkles == UMASettings.ExpressionWrinkleMode.AllAvatars ? "." :
                    " (it applies when Expression Wrinkles is set to All Avatars).");
        }

        private static Texture2D CreateData(string name, int size, Color[] pixels)
        {
            // Half floats hold the frame to about 0.1 % and texel coordinates exactly up to 2048.
            var format = size <= 2048 ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat;
            var texture = new Texture2D(size, size, format, false, true)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
