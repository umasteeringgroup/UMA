using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// A trim to paint onto a slot's texture: a zipper, a row of stitching, piping, a hem, drawn along a path in
    /// the slot's UVs. Tools that know where trims go (the Clothes Designer, from a sewing pattern) describe
    /// them; a texture painter that is installed (the Overlay Painter) paints them, as editable layers.
    /// </summary>
    public sealed class UMATrimPath
    {
        /// <summary>The layer's name, for the artist.</summary>
        public string name;
        /// <summary>Which construction draws it: an id from <see cref="IUMATrimPainter.Constructions"/>.</summary>
        public string constructionId;
        /// <summary>The path, in the slot's UVs (0 to 1), in order.</summary>
        public List<Vector2> uv = new List<Vector2>();
        public bool closed;
        /// <summary>How wide the trim is, in UV units across the path.</summary>
        public float widthUV;
    }

    /// <summary>One way a trim can be drawn (a metal zipper, a twin-needle stitch...), as the painter lists it.</summary>
    public readonly struct UMATrimConstruction
    {
        public readonly string id;
        /// <summary>Where it sits in the painter's menu, "Fasteners and Distress/Metal Zipper" for example.</summary>
        public readonly string menuPath;

        public UMATrimConstruction(string id, string menuPath)
        {
            this.id = id;
            this.menuPath = menuPath;
        }
    }

    /// <summary>A texture painter that can paint trims onto a slot.</summary>
    public interface IUMATrimPainter
    {
        /// <summary>The painter's name, for buttons and messages.</summary>
        string Name { get; }
        /// <summary>The constructions trims can use.</summary>
        IReadOnlyList<UMATrimConstruction> Constructions { get; }
        /// <summary>
        /// Opens the painter on <paramref name="slot"/>, drawn with <paramref name="overlay"/>, with one editable
        /// layer for each trim. False, with the reason in <paramref name="message"/>, when it could not.
        /// </summary>
        bool PaintTrims(SlotDataAsset slot, OverlayDataAsset overlay, IReadOnlyList<UMATrimPath> trims, out string message);
    }

    /// <summary>
    /// Where a trim painter registers itself, so tools that describe trims can hand them over without
    /// depending on it. Null while no painter is installed.
    /// </summary>
    public static class UMATrimPainting
    {
        public static IUMATrimPainter Painter { get; private set; }

        public static void Register(IUMATrimPainter painter) => Painter = painter;

        public static void Unregister(IUMATrimPainter painter)
        {
            if (Painter == painter) Painter = null;
        }

        /// <summary>The menu path of a construction, or null when the painter (or the construction) is missing.</summary>
        public static string MenuPathOf(string constructionId)
        {
            if (Painter == null || string.IsNullOrEmpty(constructionId)) return null;
            foreach (UMATrimConstruction c in Painter.Constructions) if (c.id == constructionId) return c.menuPath;
            return null;
        }
    }
}
