using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// What the bake adds besides the cloth: buttons and small hard parts fixed to it, and topstitching
    /// drawn into a texture for an overlay.
    /// </summary>
    public static class GarmentExtras
    {
        /// <summary>A part as the bake places it: where on which piece, its shape and size.</summary>
        public struct PlacedPart
        {
            public int piece;
            public Vector2 drawn;
            public PartShape shape;
            public float sizeMm, aspect, heightMm, rotation;
            public Mesh mesh;
        }

        /// <summary>Every part of the pattern, and a button for every tack marked as one.</summary>
        public static List<PlacedPart> PartsOf(ClothesPattern pattern)
        {
            var parts = new List<PlacedPart>();
            if (pattern.parts != null)
            {
                foreach (PatternPart part in pattern.parts)
                {
                    int piece = pattern.IndexOfPiece(part.pieceId);
                    if (piece < 0) continue;
                    parts.Add(new PlacedPart { piece = piece, drawn = part.position, shape = part.shape, sizeMm = part.sizeMm, aspect = part.aspect, heightMm = part.heightMm, rotation = part.rotation, mesh = part.mesh });
                }
            }
            if (pattern.tacks != null)
            {
                foreach (PatternTack tack in pattern.tacks)
                {
                    if (tack == null || !tack.button) continue;
                    int piece = pattern.IndexOfPiece(tack.pieceA);
                    if (piece < 0) continue;
                    parts.Add(new PlacedPart { piece = piece, drawn = tack.a, shape = PartShape.Button, sizeMm = tack.buttonDiameterMm, aspect = 1f, heightMm = Mathf.Max(1.5f, tack.buttonDiameterMm * 0.2f) });
                }
            }
            return parts;
        }

        /// <summary>
        /// Adds the parts to a slot's geometry: each sits on the cloth where it is drawn, facing out, its
        /// up along the piece's up. Each new vertex copies the bone weights of the cloth vertex under it
        /// (<paramref name="sourceOf"/>) and takes the cloth's UVs under it, so painting the cloth there
        /// paints the part. Returns how many triangles were added.
        /// </summary>
        /// <param name="positions">The cloth's welded positions (one per mesh vertex).</param>
        /// <param name="facingOut">The cloth's triangles, facing out, in the mesh's triangle order.</param>
        public static int AddParts(ClothesPattern pattern, PatternMesh mesh, Vector3[] positions, int[] facingOut,
            List<Vector3> outPositions, List<int> outTriangles, List<Vector2> outUv, List<int> sourceOf, List<string> messages)
        {
            List<PlacedPart> parts = PartsOf(pattern);
            if (parts.Count == 0) return 0;
            var sampler = new PatternMeshSampler(mesh);
            int added = 0;
            foreach (PlacedPart part in parts)
            {
                if (!sampler.Locate(part.piece, part.drawn, out int t, out Vector3 w))
                {
                    messages.Add("A part on '" + pattern.pieces[part.piece].name + "' is off the cloth and was left out.");
                    continue;
                }
                int i0 = facingOut[t], i1 = facingOut[t + 1], i2 = facingOut[t + 2];
                // The mesh's own corner order, for the barycentric weights and the flat positions.
                int m0 = mesh.triangles[t], m1 = mesh.triangles[t + 1], m2 = mesh.triangles[t + 2];
                Vector3 center = positions[m0] * w.x + positions[m1] * w.y + positions[m2] * w.z;
                Vector3 normal = Vector3.Cross(positions[i1] - positions[i0], positions[i2] - positions[i0]);
                if (normal.sqrMagnitude < 1e-16f) continue;
                normal.Normalize();
                // The piece's drawn x and y on the cloth: how position changes with them in this triangle.
                Vector2 e1 = mesh.drawn[m1] - mesh.drawn[m0], e2 = mesh.drawn[m2] - mesh.drawn[m0];
                Vector3 p1 = positions[m1] - positions[m0], p2 = positions[m2] - positions[m0];
                float det = e1.x * e2.y - e2.x * e1.y;
                if (Mathf.Abs(det) < 1e-9f) continue;
                Vector3 alongX = (p1 * e2.y - p2 * e1.y) / det, alongY = (-p1 * e2.x + p2 * e1.x) / det;
                // The part's own axes on the drawn piece, turned by its rotation; then on the cloth.
                float rc = Mathf.Cos(part.rotation * Mathf.Deg2Rad), rs = Mathf.Sin(part.rotation * Mathf.Deg2Rad);
                Vector2 dx = new Vector2(rc, rs), dy = new Vector2(-rs, rc);
                Vector3 right = Vector3.ProjectOnPlane(alongX * dx.x + alongY * dx.y, normal);
                Vector3 up = Vector3.ProjectOnPlane(alongY * dy.y + alongX * dy.x, normal);
                if (right.sqrMagnitude < 1e-12f || up.sqrMagnitude < 1e-12f) continue;
                right.Normalize();
                up = (up - right * Vector3.Dot(up, right)).normalized;
                // The drawn axes may run either way round the normal: wind the part's triangles to face out.
                bool flip = Vector3.Dot(Vector3.Cross(right, up), normal) < 0f;
                int source = w.x >= w.y && w.x >= w.z ? m0 : (w.y >= w.z ? m1 : m2);
                float size = part.sizeMm * 0.001f, height = part.heightMm * 0.001f;
                // A point of the part's footprint (m, along its right and up) and the cloth's UV under it.
                Vector2 UvAt(Vector2 local) => UvLayout.ToUV(pattern, part.piece, part.drawn + (dx * local.x + dy * local.y) * 100f);
                int first = outPositions.Count;
                void Vertex(Vector3 p, Vector2 local) { outPositions.Add(p); outUv.Add(UvAt(local)); sourceOf.Add(source); }
                void Triangle(int a, int b, int c) { outTriangles.Add(a); outTriangles.Add(flip ? c : b); outTriangles.Add(flip ? b : c); }
                // Just above the cloth, so it never sinks into it.
                Vector3 baseCenter = center + normal * 0.0003f;
                if (part.shape == PartShape.Mesh && part.mesh != null && part.mesh.isReadable)
                {
                    Vector3[] mv = part.mesh.vertices;
                    int[] mt = part.mesh.triangles;
                    Bounds b = part.mesh.bounds;
                    float scale = size / Mathf.Max(1e-6f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                    foreach (Vector3 v in mv)
                    {
                        Vector3 q = (v - new Vector3(b.center.x, b.center.y, b.min.z)) * scale;
                        Vertex(baseCenter + right * q.x + up * q.y + normal * q.z, new Vector2(q.x, q.y));
                    }
                    for (int i = 0; i + 2 < mt.Length; i += 3) Triangle(first + mt[i], first + mt[i + 1], first + mt[i + 2]);
                    added += mt.Length / 3;
                    continue;
                }
                if (part.shape == PartShape.Mesh)
                {
                    messages.Add("A part's mesh is missing or not readable (turn on Read/Write in its import settings); it was made a button.");
                }
                // A prism: a footprint outline, its top and its sides.
                var outline = new List<Vector2>();
                if (part.shape == PartShape.Plate)
                {
                    float hw = size * 0.5f, hh = size * Mathf.Max(0.05f, part.aspect) * 0.5f;
                    outline.Add(new Vector2(-hw, -hh)); outline.Add(new Vector2(hw, -hh)); outline.Add(new Vector2(hw, hh)); outline.Add(new Vector2(-hw, hh));
                }
                else
                {
                    const int sides = 16;
                    for (int k = 0; k < sides; k++) { float a = k * Mathf.PI * 2f / sides; outline.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size * 0.5f); }
                }
                int n = outline.Count;
                Vector3 At(Vector2 q, float h) => baseCenter + right * q.x + up * q.y + normal * h;
                // Top: a fan from the middle, counter-clockwise round the normal.
                Vertex(At(Vector2.zero, height), Vector2.zero);
                for (int k = 0; k < n; k++) Vertex(At(outline[k], height), outline[k]);
                for (int k = 0; k < n; k++) Triangle(first, first + 1 + k, first + 1 + (k + 1) % n);
                // Sides: from the cloth up to the top.
                int sideStart = outPositions.Count;
                for (int k = 0; k < n; k++) { Vertex(At(outline[k], 0f), outline[k]); Vertex(At(outline[k], height), outline[k]); }
                for (int k = 0; k < n; k++)
                {
                    int b0 = sideStart + 2 * k, t0 = b0 + 1, b1 = sideStart + 2 * ((k + 1) % n), t1 = b1 + 1;
                    Triangle(b0, b1, t0);
                    Triangle(b1, t1, t0);
                }
                added += n + 2 * n;
            }
            return added;
        }

        /// <summary>True when any seam asks for topstitching.</summary>
        public static bool HasTopstitch(ClothesPattern pattern)
        {
            foreach (PatternSeam seam in pattern.seams) if (seam != null && seam.topstitch != TopstitchKind.None) return true;
            return false;
        }

        /// <summary>Thread length and gap of a stitch (cm), and its width.</summary>
        public const float StitchCm = 0.3f, GapCm = 0.18f, ThreadCm = 0.07f, RowGapCm = 0.4f;

        /// <summary>
        /// The seams' topstitching drawn in the slot's UV layout: rows of short stitches beside each sewn
        /// edge, on the inside of its piece, transparent elsewhere, for an overlay over the clothes' own.
        /// </summary>
        public static Texture2D DrawTopstitch(ClothesPattern pattern, int size, Color thread)
        {
            var pixels = new Color32[size * size];
            foreach (PatternSeam seam in pattern.seams)
            {
                if (seam == null || seam.topstitch == TopstitchKind.None) continue;
                int rows = seam.topstitch == TopstitchKind.Double ? 2 : 1;
                foreach (SeamSpan span in new[] { seam.a, seam.b })
                {
                    int p = pattern.IndexOfPiece(span.pieceId);
                    if (p < 0) continue;
                    PatternPiece piece = pattern.pieces[p];
                    int edge = piece.IndexOfEdge(span.edgeId);
                    // Lines inside a piece carry the other piece's edge: the stitches are on that piece.
                    if (edge < 0) continue;
                    var curve = new PatternGeometry.EdgeCurve(piece, edge, PieceScale.Identity);
                    float length = curve.Length * (span.end - span.start);
                    float scale = piece.uvIsland.scale * size;
                    float width = Mathf.Max(1.2f, ThreadCm * scale);
                    for (int row = 0; row < rows; row++)
                    {
                        float offset = seam.topstitchOffsetMm * 0.1f + row * RowGapCm;
                        for (float s = 0.1f; s + StitchCm < length; s += StitchCm + GapCm)
                        {
                            Vector2 a = Inside(curve, span, s / length, offset), b = Inside(curve, span, (s + StitchCm) / length, offset);
                            Line(pixels, size, UvLayout.ToUV(pattern, p, a) * size, UvLayout.ToUV(pattern, p, b) * size, width, thread);
                        }
                    }
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>A point beside an edge, inside its piece (outlines run counter-clockwise: the inside is on the left).</summary>
        private static Vector2 Inside(PatternGeometry.EdgeCurve curve, SeamSpan span, float u, float offsetCm)
        {
            float f = Mathf.Lerp(span.start, span.end, Mathf.Clamp01(u));
            Vector2 t = curve.Tangent(f);
            return curve.At(f) + new Vector2(-t.y, t.x) * offsetCm;
        }

        private static void Line(Color32[] pixels, int size, Vector2 a, Vector2 b, float width, Color color)
        {
            float r = width * 0.5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - r - 1)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + r + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - r - 1)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + r + 1));
            Vector2 ab = b - a;
            float len2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float cover = Mathf.Clamp01(r + 0.5f - (a + ab * t - p).magnitude);
                    if (cover <= 0f) continue;
                    int i = y * size + x;
                    byte alpha = (byte)Mathf.Max(pixels[i].a, Mathf.RoundToInt(cover * color.a * 255f));
                    pixels[i] = new Color32((byte)(color.r * 255f), (byte)(color.g * 255f), (byte)(color.b * 255f), alpha);
                }
            }
        }

        /// <summary>Draws the topstitching and saves it as a PNG asset. Returns the imported texture.</summary>
        public static Texture2D WriteTopstitch(ClothesPattern pattern, string assetPath, int size, Color thread)
        {
            Texture2D texture = DrawTopstitch(pattern, Mathf.Clamp(size, 256, 4096), thread);
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
