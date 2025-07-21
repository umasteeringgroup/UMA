using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    [System.Serializable]
    public class DNAEffect_MeshModifier : DNAEffect
    {
        public MeshModifier meshModifier;
        public override string Description => "Applies a mesh modifier to the character's mesh.";
        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.Mesh;

#if UNITY_EDITOR
        public override void DoGui(bool showDescription, bool showHelp)
        {
            base.DoGui(showDescription, showHelp);
            meshModifier = UnityEditor.EditorGUILayout.ObjectField("Mesh Modifier", meshModifier, typeof(MeshModifier), true) as MeshModifier;
            if (meshModifier == null)
            {
                UnityEditor.EditorGUILayout.HelpBox("Mesh Modifier is required.", UnityEditor.MessageType.Error);
            }
        }
#endif
        public override void AfterRecipeGenerated(UMAData avatar, DNA dna, float value)
        {
            base.AfterRecipeGenerated(avatar, dna, value);
            if (avatar != null && meshModifier != null)
            {
                avatar.AddMeshModifiers(meshModifier.modifiers);
            }
        }
    }
}
