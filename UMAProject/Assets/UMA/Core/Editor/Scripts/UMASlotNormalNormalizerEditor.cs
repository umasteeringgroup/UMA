#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMASlotNormalNormalizer))]
	public class UMASlotNormalNormalizerEditor : Editor
	{
		private SlotData[] _slots = new SlotData[0];
		private string[] _slotLabels = new string[0];
		private OverlayColorData[] _colors = new OverlayColorData[0];
		private string[] _colorLabels = new string[0];

		private bool _slotCacheValid;
		private bool _colorCacheValid;
		private string _copyNormalsInfo;

		public override void OnInspectorGUI()
		{
			UMASlotNormalNormalizer normalizer = (UMASlotNormalNormalizer)target;
			if (normalizer == null)
			{
				return;
			}

			DrawSourceFields(normalizer);
			EditorGUILayout.Space();
			DrawSlotSelection(normalizer);
			DrawColorSelection(normalizer);
			EditorGUILayout.Space();
			DrawPreviewControls(normalizer);
			EditorGUILayout.Space();
			DrawCoveringMeshControls(normalizer);
			EditorGUILayout.Space();
			DrawPreviewInfo(normalizer);
		}

		private void DrawSourceFields(UMASlotNormalNormalizer normalizer)
		{
			bool sourceChanged = false;

			EditorGUI.BeginChangeCheck();
			UMAWardrobeRecipe newRecipe = (UMAWardrobeRecipe)EditorGUILayout.ObjectField("Wardrobe Recipe", normalizer.wardrobeRecipe, typeof(UMAWardrobeRecipe), false);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Assign Wardrobe Recipe");
				normalizer.wardrobeRecipe = newRecipe;
				normalizer.selectedSlotIndex = 0;
				_slotCacheValid = false;
				sourceChanged = true;
				EditorUtility.SetDirty(normalizer);
			}

			EditorGUI.BeginChangeCheck();
			SharedColorTable newTable = (SharedColorTable)EditorGUILayout.ObjectField("Shared Color Table", normalizer.sharedColorTable, typeof(SharedColorTable), false);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Assign Shared Color Table");
				normalizer.sharedColorTable = newTable;
				normalizer.selectedColorIndex = 0;
				_colorCacheValid = false;
				sourceChanged = true;
				EditorUtility.SetDirty(normalizer);
			}

			if (!_slotCacheValid)
			{
				RebuildSlotCache(normalizer);
			}

			if (!_colorCacheValid)
			{
				RebuildColorCache(normalizer);
			}

			if (sourceChanged)
			{
				RebuildPreview(normalizer);
				EditorUtility.SetDirty(normalizer);
			}
		}

		private void DrawSlotSelection(UMASlotNormalNormalizer normalizer)
		{
			if (normalizer.wardrobeRecipe == null)
			{
				EditorGUILayout.HelpBox("Assign a wardrobe recipe to select a slot.", MessageType.Info);
				return;
			}

			if (_slots.Length == 0)
			{
				EditorGUILayout.HelpBox("No mesh-bearing SlotData entries were found in the recipe.", MessageType.Warning);
				return;
			}

			normalizer.selectedSlotIndex = Mathf.Clamp(normalizer.selectedSlotIndex, 0, _slots.Length - 1);
			EditorGUI.BeginChangeCheck();
			int newSlotIndex = EditorGUILayout.Popup("Slot", normalizer.selectedSlotIndex, _slotLabels);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Preview Slot");
				normalizer.selectedSlotIndex = newSlotIndex;
				RebuildPreview(normalizer);
				EditorUtility.SetDirty(normalizer);
			}
		}

		private void DrawColorSelection(UMASlotNormalNormalizer normalizer)
		{
			if (normalizer.sharedColorTable == null)
			{
				EditorGUILayout.HelpBox("Assign a shared color table to select a color.", MessageType.Info);
				return;
			}

			if (_colors.Length == 0)
			{
				EditorGUILayout.HelpBox("The shared color table does not contain any colors.", MessageType.Warning);
				return;
			}

			normalizer.selectedColorIndex = Mathf.Clamp(normalizer.selectedColorIndex, 0, _colors.Length - 1);
			EditorGUI.BeginChangeCheck();
			int newColorIndex = EditorGUILayout.Popup("Color", normalizer.selectedColorIndex, _colorLabels);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Preview Color");
				normalizer.selectedColorIndex = newColorIndex;
				RebuildPreview(normalizer);
				EditorUtility.SetDirty(normalizer);
			}
		}

		private void DrawPreviewControls(UMASlotNormalNormalizer normalizer)
		{
			using (new EditorGUI.DisabledScope(normalizer.wardrobeRecipe == null || _slots.Length == 0))
			{
				if (GUILayout.Button("Build Preview"))
				{
					RebuildPreview(normalizer);
					EditorUtility.SetDirty(normalizer);
				}
			}

			using (new EditorGUI.DisabledScope(normalizer.previewObject == null && normalizer.previewMaterial == null && normalizer.previewMesh == null))
			{
				if (GUILayout.Button("Destroy Preview"))
				{
					normalizer.DestroyPreview();
					EditorUtility.SetDirty(normalizer);
				}
			}
		}

		private void DrawCoveringMeshControls(UMASlotNormalNormalizer normalizer)
		{
			EditorGUILayout.LabelField("Covering Mesh", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();
			float newMetaballSize = EditorGUILayout.Slider("Metaball Size", normalizer.metaballSize, 0f, 1f);
			float newSmoothness = EditorGUILayout.Slider("Smoothness", normalizer.smoothness, 0f, 1f);
			int newResolution = EditorGUILayout.IntSlider("Resolution", normalizer.coveringMeshResolution, 8, 256);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Covering Mesh Settings");
				normalizer.metaballSize = newMetaballSize;
				normalizer.smoothness = newSmoothness;
				normalizer.coveringMeshResolution = newResolution;
				// Settings changed: invalidate the existing covering mesh so it is rebuilt explicitly.
				normalizer.DestroyCoveringMesh();
				EditorUtility.SetDirty(normalizer);
			}

			EditorGUI.BeginChangeCheck();
			bool newShow = EditorGUILayout.Toggle("Show Covering Mesh", normalizer.showCoveringMesh);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Toggle Covering Mesh Visibility");
				normalizer.SetCoveringMeshVisible(newShow);
				EditorUtility.SetDirty(normalizer);
			}

			EditorGUI.BeginChangeCheck();
			float newAlpha = EditorGUILayout.Slider("Covering Mesh Alpha", normalizer.coveringMeshAlpha, 0f, 1f);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Covering Mesh Alpha");
				normalizer.coveringMeshAlpha = newAlpha;
				normalizer.ApplyCoveringMeshAlpha();
				EditorUtility.SetDirty(normalizer);
				SceneView.RepaintAll();
			}

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("Normal Projection", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			float newClusterEpsScale = EditorGUILayout.FloatField("Cluster Eps Scale", normalizer.clusterEpsScale);
			int newMinClusterSize = EditorGUILayout.IntField("Min Cluster Size", normalizer.minClusterSize);
			float newDotThreshold = EditorGUILayout.Slider("Normal Dot Threshold", normalizer.normalDotThreshold, -1f, 1f);
			float newMaxRayDistance = EditorGUILayout.FloatField("Max Ray Distance", normalizer.maxRayDistance);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Projection Settings");
				normalizer.clusterEpsScale = Mathf.Max(0.0001f, newClusterEpsScale);
				normalizer.minClusterSize = Mathf.Max(1, newMinClusterSize);
				normalizer.normalDotThreshold = newDotThreshold;
				normalizer.maxRayDistance = Mathf.Max(0f, newMaxRayDistance);
				EditorUtility.SetDirty(normalizer);
			}

			EditorGUI.BeginChangeCheck();
			int newSmoothIters = EditorGUILayout.IntSlider("Smoothing Passes", normalizer.smoothingIterations, 0, 5);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Smoothing Iterations");
				normalizer.smoothingIterations = newSmoothIters;
				EditorUtility.SetDirty(normalizer);
			}

			EditorGUI.BeginChangeCheck();
			float newStripEdgeNormalCurveDegrees = EditorGUILayout.Slider("Strip Edge Normal Curve", normalizer.stripEdgeNormalCurveDegrees, -45f, 45f);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Strip Edge Normal Curve");
				normalizer.stripEdgeNormalCurveDegrees = newStripEdgeNormalCurveDegrees;
				normalizer.ApplyStripNormalCurveToPreviewMesh();
				EditorUtility.SetDirty(normalizer);
				SceneView.RepaintAll();
			}

			EditorGUILayout.LabelField("Normal Visualization", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			bool showCoveringNormals = EditorGUILayout.Toggle("Show Covering Normals", normalizer.showCoveringMeshNormals);
			bool showPreviewNormals = EditorGUILayout.Toggle("Show Preview Normals", normalizer.showPreviewMeshNormals);
			float newLength = EditorGUILayout.FloatField("Normal Display Length", normalizer.normalDisplayLength);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Normal Visualization");
				normalizer.showCoveringMeshNormals = showCoveringNormals;
				normalizer.showPreviewMeshNormals = showPreviewNormals;
				normalizer.normalDisplayLength = Mathf.Max(0f, newLength);
				EditorUtility.SetDirty(normalizer);
				SceneView.RepaintAll();
			}

			EditorGUI.BeginChangeCheck();
			float newNormalEffectiveness = EditorGUILayout.Slider("Normal Effectiveness", normalizer.normalEffectiveness, 0f, 1f);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Normal Effectiveness");
				normalizer.normalEffectiveness = newNormalEffectiveness;
				normalizer.ApplyNormalEffectivenessToPreviewMesh();
				EditorUtility.SetDirty(normalizer);
				SceneView.RepaintAll();
			}
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Projection Debug", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			bool newEnableDebug = EditorGUILayout.Toggle("Enable Debug Visualization", normalizer.enableDebugVisualization);
			bool newShowRays = EditorGUILayout.Toggle("Show Ray Gizmos", normalizer.showRayGizmos);
			Color newAcceptedColor = EditorGUILayout.ColorField("Accepted Ray Color", normalizer.acceptedRayColor);
			Color newRejectedColor = EditorGUILayout.ColorField("Rejected Ray Color", normalizer.rejectedRayColor);
			int newMaxDebugCount = EditorGUILayout.IntField("Max Debug Ray Count", normalizer.maxDebugRayCount);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(normalizer, "Change Projection Debug Settings");
				normalizer.enableDebugVisualization = newEnableDebug;
				normalizer.showRayGizmos = newShowRays;
				normalizer.acceptedRayColor = newAcceptedColor;
				normalizer.rejectedRayColor = newRejectedColor;
				normalizer.maxDebugRayCount = Mathf.Max(0, newMaxDebugCount);
				EditorUtility.SetDirty(normalizer);
				SceneView.RepaintAll();
			}

			if (normalizer.previewMesh == null)
			{
				EditorGUILayout.HelpBox("Build a preview before constructing a covering mesh.", MessageType.Info);
			}

			using (new EditorGUI.DisabledScope(normalizer.previewMesh == null))
			{
				if (GUILayout.Button("Construct a Covering Mesh"))
				{
					normalizer.BuildCoveringMesh();
					EditorUtility.SetDirty(normalizer);
				}
			}

			using (new EditorGUI.DisabledScope(normalizer.coveringMesh == null))
			{
				if (GUILayout.Button("Project Normals to Preview"))
				{
					normalizer.ProjectNormalsToPreview();
					_copyNormalsInfo = string.Empty;
					EditorUtility.SetDirty(normalizer);
				}

				if (GUILayout.Button("Destroy Covering Mesh"))
				{
					normalizer.DestroyCoveringMesh();
					EditorUtility.SetDirty(normalizer);
				}
			}

			SlotData selectedSlot = GetSelectedSlot(normalizer);
			SlotDataAsset selectedSlotAsset = selectedSlot != null ? selectedSlot.asset : null;
			bool canCopyEffectiveNormals = CanCopyEffectiveNormals(normalizer, selectedSlotAsset);
			using (new EditorGUI.DisabledScope(!canCopyEffectiveNormals))
			{
				if (GUILayout.Button("Copy Effective Normals to SlotDataAsset"))
				{
					_copyNormalsInfo = CopyEffectiveNormalsToSlotDataAsset(normalizer, selectedSlotAsset);
				}
			}

			if (!string.IsNullOrEmpty(_copyNormalsInfo))
			{
				EditorGUILayout.HelpBox(_copyNormalsInfo, _copyNormalsInfo.StartsWith("Copied") ? MessageType.Info : MessageType.Warning);
			}
		}

		private void DrawPreviewInfo(UMASlotNormalNormalizer normalizer)
		{
			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.ObjectField("Preview Object", normalizer.previewObject, typeof(GameObject), true);
			EditorGUILayout.ObjectField("Preview Material", normalizer.previewMaterial, typeof(Material), false);
			EditorGUILayout.ObjectField("Preview Mesh", normalizer.previewMesh, typeof(Mesh), false);
			EditorGUI.EndDisabledGroup();
		}

		private void RebuildSlotCache(UMASlotNormalNormalizer normalizer)
		{
			_slotCacheValid = true;
			_slots = new SlotData[0];
			_slotLabels = new string[0];

			if (normalizer.wardrobeRecipe == null)
			{
				return;
			}

			UMAData.UMARecipe cachedRecipe;
			try
			{
				cachedRecipe = normalizer.wardrobeRecipe.GetCachedRecipe(true);
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"[UMASlotNormalNormalizerEditor] Could not load wardrobe recipe: {ex.Message}", normalizer);
				return;
			}

			if (cachedRecipe == null || cachedRecipe.slotDataList == null)
			{
				return;
			}

			List<SlotData> slots = new List<SlotData>();
			List<string> labels = new List<string>();
			for (int i = 0; i < cachedRecipe.slotDataList.Length; i++)
			{
				SlotData slot = cachedRecipe.slotDataList[i];
				if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
				{
					continue;
				}

				slots.Add(slot);
				labels.Add($"{i + 1}: {slot.slotName}");
			}

			_slots = slots.ToArray();
			_slotLabels = labels.ToArray();
			normalizer.selectedSlotIndex = Mathf.Clamp(normalizer.selectedSlotIndex, 0, Mathf.Max(0, _slots.Length - 1));
		}

		private void RebuildColorCache(UMASlotNormalNormalizer normalizer)
		{
			_colorCacheValid = true;
			_colors = new OverlayColorData[0];
			_colorLabels = new string[0];

			if (normalizer.sharedColorTable == null || normalizer.sharedColorTable.colors == null)
			{
				return;
			}

			List<OverlayColorData> colors = new List<OverlayColorData>();
			List<string> labels = new List<string>();
			for (int i = 0; i < normalizer.sharedColorTable.colors.Length; i++)
			{
				OverlayColorData color = normalizer.sharedColorTable.colors[i];
				colors.Add(color);
				string label = color != null && !string.IsNullOrEmpty(color.name) ? color.name : $"Color {i + 1}";
				labels.Add($"{i + 1}: {label}");
			}

			_colors = colors.ToArray();
			_colorLabels = labels.ToArray();
			normalizer.selectedColorIndex = Mathf.Clamp(normalizer.selectedColorIndex, 0, Mathf.Max(0, _colors.Length - 1));
		}

		private void RebuildPreview(UMASlotNormalNormalizer normalizer)
		{
			SlotData selectedSlot = GetSelectedSlot(normalizer);

			OverlayColorData selectedColor = null;
			if (_colors.Length > 0 && normalizer.selectedColorIndex >= 0 && normalizer.selectedColorIndex < _colors.Length)
			{
				selectedColor = _colors[normalizer.selectedColorIndex];
			}

			normalizer.BuildPreview(selectedSlot, selectedColor);
		}

		private SlotData GetSelectedSlot(UMASlotNormalNormalizer normalizer)
		{
			if (_slots.Length > 0 && normalizer.selectedSlotIndex >= 0 && normalizer.selectedSlotIndex < _slots.Length)
			{
				return _slots[normalizer.selectedSlotIndex];
			}

			return null;
		}

		private static bool CanCopyEffectiveNormals(UMASlotNormalNormalizer normalizer, SlotDataAsset slotDataAsset)
		{
			if (normalizer == null || slotDataAsset == null || UMAMeshData.IsNullOrEmptyMeshData(slotDataAsset.meshData) || normalizer.previewMesh == null || !normalizer.HasEffectiveNormalPreview())
			{
				return false;
			}

			return normalizer.previewMesh.vertexCount == slotDataAsset.meshData.vertexCount;
		}

		private static string CopyEffectiveNormalsToSlotDataAsset(UMASlotNormalNormalizer normalizer, SlotDataAsset slotDataAsset)
		{
			if (!CanCopyEffectiveNormals(normalizer, slotDataAsset))
			{
				return "Build a preview, project normals, and select a matching SlotDataAsset before copying normals.";
			}

			normalizer.ApplyStripNormalCurveToPreviewMesh();
			Vector3[] previewNormals = normalizer.previewMesh.normals;
			if (previewNormals == null || previewNormals.Length != slotDataAsset.meshData.vertexCount)
			{
				return "Preview normals do not match the selected SlotDataAsset vertex count.";
			}

			Undo.RecordObject(slotDataAsset, "Copy Effective Slot Normals");
			Vector3[] copiedNormals = new Vector3[previewNormals.Length];
			for (int i = 0; i < previewNormals.Length; i++)
			{
				copiedNormals[i] = previewNormals[i].sqrMagnitude > 0.0000001f ? previewNormals[i].normalized : Vector3.up;
			}

			slotDataAsset.meshData.normals = copiedNormals;
			slotDataAsset.meshData.normalsModified = true;

			Vector4[] previewTangents = normalizer.previewMesh.tangents;
			bool copiedTangents = previewTangents != null && previewTangents.Length == previewNormals.Length;
			if (copiedTangents)
			{
				Vector4[] copiedTangentData = new Vector4[previewTangents.Length];
				for (int i = 0; i < previewTangents.Length; i++)
				{
					copiedTangentData[i] = previewTangents[i];
				}

				slotDataAsset.meshData.tangents = copiedTangentData;
				slotDataAsset.meshData.tangentsModified = true;
			}

			slotDataAsset.ValidateMeshData();
			EditorUtility.SetDirty(slotDataAsset);
			AssetDatabase.SaveAssetIfDirty(slotDataAsset);
			string path = AssetDatabase.GetAssetPath(slotDataAsset.GetUmaObjectId());
			if (!string.IsNullOrEmpty(path))
			{
				AssetDatabase.ImportAsset(path);
			}

			UMAUpdateProcessor.UpdateSlot(slotDataAsset, false);
			SceneView.RepaintAll();
			return copiedTangents
				? $"Copied {copiedNormals.Length} effective normal(s) and matching tangent(s) to '{slotDataAsset.name}'."
				: $"Copied {copiedNormals.Length} effective normal(s) to '{slotDataAsset.name}'.";
		}
	}
}
#endif
