using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Builds a pattern in code: pieces from outline points (counter-clockwise, as seen from outside the
    /// clothes, centimetres), curved edges, openings, mirrored copies, seams by edge name and sizes. The
    /// sample patterns are made with it; it is also a short way to write a pattern of your own.
    /// </summary>
    public sealed class PatternBuilder
    {
        public readonly ClothesPattern Pattern;

        public PatternBuilder(string name, string notes)
        {
            Pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            Pattern.name = name;
            Pattern.notes = notes;
            Pattern.meshLevel = MeshLevel.Medium;
        }

        /// <summary>Starts a piece. Add its outline with <see cref="PieceBuilder.Point"/>, in order round the outline.</summary>
        public PieceBuilder Piece(string name)
        {
            var piece = new PatternPiece { id = ClothesPattern.NewId(), name = name, fabric = FabricPresets.Create(FabricPresets.DefaultName) };
            Pattern.pieces.Add(piece);
            return new PieceBuilder(piece);
        }

        /// <summary>A mirror image of a piece (left to right), with the same edge names, placed as given.</summary>
        public PieceBuilder Mirror(PieceBuilder source, string name)
        {
            PatternPiece copy = PatternEditing.MirrorCopy(source.Piece, name);
            Pattern.pieces.Add(copy);
            return new PieceBuilder(copy);
        }

        /// <summary>Sews two whole edges, start of one to the end of the other (as two pieces seen from outside meet).</summary>
        public PatternSeam Sew(PieceBuilder a, string edgeA, PieceBuilder b, string edgeB) => Sew(a, edgeA, 0f, 1f, b, edgeB, 0f, 1f);

        /// <summary>Sews part of one edge to part of another (fractions of each edge's length).</summary>
        public PatternSeam Sew(PieceBuilder a, string edgeA, float a0, float a1, PieceBuilder b, string edgeB, float b0, float b1)
        {
            var seam = new PatternSeam
            {
                id = ClothesPattern.NewId(),
                a = new SeamSpan(a.Piece.id, a.Edge(edgeA).id, a0, a1),
                b = new SeamSpan(b.Piece.id, b.Edge(edgeB).id, b0, b1),
                reversed = true
            };
            Pattern.seams.Add(seam);
            return seam;
        }

        /// <summary>
        /// Sews one edge along several others in turn, each taking the share of it its length asks for:
        /// a collar or a band along a neckline made of several edges.
        /// </summary>
        public void SewAlong(PieceBuilder a, string edgeA, params (PieceBuilder piece, string edge)[] others)
        {
            float total = 0f;
            var lengths = new float[others.Length];
            for (int i = 0; i < others.Length; i++) { lengths[i] = others[i].piece.Length(others[i].edge); total += lengths[i]; }
            float at = 0f;
            for (int i = 0; i < others.Length; i++)
            {
                float next = i == others.Length - 1 ? 1f : at + lengths[i] / total;
                Sew(a, edgeA, at, next, others[i].piece, others[i].edge, 0f, 1f);
                at = next;
            }
        }

        /// <summary>A circumference shared by pieces, measured across them at a height on the pattern.</summary>
        public PatternDimension Width(string name, BodyMeasurement measurement, float fixedCm, float allowanceCm, int share, float lineY, params PieceBuilder[] pieces)
        {
            var d = new PatternDimension
            {
                id = ClothesPattern.NewId(), name = name, kind = DimensionKind.Width, measurement = measurement,
                fixedCm = fixedCm, allowanceCm = allowanceCm, shareCount = share, lineY = lineY
            };
            foreach (PieceBuilder p in pieces) d.pieceIds.Add(p.Piece.id);
            Pattern.dimensions.Add(d);
            return d;
        }

        /// <summary>The height of pieces, from a body measurement plus an allowance.</summary>
        public PatternDimension Length(string name, BodyMeasurement measurement, float fixedCm, float allowanceCm, params PieceBuilder[] pieces)
        {
            var d = new PatternDimension
            {
                id = ClothesPattern.NewId(), name = name, kind = DimensionKind.Length, measurement = measurement,
                fixedCm = fixedCm, allowanceCm = allowanceCm, shareCount = 1
            };
            foreach (PieceBuilder p in pieces) d.pieceIds.Add(p.Piece.id);
            Pattern.dimensions.Add(d);
            return d;
        }

        /// <summary>
        /// Lays the pieces out side by side on the canvas and packs the UVs. Pieces move only sideways, so
        /// width lines (heights on the pieces) stay where they were written. Where a piece is drawn does not
        /// change the cloth.
        /// </summary>
        public ClothesPattern Finish()
        {
            float x = 0f;
            foreach (PatternPiece piece in Pattern.pieces)
            {
                Rect b = piece.Bounds();
                Vector2 shift = new Vector2(x - b.xMin, 0f);
                foreach (PatternPoint p in piece.points) p.position += shift;
                foreach (PatternDart d in piece.darts) { d.tip += shift; d.tip2 += shift; }
                x += b.width + 8f;
            }
            Pattern.Migrate();
            UvLayout.Repack(Pattern);
            return Pattern;
        }
    }

    /// <summary>One piece being built.</summary>
    public sealed class PieceBuilder
    {
        public readonly PatternPiece Piece;

        public PieceBuilder(PatternPiece piece) { Piece = piece; }

        /// <summary>Adds the next outline point, and the edge that starts there (running to the next point).</summary>
        public PieceBuilder Point(float x, float y, string edgeName, bool opening = false)
        {
            Piece.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = new Vector2(x, y) });
            Piece.edges.Add(new PatternEdge { id = ClothesPattern.NewId(), name = edgeName, opening = opening });
            return this;
        }

        /// <summary>Curves an edge: handles relative to its start and its end point.</summary>
        public PieceBuilder Curve(string edgeName, float ax, float ay, float bx, float by)
        {
            PatternEdge e = Edge(edgeName);
            e.curved = true;
            e.handleA = new Vector2(ax, ay);
            e.handleB = new Vector2(bx, by);
            return this;
        }

        /// <summary>Curves an edge as a circular arc about a centre (handles for a cubic arc).</summary>
        public PieceBuilder Arc(string edgeName, Vector2 center)
        {
            int i = Index(edgeName);
            Vector2 p0 = Piece.points[i].position, p3 = Piece.points[(i + 1) % Piece.points.Count].position;
            float r = (p0 - center).magnitude;
            float a0 = Mathf.Atan2(p0.y - center.y, p0.x - center.x), a1 = Mathf.Atan2(p3.y - center.y, p3.x - center.x);
            float sweep = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float k = 4f / 3f * Mathf.Tan(sweep / 4f) * r;
            Vector2 t0 = new Vector2(-Mathf.Sin(a0), Mathf.Cos(a0)), t1 = new Vector2(-Mathf.Sin(a1), Mathf.Cos(a1));
            PatternEdge e = Edge(edgeName);
            e.curved = true;
            e.handleA = t0 * k;
            e.handleB = -t1 * k;
            return this;
        }

        public PieceBuilder Opening(string edgeName, bool opening = true) { Edge(edgeName).opening = opening; return this; }

        public PieceBuilder Place(BodyPart part, float angle, float top, float distance = 0f, bool flat = false, float across = 0f)
        {
            Piece.placement = new PiecePlacement { part = part, angle = angle, topAboveShoulderCm = top, distanceCm = distance, flat = flat, acrossCm = across };
            return this;
        }

        public PieceBuilder Fabric(string preset) { Piece.fabric = FabricPresets.Create(preset); return this; }

        public PieceBuilder Pleat(string edgeName, PleatKind kind, float position, float depthCm, float stitchedCm, bool pressed = true)
        {
            Piece.pleats.Add(new PatternPleat
            {
                id = ClothesPattern.NewId(), kind = kind, edgeId = Edge(edgeName).id, position = position,
                depthCm = depthCm, stitchedCm = stitchedCm, pressed = pressed
            });
            return this;
        }

        public PieceBuilder Dart(string edgeName, float position, float widthCm, float tipX, float tipY)
        {
            Piece.darts.Add(new PatternDart
            {
                id = ClothesPattern.NewId(), kind = DartKind.Edge, edgeId = Edge(edgeName).id, position = position,
                widthCm = widthCm, tip = new Vector2(tipX, tipY)
            });
            return this;
        }

        public PieceBuilder DoubleDart(float x, float yTop, float yBottom, float widthCm)
        {
            Piece.darts.Add(new PatternDart
            {
                id = ClothesPattern.NewId(), kind = DartKind.Double, widthCm = widthCm,
                tip = new Vector2(x, yTop), tip2 = new Vector2(x, yBottom), widestAt = 0.5f, roundness = 0.5f
            });
            return this;
        }

        public int Index(string edgeName)
        {
            for (int i = 0; i < Piece.edges.Count; i++) if (Piece.edges[i].name == edgeName) return i;
            throw new ArgumentException("'" + Piece.name + "' has no edge '" + edgeName + "'.");
        }

        public PatternEdge Edge(string edgeName) => Piece.edges[Index(edgeName)];

        public float Length(string edgeName) => PatternGeometry.EdgeLength(Piece, Index(edgeName), PieceScale.Identity);
    }
}
