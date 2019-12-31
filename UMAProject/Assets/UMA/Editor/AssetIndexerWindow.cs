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
		SearchField m_SearchField;
		UMAAssetIndexer _UAI;
		UMAAssetTreeView m_TreeView;
		//MyTreeAsset m_MyTreeAsset;
		UMAAssetIndexer UAI
		{
			get
			{
				if (_UAI == null)
				{
					_UAI = UMAAssetIndexer.Instance;
				}
				return _UAI;
			}
		}

		[MenuItem("UMA/Global Library (experimental)")]
		public static AssetIndexerWindow GetWindow ()
		{
			var window = GetWindow<AssetIndexerWindow>();
			window.titleContent = new GUIContent("Global Library");
			window.Focus();
			window.Repaint();
			return window;
		}

		//void SetTreeAsset (MyTreeAsset myTreeAsset)
		//{
		//	m_MyTreeAsset = myTreeAsset;
		//	m_Initialized = false;
		//}

		Rect multiColumnTreeViewRect
		{
			get { return new Rect(20, 30, position.width-40, position.height-60); }
		}

		Rect toolbarRect
		{
			get { return new Rect (20f, 10f, position.width-40f, 20f); }
		}

		Rect bottomToolbarRect
		{
			get { return new Rect(20f, position.height - 18f, position.width - 40f, 16f); }
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
				if (firstInit)
					multiColumnHeader.ResizeToFit ();

				var treeModel = new TreeModel<AssetTreeElement>(GetData());
				
				m_TreeView = new UMAAssetTreeView(m_TreeViewState, multiColumnHeader, treeModel);

				m_SearchField = new SearchField();
				m_SearchField.downOrUpArrowKeyPressed += m_TreeView.SetFocusAndEnsureSelectedItem;

				m_Initialized = true;
			}
		}
		
		IList<AssetTreeElement> GetData ()
		{
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
					treeElements.Add(ate);

					AssetItem[] items = new AssetItem[TypeDic.Values.Count];
					TypeDic.Values.CopyTo(items, 0);

					for (int i = 0; i < TypeDic.Values.Count; i++)
					{
						AssetItem ai = items[i];
						AssetTreeElement atai = new AssetTreeElement(ai.EvilName, 1, ++totalitems);
						atai.ai = ai;
						atai.index = i;
						atai.type = t;
						treeElements.Add(atai);
					}
				}
			}
			return treeElements;
			// generate some test data
			//return MyTreeElementGenerator.GenerateRandomTree(130); 
		}

		/*
		void OnSelectionChange ()
		{
			if (!m_Initialized)
				return;

			var myTreeAsset = Selection.activeObject as MyTreeAsset;
			if (myTreeAsset != null && myTreeAsset != m_MyTreeAsset)
			{
				m_MyTreeAsset = myTreeAsset;
				m_TreeView.treeModel.SetData (GetData ());
				m_TreeView.Reload ();
			}
		} */

		void OnGUI ()
		{
			InitIfNeeded();

			SearchBar (toolbarRect);
			DoTreeView (multiColumnTreeViewRect);
			BottomToolBar (bottomToolbarRect);
		}

		void SearchBar (Rect rect)
		{
			treeView.searchString = m_SearchField.OnGUI (rect, treeView.searchString);
		}

		void DoTreeView (Rect rect)
		{
			m_TreeView.OnGUI(rect);
		}

		void BottomToolBar (Rect rect)
		{
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

				// GUILayout.Label (m_MyTreeAsset != null ? AssetDatabase.GetAssetPath (m_MyTreeAsset) : string.Empty);

				GUILayout.FlexibleSpace ();

				if (GUILayout.Button("Set sorting", style))
				{
					var myColumnHeader = (MyMultiColumnHeader)treeView.multiColumnHeader;
					myColumnHeader.SetSortingColumns (new int[] {4, 3, 2}, new[] {true, false, true});
					myColumnHeader.mode = MyMultiColumnHeader.Mode.LargeHeader;
				}


				GUILayout.Label ("Header: ", "minilabel");
				if (GUILayout.Button("Large", style))
				{
					var myColumnHeader = (MyMultiColumnHeader) treeView.multiColumnHeader;
					myColumnHeader.mode = MyMultiColumnHeader.Mode.LargeHeader;
				}
				if (GUILayout.Button("Default", style))
				{
					var myColumnHeader = (MyMultiColumnHeader)treeView.multiColumnHeader;
					myColumnHeader.mode = MyMultiColumnHeader.Mode.DefaultHeader;
				}
				if (GUILayout.Button("No sort", style))
				{
					var myColumnHeader = (MyMultiColumnHeader)treeView.multiColumnHeader;
					myColumnHeader.mode = MyMultiColumnHeader.Mode.MinimumHeaderWithoutSorting;
				}

				GUILayout.Space (10);
				
				if (GUILayout.Button("values <-> controls", style))
				{
					treeView.showControls = !treeView.showControls;
				}
			}

			GUILayout.EndArea();
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
