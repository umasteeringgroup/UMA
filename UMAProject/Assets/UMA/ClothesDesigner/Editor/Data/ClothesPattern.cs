using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>How finely the cloth is meshed, as a target edge length in centimetres.</summary>
    public enum MeshLevel
    {
        Draft = 0,
        Medium = 1,
        Fine = 2,
        VeryFine = 3,
        Custom = 4
    }

    /// <summary>How the inside of a piece is meshed.</summary>
    public enum MeshTopology
    {
        /// <summary>Even, nearly equilateral triangles: the most even cloth.</summary>
        Even = 0,
        /// <summary>Squares along the grain and across it, each split in two: rows and columns that follow the warp and the weft.</summary>
        GrainGrid = 1
    }

    /// <summary>A body measurement a pattern dimension can follow.</summary>
    public enum BodyMeasurement
    {
        None = 0,
        Chest = 1,
        Waist = 2,
        Hip = 3,
        /// <summary>From the top of the shoulder down to the waist line.</summary>
        ShoulderToWaist = 4,
        /// <summary>From the shoulder joint to the wrist.</summary>
        ArmLength = 5,
        /// <summary>From the waist line down to the ankle.</summary>
        WaistToAnkle = 6,
        /// <summary>From the crotch down to the ankle.</summary>
        Inseam = 7,
        /// <summary>Round the base of the neck.</summary>
        Neck = 8,
        /// <summary>Round the head at its widest.</summary>
        Head = 9,
        /// <summary>Round one thigh, just below the crotch.</summary>
        Thigh = 10,
        /// <summary>Round one upper arm, halfway to the elbow.</summary>
        UpperArm = 11
    }

    /// <summary>What a pattern dimension sizes.</summary>
    public enum DimensionKind
    {
        /// <summary>A circumference shared by the listed pieces: each piece's width at <see cref="PatternDimension.lineY"/>.</summary>
        Width = 0,
        /// <summary>The height of the listed pieces.</summary>
        Length = 1
    }

    /// <summary>A reusable sewing pattern: flat pieces in centimetres, the seams that join them and the dimensions that size them.</summary>
    /// <remarks>
    /// The pattern does not know any body or mesh. Piece, point, edge and seam ids are generated once,
    /// when the element is made, and never derived from list positions, so seams, pins and fits keep
    /// pointing at the same thing after edits and after Save Pattern As.
    /// </remarks>
    [CreateAssetMenu(menuName = "UMA/Clothes Designer/Clothes Pattern", fileName = "New Clothes Pattern")]
    public sealed class ClothesPattern : ScriptableObject
    {
        /// <summary>
        /// 2: each piece has its own fabric, and placement a distance from the body.
        /// 3: lines inside pieces (seam lines, folds, holes), tacks and buttons, attached parts.
        /// </summary>
        public const int CurrentSchemaVersion = 3;

        public int schemaVersion = CurrentSchemaVersion;
        public List<PatternPiece> pieces = new List<PatternPiece>();
        public List<PatternSeam> seams = new List<PatternSeam>();
        /// <summary>Single stitches between two points anywhere on the pieces: tacks and buttons.</summary>
        public List<PatternTack> tacks = new List<PatternTack>();
        /// <summary>Small hard parts fixed to the cloth: buttons, buckles, zip pulls.</summary>
        public List<PatternPart> parts = new List<PatternPart>();
        /// <summary>
        /// Trims painted onto the clothes' texture along an edge or a line: zippers, stitching, piping, hems.
        /// The Overlay Painter draws them, as editable layers, after the clothes are baked.
        /// </summary>
        public List<PatternTrim> trims = new List<PatternTrim>();
        /// <summary>The fabrics the pieces can look like (<see cref="PatternPiece.lookId"/>); each becomes an overlay when baked.</summary>
        public List<PieceLook> looks = new List<PieceLook>();
        /// <summary>A picture shown behind the pieces on the Pattern Canvas, to trace.</summary>
        public Texture2D referenceImage;
        /// <summary>The picture's lower left corner on the canvas (cm) and its height there (cm).</summary>
        public Vector2 referenceOrigin = new Vector2(-50f, 0f);
        [Min(1f)] public float referenceHeightCm = 100f;
        [Range(0.05f, 1f)] public float referenceOpacity = 0.35f;
        public List<PatternDimension> dimensions = new List<PatternDimension>();
        public MeshLevel meshLevel = MeshLevel.Medium;
        [Min(0.3f)] public float customEdgeLengthCm = 2f;
        /// <summary>How the inside of each piece is meshed: even triangles, or a grid along the grain.</summary>
        public MeshTopology meshTopology = MeshTopology.Even;
        public UvLayoutSettings uvLayout = new UvLayoutSettings();
        [TextArea] public string notes;
        /// <summary>
        /// The garment generator's style this pattern was drafted from (<see cref="GarmentStyle"/> as JSON), so the
        /// generator can open it again; empty for a pattern drawn by hand.
        /// </summary>
        [HideInInspector] public string generatorStyle;

        public static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>The edge length a mesh level stands for, in centimetres.</summary>
        public static float EdgeLengthCm(MeshLevel level, float custom)
        {
            switch (level)
            {
                case MeshLevel.Draft: return 3f;
                case MeshLevel.Medium: return 2f;
                case MeshLevel.Fine: return 1.5f;
                case MeshLevel.VeryFine: return 1f;
                default: return Mathf.Max(0.3f, custom);
            }
        }

        public PatternPiece FindPiece(string id)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] != null && pieces[i].id == id) return pieces[i];
            }
            return null;
        }

        /// <summary>The look with this id, or null.</summary>
        public PieceLook FindLook(string id)
        {
            if (looks == null || string.IsNullOrEmpty(id)) return null;
            foreach (PieceLook look in looks) if (look != null && look.id == id) return look;
            return null;
        }

        public int IndexOfPiece(string id)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] != null && pieces[i].id == id) return i;
            }
            return -1;
        }

        /// <summary>The seam that sews this edge, or null.</summary>
        public PatternSeam FindSeamOn(string pieceId, string edgeId)
        {
            foreach (PatternSeam seam in seams)
            {
                if (seam == null) continue;
                if ((seam.a.pieceId == pieceId && seam.a.edgeId == edgeId) || (seam.b.pieceId == pieceId && seam.b.edgeId == edgeId)) return seam;
            }
            return null;
        }

        /// <summary>
        /// A fingerprint of everything that shapes the cloth (outlines, curves, openings, seams, sizes,
        /// placement, mesh level), for knowing when a saved drape is out of date. Names, notes and the UV
        /// layout do not change the cloth's shape and are left out.
        /// </summary>
        public string ComputeRevision()
        {
            var text = new System.Text.StringBuilder();
            foreach (PatternPiece piece in pieces)
            {
                if (piece == null) continue;
                text.Append(piece.id).Append('|').Append(piece.placement != null ? piece.placement.angle + ";" + piece.placement.topAboveShoulderCm
                    // Only when set, so drapes kept before pieces had a distance still match.
                    + (piece.placement.distanceCm != 0f ? ";" + piece.placement.distanceCm.ToString("R") : "")
                    + (piece.placement.part != BodyPart.Torso ? ";" + piece.placement.part : "")
                    + (piece.placement.flat ? ";flat" : "")
                    + (piece.placement.acrossCm != 0f ? ";x" + piece.placement.acrossCm.ToString("R") : "")
                    + (piece.placement.tiltDeg != 0f ? ";t" + piece.placement.tiltDeg.ToString("R") : "")
                    + (piece.placement.endDistanceCm != 0f ? ";e" + piece.placement.endDistanceCm.ToString("R") : "") : "").Append('|');
                // Only when set, so drapes kept before pieces had them still match.
                if (PatternPiece.DetailOf(piece) != 1f) text.Append("detail").Append(PatternPiece.DetailOf(piece).ToString("R")).Append('|');
                if (piece.interfacing != InterfacingLevel.None) text.Append("interfacing").Append(piece.interfacing).Append('|');
                for (int i = 0; i < piece.points.Count; i++)
                {
                    text.Append(piece.points[i].position.x.ToString("R")).Append(',').Append(piece.points[i].position.y.ToString("R")).Append(';');
                    PatternEdge e = i < piece.edges.Count ? piece.edges[i] : null;
                    if (e == null) continue;
                    text.Append(e.id).Append(e.opening ? "o" : "").Append(e.curved ? "c" + e.handleA.x.ToString("R") + "," + e.handleA.y.ToString("R") + "," + e.handleB.x.ToString("R") + "," + e.handleB.y.ToString("R") : "").Append(';');
                }
                // Darts and pleats shape the cloth; listed only when there are any, so older drapes still match.
                if (piece.darts != null) foreach (PatternDart d in piece.darts) if (d != null) text.Append("d").Append(JsonUtility.ToJson(d));
                if (piece.pleats != null) foreach (PatternPleat p in piece.pleats) if (p != null) text.Append("p").Append(JsonUtility.ToJson(p));
                // Lines change the mesh; likewise only when there are any. (Elastic, layers and pressure only
                // change how the cloth moves, like its fabric, so a kept drape stays valid.)
                if (piece.lines != null) foreach (PatternLine l in piece.lines) if (l != null) text.Append("l").Append(JsonUtility.ToJson(l));
                text.Append('\n');
            }
            if (tacks != null) foreach (PatternTack t in tacks) if (t != null) text.Append("t").Append(t.pieceA).Append(t.a.x.ToString("R")).Append(t.a.y.ToString("R")).Append(t.pieceB).Append(t.b.x.ToString("R")).Append(t.b.y.ToString("R")).Append('\n');
            foreach (PatternSeam s in seams)
            {
                if (s == null) continue;
                text.Append(s.a.pieceId).Append(s.a.edgeId).Append(s.a.start.ToString("R")).Append(s.a.end.ToString("R"))
                    .Append(s.b.pieceId).Append(s.b.edgeId).Append(s.b.start.ToString("R")).Append(s.b.end.ToString("R")).Append(s.reversed).Append(s.folded ? "folded" : "").Append('\n');
            }
            foreach (PatternDimension d in dimensions)
            {
                if (d == null) continue;
                text.Append(d.id).Append(d.kind).Append(d.measurement).Append(d.fixedCm.ToString("R")).Append(d.allowanceCm.ToString("R")).Append(d.shareCount).Append(d.lineY.ToString("R"));
                foreach (string id in d.pieceIds) text.Append(id);
                text.Append('\n');
            }
            text.Append(meshLevel).Append(customEdgeLengthCm.ToString("R"));
            // Only when set, so drapes kept before there was a choice still match.
            if (meshTopology != MeshTopology.Even) text.Append(meshTopology);
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < text.Length; i++) hash = (hash ^ text[i]) * 1099511628211UL;
                return hash.ToString("x16");
            }
        }

        /// <summary>Brings an older pattern up to <see cref="CurrentSchemaVersion"/>. Returns what was changed, for the artist.</summary>
        public List<string> Migrate()
        {
            var notesOut = new List<string>();
            if (pieces == null) pieces = new List<PatternPiece>();
            if (seams == null) seams = new List<PatternSeam>();
            if (dimensions == null) dimensions = new List<PatternDimension>();
            if (tacks == null) tacks = new List<PatternTack>();
            if (parts == null) parts = new List<PatternPart>();
            tacks.RemoveAll(t => t == null);
            parts.RemoveAll(t => t == null);
            foreach (PatternTack t in tacks) if (string.IsNullOrEmpty(t.id)) t.id = NewId();
            foreach (PatternPart t in parts) if (string.IsNullOrEmpty(t.id)) t.id = NewId();
            if (uvLayout == null) uvLayout = new UvLayoutSettings();
            foreach (PatternPiece piece in pieces)
            {
                if (piece == null) continue;
                if (string.IsNullOrEmpty(piece.id)) { piece.id = NewId(); notesOut.Add("Gave piece '" + piece.name + "' an id."); }
                if (piece.points == null) piece.points = new List<PatternPoint>();
                if (piece.edges == null) piece.edges = new List<PatternEdge>();
                foreach (PatternPoint p in piece.points) if (p != null && string.IsNullOrEmpty(p.id)) p.id = NewId();
                while (piece.edges.Count < piece.points.Count) piece.edges.Add(new PatternEdge { id = NewId() });
                if (piece.edges.Count > piece.points.Count) piece.edges.RemoveRange(piece.points.Count, piece.edges.Count - piece.points.Count);
                foreach (PatternEdge e in piece.edges) if (e != null && string.IsNullOrEmpty(e.id)) e.id = NewId();
                if (piece.placement == null) piece.placement = new PiecePlacement();
                if (piece.uvIsland == null) piece.uvIsland = new UvIsland();
                if (piece.fabric == null || string.IsNullOrEmpty(piece.fabric.preset)) piece.fabric = FabricPresets.Create(FabricPresets.DefaultName);
                if (piece.darts == null) piece.darts = new List<PatternDart>();
                if (piece.pleats == null) piece.pleats = new List<PatternPleat>();
                if (piece.meshDetail < 1f) piece.meshDetail = 1f;
                piece.darts.RemoveAll(d => d == null);
                piece.pleats.RemoveAll(p => p == null);
                foreach (PatternDart d in piece.darts) if (string.IsNullOrEmpty(d.id)) d.id = NewId();
                foreach (PatternPleat p in piece.pleats) if (string.IsNullOrEmpty(p.id)) p.id = NewId();
                if (piece.lines == null) piece.lines = new List<PatternLine>();
                piece.lines.RemoveAll(l => l == null);
                foreach (PatternLine l in piece.lines)
                {
                    if (string.IsNullOrEmpty(l.id)) l.id = NewId();
                    if (l.points == null) l.points = new List<Vector2>();
                }
            }
            foreach (PatternSeam seam in seams) if (seam != null && string.IsNullOrEmpty(seam.id)) seam.id = NewId();
            if (schemaVersion < CurrentSchemaVersion)
            {
                notesOut.Add("Updated from format " + schemaVersion + " to " + CurrentSchemaVersion + ".");
                schemaVersion = CurrentSchemaVersion;
            }
            return notesOut;
        }

        /// <summary>Problems that keep the pattern from being simulated or baked, in words for the artist. Empty when it is ready.</summary>
        public List<string> FindProblems()
        {
            var problems = new List<string>();
            if (pieces.Count == 0) problems.Add("The pattern has no pieces.");
            foreach (PatternPiece piece in pieces)
            {
                if (piece == null) continue;
                if (piece.points.Count < 3) { problems.Add("'" + piece.name + "' needs at least three points."); continue; }
                if (PatternGeometry.OutlineSelfIntersects(piece)) problems.Add("The outline of '" + piece.name + "' crosses itself.");
                if (Mathf.Abs(PatternGeometry.SignedArea(PatternGeometry.SampleOutline(piece, 1f, null))) < 1f) problems.Add("'" + piece.name + "' has no area.");
                problems.AddRange(DartGeometry.FindProblems(piece));
                problems.AddRange(LineGeometry.FindProblems(piece));
            }
            foreach (PatternSeam seam in seams)
            {
                if (seam == null) continue;
                if (!SpanResolves(seam.a) || !SpanResolves(seam.b)) problems.Add("A seam refers to an edge that no longer exists. Unsew it and sew it again.");
            }
            for (int i = 0; i < seams.Count; i++)
            {
                for (int j = i + 1; j < seams.Count; j++)
                {
                    if (seams[i] == null || seams[j] == null) continue;
                    if (Overlap(seams[i].a, seams[j].a) || Overlap(seams[i].a, seams[j].b) || Overlap(seams[i].b, seams[j].a) || Overlap(seams[i].b, seams[j].b))
                    {
                        problems.Add("Two seams use the same stretch of an edge. Unsew one of them.");
                    }
                }
            }
            if (tacks != null)
            {
                foreach (PatternTack tack in tacks)
                {
                    if (tack == null) continue;
                    PatternPiece a = FindPiece(tack.pieceA), b = FindPiece(tack.pieceB);
                    if (a == null || b == null) { problems.Add("A tack is on a piece that no longer exists. Delete it."); continue; }
                    if (!LineGeometry.Inside(a, tack.a) || !LineGeometry.Inside(b, tack.b)) problems.Add("A tack between '" + a.name + "' and '" + b.name + "' is outside its piece. Move it inside.");
                }
            }
            return problems;
        }

        private static bool Overlap(SeamSpan x, SeamSpan y) =>
            x.pieceId == y.pieceId && x.edgeId == y.edgeId && x.start < y.end - 1e-4f && y.start < x.end - 1e-4f;

        private bool SpanResolves(SeamSpan span)
        {
            PatternPiece piece = FindPiece(span.pieceId);
            return piece != null && (piece.IndexOfEdge(span.edgeId) >= 0 || piece.FindLine(span.edgeId) != null) && span.end > span.start;
        }

        /// <summary>Every seam with a side on this edge (or line inside a piece).</summary>
        public List<PatternSeam> FindSeamsOn(string pieceId, string edgeId)
        {
            var found = new List<PatternSeam>();
            foreach (PatternSeam seam in seams)
            {
                if (seam == null) continue;
                if ((seam.a.pieceId == pieceId && seam.a.edgeId == edgeId) || (seam.b.pieceId == pieceId && seam.b.edgeId == edgeId)) found.Add(seam);
            }
            return found;
        }

        /// <summary>
        /// The longest stretch of an edge (or line) that no seam uses yet, as fractions of its length.
        /// False when it is sewn all along.
        /// </summary>
        public bool FreeSpan(string pieceId, string edgeId, out float start, out float end)
        {
            var used = new List<Vector2>();
            foreach (PatternSeam seam in FindSeamsOn(pieceId, edgeId))
            {
                if (seam.a.pieceId == pieceId && seam.a.edgeId == edgeId) used.Add(new Vector2(seam.a.start, seam.a.end));
                if (seam.b.pieceId == pieceId && seam.b.edgeId == edgeId) used.Add(new Vector2(seam.b.start, seam.b.end));
            }
            used.Sort((x, y) => x.x.CompareTo(y.x));
            start = 0f; end = 0f;
            float at = 0f;
            used.Add(new Vector2(1f, 1f));
            foreach (Vector2 u in used)
            {
                if (u.x - at > end - start) { start = at; end = u.x; }
                at = Mathf.Max(at, u.y);
            }
            return end - start > 0.01f;
        }
    }

    /// <summary>A flat piece of cloth: a closed outline, drawn as seen from outside the garment.</summary>
    [Serializable]
    public sealed class PatternPiece
    {
        public string id;
        public string name;
        /// <summary>The outline's corners, counter-clockwise. Edge i runs from point i to point i + 1.</summary>
        public List<PatternPoint> points = new List<PatternPoint>();
        /// <summary>One edge per point, in the same order.</summary>
        public List<PatternEdge> edges = new List<PatternEdge>();
        /// <summary>The lengthwise direction of the cloth, in degrees from the piece's up direction.</summary>
        public float grainAngle;
        /// <summary>The piece this one was mirrored from, or empty.</summary>
        public string mirrorOf;
        public PiecePlacement placement = new PiecePlacement();
        /// <summary>
        /// For a piece made from a region painted on the body (Body Region): where each part of it lay on that body, so
        /// on the same race it is laid exactly where it was painted. Empty for other pieces; ignored on other races,
        /// which use <see cref="placement"/>.
        /// </summary>
        [HideInInspector] public BodyLayout bodyLayout = new BodyLayout();
        /// <summary>What this piece is made of. Pieces sewn together can be different fabrics.</summary>
        public FabricSettings fabric = new FabricSettings();
        /// <summary>Darts sewn on this piece: edge darts (a V from an edge) and double-pointed darts (a diamond inside).</summary>
        public List<PatternDart> darts = new List<PatternDart>();
        /// <summary>Pleats folded at an edge of this piece.</summary>
        public List<PatternPleat> pleats = new List<PatternPleat>();
        public UvIsland uvIsland = new UvIsland();
        /// <summary>
        /// How much finer than the pattern's Mesh Level this piece is meshed (1 to 4): small pieces such as
        /// collars and cuffs need more triangles to hold a curve or a point than a body panel does.
        /// </summary>
        [Range(1f, 4f)] public float meshDetail = 1f;
        /// <summary>
        /// How many texels per centimetre the piece gets in the texture, against the pattern's (0.25 to 2):
        /// less for pieces seldom seen (a collar stand, a waistband's inside), more for a printed front.
        /// </summary>
        [Range(0.25f, 2f)] public float textureDetail = 1f;
        /// <summary>The look (<see cref="ClothesPattern.looks"/>) this piece is made of; empty shows the clothes' own overlay.</summary>
        public string lookId;
        /// <summary>A stiffening layer fused to the piece: stands, collars, cuffs, waistbands and plackets.</summary>
        public InterfacingLevel interfacing;
        /// <summary>Lines drawn inside the piece: lines to sew other pieces onto (pockets, patches), folds and holes.</summary>
        public List<PatternLine> lines = new List<PatternLine>();
        /// <summary>
        /// Which layer of the clothes the piece is: where two pieces cross, the one with the higher layer is
        /// kept outside the other (a jacket front over a shirt front, a pocket over its panel).
        /// </summary>
        public int layer;
        /// <summary>A linked mirror keeps the shape of the piece it was mirrored from (<see cref="mirrorOf"/>): edit the original.</summary>
        public bool mirrorLinked;
        /// <summary>
        /// Duplicate with Seams (or Sewn Round): the piece this one was copied from. A seam of the original's to another
        /// piece is made between their copies, whichever is duplicated last.
        /// </summary>
        public string duplicateOf;
        /// <summary>
        /// Pushes the piece out from the body like padding or air in a puffer jacket (0 none, 1 strong).
        /// </summary>
        [Range(0f, 1f)] public float pressure;
        /// <summary>
        /// How thick the piece is baked (mm): a belt, a waistband, a strap. 0 bakes it as thin as the rest. The thickness
        /// is built outward from the draped cloth, so the piece stands out from what it is sewn to (see
        /// <see cref="PieceThickness"/>). The simulation is not changed by it.
        /// </summary>
        [Range(0f, 30f)] public float thicknessMm;

        public PatternLine FindLine(string lineId)
        {
            if (lines == null) return null;
            foreach (PatternLine l in lines) if (l != null && l.id == lineId) return l;
            return null;
        }

        public int IndexOfLine(string lineId)
        {
            if (lines == null) return -1;
            for (int i = 0; i < lines.Count; i++) if (lines[i] != null && lines[i].id == lineId) return i;
            return -1;
        }

        /// <summary>The piece's mesh detail, kept in range (old patterns have none).</summary>
        public static float DetailOf(PatternPiece piece) => piece == null || piece.meshDetail < 1f ? 1f : Mathf.Min(piece.meshDetail, 4f);

        public PatternDart FindDart(string dartId)
        {
            foreach (PatternDart d in darts) if (d != null && d.id == dartId) return d;
            return null;
        }

        public int IndexOfEdge(string edgeId)
        {
            for (int i = 0; i < edges.Count; i++) if (edges[i] != null && edges[i].id == edgeId) return i;
            return -1;
        }

        public int IndexOfPoint(string pointId)
        {
            for (int i = 0; i < points.Count; i++) if (points[i] != null && points[i].id == pointId) return i;
            return -1;
        }

        public Rect Bounds()
        {
            if (points.Count == 0) return new Rect();
            Vector2 min = points[0].position, max = min;
            foreach (PatternPoint p in points) { min = Vector2.Min(min, p.position); max = Vector2.Max(max, p.position); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }

    [Serializable]
    public sealed class PatternPoint
    {
        public string id;
        /// <summary>Centimetres.</summary>
        public Vector2 position;
    }

    public enum PatternLineKind
    {
        /// <summary>A line to sew an edge of another piece onto: a pocket, a patch, a strap.</summary>
        Seam = 0,
        /// <summary>A crease the cloth keeps: a trouser crease, a lapel's roll line.</summary>
        Fold = 1,
        /// <summary>A closed shape cut out of the piece.</summary>
        Hole = 2
    }

    /// <summary>
    /// A line drawn inside a piece (cm, as drawn): straight from point to point, open or closed. The cloth's
    /// mesh keeps it as edges, so an edge of another piece can be sewn to it, the cloth can crease along
    /// it, or the shape it encloses can be cut out.
    /// </summary>
    [Serializable]
    public sealed class PatternLine
    {
        public string id;
        public string name;
        public PatternLineKind kind;
        public List<Vector2> points = new List<Vector2>();
        /// <summary>The last point joins the first: a shape.</summary>
        public bool closed;
        /// <summary>Folds: how far the cloth is folded from flat, in degrees (0 flat, 180 folded back on itself).</summary>
        [Range(0f, 170f)] public float foldAngle = 60f;
        /// <summary>Folds: the crease points in towards the body (a valley) instead of out (a ridge).</summary>
        public bool foldInward;
        /// <summary>Folds: a sharp crease. The baked slot is split along it, with a normal for each side; the two sides stay one.</summary>
        public bool sharp;
        /// <summary>For a linked mirror: the line of the original this one copies.</summary>
        public string mirrorOfLine;
    }

    /// <summary>
    /// A single stitch holding two points together, anywhere on the pieces: a tack, or a button and its
    /// buttonhole. Each point is on a piece, in its centimetres as drawn.
    /// </summary>
    [Serializable]
    public sealed class PatternTack
    {
        public string id;
        public string pieceA;
        public Vector2 a;
        public string pieceB;
        public Vector2 b;
        /// <summary>A button: the bake puts a button on the first piece here.</summary>
        public bool button;
        /// <summary>The button's diameter (mm).</summary>
        [Min(2f)] public float buttonDiameterMm = 12f;
    }

    /// <summary>
    /// A trim along part of an edge, or a line inside a piece: a zipper, a row of stitching, piping, a hem. It is
    /// texture, not geometry: the Overlay Painter draws it with one of its path generators, as an editable layer,
    /// onto the baked clothes (Publish and Open in Overlay Painter, in the Publish window). The pattern keeps where it goes, so it follows the piece
    /// when the piece is reshaped or the UVs are packed again.
    /// </summary>
    [Serializable]
    public sealed class PatternTrim
    {
        public string id;
        public string name = "Trim";
        public string pieceId;
        /// <summary>The edge, or the line inside the piece, the trim follows.</summary>
        public string pathId;
        /// <summary>The part of the edge or line it covers, as fractions of its length.</summary>
        [Range(0f, 1f)] public float start, end = 1f;
        /// <summary>The painter's construction (a path generator id), and its menu path to show when the painter is missing.</summary>
        public string constructionId;
        public string constructionName;
        /// <summary>How wide the trim is on the cloth (cm).</summary>
        [Min(0.05f)] public float widthCm = 1.2f;
        /// <summary>How far inside the piece its middle runs from an edge (cm); 0 runs it along the edge itself. Lines ignore it.</summary>
        public float insetCm = 0.8f;
    }

    /// <summary>
    /// What a piece looks like: a fabric's textures, tiled over the piece at their real size and along its
    /// grain, and its colour. Pieces with different looks share one slot and material: the bake makes an overlay
    /// per look, masked to its pieces' places in the texture, so each look can also be recoloured on its own
    /// (by its shared colour, when it has one).
    /// </summary>
    [Serializable]
    public sealed class PieceLook
    {
        public string id;
        public string name = "Fabric";
        /// <summary>The fabric's colour texture; none paints the look's colour alone.</summary>
        public Texture2D albedo;
        /// <summary>The fabric's tangent-space normal map (its weave), or none.</summary>
        public Texture2D normal;
        /// <summary>The fabric's map for the material's other channel (specular or metallic and smoothness), or none.</summary>
        public Texture2D mask;
        /// <summary>The look's colour, multiplied with the colour texture by UMA, and changeable at run time.</summary>
        public Color color = Color.white;
        /// <summary>A shared colour name (such as "Shirt2") the recipe gives the look, so characters can recolour it; empty for none.</summary>
        public string sharedColorName;
        /// <summary>How much cloth one repeat of the textures covers (cm).</summary>
        [Min(0.5f)] public float repeatCm = 20f;
        /// <summary>The textures' turn against the piece's grain (degrees): 45 for a fabric cut on the bias.</summary>
        public float rotation;
        /// <summary>How strong the normal map is (0 flat, 1 as drawn).</summary>
        [Range(0f, 2f)] public float normalStrength = 1f;
    }

    public enum PartShape
    {
        /// <summary>A flat round button.</summary>
        Button = 0,
        /// <summary>A flat rectangle: a buckle plate, a label, a patch.</summary>
        Plate = 1,
        /// <summary>A mesh of your own.</summary>
        Mesh = 2
    }

    /// <summary>
    /// A small hard part fixed to the cloth: a button, a buckle, a zip pull. The bake adds it to the slot,
    /// moving with the cloth under it. It takes its texture from the cloth's texture where it sits, so
    /// painting that spot paints the part.
    /// </summary>
    [Serializable]
    public sealed class PatternPart
    {
        public string id;
        public string name;
        public string pieceId;
        /// <summary>Where on the piece (cm, as drawn).</summary>
        public Vector2 position;
        public PartShape shape;
        /// <summary>Its size across (mm): a button's diameter, a plate's width, a mesh's largest side.</summary>
        [Min(1f)] public float sizeMm = 12f;
        /// <summary>A plate's height, as a share of its width.</summary>
        [Min(0.05f)] public float aspect = 0.6f;
        /// <summary>How far it stands out from the cloth (mm).</summary>
        [Min(0.1f)] public float heightMm = 2.5f;
        /// <summary>Turned on the cloth, in degrees from the piece's up.</summary>
        public float rotation;
        /// <summary>Shape Mesh: the mesh. Its z axis points out of the cloth and its y axis up the piece.</summary>
        public Mesh mesh;
    }

    /// <summary>An edge of a piece's outline: straight, or a cubic Bézier with two handles.</summary>
    [Serializable]
    public sealed class PatternEdge
    {
        public string id;
        public string name;
        public bool curved;
        /// <summary>The first handle, relative to the edge's start point (cm).</summary>
        public Vector2 handleA;
        /// <summary>The second handle, relative to the edge's end point (cm).</summary>
        public Vector2 handleB;
        /// <summary>An intentional opening (hem, neckline, armhole): never sewn, drawn as open.</summary>
        public bool opening;
        /// <summary>Elastic sewn along the edge: it draws the edge up to <see cref="elasticPercent"/> of its length and stretches back.</summary>
        public bool elastic;
        /// <summary>With elastic: the edge's length at rest, as a percentage of its drawn length.</summary>
        [Range(30f, 100f)] public float elasticPercent = 75f;
        /// <summary>For a linked mirror: the edge of the original this one copies.</summary>
        public string mirrorOfEdge;
    }

    /// <summary>Part of one edge, as fractions of its length.</summary>
    [Serializable]
    public struct SeamSpan
    {
        public string pieceId;
        public string edgeId;
        public float start;
        public float end;

        public SeamSpan(string pieceId, string edgeId, float start = 0f, float end = 1f)
        {
            this.pieceId = pieceId; this.edgeId = edgeId; this.start = start; this.end = end;
        }
    }

    /// <summary>Two edge spans sewn together. With <see cref="reversed"/> the start of one meets the end of the other.</summary>
    [Serializable]
    public sealed class PatternSeam
    {
        public string id;
        public SeamSpan a;
        public SeamSpan b;
        public bool reversed;
        /// <summary>
        /// A fold: piece b is turned over piece a along the seam, like a collar's leaf over its stand or a cuff
        /// turned back. Draw the turned piece as it looks turned over (seen from outside), so its edge runs
        /// the same way as the other one (not reversed), and lay it out already turned, a little farther out.
        /// The simulation holds the turn as it was laid out.
        /// </summary>
        public bool folded;
        /// <summary>
        /// A crisp seam (a welt, a panel line on leather): the baked slot keeps a normal for each side of it instead
        /// of one shared normal. Its two sides stay at one position and move as one.
        /// </summary>
        public bool sharp;
        /// <summary>Topstitching drawn along the seam when the clothes are baked.</summary>
        public TopstitchKind topstitch;
        /// <summary>How far from the seam the topstitching runs (mm).</summary>
        [Min(0.5f)] public float topstitchOffsetMm = 3f;
    }

    public enum TopstitchKind
    {
        None = 0,
        /// <summary>One row of stitches on each side of the seam.</summary>
        Single = 1,
        /// <summary>Two rows on each side, as on jeans.</summary>
        Double = 2
    }

    /// <summary>A size control: a fixed length, or a body measurement plus an allowance (ease).</summary>
    [Serializable]
    public sealed class PatternDimension
    {
        public string id;
        public string name;
        public DimensionKind kind;
        public BodyMeasurement measurement;
        /// <summary>The length used when no measurement is chosen or no body is known (cm).</summary>
        public float fixedCm;
        /// <summary>Added to the measurement (cm). For a circumference this is the garment's ease.</summary>
        public float allowanceCm;
        /// <summary>How many pieces share a circumference (for example 2: a front and a back).</summary>
        [Min(1)] public int shareCount = 1;
        /// <summary>For a width: the height on the pieces where it is measured (cm).</summary>
        public float lineY;
        public List<string> pieceIds = new List<string>();
    }

    public enum DartKind
    {
        /// <summary>A V cut into an edge, its two legs sewn together up to the point. Straight or with curved legs: bust, waist, French, shoulder, neck and elbow darts.</summary>
        Edge = 0,
        /// <summary>A diamond inside the piece, sewn shut from point to point (also called fish-eye or contour dart).</summary>
        Double = 1
    }

    /// <summary>
    /// A dart: fabric taken out of a piece and sewn shut, to shape flat cloth round the body. Drawn on the
    /// pattern as seen from outside, in the piece's own centimetres.
    /// </summary>
    [Serializable]
    public sealed class PatternDart
    {
        public string id;
        public DartKind kind;
        /// <summary>Edge darts: the edge it opens on.</summary>
        public string edgeId;
        /// <summary>Edge darts: the middle of its opening, as a fraction of the edge's length.</summary>
        public float position = 0.5f;
        /// <summary>The intake: the width of fabric taken out, across the opening (edge) or at the widest point (double).</summary>
        public float widthCm = 3f;
        /// <summary>Edge darts: the point. Double darts: the first point.</summary>
        public Vector2 tip;
        /// <summary>Double darts: the second point.</summary>
        public Vector2 tip2;
        /// <summary>Edge darts: how far the legs bow out from straight at their middle (cm). Positive bows them towards each other, taking out less near the point.</summary>
        public float curveCm;
        /// <summary>Double darts: where it is widest, as a fraction of the way from the first point to the second.</summary>
        [Range(0.1f, 0.9f)] public float widestAt = 0.5f;
        /// <summary>Double darts: 0 a straight-sided diamond, 1 rounded sides (a fish-eye).</summary>
        [Range(0f, 1f)] public float roundness = 0.5f;
    }

    public enum PleatKind
    {
        /// <summary>A knife pleat whose fold faces right (as drawn): the fabric to its left lies on top.</summary>
        KnifeRight = 0,
        /// <summary>A knife pleat whose fold faces left: the fabric to its right lies on top.</summary>
        KnifeLeft = 1,
        /// <summary>Two knife pleats folded away from each other: a flat panel on top with the folds behind.</summary>
        Box = 2,
        /// <summary>Two knife pleats folded towards each other, meeting on the outside (the back of a box pleat).</summary>
        Inverted = 3
    }

    /// <summary>
    /// A pleat: fabric folded and held at an edge, so the edge is shorter than the cloth below it. Its fold
    /// lines run into the piece from the edge.
    /// </summary>
    [Serializable]
    public sealed class PatternPleat
    {
        public string id;
        public PleatKind kind;
        public string edgeId;
        /// <summary>The middle of the pleat along its edge, as a fraction of the edge's length.</summary>
        public float position = 0.5f;
        /// <summary>How deep each fold goes (cm). A knife pleat takes up twice this from the edge, box and inverted pleats four times.</summary>
        public float depthCm = 2f;
        /// <summary>Box pleats: the width of the flat panel on top (cm).</summary>
        public float boxWidthCm = 4f;
        /// <summary>How far down from the edge the folds are sewn together (cm). 0 holds them only at the edge.</summary>
        public float stitchedCm;
        /// <summary>Pressed folds keep their crease all the way down; unpressed pleats open out below the stitching.</summary>
        public bool pressed = true;
        /// <summary>Pressed folds baked sharp: the slot is split along each fold, with a normal for each side; the sides stay one.</summary>
        public bool sharpFolds;
        /// <summary>How far the fold lines run into the piece (cm). 0 runs them across the whole piece.</summary>
        public float lengthCm;
    }

    /// <summary>
    /// A stiffening layer fused to a piece. It stiffens the piece against folding across its height (as
    /// drawn), so a collar stand stays standing and a cuff stays flat, while it can still curve round the
    /// neck or the wrist.
    /// </summary>
    public enum InterfacingLevel
    {
        None = 0,
        /// <summary>Holds its shape but bends: collar leaves, plackets.</summary>
        Light = 1,
        /// <summary>Stands up on its own: collar stands, cuffs, waistbands.</summary>
        Firm = 2
    }

    /// <summary>The part of the body a piece is laid round before sewing.</summary>
    public enum BodyPart
    {
        /// <summary>Round the torso (and the legs below it, for skirts and dresses).</summary>
        Torso = 0,
        /// <summary>Round the head: hoods and hats.</summary>
        Head = 1,
        /// <summary>Round the body's left arm, from the shoulder joint down: sleeves, cuffs.</summary>
        LeftArm = 2,
        RightArm = 3,
        /// <summary>Round the body's left leg, from the waist down: trouser legs, socks.</summary>
        LeftLeg = 4,
        RightLeg = 5
    }

    /// <summary>
    /// Where a piece made from a painted body region lay on the body: its flattened source triangles as drawn (cm, in
    /// the piece's canvas space when it was made) and on the body (metres, with the body's normals), the race it was
    /// painted on, and one outline point to follow the piece if it is moved on the canvas.
    /// </summary>
    [Serializable]
    public sealed class BodyLayout
    {
        public string race;
        public string anchorPointId;
        public Vector2 anchor;
        public Vector2[] flat = new Vector2[0];
        public Vector3[] positions = new Vector3[0];
        public Vector3[] normals = new Vector3[0];
        public int[] triangles = new int[0];

        public bool Valid => flat != null && flat.Length > 2 && positions != null && positions.Length == flat.Length && normals != null && normals.Length == flat.Length && triangles != null && triangles.Length >= 3;
    }

    /// <summary>Where a piece is laid around the body before sewing.</summary>
    [Serializable]
    public sealed class PiecePlacement
    {
        /// <summary>
        /// The direction the piece faces, in degrees round its body part: on the torso, head and legs 0
        /// front, 180 back, 90 the body's left; on the arms 0 the top of the arm and 90 its front.
        /// </summary>
        public float angle;
        /// <summary>
        /// How far the piece's top sits above the part's top, in cm: on the torso the top of the shoulders,
        /// on the head the top of the head, on the arms the shoulder joint (measured along the arm; negative
        /// starts it down the arm), on the legs the waist.
        /// </summary>
        public float topAboveShoulderCm = 4f;
        /// <summary>How much farther out from the body than the other pieces it starts (cm). Negative brings it closer.</summary>
        public float distanceCm;
        /// <summary>The body part the piece is laid round.</summary>
        public BodyPart part;
        /// <summary>Laid flat beside the part, facing out in its direction, instead of wrapped round it (socks, hood sides).</summary>
        public bool flat;
        /// <summary>
        /// Moves the piece sideways round its part (across it, when flat), in cm towards its right as drawn.
        /// A piece is otherwise centred on its direction by its outline's middle, which for an L-shaped piece
        /// such as half a sock is not the part it should sit on.
        /// </summary>
        public float acrossCm;
        /// <summary>
        /// Arm and leg pieces: turns the tube the piece is wrapped on about its top, in degrees. On an arm
        /// a positive tilt lowers the far end (towards the wrist, below the arm); on a leg it moves the far
        /// end out from the other leg. The tube moves only as far as the limb still fits inside it.
        /// </summary>
        public float tiltDeg;
        /// <summary>
        /// Arm and leg pieces: how much farther from the limb the piece's far end starts than its top, in cm;
        /// negative brings the far end (the cuff, the hem) closer. The distance changes evenly along the piece.
        /// </summary>
        public float endDistanceCm;

        public PiecePlacement Clone() => (PiecePlacement)MemberwiseClone();

        public bool SameAs(PiecePlacement o) =>
            o != null && Mathf.Approximately(angle, o.angle) && Mathf.Approximately(topAboveShoulderCm, o.topAboveShoulderCm)
            && Mathf.Approximately(distanceCm, o.distanceCm) && part == o.part && flat == o.flat && Mathf.Approximately(acrossCm, o.acrossCm)
            && Mathf.Approximately(tiltDeg, o.tiltDeg) && Mathf.Approximately(endDistanceCm, o.endDistanceCm);

        /// <summary>True for the parts whose pieces are wrapped on a tube along a limb (arms and legs).</summary>
        public static bool IsLimb(BodyPart part) => IsArm(part) || IsLeg(part);

        /// <summary>What <see cref="topAboveShoulderCm"/> is measured from, in words for the panel.</summary>
        public static string TopLabel(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Head: return "Above Head Top";
                case BodyPart.LeftArm:
                case BodyPart.RightArm: return "Above Shoulder Joint";
                case BodyPart.LeftLeg:
                case BodyPart.RightLeg: return "Above Waist";
                default: return "Above Shoulders";
            }
        }

        public static bool IsArm(BodyPart part) => part == BodyPart.LeftArm || part == BodyPart.RightArm;
        public static bool IsLeg(BodyPart part) => part == BodyPart.LeftLeg || part == BodyPart.RightLeg;
    }

    /// <summary>Where a piece lies in the slot's texture.</summary>
    [Serializable]
    public sealed class UvIsland
    {
        public bool placed;
        /// <summary>The UV position of the piece's flat origin.</summary>
        public Vector2 offset;
        /// <summary>UV units per centimetre.</summary>
        public float scale;
        /// <summary>Degrees; turns the grain to the texture's up direction.</summary>
        public float rotation;
        /// <summary>The UV rectangle reserved for the island, to see when a resize no longer fits.</summary>
        public Rect reserved;
        /// <summary>
        /// The id of the piece whose island this one uses, mirrored (a linked mirror with Mirrors Share Space),
        /// or empty. A point of this piece maps to (<see cref="mirrorSumX"/> - x, y - <see cref="mirrorDy"/>) on that one.
        /// </summary>
        public string sharesWith;
        public float mirrorSumX, mirrorDy;
    }

    [Serializable]
    public sealed class UvLayoutSettings
    {
        [Min(32)] public int bakeResolution = 1024;
        [Min(0)] public int gutterPixels = 8;
        /// <summary>Width divided by height of the texture the slot uses.</summary>
        [Min(0.05f)] public float aspect = 1f;
        /// <summary>Let the packing turn pieces a quarter or half turn (the grain stays along an axis) to fit them tighter.</summary>
        public bool turnPieces = true;
        /// <summary>
        /// A linked mirror (a left and right sleeve) uses its original's place in the texture, mirrored, so both
        /// cost the texels of one. Both sides then show the same texture: no different decal or wear on each.
        /// </summary>
        public bool shareMirrors;
        /// <summary>
        /// The pieces were laid out quickly by their bounding boxes (the garment generator does while the artist
        /// changes the style); the bake packs them tightly first.
        /// </summary>
        [HideInInspector] public bool quickPacked;
    }
}
