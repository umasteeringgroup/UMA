#define UNITY_EDITOR
#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UMA.Controls;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    public abstract class CharacterBaseEditor : Editor
    {
        protected readonly string[] toolbar =
        {
           "Slots", "DNA"
        };
        public static bool _AutomaticUpdates = true;
        // The graph embeds this inspector. Its edits are immediate, with disk writes controlled by the window.
        internal Func<bool> HostAutoSave { get; set; }
        protected bool ProcessEditsImmediately => HostAutoSave != null || _AutomaticUpdates;
        protected bool SaveEditsToDisk => HostAutoSave?.Invoke() ?? _AutomaticUpdates;
        protected Vector2 scrollPosition;
        protected string _description;
        protected string _errorMessage;
        protected bool _needsUpdate;
        protected bool _forceUpdate;
        protected bool _dnaDirty;
        protected bool _textureDirty;
        protected bool _meshDirty;
        protected Object _oldTarget;
        protected bool showBaseEditor;
        protected bool _rebuildOnLayout = false;
        protected UMAData.UMARecipe _recipe;
        protected static int _LastToolBar = 0;
        protected int _toolbarIndex = _LastToolBar;
        protected DNAMasterEditor dnaEditor;
        protected SlotMasterEditor slotEditor;
        protected bool InitialResourcesOnlyFlag;

        public static int selectedTag = 0;

        protected bool NeedsReenable()
        {
            if (dnaEditor == null || dnaEditor.NeedsReenable())
            {
                return true;
            }

            if (dnaEditor.IsValid == false)
            {
                return true;
            }

            if (_oldTarget == target)
            {
                return false;
            }

            _oldTarget = target;
            return true;
        }

        public List<UnityEngine.Object> InspectMe = new List<UnityEngine.Object>();

        public void DoInspectors()
        {
            if (InspectMe.Count > 0)
            {
                for (int i = 0; i < InspectMe.Count; i++)
                {
                    InspectorUtlity.InspectTarget(InspectMe[i]);
                }
                InspectMe.Clear();
            }
        }


        public virtual void OnEnable()
        {
            _needsUpdate = false;
            _forceUpdate = false;
            UMATextRecipe theRecipe = target as UMATextRecipe;
            if (theRecipe != null)
            {
                InitialResourcesOnlyFlag = theRecipe.resourcesOnly;
            }
            EditorApplication.update += DoInspectors;
        }

        public virtual void OnDisable()
        {
            EditorApplication.update -= DoInspectors;
            SetRecipeSaveBlocked(target, false, this);
            if (_needsUpdate && !RecipeSaveBlocked)
            {
                if (!ProcessEditsImmediately && !_forceUpdate && target is UMARecipeBase recipeBase)
                {
                    // Automatic Updates off: closing the inspector keeps the edits in the recipe as
                    // unsaved changes (Save Project writes them) instead of writing the file. Only marked
                    // unsaved when that changed the recipe: the edits are normally in it already.
                    string before = (recipeBase as UMATextRecipe)?.recipeString;
                    recipeBase.Save(_recipe);
                    if (!(recipeBase is UMATextRecipe packed) || packed.recipeString != before)
                    {
                        EditorUtility.SetDirty(recipeBase);
                    }
                }
                else
                {
                    DoUpdate();
                }
                _needsUpdate = false;
                _forceUpdate = false;
            }
        }

        protected virtual bool PreInspectorGUI()
        {
            return false;
        }

        protected virtual bool PostInspectorGUI()
        {
            return false;
        }

        bool? editBustedRecipe = null;

        /// <summary>
        /// True while the recipe did not load completely and the user has not chosen Enable Editing.
        /// Nothing may be written back then: the partial recipe would replace the full one.
        /// </summary>
        protected bool RecipeSaveBlocked => _errorMessage != null && editBustedRecipe != true;

        // Recipes an open editor (an inspector, the wardrobe graph) holds an incomplete load of, with
        // editing not enabled (RecipeSaveBlocked), and which editor holds it: one editor letting go of
        // the recipe must not unlock it for another that still holds a partial copy. The slot and
        // overlay editors save the recipe themselves, so they check this first. Compared by
        // reference, never by instance ID.
        private static readonly List<KeyValuePair<Object, object>> saveBlockedRecipes = new List<KeyValuePair<Object, object>>();

        private static bool HolderGone(object holder) => holder == null || (holder is Object unityHolder && unityHolder == null);

        /// <summary>
        /// True while an open recipe inspector (or the wardrobe graph) holds an incomplete load of this
        /// recipe and editing has not been enabled. Code that saves the recipe from inside the inspector
        /// must not save it then.
        /// </summary>
        public static bool IsRecipeSaveBlocked(Object recipe)
        {
            if (recipe is null) return false;
            saveBlockedRecipes.RemoveAll(e => e.Key == null || HolderGone(e.Value));
            for (int i = 0; i < saveBlockedRecipes.Count; i++)
            {
                if (ReferenceEquals(saveBlockedRecipes[i].Key, recipe)) return true;
            }
            return false;
        }

        /// <param name="holder">The editor that holds the copy; each holder blocks and unblocks only for itself.</param>
        internal static void SetRecipeSaveBlocked(Object recipe, bool blocked, object holder)
        {
            if (recipe is null) return;
            saveBlockedRecipes.RemoveAll(e => e.Key == null || HolderGone(e.Value) ||
                (ReferenceEquals(e.Key, recipe) && ReferenceEquals(e.Value, holder)));
            if (blocked && holder != null) saveBlockedRecipes.Add(new KeyValuePair<Object, object>(recipe, holder));
        }

        /// <summary>
        /// An editor that edits a recipe through a working copy (the recipe inspector, the wardrobe graph).
        /// The slot and overlay controls it draws write that copy into the asset themselves; they ask it first.
        /// </summary>
        internal interface IRecipeWorkingCopyOwner
        {
            Object WorkingCopyAsset { get; }
            UMAData.UMARecipe WorkingCopy { get; }
            /// <summary>True when the asset still holds what this editor last loaded or wrote, and no edit conflict waits.</summary>
            bool WorkingCopyInSync { get; }
            /// <summary>This editor's own Automatic Updates (Auto Save) setting: whether an edit is written to disk at once.</summary>
            bool WritesToDisk { get; }
            /// <summary>A control wrote the working copy into the asset (saved: to disk too).</summary>
            void OnWorkingCopyWritten(bool saved);
            /// <summary>A control changed the working copy but did not write it (the asset changed elsewhere): keep it as an unsaved edit.</summary>
            void OnWorkingCopyEditPending();
        }

        // A graph edit can change the packed recipe and editor-only graph data in the same Undo operation.
        internal interface IRecipeWorkingCopySerializer
        {
            void PackWorkingCopy();
        }

        private static readonly List<IRecipeWorkingCopyOwner> workingCopyOwners = new List<IRecipeWorkingCopyOwner>();

        internal static void RegisterWorkingCopyOwner(IRecipeWorkingCopyOwner owner)
        {
            if (owner != null && !workingCopyOwners.Contains(owner)) workingCopyOwners.Add(owner);
        }

        internal static void UnregisterWorkingCopyOwner(IRecipeWorkingCopyOwner owner)
        {
            workingCopyOwners.Remove(owner);
        }

        /// <summary>
        /// The open editor whose working copy of this recipe asset is <paramref name="recipe"/>. When an open
        /// editor holds the asset through another copy, <paramref name="stale"/> is true: that copy was replaced
        /// (a reload), and writing it would put back an older version of the recipe.
        /// </summary>
        internal static IRecipeWorkingCopyOwner FindWorkingCopyOwner(Object asset, UMAData.UMARecipe recipe, out bool stale)
        {
            stale = false;
            if (asset is null) return null;
            workingCopyOwners.RemoveAll(o => o == null || HolderGone(o));
            bool heldElsewhere = false;
            for (int i = 0; i < workingCopyOwners.Count; i++)
            {
                IRecipeWorkingCopyOwner owner = workingCopyOwners[i];
                if (!ReferenceEquals(owner.WorkingCopyAsset, asset)) continue;
                if (ReferenceEquals(owner.WorkingCopy, recipe)) return owner;
                heldElsewhere = true;
            }
            stale = heldElsewhere;
            return null;
        }

        /// <summary>
        /// Writes a working copy that a slot or overlay control changed into its recipe asset, and with
        /// Automatic Updates on, to disk. Returns false when nothing was written: the load was incomplete,
        /// the copy is no longer the open editor's, or the asset changed elsewhere since the editor loaded
        /// or wrote it (the edit then stays pending in that editor, which offers the conflict instead of
        /// writing its older copy over the other change or dropping the edit).
        /// </summary>
        /// <param name="undoName">Records Undo for the write under this name (the change is in the working copy,
        /// so this is where the asset changes).</param>
        internal static bool WriteWorkingCopy(Object recipeContext, UMAData.UMARecipe recipe, string undoName = null)
        {
            if (recipeContext == null || recipe == null)
            {
                return false;
            }
            if (IsRecipeSaveBlocked(recipeContext))
            {
                // The recipe did not load completely: writing this copy back would remove the
                // slots and overlays that are missing from it.
                return false;
            }
            IRecipeWorkingCopyOwner owner = FindWorkingCopyOwner(recipeContext, recipe, out bool stale);
            if (stale)
            {
                Debug.LogWarning("UMA: an edit to '" + recipeContext.name + "' was not written: the editor it was made in has reloaded the recipe since. Make the edit again.", recipeContext);
                return false;
            }
            if (owner != null && !owner.WorkingCopyInSync)
            {
                owner.OnWorkingCopyEditPending();
                return false;
            }

            if (!string.IsNullOrEmpty(undoName))
            {
                Undo.RecordObject(recipeContext, undoName);
            }
            if (recipeContext is UMARecipeBase recipeBase)
            {
                if (owner is IRecipeWorkingCopySerializer serializer)
                    serializer.PackWorkingCopy();
                else
                    recipeBase.Save(recipe);
                EditorUtility.SetDirty(recipeBase);
                bool saved = false;
                // Automatic Updates off: keep the edit as an unsaved change; Save Recipe (or File > Save
                // Project) writes it. The editor's own setting: the graph's Auto Save is its own.
                if (owner != null ? owner.WritesToDisk : _AutomaticUpdates)
                {
                    if (EditorUtility.IsPersistent(recipeBase))
                    {
                        AssetDatabase.SaveAssetIfDirty(recipeBase);
                        saved = true;
                    }
                    if (recipeBase is UMATextRecipe textRecipe)
                    {
                        UMAUpdateProcessor.UpdateRecipe(textRecipe);
                    }
                }
                owner?.OnWorkingCopyWritten(saved);
                return true;
            }

            EditorUtility.SetDirty(recipeContext);
            if (EditorUtility.IsPersistent(recipeContext))
            {
                AssetDatabase.SaveAssetIfDirty(recipeContext);
            }
            return true;
        }

        /// <summary>
        /// Clears the load error before a (re)load. Enable Editing applies to one failed load only:
        /// a reload that fails again is locked again, and one that succeeds is editable.
        /// </summary>
        protected void ResetLoadState()
        {
            _errorMessage = null;
            editBustedRecipe = null;
        }

        /// <summary>
        /// The load error for slots and overlays the recipe names but the project does not have
        /// (the loader leaves those out instead of failing). Null when the load was complete.
        /// </summary>
        protected internal static string MissingAssetsMessage(UMAData.UMARecipe recipe)
        {
            if (recipe == null || recipe.missingAssets == null || recipe.missingAssets.Count == 0)
            {
                return null;
            }
            return "This recipe uses assets that could not be found: " + string.Join(", ", recipe.missingAssets) +
                   ". They are not shown, and saving now would remove them from the recipe for good.";
        }

        public static string[] DefaultTags
        {
            get
            {
                return UMAEditorUtilities.GetDefaultTags();
            }
        }

        public static string DoTagSelector(string[] tagsField)
        {
            List<string> tags = new List<string>(tagsField);
            bool changed = DoTagSelector(tags);

            if (changed)
            {
                return tags[tags.Count - 1];
            }
            return string.Empty;
        }

        public static string DoTagSelector(string tagField)
        {
            List<string> tags = new List<string>();
            bool changed = DoTagSelector(tags);

            if (changed)
            {
                return tags[0];
            }
            return string.Empty;
        }


        public static bool DoTagSelector(List<string> tagsField)
        {
            bool changed = false;
            if (DefaultTags != null && DefaultTags.Length > 0)
            {
                if (selectedTag < 0 || selectedTag >= DefaultTags.Length)
                {
                    selectedTag = 0;
                }

                GUILayout.BeginHorizontal();
                selectedTag = EditorGUILayout.Popup(selectedTag, DefaultTags);
                string currentTag = DefaultTags[selectedTag];
                if (GUILayout.Button("Add Tag", GUILayout.Width(80)))
                {
                    if (!tagsField.Contains(currentTag))
                    {
                        tagsField.Add(currentTag);
                        changed = true;
                    }
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("No tags found");
            }

            return changed;
        }

        public override void OnInspectorGUI()
        {
			if(EditorApplication.isCompiling || EditorApplication.isUpdating) {
				EditorGUILayout.HelpBox("UMA Recipes cannot be during compilation/updating", MessageType.Info);
				return;
			}


            if (target is UMATextRecipe textRecipe)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("View JSON string", GUILayout.ExpandWidth(false)))
                    {
                        UMAJsonViewer.ShowJson(textRecipe.recipeString, textRecipe.name + " - JSON");
                    }
                    GUILayout.Label("string length " + (textRecipe.recipeString?.Length ?? 0));
                }
            }
            else
            {
                GUILayout.Label(_description);
            }
            if (HostAutoSave == null)
                _AutomaticUpdates = GUILayout.Toggle(_AutomaticUpdates, "Automatic Updates");
            _forceUpdate = false;

            if (!ProcessEditsImmediately)
            {
                EditorGUILayout.HelpBox("Automatic Updates are disabled. You will need to click the 'Save Recipe' button to save any changes you make.", MessageType.Warning);
                // Drawn above the load-error section, so it must check the load itself.
                using (new EditorGUI.DisabledScope(RecipeSaveBlocked))
                {
                    if (GUILayout.Button("Save Recipe"))
                    {
                        _needsUpdate = true;
                        _forceUpdate = true;
                    }
                }
            }


            if (target as UMATextRecipe != null)
            {
                UMATextRecipe theRecipe = target as UMATextRecipe;
#if UMA_ADDRESSABLES
                bool changed = false;
                if (!serializedObject.isEditingMultipleObjects)
                {
                    string newLabel = EditorGUILayout.TextField("Alt Addressable Label", theRecipe.label);
                    if (newLabel != theRecipe.label)
                    {
                        theRecipe.label = newLabel;
                        _needsUpdate = true;
                        _forceUpdate = true;
                    }
                    if (theRecipe.resourcesOnly)
                    {
                        GUILayout.Label("RESOURCES ONLY: TRUE");
                        EditorGUILayout.HelpBox("Removing the Resources Only flag will instruct UMA to include this in the addressable groups. You will need to regenerate the groups, and rebuild the addressable bundles.", MessageType.Info);
                        if (GUILayout.Button("Remove Resources Only flag"))
                        {
                            theRecipe.resourcesOnly = false;
                            DoUpdate();
                            RebuildIfNeeded();
                        }
                    }
                    else
                    {
                        GUILayout.Label("RESOURCES ONLY: FALSE");
                        EditorGUILayout.HelpBox("Making this Resources Only will remove this recipe, and the items contained in it, from the addressable groups. This can take a few moments. Addressable bundles will need to be rebuilt after this is toggled.", MessageType.Info);
                        if (GUILayout.Button("Make this Resources Only"))
                        {
                            theRecipe.resourcesOnly = true;
                            DoUpdate();
                            RebuildIfNeeded();
                        }
                    }
                }
                EditorGUILayout.HelpBox("Checking ForceKeep will set the keep flag on the item", MessageType.Info);
                bool oldForceKeep = theRecipe.forceKeep;
                theRecipe.forceKeep = EditorGUILayout.Toggle("Force Keep", theRecipe.forceKeep);
                if (oldForceKeep != theRecipe.forceKeep)
                {
                    changed = true;
                }
                bool oldLabelLocalFiles = theRecipe.labelLocalFiles;
                EditorGUILayout.HelpBox("If you check Label Local Files, then the contents will be looked up locally, not from the index. Use this when you are substituting recipes for branding, etc.", MessageType.Info);
                theRecipe.labelLocalFiles = EditorGUILayout.Toggle("Label Local Files", theRecipe.labelLocalFiles);
                if (oldLabelLocalFiles != theRecipe.labelLocalFiles)
                {
                    changed = true;
                }

                if (changed)
                {
                    DoUpdate();
                }
#endif
            }
            if (_errorMessage != null)
            {
                EditorGUILayout.HelpBox("The Recipe Editor could not be drawn correctly because the libraries could not find some of the required Assets. The error message was...", MessageType.Warning);
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("You can either continue editing this recipe (in which case it will only contain the slots and overlays you see below) or you can fix the missing asset and come back to this recipe after (in which case it will contain everything the recipe had originally)", MessageType.Info);
                EditorGUILayout.Space();
                editBustedRecipe = editBustedRecipe == null ? false : editBustedRecipe;
                if (GUILayout.Button("Enable Editing"))
                {
                    editBustedRecipe = true;
                }
                EditorGUILayout.Space();
            }

            // Locked while the load is incomplete and editing has not been enabled. Follows the
            // current load, so a successful reload unlocks the inspector.
            SetRecipeSaveBlocked(target, RecipeSaveBlocked, this);
            EditorGUI.BeginDisabledGroup(RecipeSaveBlocked);

            try
            {
                if (target != _oldTarget)
                {
                    _rebuildOnLayout = true;
                    _oldTarget = target;
                }

                if (_rebuildOnLayout && Event.current.type == EventType.Layout)
                {
                    Rebuild();
                }


                // Whether something changed in this draw; _needsUpdate also stays set while a change waits
                // for Save Recipe (Automatic Updates off).
                bool changedNow = false;
                if (PreInspectorGUI())
                {
                    changedNow = true;
                }

                if (ToolbarGUI())
                {
                    changedNow = true;
                }

                if (PostInspectorGUI())
                {
                    changedNow = true;
                }
                if (changedNow)
                {
                    _needsUpdate = true;
                }

                if (RecipeSaveBlocked)
                {
                    // The recipe did not load completely: never write the partial copy back.
                    _needsUpdate = false;
                    _forceUpdate = false;
                }
                else if ((ProcessEditsImmediately && _needsUpdate) || _forceUpdate)
                {
                    DoUpdate();
                    _needsUpdate = false;
                    _forceUpdate = false;
                }
                else
                {
                    if (changedNow)
                    {
                        var recipeBase = (UMARecipeBase)target;
                        recipeBase.Save(_recipe);
                        EditorUtility.SetDirty(target);
                        // Rebuild the rows from the recipe, as a save does with Automatic Updates on: Clear
                        // Recipe left the cleared slots on screen, and an added slot had no row. Only after a
                        // change in this draw: rebuilding on every draw while a change waited for Save Recipe
                        // replaced the slot editors under an open asset picker, which then lost its pick.
                        _rebuildOnLayout = true;
                    }
                }
            }
            catch (UMAResourceNotFoundException e)
            {
                _errorMessage = e.Message;
                SetRecipeSaveBlocked(target, RecipeSaveBlocked, this);
            }
            if (showBaseEditor)
            {
                base.OnInspectorGUI();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.Label("** end of recipe **");
        }

#if UMA_ADDRESSABLES
        private void RebuildIfNeeded()
        {
            List<Type> PluginTypes = AssetIndexerWindow.GetAddressablePlugins();

            if (EditorUtility.DisplayDialog("UMA System Request", "The Addressable groups should be recalculated after setting this. Do it now? This is recommended.", "Recalculate", "Do it later"))
            {
                if (PluginTypes.Count == 1 && UMAEditorUtilities.UseSharedGroupConfigured())
                {
                    IUMAAddressablePlugin addrplug = (IUMAAddressablePlugin)Activator.CreateInstance(PluginTypes[0]);
                    UMAAddressablesEditorBridge.GenerateAddressables(addrplug);
                    Resources.UnloadUnusedAssets();
                }
                else
                {
                    UMAAddressablesEditorBridge.CleanupAddressables();
                    UMAAddressablesEditorBridge.GenerateAddressables();
                    Resources.UnloadUnusedAssets();
                }
            }
        }
#endif
        protected abstract void DoUpdate();

        protected virtual void Rebuild()
        {
            _rebuildOnLayout = false;
            if (_recipe != null && dnaEditor != null)
            {
                int oldViewDNA = dnaEditor.viewDna;
                UMAData.UMARecipe oldRecipe = dnaEditor.recipe;
                dnaEditor = new DNAMasterEditor(_recipe);
                if (oldRecipe == _recipe)
                {
                    dnaEditor.viewDna = oldViewDNA;
                }
                slotEditor = new SlotMasterEditor(_recipe, target);
            }
        }

        protected virtual bool ToolbarGUI()
        {
            _toolbarIndex = GUILayout.Toolbar(_toolbarIndex, toolbar);
            _LastToolBar = _toolbarIndex;
            if (dnaEditor != null && slotEditor != null)
            {
                switch (_toolbarIndex)
                {
                    case 1:
                        if (!dnaEditor.IsValid)
                        {
                            return false;
                        }

                        return dnaEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);
                    case 0:
                        return slotEditor.OnGUI(target.name, ref _dnaDirty, ref _textureDirty, ref _meshDirty);
                }
            }

            return false;
        }
    }
}
#endif
