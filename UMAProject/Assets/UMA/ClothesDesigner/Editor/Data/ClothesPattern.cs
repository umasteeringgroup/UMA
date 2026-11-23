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
        /// <summary>2: each piece has its own fabric, and placement a distance from the body.</summary>
        public const int CurrentSchemaVersion = 2;

        public int schemaVersion = CurrentSchemaVersion;
        public List<PatternPiece> pieces = new List<PatternPiece>();
        public List<PatternSeam> seams = new List<PatternSeam>();
        public List<PatternDimension> dimensions = new List<PatternDimension>();
        public MeshLevel meshLevel = MeshLevel.Medium;
        [Min(0.3f)] public float customEdgeLengthCm = 2f;
        public UvLayoutSettings uvLayout = new UvLayoutSettings();
        [TextArea] public string notes;

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
                    + (piece.placement.acrossCm != 0f ? ";x" + piece.placement.acrossCm.ToString("R") : "") : "").Append('|');
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
                text.Append('\n');
            }
            foreach (PatternSeam s in seams)
            {
                if (s == null) continue;
                text.Append(s.a.pieceId).Append(s.a.edgeId).Append(s.a.start.ToString("R")).Append(s.a.end.ToString("R"))
                    .Append(s.b.pieceId).Append(s.b.edgeId).Append(s.b.start.ToString("R")).Append(s.b.end.ToString("R")).Append(s.reversed).Append('\n');
            }
            foreach (PatternDimension d in dimensions)
            {
                if (d == null) continue;
                text.Append(d.id).Append(d.kind).Append(d.measurement).Append(d.fixedCm.ToString("R")).Append(d.allowanceCm.ToString("R")).Append(d.shareCount).Append(d.lineY.ToString("R"));
                foreach (string id in d.pieceIds) text.Append(id);
                text.Append('\n');
            }
            text.Append(meshLevel).Append(customEdgeLengthCm.ToString("R"));
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
                piece.darts.RemoveAll(d => d == null);
                piece.pleats.RemoveAll(p => p == null);
                foreach (PatternDart d in piece.darts) if (string.IsNullOrEmpty(d.id)) d.id = NewId();
                foreach (PatternPleat p in piece.pleats) if (string.IsNullOrEmpty(p.id)) p.id = NewId();
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
            }
            foreach (PatternSeam seam in seams)
            {
                if (seam == null) continue;
                if (!SpanResolves(seam.a) || !SpanResolves(seam.b)) problems.Add("A seam refers to an edge that no longer exists. Unsew it and sew it again.");
            }
            return problems;
        }

        private bool SpanResolves(SeamSpan span)
        {
            PatternPiece piece = FindPiece(span.pieceId);
            return piece != null && piece.IndexOfEdge(span.edgeId) >= 0 && span.end > span.start;
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
        /// <summary>What this piece is made of. Pieces sewn together can be different fabrics.</summary>
        public FabricSettings fabric = new FabricSettings();
        /// <summary>Darts sewn on this piece: edge darts (a V from an edge) and double-pointed darts (a diamond inside).</summary>
        public List<PatternDart> darts = new List<PatternDart>();
        /// <summary>Pleats folded at an edge of this piece.</summary>
        public List<PatternPleat> pleats = new List<PatternPleat>();
        public UvIsland uvIsland = new UvIsland();

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
        /// <summary>How far the fold lines run into the piece (cm). 0 runs them across the whole piece.</summary>
        public float lengthCm;
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

        public PiecePlacement Clone() => (PiecePlacement)MemberwiseClone();

        public bool SameAs(PiecePlacement o) =>
            o != null && Mathf.Approximately(angle, o.angle) && Mathf.Approximately(topAboveShoulderCm, o.topAboveShoulderCm)
            && Mathf.Approximately(distanceCm, o.distanceCm) && part == o.part && flat == o.flat && Mathf.Approximately(acrossCm, o.acrossCm);

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
    }

    [Serializable]
    public sealed class UvLayoutSettings
    {
        [Min(32)] public int bakeResolution = 1024;
        [Min(0)] public int gutterPixels = 8;
        /// <summary>Width divided by height of the texture the slot uses.</summary>
        [Min(0.05f)] public float aspect = 1f;
    }
}
