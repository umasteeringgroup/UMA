using System.Collections.Generic;
using System.Linq;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    /// <summary>
    /// The Overlay Painter as UMA's trim painter (<see cref="UMATrimPainting"/>): another tool hands it a slot,
    /// its overlay and trims laid out in the slot's UVs, and it opens the slot with one editable path layer per
    /// trim, drawn by one of its path generators (zippers, stitching, seams, piping, hems).
    /// </summary>
    [InitializeOnLoad]
    internal sealed class TexturePaintTrimPainter : IUMATrimPainter
    {
        static TexturePaintTrimPainter() => UMATrimPainting.Register(new TexturePaintTrimPainter());

        public string Name => "Overlay Painter";

        // Generators that draw on skin or hair (scars, tattoos, eyebrows) are not trims for clothes.
        public IReadOnlyList<UMATrimConstruction> Constructions =>
            TexturePaintPathGenerators.All
                .Where(g => g != null && !TexturePaintPathGenerators.IsTattoo(g.Id) && !IsForSkinOrHair(g.MenuPath))
                .Select(g => new UMATrimConstruction(g.Id, g.MenuPath))
                .ToList();

        private static bool IsForSkinOrHair(string menuPath) =>
            string.IsNullOrEmpty(menuPath) || menuPath.StartsWith("Skin/") || menuPath.StartsWith("Hair/");

        public bool PaintTrims(SlotDataAsset slot, OverlayDataAsset overlay, IReadOnlyList<UMATrimPath> trims, out string message)
        {
            message = null;
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData)) { message = "There is no slot to paint."; return false; }
            if (overlay == null || overlay.material == null) { message = "The slot's overlay has no UMA material."; return false; }
            UMAMaterial material = overlay.material;
            int channels = material.channels != null ? material.channels.Length : 0;
            if (channels == 0) { message = "The overlay's UMA material declares no channels."; return false; }
            if (overlay.textureList == null || overlay.textureList.Length < channels)
            {
                message = $"The overlay '{overlay.name}' has {overlay.textureList?.Length ?? 0} textures; its material needs {channels}.";
                return false;
            }
            Texture first = overlay.textureList[0];
            int resolution = first != null ? Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(first.width, first.height)), 512, 4096) : 2048;
            var context = new TexturePaintLaunchContext
            {
                kind = TexturePaintLaunchKind.StandaloneSlot,
                sourceMode = TexturePaintStandaloneSourceMode.OverlayDataAsset,
                selectedSlot = slot,
                umaMaterial = material,
                selectedSlotGuid = GuidFor(slot),
                umaMaterialGuid = GuidFor(material),
                udimGroupId = slot.IsUdimMember ? slot.udimGroupId : string.Empty,
                resolution = resolution
            };
            context.members.Add(new TexturePaintStandaloneMemberContext
            {
                slot = slot,
                overlay = overlay,
                slotGuid = GuidFor(slot),
                overlayGuid = GuidFor(overlay),
                tileNumber = slot.IsUdimMember ? slot.udimTileNumber : 0,
                sourceFingerprint = Fingerprint(slot, overlay)
            });
            TexturePaintStageWindow stage = TexturePaintStageWindow.ShowStage(context);
            if (stage == null) { message = "The Overlay Painter could not open the slot."; return false; }
            int added = stage.ImportTrimPaths(trims, out message);
            if (trims != null && trims.Count > 0 && added == 0 && string.IsNullOrEmpty(message)) message = "No trim could be laid out.";
            return true;
        }

        private static string GuidFor(Object asset) =>
            asset == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

        // As the slot setup window records it, so reopening the document recognises the same sources.
        private static string Fingerprint(SlotDataAsset slot, OverlayDataAsset overlay)
        {
            string slotPath = AssetDatabase.GetAssetPath(slot);
            string overlayPath = AssetDatabase.GetAssetPath(overlay);
            return Hash128.Compute(string.Join("|", GuidFor(slot), slot != null ? slot.udimTileNumber.ToString() : "0",
                string.IsNullOrEmpty(slotPath) ? string.Empty : AssetDatabase.GetAssetDependencyHash(slotPath).ToString(),
                GuidFor(overlay), string.IsNullOrEmpty(overlayPath) ? string.Empty : AssetDatabase.GetAssetDependencyHash(overlayPath).ToString())).ToString();
        }
    }

    public sealed partial class TexturePaintStageWindow
    {
        /// <summary>
        /// Adds one editable UV path layer per trim, drawn by the trim's generator, as Automatic Seams does for
        /// UV contours. Returns how many were added; <paramref name="message"/> names any that were not.
        /// </summary>
        internal int ImportTrimPaths(IReadOnlyList<UMATrimPath> trims, out string message)
        {
            message = null;
            if (trims == null || trims.Count == 0) return 0;
            List<TextureSet> sets = controller?.Textures?.Sets?.ToList();
            if (sets == null || sets.Count == 0) { message = "The Overlay Painter opened without a surface to paint."; return 0; }
            TextureSet set = sets[0];
            var skipped = new List<string>();
            int added = 0;
            selectedSurface = sets.IndexOf(set);
            TexturePaintLayer group = null;
            foreach (UMATrimPath trim in trims)
            {
                if (trim?.uv == null || trim.uv.Count < 2) continue;
                ITexturePaintPathGenerator definition = TexturePaintPathGenerators.Find(trim.constructionId);
                if (definition == null) { skipped.Add(trim.name); continue; }
                selectedSurface = sets.IndexOf(set);
                // A new layer goes into the selected group: the trim group (made with the first trim), selected for each.
                group ??= TrimGroup(set);
                set.activeLayerIndex = set.layers.IndexOf(group);
                BeginLayerCreationUndo("Create Trim Path");
                var layer = CreateSplineLayer(set);
                pathGenerator = definition.CreateSettings();
                layer.name = !string.IsNullOrEmpty(trim.name) ? trim.name : definition.MenuPath.Substring(definition.MenuPath.LastIndexOf('/') + 1);
                spline.worldSpace = false; spline.closed = trim.closed; spline.useBezier = false;
                foreach (Vector2 uv in trim.uv) spline.AddPoint(new Vector3(uv.x, uv.y, 0f), uv, selectedSurface, -1, Vector3.forward);
                ActiveBrush.size = Mathf.Clamp(trim.widthUV, 0.0001f, 0.2f) * 0.5f;
                // As a path generator made from the layer menu starts, so the trim does not take the last brush used.
                ActiveBrush.hardness = 1f; ActiveBrush.flow = 1f; ActiveBrush.rotation = 0f;
                ActiveBrush.blendMode = TexturePaintBlendMode.Normal;
                pathStartFade = pathEndFade = pathSideFadeExtra = 0f;
                pathStartFadeCurve = pathEndFadeCurve = pathSideFadeCurve = null;
                strength = 1f; tool = TexturePaintTool.Paint; paintSource = TexturePaintBrushSource.Color;
                sourceMode = TexturePaintSourceMode.SourceOverlay;
                EnsurePathGeneratorChannels(set, layer, pathGenerator);
                CaptureSplineSettings(layer);
                CompleteLayerCreationUndo(layer);
                ApplySpline();
                added++;
            }
            // The group selected: every trim shows, and clicking one edits it (Edit Paths Together).
            if (group != null)
            {
                set.activeLayerIndex = set.layers.IndexOf(group);
                SyncActiveLayerSelection(set);
            }
            if (added > 0) { MarkDocumentDirty(); RepaintAll(); }
            if (skipped.Count > 0) message = "No path generator for: " + string.Join(", ", skipped) + ".";
            return added;
        }

        /// <summary>The name of the group trims are put in.</summary>
        internal const string TrimGroupName = "Trim";

        /// <summary>The surface's trim group at its top level, made (with Edit Paths Together on) when it has none.</summary>
        private TexturePaintLayer TrimGroup(TextureSet set)
        {
            foreach (TexturePaintLayer layer in set.layers)
                if (layer != null && layer.kind == TexturePaintLayerKind.Group && string.IsNullOrEmpty(layer.parentId) && layer.name == TrimGroupName)
                    return layer;
            // Made at the top level, not inside whatever group happens to be selected.
            set.activeLayerIndex = -1;
            BeginLayerCreationUndo("Create Trim Group");
            TexturePaintLayer group = set.AddGroup(TrimGroupName);
            group.editPathsTogether = true;
            CompleteLayerCreationUndo(group);
            return group;
        }
    }
}
