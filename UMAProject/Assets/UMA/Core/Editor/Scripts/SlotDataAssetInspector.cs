#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Text;

namespace UMA.Editors
{
	[CustomEditor(typeof(SlotDataAsset))]
	[CanEditMultipleObjects]
	public class SlotDataAssetInspector : Editor
	{
		enum SlotPreviewMode { ThisSlot, WeldSlot, BothSlots };

		static string[] RegularSlotFields = new string[] { "slotName", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "tags", "isWildCardSlot", "Races", "smooshOffset", "smooshExpand", "Welds" };
		static string[] WildcardSlotFields = new string[] { "slotName", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "tags", "isWildCardSlot", "Races", "_rendererAsset", "maxLOD", "useAtlasOverlay", "overlayScale", "_slotDNA", "meshData", "subMeshIndex", "Welds" };
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
		SlotDataAsset WeldToSlot = null;

		// New: Source slot for bindpose conformity
		SlotDataAsset bindposeSourceSlot = null;
		string lastBindposeInfo = "";

		bool CopyNormals;
		bool CopyBoneWeights;
		UMA.SlotDataAsset.BlendshapeCopyMode blendshapeCopyMode;
		UMA.SlotDataAsset.NormalCopyMode normalCopyMode;
        bool AverageNormals;
		float weldDistance = 0.0001f;
		bool reConfigurePreview = false;
		private static string lastInfo = "";
        private int selectedRaceIndex = -1;
		private List<RaceData> foundRaces = new List<RaceData>();
		private List<string> foundRaceNames = new List<string>();
		private int uvChannel;
		private int uvChannelToMirror;

        public override bool HasPreviewGUI() => true;
		MeshPreview MeshPreview;
		Mesh meshToPreview;
		static Vector3 previewRotation = Vector3.zero;
		SlotPreviewMode previewMode = SlotPreviewMode.ThisSlot;
		int previewVertex = -1;


        [MenuItem("Assets/Create/UMA/Core/Custom Slot Asset")]
		public static void CreateCustomSlotAssetMenuItem()
		{
			CustomAssetUtility.CreateAsset<SlotDataAsset>("", true, "Custom");
		}

		[MenuItem("Assets/Create/UMA/Core/Wildcard Slot Asset")]
		public static void CreateWildcardSlotAssetMenuItem()
		{
			SlotDataAsset wildcard = CustomAssetUtility.CreateAsset<SlotDataAsset>("", true, "Wildcard", true);
			wildcard.isWildCardSlot = true;
			wildcard.slotName = "WildCard";
			EditorUtility.SetDirty(wildcard);
			string path = AssetDatabase.GetAssetPath(wildcard.GetInstanceID());
			AssetDatabase.ImportAsset(path);
			EditorUtility.DisplayDialog("UMA", "Wildcard slot created. You should first change the SlotName in the inspector, and then add it to the global library or to a scene library", "OK");
		}

		private void OnDestroy()
		{
			// clean up
			if (meshToPreview != null)
			{
				DestroyImmediate(meshToPreview);
			}
			meshToPreview = null;
			if (MeshPreview != null)
			{
				MeshPreview.Dispose();
				MeshPreview = null;
			}
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
			if (slot.tags == null)
			{
				slot.backingTags = new List<string>();
			}
			else
			{
				slot.backingTags = new List<string>(slot.tags);
			}
			slot.tagList = GUIHelper.InitGenericTagsList(slot.backingTags);
		}

		private void OnDisable()
		{
			if (meshToPreview != null)
			{
				DestroyImmediate(meshToPreview);
			}
			meshToPreview = null;
			if (MeshPreview != null)
			{
				MeshPreview.Dispose();
				MeshPreview = null;
			}
		}

		public void SetRaceLists()
		{

			RaceData[] raceDataArray = UMAAssetIndexer.Instance.GetAllRaces();
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
			if (GUILayout.Button("Use Obj Name", GUILayout.Width(90)))
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

			EditorGUI.BeginChangeCheck();

			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			slot.smooshFoldout = EditorGUILayout.Foldout(slot.smooshFoldout, "Smooshing");
			GUILayout.EndHorizontal();
			if (slot.smooshFoldout)
			{
                #region Smooshing
                GUILayout.Space(10);
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
				EditorGUILayout.HelpBox("Smooshing is a feature that conforms one slot to another using a clipping plane. Smoosh Offset is used to adjust the offset of the conforming vertexes to help assist conforming and fitting. Smoosh Expand expands scales the vertexes. ", MessageType.Info);

				var currentTarget = target as SlotDataAsset;

				forceUpdate = EditorGUI.EndChangeCheck();
				EditorGUILayout.PropertyField(smooshOffset);
				EditorGUILayout.PropertyField(smooshExpand);

				if (GUILayout.Button("Save and Test Smoosh"))
				{
					UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
					EditorUtility.SetDirty(target);
					AssetDatabase.SaveAssetIfDirty(target);
					string path = AssetDatabase.GetAssetPath(target.GetInstanceID());
					AssetDatabase.ImportAsset(path);
					forceUpdate = true;
				}
				GUIHelper.EndVerticalPadded(10);
                #endregion
            }


            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			slot.tagsFoldout = EditorGUILayout.Foldout(slot.tagsFoldout, "Tags");
			GUILayout.EndHorizontal();

			if (slot.tagsFoldout)
			{
				GUILayout.Space(10);
				slot.tagList.DoLayoutList();
				if (GUI.changed)
				{
					slot.tags = slot.backingTags.ToArray();
					EditorUtility.SetDirty(slot);
					forceUpdate = true;
				}
			}

			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			(target as SlotDataAsset).eventsFoldout = EditorGUILayout.Foldout((target as SlotDataAsset).eventsFoldout, "Slot Events");
			GUILayout.EndHorizontal();
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

			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			slot.utilitiesFoldout = EditorGUILayout.Foldout(slot.utilitiesFoldout, "Slot Utilities");
			GUILayout.EndHorizontal();

			if (slot.utilitiesFoldout)
			{
                #region UV_Utilities
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
				GUILayout.Label("UV Utilities", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Copy UV0 to UV Channel", GUILayout.Width(150));
                uvChannel = EditorGUILayout.Popup(uvChannel, new string[] { "2", "3", "4" }, GUILayout.Width(50));
                if (GUILayout.Button("Copy"))
                {
                    SlotDataAsset slotDataAsset = target as SlotDataAsset;
                    switch (uvChannel)
					{
						case 0:
							slotDataAsset.meshData.uv2 = slotDataAsset.meshData.uv.Clone() as Vector2[];
							break;
                        case 1:
                            slotDataAsset.meshData.uv3 = slotDataAsset.meshData.uv.Clone() as Vector2[];
                            break;
                        case 2:
                            slotDataAsset.meshData.uv4 = slotDataAsset.meshData.uv.Clone() as Vector2[];
                            break;
                    }
					EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                    UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
                    EditorUtility.DisplayDialog("Complete", "UV0 copied to UV" + (uvChannel + 2), "OK");
                }
				GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Mirror UV Channel ", GUILayout.Width(150));
                uvChannelToMirror = EditorGUILayout.Popup(uvChannelToMirror, new string[] { "1", "2", "3", "4" }, GUILayout.Width(50));

                if (GUILayout.Button("Mirror U"))
                {
                    SlotDataAsset slotDataAsset = target as SlotDataAsset;
                    switch (uvChannelToMirror)
                    {
                        case 0:
                            slotDataAsset.meshData.MirrorU(0);
                            break;
                        case 1:
                            slotDataAsset.meshData.MirrorU(1);
                            break;
                        case 2:
                            slotDataAsset.meshData.MirrorU(2);
                            break;
                        case 3:
                            slotDataAsset.meshData.MirrorU(3);
                            break;
                    }
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                    UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
                    EditorUtility.DisplayDialog("Complete", "UV U" + (uvChannelToMirror + 1) + " mirrored", "OK");
                }
                if (GUILayout.Button("Mirror V"))
                {
                    SlotDataAsset slotDataAsset = target as SlotDataAsset;
                    switch (uvChannelToMirror)
                    {
                        case 0:
                            slotDataAsset.meshData.MirrorV(0);
                            break;
                        case 1:
                            slotDataAsset.meshData.MirrorV(1);
                            break;
                        case 2:
                            slotDataAsset.meshData.MirrorV(2);
                            break;
                        case 3:
                            slotDataAsset.meshData.MirrorV(3);
                            break;
                    }
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                    UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
                    EditorUtility.DisplayDialog("Complete", "UV V" + (uvChannelToMirror + 1) + " mirrored", "OK");
                }
				GUILayout.EndHorizontal();
                GUIHelper.EndVerticalPadded(10);
                #endregion

				#region Bindpose Conform
				GUIHelper.BeginVerticalPadded(10, new Color(0.80f, 0.95f, 0.80f));
				GUILayout.Label("Bindpose Conform", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("Conform this slot's bindposes and vertex positions to those in the source slot. Vertices are adjusted using the dominant bone so skin output stays consistent. Bones not present in the source keep their original bindpose.", MessageType.Info);
				bindposeSourceSlot = EditorGUILayout.ObjectField("Source Slot", bindposeSourceSlot, typeof(SlotDataAsset), false) as SlotDataAsset;

				EditorGUI.BeginDisabledGroup(bindposeSourceSlot == null || bindposeSourceSlot.meshData == null || slot.meshData == null);
				if (GUILayout.Button("Conform Bindposes && Vertices"))
				{
					lastBindposeInfo = ConformBindposesAndVertices(slot, bindposeSourceSlot);
					EditorUtility.SetDirty(slot);
					AssetDatabase.SaveAssetIfDirty(slot);
					UMAUpdateProcessor.UpdateSlot(slot, false);
				}
				EditorGUI.EndDisabledGroup();

				if (!string.IsNullOrEmpty(lastBindposeInfo))
				{
					EditorGUILayout.HelpBox(lastBindposeInfo, MessageType.None);
				}
				GUIHelper.EndVerticalPadded(10);
				#endregion

                #region WELDS
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                selectedRaceIndex = EditorGUILayout.Popup("Select Base Slot by Race", selectedRaceIndex, foundRaceNames.ToArray());
				if (selectedRaceIndex <= 0)
				{
					EditorGUILayout.HelpBox("Select a slot by race quickly, or use manual selection below", MessageType.Info);
				}
				else
				{
					UMAData.UMARecipe baseRecipe = new UMAData.UMARecipe();
					foundRaces[selectedRaceIndex].baseRaceRecipe.Load(baseRecipe);

					foreach (SlotData sd in baseRecipe.slotDataList)
					{
						if (sd != null && sd.asset != null)
						{
							if (GUILayout.Button(string.Format("{0} ({1})", sd.asset.name, sd.slotName)))
							{
								WeldToSlot = sd.asset;
							}
						}
					}
				}

				GUILayout.Space(12);

				WeldToSlot = EditorGUILayout.ObjectField("Source SLot", WeldToSlot, typeof(SlotDataAsset), false) as SlotDataAsset;

				weldDistance = EditorGUILayout.FloatField("Max Vertex Distance", weldDistance);

				if (WeldToSlot == null)
				{
					EditorGUI.BeginDisabledGroup(true);
				}
				string weldSlotName = WeldToSlot != null ? WeldToSlot.slotName : "No Slot Selected";
 
				GUILayout.Box("Warning! averaging normals will update both slots!", GUILayout.ExpandWidth(true));

				if (GUILayout.Button($"Copy boneweights"))
				{
					lastInfo = slot.CopyBoneweightsFrom(WeldToSlot);
				}

				GUILayout.BeginHorizontal();
				GUILayout.Label("Normal Copy Mode", GUILayout.Width(150));

				normalCopyMode = (UMA.SlotDataAsset.NormalCopyMode)EditorGUILayout.EnumPopup(normalCopyMode, GUILayout.Width(130));
				if (GUILayout.Button($"Copy Normals"))
				{
					lastInfo = slot.CopyNormalsFrom(WeldToSlot, weldDistance, normalCopyMode);
				}
				GUILayout.EndHorizontal();

				GUILayout.BeginHorizontal();
				GUILayout.Label("Blendshape Copy Mode", GUILayout.Width(150));
				blendshapeCopyMode = (UMA.SlotDataAsset.BlendshapeCopyMode)EditorGUILayout.EnumPopup(blendshapeCopyMode, GUILayout.Width(130));

				if (GUILayout.Button($"Copy Blendshapes"))
				{
					lastInfo = slot.CopyBlendshapesFrom(WeldToSlot, blendshapeCopyMode);
				}
				GUILayout.EndHorizontal();

				if (!string.IsNullOrEmpty(lastInfo))
				{
					EditorGUILayout.HelpBox(lastInfo, MessageType.Info);
				}

				if (WeldToSlot == null)
				{
					EditorGUI.EndDisabledGroup();
				}
				GUIHelper.EndVerticalPadded(10);
                #endregion 
                #region info
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
				GUILayout.Label("This mesh"); 

				GUILayout.BeginHorizontal();
                GUILayout.Label("  Vertices: ",GUILayout.Width(160));
				GUILayout.Label($"{slot.meshData.vertices.Length}", GUILayout.Width(160));
				GUILayout.Label("", GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("  BoneWeights: ", GUILayout.Width(160));
                GUILayout.Label($"{slot.meshData.ManagedBoneWeights.Length}", GUILayout.Width(160));
                GUILayout.Label("", GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                if (WeldToSlot != null)
                {
					GUILayout.Space(10);
                    GUILayout.Label("Source Mesh");
                    GUILayout.BeginHorizontal();
					GUILayout.Label("  Vertices: ", GUILayout.Width(160));
                    GUILayout.Label($"{WeldToSlot.meshData.vertices.Length}", GUILayout.Width(160));
                    GUILayout.Label("", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("  BoneWeights: ", GUILayout.Width(160));
                    GUILayout.Label($"{WeldToSlot.meshData.ManagedBoneWeights.Length}", GUILayout.Width(160));
                    GUILayout.Label("", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }

                GUIHelper.EndVerticalPadded(10);
                #endregion
                #region Preview

                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

				SlotPreviewMode newPreviewMode = (SlotPreviewMode)EditorGUILayout.EnumPopup("Preview Mode", previewMode);
				if (meshToPreview != null)
				{
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Preview Vert", GUILayout.Width(100));
					int newpreviewVertex = EditorGUILayout.IntSlider(previewVertex, -1, meshToPreview.vertexCount - 1);
					if (newpreviewVertex != previewVertex)
					{
						previewVertex = newpreviewVertex;
						reConfigurePreview = true;
					}
					if (GUILayout.Button("Dump Vert", GUILayout.Width(50)))
					{
						ShowDebugVertInfo(previewVertex);
					}
					EditorGUILayout.EndHorizontal();
				}
				Vector3 savedPreviewRotation = previewRotation;
				previewRotation = EditorGUILayout.Vector3Field("Preview Rotation", previewRotation);
				if (savedPreviewRotation != previewRotation)
				{
					reConfigurePreview = true;
				}
				if (newPreviewMode != previewMode)
				{
					reConfigurePreview = true;
					previewMode = newPreviewMode;
				}
				if (reConfigurePreview)
				{
					reConfigurePreview = false;
					if (MeshPreview != null)
					{
						MeshPreview.Dispose();
						MeshPreview = null;
					}
					if (meshToPreview != null)
					{
						DestroyImmediate(meshToPreview);
						meshToPreview = null;
					}
					meshToPreview = GetPreviewMesh();
					if (meshToPreview != null)
					{
						MeshPreview = new MeshPreview(meshToPreview);
					}
					else
					{
						if (MeshPreview != null)
						{
							MeshPreview.Dispose();
							MeshPreview = null;
						}
					}

				}
                GUIHelper.EndVerticalPadded(10);
                #endregion
            }


			if (!slot.isWildCardSlot)
			{
				GUILayout.Space(20);
				Rect updateDropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
				GUI.Box(updateDropArea, "Drag SkinnedMeshRenderers here to update the slot meshData.");
				GUILayout.Space(10);
				UpdateSlotDropAreaGUI(updateDropArea);

				GUILayout.Space(10);
			}

			serializedObject.ApplyModifiedProperties();

			if (EditorGUI.EndChangeCheck() || forceUpdate)
			{
				EditorUtility.SetDirty(target);
				AssetDatabase.SaveAssetIfDirty(target);
				string path = AssetDatabase.GetAssetPath(target.GetInstanceID());
				AssetDatabase.ImportAsset(path);
				UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
			}
		}

		private void ShowDebugVertInfo(int previewVertex)
		{
			StringBuilder sb = new StringBuilder();

			slot.BuildVertexLookups(WeldToSlot);
			slot.BuildOurAndTheirBoneWeights(WeldToSlot);
			slot.BuildBoneLookups(WeldToSlot);

			foreach (var bw in slot.OurBoneWeights[previewVertex])
			{
				string boneName = slot.meshData.umaBones[bw.boneIndex].name;
				sb.Append($"Bone {boneName}({bw.boneIndex}): Weight {bw.weight}");
				sb.Append(Environment.NewLine);
			}
			Debug.Log("Our vertex " + previewVertex + Environment.NewLine + sb.ToString());

			int theirVertex = slot.OurVertextoTheirVertex[previewVertex];
			foreach (var bw in slot.TheirBoneWeights[theirVertex])
			{
				string boneName = WeldToSlot.meshData.umaBones[bw.boneIndex].name;
				sb.Append($"Bone {boneName}({bw.boneIndex}): Weight {bw.weight}");
				sb.Append(Environment.NewLine);
			}
			Debug.Log("Their vertex " + theirVertex + Environment.NewLine + sb.ToString());

		}

        public override void OnPreviewSettings()
        {
			if (MeshPreview == null)
				return;
			try
			{
				MeshPreview.OnPreviewSettings();
			}
            catch (System.Exception)
			{

			}
        }

		private Mesh GetPreviewMesh()
		{
			Quaternion pRot = Quaternion.Euler(previewRotation);
            if (previewMode == SlotPreviewMode.ThisSlot)
			{
				return SlotToMesh.ConvertSlotToMesh((target as SlotDataAsset),pRot, previewVertex);
			}
			if (previewMode == SlotPreviewMode.WeldSlot)
			{
                if (WeldToSlot != null)
				{
                    return SlotToMesh.ConvertSlotToMesh(WeldToSlot, pRot, previewVertex);
                }
            }
            if (previewMode == SlotPreviewMode.BothSlots)
			{
				Mesh mesh = SlotToMesh.ConvertSlotToMesh((target as SlotDataAsset), pRot, previewVertex);
                if (WeldToSlot != null)
                {
                    Mesh weldMesh = SlotToMesh.ConvertSlotToMesh(WeldToSlot, pRot, previewVertex);
                    if (weldMesh != null)
                    {
						CombineInstance[] combine = new CombineInstance[2];
                        combine[0].mesh = mesh;
                        combine[1].mesh = weldMesh;
                        Mesh combinedMesh = new Mesh();
                        combinedMesh.CombineMeshes(combine,false,false,false);
						DestroyImmediate(mesh);
                        DestroyImmediate(weldMesh);
                        return combinedMesh;
                    }
                }
                return mesh;
            }
            return null;
        }

        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background)
		{
            if (meshToPreview == null)
            {
				meshToPreview = GetPreviewMesh();
				if (meshToPreview != null) 
				{
                    MeshPreview = new MeshPreview(meshToPreview);
                }	
            }
			if (meshToPreview != null && MeshPreview != null)
			{
				MeshPreview.OnPreviewGUI(r, background);
				GUI.Label(r, MeshPreview.GetInfoString(meshToPreview));
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

		private void UpdateSlotData(SkinnedMeshRenderer seamsMesh, SkinnedMeshRenderer skinnedMesh)
		{
			SlotDataAsset slot = target as SlotDataAsset;

			string existingRootBone = slot.meshData.RootBoneName;

			UMASlotProcessingUtil.UpdateSlotData(slot, skinnedMesh, slot.material, seamsMesh, existingRootBone, true);
			string path = AssetDatabase.GetAssetPath(target.GetInstanceID());
			AssetDatabase.ImportAsset(path);
			UMAUpdateProcessor.UpdateSlot(slot);
		}

		/// <summary>
		/// Conform this slot's bindposes & vertices to those of sourceSlot.
		/// Vertices are transformed using the dominant bone (highest weight).
		/// Bones absent in source retain original bindpose.
		/// </summary>
		private string ConformBindposesAndVertices(SlotDataAsset targetSlot, SlotDataAsset sourceSlot)
		{
			if (targetSlot == null || sourceSlot == null || targetSlot.meshData == null || sourceSlot.meshData == null)
				return "Missing mesh data.";

			var tMesh = targetSlot.meshData;
			var sMesh = sourceSlot.meshData;

			if (tMesh.bindPoses == null || sMesh.bindPoses == null ||
				tMesh.boneNameHashes == null || sMesh.boneNameHashes == null)
				return "Bindpose arrays missing.";

			int tBoneCount = tMesh.bindPoses.Length;
			int vCount = tMesh.vertexCount;
			if (vCount == 0 || tBoneCount == 0) return "No vertices or bones.";

			// Map source hash -> bindPose
			var srcMap = new Dictionary<int, Matrix4x4>(sMesh.boneNameHashes.Length);
			for (int i = 0; i < sMesh.boneNameHashes.Length && i < sMesh.bindPoses.Length; i++)
			{
				int h = sMesh.boneNameHashes[i];
				if (!srcMap.ContainsKey(h))
					srcMap.Add(h, sMesh.bindPoses[i]);
			}

			// Prepare transformation per target bone (identity if no change)
			var boneTransforms = new Matrix4x4[tBoneCount];
			bool anyChange = false;
			for (int i = 0; i < tBoneCount; i++)
            {
                boneTransforms[i] = Matrix4x4.identity;
                int hash = tMesh.boneNameHashes[i];
                if (srcMap.TryGetValue(hash, out var srcBind))
                {
                    var oldBind = tMesh.bindPoses[i];
                    if (!CompareBindpose(oldBind, srcBind))
                    {
                        // We want: srcBind * p_new = oldBind * p_old  =>  p_new = inverse(srcBind) * oldBind * p_old
                        // So per-bone correction T = inverse(srcBind) * oldBind
                        Matrix4x4 T = Matrix4x4.Inverse(srcBind) * oldBind;
                        boneTransforms[i] = T;
                         anyChange = true;
                     }
                 }
             }
			if (!anyChange) return "No differing bindposes found.";

			// Bone weights
			byte[] bonesPerVertex = tMesh.ManagedBonesPerVertex;
			BoneWeight1[] weights = tMesh.ManagedBoneWeights;
			if (bonesPerVertex == null || weights == null || bonesPerVertex.Length == 0 || weights.Length == 0)
				return "Bone weights missing.";

			Vector3[] verts = tMesh.vertices;
			Vector3[] normals = tMesh.normals;
			Vector4[] tangents = tMesh.tangents;

			int wOffset = 0;
			for (int v = 0; v < vCount; v++)
			{
				byte count = bonesPerVertex[v];
				if (count == 0) { continue; }

				int dominantIndex = -1;
				float dominantWeight = -1f;
				for (int j = 0; j < count; j++)
				{
					var bw = weights[wOffset + j];
					if (bw.weight > dominantWeight)
					{
						dominantWeight = bw.weight;
						dominantIndex = bw.boneIndex;
					}
				}

				if (dominantIndex >= 0 && dominantIndex < boneTransforms.Length)
				{
                    Matrix4x4 T = boneTransforms[dominantIndex];
                    if (!IsIdentity(T))
                    {
                        // Position
                        Vector3 p = verts[v];
                        Vector4 hp = new Vector4(p.x, p.y, p.z, 1f);
                        hp = T * hp;
                        verts[v] = new Vector3(hp.x, hp.y, hp.z);
 
                        // Normal
                        if (normals != null && v < normals.Length)
                        {
                            Vector3 n = normals[v];
                            Vector3 tn = T.MultiplyVector(n);
                             if (tn.sqrMagnitude > 0f) tn.Normalize();
                             normals[v] = tn;
                        }
                        // Tangent
                        if (tangents != null && v < tangents.Length)
                        {
                            Vector4 tan = tangents[v];
                            Vector3 tv = new Vector3(tan.x, tan.y, tan.z);
                            tv = T.MultiplyVector(tv);
                             if (tv.sqrMagnitude > 0f) tv.Normalize();
                             tangents[v] = new Vector4(tv.x, tv.y, tv.z, tan.w);
                        }
                    }
                }
				wOffset += count;
			}

			// Replace bindposes (only those with matches)
			for (int i = 0; i < tBoneCount; i++)
			{
				int hash = tMesh.boneNameHashes[i];
				if (srcMap.TryGetValue(hash, out var srcBind))
				{
					tMesh.bindPoses[i] = srcBind;
				}
			}

			// Mark modifications
			tMesh.verticesModified = true;
			tMesh.normalsModified = true;
			tMesh.tangentsModified = true;
			targetSlot.ValidateMeshData();
			EditorUtility.SetDirty(targetSlot);
			return "Bindpose/vertex conformity complete.";
		}

		private static bool CompareBindpose(Matrix4x4 a, Matrix4x4 b)
		{
			const float eps = 0.0001f;
			return
				Mathf.Abs(a.m00 - b.m00) < eps &&
				Mathf.Abs(a.m01 - b.m01) < eps &&
				Mathf.Abs(a.m02 - b.m02) < eps &&
				Mathf.Abs(a.m03 - b.m03) < eps &&
				Mathf.Abs(a.m10 - b.m10) < eps &&
				Mathf.Abs(a.m11 - b.m11) < eps &&
				Mathf.Abs(a.m12 - b.m12) < eps &&
				Mathf.Abs(a.m13 - b.m13) < eps &&
				Mathf.Abs(a.m20 - b.m20) < eps &&
				Mathf.Abs(a.m21 - b.m21) < eps &&
				Mathf.Abs(a.m22 - b.m22) < eps &&
				Mathf.Abs(a.m23 - b.m23) < eps;
		}

		private static bool IsIdentity(Matrix4x4 m)
		{
			return m == Matrix4x4.identity;
		}
    }
}
#endif
