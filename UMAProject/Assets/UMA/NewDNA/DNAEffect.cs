using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    public abstract class DNAEffect
    {
        // raw values come in as 0-1.
        // this is then mapped to the min/max values 
        // defined in the DNAEffect class.
        public float minMapping = 0.0f;
        public float maxMapping = 1.0f;

        private float GetMappedValue(float value)
        {
            return minMapping + (value * (maxMapping - minMapping));
        }

        public abstract string Name { get; }
        public abstract string Description { get; }

        public virtual void PreApply(DynamicCharacterAvatar avatar, DNA dna, float value)
        {
            // This is called before Apply, so we can do any pre-processing here.
            // For example, we could map the value to a range or perform other calculations.
        }

        // This is called after PreApply, so we can do the actual application of the effect.
        public virtual void Apply(DynamicCharacterAvatar avatar, DNA dna, float value)
        {
            // This is called after PreApply, so we can do the actual application of the effect.
            // For example, we could modify the UMAData based on the mapped value.
            float mappedValue = GetMappedValue(value);
        }

        // This is called after Apply, so we can do any post-processing here.
        public virtual void PostApply(DynamicCharacterAvatar avatar, DNA dna, float value)
        {
            // This is called after Apply, so we can do any post-processing here.
            // For example, we could clean up or reset any temporary values.
        }
    }
}
