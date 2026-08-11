using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Assertions;
#if UNITY_6000_2_OR_NEWER
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#endif


namespace UMA.Controls
{
    internal class UMAAssetTreeView : TreeViewWithTreeModel<AssetTreeElement>
	{
		const float kCheckboxOffset = 12;
		const float kRowHeights = 20f;
		const float kToggleWidth = 18f;
		public bool showControls = true;
		public AssetIndexerWindow owningWindow;
		private static GUIContent pingIcon = null;
		private static GUIContent inspectIcon = null;

		enum AssetColumns
		{
			Selection,
			Name,
			Actions,
			Type,
			Loaded,
			IsResource,
			IsAddressable,
            Total,
			Group,
			Labels,
			Ignore,
			Always,
            Buttons
        }

        public enum SortOption
		{
			Name,
			Group
		}


		// Sort options per column
		SortOption[] m_SortOptions =
		{
			SortOption.Name,
			SortOption.Name,
			SortOption.Name,
			SortOption.Name,
			SortOption.Name,
			SortOption.Name,
            SortOption.Name,
            SortOption.Name,
            SortOption.Name,
            SortOption.Group,
			SortOption.Name,
			SortOption.Name,
			SortOption.Name
		};

		public static void TreeToList(TreeViewItem root, IList<TreeViewItem> result)
		{
			if (root == null)
            {
                throw new System.NullReferenceException("root");
            }

            if (result == null)
            {
                throw new System.NullReferenceException("result");
            }

            result.Clear();

			if (root.children == null)
            {
                return;
            }

            Stack<TreeViewItem> stack = new Stack<TreeViewItem>();
			for (int i = root.children.Count - 1; i >= 0; i--)
            {
                stack.Push(root.children[i]);
            }

            while (stack.Count > 0)
			{
				TreeViewItem current = stack.Pop();
				result.Add(current);

				if (current.hasChildren && current.children[0] != null)
				{
					for (int i = current.children.Count - 1; i >= 0; i--)
					{
						stack.Push(current.children[i]);
					}
				}
			}
		}


		// Note we We only build the visible rows, only the backend has the full tree information. 
		// The treeview only creates info for the row list.
		protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
		{
			var rows = base.BuildRows(root);
			SortIfNeeded(root, rows);
			return rows;
		}


		#region SORTING
		void OnSortingChanged(MultiColumnHeader multiColumnHeader)
		{
			SortIfNeeded(rootItem, GetRows());
		}

		void SortIfNeeded(TreeViewItem root, IList<TreeViewItem> rows)
		{
			if (rows.Count <= 1)
            {
                return;
            }

            if (multiColumnHeader.sortedColumnIndex == -1)
			{
				return; // No column to sort for (just use the order the data are in)
			}

			// Sort the roots of the existing tree items
			SortByMultipleColumns();
			TreeToList(root, rows);
			Repaint();
		}

		private int CompareItemsBySortedColumns(TreeViewItem<AssetTreeElement> left, TreeViewItem<AssetTreeElement> right, int[] sortedColumns)
		{
			for (int sortIndex = 0; sortIndex < sortedColumns.Length; sortIndex++)
			{
				SortOption sortOption = m_SortOptions[sortedColumns[sortIndex]];
				bool ascending = multiColumnHeader.IsSortedAscending(sortedColumns[sortIndex]);
				int comparison = CompareItemsForSortOption(left, right, sortOption);
				if (comparison != 0)
				{
					return ascending ? comparison : -comparison;
				}
			}

			return 0;
		}

		private static int CompareItemsForSortOption(TreeViewItem<AssetTreeElement> left, TreeViewItem<AssetTreeElement> right, SortOption sortOption)
		{
			switch (sortOption)
			{
				case SortOption.Name:
					return StringComparer.Ordinal.Compare(left.data.name ?? string.Empty, right.data.name ?? string.Empty);
				case SortOption.Group:
					return StringComparer.Ordinal.Compare(left.data.ai.AddressableGroup ?? string.Empty, right.data.ai.AddressableGroup ?? string.Empty);
				default:
					Assert.IsTrue(false, "Unhandled enum");
					return 0;
			}
		}

		void SortByMultipleColumns()
		{
			var sortedColumns = multiColumnHeader.state.sortedColumns;

			if (sortedColumns.Length == 0)
            {
                return;
            }

			List<TreeViewItem<AssetTreeElement>> sortedChildren = new List<TreeViewItem<AssetTreeElement>>(rootItem.children.Count);
			for (int childIndex = 0; childIndex < rootItem.children.Count; childIndex++)
			{
				sortedChildren.Add(rootItem.children[childIndex] as TreeViewItem<AssetTreeElement>);
			}

			sortedChildren.Sort((left, right) => CompareItemsBySortedColumns(left, right, sortedColumns));

			List<TreeViewItem> newChildren = new List<TreeViewItem>(sortedChildren.Count);
			for (int childIndex = 0; childIndex < sortedChildren.Count; childIndex++)
			{
				newChildren.Add(sortedChildren[childIndex]);
			}

			rootItem.children = newChildren;
		}
		#endregion
		public UMAAssetTreeView(AssetIndexerWindow owner, TreeViewState state, MultiColumnHeader multiColumnHeader, TreeModel<AssetTreeElement> model) : base(state, multiColumnHeader, model)
        {
			owningWindow = owner;
			rowHeight = kRowHeights;
			columnIndexForTreeFoldouts = 1;
			showAlternatingRowBackgrounds = true;
			showBorder = true;
			customFoldoutYOffset = (kRowHeights - EditorGUIUtility.singleLineHeight) * 0.5f; // center foldout in the row since we also center content. See RowGUI
			extraSpaceBeforeIconAndLabel = kToggleWidth;
			
			//multiColumnHeader.sortingChanged += OnSortingChanged;
			//	var myColumnHeader = (MyMultiColumnHeader)treeView.multiColumnHeader;
			//this.multiColumnHeader.mode = MyMultiColumnHeader.Mode.MinimumHeaderWithoutSorting;
			Reload();
		}

		protected override void RowGUI(RowGUIArgs args)
		{
			var item = (TreeViewItem<AssetTreeElement>)args.item;

			for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
			{
				CellGUI(args.GetCellRect(i), item, (AssetColumns)args.GetColumn(i), ref args);
			}
		}
		void HeaderCellGUI(Rect cellRect, TreeViewItem<AssetTreeElement> item, AssetColumns column, ref RowGUIArgs args)
		{
			AssetTreeElement ate = item.data;

			switch (column)
			{
				case AssetColumns.Selection:
					{

						// EditorGUI.Toggle(cellRect, item.data.ai._SerializedItem != null);
						if (item.data.AmountChecked == Amount.Mixed)
						{
							EditorGUI.showMixedValue = true;
						} 
						bool checkval = item.data.AmountChecked == Amount.All;
						bool newval = EditorGUI.Toggle(cellRect, checkval);
						EditorGUI.showMixedValue = false;

						if (checkval != newval)
						{
							SetAllChildren(item.data.type, newval);
						}
						// checking/unchecking this will toggle ALL the items below.
					}
					break;

					case AssetColumns.Loaded:
						{
							GUI.Label(cellRect, ate.LoadedCount.ToString());
						}
						break;

				case AssetColumns.Always:
					{
						GUI.Label(cellRect, ate.Keepcount.ToString());
					}
					break;

				case AssetColumns.Ignore:
					{
						GUI.Label(cellRect, ate.IgnoreCount.ToString());
					}
					break;

				case AssetColumns.IsResource:
					{
						GUI.Label(cellRect, ate.IsResourceCount.ToString());
					}
					break;

				case AssetColumns.Name:
					{
						Rect toggleRect = cellRect;
						toggleRect.x += GetContentIndent(item);
						// Default icon and label
						args.rowRect = cellRect;
						base.RowGUI(args);
						//					DefaultGUI.Label(cellRect, item.data.name, false, false);
					}
					break;

				case AssetColumns.Actions:
					break;

				case AssetColumns.IsAddressable:
					{
						GUI.Label(cellRect, ate.IsAddrCount.ToString());
					}
					break;

				case AssetColumns.Total:
					{
						GUI.Label(cellRect, ate.totalCount.ToString());
					}
					break;

				case AssetColumns.Group:
					break;

				case AssetColumns.Labels:
					break;

				case AssetColumns.Buttons:
					{

						string QualifiedName = item.data.type.AssemblyQualifiedName;
						if (UMAAssetIndexer.Instance.IsAdditionalIndexedType(QualifiedName))
						{
							Rect firsthalf = new Rect(cellRect);
							firsthalf.width = cellRect.width / 2;
							Rect secondhalf = new Rect(cellRect);
							secondhalf.width = cellRect.width / 2;
							secondhalf.x = cellRect.x + (cellRect.width / 2);
							string removeText = "N";
						
							if (UMAAssetIndexer.Instance.isRemoveUnlabelledType(QualifiedName))
							{
								removeText = "Y";
							}

                            if (GUI.Button(firsthalf, $"Trim Unlabeled ({removeText})", EditorStyles.toolbarButton))
							{
								UMAAssetIndexer.Instance.toggleRemoveUnabelledType(QualifiedName);
                                owningWindow.Repaint();
                            }

                            if (GUI.Button(secondhalf, "Del Type", EditorStyles.toolbarButton))
							{
								UMAAssetIndexer.Instance.RemoveType(item.data.type);
								List<AssetTreeElement> RemoveMe = new List<AssetTreeElement>();
								RemoveMe.Add(item.data);
								this.treeModel.RemoveElements(RemoveMe);
							}
						}
						//string QualifiedName = sType.AssemblyQualifiedName;
						//if (!IsAdditionalIndexedType(QualifiedName)) return;
					}
					break;
			}
		}

		bool isRecipe(AssetItem ai)
		{
			if (ai._Type.IsSubclassOf(typeof(UMARecipeBase)))
			{
				return true; 
			}
			return false;
		}

		void CellGUI(Rect cellRect, TreeViewItem<AssetTreeElement> item, AssetColumns column, ref RowGUIArgs args)
		{
			// Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
			CenterRectUsingSingleLineHeight(ref cellRect);

			if (item.data.depth == 0)
			{
				HeaderCellGUI(cellRect, item, column, ref args);
				return;
			}

			AssetItem ai = item.data.ai;
			AssetTreeElement element = item.data;

			switch (column)
			{
				case AssetColumns.Selection:
					{


						// EditorGUI.Toggle(cellRect, item.data.ai._SerializedItem != null);
						bool newVal = EditorGUI.Toggle(cellRect, element.Checked);
						if (newVal != element.Checked)
						{
							element.Checked = newVal;
							RecalcTypeChecks(element.type);
						}
						if (pingIcon == null)
						{
                            InitIcons();
                        }

					}
				break;

				case AssetColumns.Actions:
					{
						Rect rect = cellRect;
						rect.width = 22;
						rect.height = 22;

						if (GUI.Button(rect, inspectIcon))
						{
							UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(ai._Path);
							InspectorUtlity.InspectTarget(o);
						}
                        rect.x += 30;
                        if (GUI.Button(rect, pingIcon))
                        {
                            UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(ai._Path);
                            EditorGUIUtility.PingObject(o);
                        }

                    }
                    break;

				case AssetColumns.Type:
				{
					EditorGUI.LabelField(cellRect, element.type.Name);
				}
				break;

				case AssetColumns.Always:
				{
					cellRect.x += kCheckboxOffset;
					cellRect.width -= kCheckboxOffset;
					bool clicked = EditorGUI.Toggle(cellRect, ai.IsAlwaysLoaded);
					if (clicked != ai.IsAlwaysLoaded)
					{
						ai.IsAlwaysLoaded = clicked;
						UMAAssetIndexer.Instance.ForceSave();
						owningWindow.RecountTypes();
						owningWindow.Repaint();
                    }
				}
				break;

                case AssetColumns.Ignore:
                    {
                        cellRect.x += kCheckboxOffset;
                        cellRect.width -= kCheckboxOffset;
                        bool clicked = EditorGUI.Toggle(cellRect, ai.Ignore);
                        if (clicked != ai.Ignore)
                        {
                            ai.Ignore = clicked;
                            UMAAssetIndexer.Instance.ForceSave();
                            owningWindow.RecountTypes();
                            owningWindow.Repaint();

                        }
                    }
                    break;

				case AssetColumns.Loaded:
					{
					cellRect.x += kCheckboxOffset;
					cellRect.width -= kCheckboxOffset;
					EditorGUI.Toggle(cellRect, ai.IsLoaded);
					}
				break;

			case AssetColumns.IsResource:
				{
					cellRect.x += kCheckboxOffset;
					cellRect.width -= kCheckboxOffset;
					EditorGUI.Toggle(cellRect, ai.IsResource);
				}
				break;

				case AssetColumns.Name:
				{
					// Do toggle
					Rect toggleRect = cellRect;
					toggleRect.x += GetContentIndent(item);
					toggleRect.width = kToggleWidth;
					//if (toggleRect.xMax < cellRect.xMax)
					//	item.data.enabled = EditorGUI.Toggle(toggleRect, item.data.enabled); // hide when outside cell rect

					// Default icon and label
					args.rowRect = cellRect;
					base.RowGUI(args);
				}
				break;

				case AssetColumns.IsAddressable:
				{
					cellRect.x += kCheckboxOffset;
					cellRect.width -= kCheckboxOffset;
					EditorGUI.Toggle(cellRect, ai.IsAddressable);
				}
				break;

                case AssetColumns.Total:
                    {
                        cellRect.x += kCheckboxOffset;
                        cellRect.width -= kCheckboxOffset;
                    }
                    break;

				case AssetColumns.Group:
					EditorGUI.LabelField(cellRect, ai.AddressableGroup);
					break;

				case AssetColumns.Labels:
				{
					if (!string.IsNullOrEmpty(ai.AddressableLabels))
					{
						Rect Button = new Rect(cellRect);
						Button.width = 32;
						Button.height -= 2;

						if (GUI.Button(Button,"View", EditorStyles.toolbarButton))
						{
							List<string> labels = new List<string>();
							labels.AddRange(ai.AddressableLabels.Split(';'));
							DisplayListWindow.ShowDialog("Addressable Labels", owningWindow.position, labels);
						}
						cellRect.x += 32;
						cellRect.width -= 32;
						EditorGUI.LabelField(cellRect, ai.AddressableLabels);
					}
				}
					break;

				case AssetColumns.Buttons:
				{
					float BtnWidth = (cellRect.width/3)- (kToggleWidth * 2);
					Rect ButtonRect = new Rect(cellRect);
					ButtonRect.width = BtnWidth;

					//if(GUI.Button(ButtonRect,"Inspect",EditorStyles.toolbarButton))
					//{
					//	UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(ai._Path);
					//	InspectorUtlity.InspectTarget(o);
					//}
					/*
					ButtonRect.x = ButtonRect.x + BtnWidth;
					if (item.data.ai._SerializedItem == null)
					{
						if(GUI.Button(ButtonRect, "Add Ref", EditorStyles.toolbarButton))
						{
							ai.CacheSerializedItem();
							Repaint();
						}
					}
					else
					{
						if(GUI.Button(ButtonRect, "Rmv Ref",EditorStyles.toolbarButton))
						{
							ai.ReleaseItem();
							Repaint();
						}
					}
					*/
#if UMA_ADDRESSABLES

					if (ai._Type == typeof(UMATextRecipe))
					{
						UMATextRecipe recipe = ai.Item as UMATextRecipe;

						if (owningWindow.LoadedLabels.Contains(recipe.AssignedLabel))
						{
							ButtonRect.x = ButtonRect.x + ButtonRect.width;
							ButtonRect.width = 110;

							if (GUI.Button(ButtonRect, "Update Groups", EditorStyles.toolbarButton))
							{
								UMAAddressablesEditorBridge.AddRecipeGroup(recipe);
								owningWindow.LoadedLabels.Add(recipe.AssignedLabel);
							}
						}
						else
						{
							ButtonRect.x = ButtonRect.x + ButtonRect.width;
							ButtonRect.width = 110;

							if (GUI.Button(ButtonRect, "Make Addressable", EditorStyles.toolbarButton))
							{
								UMAAddressablesEditorBridge.AddRecipeGroup(recipe);
								owningWindow.LoadedLabels.Add(recipe.AssignedLabel);
							}
						}
					}
#endif
					//ButtonRect.x = ButtonRect.x + ButtonRect.width;
					//ButtonRect.width = 32;
					//if (GUI.Button(ButtonRect,"Ping", EditorStyles.toolbarButton))
					//{
					//	UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(ai._Path);
					//	EditorGUIUtility.PingObject(o);
					//}

					ButtonRect.x = ButtonRect.x + 32;
					ButtonRect.width = kToggleWidth;
					if(GUI.Button(ButtonRect, "X",EditorStyles.toolbarButton))
					{
						// remove from index.
						// remove from tree.

						List<AssetTreeElement> RemoveMe = new List<AssetTreeElement>();
						UMAAssetIndexer.Instance.RemoveAsset(ai);
						RemoveMe.Add(item.data);
						this.treeModel.RemoveElements(RemoveMe);
						owningWindow.RecountTypes();
						RecalcTypeChecks(element.type);
						Repaint();
					}
				}
				break;
			}
		}
		private void SetAllChildren(Type type, bool newval)
		{
			foreach (AssetTreeElement ate in treeModel.root.children)
			{
				if (ate.type == type)
				{
					foreach (AssetTreeElement child in ate.children)
					{
						child.SetChecked(newval);
					}
					if (newval)
					{
						ate.SetAmountChecked(Amount.All);
					}
					else
					{
						ate.SetAmountChecked(Amount.None);
					}
				}
			}
			Repaint();
		}

		public void RecalcTypeChecks(Type type = null)
		{
			foreach(AssetTreeElement ate in treeModel.root.children)
			{
				if (type == null || ate.type == type)
				{
					int count = 0;
					int checkedcount = 0;
					if (ate.hasChildren)
					{
						foreach (AssetTreeElement child in ate.children)
						{
							count++;
							if (child.Checked)
							{
								checkedcount++;
							}

						}
					}
					if (checkedcount == 0)
					{
						ate.SetAmountChecked(Amount.None);
					}
					else if (checkedcount == count)
					{
						ate.SetAmountChecked(Amount.All);
					}
					else if (checkedcount < count)
					{
						ate.SetAmountChecked(Amount.Mixed);
					}
				}
			}
			Repaint();
		}

		protected override bool CanMultiSelect(TreeViewItem item)
		{
			return true;
		}

		public static void InitIcons()
		{
			if (pingIcon == null)
			{
				pingIcon = EditorGUIUtility.IconContent("d_Selectable Icon","|Select the item in the project view");
			}
			if (inspectIcon == null)
			{
                inspectIcon = EditorGUIUtility.IconContent("d_search_icon","|Inspect the item in a popup inspector");
            }
		}

		public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState(float treeViewWidth)
		{
			var columns = new[]
			{
				new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("X", "Selection Column"),
					headerTextAlignment = TextAlignment.Center,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Right,
					width = 30,
					minWidth = 30,
					maxWidth = 30,
					autoResize = false,
					allowToggleVisibility = true
				},
				new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Name"),
					headerTextAlignment = TextAlignment.Left,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Center,
					width = 160,
					minWidth = 130,
					autoResize = false,
					allowToggleVisibility = false
				},
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Action", "Actions"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Right,
                    width = 60,
                    minWidth = 60,
                    maxWidth = 60,
                    autoResize = false,
                    allowToggleVisibility = true
                },
                new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Type"),
					headerTextAlignment = TextAlignment.Left,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Center,
					width = 120,
					minWidth = 60,
					autoResize = false,
					allowToggleVisibility = false
				},
				new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Loaded", "Is the item loaded (has a cached SerializedObject reference)?"),
					headerTextAlignment = TextAlignment.Center,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Left,
					width = 50,
					minWidth = 50,
					maxWidth = 50,
					autoResize = true
				},
 				new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Resx", "Is this in resources?"),
					headerTextAlignment = TextAlignment.Center,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Left,
					width = 40,
					minWidth = 40,
					maxWidth = 40,
					autoResize = true
				}, 
				new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Adr", "Is this addressable?"),
					headerTextAlignment = TextAlignment.Center,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Left,
					width = 40,
					minWidth = 40,
					maxWidth = 40,
					autoResize = true
				},

				new MultiColumnHeaderState.Column
				{
                    headerContent = new GUIContent("Tot", "Just the count"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Left,
                    width = 40,
                    minWidth = 40,
                    maxWidth = 40,
                    autoResize = true
                },


                new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Group", "Addressable Group this is in (asset bundle)"),
					headerTextAlignment = TextAlignment.Left,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Left,
					width = 125,
					minWidth = 60,
					autoResize = true,
					allowToggleVisibility = true
				},
				new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Labels", "Addressable Labels"),
					headerTextAlignment = TextAlignment.Left,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Left,
					width = 125,
					minWidth = 60,
					autoResize = true,
					allowToggleVisibility = true
				},
				new MultiColumnHeaderState.Column
				{
                    headerContent = new GUIContent("Ignore", "This is not included in addressables or resources"),
					headerTextAlignment = TextAlignment.Center,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Left,
					width = 40,
					minWidth = 40,
					maxWidth = 40,
					autoResize = false,
					allowToggleVisibility = true
				},
                new MultiColumnHeaderState.Column
                {
					headerContent = new GUIContent("Keep", "This is always loaded"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Left,
                    width = 40,
                    minWidth = 40,
                    maxWidth = 40,
                    autoResize = false,
                    allowToggleVisibility = true
                },
                new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Commands", "Command Buttons"),
					headerTextAlignment = TextAlignment.Center,
					sortedAscending = true,
					sortingArrowAlignment = TextAlignment.Left,
					width = 280,
					minWidth = 280,
					maxWidth = 280,
					autoResize = false
				}
			};
			Assert.AreEqual(columns.Length, System.Enum.GetValues(typeof(AssetColumns)).Length, "Number of columns should match number of enum values: You probably forgot to update one of them.");

			var state = new MultiColumnHeaderState(columns);
			return state;
		}
	}
}
