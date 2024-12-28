using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEngine;
using UnityEngine.XR;

namespace UMA
{
    [Serializable]
    // MeshModifier is a ScriptableObject that contains lists of VertexAdjustments.
    // Note: This is added to recipes 
    public class MeshModifier : ScriptableObject
    {
        [Serializable]
        // each slot affected, will have a modifier.
        public class Modifier
        {
#if UNITY_EDITOR
            public string ModifierName;
#endif

            [Tooltip("The name of the slot this modifier is applied to.")]
            public string SlotName;
            [Tooltip("The name of the DNA this modifier gets it's scale value from. Leave blank to manually set the scale.")]
            public string DNAName;
            [Tooltip("The scale value, can be set manually or from a DNA value.")]
            public float Scale;
            [Tooltip("This is the list of adjustments for the current slot.")]
            public VertexAdjustmentCollection[] adjustments = new VertexAdjustmentCollection[0];
            public MeshDetails Process(MeshDetails src)
            {
                MeshDetails Working = src.ShallowCopy();
                if (Scale == 0.0f)
                {
                    return src;
                }
                else if (Scale == 1.0f)
                {
                    foreach (var adj in adjustments)
                    {
                        adj.Apply(Working,src);
                    }
                }
                else
                {
                    foreach (var adj in adjustments)
                    {
                        adj.ApplyScaled(Working, src, Scale);
                    }
                }

                return Working;
            }
        }

        // There is one modifier per slot.
        // each modifier can contain multiple adjustments.
        List<Modifier> modifiers = new List<Modifier>();

        public List<Modifier> Modifiers
        {
            get { return Modifiers; }
        }

        // This method creates a shallow copy of the MeshDetails object, applies the adjustments, and returns the modified copy.
        public MeshDetails Process(string Slot, MeshDetails Src)
        {
            foreach (var mod in Modifiers)
            {
                if (mod.SlotName == Slot)
                {
                    return mod.Process(Src);
                }
            }
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