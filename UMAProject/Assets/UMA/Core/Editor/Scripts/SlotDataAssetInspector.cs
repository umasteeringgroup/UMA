#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace UMA.Editors
{
	[CustomEditor(typeof(SlotDataAsset))]
	[CanEditMultipleObjects]
	public class SlotDataAssetInspector : Editor
	{
		static string[] RegularSlotFields = new string[] { "slotName", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy","tags","isWildCardSlot","Races","smooshOffset", "smooshExpand"};
		static string[] WildcardSlotFields = new string[] { "slotName", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "tags", "isWildCardSlot", "Races", "_rendererAsset", "maxLOD", "useAtlasOverlay", "overlayScale", "animatedBoneNames", "_slotDNA", "meshData", "subMeshIndex", };
		SerializedProperty slotName;
		SerializedProperty CharacterBegun;
		SerializedProperty SlotAtlassed;
		SerializedProperty SlotProcessed;
        SerializedProperty SlotBeginProcessing;
        SerializedProperty DNAApplied;
		SerializedProperty CharacterCompleted;
		SerializedProperty MaxLOD;
		SerializedProperty isClippingPlane;
        SerializedProperty smooshOffset;
        SerializedProperty smooshExpand;
		SlotDataAsset slot;
		SlotDataAsset.Welding lastWeld = null;
        SlotDataAsset WeldToSlot = null;

        bool CopyNormals;
		bool CopyBoneWeights;
		bool AverageNormals;
		private int selectedRaceIndex = -1;
		private List<RaceData> foundRaces = new List<RaceData>();
		private List<string> foundRaceNames = new List<string>();

		[MenuItem("Assets/Create/UMA/Core/Custom Slot Asset")]
        public static void CreateCustomSlotAssetMenuItem()
        {
        	CustomAssetUtility.CreateAsset<SlotDataAsset>("", true, "Custom");
        }

		[MenuItem("Assets/Create/UMA/Core/Wildcard Slot Asset")]
		public static void CreateWildcardSlotAssetMenuItem()
		{
			SlotDataAsset wildcard = CustomAssetUtility.CreateAsset<SlotDataAsset>("", true, "Wildcard",true);
			wildcard.isWildCardSlot = true;
			wildcard.slotName = "WildCard";
			EditorUtility.SetDirty(wildcard);
			string path = AssetDatabase.GetAssetPath(wildcard.GetInstanceID());
			AssetDatabase.ImportAsset(path);
			EditorUtility.DisplayDialog("UMA", "Wildcard slot created. You should first change the SlotName in the inspector, and then add it to the global library or to a scene library", "OK");
		}

		private void OnDestroy()
		{

		}

		void OnEnable()
		{
			slotName = serializedObject.FindProperty("slotName");
			CharacterBegun = serializedObject.FindProperty("CharacterBegun");
			SlotAtlassed = serializedObject.FindProperty("SlotAtlassed");
			DNAApplied = serializedObject.FindProperty("DNAApplied");
			SlotProcessed = serializedObject.FindProperty("SlotProcessed");
			SlotBeginProcessing = serializedObject.FindProperty("SlotBeginProcessing");
			CharacterCompleted = serializedObject.FindProperty("CharacterCompleted");
			MaxLOD = serializedObject.FindProperty("maxLOD");
            isClippingPlane = serializedObject.FindProperty("isClippingPlane");	
            smooshExpand = serializedObject.FindProperty("smooshExpand");
            smooshOffset = serializedObject.FindProperty("smooshOffset");
			slot = (target as SlotDataAsset);
			SetRaceLists();

			InitTagList(slot);
		}

		private void InitTagList(SlotDataAsset _slotDataAsset)
		{
			
			var HideTagsProperty = serializedObject.FindProperty("tags");
			slot.tagList = new ReorderableList(serializedObject, HideTagsProperty, true, true, true, true);
			slot.tagList.drawHeaderCallback = (Rect rect) => 
			{
				if (_slotDataAsset.isWildCardSlot)
				{
					EditorGUI.LabelField(rect, "Match the following tags:");
				}
				else
				{
					EditorGUI.LabelField(rect, "Tags");
				}
			};
			slot.tagList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => 
			{
				var element = (target as SlotDataAsset).tagList.serializedProperty.GetArrayElementAtIndex(index);
				rect.y += 2;
				element.stringValue = EditorGUI.TextField(new Rect(rect.x + 10, rect.y, rect.width - 10, EditorGUIUtility.singleLineHeight), element.stringValue);
			};
		}

		public void SetRaceLists()
		{
			UMAContextBase ubc = UMAContext.Instance;
			if (ubc != null)
			{
				RaceData[] raceDataArray = ubc.GetAllRaces();
				foundRaces.Clear();
				foundRaceNames.Clear();
				foundRaces.Add(null);
				foundRaceNames.Add("None Set");
				foreach (RaceData race in raceDataArray)
				{
					if (race != null && race.raceName != "RaceDataPlaceholder")
					{
						foundRaces.Add(race);
						foundRaceNames.Add(race.raceName);
					}
				}
			}
		}

		private void UpdateSourceAsset(SlotDataAsset sda)
		{
			if (sda != null)
			{
				lastWeld = slot.CalculateWelds(sda, CopyNormals, CopyBoneWeights, AverageNormals);
			}
		}

		public override void OnInspectorGUI()
        {
			if (slot == null)
			{
				OnEnable();
			}
            bool forceUpdate = false;
			SlotDataAsset targetAsset = target as SlotDataAsset;
			serializedObject.Update();

			EditorGUI.BeginChangeCheck();
			GUILayout.BeginHorizontal();
			EditorGUILayout.DelayedTextField(slotName);
			if (GUILayout.Button("Use Obj Name",GUILayout.Width(90)))
            {
				foreach (var t in targets)
				{
					var slotDataAsset = t as SlotDataAsset;
					slotDataAsset.slotName = slotDataAsset.name;
					EditorUtility.SetDirty(slotDataAsset);
					GUI.changed = true;
				}
			}
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Validate"))
			{
			    foreach (var t in targets)
				{
                    var slotDataAsset = t as SlotDataAsset;
                    if (slotDataAsset != null)
					{
                        slotDataAsset.ValidateMeshData();
                    }
                }
            }
			if (GUILayout.Button("Clear Errors"))
			{
				foreach (var t in targets)
				{
                    var slotDataAsset = t as SlotDataAsset;
                    if (slotDataAsset != null)
					{
                        slotDataAsset.Errors = "";
                        EditorUtility.SetDirty(slotDataAsset);
                    }
                }
			}
			GUILayout.EndHorizontal();
			if (!string.IsNullOrEmpty(targetAsset.Errors))
			{
                EditorGUILayout.HelpBox($"Errors: {targetAsset.Errors}", MessageType.Error);
            }
			if ((target as SlotDataAsset).isWildCardSlot)
			{
				EditorGUILayout.HelpBox("This is a wildcard slot", MessageType.Info);
			}
			 
			EditorGUILayout.LabelField($"UtilitySlot: " + targetAsset.isUtilitySlot);
			 
			if (slot.isWildCardSlot)
            {
                Editor.DrawPropertiesExcluding(serializedObject, WildcardSlotFields);
            }
            else
            {
                Editor.DrawPropertiesExcluding(serializedObject, RegularSlotFields);
            }

            GUILayout.Space(10);
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.HelpBox("Smooshing is a feature that conforms one slot to another using a clipping plane. Smoosh Offset is used to adjust the offset of the conforming vertexes to help assist conforming and fitting. Smoosh Expand expands scales the vertexes. ", MessageType.Info);

            var currentTarget = target as SlotDataAsset;

            forceUpdate = EditorGUI.EndChangeCheck();
            EditorGUILayout.PropertyField(smooshOffset);
            EditorGUILayout.PropertyField(smooshExpand);

            //GUILayout.BeginHorizontal();
            /*GUILayout.Label("Smoosh Offset", GUILayout.Width(100));
            currentTarget.smooshOffset.x = EditorGUILayout.DelayedFloatField("X", currentTarget.smooshOffset.x, GUILayout.Width(50),GUILayout.ExpandWidth(false));
            currentTarget.smooshOffset.y = EditorGUILayout.DelayedFloatField("Y", currentTarget.smooshOffset.y, GUILayout.Width(50),GUILayout.ExpandWidth(false));
            currentTarget.smooshOffset.z = EditorGUILayout.DelayedFloatField("Z", currentTarget.smooshOffset.z, GUILayout.Width(50),GUILayout.ExpandWidth(false));
           // GUILayout.EndHorizontal();
           // GUILayout.BeginHorizontal();
            GUILayout.Label("Smoosh Expand", GUILayout.Width(100));
            currentTarget.smooshOffset.x = EditorGUILayout.DelayedFloatField("X", currentTarget.smooshOffset.x, GUILayout.Width(50),GUILayout.ExpandWidth(false));
            currentTarget.smooshOffset.y = EditorGUILayout.DelayedFloatField("Y", currentTarget.smooshOffset.y, GUILayout.Width(50),GUILayout.ExpandWidth(false));
            currentTarget.smooshOffset.z = EditorGUILayout.DelayedFloatField("Z", currentTarget.smooshOffset.z, GUILayout.Width(50),GUILayout.ExpandWidth(false));
            //GUILayout.EndHorizontal(); */
            if (GUILayout.Button("Save and Test Smoosh"))
            {
                UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
                string path = AssetDatabase.GetAssetPath(target.GetInstanceID());
                AssetDatabase.ImportAsset(path);
                forceUpdate = true;
            }
            EditorGUI.BeginChangeCheck();
           // EditorGUILayout.PropertyField(smooshOffset);
            //EditorGUILayout.PropertyField(smooshExpand);
            GUIHelper.EndVerticalPadded(10);
			GUILayout.Space(10);
			slot.tagList.DoLayoutList();


			(target as SlotDataAsset).eventsFoldout = EditorGUILayout.Foldout((target as SlotDataAsset).eventsFoldout, "Slot Events");
			if ((target as SlotDataAsset).eventsFoldout)
			{
				EditorGUILayout.PropertyField(CharacterBegun);   
				if (!slot.isWildCardSlot)
				{
					EditorGUILayout.PropertyField(SlotAtlassed);
					EditorGUILayout.PropertyField(DNAApplied);
                    EditorGUILayout.PropertyField(SlotBeginProcessing);
                    EditorGUILayout.PropertyField(SlotProcessed);
				}
				EditorGUILayout.PropertyField(CharacterCompleted);
			}


			foreach (var t in targets)
			{
				var slotDataAsset = t as SlotDataAsset;
				if (slotDataAsset != null)
				{
					if (slotDataAsset.animatedBoneHashes.Length != slotDataAsset.animatedBoneNames.Length)
					{
						slotDataAsset.animatedBoneHashes = new int[slotDataAsset.animatedBoneNames.Length];
						for (int i = 0; i < slotDataAsset.animatedBoneNames.Length; i++)
						{
							slotDataAsset.animatedBoneHashes[i] = UMASkeleton.StringToHash(slotDataAsset.animatedBoneNames[i]);
						}
						GUI.changed = true;
						EditorUtility.SetDirty(slotDataAsset);
					}
				}
			}

			if (!slot.isWildCardSlot)
			{
				GUILayout.Space(20);
				Rect updateDropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
				GUI.Box(updateDropArea, "Drag SkinnedMeshRenderers here to update the slot meshData.");
				GUILayout.Space(10);
				UpdateSlotDropAreaGUI(updateDropArea);

				GUILayout.Space(10);
				Rect boneDropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
				GUI.Box(boneDropArea, "Drag Bone Transforms here to add their names to the Animated Bone Names.\nSo the power tools will preserve them!");
				GUILayout.Space(10);
				AnimatedBoneDropAreaGUI(boneDropArea);
			}

			serializedObject.ApplyModifiedProperties();

            #region WELDS


			selectedRaceIndex = EditorGUILayout.Popup("Select Base Slot by Race", selectedRaceIndex, foundRaceNames.ToArray());
			if (selectedRaceIndex <= 0)
			{
				EditorGUILayout.HelpBox("Select a slot by race quickly, or use manual selection below", MessageType.Info);
			}
			else
			{ 
				UMAData.UMARecipe baseRecipe = new UMAData.UMARecipe();
				foundRaces[selectedRaceIndex].baseRaceRecipe.Load(baseRecipe, UMAContextBase.Instance);

				foreach (SlotData sd in baseRecipe.slotDataList)
				{
					if (sd != null && sd.asset != null)
					{
						if (GUILayout.Button(string.Format("{0} ({1})", sd.asset.name, sd.slotName)))
						{
							// UpdateSourceAsset(sd.asset);
							WeldToSlot = sd.asset;
						}
					}
				}
			}

			GUILayout.Space(12);



			WeldToSlot = EditorGUILayout.ObjectField("Drop slot here to create weld",WeldToSlot, typeof(SlotDataAsset),false) as SlotDataAsset;


			CopyBoneWeights = EditorGUILayout.Toggle("Copy Boneweights", CopyBoneWeights);
			CopyNormals = EditorGUILayout.Toggle("Copy Normals", CopyNormals);
			AverageNormals = EditorGUILayout.Toggle("Average Normals", AverageNormals);
			GUILayout.Box("Warning! averaging normals will update both slots!",GUILayout.ExpandWidth(true));

			if (WeldToSlot == null)
			{
				EditorGUI.BeginDisabledGroup(true);
			}
			if (GUILayout.Button("Perform Weld"))
			{
                lastWeld = slot.CalculateWelds(WeldToSlot, CopyNormals, CopyBoneWeights,AverageNormals);
                forceUpdate = true;
            }
            if (WeldToSlot == null)
            {
				EditorGUI.EndDisabledGroup();
            }

			int lastWeldCount = 0;
			int lastWeldMismatch = 0;
			if (lastWeld != null) 
			{
				lastWeldCount = lastWeld.WeldPoints.Count;
				lastWeldMismatch = lastWeld.MisMatchCount;
			}

            if (lastWeld != null)
            {
            GUILayout.Label($"Last Weld: {lastWeldCount} points, {lastWeld.MisMatchCount} mismatches", GUILayout.ExpandWidth(true));
            }
            else
            {
                GUILayout.Label($"Last Weld: None", GUILayout.ExpandWidth(true));
            }
            #endregion

            if (EditorGUI.EndChangeCheck() || forceUpdate)
			{
				EditorUtility.SetDirty(target);
				AssetDatabase.SaveAssetIfDirty(target);
				string path = AssetDatabase.GetAssetPath(target.GetInstanceID());
				AssetDatabase.ImportAsset(path);
				UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
			}	
        }

        private void AnimatedBoneDropAreaGUI(Rect dropArea)
        {
            GameObject obj = DropAreaGUI(dropArea);
            if (obj != null)
            {
                AddAnimatedBone(obj.name);
            }
        }

		private void UpdateSlotDropAreaGUI(Rect dropArea)
		{
			GameObject obj = DropAreaGUI(dropArea);
			if (obj != null)
			{
				SkinnedMeshRenderer skinnedMesh = obj.GetComponent<SkinnedMeshRenderer>();
				if (skinnedMesh != null)
				{
					UpdateSlotData(slot.normalReferenceMesh, skinnedMesh);
					GUI.changed = true;
					EditorUtility.DisplayDialog("Complete", "Update completed","OK");
				}
				else
                {
                    EditorUtility.DisplayDialog("Error", "No skinned mesh renderer found!", "Ok");
                }
            }

		}

		private GameObject DropAreaGUI(Rect dropArea)
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
					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
					for (int i = 0; i < draggedObjects.Length; i++)
					{
						if (draggedObjects[i])
						{
							var go = draggedObjects[i] as GameObject;
							if (go != null)
							{
                                return go;
							}
						}
					}
				}
			}
            return null;
		}
		private void AddAnimatedBone(string animatedBone)
		{
			var hash = UMASkeleton.StringToHash(animatedBone);
			foreach (var t in targets)
			{
				var slotDataAsset = t as SlotDataAsset;
				if (slotDataAsset != null)
				{
					ArrayUtility.Add(ref slotDataAsset.animatedBoneNames, animatedBone);
					ArrayUtility.Add(ref slotDataAsset.animatedBoneHashes, hash);
					EditorUtility.SetDirty(slotDataAsset);
				}
			}			
		}
		private void UpdateSlotData(SkinnedMeshRenderer seamsMesh, SkinnedMeshRenderer skinnedMesh)
		{
			SlotDataAsset slot = target as SlotDataAsset;

			string existingRootBone = slot.meshData.RootBoneName;

			UMASlotProcessingUtil.UpdateSlotData(slot, skinnedMesh, slot.material, seamsMesh, existingRootBone, true);
			string path = AssetDatabase.GetAssetPath(target.GetInstanceID());
			AssetDatabase.ImportAsset(path);
			UMAUpdateProcessor.UpdateSlot(slot);
		}
    }
}
#endif
