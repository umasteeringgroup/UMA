using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Pictures of the sample patterns for the sample browser: the pattern (its pieces filled in their own colours) and
    /// the drape on a grey body (each piece in the same colour, so the two pictures read together). Pattern pictures are
    /// drawn the first time they are asked for. Drape pictures ship with Clothes Designer Examples (rendered on
    /// Human Female 3.0); <see cref="SampleDrape"/> renders them again on another body, kept in the project's Library
    /// per race.
    /// </summary>
    public static class SamplePictures
    {
        public const int PatternWidth = 160, PatternHeight = 200;
        public const int DrapeWidth = 200, DrapeHeight = 250;
        /// <summary>Where Clothes Designer Examples keeps the pictures it ships.</summary>
        public const string ShippedFolder = "Assets/UMA/ClothesDesignerExamples/Previews";
        /// <summary>How long each sample is draped for its picture.</summary>
        public const int DrapeFrames = 400;
        /// <summary>The race the shipped pictures are rendered on.</summary>
        public const string ShippedRace = "Human Female 3.0";

        private static readonly Dictionary<string, Texture2D> patternPictures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> drapePictures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<int, Texture2D> swatches = new Dictionary<int, Texture2D>();

        /// <summary>The colour of a piece in both pictures: soft, and far apart in hue from its neighbours.</summary>
        public static Color PieceColor(int index)
        {
            float hue = (0.58f + index * 0.618034f) % 1f;
            return Color.HSVToRGB(hue, 0.42f, 0.93f);
        }

        // ---- Pattern pictures -------------------------------------------------------------------------

        /// <summary>The pattern picture of a sample, drawn once and kept for the session.</summary>
        public static Texture2D PatternPicture(string sampleName)
        {
            if (patternPictures.TryGetValue(sampleName, out Texture2D cached) && cached != null) return cached;
            ClothesPattern pattern = SamplePatterns.CreateQuick(sampleName);
            if (pattern == null) return null;
            try
            {
                Texture2D picture = DrawPattern(pattern, PatternWidth, PatternHeight);
                patternPictures[sampleName] = picture;
                return picture;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pattern);
            }
        }

        /// <summary>
        /// Draws a pattern's pieces, as laid out on the canvas, scaled to fit: each filled in its colour with a darker
        /// outline. Transparent behind.
        /// </summary>
        public static Texture2D DrawPattern(ClothesPattern pattern, int width, int height)
        {
            var outlines = new List<List<Vector2>>();
            var bounds = new Rect();
            bool any = false;
            foreach (PatternPiece piece in pattern.pieces)
            {
                List<Vector2> outline = piece != null ? PatternGeometry.SampleOutline(piece, 0.5f, null) : null;
                outlines.Add(outline);
                if (outline == null) continue;
                foreach (Vector2 p in outline)
                {
                    if (!any) { bounds = new Rect(p, Vector2.zero); any = true; }
                    bounds.xMin = Mathf.Min(bounds.xMin, p.x); bounds.xMax = Mathf.Max(bounds.xMax, p.x);
                    bounds.yMin = Mathf.Min(bounds.yMin, p.y); bounds.yMax = Mathf.Max(bounds.yMax, p.y);
                }
            }
            var pixels = new Color32[width * height];
            if (any && bounds.width > 0f && bounds.height > 0f)
            {
                // Pieces laid side by side make a wide strip; wrap it into rows so it fills the picture.
                List<Vector2> offsets = WrapIntoRows(pattern, outlines, width / (float)height, out Rect wrapped);
                const float margin = 6f;
                float scale = Mathf.Min((width - 2f * margin) / wrapped.width, (height - 2f * margin) / wrapped.height);
                Vector2 origin = new Vector2((width - wrapped.width * scale) * 0.5f, (height - wrapped.height * scale) * 0.5f);
                for (int i = 0; i < outlines.Count; i++)
                {
                    if (outlines[i] == null || outlines[i].Count < 3) continue;
                    var points = new Vector2[outlines[i].Count];
                    for (int k = 0; k < points.Length; k++) points[k] = origin + (outlines[i][k] + offsets[i] - wrapped.min) * scale;
                    Color fill = PieceColor(i);
                    Fill(pixels, width, height, points, fill);
                    Color edge = Color.Lerp(fill, Color.black, 0.55f);
                    for (int k = 0; k < points.Length; k++) Line(pixels, width, height, points[k], points[(k + 1) % points.Length], edge);
                }
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = pattern.name + " (pattern picture)" };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>Moves pieces into rows (each piece keeps its place in a row) so the whole has about the given width to height.</summary>
        private static List<Vector2> WrapIntoRows(ClothesPattern pattern, List<List<Vector2>> outlines, float aspect, out Rect wrapped)
        {
            var boxes = new List<Rect>();
            float area = 0f;
            for (int i = 0; i < outlines.Count; i++)
            {
                Rect b = new Rect();
                if (outlines[i] != null && outlines[i].Count > 0)
                {
                    b = new Rect(outlines[i][0], Vector2.zero);
                    foreach (Vector2 p in outlines[i]) { b.xMin = Mathf.Min(b.xMin, p.x); b.xMax = Mathf.Max(b.xMax, p.x); b.yMin = Mathf.Min(b.yMin, p.y); b.yMax = Mathf.Max(b.yMax, p.y); }
                }
                boxes.Add(b);
                area += (b.width + 4f) * (b.height + 4f);
            }
            float rowWidth = Mathf.Sqrt(area * aspect);
            foreach (Rect b in boxes) rowWidth = Mathf.Max(rowWidth, b.width);
            var offsets = new List<Vector2>();
            float x = 0f, y = 0f, rowHeight = 0f, maxWidth = 0f;
            const float gap = 4f;
            // Rows run downwards (the first row on top).
            var rowStart = new List<int>();
            var placed = new List<Vector2>();
            rowStart.Add(0);
            for (int i = 0; i < boxes.Count; i++)
            {
                Rect b = boxes[i];
                if (x > 0f && x + b.width > rowWidth)
                {
                    y -= rowHeight + gap;
                    x = 0f; rowHeight = 0f;
                    rowStart.Add(i);
                }
                placed.Add(new Vector2(x, y));
                x += b.width + gap;
                maxWidth = Mathf.Max(maxWidth, x - gap);
                rowHeight = Mathf.Max(rowHeight, b.height);
            }
            float bottom = y - rowHeight;
            // Each piece's top at its row's top: offset = place - its own top-left.
            for (int i = 0; i < boxes.Count; i++)
            {
                Rect b = boxes[i];
                offsets.Add(new Vector2(placed[i].x - b.xMin, placed[i].y - b.yMax));
            }
            wrapped = Rect.MinMaxRect(0f, bottom, Mathf.Max(1f, maxWidth), 0f);
            if (wrapped.height <= 0f) wrapped.yMin = -1f;
            return offsets;
        }

        private static void Fill(Color32[] pixels, int width, int height, Vector2[] polygon, Color color)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 p in polygon) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minY), 0, height - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, height - 1);
            var crossings = new List<float>();
            Color32 c = color;
            for (int y = y0; y <= y1; y++)
            {
                float sy = y + 0.5f;
                crossings.Clear();
                for (int k = 0; k < polygon.Length; k++)
                {
                    Vector2 a = polygon[k], b = polygon[(k + 1) % polygon.Length];
                    if ((a.y <= sy) == (b.y <= sy)) continue;
                    crossings.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    int x0 = Mathf.Clamp(Mathf.CeilToInt(crossings[k] - 0.5f), 0, width - 1), x1 = Mathf.Clamp(Mathf.FloorToInt(crossings[k + 1] - 0.5f), 0, width - 1);
                    for (int x = x0; x <= x1; x++) pixels[y * width + x] = c;
                }
            }
        }

        private static void Line(Color32[] pixels, int width, int height, Vector2 a, Vector2 b, Color color)
        {
            Color32 c = color;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)) * 1.5f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                int x = Mathf.RoundToInt(p.x - 0.5f), y = Mathf.RoundToInt(p.y - 0.5f);
                if (x >= 0 && y >= 0 && x < width && y < height) pixels[y * width + x] = c;
            }
        }

        // ---- Drape pictures -----------------------------------------------------------------------------

        /// <summary>The Library folder kept for a race's drape pictures.</summary>
        public static string CacheFolder(string raceName) => Path.Combine("Library", "UMAClothesDesigner", "SamplePreviews", SafeName(raceName));

        private static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Body";
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            return name;
        }

        public static string FileName(string sampleName) => SafeName(sampleName) + ".png";

        /// <summary>
        /// The drape picture of a sample for a race: rendered on that race (kept in the Library), else the shipped one,
        /// else null.
        /// </summary>
        public static Texture2D DrapePicture(string sampleName, string raceName, out bool onThisRace)
        {
            onThisRace = false;
            if (!string.IsNullOrEmpty(raceName))
            {
                string key = raceName + "|" + sampleName;
                if (drapePictures.TryGetValue(key, out Texture2D cached) && cached != null) { onThisRace = true; return cached; }
                string file = Path.Combine(CacheFolder(raceName), FileName(sampleName));
                if (File.Exists(file))
                {
                    var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = sampleName + " (drape on " + raceName + ")" };
                    if (loaded.LoadImage(File.ReadAllBytes(file), false))
                    {
                        drapePictures[key] = loaded;
                        onThisRace = true;
                        return loaded;
                    }
                    UnityEngine.Object.DestroyImmediate(loaded);
                }
            }
            Texture2D shipped = AssetDatabase.LoadAssetAtPath<Texture2D>(ShippedFolder + "/" + FileName(sampleName));
            if (shipped != null && raceName == ShippedRace) onThisRace = true;
            return shipped;
        }

        /// <summary>Keeps a freshly rendered picture for a race (in memory and in the Library).</summary>
        public static void KeepDrapePicture(string sampleName, string raceName, Texture2D picture)
        {
            string key = raceName + "|" + sampleName;
            if (drapePictures.TryGetValue(key, out Texture2D old) && old != null && old != picture) UnityEngine.Object.DestroyImmediate(old);
            drapePictures[key] = picture;
            string folder = CacheFolder(raceName);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, FileName(sampleName)), picture.EncodeToPNG());
        }

        /// <summary>A grey, double-sided mesh of the measured body, for pictures.</summary>
        public static Mesh BodyMesh(BodyReference body)
        {
            BodyCollider c = body.Collider;
            int n = c.TriangleCount;
            var vertices = new Vector3[n * 3];
            var triangles = new int[n * 3];
            for (int i = 0; i < n; i++)
            {
                vertices[3 * i] = c.A[i]; vertices[3 * i + 1] = c.B[i]; vertices[3 * i + 2] = c.C[i];
                triangles[3 * i] = 3 * i; triangles[3 * i + 1] = 3 * i + 1; triangles[3 * i + 2] = 3 * i + 2;
            }
            var normals = new Vector3[vertices.Length];
            new UMABuddyNormals(vertices, triangles).Recalculate(vertices, normals, true);
            var mesh = new Mesh { name = "Clothes Designer Picture Body", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            Vector3[] both = new Vector3[vertices.Length * 2];
            Array.Copy(vertices, both, vertices.Length);
            Array.Copy(vertices, 0, both, vertices.Length, vertices.Length);
            mesh.vertices = both;
            mesh.normals = DesignerSession.WithBackFaceNormals(normals);
            mesh.triangles = DesignerSession.WithBackFaces(triangles, vertices.Length);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A material in a flat colour that renders in the project's pipeline.</summary>
        private static Material Flat(Color color)
        {
            Material template = UMAUtils.GetDefaultDiffuseMaterial();
            Material material = template != null ? template : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.hideFlags = HideFlags.HideAndDontSave;
            Texture2D swatch = Swatch(color);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", swatch);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", swatch);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            return material;
        }

        private static Texture2D Swatch(Color color)
        {
            int key = ((Color32)color).GetHashCode();
            if (swatches.TryGetValue(key, out Texture2D t) && t != null) return t;
            t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(new Color32[] { color, color, color, color });
            t.Apply(false, false);
            swatches[key] = t;
            return t;
        }

        /// <summary>
        /// Renders cloth on a body: the cloth's pieces in their colours, from the front and a little to the side, framed
        /// on the cloth. The body mesh is made by <see cref="BodyMesh"/>.
        /// </summary>
        public static Texture2D RenderDrape(Mesh bodyMesh, BodyCollider body, PatternMesh mesh, Vector3[] positions, int width, int height)
        {
            int pieces = mesh.pieceStart != null ? mesh.pieceStart.Length : 0;
            int count = positions.Length;
            // One submesh per piece, each drawn from both sides.
            var lists = new List<int>[Mathf.Max(1, pieces)];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<int>();
            for (int t = 0; t + 2 < mesh.triangles.Count; t += 3)
            {
                int a = mesh.triangles[t];
                int piece = PieceOf(mesh, a);
                List<int> list = lists[Mathf.Clamp(piece, 0, lists.Length - 1)];
                list.Add(a); list.Add(mesh.triangles[t + 1]); list.Add(mesh.triangles[t + 2]);
            }
            var all = mesh.triangles.ToArray();
            var normals = new Vector3[count];
            new UMABuddyNormals(positions, all).Recalculate(positions, normals, true);
            var cloth = new Mesh { name = "Clothes Designer Picture Cloth", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            var both = new Vector3[count * 2];
            Array.Copy(positions, both, count);
            Array.Copy(positions, 0, both, count, count);
            // The back copy sits a hair behind the front, so the two sides do not fight.
            for (int v = 0; v < count; v++) both[count + v] -= normals[v] * 0.0004f;
            cloth.vertices = both;
            cloth.normals = DesignerSession.WithBackFaceNormals(normals);
            cloth.subMeshCount = lists.Length;
            for (int i = 0; i < lists.Length; i++) cloth.SetTriangles(DesignerSession.WithBackFaces(lists[i].ToArray(), count), i, false);
            cloth.RecalculateBounds();

            var materials = new List<Material>();
            Material bodyMaterial = Flat(new Color(0.62f, 0.62f, 0.64f));
            materials.Add(bodyMaterial);
            var render = new PreviewRenderUtility();
            try
            {
                Bounds b = new Bounds(positions.Length > 0 ? positions[0] : Vector3.zero, Vector3.zero);
                foreach (Vector3 p in positions) b.Encapsulate(p);
                Camera camera = render.camera;
                camera.orthographic = true;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 20f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.17f, 0.17f, 0.18f, 1f);
                float aspect = width / (float)height;
                camera.orthographicSize = Mathf.Max(b.size.y, Mathf.Max(b.size.x, b.size.z) / aspect) * 0.5f * 1.12f + 0.025f;
                // The body faces +Z in its own space: look from the side that sees most of the cloth, from a little
                // above (the front for most clothes, the side or back for a cap worn at the back of the head).
                Vector3 from = Quaternion.Euler(-8f, BestView(positions, mesh.triangles, body), 0f) * Vector3.forward;
                camera.transform.position = b.center + from * 4f;
                camera.transform.LookAt(b.center);
                render.lights[0].intensity = 1.15f;
                render.lights[0].transform.rotation = Quaternion.LookRotation(-(Quaternion.Euler(-35f, -40f, 0f) * Vector3.forward));
                render.lights[1].intensity = 0.55f;
                render.lights[1].transform.rotation = Quaternion.LookRotation(-(Quaternion.Euler(-10f, 60f, 0f) * Vector3.forward));
                render.ambientColor = new Color(0.25f, 0.25f, 0.27f);
                render.BeginStaticPreview(new Rect(0f, 0f, width, height));
                render.DrawMesh(bodyMesh, Matrix4x4.identity, bodyMaterial, 0);
                for (int i = 0; i < lists.Length; i++)
                {
                    if (lists[i].Count == 0) continue;
                    Material m = Flat(PieceColor(i));
                    materials.Add(m);
                    render.DrawMesh(cloth, Matrix4x4.identity, m, i);
                }
                render.Render(true);
                Texture2D picture = render.EndStaticPreview();
                picture.hideFlags = HideFlags.HideAndDontSave;
                return picture;
            }
            finally
            {
                render.Cleanup();
                UnityEngine.Object.DestroyImmediate(cloth);
                foreach (Material m in materials) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            }
        }

        /// <summary>
        /// The turn about the body's up axis (degrees, 0 the front) to picture cloth from: a front three-quarter view,
        /// unless it sees less than 60% of the cloth (facing out from the body) that a back three-quarter view sees: a cap
        /// worn at the back of the head is pictured from behind, a shirt from the front. (Side views were tried: sleeves
        /// and trouser legs face sideways and drew them to the side.)
        /// </summary>
        public static float BestView(Vector3[] positions, IList<int> triangles, BodyCollider body)
        {
            float[] yaws = { -22f, 22f, -145f, 145f };
            var scores = new float[yaws.Length];
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Vector3 a = positions[triangles[t]], b = positions[triangles[t + 1]], c = positions[triangles[t + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                // Out from the body: the normal of the body under the cloth, level.
                if (body == null || !body.ClosestNear((a + b + c) / 3f, 0.03f, out int under, out _, out _)) continue;
                Vector3 outward = body.N[under];
                outward.y = 0f;
                if (outward.sqrMagnitude < 1e-6f) continue;
                outward.Normalize();
                for (int k = 0; k < yaws.Length; k++)
                {
                    Vector3 view = Quaternion.Euler(0f, yaws[k], 0f) * Vector3.forward;
                    scores[k] += area * Mathf.Max(0f, Vector3.Dot(outward, view));
                }
            }
            int front = scores[1] > scores[0] ? 1 : 0, best = 0;
            for (int k = 1; k < yaws.Length; k++) if (scores[k] > scores[best]) best = k;
            return scores[front] >= 0.6f * scores[best] ? yaws[front] : yaws[best];
        }

        private static int PieceOf(PatternMesh mesh, int vertex)
        {
            for (int p = 0; p < mesh.pieceStart.Length; p++)
            {
                if (vertex >= mesh.pieceStart[p] && vertex < mesh.pieceStart[p] + mesh.pieceCount[p]) return p;
            }
            return 0;
        }
    }

    /// <summary>
    /// One sample draped on a body and pictured, a few frames at a time so the Editor never waits long. The sample
    /// browser runs one after another in the background; a script can run one to the end with <see cref="Run"/>.
    /// </summary>
    public sealed class SampleDrape : IDisposable
    {
        public readonly string Name;
        private readonly ClothesPattern pattern;
        private readonly ClothesFit fit;
        private readonly FitSession session;
        private readonly int target;
        public int Frames { get; private set; }
        public bool Done => Frames >= target;
        public string Problem { get; private set; }

        public SampleDrape(string sampleName, BodyReference body, RaceData race, int frames = SamplePictures.DrapeFrames)
        {
            Name = sampleName;
            target = frames;
            // A picture needs neither tight UVs nor the pattern's own mesh: one level coarser meshes, lays out and
            // drapes in about a third of the time (the Oxford shirt started in 0.7 s at its own level).
            pattern = SamplePatterns.CreateQuick(sampleName);
            if (pattern == null) { Problem = "No sample named '" + sampleName + "'."; Frames = target; return; }
            pattern.hideFlags = HideFlags.HideAndDontSave;
            fit = ScriptableObject.CreateInstance<ClothesFit>();
            fit.hideFlags = HideFlags.HideAndDontSave;
            fit.pattern = pattern;
            fit.race = race;
            // Pleats need their own fine mesh along the folds (coarser, they came out jagged).
            bool pleated = false;
            foreach (PatternPiece piece in pattern.pieces) if (piece != null && piece.pleats.Count > 0) pleated = true;
            if (pattern.meshLevel != MeshLevel.Custom && !pleated)
            {
                fit.overrideMeshLevel = true;
                fit.meshLevel = (MeshLevel)Mathf.Max((int)MeshLevel.Draft, (int)pattern.meshLevel - 1);
            }
            session = new FitSession { Pattern = pattern, Fit = fit, Body = body };
            building = session.RebuildInSteps();
        }

        private IEnumerator<bool> building;

        /// <summary>True while the cloth is still being built (each <see cref="Advance"/> then does one part of it).</summary>
        public bool Building => building != null;

        /// <summary>Builds the next part of the cloth, or once it is built simulates up to this many more frames.</summary>
        public void Advance(int frames)
        {
            if (Done || Problem != null) return;
            if (building != null)
            {
                try
                {
                    if (building.MoveNext()) return;
                }
                catch (Exception e) { Problem = e.Message; Frames = target; }
                building = null;
                if (Problem == null && !session.HasCloth)
                {
                    Problem = session.Messages.Count > 0 ? string.Join(" ", session.Messages) : "The sample made no cloth.";
                    Frames = target;
                }
                return;
            }
            int step = Mathf.Min(frames, target - Frames);
            session.Step(step);
            Frames += step;
        }

        /// <summary>The picture of the drape as it is now (null if the sample could not be draped).</summary>
        public Texture2D Picture(Mesh bodyMesh)
        {
            if (Problem != null || session == null || !session.HasCloth) return null;
            Vector3[] positions = session.SyncPositions();
            return SamplePictures.RenderDrape(bodyMesh, session.Body.Collider, session.Mesh, positions, SamplePictures.DrapeWidth, SamplePictures.DrapeHeight);
        }

        /// <summary>Drapes to the end and pictures it, in one go (for scripts and the shipped pictures).</summary>
        public static Texture2D Run(string sampleName, BodyReference body, RaceData race, Mesh bodyMesh, out string problem)
        {
            using (var drape = new SampleDrape(sampleName, body, race))
            {
                while (!drape.Done) drape.Advance(50);
                problem = drape.Problem;
                return drape.Picture(bodyMesh);
            }
        }

        public void Dispose()
        {
            session?.Dispose();
            if (fit != null) UnityEngine.Object.DestroyImmediate(fit);
            if (pattern != null) UnityEngine.Object.DestroyImmediate(pattern);
        }
    }
}
