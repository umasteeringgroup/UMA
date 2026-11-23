using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The example two-panel sleeveless vest, built in code: a front with a deep neckline and a back,
    /// sewn at the shoulders and sides, open at the neck, armholes and hem. The chest follows the body's
    /// chest plus 8 cm of ease; the length follows shoulder-to-waist plus 20 cm.
    /// </summary>
    public static class VestPattern
    {
        public const string FrontName = "Front";
        public const string BackName = "Back";

        public static ClothesPattern Create()
        {
            var pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            pattern.name = "Sleeveless Vest";
            pattern.notes = "Two-panel sleeveless vest: the Clothes Designer's first example.";
            PatternPiece front = Panel(FrontName, 44f, 0f, deepNeck: true);
            PatternPiece back = Panel(BackName, 59f, 180f, deepNeck: false);
            // Drawn beside the front on the canvas; where a piece is drawn does not change the cloth.
            foreach (PatternPoint p in back.points) p.position.x += 65f;
            pattern.pieces.Add(front);
            pattern.pieces.Add(back);
            // Edges: 0 hem, 1 right side, 2 right armhole, 3 right shoulder, 4 right neck, 5 left neck,
            // 6 left shoulder, 7 left armhole, 8 left side. A piece is drawn as seen from outside, so the
            // front's right side meets the back's left side.
            pattern.seams.Add(Seam(front, 1, back, 8));
            pattern.seams.Add(Seam(front, 8, back, 1));
            pattern.seams.Add(Seam(front, 3, back, 6));
            pattern.seams.Add(Seam(front, 6, back, 3));
            pattern.dimensions.Add(new PatternDimension
            {
                id = ClothesPattern.NewId(), name = "Chest", kind = DimensionKind.Width, measurement = BodyMeasurement.Chest,
                fixedCm = 104f, allowanceCm = 8f, shareCount = 2, lineY = 32f, pieceIds = new List<string> { front.id, back.id }
            });
            pattern.dimensions.Add(new PatternDimension
            {
                id = ClothesPattern.NewId(), name = "Length", kind = DimensionKind.Length, measurement = BodyMeasurement.ShoulderToWaist,
                fixedCm = 62f, allowanceCm = 20f, shareCount = 1, pieceIds = new List<string> { front.id, back.id }
            });
            pattern.meshLevel = MeshLevel.Medium;
            return pattern;
        }

        private static PatternPiece Panel(string name, float neckDepthY, float angle, bool deepNeck)
        {
            var piece = new PatternPiece { id = ClothesPattern.NewId(), name = name };
            Vector2[] points =
            {
                new Vector2(-26f, 0f), new Vector2(26f, 0f), new Vector2(26f, 33f), new Vector2(15f, 58f),
                new Vector2(8f, 62f), new Vector2(0f, neckDepthY), new Vector2(-8f, 62f), new Vector2(-15f, 58f), new Vector2(-26f, 33f)
            };
            string[] edgeNames = { "Hem", "Right side", "Right armhole", "Right shoulder", "Right neckline", "Left neckline", "Left shoulder", "Left armhole", "Left side" };
            for (int i = 0; i < points.Length; i++)
            {
                piece.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = points[i] });
                piece.edges.Add(new PatternEdge { id = ClothesPattern.NewId(), name = edgeNames[i] });
            }
            piece.edges[0].opening = true;
            SetCurve(piece.edges[2], new Vector2(-8f, 3f), new Vector2(0f, -10f), true);
            SetCurve(piece.edges[7], new Vector2(0f, -10f), new Vector2(8f, 3f), true);
            if (deepNeck)
            {
                SetCurve(piece.edges[4], new Vector2(-1f, -8f), new Vector2(5f, 2f), true);
                SetCurve(piece.edges[5], new Vector2(-5f, 2f), new Vector2(1f, -8f), true);
            }
            else
            {
                SetCurve(piece.edges[4], new Vector2(-2f, -2f), new Vector2(4f, 0f), true);
                SetCurve(piece.edges[5], new Vector2(-4f, 0f), new Vector2(2f, -2f), true);
            }
            piece.placement = new PiecePlacement { angle = angle, topAboveShoulderCm = 4f };
            return piece;
        }

        private static void SetCurve(PatternEdge edge, Vector2 a, Vector2 b, bool opening)
        {
            edge.curved = true; edge.handleA = a; edge.handleB = b; edge.opening = opening;
        }

        private static PatternSeam Seam(PatternPiece a, int edgeA, PatternPiece b, int edgeB) => new PatternSeam
        {
            id = ClothesPattern.NewId(),
            a = new SeamSpan(a.id, a.edges[edgeA].id),
            b = new SeamSpan(b.id, b.edges[edgeB].id),
            reversed = true
        };
    }
}
