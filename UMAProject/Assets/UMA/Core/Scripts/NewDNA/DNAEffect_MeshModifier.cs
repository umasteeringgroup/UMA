using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// DNA effect that injects mesh modifiers (vertex adjustment operations) into the UMA build pipeline.
    /// Executed after the recipe merges so modifiers participate in mesh generation.
    /// </summary>
    [System.Serializable]
    public class DNAEffect_MeshModifier : DNAEffect
    {
        /// <summary>
        /// MeshModifier asset containing per-slot vertex operations.
        /// </summary>
        public MeshModifier meshModifier;
        public override string Description => "Applies a mesh modifier to the character's mesh.";
        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.Mesh;

#if UNITY_EDITOR
        /// <inheritdoc />
        public override void DoGui(bool showDescription, bool showHelp, out AnimationCurve curveToCopy)
        {
            base.DoGui(showDescription, showHelp, out curveToCopy);
            meshModifier = UnityEditor.EditorGUILayout.ObjectField("Mesh Modifier", meshModifier, typeof(MeshModifier), true) as MeshModifier;
            if (meshModifier == null)
            {
                UnityEditor.EditorGUILayout.HelpBox("Mesh Modifier is required.", UnityEditor.MessageType.Error);
            }
        }
#endif
        /// <inheritdoc />
        public override void AfterRecipeGenerated(UMAData avatar, DNA dna, float value)
        {
            base.AfterRecipeGenerated(avatar, dna, value);
            if (avatar != null && meshModifier != null)
            {
                avatar.AddMeshModifiers(meshModifier.GetScaledRuntimeModifiers(GetMappedValue(value)));
            }
        }
    }
}
