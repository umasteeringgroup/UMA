using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Turns modelled clothes into a sewing pattern. Each UV island becomes a piece, drawn from its UVs at
    /// its true size (measured on the 3D mesh) and turned so its top is up. Where two islands' edges lie on
    /// the same 3D points (the island's "battle buddies"), they are sewn; free edges are openings. Each
    /// piece is placed round the body part it was worn on. The original UVs are kept, so the slot's
    /// textures still fit.
    /// <para>
    /// Modelled clothes usually have their folds and wrinkles baked into the mesh, and those make the
    /// surface larger than the flat cloth would be. The converter smooths a copy of the mesh to measure
    /// how much area the folds add to each island and shrinks the piece by that much (the fold allowance),
    /// so the simulation can make its own folds.
    /// </para>
    /// </summary>
    public static class PatternFromMesh
    {
        public sealed class Result
        {
            public ClothesPattern Pattern;
            public readonly List<string> Messages = new List<string>();
            public int Islands, Pieces, Seams, MirroredIslands, DroppedIslands, Holes;
            /// <summary>The mean share of each island's area its baked-in folds added, which was taken out (0-1).</summary>
            public float MeanFoldShrink;
        }

        public sealed class Settings
        {
            /// <summary>How closely the outlines follow the island's boundary (cm). Smaller keeps more points.</summary>
            public float OutlineToleranceCm = 0.4f;
            /// <summary>Smoothing passes used to measure the folds. 0 keeps the modelled size.</summary>
            public int FoldSmoothing = 20;
            /// <summary>Islands smaller than this are left out (cm²): trims, labels and slivers a pattern does not need.</summary>
            public float MinimumIslandCm2 = 25f;
        }

        /// <param name="positions">The clothes as worn, in the body's (renderer's) space, metres.</param>
        /// <param name="uv">The texture coordinates: one island per piece.</param>
        /// <param name="triangles">The triangles, facing out of the clothes.</param>
        /// <param name="body">The body's landmarks, to place each piece round its part. Null lays every piece round the torso.</param>
        public static Result Convert(string name, Vector3[] positions, Vector2[] uv, int[] triangles, BodyLandmarks body, Settings settings = null)
        {
            settings = settings ?? new Settings();
            var result = new Result();
            int n = positions.Length;
            int[] group = Weld(positions);

            // UV islands: triangles joined through shared vertex indices (a UV seam splits the vertices).
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = Find(triangles[t]), b = Find(triangles[t + 1]), c = Find(triangles[t + 2]);
                parent[b] = a; parent[Find(c)] = a;
            }
            var islandOf = new Dictionary<int, int>();
            var islandTriangles = new List<List<int>>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int root = Find(triangles[t]);
                if (!islandOf.TryGetValue(root, out int island)) { island = islandTriangles.Count; islandOf[root] = island; islandTriangles.Add(new List<int>()); }
                islandTriangles[island].Add(t);
            }
            var vertexIsland = new int[n];
            for (int i = 0; i < n; i++) vertexIsland[i] = islandOf.TryGetValue(Find(i), out int isl) ? isl : -1;
            result.Islands = islandTriangles.Count;

            // The folds: smooth a welded copy and compare areas island by island.
            Vector3[] smooth = settings.FoldSmoothing > 0 ? Smooth(positions, triangles, group, settings.FoldSmoothing) : positions;

            // Every boundary edge (used by one triangle), keyed by its welded ends to find its partner.
            var edgeUse = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    long key = Key(triangles[t + k], triangles[t + (k + 1) % 3]);
                    edgeUse[key] = edgeUse.TryGetValue(key, out int u) ? u + 1 : 1;
                }
            }
            // Directed boundary edges in triangle order, and welded-key -> directed edges.
            var next = new Dictionary<int, List<int>>();
            var byWelded = new Dictionary<long, List<(int a, int b)>>(EdgeKeyComparer.Instance);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = triangles[t + k], b = triangles[t + (k + 1) % 3];
                    if (edgeUse[Key(a, b)] != 1) continue;
                    if (!next.TryGetValue(a, out List<int> list)) next[a] = list = new List<int>(2);
                    list.Add(b);
                    long wk = Key(group[a], group[b]);
                    if (!byWelded.TryGetValue(wk, out List<(int, int)> partners)) byWelded[wk] = partners = new List<(int, int)>(2);
                    partners.Add((a, b));
                }
            }

            ClothesPattern pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            pattern.name = name;
            pattern.notes = "Converted from the modelled clothes '" + name + "'. Sized for the body it was worn on.";
            pattern.uvLayout.aspect = 1f;
            var pieceOfIsland = new int[islandTriangles.Count];
            var islands = new List<IslandOutline>();
            double foldSum = 0;
            for (int island = 0; island < islandTriangles.Count; island++)
            {
                pieceOfIsland[island] = -1;
                IslandOutline outline = Outline(island, islandTriangles[island], triangles, positions, smooth, uv, next, body, settings, result, out float foldShrink);
                if (outline == null) continue;
                foldSum += foldShrink;
                pieceOfIsland[island] = pattern.pieces.Count;
                outline.pieceIndex = pattern.pieces.Count;
                outline.piece.name = PieceName(outline.piece.placement, pattern.pieces.Count);
                islands.Add(outline);
                pattern.pieces.Add(outline.piece);
            }
            result.Pieces = pattern.pieces.Count;
            result.MeanFoldShrink = islands.Count > 0 ? (float)(foldSum / islands.Count) : 0f;

            // Runs: stretches of an outline sewn to one other stretch, or open. Corners where the partner changes.
            var runs = new List<Run>();
            // Islands are numbered by piece from here on.
            var vertexPiece = new int[n];
            for (int i = 0; i < n; i++) vertexPiece[i] = vertexIsland[i] >= 0 ? pieceOfIsland[vertexIsland[i]] : -1;
            foreach (IslandOutline o in islands) SplitRuns(o, group, byWelded, vertexPiece, runs);
            foreach (IslandOutline o in islands)
            {
                // Simplifying can make an outline cross itself where it doubles back; keep more points then.
                float tolerance = settings.OutlineToleranceCm;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    o.piece.points.Clear(); o.piece.edges.Clear();
                    foreach (int id in o.runIds) runs[id].edges.Clear();
                    BuildEdges(o, runs, tolerance);
                    if (!PatternGeometry.OutlineSelfIntersects(o.piece)) break;
                    tolerance *= 0.4f;
                }
                if (PatternGeometry.OutlineSelfIntersects(o.piece)) result.Messages.Add("The outline of '" + o.piece.name + "' crosses itself (its UV island folds over). Fix it on the Pattern Canvas before simulating.");
            }
            LinkPartners(runs, group, result);
            // Seams between partner runs, once per pair.
            var done = new HashSet<long>();
            foreach (Run r in runs)
            {
                if (r.partner < 0) continue;
                long pair = r.index < r.partner ? ((long)r.index << 32) | (uint)r.partner : ((long)r.partner << 32) | (uint)r.index;
                if (!done.Add(pair)) continue;
                result.Seams += Sew(pattern, r, runs[r.partner], islands);
            }
            pattern.Migrate();
            result.Pattern = pattern;
            if (result.DroppedIslands > 0) result.Messages.Add(result.DroppedIslands + " tiny islands (under " + settings.MinimumIslandCm2 + " cm²) were left out.");
            if (result.Holes > 0) result.Messages.Add(result.Holes + " holes inside islands were left out: a piece has one outline.");
            if (result.MirroredIslands > 0) result.Messages.Add(result.MirroredIslands + " islands are mirrored in the slot's texture; their pieces keep their place in the texture but the print on them reads mirrored.");
            result.Messages.Add("Folds: the pieces were made " + (result.MeanFoldShrink * 100f).ToString("0.0") + "% smaller in area on average, the cloth the modelled folds took up.");
            return result;
        }

        /// <summary>
        /// Converts the clothes of a wardrobe recipe: a hidden character of the race wears it in the pose
        /// the race was modelled in, and each of the recipe's slots is converted (their islands together, as
        /// one pattern). The character is removed afterwards. Null when the race cannot wear the recipe.
        /// </summary>
        public static Result FromWardrobeRecipe(RaceData race, UMA.CharacterSystem.UMAWardrobeRecipe recipe, BodyLandmarks body, Settings settings = null)
        {
            if (race == null || recipe == null) return null;
            GameObject go = DesignerTemporaryObjects.Create(DesignerTemporaryObjects.ConverterName, HideFlags.HideAndDontSave);
            go.transform.position = new Vector3(300f, 0f, 0f);
            var temporary = new List<UnityEngine.Object> { go };
            var baked = new Mesh();
            temporary.Add(baked);
            try
            {
                var avatar = go.AddComponent<UMA.CharacterSystem.DynamicCharacterAvatar>();
                avatar.editorTimeGeneration = false;
                avatar.showPlaceholder = false;
                avatar.activeRace.name = race.raceName;
                avatar.activeRace.data = race;
                if (avatar.predefinedDNA == null) avatar.predefinedDNA = new UMAPredefinedDNA();
                avatar.ignoreMeshHideAssets = true;
                avatar.GenerateNow();
                avatar.enabled = false;
                var baseSlots = new HashSet<string>();
                foreach (SlotData s in avatar.umaData.umaRecipe.slotDataList) if (s != null) baseSlots.Add(s.slotName);
                avatar.SetSlot(recipe);
                avatar.BuildCharacter(false, true);
                var modelled = VertexEditorStage.CreateTPoseWithoutHumanPose(avatar.umaData.GetTPose());
                if (modelled != null) { avatar.umaData.OverrideTpose = modelled; temporary.Add(modelled); }
                avatar.umaData.Dirty(true, true, true);
                UMAAssetIndexer.Instance.Generator.GenerateSingleUMA(avatar.umaData, false);
                VertexEditorStage.ApplyModelledPose(avatar);
                SkinnedMeshRenderer renderer = avatar.umaData.GetRenderer(0);
                renderer.BakeMesh(baked, true);
                Vector3[] all = baked.vertices;
                var positions = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                var names = new List<string>();
                foreach (SlotData worn in avatar.umaData.umaRecipe.slotDataList)
                {
                    if (worn == null || worn.asset == null || worn.asset.meshData == null || baseSlots.Contains(worn.slotName)) continue;
                    UMAMeshData md = worn.asset.meshData;
                    int start = positions.Count;
                    for (int v = 0; v < md.vertexCount; v++) { positions.Add(all[worn.vertexOffset + v]); uvs.Add(md.uv != null && v < md.uv.Length ? md.uv[v] : Vector2.zero); }
                    foreach (var sm in md.submeshes) foreach (int t in sm.GetBaseTriangles()) triangles.Add(start + t);
                    names.Add(worn.slotName);
                }
                if (positions.Count == 0) return null;
                Result result = Convert(recipe.name, positions.ToArray(), uvs.ToArray(), triangles.ToArray(), body, settings);
                result.Messages.Insert(0, "Converted " + string.Join(", ", names) + " from '" + recipe.name + "' as worn by " + race.raceName + ".");
                return result;
            }
            finally
            {
                foreach (UnityEngine.Object o in temporary) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
        }

        // ---- Islands ---------------------------------------------------------------------------------

        private sealed class IslandOutline
        {
            public PatternPiece piece;
            public int pieceIndex;
            /// <summary>The outer boundary loop, vertex indices, counter-clockwise as drawn.</summary>
            public List<int> loop;
            /// <summary>Each loop vertex as drawn (cm).</summary>
            public List<Vector2> drawn;
            /// <summary>The runs of this outline, in loop order, and where each starts in the loop.</summary>
            public List<int> runIds = new List<int>();
            public List<int> runStart = new List<int>();
        }

        private sealed class Run
        {
            public int index, island, outline;
            /// <summary>Loop positions from first to last (inclusive), wrapping.</summary>
            public List<int> loopIndices = new List<int>();
            public List<int> vertices = new List<int>();
            /// <summary>Cumulative 3D length fraction at each vertex.</summary>
            public float[] fraction;
            public int partner = -1;
            public int partnerIsland = -1;
            /// <summary>True when the partner walks the shared points the other way (the usual case).</summary>
            public bool partnerReversed = true;
            /// <summary>The run's pattern edges: their indices on the piece and the run fractions they cover.</summary>
            public List<(int edge, float t0, float t1)> edges = new List<(int, float, float)>();
        }

        private static IslandOutline Outline(int island, List<int> tris, int[] triangles, Vector3[] positions, Vector3[] smooth, Vector2[] uv,
            Dictionary<int, List<int>> next, BodyLandmarks body, Settings settings, Result result, out float foldShrink)
        {
            foldShrink = 0f;
            double area3 = 0, areaSmooth = 0, areaUv = 0, signedUv = 0;
            Vector3 centroid = Vector3.zero;
            foreach (int t in tris)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                float a3 = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).magnitude * 0.5f;
                area3 += a3;
                areaSmooth += Vector3.Cross(smooth[b] - smooth[a], smooth[c] - smooth[a]).magnitude * 0.5f;
                float su = ((uv[b].x - uv[a].x) * (uv[c].y - uv[a].y) - (uv[b].y - uv[a].y) * (uv[c].x - uv[a].x)) * 0.5f;
                signedUv += su; areaUv += Mathf.Abs(su);
                centroid += (positions[a] + positions[b] + positions[c]) / 3f * a3;
            }
            if (area3 * 1e4 < settings.MinimumIslandCm2 || areaUv <= 1e-12) { result.DroppedIslands++; return null; }
            centroid /= (float)area3;
            foldShrink = Mathf.Clamp01(1f - (float)(areaSmooth / area3));
            float fold = Mathf.Sqrt(Mathf.Clamp((float)(areaSmooth / area3), 0.7f, 1f));
            float cmPerUv = Mathf.Sqrt((float)(area3 / areaUv)) * 100f * fold;
            // Front faces turn clockwise in Unity: an island laid out as seen from outside has clockwise UVs.
            bool mirrored = signedUv > 0;
            if (mirrored) result.MirroredIslands++;

            // The part it is worn on, and its "up" there, to turn the piece upright.
            PiecePlacement placement = Place(centroid, body);
            Func<Vector3, float> up = Up(placement.part, body);
            var verts = new HashSet<int>();
            foreach (int t in tris) { verts.Add(triangles[t]); verts.Add(triangles[t + 1]); verts.Add(triangles[t + 2]); }
            Vector2 Flat(Vector2 p) => mirrored ? new Vector2(-p.x, p.y) : p;
            // Least squares: up(p) ~ g . flat(uv) + c; the gradient g is the piece's up direction.
            double suu = 0, suv = 0, svv = 0, sfu = 0, sfv = 0, su0 = 0, sv0 = 0, sf0 = 0; int count = 0;
            foreach (int v in verts) { Vector2 q = Flat(uv[v]); su0 += q.x; sv0 += q.y; sf0 += up(positions[v]); count++; }
            double mu = su0 / count, mv = sv0 / count, mf = sf0 / count;
            foreach (int v in verts)
            {
                Vector2 q = Flat(uv[v]); double du = q.x - mu, dv = q.y - mv, df = up(positions[v]) - mf;
                suu += du * du; suv += du * dv; svv += dv * dv; sfu += df * du; sfv += df * dv;
            }
            double det = suu * svv - suv * suv;
            Vector2 g = Math.Abs(det) > 1e-20 ? new Vector2((float)((sfu * svv - sfv * suv) / det), (float)((sfv * suu - sfu * suv) / det)) : Vector2.up;
            float turn = g.sqrMagnitude > 1e-12f ? Mathf.Atan2(g.x, g.y) * Mathf.Rad2Deg : 0f; // turning by this brings g to +y
            Vector2 Draw(Vector2 p) => Rotate(Flat(p) * cmPerUv, turn);

            // The outer boundary: of the island's loops, the one enclosing the most area.
            var loops = new List<List<int>>();
            var used = new HashSet<long>(EdgeKeyComparer.Instance);
            foreach (int start in verts)
            {
                if (!next.TryGetValue(start, out List<int> outs)) continue;
                foreach (int first in outs)
                {
                    if (used.Contains(((long)start << 32) | (uint)first)) continue;
                    var loop = new List<int> { start };
                    int prev = start, cur = first;
                    used.Add(((long)start << 32) | (uint)first);
                    int guard = 0;
                    while (cur != start && guard++ < 100000)
                    {
                        loop.Add(cur);
                        if (!next.TryGetValue(cur, out List<int> o) || o.Count == 0) break;
                        int pick = o[0];
                        foreach (int candidate in o) if (!used.Contains(((long)cur << 32) | (uint)candidate)) { pick = candidate; break; }
                        used.Add(((long)cur << 32) | (uint)pick);
                        prev = cur; cur = pick;
                    }
                    if (cur == start && loop.Count >= 3) loops.Add(loop);
                }
            }
            if (loops.Count == 0) { result.DroppedIslands++; return null; }
            List<int> outer = null; float best = -1f;
            foreach (List<int> l in loops)
            {
                var pts = new List<Vector2>(); foreach (int v in l) pts.Add(Draw(uv[v]));
                float a = Mathf.Abs(PatternGeometry.SignedArea(pts));
                if (a > best) { best = a; outer = l; }
            }
            result.Holes += loops.Count - 1;
            var drawn = new List<Vector2>();
            foreach (int v in outer) drawn.Add(Draw(uv[v]));
            if (PatternGeometry.SignedArea(drawn) < 0f) { outer.Reverse(); drawn.Reverse(); }

            // The direction the piece faces: where its middle near the top is worn. (The centre of area can
            // hang well away from it, as a sleeve's does under the arm.)
            Vector2 dMin = new Vector2(float.MaxValue, float.MaxValue), dMax = -dMin;
            foreach (int v in verts) { Vector2 d = Draw(uv[v]); dMin = Vector2.Min(dMin, d); dMax = Vector2.Max(dMax, d); }
            Vector2 anchorDrawn = new Vector2((dMin.x + dMax.x) * 0.5f, Mathf.Lerp(dMin.y, dMax.y, 0.75f));
            int anchor = -1; float nearest = float.MaxValue;
            foreach (int v in verts) { float d = (Draw(uv[v]) - anchorDrawn).sqrMagnitude; if (d < nearest) { nearest = d; anchor = v; } }
            if (anchor >= 0) placement.angle = AngleOn(placement.part, positions[anchor], body, placement.angle);
            // Where the piece's top is, for its placement, and the UVs kept as they were.
            float topUp = float.MinValue;
            foreach (int v in verts) topUp = Mathf.Max(topUp, up(positions[v]));
            placement.topAboveShoulderCm = TopOffset(placement.part, topUp, body);
            var piece = new PatternPiece { id = ClothesPattern.NewId(), name = "Piece", placement = placement, fabric = FabricPresets.Create(FabricPresets.DefaultName) };
            // ToUV(drawn) = offset + rotate(drawn, back) / cmPerUv gives the original UV. A mirrored island's
            // drawn x is the UV's -x: shifted by its rectangle's width it lands back in its own rectangle.
            Rect bounds = UvBounds(verts, uv);
            piece.uvIsland = new UvIsland
            {
                placed = true,
                scale = 1f / cmPerUv,
                rotation = -turn,
                offset = mirrored ? new Vector2(bounds.xMin + bounds.xMax, 0f) : Vector2.zero,
                reserved = bounds
            };
            return new IslandOutline { piece = piece, loop = outer, drawn = drawn };
        }

        private static Rect UvBounds(IEnumerable<int> verts, Vector2[] uv)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            foreach (int v in verts) { min = Vector2.Min(min, uv[v]); max = Vector2.Max(max, uv[v]); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static string PieceName(PiecePlacement p, int index)
        {
            string part = p.part == BodyPart.Torso ? (Mathf.Abs(Mathf.DeltaAngle(p.angle, 180f)) < 60f ? "Back" : Mathf.Abs(p.angle) < 60f ? "Front" : p.angle > 0f ? "Left Side" : "Right Side")
                : p.part == BodyPart.LeftArm ? "Left Sleeve" : p.part == BodyPart.RightArm ? "Right Sleeve"
                : p.part == BodyPart.LeftLeg ? (Mathf.Abs(p.angle) < 90f ? "Left Front Leg" : "Left Back Leg")
                : p.part == BodyPart.RightLeg ? (Mathf.Abs(p.angle) < 90f ? "Right Front Leg" : "Right Back Leg") : "Hood";
            return part + " " + (index + 1);
        }

        // ---- Placement ---------------------------------------------------------------------------------

        /// <summary>The body part nearest a point, and the direction of the point round it.</summary>
        public static PiecePlacement Place(Vector3 p, BodyLandmarks body)
        {
            var placement = new PiecePlacement();
            if (body == null || !body.valid) return placement;
            Vector2 c = body.torsoCenter;
            placement.angle = Mathf.Round(Mathf.Atan2(-(p.x - c.x), p.z - c.y) * Mathf.Rad2Deg);
            if (!body.limbs) return placement;
            if (p.y > body.neckY + 0.04f)
            {
                placement.part = BodyPart.Head;
                placement.angle = Mathf.Round(Mathf.Atan2(-(p.x - body.headCenter.x), p.z - body.headCenter.y) * Mathf.Rad2Deg);
                return placement;
            }
            foreach (bool left in new[] { true, false })
            {
                body.ArmFrame(left, out Vector3 root, out Vector3 axis, out Vector3 top, out Vector3 side);
                float length = ((left ? body.leftWrist : body.rightWrist) - root).magnitude;
                Vector3 d = p - root;
                float s = Vector3.Dot(d, axis);
                Vector3 off = d - axis * s;
                if (s > 0.08f && s < length + 0.15f && off.magnitude < body.armRadius + 0.08f)
                {
                    placement.part = left ? BodyPart.LeftArm : BodyPart.RightArm;
                    float a = Mathf.Atan2(Vector3.Dot(off, side), Vector3.Dot(off, top)) * Mathf.Rad2Deg;
                    placement.angle = Mathf.Round(left ? -a : a);
                    return placement;
                }
            }
            if (p.y < body.crotchY + 0.02f && Mathf.Abs(p.x - c.x) > 0.02f)
            {
                bool left = p.x < c.x;
                Vector3 a = body.LegAxisAt(left, p.y);
                placement.part = left ? BodyPart.LeftLeg : BodyPart.RightLeg;
                placement.angle = Mathf.Round(Mathf.Atan2(-(p.x - a.x), p.z - a.z) * Mathf.Rad2Deg);
            }
            return placement;
        }

        /// <summary>The direction of a point round a given part, in that part's angle convention.</summary>
        public static float AngleOn(BodyPart part, Vector3 p, BodyLandmarks body, float fallback)
        {
            if (body == null || !body.valid) return fallback;
            switch (part)
            {
                case BodyPart.Head:
                    return Mathf.Round(Mathf.Atan2(-(p.x - body.headCenter.x), p.z - body.headCenter.y) * Mathf.Rad2Deg);
                case BodyPart.LeftArm:
                case BodyPart.RightArm:
                {
                    bool left = part == BodyPart.LeftArm;
                    body.ArmFrame(left, out Vector3 root, out Vector3 axis, out Vector3 top, out Vector3 side);
                    Vector3 d = p - root;
                    Vector3 off = d - axis * Vector3.Dot(d, axis);
                    float a = Mathf.Atan2(Vector3.Dot(off, side), Vector3.Dot(off, top)) * Mathf.Rad2Deg;
                    return Mathf.Round(left ? -a : a);
                }
                case BodyPart.LeftLeg:
                case BodyPart.RightLeg:
                {
                    Vector3 a = body.LegAxisAt(part == BodyPart.LeftLeg, p.y);
                    return Mathf.Round(Mathf.Atan2(-(p.x - a.x), p.z - a.z) * Mathf.Rad2Deg);
                }
                default:
                    return Mathf.Round(Mathf.Atan2(-(p.x - body.torsoCenter.x), p.z - body.torsoCenter.y) * Mathf.Rad2Deg);
            }
        }

        /// <summary>The measure of "up" a piece on a part is turned to: height, or along the arm towards the shoulder.</summary>
        private static Func<Vector3, float> Up(BodyPart part, BodyLandmarks body)
        {
            if (PiecePlacement.IsArm(part) && body != null)
            {
                body.ArmFrame(part == BodyPart.LeftArm, out Vector3 root, out Vector3 axis, out _, out _);
                return p => -Vector3.Dot(p - root, axis);
            }
            return p => p.y;
        }

        private static float TopOffset(BodyPart part, float topUp, BodyLandmarks body)
        {
            if (body == null || !body.valid) return 4f;
            switch (part)
            {
                case BodyPart.Head: return Mathf.Round((topUp - body.headTopY) * 100f + 2f);
                case BodyPart.LeftArm:
                case BodyPart.RightArm: return Mathf.Round(topUp * 100f + 1f);
                case BodyPart.LeftLeg:
                case BodyPart.RightLeg: return Mathf.Round((topUp - body.waistY) * 100f + 1f);
                default: return Mathf.Round((topUp - body.shoulderTopY) * 100f + 2f);
            }
        }

        // ---- Runs, edges and seams ----------------------------------------------------------------------

        private static void SplitRuns(IslandOutline o, int[] group, Dictionary<long, List<(int a, int b)>> byWelded, int[] vertexIsland, List<Run> runs)
        {
            int m = o.loop.Count;
            // For each boundary segment, the partner segment (another boundary edge on the same welded points).
            var partnerA = new int[m];
            var partnerIsland = new int[m];
            for (int i = 0; i < m; i++)
            {
                int a = o.loop[i], b = o.loop[(i + 1) % m];
                partnerA[i] = -1; partnerIsland[i] = -1;
                if (!byWelded.TryGetValue(Key(group[a], group[b]), out List<(int a, int b)> list)) continue;
                foreach ((int pa, int pb) in list)
                {
                    if ((pa == a && pb == b) || (pa == b && pb == a)) continue;
                    partnerA[i] = pa; partnerIsland[i] = vertexIsland[pa];
                    break;
                }
            }
            // Start at a change of partner, so no run wraps past the loop's start.
            int start = 0;
            for (int i = 0; i < m; i++) if (partnerIsland[i] != partnerIsland[(i - 1 + m) % m]) { start = i; break; }
            Run current = null;
            for (int k = 0; k < m; k++)
            {
                int i = (start + k) % m;
                bool breakHere = current == null || partnerIsland[i] != current.partnerIsland;
                if (breakHere)
                {
                    current = new Run { index = runs.Count, island = o.pieceIndex, outline = o.pieceIndex, partnerIsland = partnerIsland[i] };
                    runs.Add(current);
                    o.runIds.Add(current.index);
                    o.runStart.Add(i);
                    current.loopIndices.Add(i);
                }
                current.loopIndices.Add((i + 1) % m);
            }
        }

        /// <summary>Each sewn run's partner: the run on the other island that walks the same welded points the other way.</summary>
        private static void LinkPartners(List<Run> runs, int[] group, Result result)
        {
            var segmentRun = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            foreach (Run r in runs)
            {
                for (int k = 0; k + 1 < r.vertices.Count; k++) segmentRun[((long)group[r.vertices[k]] << 32) | (uint)group[r.vertices[k + 1]]] = r.index;
            }
            int unmatched = 0;
            foreach (Run r in runs)
            {
                if (r.partnerIsland < 0 || r.vertices.Count < 2) continue;
                long backwards = ((long)group[r.vertices[1]] << 32) | (uint)group[r.vertices[0]];
                long forwards = ((long)group[r.vertices[0]] << 32) | (uint)group[r.vertices[1]];
                if (segmentRun.TryGetValue(backwards, out int other) && other != r.index) { r.partner = other; r.partnerReversed = true; }
                else if (segmentRun.TryGetValue(forwards, out other) && other != r.index) { r.partner = other; r.partnerReversed = false; }
                if (r.partner < 0) { unmatched++; continue; }
                // Only stretches that walk the same welded points end to end are sewn: a partial or
                // tangled match would pull the cloth across itself.
                Run o = runs[r.partner];
                int first = group[r.vertices[0]], last = group[r.vertices[r.vertices.Count - 1]];
                int oFirst = group[o.vertices[0]], oLast = group[o.vertices[o.vertices.Count - 1]];
                bool ends = r.partnerReversed ? first == oLast && last == oFirst : first == oFirst && last == oLast;
                if (!ends || r.vertices.Count != o.vertices.Count) { r.partner = -1; unmatched++; }
            }
            if (unmatched > 0) result.Messages.Add(unmatched + " stretches of outline meet another island's edge in a way the converter could not pair; they were left open.");
        }

        private static void BuildEdges(IslandOutline o, List<Run> runs, float tolerance)
        {
            // Corners: each run's ends, and points kept by simplifying the run's polyline.
            var corners = new List<int>(); // loop indices, in loop order
            var cornerRun = new List<int>();
            foreach (int id in o.runIds)
            {
                Run r = runs[id];
                List<int> keep = Simplify(r.loopIndices, o.drawn, tolerance);
                for (int k = 0; k < keep.Count - 1; k++) { corners.Add(keep[k]); cornerRun.Add(id); }
            }
            // A piece needs three corners at least (an island whose whole outline is one run).
            if (corners.Count < 3 && o.loop.Count >= 3)
            {
                int run = cornerRun.Count > 0 ? cornerRun[0] : o.runIds[0];
                int start = corners.Count > 0 ? corners[0] : 0, m = o.loop.Count;
                corners.Clear(); cornerRun.Clear();
                for (int k = 0; k < 3; k++) { corners.Add((start + k * m / 3) % m); cornerRun.Add(run); }
            }
            PatternPiece piece = o.piece;
            for (int c = 0; c < corners.Count; c++)
            {
                piece.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = o.drawn[corners[c]] });
                Run r = runs[cornerRun[c]];
                piece.edges.Add(new PatternEdge { id = ClothesPattern.NewId(), name = r.partnerIsland < 0 ? "Opening" : "Seam", opening = r.partnerIsland < 0 });
            }
            // Each run's fractions (by length as drawn) and which edges cover them.
            foreach (int id in o.runIds)
            {
                Run r = runs[id];
                r.fraction = new float[r.loopIndices.Count];
                float total = 0f;
                for (int k = 1; k < r.loopIndices.Count; k++) { total += (o.drawn[r.loopIndices[k]] - o.drawn[r.loopIndices[k - 1]]).magnitude; r.fraction[k] = total; }
                for (int k = 0; k < r.fraction.Length; k++) r.fraction[k] = total > 0f ? r.fraction[k] / total : 0f;
                r.vertices.Clear();
                foreach (int li in r.loopIndices) r.vertices.Add(o.loop[li]);
            }
            for (int c = 0; c < corners.Count; c++)
            {
                Run r = runs[cornerRun[c]];
                int from = corners[c], to = corners[(c + 1) % corners.Count];
                float t0 = r.fraction[r.loopIndices.IndexOf(from)];
                int toIndex = r.loopIndices.LastIndexOf(to);
                float t1 = toIndex >= 0 ? r.fraction[toIndex] : 1f;
                r.edges.Add((c, t0, t1));
            }
        }

        /// <summary>Douglas-Peucker on a run's loop positions: the kept loop indices, both ends included.</summary>
        private static List<int> Simplify(List<int> run, List<Vector2> drawn, float tolerance)
        {
            var keep = new bool[run.Count];
            keep[0] = keep[run.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, run.Count - 1));
            while (stack.Count > 0)
            {
                (int i0, int i1) = stack.Pop();
                if (i1 - i0 < 2) continue;
                Vector2 a = drawn[run[i0]], b = drawn[run[i1]];
                float worst = -1f; int at = -1;
                for (int i = i0 + 1; i < i1; i++)
                {
                    float d = DistanceToSegment(drawn[run[i]], a, b);
                    if (d > worst) { worst = d; at = i; }
                }
                if (worst > tolerance) { keep[at] = true; stack.Push((i0, at)); stack.Push((at, i1)); }
            }
            // A closed run (a whole loop) needs points in between to make a piece.
            if (run.Count > 3 && run[0] == run[run.Count - 1] || drawn[run[0]] == drawn[run[run.Count - 1]])
            {
                keep[run.Count / 3] = true; keep[2 * run.Count / 3] = true;
            }
            var result = new List<int>();
            for (int i = 0; i < run.Count; i++) if (keep[i]) result.Add(run[i]);
            return result;
        }

        private static int Sew(ClothesPattern pattern, Run a, Run b, List<IslandOutline> islands)
        {
            // b walks the shared points the other way (usually), so a's fraction s is b's 1 - s.
            int count = 0;
            bool reversed = a.partnerReversed;
            float Map(float s) => reversed ? 1f - s : s;
            // Breakpoints along a (0-1) from both runs' edge ends.
            var breaks = new List<float> { 0f, 1f };
            foreach (var e in a.edges) { breaks.Add(e.t0); breaks.Add(e.t1); }
            foreach (var e in b.edges) { breaks.Add(Map(e.t0)); breaks.Add(Map(e.t1)); }
            breaks.Sort();
            for (int k = 0; k + 1 < breaks.Count; k++)
            {
                float s0 = breaks[k], s1 = breaks[k + 1];
                if (s1 - s0 < 1e-3f) continue;
                float mid = (s0 + s1) * 0.5f;
                if (!EdgeAt(a, mid, out int ea, out float ea0, out float ea1)) continue;
                if (!EdgeAt(b, Map(mid), out int eb, out float eb0, out float eb1)) continue;
                float b0 = Mathf.Min(Map(s0), Map(s1)), b1 = Mathf.Max(Map(s0), Map(s1));
                var seam = new PatternSeam
                {
                    id = ClothesPattern.NewId(),
                    a = new SeamSpan(pattern.pieces[a.island].id, pattern.pieces[a.island].edges[ea].id, Frac(s0, ea0, ea1), Frac(s1, ea0, ea1)),
                    b = new SeamSpan(pattern.pieces[b.island].id, pattern.pieces[b.island].edges[eb].id, Frac(b0, eb0, eb1), Frac(b1, eb0, eb1)),
                    reversed = reversed
                };
                if (seam.a.end - seam.a.start < 1e-3f || seam.b.end - seam.b.start < 1e-3f) continue;
                pattern.seams.Add(seam);
                count++;
            }
            return count;
        }

        private static float Frac(float s, float t0, float t1) => t1 > t0 ? Mathf.Clamp01((s - t0) / (t1 - t0)) : 0f;

        private static bool EdgeAt(Run r, float s, out int edge, out float t0, out float t1)
        {
            foreach (var e in r.edges)
            {
                if (s >= e.t0 - 1e-5f && s <= e.t1 + 1e-5f) { edge = e.edge; t0 = e.t0; t1 = e.t1; return true; }
            }
            edge = -1; t0 = t1 = 0f;
            return false;
        }

        // ---- Helpers -----------------------------------------------------------------------------------

        private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static Vector2 Rotate(Vector2 p, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - p).magnitude;
        }

        /// <summary>Vertices at the same position (to 0.01 mm) share a group: the copies along UV and slot seams.</summary>
        public static int[] Weld(Vector3[] positions)
        {
            var group = new int[positions.Length];
            var seen = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i] * 1e5f;
                var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (!seen.TryGetValue(key, out int g)) { g = i; seen[key] = i; }
                group[i] = g;
            }
            return group;
        }

        /// <summary>Taubin smoothing of the welded surface (shrinks folds, keeps the overall shape), boundary points held.</summary>
        private static Vector3[] Smooth(Vector3[] positions, int[] triangles, int[] group, int passes)
        {
            int n = positions.Length;
            var neighbours = new Dictionary<int, HashSet<int>>();
            var edgeUse = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = group[triangles[t + k]], b = group[triangles[t + (k + 1) % 3]];
                    if (a == b) continue;
                    if (!neighbours.TryGetValue(a, out HashSet<int> na)) neighbours[a] = na = new HashSet<int>();
                    if (!neighbours.TryGetValue(b, out HashSet<int> nb)) neighbours[b] = nb = new HashSet<int>();
                    na.Add(b); nb.Add(a);
                    long key = Key(a, b);
                    edgeUse[key] = edgeUse.TryGetValue(key, out int u) ? u + 1 : 1;
                }
            }
            var fixedPoint = new HashSet<int>();
            foreach (var kv in edgeUse) if (kv.Value == 1) { fixedPoint.Add((int)(kv.Key >> 32)); fixedPoint.Add((int)(kv.Key & 0xffffffff)); }
            var p = new Dictionary<int, Vector3>();
            foreach (int g in neighbours.Keys) p[g] = positions[g];
            var keys = new List<int>(neighbours.Keys);
            for (int pass = 0; pass < passes * 2; pass++)
            {
                float factor = pass % 2 == 0 ? 0.5f : -0.53f;
                var moved = new Dictionary<int, Vector3>(p.Count);
                foreach (int g in keys)
                {
                    if (fixedPoint.Contains(g)) { moved[g] = p[g]; continue; }
                    Vector3 avg = Vector3.zero;
                    foreach (int h in neighbours[g]) avg += p[h];
                    avg /= neighbours[g].Count;
                    moved[g] = p[g] + (avg - p[g]) * factor;
                }
                p = moved;
            }
            var result = new Vector3[n];
            for (int i = 0; i < n; i++) result[i] = p.TryGetValue(group[i], out Vector3 q) ? q : positions[i];
            return result;
        }
    }
}
