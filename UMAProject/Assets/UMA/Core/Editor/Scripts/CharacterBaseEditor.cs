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
            if (_needsUpdate)
            {
                DoUpdate();
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


            GUILayout.Label(_description);
            _AutomaticUpdates = GUILayout.Toggle(_AutomaticUpdates, "Automatic Updates");
            _forceUpdate = false;

            if (!_AutomaticUpdates)
            {
                EditorGUILayout.HelpBox("Automatic Updates are disabled. You will need to click the 'Save Recipe' button to save any changes you make.", MessageType.Warning);
                if (GUILayout.Button("Save Recipe"))
                {
                    _needsUpdate = true;
                    _forceUpdate = true;
                }
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Open in Node Recipe Editor (Do not use!)"))
            {
                var win = EditorWindow.GetWindow<UMANodeRecipeEditorWindow>("UMA Node Recipe Editor");
                if (target is UMATextRecipe tr && win != null)
                {
                    var mi = typeof(UMANodeRecipeEditorWindow).GetMethod("LoadRecipe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (mi != null)
                    {
                        mi.Invoke(win, new object[] { tr });
                    }
                }
                win.Show();
                win.Focus();
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

            EditorGUI.BeginDisabledGroup(editBustedRecipe == false);

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


                if (PreInspectorGUI())
                {
                    _needsUpdate = true;
                }

                if (ToolbarGUI())
                {
                    _needsUpdate = true;
                }

                if (PostInspectorGUI())
                {
                    _needsUpdate = true;
                }

                if ((_AutomaticUpdates && _needsUpdate) || _forceUpdate)
                {
                    DoUpdate();
                    _needsUpdate = false;
                    _forceUpdate = false;
                }
                else
                {
                    if (_needsUpdate)
                    {
                        var recipeBase = (UMARecipeBase)target;
                        recipeBase.Save(_recipe);
                        EditorUtility.SetDirty(target);
                    }
                }
            }
            catch (UMAResourceNotFoundException e)
            {
                _errorMessage = e.Message;
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
                    UMAAddressablesSupport.Instance.GenerateAddressables(addrplug);
                    Resources.UnloadUnusedAssets();
                }
                else
                {
                    UMAAddressablesSupport.Instance.CleanupAddressables();
                    UMAAddressablesSupport.Instance.GenerateAddressables();
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
