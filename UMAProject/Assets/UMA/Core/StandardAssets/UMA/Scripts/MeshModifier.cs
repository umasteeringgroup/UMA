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
    public class MeshModifier : ScriptableObject, ISerializationCallbackReceiver
    {
        [Serializable]
        // each slot affected, will have a modifier.
        public class Modifier
        {
#if UNITY_EDITOR
            public string ModifierName;
            public bool isTemporary = false;
            public VertexAdjustment TemplateAdjustment; // contains the parameters for all the adjustments when adding.
            public bool manuallyModified = false;       // if true, the adjustments have been manually modified, and can't be updated from the template.
            public bool keepAsIs = false;               // if true, this modifier does not need to be split, as it's already set up. Used internally for Blendshape Extraction.
#endif

            [Tooltip("The name of the slot this modifier is applied to.")]
            public string SlotName;
            [Tooltip("The name of the DNA this modifier gets it's scale value from. Leave blank to manually set the scale.")]
            public string DNAName;
            [Tooltip("The scale value, can be set manually or from a DNA value.")]
            public float Scale = 1.0f;

            [Tooltip("This is the list of adjustments for the current slot.")]
            public VertexAdjustmentCollection adjustments;

            public string TemplateAdjustmentJSON;
            public string AdjustmentType;
            public string CollectionType;
            public List<string> JsonAdjustments = new List<string>();

            public void EditorInitialize(Type collectionType)
            {
                adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(collectionType);
                CollectionType = collectionType.AssemblyQualifiedName;

#if UNITY_EDITOR
                Type adjustmentType = adjustments.AdjustmentType;
                TemplateAdjustment = (VertexAdjustment)Activator.CreateInstance(adjustmentType);
                AdjustmentType = adjustmentType.AssemblyQualifiedName;
#endif
            }

            public void BeforeSaving()
            {
#if UNITY_EDITOR
                if (TemplateAdjustment != null)
                {
                    TemplateAdjustmentJSON = JsonUtility.ToJson(TemplateAdjustment);
                }
                else
                {
                    TemplateAdjustmentJSON = "";
                }
#endif
                JsonAdjustments.Clear();
                foreach (var adj in adjustments.vertexAdjustments)
                {
                    JsonAdjustments.Add(JsonUtility.ToJson(adj));
                }
                CollectionType = adjustments.GetType().AssemblyQualifiedName;
                AdjustmentType = adjustments.vertexAdjustments[0].GetType().AssemblyQualifiedName;
            }

            public void AfterLoading()
            {
                Type adjType = Type.GetType(AdjustmentType);
                Type colType = Type.GetType(CollectionType);
                adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(colType);
#if UNITY_EDITOR
                TemplateAdjustment = VertexAdjustment.FromJSON(TemplateAdjustmentJSON);
#endif
                foreach (string json in JsonAdjustments)
                {
                    VertexAdjustment va = VertexAdjustment.FromJSON(json);
                    if (va != null)
                    {
                        adjustments.Add(va);
                    }
                }
            }
             public UMAMeshData Process(UMAMeshData src)
            {
                //??
                if (adjustments == null) return src;

                UMAMeshData Working = src.ShallowClearCopy();
#if UNITY_EDITOR
                Working.ID = "Modified";
#endif
                if (Scale == 0.0f)
                {
                    return src;
                }
                else if (Scale == 1.0f)
                {
                    adjustments.Apply(Working, src);
                }
                else
                {
                    adjustments.ApplyScaled(Working, src, Scale);
                }
                return Working;
            }
            public MeshDetails Process(MeshDetails src)
            {
                MeshDetails Working = src.ShallowCopy();
                if (Scale == 0.0f)
                {
                    return src;
                }
                else if (Scale == 1.0f)
                {
                    adjustments.Apply(Working, src);
                }
                else
                {
                    adjustments.ApplyScaled(Working, src, Scale);
                }
                return Working;
            }
        }

        // There is one modifier per slot.
        // each modifier can contain multiple adjustments.
        public List<Modifier> modifiers = new List<Modifier>();

        public void OnBeforeSerialize()
        {
            foreach (var mod in modifiers)
            {
                mod.BeforeSaving();
            }
        }

        public void OnAfterDeserialize()
        {
            foreach (var mod in modifiers)
            {
                if (mod.adjustments == null || mod.adjustments.vertexAdjustments == null || mod.adjustments.vertexAdjustments.Count == 0)
                {
                    mod.AfterLoading();
                }
            }
        }

        public List<Modifier> Modifiers
        {
            get { return modifiers; }
            set { modifiers = value; }
        }

#if UNITY_EDITOR        
        // These are the "pre-split" modifiers as created in the editor.
        // these are not used at runtime.
        public List<Modifier> editorModifiers = new List<Modifier>();
        public List<Modifier> EditorModifiers
        {
            get { return editorModifiers; }
            set { editorModifiers = value; }
        }

        // These are the "pre-split" ad-hoc adjustments as created in the editor.
        public List<string> AdHocAdjustmentJSON = new List<string>();
#endif


        // This method creates a shallow copy of the MeshDetails object, applies the adjustments, and returns the modified copy.
        public MeshDetails Process(string Slot, MeshDetails Src)
        {
            foreach (var mod in Modifiers)
            {
                // TODO: remove this check, it should be done in the editor.
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