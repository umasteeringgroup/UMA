using UMA.CharacterSystem;
using UnityEngine;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    public class DNAEffect_SharedColor : DNAEffect
    {
        public enum CombinationMethod
        {
            Additive,
            Subtractive,
            Multiply,
            Replace
        }

        public string sharedColorName = "SharedColor";
        public Color BaseModifier;
        public CombinationMethod colorCombineMethod = CombinationMethod.Additive;


        // Updating a sharedcolor only touches textures
        public override DNAInstanceCollection.DNABuildType AreaEffect => DNABuildType.Texture;
        public override void PreApply(DynamicCharacterAvatar avatar, DNA dna, float value)
        {            
            base.PreApply(avatar, dna, value);
        }
    }
}
