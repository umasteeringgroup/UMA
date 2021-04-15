using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UMA.CharacterSystem;
using UMA.PoseTools;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Callbacks;
using UnityEditor.IMGUI.Controls;
using UnityEngine;


namespace UMA.Controls
{
	class AssetIndexerWindow : EditorWindow
	{
		[NonSerialized] private float UtilityPanelHeight = 20.0f;
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
		UMAMaterial Replacement;

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
		string[] LoadedValues = { "All", "Addressable Only","Non-Addressable Only", "Keep Loaded","In Resources","Recipes not added to groups","Currently Selected Items" };
		public List<AssetItem> LoadOnly = new List<AssetItem>();

		enum eShowTypes { All, WithItems};
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

		    foreach(var asm in Assemblies)
            {

				try
                {
					var Types = asm.GetTypes();
					foreach(var t in Types)
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
		public static AssetIndexerWindow GetWindow ()
		{
			var window = GetWindow<AssetIndexerWindow>();

			/* Setup the window menus */
			window.SetupMenus();

			Texture icon = AssetDatabase.LoadAssetAtPath<Texture>("Assets/UMA/InternalDataStore/UMA32.png");
			window.titleContent = new GUIContent(UmaAboutWindow.umaVersion+" Global Library", icon);
			window.Focus();
			window.Repaint();
			return window;
		}

#region utility functions

		

		void AddPlugins(List<Type> PluginTypes)
		{
			addressablePlugins = new List<IUMAAddressablePlugin>();
			foreach(Type t in PluginTypes)
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
				UAI.Clear();
				UAI.BuildStringTypes();
				UAI.AddEverything(false);
				Resources.UnloadUnusedAssets();
				m_Initialized = false;
				Repaint();
			});

			AddMenuItemWithCallback(FileMenu, "Rebuild From Project (include text assets)", () =>
			{
				UAI.Clear();
				UAI.BuildStringTypes();
				UAI.AddEverything(true);
				Resources.UnloadUnusedAssets();
				m_Initialized = false;
				Repaint();
			});
			AddMenuItemWithCallback(FileMenu, "Cleanup References", () =>
			{
				UAI.UpdateReferences();
				Resources.UnloadUnusedAssets();
				m_Initialized = false;
				Repaint();
				EditorUtility.DisplayDialog("Repair", "References cleaned", "OK");
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
				ShowUtilities = ! ShowUtilities;
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
						EditorUtility.DisplayDialog("Error", "Error writing backup: " + ex.Message,"OK");
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

			foreach(IUMAAddressablePlugin plugin in addressablePlugins)
			{
				AddMenuItemWithCallbackParm(_AddressablesMenu, "Generators/"+plugin.Menu, (object o) =>
				{
					IUMAAddressablePlugin addrplug = o as IUMAAddressablePlugin;
					UMAAddressablesSupport.Instance.GenerateAddressables(addrplug);
					Resources.UnloadUnusedAssets();
					m_Initialized = false;
					Repaint();
				},plugin);
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
				var treeElements = new List<AssetTreeElement>();
				TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);
				foreach(AssetTreeElement ate in treeElements)
				{
					ate.Checked = true;
				}
				treeView.RecalcTypeChecks();
				Repaint();
				return;
			});

			AddMenuItemWithCallback(ItemsMenu, "Clear Selection", () =>
			{
				var treeElements = new List<AssetTreeElement>();
				TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);
				foreach (AssetTreeElement ate in treeElements)
				{
					ate.Checked = false;
				}
				treeView.RecalcTypeChecks();
				Repaint();
				return;
			});


			AddMenuItemWithCallback(ToolsMenu, "Validate All Indexed Slots", () =>
			{
				EditorUtility.DisplayProgressBar("Validating", "Validating Slots", 0.0f);
                List<SlotDataAsset> slots = UMAAssetIndexer.Instance.GetAllAssets<SlotDataAsset>();
				List<SlotDataAsset> BadSlots = new List<SlotDataAsset>();

				for(int i=0;i<slots.Count;i++)
                {
					SlotDataAsset sda = slots[i];
					if (!sda.ValidateMeshData())
                    {
						BadSlots.Add(sda);
                    }
					float perc = (float)i /(float)slots.Count;
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

			ItemsMenu.AddSeparator("");

			AddMenuItemWithCallback(ItemsMenu, "Apply selected races to selected wardrobe recipes", () =>
			 {
				 ApplyRacesToRecipes();
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
			AddMenuItemWithCallback(ItemsMenu, "Force Selected Items to Save", () => 
			{
				ForceSave();
				m_Initialized = false;
				Repaint();
				return; 
			});



		}

        private void ApplyRacesToRecipes()
        {
			List<AssetTreeElement> selectedElements = GetSelectedElements();

			List<RaceData> races = new List<RaceData>();
			List<UMATextRecipe> recipes = new List<UMATextRecipe>();

			foreach(AssetTreeElement element in selectedElements)
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
				EditorUtility.DisplayDialog("Error","No races selected. You must select both the races and the wardrobe items to run this command.","OK");
				return;
            }
			if (recipes.Count == 0)
            {
				EditorUtility.DisplayDialog("Error", "No wardrobe recipes/collections selected. You must select both the races and the wardrobe items to run this command.", "OK");
				return;
			}
			if (EditorUtility.DisplayDialog("Update Recipes?","This will apply the selected race(s) to the selected wardrobe items (UMAWardrobeRecipe or UMAWardrobeCollection","Continue","Cancel"))
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
				EditorUtility.DisplayDialog("Warning","No items are selected. Please select the items in the list before using this option.", "OK");
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
				foreach(var dna in raceData.dnaConverterList)
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

			foreach(var posepair in expressionSet.posePairs)
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

			foreach(AssetItem ai in dependencies)
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
				foreach(MeshHideAsset mha in uMATextRecipe.MeshHideAssets)
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

			foreach(Texture t in overlayDataAsset.textureList)
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
				(ai.Item as SlotDataAsset).material = Replacement;
				EditorUtility.SetDirty(ai.Item);
			}
			if (ai._Type == typeof(OverlayDataAsset))
			{
				(ai.Item as OverlayDataAsset).material = Replacement;
				EditorUtility.SetDirty(ai.Item);
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

		void SelectByAssetItems(List<AssetItem> items)
		{
			var treeElements = new List<AssetTreeElement>();
			TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

			foreach(AssetTreeElement ate in treeElements)
			{
				if (ate.ai != null && items.Contains(ate.ai))
				{
					ate.Checked = true;
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
			foreach(TreeElement t in treeView.treeModel.root.children)
			{
				AssetTreeElement ate = t as AssetTreeElement;
				ate.IsResourceCount = 0;
				ate.IsAddrCount = 0;
				ate.Keepcount = 0;
				if (t.hasChildren)
				{
					foreach (TreeElement c in t.children)
					{
						AssetItem ai = (c as AssetTreeElement).ai;
						if (ai.IsResource)
							ate.IsResourceCount++;
						if (ai.IsAlwaysLoaded)
							ate.Keepcount++;
						if (ai.IsAddressable)
							ate.IsAddrCount++;
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
		Rect multiColumnTreeViewRect
		{
			get 
			{
				if (ShowUtilities)
				{
					return new Rect(10, 46 + UtilityPanelHeight, position.width - 20, position.height - (90+ UtilityPanelHeight));
				}
				else
				{
					return new Rect(10, 46, position.width - 20, position.height - 90);
				}
			}
		}

		Rect toolbarRect
		{
			get 
			{
				if (ShowUtilities)
				{
					return new Rect(10f, 23f+ UtilityPanelHeight, position.width - 20f, 20f);
				}
				else
				{
					return new Rect(10f, 23f, position.width - 20f, 20f);
				}
			}
		}
		Rect menubarRect
		{
			get { return new Rect(0f, 0f, position.width, 20f); }
		}

		Rect bottomToolbarRect
		{
			get { return new Rect( 10f, position.height - 42f, position.width - 20f, 40f); }
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
		void InitIfNeeded ()
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
					multiColumnHeader.ResizeToFit ();

				var treeModel = new TreeModel<AssetTreeElement>(GetData());
				
				treeView = new UMAAssetTreeView(this, m_TreeViewState, multiColumnHeader, treeModel);

				m_SearchField = new SearchField();
				m_SearchField.downOrUpArrowKeyPressed += treeView.SetFocusAndEnsureSelectedItem;

				m_Initialized = true;
			}
		}

		bool ShouldLoad(eLoaded itemsToLoad, AssetItem ai)
		{
			switch(itemsToLoad)
			{
				case eLoaded.Resx:
					return (ai.IsResource || (ai.IsAddressable == false)) ;
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
						if( !LoadedLabels.Contains(ubr.AssignedLabel))
						{
							return true;
						}
					}
					return false;
				}
				case eLoaded.SelectedOnly:
				{
					if (LoadOnly.Contains(ai))
						return true;
					else
						return false;
				}
			}
			return true;
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
				foreach(string s in labels)
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
		void OnGUI ()
		{
			if (EditorApplication.isCompiling)
			{
				dots += ".";
				if (dots.Length > 20)
						dots = "";
				GUILayout.Space(30);
				EditorGUILayout.LabelField("    Compile in progress  "+dots);
				System.Threading.Thread.Sleep(100);
				Repaint();
				return;
			}
			InitIfNeeded();

			MenuBar(menubarRect);
			SearchBar (toolbarRect);
			DoTreeView (multiColumnTreeViewRect);
			BottomToolBar (bottomToolbarRect);
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

			if (ShowUtilities)
			{
				rect.y += rect.height;
				GUI.Box(rect, "");
				GUILayout.BeginArea(rect);
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Apply UMAMaterials to Selection",GUILayout.Width(259)))
				{
					UpdateMaterials();
					AssetDatabase.SaveAssets();
				}
				Replacement = EditorGUILayout.ObjectField("", Replacement, typeof(UMAMaterial), false, GUILayout.Width(250)) as UMAMaterial;
				GUILayout.EndHorizontal();
				GUILayout.EndArea();
			}
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
