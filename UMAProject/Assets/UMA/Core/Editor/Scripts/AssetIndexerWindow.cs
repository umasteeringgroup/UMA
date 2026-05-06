using System;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using UMA.Editors;
using UMA.PoseTools;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using System.Xml.Serialization;

#if UNITY_6000_2_OR_NEWER
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#endif


namespace UMA.Controls
{
    class AssetIndexerWindow : EditorWindow
    {
		private class SlotValidationReportWindow : EditorWindow
		{
			internal struct SlotIssue
			{
				public SlotDataAsset Slot;
				public List<string> Reasons;
			}

			private readonly List<SlotIssue> _issues = new List<SlotIssue>();
			private Vector2 _scroll;
			private bool _isRunning;

			public static void ShowReport(List<SlotIssue> issues)
			{
				var w = GetWindow<SlotValidationReportWindow>(true, "UMA Slot Validation", true);
				w._issues.Clear();
				if (issues != null)
				{
					w._issues.AddRange(issues);
				}
				w.minSize = new Vector2(600, 300);
				w.ShowUtility();
				w.Focus();
				w.Repaint();
			}

			private void OnGUI()
			{
				using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
				{
					GUILayout.Label("Invalid slots: " + _issues.Count, EditorStyles.toolbarButton);
					GUILayout.FlexibleSpace();
					using (new EditorGUI.DisabledScope(_isRunning))
					{
						//if (GUILayout.Button("Fix all slots without slot names", EditorStyles.toolbarButton))
						//{
						//	FixAllSlotsWithoutSlotNames();
						//}
						if (GUILayout.Button("Load missing materials", EditorStyles.toolbarButton))
						{
							LoadMissingMaterials();
						}
					}
					if (GUILayout.Button("Close", EditorStyles.toolbarButton, GUILayout.Width(80)))
					{
						Close();
					}
				}

				if (_issues.Count == 0)
				{
					EditorGUILayout.HelpBox("No invalid slots detected.", MessageType.Info);
					return;
				}

				_scroll = EditorGUILayout.BeginScrollView(_scroll);
				for (int i = 0; i < _issues.Count; i++)
				{
					var issue = _issues[i];
					if (issue.Slot == null)
					{
						continue;
					}

					EditorGUILayout.BeginVertical(EditorStyles.helpBox);
					using (new EditorGUILayout.HorizontalScope())
					{
						EditorGUILayout.ObjectField(issue.Slot, typeof(SlotDataAsset), false);
						if (GUILayout.Button("Select", GUILayout.Width(80)))
						{
							Selection.activeObject = issue.Slot;
							EditorGUIUtility.PingObject(issue.Slot);
						}
						if (GUILayout.Button("Inspect", GUILayout.Width(80)))
						{
                            InspectorUtlity.InspectTarget(issue.Slot);
						}
					}

					if (issue.Reasons != null)
					{
						for (int r = 0; r < issue.Reasons.Count; r++)
						{
							if (string.IsNullOrEmpty(issue.Reasons[r]))
							{
								continue;
							}
							EditorGUILayout.LabelField("- " + issue.Reasons[r], EditorStyles.wordWrappedLabel);
						}
					}
					EditorGUILayout.EndVertical();
				}
				EditorGUILayout.EndScrollView();
			}

			private void LoadMissingMaterials()
			{
				var indexer = UMAAssetIndexer.Instance;
				if (indexer == null)
				{
					return;
				}

              var overlays = indexer.GetAllAssets<OverlayDataAsset>();
				int fixedCount = 0;
				int missingName = 0;
				try
				{
					_isRunning = true;
                   for (int i = 0; i < overlays.Count; i++)
					{
                       var overlay = overlays[i];
                        if (overlay == null)
						{
							continue;
						}
                     if (overlay.material != null)
						{
							continue;
						}
                       if (string.IsNullOrEmpty(overlay.materialName))
						{
							missingName++;
							continue;
						}

                        var mat = indexer.GetAsset<UMAMaterial>(overlay.materialName);
						if (mat == null)
						{
							continue;
						}

                     Undo.RecordObject(overlay, "Load missing overlay material");
                        overlay.material = mat;
                        EditorUtility.SetDirty(overlay);
						fixedCount++;
					}
				}
				finally
				{
					_isRunning = false;
				}

				AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Load missing materials", "Updated " + fixedCount + " overlay(s). Overlays missing materialName: " + missingName + ".", "OK");
				RefreshReport();
			}

			private void RefreshReport()
			{
				var indexer = UMAAssetIndexer.Instance;
				if (indexer == null)
				{
					return;
				}
				var slots = indexer.GetAllAssets<SlotDataAsset>();
				var newIssues = new List<SlotIssue>();
				var reasons = new List<string>();
				for (int i = 0; i < slots.Count; i++)
				{
					var sda = slots[i];
					if (sda == null)
					{
						continue;
					}
					if (!sda.ValidateMeshData(reasons))
					{
						newIssues.Add(new SlotIssue { Slot = sda, Reasons = new List<string>(reasons) });
					}
				}
				_issues.Clear();
				_issues.AddRange(newIssues);
				Repaint();
			}
		}

        [NonSerialized] bool m_Initialized;
        [SerializeField] TreeViewState m_TreeViewState; // Serialized in the window layout file so it survives assembly reloading
        [SerializeField] MultiColumnHeaderState m_MultiColumnHeaderState;
        public UMAAssetTreeView treeView { get; private set; }

        List<IUMAAddressablePlugin> addressablePlugins = new List<IUMAAddressablePlugin>();

        private static bool IsEditorBusy()
        {
            return EditorApplication.isCompiling || EditorApplication.isUpdating;
        }

        private void OnBeforeAssemblyReload()
        {
            // nothing to unsubscribe currently, but ensure we drop heavy refs
            try { m_Initialized = false; } catch { }
        }

        private void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        #region Menus
        GenericMenu _FileMenu;
        GenericMenu _AddressablesMenu;
        GenericMenu _ItemsMenu;
        GenericMenu _ToolsMenu;
        bool ShowUtilities;
        UMAMaterial umaMaterial;
        RaceData umaRaceData;
        OverlayDataAsset umaOverlay;
        Texture2D umaTexture;
        SlotDataAsset umaSlot;
        MeshHideAsset AddedMHA = null;

        private const string SlotLodPrefKeyPrefix = "UMA.UMASimpleLODEditor.InternalSlotLOD.";

		private static bool IsBakedSlotName(string assetName)
		{
			if (string.IsNullOrEmpty(assetName))
			{
				return false;
			}
			return assetName.IndexOf("_baked_", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static string MakeSafeFileStem(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return string.Empty;
			}
			string safe = name.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
			safe = safe.Replace('*', '_').Replace('?', '_').Replace('"', '_').Replace('<', '_').Replace('>', '_').Replace('|', '_');
			return safe.Trim();
		}

		private static string GetPreferredBakedSlotAssetName(SlotDataAsset slot)
		{
			if (slot == null)
			{
				return string.Empty;
			}
			// If Unity object name is default/empty, use slotName as a better identifier
			string n = slot.name;
			if (string.IsNullOrEmpty(n) || n == "SlotDataAsset" || n == "New SlotDataAsset")
			{
				n = slot.slotName;
			}
			return MakeSafeFileStem(n);
		}

		private void SaveBakedSlotsToDisk(List<SlotDataAsset> slotsSource, string title)
		{
			if (UAI == null)
			{
				return;
			}

			string destFolder = EditorUtility.OpenFolderPanel(title, "Assets", "");
			if (string.IsNullOrEmpty(destFolder))
			{
				return;
			}

			// Convert absolute path under project to an Assets-relative path
			destFolder = destFolder.Replace('\\', '/');
			string projectPath = Application.dataPath.Replace('\\', '/');
			if (!destFolder.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
			{
				EditorUtility.DisplayDialog("Save baked slots", "Please select a folder under this project's Assets folder.", "OK");
				return;
			}
			string destAssetFolder = "Assets" + destFolder.Substring(projectPath.Length);

			if (slotsSource == null || slotsSource.Count == 0)
			{
				EditorUtility.DisplayDialog("Save baked slots", "No SlotDataAsset found to save.", "OK");
				return;
			}

			int saved = 0;
			int skipped = 0;
			int total = slotsSource.Count;
			int processed = 0;
			try
			{
				for (int i = 0; i < slotsSource.Count; i++)
				{
					var slot = slotsSource[i];
					processed++;
					EditorUtility.DisplayProgressBar("Save baked slots", "Processing slots...", Mathf.Clamp01((float)processed / Mathf.Max(1, total)));

					if (slot == null)
					{
						skipped++;
						continue;
					}

					// We only want the baked (in-memory) ones.
					if (!IsBakedSlotName(slot.name) && !IsBakedSlotName(slot.slotName))
					{
						skipped++;
						continue;
					}

					string existingPath = AssetDatabase.GetAssetPath(slot);
					if (!string.IsNullOrEmpty(existingPath))
					{
						// Already on disk
						skipped++;
						continue;
					}

					// Derive filename
					string stem = GetPreferredBakedSlotAssetName(slot);
					if (string.IsNullOrEmpty(stem))
					{
						stem = "BakedSlot";
					}
					string targetPath = AssetDatabase.GenerateUniqueAssetPath(destAssetFolder + "/" + stem + ".asset");

					// Create a persisted clone so we don't mutate the in-memory instance in-place
					var clone = UnityEngine.Object.Instantiate(slot);
					clone.name = Path.GetFileNameWithoutExtension(targetPath);
					Undo.RegisterCreatedObjectUndo(clone, "Save baked slot");
					AssetDatabase.CreateAsset(clone, targetPath);
					EditorUtility.SetDirty(clone);

					// Replace the indexer reference to point to the saved asset
					try
					{
						UAI.ProcessNewItem(clone, false, false);
					}
					catch { }

					saved++;
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			m_Initialized = false;
			Repaint();
			EditorUtility.DisplayDialog("Save baked slots", "Saved " + saved + " baked slot(s). Skipped " + skipped + ".", "OK");
		}

		private void SaveAllBakedSlotsToDisk()
		{
			var allSlots = UAI.GetAllAssets<SlotDataAsset>();
			SaveBakedSlotsToDisk(allSlots, "Save all baked slots to folder");
		}

		private void SaveSelectedBakedSlotsToDisk()
		{
			var selected = GetSelectedAssets(typeof(SlotDataAsset));
			var slots = new List<SlotDataAsset>(selected != null ? selected.Count : 0);
			if (selected != null)
			{
				for (int i = 0; i < selected.Count; i++)
				{
					var s = selected[i].Item as SlotDataAsset;
					if (s != null)
					{
						slots.Add(s);
					}
				}
			}
			SaveBakedSlotsToDisk(slots, "Save selected baked slots to folder");
		}

        private static int LoadSlotLodInt(string key, int defaultValue)
        {
            return EditorPrefs.GetInt(SlotLodPrefKeyPrefix + key, defaultValue);
        }

        private static float LoadSlotLodFloat(string key, float defaultValue)
        {
            return EditorPrefs.GetFloat(SlotLodPrefKeyPrefix + key, defaultValue);
        }

        private static bool LoadSlotLodBool(string key, bool defaultValue)
        {
            return EditorPrefs.GetBool(SlotLodPrefKeyPrefix + key, defaultValue);
        }

        private static void SaveSlotLodInt(string key, int value)
        {
            EditorPrefs.SetInt(SlotLodPrefKeyPrefix + key, value);
        }

        private static void SaveSlotLodFloat(string key, float value)
        {
            EditorPrefs.SetFloat(SlotLodPrefKeyPrefix + key, value);
        }

        private static void SaveSlotLodBool(string key, bool value)
        {
            EditorPrefs.SetBool(SlotLodPrefKeyPrefix + key, value);
        }

        private bool _slotLodOptionsFoldout = true;

#if UNITY_6000_2_OR_NEWER
		private MeshHideAsset.TriangleHideStrategy _fixMhaCopyLodMode = MeshHideAsset.TriangleHideStrategy.Conservative;
		private int _fixMhaCopyPolicy = 0; // 0=Replace, 1=Missing
		private static readonly GUIContent[] _fixMhaCopyPolicyOptions =
		{
			new GUIContent("Replace", "Overwrite destination LOD masks."),
			new GUIContent("Missing", "Only fill destination LOD masks that are missing/unallocated.")
		};
#endif

        private GenericMenu FileMenu
        {
            get
            {
                if (_FileMenu == null)
                {
                    SetupMenus();
                }
                return _FileMenu;
            }
        }

        private GenericMenu ItemsMenu
        {
            get
            {
                if (_ItemsMenu == null)
                {
                    SetupMenus();
                }
                return _ItemsMenu;
            }
        }

        private GenericMenu ToolsMenu
        {
            get
            {
                if (_ToolsMenu == null)
                {
                    SetupMenus();
                }
                return _ToolsMenu;
            }
        }

        private GenericMenu AddressablesMenu
        {
            get
            {
                if (_AddressablesMenu == null)
                {
                    SetupMenus();
                }
#if UMA_ADDRESSABLES 
                //  Rebuild menus if addressables changed.
                if (_AddressablesMenu.GetItemCount() == 1)
                {
                    SetupMenus();
                }
#endif
                return _AddressablesMenu;
            }
        }
        #endregion

        SearchField m_SearchField;
        UMAAssetIndexer _UAI;
        int LoadedItems = 0;
        public HashSet<string> LoadedLabels = new HashSet<string>();

        enum eLoaded { All, Addressable, NonAddressable, Keep, Resx, NoGroups, SelectedOnly };
        string[] LoadedValues = { "All", "Addressable Only", "Non-Addressable Only", "Keep Loaded", "In Resources", "Recipes not added to groups", "Currently Selected Items" };
        public List<AssetItem> LoadOnly = new List<AssetItem>();

        enum eShowTypes { All, WithItems };
        string[] ShowTypes = { "All Types", "Only Types with Children" };
        int ShowIndex = 0;

        UMAAssetIndexer UAI
        {
            get
            {
                return UMAAssetIndexer.Instance;
            }
        }

        /// <summary>
        /// Returns a list of all AddressablePlugins
        /// </summary>
        /// <returns></returns>
        public static List<Type> GetAddressablePlugins()
        {
            List<Type> theTypes = new List<Type>();

            var Assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var asm in Assemblies)
            {

                try
                {
                    var Types = asm.GetTypes();
                    foreach (var t in Types)
                    {
                        if (typeof(IUMAAddressablePlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                        {
                            theTypes.Add(t);
                        }
                    }
                }
                catch (Exception)
                {
                    // This apparently blows up on some assemblies. 
                }
            }

			return theTypes;
        }

        private static List<AssetItem> FilterAssetItemsByType(List<AssetItem> sourceItems, Type assetType)
        {
            List<AssetItem> filteredItems = new List<AssetItem>();
            if (sourceItems == null)
            {
                return filteredItems;
            }

            for (int itemIndex = 0; itemIndex < sourceItems.Count; itemIndex++)
            {
                AssetItem item = sourceItems[itemIndex];
                if (item != null && item._Type == assetType)
                {
                    filteredItems.Add(item);
                }
            }

            return filteredItems;
        }

        private AssetItem GetFirstSelectedAsset(Type assetType)
        {
            List<AssetItem> selectedAssets = GetSelectedAssets(assetType);
            if (selectedAssets == null || selectedAssets.Count == 0)
            {
                return null;
            }

            return selectedAssets[0];
        }

        [MenuItem("UMA/Global Library", priority = 99)]
        public static AssetIndexerWindow GetWindow()
        {
            var window = GetWindow<AssetIndexerWindow>();

            /* Setup the window menus */
            window.SetupMenus();

            Texture icon = AssetDatabase.LoadAssetAtPath<Texture>("Assets/UMA/InternalDataStore/UMA32.png");
            window.titleContent = new GUIContent(UmaAboutWindow.umaVersion + " Global Library", icon);
            window.Focus();
            window.Repaint();
            return window;
        }

        #region utility functions



        void AddPlugins(List<Type> PluginTypes)
        {
            addressablePlugins = new List<IUMAAddressablePlugin>();
            foreach (Type t in PluginTypes)
            {
                addressablePlugins.Add((IUMAAddressablePlugin)Activator.CreateInstance(t));
            }
        }

        // a method to simplify adding menu items
        void AddMenuItemWithCallback(GenericMenu menu, string menuPath, GenericMenu.MenuFunction function)
        {
            // the menu item is marked as selected if it matches the current value of m_Color
            menu.AddItem(new GUIContent(menuPath), false, function);
        }

        // a method to simplify adding menu items
        void AddMenuItemWithCallbackParm(GenericMenu menu, string menuPath, GenericMenu.MenuFunction2 function, System.Object o)
        {
            // the menu item is marked as selected if it matches the current value of m_Color
            menu.AddItem(new GUIContent(menuPath), false, function, o);
        }

        private void SetupMenus()
        {
            if (IsEditorBusy())
            {
                EditorApplication.delayCall += SetupMenus;
                return;
            }

            _FileMenu = new GenericMenu();
            _AddressablesMenu = new GenericMenu();
            _ItemsMenu = new GenericMenu();
            _ToolsMenu = new GenericMenu();

            AddPlugins(GetAddressablePlugins());

            // ***********************************************************************************
            // File Menu items
            // ***********************************************************************************
            AddMenuItemWithCallback(FileMenu, "Rebuild From Project", () =>
            {
                if (UAI == null) return;
                UAI.RebuildLibrary();
                m_Initialized = false;
                Repaint();
            });

            AddMenuItemWithCallback(FileMenu, "Rebuild From Project (include text assets)", () =>
            {
                if (UAI == null) return;
                UAI.SaveKeeps();
                UAI.Clear();
                UAI.BuildStringTypes();
                UAI.AddEverything(true);
                UAI.RestoreKeeps();
                UAI.ForceSave();
                Resources.UnloadUnusedAssets();
                m_Initialized = false;
                Repaint();
            });
            AddMenuItemWithCallback(FileMenu, "Clear References", () =>
            {
                if (UAI == null) return;
                UAI.RemoveReferences();
                Resources.UnloadUnusedAssets();
                m_Initialized = false;
                Repaint();
                EditorUtility.DisplayDialog("Repair", "References Removed", "OK");
            });

            AddMenuItemWithCallback(FileMenu, "Repair and remove invalid items", () =>
            {
                if (UAI == null) return;
                UAI.BuildStringTypes();
                UAI.RepairAndCleanup();
                Resources.UnloadUnusedAssets();
                m_Initialized = false;
                Repaint();
                EditorUtility.DisplayDialog("Repair", "AssetIndex successfully repaired", "OK");
            });
            /* AddMenuItemWithCallback(FileMenu, "Add Build refs to all non-addressables", () => 
			{
				UAI.AddReferences();
				RecountTypes();
				Resources.UnloadUnusedAssets();
				Repaint();
			});
			AddMenuItemWithCallback(FileMenu, "Clear build refs from all items", () => 
			{
				UAI.ClearReferences();
				Resources.UnloadUnusedAssets();
				RecountTypes();
				Repaint();
			}); */
            FileMenu.AddSeparator("");
            AddMenuItemWithCallback(FileMenu, "Toggle Utilities Panel", () =>
            {
                ShowUtilities = !ShowUtilities;
                Repaint();
            });
            FileMenu.AddSeparator("");

            AddMenuItemWithCallback(FileMenu, "Empty Index", () =>
            {
                if (UAI == null) return;
                UAI.Clear();
                m_Initialized = false;
                Repaint();
            });


            AddMenuItemWithCallback(FileMenu, "Backup Index", () =>
            {
                if (UAI == null) return;
                // string index = UAI.Backup();
                string filename = EditorUtility.SaveFilePanel("Backup Index", "", "librarybackup", "bak");
                if (!string.IsNullOrEmpty(filename))
                {
                    try
                    {
                        string backup = UAI.Backup();
                        System.IO.File.WriteAllText(filename, backup);
                        backup = "";
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        EditorUtility.DisplayDialog("Error", "Error writing backup: " + ex.Message, "OK");
                    }
                }
            });

            AddMenuItemWithCallback(FileMenu, "Save to disk", () =>
            {
                if (UAI == null) return;
                UMAAssetIndexer.Instance.ForceSave();
            });

			AddMenuItemWithCallback(ToolsMenu, "Save all baked slots to disk", () =>
			{
				SaveAllBakedSlotsToDisk(); 
			});

			AddMenuItemWithCallback(ToolsMenu, "Save selected baked slots to disk", () =>
			{
				SaveSelectedBakedSlotsToDisk();
			});

            AddMenuItemWithCallback(FileMenu, "Rebuild Dictionaries", () =>
            {
                if (UAI == null) return;
                UMAAssetIndexer.Instance.UpdateSerializedDictionaryItems();
                Repaint();
            });

            AddMenuItemWithCallback(FileMenu, "Restore Index", () =>
            {
                if (UAI == null) return;
                string filename = EditorUtility.OpenFilePanel("Restore", "", "bak");
                if (!string.IsNullOrEmpty(filename))
                {
                    try
                    {
                        string backup = System.IO.File.ReadAllText(filename);
                        EditorUtility.DisplayProgressBar("Restore", "Restoring index", 0);
                        if (!UAI.Restore(backup))
                        {
                            EditorUtility.DisplayDialog("Error", "Unable to restore index. Please review the console for more information.", "OK");
                        }
                        backup = "";
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        EditorUtility.DisplayDialog("Error", "Error writing backup: " + ex.Message, "OK");
                    }
                    EditorUtility.ClearProgressBar();
                    m_Initialized = false;
                    Repaint();
                }
            });

#if UMA_ADDRESSABLES


            foreach (IUMAAddressablePlugin plugin in addressablePlugins)
            {
                AddMenuItemWithCallbackParm(_AddressablesMenu, "Generators/" + plugin.Menu, (object o) =>
                {
                    if (UAI == null) return;
                    IUMAAddressablePlugin addrplug = o as IUMAAddressablePlugin;
                    UMAAddressablesSupport.Instance.GenerateAddressables(addrplug);
                    Resources.UnloadUnusedAssets();
                    m_Initialized = false;
                    Repaint();
                }, plugin);
            }

            _AddressablesMenu.AddSeparator("Generators/");

            // ***********************************************************************************
            // Addressables Menu items
            // ***********************************************************************************
            AddMenuItemWithCallback(_AddressablesMenu, "Generators/Generate Groups (optimized)", () =>
            {
                if (UAI == null) return;
                UMAAddressablesSupport.Instance.CleanupAddressables();
                UMAAddressablesSupport.Instance.GenerateAddressables();
                Resources.UnloadUnusedAssets();
                m_Initialized = false;
                Repaint();
            });

            AddMenuItemWithCallback(_AddressablesMenu, "Generators/Generate Single Group (Final Build Only)", () =>
            {
                if (UAI == null) return;
                UMAAddressablesSupport.Instance.CleanupAddressables();
                SingleGroupGenerator sgs = new SingleGroupGenerator();
                sgs.ClearMaterials = true;
                UMAAddressablesSupport.Instance.GenerateAddressables(sgs);
                Resources.UnloadUnusedAssets();
                m_Initialized = false;
                Repaint();
            });

            AddMenuItemWithCallback(_AddressablesMenu, "Generators/Postbuild Material Fixup", () =>
            {
                UMAAssetIndexer.Instance.PostBuildMaterialFixup();
                Repaint();
            });

			ItemsMenu.AddSeparator("");


			AddMenuItemWithCallback(_AddressablesMenu, "Generators/Prepare Build", () => {
				UMASettings umaSettings = UMASettings.GetOrCreateSettings();
				umaSettings.addrStripTextures = true; //this tells uma to replace the recipe materials with Hidden/InternalErrorShader shader and creates a tag on the real shader, which it reapplies at runtime load. Note that the shader variant must be in the project build (reference in Init scene "ForceIncludeShaders" prefab). And so obviously we don't want that in the normal editor settings as we'd lose the references.
				umaSettings.addrStripUVAttachedShaders = true; //same as above, except for uv attach prefab materials
				SingleGroupGenerator sg = new SingleGroupGenerator();
				sg.ClearMaterials = true; // this tells UMA to remove materials from slots and overlays so they don't bloat the addressables
				UMAAddressablesSupport.Instance.GenerateAddressables(sg);
				UMAAssetIndexer.Instance.PrepareBuild();
				Resources.UnloadUnusedAssets();
				UMAAddressablesSupport.Instance.CleanupOrphans(typeof(SlotDataAsset), true, $"Orphan Cleanup of type {typeof(SlotDataAsset).Name} - Menu Option");
				UMAAddressablesSupport.Instance.CleanupOrphans(typeof(OverlayDataAsset), true, $"Orphan Cleanup of type {typeof(OverlayDataAsset).Name} - Menu Option");
			});



            AddMenuItemWithCallback(_AddressablesMenu, "Reset stripped shaders", () =>
            {
                int total = UMAAssetIndexer.Instance.ResetStrippedShaders();
                EditorUtility.DisplayDialog("Reset Stripped Shaders", $"Reset shaders on {total} materials", "OK");
                Repaint();
            });

            AddMenuItemWithCallback(_AddressablesMenu, "Remove Addressables", () =>
            {
                if (UAI == null) return;
                UMAAddressablesSupport.Instance.CleanupAddressables(false, true);
                m_Initialized = false;
                Repaint();
            });
            AddMenuItemWithCallback(_AddressablesMenu, "Delete Empty Groups", () =>
            {
                UMAAddressablesSupport.Instance.CleanupAddressables(true);
            });

            /*
			AddMenuItemWithCallback(AddressablesMenu, "Force Add Refs (Bad!!)", () => 
			{
				UAI.AddReferences(true);
				RecountTypes();
				Resources.UnloadUnusedAssets();
				Repaint();
			}); */

            AddMenuItemWithCallback(_AddressablesMenu, "Remove Orphaned Slots", () =>
            {
                if (EditorUtility.DisplayDialog("Warning!", "You *must* build the addressable groups, and mark any slots you want to keep as 'keep' before running this!", "OK", "Cancel"))
                {
                    UMAAddressablesSupport.Instance.CleanupOrphans(typeof(SlotDataAsset));
                    m_Initialized = false;
                    Repaint();
                }
            });
            AddMenuItemWithCallback(_AddressablesMenu, "Remove Orphaned Overlays", () =>
            {
                if (EditorUtility.DisplayDialog("Warning!", "You *must* build the addressable groups, and mark any slots you want to keep as 'keep' before running this.", "OK", "Cancel"))
                {
                    UMAAddressablesSupport.Instance.CleanupOrphans(typeof(OverlayDataAsset));
                    m_Initialized = false;
                    Repaint();
                }
            });

            AddMenuItemWithCallback(_AddressablesMenu, "Select Orphaned Slots", () =>
            {
                if (EditorUtility.DisplayDialog("Warning!", "You *must* build the addressable groups, and mark any slots you want to keep as 'keep' before running this!", "OK", "Cancel"))
                {
                    List<AssetItem> orphans = UMAAddressablesSupport.Instance.GetOrphans(typeof(SlotDataAsset));
                    SelectByAssetItems(orphans);
                    Repaint();
                }
            });

            AddMenuItemWithCallback(_AddressablesMenu, "Select Orphaned Overlays", () =>
            {
                if (EditorUtility.DisplayDialog("Warning!", "You *must* build the addressable groups, and mark any slots you want to keep as 'keep' before running this.", "OK", "Cancel"))
                {
                    List<AssetItem> orphans = UMAAddressablesSupport.Instance.GetOrphans(typeof(OverlayDataAsset));
                    SelectByAssetItems(orphans);
                    Repaint();
                }
            });

#else
			AddMenuItemWithCallback(_AddressablesMenu, "Enable Addressables (Package must be installed first)", () =>
			{
				if (EditorUtility.DisplayDialog("Warning!", "The Addressables Package must be installed first before enabling Addressables support in UMA. Enabling addressables will trigger a recompile during which the library will be unavailable.", "OK", "Cancel"))
				{
                    var currentBuildTarget = UMASettingsProvider.CurrentNamedBuildTarget;
                    var defines = PlayerSettings.GetScriptingDefineSymbols(currentBuildTarget);
					var defineSymbols = new HashSet<string>(defines.Split(';'));

					defineSymbols.Add("UMA_ADDRESSABLES");
                    PlayerSettings.SetScriptingDefineSymbols(currentBuildTarget, string.Join(";", defineSymbols));
					m_Initialized = false;
					Repaint();
				}
			});
#endif
            // ***********************************************************************************
            // Items Menu items
            // ***********************************************************************************
            AddMenuItemWithCallback(ItemsMenu, "Select All", () =>
            {
                SelectAll();
                return;
            });

            AddMenuItemWithCallback(ItemsMenu, "Select all highlighted items", () =>
            {
                SetHighlighted(true);
                return;
            });


            AddMenuItemWithCallback(ItemsMenu, "Clear Selection", () =>
            {
                ClearSelection();
                return;
            });

            AddMenuItemWithCallback(ItemsMenu, "Clear highlighted items", () =>
            {
                SetHighlighted(false);
                return;
            });


            AddMenuItemWithCallback(ToolsMenu, "Validate All Indexed Slots", () =>
            {
                if (UAI == null) return;
				List<SlotDataAsset> slots = UMAAssetIndexer.Instance.GetAllAssets<SlotDataAsset>();
				var issues = new List<SlotValidationReportWindow.SlotIssue>();
				var reasons = new List<string>();
				try
				{
					EditorUtility.DisplayProgressBar("Validating", "Validating Slots", 0.0f);
					for (int i = 0; i < slots.Count; i++)
					{
						SlotDataAsset sda = slots[i];
						float perc = (slots.Count > 0) ? ((float)i / (float)slots.Count) : 1.0f;
						EditorUtility.DisplayProgressBar("Validating", sda != null ? ("Validating " + sda.name) : "Validating", perc);

						if (sda == null)
						{
							continue;
						}

						if (!sda.ValidateMeshData(reasons))
						{
							issues.Add(new SlotValidationReportWindow.SlotIssue
							{
								Slot = sda,
								Reasons = new List<string>(reasons)
							});
						}
					}
				}
				finally
				{
					EditorUtility.ClearProgressBar();
				}

				if (issues.Count == 0)
				{
					EditorUtility.DisplayDialog("Validate Slots", "No invalid slots detected.", "OK");
					return;
				}

				SlotValidationReportWindow.ShowReport(issues);
            });



            foreach (RaceData rc in UAI.GetAllAssets<RaceData>())
            {
                if (rc != null)
                {
                    AddMenuItemWithCallbackParm(ItemsMenu, "Select Slots + Overlays By Race/" + rc.raceName, SelectByRace, rc);
                    AddMenuItemWithCallbackParm(ItemsMenu, "Select Slots By Race/" + rc.raceName, SelectSlotsByRace, rc);
                    AddMenuItemWithCallbackParm(ItemsMenu, "Select Overlays By Race/" + rc.raceName, SelectOverlaysByRace, rc);
                }
            }

            ItemsMenu.AddSeparator("");

            AddMenuItemWithCallback(ItemsMenu, "Add Selected Items to Unity Selection", () =>
            {
                SelectSelected(false);
                return;
            });

            AddMenuItemWithCallback(ItemsMenu, "Add Selected Items to Unity Selection(include Dependencies)", () =>
            {
                SelectSelected(true);
                return;
            });

            ItemsMenu.AddSeparator("");

            AddMenuItemWithCallback(ItemsMenu, "Add Keep Flag to Selected Items", () =>
            {
                MarkKeep(true);
                Repaint();
                return;
            });

            AddMenuItemWithCallback(ItemsMenu, "Clear Keep Flag from Selected Items", () =>
            {
                MarkKeep(false);
                Repaint();
                return;
            });

            AddMenuItemWithCallback(ItemsMenu, "Add Ignore Flag to Selected Items", () =>
            {
                MarkIgnore(true);
                Repaint();
                return;
            });

            AddMenuItemWithCallback(ItemsMenu, "Clear Ignore Flag from Selected Items", () =>
            {
                MarkIgnore(false);
                Repaint();
                return;
            });

            ItemsMenu.AddSeparator("");

            AddMenuItemWithCallback(ItemsMenu, "Apply selected races to selected wardrobe recipes", () =>
             {
                 ApplyRacesToRecipes();
                 Repaint();
                 return;
             });

            AddMenuItemWithCallback(ItemsMenu, "Copy highlighted wardrobe recipe settings to checked wardrobe recipes", () =>
            {
                CopyHighlightedToChecked();
                Repaint();
                return;
            });

            AddMenuItemWithCallback(ItemsMenu, "Copy highlighted wardrobe recipe shared colors to checked wardrobe recipes", () =>
            {
                CopyHighlightedColorsToChecked();
                Repaint();
                return;
            });

            AddMenuItemWithCallback(ItemsMenu, "Remove Selected", () =>
            {
                RemoveSelected();
                m_Initialized = false;
                Repaint();
                return;
            });

            ItemsMenu.AddSeparator("");

            AddMenuItemWithCallback(ItemsMenu,"Recount Types", () =>
            {
                RecountTypes();
                m_Initialized = false;
                Repaint();
                return;
            });


            AddMenuItemWithCallback(ItemsMenu, "Permanently delete Selected", () =>
            {
                if (EditorUtility.DisplayDialog("Warning!", "This is permanent! There is NO undo! If you really want to continue, press 'Delete Selected'", "Delete Selected", "Cancel"))
                {
                    DeleteSelected();
                    m_Initialized = false;
                    Repaint();
                    return;
                }
            });

            AddMenuItemWithCallback(ItemsMenu, "Calculate size of selected items", () =>
            {
                int sizek = CalculateSelectedSize();
                EditorUtility.DisplayDialog("Calculate Size", $"Size of selected items is {sizek}k", "OK");
            });


            AddMenuItemWithCallback(ItemsMenu, "Force Selected Items to Save", () =>
            {
                ForceSave();
                m_Initialized = false;
                Repaint();
                return;
            });



        }

        private void ClearSelection()
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);
            foreach (AssetTreeElement ate in treeElements)
            {
                ate.Checked = false;
            }
            treeView.RecalcTypeChecks();
            Repaint();
        }

        private void SelectAll()
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);
            foreach (AssetTreeElement ate in treeElements)
            {
                ate.Checked = true;
            }
            treeView.RecalcTypeChecks();
            Repaint();
        }

        private Dictionary<int, AssetTreeElement> GetAllItems()
        {
            Dictionary<int, AssetTreeElement> AllItems = new Dictionary<int, AssetTreeElement>();
            if (treeView == null || treeView.treeModel == null) return AllItems;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement ate in treeElements)
            {
                AllItems.Add(ate.id, ate);
            }

            return AllItems;
        }

        private List<AssetTreeElement> GetHighlightedItems()
        {
            Dictionary<int, AssetTreeElement> allItems = GetAllItems();
            if (treeView == null) return new List<AssetTreeElement>();
            IList<int> list = treeView.GetSelection();

            var treeElements = new List<AssetTreeElement>();

            foreach (int i in list)
            {
                if (allItems.ContainsKey(i))
                {
                    treeElements.Add(allItems[i]);
                }
            }
            return treeElements;
        }

        private void SetHighlighted(bool v)
        {
            var selected = GetHighlightedItems();
            foreach (AssetTreeElement ate in selected)
            {
                ate.Checked = v;
            }
            if (treeView != null)
            {
                treeView.RecalcTypeChecks();
            }
            Repaint();
        }


        private void CopyHighlightedToChecked()
        {
            var highlight = GetHighlightedItems();
            var selected = GetSelectedElements();

            if (highlight.Count > 1 || highlight.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "One UMAWardrobeRecipe must be highlighted in the tree. This item will be used as the source item.", "OK");
                return;
            }

            if (highlight[0].ai.Item as UMAWardrobeRecipe == null)
            {
                EditorUtility.DisplayDialog("Error", "A UMAWardrobeRecipe must be highlighted in the tree. This item will be used as the source item.", "OK");
                return;
            }

            if (selected.Count < 1)
            {
                EditorUtility.DisplayDialog("Error", "At least one UMAWardrobeRecipe must be checked in the tree. These items will be updated", "OK");
                return;
            }

            UMAWardrobeRecipe source = highlight[0].ai.Item as UMAWardrobeRecipe;

            foreach (var ate in selected)
            {
                if (ate.ai.Item is UMAWardrobeRecipe)
                {
                    UMAWardrobeRecipe uwr = ate.ai.Item as UMAWardrobeRecipe;
                    // Copy Compatible Races
                    foreach (string s in source.compatibleRaces)
                    {
                        if (uwr.compatibleRaces.Contains(s)) continue;
                        uwr.compatibleRaces.Add(s);
                    }
                    uwr.wardrobeSlot = source.wardrobeSlot;
                    EditorUtility.SetDirty(uwr);
                }
            }
            if (UAI != null) UAI.ForceSave();
            if (treeView != null) treeView.RecalcTypeChecks();
            Repaint();
            EditorUtility.DisplayDialog("Copy", "Complete", "OK");
        }

        private void CopyHighlightedColorsToChecked()
        {
            var highlight = GetHighlightedItems();
            var selected = GetSelectedElements();

            if (highlight.Count > 1 || highlight.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "One Recipe must be highlighted in the tree. This item will be used as the source item.", "OK");
                return;
            }

            if (highlight[0].ai.Item as UMAWardrobeRecipe == null)
            {
                EditorUtility.DisplayDialog("Error", "A Recipe must be highlighted in the tree. This item will be used as the source item.", "OK");
                return;
            }

            if (selected.Count < 1)
            {
                EditorUtility.DisplayDialog("Error", "At least one Recipe must be checked in the tree. These items will be updated", "OK");
                return;
            }


            UMATextRecipe source = highlight[0].ai.Item as UMATextRecipe;
            UMAPackedRecipeBase.UMAPackRecipe upr = source.PackedLoad();
            UMAPackedRecipeBase.PackedOverlayColorDataV3[] sourceColors = upr.fColors; 

            if (sourceColors == null)
            {
                EditorUtility.DisplayDialog("Error", "Source recipe does not have any shared colors", "OK");
                return;
            }

            foreach (var ate in selected)
            {
                if (ate.ai.Item is UMATextRecipe)
                {
                    UMATextRecipe utr = ate.ai.Item as UMATextRecipe;

                    UMAPackedRecipeBase.UMAPackRecipe dest = utr.PackedLoad();
                    if (dest.fColors == null)
                    {
                        dest.fColors = sourceColors;
                        continue;
                    }

                    List<UMAPackedRecipeBase.PackedOverlayColorDataV3> currentColors = new List<UMAPackedRecipeBase.PackedOverlayColorDataV3>();
                    currentColors.AddRange(dest.fColors);
                    foreach (var color in sourceColors)
                    {
                        if (string.IsNullOrEmpty(color.name))
                            continue;
                        if (color.name.StartsWith("-"))
                            continue;
                        bool found = false;
                        foreach (var ocd in currentColors)
                        {
                            if (ocd.name == color.name)
                            {
                                ocd.colors = color.colors;
                                ocd.ShaderParms = color.ShaderParms;
                                found = true;
                            }
                        }
                        if (!found)
                        {
                            currentColors.Add(color);
                        }
                    }

                    dest.fColors = currentColors.ToArray();
                    dest.sharedColorCount = dest.fColors.Length;
                    utr.PackedSave(dest);
                    UMAData.UMARecipe ur = new UMAData.UMARecipe();
                    utr.Load(ur);
                    EditorUtility.SetDirty(utr);
                    ate.ai._SerializedItem = null;
                }
            }

            if (UAI != null) UAI.ForceSave();
            if (treeView != null) treeView.RecalcTypeChecks();
            Repaint();
            EditorUtility.DisplayDialog("Copy", "Complete", "OK");
        }

        private void ApplyRacesToRecipes()
        {
            List<AssetTreeElement> selectedElements = GetSelectedElements();

            List<RaceData> races = new List<RaceData>();
            List<UMATextRecipe> recipes = new List<UMATextRecipe>();

            foreach (AssetTreeElement element in selectedElements)
            {
                AssetItem item = element.ai;
                if (item != null)
                {
                    if (item._Type.IsAssignableFrom(typeof(UMAWardrobeRecipe)) || item._Type.IsSubclassOf(typeof(UMAWardrobeRecipe)) || item._Type == typeof(UMAWardrobeCollection))
                    {
                        recipes.Add(item.Item as UMATextRecipe);
                    }
                    if (item._Type.IsAssignableFrom(typeof(RaceData)) || item._Type.IsSubclassOf(typeof(RaceData)))
                    {
                        races.Add(item.Item as RaceData);
                    }
                }
            }

            if (races.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No races selected. You must select both the races and the wardrobe items to run this command.", "OK");
                return;
            }
            if (recipes.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No wardrobe recipes/collections selected. You must select both the races and the wardrobe items to run this command.", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog("Update Recipes?", "This will apply the selected race(s) to the selected wardrobe items (UMAWardrobeRecipe or UMAWardrobeCollection", "Continue", "Cancel"))
            {
                foreach (UMATextRecipe uwr in recipes)
                {
                    foreach (RaceData race in races)
                    {
                        uwr.compatibleRaces.Add(race.raceName);
                    }
                    EditorUtility.SetDirty(uwr);
                }
                if (UAI != null) UAI.ForceSave();
                EditorUtility.DisplayDialog("Update Races", "Races assigned and index saved", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Update Recipes", "Race application was cancelled", "OK");
            }
        }

        private void SelectSelected(bool AddDependencies)
        {
            List<AssetTreeElement> selectedElements = GetSelectedElements();
            if (selectedElements.Count == 0)
            {
                EditorUtility.DisplayDialog("Warning", "No items are selected. Please select the items in the list before using this option.", "OK");
                return;
            }

            List<UnityEngine.Object> selectedObjects = new List<UnityEngine.Object>();
            foreach (AssetTreeElement element in selectedElements)
            {
                AssetItem item = element.ai;
                if (item != null)
                {
                    selectedObjects.Add(item.Item);
                    if (AddDependencies)
                    {
                        List<UnityEngine.Object> dependencies = GetDependencies(item.Item);
                        selectedObjects.AddRange(dependencies);
                    }
                }
            }
            Selection.objects = selectedObjects.ToArray();
        }

        private List<UnityEngine.Object> GetDependencies(UnityEngine.Object item)
        {
            if (item is SlotDataAsset)
            {
                return GetSlotDependencies(item as SlotDataAsset);
            }
            if (item is OverlayDataAsset)
            {
                return GetOverlayDependencies(item as OverlayDataAsset);
            }
            if (item is RaceData)
            {
                return GetRaceDependencies(item as RaceData);
            }
            if (item is UMATextRecipe)
            {
                return GetRecipeDependencies(item as UMATextRecipe);
            }
            // return an empty list.
            return new List<UnityEngine.Object>();
        }

        private List<UnityEngine.Object> GetRaceDependencies(RaceData raceData)
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

            if (raceData.baseRaceRecipe != null)
            {
                objects.Add(raceData.baseRaceRecipe);
                objects.AddRange(GetRecipeDependencies(raceData.baseRaceRecipe as UMATextRecipe));
            }
            if (raceData.TPose != null)
                objects.Add(raceData.TPose);

            if (raceData.expressionSet != null)
            {
                objects.Add(raceData.expressionSet);
                objects.AddRange(GetExpressionSetDependencies(raceData.expressionSet));
            }

            if (raceData.dnaConverterList != null)
            {
                foreach (var dna in raceData.dnaConverterList)
                {
                    objects.AddRange(GetDNADepenencies(dna));
                }
            }

            if (raceData.dnaRanges != null)
            {
                objects.AddRange(raceData.dnaRanges);
            }
            return objects;
        }

        private IEnumerable<UnityEngine.Object> GetExpressionSetDependencies(UMAExpressionSet expressionSet)
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

            foreach (var posepair in expressionSet.posePairs)
            {
                if (posepair.primary != null)
                    objects.Add(posepair.primary);
                if (posepair.inverse != null)
                    objects.Add(posepair.inverse);
            }
            return objects;
        }

        private List<UnityEngine.Object> GetRecipeDependencies(UMATextRecipe uMATextRecipe)
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
            if (UAI == null) return objects;
            List<AssetItem> dependencies = UMAAssetIndexer.Instance.GetAssetItems(uMATextRecipe, true);

            foreach (AssetItem ai in dependencies)
            {
                if (ai.Item != null)
                {
                    if (ai.Item is SlotDataAsset)
                    {
                        SlotDataAsset sda = ai.Item as SlotDataAsset;
                        objects.Add(sda);
                        objects.AddRange(GetSlotDependencies(sda));
                    }
                    if (ai.Item is OverlayDataAsset)
                    {
                        OverlayDataAsset oda = ai.Item as OverlayDataAsset;
                        objects.Add(oda);
                        objects.AddRange(GetOverlayDependencies(oda));
                    }
                }
            }

            if (uMATextRecipe.MeshHideAssets != null)
            {
                foreach (MeshHideAsset mha in uMATextRecipe.MeshHideAssets)
                {
                    if (mha != null)
                    {
                        objects.Add(mha);
                    }
                }
            }

            return objects;
        }

        private List<UnityEngine.Object> GetOverlayDependencies(OverlayDataAsset overlayDataAsset)
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

            if (overlayDataAsset.material != null)
            {
                objects.Add(overlayDataAsset.material);
                if (overlayDataAsset.material.material != null)
                {
                    objects.Add(overlayDataAsset.material.material);
                    objects.AddRange(GetMaterialDepencies(overlayDataAsset.material.material));
                }
            }

            if (overlayDataAsset.alphaMask != null)
            {
                objects.Add(overlayDataAsset.alphaMask);
            }

            foreach (Texture t in overlayDataAsset.textureList)
            {
                if (t != null)
                    objects.Add(t);
            }
            return objects;
        }

        private List<UnityEngine.Object> GetSlotDependencies(SlotDataAsset slotDataAsset)
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
            if (slotDataAsset.RendererAsset != null)
            {
                objects.Add(slotDataAsset.RendererAsset);
            }
            if (slotDataAsset.slotDNA != null)
            {
                objects.AddRange(GetDNADepenencies(slotDataAsset.slotDNA));
            }
            return objects;
        }

        private List<UnityEngine.Object> GetMaterialDepencies(Material material)
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

            string[] txprops = material.GetTexturePropertyNames();

            foreach (string s in txprops)
            {
                Texture t = material.GetTexture(s);
                if (t is Texture2D)
                {
                    objects.Add(t);
                }
            }
            return objects;
        }

        private List<UnityEngine.Object> GetDNADepenencies(IDNAConverter converter)
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

            if (converter is DynamicDNAConverterController)
            {
                var cvt = converter as DynamicDNAConverterController;
                objects.Add(cvt);
                if (cvt.dnaAsset != null)
                {
                    objects.Add(cvt.dnaAsset);
                }
                List<DynamicDNAPlugin> plugins = cvt.GetPlugins();
                foreach (var p in plugins)
                {
                    if (p != null)
                        objects.Add(p);
                    if (p is BonePoseDNAConverterPlugin)
                    {
                        var bp = p as BonePoseDNAConverterPlugin;
                        foreach (var pdc in bp.poseDNAConverters)
                        {
                            objects.Add(pdc.poseToApply);
                        }
                    }
                }
            }
            return objects;
        }

        void SetItemMaterial(AssetItem ai)
        {
            if (ai._Type == typeof(OverlayDataAsset))
            {
                (ai.Item as OverlayDataAsset).material = umaMaterial;
                EditorUtility.SetDirty(ai.Item);
            }
        }

        bool RemoveItemMHA(AssetItem ai)
        {
            UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
            if (uwr != null)
            {
                bool removed = uwr.MeshHideAssets.Remove(AddedMHA);
                if (removed)
                {
                    EditorUtility.SetDirty(uwr);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(uwr);
#else
                    AssetDatabase.SaveAssets();
#endif
                    string path = AssetDatabase.GetAssetPath(uwr.GetEntityId());
                    AssetDatabase.ImportAsset(path);
                    return true;
                }
            }
            else
            {
                Debug.LogError("Error: Processed item is null: " + ai._Name);
            }
            return false;
        }

        bool SetItemMHA(AssetItem ai)
        {
            UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
            if (uwr != null)
            {
                bool found = false;
                foreach (MeshHideAsset theAsset in uwr.MeshHideAssets)
                {
                    if (theAsset.GetEntityId() == AddedMHA.GetEntityId())
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Debug.Log("Updating item: " + ai._Name);
                    uwr.MeshHideAssets.Add(AddedMHA);
                    EditorUtility.SetDirty(uwr);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(uwr);
#else
                    AssetDatabase.SaveAssets();
#endif

                    string path = AssetDatabase.GetAssetPath(uwr.GetEntityId());
                    AssetDatabase.ImportAsset(path);
                    return true;
                }
                else
                {

                }
            }
            else
            {
                Debug.LogError("Error: Processed item is null: " + ai._Name);
            }
            return false;
        }

        void UpdateMeshHideAssets()
        {
            int count = 0;
            int founditems = 0;
            if (treeView == null || treeView.treeModel == null) return;
            List<AssetTreeElement> treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement ate in treeElements)
            {
                if (ate.ai != null && ate.Checked)
                {
                    founditems++;
                    if (ate.ai.Item is UMAWardrobeRecipe)
                    {
                        if (SetItemMHA(ate.ai))
                        {
                            count++;
                        }
                    }
                    else
                    {
                        Debug.Log("Item is not a wardrobe item! " + ate.ai._Name);
                    }
                }
            }
            if (founditems < 1)
            {
                EditorUtility.DisplayDialog("Info", "No items found to update.", "OK");
            }
            else
            {
                if (count > 0)
                {
                    EditorUtility.DisplayDialog("Info", count + " recipes updated.", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Info", "No recipes updated.", "OK");
                }
            }
        }


        List<AssetItem> GetSelectedAssets()
        {
            List<AssetItem> assets = new List<AssetItem>();

            if (treeView == null || treeView.treeModel == null) return assets;
            List<AssetTreeElement> treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement ate in treeElements)
            {
                if (ate.ai != null && ate.Checked)
                {
                    assets.Add(ate.ai);
                }
            }
            return assets;
        }

        List<AssetItem> GetSelectedAssets(Type type)
        {
            List<AssetItem> assets = new List<AssetItem>();

            if (treeView == null || treeView.treeModel == null) return assets;
            List<AssetTreeElement> treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement ate in treeElements)
            {
                if (ate.ai != null && ate.Checked)
                {
                    if (ate.ai._Type == type)
                    {
                        assets.Add(ate.ai);
                    }
                }
            }
            return assets;
        }

        bool setRect;
        bool setRotation;
        bool setScale;

        private void UpdateRecipeTransforms(Rect rect, Vector3 scale, float rotation, bool rectLimit, Rect rectCheck)
        {
            var assets = GetSelectedAssets(typeof(UMAWardrobeRecipe));
            foreach (var ai in assets)
            {
                UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
                if (uwr != null)
                {
                    uwr.PackedLoad();
                    UMAData.UMARecipe _recipe = new UMAData.UMARecipe();
                    uwr.Load(_recipe);

                    foreach (SlotData sd in _recipe.slotDataList)
                    {
                        if (sd != null)
                        {
                            var ovls = sd.GetOverlayList();

                            foreach (OverlayData od in ovls)
                            {
                                if (_overlayLimit)
                                {
                                    if (od.overlayName != umaOverlay.overlayName)
                                    {
                                        continue;
                                    }
                                }
                                if (rectLimit)
                                {
                                    if (od.rect.x != rectCheck.x || od.rect.y != rectCheck.y || od.rect.width != rectCheck.width || od.rect.height != rectCheck.height)
                                    {
                                        continue;
                                    }
                                }
                                if (setRect)
                                {
                                    od.rect.Set(rect.x, rect.y, rect.width, rect.height);
                                }
                                if (setScale)
                                {
                                    od.Scale.Set(scale.x, scale.y);
                                }
                                if (setRotation)
                                {
                                    od.Rotation = rotation;
                                }
                            }
                        }
                    }


                    uwr.Save(_recipe);
                    EditorUtility.SetDirty(uwr);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(uwr);
#else
                    AssetDatabase.SaveAssets();
#endif

                    string path = AssetDatabase.GetAssetPath(uwr.GetEntityId());
                    AssetDatabase.ImportAsset(path);
                }
            }
        }

        void AddToWardrobeRecipes(RaceData race)
        {
            var assets = GetSelectedAssets(typeof(UMAWardrobeRecipe));
            foreach (var ai in assets)
            {
                UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
                if (uwr != null)
                {
                    uwr.compatibleRaces.Add(race.raceName);
                    EditorUtility.SetDirty(uwr);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(uwr);
#else
                    AssetDatabase.SaveAssets();
#endif

                    string path = AssetDatabase.GetAssetPath(uwr.GetEntityId());
                    AssetDatabase.ImportAsset(path);
                }
            }
        }

        void ReplaceRaceInWardrobeRecipes(RaceData race)
        {
            var assets = GetSelectedAssets(typeof(UMAWardrobeRecipe));
            foreach (var ai in assets)
            {
                UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
                if (uwr != null)
                {
                    uwr.compatibleRaces.Clear();
                    uwr.compatibleRaces.Add(race.raceName);
                    EditorUtility.SetDirty(uwr);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(uwr);
#else
                    AssetDatabase.SaveAssets();
#endif

                    string path = AssetDatabase.GetAssetPath(uwr.GetEntityId());
                    AssetDatabase.ImportAsset(path);
                }
            }
        }

        void RemoveRaceFromWardrobeRecipes(RaceData race)
        {
            var assets = GetSelectedAssets(typeof(UMAWardrobeRecipe));
            foreach (var ai in assets)
            {
                UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
                if (uwr != null)
                {
                    uwr.compatibleRaces.Remove(race.raceName);
                    EditorUtility.SetDirty(uwr);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(uwr);
#else
                    AssetDatabase.SaveAssets();
#endif

                    string path = AssetDatabase.GetAssetPath(uwr.GetEntityId());
                    AssetDatabase.ImportAsset(path);
                }
            }
        }

        void SelectAllWardrobeRecipesForRace(RaceData race)
        {
            if (UAI == null) return;
            List<AssetItem> allRecipes = UMAAssetIndexer.Instance.GetAssetItems<UMAWardrobeRecipe>();
            List<AssetItem> selectedItems = new List<AssetItem>();
            foreach (var ai in allRecipes)
            {
                if (ai.Item != null)
                {
                    UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
                    if (uwr != null)
                    {
                        if (uwr.compatibleRaces.Contains(race.raceName))
                        {
                            if (filterBySlot)
                            {
                                if (uwr.wardrobeSlot == filterSlot)
                                {
                                    selectedItems.Add(ai);
                                }
                            }
                            else
                            {
                                selectedItems.Add(ai);
                            }
                        }
                    }
                }
            }
            SelectByAssetItems(selectedItems);
        }

        void SelectBaseRecipeForRace(RaceData race)
        {
            if (UAI == null) return;
            List<AssetItem> allRecipes = UMAAssetIndexer.Instance.GetAssetItems<UMATextRecipe>();
            List<AssetItem> selectedItems = new List<AssetItem>();
            foreach (var ai in allRecipes)
            {
                if (ai.Item != null)
                {
                    UMATextRecipe utr = ai.Item as UMATextRecipe;
                    if (utr != null)
                    {
                        if (utr.name == race.baseRaceRecipe.name)
                        {
                            selectedItems.Add(ai);
                        }
                    }
                }
            }
            SelectByAssetItems(selectedItems);
        }

        void RemoveMeshHideAssets()
        {
            int count = 0;
            int founditems = 0;
            if (treeView == null || treeView.treeModel == null) return;
            List<AssetTreeElement> treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement ate in treeElements)
            {
                if (ate.ai != null && ate.Checked)
                {
                    founditems++;
                    if (ate.ai.Item is UMAWardrobeRecipe)
                    {
                        if (RemoveItemMHA(ate.ai))
                        {
                            count++;
                        }
                    }
                    else
                    {
                        Debug.Log("Item is not a wardrobe item! " + ate.ai._Name);
                    }
                }
            }
            if (founditems < 1)
            {
                EditorUtility.DisplayDialog("Info", "No items found to update.", "OK");
            }
            else
            {
                if (count > 0)
                {
                    EditorUtility.DisplayDialog("Info", count + " recipes updated.", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Info", "No recipes updated.", "OK");
                }
            }
        }
        void UpdateMaterials()
        {
            if (treeView == null || treeView.treeModel == null) return;
            List<AssetTreeElement> treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement ate in treeElements)
            {
                if (ate.ai != null && ate.Checked)
                {
                    SetItemMaterial(ate.ai);
                }
            }
        }

        void MarkKeep(bool Keep)
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement tr in treeElements)
            {
                if (tr.ai != null && tr.Checked)
                {
                    tr.ai.IsAlwaysLoaded = Keep;
                }
            }
            if (UAI != null) UMAAssetIndexer.Instance.ForceSave();
            RecountTypes();
        }

        void MarkIgnore(bool IgnoreFlag)
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement tr in treeElements)
            {
                if (tr.ai != null && tr.Checked)
                {
                    tr.ai.Ignore = IgnoreFlag;
                }
            }
            if (UAI != null) UMAAssetIndexer.Instance.ForceSave();
            RecountTypes();
        }

        void SelectByAssetItems(List<AssetItem> items, bool recalculate = true)
        {
            if (treeView == null || treeView.treeModel == null) return;
            Dictionary<Type,List<AssetItem>> indexedItems = new Dictionary<Type, List<AssetItem>>();

            for (int i = 0;i < items.Count; i++)
            {
                if (!indexedItems.ContainsKey(items[i]._Type))
                {
                    indexedItems.Add(items[i]._Type, new List<AssetItem>());
                }
                indexedItems[items[i]._Type].Add(items[i]);
            }

            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement ate in treeElements)
            {
                if (ate.ai != null && indexedItems.ContainsKey(ate.ai._Type))
                {
                    if (indexedItems[ate.ai._Type].Contains(ate.ai))
                    {
                        ate.Checked = true;
                    }
                }
                //if (ate.ai != null && items.Contains(ate.ai))
                //{
                //    ate.Checked = true;
                //}
            }
            if (recalculate && treeView != null)
            {
                treeView.RecalcTypeChecks();
            }
        }

        void FixupTextureChannels(UMAMaterial material)
        {
            if (UAI == null) return;
            int ChannelLength = material.channels.Length;

            var Overlays = UMAAssetIndexer.Instance.GetAllAssets<OverlayDataAsset>();

            foreach (OverlayDataAsset oda in Overlays)
            {
                if (oda.material == null) continue;

                if (oda.material.name == material.name)
                {
                    if (oda.textureCount == ChannelLength) continue;

                    if (oda.textureCount > ChannelLength)
                    {
                        // lower the texture count.
                        List<Texture> newTextures = new List<Texture>();
                        for (int i = 0; i < ChannelLength; i++)
                        {
                            newTextures.Add(oda.textureList[i]);
                        }
                        oda.textureList = newTextures.ToArray();
                        EditorUtility.SetDirty(oda);
                    }
                    else
                    {
                        // todo: increase the texture count.
                    }
                    // todo: We may need to go through the recipes and update the "ColorData" array to have the right number of channels.
                }
            }
            AssetDatabase.SaveAssets();
        }

        void SelectMaterial(UMAMaterial material)
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);
            foreach (AssetTreeElement ate in treeElements)
            {
                if (ate.type == typeof(UMAMaterial))
                {
                    if (ate.ai != null)
                    {
                        UMAMaterial um = ate.ai.Item as UMAMaterial;
                        if (um.name == material.name)
                        {
                            ate.Checked = true;
                        }
                    }
                }
                treeView.RecalcTypeChecks();
            }
        }


        void SelectByMaterial(UMAMaterial material, Type assetType)
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement ate in treeElements)
            {
                if (ate.ai != null)
                {
                    if (ate.type == assetType)
                    {
                        if (ate.type == typeof(OverlayDataAsset))
                        {
                            OverlayDataAsset oda = ate.ai.Item as OverlayDataAsset;
                            if (oda.material == null) continue;

                            if (oda.material.name == material.name)
                            {
                                ate.Checked = true;
                            }
                        }
                    }
                }
            }
            treeView.RecalcTypeChecks();
        }

        void SelectByRace(object Race)
        {
            if (UAI == null) return;
            RaceData rc = Race as RaceData;
            List<AssetItem> recipeItems = UAI.GetAssetItems(rc.baseRaceRecipe as UMAPackedRecipeBase);
            SelectByAssetItems(recipeItems);
        }

        void SelectSlotsByRace(object Race)
        {
            if (UAI == null) return;
            RaceData rc = Race as RaceData;
            List<AssetItem> recipeItems = UAI.GetAssetItems(rc.baseRaceRecipe as UMAPackedRecipeBase);

            recipeItems = FilterAssetItemsByType(recipeItems, typeof(SlotDataAsset));
            SelectByAssetItems(recipeItems);
        }

        void SelectOverlaysByRace(object Race)
        {
            if (UAI == null) return;
            RaceData rc = Race as RaceData;
            List<AssetItem> recipeItems = UAI.GetAssetItems(rc.baseRaceRecipe as UMAPackedRecipeBase);
            recipeItems = FilterAssetItemsByType(recipeItems, typeof(OverlayDataAsset));
            SelectByAssetItems(recipeItems);
        }

        public void RecountTypes()
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            List<AssetTreeElement> Types = new List<AssetTreeElement>();
            foreach (TreeElement t in treeView.treeModel.root.children)
            {
                AssetTreeElement ate = t as AssetTreeElement;
                ate.IsResourceCount = 0;
				ate.LoadedCount = 0;
                ate.IsAddrCount = 0;
                ate.Keepcount = 0;
                ate.IgnoreCount = 0;
                ate.totalCount = 0;
                if (t.hasChildren)
                {
                    foreach (TreeElement c in t.children)
                    {
                        AssetItem ai = (c as AssetTreeElement).ai;
                        if (ai.IsResource)
                        {
                            ate.IsResourceCount++;
                        }

						if (ai._SerializedItem != null)
						{
							ate.LoadedCount++;
						}

                        if (ai.IsAlwaysLoaded)
                        {
                            ate.Keepcount++;
                        }

                        if (ai.IsAddressable)
                        {
                            ate.IsAddrCount++;
                        }

                        if (ai.Ignore)
                        {
                            ate.IgnoreCount++;
                        }

                        ate.totalCount++;
                    }
                }
            }
        }


        private List<AssetTreeElement> GetSelectedElements()
        {
            var treeElements = new List<AssetTreeElement>();
            var selectedElements = new List<AssetTreeElement>();
            if (treeView == null || treeView.treeModel == null) return selectedElements;
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement tr in treeElements)
            {
                if (tr.ai != null && tr.Checked)
                {
                    selectedElements.Add(tr);
                }
            }
            return selectedElements;
        }

        private void ForceSave()
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            EditorUtility.DisplayProgressBar("Marking Assets", "Finding and marking selected assets", 0.0f);

            float total = 0.0f;
            foreach (AssetTreeElement tr in treeElements)
            {
                if (tr.ai != null && tr.Checked)
                {
                    total += 1.0f;
                }
            }

            if (total > 0.0f)
            {
                float current = 0.0f;
                foreach (AssetTreeElement tr in treeElements)
                {
                    if (tr.ai != null && tr.Checked)
                    {

                        EditorUtility.DisplayProgressBar("Marking Assets", "Marking Item: " + tr.ai.EvilName, current / total);
                        EditorUtility.SetDirty(tr.ai.Item);
                        current += 1.0f;
                    }
                }
            }
            EditorUtility.DisplayProgressBar("Saving Assets", "Save Assets to Disk", 1.0f);
            AssetDatabase.SaveAssets();
            EditorUtility.ClearProgressBar();
        }

        private void DeleteSelected()
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            EditorUtility.DisplayProgressBar("Deleting Assets", "Finding and deleting selected assets from filesystem", 0.0f);

            float total = 0.0f;
            foreach (AssetTreeElement tr in treeElements)
            {
                if (tr.ai != null && tr.Checked)
                {
                    total += 1.0f;
                }
            }

            if (total > 0.0f)
            {
                float current = 0.0f;
                foreach (AssetTreeElement tr in treeElements)
                {
                    if (tr.ai != null && tr.Checked)
                    {

                        EditorUtility.DisplayProgressBar("Deleting Assets", "Deleting Item: " + tr.ai.EvilName, current / total);
                        UAI.DeleteAsset(tr.ai._Type, tr.ai._Name);
                        current += 1.0f;
                    }
                }
            }
            EditorUtility.DisplayProgressBar("Deleting Assets", "Save Index to Disk", 1.0f);
            if (UAI != null) UAI.ForceSave();
            EditorUtility.ClearProgressBar();
        }

        private int CalculateSelectedSize()
        {
            long kbytes = 0;

            if (treeView == null || treeView.treeModel == null) return 0;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement tr in treeElements)
            {
                if (tr.ai != null && tr.Checked)
                {
                    System.IO.FileInfo fi = new System.IO.FileInfo(tr.ai._Path);

                    kbytes += fi.Length;
                }
            }

            return (int)(kbytes / 1024);
        }

        private void RemoveSelected()
        {
            if (treeView == null || treeView.treeModel == null) return;
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            EditorUtility.DisplayProgressBar("Removing Assets", "Finding and removing selected assets", 0.0f);

            float total = 0.0f;
            foreach (AssetTreeElement tr in treeElements)
            {
                if (tr.ai != null && tr.Checked)
                {
                    total += 1.0f;
                }
            }

            if (total > 0.0f)
            {
                float current = 0.0f;
                foreach (AssetTreeElement tr in treeElements)
                {
                    if (tr.ai != null && tr.Checked)
                    {

                        EditorUtility.DisplayProgressBar("Removing Assets", "Removing Item: " + tr.ai.EvilName, current / total);
                        UAI.RemoveAsset(tr.ai._Type, tr.ai._Name, false);
                        current += 1.0f;
                    }
                }
            }
            EditorUtility.DisplayProgressBar("Removing Assets", "Save Index to Disk", 1.0f);
            if (UAI != null) UAI.ForceSave();
            EditorUtility.ClearProgressBar();
        }

        #endregion

        #region GUI Rectangles
        int sidePanelWidth = 300;

        float positionwidth
        {
            get
            {
                if (ShowUtilities)
                {
                    return position.width - sidePanelWidth;
                }
                return position.width;
            }
        }

        Rect multiColumnTreeViewRect
        {
            get
            {
                return new Rect(10, 46, positionwidth - 20, position.height - 90);
            }
        }

        Rect toolbarRect
        {
            get
            {
                return new Rect(10f, 23f, positionwidth - 20f, 20f);
            }
        }

        Rect menubarRect
        {
            get { return new Rect(0f, 0f, positionwidth, 20f); }
        }

        Rect bottomToolbarRect
        {
            get { return new Rect(10f, position.height - 42f, positionwidth - 20f, 40f); }
        }

        Rect AddPadRect
        {
            get
            {
                Rect toolbar = bottomToolbarRect;
                float DropWidth = toolbar.width / 3.0f;

                toolbar.x += 2;
                toolbar.width = DropWidth - 4;
                return toolbar;
            }
        }

        Rect RemovePadRect
        {
            get
            {
                Rect toolbar = bottomToolbarRect;
                float DropWidth = toolbar.width / 3.0f;

                toolbar.x += 2 + DropWidth;
                toolbar.width = DropWidth - 4;
                return toolbar;
            }
        }

        Rect AddTypePadRect
        {
            get
            {
                Rect toolbar = bottomToolbarRect;
                float DropWidth = toolbar.width / 3.0f;

                toolbar.x += 2 + (DropWidth * 2);
                toolbar.width = DropWidth - 4;
                return toolbar;
            }
        }
        #endregion

        #region GUI
        void InitIfNeeded()
        {
            if (m_Initialized) return;
            if (IsEditorBusy()) return;
            if (UAI == null) return;
            // Check if it already exists (deserialized from window layout file or scriptable object)
            if (m_TreeViewState == null)
                m_TreeViewState = new TreeViewState();

            bool firstInit = m_MultiColumnHeaderState == null;
            var headerState = UMAAssetTreeView.CreateDefaultMultiColumnHeaderState(multiColumnTreeViewRect.width);
            if (MultiColumnHeaderState.CanOverwriteSerializedFields(m_MultiColumnHeaderState, headerState))
                MultiColumnHeaderState.OverwriteSerializedFields(m_MultiColumnHeaderState, headerState);
            m_MultiColumnHeaderState = headerState;

            var multiColumnHeader = new MyMultiColumnHeader(headerState);
            multiColumnHeader.mode = MyMultiColumnHeader.Mode.MinimumHeaderWithoutSorting;

            if (firstInit)
                multiColumnHeader.ResizeToFit();

            var treeModel = new TreeModel<AssetTreeElement>(GetData());

            treeView = new UMAAssetTreeView(this, m_TreeViewState, multiColumnHeader, treeModel);

            m_SearchField = new SearchField();
            m_SearchField.downOrUpArrowKeyPressed += treeView.SetFocusAndEnsureSelectedItem;

            m_Initialized = true;
        }

        bool ShouldLoad(eLoaded itemsToLoad, AssetItem ai)
        {
            switch (itemsToLoad)
            {
                case eLoaded.Resx:
                    return (ai.IsResource || (ai.IsAddressable == false));
                case eLoaded.Keep:
                    return ai.IsAlwaysLoaded;
                case eLoaded.Addressable:
                    return ai.IsAddressable;
                case eLoaded.NonAddressable:
                    return !ai.IsAddressable;
                case eLoaded.NoGroups:
                    {
                        if (ai.Item is UMARecipeBase)
                        {
                            UMARecipeBase ubr = ai.Item as UMARecipeBase;
                            if (!LoadedLabels.Contains(ubr.AssignedLabel))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                case eLoaded.SelectedOnly:
                    {
                        if (DoesMatchLoaded(ai))
                            return true;
                        else
                            return false;
                    }
            }
            return true;
        }

        private bool DoesMatchLoaded(AssetItem assetItem)
        {
            for(int i=0; i < LoadOnly.Count; i++)
            {
                if (LoadOnly[i] == assetItem && assetItem._Type == LoadOnly[i]._Type)
                    return true;
            }

            return false;
        }

        IList<AssetTreeElement> GetData()
        {
            LoadedLabels = new HashSet<string>();

            eLoaded itemstoload = (eLoaded)LoadedItems;
            eShowTypes typesToShow = (eShowTypes)ShowIndex;
            int totalitems = 0;
            var treeElements = new List<AssetTreeElement>();

            var root = new AssetTreeElement("Root", -1, totalitems);

            treeElements.Add(root);

            if (UAI == null)
            {
                return treeElements;
            }
            System.Type[] Types = UAI.GetTypes();


            // Preprocess to get labels (we need to filter on them later).
            foreach (System.Type t in Types)
            {
                if (t != typeof(AnimatorController) && t != typeof(AnimatorOverrideController)) // Somewhere, a kitten died because I typed that.
                {
                    Dictionary<string, AssetItem> TypeDic = UAI.GetAssetDictionary(t);
                    AssetItem[] items = new AssetItem[TypeDic.Values.Count];
                    TypeDic.Values.CopyTo(items, 0);

                    List<AssetTreeElement> ElementsToLoad = new List<AssetTreeElement>();
                    for (int i = 0; i < TypeDic.Values.Count; i++)
                    {
                        AssetItem ai = items[i];
                        AddLabels(ai);
                    }
                }
            }



            foreach (System.Type t in Types)
            {
                if (t != typeof(AnimatorController) && t != typeof(AnimatorOverrideController)) // Somewhere, a kitten died because I typed that.
                {
                    Dictionary<string, AssetItem> TypeDic = UAI.GetAssetDictionary(t);

                    AssetTreeElement ate = new AssetTreeElement(t.Name, 0, ++totalitems);
                    ate.type = t;
                    AssetItem[] items = new AssetItem[TypeDic.Values.Count];
                    TypeDic.Values.CopyTo(items, 0);

                    List<AssetTreeElement> ElementsToLoad = new List<AssetTreeElement>();
                    for (int i = 0; i < TypeDic.Values.Count; i++)
                    {
                        AssetItem ai = items[i];
                        if (ShouldLoad(itemstoload, ai))
                        {
                            AssetTreeElement atai = new AssetTreeElement(ai._Name, 1, ++totalitems);
                            atai.ai = ai;
                            atai.index = i;
                            atai.type = t;
                            ElementsToLoad.Add(atai);

                            if (ai.IsResource)
                                ate.IsResourceCount++;
							if (ai._SerializedItem != null)
								ate.LoadedCount++;
                            if (ai.IsAlwaysLoaded)
                                ate.Keepcount++;
                            if (ai.IsAddressable)
                                ate.IsAddrCount++;
                            if (ai.Ignore)
                                ate.IgnoreCount++;
                            ate.totalCount ++;
                        }
                    }

                    if (ElementsToLoad.Count < 1)
                    {
                        if (typesToShow == eShowTypes.WithItems || itemstoload == eLoaded.SelectedOnly)
                            continue;
                    }

                    treeElements.Add(ate);
                    treeElements.AddRange(ElementsToLoad);
                }
            }
            LoadOnly.Clear();
            return treeElements;
            // generate some test data
            //return MyTreeElementGenerator.GenerateRandomTree(130); 
        }

        private void AddLabels(AssetItem ai)
        {
            if (!string.IsNullOrEmpty(ai.AddressableLabels))
            {
                string[] labels = ai.AddressableLabels.Split(';');
                foreach (string s in labels)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        LoadedLabels.Add(s);
                    }
                }
            }
        }

        #region DragDrop
        private void DragDropAdd(Rect dropArea)
        {

            var evt = Event.current;

            if (evt.type == EventType.DragUpdated)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                }
            }

            if (evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.AcceptDrag();
                    UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
                    for (int i = 0; i < draggedObjects.Length; i++)
                    {
                        if (draggedObjects[i])
                        {
                            m_Initialized = false; // need to reload when we're done.
                            if (UAI != null) UAI.AddIfIndexed(draggedObjects[i]);

                            var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
                            if (System.IO.Directory.Exists(path))
                            {
                                if (UAI != null) UAI.RecursiveScanFoldersForAssets(path);
                            }
                        }
                    }
                    if (UAI != null) UAI.ForceSave();
                }
            }
        }
        private void DragDropRemove(Rect dropArea)
        {

            var evt = Event.current;

            if (evt.type == EventType.DragUpdated)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                }
            }

            if (evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.AcceptDrag();
                    UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
                    for (int i = 0; i < draggedObjects.Length; i++)
                    {
                        if (draggedObjects[i])
                        {
                            m_Initialized = false; // need to reload when we're done.
                            if (UAI != null) UAI.RemoveIfIndexed(draggedObjects[i], true);

                            var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
                            if (System.IO.Directory.Exists(path))
                            {
                                if (UAI != null) UAI.RecursiveScanFoldersForRemovingAssets(path);
                            }
                        }
                    }
                    if (UAI != null) UAI.ForceSave();
                }
            }
        }


        private void DragDropType(Rect dropArea)
        {
            var evt = Event.current;

            if (evt.type == EventType.DragUpdated)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                }
            }

            if (evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.AcceptDrag();
                    m_Initialized = false;
                    UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
                    for (int i = 0; i < draggedObjects.Length; i++)
                    {
                        if (draggedObjects[i])
                        {
                            System.Type sType = draggedObjects[i].GetType();
                            if (UAI != null) UAI.AddType(sType);
                        }
                    }
                    if (UAI != null) UAI.ForceSave();
                }
            }
        }
        #endregion

        private string dots = "";
        private bool filterBySlot = false;
        private int selectedSlot = 0;
        private string filterSlot = "";
        private string[] NullArray = { "None"};

        void OnGUI()
        {
            if (IsEditorBusy())
            {
                dots += ".";
                if (dots.Length > 20)
                    dots = "";
                GUILayout.Space(30);
                EditorGUILayout.LabelField("    Compile/update in progress  " + dots);
                Repaint();
                return;
            }
            InitIfNeeded();

            if (UAI == null)
            {
                GUILayout.Space(30);
                EditorGUILayout.HelpBox("UMAAssetIndexer not available during domain reload. Please wait for compilation to finish.", MessageType.Info);
                return;
            }

            GUILayout.BeginArea(new Rect(0, 0, positionwidth, position.height));

            MenuBar(menubarRect);
            SearchBar(toolbarRect);
            DoTreeView(multiColumnTreeViewRect);
            BottomToolBar(bottomToolbarRect);
            GUILayout.EndArea();

            if (ShowUtilities)
            {
                Rect Box = new Rect(positionwidth, 0, sidePanelWidth, position.height);

                GUI.Box(Box, "", EditorStyles.helpBox);
                GUILayout.BeginArea(Box);
                ShowSidebar();
                GUILayout.EndArea();
            }
        }


		private void OpenFileWithEditorUtility(string filePath) {
			if(!File.Exists(filePath)) {
				Debug.LogError($"File does not exist: {filePath}");
				return;
			}

			try {
				// This method works in the Unity Editor and opens with the default application
				UnityEditor.EditorUtility.OpenWithDefaultApp(filePath);
				Debug.Log($"Opened report file: {filePath}");
			} catch(System.Exception ex) {
				Debug.LogError($"Failed to open file {filePath}: {ex.Message}");

				// Fallback: copy path to clipboard
				GUIUtility.systemCopyBuffer = filePath;
				Debug.Log($"File path copied to clipboard: {filePath}");
			}
		}

		Vector2 sideBarPosition;
		bool _IndexFoldout;
        bool _meshHideFoldout;
        bool _materialFoldout;
        bool _raceFoldout;
        bool _recipeFoldout;
        bool _conversionFoldout;
        bool _OverlayFoldout;
        bool _SlotFoldout;
        bool _TextureFoldout;
        Rect _rect;
        Vector3 _scale;
        float _rotation;
        bool _rectLimit;
        bool _overlayLimit;
        Rect _rectCheck;
        int _channelType;
		private UMAAssetIndexer beforeIndex;
		private UMAAssetIndexer afterIndex;
		private UMAAssetIndexer AnalyzeIndex;
        private UMABonePose PoseConverter;
        private RaceData raceForPose;
        private RaceData toRace;
        private SlotDataAsset donorSlot;
        float rotX, rotY, rotZ;



        void ShowSidebar()
        {
            GUILayout.Label("Utilities Panel", EditorStyles.toolbarButton,GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Sel None"))
            {
                ClearSelection();
            }
            if (GUILayout.Button("Sel All"))
            {
                SelectAll();
            }
            GUILayout.EndHorizontal();

            sideBarPosition = GUILayout.BeginScrollView(sideBarPosition,false,true);

			_IndexFoldout = EditorGUILayout.Foldout(_IndexFoldout, "Asset Indexer");
			if(_IndexFoldout) {
				string compareFile = Application.dataPath + "/AssetIndexCompareSerializedItems2.txt";
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

				beforeIndex = EditorGUILayout.ObjectField("Before Index:", beforeIndex, typeof(UMAAssetIndexer), false) as UMAAssetIndexer;
				afterIndex = EditorGUILayout.ObjectField("After Index:", afterIndex, typeof(UMAAssetIndexer), false) as UMAAssetIndexer;
				if (GUILayout.Button("Compare Serialized Items"))
				{
					if (beforeIndex != null && afterIndex != null)
					{
						beforeIndex.CompareSerializedItems2(afterIndex,compareFile);
						OpenFileWithEditorUtility(compareFile);

					}
					else
					{
						EditorUtility.DisplayDialog("Error", "Please select both before and after indexes.", "OK");
					}
				}
				if (GUILayout.Button("Full Compare"))
				{
					if (beforeIndex != null && afterIndex != null)
					{
						beforeIndex.CompareTo(afterIndex, compareFile);
						OpenFileWithEditorUtility(compareFile);
					}
					else
					{
						EditorUtility.DisplayDialog("Error", "Please select both before and after indexes.", "OK");
					}
				}
				AnalyzeIndex = EditorGUILayout.ObjectField("Analyze Index:", AnalyzeIndex, typeof(UMAAssetIndexer), false) as UMAAssetIndexer;
				if (GUILayout.Button("Analyze Index"))
				{
					if (AnalyzeIndex != null)
					{
						Debug.Log("Analyzing index: " + AnalyzeIndex.name);
                        //AnalyzeIndex.Clear();
                        //AnalyzeIndex.RebuildLibrary();
#if UMA_ADDRESSABLES
						Debug.Log($"ANALYZE: Before count = {AnalyzeIndex.SerializedItems.Count}");
                        UMAAddressablesSupport.Instance.GenerateAddressables(new SingleGroupGenerator { ClearMaterials = true },AnalyzeIndex);
						Debug.Log($"ANALYZE: After count = {AnalyzeIndex.SerializedItems.Count}");
#endif
                        Debug.Log("ANALYZE: Running startup");
						AnalyzeIndex.Initialize();
						Debug.Log($"ANALYZE: After Initialize count = {AnalyzeIndex.SerializedItems.Count}");
						AnalyzeIndex.UpdateSerializedDictionaryItems();
						Debug.Log($"ANALYZE: After UpdateSerializedDictionaryItems count = {AnalyzeIndex.SerializedItems.Count}");
						AnalyzeIndex.RebuildRaceRecipes();
						Debug.Log($"ANALYZE: After RebuildRaceRecipes count = {AnalyzeIndex.SerializedItems.Count}");
					}
					else
					{
						EditorUtility.DisplayDialog("Error", "Please select an index to analyze.", "OK");
					}
				}

				GUIHelper.EndVerticalPadded(10);
			}

			

			_meshHideFoldout = EditorGUILayout.Foldout(_meshHideFoldout, "Mesh Hide Assets");
            if (_meshHideFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                GUILayout.Label("Mesh Hide Asset:");
                GUILayout.BeginHorizontal();
                AddedMHA = EditorGUILayout.ObjectField("", AddedMHA, typeof(MeshHideAsset), false, GUILayout.Width(175)) as MeshHideAsset;
                if (GUILayout.Button("Selected", GUILayout.Width(75)))
                {
                    var o = GetFirstSelectedAsset(typeof(MeshHideAsset));
                    if (o != null)
                    {
                        AddedMHA = o.Item as MeshHideAsset;
                    }
                    else
                    {
                        // try the project selection?
                        UnityEngine.Object[] objs = Selection.GetFiltered(typeof(MeshHideAsset), SelectionMode.Assets);
                        if (objs.Length > 0)
                        {
                            AddedMHA = objs[0] as MeshHideAsset;
                        }
                    }
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Add to selected recipes"))
                {
                    UpdateMeshHideAssets();
                }
                if (GUILayout.Button("Remove from selected recipes"))
                {
                    RemoveMeshHideAssets();
                }
                if (GUILayout.Button("Select recipes with Mesh Hide"))
                {
                    SelectByMeshHide(AddedMHA);
                }
                if (GUILayout.Button("Find broken Mesh Hide Assets"))
                {
                    FindBrokenMeshHideAssets();
                }
                if (GUILayout.Button("Select unused Mesh Hide Assets"))
                {
                    SelectUnusedMeshHideAssets();
                }

#if UNITY_6000_2_OR_NEWER
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("LOD Fix Options", EditorStyles.boldLabel);
                _fixMhaCopyLodMode = (MeshHideAsset.TriangleHideStrategy)EditorGUILayout.EnumPopup(new GUIContent(
                    "Copy LOD Mode",
                    "Controls how destination triangles are marked hidden based on how many of their vertices were part of any hidden triangle in the source LOD.\n\n" +
                    "Strict: hide only if ALL 3 vertices were previously hidden.\n" +
                    "Weighted: hide if 2 or more vertices were previously hidden.\n" +
                    "Conservative: hide if ANY 1 vertex was previously hidden."),
                    _fixMhaCopyLodMode);
                _fixMhaCopyPolicy = EditorGUILayout.Popup(new GUIContent("Copy Policy", "Replace overwrites destination LODs; Missing only fills unallocated LODs."), _fixMhaCopyPolicy, _fixMhaCopyPolicyOptions);
#endif

                if (GUILayout.Button("Gen LOD on ALL MHA"))
                {
                    FixMeshHideAssetLOD(true);
                }

                if (GUILayout.Button("Gen LOD on Selected MHA"))
                {
                    FixMeshHideAssetLOD(false);
                }


                GUIHelper.EndVerticalPadded(10);
            }
            _materialFoldout = EditorGUILayout.Foldout(_materialFoldout, "UMA Materials");
            if (_materialFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                GUILayout.Label("UMA Material:");
                GUILayout.BeginHorizontal();
                umaMaterial = EditorGUILayout.ObjectField("", umaMaterial, typeof(UMAMaterial), false, GUILayout.Width(175)) as UMAMaterial;
                if (GUILayout.Button("Selected",GUILayout.Width(75)))
                {
                    var o = GetFirstSelectedAsset(typeof(UMAMaterial));
                    if (o != null)
                    {
                        umaMaterial = o.Item as UMAMaterial;
                    }
                    else
                    {
                        // try the project selection?
                        UnityEngine.Object[] objs = Selection.GetFiltered(typeof(UMAMaterial), SelectionMode.Assets);
                        if (objs.Length > 0)
                        {
                            umaMaterial = objs[0] as UMAMaterial;
                        }
                    }
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Apply to Selection"))
                {
                    UpdateMaterials();
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Select Overlays with Material"))
                {
                    SelectByMaterial(umaMaterial, typeof(OverlayDataAsset));
                }
                if (GUILayout.Button("Select Slots with Material"))
                {
                    SelectByMaterial(umaMaterial, typeof(SlotDataAsset));
                }
                if (GUILayout.Button("Fixup Texture Channels"))
                {
                    FixupTextureChannels(umaMaterial);
                }
                if (GUILayout.Button("Select unused materials"))
                {
                    SelectUnusedMaterials();
                }
                GUILayout.BeginHorizontal();

                _channelType = EditorGUILayout.Popup(_channelType, System.Enum.GetNames(typeof(UMAMaterial.ChannelType)));
                if (GUILayout.Button("Sel by channel type"))
                {
                    SelectByChannelType(_channelType);
                }
                GUILayout.EndHorizontal();
                GUIHelper.EndVerticalPadded(10);
            }
            _raceFoldout = EditorGUILayout.Foldout(_raceFoldout, "Races");
            if (_raceFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                GUILayout.Label("RaceData:");
                GUILayout.BeginHorizontal();
                umaRaceData = EditorGUILayout.ObjectField("", umaRaceData, typeof(RaceData), false, GUILayout.Width(175)) as RaceData;
                if (GUILayout.Button("Selected", GUILayout.Width(75)))
                {
                    var o = GetFirstSelectedAsset(typeof(RaceData));
                    if (o != null)
                    {
                        umaRaceData = o.Item as RaceData;
                    }
                    else
                    {
                        // try the project selection?
                        UnityEngine.Object[] objs = Selection.GetFiltered(typeof(RaceData), SelectionMode.Assets);
                        if (objs.Length > 0)
                        {
                            umaRaceData = objs[0] as RaceData;
                        }
                    }
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Add to Selection"))
                {
                    AddToWardrobeRecipes(umaRaceData);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Replace all on Selection"))
                {
                    ReplaceRaceInWardrobeRecipes(umaRaceData);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Remove from Selection"))
                {
                    RemoveRaceFromWardrobeRecipes(umaRaceData);
                }
                if (GUILayout.Button("Select slots for race"))
                {
                    SelectSlotsByRace(umaRaceData);
                }
                if (GUILayout.Button("Select overlays for race"))
                {
                    SelectOverlaysByRace(umaRaceData);
                }
                if (GUILayout.Button("Select Wardrobe Recipes for Race"))
                {
                    SelectAllWardrobeRecipesForRace(umaRaceData);
                }
                filterBySlot = GUILayout.Toggle(filterBySlot, "Filter by Slot");
                if (filterBySlot)
                {
                        selectedSlot = EditorGUILayout.Popup(selectedSlot, umaRaceData.wardrobeSlots.ToArray());
                        filterSlot = umaRaceData.wardrobeSlots[selectedSlot];
                }
                else
                {
                    filterSlot = "";
                    EditorGUILayout.Popup(0, NullArray);
                }

                if (GUILayout.Button("Select Base Recipe for Race"))
                {
                    SelectBaseRecipeForRace(umaRaceData);
                }
                GUIHelper.EndVerticalPadded(10);
            }
            _recipeFoldout = EditorGUILayout.Foldout(_recipeFoldout, "Update Recipe Transforms");
            if (_recipeFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

                setRect = EditorGUILayout.ToggleLeft("Set Rect Value",setRect);
                if (setRect)
                {
                    _rect = EditorGUILayout.RectField("Rect: ", _rect);
                }

                setRotation = EditorGUILayout.ToggleLeft("Set Rotation Value", setRotation);
                if (setRotation)
                {
                    _rotation = EditorGUILayout.FloatField("Rotation: ", _rotation);
                }

                setScale = EditorGUILayout.ToggleLeft("Set Scale", setScale);
                if (setScale)
                {
                    _scale = EditorGUILayout.Vector3Field("Scale: ", _scale);
                }

                _rectLimit = EditorGUILayout.ToggleLeft("Only where Rect = ",_rectLimit, GUILayout.ExpandWidth(false));
                if (_rectLimit)
                {
                    _rectCheck = EditorGUILayout.RectField(_rectCheck);
                }

                _overlayLimit = EditorGUILayout.ToggleLeft("Only where Overlay = ", _overlayLimit, GUILayout.ExpandWidth(false));
                if (_overlayLimit)
                {
                    umaOverlay = EditorGUILayout.ObjectField("", umaOverlay, typeof(OverlayDataAsset), false, GUILayout.Width(175)) as OverlayDataAsset;
                }

                if (GUILayout.Button("Update Transforms"))
                {
                    UpdateRecipeTransforms(_rect,_scale,_rotation,_rectLimit,_rectCheck);
                    AssetDatabase.SaveAssets();
                }
                GUIHelper.EndVerticalPadded(10);
            }

            _OverlayFoldout = EditorGUILayout.Foldout(_OverlayFoldout, "Overlays");
            if (_OverlayFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                GUILayout.Label("OverlayDataAsset:");
                GUILayout.BeginHorizontal();
                umaOverlay = EditorGUILayout.ObjectField("", umaOverlay, typeof(OverlayDataAsset), false, GUILayout.Width(175)) as OverlayDataAsset;
                if (GUILayout.Button("Selected", GUILayout.Width(75)))
                {
                    var o = GetFirstSelectedAsset(typeof(OverlayDataAsset));
                    if (o != null)
                    {
                        umaOverlay = o.Item as OverlayDataAsset;
                    }
                    else
                    {
                        // try the project selection?
                        UnityEngine.Object[] objs = Selection.GetFiltered(typeof(OverlayDataAsset), SelectionMode.Assets);
                        if (objs.Length > 0)
                        {
                            umaOverlay = objs[0] as OverlayDataAsset;
                        }
                    }
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Select Recipes with Overlay"))
                {
                    SelectWithOverlay(umaOverlay);
                }
                if (GUILayout.Button("Select Overlays with selected materials"))
                {
                    SelectOverlaysWithMaterials();
                }

                if (GUILayout.Button("Find Overlays with invalid textures"))
                {
                    FindOverlaysWithInvalidTextures();
                }
                GUIHelper.EndVerticalPadded(10);
            }

            _SlotFoldout = EditorGUILayout.Foldout(_SlotFoldout, "Slots");
            if (_SlotFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                GUILayout.Label("SlotDataAsset:");
                GUILayout.BeginHorizontal();
                umaSlot = EditorGUILayout.ObjectField("", umaSlot, typeof(SlotDataAsset), false, GUILayout.Width(175)) as SlotDataAsset;
                if (GUILayout.Button("Selected", GUILayout.Width(75)))
                {
                    var o = GetFirstSelectedAsset(typeof(SlotDataAsset));
                    if (o != null)
                    {
                        umaSlot = o.Item as SlotDataAsset;
                    }
                    else
                    {
                        // try the project selection?
                        UnityEngine.Object[] objs = Selection.GetFiltered(typeof(SlotDataAsset), SelectionMode.Assets);
                        if (objs.Length > 0)
                        {
                            umaSlot = objs[0] as SlotDataAsset;
                        }
                    }
                }
                GUILayout.EndHorizontal();


                if (GUILayout.Button("Select Recipes with Slot"))
                {
                    SelectWithSlot(umaSlot);
                }
                if (GUILayout.Button("Select Slots with selected materials"))
                {
                    SelectSlotsWithMaterials();
                }
                if (GUILayout.Button("Find Slots with invalid meshes"))
                {
                    FindSlotsWithInvalidMeshes();
                }
                if (GUILayout.Button("Select all clipping slots "))
                {
                    SelectClippingSlots();
                }
                if (GUILayout.Button("Select all smooshable slots "))
                {
                    SelectSmooshableSlots();
                }
                if (GUILayout.Button("Select all LOD slots")) //PigEdit
                {
                    SelectLODSlots();
                }
                if (GUILayout.Button("Clear Legacy Flag on slots"))
                {
                    SetLegacyFlagOnSelectedSlots(false);
                }
                if (GUILayout.Button("Set Legacy Flag on slots"))
                {
                    SetLegacyFlagOnSelectedSlots(true);
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("LOD Generation", EditorStyles.boldLabel);
                _slotLodOptionsFoldout = EditorGUILayout.Foldout(_slotLodOptionsFoldout, "LOD Gen Options", true);
                if (_slotLodOptionsFoldout)
                {
                    int maxLodLevels = LoadSlotLodInt("MaxLodLevels", 8);
                    int minTriangles = LoadSlotLodInt("MinTriangles", 256);
                    float reduction = LoadSlotLodFloat("TargetReductionPerLevel", 0.5f);
                    bool preserveBorders = LoadSlotLodBool("PreserveBoundaryEdges", true);
                    float boundaryWeight = LoadSlotLodFloat("BoundaryWeight", 10f);
                    bool preserveVolume = LoadSlotLodBool("PreserveVolume", true);
                    float volumeWeight = LoadSlotLodFloat("VolumeWeight", 1.0f);
                    bool useUnityLodGenerator = LoadSlotLodBool("UseUnityLodGenerator", false);

                    EditorGUI.BeginChangeCheck();
                    maxLodLevels = EditorGUILayout.IntSlider(new GUIContent("Max LOD Levels"), maxLodLevels, 1, 8);
                    useUnityLodGenerator = EditorGUILayout.Toggle(new GUIContent(
                        "Use Unity LOD Generator",
                        "When enabled, uses Unity's MeshLodUtility.GenerateMeshLods instead of UMA's internal reducer."),
                        useUnityLodGenerator);

                    using (new EditorGUI.DisabledScope(useUnityLodGenerator))
                    {
                        minTriangles = EditorGUILayout.IntField(new GUIContent("Min Triangles"), Mathf.Max(0, minTriangles));
                        reduction = EditorGUILayout.Slider(new GUIContent("Reduction Per Level"), reduction, 0.01f, 0.99f);
                        preserveBorders = EditorGUILayout.Toggle(new GUIContent("Preserve Boundary Edges"), preserveBorders);
                        boundaryWeight = EditorGUILayout.FloatField(new GUIContent("Boundary Weight"), Mathf.Max(0f, boundaryWeight));
                        preserveVolume = EditorGUILayout.Toggle(new GUIContent(
                            "Preserve Volume",
                            "When enabled, penalizes edge collapses that would flatten thin features like arms and fingers."),
                            preserveVolume);

                        using (new EditorGUI.DisabledScope(!preserveVolume))
                        {
                            volumeWeight = EditorGUILayout.Slider(new GUIContent(
                                "Volume Weight",
                                "How strongly to preserve volume. Higher values prevent more flattening but may reduce simplification quality."),
                                volumeWeight, 0.1f, 5.0f);
                        }
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        SaveSlotLodInt("MaxLodLevels", maxLodLevels);
                        SaveSlotLodBool("UseUnityLodGenerator", useUnityLodGenerator);
                        SaveSlotLodInt("MinTriangles", minTriangles);
                        SaveSlotLodFloat("TargetReductionPerLevel", reduction);
                        SaveSlotLodBool("PreserveBoundaryEdges", preserveBorders);
                        SaveSlotLodFloat("BoundaryWeight", boundaryWeight);
                        SaveSlotLodBool("PreserveVolume", preserveVolume);
                        SaveSlotLodFloat("VolumeWeight", volumeWeight);
                    }
                }
                if (GUILayout.Button("Create LOD for selected slots"))
                {
                    CreateLODForSlots(false);
                }
                if (GUILayout.Button("Create LOD for all slots"))
                {
                    CreateLODForSlots(true);
                }

#if EXP_SLOT_CONVERSION
                GUILayout.Label("Slot Conversion",EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("BonePose:");
                PoseConverter = EditorGUILayout.ObjectField("", PoseConverter, typeof(UMABonePose), false, GUILayout.Width(175)) as UMABonePose;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("To RaceData:");
                raceForPose = EditorGUILayout.ObjectField("", raceForPose,  typeof(RaceData), false, GUILayout.Width(175)) as RaceData;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Donor:");
                donorSlot = EditorGUILayout.ObjectField("", donorSlot, typeof(SlotDataAsset), false, GUILayout.Width(175)) as SlotDataAsset;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("X:", GUILayout.Width(22));
                rotX = EditorGUILayout.FloatField(rotX,GUILayout.Width(60));
                EditorGUILayout.LabelField("Y:", GUILayout.Width(22));
                rotY = EditorGUILayout.FloatField(rotY, GUILayout.Width(60));
                EditorGUILayout.LabelField("Z:", GUILayout.Width(22));
                rotZ = EditorGUILayout.FloatField(rotZ, GUILayout.Width(60));
                GUILayout.EndHorizontal();
                postRotate = EditorGUILayout.ToggleLeft("Post Rotate", postRotate);


                if (GUILayout.Button("Convert to new format", GUILayout.Width(150)))
                {
                    ConvertSlotFromLegacy(donorSlot, PoseConverter, raceForPose, rotX, rotY, rotZ, postRotate);
                }
                if (GUILayout.Button("Convert to new format (old method)", GUILayout.Width(200)))
                {
                    ConvertSlotFromLegacyOld(donorSlot, PoseConverter, raceForPose, rotX, rotY, rotZ, postRotate);
                }
                if (GUILayout.Button("Restore from backup (_Original)"))
                {
                    RestoreSlots();
                }
#endif
                GUIHelper.EndVerticalPadded(10);
            }
#if EXP_SLOT_CONVERSION
            _conversionFoldout = EditorGUILayout.Foldout(_conversionFoldout, "Conversions");
            if (_conversionFoldout)
            {
                GUILayout.Label("Scene base slot Conversion", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox($"This will convert all equipped slots on the 'From DCA' to the 'To DCA' using the BonePose specified below and will create new slots in the project folder 'Assets/UMA/ConvertedSlots'.", MessageType.Info);
                GUILayout.BeginHorizontal();
                GUILayout.Label("BonePose:");
                PoseConverter = EditorGUILayout.ObjectField("", PoseConverter, typeof(UMABonePose), false, GUILayout.Width(175)) as UMABonePose;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("From Character");
                _fromCharacter = EditorGUILayout.ObjectField("", _fromCharacter, typeof(DynamicCharacterAvatar), true, GUILayout.Width(175)) as DynamicCharacterAvatar;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("To Character");
                _toCharacter = EditorGUILayout.ObjectField("", _toCharacter, typeof(DynamicCharacterAvatar), true, GUILayout.Width(175)) as DynamicCharacterAvatar;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("X:", GUILayout.Width(22));
                rotX = EditorGUILayout.FloatField(rotX, GUILayout.Width(60));
                EditorGUILayout.LabelField("Y:", GUILayout.Width(22));
                rotY = EditorGUILayout.FloatField(rotY, GUILayout.Width(60));
                EditorGUILayout.LabelField("Z:", GUILayout.Width(22));
                rotZ = EditorGUILayout.FloatField(rotZ, GUILayout.Width(60));
                GUILayout.EndHorizontal();
                alignBindPoses = EditorGUILayout.ToggleLeft("Align Bind Poses", alignBindPoses);
                if (GUILayout.Button("inc"))
                {
                    rotX += 90f;
                    if (rotX >= 360f)
                    {
                        rotX = 0f;
                        rotY += 90f;
                        if (rotY >= 360f)
                        {
                            rotY = 0f;
                            rotZ += 90f;
                            if (rotZ >= 360f)
                            {
                                rotZ = 0f;
                            }
                        }

                    }
                    ConvertEquippedSlots(_fromCharacter, _toCharacter, PoseConverter, rotX, rotY, rotZ,alignBindPoses);
                }

                if (GUILayout.Button("Convert Now"))
                {
                    ConvertEquippedSlots(_fromCharacter, _toCharacter, PoseConverter, rotX, rotY,rotZ,alignBindPoses);
                }
            }
#endif

            _TextureFoldout = EditorGUILayout.Foldout(_TextureFoldout, "Textures");
            if (_TextureFoldout)
            {
                umaTexture = EditorGUILayout.ObjectField("Texture: ", umaTexture, typeof(Texture2D), false) as Texture2D;

                if (GUILayout.Button("Find texture in OVL"))
                {
                    FindOverlaysWithTexture(umaTexture);
                }
                if (GUILayout.Button("Find texture in UMAMaterials"))
                {
                    FindUMAMaterialsWithTexture(umaTexture);
                }
            }
            GUILayout.EndScrollView();
        }

        private void CreateLODForSlots(bool processAll)
        {
            List<SlotDataAsset> slots = new List<SlotDataAsset>();
            if (processAll)
            {
                var allItems = UAI.GetAllAssets<SlotDataAsset>();
                foreach (var item in allItems)
                {
                    slots.Add(item);
                }
            }
            else
            {
                var selectedItems = GetSelectedAssets(typeof(SlotDataAsset));
                foreach (var item in selectedItems)
                {
                    slots.Add(item.Item as SlotDataAsset);
                }
            }

            if (slots.Count == 0)
            {
                return;
            }

            int maxLodLevels = LoadSlotLodInt("MaxLodLevels", 8);
            int minTriangles = LoadSlotLodInt("MinTriangles", 256);
            float reduction = LoadSlotLodFloat("TargetReductionPerLevel", 0.5f);
            bool preserveBorders = LoadSlotLodBool("PreserveBoundaryEdges", true);
            float boundaryWeight = LoadSlotLodFloat("BoundaryWeight", 10f);
            bool preserveVolume = LoadSlotLodBool("PreserveVolume", true);
            float volumeWeight = LoadSlotLodFloat("VolumeWeight", 1.0f);
            bool useUnityLodGenerator = LoadSlotLodBool("UseUnityLodGenerator", false);

            var opts = new SlotLodGenerator.LodGenOptions();
            opts.MaxLodLevels = maxLodLevels;
            opts.MinTriangles = minTriangles;
            opts.TargetReductionPerLevel = reduction;
            opts.PreserveBoundaryEdges = preserveBorders;
            opts.BoundaryWeight = boundaryWeight;
            opts.PreserveVolume = preserveVolume;
            opts.VolumeWeight = volumeWeight;
            opts.useUnityLodGenerator = useUnityLodGenerator;

            int updated = 0;
            int skipped = 0;

            try
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
                    {
                        skipped++;
                        continue;
                    }

                    float t = (slots.Count > 0) ? ((float)i / (float)slots.Count) : 1.0f;
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(
                        "Generate Slot LODs",
                        "Processing: " + slot.name,
                        t);
                    if (cancel)
                    {
                        break;
                    }

                    bool did = false;
                    try
                    {
                        did = SlotLodGenerator.GenerateAndApplyLods(slot, opts);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        did = false;
                    }

                    if (did)
                    {
                        updated++;
                        EditorUtility.SetDirty(slot);
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (updated > 0)
            {
                AssetDatabase.SaveAssets();
            }

            EditorUtility.DisplayDialog("Slot LOD", "Updated " + updated + " slot(s). Skipped " + skipped + ".", "OK");
        }



        private void FixMeshHideAssetLOD(bool processAll)
        {
            List<MeshHideAsset> mhas = new List<MeshHideAsset>();
            if (processAll)
            {
                var allItems = UAI.GetAllAssets<MeshHideAsset>();
                foreach (var item in allItems)
                {
                    mhas.Add(item);
                }
            }
            else
            {
                var selectedItems = GetSelectedAssets(typeof(MeshHideAsset));
                foreach (var item in selectedItems)
                {
                    mhas.Add(item.Item as MeshHideAsset);
                }
            }
            foreach (var mha in mhas)
            {
                bool modified = false;

#if UNITY_6000_2_OR_NEWER
                if (mha == null)
                {
                    continue;
                }

                SlotDataAsset slot = mha.asset;
                if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
                {
                    continue;
                }

                int submesh = slot.subMeshIndex;
                if (submesh < 0 || submesh >= slot.meshData.subMeshCount)
                {
                    continue;
                }

                int lodCount = 1;
                var ranges = slot.meshData.submeshes[submesh].lodRanges;
                if (ranges != null && ranges.Count > 0)
                {
                    lodCount = ranges.Count;
                }

                if (lodCount < 1)
                {
                    lodCount = 1;
                }

                bool replace = (_fixMhaCopyPolicy == 0);
                bool onlyMissing = (_fixMhaCopyPolicy != 0);

                // Ensure base selection exists (legacy assets might have never been initialized)
                if (mha.triangleFlags == null || mha.triangleFlags.Length == 0)
                {
                    mha.Initialize();
                    modified = true;
                }

                // Copy base LOD mask (0) to all other LODs, allocating as needed.
                for (int lod = 1; lod < lodCount; lod++)
                {
                    // In "Missing" mode, skip LODs that already have stored data.
                    if (onlyMissing)
                    {
                        if (mha.HasStoredLODMask(lod))
                        {
                            continue;
                        }
                    }

                    mha.CopyLODMask(0, lod, replace, _fixMhaCopyLodMode);
                    modified = true;
                }
#endif
                if (modified)
                {
                    EditorUtility.SetDirty(mha);
                }
            }
            AssetDatabase.SaveAssets();
        }

        private DynamicCharacterAvatar _fromCharacter;
        private DynamicCharacterAvatar _toCharacter;


        class SaveBonePoseInfo
        {
            public DynamicDNAConverterController Controller;
            public int BonePoseConverterNumber;
            public float BonePoseConverterWeight;
            public UMABonePose bonePose;

            public SaveBonePoseInfo(DynamicDNAConverterController controller, int converterNumber, float weight, UMABonePose pose)
            {
                Controller = controller;
                BonePoseConverterNumber = converterNumber;
                BonePoseConverterWeight = weight;
                bonePose = pose;
            }
        }

        // todo: pass a donor slot. Copy everything from that except the meshdata, which comes from the fromDCA after applying the bonepose.

        private void ConvertEquippedSlots(DynamicCharacterAvatar fromDCA, DynamicCharacterAvatar toDCA, UMABonePose poseConverter, float rotX, float rotY, float rotZ, bool alignBindPoses)
        {
            if (fromDCA == null || toDCA == null || poseConverter == null)
            {
                Debug.LogError("Please ensure From DCA, To DCA and BonePose are all assigned.");
                return;
            }

            // save the boneposes, and then clear their weights so they don't apply.
            // the set our new bonepose with weight 1f
            // then force build the character to apply the bonepose to the rig 
            // then bake the character with no animator to get the meshes with the bonepose applied



            RaceData race = fromDCA.activeRace.data;
            if (race.useNewDNA)
            {
                Debug.LogError("From DCA uses new DNA system. This conversion only works with legacy DNA");
                EditorUtility.DisplayDialog("Error", "From DCA uses new DNA system. This conversion only works with legacy DNA", "OK");
            }
            if (race.dnaConverterList == null || race.dnaConverterList.Length == 0)
            {               
                Debug.LogError("From DCA Race has no DNA Converters.");
                EditorUtility.DisplayDialog("Error", "From DCA Race has no DNA converters", "OK");
                return;
            }

            List <DynamicDNAConverterController> controllers = new List<DynamicDNAConverterController>();
            controllers.AddRange(race.dnaConverterList);

            List <SaveBonePoseInfo> BonePoseSaves = new List<SaveBonePoseInfo>();
            foreach (var controller in controllers)
            {
                var bonePoseConverters = controller.GetBonePoseConverters();
                for (int bpConverter = 0; bpConverter < bonePoseConverters.Count; bpConverter++)
                {
                    BonePoseDNAConverterPlugin.BonePoseDNAConverter bonePoseConverter = bonePoseConverters[bpConverter];
                    SaveBonePoseInfo sbp = new SaveBonePoseInfo(controller, bpConverter, bonePoseConverter.startingPoseWeight, bonePoseConverter.poseToApply);
                    BonePoseSaves.Add(sbp);
                    bonePoseConverter.startingPoseWeight = 0f;
                }
            }

            if (BonePoseSaves.Count == 0)
            {
                var plugin = controllers[0].EnsureBonePosePlugin();
            }

            var addedbpc  = controllers[0].AddBonePoseConverter(poseConverter, startingWeight: 1f);

            var toSlots = toDCA.GetBaseSlots();

            var fromSlots = fromDCA.GetEquippedSlots();
            fromDCA.BuildNow();
            //ApplyBoneposeToRig(fromDCA, poseConverter);
            List<Mesh> meshes = BakeDCA(fromDCA);
            Quaternion rot = Quaternion.Euler(rotX, rotY, rotZ);

            foreach (var slot in fromSlots)
            {
                SlotDataAsset backupSlot;
                // if backup exists, restore from that first.

                BackupSlot(backupFolder, slot.asset, out backupSlot);
                // Restore the backup slot if it exists
                if (backupSlot != null)
                {
                    slot.asset.meshData = backupSlot.meshData.DeepCopy();
                }
                // Convert each slot using the baked meshes
                SlotDataAsset sda = slot.asset;
                int meshNumber = slot.skinnedMeshRenderer;
                int vertexOffset = slot.vertexOffset;
                int vertexCount = sda.meshData.vertexCount;
                Mesh bakedMesh = meshes[meshNumber];
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 newVector = bakedMesh.vertices[vertexOffset + i];
                    sda.meshData.vertices[i] = rot * newVector;
                }
                if (alignBindPoses)
                {
                    SlotDataAssetInspector.ConformBindposesAndVertices(sda, toSlots[0].asset);
                }
                else
                {
                    // copy the bindpose from the toDCA first slot.
                    Matrix4x4[] bindPoses = toSlots[0].asset.meshData.bindPoses;
                }
                FinalizeSlot(sda);
            }


            addedbpc.startingPoseWeight = 0f;
            controllers[0].RemoveBonePoseConverters(poseConverter);
            // restore bone poses
            foreach (var bonePoseInfo in BonePoseSaves)
            {
                var controller = bonePoseInfo.Controller;
                var bonePoseConverter = controller.GetBonePoseConverters()[bonePoseInfo.BonePoseConverterNumber];
                bonePoseConverter.startingPoseWeight = bonePoseInfo.BonePoseConverterWeight;
                bonePoseConverter.poseToApply = bonePoseInfo.bonePose;
            }

            CopyPreloadWardrobeRecipes(fromDCA, toDCA);
            toDCA.BuildNow();
        }

        private void CopyPreloadWardrobeRecipes(DynamicCharacterAvatar fromDCA, DynamicCharacterAvatar toDCA)
        {
            if (fromDCA == null || toDCA == null)
            {
                Debug.LogError("CopyPreloadWardrobeRecipes: fromDCA or toDCA is null.");
                return;
            }

            var src = fromDCA.preloadWardrobeRecipes;
            if (src == null)
            {
                Debug.LogWarning("CopyPreloadWardrobeRecipes: Source DCA has no preloadWardrobeRecipes.");
                return;
            }

#if UNITY_EDITOR
            Undo.RecordObject(toDCA, "Copy Preload Wardrobe Recipes");
#endif

            // Ensure destination list exists
            if (toDCA.preloadWardrobeRecipes == null)
            {
                toDCA.preloadWardrobeRecipes = new DynamicCharacterAvatar.WardrobeRecipeList();
            }

            toDCA.preloadWardrobeRecipes.loadDefaultRecipes = src.loadDefaultRecipes;

            var dstList = new List<DynamicCharacterAvatar.WardrobeRecipeListItem>(); // alias not available; use fully qualified below
            dstList = new List<DynamicCharacterAvatar.WardrobeRecipeListItem>(src.recipes != null ? src.recipes.Count : 0);

            var idx = UMAAssetIndexer.Instance;

            if (src.recipes != null)
            {
                dstList.Clear();
                for (int i = 0; i < src.recipes.Count; i++)
                {
                    var s = src.recipes[i];
                    if (s == null) continue;

                    var item = new DynamicCharacterAvatar.WardrobeRecipeListItem
                    {
                        _recipeName = s._recipeName,
                        _enabledInDefaultWardrobe = s._enabledInDefaultWardrobe,
                        ForceLoad = s.ForceLoad,
                        _compatibleRaces = (s._compatibleRaces != null) ? new List<string>(s._compatibleRaces) : new List<string>()
                    };

                    // Try to resolve the recipe asset by name for convenience
                    if (!string.IsNullOrEmpty(item._recipeName) && idx != null)
                    {
                        try
                        {
                            item._recipe = idx.GetAsset<UMATextRecipe>(item._recipeName);
                            item.ForceLoad = true;
                        }
                        catch { /* ignore resolve failures; name will still be copied */ }
                    }

                    dstList.Add(item);
                }
            }

            toDCA.preloadWardrobeRecipes.recipes = dstList;

#if UNITY_EDITOR
            EditorUtility.SetDirty(toDCA);
#endif
        }

        public List<Mesh> BakeDCA(DynamicCharacterAvatar dca)
        {
            List<Mesh> meshes = new List<Mesh>();
            UMAData ud = dca as UMAData;
            for (int i = 0; i < ud.RendererCount; i++)
            {
                SkinnedMeshRenderer smr = ud.GetRenderer(i);
                Mesh bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh);
                meshes.Add(bakedMesh);
            }
            return meshes;
        }

        public void ApplyBoneposeToRig(DynamicCharacterAvatar DCA, UMABonePose PoseConverter)
        {
            if (DCA == null || DCA.umaData == null || DCA.umaData.skeleton == null)
            {
                Debug.LogError("DCA or UMAData or Skeleton is null.");
                return;
            }
            var poseConverter = PoseConverter;
            if (poseConverter == null)
            {
                Debug.LogError("PoseConverter is null.");
                return;
            }
            foreach (var bonePose in poseConverter.poses)
            {
                var boneTransform = DCA.umaData.skeleton.GetBoneTransform(bonePose.bone);
                if (boneTransform != null)
                {
                    boneTransform.localPosition = bonePose.position;
                    boneTransform.localRotation = bonePose.rotation;
                    boneTransform.localScale = bonePose.scale;
                }
            }
        }


        public void BakeSelectedSlotsToNewRace()
        {
            var selectedSlots = GetSelectedAssets(typeof(SlotDataAsset));
            foreach (var slotItem in selectedSlots)
            {
                SlotDataAsset slot = slotItem.Item as SlotDataAsset;
                if (slot != null)
                {
                    BakeSlotToNewRace(slot, raceForPose, null, 0, 0, 0, PoseConverter);
                }
            }
        }


        /// <summary>
        /// Restores the specified slot from its original backup (slotName + "_Original").
        /// Copies all data from the backup into the current slot, but preserves the current slot's asset name and slotName.
        /// The backup asset is left untouched in the backup folder for future reuse.
        /// </summary>
        /// <param name="slot">The slot to restore (must not be a backup slot itself).</param>
        public void RestoreSlot(SlotDataAsset slot)
        {
            if (slot == null)
            {
                Debug.LogError("[SlotRestore] Slot parameter is null.");
                return;
            }
            if (string.IsNullOrEmpty(slot.slotName))
            {
                Debug.LogError("[SlotRestore] Slot has an empty slotName.");
                return;
            }
            if (slot.slotName.EndsWith("_Original", StringComparison.Ordinal))
            {
                Debug.LogWarning("[SlotRestore] Cannot restore a backup slot directly.");
                return;
            }

            // Derive backup name exactly as ConvertSlotFromLegacy does
            string backupName = slot.slotName + "_Original";

            // Try to locate backup in the UMA indexer
            SlotDataAsset backup = UMAAssetIndexer.Instance?.GetAsset<SlotDataAsset>(backupName);

            // Fallback: search AssetDatabase if not found in index
            if (backup == null)
            {
                string[] guids = AssetDatabase.FindAssets($"{backupName} t:SlotDataAsset");
                if (guids != null && guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    backup = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
                }
            }

            if (backup == null)
            {
                Debug.LogError($"[SlotRestore] Backup slot '{backupName}' not found. Cannot restore '{slot.slotName}'.");
                return;
            }

            // Preserve original slotName & asset name before copy
            string originalSlotName = slot.slotName;
            string originalAssetName = slot.name; // Unity asset name (may differ)

            try
            {
                // Use provided Assign() API to copy all relevant data
                slot.Assign(backup);

                // Restore identifying names to keep this asset as the active (non-backup) slot
                slot.name = originalAssetName;

                // If legacy status should be restored to true (backup is legacy)
                slot.isLegacySlot = backup.isLegacySlot;

                // Recompute name hash if necessary

                EditorUtility.SetDirty(slot);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
                AssetDatabase.SaveAssetIfDirty(slot);
#else
                AssetDatabase.SaveAssets();
#endif
                // Update UMA systems (mesh/cache)
                UMAUpdateProcessor.UpdateSlot(slot, false);

                Debug.Log($"[SlotRestore] Restored slot '{originalSlotName}' from backup '{backupName}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SlotRestore] Exception restoring '{originalSlotName}' from '{backupName}': {ex.Message}");
            }
        }
    

        private void RestoreSlots()
        {
            var selectedSlots = GetSelectedAssets(typeof(SlotDataAsset));
            foreach (var slotItem in selectedSlots)
            {
                SlotDataAsset slot = slotItem.Item as SlotDataAsset;
                if (slot != null)
                {
                    Debug.Log($"Restoring converted slot {slot.slotName}");
                    this.RestoreSlot(slot);
                    EditorUtility.SetDirty(slot);
                    AssetDatabase.SaveAssetIfDirty(slot);
                    UMAUpdateProcessor.UpdateSlot(slot, false);
                }
            }
        }
        

        private void ConvertSlotFromLegacyOld(SlotDataAsset donor, UMABonePose poseConverter, RaceData raceData, float x=0f, float y=0f, float z = 0f, bool postRotate=false)
        {
            if (poseConverter == null)
            {
                //EditorUtility.DisplayDialog("Error", "Please select a UMABonePose to convert selected slots.", "OK");
                //return;
            }
            var selectedSlots = GetSelectedAssets(typeof(SlotDataAsset));
            foreach (var slotItem in selectedSlots)
            {
                SlotDataAsset slot = slotItem.Item as SlotDataAsset;
                if (slot != null)
                {
                    if (donor != null)
                    {
                        slot.ConvertBonePosesFromLegacy(donor, poseConverter, raceData, x, y, z, postRotate);
                    }
                    else
                    {
                        slot.ConvertBonePosesFromLegacy(poseConverter, raceData, x, y, z, postRotate);
                    }
                    Debug.Log("Updating converted slot");
                    EditorUtility.SetDirty(slot);
                    AssetDatabase.SaveAssetIfDirty(slot);
                    UMAUpdateProcessor.UpdateSlot(slot, false);
                }
            }
        }


        const string backupFolder = "Assets/UMA/SlotBackup";


        private void ConvertSlotFromLegacy(SlotDataAsset donor, UMABonePose poseConverter, RaceData raceData, float x, float y, float z, bool postRotate)
        {
            // Backup-aware legacy conversion:
            // 1. Skip slots whose slotName already ends with _Original
            // 2. If backup (slotName + _Original) does not exist, create via Clone (asset name & slotName both changed)
            // 3. Never overwrite existing backup; reuse it
            // 4. Always convert from backup's meshData (unless donor provided)
            // 5. If donor provided, use donor instead of backup (donor's current state, not its backup)
            // 6. Add backup to UMAAssetIndexer (so it can be found quickly)
            // 7. backup.isLegacySlot stays true; converted slot sets isLegacySlot = false
            // 8. Folder: Assets/UMA/SlotBackup (created if missing)
            // 9. Re-run conversions always source from original backup
            // 10. Use Clone method; keep other slot fields unchanged
            // 11. No Undo integration; just mark dirty & save
            var selected = GetSelectedAssets(typeof(SlotDataAsset));
            if (selected == null || selected.Count == 0) return;




            foreach (var ai in selected)
            {
                var slot = ai.Item as SlotDataAsset;
                if (slot == null) continue;
                if (string.IsNullOrEmpty(slot.slotName)) continue;
                // Skip backup assets themselves
                SlotDataAsset backup;
                if (!BackupSlot(backupFolder, slot, out backup))
                {
                    continue;
                }
                // Choose source (donor overrides backup)
                SlotDataAsset sourceForConversion = donor != null ? donor : backup;
                if (sourceForConversion == null)
                {
                    Debug.LogError($"[SlotConvert] Source slot null for '{slot.slotName}'.");
                    continue;
                }

                // Perform conversion
                if (donor != null)
                {
                    slot.ConvertBonePosesFromLegacy(donor, poseConverter, raceData, x, y, z, postRotate);
                }
                else
                {
                    slot.ConvertBonePosesFromLegacy(sourceForConversion, poseConverter, raceData, x, y, z, postRotate);
                }

                FinalizeSlot(slot);
                Debug.Log($"[SlotConvert] Converted '{slot.slotName}' using '{sourceForConversion.slotName}'.");
            }
        }

        private static void FinalizeSlot(SlotDataAsset slot)
        {
            // Mark converted slot (legacy cleared)
            slot.isLegacySlot = false;
            EditorUtility.SetDirty(slot);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
                AssetDatabase.SaveAssetIfDirty(slot);
#else
            AssetDatabase.SaveAssets();
#endif
            UMAUpdateProcessor.UpdateSlot(slot, false);
        }

        private static bool BackupSlot(string backupFolder, SlotDataAsset slot, out SlotDataAsset backup)
        {
            if (!Directory.Exists(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
                AssetDatabase.ImportAsset(backupFolder);
            }

            backup = null;
            if (slot == null || string.IsNullOrEmpty(slot.slotName))
            {
                Debug.LogError("[SlotConvert] Invalid slot passed to BackupSlot.");
                return false;
            }

            string backupName = slot.slotName + "_Original";

            // Try UMAAssetIndexer first
            backup = UMAAssetIndexer.Instance?.GetAsset<SlotDataAsset>(backupName);

            // Fallback to AssetDatabase search
            if (backup == null)
            {
                // Exact name search prevents partial matches
                string[] guids = AssetDatabase.FindAssets($"\"{backupName}\" t:SlotDataAsset");
                if (guids != null && guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    backup = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
                }
            }

            // Need to create backup
            if (backup == null)
            {
                backup = slot.Clone(backupName, backupName, true, backupFolder);
                if (backup == null)
                {
                    Debug.LogError($"[SlotConvert] Failed to create backup for '{slot.slotName}'. Skipping.");
                    return false;
                }

                backup.isLegacySlot = true; // retain legacy flag on original
                EditorUtility.SetDirty(backup);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
        AssetDatabase.SaveAssetIfDirty(backup);
#else
                AssetDatabase.SaveAssets();
#endif
                UMAAssetIndexer.Instance?.ProcessNewItem(backup, false, false);
                Debug.Log($"[SlotConvert] Created backup '{backupName}'.");
            }

            return true;
        }

        private void BakeSlotToNewRace(SlotDataAsset slot, RaceData oldRace, RaceData newRace, float rotx, float roty, float rotz, UMABonePose SourceToDest)
        {
            // First, bake the new bone pose on the slot. 
            if (string.IsNullOrEmpty(slot.slotName))
            {
                Debug.Log("Slot has not slotName! slot base name is: " + slot.name);
                return;
            }

            // Skip backup assets themselves
            SlotDataAsset backup;
            if (!BackupSlot(backupFolder, slot, out backup))
            {
                return;
            }

            slot.meshData.ApplyBonePose(oldRace, SourceToDest);
            FinalizeSlot(slot);
        }

        private void SetLegacyFlagOnSelectedSlots(bool legacyFlag)
        {
            var selectedSlots = GetSelectedAssets(typeof(SlotDataAsset));
            foreach(var slotItem in selectedSlots)
            {
                SlotDataAsset slot = slotItem.Item as SlotDataAsset;
                if (slot != null)
                {
                    slot.isLegacySlot = legacyFlag;
                    EditorUtility.SetDirty(slot);
                }
            }
            AssetDatabase.SaveAssets();
        }

        private void SelectUnusedMeshHideAssets()
        {
            if (UAI == null) return;
            var MHAS = UAI.GetAssetItems<MeshHideAsset>();
            var NotUsed = new List<AssetItem>();
            var recipes = UAI.GetAssetItems<UMAWardrobeRecipe>();


            foreach (var mha in MHAS)
            {
                bool found = false;
                var item = mha.Item as MeshHideAsset;
                foreach (var recipe in recipes)
                {
                    var wr = recipe.Item as UMAWardrobeRecipe;
                    if (wr.MeshHideAssets.Contains(item))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    NotUsed.Add(mha);
                }
            }
            if (NotUsed.Count > 0)
            {
                SelectByAssetItems(NotUsed);
                EditorUtility.DisplayDialog("Utilities", $"{NotUsed.Count} unused Mesh Hide Assets found", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Utilities", "No unused Mesh Hide Assets found", "OK");
            }
        }

        private void SelectByMeshHide(MeshHideAsset addedMHA)
        {
            if (UAI == null) return;
            List<AssetItem> items = new List<AssetItem>();

            var recipes = UAI.GetAssetItems<UMAWardrobeRecipe>();

            foreach(var recipe in recipes)
            {
                UMAWardrobeRecipe wr = recipe.Item as UMAWardrobeRecipe;
                if (wr != null)
                {
                    foreach(var meshHide in wr.MeshHideAssets)
                    {
                        if (meshHide == addedMHA)
                        {
                            items.Add(recipe);
                            break;
                        }
                    }
                }
            }


            if (items.Count == 0) 
            {
                EditorUtility.DisplayDialog("No Recipes Found", "No recipes found for selected Mesh Hide Asset", "OK");
                return;
            }
            SelectByAssetItems(items);
        }

        private void FindBrokenMeshHideAssets()
        {
            if (UAI == null) return;
            var MHAS = UAI.GetAssetItems<MeshHideAsset>();
            if (MHAS == null || MHAS.Count == 0)
            {
                EditorUtility.DisplayDialog("No Mesh Hide Assets", "No Mesh Hide Assets found in library", "OK");
                return;
            }
            int errors = 0;
            for(int i=0;i<MHAS.Count;i++)
            {
                var item = MHAS[i].Item as MeshHideAsset;
                if (item.AssetSlotName == null || item.AssetSlotName.Length == 0)
                {
                    errors++;
                    SelectByAssetItems(new List<AssetItem>() { MHAS[i] });
                    Debug.Log("MHAERR No Slot on asset: " + MHAS[i]._Name);
                    continue;
                }
                if (item.AssetSlotName.Length > 0)
                {
                    var assetItem = UAI.GetAssetItem<SlotDataAsset>(item.AssetSlotName);

                    if (assetItem == null)
                    {
                        errors++;
                        SelectByAssetItems(new List<AssetItem>() { MHAS[i] });
                        Debug.Log($"MHAERR Slot '{item.AssetSlotName}' not found: " + MHAS[i]._Name);
                        continue;
                    }
                    SlotDataAsset slot =assetItem.Item as SlotDataAsset;

                    if (item.SubmeshCount != slot.meshData.subMeshCount)
                    {
                        errors++;
                        SelectByAssetItems(new List<AssetItem>() { MHAS[i] });
                        Debug.Log("MHAERR Submesh count mismatch: " + MHAS[i]._Name);
                        continue;
                    }
                    for(int sm=0;sm<item.triangleFlags.Length;sm++)
                    {
                        if (item.triangleFlags[sm].Length != slot.meshData.submeshes[sm].getManagedTriangles(0).Length/3)
                        {
                            errors++;
                            SelectByAssetItems(new List<AssetItem>() { MHAS[i] });
                            Debug.Log($"MHAERR Submesh {sm} triangle count mismatch: " + MHAS[i]._Name);
                        }
                    }
                }
            }
            if (errors == 0) 
            {
                EditorUtility.DisplayDialog("No Errors", "No errors found in Mesh Hide Assets", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Errors Found", $"{errors} error(s) found in Mesh Hide Assets. These assets were selected in the library. Please review the console log for details.", "OK");
            }
        }

        private void SelectUnusedMaterials()
        {
            if (UAI == null) return;
            List<AssetItem> materials = new List<AssetItem>();

            var slots = UAI.GetAssetItems<SlotDataAsset>();
            var overlays = UAI.GetAssetItems<OverlayDataAsset>();
            var materialsList = UAI.GetAssetItems<UMAMaterial>();

            for (int materialIndex = 0; materialIndex < materialsList.Count; materialIndex++)
            {
                AssetItem ai = materialsList[materialIndex];
                UMAMaterial uMAMaterial = ai.Item as UMAMaterial;
                bool found = false;
                // check overlays
                if (!found)
                {
                    for (int i = 0; i < overlays.Count; i++)
                    {
                        if (overlays[i] != null && overlays[i].Item != null)
                        {
                            OverlayDataAsset overlay = overlays[i].Item as OverlayDataAsset;
                            if (overlay.material != null && overlay.material.name == uMAMaterial.name)
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                }
                if (!found)
                {
                    materials.Add(ai);
                }
            }

            if (materials.Count > 0)
            {
                SelectByAssetItems(materials);
            }
            else
            {
                EditorUtility.DisplayDialog("No Unused Materials", "No unused materials found", "OK");
            }
        }

        private void SelectSmooshableSlots()
        {
            if (UAI == null) return;
            List<AssetItem> items = new List<AssetItem>();

            var slots = UAI.GetAssetItems<SlotDataAsset>();
            for(int i=0;i<slots.Count;i++)
            {
                if (slots[i] != null && (slots[i].Item as SlotDataAsset).isSmooshable)
                {
                    items.Add(slots[i]);
                }
            }

            SelectByAssetItems(items);
        }

        private void SelectLODSlots() //PigEdit
        {
            if (UAI == null) return;
            List<AssetItem> items = new List<AssetItem>();

            var slots = UAI.GetAssetItems<SlotDataAsset>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && (slots[i].Item as SlotDataAsset).maxLOD != -1)
                {
                    items.Add(slots[i]);
                }
            }

            SelectByAssetItems(items);
        }
        

        private void SelectClippingSlots()
        {
            if (UAI == null) return;
            List<AssetItem> items = new List<AssetItem>();

            var slots = UAI.GetAssetItems<SlotDataAsset>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && (slots[i].Item as SlotDataAsset).isClippingPlane)
                {
                    items.Add(slots[i]);
                }
            }

            SelectByAssetItems(items);
        }

        private void FindSlotsWithInvalidMeshes()
        {
            if (UAI == null) return;
            List<AssetItem> items = new List<AssetItem>();

            var slots = UAI.GetAssetItems<SlotDataAsset>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                {
                    var s = slots[i].Item as SlotDataAsset;
                    if (!UMAMeshData.IsNullOrEmptyMeshData(s.meshData))
                    {
                        s.ValidateMeshData();
                        if (!string.IsNullOrEmpty(s.Errors))
                        {
                            items.Add(slots[i]);
                        }
                    }
                }
            }
            SelectByAssetItems(items);
        }

        private List<AssetItem> GetSelectedMaterials()
        {
            List<AssetTreeElement> selectedElements = GetSelectedElements();
            List<AssetItem> selectedMaterials = new List<AssetItem>();

            for(int i=0;i<selectedElements.Count;i++)
            {
                if (selectedElements[i].ai != null && selectedElements[i].ai._Type == typeof(UMAMaterial))
                {
                    selectedMaterials.Add(selectedElements[i].ai);
                }
            }
            return selectedMaterials;
        }

        private void SelectSlotsWithMaterials()
        {
            var mats = GetSelectedMaterials();
            SelectByAssetItems(mats);
        }

        private void SelectWithSlot(SlotDataAsset umaSlot)
        {
            if (UAI == null) return;
            List<AssetItem> items = new List<AssetItem>();
            items.Add(UAI.GetAssetItem<SlotDataAsset>(umaSlot.slotName));
            SelectByAssetItems(items);
        }

        private void FindOverlaysWithTexture(Texture2D tex)
        {
            if (UAI == null) return;
            List<AssetItem> badItems = new List<AssetItem>();
            var ovls = UAI.GetAssetItems<OverlayDataAsset>();
            for (int i = 0; i < ovls.Count; i++)
            {
                if (ovls[i] != null)
                {
                    var o = ovls[i].Item as OverlayDataAsset;

                    if (o != null)
                    {
                        for (int j = 0; j < o.textureList.Length; j++)
                        {
                            if (o.textureList[j] == null)
                            {
                                continue;
                            }
                            if (tex != null && o.textureList[j].GetEntityId() == tex.GetEntityId())
                            {
                                badItems.Add(ovls[i]);
                            }
                        }
                    }
                }
            }
            SelectByAssetItems(badItems);
        }

        private void FindUMAMaterialsWithTexture(Texture2D tex)
        {
            if (UAI == null) return;
            List<AssetItem> badItems = new List<AssetItem>();
            var umats = UAI.GetAssetItems<UMAMaterial>();
            for (int i = 0; i < umats.Count; i++)
            {
                if (umats[i] != null)
                {
                    var u = umats[i].Item as UMAMaterial;

                    if (u != null)
                    {
                        Material m = u.material;
                        if (m != null)
                        {
                            for(int j=0; j< m.GetTexturePropertyNames().Length; j++)
                            {
                                if (tex != null && m.GetTexture(m.GetTexturePropertyNames()[j]) == tex)
                                {
                                    badItems.Add(umats[i]);
                                }
                            }
                        }
                    }
                }
            }
            SelectByAssetItems(badItems);
        }

        private void FindOverlaysWithInvalidTextures()
        {
            if (UAI == null) return;
            List<AssetItem> badItems = new List<AssetItem>(); 
            var ovls = UAI.GetAssetItems<OverlayDataAsset>();
            for (int i = 0; i < ovls.Count; i++)
            {
                if (ovls[i] != null)
                {
                    var o = ovls[i].Item as OverlayDataAsset;

                    if (o != null)
                    {
                        for (int j = 0; j < o.textureList.Length; j++)
                        {
                            if (o.textureList[j] == null)
                            {
                                badItems.Add(ovls[i]);
                            }
                        }
                    }
                }
            }
            SelectByAssetItems(badItems);
        }

        private void SelectOverlaysWithMaterials()
        {
            var mats = GetSelectedMaterials();

            SelectByAssetItems(mats);
        }

        private void SelectWithOverlay(OverlayDataAsset umaOverlay)
        {
            if (UAI == null) return;
            List<AssetItem> items = new List<AssetItem>();
            items.Add(UAI.GetAssetItem<OverlayDataAsset>(umaOverlay.overlayName));
            SelectByAssetItems(items);
        }

        private void SelectByChannelType(int channelType)
        {
            if (UAI == null) return;
            var mats = UAI.GetAllAssets<UMAMaterial>();
            for (int i = 0; i < mats.Count; i++)
            {

                for (int j = 0; j < mats[i].channels.Length; j++)
                {
                    if (mats[i].channels[j].channelType == (UMAMaterial.ChannelType)channelType)
                    {
                        SelectMaterial(mats[i]);
                        break;
                    }
                }
            }
        }

        void MenuBar(Rect rect)
		{
#if UMA_ADDRESSABLES
			if (AddressablesMenu.GetItemCount() == 1)
			{
				SetupMenus();
			}
#endif
			Rect MenuRect = new Rect(rect);
			MenuRect.width = 60;


			if(EditorGUI.DropdownButton(MenuRect, new GUIContent("File"), FocusType.Passive,EditorStyles.toolbarDropDown))
			{
				FileMenu.DropDown(new Rect(MenuRect));
			}
			MenuRect.x += 60;
			MenuRect.width = 100;
			if (EditorGUI.DropdownButton(MenuRect, new GUIContent("Addressables"), FocusType.Passive, EditorStyles.toolbarDropDown))
			{
				AddressablesMenu.DropDown(new Rect(MenuRect));
			}
			
			MenuRect.x += 100;
			MenuRect.width = 70;

			if (EditorGUI.DropdownButton(MenuRect, new GUIContent("Items"), FocusType.Passive, EditorStyles.toolbarDropDown))
			{
				ItemsMenu.DropDown(new Rect(MenuRect));
			}

			MenuRect.x += 70;
			MenuRect.width = 70;
			if (EditorGUI.DropdownButton(MenuRect, new GUIContent("Tools"), FocusType.Passive, EditorStyles.toolbarDropDown))
			{
				ToolsMenu.DropDown(new Rect(MenuRect));
			}

			MenuRect.x += 70;
			MenuRect.width = 100;

			if (GUI.Button(MenuRect, new GUIContent("Collapse All"), EditorStyles.toolbarButton))
			{
				if (treeView != null) treeView.CollapseAll();
			}

			MenuRect.x += 100;
			MenuRect.width = 100;

			if (GUI.Button(MenuRect, new GUIContent("Expand All"), EditorStyles.toolbarButton))
			{
				if (treeView != null) treeView.ExpandAll();
			}

			MenuRect.x += 100;
			MenuRect.width = 100;

			bool newShowUtilities = GUI.Toggle(MenuRect, ShowUtilities, "Show Utilities", EditorStyles.toolbarButton);

			if (newShowUtilities != ShowUtilities)
			{
				ShowUtilities = newShowUtilities;
                Repaint();
			}

            if (UAI != null && EditorUtility.IsDirty(UAI))
            {
                MenuRect.x += 100;
                MenuRect.width = 150;
                GUI.Label(MenuRect, new GUIContent("Unsaved Changes"), EditorStyles.boldLabel);
            }

            Rect FillRect = new Rect(rect);
			FillRect.x += 530;
			FillRect.width -= 530;
			GUI.Box(FillRect, "", EditorStyles.toolbar);
        }

        void SearchBar (Rect rect)
		{
			Rect DropDown = new Rect(rect);
			DropDown.width = 150;

			int newLoadedItems = EditorGUI.Popup(DropDown, LoadedItems, LoadedValues);
			if (newLoadedItems != LoadedItems)
			{
				LoadedItems = newLoadedItems;
				if ((eLoaded) LoadedItems == eLoaded.SelectedOnly)
				{
					LoadOnly.Clear();
					var treeElements = new List<AssetTreeElement>();
					if (treeView != null && treeView.treeModel != null)
					{
						TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);
						foreach(AssetTreeElement ate in treeElements)
						{
							if (ate.ai != null && ate.Checked)
							{
								LoadOnly.Add(ate.ai);
							}
						}
						treeView.ExpandAll();
					}
				}
				m_Initialized = false;
				Repaint();
			}

			DropDown.x += DropDown.width;
			DropDown.width = 110;

			int newShowIndex = EditorGUI.Popup(DropDown, ShowIndex, ShowTypes);
			if (newShowIndex != ShowIndex)
			{
				ShowIndex = newShowIndex;
				m_Initialized = false;
				Repaint();
			}

			rect.x = DropDown.x+DropDown.width;
			rect.width -= rect.x;
            if (treeView != null)
            {
			    treeView.searchString = m_SearchField.OnGUI (rect, treeView.searchString);
            }
		}

		void DoTreeView (Rect rect)
		{
            if (treeView != null)
            {
		        treeView.OnGUI(rect);
            }
		}

		void BottomToolBar (Rect rect)
		{
			GUIStyle DropBox = new GUIStyle(EditorStyles.helpBox);
			DropBox.padding.left += 3;
			DropBox.padding.right += 3;
			DropBox.alignment = TextAnchor.MiddleCenter;

			GUI.Box(AddPadRect, "Drag indexable assets here to ADD them to the index.", DropBox);
			GUI.Box(RemovePadRect, "Drag indexable assets here to REMOVE them from the index.", DropBox);
			GUI.Box(AddTypePadRect, "Drag an asset here to start indexing that type of asset.", DropBox);
			DragDropAdd(AddPadRect);
			DragDropRemove(RemovePadRect);
			DragDropType(AddTypePadRect);
		}
#endregion
	}


	internal class MyMultiColumnHeader : MultiColumnHeader
	{
		Mode m_Mode;

		public enum Mode
		{
			LargeHeader,
			DefaultHeader,
			MinimumHeaderWithoutSorting
		}

		public MyMultiColumnHeader(MultiColumnHeaderState state)
			: base(state)
		{
			mode = Mode.DefaultHeader;
		}

		public Mode mode
		{
			get
			{
				return m_Mode;
			}
			set
			{
				m_Mode = value;
				switch (m_Mode)
				{
					case Mode.LargeHeader:
						canSort = true;
						height = 37f;
						break;
					case Mode.DefaultHeader:
						canSort = true;
						height = DefaultGUI.defaultHeight;
						break;
					case Mode.MinimumHeaderWithoutSorting:
						canSort = false;
						height = DefaultGUI.minimumHeight;
						break;
				}
			}
		}

		protected override void ColumnHeaderGUI (MultiColumnHeaderState.Column column, Rect headerRect, int columnIndex)
		{
			// Default column header gui
			base.ColumnHeaderGUI(column, headerRect, columnIndex);

			// Add additional info for large header
			if (mode == Mode.LargeHeader)
			{
				// Show example overlay stuff on some of the columns
				if (columnIndex > 2)
				{
					headerRect.xMax -= 3f;
					var oldAlignment = EditorStyles.largeLabel.alignment;
					EditorStyles.largeLabel.alignment = TextAnchor.UpperRight;
					GUI.Label(headerRect, 36 + columnIndex + "%", EditorStyles.largeLabel);
					EditorStyles.largeLabel.alignment = oldAlignment;
				}
			}
		}
	}
}
