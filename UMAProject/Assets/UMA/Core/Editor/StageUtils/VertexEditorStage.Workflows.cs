using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Workflows that plugins add to the Vertex Editor (see <see cref="VertexEditorWorkflow"/>), and
    /// what the stage shows of itself to them.
    /// </summary>
    /// <remarks>
    /// A plugin workflow runs with the stage's own tools in the plain selection mode and their
    /// input switched off, so nothing of Sculpt, Vertex Paint or Advanced reacts while it is active.
    /// Choosing a built-in workflow, from the stage's bar or the window's, ends the plugin workflow.
    /// </remarks>
    public partial class VertexEditorStage
    {
        [NonSerialized] private VertexEditorWorkflow activeWorkflow;
        // The built-in workflow that was in use when a plugin workflow took over; it stays the stored choice.
        [NonSerialized] private MeshModifierEditor.AuthoringWorkflow workflowBeforePlugin = MeshModifierEditor.AuthoringWorkflow.Sculpt;
        [NonSerialized] private readonly List<VertexEditorWorkflow> availableWorkflows = new List<VertexEditorWorkflow>();
        [NonSerialized] private GUIContent[] availableWorkflowButtons = new GUIContent[0];

        /// <summary>The plugin workflow that is active, or null while a built-in workflow is.</summary>
        public VertexEditorWorkflow ActiveWorkflow => activeWorkflow;

        /// <summary>The character the stage is editing.</summary>
        public DynamicCharacterAvatar Character => thisDCA;

        /// <summary>The race of the character the stage is editing, or null.</summary>
        public RaceData CharacterRace
        {
            get
            {
                if (thisDCA == null || thisDCA.activeRace == null)
                {
                    return null;
                }
                return thisDCA.activeRace.data != null ? thisDCA.activeRace.data : thisDCA.activeRace.racedata;
            }
        }

        /// <summary>
        /// True when the stage built the character itself (see <see cref="ShowStageForRace"/> and
        /// <see cref="ShowStageForSlots"/>). Such a character stands in the pose its meshes were
        /// modelled in, and goes when the stage closes.
        /// </summary>
        public bool OwnsTemporaryCharacter => ownsTemporaryCharacter;

        /// <summary>The character's generated renderer. The stage keeps it disabled and draws a baked copy.</summary>
        public SkinnedMeshRenderer CharacterRenderer => GetCurrentSkinnedMeshRenderer();

        /// <summary>The tools panel's place in the Scene view, in GUI points.</summary>
        public Rect ToolsPanelRect => leftPanelRect;

        /// <summary>
        /// Generates the character again, for example after its DNA was changed. The stage then shows the
        /// new body (in the modelled pose, for a character the stage built itself) and plugin workflows get
        /// <see cref="VertexEditorWorkflow.OnCharacterRebuilt"/>.
        /// </summary>
        public void RebuildCharacter(bool dnaChanged)
        {
            if (thisDCA == null || thisDCA.umaData == null || closing) return;
            var generator = thisDCA.umaData.umaGenerator as UMAGeneratorBuiltin;
            if (generator == null) return;
            thisDCA.umaData.OnCharacterUpdated -= BuildCollisionMesh;
            thisDCA.umaData.OnCharacterUpdated += BuildCollisionMesh;
            KeepTemporaryCharacterPose();
            thisDCA.umaData.Dirty(dnaChanged, true, true);
            generator.GenerateSingleUMA(thisDCA.umaData, true);
        }

        /// <summary>Redraws the Scene view and the Vertex Editor window.</summary>
        public void RepaintEditors()
        {
            RepaintLinkedEditors();
            SceneView.RepaintAll();
        }

        private bool WorkflowExtensionsAllowed => !slotWeightEditorMode && !touchupWeightsMode && !closing;

        /// <summary>
        /// Makes a plugin workflow the active one. Returns false when no workflow has that id, when it
        /// is not available for this stage, or when the stage is a weight editor.
        /// </summary>
        public bool ActivateWorkflow(string id)
        {
            VertexEditorWorkflow workflow = VertexEditorWorkflows.Find(id);
            if (workflow == null || !WorkflowExtensionsAllowed || !IsWorkflowAvailable(workflow))
            {
                return false;
            }
            if (workflow == activeWorkflow)
            {
                return true;
            }
            if (activeWorkflow == null)
            {
                workflowBeforePlugin = CurrentAuthoringWorkflow();
            }
            DeactivateWorkflow();
            if (sceneToolMode != SceneToolMode.Select)
            {
                // Ends a stroke in progress and puts the stage's own tools in their quiet mode.
                SelectAuthoringWorkflow(MeshModifierEditor.AuthoringWorkflow.AdvancedVertexAdjustments);
            }
            CancelInteraction();
            activeWorkflow = workflow;
            if (!RunWorkflowCallback(workflow, "OnActivate", () => workflow.OnActivate(this)))
            {
                return false;
            }
            RepaintEditors();
            return true;
        }

        /// <summary>Ends the plugin workflow; the stage is left in the Advanced workflow's selection mode.</summary>
        public void DeactivateWorkflow()
        {
            VertexEditorWorkflow workflow = activeWorkflow;
            if (workflow == null)
            {
                return;
            }
            activeWorkflow = null;
            try
            {
                workflow.OnDeactivate(this);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            RepaintEditors();
        }

        private bool IsWorkflowAvailable(VertexEditorWorkflow workflow)
        {
            try
            {
                return workflow.IsAvailable(this);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        /// <summary>
        /// Runs a plugin callback. When it throws, the error is logged and the stage goes back to a
        /// built-in workflow. Returns false in that case.
        /// </summary>
        private bool RunWorkflowCallback(VertexEditorWorkflow workflow, string what, Action callback)
        {
            try
            {
                callback();
                return true;
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError("[UMA] The Vertex Editor workflow '" + workflow.Id + "' failed in " + what +
                    " and was closed. The Vertex Editor is back in Advanced.");
                Debug.LogException(ex);
                if (activeWorkflow == workflow)
                {
                    activeWorkflow = null;
                    try
                    {
                        workflow.OnDeactivate(this);
                    }
                    catch (Exception inner)
                    {
                        Debug.LogException(inner);
                    }
                }
                return false;
            }
        }

        /// <summary>The plugin workflows this stage offers, in button order.</summary>
        internal List<VertexEditorWorkflow> GetAvailableWorkflows()
        {
            availableWorkflows.Clear();
            if (!WorkflowExtensionsAllowed)
            {
                return availableWorkflows;
            }
            IReadOnlyList<VertexEditorWorkflow> all = VertexEditorWorkflows.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && IsWorkflowAvailable(all[i]))
                {
                    availableWorkflows.Add(all[i]);
                }
            }
            return availableWorkflows;
        }

        /// <summary>
        /// The row of plugin workflow buttons, under the built-in workflow bar. Draws nothing when no
        /// plugin has added a workflow. Used by the stage's panel and by the Vertex Editor window.
        /// </summary>
        internal void DrawWorkflowExtensionBar()
        {
            List<VertexEditorWorkflow> workflows = GetAvailableWorkflows();
            if (workflows.Count == 0)
            {
                return;
            }
            if (availableWorkflowButtons.Length != workflows.Count)
            {
                availableWorkflowButtons = new GUIContent[workflows.Count];
            }
            int current = -1;
            for (int i = 0; i < workflows.Count; i++)
            {
                GUIContent content = null;
                try
                {
                    content = workflows[i].ToolbarContent;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
                availableWorkflowButtons[i] = content ?? new GUIContent(workflows[i].Id);
                if (workflows[i] == activeWorkflow)
                {
                    current = i;
                }
            }
            int requested = GUILayout.Toolbar(current, availableWorkflowButtons, GUI.skin.button, GUI.ToolbarButtonSize.FitToContents);
            if (requested != current && requested >= 0 && requested < workflows.Count)
            {
                ActivateWorkflow(workflows[requested].Id);
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>The active plugin workflow's controls in the tools panel.</summary>
        private void DrawActiveWorkflowTools()
        {
            VertexEditorWorkflow workflow = activeWorkflow;
            if (workflow == null)
            {
                return;
            }
            if (!RunWorkflowCallback(workflow, "OnToolsGUI", () => workflow.OnToolsGUI(this)))
            {
                // The layout the plugin started is abandoned with the event.
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>The active plugin workflow's part of the Vertex Editor window. False when none is active.</summary>
        internal bool DrawActiveWorkflowWindow()
        {
            VertexEditorWorkflow workflow = activeWorkflow;
            if (workflow == null)
            {
                return false;
            }
            if (!RunWorkflowCallback(workflow, "OnWindowGUI", () => workflow.OnWindowGUI(this)))
            {
                GUIUtility.ExitGUI();
            }
            return true;
        }

        private void RunActiveWorkflowSceneGUI(SceneView sceneView, bool pointerOverPanel)
        {
            VertexEditorWorkflow workflow = activeWorkflow;
            if (workflow != null)
            {
                RunWorkflowCallback(workflow, "OnSceneGUI", () => workflow.OnSceneGUI(this, sceneView, pointerOverPanel));
            }
        }

        private void NotifyWorkflowCharacterRebuilt()
        {
            VertexEditorWorkflow workflow = activeWorkflow;
            if (workflow != null)
            {
                RunWorkflowCallback(workflow, "OnCharacterRebuilt", () => workflow.OnCharacterRebuilt(this));
            }
        }

        /// <summary>
        /// Asks about the unsaved work of each plugin workflow before the stage is left. Returns false
        /// when the user chose to stay.
        /// </summary>
        private bool ResolveWorkflowUnsavedWork(bool canStay)
        {
            IReadOnlyList<VertexEditorWorkflow> all = VertexEditorWorkflows.All;
            for (int i = 0; i < all.Count; i++)
            {
                VertexEditorWorkflow workflow = all[i];
                if (workflow == null)
                {
                    continue;
                }
                for (int round = 0; round < 6; round++)
                {
                    bool unsaved;
                    string description = null;
                    try
                    {
                        unsaved = workflow.HasUnsavedChanges(this);
                        if (unsaved)
                        {
                            description = workflow.DescribeUnsavedChanges(this);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        unsaved = false;
                    }
                    if (!unsaved)
                    {
                        break;
                    }
                    if (string.IsNullOrEmpty(description))
                    {
                        GUIContent button = workflow.ToolbarContent;
                        description = "Not saved: changes in " + (button != null ? button.text : workflow.Id) + ".";
                    }
                    var options = new List<VertexEditorLeaveDialog.Option>
                    {
                        new VertexEditorLeaveDialog.Option { id = 0, label = workflow.SaveLabel, help = workflow.SaveHelp, enabled = true }
                    };
                    int choice = VertexEditorLeaveDialog.Ask(description, options, canStay);
                    if (choice == VertexEditorLeaveDialog.Stay)
                    {
                        return false;
                    }
                    if (choice == VertexEditorLeaveDialog.LeaveWithoutSaving)
                    {
                        try
                        {
                            workflow.DiscardChanges(this);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                        }
                        break;
                    }
                    try
                    {
                        // Whatever the save did, ask again about what is left.
                        workflow.SaveChanges(this);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
            return true;
        }

        /// <summary>The stage is closing: the active plugin workflow ends and every workflow lets go of the stage.</summary>
        private void CloseWorkflows()
        {
            VertexEditorWorkflow active = activeWorkflow;
            activeWorkflow = null;
            if (active != null)
            {
                try
                {
                    active.OnDeactivate(this);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            IReadOnlyList<VertexEditorWorkflow> all = VertexEditorWorkflows.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null)
                {
                    continue;
                }
                try
                {
                    all[i].OnStageClosing(this);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        /// <summary>
        /// Leaves a Vertex Editor that is open, the way its Close button does: unsaved work is asked
        /// about first. Every way of opening the editor calls this, because opening a second one over
        /// the first closed both (they share the Vertex Editor window, and the first one closing took
        /// the window, and with it the second editor, along).
        /// </summary>
        /// <returns>False when the user chose to stay in the open editor. True when none was open or it was left.</returns>
        public static bool CloseOpenVertexEditor()
        {
            var open = UnityEditor.SceneManagement.StageUtility.GetCurrentStage() as VertexEditorStage;
            if (open == null)
            {
                return true;
            }
            DynamicCharacterAvatar sceneCharacter = open.ownsTemporaryCharacter || open.ownsSlotWeightPreviewAvatar ? null : open.thisDCA;
            open.RequestClose();
            if (UnityEditor.SceneManagement.StageUtility.GetCurrentStage() is VertexEditorStage)
            {
                return false;
            }
            if (sceneCharacter != null)
            {
                // Leaving queued a rebuild of the scene's character. It must run before another editor
                // takes that character's materials, or the rebuild destroys them under it.
                sceneCharacter.RunQueuedEditorGeneration();
            }
            return true;
        }

        /// <summary>
        /// Opens the editor on a temporary character of a race, built from the race's base recipe with
        /// its default DNA and standing in the pose its meshes were modelled in. Nothing needs to be in
        /// the scene. The character belongs to the stage and goes when the stage closes.
        /// </summary>
        /// <param name="message">Why the editor could not be opened, or null.</param>
        public static VertexEditorStage ShowStageForRace(RaceData race, out string message)
        {
            return ShowStageOnTemporaryCharacter(race, null, false, out message);
        }
    }
}
