using System;
using System.Collections.Generic;
using System.Linq;
using UMA.CharacterSystem;
using UMA.Editors;
using UMA.PoseTools;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using UnityEngine;


namespace UMA.Controls
{
    class AssetIndexerWindow : EditorWindow
    {
        [NonSerialized] bool m_Initialized;
        [SerializeField] TreeViewState m_TreeViewState; // Serialized in the window layout file so it survives assembly reloading
        [SerializeField] MultiColumnHeaderState m_MultiColumnHeaderState;
        public UMAAssetTreeView treeView { get; private set; }

        List<IUMAAddressablePlugin> addressablePlugins = new List<IUMAAddressablePlugin>();

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
            /*			return AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                             .Where(x => typeof(IUMAAddressablePlugin).IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract)
                             .Select(x => x).ToList();*/
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
                UAI.RebuildLibrary();
                /*UAI.SaveKeeps();
                UAI.Clear();
                UAI.BuildStringTypes();
                UAI.AddEverything(false);
                UAI.RestoreKeeps();
                UAI.ForceSave();
                Resources.UnloadUnusedAssets(); */
                m_Initialized = false;
                Repaint();
            });

            AddMenuItemWithCallback(FileMenu, "Rebuild From Project (include text assets)", () =>
            {
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
                UAI.RemoveReferences();
                Resources.UnloadUnusedAssets();
                m_Initialized = false;
                Repaint();
                EditorUtility.DisplayDialog("Repair", "References Removed", "OK");
            });

            AddMenuItemWithCallback(FileMenu, "Repair and remove invalid items", () =>
            {
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
                UAI.Clear();
                m_Initialized = false;
                Repaint();
            });


            AddMenuItemWithCallback(FileMenu, "Backup Index", () =>
            {
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

            AddMenuItemWithCallback(FileMenu, "Restore Index", () =>
            {
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
                UMAAddressablesSupport.Instance.CleanupAddressables();
                UMAAddressablesSupport.Instance.GenerateAddressables();
                Resources.UnloadUnusedAssets();
                m_Initialized = false;
                Repaint();
            });

            AddMenuItemWithCallback(_AddressablesMenu, "Generators/Generate Single Group (Final Build Only)", () =>
            {
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


            AddMenuItemWithCallback(_AddressablesMenu, "Remove Addressables", () =>
            {
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
					var defineSymbols = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup).Split(';'));
					defineSymbols.Add("UMA_ADDRESSABLES");
					PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, string.Join(";", defineSymbols));
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
                EditorUtility.DisplayProgressBar("Validating", "Validating Slots", 0.0f);
                List<SlotDataAsset> slots = UMAAssetIndexer.Instance.GetAllAssets<SlotDataAsset>();
                List<SlotDataAsset> BadSlots = new List<SlotDataAsset>();

                for (int i = 0; i < slots.Count; i++)
                {
                    SlotDataAsset sda = slots[i];
                    if (!sda.ValidateMeshData())
                    {
                        BadSlots.Add(sda);
                    }
                    float perc = (float)i / (float)slots.Count;
                    EditorUtility.DisplayProgressBar("Validating", "Validating Slots", perc);
                }
                return;
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
            treeView.RecalcTypeChecks();
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
            UAI.ForceSave();
            treeView.RecalcTypeChecks();
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

                    /*
					Dictionary<string, UMAPackedRecipeBase.PackedOverlayColorDataV3> NewColors = new Dictionary<string, UMAPackedRecipeBase.PackedOverlayColorDataV3>();

					foreach(var ocd in dest.fColors)
                    {
						if (!string.IsNullOrEmpty(ocd.name))
						{
							NewColors.Add(ocd.name, ocd);
						}
                    }

					foreach(var color in col)
                    {
						if (NewColors.ContainsKey(color.name))
                        {
							NewColors[color.name] = color;
                        }
						else
                        {
							NewColors.Add(color.name, color);
                        }
                    }
					dest.fColors = NewColors.Values.ToArray();*/

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
                    utr.PackedSave(dest, null);
                    UMAData.UMARecipe ur = new UMAData.UMARecipe();
                    utr.Load(ur);
                    EditorUtility.SetDirty(utr);
                    ate.ai._SerializedItem = null;
                }
            }

            UAI.ForceSave();
            treeView.RecalcTypeChecks();
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
                UAI.ForceSave();
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
            if (slotDataAsset.material != null)
            {
                objects.Add(slotDataAsset.material);
                if (slotDataAsset.material.material != null)
                {
                    objects.Add(slotDataAsset.material.material);
                    objects.AddRange(GetMaterialDepencies(slotDataAsset.material.material));
                }
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
            if (ai._Type == typeof(SlotDataAsset))
            {
                (ai.Item as SlotDataAsset).material = umaMaterial;
                EditorUtility.SetDirty(ai.Item);
            }
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
                    string path = AssetDatabase.GetAssetPath(uwr.GetInstanceID());
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
                    if (theAsset.GetInstanceID() == AddedMHA.GetInstanceID())
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

                    string path = AssetDatabase.GetAssetPath(uwr.GetInstanceID());
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
            if (UMAContext.Instance == null)
            {
                EditorUtility.DisplayDialog("No context found!","A valid context must be loaded in an open scene to use this function.","OK");
                return;
            }
            var assets = GetSelectedAssets(typeof(UMAWardrobeRecipe));
            foreach (var ai in assets)
            {
                UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
                if (uwr != null)
                {
                    uwr.PackedLoad(UMAContext.Instance);
                    UMAData.UMARecipe _recipe = new UMAData.UMARecipe();
                    uwr.Load(_recipe, UMAContext.Instance);

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


                    uwr.Save(_recipe, UMAContextBase.Instance);
                    EditorUtility.SetDirty(uwr);
#if (UNITY_2020_3 && UNITY_2020_3_16_OR_NEWER) || UNITY_2021_1_17_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(uwr);
#else
                    AssetDatabase.SaveAssets();
#endif

                    string path = AssetDatabase.GetAssetPath(uwr.GetInstanceID());
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

                    string path = AssetDatabase.GetAssetPath(uwr.GetInstanceID());
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

                    string path = AssetDatabase.GetAssetPath(uwr.GetInstanceID());
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

                    string path = AssetDatabase.GetAssetPath(uwr.GetInstanceID());
                    AssetDatabase.ImportAsset(path);
                }
            }
        }

        void SelectAllWardrobeRecipesForRace(RaceData race)
        {
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
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement tr in treeElements)
            {
                if (tr.ai != null && tr.Checked)
                {
                    tr.ai.IsAlwaysLoaded = Keep;
                }
            }
            UMAAssetIndexer.Instance.ForceSave();
            RecountTypes();
        }

        void MarkIgnore(bool IgnoreFlag)
        {
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            foreach (AssetTreeElement tr in treeElements)
            {
                if (tr.ai != null && tr.Checked)
                {
                    tr.ai.Ignore = IgnoreFlag;
                }
            }
            UMAAssetIndexer.Instance.ForceSave();
            RecountTypes();
        }

        void SelectByAssetItems(List<AssetItem> items, bool recalculate = true)
        {
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
            if (recalculate)
            {
                treeView.RecalcTypeChecks();
            }
        }

        void FixupTextureChannels(UMAMaterial material)
        {
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
                        if (ate.type == typeof(SlotDataAsset))
                        {
                            SlotDataAsset sda = ate.ai.Item as SlotDataAsset;
                            if (sda.material == null) continue;

                            if (sda.material.name == material.name)
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
            RaceData rc = Race as RaceData;
            List<AssetItem> recipeItems = UAI.GetAssetItems(rc.baseRaceRecipe as UMAPackedRecipeBase);
            SelectByAssetItems(recipeItems);
        }

        void SelectSlotsByRace(object Race)
        {
            RaceData rc = Race as RaceData;
            List<AssetItem> recipeItems = UAI.GetAssetItems(rc.baseRaceRecipe as UMAPackedRecipeBase);

            recipeItems = recipeItems.Where(x => x._Type == typeof(SlotDataAsset)).ToList();
            SelectByAssetItems(recipeItems);
        }

        void SelectOverlaysByRace(object Race)
        {
            RaceData rc = Race as RaceData;
            List<AssetItem> recipeItems = UAI.GetAssetItems(rc.baseRaceRecipe as UMAPackedRecipeBase);
            recipeItems = recipeItems.Where(x => x._Type == typeof(OverlayDataAsset)).ToList();
            SelectByAssetItems(recipeItems);
        }

        public void RecountTypes()
        {
            var treeElements = new List<AssetTreeElement>();
            TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

            List<AssetTreeElement> Types = new List<AssetTreeElement>();
            foreach (TreeElement t in treeView.treeModel.root.children)
            {
                AssetTreeElement ate = t as AssetTreeElement;
                ate.IsResourceCount = 0;
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
            UAI.ForceSave();
            EditorUtility.ClearProgressBar();
        }

        private int CalculateSelectedSize()
        {
            long kbytes = 0;

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
                        UAI.RemoveAsset(tr.ai._Type, tr.ai._Name);
                        current += 1.0f;
                    }
                }
            }
            EditorUtility.DisplayProgressBar("Removing Assets", "Save Index to Disk", 1.0f);
            UAI.ForceSave();
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
            if (!m_Initialized)
            {
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

                            UAI.AddIfIndexed(draggedObjects[i]);

                            var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
                            if (System.IO.Directory.Exists(path))
                            {
                                UAI.RecursiveScanFoldersForAssets(path);
                            }
                        }
                    }
                    UAI.ForceSave();
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
                            UAI.RemoveIfIndexed(draggedObjects[i]);

                            var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
                            if (System.IO.Directory.Exists(path))
                            {
                                UAI.RecursiveScanFoldersForRemovingAssets(path);
                            }
                        }
                    }
                    UAI.ForceSave();
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
                            UAI.AddType(sType);
                        }
                    }
                    UAI.ForceSave();
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
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                dots += ".";
                if (dots.Length > 20)
                    dots = "";
                GUILayout.Space(30);
                EditorGUILayout.LabelField("    Compile/update in progress  " + dots);
                System.Threading.Thread.Sleep(100);
                Repaint();
                return;
            }
            InitIfNeeded();

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


        Vector2 sideBarPosition;
        bool _meshHideFoldout;
        bool _materialFoldout;
        bool _raceFoldout;
        bool _recipeFoldout;
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

            GUILayout.BeginScrollView(sideBarPosition,false,true);

            _meshHideFoldout = EditorGUILayout.Foldout(_meshHideFoldout, "Mesh Hide Assetz");
            if (_meshHideFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                GUILayout.Label("Mesh Hide Asset:");
                GUILayout.BeginHorizontal();
                AddedMHA = EditorGUILayout.ObjectField("", AddedMHA, typeof(MeshHideAsset), false, GUILayout.Width(175)) as MeshHideAsset;
                if (GUILayout.Button("Selected", GUILayout.Width(75)))
                {
                    var o = GetSelectedAssets(typeof(MeshHideAsset)).FirstOrDefault();
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
                    var o = GetSelectedAssets(typeof(UMAMaterial)).FirstOrDefault();
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
                    var o = GetSelectedAssets(typeof(RaceData)).FirstOrDefault();
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
                    var o = GetSelectedAssets(typeof(OverlayDataAsset)).FirstOrDefault();
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
                    var o = GetSelectedAssets(typeof(SlotDataAsset)).FirstOrDefault();
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
                GUIHelper.EndVerticalPadded(10);
            }

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

        private void SelectUnusedMeshHideAssets()
        {
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
                        if (item.triangleFlags[sm].Length != slot.meshData.submeshes[sm].getBaseTriangles().Length/3)
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
            List<AssetItem> materials = new List<AssetItem>();

            var slots = UAI.GetAssetItems<SlotDataAsset>();
            var overlays = UAI.GetAssetItems<OverlayDataAsset>();
            var materialsList = UAI.GetAssetItems<UMAMaterial>();

            for (int materialIndex = 0; materialIndex < materialsList.Count; materialIndex++)
            {
                AssetItem ai = materialsList[materialIndex];
                UMAMaterial uMAMaterial = ai.Item as UMAMaterial;
                bool found = false;
                for(int i=0;i<slots.Count;i++)
                {
                    if (slots[i] != null && slots[i].Item != null)
                    {
                        SlotDataAsset slot = slots[i].Item as SlotDataAsset;
                        if (slot.material != null && slot.material.name == uMAMaterial.name)
                        {
                            found = true;
                            break;
                        }
                    }
                }
                // now check overlays
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

        private void SelectClippingSlots()
        {
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
            List<AssetItem> items = new List<AssetItem>();

            var slots = UAI.GetAssetItems<SlotDataAsset>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                {
                    var s = slots[i].Item as SlotDataAsset;
                    if (s.meshData != null)
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
            List<AssetItem> items = new List<AssetItem>();
            items.Add(UAI.GetAssetItem<SlotDataAsset>(umaSlot.slotName));
            SelectByAssetItems(items);
        }

        private void FindOverlaysWithTexture(Texture2D tex)
        {
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
                            if (o.textureList[j].GetInstanceID() == tex.GetInstanceID())
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
                                if (m.GetTexture(m.GetTexturePropertyNames()[j]) == tex)
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
            List<AssetItem> items = new List<AssetItem>();
            items.Add(UAI.GetAssetItem<OverlayDataAsset>(umaOverlay.overlayName));
            SelectByAssetItems(items);
        }

        private void SelectByChannelType(int channelType)
        {
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
			MenuRect.width = 100;

			if (GUI.Button(MenuRect, new GUIContent("Collapse All"), EditorStyles.toolbarButton))
			{
				treeView.CollapseAll();
			}

			MenuRect.x += 100;
			MenuRect.width = 100;

			if (GUI.Button(MenuRect, new GUIContent("Expand All"), EditorStyles.toolbarButton))
			{
				treeView.ExpandAll();
			}

			MenuRect.x += 100;
			MenuRect.width = 100;

			bool newShowUtilities = GUI.Toggle(MenuRect, ShowUtilities, "Show Utilities", EditorStyles.toolbarButton);

			if (newShowUtilities != ShowUtilities)
			{
				ShowUtilities = newShowUtilities;
                Repaint();
			}

			Rect FillRect = new Rect(rect);
			FillRect.x += 530;
			FillRect.width -= 530;
			GUI.Box(FillRect, "", EditorStyles.toolbar);

            /*
			if (ShowUtilities)
			{
				rect.y += rect.height;
				GUI.Box(rect, "");
				GUILayout.BeginArea(rect);
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Apply MeshHideAssets to Selection", GUILayout.Width(259)))
				{
					UpdateMeshHideAssets();
					AssetDatabase.SaveAssets();
				}
				AddedMHA = EditorGUILayout.ObjectField("", AddedMHA, typeof(MeshHideAsset), false, GUILayout.Width(250)) as MeshHideAsset;
				GUILayout.EndHorizontal();
				GUILayout.EndArea();
				rect.y += rect.height;
				GUI.Box(rect, "");
				GUILayout.BeginArea(rect);
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Apply UMAMaterial to Selection", GUILayout.Width(259)))
				{
					UpdateMaterials();
					AssetDatabase.SaveAssets();
				}
				umaMaterial = EditorGUILayout.ObjectField("", umaMaterial, typeof(UMAMaterial), false, GUILayout.Width(250)) as UMAMaterial;
				if (GUILayout.Button("Select overlays with UMAMaterial", GUILayout.Width(259)))
				{
					SelectByMaterial(umaMaterial);
				}
				if (GUILayout.Button("Fixup Texture Channels",GUILayout.Width(150)))
                {
					FixupTextureChannels(umaMaterial);
                }
				GUILayout.EndHorizontal();
				GUILayout.EndArea();


			}
            */
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
			treeView.searchString = m_SearchField.OnGUI (rect, treeView.searchString);
		}

		void DoTreeView (Rect rect)
		{
			treeView.OnGUI(rect);
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
