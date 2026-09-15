using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    /// <summary>A small, ordered generation graph: authored guides, zero or more clump
    /// populations, then cards. Each stage reads the previous enabled population exactly once.</summary>
    [Serializable]
    public sealed class HairGenerationPipeline
    {
        public bool enabled;
        public List<HairGenerationStage> clumps = new List<HairGenerationStage>();
        public HairGenerationStage cards = new HairGenerationStage { name = "Final cards", count = 4500, neighbors = 3, clump = 0.75f };
        public HairScalpShadingSettings scalp = new HairScalpShadingSettings();

        public void EnsureIntegrity()
        {
            clumps ??= new List<HairGenerationStage>();
            foreach (var stage in clumps) stage?.EnsureIntegrity();
            cards ??= new HairGenerationStage { name = "Final cards", count = 4500 };
            cards.EnsureIntegrity();
            scalp ??= new HairScalpShadingSettings();
        }

        public HairGenerationPipeline DuplicateForNewGroom()
        {
            var copy = JsonUtility.FromJson<HairGenerationPipeline>(JsonUtility.ToJson(this));
            copy.clumps = new List<HairGenerationStage>();
            foreach (var stage in clumps) if (stage != null) copy.clumps.Add(stage.Duplicate());
            copy.cards = cards.Duplicate();
            // Settings are reusable; references to the old body's topology and masks are not.
            copy.scalp.meshModifier = null; copy.scalp.bindingTopology = null; copy.scalp.slots.Clear();
            void ClearBindings(HairGenerationStage stage)
            {
                stage.partMapId = null;
                foreach (var modifier in stage.modifiers)
                { modifier.maskMapId = null; modifier.helperId = null; }
            }
            foreach (var stage in copy.clumps) ClearBindings(stage);
            ClearBindings(copy.cards);
            copy.EnsureIntegrity(); return copy;
        }
    }

    [Serializable]
    public sealed class HairGenerationStage
    {
        [SerializeField] private string id;
        public string Id => id;
        public string name = "Clump guides";
        public bool enabled = true;
        [Range(1, 20000)] public int count = 400;
        [Min(0f)] public float minimumSpacing = 0.001f;
        [Range(0f, 1f)] public float uniformity = 0.35f;
        [Range(1, 8)] public int neighbors = 6;
        [Range(-1f, 1f)] public float minimumNormalDot = 0.2f;
        [Min(0.001f)] public float influenceRadius = 0.2f;
        public string partMapId;
        [Range(0f, 1f)] public float surfaceConform = 1f;
        [Range(0f, 1f)] public float clump = 0.3f;
        public AnimationCurve clumpAlongStrand = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.45f), new Keyframe(1f, 1f));
        [Range(0f, 1f)] public float rootInfluence = 0.3f;
        [Range(0f, 1f)] public float clumpSpread;
        [Range(0f, 1f)] public float splitTips;
        [Range(0f, 1f)] public float lengthVariation = 0.08f;
        [Range(0f, 1f)] public float widthVariation = 0.15f;
        [Range(0f, 90f)] public float tiltVariation = 8f;
        [Range(0f, 1f)] public float parentCoherence = 0.6f;
        [Range(0f, 1f)] public float flyawayFraction = 0.03f;
        [Min(0f)] public float flyawayAmplitude = 0.003f;
        public int seed = 1729;
        // The curve shape always uses this fixed control lattice. Preview / LOD tessellation
        // is a later operation and cannot change random values or parent assignments.
        [Range(4, 32)] public int shapeSamples = 16;
        public HairHairlineSettings hairline = new HairHairlineSettings();
        public List<HairModifierSettings> modifiers = new List<HairModifierSettings>();
        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            count = Mathf.Clamp(count, 1, 20000); neighbors = Mathf.Clamp(neighbors, 1, 8);
            minimumSpacing = Finite(minimumSpacing, 0f, 1f); uniformity = Finite(uniformity, 0f, 1f);
            minimumNormalDot = Finite(minimumNormalDot, -1f, 1f); influenceRadius = Finite(influenceRadius, 0.001f, 100f);
            clump = Finite(clump, 0f, 1f); rootInfluence = Finite(rootInfluence, 0f, 1f);
            surfaceConform = Finite(surfaceConform, 0f, 1f);
            clumpSpread = Finite(clumpSpread, 0f, 1f); splitTips = Finite(splitTips, 0f, 1f);
            lengthVariation = Finite(lengthVariation, 0f, 1f); widthVariation = Finite(widthVariation, 0f, 1f);
            tiltVariation = Finite(tiltVariation, 0f, 90f); parentCoherence = Finite(parentCoherence, 0f, 1f);
            flyawayFraction = Finite(flyawayFraction, 0f, 1f); flyawayAmplitude = Finite(flyawayAmplitude, 0f, 1f);
            shapeSamples = Mathf.Clamp(shapeSamples, 4, 32);
            clumpAlongStrand ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
            hairline ??= new HairHairlineSettings(); modifiers ??= new List<HairModifierSettings>();
            foreach (var modifier in modifiers) modifier?.EnsureIntegrity();
        }
        private static float Finite(float value, float min, float max) => float.IsFinite(value) ? Mathf.Clamp(value, min, max) : min;
        public HairGenerationStage Duplicate()
        {
            var copy = JsonUtility.FromJson<HairGenerationStage>(JsonUtility.ToJson(this));
            copy.id = null;
            copy.modifiers = new List<HairModifierSettings>();
            foreach (var modifier in modifiers) if (modifier != null) copy.modifiers.Add(modifier.Duplicate());
            copy.EnsureIntegrity(); return copy;
        }
    }

    [Serializable]
    public sealed class HairHairlineSettings
    {
        public bool enabled;
        [Min(0.001f)] public float distance = 0.02f;
        [Range(0.01f, 1f)] public float edgeLength = 0.6f;
        [Range(0.01f, 1f)] public float edgeWidth = 0.4f;
        [Range(0f, 1f)] public float edgeDensity = 0.65f;
        [Range(0f, 1f)] public float alignFacing = 0.7f;
        [Range(0f, 1f)] public float inwardLean = 0.15f;
    }

    [Serializable]
    public sealed class HairScalpShadingSettings
    {
        public bool enabled;
        public Color color = new Color(0.16f, 0.09f, 0.04f, 1f);
        [Range(0f, 1f)] public float strength = 0.7f;
        [Min(0.001f)] public float edgeFade = 0.012f;
        public bool preserveAlpha = true;
        public UnityEngine.Object meshModifier;
        public string bindingTopology;
        public List<HairScalpSlotBinding> slots = new List<HairScalpSlotBinding>();
    }

    [Serializable]
    public sealed class HairScalpSlotBinding
    {
        public string slotName;
        public int sourceVertexStart;
        public int vertexCount;
    }

    [Serializable]
    public sealed class HairModifierMask
    {
        public bool useLength;
        public Vector2 lengthRange = new Vector2(0f, 0.2f);
        public bool useHairline;
        public Vector2 hairlineRange = new Vector2(0f, 0.03f);
        [Range(0f, 1f)] public float randomVariation;
        [Range(0f, 1f)] public float parentCoherence;
        public bool invert;
        public AnimationCurve remap = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }
}
