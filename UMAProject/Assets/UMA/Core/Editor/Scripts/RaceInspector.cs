#pragma warning disable 0472 // disable warnings about result of comparison being unused (because of if/else usage)
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace UMA.Editors
{
	[CustomEditor(typeof(RaceData))]
	public class RaceInspector : Editor
	{
		[MenuItem("Assets/Create/UMA/Core/RaceData")]
		public static void CreateRaceMenuItem()
		{
			var rc = CustomAssetUtility.CreateAsset<RaceData>();
			if (rc != null)
			{
				Selection.activeObject = rc;
				rc.useNewDNA = true;
				EditorUtility.SetDirty(rc);
				AssetDatabase.SaveAssetIfDirty(rc);
			}
			else
			{
				Debug.LogError("Failed to create RaceData asset.");
			}
		}

		/// <summary>
		/// Replaces all thumbnail data on a race with a new, empty container.
		/// </summary>
		public static bool ClearRaceThumbnails(RaceData raceData, bool recordUndo = true)
		{
			if (raceData == null)
			{
				return false;
			}

			if (recordUndo)
			{
				Undo.RecordObject(raceData, "Clear Race Thumbnails");
			}

			raceData.raceThumbnails = new RaceData.RaceThumbnails();
			EditorUtility.SetDirty(raceData);
			AssetDatabase.SaveAssetIfDirty(raceData);
			return true;
		}

		public static bool showRaceGeneration = false;
		public static bool showUtilities = false;
		protected RaceData race;
		protected bool _needsUpdate;
		protected string _errorMessage;
		//we dont really want to use delayedFields because if the user does not change focus from the field in the inspector but instead selects another asset in their projects their changes dont save
		//Instead what we really want to do is set a short delay on saving so that the asset doesn't save while the user is typing in a field
		private float lastActionTime = 0;
		private bool doSave = false;
		//pRaceInspector needs to get unpacked UMATextRecipes so we might need a virtual UMAContextBase
		GameObject EditorUMAContextBase;
		List<string> ValidationMessages = new List<string>();
		#region DCS variables
		private ReorderableList wardrobeSlotList;
		private bool wardrobeSlotListInitialized = false;

		private ReorderableList prebakedBlendshapeList;
		private bool prebakedBlendshapeListInitialized = false;

		private ReorderableList unbakedShapesList;
		private bool unbakedShapesListInitialized = false;

		private int compatibleRacePickerID;
		static bool[] _BCFoldouts = new bool[0];
		List<SlotData> baseSlotsList = new List<SlotData>();
		List<string> baseSlotsNamesList = new List<string>();

		// Cached blendshape lookup for baseRaceRecipe slots
		private string[] _bsSlotNames = Array.Empty<string>();
		private Dictionary<string, string[]> _bsBySlot = new Dictionary<string, string[]>();
		private UMAObjectId _lastBaseRecipeId = 0;
		private bool _bsCacheValid = false;
		// UI selections for add-from-slot
		private int _prebakeAddSlotIndex = 0;
		private int _prebakeAddShapeIndex = 0;
		private int _unbakedAddSlotIndex = 0;
		private int _unbakedAddShapeIndex = 0;

		// Blendshape extraction UI
		private string[] _allBlendshapeNames = Array.Empty<string>();
		private int _selectedExtractBlendshapeIndex = 0;
		#endregion

		public void OnEnable() {
			race = target as RaceData;
			EditorApplication.update += DoDelayedSave;
		}

		void OnDestroy()
		{
			EditorApplication.update -= DoDelayedSave;
		}

		void DoDelayedSave()
		{
			if (doSave && Time.realtimeSinceStartup > (lastActionTime + 0.5f))
			{
				doSave = false;
				lastActionTime = Time.realtimeSinceStartup;
				EditorUtility.SetDirty(race);
				string path = AssetDatabase.GetAssetPath(race.GetEntityId());
				AssetDatabase.ImportAsset(path);
				UMAUpdateProcessor.UpdateRace(race);
			}
		}

		private void EnsureBlendshapeCache()
		{
			if (race != null && race.UsesFbxRoute)
			{
				_bsSlotNames = Array.Empty<string>();
				_bsBySlot.Clear();
				_bsCacheValid = true;
				_lastBaseRecipeId = 0;
				return;
			}

			// Pull the baseRaceRecipe reference
			var baseRecipeProp = serializedObject.FindProperty("baseRaceRecipe");
			var baseRecipe = baseRecipeProp != null ? baseRecipeProp.objectReferenceValue as UMARecipeBase : null;
			UMAObjectId recipeId = baseRecipe != null ? baseRecipe.GetUmaObjectId() : 0;
			if (_bsCacheValid && recipeId == _lastBaseRecipeId)
			{
				return;
			}

			_bsSlotNames = Array.Empty<string>();
			_bsBySlot.Clear();
			_bsCacheValid = true;
			_lastBaseRecipeId = recipeId;

			if (baseRecipe == null)
			{
				return;
			}
			var cached = baseRecipe.GetCachedRecipe();
			if (cached == null)
			{
				return;
			}
			var slots = cached.GetAllSlots();
			if (slots == null)
			{
				return;
			}
			var slotNames = new List<string>();
			for (int i = 0; i < slots.Length; i++)
			{
				var sd = slots[i];
				if (sd == null || sd.asset == null || UMAMeshData.IsNullOrEmptyMeshData(sd.asset.meshData)) continue;
				var md = sd.asset.meshData;
				var shapes = md.blendShapes;
				if (shapes == null || shapes.Length == 0) continue;
				// collect unique names for this slot
				var names = new List<string>();
				for (int s = 0; s < shapes.Length; s++)
				{
					var sh = shapes[s];
					if (sh == null || string.IsNullOrEmpty(sh.shapeName)) continue;
					if (!names.Contains(sh.shapeName)) names.Add(sh.shapeName);
				}
				if (names.Count == 0) continue;
				_bsBySlot[sd.slotName] = names.ToArray();
				slotNames.Add(sd.slotName);
			}
			slotNames.Sort(StringComparer.Ordinal);
			_bsSlotNames = slotNames.ToArray();
		// Build a global unique list of blendshape names across all slots
		var allNamesSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (var kvp in _bsBySlot)
		{
			var arr = kvp.Value;
			if (arr == null) continue;
			for (int n = 0; n < arr.Length; n++)
			{
				if (!string.IsNullOrEmpty(arr[n])) allNamesSet.Add(arr[n]);
			}
		}
		var allNamesList = new List<string>(allNamesSet);
		allNamesList.Sort(StringComparer.Ordinal);
		_allBlendshapeNames = allNamesList.ToArray();
		// reset indices if out of range
		_prebakeAddSlotIndex = Mathf.Clamp(_prebakeAddSlotIndex, 0, Math.Max(0, _bsSlotNames.Length - 1));
		_unbakedAddSlotIndex = Mathf.Clamp(_unbakedAddSlotIndex, 0, Math.Max(0, _bsSlotNames.Length - 1));
		_prebakeAddShapeIndex = 0;
		_unbakedAddShapeIndex = 0;
	}

	public static Vector3 StaticBounds= new Vector3(0.75f, 1f, 0.5f);
	public static Vector3 StaticBoundsCenter = new Vector3(0, 1f, 0);

	public void DoUtilitiesGUI()
	{
		EnsureBlendshapeCache();
		GUILayout.BeginHorizontal();
		if (_allBlendshapeNames == null || _allBlendshapeNames.Length == 0)
		{
			EditorGUILayout.LabelField("No blendshapes found in baseRaceRecipe");
		}
		else
		{
			_selectedExtractBlendshapeIndex = EditorGUILayout.Popup("Blendshape", _selectedExtractBlendshapeIndex, _allBlendshapeNames);
		}
		GUILayout.EndHorizontal();
		if (_allBlendshapeNames != null && _allBlendshapeNames.Length > 0)
		{
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Extract to MeshModifier"))
			{
				ExtractBlendshapeToMeshModifier();
			}
			if (GUILayout.Button("Extract all"))
			{
				ExtractAllBlendshapesToMeshModifiers();
			}
			GUILayout.EndHorizontal();
		}
	}

	private void ExtractBlendshapeToMeshModifier()
	{
		var blendName = _allBlendshapeNames[Mathf.Clamp(_selectedExtractBlendshapeIndex, 0, _allBlendshapeNames.Length - 1)];
		var mm = ScriptableObject.CreateInstance<UMA.MeshModifier>();
		mm.EditorModifiers = new List<UMA.MeshModifier.Modifier>();
		
		var baseRecipeProp = serializedObject.FindProperty("baseRaceRecipe");
		var baseRecipe = baseRecipeProp != null ? baseRecipeProp.objectReferenceValue as UMARecipeBase : null;
		if (baseRecipe != null)
		{
			var cached = baseRecipe.GetCachedRecipe();
			if (cached != null)
			{
				var slots = cached.GetAllSlots();
				if (slots != null)
				{
					foreach (var sd in slots)
					{
						if (sd == null || sd.asset == null || UMAMeshData.IsNullOrEmptyMeshData(sd.asset.meshData)) continue;
						var md = sd.asset.meshData;
						var shapes = md.blendShapes;
						if (shapes == null || shapes.Length == 0) continue;
						
						UMABlendShape foundShape = null;
						foreach (var bs in shapes)
						{
							if (bs != null && bs.shapeName == blendName)
							{
								foundShape = bs;
								break;
							}
						}
						if (foundShape == null) continue;
						if (TryCreateBlendshapeModifier(sd, foundShape, blendName, sd.slotName, out var newMod))
						{
							mm.EditorModifiers.Add(newMod);
						}
					}
				}
			}
		}

		if (mm.EditorModifiers.Count == 0)
		{
			DestroyImmediate(mm);
			EditorUtility.DisplayDialog("MeshModifier", "No contributing vertices were found for the selected blendshape.", "OK");
			return;
		}
		
		mm.SyncRuntimeModifiersFromEditorModifiers();
		var defaultName = ("MeshModifier_" + race.raceName + "_" + blendName).Replace(' ', '_') + ".asset";
		var path = EditorUtility.SaveFilePanelInProject("Save MeshModifier", defaultName, "asset", "Save MeshModifier asset");
		if (!string.IsNullOrEmpty(path))
		{
			AssetDatabase.CreateAsset(mm, path);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			EditorUtility.FocusProjectWindow();
			Selection.activeObject = mm;
			EditorUtility.DisplayDialog("MeshModifier Created", $"MeshModifier created at {path}", "OK");
		}
	}

	private void ExtractAllBlendshapesToMeshModifiers()
	{
		var baseRecipeProp = serializedObject.FindProperty("baseRaceRecipe");
		var baseRecipe = baseRecipeProp != null ? baseRecipeProp.objectReferenceValue as UMARecipeBase : null;
		if (baseRecipe == null)
		{
			EditorUtility.DisplayDialog("Extract All", "No baseRaceRecipe assigned.", "OK");
			return;
		}

		var cached = baseRecipe.GetCachedRecipe();
		if (cached == null)
		{
			EditorUtility.DisplayDialog("Extract All", "Could not load cached recipe.", "OK");
			return;
		}

		// Determine a sensible starting folder (next to the base recipe asset)
		string startFolder = Application.dataPath;
		string recipePath = AssetDatabase.GetAssetPath(baseRecipe);
		if (!string.IsNullOrEmpty(recipePath))
		{
			string recipeFolder = Path.GetDirectoryName(recipePath);
			if (!string.IsNullOrEmpty(recipeFolder))
			{
				string projectRoot = Directory.GetParent(Application.dataPath).FullName;
				startFolder = Path.GetFullPath(Path.Combine(projectRoot, recipeFolder));
			}
		}

		string selectedFolder = EditorUtility.SaveFolderPanel("Select folder for extracted MeshModifiers", startFolder, "");
		if (string.IsNullOrEmpty(selectedFolder))
			return;

		string relativeFolder = FileUtil.GetProjectRelativePath(selectedFolder).Replace("\\", "/");
		if (string.IsNullOrEmpty(relativeFolder) || !relativeFolder.StartsWith("Assets", StringComparison.Ordinal))
		{
			EditorUtility.DisplayDialog("Invalid Folder", "Please choose a folder inside this Unity project.", "OK");
			return;
		}

		var slots = cached.GetAllSlots();
		if (slots == null)
		{
			EditorUtility.DisplayDialog("Extract All", "No slots found in the recipe.", "OK");
			return;
		}

		int createdCount = 0;
		AssetDatabase.StartAssetEditing();
		try
		{
			foreach (string blendName in _allBlendshapeNames)
			{
				var mm = ScriptableObject.CreateInstance<UMA.MeshModifier>();
				mm.EditorModifiers = new List<UMA.MeshModifier.Modifier>();

				foreach (var sd in slots)
				{
					if (sd == null || sd.asset == null || UMAMeshData.IsNullOrEmptyMeshData(sd.asset.meshData)) continue;
					var shapes = sd.asset.meshData.blendShapes;
					if (shapes == null || shapes.Length == 0) continue;

					UMABlendShape foundShape = null;
					foreach (var bs in shapes)
					{
						if (bs != null && bs.shapeName == blendName) { foundShape = bs; break; }
					}
					if (foundShape == null) continue;
						if (TryCreateBlendshapeModifier(sd, foundShape, blendName, sd.asset.sourceSlot, out var newMod))
						{
							mm.EditorModifiers.Add(newMod);
						}
				}

				if (mm.EditorModifiers.Count == 0)
				{
					DestroyImmediate(mm);
					continue;
				}

				mm.SyncRuntimeModifiersFromEditorModifiers();
				string fileName = (race.raceName + "_" + blendName).Replace(' ', '_') + ".asset";
				string assetPath = AssetDatabase.GenerateUniqueAssetPath((relativeFolder + "/" + fileName).Replace("\\", "/"));
				AssetDatabase.CreateAsset(mm, assetPath);
				createdCount++;
			}
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}

		EditorUtility.DisplayDialog("Extract All", $"Created {createdCount} MeshModifier assets.", "OK");
	}

    private static void AppendExpressionTargetWarnings(
        RaceData targetRace,
        List<ExpressionValidationMessage> results)
    {
        if (targetRace == null || targetRace.expressionGroup == null ||
            targetRace.expressionGroup.expressions == null) return;

        var bones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blendShapes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var overlays =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sharedColors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (targetRace.TPose != null)
        {
            if (targetRace.TPose.boneInfo == null &&
                targetRace.TPose.serializedChunk != null &&
                targetRace.TPose.serializedChunk.Length > 0)
                targetRace.TPose.DeSerialize();
            if (targetRace.TPose.boneInfo != null)
                for (int i = 0; i < targetRace.TPose.boneInfo.Length; i++)
                    bones.Add(targetRace.TPose.boneInfo[i].name);
        }

        UMAData.UMARecipe recipe = null;
        if (targetRace.baseRaceRecipe != null)
        {
            try
            {
                recipe = targetRace.baseRaceRecipe.GetCachedRecipe();
            }
            catch (Exception exception)
            {
                results.Add(new ExpressionValidationMessage(
                    ExpressionValidationSeverity.Warning,
                    "Could not inspect base recipe expression targets: " +
                    exception.Message));
            }
        }
        if (recipe != null)
        {
            if (recipe.sharedColors != null)
                for (int i = 0; i < recipe.sharedColors.Length; i++)
                    if (recipe.sharedColors[i] != null)
                        sharedColors.Add(recipe.sharedColors[i].name);
            SlotData[] slots = recipe.GetAllSlots();
            if (slots != null)
                for (int slotIndex = 0;
                     slotIndex < slots.Length;
                     slotIndex++)
                {
                    SlotData slot = slots[slotIndex];
                    if (slot == null) continue;
                    if (slot.asset != null &&
                        slot.asset.meshData != null &&
                        slot.asset.meshData.blendShapes != null)
                        for (int shapeIndex = 0;
                             shapeIndex <
                             slot.asset.meshData.blendShapes.Length;
                             shapeIndex++)
                        {
                            UMABlendShape shape =
                                slot.asset.meshData
                                    .blendShapes[shapeIndex];
                            if (shape != null)
                                blendShapes.Add(shape.shapeName);
                        }
                    List<OverlayData> slotOverlays =
                        slot.GetOverlayList();
                    if (slotOverlays == null) continue;
                    for (int overlayIndex = 0;
                         overlayIndex < slotOverlays.Count;
                         overlayIndex++)
                    {
                        OverlayData overlay =
                            slotOverlays[overlayIndex];
                        if (overlay != null && overlay.asset != null)
                            overlays.Add(overlay.overlayName);
                    }
                }
        }

        for (int definitionIndex = 0;
             definitionIndex <
             targetRace.expressionGroup.expressions.Count;
             definitionIndex++)
        {
            UMAExpressionDefinition definition =
                targetRace.expressionGroup.expressions[definitionIndex];
            if (definition?.dna?.effects == null) continue;
            for (int effectIndex = 0;
                 effectIndex < definition.dna.effects.Count;
                 effectIndex++)
            {
                DNAEffect effect = definition.dna.effects[effectIndex];
                if (effect == null || !effect.enabled) continue;
                if (bones.Count > 0)
                    ValidateExpressionBoneTarget(effect, bones,
                        definition, definitionIndex, results);
                if (effect is DNAEffect_BlendShape blendShape &&
                    blendShapes.Count > 0 &&
                    !blendShapes.Contains(blendShape.BlendShapeName))
                    AddMissingExpressionTarget(results, definition,
                        definitionIndex, "blendshape",
                        blendShape.BlendShapeName);
                if (effect is DNAEffect_OverlayUVTransform uv &&
                    overlays.Count > 0 &&
                    !overlays.Contains(uv.overlayName))
                    AddMissingExpressionTarget(results, definition,
                        definitionIndex, "overlay", uv.overlayName);

                string sharedColor = null;
                if (effect is DNAEffect_SharedColor color)
                    sharedColor = color.sharedColorName;
                else if (effect is DNAEffect_SharedColorChannel channel)
                    sharedColor = channel.SharedColorName;
                else if (effect is DNAEffect_SharedColorProperty property)
                    sharedColor = property.sharedColorName;
                else if (effect is
                    DNAEffect_RuntimeMaterialProperty runtime)
                    sharedColor = runtime.sharedColorName;
                if (!string.IsNullOrWhiteSpace(sharedColor) &&
                    sharedColors.Count > 0 &&
                    !sharedColors.Contains(sharedColor))
                    AddMissingExpressionTarget(results, definition,
                        definitionIndex, "shared color", sharedColor);
            }
        }
    }

    private static void ValidateExpressionBoneTarget(
        DNAEffect effect,
        HashSet<string> bones,
        UMAExpressionDefinition definition,
        int definitionIndex,
        List<ExpressionValidationMessage> results)
    {
        if (effect is DNAEffect_BonePose pose &&
            pose.bonePose?.poses != null)
        {
            for (int i = 0; i < pose.bonePose.poses.Length; i++)
            {
                UMA.PoseTools.UMABonePose.PoseBone poseBone =
                    pose.bonePose.poses[i];
                if (poseBone != null && poseBone.enabled &&
                    !bones.Contains(poseBone.bone))
                    AddMissingExpressionTarget(results, definition,
                        definitionIndex, "bone", poseBone.bone);
            }
            return;
        }

        string bone = null;
        if (effect is DNAEffect_BoneRotate rotate)
            bone = rotate.BoneName;
        else if (effect is DNAEffect_BoneTranslate translate)
            bone = translate.BoneName;
        else if (effect is DNAEffect_BoneScale scale)
            bone = scale.BoneName;
        else if (effect is DNAEffect_BoneTransform transform)
            bone = transform.boneName;
        if (!string.IsNullOrWhiteSpace(bone) && !bones.Contains(bone))
            AddMissingExpressionTarget(results, definition,
                definitionIndex, "bone", bone);
    }

    private static void AddMissingExpressionTarget(
        List<ExpressionValidationMessage> results,
        UMAExpressionDefinition definition,
        int definitionIndex,
        string targetType,
        string targetName)
    {
        results.Add(new ExpressionValidationMessage(
            ExpressionValidationSeverity.Warning,
            "Expression '" + definition.id + "' references " +
            targetType + " '" + targetName +
            "', which is not present in the race's base assets. " +
            "It may be supplied by wardrobe at runtime.",
            definitionIndex));
    }

	public override void OnInspectorGUI()
		{
			if (lastActionTime == 0)
			{
				lastActionTime = Time.realtimeSinceStartup;
			}

			EditorGUI.BeginChangeCheck();
			if (!string.IsNullOrEmpty(race._oldRaceName))
			{
				EditorGUILayout.HelpBox("This race is using the old racename and should be cleared. The old racename is only used for backwards compatibility when loading old recipes that reference it", MessageType.Warning);
				race._oldRaceName = EditorGUILayout.TextField("Legacy Name", race.raceName);
            }
			race.umaTarget = (UMA.RaceData.UMATarget)EditorGUILayout.EnumPopup(new GUIContent("UMA Target", "The Mecanim animation rig type."), race.umaTarget);
			race.genericRootMotionTransformName = EditorGUILayout.TextField("Root Motion Transform", race.genericRootMotionTransformName);
			race.TPose = EditorGUILayout.ObjectField(new GUIContent("T-Pose", "The UMA T-Pose asset can be created by selecting the race fbx and choosing the Extract T-Pose dropdown. Only needs to be done once per race."), race.TPose, typeof(UmaTPose), false) as UmaTPose;
			race.expressionSet = EditorGUILayout.ObjectField(new GUIContent("Expression Set", "The Expression Set asset is used by the Expression player."), race.expressionSet, typeof(UMA.PoseTools.UMAExpressionSet), false) as UMA.PoseTools.UMAExpressionSet;
            race.expressionGroup = EditorGUILayout.ObjectField(
                new GUIContent(
                    "Expression Group",
                    "DNA-based expressions used by DynamicExpressionPlayer. " +
                    "When assigned, this is preferred over the legacy Expression Set."),
                race.expressionGroup,
                typeof(UMAExpressionGroup),
                false) as UMAExpressionGroup;
            if (race.expressionGroup != null &&
                race.expressionSet != null)
            {
                EditorGUILayout.HelpBox(
                    "Both expression systems are assigned. " +
                    "DynamicExpressionPlayer uses Expression Group; " +
                    "UMAExpressionPlayer continues to use Expression Set.",
                    MessageType.Info);
            }
            if (race.expressionGroup != null)
            {
                var expressionValidation =
                    new List<ExpressionValidationMessage>();
                race.expressionGroup.Validate(expressionValidation);
                AppendExpressionTargetWarnings(race,
                    expressionValidation);
                for (int i = 0; i < expressionValidation.Count; i++)
                {
                    ExpressionValidationMessage message =
                        expressionValidation[i];
                    if (message.severity ==
                        ExpressionValidationSeverity.Info)
                    {
                        continue;
                    }
                    MessageType messageType = message.severity ==
                        ExpressionValidationSeverity.Error
                            ? MessageType.Error
                            : MessageType.Warning;
                    EditorGUILayout.HelpBox(
                        "Expression Group: " + message.message,
                        messageType);
                }
            }
			EditorGUILayout.HelpBox("Fixup Rotations should be true for Blender FBX slots", MessageType.Info);
			race.FixupRotations = EditorGUILayout.Toggle("Fixup Rotations", race.FixupRotations);

			// Renderer Bounds section
			EditorGUILayout.Space();
			GUILayout.Label("Renderer Bounds", EditorStyles.boldLabel);
			SerializedProperty useManualBoundsProp = serializedObject.FindProperty("useManualRendererBounds");
			SerializedProperty manualBoundsProp = serializedObject.FindProperty("manualRendererBounds");
			SerializedProperty manualBoundsCenterProp = serializedObject.FindProperty("manualRendererBoundsCenter");
			EditorGUILayout.PropertyField(useManualBoundsProp, new GUIContent("Use Manual Renderer Bounds", "When enabled, UMA renderers will use these manual bounds (extents) instead of calculated bounds."));
			using (new EditorGUI.DisabledScope(!useManualBoundsProp.boolValue))
			{
				EditorGUILayout.PropertyField(manualBoundsProp, new GUIContent("Manual Bounds (Extents)", "Extents in local space before scaling by the 'Position' bone."));
				EditorGUILayout.PropertyField(manualBoundsCenterProp, new GUIContent("Manual Bounds Center", "Center offset in local space before scaling by the 'Position' bone."));
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Copy Bounds"))
				{
					StaticBounds = manualBoundsProp.vector3Value;
					StaticBoundsCenter = manualBoundsCenterProp.vector3Value;
				}
				if (GUILayout.Button("Paste Bounds"))
				{
					manualBoundsProp.vector3Value = StaticBounds;
					manualBoundsCenterProp.vector3Value = StaticBoundsCenter;
					serializedObject.ApplyModifiedProperties();
					EditorUtility.SetDirty(race);
					_needsUpdate = true;
				}
				GUILayout.EndHorizontal();				
			}
			EditorGUILayout.Space();
			SerializedProperty useNewDNA = serializedObject.FindProperty("useNewDNA");
			EditorGUILayout.PropertyField(useNewDNA, new GUIContent("Use New DNA System", "When enabled, the new DNA system using DNA Collections will be used. Otherwise the legacy DNA Converter system will be used."));

            if (useNewDNA.boolValue)
			{
                EditorGUILayout.PropertyField(serializedObject.FindProperty("DNACollection"));
			}
			else
			{
				EditorGUILayout.PropertyField(serializedObject.FindProperty("disableDNAConverters"));
				SerializedProperty dnaConverterListprop = serializedObject.FindProperty("_dnaConverterList");
				EditorGUILayout.PropertyField(dnaConverterListprop, true);
				SerializedProperty dnaRanges = serializedObject.FindProperty("dnaRanges");
				EditorGUILayout.PropertyField(dnaRanges, true);
			}

			showRaceGeneration = EditorGUILayout.Foldout(showRaceGeneration, "Race Generation");
			if (showRaceGeneration)
			{
				EditorGUILayout.HelpBox("Force Rebuild Race Slots should only be enabled for testing during design phase! it forces the slots to be rebuilt every generation!", MessageType.Warning);
				SerializedProperty ForceRebuildRaceSlots = serializedObject.FindProperty("forceRebuildRaceSlots");
                ForceRebuildRaceSlots.boolValue = EditorGUILayout.Toggle(new GUIContent("Force Rebuild Race Slots", "If true, the race slots will be rebuilt when characters are generated."), ForceRebuildRaceSlots.boolValue);
                // Prebaked Blendshapes list
                DrawPrebakedBlendshapeList();

				// Unbaked Shapes To Include list
				DrawUnbakedShapesToIncludeList();
			}

			// Utilities section
			showUtilities = EditorGUILayout.Foldout(showUtilities, "Utilities");
			if (showUtilities)
			{
				DoUtilitiesGUI();
			}

			/* tags GUI */
			SerializedProperty tags = serializedObject.FindProperty("tags");
			EditorGUILayout.PropertyField(tags, true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				_needsUpdate = true;
			}

			foreach (var field in race.GetType().GetFields())
			{
				foreach (var attribute in System.Attribute.GetCustomAttributes(field))
				{
					if (attribute is UMAAssetFieldVisible)
					{
						SerializedProperty serializedProp = serializedObject.FindProperty(field.Name);
						EditorGUI.BeginChangeCheck();
						EditorGUILayout.PropertyField(serializedProp);
						if (EditorGUI.EndChangeCheck())
						{
							serializedObject.ApplyModifiedProperties();
							_needsUpdate = true;
						}
						break;
					}
				}
			}

			#region Validation
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Validate RaceData"))
			{
				ValidationMessages.Clear();
				DoValidate();
			}
			if (GUILayout.Button(" Clear Messages "))
			{
				ValidationMessages.Clear();
			}
			GUILayout.EndHorizontal();
			if (ValidationMessages.Count > 0)
			{
				// draw the validation messages one by one, with a little space between them. Each message should be in a helpbox.
				// if the message starts with "Error:" it should be a error helpbox, if it starts with "Warning:" it should be a warning helpbox, otherwise it should be an info helpbox.
				// put an "x" button on each message to clear it.
				List<string> displayedMessages = new List<string>();
				displayedMessages.AddRange(ValidationMessages);
				GUILayout.Label("Validation Messages:", EditorStyles.boldLabel);
				for (int i = displayedMessages.Count - 1; i >= 0; i--)
				{
					MessageType messageType = MessageType.Info;
					if (displayedMessages[i].StartsWith("Error:"))
					{
						messageType = MessageType.Error;
					}
					else if (displayedMessages[i].StartsWith("Warning:"))
					{
						messageType = MessageType.Warning;
					}
					GUILayout.BeginHorizontal();
					EditorGUILayout.HelpBox(displayedMessages[i], messageType);
					if (GUILayout.Button("x", GUILayout.Width(20)))
					{
						ValidationMessages.RemoveAt(i);
					}
					GUILayout.EndHorizontal();
				}
			}
			else
			{
				EditorGUILayout.HelpBox("No validation messages.", MessageType.Info);
			}

			void DoValidate()
			{
				ValidationMessages.AddRange(UMARaceValidation.GetInspectorMessages(target as RaceData));
			}

			#endregion
			try
			{
				PreInspectorGUI(ref _needsUpdate);
				if (_needsUpdate == true) {
					_needsUpdate = false;
					DoUpdate();
				}
			} catch (UMAResourceNotFoundException e) {
				_errorMessage = e.Message;
			}

			if (GUI.changed)
			{
				doSave = true;
				lastActionTime = Time.realtimeSinceStartup;
				UMAAssetIndexer.RebuildUMAS(SceneManager.GetActiveScene());
			}
		}

		/// <summary>
		/// Add to this method in extender editors if you need to do anything extra when updating the data.
		/// </summary>
		protected virtual void DoUpdate()
		{
			serializedObject.ApplyModifiedProperties();
			EditorUtility.SetDirty(race);
			AssetDatabase.SaveAssetIfDirty(race);
			RaceData ra = UMAAssetIndexer.Instance.GetAsset<RaceData>(race.raceName);
			if (ra != null)
			{
				UMAUpdateProcessor.UpdateRace(ra);
			}
		}

		private bool TryCreateBlendshapeModifier(SlotData sd, UMABlendShape foundShape, string blendName, string slotName, out UMA.MeshModifier.Modifier modifier)
		{
			modifier = null;
			if (sd == null || foundShape == null || foundShape.frames == null || foundShape.frames.Length == 0)
			{
				return false;
			}

			var frame = foundShape.frames[foundShape.frames.Length - 1];
			if (frame == null || frame.deltaVertices == null || frame.deltaVertices.Length == 0)
			{
				return false;
			}

			var adjustments = new VertexBlendshapeAdjustmentCollection();
			for (int i = 0; i < frame.deltaVertices.Length; i++)
			{
				var delta = frame.deltaVertices[i];
				if (delta == Vector3.zero)
				{
					continue;
				}

				var vba = new VertexBlendshapeAdjustment();
				vba.vertexIndex = i;
				vba.slotName = slotName;
				vba.delta = delta;
				vba.tangent = frame.HasTangents() ? frame.deltaTangents[i] : Vector3.zero;
				vba.normal = frame.HasNormals() ? frame.deltaNormals[i] : Vector3.zero;
				adjustments.Add(vba);
			}

			if (adjustments.Count() == 0)
			{
				return false;
			}

			modifier = new UMA.MeshModifier.Modifier();
			modifier.ModifierName = blendName;
			modifier.DNAName = string.Empty;
			modifier.Scale = 1.0f;
			modifier.SlotName = slotName;
			modifier.keepAsIs = true;
			modifier.adjustments = adjustments;
			modifier.TemplateAdjustment = new VertexBlendshapeAdjustment();
			return true;
		}

		#region DCS functions
		// Drop area for Backwards Compatible Races
		private void CompatibleRacesDropArea(Rect dropArea, SerializedProperty crossCompatibilitySettingsData)
		{
			Event evt = Event.current;
			//make the box clickable so that the user can select raceData assets from the asset selection window
			if (evt.type == EventType.MouseUp)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					compatibleRacePickerID = EditorGUIUtility.GetControlID(new GUIContent("crfObjectPicker"), FocusType.Passive);
					EditorGUIUtility.ShowObjectPicker<RaceData>(null, false, "", compatibleRacePickerID);
					Event.current.Use();//stops the Mismatched LayoutGroup errors
					return;
				}
			}
			if (evt.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == compatibleRacePickerID)
			{
				RaceData tempRaceDataAsset = EditorGUIUtility.GetObjectPickerObject() as RaceData;
				if (tempRaceDataAsset)
				{
					AddRaceDataAsset(tempRaceDataAsset, crossCompatibilitySettingsData);
				}
				if (Event.current.type != EventType.Layout)
				{
					Event.current.Use();//stops the Mismatched LayoutGroup errors
				}

				return;
			}
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
							RaceData tempRaceDataAsset = draggedObjects[i] as RaceData;
							if (tempRaceDataAsset)
							{
								AddRaceDataAsset(tempRaceDataAsset, crossCompatibilitySettingsData);
								continue;
							}

							var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
							if (System.IO.Directory.Exists(path))
							{
								RecursiveScanFoldersForAssets(path, crossCompatibilitySettingsData);
							}
						}
					}
				}
			}
		}

		private void InitPrebakedBlendshapeList()
		{
			var listProp = serializedObject.FindProperty("PrebakedBlendshapes");
			prebakedBlendshapeList = new ReorderableList(serializedObject, listProp, true, true, true, true);
			prebakedBlendshapeList.drawHeaderCallback = rect =>
			{
				EditorGUI.LabelField(rect, "Prebaked Blendshapes");
			};
			prebakedBlendshapeList.elementHeight = EditorGUIUtility.singleLineHeight + 6;
			prebakedBlendshapeList.drawElementCallback = (rect, index, isActive, isFocused) =>
			{
				var element = prebakedBlendshapeList.serializedProperty.GetArrayElementAtIndex(index);
				var blendShapeProp = element.FindPropertyRelative("BlendShape");
				var valueProp = element.FindPropertyRelative("value");

				rect.y += 2;
				float half = (rect.width - 20f) * 0.6f;
				var nameRect = new Rect(rect.x + 10, rect.y, half, EditorGUIUtility.singleLineHeight);
				var valueRect = new Rect(nameRect.xMax + 5, rect.y, rect.width - nameRect.width - 25f, EditorGUIUtility.singleLineHeight);

				EditorGUI.PropertyField(nameRect, blendShapeProp, GUIContent.none);
				EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none);
			};
			prebakedBlendshapeList.onAddCallback = l =>
			{
				var idx = l.serializedProperty.arraySize;
				l.serializedProperty.InsertArrayElementAtIndex(idx);
				var el = l.serializedProperty.GetArrayElementAtIndex(idx);
				el.FindPropertyRelative("BlendShape").stringValue = string.Empty;
				el.FindPropertyRelative("value").floatValue = 0f;
				serializedObject.ApplyModifiedProperties();
			};
			prebakedBlendshapeListInitialized = true;
		}

		private void DrawPrebakedBlendshapeList()
		{
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f));
            if (!prebakedBlendshapeListInitialized || prebakedBlendshapeList == null)
			{
				InitPrebakedBlendshapeList();
			}
			EditorGUI.BeginChangeCheck();
			prebakedBlendshapeList.DoLayoutList();
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				_needsUpdate = true;
			}

			// Add-from-slot UI
			EnsureBlendshapeCache();
			GUI.enabled = (_bsSlotNames.Length > 0);
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PrefixLabel("Add from Base Recipe");
			if (_bsSlotNames.Length == 0)
			{
				EditorGUILayout.LabelField("No slots with blendshapes found.");
			}
			else
			{
				_prebakeAddSlotIndex = EditorGUILayout.Popup(_prebakeAddSlotIndex, _bsSlotNames, GUILayout.MaxWidth(220));
				var slotName = _bsSlotNames.Length > 0 ? _bsSlotNames[Mathf.Clamp(_prebakeAddSlotIndex, 0, _bsSlotNames.Length - 1)] : null;
				string[] shapes;
				if (!string.IsNullOrEmpty(slotName) && _bsBySlot.TryGetValue(slotName, out var arr0))
				{
					shapes = arr0;
				}
				else
				{
					shapes = Array.Empty<string>();
				}
				_prebakeAddShapeIndex = Mathf.Clamp(_prebakeAddShapeIndex,0, Math.Max(0, shapes.Length -1));
				_prebakeAddShapeIndex = EditorGUILayout.Popup(_prebakeAddShapeIndex, shapes, GUILayout.MaxWidth(260));
				EditorGUI.BeginDisabledGroup(shapes.Length ==0);
				if (GUILayout.Button("Add", GUILayout.Width(60)))
				{
					var shapeName = shapes[_prebakeAddShapeIndex];
					var listProp = serializedObject.FindProperty("PrebakedBlendshapes");
					// prevent duplicates in the prebaked list
					bool exists = false;
					for (int i =0; i < listProp.arraySize; i++)
					{
						var el = listProp.GetArrayElementAtIndex(i);
						var n = el.FindPropertyRelative("BlendShape");
						if (n != null && n.stringValue == shapeName) { exists = true; break; }
					}
					if (!exists)
					{
						int idx = listProp.arraySize;
						listProp.InsertArrayElementAtIndex(idx);
						var el = listProp.GetArrayElementAtIndex(idx);
						el.FindPropertyRelative("BlendShape").stringValue = shapeName;
						el.FindPropertyRelative("value").floatValue =0f;
					}
					bool removedFromUnbaked = RemoveExactUnbakedShapeToInclude(shapeName) > 0;
					if (!exists || removedFromUnbaked)
					{
						serializedObject.ApplyModifiedProperties();
						_needsUpdate = true;
					}
				}
				// Add all missing shapes from the selected slot
				if (GUILayout.Button("Add all from slot", GUILayout.Width(150)))
				{
					var listProp = serializedObject.FindProperty("PrebakedBlendshapes");
					// Build set of existing prebaked names
					var existing = new HashSet<string>(StringComparer.Ordinal);
					for (int i =0; i < listProp.arraySize; i++)
					{
						var el = listProp.GetArrayElementAtIndex(i);
						var n = el.FindPropertyRelative("BlendShape");
						if (n != null && !string.IsNullOrEmpty(n.stringValue)) existing.Add(n.stringValue);
					}
					int added =0;
					int removed =0;
					for (int s =0; s < shapes.Length; s++)
					{
						var shapeName = shapes[s];
						if (string.IsNullOrEmpty(shapeName)) continue;
						if (!existing.Contains(shapeName))
						{
							int idx = listProp.arraySize;
							listProp.InsertArrayElementAtIndex(idx);
							var el = listProp.GetArrayElementAtIndex(idx);
							el.FindPropertyRelative("BlendShape").stringValue = shapeName;
							el.FindPropertyRelative("value").floatValue =0f;
							existing.Add(shapeName);
							added++;
						}
						removed += RemoveExactUnbakedShapeToInclude(shapeName);
					}
					if (added >0 || removed >0)
					{
						serializedObject.ApplyModifiedProperties();
						_needsUpdate = true;
					}
				}
				EditorGUI.EndDisabledGroup();
			}
			EditorGUILayout.EndHorizontal();
			GUI.enabled = true;
			GUIHelper.EndVerticalPadded(5.0f);
        }

		private int RemoveExactUnbakedShapeToInclude(string shapeName)
		{
			if (string.IsNullOrEmpty(shapeName))
			{
				return 0;
			}

			var unbakedList = serializedObject.FindProperty("UnbakedShapesToInclude");
			if (unbakedList == null)
			{
				return 0;
			}

			int removed = 0;
			for (int i = unbakedList.arraySize - 1; i >= 0; i--)
			{
				var element = unbakedList.GetArrayElementAtIndex(i);
				if (element != null && element.stringValue == shapeName)
				{
					unbakedList.DeleteArrayElementAtIndex(i);
					removed++;
				}
			}

			return removed;
		}

		private void InitUnbakedShapesToIncludeList()
		{
			var listProp = serializedObject.FindProperty("UnbakedShapesToInclude");
			unbakedShapesList = new ReorderableList(serializedObject, listProp, true, true, true, true);
			unbakedShapesList.drawHeaderCallback = rect =>
			{
				EditorGUI.LabelField(rect, "Unbaked Shapes To Include");
			};
			unbakedShapesList.elementHeightCallback = index =>
			{
				if (unbakedShapesList.serializedProperty == null || index < 0 || index >= unbakedShapesList.serializedProperty.arraySize)
					return EditorGUIUtility.singleLineHeight + 6;
				var el = unbakedShapesList.serializedProperty.GetArrayElementAtIndex(index);
				var str = el != null ? el.stringValue : string.Empty;
				if (string.IsNullOrEmpty(str)) return EditorGUIUtility.singleLineHeight + 6;
				// Try to validate as regex; if invalid, add a second line for warning
				try { _ = new Regex(str); }
				catch (ArgumentException) { return (EditorGUIUtility.singleLineHeight * 2f) + 10f; }
				return EditorGUIUtility.singleLineHeight + 6;
			};
			unbakedShapesList.drawElementCallback = (rect, index, isActive, isFocused) =>
			{
				var element = unbakedShapesList.serializedProperty.GetArrayElementAtIndex(index);
				rect.y += 2;
				var textRect = new Rect(rect.x + 10, rect.y, rect.width - 10, EditorGUIUtility.singleLineHeight);
				EditorGUI.BeginChangeCheck();
				var newVal = EditorGUI.TextField(textRect, GUIContent.none, element.stringValue);
				if (EditorGUI.EndChangeCheck())
				{
					element.stringValue = newVal ?? string.Empty;
				}
				if (!string.IsNullOrEmpty(element.stringValue))
				{
					try { _ = new Regex(element.stringValue); }
					catch (ArgumentException ex)
					{
						var warnRect = new Rect(rect.x + 10, textRect.yMax + 2, rect.width - 10, EditorGUIUtility.singleLineHeight);
						EditorGUI.HelpBox(warnRect, $"Invalid Regex: {ex.Message}", MessageType.Warning);
					}
				}
			};
			unbakedShapesList.onAddCallback = l =>
			{
				var idx = l.serializedProperty.arraySize;
				l.serializedProperty.InsertArrayElementAtIndex(idx);
				var el = l.serializedProperty.GetArrayElementAtIndex(idx);
				el.stringValue = string.Empty;
				serializedObject.ApplyModifiedProperties();
			};
			unbakedShapesListInitialized = true;
		}

		private void DrawUnbakedShapesToIncludeList()
		{
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f));
            if (!unbakedShapesListInitialized || unbakedShapesList == null)
			{
				InitUnbakedShapesToIncludeList();
			}
			EditorGUILayout.HelpBox(
			"Enter exact blendshape names to include when not prebaked, or use Regular Expressions to match multiple shapes. Examples: '^Eye.*', '.*Smile.*'. Invalid regex patterns will be highlighted.",
			MessageType.Info);
			EditorGUI.BeginChangeCheck();
			unbakedShapesList.DoLayoutList();
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				_needsUpdate = true;
			}

			// Add-from-slot UI
			EnsureBlendshapeCache();
			GUI.enabled = (_bsSlotNames.Length > 0);
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PrefixLabel("Add from Base Recipe");
			if (_bsSlotNames.Length == 0)
			{
				EditorGUILayout.LabelField("No slots with blendshapes found.");
			}
			else
			{
				_unbakedAddSlotIndex = EditorGUILayout.Popup(_unbakedAddSlotIndex, _bsSlotNames, GUILayout.MaxWidth(220));
				var slotName = _bsSlotNames.Length > 0 ? _bsSlotNames[Mathf.Clamp(_unbakedAddSlotIndex, 0, _bsSlotNames.Length - 1)] : null;
				string[] shapes;
				if (!string.IsNullOrEmpty(slotName) && _bsBySlot.TryGetValue(slotName, out var arr1))
				{
					shapes = arr1;
				}
				else
				{
					shapes = Array.Empty<string>();
				}
				_unbakedAddShapeIndex = Mathf.Clamp(_unbakedAddShapeIndex,0, Math.Max(0, shapes.Length -1));
				_unbakedAddShapeIndex = EditorGUILayout.Popup(_unbakedAddShapeIndex, shapes, GUILayout.MaxWidth(260));
				EditorGUI.BeginDisabledGroup(shapes.Length ==0);
				if (GUILayout.Button("Add", GUILayout.Width(60)))
				{
					var shapeName = shapes[_unbakedAddShapeIndex];
					var listProp = serializedObject.FindProperty("UnbakedShapesToInclude");
					// prevent duplicates
					bool exists = false;
					for (int i =0; i < listProp.arraySize; i++)
					{
						var el = listProp.GetArrayElementAtIndex(i);
						if (el != null && el.stringValue == shapeName) { exists = true; break; }
					}
					// also prevent if the shape exists in PrebakedBlendshapes
					if (!exists)
					{
						var otherList = serializedObject.FindProperty("PrebakedBlendshapes");
						for (int i =0; i < otherList.arraySize; i++)
						{
							var el = otherList.GetArrayElementAtIndex(i);
							var n = el.FindPropertyRelative("BlendShape");
							if (n != null && n.stringValue == shapeName) { exists = true; break; }
						}
					}
					if (!exists)
					{
						int idx = listProp.arraySize;
						listProp.InsertArrayElementAtIndex(idx);
						var el = listProp.GetArrayElementAtIndex(idx);
						el.stringValue = shapeName;
						serializedObject.ApplyModifiedProperties();
						_needsUpdate = true;
					}
				}
				// Add all missing shapes from the selected slot
				if (GUILayout.Button("Add all from slot", GUILayout.Width(150)))
				{
					var listProp = serializedObject.FindProperty("UnbakedShapesToInclude");
					// Build set of existing names
					var existing = new HashSet<string>(StringComparer.Ordinal);
					for (int i =0; i < listProp.arraySize; i++)
					{
						var el = listProp.GetArrayElementAtIndex(i);
						if (el != null && !string.IsNullOrEmpty(el.stringValue)) existing.Add(el.stringValue);
					}
					// include names from PrebakedBlendshapes to avoid cross-adding
					var otherList = serializedObject.FindProperty("PrebakedBlendshapes");
					for (int i =0; i < otherList.arraySize; i++)
					{
						var el = otherList.GetArrayElementAtIndex(i);
						var n = el.FindPropertyRelative("BlendShape");
						if (n != null && !string.IsNullOrEmpty(n.stringValue)) existing.Add(n.stringValue);
					}
					int added =0;
					for (int s =0; s < shapes.Length; s++)
					{
						var shapeName = shapes[s];
						if (string.IsNullOrEmpty(shapeName) || existing.Contains(shapeName)) continue;
						int idx = listProp.arraySize;
						listProp.InsertArrayElementAtIndex(idx);
						var el = listProp.GetArrayElementAtIndex(idx);
						el.stringValue = shapeName;
						existing.Add(shapeName);
						added++;
					}
					if (added >0)
					{
						serializedObject.ApplyModifiedProperties();
						_needsUpdate = true;
					}
				}
				EditorGUI.EndDisabledGroup();
			}
			EditorGUILayout.EndHorizontal();
			GUI.enabled = true;
            GUIHelper.EndVerticalPadded(5.0f);
        }

		private void RecursiveScanFoldersForAssets(string path, SerializedProperty crossCompatibilitySettingsData)
		{
			var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
			foreach (var assetFile in assetFiles)
			{
				var tempRaceDataAsset = AssetDatabase.LoadAssetAtPath(assetFile, typeof(RaceData)) as RaceData;
				if (tempRaceDataAsset)
				{
					AddRaceDataAsset(tempRaceDataAsset, crossCompatibilitySettingsData);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), crossCompatibilitySettingsData);
			}
		}

		private void AddRaceDataAsset(RaceData raceDataAsset, SerializedProperty crossCompatibilitySettingsData)
		{
			var thisRace = target as RaceData;
			if (thisRace != null && raceDataAsset.raceName == thisRace.raceName)
			{
				return;
			}

			bool found = false;
			for (int i = 0; i < crossCompatibilitySettingsData.arraySize; i++)
			{
				var ccRaceName = crossCompatibilitySettingsData.GetArrayElementAtIndex(i).FindPropertyRelative("ccRace").stringValue;
				if (ccRaceName == raceDataAsset.raceName)
				{
					found = true;
				}
			}
			if (!found)
			{
				crossCompatibilitySettingsData.InsertArrayElementAtIndex(crossCompatibilitySettingsData.arraySize);
				crossCompatibilitySettingsData.GetArrayElementAtIndex(crossCompatibilitySettingsData.arraySize - 1).FindPropertyRelative("ccRace").stringValue = raceDataAsset.raceName;
				serializedObject.ApplyModifiedProperties();
			}
			//if (!compatibleRaces.Contains(raceDataAsset.raceName))
			//	compatibleRaces.Add(raceDataAsset.raceName);
		}

		/// <summary>
		/// Add to PreInspectorGUI in any derived editors to allow editing of new properties added to races.
		/// </summary>
		//partial void PreInspectorGUI(ref bool result);
		protected virtual void PreInspectorGUI(ref bool result)
		{
			if (!wardrobeSlotListInitialized)
			{
				InitWardrobeSlotList();
			}
			result = AddExtraStuff();
		}

		private void InitWardrobeSlotList()
		{
			var thisWardrobeSlotList = serializedObject.FindProperty("Regions");
			if (thisWardrobeSlotList.arraySize == 0)
			{
				race.ValidateWardrobeSlots(true);
				thisWardrobeSlotList = serializedObject.FindProperty("Regions");
			}
			wardrobeSlotList = new ReorderableList(serializedObject, thisWardrobeSlotList, true, true, true, true);
			wardrobeSlotList.drawHeaderCallback = (Rect rect) => {
				EditorGUI.LabelField(rect, "Wardrobe Regions");
			};
			wardrobeSlotList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => {
				var element = wardrobeSlotList.serializedProperty.GetArrayElementAtIndex(index);
				rect.y += 2;
				element.stringValue = EditorGUI.TextField(new Rect(rect.x + 10, rect.y, rect.width - 10, EditorGUIUtility.singleLineHeight), element.stringValue);
			};
			wardrobeSlotListInitialized = true;
		}

		public bool AddExtraStuff()
		{
			SerializedProperty baseRaceRecipe = serializedObject.FindProperty("baseRaceRecipe");
			SerializedProperty useFbxRoute = serializedObject.FindProperty("useFbxRoute");
			SerializedProperty baseFbxRenderer = serializedObject.FindProperty("baseFbxRenderer");
			SerializedProperty fbxBaseMeshHideBindings = serializedObject.FindProperty("fbxBaseMeshHideBindings");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(useFbxRoute, new GUIContent("Use FBX Route"));
			if (useFbxRoute.boolValue)
			{
				baseFbxRenderer.objectReferenceValue = EditorGUILayout.ObjectField(new GUIContent("Base FBX Renderer", "The source FBX/prefab SkinnedMeshRenderer used as the preserved base body."), baseFbxRenderer.objectReferenceValue, typeof(SkinnedMeshRenderer), false);
				EditorGUILayout.PropertyField(fbxBaseMeshHideBindings, true);
				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.PropertyField(baseRaceRecipe, true);
				}
			}
			else
			{
				EditorGUILayout.PropertyField(baseRaceRecipe, true);
			}
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				_bsCacheValid = false; // force rebuild of blendshape cache when recipe changes
			}
			if (wardrobeSlotList == null)
			{
				InitWardrobeSlotList();
			}

			EditorGUILayout.Space();

			EditorGUI.BeginChangeCheck();
			wardrobeSlotList.DoLayoutList();
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				if (!race.ValidateWardrobeSlots())
				{
					EditorUtility.SetDirty(race);
				}
			}
			//new CrossCompatibilitySettings
			//To push any old settings in RaceData.backwardsCompatibleWith into the new crossCompatibilitySettings we have to call GetCrossCompatibleRaces() directly on the target
#pragma warning disable 618
			if (race.backwardsCompatibleWith.Count > 0)
			{
				var cc = race.GetCrossCompatibleRaces();
				if (cc.Count > 0)
				{
					serializedObject.Update();
				}
			}
#pragma warning restore 618
			SerializedProperty _crossCompatibilitySettings = serializedObject.FindProperty("_crossCompatibilitySettings");
			SerializedProperty _crossCompatibilitySettingsData = _crossCompatibilitySettings.FindPropertyRelative("settingsData");
			//draw the new version of the crossCompatibility list that allows users to define what slots in THIS races base recipe equate to in the backwards compatible races base recipe
			_crossCompatibilitySettings.isExpanded = EditorGUILayout.Foldout(_crossCompatibilitySettings.isExpanded, "Cross Compatibility Settings");
			if (_crossCompatibilitySettings.isExpanded)
			{
				//draw an info foldout
				EditorGUI.indentLevel++;
				_crossCompatibilitySettingsData.isExpanded = EditorGUILayout.Foldout(_crossCompatibilitySettingsData.isExpanded, "Help");
				if (_crossCompatibilitySettingsData.isExpanded)
				{
					var helpText = "CrossCompatibilitySettings allows this race to wear wardrobe slots from another race, if this race has a wardrobe slot that the recipe is set to.";
					helpText += " You can further configure the compatibility settings for each compatible race to define 'equivalent' slotdatas in the races' base recipes.";
					helpText += " For example you could define that this races 'highpolyMaleChest' slotdata in its base recipe is equivalent to HumanMales 'MaleChest' slot data in its base recipe.";
					helpText += " This would mean that any recipes which hid or applied an overlay to 'MaleChest' would hide or apply an overlay to 'highPolyMaleChest' on this race.";
					helpText += " If 'Overlays Match' is unchecked then overlays in a recipe wont be applied.";
					EditorGUILayout.HelpBox(helpText, MessageType.Info);
				}
				EditorGUI.indentLevel--;
				if (!useFbxRoute.boolValue && baseRaceRecipe.objectReferenceValue != null)
				{
					Rect dropArea = new Rect();
					dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
					GUI.Box(dropArea, "Drag cross compatible Races here. Click to pick.");
					CompatibleRacesDropArea(dropArea, _crossCompatibilitySettingsData);
					EditorGUILayout.Space();
					//update the foldouts list if the dropbox changes anything
					if (_BCFoldouts.Length != _crossCompatibilitySettingsData.arraySize)
					{
						Array.Resize<bool>(ref _BCFoldouts, _crossCompatibilitySettingsData.arraySize);
					}
					//we need an uptodate list of the slots in THIS races base recipe
					baseSlotsList.Clear();
					baseSlotsNamesList.Clear();

					UMAData.UMARecipe thisBaseRecipe = (baseRaceRecipe.objectReferenceValue as UMARecipeBase).GetCachedRecipe();
					SlotData[] thisBaseSlots = thisBaseRecipe.GetAllSlots();
					foreach (SlotData slot in thisBaseSlots)
					{
						if (slot != null)
						{
							baseSlotsList.Add(slot);
							baseSlotsNamesList.Add(slot.slotName);
						}
					}
					List<int> crossCompatibleSettingsToDelete = new List<int>();
					//draw a foldout area for each compatible race that will show an entry for each slot in this races base recipe 
					//with a picker to choose the slot from the compatible race's base recipe that it equates to
					for (int i = 0; i < _crossCompatibilitySettingsData.arraySize; i++)
					{
						bool del = false;
						var thisCCSettings = _crossCompatibilitySettingsData.GetArrayElementAtIndex(i).FindPropertyRelative("ccSettings");
						var ccRaceName = _crossCompatibilitySettingsData.GetArrayElementAtIndex(i).FindPropertyRelative("ccRace").stringValue;
						//this could be missing- we should show that
						var label = ccRaceName;
						if (GetCompatibleRaceData(ccRaceName) == null)
						{
							label += " (missing)";
						}

						GUIHelper.FoldoutBar(ref _BCFoldouts[i], label, out del);
						if (del)
						{
							crossCompatibleSettingsToDelete.Add(i);
						}
						if (_BCFoldouts[i])
						{
							DrawCCUI(ccRaceName, baseRaceRecipe, thisCCSettings);
						}
					}
					if (crossCompatibleSettingsToDelete.Count > 0)
					{
						foreach (int del in crossCompatibleSettingsToDelete)
						{
							_crossCompatibilitySettingsData.DeleteArrayElementAtIndex(del);
							serializedObject.ApplyModifiedProperties();
						}

					}
				}
				else if (useFbxRoute.boolValue)
				{
					EditorGUILayout.HelpBox("Cross compatibility slot mapping uses the base race recipe and is disabled while the FBX route is active.", MessageType.Info);
				}
				else
				{
					EditorGUILayout.HelpBox("Please define this races baseRaceRecipe before trying to define its cross compatibility settings.", MessageType.Info);
				}
			}

			EditorGUILayout.Space();

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(serializedObject.FindProperty("raceThumbnails"), true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}

			if (GUILayout.Button("Clear Race Thumbnails"))
			{
				bool confirmed = EditorUtility.DisplayDialog(
					"Clear Race Thumbnails",
					$"Clear the Full Thumb, Face Thumb, and every Wardrobe Slot Thumb from '{race.name}'?\n\n" +
					"The Race Thumbnails data will be replaced with a new empty container. This action can be undone.",
					"Clear Thumbnails",
					"Cancel");

				if (confirmed)
				{
					serializedObject.ApplyModifiedProperties();
					ClearRaceThumbnails(race);
					serializedObject.Update();
				}
			}
			return false;
		}

		private RaceData GetCompatibleRaceData(string raceName)
		{
			if (string.IsNullOrWhiteSpace(raceName))
			{
				return null;
			}

			string[] foundRacesStrings = AssetDatabase.FindAssets("t:RaceData");
			for (int i = 0; i < foundRacesStrings.Length; i++)
			{
				string assetPath = AssetDatabase.GUIDToAssetPath(foundRacesStrings[i]);
				if (string.IsNullOrWhiteSpace(assetPath))
				{
					continue;
				}

				RaceData thisFoundRace = AssetDatabase.LoadAssetAtPath<RaceData>(assetPath);
				if (thisFoundRace == null)
				{
					continue;
				}

				if (string.Equals(thisFoundRace.raceName, raceName, StringComparison.Ordinal))
				{
					return thisFoundRace;
				}
			}

			return null;
		}

		private void DrawCCUI(string ccRaceName, SerializedProperty baseRaceRecipe, SerializedProperty thisCCSettings)
		{
			GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f));
			EditorGUILayout.LabelField("Equivalent Slots with " + ccRaceName, EditorStyles.centeredGreyMiniLabel);
			if (baseRaceRecipe.objectReferenceValue == null)
			{
				EditorGUILayout.HelpBox("Please set this Races 'Base Race Recipe' before trying to set equivalent Slots.", MessageType.Warning);
			}
			else
			{
				//we need to get the base raceRecipeSlots for this compatible race
				var ccRaceData = GetCompatibleRaceData(ccRaceName);
				if (ccRaceData != null)
				{
					if (ccRaceData.baseRaceRecipe == null)
					{
						EditorGUILayout.HelpBox("Please set " + ccRaceData.raceName + " Races 'Base Race Recipe' before trying to set equivalent Slots.", MessageType.Warning);
					}
					else
					{
						var ccSlotsList = new List<SlotData>();
						var ccSlotsNamesList = new List<string>();
						UMAData.UMARecipe ccBaseRecipe = ccRaceData.baseRaceRecipe.GetCachedRecipe();
						SlotData[] ccBaseSlots = ccBaseRecipe.GetAllSlots();
						foreach (SlotData slot in ccBaseSlots)
						{
							if (slot != null)
							{
								ccSlotsList.Add(slot);
								ccSlotsNamesList.Add(slot.slotName);
							}
						}
						//if that worked we can draw the UI for any set values and a button to add new ones
						GUIHelper.BeginVerticalPadded(2, new Color(1f, 1f, 1f, 0.5f));
						var headerRect = GUILayoutUtility.GetRect(0.0f, (EditorGUIUtility.singleLineHeight * 2), GUILayout.ExpandWidth(true));
						var slotLabelRect = headerRect;
						var gapRect = headerRect;
						var cSlotLabelRect = headerRect;
						var overlaysMatchLabelRect = headerRect;
						var deleteRect = headerRect;
						slotLabelRect.width = (headerRect.width - 50f - 22f - 22f) / 2;
						gapRect.xMin = slotLabelRect.xMax;
						gapRect.width = 22f;
						cSlotLabelRect.xMin = gapRect.xMax;
						cSlotLabelRect.width = slotLabelRect.width;
						overlaysMatchLabelRect.xMin = cSlotLabelRect.xMax;
						overlaysMatchLabelRect.width = 50f;
						deleteRect.xMin = overlaysMatchLabelRect.xMax;
						deleteRect.width = 22f;
						//move this up
						var tableHeaderStyle = EditorStyles.wordWrappedMiniLabel;
						tableHeaderStyle.alignment = TextAnchor.MiddleCenter;
						//we need a gui style for this that wraps the text and vertically centers it in the space
						EditorGUI.LabelField(slotLabelRect, "This Races Slot", tableHeaderStyle);
						EditorGUI.LabelField(gapRect, "", tableHeaderStyle);
						EditorGUI.LabelField(cSlotLabelRect, "Compatible Races Slot", tableHeaderStyle);
						EditorGUI.LabelField(overlaysMatchLabelRect, "Overlays Match", tableHeaderStyle);
						GUIHelper.EndVerticalPadded(2);
						GUIHelper.BeginVerticalPadded(2, new Color(0.75f, 0.875f, 1f));
						if (thisCCSettings.arraySize > 0)
						{
							for (int ccsd = 0; ccsd < thisCCSettings.arraySize; ccsd++)
							{
								if (DrawCCUISetting(ccsd, thisCCSettings, ccSlotsNamesList))
								{
									serializedObject.ApplyModifiedProperties();
								}
							}

						}
						else
						{
							EditorGUILayout.LabelField("No equivalent slots defined", EditorStyles.miniLabel);
						}
						GUIHelper.EndVerticalPadded(2);
						var addButtonRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
						addButtonRect.xMin = addButtonRect.xMax - 70f;
						addButtonRect.width = 70f;
						if (GUI.Button(addButtonRect, "Add"))
						{
							thisCCSettings.InsertArrayElementAtIndex(thisCCSettings.arraySize);
							serializedObject.ApplyModifiedProperties();
						}
					}
				}
				else
				{
					EditorGUILayout.HelpBox("The cross compatible race " + ccRaceName + " could not be found!", MessageType.Warning);
				}
			}
			GUIHelper.EndVerticalPadded(5);
		}

		private bool DrawCCUISetting(int ccsd, SerializedProperty thisCCSettings, List<string> ccSlotsNamesList)
		{
			var changed = false;
			var startingRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
			var thisSlot = thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("raceSlot").stringValue;
			var thisSlotIndex = baseSlotsNamesList.IndexOf(thisSlot);
			var thisCompatibleSlot = thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("compatibleRaceSlot").stringValue;
			var thisCompatibleSlotIndex = ccSlotsNamesList.IndexOf(thisCompatibleSlot);
			var thisOverlaysMatch = thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("overlaysMatch").boolValue;
			var thisSlotRect = startingRect;
			var thisEqualsLabelRect = startingRect;
			var thisCompatibleSlotRect = startingRect;
			//var thisOverlaysLabelRect = startingRect;
			var thisOverlaysMatchRect = startingRect;
			var thisDeleteRect = startingRect;
			thisSlotRect.width = (startingRect.width - 50f - 22f - 22f) / 2;
			thisEqualsLabelRect.xMin = thisSlotRect.xMax;
			thisEqualsLabelRect.width = 22f;
			thisCompatibleSlotRect.xMin = thisEqualsLabelRect.xMax;
			thisCompatibleSlotRect.width = thisSlotRect.width;
			thisOverlaysMatchRect.xMin = thisCompatibleSlotRect.xMax + 22f;
			thisOverlaysMatchRect.width = 50f - 22f;
			thisDeleteRect.xMin = thisOverlaysMatchRect.xMax;
			thisDeleteRect.width = 22f;
			EditorGUI.BeginChangeCheck();
			var newSlotIndex = EditorGUI.Popup(thisSlotRect, "", thisSlotIndex, baseSlotsNamesList.ToArray());
			if (EditorGUI.EndChangeCheck())
			{
				if (newSlotIndex != thisSlotIndex)
				{
					thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("raceSlot").stringValue = baseSlotsNamesList[newSlotIndex];
					changed = true;
				}
			}
			EditorGUI.LabelField(thisEqualsLabelRect, "==");
			EditorGUI.BeginChangeCheck();
			var newCompatibleSlotIndex = EditorGUI.Popup(thisCompatibleSlotRect, "", thisCompatibleSlotIndex, ccSlotsNamesList.ToArray());
			if (EditorGUI.EndChangeCheck())
			{
				if (newCompatibleSlotIndex != thisCompatibleSlotIndex)
				{
					thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("compatibleRaceSlot").stringValue = ccSlotsNamesList[newCompatibleSlotIndex];
					/*var ccSlotsOverlays = ccSlotsList[newCompatibleSlotIndex].GetOverlayList();
					thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("compatibleRaceSlotOverlays").arraySize = ccSlotsOverlays.Count;
					for (int ccai =0; ccai < ccSlotsOverlays.Count; ccai++)
						thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("compatibleRaceSlotOverlays").GetArrayElementAtIndex(ccai).stringValue = ccSlotsOverlays[ccai].overlayName;*/
					changed = true;
				}
			}
			//we need a gui style for this that centers this horizontally
			EditorGUI.BeginChangeCheck();
			var newOverlaysMatch = EditorGUI.ToggleLeft(thisOverlaysMatchRect, " ", thisOverlaysMatch);
			if (EditorGUI.EndChangeCheck())
			{
				if (newOverlaysMatch != thisOverlaysMatch)
				{
					thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("overlaysMatch").boolValue = newOverlaysMatch;
					changed = true;
				}
			}
			if (GUI.Button(thisDeleteRect, "X", EditorStyles.miniButton))
			{
				thisCCSettings.DeleteArrayElementAtIndex(ccsd);
				changed = true;
			}
			//******NEEDS TO BE IN THE RETURN***//
			//if (changed)
			//	serializedObject.ApplyModifiedProperties();
			//GUILayout.EndHorizontal();
			GUILayout.Space(2f);
			return changed;
		}
		#endregion
	}
}
#endif
#pragma warning restore 0472
