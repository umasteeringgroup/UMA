using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public partial class TexturePaintStageWindow
    {
        internal static TexturePaintLayerKind SpriteMaterialLayerKind(string category)
            => category == OverlayPainterSpriteSet.Projection ? TexturePaintLayerKind.Projection
             : category == OverlayPainterSpriteSet.Path ? TexturePaintLayerKind.Spline : TexturePaintLayerKind.Fill;

        private bool HandleSpriteMaterialDrop(Event current, bool targetHover)
        {
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform) return false;
            if (DragAndDrop.GetGenericData(TexturePaintSpriteMaterialsWindow.DragKey) is not TexturePaintSpriteMaterialsWindow.DragMaterial item)
                return false;
            var set = targetHover && hoverSurface != null ? controller.Textures.FindSet(hoverSurface.index) : null;
            bool valid = set != null && !IsPersistenceActive && item.set != null && item.set.HasCategory(item.category) &&
                (uint)item.index < (uint)OverlayPainterSpriteSetEditorUtility.GetCommonSpriteCount(item.set);
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (current.type == EventType.DragPerform && valid)
            {
                DragAndDrop.AcceptDrag();
                DragAndDrop.SetGenericData(TexturePaintSpriteMaterialsWindow.DragKey, null);
                ActivateSurfaceForSpline(hoverSurface);
                switch (SpriteMaterialLayerKind(item.category))
                {
                    case TexturePaintLayerKind.Fill:
                        AddFillLayerFromSpriteSet(set, item.set, item.index,
                            SpriteMaterialFillTiling(item.set, item.index), TexturePaintFillProjection.Flat);
                        break;
                    case TexturePaintLayerKind.Spline:
                        AddPathLayerFromSpriteSet(set, item.set, item.index, hoverSurface, hoverHit);
                        break;
                    case TexturePaintLayerKind.Projection:
                        AddProjectionFromSpriteMaterial(set, item.set, item.index);
                        break;
                }
                RepaintAll();
            }
            current.Use();
            return true;
        }

        internal static Vector2 SpriteMaterialFillTiling(OverlayPainterSpriteSet source, int index)
        {
            Sprite reference = null;
            foreach (var sheet in source.spriteSheets)
            {
                if (!OverlayPainterSpriteSetEditorUtility.TryGetSprite(sheet, index, out var sprite)) continue;
                reference ??= sprite;
                if (sheet.channel == TexturePaintChannel.Albedo) { reference = sprite; break; }
            }
            // Use the individual sprite rectangle, never the full atlas dimensions.
            float width = reference != null ? reference.rect.width : 2048f;
            float repeats = Mathf.Max(1f, (float)Math.Round(2048f / Mathf.Max(1f, width), MidpointRounding.AwayFromZero));
            return Vector2.one * repeats;
        }

        private void AddProjectionFromSpriteMaterial(TextureSet set, OverlayPainterSpriteSet source, int index)
        {
            var channels = new List<TexturePaintProjectionChannelSource>();
            var assigned = new HashSet<TexturePaintChannel>();
            foreach (var sheet in source.spriteSheets)
            {
                if (sheet == null || set.GetChannel(sheet.channel) == null || !assigned.Add(sheet.channel) ||
                    !OverlayPainterSpriteSetEditorUtility.TryGetSprite(sheet, index, out var sprite)) continue;
                channels.Add(new TexturePaintProjectionChannelSource
                {
                    channel = sheet.channel, sprite = sprite, invert = sheet.inverted,
                    source = TexturePaintBrushSource.Texture, normalConvention = normalConvention
                });
            }
            if (channels.Count == 0) { ShowWorkspaceStatus("No compatible Sprite Set channels on the current target."); return; }
            SetSelectedChannelAndRefreshSource(channels[0].channel);
            AddProjectionLayerCore(set, null, layer =>
            {
                layer.name = source.GetSpriteName(index, channels[0].sprite.name) + " Projection";
                layer.projectionSettings = new TexturePaintProjectionSettings
                {
                    channelSourcesVersion = 1, channelSources = channels,
                    source = TexturePaintBrushSource.Texture, channel = channels[0].channel,
                    sprite = channels[0].sprite, normalConvention = normalConvention,
                    fade = TexturePaintProjectionFade.None
                };
            });
        }

        internal static void AddSpriteMaterialFirstPoint(TexturePaintLayer layer, TextureSet set,
            ReconstructedSurface surface, RaycastHit hit)
        {
            var path = layer.spline;
            path.worldSpace = true;
            path.UpgradeWorldCurve();
            path.AddPoint(hit.point, hit.textureCoord, surface.index, hit.triangleIndex, hit.normal);
            var anchor = path.anchors[0];
            anchor.surfaceId = set.persistentId;
            anchor.barycentric = hit.barycentricCoordinate;
            anchor.normal = hit.normal;
            path.anchors[0] = anchor;
        }
    }
}
