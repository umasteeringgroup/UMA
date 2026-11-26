using System;
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
        /// <summary>Cells across the grid the outlines are packed on (4 texels a cell in a 1024 texture).</summary>
        public const int PackGrid = 256;
        /// <summary>Steps of the search for the largest scale that fits, between 55% and 100% of the area bound.</summary>
        private const int ScaleSteps = 9;

        /// <summary>
        /// Packs every piece again. Moves painted regions; call only when the artist asks or nothing is laid out
        /// yet. At the largest scale that fits, by either of two ways: the pieces' outlines packed on a grid, largest
        /// first, each where it sits lowest, turned a quarter or half turn when that fits better
        /// (<see cref="UvLayoutSettings.turnPieces"/>); or their bounding boxes packed by UMA's atlas packer
        /// (<see cref="MaxRectsBinPack"/>). Measured over the 20 samples the cloth covers 63% of the texture
        /// (shelf packing by bounding box: 54%).
        /// </summary>
        /// <returns>The texel density reached at a Texture Detail of 1, in texels per centimetre.</returns>
        public static float Repack(ClothesPattern pattern) => Repack(pattern, out _);

        /// <param name="coverage">The share of the texture the cloth covers, 0 to 1.</param>
        public static float Repack(ClothesPattern pattern, out float coverage) => Pack(pattern, false, out coverage);

        /// <summary>
        /// Lays the pieces out by their bounding boxes only (UMA's MaxRects packer): a millisecond or two, against
        /// a few hundred for <see cref="Repack"/>, with less of the texture covered. For layouts redone many times a
        /// second (the garment generator while a slider moves); marks the pattern so the bake packs it tightly.
        /// </summary>
        public static float QuickPack(ClothesPattern pattern) => Pack(pattern, true, out _);

        private static float Pack(ClothesPattern pattern, bool boxesOnly, out float coverage)
        {
            coverage = 0f;
            if (pattern.uvLayout == null) pattern.uvLayout = new UvLayoutSettings();
            UvLayoutSettings settings = pattern.uvLayout;
            settings.quickPacked = boxesOnly;
            float aspect = Mathf.Max(0.05f, settings.aspect);
            int count = pattern.pieces.Count;
            var outlines = new List<Vector2>[count];
            var packed = new List<int>();
            for (int i = 0; i < count; i++)
            {
                PatternPiece piece = pattern.pieces[i];
                if (piece == null || piece.points.Count < 3) continue;
                if (piece.uvIsland == null) piece.uvIsland = new UvIsland();
                piece.uvIsland.sharesWith = null;
                if (settings.shareMirrors && MirrorSource(pattern, piece) != null) continue;
                outlines[i] = PatternGeometry.SampleOutline(piece, 0.5f, PieceScale.Identity);
                packed.Add(i);
            }
            int gutterCells = Mathf.Max(1, Mathf.CeilToInt(settings.gutterPixels * PackGrid / (float)Mathf.Max(1, settings.bakeResolution)));
            int[] turns = settings.turnPieces ? new[] { 0, 90, 180, 270 } : new[] { 0 };

            // An upper bound for the scale from the cloth's area, then the largest scale at which everything fits.
            double area = 0;
            foreach (int i in packed)
            {
                float w = Weight(pattern.pieces[i]);
                area += System.Math.Abs(PatternGeometry.SignedArea(outlines[i])) * w * w;
            }
            // No packing covers more than the whole square, so the area gives the top; outlines with gutters pack
            // well above half of it, so the search starts there and halves the gap.
            float hi = area > 0 ? (float)System.Math.Sqrt(1.0 / (area * aspect)) : 1f;
            float lo = hi * 0.55f;
            // Largest first, by the cloth's own area.
            var order = new List<int>(packed);
            order.Sort((a, b) =>
            {
                float wa = Weight(pattern.pieces[a]), wb = Weight(pattern.pieces[b]);
                double aa = System.Math.Abs(PatternGeometry.SignedArea(outlines[a])) * wa * wa, ab = System.Math.Abs(PatternGeometry.SignedArea(outlines[b])) * wb * wb;
                return ab.CompareTo(aa);
            });
            var packer = new Packer
            {
                pattern = pattern, outlines = outlines, order = order, aspect = aspect, gutterCells = gutterCells, turns = turns,
                resolution = Mathf.Max(64, settings.bakeResolution), gutterPixels = Mathf.Max(1, settings.gutterPixels), turnBoxes = settings.turnPieces,
                boxesOnly = boxesOnly
            };
            Placement[] best = packer.Pack(lo);
            while (best == null && lo > 1e-5f)
            {
                hi = lo; lo *= 0.7f;
                best = packer.Pack(lo);
            }
            for (int step = 0; step < ScaleSteps && best != null; step++)
            {
                float mid = (lo + hi) * 0.5f;
                Placement[] tried = packer.Pack(mid);
                if (tried != null) { lo = mid; best = tried; }
                else hi = mid;
            }
            if (best == null) best = new Placement[count];

            double covered = 0;
            foreach (int i in packed)
            {
                PatternPiece piece = pattern.pieces[i];
                Placement p = best[i];
                float k = lo * Weight(piece);
                piece.uvIsland.placed = true;
                piece.uvIsland.scale = k;
                piece.uvIsland.rotation = -piece.grainAngle + p.turn;
                Vector2 uvMin = p.corner;
                piece.uvIsland.offset = uvMin - p.min;
                piece.uvIsland.reserved = new Rect(uvMin, p.size);
                covered += System.Math.Abs(PatternGeometry.SignedArea(outlines[i])) * k * k * aspect;
            }
            for (int i = 0; i < count; i++)
            {
                PatternPiece piece = pattern.pieces[i];
                if (piece == null || piece.points.Count < 3 || outlines[i] != null) continue;
                ShareMirror(pattern, piece);
            }
            coverage = (float)covered;
            return lo * settings.bakeResolution;
        }

        private struct Placement
        {
            /// <summary>Where the piece's UV bounds start in the texture, its turn, and its own UV bounds before placing.</summary>
            public Vector2 corner, min, size;
            public int turn;
        }

        private static float Weight(PatternPiece piece) => Mathf.Clamp(piece.textureDetail <= 0f ? 1f : piece.textureDetail, 0.25f, 2f);

        /// <summary>The ways of packing tried at each scale: the outlines on a grid, and bounding boxes by UMA's atlas packer.</summary>
        private static readonly MaxRectsBinPack.FreeRectChoiceHeuristic[] BoxRules =
        {
            MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestShortSideFit,
            MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestAreaFit,
            MaxRectsBinPack.FreeRectChoiceHeuristic.RectBottomLeftRule,
            MaxRectsBinPack.FreeRectChoiceHeuristic.RectContactPointRule
        };

        /// <summary>
        /// Packs at one scale by whichever way fits: bounding boxes (UMA's MaxRects atlas packer, quick, best for
        /// pieces close to rectangles) and then outlines (slower, best for shaped pieces). The way that fitted last
        /// is tried first next time.
        /// </summary>
        private sealed class Packer
        {
            public ClothesPattern pattern;
            public List<Vector2>[] outlines;
            public List<int> order;
            public float aspect;
            public int gutterCells, resolution, gutterPixels;
            public int[] turns;
            public bool turnBoxes, boxesOnly;
            private int lastWay;

            /// <summary>How many ways there are: one per box rule, then the outlines.</summary>
            private int Ways => BoxRules.Length + (boxesOnly ? 0 : 1);

            public Placement[] Pack(float scale)
            {
                for (int n = 0; n < Ways; n++)
                {
                    int way = (lastWay + n) % Ways;
                    Placement[] result = way < BoxRules.Length
                        ? PackBoxes(pattern, outlines, order, scale, aspect, resolution, gutterPixels, turnBoxes, BoxRules[way])
                        : PackOutlines(pattern, outlines, order, scale, aspect, gutterCells, turns);
                    if (result != null) { lastWay = way; return result; }
                }
                return null;
            }
        }

        /// <summary>
        /// Packs the pieces' bounding boxes at one scale with UMA's atlas packer, in texels of the bake, each with
        /// the gutter on its right and top and the bin moved in by one gutter, so pieces and the border end a gutter
        /// apart. A box turned by the packer is the piece a quarter turn round. Null when one does not fit.
        /// </summary>
        private static Placement[] PackBoxes(ClothesPattern pattern, List<Vector2>[] outlines, List<int> order, float scale, float aspect, int resolution, int gutterPixels, bool turn, MaxRectsBinPack.FreeRectChoiceHeuristic rule)
        {
            // Boxes are packed in square texels (V divided by the aspect), so a quarter turn swaps width and height.
            int g = Mathf.Max(1, Mathf.CeilToInt(gutterPixels * Mathf.Max(1f, 1f / aspect)));
            int binW = resolution - g, binH = Mathf.FloorToInt(resolution / aspect) - g;
            if (binW <= 0 || binH <= 0) return null;
            var bin = new MaxRectsBinPack(binW, binH, turn);
            var result = new Placement[outlines.Length];
            foreach (int i in order)
            {
                PatternPiece piece = pattern.pieces[i];
                float k = scale * Weight(piece);
                PieceBounds(outlines[i], -piece.grainAngle, k, aspect, out Vector2 min, out Vector2 size);
                int w = Mathf.CeilToInt(size.x * resolution) + g, h = Mathf.CeilToInt(size.y / aspect * resolution) + g;
                Rect r = bin.Insert(w, h, rule);
                if (r.height <= 0) return null;
                bool turned = w != h && Mathf.RoundToInt(r.width) == h && Mathf.RoundToInt(r.height) == w;
                if (turned) PieceBounds(outlines[i], -piece.grainAngle + 90f, k, aspect, out min, out size);
                result[i] = new Placement
                {
                    corner = new Vector2((r.x + g) / resolution, (r.y + g) * aspect / resolution),
                    turn = turned ? 90 : 0,
                    min = min,
                    size = size
                };
            }
            return result;
        }

        /// <summary>A piece's UV bounds at a turn and scale, before placing.</summary>
        private static void PieceBounds(List<Vector2> outline, float rotation, float k, float aspect, out Vector2 min, out Vector2 size)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector2 p in outline)
            {
                Vector2 q = Rotate(p, rotation);
                Vector2 uv = new Vector2(q.x * k, q.y * k * aspect);
                min = Vector2.Min(min, uv); max = Vector2.Max(max, uv);
            }
            size = max - min;
        }

        /// <summary>
        /// Packs the pieces' outlines at one scale on the grid, in order, each at the lowest place it fits in any
        /// allowed turn (then the leftmost). Null when one does not fit.
        /// </summary>
        private static Placement[] PackOutlines(ClothesPattern pattern, List<Vector2>[] outlines, List<int> order, float scale, float aspect, int gutter, int[] turns)
        {
            int words = PackGrid / 64;
            var occupied = new ulong[PackGrid, words];
            // The gutter round the texture's border: taken from the start.
            for (int r = 0; r < PackGrid; r++)
            {
                for (int c = 0; c < PackGrid; c++)
                {
                    if (r < gutter || r >= PackGrid - gutter || c < gutter || c >= PackGrid - gutter) occupied[r, c >> 6] |= 1UL << (c & 63);
                }
            }
            // How many free cells run rightwards from each cell: a place is tested a span at a time, and a span that
            // meets a taken cell moves the next try past it (the places skipped all cover that cell).
            var freeRun = new int[PackGrid, PackGrid + 1];
            for (int r = 0; r < PackGrid; r++) FreeRuns(occupied, freeRun, r);
            var result = new Placement[outlines.Length];
            float cell = 1f / PackGrid;
            foreach (int i in order)
            {
                PatternPiece piece = pattern.pieces[i];
                float k = scale * Weight(piece);
                bool found = false;
                Placement chosen = default;
                ulong[,] chosenGrown = null;
                int chosenRows = 0, chosenX = 0, chosenY = 0;
                foreach (int turn in turns)
                {
                    // The piece itself is tested against what is taken; what is taken includes the gutter round
                    // every piece already placed, so neighbours end one gutter apart.
                    Mask(outlines[i], -piece.grainAngle + turn, k, aspect, gutter, out ulong[,] mask, out ulong[,] grown, out int rows, out int cols, out Vector2 min, out Vector2 size, out List<Vector2Int>[] spans);
                    if (rows > PackGrid || cols > PackGrid) continue;
                    for (int y = 0; y + rows <= PackGrid; y++)
                    {
                        if (found && y + rows >= chosenY + chosenRows) break;
                        bool placedHere = false;
                        for (int x = 0; x + cols <= PackGrid;)
                        {
                            int next = NextPlace(freeRun, spans, rows, x, y);
                            if (next < 0)
                            {
                                if (!found || y + rows < chosenY + chosenRows || (y + rows == chosenY + chosenRows && x < chosenX))
                                {
                                    chosen = new Placement { corner = new Vector2(x * cell, y * cell), turn = turn, min = min, size = size };
                                    chosenX = x; chosenY = y;
                                    chosenGrown = grown; chosenRows = rows; found = true;
                                }
                                placedHere = true;
                                break;
                            }
                            x = next;
                        }
                        if (placedHere) break;
                    }
                }
                if (!found) return null;
                Stamp(occupied, chosenGrown, chosenRows + 2 * gutter, chosenX - gutter, chosenY - gutter);
                for (int r = Mathf.Max(0, chosenY - gutter); r < Mathf.Min(PackGrid, chosenY + chosenRows + gutter); r++) FreeRuns(occupied, freeRun, r);
                result[i] = chosen;
            }
            return result;
        }

        private static void FreeRuns(ulong[,] occupied, int[,] freeRun, int r)
        {
            freeRun[r, PackGrid] = 0;
            for (int c = PackGrid - 1; c >= 0; c--) freeRun[r, c] = (occupied[r, c >> 6] & (1UL << (c & 63))) != 0 ? 0 : freeRun[r, c + 1] + 1;
        }

        /// <summary>
        /// -1 when a mask's spans, placed with its corner at (x, y), meet no taken cell; otherwise the next x worth
        /// trying: the first one at which the span that met a taken cell starts past it.
        /// </summary>
        private static int NextPlace(int[,] freeRun, List<Vector2Int>[] spans, int rows, int x, int y)
        {
            for (int r = 0; r < rows; r++)
            {
                List<Vector2Int> row = spans[r];
                for (int s = 0; s < row.Count; s++)
                {
                    int left = row[s].x, length = row[s].y - left + 1;
                    int run = freeRun[y + r, x + left];
                    if (run < length) return x + run + 1;
                }
            }
            return -1;
        }

        /// <summary>
        /// The cells a piece covers at a turn and scale, grown by the gutter on every side, as rows of bits;
        /// <paramref name="min"/> and <paramref name="size"/> are the piece's own UV bounds before placing.
        /// </summary>
        private static void Mask(List<Vector2> outline, float rotation, float k, float aspect, int gutter, out ulong[,] mask, out ulong[,] grown, out int rows, out int cols, out Vector2 min, out Vector2 size, out List<Vector2Int>[] spans)
        {
            spans = null;
            var uv = new Vector2[outline.Count];
            min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int j = 0; j < outline.Count; j++)
            {
                Vector2 q = Rotate(outline[j], rotation);
                uv[j] = new Vector2(q.x * k, q.y * k * aspect);
                min = Vector2.Min(min, uv[j]); max = Vector2.Max(max, uv[j]);
            }
            size = max - min;
            float cell = 1f / PackGrid;
            int inner = Mathf.Max(1, Mathf.CeilToInt(size.x / cell)), innerRows = Mathf.Max(1, Mathf.CeilToInt(size.y / cell));
            cols = inner; rows = innerRows;
            int words = PackGrid / 64;
            mask = new ulong[Mathf.Max(1, rows), words];
            grown = new ulong[Mathf.Max(1, rows + 2 * gutter), words];
            if (cols + 2 * gutter > PackGrid || rows + 2 * gutter > PackGrid) { rows = cols = PackGrid + 1; return; }
            var covered = new bool[innerRows, inner];
            // Cells whose centre is inside, a row at a time (where the row's centre line crosses the outline),
            // and cells the outline passes through (sampled finer than a cell).
            var crossings = new List<float>();
            for (int r = 0; r < innerRows; r++)
            {
                float y = min.y + (r + 0.5f) * cell;
                crossings.Clear();
                for (int j = 0, prev = uv.Length - 1; j < uv.Length; prev = j++)
                {
                    Vector2 a = uv[j], b = uv[prev];
                    if ((a.y > y) != (b.y > y)) crossings.Add(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                crossings.Sort();
                for (int s = 0; s + 1 < crossings.Count; s += 2)
                {
                    int c0 = Mathf.Max(0, Mathf.CeilToInt((crossings[s] - min.x) / cell - 0.5f));
                    int c1 = Mathf.Min(inner - 1, Mathf.FloorToInt((crossings[s + 1] - min.x) / cell - 0.5f));
                    for (int c = c0; c <= c1; c++) covered[r, c] = true;
                }
            }
            for (int j = 0; j < uv.Length; j++)
            {
                Vector2 a = uv[j], b = uv[(j + 1) % uv.Length];
                int steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / (cell * 0.5f)));
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / (float)steps) - min;
                    int c = Mathf.Clamp((int)(p.x / cell), 0, inner - 1), r = Mathf.Clamp((int)(p.y / cell), 0, innerRows - 1);
                    covered[r, c] = true;
                }
            }
            // The piece as runs of covered cells, row by row, for testing places a run at a time.
            spans = new List<Vector2Int>[innerRows];
            for (int r = 0; r < innerRows; r++)
            {
                var row = new List<Vector2Int>(2);
                for (int c = 0; c < inner; c++)
                {
                    if (!covered[r, c]) continue;
                    int start = c;
                    while (c + 1 < inner && covered[r, c + 1]) c++;
                    row.Add(new Vector2Int(start, c));
                }
                spans[r] = row;
            }
            // The piece, and grown by the gutter (a square round each covered cell, the mask's corner moved out by it).
            for (int r = 0; r < innerRows; r++)
            {
                for (int c = 0; c < inner; c++)
                {
                    if (!covered[r, c]) continue;
                    mask[r, c >> 6] |= 1UL << (c & 63);
                    for (int dr = 0; dr <= 2 * gutter; dr++)
                    {
                        int rr = r + dr;
                        for (int dc = 0; dc <= 2 * gutter; dc++)
                        {
                            int cc = c + dc;
                            grown[rr, cc >> 6] |= 1UL << (cc & 63);
                        }
                    }
                }
            }
        }

        private static void Stamp(ulong[,] occupied, ulong[,] mask, int rows, int x, int y)
        {
            int words = PackGrid / 64, shiftWords = x >> 6, shiftBits = x & 63;
            for (int r = 0; r < rows; r++)
            {
                for (int w = 0; w < words; w++)
                {
                    ulong m = mask[r, w];
                    if (m == 0) continue;
                    int target = w + shiftWords;
                    if (target < words) occupied[y + r, target] |= m << shiftBits;
                    if (shiftBits != 0 && target + 1 < words) occupied[y + r, target + 1] |= m >> (64 - shiftBits);
                }
            }
        }

        /// <summary>The piece a linked mirror is the mirror image of, or null.</summary>
        public static PatternPiece MirrorSource(ClothesPattern pattern, PatternPiece piece)
        {
            if (piece == null || !piece.mirrorLinked || string.IsNullOrEmpty(piece.mirrorOf)) return null;
            PatternPiece source = pattern.FindPiece(piece.mirrorOf);
            return source != null && source != piece && source.points.Count >= 3 && !source.mirrorLinked ? source : null;
        }

        /// <summary>
        /// Gives a linked mirror its original's island, mirrored: a point (x, y) of the mirror is the original's
        /// point (sum - x, y - dy), as <see cref="PatternEditing.UpdateLinkedMirrors"/> lays the mirror out.
        /// </summary>
        private static void ShareMirror(ClothesPattern pattern, PatternPiece piece)
        {
            PatternPiece source = MirrorSource(pattern, piece);
            if (source == null || source.uvIsland == null || !source.uvIsland.placed) return;
            Rect sourceBounds = source.Bounds(), copyBounds = piece.Bounds();
            piece.uvIsland.placed = true;
            piece.uvIsland.sharesWith = source.id;
            piece.uvIsland.mirrorSumX = sourceBounds.center.x + copyBounds.center.x;
            piece.uvIsland.mirrorDy = copyBounds.yMin - sourceBounds.yMin;
            piece.uvIsland.scale = source.uvIsland.scale;
            piece.uvIsland.rotation = source.uvIsland.rotation;
            piece.uvIsland.offset = source.uvIsland.offset;
            piece.uvIsland.reserved = source.uvIsland.reserved;
        }

        private static Vector2 Rotate(Vector2 p, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        /// <summary>True when two pieces map the pattern into the texture the same way (the halves of a cut piece).</summary>
        public static bool SameIsland(UvIsland a, UvIsland b) =>
            a != null && b != null && a.placed && b.placed && a.offset == b.offset && Mathf.Approximately(a.scale, b.scale) && Mathf.Approximately(a.rotation, b.rotation);

        /// <summary>The UV of a point on a piece as drawn (before the sizes are applied).</summary>
        public static Vector2 ToUV(ClothesPattern pattern, int pieceIndex, Vector2 drawn)
        {
            UvIsland island = pattern.pieces[pieceIndex].uvIsland;
            if (island != null && !string.IsNullOrEmpty(island.sharesWith))
            {
                // A mirror sharing its original's island: the original's point, through the original's island.
                PatternPiece source = pattern.FindPiece(island.sharesWith);
                if (source != null && source.uvIsland != null && source.uvIsland.placed)
                {
                    drawn = new Vector2(island.mirrorSumX - drawn.x, drawn.y - island.mirrorDy);
                    island = source.uvIsland;
                }
            }
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

        // The last check and what it was made from. A full check draws every triangle on a grid (23 ms on a
        // 9,000-point shirt), and the Publish panel and the Pattern Canvas's UV view ask for it on every GUI
        // event, which made the Scene view crawl while the designer was open.
        [ThreadStatic] private static List<string> lastProblems;
        [ThreadStatic] private static PatternMesh lastProblemsMesh;
        [ThreadStatic] private static ClothesPattern lastProblemsPattern;
        [ThreadStatic] private static ulong lastProblemsSignature;

        /// <summary>
        /// Islands that leave their reserved space, overlap another or leave the 0-1 square. Checked again only
        /// when the mesh, its UVs or the islands have changed since the last call.
        /// </summary>
        public static List<string> FindProblems(ClothesPattern pattern, PatternMesh mesh)
        {
            ulong signature = ProblemsSignature(pattern, mesh);
            if (lastProblems == null || !ReferenceEquals(mesh, lastProblemsMesh) || !ReferenceEquals(pattern, lastProblemsPattern) || signature != lastProblemsSignature)
            {
                lastProblems = FindProblemsNow(pattern, mesh);
                lastProblemsMesh = mesh;
                lastProblemsPattern = pattern;
                lastProblemsSignature = signature;
            }
            return new List<string>(lastProblems);
        }

        /// <summary>
        /// A hash of everything the check reads: the mesh's UVs, flat positions, triangles and pieces, and each
        /// piece's name, reserved space and the island it shares (a fraction of a millisecond on a large garment).
        /// </summary>
        private static ulong ProblemsSignature(ClothesPattern pattern, PatternMesh mesh)
        {
            // FNV-1a over 32-bit words, with the loops written out: this runs for every GUI event.
            const ulong prime = 1099511628211UL;
            ulong h = 14695981039346656037UL;
            List<Vector2> uv = mesh.uv, flat = mesh.flat;
            List<int> triangles = mesh.triangles, piece = mesh.piece;
            h = (h ^ (uint)uv.Count) * prime;
            h = (h ^ (uint)flat.Count) * prime;
            h = (h ^ (uint)triangles.Count) * prime;
            h = (h ^ (uint)piece.Count) * prime;
            for (int i = 0; i < uv.Count; i++)
            {
                Vector2 v = uv[i];
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(v.x)) * prime;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(v.y)) * prime;
            }
            for (int i = 0; i < flat.Count; i++)
            {
                Vector2 v = flat[i];
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(v.x)) * prime;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(v.y)) * prime;
            }
            for (int i = 0; i < triangles.Count; i++) h = (h ^ (uint)triangles[i]) * prime;
            for (int i = 0; i < piece.Count; i++) h = (h ^ (uint)piece[i]) * prime;
            h = (h ^ (uint)pattern.pieces.Count) * prime;
            foreach (PatternPiece p in pattern.pieces)
            {
                if (p == null) { h *= prime; continue; }
                h = (h ^ (uint)(p.name != null ? p.name.GetHashCode() : 0)) * prime;
                UvIsland island = p.uvIsland;
                if (island == null) { h = (h ^ 1u) * prime; continue; }
                Rect r = island.reserved;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(r.xMin)) * prime;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(r.yMin)) * prime;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(r.xMax)) * prime;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(r.yMax)) * prime;
                h = (h ^ (uint)(island.sharesWith != null ? island.sharesWith.GetHashCode() : 0)) * prime;
            }
            return h;
        }

        private static List<string> FindProblemsNow(ClothesPattern pattern, PatternMesh mesh)
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
            }
            // Overlaps, from the triangles themselves: packed outlines interlock, so their boxes overlap freely.
            foreach (Vector2Int pair in OverlappingPieces(pattern, mesh))
            {
                problems.Add("'" + pattern.pieces[pair.x].name + "' and '" + pattern.pieces[pair.y].name + "' overlap in the texture. Use Repack UVs.");
            }
            // Collapsed or flipped UV triangles (a mirror sharing its original's island is mirrored on purpose).
            int flipped = 0, collapsed = 0;
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                Vector2 a = mesh.uv[mesh.triangles[t]], b = mesh.uv[mesh.triangles[t + 1]], c = mesh.uv[mesh.triangles[t + 2]];
                UvIsland own = pattern.pieces[mesh.piece[mesh.triangles[t]]].uvIsland;
                bool mirroredOnPurpose = own != null && !string.IsNullOrEmpty(own.sharesWith);
                Vector2 fa = mesh.flat[mesh.triangles[t]], fb = mesh.flat[mesh.triangles[t + 1]], fc = mesh.flat[mesh.triangles[t + 2]];
                float uvArea = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                float flatArea = (fb.x - fa.x) * (fc.y - fa.y) - (fb.y - fa.y) * (fc.x - fa.x);
                if (Mathf.Abs(uvArea) < 1e-12f) collapsed++;
                else if ((Mathf.Sign(uvArea) != Mathf.Sign(flatArea)) != mirroredOnPurpose) flipped++;
            }
            if (collapsed > 0) problems.Add(collapsed + " triangles have no area in the texture.");
            if (flipped > 0) problems.Add(flipped + " triangles are mirrored in the texture.");
            return problems;
        }

        /// <summary>
        /// Pairs of pieces whose triangles cover the same texels (more than a few cells of a fine grid, so
        /// triangles meeting along a gutter's edge do not count). Halves of a cut piece and a mirror sharing
        /// its original's island are allowed to.
        /// </summary>
        public static List<Vector2Int> OverlappingPieces(ClothesPattern pattern, PatternMesh mesh)
        {
            const int grid = 512;
            const int allowedCells = 3;
            var owner = new int[grid * grid];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
            var counts = new Dictionary<long, int>();
            for (int t = 0; t + 2 < mesh.triangles.Count; t += 3)
            {
                int ia = mesh.triangles[t], ib = mesh.triangles[t + 1], ic = mesh.triangles[t + 2];
                int piece = mesh.piece[ia];
                Vector2 a = mesh.uv[ia] * grid, b = mesh.uv[ib] * grid, c = mesh.uv[ic] * grid;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(grid - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(grid - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area) < 1e-9f) continue;
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                        float w0 = ((b.x - p.x) * (c.y - p.y) - (b.y - p.y) * (c.x - p.x)) / area;
                        float w1 = ((c.x - p.x) * (a.y - p.y) - (c.y - p.y) * (a.x - p.x)) / area;
                        if (w0 < 0f || w1 < 0f || w0 + w1 > 1f) continue;
                        int index = y * grid + x;
                        int other = owner[index];
                        if (other < 0) { owner[index] = piece; continue; }
                        if (other == piece || SharesTexels(pattern, other, piece)) continue;
                        long key = Mathf.Min(other, piece) * 100000L + Mathf.Max(other, piece);
                        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                }
            }
            var result = new List<Vector2Int>();
            foreach (var kv in counts) if (kv.Value > allowedCells) result.Add(new Vector2Int((int)(kv.Key / 100000L), (int)(kv.Key % 100000L)));
            return result;
        }

        private static bool SharesTexels(ClothesPattern pattern, int a, int b)
        {
            PatternPiece pa = pattern.pieces[a], pb = pattern.pieces[b];
            if (SameIsland(pa.uvIsland, pb.uvIsland)) return true;
            return (pa.uvIsland != null && pa.uvIsland.sharesWith == pb.id) || (pb.uvIsland != null && pb.uvIsland.sharesWith == pa.id);
        }
    }
}
