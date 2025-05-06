using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    public class DNA
    {
        public string name;
        public string description;
        public List<DNAEffect> effects = new List<DNAEffect>();
        public void PreApply(DynamicCharacterAvatar avatar, float value)
        {
            foreach (var effect in effects)
            {
                effect.PreApply(avatar, this, value);
            }
        }
        public void Apply(DynamicCharacterAvatar avatar, float value)
        {
            foreach (var effect in effects)
            {
                effect.Apply(avatar, this, value);
            }
        }
        public void PostApply(DynamicCharacterAvatar avatar, float value)
        {
            foreach (var effect in effects)
            {
                effect.PostApply(avatar, this, value);
            }
        }
    }
}