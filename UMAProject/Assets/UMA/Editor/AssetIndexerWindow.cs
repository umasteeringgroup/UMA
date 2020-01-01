using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Callbacks;
using UnityEditor.IMGUI.Controls;
using UnityEngine;


namespace UMA.Controls
{
	class AssetIndexerWindow : EditorWindow
	{
		[NonSerialized] bool m_Initialized;
		[SerializeField] TreeViewState m_TreeViewState; // Serialized in the window layout file so it survives assembly reloading
		[SerializeField] MultiColumnHeaderState m_MultiColumnHeaderState;
		#region Menus
		GenericMenu _FileMenu;
		GenericMenu _AddressablesMenu;
		GenericMenu _ItemsMenu;

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

		private GenericMenu AddressablesMenu
		{
			get
			{
				if (_AddressablesMenu == null)
				{
					SetupMenus();
				}
				return _AddressablesMenu;
			}
		}
		#endregion

		SearchField m_SearchField;
		UMAAssetIndexer _UAI;
		int LoadedItems = 0;

		enum eLoaded { All, Addressable, NonAddressable, Keep, Refs, NoRefs };
		string[] LoadedValues = { "All", "Addressable Only","Non-Addressable Only", "Keep Loaded","With References", "Non-Addressable Without References" };

		enum eShowTypes { All, WithItems};
		string[] ShowTypes = { "All Types", "Only Types with Children" };
		int ShowIndex = 0;
		
		UMAAssetTreeView m_TreeView;
 
		UMAAssetIndexer UAI
		{
			get
			{
				return UMAAssetIndexer.Instance;
			}
		}

		[MenuItem("UMA/Global Library (experimental)")]
		public static AssetIndexerWindow GetWindow ()
		{
			var window = GetWindow<AssetIndexerWindow>();
			window.SetupMenus();
			window.titleContent = new GUIContent("Global Library");
			window.Focus();
			window.Repaint();
			return window;
		}

		// a method to simplify adding menu items
		void AddMenuItemWithCallback(GenericMenu menu, string menuPath, GenericMenu.MenuFunction function)
		{
			// the menu item is marked as selected if it matches the current value of m_Color
			menu.AddItem(new GUIContent(menuPath), false, function);
		}

		private void SetupMenus()
		{
			_FileMenu = new GenericMenu();
			_AddressablesMenu = new GenericMenu();
			_ItemsMenu = new GenericMenu();

			// ***********************************************************************************
			// File Menu items
			// ***********************************************************************************
			AddMenuItemWithCallback(FileMenu, "Rebuild From Project", () => 
			{
				UAI.Clear();
				UAI.AddEverything(false);
				Resources.UnloadUnusedAssets();
				m_Initialized = false;
				Repaint();
			});

			AddMenuItemWithCallback(FileMenu, "Rebuild From Project (include text assets)", () =>
			{
				UAI.Clear();
				UAI.AddEverything(true);
				Resources.UnloadUnusedAssets();
				m_Initialized = false;
				Repaint();
			});

			AddMenuItemWithCallback(FileMenu, "Repair and remove invalid items", () => 
			{
				UAI.RepairAndCleanup();
				Resources.UnloadUnusedAssets();
				m_Initialized = false;
				Repaint();
			});
			AddMenuItemWithCallback(FileMenu, "Add Build refs to all non-addressables", () => 
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
			});
			AddMenuItemWithCallback(FileMenu, "Empty Index", () => 
			{ 
				UAI.Clear();
				m_Initialized = false;
				Repaint();
			});

			// ***********************************************************************************
			// Addressables Menu items
			// ***********************************************************************************
			AddMenuItemWithCallback(AddressablesMenu, "(Re)Generate Groups", () => 
			{
				UAI.CleanupAddressables();
				UAI.GenerateAddressables();
				Resources.UnloadUnusedAssets();
				m_Initialized = false;
				Repaint();
			});
			AddMenuItemWithCallback(AddressablesMenu, "Remove Addressables", () => 
			{ 
				UAI.CleanupAddressables(false, true);
				m_Initialized = false;
				Repaint();
			});
			AddMenuItemWithCallback(AddressablesMenu, "Delete Empty Groups", () => 
			{
				UAI.CleanupAddressables(true);
			});

			AddMenuItemWithCallback(AddressablesMenu, "Force Add Refs (Bad!!)", () => 
			{
				UAI.AddReferences(true);
				RecountTypes();
				Resources.UnloadUnusedAssets();
				Repaint();
			});
			AddMenuItemWithCallback(AddressablesMenu, "Remove Orphaned Slots", () => 
			{ 
				UAI.CleanupOrphans(typeof(SlotDataAsset));
				m_Initialized = false;
				Repaint();
			});
			AddMenuItemWithCallback(AddressablesMenu, "Remove Orphaned Overlays", () => 
			{ 
				UAI.CleanupOrphans( typeof(OverlayDataAsset) );
				m_Initialized = false;
				Repaint();
			});

			// ***********************************************************************************
			// Items Menu items
			// ***********************************************************************************
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

		public void RecountTypes()
		{
			var treeElements = new List<AssetTreeElement>();
			TreeElementUtility.TreeToList<AssetTreeElement>(treeView.treeModel.root, treeElements);

			List<AssetTreeElement> Types = new List<AssetTreeElement>();
			foreach(TreeElement t in treeView.treeModel.root.children)
			{
				AssetTreeElement ate = t as AssetTreeElement;
				ate.HasRefCount = 0;
				ate.IsAddrCount = 0;
				ate.Keepcount = 0;
				if (t.hasChildren)
				{
					foreach (TreeElement c in t.children)
					{
						AssetItem ai = (c as AssetTreeElement).ai;
						if (ai._SerializedItem != null)
							ate.HasRefCount++;
						if (ai.IsAlwaysLoaded)
							ate.Keepcount++;
						if (ai.IsAddressable)
							ate.IsAddrCount++;
					}
				}
			}
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
		}

		Rect multiColumnTreeViewRect
		{
			get { return new Rect(10, 46, position.width - 20, position.height - 90); }
		}

		Rect toolbarRect
		{
			get { return new Rect(10f, 23f, position.width - 20f, 20f); }
		}
		Rect menubarRect
		{
			get { return new Rect(0f, 0f, position.width, 20f); }
		}

		Rect bottomToolbarRect
		{
			get { return new Rect( 10f, position.height - 42f, position.width - 20f, 40f); }
		}

		public UMAAssetTreeView treeView
		{
			get { return m_TreeView; }
		}

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
				
				m_TreeView = new UMAAssetTreeView(this, m_TreeViewState, multiColumnHeader, treeModel);

				m_SearchField = new SearchField();
				m_SearchField.downOrUpArrowKeyPressed += m_TreeView.SetFocusAndEnsureSelectedItem;

				m_Initialized = true;
			}
		}

		bool ShouldLoad(eLoaded itemsToLoad, AssetItem ai)
		{
			switch(itemsToLoad)
			{
				case eLoaded.Refs:
					return (ai._SerializedItem != null) ;
				case eLoaded.Keep:
					return ai.IsAlwaysLoaded;
				case eLoaded.Addressable:
					return ai.IsAddressable;
				case eLoaded.NonAddressable:
					return !ai.IsAddressable;
				case eLoaded.NoRefs:
				{
					if (ai._SerializedItem == null && ai.IsAddressable == false)
						return true;
					return false;
				}
			}
			return true;
		}
		
		IList<AssetTreeElement> GetData ()
		{
			eLoaded itemstoload = (eLoaded)LoadedItems;
			eShowTypes typesToShow = (eShowTypes)ShowIndex;
			int totalitems = 0;
			var treeElements = new List<AssetTreeElement>();

			var root = new AssetTreeElement("Root", -1, totalitems);

			treeElements.Add(root);

			System.Type[] Types = UAI.GetTypes();

			foreach(System.Type t in Types)
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
							AssetTreeElement atai = new AssetTreeElement(ai.EvilName, 1, ++totalitems);
							atai.ai = ai;
							atai.index = i;
							atai.type = t;
							ElementsToLoad.Add(atai);

							if (ai._SerializedItem != null)
								ate.HasRefCount++;
							if (ai.IsAlwaysLoaded)
								ate.Keepcount++;
							if (ai.IsAddressable)
								ate.IsAddrCount++;
						}
					}

					if (typesToShow == eShowTypes.WithItems && ElementsToLoad.Count < 1)
					{
						continue;
					}

					treeElements.Add(ate);
					treeElements.AddRange(ElementsToLoad);
				}
			}
			return treeElements;
			// generate some test data
			//return MyTreeElementGenerator.GenerateRandomTree(130); 
		}

		void OnGUI ()
		{
			InitIfNeeded();

			MenuBar(menubarRect);
			SearchBar (toolbarRect);
			DoTreeView (multiColumnTreeViewRect);
			BottomToolBar (bottomToolbarRect);
		}
		void MenuBar(Rect rect)
		{
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

			rect.x += 430;
			rect.width -= 430;
			GUI.Box(rect, "", EditorStyles.toolbar);
		}

		void SearchBar (Rect rect)
		{
			Rect DropDown = new Rect(rect);
			DropDown.width = 64;

			int newLoadedItems = EditorGUI.Popup(DropDown, LoadedItems, LoadedValues);
			if (newLoadedItems != LoadedItems)
			{
				LoadedItems = newLoadedItems;
				m_Initialized = false;
				Repaint();
			}

			DropDown.x += 64;

			int newShowIndex = EditorGUI.Popup(DropDown, ShowIndex, ShowTypes);
			if (newShowIndex != ShowIndex)
			{
				ShowIndex = newShowIndex;
				m_Initialized = false;
				Repaint();
			}

			rect.x += 128;
			rect.width -= 128;
			treeView.searchString = m_SearchField.OnGUI (rect, treeView.searchString);
		}

		void DoTreeView (Rect rect)
		{
			m_TreeView.OnGUI(rect);
		}

		void BottomToolBar (Rect rect)
		{
			float DropWidth = rect.width / 3.0f;

			Rect DropArea = new Rect(rect);

			GUIStyle DropBox = new GUIStyle(EditorStyles.helpBox);
			DropBox.padding.left += 3;
			DropBox.padding.right += 3;
			DropBox.alignment = TextAnchor.MiddleCenter;
			DropArea.width = DropWidth;

			Rect Box = new Rect(DropArea);
			Box.x += 2;
			Box.width -= 4;
			GUI.Box(Box, "Drag indexable assets here to ADD them to the index.", DropBox);

			DropArea.x += DropWidth;
			Box = new Rect(DropArea);
			Box.x += 2;
			Box.width -= 4;
			GUI.Box(Box, "Drag indexable assets here to REMOVE them from the index.", DropBox);

			DropArea.x += DropWidth;
			Box = new Rect(DropArea);
			Box.x += 2;
			Box.width -= 4;
			GUI.Box(Box, "Drag an item here to start indexing that type of item.", DropBox);


			/*
			GUILayout.BeginArea (rect);

			using (new EditorGUILayout.HorizontalScope ())
			{

				var style = "miniButton";
				if (GUILayout.Button("Expand All", style))
				{
					treeView.ExpandAll ();
				}

				if (GUILayout.Button("Collapse All", style))
				{
					treeView.CollapseAll ();
				}

				GUILayout.FlexibleSpace();
			}

			GUILayout.EndArea(); */
		}
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
