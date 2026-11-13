using System;
using System.Collections.Generic;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.CharacterSystem.Editors
{
    // Advanced View, as foldable panels with a Help toggle each, grouped by job:
    //   Character & race, Editing tools, Wardrobe, Colors, Body shape & DNA (drawn with Standard View's sections),
    //   then Animation, Generation, Build behaviour, Performance & sharing, Rendering, Bounds, Addressables, Events,
    //   Diagnostics and Legacy Load/Save (drawn here).
    // Whether a panel is open and whether its help shows are editor preferences, never stored on the avatar.
    public partial class DynamicCharacterAvatarEditor
    {
        // ------------------------------------------------------------------
        // Help texts. Each panel describes all of its settings in one place.
        // ------------------------------------------------------------------

        private const string RaceAdvancedHelp =
            "Active Race chooses the character's base body, rig, DNA controls, compatible wardrobe and default recipe. " +
            "Race and Base Recipe inspect the source assets. Changing the race can rebuild the avatar when Editor Time Generation is on.\n\n" +
            "User Information: free text stored with the character. UMA does not use it.\n\n" +
            "When the race changes: the Race Change Options are the default behaviour, which a call to ChangeRace can override. " +
            "Cache Current State caches the avatar's state and tries to restore what still fits the new race. " +
            "Rebuild Skeleton rebuilds the skeleton for the new race.";

        private const string WardrobeAdvancedHelp =
            "Default Wardrobe Recipes are equipped when the avatar is first built or when its defaults are restored. Each recipe " +
            "occupies or appends to a race wardrobe region and may hide or suppress other content. Runtime wardrobe changes belong " +
            "to the generated avatar instance and do not alter the recipe assets.\n\n" +
            "Additional Utility Recipes are added every time the character is generated, for example the capsule collider recipe.\n\n" +
            "Current Wardrobe lists what the built character is wearing now. Inspect opens a recipe, 0/1 turns it off and on, and " +
            "X takes it off this character only. Additive Recipes are listed below it.";

        private const string AnimationHelp =
            "Race Animation Controllers: the animator controller for each race. When the race changes, the controller for the new race is used.\n\n" +
            "Keep Avatar reuses the existing Mecanim avatar. Keep Animator Controller leaves the Animator Controller alone when the race changes. " +
            "Raw Avatar assigns a specific Mecanim avatar. Disable Animation turns off animator creation and animation for this avatar. " +
            "Force Rebind Animator rebinds the Animator after each build. Recreate Animator On Race Change destroys and recreates the Animator " +
            "when the race changes.\n\n" +
            "Initialize Animator When Added sets up a newly added Animator with Apply Root Motion, Animate Physics, Update Mode, Culling Mode " +
            "and Fire Events.";

        private const string GenerationAdvancedHelp =
            "Editor Time Generation keeps the scene preview in step while you edit. Turn it off to make several changes before rebuilding.\n\n" +
            "Generate UMA rebuilds only the parts that are switched on (Rig, Texture, Mesh). Regenerate Avatar rebuilds it from the current " +
            "race, wardrobe, colors and DNA.\n\n" +
            "Save Preset and Load Preset write and read .umapreset files. Save AvatarDef exports the avatar's current appearance as an .adf file. " +
            "In Play mode, Full Build, Textures, DNA and Mesh force those parts to rebuild, and Save and Load Avatar Definition write and read .adf files.";

        private const string BuildBehaviourHelp =
            "Build Character Enabled: builds the character when a recipe is loaded or the race changes. Turn it off to load several recipes, " +
            "then turn it on again to build once. Normally on.\n\n" +
            "Always Rebuild Skeleton: cleans up the skeleton on every build. Use it when slots add extra bones, so they do not pile up.";

        private const string PerformanceHelp =
            "Inherit Generator Quality and Inherit Generator Reuse Policy take those settings from the UMA generator.\n\n" +
            "Share generated meshes, Use Completed NPC Builds and Share atlases / matching materials let matching characters share what was " +
            "built for them. This applies on the next build. Characters share immutable outputs, not skeletons or animation. Use " +
            "MaterialPropertyBlock for per-character shading, and make a shared resource unique before editing it directly. Turning reuse " +
            "off does not detach existing outputs until they are rebuilt.\n\n" +
            "Atlas Resolution Scale scales the atlas textures: quality against memory and speed.";

        private const string RenderingHelp =
            "Default Renderer Asset: the renderer settings for this avatar. Empty uses the UMA default renderer.\n\n" +
            "Force Slot Materials makes slots use their own materials instead of the ones resolved from recipes and overlays.\n\n" +
            "Hide stops the avatar being displayed without stopping generation; disable the component to stop generation. Lean Hiding " +
            "hides with fewer resources: textures are destroyed and made again when needed.\n\n" +
            "Mark Not Readable makes the built meshes non-readable to save memory. Turn it off to read mesh data at runtime.\n\n" +
            "Mark Dynamic asks the Jobified and Incremental mesh combiners to mark the generated mesh as dynamic. The Default and bone " +
            "baking combiners ignore it, because a dynamic mesh written the way they write it is much slower to render with GPU skinning. " +
            "Leave it off unless you have measured a benefit.";

        private const string BoundsHelp =
            "Bounds Offset is added to the calculated mesh bounds, to stop the character being culled when it should show. Always Adjust " +
            "Bounds recalculates the bounds on every build. Visualize Renderer Bounds draws the current bounds in the Scene view, and " +
            "Current Renderer Bounds shows them as numbers.";

        private const string AddressablesHelp =
            "Delay Unload delays unloading Addressable assets briefly, so an immediate rebuild does not load them again. Usually off.\n\n" +
            "Bundle Check verifies and loads the Addressable bundles a build needs. Keep it on when using Addressables.";

        private const string EventsHelp =
            "In the order they happen:\n" +
            "Character Start: in Start, after initialization and before the automatic build.\n" +
            "Build Character Begun: at the start of BuildCharacter, before the recipes are merged.\n" +
            "Recipe Updated: after the UMAData recipe has been updated, when the build is ready to be scheduled.\n" +
            "Character Begun: when the build process starts.\n" +
            "Character Dna Updated: during the build, after the DNA has been applied.\n" +
            "Character Created: after the first build has completed. Called once.\n" +
            "Character Updated: after every completed build.\n" +
            "Wardrobe Suppressed: after recipe generation, with the recipes other items suppressed.\n" +
            "Slots Hidden: after recipe generation, with the slots other items hid.\n" +
            "Wardrobe Added and Wardrobe Removed: after a wardrobe recipe is added to or removed from the character.\n" +
            "Animator State Saved and Animator State Restored: after the animator state is saved or restored.\n" +
            "Character Destroyed: when the character is destroyed.";

        private const string DiagnosticsHelp =
            "Tools for testing and debugging; none of them is needed to set up a character.\n\n" +
            "Loop Build 10x builds the character ten times, with the Rig, Texture and Mesh choices from Generation. Log Cache Diagnostics " +
            "writes the shared resource cache's counters to the Console.\n\n" +
            "Manual Mesh Modifiers apply MeshModifier assets to the built character, to try them in the editor. They are not saved with the avatar.\n\n" +
            "UMA Data shows the generated data of the built character.\n\n" +
            "Placeholder: Show Placeholder draws a stand-in model in the editor while the avatar is not built. Preview Model chooses it " +
            "(Custom uses Custom Model, turned by Custom Rotation), and Preview Color colors it.";

        private const string LegacyHelp =
            "Loading and saving recipes the UMA 2 way. New projects use Avatar Definitions and presets instead (in Generation).\n\n" +
            "Load Path Type: where recipes are loaded from. Load String is the recipe text, Load Path the folder, and Load Filename the " +
            "file (optional). Load On Start loads it in Start. Load Options are the default flags for loading, which LoadFromRecipe and " +
            "LoadFromString can override.\n\n" +
            "Save Path Type: where recipes are saved. Save Path is the folder and Save Filename the file. Save Options are the default " +
            "flags for saving, which DoSave can override; Ensure Shared Colors includes the shared colors, and Make Unique Filename " +
            "adds a unique suffix.\n\n" +
            "In Play mode, Perform Load and Perform Save run them.";

        /// <summary>
        /// A section in Standard View; a foldable panel in Advanced View. <paramref name="open"/> says whether to draw
        /// its contents.
        /// </summary>
        private IDisposable Panel(string title, string help, bool standard, bool openByDefault, out bool open)
        {
            if (standard)
            {
                open = true;
                return inspectorView.Section(title, help);
            }
            UMAInspectorView.CollapsibleScope scope = inspectorView.CollapsibleSection(title, help, openByDefault);
            open = scope.Expanded;
            return scope;
        }

        /// <summary>A line of small bold text, to head a group inside a panel.</summary>
        private static void SubHeading(string text)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(text, EditorStyles.miniBoldLabel);
        }

        private bool BuiltOrPlaying => Application.isPlaying || thisDCA.editorTimeGeneration;

        /// <summary>The panels below Body shape & DNA.</summary>
        private bool DrawAdvancedPanels()
        {
            bool wasChanged = false;
            DrawAnimationPanel();
            wasChanged |= DrawGenerationPanel();
            DrawBuildBehaviourPanel();
            DrawPerformancePanel();
            DrawRenderingPanel();
            DrawBoundsPanel();
#if UMA_ADDRESSABLES
            DrawAddressablesPanel();
#endif
            DrawEventsPanel();
            DrawDiagnosticsPanel();
            DrawLegacyPanel();
            return wasChanged;
        }

        /// <summary>The Advanced View additions to Character & race: User Information and the race change options.</summary>
        private bool DrawAdvancedRaceExtras()
        {
            bool wasChanged = false;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("userInformation"));

            SubHeading("When the race changes");
            SerializedProperty defaultChangeRaceOptions = serializedObject.FindProperty("defaultChangeRaceOptions");
            EditorGUILayout.PropertyField(defaultChangeRaceOptions, new GUIContent("Race Change Options",
                "The default options for when the race is changed. A call to ChangeRace can override them."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cacheCurrentState"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rebuildSkeleton"));
            if (EditorGUI.EndChangeCheck())
            {
                wasChanged = true;
            }
            return wasChanged;
        }

        /// <summary>The Advanced View additions to Wardrobe: utility recipes and what the built character wears.</summary>
        private bool DrawAdvancedWardrobeExtras()
        {
            bool wasChanged = false;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("umaAdditionalRecipes"),
                new GUIContent("Additional Utility Recipes", "Recipes added every time the character is generated, like the capsule collider recipe."), true);
            if (EditorGUI.EndChangeCheck())
            {
                wasChanged = true;
            }

            SubHeading("Current Wardrobe");
            if (BuiltOrPlaying)
            {
                DoShowWardrobeGUI();
            }
            else
            {
                EditorGUILayout.LabelField("Shown when the character is built: turn on Editor Time Generation, or enter Play mode.", EditorStyles.wordWrappedMiniLabel);
            }
            return wasChanged;
        }

        private void DrawAnimationPanel()
        {
            using (Panel("Animation", AnimationHelp, false, false, out bool open))
            {
                if (open) ShowAnimatorGUI(thisDCA);
            }
        }

        private bool DrawGenerationPanel()
        {
            bool wasChanged = false;
            using (Panel("Generation", GenerationAdvancedHelp, false, false, out bool open))
            {
                if (!open) return false;
                if (Application.isPlaying)
                {
                    SubHeading("Force Regenerate (Play mode)");
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
                    EditorGUILayout.Space(4f);
                }
                wasChanged = DrawGenerationControls(false);
            }
            return wasChanged;
        }

        private void DrawBuildBehaviourPanel()
        {
            using (Panel("Build behaviour", BuildBehaviourHelp, false, false, out bool open))
            {
                if (!open) return;
                // Build Character Enabled goes through its property, which has side effects.
                SerializedProperty buildCharacterEnabled = serializedObject.FindProperty("_buildCharacterEnabled");
                bool current = buildCharacterEnabled.boolValue;
                EditorGUI.BeginChangeCheck();
                bool requested = EditorGUILayout.Toggle(new GUIContent(buildCharacterEnabled.displayName,
                    "Builds the character when a recipe is loaded or the race changes. Turn it off to load several recipes, then on again to build once."), current);
                if (EditorGUI.EndChangeCheck())
                {
                    if (requested != current)
                    {
                        thisDCA.BuildCharacterEnabled = requested;
                    }
                    serializedObject.ApplyModifiedProperties();
                }
                EditorGUILayout.PropertyField(serializedObject.FindProperty("alwaysRebuildSkeleton"));
            }
        }

        private void DrawPerformancePanel()
        {
            using (Panel("Performance & sharing", PerformanceHelp, false, false, out bool open))
            {
                if (!open) return;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("inheritGeneratorQuality"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("inheritGeneratorReusePolicy"));
                EditorGUILayout.PropertyField(reuseGeneratedMeshes, ReuseMeshesLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("useNPCBuilds"), new GUIContent("Use Completed NPC Builds"));
                EditorGUILayout.PropertyField(reuseGeneratedTextures, ReuseTexturesLabel);
                if ((reuseGeneratedMeshes.boolValue || reuseGeneratedTextures.boolValue) &&
                    thisDCA.umaData != null && !string.IsNullOrEmpty(thisDCA.umaData.resourceReuseStatus))
                {
                    EditorGUILayout.LabelField(thisDCA.umaData.resourceReuseStatus, EditorStyles.wordWrappedMiniLabel);
                }
                EditorGUILayout.PropertyField(serializedObject.FindProperty("AtlasResolutionScale"));
            }
        }

        private void DrawRenderingPanel()
        {
            using (Panel("Rendering", RenderingHelp, false, false, out bool open))
            {
                if (!open) return;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultRendererAsset"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("forceSlotMaterials"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("hide"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("leanHiding"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("markNotReadable"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("markDynamic"));
            }
        }

        private void DrawBoundsPanel()
        {
            using (Panel("Bounds", BoundsHelp, false, false, out bool open))
            {
                if (!open) return;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("BoundsOffset"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("alwaysAdjustBounds"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("visualizeRendererBounds"));
                SubHeading("Current Renderer Bounds");
                DrawCurrentRendererBounds();
            }
        }

#if UMA_ADDRESSABLES
        private void DrawAddressablesPanel()
        {
            using (Panel("Addressables", AddressablesHelp, false, false, out bool open))
            {
                if (!open) return;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("DelayUnload"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("BundleCheck"));
            }
        }
#endif

        private void DrawEventsPanel()
        {
            using (Panel("Events", EventsHelp, false, false, out bool open))
            {
                if (open) DoEventsGUI(serializedObject.FindProperty("CharacterCreated"));
            }
        }

        private void DrawDiagnosticsPanel()
        {
            using (Panel("Diagnostics", DiagnosticsHelp, false, false, out bool open))
            {
                if (!open) return;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Loop Build 10x", "Build the character ten times, with the Rig, Texture and Mesh choices from Generation.")))
                {
                    LoopBuild();
                }
                if (GUILayout.Button(new GUIContent("Log Cache Diagnostics", "Write the shared resource cache's counters to the Console.")))
                {
                    var cache = UMAGeneratedResourceCache.Shared;
                    Debug.Log($"UMA reuse: {cache.EntryCount} entries, {cache.ReferenceCount} references, {cache.Hits} hits, {cache.Misses} misses, {cache.PendingJoins} pending joins, {cache.Publications} outputs built.\n{cache.DescribeEntries()}", thisDCA);
                }
                EditorGUILayout.EndHorizontal();

                SubHeading("Manual Mesh Modifiers (edit time test only)");
                if (BuiltOrPlaying)
                {
                    DoUtilitiesGUI();
                }
                else
                {
                    EditorGUILayout.LabelField("Shown when the character is built: turn on Editor Time Generation, or enter Play mode.", EditorStyles.wordWrappedMiniLabel);
                }

                SubHeading("UMA Data");
                showUMAData = EditorGUILayout.Foldout(showUMAData, "Generated Data", true);
                if (showUMAData)
                {
                    if (innerEditor == null) innerEditor = Editor.CreateEditor(thisDCA, typeof(UMADataEditor));
                    if (innerEditor != null)
                    {
                        innerEditor.OnInspectorGUI();
                    }
                }

                SubHeading("Placeholder");
                DoGizmosUI(serializedObject.FindProperty("showPlaceholder"), serializedObject.FindProperty("previewModel"),
                    serializedObject.FindProperty("customModel"), serializedObject.FindProperty("customRotation"),
                    serializedObject.FindProperty("previewColor"));
            }
        }

        private void DrawLegacyPanel()
        {
            using (Panel("Legacy Load/Save", LegacyHelp, false, false, out bool open))
            {
                if (open) DoLegacyLoadSave(serializedObject.FindProperty("loadPathType"));
            }
        }
    }
}
