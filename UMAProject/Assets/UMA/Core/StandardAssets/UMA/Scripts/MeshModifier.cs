using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEngine;
using UnityEngine.XR;

namespace UMA
{
    // MeshModifier is a ScriptableObject that contains lists of VertexAdjustments.
    // Note: This is added to recipes *by name*
    public class MeshModifier : ScriptableObject
    {
        [Tooltip("The name of the slot this modifier is applied to.")]
        public string SlotName;
        [Tooltip("The name of the DNA this modifier gets it's scale value from. Leave blank to manually set the scale.")]
        public string DNAName;
        [Tooltip("The scale value, can be set manually or from a DNA value.")]
        public float Scale = 1.0f;

#if UNITY_EDITOR
        [Tooltip("Named vertex groups")]
        public List<VertexGroup> vertexGroup = new List<VertexGroup>(); // editor only
#endif
        [Tooltip("NormalAdjustments apply first to the mesh. This is so scaling works later in the chain.")]
        public VertexNormalAdjustmentCollection normalAdjustments = new VertexNormalAdjustmentCollection();
        [Tooltip("ColorAdjustments apply to the vertex colors of the mesh.")]
        public VertexColorAdjustmentCollection colorAdjustments = new VertexColorAdjustmentCollection();
        [Tooltip("DeltaAdjustments are added to the vertex positions of the mesh.")]
        public VertexDeltaAdjustmentCollection deltaAdjustments = new VertexDeltaAdjustmentCollection();
        [Tooltip("ScaleAdjustments scale the vertex along it's normal.")]
        public VertexScaleAdjustmentCollection scaleAdjustments = new VertexScaleAdjustmentCollection();
        [Tooltip("UVAdjustments adjust the UVs of the mesh.")]
        public VertexUVAdjustmentCollection uvAdjustments = new VertexUVAdjustmentCollection();
        [Tooltip("BlendshapeAdjustments are for adjusting Delta position, normal, tangents")]
        public VertexBlendshapeAdjustmentCollection blendshapeAdjustments = new VertexBlendshapeAdjustmentCollection();
        [Tooltip("UserAdjustments are for custom adjustments.")]
        public VertexUserAdjustmentCollection userAdjustments = new VertexUserAdjustmentCollection();

        // This method creates a shallow copy of the MeshDetails object, applies the adjustments, and returns the modified copy.
        public MeshDetails Process(MeshDetails Src)
        {
            if (Scale == 0.0f)
            {
                return Src;
            }
            else if (Scale == 1.0f)
            {
                return ApplyAdjustments(Src);
            }

            return ApplyAdjustments(Src, Scale);
        }

        public MeshDetails ApplyAdjustments(MeshDetails Src, float Scale)
        {
            MeshDetails Working = Src.ShallowCopy();
            normalAdjustments.Apply(Src, Working);
            colorAdjustments.Apply(Src, Working);
            deltaAdjustments.Apply(Src, Working);
            scaleAdjustments.Apply(Src, Working);
            uvAdjustments.Apply(Src, Working);
            blendshapeAdjustments.Apply(Src, Working);
            userAdjustments.Apply(Src, Working);

            return Src;
        }

        public MeshDetails ApplyAdjustments(MeshDetails Src)
        {
            MeshDetails Working = Src.ShallowCopy();

            normalAdjustments.ApplyScaled(Src, Working, Scale);
            colorAdjustments.Apply(Src, Working);
            deltaAdjustments.Apply(Src, Working);
            scaleAdjustments.Apply(Src, Working);
            uvAdjustments.Apply(Src, Working);
            blendshapeAdjustments.Apply(Src, Working);
            userAdjustments.Apply(Src, Working);

            return Src;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/UMA/Misc/MeshModifier")]
        public static void CreateMeshModifier()
        {
            UMA.CustomAssetUtility.CreateAsset<MeshModifier>();
        }
#endif
    }
}