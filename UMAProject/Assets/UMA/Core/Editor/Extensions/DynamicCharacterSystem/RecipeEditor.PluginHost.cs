using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public partial class RecipeEditor : IUMAPluginHost
    {
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
            if (target != null) pluginRecipeRevision = CurrentPluginRecipeRevision();
        }

        protected bool SynchronizePluginRecipe()
        {
            if (!Initialized || target == null || _recipe == null) return false;
            if (pluginRecipeRevision == null) RecordPluginRecipeRevision();
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
            _errorMessage = null;
            try { ((UMARecipeBase)target).Load(loaded); }
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
