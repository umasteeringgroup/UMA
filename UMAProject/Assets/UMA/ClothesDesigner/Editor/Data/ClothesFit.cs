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
        /// <summary>The least distance kept between pieces of the cloth once sewn (mm), so a collar lies on the shirt instead of through it. 0 lets pieces pass through each other.</summary>
        [Range(0f, 10f)] public float clothThicknessMm = 2f;
        /// <summary>Keep cloth of one piece apart from itself as well (deep folds, gathers), not only from other pieces.</summary>
        public bool selfCollideWithinPieces = true;
        /// <summary>Pieces (by id) held where they are while the rest is simulated.</summary>
        public List<string> frozenPieces = new List<string>();
        /// <summary>Clothes the body wears under these: the cloth drapes over them.</summary>
        public List<UMAWardrobeRecipe> wornUnder = new List<UMAWardrobeRecipe>();
        /// <summary>With clothes worn underneath: the sizes follow the chest, waist and hips measured round them.</summary>
        public bool sizeOverWorn = true;
        public bool overrideMeshLevel;
        public MeshLevel meshLevel = MeshLevel.Medium;
        [Min(0.3f)] public float customEdgeLengthCm = 2f;
        /// <summary>Weight donors leave out the arms (right for a vest; turn off for sleeves).</summary>
        public bool donorsExcludeArms = true;
        /// <summary>An animation to play on the try-on character, to see the clothes move.</summary>
        public AnimationClip tryOnClip;
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
        /// <summary>
        /// How far each arm's surface reaches from its shoulder-to-wrist axis, every
        /// <see cref="ArmProfileStepM"/> along it from <see cref="ArmProfileStartM"/> (m); the hand is left out.
        /// Empty for a body measured before the profile was kept: <see cref="armRadius"/> is used then.
        /// </summary>
        public float[] leftArmProfile = new float[0], rightArmProfile = new float[0];
        public const float ArmProfileStepM = 0.02f;
        public const float ArmProfileStartM = -0.06f;
        public float armLengthCm, waistToAnkleCm, inseamCm, neckCm, headCm, thighCm, upperArmCm;

        /// <summary>The point at a height on the line from a hip joint through its ankle (carried on above the hip and below the ankle).</summary>
        public Vector3 LegAxisAt(bool left, float y)
        {
            Vector3 hip = left ? leftHip : rightHip, ankle = left ? leftAnkle : rightAnkle;
            float drop = hip.y - ankle.y;
            return Vector3.LerpUnclamped(hip, ankle, drop > 1e-4f ? (hip.y - y) / drop : 0f);
        }

        /// <summary>
        /// How far the arm's surface reaches from its axis at a distance along it from the shoulder joint (m),
        /// the profile's value between its samples (the last one past the wrist, the first one above the shoulder).
        /// </summary>
        public float ArmSurfaceRadius(bool left, float along)
        {
            float[] profile = left ? leftArmProfile : rightArmProfile;
            if (profile == null || profile.Length == 0) return armRadius;
            float f = Mathf.Clamp((along - ArmProfileStartM) / ArmProfileStepM - 0.5f, 0f, profile.Length - 1);
            int i = Mathf.Min(profile.Length - 2, (int)f);
            return i < 0 ? profile[0] : Mathf.Lerp(profile[i], profile[i + 1], f - i);
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
        /// <summary>
        /// Coarser copies of the slot for distant characters, named Slot_LOD1, _LOD2 ..., each meshed at
        /// half again the edge length of the one before. UMA's Simple LOD swaps them in by name.
        /// </summary>
        [Range(0, 3)] public int lodLevels;
        /// <summary>Give the whole garment a thickness: a turned-in copy of all of it, joined at every opening.</summary>
        public bool fullThickness;
        /// <summary>With full thickness: how thick the cloth is (mm).</summary>
        [Min(0.2f)] public float thicknessMm = 2f;
        /// <summary>Add the pattern's buttons and parts (buckles, zip pulls) to the slot.</summary>
        public bool addParts = true;
        /// <summary>Draw the seams' topstitching into a texture overlay on the clothes.</summary>
        public bool addTopstitch = true;
        /// <summary>The topstitching's colour.</summary>
        public Color topstitchColor = new Color(0.92f, 0.85f, 0.6f, 1f);

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
