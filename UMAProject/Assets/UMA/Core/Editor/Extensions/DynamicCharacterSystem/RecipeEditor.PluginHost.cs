using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    public partial class RecipeEditor : IUMAPluginHost, CharacterBaseEditor.IRecipeWorkingCopyOwner
    {
        // The slot and overlay controls write this inspector's working copy themselves (Add Overlay, Remove
        // Overlay, the overlay positioner). They ask here first, and report back, so the edit counts as
        // pending and the revision stays the one this inspector wrote.
        Object CharacterBaseEditor.IRecipeWorkingCopyOwner.WorkingCopyAsset => isDisposed ? null : target;
        UMAData.UMARecipe CharacterBaseEditor.IRecipeWorkingCopyOwner.WorkingCopy => _recipe;
        bool CharacterBaseEditor.IRecipeWorkingCopyOwner.WorkingCopyInSync => Initialized && !pluginRecipeConflict &&
            target != null && (pluginRecipeRevision == null || CurrentPluginRecipeRevision() == pluginRecipeRevision);

        bool CharacterBaseEditor.IRecipeWorkingCopyOwner.WritesToDisk => SaveEditsToDisk;

        void CharacterBaseEditor.IRecipeWorkingCopyOwner.OnWorkingCopyWritten(bool saved)
        {
            RecordPluginRecipeRevision();
            // The control may have changed the slot list (Add Slot on a slot row) and then left the draw
            // (ExitGUI), so the rows are rebuilt from the recipe: the added slot had no row.
            _rebuildOnLayout = true;
            Repaint();
            if (HostAutoSave != null && !SaveEditsToDisk && target is UMATextRecipe textRecipe)
                UMAUpdateProcessor.UpdateRecipe(textRecipe);
            if (!ProcessEditsImmediately)
            {
                // Not on disk yet: pending until Save Recipe. The control calls ExitGUI before this inspector
                // sees the change, so without this a later outside save replaced the edit without a conflict.
                _needsUpdate = true;
            }
        }

        void CharacterBaseEditor.IRecipeWorkingCopyOwner.OnWorkingCopyEditPending()
        {
            // The asset changed elsewhere: the next draw offers the conflict (keep these edits or reload).
            _needsUpdate = true;
            _rebuildOnLayout = true;
            Repaint();
        }

        private IDisposable pluginPlacement;
        private string pluginRecipeRevision;
        private bool pluginRecipeConflict;
        private int pluginDrawDepth;
        protected bool PluginRecipeConflictResolved => !pluginRecipeConflict;

        public bool CanRunPluginActions => this != null && target != null && targets.Length == 1 &&
            Initialized && !isDisposed && _recipe != null && _errorMessage == null && !pluginRecipeConflict;

        protected virtual bool HasPendingPluginEdits => _needsUpdate || _forceUpdate ||
            (target != null && serializedObject.hasModifiedProperties);

        private string CurrentPluginRecipeRevision() => target is UMATextRecipe text
            ? text.recipeString ?? string.Empty : EditorJsonUtility.ToJson(target);

        private void RecordPluginRecipeRevision()
        {
            if (target == null) return;
            pluginRecipeRevision = CurrentPluginRecipeRevision();
            RememberOwnRecipeRevision(pluginRecipeRevision);
        }

        // Recent recipe states this inspector itself loaded or wrote. Undo and Redo can only return to one
        // of these by undoing this inspector's own edits; any other state came from outside.
        private const int OwnRecipeRevisionLimit = 64;
        private readonly System.Collections.Generic.List<string> ownRecipeRevisions = new System.Collections.Generic.List<string>();

        private void RememberOwnRecipeRevision(string revision)
        {
            if (revision == null) return;
            int last = ownRecipeRevisions.Count - 1;
            if (last >= 0 && ownRecipeRevisions[last] == revision) return;
            ownRecipeRevisions.Remove(revision);
            ownRecipeRevisions.Add(revision);
            if (ownRecipeRevisions.Count > OwnRecipeRevisionLimit) ownRecipeRevisions.RemoveAt(0);
        }

        /// <summary>
        /// With Automatic Updates off, every edit is written into the asset as an unsaved change. When the
        /// asset is no longer dirty but still holds what this inspector wrote, File > Save Project (or another
        /// save) has written the edits: they are no longer pending. The flag left set used to re-pack and
        /// dirty the just-saved recipe when it was deselected, and made a later outside save a conflict.
        /// </summary>
        private void ForgetPendingEditsSavedElsewhere()
        {
            if (ProcessEditsImmediately || _forceUpdate || !_needsUpdate || target == null || pluginRecipeConflict ||
                !EditorUtility.IsPersistent(target) || EditorUtility.IsDirty(target))
                return;
            if (pluginRecipeRevision != null && CurrentPluginRecipeRevision() == pluginRecipeRevision)
                _needsUpdate = false;
        }

        protected bool SynchronizePluginRecipe()
        {
            if (!Initialized || target == null || _recipe == null) return false;
            if (pluginRecipeRevision == null) RecordPluginRecipeRevision();
            ForgetPendingEditsSavedElsewhere();
            if (CurrentPluginRecipeRevision() == pluginRecipeRevision) return !pluginRecipeConflict;
            if (HasPendingPluginEdits)
            {
                pluginRecipeConflict = true;
                return false;
            }
            RefreshAfterPluginAction();
            return _errorMessage == null;
        }

        public virtual bool TryPreparePluginAction()
        {
            if (!CanRunPluginActions || !SynchronizePluginRecipe()) return false;
            serializedObject.ApplyModifiedProperties();
            if (CurrentPluginRecipeRevision() != pluginRecipeRevision && !_needsUpdate && !_forceUpdate)
                RefreshAfterPluginAction();
            if (_needsUpdate || _forceUpdate) DoUpdate();
            _needsUpdate = _forceUpdate = false;
            RecordPluginRecipeRevision();
            return _errorMessage == null;
        }

        public virtual void RefreshAfterPluginAction()
        {
            if (target == null) return;
            serializedObject.Update();
            var loaded = new UMAData.UMARecipe();
            ResetLoadState();
            try
            {
                ((UMARecipeBase)target).Load(loaded);
                _errorMessage = MissingAssetsMessage(loaded);
            }
            catch (UMAResourceNotFoundException exception) { _errorMessage = exception.Message; }
            _recipe = loaded;
            _needsUpdate = _forceUpdate = false;
            pluginRecipeConflict = false;
            _rebuildOnLayout = true;
            RecordPluginRecipeRevision();
            OnPluginRecipeReloaded();
            Repaint();
        }

        protected virtual void OnPluginRecipeReloaded() { }

        /// <summary>
        /// Undo or redo put the recipe back to another state of this inspector's own edits. That is not
        /// an outside change: reload it so the inspector shows it, instead of reporting a conflict (whose
        /// "Use inspector edits" would have re-applied the edit that was just undone). With Automatic
        /// Updates on, the restored recipe is saved like any other edit, exactly as Undo restored it:
        /// re-packing it would also rewrite older fields in their current form.
        /// </summary>
        private void OnUndoRedoRecipe()
        {
            if (!Initialized || isDisposed || target == null || _recipe == null) return;
            // An edit conflict is waiting for the user: Undo must not resolve it by discarding edits.
            if (pluginRecipeConflict) return;
            string current = CurrentPluginRecipeRevision();
            if (pluginRecipeRevision != null && current == pluginRecipeRevision) return;
            // A state this inspector never had came from outside (or the Undo was of another object after
            // an outside change): leave it to the conflict check, which keeps pending edits.
            if (!ownRecipeRevisions.Contains(current)) return;
            RefreshAfterPluginAction();
            if (HostAutoSave == null && _AutomaticUpdates && !RecipeSaveBlocked && EditorUtility.IsPersistent(target))
            {
                AssetDatabase.SaveAssetIfDirty(target);
                if (target is UMATextRecipe textRecipe)
                {
                    UMAUpdateProcessor.UpdateRecipe(textRecipe);
                }
            }
        }

        protected readonly struct PluginInspectorScope : IDisposable
        {
            private readonly RecipeEditor owner;
            internal PluginInspectorScope(RecipeEditor owner) { this.owner = owner; }
            public void Dispose()
            {
                if (--owner.pluginDrawDepth == 0 && !owner.pluginRecipeConflict && owner.Initialized)
                    owner.RecordPluginRecipeRevision();
            }
        }

        protected PluginInspectorScope BeginPluginInspector()
        {
            pluginDrawDepth++;
            try
            {
                if (pluginDrawDepth == 1 && Event.current != null)
                {
                    if (Initialized)
                        SynchronizePluginRecipe();
                    UMAPluginGUI.Draw(this, Initialized && plugins != null && plugins.Count > 0, DrawLegacyPlugins);
                    if (pluginRecipeConflict)
                    {
                        EditorGUILayout.HelpBox("This recipe changed outside this inspector while local edits were pending. " +
                            "Resolve the conflict before editing or opening a plugin.", MessageType.Warning);
                        if (GUILayout.Button("Use inspector edits"))
                        {
                            Undo.RecordObject(target, "Use pending recipe inspector edits");
                            pluginRecipeConflict = false;
                            RecordPluginRecipeRevision();
                            TryPreparePluginAction();
                            GUIUtility.ExitGUI();
                        }
                        if (GUILayout.Button("Reload asset and discard inspector edits"))
                        {
                            RefreshAfterPluginAction();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                return new PluginInspectorScope(this);
            }
            catch { pluginDrawDepth--; throw; }
        }

        private void DrawLegacyPlugins()
        {
            foreach (IUMARecipePlugin plugin in plugins)
            {
                try
                {
                    plugin.foldOut = GUIHelper.FoldoutBar(plugin.foldOut, plugin.GetSectionLabel());
                    if (!plugin.foldOut) continue;
                    GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
                    try { plugin.OnInspectorGUI(serializedObject); }
                    finally { GUIHelper.EndVerticalPadded(10); }
                }
                catch (Exception exception) { UMAPluginDiagnostics.Report("legacy drawing", plugin.GetType(), exception); }
            }
            if (serializedObject.hasModifiedProperties) serializedObject.ApplyModifiedProperties();
            if (!pluginRecipeConflict) SynchronizePluginRecipe();
        }

        private void PreserveConflictingPluginEdits()
        {
            if (!Initialized || target == null || _recipe == null || SynchronizePluginRecipe() || !pluginRecipeConflict) return;
            _needsUpdate = _forceUpdate = false;
            // Preserve the working copy outside Assets before closing a conflicting inspector.
            UMARecipeBase snapshot = Instantiate((UMARecipeBase)target);
            snapshot.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                using (var copy = new SerializedObject(snapshot))
                {
                    var property = serializedObject.GetIterator();
                    bool children = true;
                    while (property.NextVisible(children))
                    {
                        children = false;
                        if (property.name != "m_Script") copy.CopyFromSerializedProperty(property);
                    }
                    copy.ApplyModifiedPropertiesWithoutUndo();
                }
                snapshot.Save(_recipe);
                string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library/UMA/PluginRecovery");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(path, EditorJsonUtility.ToJson(snapshot, true));
                Debug.LogWarning("[UMA Plugins] Pending recipe edits conflicted with an external save. " +
                    "The asset was preserved; the inspector working copy was saved to " + path, target);
                _needsUpdate = _forceUpdate = false;
            }
            finally
            {
                // Never let the base inspector save a stale working recipe over an external revision.
                _needsUpdate = _forceUpdate = false;
                DestroyImmediate(snapshot);
            }
        }
    }
}
