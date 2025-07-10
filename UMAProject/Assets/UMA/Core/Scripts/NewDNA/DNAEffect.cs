using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    [Serializable]

    public abstract class DNAEffect 
    {
        // raw values come in as 0-1.
        // Then are passed through the curve unless the curve is linear 0-1 (no effect).
        // this is then mapped to the min/max values
        // And finally applied to the avatar's DNA.

        // The curve is used to modify the value before applying it.
        [SerializeField]
        public AnimationCurve curve = new AnimationCurve();
        public float minMapping = 0.0f; // The minimum value to map. This will be the base value when the adjusted input is 0.
        public float maxMapping = 1.0f; // The maximum value to map. This will be the maximum value when the adjusted input is 1.
#if UNITY_EDITOR
        private DNACurve _TemplateCurve = null;
        public bool expanded;
        public string title
        {
            get
            {
                return EffectName + " (" + baseEffectName+ ")";
            }
        }

        public string baseEffectName
        {
            get
            {
                return GetType().ToString().Replace("DNA", "");
            }
        }
#endif
        public virtual DNABuildType AreaEffect
        {
            get
            {
                // This is a placeholder. You need to return the actual area affected by this effect.
                return DNABuildType.None;
            }
        }

        protected float GetMappedValue(float value)
        { 
            if (curve != null && curve.length > 0)
            {
                value = curve.Evaluate(value);
            }
            return minMapping + (value * (maxMapping - minMapping));
        }

        public string EffectName;
        public virtual string Description { get; }

#if UNITY_EDITOR
        public virtual void DoGui(bool showDescription, bool showHelp = false)
        {

            if (showHelp)
            {
                EditorGUILayout.HelpBox($"{baseEffectName}: {this.Description}", MessageType.None);
                EditorGUILayout.HelpBox("Raw DNA Values are 0 - 1. These are evaluated on the curve (if present), and then mapped to the min & max values ", MessageType.None);
            }
            // select: 0,1
            // select: 1,0
            // select: 0,1,0
            // select: 1,0,1
            EffectName = EditorGUILayout.DelayedTextField("Effect Name", EffectName);
            curve = EditorGUILayout.CurveField("Curve", curve);
            minMapping = EditorGUILayout.DelayedFloatField("Min", minMapping);
            maxMapping = EditorGUILayout.DelayedFloatField("Max", maxMapping);
            EditorGUILayout.HelpBox("You can load a template curve here. This will set the Min, Max and Curve values to the values in the template curve. The template curve is not saved.", MessageType.Info);
            bool wasNull = _TemplateCurve == null;
            _TemplateCurve = EditorGUILayout.ObjectField("Template Curve", _TemplateCurve, typeof(DNACurve), false) as DNACurve;
            if (_TemplateCurve != null && wasNull)
            {
                minMapping = _TemplateCurve.minMapping;
                maxMapping = _TemplateCurve.maxMapping;
                curve = _TemplateCurve.Curve;
            }
            // add option to save curve to asset for use in other effects
        }
#endif
        public virtual void AfterRecipeGenerated(DynamicCharacterAvatar avatar, DNA dna, float value)
        {
            // This is called during the avatar Load process, after the recipe is merged.
            // Some effects need to be applied during the load process, such as those that modify avatar shared colors.
        }

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
