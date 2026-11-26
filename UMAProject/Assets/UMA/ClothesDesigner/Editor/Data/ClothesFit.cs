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
        /// <summary>Pieces not laid round an arm take their weights from the body without the arms (sleeves always use the arms).</summary>
        public bool donorsExcludeArms = true;
        /// <summary>How the clothes get their bone weights from the body (<see cref="UMA.Editors.Weighting.UMAWeightTransfer"/>).</summary>
        public UMA.Editors.Weighting.UMAWeightMethod weightMethod = UMA.Editors.Weighting.UMAWeightMethod.RobustInpaint;
        /// <summary>Cloth nearer the body than this (and facing the same way) copies the body's weights (cm).</summary>
        [Min(0.5f)] public float weightMatchCm = 4f;
        /// <summary>How far the cloth's normal may turn from the body's and still copy it (degrees).</summary>
        [Range(5f, 90f)] public float weightMatchAngle = 35f;
        /// <summary>The most bones a vertex keeps.</summary>
        [Range(1, 8)] public int weightMaxBones = 4;
        /// <summary>Make the two sides' weights match (Left and Right bones swapped by name).</summary>
        public bool weightMirror;
        /// <summary>Other body shapes the clothes are fitted to, each kept as a blendshape driven by its DNA.</summary>
        public List<BodyShapeTarget> bodyShapes = new List<BodyShapeTarget>();
        /// <summary>An animation to play on the try-on character, to see the clothes move.</summary>
        public AnimationClip tryOnClip;
        public List<ClothPin> pins = new List<ClothPin>();
        /// <summary>Where the Fit Brush has painted the cloth to hug the body, smooth out, puff up or slim down.</summary>
        public List<FitPaintPoint> paint = new List<FitPaintPoint>();
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

    /// <summary>
    /// A body shape the clothes are fitted to as well as the fit's own: one DNA at one end of its range. The
    /// bake drapes the clothes onto it and keeps what the bones do not carry as a blendshape on the slot
    /// (<see cref="BodyShapes.ShapeName"/>), which that DNA then drives.
    /// </summary>
    [Serializable]
    public sealed class BodyShapeTarget
    {
        public string dna;
        /// <summary>The DNA value fitted to: 1 for the top of its range, 0 for the bottom.</summary>
        [Range(0f, 1f)] public float value = 1f;
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

    /// <summary>What the Fit Brush paints.</summary>
    public enum FitPaintMode
    {
        /// <summary>The cloth tightens (cut up to <see cref="FitPaint.MaxHugShrink"/> smaller) and is drawn onto the body.</summary>
        Hug = 0,
        /// <summary>Wrinkles and bumps flatten out (after hugging, round a collar or under the arms).</summary>
        Smooth = 1,
        /// <summary>The cloth is pushed out from the body, as padding or air would.</summary>
        Puff = 2,
        /// <summary>The cloth is drawn in towards the body.</summary>
        Slim = 3,
        /// <summary>Takes the paint off.</summary>
        Erase = 4,
        /// <summary>The cloth is firmer: it stretches and bends less (a firm cuff or band without a separate piece).</summary>
        Stiffen = 5,
        /// <summary>The cloth is heavier, up to <see cref="FitPaint.MaxExtraWeight"/> times more (a weighted hem).</summary>
        Weight = 6
    }

    /// <summary>
    /// One painted point of the cloth, stored on the flat pattern (like a pin) so the paint survives a new mesh
    /// level: the values at a mesh vertex, and the spacing of the mesh it was painted on.
    /// </summary>
    [Serializable]
    public sealed class FitPaintPoint
    {
        public string pieceId;
        /// <summary>Where on the flat piece, as drawn (cm).</summary>
        public Vector2 flatPosition;
        [Range(0f, 1f)] public float hug;
        [Range(0f, 1f)] public float smooth;
        /// <summary>Positive puffs the cloth out, negative slims it in.</summary>
        [Range(-1f, 1f)] public float inflate;
        /// <summary>How much firmer the cloth is here (1: about as firm as firm interfacing).</summary>
        [Range(0f, 1f)] public float stiff;
        /// <summary>How much heavier the cloth is here (1: <see cref="FitPaint.MaxExtraWeight"/> times its weight more).</summary>
        [Range(0f, 1f)] public float weight;
        /// <summary>The mesh's edge length where it was painted (cm): how far the point reaches on another mesh.</summary>
        public float spacingCm = 2f;
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
        /// <summary>
        /// For hats and hoods (pieces laid round the head): publish a clipping plane across their openings and tag the
        /// slot, so UMA holds smooshable hair down inside them (<see cref="HairSmoosh"/>).
        /// </summary>
        public bool holdsHairDown = true;
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
        /// <summary>Bake each look the pieces use into its own overlay, masked to those pieces, over the clothes' own.</summary>
        public bool bakeLooks = true;
        /// <summary>How the slot is meshed: the simulated triangles, or quads made from the pattern (<see cref="QuadRemesher"/>).</summary>
        public SlotTopology slotTopology = SlotTopology.Triangles;
        /// <summary>With quads: also write them, as draped, to an OBJ beside the slot for other tools.</summary>
        public bool writeQuadObj = true;
        /// <summary>Fit the slot to the fit's other body shapes (<see cref="ClothesFit.bodyShapes"/>) as blendshapes.</summary>
        public bool bakeBodyShapes = true;
        /// <summary>Let the clothes move at run time with Unity's Cloth, on a renderer of their own (<see cref="ClothMotion"/>).</summary>
        public bool clothMotion;
        /// <summary>Cloth this close to the body is held: it moves only with the bones (cm).</summary>
        [Min(0f)] public float motionHeldCm = 1.5f;
        /// <summary>How far cloth may swing per centimetre of cloth between it and where it is held.</summary>
        [Range(0f, 1f)] public float motionGain = 0.3f;
        /// <summary>The most any cloth may swing from where the bones put it (cm).</summary>
        [Min(0f)] public float motionMaxCm = 10f;

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
