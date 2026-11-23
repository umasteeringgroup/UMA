using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Lays the flat pieces out in the slot's 0-1 texture space: one island per piece, all at one
    /// texel density, grain up, with a gutter between them. The layout is saved on the pattern and is
    /// made from the pieces as drawn, so simulating, posing, remeshing, reopening and fitting on another
    /// body size never move a print: a larger body only spreads the same texture over more cloth.
    /// </summary>
    public static class UvLayout
    {
        /// <summary>Packs every piece again. Moves painted regions; call only when the artist asks or nothing is laid out yet.</summary>
        /// <returns>The texel density reached, in texels per centimetre.</returns>
        public static float Repack(ClothesPattern pattern)
        {
            UvLayoutSettings settings = pattern.uvLayout ?? new UvLayoutSettings();
            int count = pattern.pieces.Count;
            var sizes = new Vector2[count];
            var mins = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                PatternPiece piece = pattern.pieces[i];
                if (piece.points.Count < 3) continue;
                PieceRotatedBounds(piece, PieceScale.Identity, -piece.grainAngle, out mins[i], out sizes[i]);
            }
            float aspect = Mathf.Max(0.05f, settings.aspect);
            float gu = settings.gutterPixels / (float)Mathf.Max(1, settings.bakeResolution);
            float gv = gu * aspect;

            // Largest uniform scale (uv per cm, in u) at which a shelf packing fits.
            float lo = 0f, hi = 1f;
            for (int guard = 0; guard < 40 && Fits(sizes, hi, aspect, gu, gv, null); guard++) hi *= 2f;
            for (int iter = 0; iter < 40; iter++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Fits(sizes, mid, aspect, gu, gv, null)) lo = mid; else hi = mid;
            }
            var rects = new Rect[count];
            Fits(sizes, lo, aspect, gu, gv, rects);
            for (int i = 0; i < count; i++)
            {
                PatternPiece piece = pattern.pieces[i];
                if (piece.uvIsland == null) piece.uvIsland = new UvIsland();
                piece.uvIsland.placed = true;
                piece.uvIsland.scale = lo;
                piece.uvIsland.rotation = -piece.grainAngle;
                piece.uvIsland.reserved = rects[i];
                piece.uvIsland.offset = new Vector2(rects[i].xMin - mins[i].x * lo, rects[i].yMin - mins[i].y * lo * aspect);
            }
            return lo * settings.bakeResolution;
        }

        private static bool Fits(Vector2[] sizes, float k, float aspect, float gu, float gv, Rect[] output)
        {
            var order = new List<int>();
            for (int i = 0; i < sizes.Length; i++) order.Add(i);
            order.Sort((a, b) => (sizes[b].y).CompareTo(sizes[a].y));
            float x = gu, y = gv, shelf = 0f;
            foreach (int i in order)
            {
                float w = sizes[i].x * k, h = sizes[i].y * k * aspect;
                if (x + w + gu > 1f)
                {
                    x = gu; y += shelf + gv; shelf = 0f;
                }
                if (x + w + gu > 1f || y + h + gv > 1f) return false;
                if (output != null) output[i] = new Rect(x, y, w, h);
                x += w + gu;
                shelf = Mathf.Max(shelf, h);
            }
            return true;
        }

        private static Vector2 Rotate(Vector2 p, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        private static void PieceRotatedBounds(PatternPiece piece, PieceScale scale, float rotation, out Vector2 min, out Vector2 size)
        {
            List<Vector2> outline = PatternGeometry.SampleOutline(piece, 0.5f, scale);
            min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector2 p in outline)
            {
                Vector2 q = Rotate(p, rotation);
                min = Vector2.Min(min, q); max = Vector2.Max(max, q);
            }
            size = max - min;
        }

        /// <summary>The UV of a point on a piece as drawn (before the sizes are applied).</summary>
        public static Vector2 ToUV(ClothesPattern pattern, int pieceIndex, Vector2 drawn)
        {
            UvIsland island = pattern.pieces[pieceIndex].uvIsland;
            float aspect = Mathf.Max(0.05f, pattern.uvLayout != null ? pattern.uvLayout.aspect : 1f);
            Vector2 q = Rotate(drawn, island.rotation);
            return island.offset + new Vector2(q.x * island.scale, q.y * island.scale * aspect);
        }

        /// <summary>
        /// Gives the mesh its UVs from the saved islands, laying the pieces out first when the pattern has
        /// no layout yet. Pieces that grew out of their reserved space are reported; nothing is moved.
        /// </summary>
        public static void ApplyUVs(ClothesPattern pattern, PatternMesh mesh)
        {
            bool anyUnplaced = false;
            foreach (PatternPiece piece in pattern.pieces) if (piece.uvIsland == null || !piece.uvIsland.placed) anyUnplaced = true;
            if (anyUnplaced) Repack(pattern);
            for (int v = 0; v < mesh.VertexCount; v++) mesh.uv[v] = ToUV(pattern, mesh.piece[v], mesh.drawn[v]);
            foreach (string problem in FindProblems(pattern, mesh)) mesh.warnings.Add(problem);
        }

        /// <summary>Islands that leave their reserved space, overlap another or leave the 0-1 square.</summary>
        public static List<string> FindProblems(ClothesPattern pattern, PatternMesh mesh)
        {
            var problems = new List<string>();
            int pieces = pattern.pieces.Count;
            var bounds = new Rect[pieces];
            var has = new bool[pieces];
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                int p = mesh.piece[v];
                Vector2 uv = mesh.uv[v];
                if (!has[p]) { bounds[p] = new Rect(uv, Vector2.zero); has[p] = true; }
                else bounds[p] = Rect.MinMaxRect(Mathf.Min(bounds[p].xMin, uv.x), Mathf.Min(bounds[p].yMin, uv.y), Mathf.Max(bounds[p].xMax, uv.x), Mathf.Max(bounds[p].yMax, uv.y));
            }
            const float tolerance = 1e-4f;
            for (int p = 0; p < pieces; p++)
            {
                if (!has[p]) continue;
                string name = pattern.pieces[p].name;
                Rect reserved = pattern.pieces[p].uvIsland.reserved;
                if (bounds[p].xMin < -tolerance || bounds[p].yMin < -tolerance || bounds[p].xMax > 1f + tolerance || bounds[p].yMax > 1f + tolerance)
                    problems.Add("'" + name + "' leaves the texture. Use Repack UVs.");
                else if (bounds[p].xMin < reserved.xMin - tolerance || bounds[p].yMin < reserved.yMin - tolerance || bounds[p].xMax > reserved.xMax + tolerance || bounds[p].yMax > reserved.yMax + tolerance)
                    problems.Add("'" + name + "' has grown out of its place in the texture. Use Repack UVs.");
                for (int q = p + 1; q < pieces; q++)
                {
                    if (has[q] && bounds[p].Overlaps(bounds[q])) problems.Add("'" + name + "' and '" + pattern.pieces[q].name + "' overlap in the texture. Use Repack UVs.");
                }
            }
            // Collapsed or flipped UV triangles.
            int flipped = 0, collapsed = 0;
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                Vector2 a = mesh.uv[mesh.triangles[t]], b = mesh.uv[mesh.triangles[t + 1]], c = mesh.uv[mesh.triangles[t + 2]];
                Vector2 fa = mesh.flat[mesh.triangles[t]], fb = mesh.flat[mesh.triangles[t + 1]], fc = mesh.flat[mesh.triangles[t + 2]];
                float uvArea = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                float flatArea = (fb.x - fa.x) * (fc.y - fa.y) - (fb.y - fa.y) * (fc.x - fa.x);
                if (Mathf.Abs(uvArea) < 1e-12f) collapsed++;
                else if (Mathf.Sign(uvArea) != Mathf.Sign(flatArea)) flipped++;
            }
            if (collapsed > 0) problems.Add(collapsed + " triangles have no area in the texture.");
            if (flipped > 0) problems.Add(flipped + " triangles are mirrored in the texture.");
            return problems;
        }
    }
}
