using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    public class DNA
    {
        public string name;
        public string description;
        public List<DNAEffect> effects = new List<DNAEffect>();

        public DnaUpdateArea PreApply(DynamicCharacterAvatar avatar, float value)
        {
            DnaUpdateArea updateFlags = DnaUpdateArea.None;
            foreach (var effect in effects)
            {
                updateFlags |= effect.AreaEffect;
                effect.PreApply(avatar, this, value);
            }
            return updateFlags;
        }
        public DnaUpdateArea Apply(DynamicCharacterAvatar avatar, float value)
        {
            DnaUpdateArea updateFlags = DnaUpdateArea.None;
            foreach (var effect in effects)
            {
                updateFlags |= effect.AreaEffect;
                effect.Apply(avatar, this, value);
            }
            return updateFlags;
        }
        public DnaUpdateArea PostApply(DynamicCharacterAvatar avatar, float value)
        {
            DnaUpdateArea updateFlags = DnaUpdateArea.None;
            foreach (var effect in effects)
            {
                updateFlags |= effect.AreaEffect;
                effect.PostApply(avatar, this, value);
            }
            return updateFlags;
        }
    }
}