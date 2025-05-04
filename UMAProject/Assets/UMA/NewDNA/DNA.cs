using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public class DNA
    {
        public string name;
        public string description;
        public List<DNAEffect> effects = new List<DNAEffect>();
        public void PreApply(UMAData umaData, float value)
        {
            foreach (var effect in effects)
            {
                effect.PreApply(umaData, this, value);
            }
        }
        public void Apply(UMAData umaData, float value)
        {
            foreach (var effect in effects)
            {
                effect.Apply(umaData, this, value);
            }
        }
        public void PostApply(UMAData umaData, float value)
        {
            foreach (var effect in effects)
            {
                effect.PostApply(umaData, this, value);
            }
        }
    }
}