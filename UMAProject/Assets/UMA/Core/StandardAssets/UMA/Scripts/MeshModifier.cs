using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEngine;

namespace UMA
{
    [Serializable]
    // MeshModifier is a ScriptableObject that contains lists of VertexAdjustments.
    // Note: This is added to recipes 
    public class MeshModifier : ScriptableObject
    {
      #if UNITY_EDITOR
        [SerializeField, TextArea(3, 20)]
        private string splitDiagnostics;
        #endif
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
            [SerializeReference]
            public VertexAdjustmentCollection adjustments;

            public void EditorInitialize(Type collectionType)
            {
                adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(collectionType);

#if UNITY_EDITOR
                Type adjustmentType = adjustments.AdjustmentType;
                TemplateAdjustment = (VertexAdjustment)Activator.CreateInstance(adjustmentType);
#endif
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

        // Runtime modifiers are split by slot and used during mesh generation.
        public List<Modifier> runtimeModifiers = new List<Modifier>();


        public List<Modifier> GetScaledRuntimeModifiers(float value)
        {
            List<Modifier> scaledModifiers = new List<Modifier>();
            if (runtimeModifiers == null || runtimeModifiers.Count == 0)
            {
                return scaledModifiers;
            }

            for (int i = 0; i < runtimeModifiers.Count; i++)
            {
                Modifier scaledModifier = CloneRuntimeModifier(runtimeModifiers[i]);
                if (scaledModifier == null)
                {
                    continue;
                }

                scaledModifier.Scale *= value;
                scaledModifiers.Add(scaledModifier);
            }

            return scaledModifiers;
        }

        private static Modifier CloneRuntimeModifier(Modifier source)
        {
            if (source == null)
            {
                return null;
            }

            Modifier clone = new Modifier();
            clone.SlotName = source.SlotName;
            clone.DNAName = source.DNAName;
            clone.Scale = source.Scale;
            clone.adjustments = CloneAdjustmentCollection(source.adjustments);
#if UNITY_EDITOR
            clone.ModifierName = source.ModifierName;
            clone.isTemporary = source.isTemporary;
            clone.TemplateAdjustment = source.TemplateAdjustment != null ? source.TemplateAdjustment.ShallowCopy() : null;
            clone.manuallyModified = source.manuallyModified;
            clone.keepAsIs = source.keepAsIs;
#endif
            return clone;
        }

        private static VertexAdjustmentCollection CloneAdjustmentCollection(VertexAdjustmentCollection source)
        {
            if (source == null)
            {
                return null;
            }

            VertexAdjustmentCollection clone = (VertexAdjustmentCollection)Activator.CreateInstance(source.GetType());
            if (source.vertexAdjustments == null)
            {
                return clone;
            }

            for (int i = 0; i < source.vertexAdjustments.Count; i++)
            {
                VertexAdjustment adjustment = source.vertexAdjustments[i];
                clone.vertexAdjustments.Add(adjustment != null ? adjustment.ShallowCopy() : null);
            }

            return clone;
        }


        public List<Modifier> RuntimeModifiers
        {
            get { return runtimeModifiers; } 
            set { runtimeModifiers = value; }
        }

        #if UNITY_EDITOR
        public string SplitDiagnostics
        {
            get { return splitDiagnostics; }
        }

        // These are the unsplit modifier stacks as created in the editor.
        [SerializeField]
        public List<Modifier> editorModifiers = new List<Modifier>();

        public List<Modifier> EditorModifiers
        {
            get { return editorModifiers; }
            set { editorModifiers = value; }
        }

        public void SyncRuntimeModifiersFromEditorModifiers()
        {
          if (editorModifiers == null || editorModifiers.Count == 0)
            {
                splitDiagnostics = "No editor modifiers to split.";
                return;
            }

            int editorStacks = editorModifiers.Count;
            int editorAdjustmentsTotal = 0;
            int stacksMissingAdjustments = 0;
            int stacksUsingModifierSlotName = 0;
            int stacksUsingAdjustmentSlotName = 0;
            int adjustmentsNull = 0;
            int adjustmentsMissingSlotName = 0;
            int adjustmentsAdded = 0;

            List<Modifier> splitModifiers = new List<Modifier>();
            for (int i = 0; i < editorModifiers.Count; i++)
            {
                Modifier source = editorModifiers[i];
                if (source == null)
                {
                    stacksMissingAdjustments++;
                    continue;
                }

                if (source.keepAsIs)
                {
                    splitModifiers.Add(source);
                    continue;
                }

                if (source.adjustments == null || source.adjustments.vertexAdjustments == null)
                {
                    stacksMissingAdjustments++;
                    continue;
                }

               editorAdjustmentsTotal += source.adjustments.vertexAdjustments.Count;
                if (!string.IsNullOrEmpty(source.SlotName))
                {
                    stacksUsingModifierSlotName++;
                }
                else
                {
                    stacksUsingAdjustmentSlotName++;
                }
                SplitModifierBySlot(splitModifiers, source, ref adjustmentsNull, ref adjustmentsMissingSlotName, ref adjustmentsAdded);
            }

            runtimeModifiers = splitModifiers;

            splitDiagnostics =
                $"Split diagnostics:\n" +
                $"- Editor stacks: {editorStacks}\n" +
             $"- Stacks using Modifier.SlotName: {stacksUsingModifierSlotName}\n" +
                $"- Stacks using adjustment.slotName fallback: {stacksUsingAdjustmentSlotName}\n" +
                $"- Editor adjustments total: {editorAdjustmentsTotal}\n" +
                $"- Stacks skipped (null/missing adjustments): {stacksMissingAdjustments}\n" +
                $"- Adjustments null: {adjustmentsNull}\n" +
                $"- Adjustments missing slotName: {adjustmentsMissingSlotName}\n" +
                $"- Adjustments added to runtime: {adjustmentsAdded}\n" +
                $"- Runtime stacks created: {runtimeModifiers.Count}\n";

            if (runtimeModifiers.Count == 0 && editorAdjustmentsTotal > 0)
            {
                Debug.LogWarning($"[MeshModifier] No runtime modifiers were created during split.\n{splitDiagnostics}", this);
            }
        }

     public static void SplitModifierBySlot(List<Modifier> target, Modifier source, ref int adjustmentsNull, ref int adjustmentsMissingSlotName, ref int adjustmentsAdded)
        {
            if (source == null)
            {
                return;
            }

            if (source.adjustments == null || source.adjustments.vertexAdjustments == null)
            {
                return;
            }

            // Preferred path: the modifier already declares its target slot.
            // This is the runtime-required association; adjustments do not need to carry slotName.
            if (!string.IsNullOrEmpty(source.SlotName))
            {
                Modifier destination = null;
                for (int i = 0; i < target.Count; i++)
                {
                    var candidate = target[i];
                    if (candidate == null || candidate.keepAsIs)
                    {
                        continue;
                    }

                    Type candidateType = candidate.adjustments != null ? candidate.adjustments.GetType() : null;
                    if (candidate.SlotName == source.SlotName && candidateType == source.adjustments.GetType())
                    {
                        bool sameDNA = candidate.DNAName == source.DNAName;
                        bool sameScale = Mathf.Approximately(candidate.Scale, source.Scale);
                        if (sameDNA && sameScale)
                        {
                            destination = candidate;
                            break;
                        }
                    }
                }

                if (destination == null)
                {
                    destination = new Modifier();
                    destination.keepAsIs = false;
                    destination.SlotName = source.SlotName;
                    destination.DNAName = source.DNAName;
                    destination.Scale = source.Scale;
                    destination.adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(source.adjustments.GetType());
#if UNITY_EDITOR
                    destination.ModifierName = source.ModifierName;
                    destination.manuallyModified = source.manuallyModified;
                    destination.isTemporary = source.isTemporary;
#endif
                    target.Add(destination);
                }

                for (int i = 0; i < source.adjustments.vertexAdjustments.Count; i++)
                {
                    var adjustment = source.adjustments.vertexAdjustments[i];
                    if (adjustment == null)
                    {
                        adjustmentsNull++;
                        continue;
                    }
                    #if UNITY_EDITOR
                    // Remove redundant slotName in runtime modifiers to reduce asset size.
                    // Slot is implied by destination.SlotName.
                    adjustment.slotName = null;
                    #endif
                    destination.adjustments.Add(adjustment);
                    adjustmentsAdded++;
                }
                return;
            }

            // Legacy fallback: split by per-adjustment slotName when source modifier has no SlotName.
            foreach (var adjustment in source.adjustments.vertexAdjustments)
            {
                if (adjustment == null || string.IsNullOrEmpty(adjustment.slotName))
                {
                   if (adjustment == null)
                    {
                        adjustmentsNull++;
                    }
                    else
                    {
                        adjustmentsMissingSlotName++;
                    }
                    continue;
                }

                Modifier destination = null;
                for (int i = 0; i < target.Count; i++)
                {
                    var candidate = target[i];
                    if (candidate == null || candidate.keepAsIs)
                    {
                        continue;
                    }

                    Type candidateType = null;
                    if (candidate.adjustments != null)
                    {
                        candidateType = candidate.adjustments.AdjustmentType;
                    }

                    if (candidate.SlotName == adjustment.slotName && candidateType == adjustment.GetType())
                    {
                        destination = candidate;
                        break;
                    }
                }

                if (destination == null)
                {
                    destination = new Modifier();
                    destination.keepAsIs = false;
                    destination.SlotName = adjustment.slotName;
                    destination.DNAName = source.DNAName;
                    destination.Scale = source.Scale;
                    destination.adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(source.adjustments.GetType());
#if UNITY_EDITOR
                    destination.ModifierName = source.ModifierName;
                    destination.manuallyModified = source.manuallyModified;
                    destination.isTemporary = source.isTemporary;
#endif
                    target.Add(destination);
                }

                destination.adjustments.Add(adjustment);
               adjustmentsAdded++;
            }
        }
#endif


        // This method creates a shallow copy of the MeshDetails object, applies the adjustments, and returns the modified copy.
        public MeshDetails Process(string Slot, MeshDetails Src)
        {
            foreach (var mod in RuntimeModifiers)
            {
                // TODO: remove this check, it should be done in the editor.
                if (string.Equals(mod.SlotName, Slot, StringComparison.OrdinalIgnoreCase))
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
