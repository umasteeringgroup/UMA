using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Editors
{
    /// <summary>
    /// Dockable Scene View toolbar for frequently used UMA authoring and
    /// diagnostics commands.
    /// </summary>
    [Overlay(typeof(SceneView), "UMA Toolbar", true)]
    public sealed class UMAToolbarOverlay : ToolbarOverlay
    {
        public UMAToolbarOverlay() : base(
            SaveSceneViewCameraButton.Id,
            RestoreSceneViewCameraButton.Id,
            RebuildSelectedUMAsButton.Id,
            RebuildSelectedModeDropdown.Id,
            RebuildAllUMAsButton.Id,
            MeshCombinerDropdown.Id,
            UMAFocusDropdown.Id,
            UMASkeletonDropdown.Id,
            PauseUMAEditorGenerationToggle.Id,
            UMADiagnosticsButton.Id,
            UMAToolsDropdown.Id)
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class SaveSceneViewCameraButton : EditorToolbarButton
    {
        public const string Id = "UMA/Toolbar/SaveSceneViewCamera";

        public SaveSceneViewCameraButton()
        {
            icon = EditorGUIUtility.IconContent("d_SceneViewCamera").image as Texture2D;
            tooltip = "Save Scene View Camera";
            clicked += () => UMAEditorTools.SaveCameraState(SceneView.lastActiveSceneView);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class RestoreSceneViewCameraButton : EditorToolbarButton
    {
        public const string Id = "UMA/Toolbar/RestoreSceneViewCamera";

        public RestoreSceneViewCameraButton()
        {
            icon = EditorGUIUtility.IconContent("d_ViewToolOrbit").image as Texture2D;
            tooltip = "Restore Scene View Camera";
            clicked += () => UMAEditorTools.RestoreCameraState(SceneView.lastActiveSceneView);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class RebuildSelectedUMAsButton : EditorToolbarButton
    {
        public const string Id = "UMA/Toolbar/RebuildSelectedUMAs";

        public RebuildSelectedUMAsButton()
        {
            icon = EditorGUIUtility.IconContent("d_Refresh").image as Texture2D;
            tooltip = "Full rebuild of selected UMA characters";
            clicked += () => UMAToolbarActions.RebuildSelected(UMASelectedRebuildMode.Full);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class RebuildSelectedModeDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/Toolbar/RebuildSelectedMode";

        public RebuildSelectedModeDropdown()
        {
            icon = EditorGUIUtility.IconContent("d_Refresh").image as Texture2D;
            tooltip = "Choose how to rebuild selected UMA characters";
            clicked += ShowRebuildMenu;
        }

        private void ShowRebuildMenu()
        {
            var menu = new GenericMenu();
            AddRebuildItem(menu, "Full Rebuild", UMASelectedRebuildMode.Full);
            menu.AddSeparator(string.Empty);
            AddRebuildItem(menu, "Rig / DNA Only", UMASelectedRebuildMode.RigOnly);
            AddRebuildItem(menu, "Mesh Only", UMASelectedRebuildMode.MeshOnly);
            AddRebuildItem(menu, "Textures Only", UMASelectedRebuildMode.TexturesOnly);
            menu.DropDown(worldBound);
        }

        private static void AddRebuildItem(
            GenericMenu menu,
            string label,
            UMASelectedRebuildMode mode)
        {
            menu.AddItem(
                new GUIContent(label, UMAToolbarActions.GetRebuildModeLabel(mode)),
                false,
                () => UMAToolbarActions.RebuildSelected(mode));
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class RebuildAllUMAsButton : EditorToolbarButton
    {
        public const string Id = "UMA/Toolbar/RebuildAllUMAs";

        public RebuildAllUMAsButton()
        {
            icon = EditorGUIUtility.IconContent("d_Avatar Icon").image as Texture2D;
            tooltip = "Rebuild all editor-enabled UMAs in the active scene";
            clicked += RebuildAllUMAs;
        }

        private static void RebuildAllUMAs()
        {
            if (!EditorApplication.isPlaying)
            {
                UMAGeneratorBuiltinEditor.RebuildAllEditorUMA();
            }
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MeshCombinerDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/Toolbar/MeshCombiner";

        public MeshCombinerDropdown()
        {
            icon = EditorGUIUtility.IconContent("d_Settings").image as Texture2D;
            clicked += ShowCombinerMenu;
            UpdateDisplay();
            schedule.Execute(UpdateDisplay).Every(1000L);
        }

        private void UpdateDisplay()
        {
            string combinerName = UMAToolbarActions.GetCurrentCombinerName(UMAToolbarActions.GetGenerator());
            text = GetShortCombinerName(combinerName);
            tooltip = "Active UMA mesh combiner: " + combinerName;
        }

        private void ShowCombinerMenu()
        {
            UMAGenerator generator = UMAToolbarActions.GetGenerator();
            var menu = new GenericMenu();
            if (generator == null)
            {
                menu.AddDisabledItem(new GUIContent("No UMA Generator Found"));
                menu.AddItem(
                    new GUIContent("Open Global Library"),
                    false,
                    () => EditorApplication.ExecuteMenuItem("UMA/Global Library"));
                menu.DropDown(worldBound);
                return;
            }

            AddCombinerItem<UMAJobifiedMeshCombiner>(menu, generator, "Jobified");
            AddCombinerItem<UMAIncrementalMeshCombiner>(menu, generator, "Incremental");
            AddCombinerItem<UMADefaultMeshCombiner>(menu, generator, "Default");
            AddCombinerItem<UMADefaultBoneBakingMeshCombiner>(menu, generator, "Default Bone Baking");
            AddCombinerItem<UMABoneBakingMeshCombiner>(menu, generator, "Bone Baking Compatibility");
            menu.AddSeparator(string.Empty);
            menu.AddItem(
                new GUIContent("Open Mesh Combiner Window"),
                false,
                () => EditorApplication.ExecuteMenuItem("UMA/Tools/Mesh Tools/Mesh Combiner Switcher"));
            menu.DropDown(worldBound);
        }

        private void AddCombinerItem<T>(GenericMenu menu, UMAGenerator generator, string label)
            where T : UMAMeshCombiner
        {
            menu.AddItem(
                new GUIContent(label),
                UMAToolbarActions.IsCurrentCombiner<T>(generator),
                () =>
                {
                    UMAToolbarActions.UseMeshCombiner<T>(generator);
                    UpdateDisplay();
                });
        }

        private static string GetShortCombinerName(string combinerName)
        {
            switch (combinerName)
            {
                case "Default Bone Baking":
                    return "Bone Bake";
                case "Bone Baking Compatibility":
                    return "BB Compat";
                default:
                    return combinerName;
            }
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class UMAFocusDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/Toolbar/Focus";

        public UMAFocusDropdown()
        {
            icon = EditorGUIUtility.IconContent("d_ViewToolZoom").image as Texture2D;
            tooltip = "Focus or select parts of the active UMA";
            clicked += ShowFocusMenu;
        }

        private void ShowFocusMenu()
        {
            DynamicCharacterAvatar avatar = UMAToolbarActions.GetActiveAvatar();
            var menu = new GenericMenu();
            if (avatar == null)
            {
                menu.AddDisabledItem(new GUIContent("Select an UMA Character"));
                menu.DropDown(worldBound);
                return;
            }

            AddTarget(menu, "Frame Character", avatar.gameObject);
            AddTarget(menu, "Select UMA Data", avatar);
            AddTarget(menu, "Select UMA Root", avatar.umaRoot);
            menu.AddSeparator(string.Empty);
            AddTarget(menu, "Select Hips", UMAToolbarActions.GetHips(avatar));
            AddTarget(menu, "Select Root Bone", UMAToolbarActions.GetRootBone(avatar));
            AddTarget(menu, "Select First Renderer", UMAToolbarActions.GetFirstRenderer(avatar));
            menu.AddSeparator(string.Empty);
            UMAGenerator generator = UMAToolbarActions.GetGenerator();
            AddTarget(menu, "Select Generator", generator != null ? generator.gameObject : null);
            menu.DropDown(worldBound);
        }

        private static void AddTarget(GenericMenu menu, string label, Object target)
        {
            if (target == null)
            {
                menu.AddDisabledItem(new GUIContent(label + " (Not Available)"));
                return;
            }

            menu.AddItem(
                new GUIContent(label),
                false,
                () => UMAToolbarActions.SelectAndFrame(target));
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class UMASkeletonDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/Toolbar/Skeleton";

        public UMASkeletonDropdown()
        {
            icon = EditorGUIUtility.IconContent("d_AvatarPivot").image as Texture2D;
            clicked += ShowSkeletonMenu;
            UpdateTooltip();
            schedule.Execute(UpdateTooltip).Every(1000L);
        }

        private void UpdateTooltip()
        {
            tooltip = UMAToolbarSkeletonRenderer.ShowSkeleton
                ? "Selected UMA skeleton is visible"
                : "Show the selected UMA skeleton";
        }

        private void ShowSkeletonMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("Show Selected Skeleton"),
                UMAToolbarSkeletonRenderer.ShowSkeleton,
                () =>
                {
                    UMAToolbarSkeletonRenderer.ShowSkeleton = !UMAToolbarSkeletonRenderer.ShowSkeleton;
                    UpdateTooltip();
                });
            menu.AddItem(
                new GUIContent("Show Bone Names"),
                UMAToolbarSkeletonRenderer.ShowBoneNames,
                () => UMAToolbarSkeletonRenderer.ShowBoneNames = !UMAToolbarSkeletonRenderer.ShowBoneNames);
            menu.DropDown(worldBound);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class PauseUMAEditorGenerationToggle : EditorToolbarToggle
    {
        public const string Id = "UMA/Toolbar/PauseEditorGeneration";

        public PauseUMAEditorGenerationToggle()
        {
            icon = EditorGUIUtility.IconContent("d_PauseButton").image as Texture2D;
            value = DynamicCharacterAvatar.EditorGenerationPaused;
            UpdateTooltip(value);
            RegisterCallback<ChangeEvent<bool>>(OnValueChanged);
            schedule.Execute(SyncState).Every(1000L);
        }

        private void OnValueChanged(ChangeEvent<bool> changeEvent)
        {
            DynamicCharacterAvatar.EditorGenerationPaused = changeEvent.newValue;
            UpdateTooltip(changeEvent.newValue);
        }

        private void SyncState()
        {
            bool paused = DynamicCharacterAvatar.EditorGenerationPaused;
            if (value != paused)
            {
                SetValueWithoutNotify(paused);
            }
            UpdateTooltip(paused);
        }

        private void UpdateTooltip(bool paused)
        {
            tooltip = paused
                ? "Automatic UMA editor generation is paused; explicit toolbar rebuilds still work"
                : "Pause automatic UMA editor generation";
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class UMADiagnosticsButton : EditorToolbarButton
    {
        public const string Id = "UMA/Toolbar/Diagnostics";

        public UMADiagnosticsButton()
        {
            icon = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image as Texture2D;
            tooltip = "Inspect the selected UMA's mesh, skeleton, generator, and build status";
            clicked += UMAToolbarDiagnosticsWindow.OpenWindow;
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class UMAToolsDropdown : EditorToolbarDropdown
    {
        public const string Id = "UMA/Toolbar/Tools";

        public UMAToolsDropdown()
        {
            icon = EditorGUIUtility.IconContent("d_ToolHandleGlobal").image as Texture2D;
            tooltip = "Open UMA editor tools";
            clicked += ShowToolsMenu;
        }

        private void ShowToolsMenu()
        {
            var menu = new GenericMenu();
            AddMenuCommand(menu, "Quick Finder", "UMA/Asset Management/Quick Finder");
            AddMenuCommand(menu, "Global Library", "UMA/Global Library");
            AddMenuCommand(menu, "Mesh Combiner Switcher", "UMA/Tools/Mesh Tools/Mesh Combiner Switcher");
            AddMenuCommand(menu, "Runtime Data Viewer", "UMA/Debug/Runtime Data Viewer");
            menu.AddSeparator(string.Empty);
            AddMenuCommand(menu, "Race Smoke Test", "UMA/Testing/Race Smoke Test...");
            AddMenuCommand(menu, "Run UMA Editor Tests", "UMA/Testing/Run UMA Editor Tests");
            AddMenuCommand(menu, "Open Unity Test Runner", "UMA/Testing/Open Unity Test Runner");
            menu.DropDown(worldBound);
        }

        private static void AddMenuCommand(GenericMenu menu, string label, string menuPath)
        {
            menu.AddItem(
                new GUIContent(label),
                false,
                () =>
                {
                    if (!EditorApplication.ExecuteMenuItem(menuPath))
                    {
                        Debug.LogWarning("[UMA Toolbar] Menu command was not found: " + menuPath);
                    }
                });
        }
    }
}
