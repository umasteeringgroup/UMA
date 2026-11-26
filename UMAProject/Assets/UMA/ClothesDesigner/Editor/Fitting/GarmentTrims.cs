using System.Collections.Generic;
using UMA.Editors;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Where a pattern's trims (zippers, stitching, piping, hems) go: on the pieces as drawn, and in the baked
    /// slot's UVs for the trim painter (<see cref="UMATrimPainting"/>, the Overlay Painter when installed).
    /// </summary>
    public static class GarmentTrims
    {
        /// <summary>How far apart a trim's path is sampled along its edge or line (cm).</summary>
        public const float SampleCm = 0.5f;

        /// <summary>
        /// The trim's middle line on its piece as drawn (cm): its part of the edge, moved inside the piece by its
        /// inset, or its part of a line. Empty when its piece, edge or line no longer exists.
        /// </summary>
        public static List<Vector2> DrawnPath(ClothesPattern pattern, PatternTrim trim, out int pieceIndex)
        {
            var path = new List<Vector2>();
            pieceIndex = -1;
            if (pattern == null || trim == null) return path;
            PatternPiece piece = pattern.FindPiece(trim.pieceId);
            if (piece == null) return path;
            pieceIndex = pattern.pieces.IndexOf(piece);
            PatternGeometry.EdgeCurve curve = LineGeometry.CurveOf(piece, trim.pathId, PieceScale.Identity);
            if (curve == null) return path;
            bool isEdge = piece.IndexOfEdge(trim.pathId) >= 0;
            float f0 = Mathf.Clamp01(Mathf.Min(trim.start, trim.end)), f1 = Mathf.Clamp01(Mathf.Max(trim.start, trim.end));
            float length = curve.Length * (f1 - f0);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / SampleCm));
            float inset = isEdge ? trim.insetCm : 0f;
            for (int i = 0; i <= steps; i++)
            {
                float f = Mathf.Lerp(f0, f1, i / (float)steps);
                Vector2 p = curve.At(f);
                if (Mathf.Abs(inset) > 1e-4f)
                {
                    Vector2 t = curve.At(Mathf.Min(1f, f + 0.002f)) - curve.At(Mathf.Max(0f, f - 0.002f));
                    if (t.sqrMagnitude > 1e-12f)
                    {
                        t.Normalize();
                        // Outlines run counter-clockwise, so the inside is on the left of an edge's direction.
                        p += new Vector2(-t.y, t.x) * inset;
                    }
                }
                path.Add(p);
            }
            return path;
        }

        /// <summary>
        /// The trims as the trim painter takes them: paths in the slot's UVs and widths in UV units, through the
        /// pattern's UV layout. Trims whose piece, edge or line is gone are left out and named in <paramref name="missing"/>.
        /// </summary>
        public static List<UMATrimPath> UvPaths(ClothesPattern pattern, List<string> missing = null)
        {
            var result = new List<UMATrimPath>();
            if (pattern?.trims == null) return result;
            foreach (PatternTrim trim in pattern.trims)
            {
                List<Vector2> drawn = DrawnPath(pattern, trim, out int pieceIndex);
                if (drawn.Count < 2 || pieceIndex < 0) { missing?.Add(trim?.name); continue; }
                UvIsland island = pattern.pieces[pieceIndex].uvIsland;
                if (island != null && !string.IsNullOrEmpty(island.sharesWith))
                {
                    PatternPiece source = pattern.FindPiece(island.sharesWith);
                    if (source?.uvIsland != null) island = source.uvIsland;
                }
                var path = new UMATrimPath
                {
                    name = trim.name,
                    constructionId = trim.constructionId,
                    widthUV = Mathf.Max(0.0001f, trim.widthCm * (island != null ? island.scale : 0f))
                };
                foreach (Vector2 p in drawn) path.uv.Add(UvLayout.ToUV(pattern, pieceIndex, p));
                result.Add(path);
            }
            return result;
        }

        /// <summary>A new trim on part of an edge or line, with the painter's first zipper-like construction when there is one.</summary>
        public static PatternTrim Create(PatternPiece piece, string pathId, float start, float end)
        {
            var trim = new PatternTrim { id = ClothesPattern.NewId(), pieceId = piece.id, pathId = pathId, start = start, end = end };
            IUMATrimPainter painter = UMATrimPainting.Painter;
            if (painter != null)
            {
                UMATrimConstruction chosen = default;
                bool found = false;
                foreach (UMATrimConstruction c in painter.Constructions)
                {
                    if (!found) { chosen = c; found = true; }
                    if (c.menuPath.ToLowerInvariant().Contains("zipper")) { chosen = c; break; }
                }
                if (found) { trim.constructionId = chosen.id; trim.constructionName = chosen.menuPath; trim.name = ShortName(chosen.menuPath); }
            }
            return trim;
        }

        /// <summary>The last part of a menu path: "Metal Zipper" from "Fasteners and Distress/Metal Zipper".</summary>
        public static string ShortName(string menuPath)
        {
            if (string.IsNullOrEmpty(menuPath)) return "Trim";
            int slash = menuPath.LastIndexOf('/');
            return slash >= 0 ? menuPath.Substring(slash + 1) : menuPath;
        }
    }
}
