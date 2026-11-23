using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// A pattern fitted to one body: the race, its shape, where the pieces were laid, the pins, the
    /// drape the artist accepted and where it was published. Saved separately from the pattern, so one
    /// pattern can have a fit for every body. The fabric of each piece is on the pattern.
    /// </summary>
    public sealed class ClothesFit : ScriptableObject
    {
        /// <summary>2: the fabric moved to the pattern's pieces; the fit keeps the damping.</summary>
        public const int CurrentSchemaVersion = 2;

        public int schemaVersion = CurrentSchemaVersion;
        public ClothesPattern pattern;
        /// <summary>For Fit Existing Slot: the clothing slot being refitted instead of a pattern.</summary>
        public SlotDataAsset sourceSlot;
        public RaceData race;
        /// <summary>The body's DNA when the fit was made. Empty means the race's default shape.</summary>
        public List<DnaValue> dna = new List<DnaValue>();
        public BodyLandmarks landmarks = new BodyLandmarks();
        public List<PlacementOverride> placements = new List<PlacementOverride>();
        public List<AllowanceOverride> allowances = new List<AllowanceOverride>();
        /// <summary>How quickly the cloth's movement dies down, per second. Only changes how fast it settles, not where.</summary>
        [Min(0f)] public float damping = 1.5f;
        [Min(0f)] public float clearanceCm = 0.6f;
        public bool overrideMeshLevel;
        public MeshLevel meshLevel = MeshLevel.Medium;
        [Min(0.3f)] public float customEdgeLengthCm = 2f;
        /// <summary>Weight donors leave out the arms (right for a vest; turn off for sleeves).</summary>
        public bool donorsExcludeArms = true;
        public List<ClothPin> pins = new List<ClothPin>();
        public AcceptedDrape drape = new AcceptedDrape();
        public PublishSettings publish = new PublishSettings();
        public OutputRecord output = new OutputRecord();

        public float EdgeLengthCm()
        {
            if (overrideMeshLevel || pattern == null) return ClothesPattern.EdgeLengthCm(meshLevel, customEdgeLengthCm);
            return ClothesPattern.EdgeLengthCm(pattern.meshLevel, pattern.customEdgeLengthCm);
        }

        public PiecePlacement PlacementFor(PatternPiece piece)
        {
            foreach (PlacementOverride o in placements)
            {
                if (o != null && o.pieceId == piece.id) return o.placement;
            }
            return piece.placement;
        }

        public float? AllowanceFor(string dimensionId)
        {
            foreach (AllowanceOverride o in allowances)
            {
                if (o != null && o.dimensionId == dimensionId) return o.allowanceCm;
            }
            return null;
        }
    }

    [Serializable]
    public sealed class DnaValue
    {
        public string name;
        public float value;
    }

    /// <summary>The heights the pattern is placed and measured at, and what was measured there (all in metres, renderer space).</summary>
    [Serializable]
    public sealed class BodyLandmarks
    {
        public bool valid;
        public float shoulderTopY;
        public float chestY;
        public float waistY;
        public float hipY;
        public float chestCm;
        public float waistCm;
        public float hipCm;
        /// <summary>The body's height, from the lowest to the highest point.</summary>
        public float heightCm;
        public Vector2 torsoCenter;

        /// <summary>True when the skeleton gave the arms, legs and head (a humanoid race).</summary>
        public bool limbs;
        /// <summary>Joints in the renderer's space (m): shoulder (upper arm), wrist (hand), hip (upper leg), ankle (foot), head.</summary>
        public Vector3 leftShoulder, rightShoulder, leftWrist, rightWrist, leftHip, rightHip, leftAnkle, rightAnkle, head;
        /// <summary>The base of the neck, the top of the head and the crotch (where the legs part), as heights.</summary>
        public float neckY, headTopY, crotchY;
        /// <summary>The head's centre across (x, z) and its half-width and half-depth.</summary>
        public Vector2 headCenter, headHalf;
        /// <summary>How far the arm and leg surfaces reach from their axes (m; most of the limb, not the widest bulge).</summary>
        public float armRadius, legRadius;
        public float armLengthCm, waistToAnkleCm, inseamCm, neckCm, headCm, thighCm, upperArmCm;

        /// <summary>The point at a height on the line from a hip joint through its ankle (carried on above the hip and below the ankle).</summary>
        public Vector3 LegAxisAt(bool left, float y)
        {
            Vector3 hip = left ? leftHip : rightHip, ankle = left ? leftAnkle : rightAnkle;
            float drop = hip.y - ankle.y;
            return Vector3.LerpUnclamped(hip, ankle, drop > 1e-4f ? (hip.y - y) / drop : 0f);
        }

        /// <summary>
        /// An arm's frame: from the shoulder joint along the arm to the wrist, the direction of the top of
        /// the arm (up, square to the arm), and the side that faces the front for the left arm's mirror.
        /// </summary>
        public void ArmFrame(bool left, out Vector3 root, out Vector3 axis, out Vector3 top, out Vector3 side)
        {
            root = left ? leftShoulder : rightShoulder;
            axis = (left ? leftWrist : rightWrist) - root;
            axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : (left ? Vector3.left : Vector3.right);
            top = Vector3.up - axis * Vector3.Dot(Vector3.up, axis);
            top = top.sqrMagnitude > 1e-6f ? top.normalized : Vector3.forward;
            // Square to both; with the piece's x running this way it is seen from outside the arm.
            side = Vector3.Cross(axis, top);
        }
    }

    [Serializable]
    public sealed class PlacementOverride
    {
        public string pieceId;
        public PiecePlacement placement = new PiecePlacement();
    }

    [Serializable]
    public sealed class AllowanceOverride
    {
        public string dimensionId;
        public float allowanceCm;
    }

    public enum PinKind
    {
        /// <summary>Holds the cloth at a point in the body's space.</summary>
        Placement = 0,
        /// <summary>Follows a point on the body's surface.</summary>
        Body = 1
    }

    /// <summary>A pin, stored on the flat pattern so it survives a change of mesh level.</summary>
    [Serializable]
    public sealed class ClothPin
    {
        public string pieceId;
        /// <summary>Where on the flat piece (cm).</summary>
        public Vector2 flatPosition;
        public PinKind kind;
        [Range(0f, 1f)] public float weight = 1f;
        /// <summary>Placement pins: the point held (renderer space, metres).</summary>
        public Vector3 target;
        /// <summary>Body pins: the body triangle (index into the captured surface), its barycentric point and the offset along its normal.</summary>
        public int bodyTriangle = -1;
        public Vector3 barycentric;
        public float normalOffset;
        /// <summary>The slot that triangle belongs to, to notice when the body changed.</summary>
        public string bodySlot;
    }

    /// <summary>The drape the artist kept, so reopening the fit restores the folds.</summary>
    [Serializable]
    public sealed class AcceptedDrape
    {
        public Vector3[] positions = new Vector3[0];
        /// <summary>The pattern revision and edge length the positions belong to.</summary>
        public string sourceRevision;
        public int vertexCount;

        public bool Matches(string revision, int vertices) =>
            positions != null && positions.Length == vertices && vertexCount == vertices && sourceRevision == revision;
    }

    [Serializable]
    public sealed class PublishSettings
    {
        public string slotName = "NewClothes";
        public string folder = "Assets/UMAProjectData/ClothesDesigner/Output";
        public UMAMaterial material;
        public OverlayDataAsset existingOverlay;
        public string wardrobeSlot = "Chest";
        public bool createWardrobeRecipe = true;
        public bool generateMeshHide;
        public bool addToGlobalLibrary = true;
        /// <summary>Make a labelled checker texture showing the pieces, for checking the UVs.</summary>
        public bool makeUvCheckTexture = true;
        /// <summary>A turned-in copy of the cloth inside each opening, so one-sided materials show cloth when you look in.</summary>
        public bool addFacing = true;
        /// <summary>How far into the clothes the facing reaches from each opening, along the cloth (cm).</summary>
        [Min(0.1f)] public float facingDepthCm = 5f;
        /// <summary>A narrow band joining each opening's edge to the facing, so the edge shows a thickness.</summary>
        public bool addRim;
        /// <summary>The rim's width: how far inside the cloth the facing lies when there is a rim (mm).</summary>
        [Min(0.1f)] public float rimThicknessMm = 3f;
        /// <summary>Bake the slot at its own mesh level (usually coarser than the drape) instead of the drape's.</summary>
        public bool bakeAtOwnLevel;
        public MeshLevel bakeMeshLevel = MeshLevel.Medium;
        [Min(0.3f)] public float bakeCustomEdgeLengthCm = 2f;
        /// <summary>With a slot coarser than the drape: keep the drape's finer folds in a normal map on the overlay.</summary>
        public bool bakeNormalMap = true;
        /// <summary>The normal map's size in texels; 0 uses the pattern's texture size (UVs tab).</summary>
        public int normalMapSize;

        /// <summary>The edge length the slot is baked at (cm), given the drape's.</summary>
        public float BakeEdgeLengthCm(float drapeEdgeCm) => bakeAtOwnLevel ? ClothesPattern.EdgeLengthCm(bakeMeshLevel, bakeCustomEdgeLengthCm) : drapeEdgeCm;
    }

    [Serializable]
    public sealed class OutputRecord
    {
        public SlotDataAsset slot;
        public OverlayDataAsset overlay;
        public UMAWardrobeRecipe recipe;
        public string bakeRevision;
        public string bakedAt;
    }
}
