using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// A workflow that a plugin adds to the Vertex Editor, next to Sculpt, Vertex Paint, Advanced and
    /// Blendshapes. While it is the active workflow it owns the tools panel in the Scene view, the
    /// body of the Vertex Editor window and the Scene view's input; the built-in brushes and vertex
    /// selection are off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register one instance with <see cref="VertexEditorWorkflows.Register"/>, usually from an
    /// <c>[InitializeOnLoad]</c> class. The same instance serves every stage, so keep what belongs to
    /// one editing session with the stage it is given, and let go of it in
    /// <see cref="OnStageClosing"/>.
    /// </para>
    /// <para>
    /// The workflow may add its own objects to <c>stage.scene</c>. It must not change the stage's
    /// baked body mesh: the built-in workflows track their edits by vertex index into it.
    /// </para>
    /// <para>
    /// An exception thrown from a callback is logged and the Vertex Editor goes back to a built-in
    /// workflow, so a failing plugin cannot leave the editor unusable.
    /// </para>
    /// </remarks>
    public abstract class VertexEditorWorkflow
    {
        /// <summary>
        /// A name that never changes, for example "com.mycompany.mytool"; it is what
        /// <c>VertexEditorStage.ActivateWorkflow</c> takes. It is not shown to the user. The Vertex
        /// Editor does not remember a plugin workflow: it always opens on one of its own.
        /// </summary>
        public abstract string Id { get; }

        /// <summary>The button on the workflow bar: its label and what it is for.</summary>
        public abstract GUIContent ToolbarContent { get; }

        /// <summary>Where the button stands among the plugin workflows. Lower comes first.</summary>
        public virtual int Order => 100;

        /// <summary>False hides the button for this stage (for example a character the workflow cannot work on).</summary>
        public virtual bool IsAvailable(VertexEditorStage stage) => true;

        /// <summary>The workflow became the active one.</summary>
        public virtual void OnActivate(VertexEditorStage stage) { }

        /// <summary>Another workflow was chosen, or the stage is closing.</summary>
        public virtual void OnDeactivate(VertexEditorStage stage) { }

        /// <summary>Draws the workflow's controls in the tools panel of the Scene view (IMGUI layout).</summary>
        public virtual void OnToolsGUI(VertexEditorStage stage) { }

        /// <summary>Draws the workflow's part of the Vertex Editor window, under the workflow bar (IMGUI layout).</summary>
        public virtual void OnWindowGUI(VertexEditorStage stage) { }

        /// <summary>
        /// The Scene view's events while the workflow is active: draw handles and take input here.
        /// </summary>
        /// <param name="pointerOverPanel">The pointer is over one of the editor's panels; leave the event alone.</param>
        public virtual void OnSceneGUI(VertexEditorStage stage, SceneView sceneView, bool pointerOverPanel) { }

        /// <summary>
        /// The character was generated again (its recipe, DNA or pose changed) and the stage has a new
        /// baked body. Anything measured on the old body is out of date.
        /// </summary>
        public virtual void OnCharacterRebuilt(VertexEditorStage stage) { }

        /// <summary>True when leaving now would lose work.</summary>
        public virtual bool HasUnsavedChanges(VertexEditorStage stage) => false;

        /// <summary>What is not saved, for the leave dialog: one short sentence the user will recognise.</summary>
        public virtual string DescribeUnsavedChanges(VertexEditorStage stage) => null;

        /// <summary>The label of the leave dialog's save button.</summary>
        public virtual string SaveLabel => "Save";

        /// <summary>What saving does, shown under the save button in the leave dialog.</summary>
        public virtual string SaveHelp => string.Empty;

        /// <summary>
        /// Saves the unsaved work. Called from the leave dialog. Return false when nothing was saved
        /// (the user cancelled a file picker, or the write failed): the dialog then asks again.
        /// </summary>
        public virtual bool SaveChanges(VertexEditorStage stage) => true;

        /// <summary>The user chose to leave without saving.</summary>
        public virtual void DiscardChanges(VertexEditorStage stage) { }

        /// <summary>The stage is closing. Release everything made for it.</summary>
        public virtual void OnStageClosing(VertexEditorStage stage) { }
    }

    /// <summary>The workflows plugins have added to the Vertex Editor.</summary>
    public static class VertexEditorWorkflows
    {
        private static readonly List<VertexEditorWorkflow> workflows = new List<VertexEditorWorkflow>();
        private static readonly string[] builtInNames = { "Sculpt", "VertexPaint", "AdvancedVertexAdjustments", "BlendshapeExtraction" };

        /// <summary>The registered workflows, in the order their buttons are drawn.</summary>
        public static IReadOnlyList<VertexEditorWorkflow> All => workflows;

        /// <summary>
        /// Adds a workflow to the Vertex Editor. Registering an id again replaces the earlier
        /// instance, so a script reload cannot leave two buttons.
        /// </summary>
        /// <returns>False when the workflow has no usable id.</returns>
        public static bool Register(VertexEditorWorkflow workflow)
        {
            if (workflow == null)
            {
                return false;
            }
            string id = workflow.Id;
            if (string.IsNullOrWhiteSpace(id) || Array.IndexOf(builtInNames, id) >= 0)
            {
                Debug.LogWarning("[UMA] A Vertex Editor workflow needs an id of its own; '" + id + "' cannot be used.");
                return false;
            }
            workflows.RemoveAll(existing => existing == null || string.Equals(existing.Id, id, StringComparison.Ordinal));
            workflows.Add(workflow);
            workflows.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Id, b.Id);
            });
            return true;
        }

        /// <summary>Removes a workflow. A stage that has it active goes back to a built-in workflow first.</summary>
        public static bool Unregister(string id)
        {
            VertexEditorWorkflow workflow = Find(id);
            if (workflow == null)
            {
                return false;
            }
            foreach (VertexEditorStage stage in Resources.FindObjectsOfTypeAll<VertexEditorStage>())
            {
                if (stage != null && stage.ActiveWorkflow == workflow)
                {
                    stage.DeactivateWorkflow();
                }
            }
            return workflows.Remove(workflow);
        }

        /// <summary>The workflow registered under an id, or null.</summary>
        public static VertexEditorWorkflow Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            for (int i = 0; i < workflows.Count; i++)
            {
                if (workflows[i] != null && string.Equals(workflows[i].Id, id, StringComparison.Ordinal))
                {
                    return workflows[i];
                }
            }
            return null;
        }
    }
}
