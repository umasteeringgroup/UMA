using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UMA;

namespace UMAEditor
{
	[CustomEditor(typeof(UMAAssetIndex), true)]
	public class UMAAssetIndexEditor : Editor
	{
		private UMAAssetIndex UAI;
		private ReorderableList typesToIndexList;
		private ReorderableList typesToIndexListReal;

		private static Dictionary<string, bool> mainExpanded = new Dictionary<string, bool>()
		{
			{"typesToIndex", false },
			{"typesToIndexInfo", false },
			{"UMAAssetsInBuild", true },
			{"UMAAssetsInBuildInfo", false },
			{"UMAAssetBundleAssets", false },
			{"UMAAssetBundleAssetsInfo", false },
		};
		private static Dictionary<string, bool> expandedBuildTypes = new Dictionary<string, bool>();
		private static Dictionary<string, bool> expandedBundleTypes = new Dictionary<string, bool>();

		float vPadding = 2f;

		//this is the list that is actyally displayed and modified. When 'Update Indexed Types' is clicked the actual typesToIndex list is updated
		List<string> currentTypesToIndex = new List<string>();
		List<string> newTypesToIndex = new List<string>();

		private void OnEnable()
		{
			UAI = target as UMAAssetIndex;
			if(expandedBuildTypes.Count == 0 || expandedBundleTypes.Count == 0 || expandedBuildTypes.Count != UAI.FullIndex.data.Length || expandedBundleTypes.Count != UAI.AssetBundleIndex.data.Length)
			{
				UpdateExpandedTypesDicts();
			}
		}

		private void UpdateExpandedTypesDicts()
		{
			//sort out the Build dict
			var newExpandedBuildTypes = new Dictionary<string, bool>();
			for (int i = 0; i < UAI.FullIndex.data.Length; i++)
			{
				bool currentVal = false;
				if (expandedBuildTypes.ContainsKey(UAI.FullIndex.data[i].type))
					currentVal = expandedBuildTypes[UAI.FullIndex.data[i].type];
				newExpandedBuildTypes.Add(UAI.FullIndex.data[i].type, currentVal);
            }
			expandedBuildTypes = newExpandedBuildTypes;
			//sort out the bundle dict
			var newExpandedBundleTypes = new Dictionary<string, bool>();
			for (int i = 0; i < UAI.AssetBundleIndex.data.Length; i++)
			{
				bool currentVal = false;
				if (expandedBundleTypes.ContainsKey(UAI.AssetBundleIndex.data[i].type))
					currentVal = expandedBundleTypes[UAI.AssetBundleIndex.data[i].type];
				newExpandedBundleTypes.Add(UAI.AssetBundleIndex.data[i].type, currentVal);
			}
			expandedBundleTypes = newExpandedBundleTypes;
		}

		private void UpdateCurrentTypesToIndex()
		{
			var typesToIndex = serializedObject.FindProperty("typesToIndex");
			currentTypesToIndex.Clear();
			for (int i = 0; i < typesToIndex.arraySize; i++)
			{
				string thisVal = "";
				//typesToIndex.GetArrayElementAtIndex(i).stringValue seems to be a refrence but we need a NEW string
				thisVal = typesToIndex.GetArrayElementAtIndex(i).stringValue;
                currentTypesToIndex.Add(thisVal);
			}
			newTypesToIndex = new List<string>(currentTypesToIndex);
			UpdateExpandedTypesDicts();
        }

		private void InitTypesToIndexList()
		{
			if(currentTypesToIndex.Count == 0 || newTypesToIndex.Count == 0)
			{
				UpdateCurrentTypesToIndex();
            }
			typesToIndexList = new ReorderableList(newTypesToIndex, typeof(string), false, true, true, true);
			typesToIndexList.drawHeaderCallback = (Rect rect) => {
				EditorGUI.LabelField(rect, "Types To Index");
			};
			typesToIndexList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => {
				//var element = typesToIndexList.serializedProperty.GetArrayElementAtIndex(index);
				//var element = (string)typesToIndexList.list[index];
				rect.y += 2;
				typesToIndexList.list[index] = EditorGUI.TextField(new Rect(rect.x + 10, rect.y, rect.width - 10, EditorGUIUtility.singleLineHeight), (string)typesToIndexList.list[index]);
			};
			//for some bloody reason ReorderableList doesn't understand that List<string> contains strings and tries to use Activator to add new elements
			typesToIndexList.onAddCallback = (ReorderableList list) =>
			{
				list.index = list.list.Add(string.Empty);
			};
		}
		private string GetTypeNameWithoutAssembly(string fullType)
		{
			string typeWithoutAssembly = fullType;
			if (fullType.IndexOf(".") > -1)
			{
				//will need to do regex on this and get the last match...
				typeWithoutAssembly = Regex.Match(fullType, "[^.]+$").Value;
			}
			return typeWithoutAssembly;
		}
		private Type GetTypeByName(string name)
		{
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (Type type in assembly.GetTypes())
				{
					if (type.Name == name)
						return type;
				}
			}

			return null;
		}
		//doing this here is too slow- do it when we modify the index in any way
		private int CompareByFolderName(UMAAssetIndexData.IndexData obj1, UMAAssetIndexData.IndexData obj2)
		{
			if (obj1 == null)
			{
				if (obj2 == null) return 0;
				else return -1;
			}
			else
			{
				if (obj2 == null)
				{
					return 1;
				}
				else
				{
					string folder1 = System.IO.Path.GetDirectoryName(obj1.fullPath);
					string folder2 = System.IO.Path.GetDirectoryName(obj2.fullPath);
					int folderCompare = String.Compare(folder1, folder2);
					if (folderCompare != 0) return folderCompare;
					string file1 = System.IO.Path.GetFileName(obj1.fullPath);
					string file2 = System.IO.Path.GetFileName(obj2.fullPath);
					int fileCompare = String.Compare(file1, file2);
					return fileCompare;
				}
			}
		}
		
		int selectedFilter = 0;
		string[] filterOptions = new string[] { "All", "Enabled", "Disabled", "AssetName or Path", "UMAName" };
		string stringFilter = "";
		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			bool changed = false;

			//GUI STYLES
			GUIStyle infoPara = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
			infoPara.margin = new RectOffset(infoPara.margin.left, infoPara.margin.right, 0, 0);
			GUIStyle infoParaSoft = new GUIStyle(infoPara);
			infoParaSoft.normal.textColor = new Color(infoParaSoft.normal.textColor.r, infoParaSoft.normal.textColor.g, infoParaSoft.normal.textColor.b, 0.5f);
            GUIStyle boldFoldout = new GUIStyle(EditorStyles.foldout);
			boldFoldout.fontStyle = FontStyle.Bold;
			var miniInfoBut = new GUIStyle(EditorStyles.miniButton);
			miniInfoBut.contentOffset = new Vector2(0f, 0f);
			miniInfoBut.fontStyle = FontStyle.Bold;
			var currentTint = GUI.color;
			var currenTextTint = GUI.contentColor;
			//--TYPES TO INDEX--//
			mainExpanded["typesToIndex"] = EditorGUILayout.Foldout(mainExpanded["typesToIndex"], new GUIContent("Types To Index", serializedObject.FindProperty("typesToIndex").tooltip), boldFoldout);
			if (mainExpanded["typesToIndex"])
			{
				if (typesToIndexList == null)
				{
					InitTypesToIndexList();
				}
				//EditorGUILayout.PropertyField(serializedObject.FindProperty("typesToIndex"),true);
				typesToIndexList.DoLayoutList();
				if(GUILayout.Button("Update Indexed Types"))
				{
                    UAI.UpdateIndexedTypes(newTypesToIndex);
					serializedObject.Update();
					UpdateCurrentTypesToIndex();
					InitTypesToIndexList();
					EditorGUIUtility.keyboardControl = 0;
					changed = true;
                }
			}
			EditorGUILayout.Space();
			//--FULL UMA ASSETS LIST--//
			mainExpanded["UMAAssetsInBuild"] = EditorGUILayout.Foldout(mainExpanded["UMAAssetsInBuild"], new GUIContent("UMA Assets in Build"), boldFoldout);
			if (mainExpanded["UMAAssetsInBuild"])
			{
				//INFO
				EditorGUI.indentLevel++;
				mainExpanded["UMAAssetsInBuildInfo"] = EditorGUILayout.Foldout(mainExpanded["UMAAssetsInBuildInfo"], "info");
				EditorGUI.indentLevel--;
				if (mainExpanded["UMAAssetsInBuildInfo"])
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space(EditorGUI.indentLevel * 20f);
					GUIHelper.BeginVerticalPadded(5f, new Color32(228, 228, 228, 249));
					//This is a list of all the assets in the project as filtered by the typesToIndex List. This does NOT include assets in AssetBundles
					EditorGUILayout.TextArea("This is a list of all the assets in the project as filtered by the typesToIndex List. This does NOT include assets in AssetBundles", infoPara);
					//Toggle the checkbox next to the asset name, to include it in your build and make it accessible to the Dynamic Libraries
					EditorGUILayout.TextArea("Toggle the checkbox next to the asset name, to include it in your build and make it accessible to the Dynamic Libraries", infoPara);
					//The displayed AssetNames are the slot/overlay/racenames (for UMA Assets) or the asset name (for UMAtextRecipes or other asset types)
					EditorGUILayout.TextArea("The displayed AssetNames are the slot/overlay/racenames (for UMA Assets) or the asset name (for UMAtextRecipes or other asset types)", infoPara);
					//Hover Asset names for the asset path in the project
					EditorGUILayout.TextArea("Hover Asset names for the asset path in the project", infoPara);
					//Click the asset name to highlight the asset in the project
					EditorGUILayout.TextArea("Click the asset name to highlight the asset in the project", infoPara);
					GUIHelper.EndVerticalPadded(5f);
					GUILayout.EndHorizontal();
					EditorGUILayout.Space();
				}
                for (int ti = 0; ti < UAI.FullIndex.data.Length; ti++)
				{
					EditorGUI.indentLevel++;
					GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
					var currentTypeName = GetTypeNameWithoutAssembly(UAI.FullIndex.data[ti].type);
                    var label = currentTypeName;
					label += "  [Total:" + UAI.FullIndex.CountType(UAI.FullIndex.data[ti].type) + " Live:" + UAI.BuildIndex.CountType(UAI.FullIndex.data[ti].type) + "]";
					if (!expandedBuildTypes.ContainsKey(UAI.FullIndex.data[ti].type))
						expandedBuildTypes.Add(UAI.FullIndex.data[ti].type, false);
					expandedBuildTypes[UAI.FullIndex.data[ti].type] = EditorGUILayout.Foldout(expandedBuildTypes[UAI.FullIndex.data[ti].type], label);
					GUILayout.EndHorizontal();
					if (expandedBuildTypes[UAI.FullIndex.data[ti].type])
					{
						//Draw the search Filter area
						var searchRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + vPadding);
						var searchLabelRect = new Rect(searchRect.x, searchRect.y + (vPadding / 2), searchRect.width, searchRect.height - vPadding);
						var searchPopupRect = new Rect(searchRect.x, searchRect.y + (vPadding / 2), searchRect.width, searchRect.height - vPadding);
						var searchFieldRect = new Rect(searchRect.x, searchRect.y + (vPadding / 2), searchRect.width, searchRect.height - vPadding);
						searchLabelRect.width = 60f;
						searchPopupRect.xMin = searchLabelRect.xMax;
						searchPopupRect.width = 140f;
						searchFieldRect.xMin = searchFieldRect.xMin + 80f + 120f;
						searchFieldRect.width = searchRect.width - 80f - 120f;
						//draw it
						EditorGUI.LabelField(searchLabelRect, "Search");
						selectedFilter = EditorGUI.Popup(searchPopupRect, selectedFilter, filterOptions);
						if (selectedFilter > 2)
							stringFilter = EditorGUI.TextField(searchFieldRect, stringFilter);
						//
						GUIHelper.BeginVerticalPadded(1f, new Color32(228, 228, 228, 150));
						//info for the type we are inspecting
						string typeString = UAI.FullIndex.data[ti].type;
						Type thisType = Type.GetType(UAI.FullIndex.data[ti].type, false, true);
						if (thisType == null)
						{
							typeString = GetTypeNameWithoutAssembly(UAI.FullIndex.data[ti].type);
							thisType = GetTypeByName(typeString);
							//if its still null show a warning
							if (thisType == null)
								Debug.LogWarning("[UMAAssetIndexEditor] Could not determine the System Type for the given type name " + UAI.FullIndex.data[ti].type);
						}
						if (thisType == null)
						{
							EditorGUILayout.HelpBox("[UMAAssetIndexEditor] Could not determine the System Type for the given type name " + UAI.FullIndex.data[ti].type, MessageType.Warning);
						}
						else
						{
							//draw the entries
							string lastFolder = "";
							List<string> uniqueNames = new List<string>();
							//Id really like to sort this array by path but cant work out how to do it
							//Array.Sort(UAI.FullIndex.data[ti].typeIndex, CompareByFolderName);
                            for (int i = 0; i < UAI.FullIndex.data[ti].typeIndex.Length; i++)
							{
								bool assetIsLive = false;
								//entry in the fullIndex
								var entry = UAI.FullIndex.data[ti].typeIndex[i];
								//check its not a duplicate
								bool isDuplicate = false;
								if (!uniqueNames.Contains(entry.name))
									uniqueNames.Add(entry.name);
								else
									isDuplicate = true;
								//This is the liveEntry that is actually serialized (if the asset is live)
								var liveAsset = UAI.LoadAssetAtPath(entry.fullPath);
								if (liveAsset != null)
									assetIsLive = true;

								//deal with filters
								if (selectedFilter == 1 && !assetIsLive)//filter for enabled
									continue;
								else if (selectedFilter == 2 && assetIsLive)//filter for disabled
									continue;
								else if (selectedFilter == 3 && stringFilter.Length >= 3 && entry.fullPath.IndexOf(stringFilter) == -1)
									continue;
								else if (selectedFilter == 4 && stringFilter.Length >= 3 && entry.name.IndexOf(stringFilter) == -1)
									continue;
								//otherwise we are good to go
								if (lastFolder != Path.GetDirectoryName(UAI.FullIndex.data[ti].typeIndex[i].fullPath))
								{
									EditorGUILayout.TextArea(Path.GetDirectoryName(UAI.FullIndex.data[ti].typeIndex[i].fullPath), infoParaSoft);
									lastFolder = Path.GetDirectoryName(UAI.FullIndex.data[ti].typeIndex[i].fullPath);
								}
								var itemRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + vPadding);
								var thisToggleRect = new Rect(itemRect.x, itemRect.y + (vPadding / 2), itemRect.width, itemRect.height - vPadding);
								var thisNameRect = new Rect(itemRect.x, itemRect.y + (vPadding / 2), itemRect.width, itemRect.height - vPadding);
								var thisFieldRect = new Rect(itemRect.x, itemRect.y + (vPadding / 2), itemRect.width, itemRect.height - vPadding);
								Rect thisDupeRect = new Rect();
								//toggeleRect
								thisToggleRect.width = 30f;
								if (isDuplicate)
								{
									thisDupeRect = new Rect(thisToggleRect);
									thisDupeRect.xMax = thisDupeRect.xMax + 25f;
                                    thisDupeRect.xMin = thisToggleRect.xMax + 5f;
									//nameRect
									thisNameRect.xMin = (thisToggleRect.width + thisDupeRect.width) + 5f;
									thisNameRect.width = ((itemRect.width - 30f - 25f) / 2f) - 5f -5f;
									//fieldRect
									thisFieldRect.xMin = thisToggleRect.width + thisDupeRect.width + thisNameRect.width;
									thisFieldRect.width = ((itemRect.width - 30f - 20f) / 2f);
								}
								else
								{
									//nameRect
									thisNameRect.xMin = thisToggleRect.width + 5f;
									thisNameRect.width = ((itemRect.width - 30f) / 2f) - 5f;
									//fieldRect
									thisFieldRect.xMin = thisToggleRect.width + thisNameRect.width;
									thisFieldRect.width = ((itemRect.width - 30f) / 2f) - 5f;
								}
								if (liveAsset != null)
									EditorGUI.DrawRect(itemRect, new Color(0.75f, 0.875f, 1f));
								else
									EditorGUI.DrawRect(itemRect, new Color(0.75f, 0.875f, 1f, 0.45f));

								if (entry.fullPath.IndexOf("/Resources/") > -1)
								{
									GUI.color = new Color32(161, 187, 220,255);
									GUI.contentColor = Color.white;
									thisToggleRect.xMin = thisToggleRect.xMin + 11f;
									thisToggleRect.width = 18f;
									GUILayout.BeginHorizontal();
									GUILayout.Space(EditorGUI.indentLevel * 25);
									GUI.Box(thisToggleRect, new GUIContent("R","This Asset is live because it is in a Resources folder. To remove it from your build you need to remove it from its Resources folder. Click the asset name to revel in project."), miniInfoBut);
									GUILayout.EndHorizontal();
									GUI.color = currentTint;
									GUI.contentColor = currenTextTint;
								}
								else
								{
									EditorGUI.BeginChangeCheck();
									var assetLiveStatus = EditorGUI.Toggle(thisToggleRect, assetIsLive);
									if (EditorGUI.EndChangeCheck())
									{
										if (assetLiveStatus != assetIsLive)
										{
											if (assetLiveStatus == true)
											{
												assetIsLive = true;
												UAI.MakeAssetLive(entry, typeString);
											}
											else
											{
												assetIsLive = false;
												UAI.MakeAssetNotLive(entry, typeString);
											}
											//serializedObject.Update();
											serializedObject.ApplyModifiedProperties();
										}
										changed = true;
                                    }
								}
								if (isDuplicate)
								{
									GUI.color = Color.yellow;
									var nameToChange = currentTypeName.IndexOf("Slot") > -1 ? "slot" : currentTypeName.IndexOf("Overlay") > -1 ? "overlay" : currentTypeName.IndexOf("RaceData") > -1 ? "race" : "asset ";
                                    GUI.Box(thisDupeRect, new GUIContent("!", "There is another "+ currentTypeName + " asset in your project with the name "+ entry.name +". This one wont be loaded. You need to change the "+nameToChange+"name of one of these assets." ), miniInfoBut);
									GUI.color = currentTint;
								}
								//GUI doesn't include Indent level so add it
								thisNameRect.xMin = thisNameRect.xMin + (EditorGUI.indentLevel * 20);
								var nameLabel = entry.name;
								//if (isDuplicate)
								//	nameLabel += " [DUPLICATE]";
								if (GUI.Button(thisNameRect, new GUIContent(nameLabel, entry.fullPath), EditorStyles.label))
								{
									EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(entry.fullPath));
								}
								//we dont allow users to drop the asset in here, this is just to show it IS referenced (I might hide this)
								EditorGUI.BeginDisabledGroup(true);
								EditorGUI.ObjectField(thisFieldRect, liveAsset, thisType, false);
								EditorGUI.EndDisabledGroup();
							}
						}
						GUIHelper.EndVerticalPadded(1f);
					}
					EditorGUI.indentLevel--;
					GUILayout.Space(vPadding);
				}
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("ReIndex"))
				{
					UAI.ClearAndReIndex();
				}
				if (GUILayout.Button("Clear and ReIndex"))
				{
					if (EditorUtility.DisplayDialog("Really Clear Indexes?", "This will also clear any assets you made live manually by checking its checkbox. Continue?", "Ok", "Cancel"))
						UAI.ClearAndReIndex(true);
				}
				GUILayout.EndHorizontal();
			}
			EditorGUILayout.Space();
			//--ASSET BUNDLE ASSETS LIST--//
			mainExpanded["UMAAssetBundleAssets"] = EditorGUILayout.Foldout(mainExpanded["UMAAssetBundleAssets"], new GUIContent("UMA Asset Bundle Assets"), boldFoldout);
			if (mainExpanded["UMAAssetBundleAssets"])
			{
				//INFO
				EditorGUILayout.HelpBox("ASSET BUNDLE ASSETS ARE NOT INCLUDED IN YOUR BUILD.", MessageType.Warning);
				EditorGUI.indentLevel++;
				mainExpanded["UMAAssetBundleAssetsInfo"] = EditorGUILayout.Foldout(mainExpanded["UMAAssetBundleAssetsInfo"], "info");
				EditorGUI.indentLevel--;
				if (mainExpanded["UMAAssetBundleAssetsInfo"])
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space(EditorGUI.indentLevel * 20f);
					GUIHelper.BeginVerticalPadded(5f, new Color32(228, 228, 228, 249));
					//This is a list of all the UMA assets in your AssetBundles as filtered by the typesToIndex List
					EditorGUILayout.TextArea("This is a list of all the UMA assets in your AssetBundles as filtered by the typesToIndex List", infoPara);
					//Asset Bundle Assets are NOT included in your build. You build them as seperate packages that you download into your game using Assets/AssetBundles/Build AssetBundles
					EditorGUILayout.TextArea("You build your asset bundles as seperate packages that you download into your game using Assets/AssetBundles/Build AssetBundles", infoPara);
					//You cannot add or disable these from here (yet). You need to do this in the Project window, by assigning or un-assigning the folders/assets to an asset bundle.
					EditorGUILayout.TextArea("You cannot add or disable these from here (yet). You need to do this in the Project window, by assigning or un-assigning the folders/assets to an asset bundle.", infoPara);
					//Click the asset name to highlight the asset in the project
					EditorGUILayout.TextArea("Click the asset name to highlight the asset in the project", infoPara);
					GUIHelper.EndVerticalPadded(5f);
					GUILayout.EndHorizontal();
					EditorGUILayout.Space();
				}
				for (int ti = 0; ti < UAI.AssetBundleIndex.data.Length; ti++)
				{
					EditorGUI.indentLevel++;
					GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
					var label = GetTypeNameWithoutAssembly(UAI.AssetBundleIndex.data[ti].type);
					label += "  [Total:" + UAI.AssetBundleIndex.CountType(UAI.AssetBundleIndex.data[ti].type) + "]";
					expandedBundleTypes[UAI.AssetBundleIndex.data[ti].type] = EditorGUILayout.Foldout(expandedBundleTypes[UAI.AssetBundleIndex.data[ti].type], label);
					GUILayout.EndHorizontal();
					if (expandedBundleTypes[UAI.AssetBundleIndex.data[ti].type])
					{
						string typeString = UAI.AssetBundleIndex.data[ti].type;
						Type thisType = Type.GetType(UAI.AssetBundleIndex.data[ti].type, false, true);
						if (thisType == null)
						{
							typeString = GetTypeNameWithoutAssembly(UAI.AssetBundleIndex.data[ti].type);
							thisType = GetTypeByName(typeString);
							//if its still null show a warning
							if (thisType == null)
								Debug.LogWarning("[UMAAssetIndexEditor] Could not determine the System Type for the given type name " + UAI.AssetBundleIndex.data[ti].type);
						}
						string lastFolder = "";
						//Array.Sort(UAI.AssetBundleIndex.data[ti].typeIndex, CompareByFolderName);
						for (int i = 0; i < UAI.AssetBundleIndex.data[ti].typeIndex.Length; i++)
						{
							if (lastFolder != Path.GetDirectoryName(UAI.AssetBundleIndex.data[ti].typeIndex[i].fullPath))
							{
								EditorGUILayout.TextArea(Path.GetDirectoryName(UAI.AssetBundleIndex.data[ti].typeIndex[i].fullPath), infoParaSoft);
								lastFolder = Path.GetDirectoryName(UAI.AssetBundleIndex.data[ti].typeIndex[i].fullPath);
							}
							var itemRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + vPadding);
							EditorGUI.DrawRect(itemRect, new Color(0.75f, 0.875f, 1));
							//entry in the fullIndex
							var entry = UAI.AssetBundleIndex.data[ti].typeIndex[i];
							var thisNameRect = new Rect(itemRect.x, itemRect.y + (vPadding / 2), itemRect.width, itemRect.height - vPadding);
							//GUI doesn't include Indent level so add it
							thisNameRect.xMin = thisNameRect.xMin + (EditorGUI.indentLevel * 20);
							if (GUI.Button(thisNameRect, new GUIContent(entry.name, entry.fullPath), EditorStyles.label))
							{
								EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(entry.fullPath));
							}
						}
					}
					EditorGUI.indentLevel--;
					GUILayout.Space(vPadding);
				}
			}
			serializedObject.ApplyModifiedProperties();
			if (changed)
			{
				//EditorUtility.SetDirty(target);
				//AssetDatabase.SaveAssets();
				EditorApplication.update -= SaveOnUpdate;
				EditorApplication.update += SaveOnUpdate;
			}

			EditorGUILayout.PropertyField(serializedObject.FindProperty("_buildIndex"),true);
		}
		
		public void SaveOnUpdate()
		{
			EditorApplication.update -= SaveOnUpdate;
			//EditorApplication.update += DoDeleteAsset;
			EditorUtility.SetDirty(target);
			AssetDatabase.SaveAssets();
		}
	}
}
