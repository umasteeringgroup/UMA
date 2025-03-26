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
		static string[] WildcardSlotFields = new string[] { "slotName", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "tags", "isWildCardSlot", "Races", "_rendererAsset", "maxLOD", "useAtlasOverlay", "overlayScale", "animatedBoneNames", "_slotDNA", "meshData", "subMeshIndex", "Welds" };
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

		/*
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
		} */

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

		/*private void UpdateSourceAsset(SlotDataAsset sda)
		{
			if (sda != null)
			{
				lastWeld = slot.CalculateWelds(sda, CopyNormals, CopyBoneWeights, AverageNormals, Vector3.kEpsilon, SlotDataAsset.BlendshapeCopyMode.None);
			}
		}*/

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
                // create a button and popup to select a UV channel to copy UV 0 to. This is on the same slot
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

                // create a button and popup to select UV channel to mirror left to right
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




                    //GUILayout.Label("Vertices: " + WeldToSlot.meshData.vertices.Length);
                    //GUILayout.Label("BoneWeights: " + WeldToSlot.meshData.boneWeights.Length);
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
