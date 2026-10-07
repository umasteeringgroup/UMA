using System;
using System.Collections.Generic;
using UMA.PoseTools;
using UnityEngine;

namespace UMA
{
    /// <summary>Utility slot handlers for recipe overrides and post-DNA bone poses.</summary>
    [AddComponentMenu("UMA/Slots/DNA Override Slot")]
    public sealed class UMADnaOverrideSlot : MonoBehaviour
    {
        [Serializable]
        public sealed class NamedValue
        {
            public string name;
            [Tooltip("UMA normalized value: 1 means 100%.")]
            public float value = 0.5f;
        }

        [Serializable]
        public sealed class BonePoseValue
        {
            public UMABonePose pose;
            [Range(0, 1)] public float weight = 1;
        }

        public List<NamedValue> blendshapes = new List<NamedValue>();
        public List<NamedValue> dna = new List<NamedValue>();
        public List<BonePoseValue> bonePoses = new List<BonePoseValue>();

        public void OnRecipePrepared(UMAData character)
        {
            if (!enabled || character?.umaRecipe == null) return;
            foreach (var entry in blendshapes)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.name)) continue;
                // Do not clear other owners' settings or change the avatar's loading policy.
                character.blendShapeSettings.blendShapes[entry.name] =
                    new BlendShapeData { isBaked = true, value = entry.value };
            }

            var instances = character.umaRecipe.dnaInstanceCollection?.dnaInstances;
            var legacyDNA = character.umaRecipe.GetAllDna();
            foreach (var entry in dna)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.name)) continue;
                if (instances != null)
                    foreach (var instance in instances)
                        if (instance != null && string.Equals(instance.Name, entry.name, StringComparison.OrdinalIgnoreCase))
                            instance.Value = entry.value;
                foreach (var block in legacyDNA)
                {
                    var names = block?.Names;
                    if (names == null) continue;
                    for (int i = 0; i < names.Length; i++)
                        if (string.Equals(names[i], entry.name, StringComparison.OrdinalIgnoreCase)) block.SetValue(i, entry.value);
                }
            }
        }

        public void OnDnaApplied(UMAData character)
        {
            if (!enabled || character?.skeleton == null) return;
            foreach (var entry in bonePoses)
                if (entry?.pose != null) entry.pose.ApplyPose(character.skeleton, Mathf.Clamp01(entry.weight));
        }
    }
}
