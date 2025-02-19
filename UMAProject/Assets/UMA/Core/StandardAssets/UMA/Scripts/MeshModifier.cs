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
            public bool isTemporary = false;
            public VertexAdjustment TemplateAdjustment; // contains the parameters for all the adjustments when adding.
            public bool manuallyModified = false;       // if true, the adjustments have been manually modified, and can't be updated from the template.
#endif

            [Tooltip("The name of the slot this modifier is applied to.")]
            public string SlotName;
            [Tooltip("The name of the DNA this modifier gets it's scale value from. Leave blank to manually set the scale.")]
            public string DNAName;
            [Tooltip("The scale value, can be set manually or from a DNA value.")]
            public float Scale = 1.0f;
            [Tooltip("This is the list of adjustments for the current slot.")]
            public VertexAdjustmentCollection adjustments;
#if UNITY_EDITOR
            public string TemplateAdjustmentJSON;
            public string AdjustmentType;
            public string CollectionType;
            public List<string> JsonAdjustments = new List<string>();

            public void EditorInitialize(Type collectionType)
            {
                adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(collectionType);
                Type adjustmentType = adjustments.AdjustmentType;
                TemplateAdjustment = (VertexAdjustment)Activator.CreateInstance(adjustmentType);
                CollectionType = collectionType.AssemblyQualifiedName;
                AdjustmentType = adjustmentType.AssemblyQualifiedName;
            }

            public void BeforeSaving()
            {

                if (TemplateAdjustment != null)
                {
                    TemplateAdjustmentJSON = JsonUtility.ToJson(TemplateAdjustment);
                }
                else
                {
                    TemplateAdjustmentJSON = "";
                }
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
                TemplateAdjustment = VertexAdjustment.FromJSON(TemplateAdjustmentJSON);
                foreach (string json in JsonAdjustments)
                {
                    VertexAdjustment va = VertexAdjustment.FromJSON(json);
                    if (va != null)
                    {
                        adjustments.Add(va);
                    }
                }
            }
#endif
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