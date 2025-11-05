using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using UMA.Editors;
using UMA.CharacterSystem;
using UMA; // Added for MeshModifier


namespace UMA.CharacterSystem.Editors
{
    [CustomEditor(typeof(DynamicCharacterAvatar), true)]
    public class DynamicCharacterAvatarEditor : Editor
    {
        public static bool showHelp = false;
        public static bool showWardrobe = false;
        public static bool showUtils = true; // JRRM set false before release
        public static bool showEditorCustomization = false; // set true before release
        public static bool showPrefinedDNA = false;
        public static bool showAnimatorGUI = false;
        public static bool showBlendshapes = false;
        public static bool showUMAFramework = false;
        public static bool showUMAData = false;
        public static bool showAdvanced = false;

        public static int currentcolorfilter =0;
        public string[] colorfilters = { "Base", "All", "Hide ColorDNA" };
        public List<string> baseColorNames = new List<string>();
        public int currentDNA =0;
        private string cachedRace = "";
        private string[] cachedRaceDNA = { };
        private string[] rawcachedRaceDNA = { };

        private MeshModifier MeshModifier = null;

        protected DynamicCharacterAvatar thisDCA;
        protected RaceSetterPropertyDrawer _racePropDrawer = new RaceSetterPropertyDrawer();
        protected WardrobeRecipeListPropertyDrawer _wardrobePropDrawer = new WardrobeRecipeListPropertyDrawer();
        protected RaceAnimatorListPropertyDrawer _animatorPropDrawer = new RaceAnimatorListPropertyDrawer();
        SerializedProperty animationController;
        protected Editor innerEditor;

        // Track any deferred OnEnable callback so it can be removed on cleanup
        private EditorApplication.CallbackFunction delayedEnableHandler;

        private static bool IsEditorBusy()
        {
            return EditorApplication.isCompiling || EditorApplication.isUpdating;
        }

        private void OnBeforeAssemblyReload()
        {
            // Ensure events and temporary editors are cleaned up before reload
            try
            {
                EditorApplication.update -= DoInspectors;
                SceneView.duringSceneGui -= DoSceneGUI;
                if (delayedEnableHandler != null)
                {
                    EditorApplication.delayCall -= delayedEnableHandler;
                    delayedEnableHandler = null;
                }
            }
            catch { }
            if (innerEditor != null)
            {
                try { DestroyImmediate(innerEditor); } catch { }
                innerEditor = null;
            }

            // Clear references to avoid leaking editor targets
            InspectMe.Clear();
            MeshModifier = null;
            if (_racePropDrawer != null)
            {
                _racePropDrawer.thisDCA = null;
            }

            if (_wardrobePropDrawer != null)
            {
                _wardrobePropDrawer.thisDCA = null;
            }

            if (_animatorPropDrawer != null)
            {
                _animatorPropDrawer.thisDCA = null;
            }

            thisDCA = null;
            animationController = null;
        }

        public void OnEnable()
        {
            if (IsEditorBusy() || target == null)
            {
                // Defer enable until editor is ready
                if (delayedEnableHandler == null)
                {
                    delayedEnableHandler = () =>
                    {
                        // Unsubscribe this handler to avoid multiple invocations
                        EditorApplication.delayCall -= delayedEnableHandler;
                        delayedEnableHandler = null;
                        if (this != null)
                        {
                            OnEnable();
                        }
                    };
                }
                // Ensure it's only added once
                EditorApplication.delayCall -= delayedEnableHandler;
                EditorApplication.delayCall += delayedEnableHandler;
                return;
            }

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            baseColorNames.Clear();
            baseColorNames.AddRange(new string[] { "skin", "hair", "eyes" });
            thisDCA = target as DynamicCharacterAvatar;

            if (thisDCA == null)
            {
                return;
            }

            innerEditor = (UMADataEditor)Editor.CreateEditor(thisDCA, typeof(UMADataEditor));
            _racePropDrawer.thisDCA = thisDCA;
            _wardrobePropDrawer.thisDCA = thisDCA;
            _animatorPropDrawer.thisDCA = thisDCA;

            SceneView.duringSceneGui += DoSceneGUI;
            EditorApplication.update += DoInspectors;
        }

        private List<UnityEngine.Object> InspectMe = new List<UnityEngine.Object>();

        public void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.update -= DoInspectors;
            SceneView.duringSceneGui -= DoSceneGUI;

            if (delayedEnableHandler != null)
            {
                EditorApplication.delayCall -= delayedEnableHandler;
                delayedEnableHandler = null;
            }

            if (innerEditor != null)
            {
                DestroyImmediate(innerEditor);
                innerEditor = null;
            }

            // Clear pending inspections and editor references
            InspectMe.Clear();
            MeshModifier = null;
            if (_racePropDrawer != null)
            {
                _racePropDrawer.thisDCA = null;
            }

            if (_wardrobePropDrawer != null)
            {
                _wardrobePropDrawer.thisDCA = null;
            }

            if (_animatorPropDrawer != null)
            {
                _animatorPropDrawer.thisDCA = null;
            }

            thisDCA = null;
            animationController = null;
        }

        private void DoInspectors()
        {
            if (InspectMe.Count >0)
            {
                for (int i =0; i < InspectMe.Count; i++)
                {
                    InspectorUtlity.InspectTarget(InspectMe[i]);
                }
                InspectMe.Clear();
            }
        }

        public void SetNewColorCount(int colorCount)
        {
            var newcharacterColors = new List<DynamicCharacterAvatar.ColorValue>();
            for (int i =0; i < colorCount; i++)
            {
                if (thisDCA != null && thisDCA.characterColors.Colors.Count > i)
                {
                    newcharacterColors.Add(thisDCA.characterColors.Colors[i]);
                }
                else
                {
                    newcharacterColors.Add(new DynamicCharacterAvatar.ColorValue(3));
                }
            }
            if (thisDCA != null)
            {
                thisDCA.characterColors.Colors = newcharacterColors;
            }
        }

        protected bool characterAvatarLoadSaveOpen;

        private void BeginVerticalPadded()
        {
            if (EditorGUIUtility.isProSkin)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(1.3f,1.4f,1.5f));
            }
            else
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f,0.875f,1f));
            }
        }

        private void EndVerticalPadded()
        {
            GUIHelper.EndVerticalPadded(10);
        }

        public override void OnInspectorGUI()
        {
            if (IsEditorBusy())
            {
                EditorGUILayout.HelpBox("Compiling/Updating...", MessageType.Info);
                return;
            }
            if (target == null)
            {
                return;
            }
            bool wasChanged = false;
            thisDCA = target as DynamicCharacterAvatar;
            if (thisDCA == null)
            {
                EditorGUILayout.HelpBox("DynamicCharacterAvatar is missing.", MessageType.Warning);
                return;
            }
            SerializedProperty userInfo = serializedObject.FindProperty("userInformation");
            showHelp = EditorGUILayout.Toggle("Show Help", showHelp);
            // Help BEFORE userInformation field
            if (showHelp)
            {
                EditorGUILayout.HelpBox("User Information: This is a field for you to put any information you want to store with the character. It is not used by the system in any way.", MessageType.Info);
            }
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(userInfo);
            if (EditorGUI.EndChangeCheck())
            {
                wasChanged = true;
            }

            if (Application.isPlaying)
            {
                BeginVerticalPadded();
                EditorGUILayout.LabelField("Force Regenerate (Playtime)", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Full Build"))
                {
                    thisDCA.BuildCharacter(true);
                }
                if (GUILayout.Button("Textures"))
                {
                    thisDCA.ForceUpdate(false, true, false);
                }
                if (GUILayout.Button("DNA"))
                {
                    thisDCA.ForceUpdate(true, false, false);
                }
                if (GUILayout.Button("Mesh"))
                {
                    thisDCA.ForceUpdate(false, false, true);
                }
                EditorGUILayout.EndHorizontal();
                EndVerticalPadded();
            }

            //The base DynamicAvatar properties- get these early because changing the race changes someof them
            SerializedProperty umaGenerator = serializedObject.FindProperty("umaGenerator");
            SerializedProperty umaRecipe = serializedObject.FindProperty("_umaRecipe");
            SerializedProperty umaAdditionalRecipes = serializedObject.FindProperty("umaAdditionalRecipes");
            animationController = serializedObject.FindProperty("animationController");

            // ************************************************************
            // Set the race
            // ************************************************************
            SerializedProperty thisRaceSetter = serializedObject.FindProperty("activeRace");
            Rect currentRect = EditorGUILayout.GetControlRect(false, _racePropDrawer.GetPropertyHeight(thisRaceSetter, GUIContent.none));
            // Help BEFORE race drawer
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Active Race: Sets the race of the character, which defines the base recipe to build the character, the available DNA, and the available wardrobe.", MessageType.Info);
            }
            EditorGUI.BeginChangeCheck();
            InspectMe = _racePropDrawer.DoGUI(currentRect, thisRaceSetter, new GUIContent(thisRaceSetter.displayName));
            if (EditorGUI.EndChangeCheck())
            {
                wasChanged = true;
                bool okToProcess = true;
                // check to see if we changed it while playing, and if so, don't do it again.
                if (Application.isPlaying)
                {
                    if (thisDCA.activeRace.data != null)
                    {
                        if (thisDCA.activeRace.data.raceName == (string)thisRaceSetter.FindPropertyRelative("name").stringValue)
                        {
                            okToProcess = false;
                        }
                    }
                }

                if (okToProcess && thisDCA.editorTimeGeneration)
                {
                    thisDCA.ChangeRace((string)thisRaceSetter.FindPropertyRelative("name").stringValue, DynamicCharacterAvatar.ChangeRaceOptions.useDefaults, true);
                    //Changing the race may cause umaRecipe, animationController to change so forcefully update these too
                    //umaRecipe.objectReferenceValue = thisDCA.serializedRecipe;
                    animationController.objectReferenceValue = thisDCA.animationController;
                    serializedObject.ApplyModifiedProperties();
                    GenerateSingleUMA(thisDCA.rebuildSkeleton);
                }
            }


            //**************************************
            // Begin In-Editor customization
            //**************************************
            showEditorCustomization = EditorGUILayout.Foldout(showEditorCustomization, new GUIContent("Customization", "Properties for customizing the look of the UMA"));
            if (showEditorCustomization)
            {
                if (ShowEditorCustomizationGUI())
                {
                    wasChanged = true;
                }
            }


            //**************************************
            // End In-Editor customization
            //********************************


            //the ChangeRaceOptions
            SerializedProperty defaultChangeRaceOptions = serializedObject.FindProperty("defaultChangeRaceOptions");
            defaultChangeRaceOptions.isExpanded = EditorGUILayout.Foldout(defaultChangeRaceOptions.isExpanded, new GUIContent("Race Change Options", "The default options for when the Race is changed. These can be overidden when calling 'ChangeRace' directly."));
            if (defaultChangeRaceOptions.isExpanded)
            {
                wasChanged |= DoRaceChangeOptionsGUI(wasChanged, defaultChangeRaceOptions);
            }


            //Move UMAAddidtionalRecipes out of advanced into its own section
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Additional Utility Recipes: Additional recipes to add when the character is generated (e.g., capsule collider).", MessageType.Info);
            }
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(umaAdditionalRecipes, new GUIContent("Additional Utility Recipes", "Additional Recipes to add when the character is generated, like the capsuleCollider recipe for example"), true);
            if (EditorGUI.EndChangeCheck())
            {
                wasChanged = true;
            }
            GUILayout.Space(2f);

            showAnimatorGUI = EditorGUILayout.Foldout(showAnimatorGUI, "Animator Parameters");
            if (showAnimatorGUI)
            {
                ShowAnimatorGUI(thisDCA);
            }

            showBlendshapes = EditorGUILayout.Foldout(showBlendshapes, "Blendshapes");
            if (showBlendshapes)
            {
                ShowBlendshapesGUI(thisDCA);
            }

            GUILayout.Space(2f);
            //Load save fields
            SerializedProperty loadPathType = serializedObject.FindProperty("loadPathType");
            loadPathType.isExpanded = EditorGUILayout.Foldout(loadPathType.isExpanded, "Legacy Load/Save Options");
            if (loadPathType.isExpanded)
            {
                DoLegacyLoadSave(loadPathType);
            }

            GUILayout.Space(2f);
            //for CharacterEvents
            SerializedProperty CharacterCreated = serializedObject.FindProperty("CharacterCreated");
            CharacterCreated.isExpanded = EditorGUILayout.Foldout(CharacterCreated.isExpanded, "Character Events");
            if (CharacterCreated.isExpanded)
            {
                DoEventsGUI(CharacterCreated);
            }

            GUILayout.Space(2f);
            //for AdvancedOptions
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced Options");
            if (showAdvanced)
            {
                DoAdvancedOptionsGUI(umaGenerator);
            }
            GUILayout.Space(2f);

            //for PlaceholderOptions
            SerializedProperty gizmo = serializedObject.FindProperty("showPlaceholder");
            SerializedProperty enableGizmo = serializedObject.FindProperty("showPlaceholder");
            SerializedProperty previewModel = serializedObject.FindProperty("previewModel");
            SerializedProperty customModel = serializedObject.FindProperty("customModel");
            SerializedProperty customRotation = serializedObject.FindProperty("customRotation");
            SerializedProperty previewColor = serializedObject.FindProperty("previewColor");
            gizmo.isExpanded = EditorGUILayout.Foldout(gizmo.isExpanded, "Placeholder Options");
            if (gizmo.isExpanded)
            {
                DoGizmosUI(enableGizmo, previewModel, customModel, customRotation, previewColor);
            }

            showUMAData = GUIHelper.FoldoutBar(showUMAData, "UMA Data");
            if (showUMAData)
            {
                if (innerEditor != null)
                {
                    innerEditor.OnInspectorGUI();
                }
                // DrawFoldoutInspector(thisDCA, ref innerEditor);
            }

            if (Application.isPlaying || thisDCA.editorTimeGeneration)
            {
                showWardrobe = EditorGUILayout.Foldout(showWardrobe, "Current Wardrobe");
                if (showWardrobe)
                {
                    DoShowWardrobeGUI();
                }
                showUtils = EditorGUILayout.Foldout(showUtils, "Utilities");
                if (showUtils)
                {
                    DoUtilitiesGUI();
                }
            }




            if (wasChanged)
            {
                serializedObject.ApplyModifiedProperties();
            }
        }



        private bool DoRaceChangeOptionsGUI(bool wasChanged, SerializedProperty defaultChangeRaceOptions)
        {
            BeginVerticalPadded();
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Race Change Options: Default behavior flags applied when the race is changed.", MessageType.Info);
            }
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(defaultChangeRaceOptions, GUIContent.none);
            EditorGUI.indentLevel++;
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Cache Current State: Cache the avatar state and try to restore appropriate elements on race changes.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cacheCurrentState"));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Rebuild Skeleton: Force the skeleton to be rebuilt when the race changes.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rebuildSkeleton"));
            EditorGUI.indentLevel--;
            if (EditorGUI.EndChangeCheck())
            {
                wasChanged = true;
            }
            EndVerticalPadded();
            return wasChanged;
        }

        private bool ShowEditorCustomizationGUI()
        {
            bool wasChanged = false;
            BeginVerticalPadded();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Preset"))
            {
                string fileName = EditorUtility.SaveFilePanel("Save Preset", "", "DCAPreset", "umapreset");
                if (!string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        UMAPreset prs = new UMAPreset();
                        prs.DefaultColors = thisDCA.characterColors;
                        prs.PredefinedDNA = thisDCA.predefinedDNA;
                        prs.DefaultWardrobe = thisDCA.preloadWardrobeRecipes;
                        string presetstring = JsonUtility.ToJson(prs);
                        System.IO.File.WriteAllText(fileName, presetstring);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        EditorUtility.DisplayDialog("Error", "Error writing preset file: " + ex.Message, "OK");
                    }
                }
            }
            if (GUILayout.Button("Load Preset"))
            {
                string fileName = EditorUtility.OpenFilePanel("Load Preset", "", "umapreset");
                if (!string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        string presetstring = System.IO.File.ReadAllText(fileName);
                        thisDCA.InitializeFromPreset(presetstring);
                        UpdateCharacter();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        EditorUtility.DisplayDialog("Error", "Error writing preset file: " + ex.Message, "OK");
                    }
                }
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (GUILayout.Button("Save AvatarDef"))
                {
                    string fileName = EditorUtility.SaveFilePanel("Save Avatar Definition File", "", "", "adf");
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        try
                        {
                            string charstr = thisDCA.GetAvatarDefinition(false, true).ToCompressedString("|");
                            System.IO.File.WriteAllText(fileName, charstr);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                            EditorUtility.DisplayDialog("Error", "Error writing avatar definition file: " + ex.Message, "OK");
                        }
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Regen"))
            {
                UpdateCharacter();
            }


            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save Avatar Definition"))
                {
                    string fileName = EditorUtility.SaveFilePanel("Save Avatar Definition", "", "", "adf");
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        try
                        {
                            AvatarDefinition adf = thisDCA.GetAvatarDefinition(false, true);
                            string charstr = adf.ToCompressedString("|");
                            System.IO.File.WriteAllText(fileName, charstr);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                            EditorUtility.DisplayDialog("Error", "Error writing avatar definition file: " + ex.Message, "OK");
                        }
                    }
                }
                if (GUILayout.Button("Load Avatar Definition"))
                {
                    string fileName = EditorUtility.OpenFilePanel("Load Avatar Definition", "", "adf");
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        try
                        {
                            string presetstring = System.IO.File.ReadAllText(fileName);
                            AvatarDefinition adf = AvatarDefinition.FromCompressedString(presetstring, '|');
                            thisDCA.LoadAvatarDefinition(adf);
                            thisDCA.BuildCharacter(false);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                            EditorUtility.DisplayDialog("Error", "Error writing preset file: " + ex.Message, "OK");
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.BeginChangeCheck();
            bool wasEnabled = GUI.enabled; //VES added
            if (wasEnabled && PrefabStageUtility.GetPrefabStage(thisDCA.gameObject) != null)
            { //VES added, checks if in prefab
                GUI.enabled = false; //VES added (we don't want anyone generating the character in the patient prefabs as it breaks inheritance, and we setup patients via code)
            }
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Editor Time Generation: When enabled, UMA builds are performed in the editor as you edit the avatar.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("editorTimeGeneration"));
            GUI.enabled = wasEnabled; //VES added
            if (EditorGUI.EndChangeCheck())
            {
                wasChanged = true;
                serializedObject.ApplyModifiedProperties();
                UpdateCharacter();
            }


            //******************************************************************
            // Preload wardrobe
            //Other DCA propertyDrawers
            //in order for the "preloadWardrobeRecipes" prop to properly check if it can load the recipies it gets assigned to it
            //it needs to know that its part of this DCA
            SerializedProperty thisPreloadWardrobeRecipes = serializedObject.FindProperty("preloadWardrobeRecipes");
            Rect pwrCurrentRect = EditorGUILayout.GetControlRect(false, _wardrobePropDrawer.GetPropertyHeight(thisPreloadWardrobeRecipes, GUIContent.none));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Preload Wardrobe: Sets the default wardrobe recipes to use on the Avatar. This is useful when creating specific Avatar prefabs.", MessageType.Info);
            }
            _wardrobePropDrawer.OnGUI(pwrCurrentRect, thisPreloadWardrobeRecipes, new GUIContent(thisPreloadWardrobeRecipes.displayName));
            if (_wardrobePropDrawer.changed)
            {
                serializedObject.ApplyModifiedProperties();
                if (Application.isPlaying)
                {
                    thisDCA.ClearSlots();
                    thisDCA.LoadDefaultWardrobe();
                    thisDCA.BuildCharacter(false);
                }
                else
                {
                    GenerateSingleUMA();
                }
            }
            // *********************************************************************************
            // 
            //NewCharacterColors
            SerializedProperty characterColors = serializedObject.FindProperty("characterColors");
            SerializedProperty newCharacterColors = characterColors.FindPropertyRelative("_colors");
            GUILayout.BeginHorizontal();
            GUILayout.Space(2);
            //for ColorValues as OverlayColorDatas we need to outout something that looks like a list but actully uses a method to add/remove colors because we need the new OverlayColorData to have3 channels 
            newCharacterColors.isExpanded = EditorGUILayout.Foldout(newCharacterColors.isExpanded, new GUIContent("Character Colors"));
            GUILayout.EndHorizontal();
            var n_origArraySize = newCharacterColors.arraySize;
            var n_newArraySize = n_origArraySize;
            if (newCharacterColors.isExpanded)
            {
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Character Colors: This lets you set predefined colors to be used when building the Avatar. The colors will be assigned to the Shared Colors on the overlays as they are applied to the Avatar.", MessageType.Info);
                }
                n_newArraySize = DoColorsGUI(newCharacterColors, n_origArraySize);
            }

            //***********************************************************************************
            // Predefined DNA
            //***********************************************************************************

            // Dropdown of the current DNA.
            // button to "add" it.

            showPrefinedDNA = EditorGUILayout.Foldout(showPrefinedDNA, "Predefined DNA");
            if (showPrefinedDNA)
            {
                var generator = UMAAssetIndexer.Instance.generator;
                if (generator == null)
                {
                    EditorGUILayout.HelpBox("UMA Generator could not be instantiated.", MessageType.Warning);
                    return false;
                }
                else
                {
                    if (generator.useNewDNA)
                    {
                        wasChanged = DoNewDNA(wasChanged);
                    }
                    else
                    {
                        wasChanged = ShowDNA(wasChanged);
                    }
                }
            }
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Predefined DNA is loaded onto the character in the initial character build. Select the DNA in the dropdown, and add it to the list of DNA to load, then edit the values as needed.", MessageType.Info);
            }
            EndVerticalPadded();

            return wasChanged;
        }

        private bool DoNewDNA(bool wasChanged)
        {
            // Ensure active race and collection
            var dca = thisDCA;
            var raceData = (dca != null && dca.activeRace != null) ? dca.activeRace.data : null;
            if (raceData == null)
            {
                EditorGUILayout.HelpBox("No active race. Select a race to add DNA from RaceData.DNACollection.", MessageType.Info);
                return wasChanged;
            }

            // Initialize DNACollection if needed
            if (raceData.DNACollection == null)
            {
                raceData.DNACollection = new DNACollection();
            }
            var collection = raceData.DNACollection;
            collection.LoadDictionary();

            // UMAData is the same component (DCA derives from UMAData)
            var umaData = dca as UMAData;

            // If a collection exists, let user tweak values live
            if (umaData != null && umaData.dnaInstanceCollection != null && umaData.dnaInstanceCollection.dnaInstances != null)
            {
                // Ensure internal dictionary restored after domain reloads
                umaData.dnaInstanceCollection.Initialize(collection);

                EditorGUILayout.LabelField("Assigned New DNA", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                var instances = umaData.dnaInstanceCollection.dnaInstances;
                for (int i = 0; i < instances.Count; i++)
                {
                    var inst = instances[i];
                    if (inst == null) continue;

                    EditorGUILayout.BeginHorizontal();
                    bool newEnabled = EditorGUILayout.ToggleLeft(inst.name, inst.enabled, GUILayout.Width(140));
                    float oldValue = inst.value;
                    inst.value = EditorGUILayout.Slider(inst.value, 0f, 1f);
                    if (GUILayout.Button("Def", GUILayout.Width(40)))
                    {
                        // Reset to default value
                        if (collection.dnaDictionary != null && collection.dnaDictionary.TryGetValue(inst.name, out var dnaAsset) && dnaAsset != null)
                        {
                            float defaultValue = Mathf.Clamp01(dnaAsset.defaultValue);
                            Undo.RecordObject(umaData, "Reset DNA Value to Default");
                            inst.value = defaultValue;
                            EditorUtility.SetDirty(umaData);
                            wasChanged = true;
                            GenerateSingleUMA();
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("DNA Not Found", $"DNA asset '{inst.name}' not found in collection.", "OK");
                        }
                    }
                    if (GUILayout.Button("Edit", GUILayout.Width(40)))
                    {
                        // Open DNA asset in inspector
                        if (collection.dnaDictionary != null && collection.dnaDictionary.TryGetValue(inst.name, out var dnaAsset) && dnaAsset != null)
                        {
                            InspectorUtlity.InspectTarget(dnaAsset);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("DNA Not Found", $"DNA asset '{inst.name}' not found in collection.", "OK");
                        }
                    }
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        // Remove this DNAInstance
                        Undo.RecordObject(umaData, "Remove DNA Instance");
                        umaData.dnaInstanceCollection.dnaInstances.RemoveAt(i);
                        EditorUtility.SetDirty(umaData);
                        wasChanged = true;
                        GenerateSingleUMA();
                        // Exit to avoid modifying collection during iteration
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();

                    if (!Mathf.Approximately(oldValue, inst.value))
                    {
                        Undo.RecordObject(umaData, "Change DNA Value");
                        EditorUtility.SetDirty(umaData);
                        wasChanged = true;
                        GenerateSingleUMA();
                    }
                    if (newEnabled != inst.enabled)
                    {
                        Undo.RecordObject(umaData, "Toggle DNA Instance");
                        inst.enabled = newEnabled;
                        EditorUtility.SetDirty(umaData);
                        wasChanged = true;
                    }
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }
            else
            {
                // Guide the user; we’ll create it automatically on first add
                EditorGUILayout.HelpBox("No New DNA collection exists yet. It will be created when you add the first entry.", MessageType.Info);
            }

            // Picker grouped by DNAGroup (always show all DNA)
            EditorGUILayout.LabelField("Add New DNA", EditorStyles.boldLabel);
            var groups = collection.DNAGroups ?? new List<DNAGroup>();
            if (groups.Count == 0)
            {
                EditorGUILayout.HelpBox("Race DNACollection has no DNA groups.", MessageType.Info);
                return wasChanged;
            }

            // Build popup of groups
            if (_newDnaGroupIndex < 0 || _newDnaGroupIndex >= groups.Count) _newDnaGroupIndex = 0;
            List<string> groupNames = new List<string>(groups.Count);
            for (int g = 0; g < groups.Count; g++)
            {
                var grp = groups[g];
                groupNames.Add(grp == null || string.IsNullOrEmpty(grp.DNAArea) ? $"Group {g}" : grp.DNAArea);
            }
            _newDnaGroupIndex = EditorGUILayout.Popup("Group", _newDnaGroupIndex, groupNames.ToArray());

            var selGroup = groups[_newDnaGroupIndex];
            var dnaList = (selGroup != null && selGroup.dnaList != null) ? selGroup.dnaList : new List<DNA>();
            if (dnaList.Count == 0)
            {
                EditorGUILayout.HelpBox("Selected group has no DNA entries.", MessageType.Info);
                return wasChanged;
            }

            // Build popup of DNA within selected group
            List<string> dnaNames = new List<string>(dnaList.Count);
            for (int i = 0; i < dnaList.Count; i++)
            {
                var d = dnaList[i];
                if (d != null) dnaNames.Add(d.dnaName);
            }
            if (dnaNames.Count == 0)
            {
                EditorGUILayout.HelpBox("Selected group has no DNA entries.", MessageType.Info);
                return wasChanged;
            }
            if (_newDnaInGroupIndex < 0 || _newDnaInGroupIndex >= dnaNames.Count) _newDnaInGroupIndex = 0;
            _newDnaInGroupIndex = EditorGUILayout.Popup("DNA", _newDnaInGroupIndex, dnaNames.ToArray());

            if (GUILayout.Button("Add DNA Instance"))
            {
                if (umaData == null)
                {
                    EditorUtility.DisplayDialog("UMAData Missing", "UMAData component not found.", "OK");
                    return wasChanged;
                }

                // Ensure collection exists and is initialized
                if (umaData.dnaInstanceCollection == null)
                {
                    Undo.RecordObject(umaData, "Create DNA Collection");
                    umaData.dnaInstanceCollection = new DNAInstanceCollection();
                    umaData.dnaInstanceCollection.Initialize(collection);
                    EditorUtility.SetDirty(umaData);
                }
                else
                {
                    umaData.dnaInstanceCollection.Initialize(collection);
                }

                string selected = dnaNames[_newDnaInGroupIndex];

                // Prevent duplicate
                var current = umaData.dnaInstanceCollection.dnaInstances;
                bool duplicate = false;
                if (current != null)
                {
                    for (int i = 0; i < current.Count; i++)
                    {
                        var inst = current[i];
                        if (inst != null && inst.name == selected) { duplicate = true; break; }
                    }
                }
                if (duplicate)
                {
                    EditorUtility.DisplayDialog("Duplicate DNA", $"DNA '{selected}' is already assigned.", "OK");
                    return wasChanged;
                }

                // Add with default value (if available)
                float defaultValue = 0.5f;
                var dict = collection.dnaDictionary;
                if (dict != null && dict.TryGetValue(selected, out var dnaAsset) && dnaAsset != null)
                {
                    defaultValue = Mathf.Clamp01(dnaAsset.defaultValue);
                }

                Undo.RecordObject(umaData, "Add DNA Instance");
                if (umaData.dnaInstanceCollection.dnaInstances == null)
                {
                    umaData.dnaInstanceCollection.dnaInstances = new List<DNAInstance>();
                }
                umaData.dnaInstanceCollection.dnaInstances.Add(new DNAInstance
                {
                    name = selected,
                    value = defaultValue,
                    enabled = true
                });
                EditorUtility.SetDirty(umaData);
                wasChanged = true;
            }

            return wasChanged;
        }

        private bool DoNewDNAOld(bool wasChanged)
        {
            // Ensure active race and collection
            var dca = thisDCA;
            var raceData = (dca != null && dca.activeRace != null) ? dca.activeRace.data : null;
            if (raceData == null)
            {
                EditorGUILayout.HelpBox("No active race. Select a race to add DNA from RaceData.DNACollection.", MessageType.Info);
                return wasChanged;
            }

            // Initialize DNACollection if needed
            if (raceData.DNACollection == null)
            {
                raceData.DNACollection = new DNACollection();
            }
            var collection = raceData.DNACollection;
            collection.LoadDictionary();

            // Ensure UMAData collection exists when available
            var umaData = dca as UMAData;
            // Assigned instances: enable/disable only
            if (umaData.dnaInstanceCollection != null && umaData.dnaInstanceCollection.dnaInstances != null)
            {
                EditorGUILayout.LabelField("Assigned New DNA", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                var instances = umaData.dnaInstanceCollection.dnaInstances;
                for (int i =0; i < instances.Count; i++)
                {
                    var inst = instances[i];
                    if (inst == null) continue;
                    GUILayout.BeginHorizontal();
                    bool newEnabled = EditorGUILayout.ToggleLeft(inst.name, inst.enabled, GUILayout.Width(100));
                    float oldValue = inst.value;
                    inst.value = EditorGUILayout.Slider(inst.value, 0f, 1f);
                    GUILayout.EndHorizontal();
                    if (oldValue != inst.value)
                    {
                        Undo.RecordObject(dca, "Change DNA Value");
                        EditorUtility.SetDirty(dca);
                        wasChanged = true;
                        GenerateSingleUMA();
                    }
                    if (newEnabled != inst.enabled)
                    {
                        Undo.RecordObject(dca, "Toggle DNA Instance");
                        inst.enabled = newEnabled;
                        EditorUtility.SetDirty(dca);
                        wasChanged = true;
                    }
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // Picker grouped by DNAGroup (always show all DNA)
            EditorGUILayout.LabelField("Add New DNA", EditorStyles.boldLabel);
            var groups = collection.DNAGroups ?? new List<DNAGroup>();
            if (groups.Count ==0)
            {
                EditorGUILayout.HelpBox("Race DNACollection has no DNA groups.", MessageType.Info);
                return wasChanged;
            }

            // Build popup of groups
            if (_newDnaGroupIndex <0 || _newDnaGroupIndex >= groups.Count) _newDnaGroupIndex =0;
            List<string> groupNames = new List<string>(groups.Count);
            for (int g =0; g < groups.Count; g++)
            {
                var grp = groups[g];
                groupNames.Add(grp == null || string.IsNullOrEmpty(grp.DNAArea) ? $"Group {g}" : grp.DNAArea);
            }
            _newDnaGroupIndex = EditorGUILayout.Popup("Group", _newDnaGroupIndex, groupNames.ToArray());

            var selGroup = groups[_newDnaGroupIndex];
            var dnaList = (selGroup != null && selGroup.dnaList != null) ? selGroup.dnaList : new List<DNA>();
            if (dnaList.Count ==0)
            {
                EditorGUILayout.HelpBox("Selected group has no DNA entries.", MessageType.Info);
                return wasChanged;
            }

            // Build popup of DNA within selected group
            List<string> dnaNames = new List<string>(dnaList.Count);
            for (int i =0; i < dnaList.Count; i++)
            {
                var d = dnaList[i];
                if (d != null) dnaNames.Add(d.dnaName);
            }
            if (dnaNames.Count ==0)
            {
                EditorGUILayout.HelpBox("Selected group has no DNA entries.", MessageType.Info);
                return wasChanged;
            }
            if (_newDnaInGroupIndex <0 || _newDnaInGroupIndex >= dnaNames.Count) _newDnaInGroupIndex =0;
            _newDnaInGroupIndex = EditorGUILayout.Popup("DNA", _newDnaInGroupIndex, dnaNames.ToArray());

            using (new EditorGUI.DisabledScope(umaData == null))
            {
                if (GUILayout.Button("Add DNA Instance"))
                {
                    if (umaData == null || umaData.dnaInstanceCollection == null)
                    {
                        EditorUtility.DisplayDialog("UMAData Missing", "UMAData is not available. Build the avatar to create UMAData before adding DNA.", "OK");
                        return wasChanged;
                    }

                    string selected = dnaNames[_newDnaInGroupIndex];
                    // Prevent duplicate
                    bool duplicate = false;
                    var current = umaData.dnaInstanceCollection.dnaInstances;
                    if (current != null)
                    {
                        for (int i =0; i < current.Count; i++)
                        {
                            var inst = current[i];
                            if (inst != null && inst.name == selected)
                            {
                                duplicate = true;
                                break;
                            }
                        }
                    }
                    if (duplicate)
                    {
                        EditorUtility.DisplayDialog("Duplicate DNA", $"DNA '{selected}' is already assigned.", "OK");
                        return wasChanged;
                    }

                    // Add with default value
                    var dict = collection.dnaDictionary;
                    if (dict != null && dict.TryGetValue(selected, out var dnaAsset) && dnaAsset != null)
                    {
                        Undo.RecordObject(dca, "Add DNA Instance");
                        if (umaData.dnaInstanceCollection.dnaInstances == null)
                        {
                            umaData.dnaInstanceCollection.dnaInstances = new List<DNAInstance>();
                        }
                        umaData.dnaInstanceCollection.dnaInstances.Add(new DNAInstance
                        {
                            name = dnaAsset.dnaName,
                            value = Mathf.Clamp01(dnaAsset.defaultValue),
                            enabled = true
                        });
                        EditorUtility.SetDirty(dca);
                        wasChanged = true;
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("DNA Not Found", $"DNA '{selected}' not found in Race DNACollection.", "OK");
                    }
                }
            }

            return wasChanged;
        }

        // state for New DNA picker
        private static int _newDnaGroupIndex =0;
        private static int _newDnaInGroupIndex =0;

        private bool ShowDNA(bool wasChanged)
        {
            {
                EditorGUI.BeginChangeCheck();
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Keep Predefined DNA: When enabled, preserves previously set predefined DNA values across builds.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(serializedObject.FindProperty("keepPredefinedDNA"));
                if (EditorGUI.EndChangeCheck())
                {
                    wasChanged = true;
                }
                if (cachedRace != thisDCA.activeRace.name)
                {
                    cachedRace = thisDCA.activeRace.name;
                    rawcachedRaceDNA = thisDCA.activeRace.data.GetDNANames().ToArray();
                    List<string> MenuDNA = new List<string>();
                    foreach (string s in rawcachedRaceDNA)
                    {
                        MenuDNA.Add(s.MenuCamelCase());
                    }
                    cachedRaceDNA = MenuDNA.ToArray();
                }

                GUILayout.BeginHorizontal();
                currentDNA = EditorGUILayout.Popup(currentDNA, cachedRaceDNA);
                if (GUILayout.Button("Add DNA"))
                {
                    string theDna = rawcachedRaceDNA[currentDNA];

                    if (thisDCA.predefinedDNA == null)
                    {
                        thisDCA.predefinedDNA = new UMAPredefinedDNA();
                    }
                    if (thisDCA.predefinedDNA.ContainsName(theDna))
                    {
                        EditorUtility.DisplayDialog("Error", "Predefined DNA Already contains DNA: " + theDna, "OK");
                    }
                    else
                    {
                        AddSingleDNA(theDna);
                        SortDNA();
                        serializedObject.Update();
                        wasChanged = true;
                    }
                }
                if (GUILayout.Button("Add All"))
                {
                    foreach (string s in rawcachedRaceDNA)
                    {
                        if (!thisDCA.predefinedDNA.ContainsName(s))
                        {
                            AddSingleDNA(s);
                        }
                    }
                    SortDNA();
                    serializedObject.Update();
                    wasChanged = true;
                }
                if (GUILayout.Button("Clear"))
                {
                    thisDCA.predefinedDNA.Clear();
                    serializedObject.Update();
                    GenerateSingleUMA();
                    Repaint();
                }
                GUILayout.EndHorizontal();

                if (thisDCA.predefinedDNA != null)
                {
                    string delme = "";
                    EditorGUI.BeginChangeCheck();
                    foreach (var pd in thisDCA.predefinedDNA.PreloadValues)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(ObjectNames.NicifyVariableName(pd.Name), GUILayout.Width(100));
                        float newValue = GUILayout.HorizontalSlider(pd.Value, 0.0f, 1.0f);
                        if (newValue != pd.Value)
                        {
                            pd.Value = newValue;
                            wasChanged = true;
                        }

                        bool delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                        if (delete)
                        {
                            delme = pd.Name;
                        }
                        GUILayout.EndHorizontal();
                    }
                    if (!string.IsNullOrEmpty(delme))
                    {
                        thisDCA.predefinedDNA.RemoveDNA(delme);
                        serializedObject.Update();
                        GenerateSingleUMA();
                        Repaint();
                        wasChanged = true;
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        wasChanged = true;
                        GenerateSingleUMA();
                    }
                }
            }

            return wasChanged;
        }

        private static bool AllowVertexSelection;

        private Color[] defaultColors = new Color[] 
        { 
 new Color(1.0f,0.9f,0.9f,1.0f), 
 new Color(0.9f,1.0f,0.9f,1.0f), 
 new Color(0.9f,0.9f,1.0f,1.0f),
 new Color(1.0f,1.0f,0.9f,1.0f),
 new Color(0.9f,1.0f,1.0f,1.0f),
 new Color(1.0f,0.9f,1.0f,1.0f)
 };


        private void DoSceneGUI(SceneView sceneView)
        {
            if (IsEditorBusy())
            {
                return;
            }

            if (thisDCA == null)
            {
                return;
            }
            // Leaving this function here so I can later add some tools to the scene view to find/rebuild/modify UMAs
            // TODO: include all that in a project setting
            Event currentEvent = Event.current;

            // Your custom GUI logic here
            //Handles.BeginGUI();
            // GUILayout.BeginArea(new Rect(10,10,200,300), "Vertex Selection", GUI.skin.window);
            //GUILayout.EndArea();
            //Handles.EndGUI();

            // Repaint the scene view only when necessary
            if (currentEvent.type == EventType.Repaint)
            {
                //SceneView.RepaintAll();
            }
        }

        private void DoUtilitiesGUI()
        {
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f,0.875f,1f));

            GUILayout.Label("Mesh Modifier", EditorStyles.boldLabel);

            // Buttons row
            if (GUILayout.Button("Create New Modifier"))
            {
                VertexEditorStage.ShowStage(thisDCA, null);
            }

            // Drag & Drop Area
            Rect dropRect = GUILayoutUtility.GetRect(0,40, GUILayout.ExpandWidth(true));
            GUIContent dropLabel;
            dropLabel = new GUIContent("Drag & Drop a MeshModifier here to edit", "Drop a MeshModifier asset");

            GUI.Box(dropRect, dropLabel, EditorStyles.helpBox);

            Event evt = Event.current;
            if (dropRect.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
                {
                    bool valid = false;
                    foreach (UnityEngine.Object o in DragAndDrop.objectReferences)
                    {
                        if (o is MeshModifier)
                        {
                            valid = true;
                            break;
                        }
                    }
                    if (valid)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        if (evt.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();
                            foreach (UnityEngine.Object o in DragAndDrop.objectReferences)
                            {
                                if (o is MeshModifier mm)
                                {
                                    MeshModifier = mm;
                                    VertexEditorStage.ShowStage(thisDCA, MeshModifier);
                                    break; // only first
                                }
                            }
                        }
                        evt.Use();
                    }
                }
            }

            GUIHelper.EndVerticalPadded(10);
        }

        private void DoShowWardrobeGUI()
        {
            string DeleteMe = null;

            EditorGUI.indentLevel++;
            Dictionary<string, UMATextRecipe> currentWardrobe = thisDCA.WardrobeRecipes;

            bool editTimeUpdateNeeded = false;
            foreach (KeyValuePair<string, UMATextRecipe> item in currentWardrobe)
            {
                string prepend = "*";
                if (item.Value.disabled)
                {
                    prepend = "-";
                }

                GUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.LabelField(prepend + item.Key, GUILayout.Width(88.0f));
                EditorGUILayout.TextField(item.Value.DisplayValue + " (" + item.Value.name + ")");
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Inspect", EditorStyles.toolbarButton, GUILayout.Width(52)))
                {
                    InspectorUtlity.InspectTarget(item.Value);
                }
                if (GUILayout.Button("0/1", EditorStyles.toolbarButton, GUILayout.Width(32)))
                {
                    item.Value.disabled = !item.Value.disabled;
                    if (Application.isPlaying)
                    {
                        thisDCA.BuildCharacter(true);
                    }
                    else
                    {
                        editTimeUpdateNeeded = true;
                    }
                }
                if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(18)))
                {
                    DeleteMe = item.Key;
                }
                GUILayout.EndHorizontal();
            }
            if (editTimeUpdateNeeded)
            {
                serializedObject.ApplyModifiedProperties();
                UpdateCharacter();
            }

            if (!string.IsNullOrEmpty(DeleteMe))
            {
                currentWardrobe.Remove(DeleteMe);
                serializedObject.Update();
                thisDCA.BuildCharacter(true);
            }

            GUILayout.Space(10);
            GUILayout.Label("Additive Recipes");
            GUILayout.Space(10);
            Dictionary<string, List<UMATextRecipe>> additiveWardrobe = thisDCA.AdditiveRecipes;

            foreach (KeyValuePair<string, List<UMATextRecipe>> additem in additiveWardrobe)
            {
                foreach (UMATextRecipe item in additem.Value)
                {
                    GUILayout.BeginHorizontal();
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.LabelField(additem.Key, GUILayout.Width(88.0f));
                    EditorGUILayout.TextField(item.DisplayValue + " (" + item.name + ")");
                    EditorGUI.EndDisabledGroup();
                    if (GUILayout.Button("Inspect", EditorStyles.toolbarButton, GUILayout.Width(52)))
                    {
                        InspectorUtlity.InspectTarget(item);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            EditorGUI.indentLevel--;
        }

        private void DoLegacyLoadSave(SerializedProperty loadPathType)
        {
            EditorGUI.BeginChangeCheck();
            BeginVerticalPadded();
            SerializedProperty loadString = serializedObject.FindProperty("loadString");
            SerializedProperty loadPath = serializedObject.FindProperty("loadPath");
            SerializedProperty loadFilename = serializedObject.FindProperty("loadFilename");
            SerializedProperty loadFileOnStart = serializedObject.FindProperty("loadFileOnStart");
            SerializedProperty savePathType = serializedObject.FindProperty("savePathType");
            SerializedProperty savePath = serializedObject.FindProperty("savePath");
            SerializedProperty saveFilename = serializedObject.FindProperty("saveFilename");
            //LoadSave Flags
            SerializedProperty defaultLoadOptions = serializedObject.FindProperty("defaultLoadOptions");
            SerializedProperty defaultSaveOptions = serializedObject.FindProperty("defaultSaveOptions");
            //extra LoadSave Options in addition to flags
            //SerializedProperty waitForBundles = serializedObject.FindProperty("waitForBundles");
            SerializedProperty makeUniqueFilename = serializedObject.FindProperty("makeUniqueFilename");
            SerializedProperty ensureSharedColors = serializedObject.FindProperty("ensureSharedColors");

            if (showHelp)
            {
                EditorGUILayout.HelpBox("Load Path Type: Where to load legacy recipes from.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(loadPathType);

            if (loadPathType.enumValueIndex == Convert.ToInt32(DynamicCharacterAvatar.loadPathTypes.String))
            {
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Load String: The legacy recipe string to load.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(loadString);
            }
            else
            {
                if (loadPathType.enumValueIndex <=1)
                {
                    if (showHelp)
                    {
                        EditorGUILayout.HelpBox("Load Path: The path to the folder containing recipes to load.", MessageType.Info);
                    }
                    EditorGUILayout.PropertyField(loadPath);

                }
            }

            if (showHelp)
            {
                EditorGUILayout.HelpBox("Load Filename: The recipe file name (optional).", MessageType.Info);
            }
            EditorGUILayout.PropertyField(loadFilename);
            if (loadFilename.stringValue != "")
            {
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Load On Start: Load the specified recipe at Start.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(loadFileOnStart);
            }
            EditorGUI.indentLevel++;
            //LoadOptionsFlags
            defaultLoadOptions.isExpanded = EditorGUILayout.Foldout(defaultLoadOptions.isExpanded, new GUIContent("Load Options", "The default options for when a character is loaded from an UMATextRecipe asset or a recipe string. Can be overidden when calling 'LoadFromRecipe' or 'LoadFromString' directly."));
            if (defaultLoadOptions.isExpanded)
            {
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Legacy Load Options: Flags controlling legacy load behavior.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(defaultLoadOptions, GUIContent.none);
                EditorGUI.indentLevel++;
                //waitForBundles.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(waitForBundles.displayName, waitForBundles.tooltip), waitForBundles.boolValue);
                //buildAfterLoad.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(buildAfterLoad.displayName, buildAfterLoad.tooltip), buildAfterLoad.boolValue);
                //just drawing these as propertyFields because the toolTip on toggle left doesn't work
                //EditorGUILayout.PropertyField(waitForBundles);
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            if (Application.isPlaying)
            {
                if (GUILayout.Button("Perform Load"))
                {
                    thisDCA.DoLoad();
                }
            }
            EditorGUILayout.Space();
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Save Path Type: Where to save legacy recipes.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(savePathType);
            if (savePathType.enumValueIndex <=2)
            {
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Save Path: Target folder for saved recipes.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(savePath);
            }
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Save Filename: The recipe file name.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(saveFilename);
            EditorGUI.indentLevel++;
            defaultSaveOptions.isExpanded = EditorGUILayout.Foldout(defaultSaveOptions.isExpanded, new GUIContent("Legacy Save Options", "The default options for when a character is save to UMATextRecipe asset or a txt. Can be overidden when calling 'DoSave' directly."));
            if (defaultSaveOptions.isExpanded)
            {
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Legacy Save Options: Flags controlling legacy save behavior.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(defaultSaveOptions, GUIContent.none);
                EditorGUI.indentLevel++;
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Ensure Shared Colors: Include shared colors when saving.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(ensureSharedColors);
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Make Unique Filename: Auto-append a unique suffix to the filename.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(makeUniqueFilename);
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            if (Application.isPlaying)
            {
                if (GUILayout.Button("Perform Save"))
                {
                    thisDCA.DoSave();
                }
            }
            EndVerticalPadded();
            EditorGUILayout.Space();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DoEventsGUI(SerializedProperty CharacterCreated)
        {
            EditorGUI.BeginChangeCheck();
            BeginVerticalPadded();
            SerializedProperty CharacterStart = serializedObject.FindProperty("CharacterStart");
            SerializedProperty CharacterBegun = serializedObject.FindProperty("CharacterBegun");
            SerializedProperty CharacterUpdated = serializedObject.FindProperty("CharacterUpdated");
            SerializedProperty CharacterDestroyed = serializedObject.FindProperty("CharacterDestroyed");
            SerializedProperty CharacterDnaUpdated = serializedObject.FindProperty("CharacterDnaUpdated");
            SerializedProperty RecipeUpdated = serializedObject.FindProperty("RecipeUpdated");
            SerializedProperty AnimatorSaved = serializedObject.FindProperty("AnimatorStateSaved");
            SerializedProperty AnimatorRestored = serializedObject.FindProperty("AnimatorStateRestored");
            SerializedProperty WardrobeAdded = serializedObject.FindProperty("WardrobeAdded");
            SerializedProperty WardrobeRemoved = serializedObject.FindProperty("WardrobeRemoved");

            SerializedProperty BuildCharacterBegun = serializedObject.FindProperty("BuildCharacterBegun");
            SerializedProperty SlotsHidden = serializedObject.FindProperty("SlotsHidden");
            SerializedProperty WardrobeSuppressed = serializedObject.FindProperty("WardrobeSuppressed");

            EditorGUILayout.HelpBox("CharacterStart is called in the character Start method, after Initialization, but before auto building.", MessageType.Info);
            EditorGUILayout.PropertyField(CharacterStart);
            EditorGUILayout.HelpBox("CharacterBegun is called when the character is starting the build process", MessageType.Info);
            EditorGUILayout.PropertyField(CharacterBegun);
            EditorGUILayout.HelpBox("CharacterCreated is called after the character has completed generation the first time. It is only called once.", MessageType.Info);
            EditorGUILayout.PropertyField(CharacterCreated);
            EditorGUILayout.HelpBox("CharacterUpdated is called after the character has completed generation. It is called every time the character is generated.", MessageType.Info);
            EditorGUILayout.PropertyField(CharacterUpdated);
            EditorGUILayout.HelpBox("CharacterDestroyed is called when the character is destroyed.", MessageType.Info);
            EditorGUILayout.PropertyField(CharacterDestroyed);
            EditorGUILayout.HelpBox("CharacterDnaUpdated is called during the build process when the character's DNA has been applied.", MessageType.Info);
            EditorGUILayout.PropertyField(CharacterDnaUpdated);

            EditorGUILayout.HelpBox("BuildCharacterBegun is called at the start of BuildCharacter, before the recipes have all been merged.", MessageType.Info);
            EditorGUILayout.PropertyField(BuildCharacterBegun);
            EditorGUILayout.HelpBox("RecipeUpdated is called after the UMAData.UMARecipe has been updated on the character, and it is ready to schedule the build", MessageType.Info);
            EditorGUILayout.PropertyField(RecipeUpdated);
            EditorGUILayout.HelpBox("AnimatorStateSaved is called after the character's animator state has been saved", MessageType.Info);
            EditorGUILayout.PropertyField(AnimatorSaved);
            EditorGUILayout.HelpBox("AnimatorStateRestored is called after the character's animator state has been restored", MessageType.Info);
            EditorGUILayout.PropertyField(AnimatorRestored);
            EditorGUILayout.HelpBox("WardrobeAdded is called after a wardrobe recipe has been added to the character", MessageType.Info);
            EditorGUILayout.PropertyField(WardrobeAdded);
            EditorGUILayout.HelpBox("WardrobeRemoved is called after a wardrobe recipe has been removed from the character", MessageType.Info);
            EditorGUILayout.PropertyField(WardrobeRemoved);
            EditorGUILayout.HelpBox("WardrobeSuppressed is called after recipe generation with a list of all recipes that were suppressed by other items", MessageType.Info);
            EditorGUILayout.PropertyField(WardrobeSuppressed);
            EditorGUILayout.HelpBox("SlotsHidden is called after recipe generation with a list of all slots that were hidden by other items", MessageType.Info);
            EditorGUILayout.PropertyField(SlotsHidden);
            EndVerticalPadded();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        
        private void DoAdvancedOptionsGUI(SerializedProperty umaGenerator)
        {
            EditorGUI.BeginChangeCheck();
            BeginVerticalPadded();

            // Always Rebuild Skeleton
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Always Rebuild Skeleton: Forces cleanup of the skeleton on every build. Use this when slots add extra bones to prevent accumulation.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("alwaysRebuildSkeleton"));

            // Hide
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Hide: Disables the display of the Avatar without preventing generation. Disable the component to stop generation entirely.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("hide"));

            // Lean Hiding
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Lean Hiding: Enables a more resource-efficient hiding path. Textures will be destroyed and recreated when needed.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leanHiding"));

#if UMA_ADDRESSABLES
            // DelayUnload
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Delay Unload: Delays unloading addressable assets briefly to support immediate rebuilds. Usually leave this unchecked.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DelayUnload"));

            // BundleCheck
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Bundle Check: Verifies and loads required Addressable bundles during UMA generation. Keep enabled when using Addressables.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("BundleCheck"));
#endif

            // Default Renderer Asset
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Default Renderer Asset: Renderer settings to use for this Avatar. Leave empty to use the UMA default renderer.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultRendererAsset"));

            // Force Slot Materials
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Force Slot Materials: Forces slots to use their own materials instead of materials resolved from recipes/overlays.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("forceSlotMaterials"));

            // Atlas Resolution Scale
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Atlas Resolution Scale: Scales atlas texture resolution (quality vs performance tradeoff).", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AtlasResolutionScale"));

            // Bounds Offset
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Bounds Offset: Offset applied to calculated mesh bounds to reduce unexpected culling.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("BoundsOffset"));

            // Mark Not Readable
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Mark Not Readable: After build, mark meshes as non-readable to save memory. Disable if you need to read mesh data at runtime.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("markNotReadable"));

            // Mark Dynamic
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Mark Dynamic: Hints meshes are updated frequently (slightly faster build, slightly higher render cost).", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("markDynamic"));

            // Always Adjust Bounds
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Always Adjust Bounds: Recalculate mesh bounds during generation to minimize clipping/culling issues.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("alwaysAdjustBounds"));

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }

            // Build Character Enabled toggle (use property + side-effect setter)
            var buildCharacterEnabled = serializedObject.FindProperty("_buildCharacterEnabled");
            var buildCharacterEnabledValue = buildCharacterEnabled.boolValue;

            if (showHelp)
            {
                EditorGUILayout.HelpBox("Build Character Enabled: Builds the character on recipe load or race change. Disable to batch multiple updates before building.", MessageType.Info);
            }
            EditorGUI.BeginChangeCheck();
            var buildCharacterEnabledNewValue = EditorGUILayout.Toggle(new GUIContent(buildCharacterEnabled.displayName, "Builds the character on recipe load or race changed. If you want to load multiple recipes into a character you can disable this and enable it when you are done. By default this should be true."), buildCharacterEnabledValue);
            if (EditorGUI.EndChangeCheck())
            {
                if (buildCharacterEnabledNewValue != buildCharacterEnabledValue)
                {
                    thisDCA.BuildCharacterEnabled = buildCharacterEnabledNewValue;
                }

                serializedObject.ApplyModifiedProperties();
            }

            EndVerticalPadded();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DoGizmosUI(SerializedProperty enableGizmo, SerializedProperty previewModel, SerializedProperty customModel, SerializedProperty customRotation, SerializedProperty previewColor)
        {
            EditorGUI.BeginChangeCheck();
            BeginVerticalPadded();
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Show Placeholder: Shows a placeholder model in the editor when the avatar is hidden.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(enableGizmo);
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Preview Model: Which model to show as a placeholder.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(previewModel);
            if (previewModel.enumValueIndex ==2)
            {
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Custom Model: The GameObject to use as a custom placeholder.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(customModel);
                if (showHelp)
                {
                    EditorGUILayout.HelpBox("Custom Rotation: The rotation to apply to the custom placeholder.", MessageType.Info);
                }
                EditorGUILayout.PropertyField(customRotation);
            }
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Preview Color: Background color for the placeholder preview.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(previewColor);
            EndVerticalPadded();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        List<GameObject> GetRenderers(GameObject parent)
        {
            List<GameObject> objs = new List<GameObject>();

            var renderers = parent.GetComponentsInChildren<Renderer>();
            for (int i =0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                objs.Add(renderer.gameObject);
            }
            return objs;
        }

        void ShowBlendshapesGUI(DynamicCharacterAvatar thisDCA)
        {
            EditorGUI.BeginChangeCheck();

            BeginVerticalPadded();
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Load BlendShapes: Load blendshapes from slots onto the character.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loadBlendShapes"));
            // EditorGUILayout.PropertyField(serializedObject.FindProperty("loadOnlyUsedBlendshapes"));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Load Blendshape Normals: Include normals for blendshapes (increases memory).", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loadBlendshapeNormals"));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Load Blendshape Tangents: Include tangents for blendshapes (increases memory).", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loadBlendshapeTangents"));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Load All Frames: Load all blendshape frames. When unchecked, only the final frame is loaded.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loadAllFrames"));
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Force Keep Blendshapes: Prevents blendshape stripping in generated meshes.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("forceKeepBlendshapes"));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            EndVerticalPadded();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        void ShowAnimatorGUI(DynamicCharacterAvatar thisDCA)
        {
            EditorGUI.BeginChangeCheck();
            BeginVerticalPadded();

            SerializedProperty thisRaceAnimationControllers = serializedObject.FindProperty("raceAnimationControllers");
            Rect racCurrentRect = EditorGUILayout.GetControlRect(false, _animatorPropDrawer.GetPropertyHeight(thisRaceAnimationControllers, GUIContent.none));
            EditorGUI.BeginChangeCheck();

            if (showHelp)
            {
                EditorGUILayout.HelpBox("Race Animation Controllers: This sets the animation controllers used for each race. When changing the race, the animation controller for the active race will be used by default.", MessageType.Info);
            }

            _animatorPropDrawer.OnGUI(racCurrentRect, thisRaceAnimationControllers, new GUIContent(thisRaceAnimationControllers.displayName));

            if (showHelp)
            {
                EditorGUILayout.HelpBox("Keep Avatar: Reuse the existing Mecanim avatar if present.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("keepAvatar"), new GUIContent("Keep Avatar"));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Keep Animator Controller: Do not change the Animator Controller when race changes.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("KeepAnimatorController"), new GUIContent("Keep Animator Controller"));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Raw Avatar: Assign a specific Mecanim Avatar.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rawAvatar"));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Force Rebind Animator: Forces the Animator to rebind after generation.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("forceRebindAnimator"));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Recreate Animator On Race Change: Destroy and recreate the Animator when race changes.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("RecreateAnimatorOnRaceChange"));


            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                if (Application.isPlaying)
                {
                    thisDCA.SetExpressionSet();//this triggers any expressions to reset.
                    thisDCA.SetAnimatorController();
                }
            }
            EndVerticalPadded();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        void GenerateSingleUMA(bool rebuild = false)
        {
            if (IsEditorBusy())
            {
                return;
            }

            if (thisDCA == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                thisDCA.BuildCharacter(rebuild);
                return;
            }

            if (thisDCA.editorTimeGeneration == false)
            {
                return;
            }

            // Debug.Log("prefab instance asset type: " + PrefabUtility.GetPrefabInstanceStatus(thisDCA.gameObject) + ", asset type: " + PrefabUtility.GetPrefabAssetType(thisDCA.gameObject));

            // Don't generate UMAs from project prefabs or if the gameObject is not active.
            if (!thisDCA.gameObject.activeInHierarchy)//PrefabUtility.GetPrefabInstanceStatus(thisDCA.gameObject) == PrefabInstanceStatus.NotAPrefab && PrefabUtility.GetPrefabAssetType(thisDCA.gameObject) != PrefabAssetType.NotAPrefab)
            {
                return;
            }

            var indexer = UMAAssetIndexer.Instance;
            if (indexer == null || indexer.Generator == null)
            {
                Debug.Log("Cannot find generator!");
                EditorUtility.DisplayDialog("Error", "Cannot find generator!", "OK");
                return;
            }

            UMAGenerator ugb = indexer.Generator;
            if (ugb == null)
            {
                Debug.Log("Cannot find generator!");
                EditorUtility.DisplayDialog("Error", "Cannot find generator!", "OK");
            }
            else
            {

                DynamicCharacterAvatar dca = target as DynamicCharacterAvatar;

                if (dca.umaData != null)
                {
                    dca.umaData.SaveMountedItems();
                }
                CleanupGeneratedData(rebuild, false);

                dca.activeRace.SetRaceData();
                if (dca.activeRace.racedata == null)
                {
                    return;
                }

                dca.LoadDefaultWardrobe();

                // save the predefined DNA...
                var dna = dca.predefinedDNA.Clone();
                dca.BuildCharacter(false, true);
                dca.predefinedDNA = dna;

                int oldScaleFactor = ugb.InitialScaleFactor;
                int oldAtlasResolution = ugb.atlasResolution;

                ugb.FreezeTime = true;
                ugb.InitialScaleFactor = ugb.editorInitialScaleFactor;
                ugb.atlasResolution = ugb.editorAtlasResolution;


                dca.activeRace.racedata.ResetDNA();

                ugb.GenerateSingleUMA(dca.umaData, false);

                ugb.FreezeTime = false;
                ugb.InitialScaleFactor = oldScaleFactor;
                ugb.atlasResolution = oldAtlasResolution;

                var mountedItems = dca.gameObject.GetComponentsInChildren<UMAMountedItem>();
                for (int i =0; i < mountedItems.Length; i++)
                {
                    UMAMountedItem mi = mountedItems[i];
                    mi.ResetMountPoint();
                }
                dca.umaData.RestoreSavedItems();
            }
        }

        void CleanupGeneratedData(bool clear, bool killUMAData = true)
        {
            if (Application.isPlaying)
            {
                return;
            }

            List<GameObject> Cleaners = GetRenderers(thisDCA.gameObject);
            thisDCA.HideAndCleanup(clear);
            for (int i =0; i < Cleaners.Count; i++)
            {
                GameObject go = Cleaners[i];
                DestroyImmediate(go);
            }
            /*if (killUMAData)
            {
                DestroyImmediate(thisDCA.umaData);
                thisDCA.umaData = null;
            }*/
            thisDCA.ClearSlots();
        }

        void UpdateCharacter()
        {
            if (IsEditorBusy())
            {
                return;
            }

            if (thisDCA == null)
            {
                return;
            }

            if (thisDCA.gameObject.scene != default)
            {
                if (thisDCA.editorTimeGeneration)
                {
                    GenerateSingleUMA();
                }
                else
                {
                    CleanupGeneratedData(true);
                }
            }
        }

        private int DoColorsGUI(SerializedProperty newCharacterColors, int n_origArraySize)
        {
            EditorGUI.BeginChangeCheck();
            int n_newArraySize;
            var charcol = thisDCA.characterColors._colors;
            int baseColors =0;
            foreach (var c in charcol)
            {
                if (c != null)
                {
                    if (c.isBaseColor)
                    {
                        baseColors++;
                    }
                }
            }

            if (baseColors ==0 && charcol.Count >0)
            {
                foreach (var c in charcol)
                {
                    if (baseColorNames.Contains(c.name.ToLower()))
                    {
                        c.isBaseColor = true;
                        baseColors++;
                    }
                }
            }

            currentcolorfilter = EditorGUILayout.Popup("Filter Colors", currentcolorfilter, colorfilters);

            n_newArraySize = EditorGUILayout.DelayedIntField(new GUIContent("Size"), n_origArraySize);
            EditorGUILayout.Space();
            EditorGUI.indentLevel++;
            if (n_origArraySize >0)
            {
                for (int i =0; i < n_origArraySize; i++)
                {
                    SerializedProperty currentColor = newCharacterColors.GetArrayElementAtIndex(i);
                    // What a hack. 
                    if (i >= thisDCA.characterColors._colors.Count)
                    {
                        break;
                    }
                    var col = thisDCA.characterColors._colors[i];
                    if (col == null)
                    {
                        continue;
                    }


                    if (currentcolorfilter ==0)
                    {
                        if (!col.isBaseColor)
                        {
                            continue;
                        }
                    }
                    //&& !baseColorNames.Contains(currentColor.displayName.ToLower())) continue;
                    if (currentcolorfilter ==2 && currentColor.displayName.ToLower().Contains("colordna"))
                    {
                        continue;
                    }

                    EditorGUILayout.PropertyField(newCharacterColors.GetArrayElementAtIndex(i));
                }
            }
            EditorGUI.indentLevel--;

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                bool updated = thisDCA.characterColors.RemoveDeletedItems();
                serializedObject.Update();


                if (n_newArraySize != n_origArraySize)
                {
                    updated = true;
                    SetNewColorCount(n_newArraySize);//this is not prompting a save so mark the scene dirty...
                }
                if (updated & (!Application.isPlaying))
                {
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                }

                serializedObject.ApplyModifiedProperties();
                if (Application.isPlaying)
                {
                    thisDCA.UpdateColors(true);
                }
                else
                {

                    GenerateSingleUMA();
                    //thisDCA.UpdateColors(false); // todo: this block is losing all the colors in the recipe somehow...
                    //thisDCA.umaData.isTextureDirty = true;
                    //UpdateUMA();
                }
            }
            return n_newArraySize;
        }

        private void SortDNA()
        {
            if (thisDCA.predefinedDNA != null)
            {
                thisDCA.predefinedDNA.Sort();
            }
        }

        private void AddSingleDNA(string theDna)
        {
            float value =0.5f;

            if (thisDCA.umaData != null)
            {
                var characterDNA = thisDCA.GetDNA();
                if (characterDNA != null)
                {
                    if (characterDNA.ContainsKey(theDna))
                    {
                        value = characterDNA[theDna].Value;
                    }
                }
            }
            thisDCA.predefinedDNA.AddDNA(theDna, value);
        }
    }
}
