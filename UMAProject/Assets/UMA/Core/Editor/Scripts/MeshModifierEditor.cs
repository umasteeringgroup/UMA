using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UMA;
using UMA.CharacterSystem;
using UMA.Editors;
using System;
using System.Xml.Serialization;
using UnityEditor.Embree;

#if UMA_BURSTCOMPILE
using Unity.Burst;
#endif

namespace UMA
{
    public class MeshModifierEditor : EditorWindow
    {
        public bool RebuildOnChanges = false;
        public List<MeshModifier.Modifier> Modifiers = new List<MeshModifier.Modifier>();
        public List<string> BlendShapes = new List<string>();
        public string[] strBlendShapes = new string[0];
        public List<string> blendShapeSlots = new List<string>();
        public List<bool> blendShapeSlotSelected = new List<bool>();

        bool wasAnimatorEnabled;
        bool wasKeepAnimator;
        bool wasRaceFixup;
        Quaternion wasGlobalRotation = Quaternion.identity;
        Quaternion wasRootRotation = Quaternion.identity;



        public static MeshModifierEditor GetOrCreateWindow(DynamicCharacterAvatar DCA, VertexEditorStage vstage)
        {
            MeshModifierEditor wnd = GetWindow<MeshModifierEditor>(true, "Mesh Modifiers", true);
            wnd.Setup(DCA, vstage, null);
            wnd.titleContent = new GUIContent("Mesh Modifiers");
            return wnd;
        }

        public static MeshModifierEditor GetOrCreateWindowFromModifier(MeshModifier modifier, DynamicCharacterAvatar DCA, VertexEditorStage vstage)
        {
            MeshModifierEditor wnd = GetWindow<MeshModifierEditor>(true, "Mesh Modifiers", true);
            wnd.Setup(DCA, vstage, modifier);
            wnd.titleContent = new GUIContent("Mesh Modifiers");
            return wnd;
        }

        public DynamicCharacterAvatar thisDCA;
        public Dictionary<string, MeshModifier> SlotNameToModifiers = new Dictionary<string, MeshModifier>();
        public bool ShowVisibleSlots = false;
        public bool ShowOptions = false;
        public VertexEditorStage vertexEditorStage;
        public int currentModifierIndex = 0;
        public Type[] ModifierTypes = new Type[0];
        public string[] ModifierTypeNames = new string[0];
        public int selectedType = 0;
        //public VertexAdjustment templateAdjustment = null;
        public VertexAdjustmentCollection templateVertexAdjustmentCollection = null;
        public GUIStyle centeredLabel = new GUIStyle();
        public Color backColor = Color.cyan;
        public bool editingCurrent = false;
        public GUIStyle selectedButton;
        public GUIStyle unselectedButton;
        public enum EditorMode { MeshModifiers, VertexAdjustments, Blendshapes }
        public EditorMode editorMode = EditorMode.VertexAdjustments;

        private bool suppressUndoRebuild = false;
        private bool activeAdjustmentInteractiveUndoArmed = false;
        private bool bulkModifierInteractiveUndoArmed = false;

        private UnityEngine.Object[] GetUndoTargets(bool includeCharacterState = false)
        {
            List<UnityEngine.Object> targets = new List<UnityEngine.Object>();
            targets.Add(this);

            if (vertexEditorStage != null)
            {
                targets.Add(vertexEditorStage);
                if (vertexEditorStage.BakedMesh != null)
                {
                    targets.Add(vertexEditorStage.BakedMesh);
                }
            }

            if (includeCharacterState && thisDCA != null)
            {
                targets.Add(thisDCA);
                if (thisDCA.umaData != null)
                {
                    targets.Add(thisDCA.umaData);
                }
            }

            return targets.ToArray();
        }

        private void RegisterUndoSnapshot(string actionName, bool includeCharacterState = false)
        {
            Undo.RegisterCompleteObjectUndo(GetUndoTargets(includeCharacterState), actionName);
        }

        private void MarkEditorStateDirty(bool includeCharacterState = false)
        {
            EditorUtility.SetDirty(this);
            if (vertexEditorStage != null)
            {
                EditorUtility.SetDirty(vertexEditorStage);
                if (vertexEditorStage.BakedMesh != null)
                {
                    EditorUtility.SetDirty(vertexEditorStage.BakedMesh);
                }
            }
            if (includeCharacterState && thisDCA != null)
            {
                EditorUtility.SetDirty(thisDCA);
                if (thisDCA.umaData != null)
                {
                    EditorUtility.SetDirty(thisDCA.umaData);
                }
            }
        }

        private void HandleInteractiveUndoCapture(ref bool isArmed, string actionName)
        {
            if (Event.current == null)
            {
                return;
            }

            EventType type = Event.current.type;
            EventType rawType = Event.current.rawType;

            if (rawType == EventType.MouseUp || type == EventType.MouseUp || type == EventType.Ignore)
            {
                isArmed = false;
                return;
            }

            if (!isArmed && (type == EventType.MouseDown || type == EventType.KeyDown || type == EventType.ScrollWheel))
            {
                RegisterUndoSnapshot(actionName);
                isArmed = true;
            }

            if (type == EventType.Repaint && GUIUtility.hotControl == 0)
            {
                isArmed = false;
            }
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnUndoRedoPerformed()
        {
            if (suppressUndoRebuild)
            {
                return;
            }

            Repaint();
            SceneView.RepaintAll();

            if (thisDCA == null || vertexEditorStage == null)
            {
                return;
            }

            suppressUndoRebuild = true;
            try
            {
                DoCharacterRebuild();
            }
            finally
            {
                suppressUndoRebuild = false;
            }
        }

        public void Setup(DynamicCharacterAvatar DCA, VertexEditorStage vstage, MeshModifier modifier)
        {
            thisDCA = DCA;
            wasKeepAnimator = DCA.KeepAnimatorController;
            wasAnimatorEnabled = DCA.gameObject.GetComponent<Animator>().enabled;
            wasRaceFixup = DCA.activeRace.data.FixupRotations;

            Transform rootTransform = DCA.umaData.skeleton.GetRootTransform();
            Transform globalTransform = DCA.umaData.skeleton.GetGlobalTransform();

            wasGlobalRotation = globalTransform.localRotation;
            wasRootRotation = rootTransform.localRotation;

            SlotNameToModifiers.Clear();
            vertexEditorStage = vstage;
            ModifierTypes = AppDomain.CurrentDomain.GetAllDerivedTypes(typeof(VertexAdjustmentCollection));
            ModifierTypeNames = new string[ModifierTypes.Length];
            for (int i = 0; i < ModifierTypes.Length; i++)
            {
                ModifierTypeNames[i] = ObjectNames.NicifyVariableName(ModifierTypes[i].Name); 
                ModifierTypeNames[i] = ModifierTypeNames[i].Replace(" Collection", "");
            }

            if (modifier == null)
            {
                Debug.Log("[MeshModifierEditor.Setup] modifier is null, creating empty list");
                // create a new modifier?
                Modifiers = new List<MeshModifier.Modifier>();
            }
            else
            {
                currentModifierIndex = 0;

                // Debug: Log the state of the incoming modifier
                int editorCount = modifier.EditorModifiers != null ? modifier.EditorModifiers.Count : -1;
                int runtimeCount = modifier.RuntimeModifiers != null ? modifier.RuntimeModifiers.Count : -1;
                int editorAdjustments = 0;
                if (modifier.EditorModifiers != null)
                {
                    foreach (var m in modifier.EditorModifiers)
                    {
                        if (m != null && m.adjustments != null && m.adjustments.vertexAdjustments != null)
                        {
                            editorAdjustments += m.adjustments.vertexAdjustments.Count;
                        }
                    }
                }
                Debug.Log($"[MeshModifierEditor.Setup] Incoming modifier: EditorModifiers={editorCount}, RuntimeModifiers={runtimeCount}, Total EditorAdjustments={editorAdjustments}");

                if (modifier.EditorModifiers != null && modifier.EditorModifiers.Count > 0)
                {
                    // Create a copy to avoid modifying the original asset data
                    Modifiers = new List<MeshModifier.Modifier>(modifier.EditorModifiers);
                    Debug.Log($"[MeshModifierEditor.Setup] Using EditorModifiers, copied {Modifiers.Count} modifiers");
                }
                else if (modifier.RuntimeModifiers != null)
                {
                    // Create a copy to avoid modifying the original asset data
                    Modifiers = new List<MeshModifier.Modifier>(modifier.RuntimeModifiers);
                    Debug.Log($"[MeshModifierEditor.Setup] Using RuntimeModifiers, copied {Modifiers.Count} modifiers");
                }
                else
                {
                    Debug.Log("[MeshModifierEditor.Setup] Both EditorModifiers and RuntimeModifiers are null/empty");
                    Modifiers = new List<MeshModifier.Modifier>();
                }

                HydrateAdHocAdjustmentsFromEditorModifiers();
                NormalizeLoadedModifiersToSourceSlots();

                // Debug: Log state after hydration
                Debug.Log($"[MeshModifierEditor.Setup] After hydration: Modifiers.Count={Modifiers.Count}");
            }
            // vertexEditorStage = VertexEditorStage.ShowStage(DCA);
            centeredLabel = EditorStyles.boldLabel;
            centeredLabel.alignment = TextAnchor.MiddleCenter;
            selectedButton = new GUIStyle(EditorStyles.miniButton);
            unselectedButton = new GUIStyle(EditorStyles.miniButton);
            selectedButton.normal.textColor = Color.white;
            selectedButton.normal.background = new Texture2D(1, 1);
            selectedButton.normal.background.SetPixel(0, 0, Color.blue);
            selectedButton.normal.background.Apply();
            unselectedButton.normal.textColor = Color.black;
            unselectedButton.normal.background = new Texture2D(1, 1);
            unselectedButton.normal.background.SetPixel(0, 0, Color.white);
            unselectedButton.normal.background.Apply();
        }

        private string GetModifierSlotKey(SlotData slot)
        {
            if (slot == null)
            {
                return string.Empty;
            }

            if (slot.asset != null && !string.IsNullOrEmpty(slot.asset.sourceSlot))
            {
                return slot.asset.sourceSlot;
            }

            return slot.slotName;
        }

        private string NormalizeModifierSlotKey(string slotKey)
        {
            if (string.IsNullOrEmpty(slotKey))
            {
                return string.Empty;
            }

            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return slotKey;
            }

            for (int i = 0; i < thisDCA.umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData slot = thisDCA.umaData.umaRecipe.slotDataList[i];
                if (slot == null || slot.asset == null)
                {
                    continue;
                }

                string sourceSlot = slot.asset.sourceSlot;
                if (string.Equals(sourceSlot, slotKey, StringComparison.OrdinalIgnoreCase))
                {
                    return sourceSlot;
                }

                if (string.Equals(slot.slotName, slotKey, StringComparison.OrdinalIgnoreCase))
                {
                    return sourceSlot;
                }
            }

            return slotKey;
        }

        private SlotData FindSlotByModifierSlotKey(string slotKey)
        {
            if (string.IsNullOrEmpty(slotKey) || thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return null;
            }

            string normalizedKey = NormalizeModifierSlotKey(slotKey);
            for (int i = 0; i < thisDCA.umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData slot = thisDCA.umaData.umaRecipe.slotDataList[i];
                if (slot == null)
                {
                    continue;
                }

                string slotSource = GetModifierSlotKey(slot);
                if (string.Equals(slotSource, normalizedKey, StringComparison.OrdinalIgnoreCase))
                {
                    return slot;
                }
            }

            return null;
        }

        private void NormalizeLoadedModifiersToSourceSlots()
        {
            if (Modifiers == null)
            {
                return;
            }

            for (int i = 0; i < Modifiers.Count; i++)
            {
                MeshModifier.Modifier modifier = Modifiers[i];
                if (modifier == null)
                {
                    continue;
                }

                modifier.SlotName = NormalizeModifierSlotKey(modifier.SlotName);
                if (modifier.adjustments == null || modifier.adjustments.vertexAdjustments == null)
                {
                    continue;
                }

                for (int j = 0; j < modifier.adjustments.vertexAdjustments.Count; j++)
                {
                    VertexAdjustment adjustment = modifier.adjustments.vertexAdjustments[j];
                    if (adjustment == null)
                    {
                        continue;
                    }

                    adjustment.slotName = NormalizeModifierSlotKey(adjustment.slotName);
                }
            }
        }
        
        private bool IncludeAdHocAdjustments = true;
        private bool IncludeActiveOnlyBulk = true;
        private bool IncludeBulkModifiers = true;

        public void OnGUI()
        {
            if (thisDCA == null)
            {
                EditorGUILayout.LabelField("No DCA selected");
                return;
            }

            GUIStyle VertexModeStyle = unselectedButton;
            GUIStyle MeshModifierModeStyle = unselectedButton;
            GUIStyle BlendshapeStyle = unselectedButton;

            if (editorMode == EditorMode.MeshModifiers)
            {
                MeshModifierModeStyle = selectedButton;
            }
            else if (editorMode == EditorMode.VertexAdjustments)
            {
                VertexModeStyle = selectedButton;
            }
            else
            {
                BlendshapeStyle = selectedButton;
            }
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            GUILayout.Label("Modifiers", centeredLabel);


            bool newIncludeAdHocAdjustments = GUILayout.Toggle(IncludeAdHocAdjustments, "Include Ad-Hoc adjustments");
            if (newIncludeAdHocAdjustments != IncludeAdHocAdjustments)
            {
                RegisterUndoSnapshot("Toggle Include Ad-Hoc Adjustments");
                IncludeAdHocAdjustments = newIncludeAdHocAdjustments;
                MarkEditorStateDirty();
            }

            bool newIncludeBulkModifiers = GUILayout.Toggle(IncludeBulkModifiers, "Include Bulk Modifiers");
            if (newIncludeBulkModifiers != IncludeBulkModifiers)
            {
                RegisterUndoSnapshot("Toggle Include Bulk Modifiers");
                IncludeBulkModifiers = newIncludeBulkModifiers;
                if (IncludeBulkModifiers == false)
                {
                    IncludeActiveOnlyBulk = false;
                }
                MarkEditorStateDirty();
            }

            bool newIncludeActiveOnlyBulk = GUILayout.Toggle(IncludeActiveOnlyBulk, "Only Active Bulk Modifier");
            if (newIncludeActiveOnlyBulk != IncludeActiveOnlyBulk)
            {
                RegisterUndoSnapshot("Toggle Include Active Bulk Modifier");
                IncludeActiveOnlyBulk = newIncludeActiveOnlyBulk;
                MarkEditorStateDirty();
            }

            bool newRebuildOnChanges = GUILayout.Toggle(RebuildOnChanges, "Rebuild on changes");
            if (newRebuildOnChanges != RebuildOnChanges)
            {
                RegisterUndoSnapshot("Toggle Rebuild On Changes");
                RebuildOnChanges = newRebuildOnChanges;
                MarkEditorStateDirty();
            }

            if (GUILayout.Button("Rebuild Now"))
            {
                RegisterUndoSnapshot("Rebuild Character", true);
                DoCharacterRebuild();
                MarkEditorStateDirty(true);
            }
            if (GUILayout.Button("Rebuild to TPose"))
            {
                RegisterUndoSnapshot("Rebuild Character To T-Pose", true);
                DoCharacterRebuild(true);
                MarkEditorStateDirty(true);
            }

            if (GUILayout.Button("Reset Build"))
            {
                RegisterUndoSnapshot("Reset Character Build", true);
                DoCharacterReset();
                MarkEditorStateDirty(true);
            }
            if (GUILayout.Button("Save to Asset"))
            {
                // Get the name of the new asset to save it to.
                // Create a new MeshModifier asset and save it.
                // split all the modifiers (ad-hoc and bulk) into a list of modifiers.
                // save the list of modifiers to the asset.

                SaveToAsset();
            }
            if (GUILayout.Button("Recalculate Normals"))
            {
                RegisterUndoSnapshot("Recalculate Normals");
                vertexEditorStage.RecalculateNormals();
                MarkEditorStateDirty();
            }

            if (IncludeBulkModifiers == false)
            {
                IncludeActiveOnlyBulk = false;
            }

            GUIHelper.EndVerticalPadded(10);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ad-hoc Adjustments", VertexModeStyle))
            {
                RegisterUndoSnapshot("Switch To Ad-Hoc Adjustments");
                editorMode = EditorMode.VertexAdjustments;
                bulkModifierInteractiveUndoArmed = false;
                activeAdjustmentInteractiveUndoArmed = false;
                MarkEditorStateDirty();
            }
            if (GUILayout.Button("Bulk Add Active", MeshModifierModeStyle))
            {
                RegisterUndoSnapshot("Switch To Bulk Modifiers");
                editorMode = EditorMode.MeshModifiers;
                bulkModifierInteractiveUndoArmed = false;
                activeAdjustmentInteractiveUndoArmed = false;
                deActivateCurrentSelection();
                vertexEditorStage.SetActive(null);
                MarkEditorStateDirty();
            }
            if (GUILayout.Button("Extract Blendshapes", BlendshapeStyle))
            {
                RegisterUndoSnapshot("Switch To Blendshape Extractor");
                editorMode = EditorMode.Blendshapes;
                bulkModifierInteractiveUndoArmed = false;
                activeAdjustmentInteractiveUndoArmed = false;
                deActivateCurrentSelection();
                vertexEditorStage.SetActive(null);
                MarkEditorStateDirty();
            }
            vertexEditorStage.editorMode = editorMode;
            GUILayout.EndHorizontal();

            if (editorMode == EditorMode.MeshModifiers)
            {
                DrawMeshModifiers();
            }
            else if (editorMode == EditorMode.VertexAdjustments)
            {
                DrawAdHocAdjustments();
            }
            else
            {
                DrawBlendshapeExtractor();
            }
        }

        private static string MeshModifierSaveFolderKey => $"UMA_MeshModifierSaveFolder_{Application.dataPath.GetHashCode()}";

        public void SaveToAsset()
        {
            // Get the last used folder for this project, default to "Assets"
            string lastFolder = EditorPrefs.GetString(MeshModifierSaveFolderKey, "Assets");

            // Ensure the folder still exists, otherwise fall back to Assets
            if (!AssetDatabase.IsValidFolder(lastFolder))
            {
                lastFolder = "Assets";
            }

            string path = EditorUtility.SaveFilePanelInProject("Save MeshModifier", "MeshModifier", "asset", "Save current MeshModifier to project", lastFolder);
            if (!string.IsNullOrEmpty(path))
            {
                // Remember the folder for next time
                string folder = System.IO.Path.GetDirectoryName(path);
                EditorPrefs.SetString(MeshModifierSaveFolderKey, folder);

                string BaseName = System.IO.Path.GetFileNameWithoutExtension(path);
                MeshModifier meshModifier = CustomAssetUtility.ReplaceAsset<MeshModifier>(path, false);
                List<MeshModifier.Modifier> editorSnapshot = BuildEditorModifierSnapshot(includeBulkModifiers: true, includeAdHocModifiers: true, onlyActiveBulkModifier: false);
                meshModifier.EditorModifiers = editorSnapshot;
                meshModifier.RuntimeModifiers = SplitModifierStacksBySlot(editorSnapshot);
                EditorUtility.SetDirty(meshModifier);
                AssetDatabase.SaveAssetIfDirty(meshModifier);
            }
        }

        private void HydrateAdHocAdjustmentsFromEditorModifiers()
        {
            if (vertexEditorStage == null)
            {
                Debug.Log("[HydrateAdHoc] vertexEditorStage is null, skipping");
                return;
            }

            List<VertexAdjustment> adHocAdjustments = vertexEditorStage.GetVertexAdjustments();
            adHocAdjustments.Clear();

            if (Modifiers == null)
            {
                Debug.Log("[HydrateAdHoc] Modifiers is null, skipping");
                return;
            }

            Debug.Log($"[HydrateAdHoc] Processing {Modifiers.Count} modifiers...");

            int totalAdjustmentsExtracted = 0;
            int modifiersKept = 0;
            int modifiersExtracted = 0;

            for (int i = Modifiers.Count - 1; i >= 0; i--)
            {
                MeshModifier.Modifier mod = Modifiers[i];
                if (mod == null)
                {
                    Debug.Log($"[HydrateAdHoc] Modifier {i} is null, skipping");
                    continue;
                }

                int adjustmentCount = mod.adjustments != null && mod.adjustments.vertexAdjustments != null 
                    ? mod.adjustments.vertexAdjustments.Count : 0;

                if (mod.manuallyModified == false)
                {
                    Debug.Log($"[HydrateAdHoc] Modifier {i} '{mod.ModifierName}' has manuallyModified=false, keeping as bulk modifier ({adjustmentCount} adjustments)");
                    modifiersKept++;
                    continue;
                }

                Debug.Log($"[HydrateAdHoc] Modifier {i} '{mod.ModifierName}' has manuallyModified=true, extracting {adjustmentCount} adjustments to ad-hoc");
                modifiersExtracted++;

                if (mod.adjustments != null && mod.adjustments.vertexAdjustments != null)
                {
                    foreach (VertexAdjustment adjustment in mod.adjustments.vertexAdjustments)
                    {
                        if (adjustment != null)
                        {
                            adHocAdjustments.Add(adjustment);
                            totalAdjustmentsExtracted++;
                        }
                    }
                }

                Modifiers.RemoveAt(i);
            }

            Debug.Log($"[HydrateAdHoc] Done: {modifiersKept} bulk modifiers kept, {modifiersExtracted} extracted to ad-hoc, {totalAdjustmentsExtracted} total adjustments extracted");
        }

        private List<MeshModifier.Modifier> BuildEditorModifierSnapshot(bool includeBulkModifiers, bool includeAdHocModifiers, bool onlyActiveBulkModifier)
        {
            List<MeshModifier.Modifier> snapshot = new List<MeshModifier.Modifier>();

            if (includeBulkModifiers && Modifiers != null)
            {
                if (onlyActiveBulkModifier)
                {
                    if (currentModifierIndex >= 0 && currentModifierIndex < Modifiers.Count)
                    {
                        snapshot.Add(Modifiers[currentModifierIndex]);
                    }
                }
                else
                {
                    snapshot.AddRange(Modifiers);
                }
            }

            if (includeAdHocModifiers && vertexEditorStage != null)
            {
                Dictionary<string, MeshModifier.Modifier> adHocStacks = new Dictionary<string, MeshModifier.Modifier>();
                foreach (VertexAdjustment adjustment in vertexEditorStage.GetVertexAdjustments())
                {
                    if (adjustment == null || string.IsNullOrEmpty(adjustment.slotName))
                    {
                        continue;
                    }

                    string normalizedSlot = NormalizeModifierSlotKey(adjustment.slotName);
                    if (string.IsNullOrEmpty(normalizedSlot))
                    {
                        continue;
                    }

                    adjustment.slotName = normalizedSlot;

                    string key = normalizedSlot + ":" + adjustment.GetType().AssemblyQualifiedName;
                    if (!adHocStacks.ContainsKey(key))
                    {
                        MeshModifier.Modifier adHocModifier = new MeshModifier.Modifier();
                        adHocModifier.manuallyModified = true;
                        adHocModifier.keepAsIs = false;
                        adHocModifier.SlotName = normalizedSlot;
                        adHocModifier.ModifierName = "Ad-hoc " + adjustment.Name;
                        adHocModifier.DNAName = string.Empty;
                        adHocModifier.Scale = 1.0f;
                        adHocModifier.TemplateAdjustment = (VertexAdjustment)Activator.CreateInstance(adjustment.GetType());
                        adHocModifier.adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(adjustment.VertexAdjustmentCollection.GetType());
                        adHocStacks.Add(key, adHocModifier);
                    }

                    adHocStacks[key].adjustments.Add(adjustment);
                }

                foreach (var stack in adHocStacks.Values)
                {
                    snapshot.Add(stack);
                }
            }

            return snapshot;
        }

        private List<MeshModifier.Modifier> SplitModifierStacksBySlot(List<MeshModifier.Modifier> sourceModifiers)
        {
            List<MeshModifier.Modifier> result = new List<MeshModifier.Modifier>();
            if (sourceModifiers == null)
            {
                return result;
            }

            foreach (MeshModifier.Modifier mod in sourceModifiers)
            {
                SplitModifiersBySlot(result, mod);
                Debug.Log($"Split modifier {mod.ModifierName} into {result.Count} modifiers");
                foreach(MeshModifier.Modifier splitMod in result)
                {
                    Debug.Log($" - {splitMod.ModifierName} (Slot: {splitMod.SlotName}, Adjustments: {(splitMod.adjustments != null ? splitMod.adjustments.vertexAdjustments.Count : 0)})");
                }
            }

           

            return result;
        }

        VertexAdjustment activeAdjustment = null;
        public void SetActive(VertexAdjustment va, bool activeState = true, bool registerUndo = false)
        {
            if (registerUndo)
            {
                RegisterUndoSnapshot(activeState ? "Activate Vertex Adjustment" : "Deactivate Vertex Adjustment");
            }
            deActivateCurrentSelection();
            
            va.active = activeState;
            if (activeState)
            {
                vertexEditorStage.SetActive(va);
                activeAdjustment = va;
            }
            else
            {
                vertexEditorStage.SetActive(null);
                activeAdjustment = null;
            }
            if (registerUndo)
            {
                MarkEditorStateDirty();
            }
        }

        private List<bool> ExpandList(List<bool> theList, int newSize)
        {
            if (theList.Count < newSize)
            {
                while (theList.Count < newSize)
                {
                    theList.Add(false);
                }
            }
            return theList;
        }

        public void SetExpanded(List<bool> theList, int active)
        {
            for (int i = 0; i < theList.Count; i++)
            {
                theList[i] = false;
            }
            theList[active] = true;
        }
        //public Dictionary<int, bool> VertexFoldouts = new Dictionary<int, bool>();

        private Vector2 vertexScrollPos = new Vector2();

        public List<bool> FoldOuts = new List<bool>();
        private VertexAdjustmentCollection templateCollection = null;
        private int currentBlendshape = 0;
        private string[] dnaNames = new string[0];
        private int currentDNA = 0;

        private void DrawBlendshapeExtractor()
        {
            // Allow to select the blendshape
            // Allow to select the slots to add.
            // Allow to select the DNA to drive it (or use "Manual").
            // foreach slot, 
            //    extract Blendshape
            //    create a modifier for it.
            //    add all the vertexes which have changes from the base.
            EditorGUILayout.HelpBox("Blendshape Extraction allows you to create a MeshModifier that acts as a Blendshape. You can assign it to DNA to vary the blendshape value", MessageType.Info);
            if (BlendShapes.Count == 0)
            {
                var Renderer = thisDCA.umaData.GetRenderer(0);
                if (Renderer == null)
                {
                    EditorGUILayout.HelpBox("No Renderer was found on this character", MessageType.Warning);
                    return;
                }
                if (Renderer.sharedMesh.blendShapeCount < 1)
                {
                    EditorGUILayout.HelpBox("No blendshapes were found on this renderer! Please turn on blendshapes and reconstruct character", MessageType.Warning);
                }
                for (int i = 0; i < Renderer.sharedMesh.blendShapeCount; i++)
                {
                    string blShape = Renderer.sharedMesh.GetBlendShapeName(i);
                    BlendShapes.Add(blShape);
                }
                strBlendShapes = BlendShapes.ToArray();

                blendShapeSlots = new List<string>();
                blendShapeSlotSelected = new List<bool>();
                foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
                {
                    if (slot != null)
                    {
                        string slotKey = GetModifierSlotKey(slot);
                        if (!blendShapeSlots.Contains(slotKey))
                        {
                            blendShapeSlots.Add(slotKey);
                            blendShapeSlotSelected.Add(false);
                        }
                    }
                }
                var dnaList = thisDCA.activeRace.data.GetDNANames();
                dnaList.Insert(0, "Manual");
                dnaNames = dnaList.ToArray();
            }
            
            int newCurrentBlendshape = EditorGUILayout.Popup("Select Blendshape", currentBlendshape, strBlendShapes);
            if (newCurrentBlendshape != currentBlendshape)
            {
                RegisterUndoSnapshot("Select Blendshape");
                currentBlendshape = newCurrentBlendshape;
                MarkEditorStateDirty();
            }

            int newCurrentDNA = EditorGUILayout.Popup("Select DNA", currentDNA, dnaNames);
            if (newCurrentDNA != currentDNA)
            {
                RegisterUndoSnapshot("Select Blendshape DNA");
                currentDNA = newCurrentDNA;
                MarkEditorStateDirty();
            }

            GUIHelper.BeginVerticalPadded();
            GUILayout.Label("Select Slots to extract blendshapes",centeredLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                RegisterUndoSnapshot("Select All Blendshape Slots");
                for (int i = 0; i < blendShapeSlotSelected.Count; i++)
                {
                    blendShapeSlotSelected[i] = true;
                }
                MarkEditorStateDirty();
            }
            if (GUILayout.Button("Clear Selection"))
            {
                RegisterUndoSnapshot("Clear Blendshape Slot Selection");
                for (int i = 0; i < blendShapeSlotSelected.Count; i++)
                {
                    blendShapeSlotSelected[i] = false;
                }
                MarkEditorStateDirty();
            }
            GUILayout.EndHorizontal();
            for (int i = 0; i < blendShapeSlots.Count; i++)
            {
                bool newSelectedState = EditorGUILayout.ToggleLeft(blendShapeSlots[i], blendShapeSlotSelected[i]);
                if (newSelectedState != blendShapeSlotSelected[i])
                {
                    RegisterUndoSnapshot("Toggle Blendshape Slot");
                    blendShapeSlotSelected[i] = newSelectedState;
                    MarkEditorStateDirty();
                }
            }
            GUIHelper.EndVerticalPadded();
            if (GUILayout.Button("Extract Blendshapes"))
            {
                RegisterUndoSnapshot("Extract Blendshapes");
                ExtractBlendshapes(strBlendShapes[currentBlendshape],dnaNames[currentDNA],blendShapeSlotSelected,blendShapeSlots);
                MarkEditorStateDirty();
            }
            GUIHelper.BeginVerticalPadded();
            foreach(var mod in Modifiers)
            {
                if (mod == null || mod.TemplateAdjustment == null)
                {
                    continue;
                }
                if (mod.TemplateAdjustment.GetType() == typeof(VertexBlendshapeAdjustment))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Blendshape: {mod.ModifierName} Slot: {mod.SlotName}");
                    if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        RegisterUndoSnapshot("Remove Extracted Blendshape Modifier");
                        Modifiers.Remove(mod);
                        MarkEditorStateDirty();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUIHelper.EndVerticalPadded();
        }

        private void ExtractBlendshapes(string blendShapeName, string dnaName, List<bool> selected, List<string> slots)
        {
            foreach (var strSlot in slots)
            {
                if (!selected[slots.IndexOf(strSlot)])
                {
                    continue;
                }

                SlotData sd = thisDCA.umaData.umaRecipe.GetSlot(strSlot);
                if (sd == null)
                {
                    sd = FindSlotByModifierSlotKey(strSlot);
                }
                if (sd == null)
                {
                    // ??
                    continue;
                }
                if (sd.asset.meshData.blendShapes == null)
                {
                    continue;
                }
                if (sd.asset.meshData.blendShapes.Length == 0)
                {
                    continue;
                }
                UMABlendShape foundShape = null;

                foreach (var bs in sd.asset.meshData.blendShapes)
                {
                    if (bs.shapeName == blendShapeName)
                    {
                        foundShape = bs;
                        break;
                    }
                }
                if (foundShape == null)
                {
                    continue;
                }

                // found the blendshape for this slot.
                // if a blendShapeModifier for this already exists, delete it.
                for (int i = 0; i < Modifiers.Count; i++)
                {
                    if (Modifiers[i].ModifierName == blendShapeName && Modifiers[i].SlotName == strSlot)
                    {
                        Modifiers.RemoveAt(i);
                        break;
                    }
                }


                int maxFrame = foundShape.frames.Length - 1;
                UMABlendFrame frame = foundShape.frames[maxFrame];
                if (frame != null)
                {
                    // create the new modifier
                    MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                    newMod.ModifierName = blendShapeName;
                    newMod.DNAName = dnaName;
                    newMod.Scale = 1.0f;
                    newMod.SlotName = NormalizeModifierSlotKey(strSlot);
                    newMod.keepAsIs = true;
                    newMod.adjustments = new VertexBlendshapeAdjustmentCollection();
                    newMod.TemplateAdjustment = new VertexBlendshapeAdjustment();
                    for (int i = 0; i < frame.deltaVertices.Length; i++)
                    {
                        if (frame.deltaVertices[i] != Vector3.zero)
                        {
                            VertexBlendshapeAdjustment vba = new VertexBlendshapeAdjustment();
                            vba.vertexIndex = i;
                            vba.slotName = NormalizeModifierSlotKey(strSlot);
                            vba.vertexIndex = i;
                            vba.delta = frame.deltaVertices[i];

                            if (frame.HasTangents())
                            {
                                vba.tangent = frame.deltaTangents[i];
                            }
                            else
                            {
                                vba.tangent = Vector3.zero;
                            }
                            if (frame.HasNormals())
                            {
                                vba.normal = frame.deltaNormals[i];
                            }
                            else
                            {
                                vba.normal = Vector3.zero;
                            }
                            newMod.adjustments.Add(vba);
                        }
                    }

                    Modifiers.Add(newMod);
                    currentModifierIndex = Modifiers.Count - 1;
                }
            }
        }


        private void DrawAdHocAdjustments()
        {
            int activeCount = 0;
            bool allowAdd = true;

            if (vertexEditorStage.CurrentSelected < 0 && allowAdd)
            {
                //Debug.Log("No vertexes selected. CurrentSelect <= 0");
                EditorGUILayout.LabelField("No Current Vertex", centeredLabel);
                EditorGUILayout.HelpBox("Please click one of the selected vertexes in the scene view to edit it. The vertex can be active or inactive.", MessageType.Info);
                allowAdd = false;
                //return;
            }
            
            VertexEditorStage.VertexSelection selectedVertex = vertexEditorStage.GetSelectedVertex();
            if (selectedVertex == null && allowAdd)
            {
                EditorGUILayout.LabelField("No Current Vertex", centeredLabel);
                EditorGUILayout.HelpBox("Please click one of the selected vertexes in the scene view to edit it. The vertex can be active or inactive.", MessageType.Info);
                //return;
                allowAdd = false;
            }
            if (selectedVertex != null && selectedVertex.suppressed && allowAdd)
            {
                EditorGUILayout.LabelField("Vertex is suppressed", centeredLabel);
                EditorGUILayout.HelpBox("This vertex is suppressed and cannot be edited. A vertex is suppressed when the slot it is on is hidden.", MessageType.Info);
                allowAdd = false;
                // return;
            }

            /* GUILayout.BeginHorizontal();
              if (GUILayout.Button("Show All", (showFiltered == false? unselectedButton:selectedButton)))
              {
                  showFiltered = false;
                  Repaint();
              }
              if (GUILayout.Button("Show Filtered", (showFiltered == true ? unselectedButton : selectedButton)))
              {
                  showFiltered = true;
                  Repaint();
              }

              GUILayout.EndHorizontal();
            */
            if (allowAdd)
            {
                EditorGUILayout.LabelField("Add Vertex Modifier", centeredLabel);

                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Type", GUILayout.Width(60));
                int newSelectedType = EditorGUILayout.Popup(selectedType, ModifierTypeNames, GUILayout.Width(180));
                if (newSelectedType != selectedType)
                {
                    RegisterUndoSnapshot("Change Adjustment Type");
                    selectedType = newSelectedType;
                    MarkEditorStateDirty();
                }
                if (selectedType >= 0)
                {
                    if (templateCollection == null || templateCollection.GetType() != ModifierTypes[selectedType])
                    {
                        templateCollection = (VertexAdjustmentCollection)Activator.CreateInstance(ModifierTypes[selectedType]);
                    }
                }

                if (templateVertexAdjustmentCollection == null || ModifierTypes[selectedType] != templateVertexAdjustmentCollection.GetType() && selectedType < ModifierTypes.Length)
                {
                    templateVertexAdjustmentCollection = (VertexAdjustmentCollection)Activator.CreateInstance(ModifierTypes[selectedType]);
                }
                if (GUILayout.Button("Add"))
                {
                    RegisterUndoSnapshot("Add Vertex Adjustment");
                    VertexAdjustment va = CreateVertexAdjustment(selectedVertex, templateVertexAdjustmentCollection);
                    int newSize = vertexEditorStage.GetVertexAdjustments().Count + 1;
                    FoldOuts = ExpandList(FoldOuts, newSize);
                    SetExpanded(FoldOuts, newSize - 1);
                    vertexEditorStage.AddVertexAdjustment(va);
                    SetActive(va);
                    MarkEditorStateDirty();
                }
                GUILayout.EndHorizontal();
                if (templateCollection != null)
                {
                    EditorGUILayout.HelpBox(templateCollection.Help, MessageType.Info);
                }

                GUIHelper.EndVerticalPadded(10);
            }

            GUILayout.Label("Adjustments", centeredLabel);

            vertexScrollPos = EditorGUILayout.BeginScrollView(vertexScrollPos);

            VertexAdjustment RemoveMe = null;
            int pos = 0;
            var adjustments = vertexEditorStage.GetVertexAdjustments();
            //FoldOuts = ExpandList(FoldOuts, adjustments.Count);

            if (activeAdjustment != null)
            {
                ShowActiveAdjustment(activeCount, activeAdjustment);
            }
            else
            {

            }

            foreach (VertexAdjustment va in adjustments)
            {
                //if (showFiltered && (selectedVertex.vertexIndexOnSlot != va.vertexIndex || selectedVertex.slot.slotName != va.slotName))
                //{
                //    continue;
                //}

                bool delme = false;
                //FoldOuts[pos] = GUIHelper.FoldoutBarWithDelete(FoldOuts[pos], $"{va.slotName},{va.vertexIndex},{va.Name}", out delme);
                GUILayout.BeginHorizontal();
                if (va == activeAdjustment)
                {
                    GUILayout.Label("(edit)", GUILayout.Width(64));
                }
                else
                {
                    GUILayout.Label(" ", GUILayout.Width(64));
                }
                if(GUILayout.Button($"{va.slotName},{va.vertexIndex},{va.Name}", EditorStyles.miniButtonMid, GUILayout.ExpandWidth(true)))
                {
                    SetActive(va, true, true);
                }
                delme = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                GUILayout.EndHorizontal();

                if (delme)
                {
                    RemoveMe = va;
                    if (activeAdjustment == va)
                    {
                        SetActive(va,false);
                    }

                }

                //if (FoldOuts[pos])
                //{
                //    activeCount = ShowActiveAdjustment(activeCount, va);
                //}
                pos++;
            }
            //if (activeCount == 0)
            //{
            //    vertexEditorStage.SetActive(null);
            //}
            if (RemoveMe != null)
            {
                RegisterUndoSnapshot("Remove Vertex Adjustment");
                vertexEditorStage.RemoveVertexAdjustment(RemoveMe);
                if (RebuildOnChanges)
                {
                    DoCharacterRebuild();
                    //DoCharacterRebuildWithUpdates();
                }
                MarkEditorStateDirty(true);
            }
            EditorGUILayout.EndScrollView();
        }

        private VertexAdjustment CreateVertexAdjustment(VertexEditorStage.VertexSelection selectedVertex, VertexAdjustmentCollection collection)
        {
            VertexAdjustment va = collection.Create();
            va.vertexIndex = selectedVertex.vertexIndexOnSlot;
            va.slotName = GetModifierSlotKey(selectedVertex.slot);
            va.active = true;
            va.Init(selectedVertex.slot.asset.meshData);
            return va;
        }


        public List<MeshModifier.Modifier> DoModifierSplit(bool useBuildOptions)
        {
            bool includeBulk = useBuildOptions ? IncludeBulkModifiers : true;
            bool includeAdHoc = useBuildOptions ? IncludeAdHocAdjustments : true;
            bool onlyActiveBulk = useBuildOptions && IncludeActiveOnlyBulk;

            List<MeshModifier.Modifier> sourceModifiers = BuildEditorModifierSnapshot(includeBulk, includeAdHoc, onlyActiveBulk);
            List<MeshModifier.Modifier> result = SplitModifierStacksBySlot(sourceModifiers);

            ValidateSplitAccounting(result, useBuildOptions);
            return result; 
        }

        private List<VertexAdjustment> GetSourceAdjustmentsForSplit(bool useBuildOptions)
        {
            bool includeBulk = useBuildOptions ? IncludeBulkModifiers : true;
            bool includeAdHoc = useBuildOptions ? IncludeAdHocAdjustments : true;
            bool onlyActiveBulk = useBuildOptions && IncludeActiveOnlyBulk;

            List<MeshModifier.Modifier> sourceStacks = BuildEditorModifierSnapshot(includeBulk, includeAdHoc, onlyActiveBulk);
            List<VertexAdjustment> sourceAdjustments = new List<VertexAdjustment>();
            foreach (MeshModifier.Modifier stack in sourceStacks)
            {
                AddModifierAdjustments(sourceAdjustments, stack);
            }
            return sourceAdjustments;
        }

        private static void AddModifierAdjustments(List<VertexAdjustment> buffer, MeshModifier.Modifier modifier)
        {
            if (buffer == null || modifier == null || modifier.adjustments == null || modifier.adjustments.vertexAdjustments == null)
            {
                return;
            }

            buffer.AddRange(modifier.adjustments.vertexAdjustments);
        }

        private static string BuildAdjustmentAccountingKey(VertexAdjustment adjustment)
        {
            return adjustment.slotName + "|" + adjustment.vertexIndex + "|" + adjustment.GetType().FullName;
        }

        private static Dictionary<string, int> BuildAdjustmentCountMap(List<VertexAdjustment> adjustments)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (adjustments == null)
            {
                return counts;
            }

            foreach (VertexAdjustment adjustment in adjustments)
            {
                if (adjustment == null || string.IsNullOrEmpty(adjustment.slotName))
                {
                    continue;
                }

                string key = BuildAdjustmentAccountingKey(adjustment);
                if (counts.ContainsKey(key))
                {
                    counts[key]++;
                }
                else
                {
                    counts.Add(key, 1);
                }
            }
            return counts;
        }

        private void ValidateSplitAccounting(List<MeshModifier.Modifier> result, bool useBuildOptions)
        {
            List<VertexAdjustment> sourceAdjustments = GetSourceAdjustmentsForSplit(useBuildOptions);
            List<VertexAdjustment> resultAdjustments = new List<VertexAdjustment>();
            if (result != null)
            {
                foreach (MeshModifier.Modifier modifier in result)
                {
                    AddModifierAdjustments(resultAdjustments, modifier);
                }
            }

            Dictionary<string, int> sourceCounts = BuildAdjustmentCountMap(sourceAdjustments);
            Dictionary<string, int> resultCounts = BuildAdjustmentCountMap(resultAdjustments);

            int sourceTotal = 0;
            foreach (var kvp in sourceCounts)
            {
                sourceTotal += kvp.Value;
            }

            int resultTotal = 0;
            foreach (var kvp in resultCounts)
            {
                resultTotal += kvp.Value;
            }

            if (sourceTotal != resultTotal)
            {
                Debug.LogError($"MeshModifier split validation mismatch. Source adjustments: {sourceTotal}, Result adjustments: {resultTotal}");
            }

            foreach (var kvp in sourceCounts)
            {
                int resultCount = resultCounts.ContainsKey(kvp.Key) ? resultCounts[kvp.Key] : 0;
                if (resultCount != kvp.Value)
                {
                    Debug.LogError($"MeshModifier split lost or duplicated adjustments for key '{kvp.Key}'. Source: {kvp.Value}, Result: {resultCount}");
                }
            }

            foreach (var kvp in resultCounts)
            {
                if (!sourceCounts.ContainsKey(kvp.Key))
                {
                    Debug.LogError($"MeshModifier split produced unexpected adjustment key '{kvp.Key}' ({kvp.Value} entries)");
                }
            }
        }

#if UMA_BURSTCOMPILE
        [BurstCompile(CompileSynchronously = true)]
#endif
        public void DoCharacterRebuild(bool forceTPose = false, bool buildCollisionMesh=true, bool LoadMeshModifiers = true)
        {
            if (forceTPose)
            {
                thisDCA.GetComponent<Animator>().enabled = false;
                thisDCA.KeepAnimatorController = true;
                /*thisDCA.activeRace.data.FixupRotations = false;
                Transform rootTransform = thisDCA.umaData.skeleton.GetRootTransform();
                Transform globalTransform = thisDCA.umaData.skeleton.GetGlobalTransform();

                globalTransform.localRotation = Quaternion.identity;
                rootTransform.localRotation = Quaternion.identity; */
            }
            else
            {
                thisDCA.GetComponent<Animator>().enabled = wasAnimatorEnabled;
                thisDCA.KeepAnimatorController = wasKeepAnimator;
                /*thisDCA.activeRace.data.FixupRotations = wasRaceFixup;
                Transform rootTransform = thisDCA.umaData.skeleton.GetRootTransform();
                Transform globalTransform = thisDCA.umaData.skeleton.GetGlobalTransform();
                globalTransform.localRotation = wasGlobalRotation;
                rootTransform.localRotation = wasRootRotation;*/
            }
            thisDCA.umaData.ManualMeshModifiers = new List<MeshModifier.Modifier>();
            if (LoadMeshModifiers)
            {
                thisDCA.umaData.ManualMeshModifiers = DoModifierSplit(true);
            }
            vertexEditorStage.RebuildMesh(forceTPose,buildCollisionMesh);
        }

        public void DoCharacterReset()
        {
            thisDCA.umaData.ManualMeshModifiers = new List<MeshModifier.Modifier>();
            vertexEditorStage.RebuildMesh(false);
        }

        /*
        public void DoCharacterRebuildWithUpdates()
        {
            Dictionary<string, MeshModifier.Modifier> testModifiers = new Dictionary<string, MeshModifier.Modifier>();

            foreach (VertexAdjustment va in vertexEditorStage.GetVertexAdjustments())
            {
                string key = va.Name + ":" + va.slotName;
                if (!testModifiers.ContainsKey(key))
                {
                    MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                    newMod.adjustments = va.VertexAdjustmentCollection;
                    newMod.SlotName = va.slotName;
                    newMod.ModifierName = va.Name;
                    testModifiers.Add(key, newMod);
                }
                testModifiers[key].adjustments.Add(va);
            }

            List<MeshModifier.Modifier> NewMods = new List<MeshModifier.Modifier>();
            // convert dictionary to a list of modifiers
            foreach (KeyValuePair<string, MeshModifier.Modifier> kvp in testModifiers)
            {
                NewMods.Add(kvp.Value);
            }

            thisDCA.umaData.manualMeshModifiers = NewMods;
            vertexEditorStage.RebuildMesh(false);
        }

        public void DoCharacterRebuildWithCurrentBulkModifiers()
        {
            thisDCA.umaData.manualMeshModifiers = new List<MeshModifier.Modifier>();
            foreach(MeshModifier.Modifier mod in Modifiers)
            {
                SplitModifiersBySlot(thisDCA.umaData.manualMeshModifiers, mod);
            }
            vertexEditorStage.RebuildMesh(false);
        } */

#if UMA_BURSTCOMPILE
        [BurstCompile(CompileSynchronously = true)]
#endif
        public void SplitModifiersBySlot(List<MeshModifier.Modifier> target, MeshModifier.Modifier activeModifier)
        {
            if (target == null || activeModifier == null)
            {
                return;
            }

            if (activeModifier.keepAsIs)
            {
                // No need to split, just add.
                target.Add(activeModifier);
                return;
            }

            if (activeModifier.adjustments == null || activeModifier.adjustments.vertexAdjustments == null)
            {
                return;
            }

            foreach (VertexAdjustment va in activeModifier.adjustments.vertexAdjustments)
            {
                if (va == null || string.IsNullOrEmpty(va.slotName))
                {
                    continue;
                }

                string key = NormalizeModifierSlotKey(va.slotName);
                va.slotName = key;
                MeshModifier.Modifier newMod = null;
                foreach (MeshModifier.Modifier mod in target)
                {
                    if (mod == null || mod.keepAsIs)
                    {
                        continue;
                    }

                    if (mod.adjustments == null)
                    {
                        continue;
                    }

                    Type modType = mod.adjustments.AdjustmentType;

                    if (mod.SlotName == key && modType == va.GetType())
                    {
                        bool sameDNA = mod.DNAName == activeModifier.DNAName;
                        bool sameScale = Mathf.Approximately(mod.Scale, activeModifier.Scale);
                        if (sameDNA && sameScale)
                        {
                            newMod = mod;
                            break;
                        }
                    }
                }
                if (newMod == null)
                {
                    newMod = new MeshModifier.Modifier();
                    newMod.keepAsIs = false;
                    newMod.SlotName = key;
                    newMod.ModifierName = activeModifier.ModifierName;
                    newMod.DNAName = activeModifier.DNAName;
                    newMod.Scale = activeModifier.Scale;
                    newMod.adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(activeModifier.adjustments.GetType());
                    target.Add(newMod);
                }
                Debug.Log($"Adding adjustment for slot {va.slotName} to modifier {newMod.ModifierName}");
                newMod.adjustments.Add(va);
            }
        }

        public void DoCharacterRebuildWithActiveBulkModifier(MeshModifier.Modifier activeModifier)
        {
            thisDCA.umaData.ManualMeshModifiers = new List<MeshModifier.Modifier>();
            SplitModifiersBySlot(thisDCA.umaData.ManualMeshModifiers, activeModifier);
            vertexEditorStage.RebuildMesh(false);
        }

        private int ShowActiveAdjustment(int activeCount, VertexAdjustment va)
        {
            if (va.active)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.9f, 0.9f, 1f));
                GUILayout.Label("Editor Active", centeredLabel);
                SetActive(va, true);
                activeCount++;
            }
            else
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.3f, 0.3f, 0.4f));
            }

            HandleInteractiveUndoCapture(ref activeAdjustmentInteractiveUndoArmed, "Edit Vertex Adjustment");

            if (va.DoGUI())
            {
                if (RebuildOnChanges)
                {
                    DoCharacterRebuild();
                }
            }
            if (va.Gizmo != VertexAdjustmentGizmo.None)
            {
                if (va.active)
                {
                    if (GUILayout.Button("Stop Editing"))
                    {
                        SetActive(va, false);
                    }
                }
                else
                {

                    if (GUILayout.Button("Edit in scene"))
                    {
                        SetActive(va);
                    }
                }
            }
            else
            {
                GUILayout.Label("No gizmo for this adjustment");
            }
            GUIHelper.EndVerticalPadded(10);
            return activeCount;
        }

        private void deActivateCurrentSelection()
        {
            VertexEditorStage.VertexSelection vs = vertexEditorStage.GetSelectedVertex();
            foreach (VertexAdjustment va in vertexEditorStage.GetVertexAdjustments())
            {
                if (va == null || vs == null || vs.slot == null)
                {
                    continue;
                }
                if (va.slotName == GetModifierSlotKey(vs.slot) && va.vertexIndex == vs.vertexIndexOnSlot)
                {
                    va.active = false;
                }
            }
        }


 
        public string FindNameForModifier(string typeName)
        {
            int maxNumber = 1;
            foreach (MeshModifier.Modifier mod in Modifiers)
            {
                if (mod.TemplateAdjustment.Name == typeName)
                {
                    string name = mod.ModifierName;
                    if (name.StartsWith(typeName))
                    {
                        string number = name.Substring(typeName.Length);
                        number = number.Replace("(", "");
                        number = number.Replace(")", "");

                        int num = 0;
                        if (int.TryParse(number, out num))
                        {
                            if (num >= maxNumber)
                            {
                                maxNumber = num+1;
                            }
                        }
                    }
                }
            }
            return $"{typeName} ({maxNumber})";
        }

        private void AddActiveVertexesToCollection(MeshModifier.Modifier meshModifier)
        {
            var SelectedVertexes = vertexEditorStage.GetActiveSelectedVertexes();

            foreach (var se in SelectedVertexes)
            {
                VertexAdjustment va = CreateVertexAdjustment(se, meshModifier.adjustments);
                meshModifier.adjustments.Add(va);
            }
        }
        private void AddAllVertexesToCollection(MeshModifier.Modifier meshModifier)
        {
            vertexEditorStage.SelectAll();
            var SelectedVertexes = vertexEditorStage.GetVertexSelections();

            foreach (var se in SelectedVertexes)
            {
                VertexAdjustment va = CreateVertexAdjustment(se, meshModifier.adjustments);
                meshModifier.adjustments.Add(va);
            }
        }

        private bool ContainsVertex(MeshModifier.Modifier modifier, string slotKey, int vertexIndex)
        {
            if (modifier == null || modifier.adjustments == null || modifier.adjustments.vertexAdjustments == null)
            {
                return false;
            }

            string normalizedSlot = NormalizeModifierSlotKey(slotKey);
            for (int i = 0; i < modifier.adjustments.vertexAdjustments.Count; i++)
            {
                VertexAdjustment existing = modifier.adjustments.vertexAdjustments[i];
                if (existing == null)
                {
                    continue;
                }

                if (existing.vertexIndex == vertexIndex && string.Equals(existing.slotName, normalizedSlot, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void EditCurrentModifier(MeshModifier.Modifier modifier)
        {
            if (modifier == null || modifier.adjustments == null || modifier.adjustments.vertexAdjustments == null || vertexEditorStage == null)
            {
                return;
            }

            List<VertexEditorStage.VertexSelection> selections = new List<VertexEditorStage.VertexSelection>();
            for (int i = 0; i < modifier.adjustments.vertexAdjustments.Count; i++)
            {
                VertexAdjustment adjustment = modifier.adjustments.vertexAdjustments[i];
                if (adjustment == null || string.IsNullOrEmpty(adjustment.slotName))
                {
                    continue;
                }

                SlotData slot = FindSlotByModifierSlotKey(adjustment.slotName);
                if (slot == null)
                {
                    continue;
                }

                selections.Add(new VertexEditorStage.VertexSelection()
                {
                    slot = slot,
                    vertexIndexOnSlot = adjustment.vertexIndex,
                    WorldPosition = vertexEditorStage.GetWorldPosition(slot, adjustment.vertexIndex),
                    isActive = true,
                    suppressed = false
                });
            }

            vertexEditorStage.SetVertexSelections(selections);
            SceneView.RepaintAll();
            FocusCurrentModifier(modifier);
        }

        private void FocusCurrentModifier(MeshModifier.Modifier modifier)
        {
            if (modifier == null || modifier.adjustments == null || modifier.adjustments.vertexAdjustments == null || vertexEditorStage == null)
            {
                return;
            }

            SceneView sceneView = vertexEditorStage.openedSceneView;
            if (sceneView == null)
            {
                sceneView = SceneView.lastActiveSceneView;
            }
            if (sceneView == null)
            {
                return;
            }

            Vector3 centroid = Vector3.zero;
            int count = 0;
            float maxDistance = 0f;
            List<Vector3> positions = new List<Vector3>();

            for (int i = 0; i < modifier.adjustments.vertexAdjustments.Count; i++)
            {
                VertexAdjustment adjustment = modifier.adjustments.vertexAdjustments[i];
                if (adjustment == null || string.IsNullOrEmpty(adjustment.slotName))
                {
                    continue;
                }

                SlotData slot = FindSlotByModifierSlotKey(adjustment.slotName);
                if (slot == null)
                {
                    continue;
                }

                Vector3 worldPosition = vertexEditorStage.GetWorldPosition(slot, adjustment.vertexIndex);
                positions.Add(worldPosition);
                centroid += worldPosition;
                count++;
            }

            if (count == 0)
            {
                return;
            }

            centroid /= count;
            for (int i = 0; i < positions.Count; i++)
            {
                float distance = Vector3.Distance(centroid, positions[i]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                }
            }

            sceneView.pivot = centroid;
            if (maxDistance > 0f)
            {
                sceneView.size = Mathf.Max(maxDistance * 2.5f, 0.05f);
            }
            sceneView.Repaint();
            SceneView.RepaintAll();
        }

        private int AddActiveVerticesToCurrentModifier(MeshModifier.Modifier modifier)
        {
            if (modifier == null || modifier.adjustments == null || vertexEditorStage == null)
            {
                return 0;
            }

            int added = 0;
            List<VertexEditorStage.VertexSelection> selectedVertexes = vertexEditorStage.GetActiveSelectedVertexes();
            for (int i = 0; i < selectedVertexes.Count; i++)
            {
                VertexEditorStage.VertexSelection selection = selectedVertexes[i];
                if (selection == null || selection.slot == null)
                {
                    continue;
                }

                string slotKey = GetModifierSlotKey(selection.slot);
                if (ContainsVertex(modifier, slotKey, selection.vertexIndexOnSlot))
                {
                    continue;
                }

                VertexAdjustment adjustment = CreateVertexAdjustment(selection, modifier.adjustments);
                if (modifier.TemplateAdjustment != null)
                {
                    adjustment.CopyFrom(modifier.TemplateAdjustment);
                }
                adjustment.active = false;
                modifier.adjustments.Add(adjustment);
                added++;
            }

            return added;
        }

        private void ClearCurrentModifier(MeshModifier.Modifier modifier)
        {
            if (modifier == null || modifier.adjustments == null || modifier.adjustments.vertexAdjustments == null)
            {
                return;
            }

            modifier.adjustments.vertexAdjustments.Clear();
        }


        private Vector2 ModifierScrollPos = Vector2.zero;

        private void OpenModifierInspector(int modifierIndex)
        {
            ModifierInspectWindow.ShowWindow(this, modifierIndex);
        }

        private void DrawMeshModifiers()
        {
            EditorGUILayout.LabelField("Mesh Modifiers", centeredLabel);
            EditorGUILayout.HelpBox("Recalculate normals to modifier will create a normal rotation modifier from the current normals and tangents to the recalculate normals and tangents. You should run this before doing any mesh modifications.", MessageType.Info);
            if (GUILayout.Button("Recalculate Normals to Reset Modifier"))
            {
                RegisterUndoSnapshot("Create Reset Normals Modifier", true);
                DoCharacterRebuild(true, false, false);
                // Get normals from "fresh" mesh.
                // Now recalculate normals and tangents.
                // then get the new normals.
                // go through the normals, and extract the rotation from/to.
                List<Vector3> oldNormals = new List<Vector3>(vertexEditorStage.BakedMesh.normals);
                vertexEditorStage.RecalculateNormals();
                List<Vector3> newNormals = new List<Vector3>(vertexEditorStage.BakedMesh.normals);



                var saveSelections = new List<VertexEditorStage.VertexSelection>();
                saveSelections.AddRange(vertexEditorStage.GetVertexSelections());
                MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                newMod.EditorInitialize(typeof(VertexNormalAdjustmentCollection));

                // newMod.EditorInitialize(typeof(VertexResetAdjustmentCollection));
                newMod.ModifierName = FindNameForModifier("Extracted recalculated normals");
                Modifiers.Add(newMod);
                newMod.adjustments = new VertexNormalAdjustmentCollection();
                // newMod.adjustments = new VertexResetAdjustmentCollection();
                AddAllVertexesToCollection(newMod);

                VertexNormalAdjustmentCollection theCollection = (VertexNormalAdjustmentCollection)newMod.adjustments;
                for (int i = 0; i < theCollection.vertexAdjustments.Count; i++)
                {
                    SlotData slot = thisDCA.umaData.umaRecipe.GetSlot(theCollection.vertexAdjustments[i].slotName);
                    if (slot == null)
                    {
                        slot = FindSlotByModifierSlotKey(theCollection.vertexAdjustments[i].slotName);
                    }
                    if (slot == null)
                    {
                        continue;
                    }

                    if (!vertexEditorStage.TryGetVisibleBakedVertexIndex(slot, theCollection.vertexAdjustments[i].vertexIndex, out int vertPos))
                    {
                        continue;
                    }

                    VertexNormalAdjustment var = theCollection.vertexAdjustments[i] as VertexNormalAdjustment;
                    var.rotation = Quaternion.FromToRotation(oldNormals[vertPos], newNormals[vertPos]);
                    //var.initialNormal = normals[vertPos];
                    //var.initialTangent = tangents[vertPos];
                }
                currentModifierIndex = Modifiers.Count - 1;
                ModifierScrollPos.y = 100000;
                vertexEditorStage.SetVertexSelections(saveSelections);
                MarkEditorStateDirty(true);
            }

            EditorGUILayout.LabelField("Extract Bulk Modifier of Type:");
            int newSelectedType = EditorGUILayout.Popup(selectedType, ModifierTypeNames);
            if (newSelectedType != selectedType)
            {
                RegisterUndoSnapshot("Change Bulk Modifier Type");
                selectedType = newSelectedType;
                MarkEditorStateDirty();
            }

            int activeCount = vertexEditorStage.GetActiveSelectedVertexCount();
            if (activeCount == 0)
            {
                EditorGUILayout.LabelField("No vertexes selected", centeredLabel);
                EditorGUILayout.HelpBox("Please selectvertexes For Bulk Modifier", MessageType.Info);
            }
            else
            {
                if (GUILayout.Button("Add Collection for selected vertexes"))
                {
                    RegisterUndoSnapshot("Add Bulk Modifier Collection");
                    MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                    newMod.EditorInitialize(ModifierTypes[selectedType]);
                    newMod.ModifierName = FindNameForModifier(newMod.TemplateAdjustment.Name);
                    Modifiers.Add(newMod);
                    AddActiveVertexesToCollection(newMod);
                    currentModifierIndex = Modifiers.Count - 1;
                    ModifierScrollPos.y = 100000;
                    MarkEditorStateDirty();
                }
            }
            if (currentModifierIndex < 0 || currentModifierIndex >= Modifiers.Count)
            {
                return;
            }

            MeshModifier.Modifier currentModifier = Modifiers[currentModifierIndex];
            if (currentModifier == null)
            {
                return;
            }

            // Ensure template state is initialized for the CURRENT modifier, without overwriting its saved adjustments.
            // The previous logic used the global `selectedType`, which can differ from the modifier's actual collection type.
            // That could result in EditorInitialize creating a new collection and implicitly losing the loaded vertices.
            if (currentModifier.adjustments == null)
            {
                // No collection loaded; initialize from the currently selected type as a fallback.
                if (selectedType < 0 || selectedType >= ModifierTypes.Length)
                {
                    return;
                }
                currentModifier.EditorInitialize(ModifierTypes[selectedType]);
            }

            if (currentModifier.TemplateAdjustment == null)
            {
                Type adjustmentType = currentModifier.adjustments.AdjustmentType;
                currentModifier.TemplateAdjustment = adjustmentType != null
                    ? (VertexAdjustment)Activator.CreateInstance(adjustmentType)
                    : null;
            }

            // Keep the UI "Extract Bulk Modifier of Type" dropdown aligned with the current modifier's collection type.
            if (currentModifier.adjustments != null)
            {
                Type currentType = currentModifier.adjustments.GetType();
                for (int t = 0; t < ModifierTypes.Length; t++)
                {
                    if (ModifierTypes[t] == currentType)
                    {
                        selectedType = t;
                        break;
                    }
                }
            }

            if (currentModifier.TemplateAdjustment == null)
            {
                EditorGUILayout.HelpBox("Unable to initialize template adjustment for this modifier.", MessageType.Warning);
                return;
            }
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField($"{currentModifier.TemplateAdjustment.Name} {currentModifier.ModifierName}" , centeredLabel);
            EditorGUILayout.LabelField($"{currentModifier.adjustments.vertexAdjustments.Count} vertexes",centeredLabel);
            HandleInteractiveUndoCapture(ref bulkModifierInteractiveUndoArmed, "Edit Bulk Modifier");
            bool changed = currentModifier.TemplateAdjustment.DoGUI();
            // RebuildOnChanges = EditorGUILayout.Toggle("Rebuild on changes", RebuildOnChanges);

            if (changed)
            {
                // update all vertexes on the current modifier
                // with the new values.
                foreach (VertexAdjustment va in currentModifier.adjustments.vertexAdjustments)
                {
                    va.CopyFrom(currentModifier.TemplateAdjustment);
                }
                if (RebuildOnChanges)
                {
                    DoCharacterRebuildWithActiveBulkModifier(currentModifier);
                }
                MarkEditorStateDirty(true);
            }

          /*  if (GUILayout.Button("Rebuild with this adjustment"))
            {
                if (currentModifierIndex < Modifiers.Count)
                {
                    DoCharacterRebuildWithActiveBulkModifier(currentModifier);
                }
            }
            if (GUILayout.Button("Rebuild with all adjustments"))
            {
                DoCharacterRebuildWithCurrentBulkModifiers();
            } */

            GUIHelper.EndVerticalPadded(10);

            if (currentModifierIndex >= 0 && currentModifierIndex < Modifiers.Count)
            {
                // DrawCurrentModifier();
            }

            EditorGUILayout.LabelField("Mesh Modifier Collections", centeredLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(currentModifier == null || currentModifier.adjustments == null))
            {
                if (GUILayout.Button("Edit Current"))
                {
                    RegisterUndoSnapshot("Edit Current Modifier Vertices");
                    EditCurrentModifier(currentModifier);
                    MarkEditorStateDirty();
                }
                if (GUILayout.Button("Focus"))
                {
                    FocusCurrentModifier(currentModifier);
                }
                if (GUILayout.Button("Add to Current"))
                {
                    RegisterUndoSnapshot("Add Active Vertices To Current Modifier");
                    if (AddActiveVerticesToCurrentModifier(currentModifier) > 0)
                    {
                        MarkEditorStateDirty(true);
                        if (RebuildOnChanges)
                        {
                            DoCharacterRebuildWithActiveBulkModifier(currentModifier);
                        }
                    }
                }
                if (GUILayout.Button("Replace Current"))
                {
                    RegisterUndoSnapshot("Replace Current Modifier Vertices");
                    ClearCurrentModifier(currentModifier);
                    AddActiveVerticesToCurrentModifier(currentModifier);
                    MarkEditorStateDirty(true);
                    if (RebuildOnChanges)
                    {
                        DoCharacterRebuildWithActiveBulkModifier(currentModifier);
                    }
                }
                if (GUILayout.Button("Clear Current"))
                {
                    RegisterUndoSnapshot("Clear Current Modifier Vertices");
                    ClearCurrentModifier(currentModifier);
                    MarkEditorStateDirty(true);
                    if (RebuildOnChanges)
                    {
                        DoCharacterRebuildWithActiveBulkModifier(currentModifier);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            ModifierScrollPos = EditorGUILayout.BeginScrollView(ModifierScrollPos);
            int deleteMe = -1;
            for (int i = 0; i < Modifiers.Count; i++)
            {
                MeshModifier.Modifier mod = Modifiers[i];
                if (mod == null)
                {
                    GUILayout.Label("<Null>", GUILayout.Width(64));
                    continue;
                }
                if (mod.TemplateAdjustment == null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<Null Tmp>", GUILayout.Width(64));
                    GUILayout.Label(mod.ModifierName ?? "<Null Name>", EditorStyles.miniButtonMid, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        RegisterUndoSnapshot("Remove Bulk Modifier");
                        deleteMe = i;
                    }
                    GUILayout.EndHorizontal();
                    continue;
                }
                if (mod.ModifierName == null)
                {
                    mod.ModifierName = "Unnamed Modifier";
                }
                GUILayout.BeginHorizontal();
                if (i == currentModifierIndex)
                {
                    GUILayout.Label("(edit)", GUILayout.Width(64));
                }
                else
                {
                    GUILayout.Label(" ", GUILayout.Width(64));
                }

                    if (GUILayout.Button($"{mod.TemplateAdjustment.Name}:{mod.ModifierName}", EditorStyles.miniButtonMid, GUILayout.ExpandWidth(true)))
                    {
                    // todo: Transfer all of these vertexes to the current selection in the vertex editor stage
                    RegisterUndoSnapshot("Select Bulk Modifier");
                        currentModifierIndex = i;
                        MarkEditorStateDirty();
                        Repaint();
                    }
                if (GUILayout.Button("Inspect", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    OpenModifierInspector(i);
                }
                if(GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    RegisterUndoSnapshot("Remove Bulk Modifier");
                    deleteMe = i;
                }
                GUILayout.EndHorizontal();
                /*
                if (i != currentModifierIndex)
                {
                    //GUIHelper.BeginVerticalPadded(10, new Color(0.7f, 0.8f, 1f));
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Edit", GUILayout.Width(32)))
                    {
                        currentModifierIndex = i;
                    }
                    string type = mod.adjustments.GetType().Name;
                    EditorGUILayout.LabelField($"{i} - {mod.ModifierName}");
                    if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        deleteMe = i;
                    }
                    GUILayout.EndHorizontal();
                    // GUIHelper.EndVerticalPadded(10);
                }
                else
                {
                    DrawCurrentModifier(false);
                } */
            }
            EditorGUILayout.EndScrollView();
            if (deleteMe >= 0)
            {
                Modifiers.RemoveAt(deleteMe);
                if (currentModifierIndex >= Modifiers.Count)
                {
                    currentModifierIndex = Modifiers.Count - 1;
                }
                MarkEditorStateDirty();
            }
        }

        private void DrawCurrentModifier()
        {
            MeshModifier.Modifier mod = Modifiers[currentModifierIndex];
            GUIHelper.BeginVerticalPadded(10, backColor);
            GUILayout.Label("Type: "+ mod.TemplateAdjustment.Name, centeredLabel);
            GUILayout.Label("Vertex count: " + mod.adjustments.Count());
            mod.ModifierName = EditorGUILayout.TextField("Modifier Name", mod.ModifierName);
            mod.DNAName = EditorGUILayout.TextField("DNA Name", mod.DNAName);
            mod.Scale = EditorGUILayout.FloatField("Scale", mod.Scale);
             
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Vertexes"))
            {
                vertexEditorStage.SelectVertexes(mod.adjustments);
            }
            if (GUILayout.Button("Edit active"))
            {
                editingCurrent = true;
            }
            if (GUILayout.Button("Delete"))
            {
                Modifiers.RemoveAt(currentModifierIndex);
                if (currentModifierIndex >= Modifiers.Count)
                {
                    currentModifierIndex = Modifiers.Count - 1;
                }
            }
            GUILayout.EndHorizontal();
            // TODO: 
            // Add a way to add and remove vertex adjustments
            // Add a way to edit the vertex adjustments (display the active ones). 
            // Add a button to select all the adjusted vertexes on the character.
            // Add the ability to edit/filter to the "active" vertexes.
            // The currently selected vertex to edit should be selected and flashing or something on the character.
            GUIHelper.EndVerticalPadded(10);
        }

        private class ModifierInspectWindow : EditorWindow
        {
            private MeshModifierEditor owner;
            private int modifierIndex = -1;
            private Vector2 vertexScroll;
            private List<SlotData> addSlots = new List<SlotData>();
            private string[] addSlotNames = new string[0];
            private int addSlotIndex = 0;
            private int addVertexIndex = 0;

            public static void ShowWindow(MeshModifierEditor owner, int modifierIndex)
            {
                ModifierInspectWindow wnd = GetWindow<ModifierInspectWindow>(true, "Modifier Inspector", true);
                wnd.minSize = new Vector2(640, 420);
                wnd.Initialize(owner, modifierIndex);
                wnd.Show();
            }

            private void Initialize(MeshModifierEditor modifierEditor, int index)
            {
                owner = modifierEditor;
                modifierIndex = index;
                RefreshSlotCache();
            }

            private MeshModifier.Modifier GetModifier()
            {
                if (owner == null || owner.Modifiers == null)
                {
                    return null;
                }
                if (modifierIndex < 0 || modifierIndex >= owner.Modifiers.Count)
                {
                    return null;
                }
                return owner.Modifiers[modifierIndex];
            }

            private void RefreshSlotCache()
            {
                addSlots = new List<SlotData>();
                if (owner == null || owner.thisDCA == null || owner.thisDCA.umaData == null || owner.thisDCA.umaData.umaRecipe == null || owner.thisDCA.umaData.umaRecipe.slotDataList == null)
                {
                    addSlotNames = new string[0];
                    addSlotIndex = 0;
                    return;
                }

                foreach (var slot in owner.thisDCA.umaData.umaRecipe.slotDataList)
                {
                    if (slot != null && slot.asset != null && slot.asset.meshData != null)
                    {
                        string slotKey = owner.GetModifierSlotKey(slot);
                        bool exists = false;
                        for (int i = 0; i < addSlots.Count; i++)
                        {
                            if (owner.GetModifierSlotKey(addSlots[i]) == slotKey)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                        {
                            addSlots.Add(slot);
                        }
                    }
                }

                addSlotNames = new string[addSlots.Count];
                for (int i = 0; i < addSlots.Count; i++)
                {
                    addSlotNames[i] = owner.GetModifierSlotKey(addSlots[i]);
                }

                if (addSlotIndex >= addSlotNames.Length)
                {
                    addSlotIndex = Mathf.Max(0, addSlotNames.Length - 1);
                }
            }

            private bool ContainsVertex(MeshModifier.Modifier modifier, string slotName, int vertexIndex)
            {
                string normalizedSlot = owner.NormalizeModifierSlotKey(slotName);
                for (int i = 0; i < modifier.adjustments.vertexAdjustments.Count; i++)
                {
                    VertexAdjustment existing = modifier.adjustments.vertexAdjustments[i];
                    if (existing.slotName == normalizedSlot && existing.vertexIndex == vertexIndex)
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool TryAddVertex(MeshModifier.Modifier modifier, SlotData slot, int vertexIndex)
            {
                if (modifier == null || modifier.adjustments == null || slot == null || slot.asset == null || slot.asset.meshData == null)
                {
                    return false;
                }

                if (vertexIndex < 0 || vertexIndex >= slot.asset.meshData.vertexCount)
                {
                    return false;
                }

                string slotKey = owner.GetModifierSlotKey(slot);
                if (ContainsVertex(modifier, slotKey, vertexIndex))
                {
                    return false;
                }

                VertexAdjustment newAdjustment = modifier.adjustments.Create();
                newAdjustment.slotName = slotKey;
                newAdjustment.vertexIndex = vertexIndex;
                newAdjustment.active = false;
                newAdjustment.Init(slot.asset.meshData);

                if (modifier.TemplateAdjustment != null)
                {
                    newAdjustment.CopyFrom(modifier.TemplateAdjustment);
                }

                modifier.adjustments.Add(newAdjustment);
                return true;
            }

            private void OnGUI()
            {
                MeshModifier.Modifier modifier = GetModifier();
                if (modifier == null)
                {
                    EditorGUILayout.HelpBox("This modifier is no longer available.", MessageType.Info);
                    if (GUILayout.Button("Close"))
                    {
                        Close();
                    }
                    return;
                }

                EditorGUILayout.LabelField($"Modifier: {modifier.ModifierName}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Type: {(modifier.TemplateAdjustment != null ? modifier.TemplateAdjustment.Name : "Unknown")}");
                EditorGUILayout.LabelField($"Slot: {modifier.SlotName}");
                EditorGUILayout.LabelField($"DNA: {modifier.DNAName}");
                EditorGUILayout.LabelField($"Scale: {modifier.Scale}");
                EditorGUILayout.LabelField($"Keep As Is: {modifier.keepAsIs}");
                EditorGUILayout.LabelField($"Vertex Count: {(modifier.adjustments != null ? modifier.adjustments.vertexAdjustments.Count : 0)}");

                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Select Modifier Vertices"))
                {
                    owner.vertexEditorStage.SelectVertexes(modifier.adjustments);
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Add Active Scene Selection"))
                {
                    owner.RegisterUndoSnapshot("Inspect Add Active Vertices");
                    int added = 0;
                    var selected = owner.vertexEditorStage.GetActiveSelectedVertexes();
                    for (int i = 0; i < selected.Count; i++)
                    {
                        if (TryAddVertex(modifier, selected[i].slot, selected[i].vertexIndexOnSlot))
                        {
                            added++;
                        }
                    }

                    if (added > 0)
                    {
                        owner.MarkEditorStateDirty(true);
                        if (owner.RebuildOnChanges)
                        {
                            owner.DoCharacterRebuild();
                        }
                        owner.Repaint();
                        Repaint();
                    }
                }
                GUILayout.EndHorizontal();

                RefreshSlotCache();
                GUILayout.Space(4);
                GUILayout.Label("Add Vertex", EditorStyles.boldLabel);
                if (addSlotNames.Length == 0)
                {
                    EditorGUILayout.HelpBox("No valid slots found on this character.", MessageType.Warning);
                }
                else
                {
                    addSlotIndex = EditorGUILayout.Popup("Slot", addSlotIndex, addSlotNames);
                    addVertexIndex = EditorGUILayout.IntField("Vertex Index", addVertexIndex);
                    if (GUILayout.Button("Add Vertex"))
                    {
                        SlotData slot = addSlots[addSlotIndex];
                        owner.RegisterUndoSnapshot("Inspect Add Vertex");
                        if (TryAddVertex(modifier, slot, addVertexIndex))
                        {
                            owner.MarkEditorStateDirty(true);
                            if (owner.RebuildOnChanges)
                            {
                                owner.DoCharacterRebuild();
                            }
                            owner.Repaint();
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Add Vertex", "Could not add vertex. Verify slot, vertex index range, and duplicate state.", "OK");
                        }
                    }
                }

                GUILayout.Space(8);
                GUILayout.Label("Vertices", EditorStyles.boldLabel);
                vertexScroll = EditorGUILayout.BeginScrollView(vertexScroll);
                int removeIndex = -1;
                if (modifier.adjustments != null)
                {
                    for (int i = 0; i < modifier.adjustments.vertexAdjustments.Count; i++)
                    {
                        VertexAdjustment va = modifier.adjustments.vertexAdjustments[i];
                        if (va == null)
                        {
                            continue;
                        }

                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{i}: {va.slotName} / {va.vertexIndex}", GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            removeIndex = i;
                        }
                        GUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();

                if (removeIndex >= 0)
                {
                    owner.RegisterUndoSnapshot("Inspect Remove Vertex");
                    modifier.adjustments.vertexAdjustments.RemoveAt(removeIndex);
                    owner.MarkEditorStateDirty(true);
                    if (owner.RebuildOnChanges)
                    {
                        owner.DoCharacterRebuild();
                    }
                    owner.Repaint();
                }
            }
        }

        private void OnDestroy()
        {
            // if ModifierEditor is closed before the VertexEditorStage is closed.
            if (vertexEditorStage != null)
            {
               bool wasChanged = false;
                if (Modifiers != null)
                {
                    if (Modifiers.Count > 0)
                    {
                        wasChanged = true;
                    }
                    if (vertexEditorStage.Adjustments.Count > 0)
                    {
                        wasChanged = true;
                    }
                }
                if (wasChanged)
                {
                    if (EditorUtility.DisplayDialog("ModifierEditor Save Changes", "Do you want to save the changes you made to the modifiers?", "Yes", "No"))
                    {
                        SaveToAsset();
                    }
                } 
                vertexEditorStage.hasSaved = true;
                vertexEditorStage.CloseStage();
            }
        }
    }
}