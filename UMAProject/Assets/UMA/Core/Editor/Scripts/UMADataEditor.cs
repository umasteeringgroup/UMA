#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMA.UMAData), true)]
    public class UMADataEditor : CharacterBaseEditor
    {
        protected UMAData _umaData;
        public bool initialized = false;
		public bool showEditInfo = false;

		//To keep the DNA inspector uptodate when DCA changes the recipe we need to track
		//the active dna and update the editor for it when the recipe changes.
		private int[] _currentDnaTypeHashes;

        public void InitializeUMADataEditor()
        {
            //   if (!NeedsReenable())
            //       return;

            dnaEditor = null;
            slotEditor = null;
            showBaseEditor = false;
            _umaData = target as UMAData;
            _errorMessage = null;
            if (_umaData == null)
            {
                _errorMessage = "UmaData is null";
                return;
            }
            _recipe = _umaData.umaRecipe;
            if (_recipe == null || _recipe.raceData == null)
            {
                _errorMessage = "Recipe data has not been generated.";
            }
            else
            {
                DNAMasterEditor.umaGenerator = _umaData.umaGenerator;
                dnaEditor = new DNAMasterEditor(_recipe);
                slotEditor = new SlotMasterEditor(_recipe);

                SetCurrentDnaTypeHashes();

                _rebuildOnLayout = true;
            }
        }

        private void SetCurrentDnaTypeHashes()
		{
			UMADnaBase[] allDna = (target as UMAData).umaRecipe.GetAllDna();
			_currentDnaTypeHashes = new int[allDna.Length];
			for (int i = 0; i < allDna.Length; i++)
			{
				_currentDnaTypeHashes[i] = allDna[i].DNATypeHash;
			}
		}

		private bool CheckCurrentDNATypeHashes()
		{
			var currentRecipe = (target as UMAData).umaRecipe;
			if (_currentDnaTypeHashes == null)
            {
				SetCurrentDnaTypeHashes();
            }
			if (_currentDnaTypeHashes.Length == 0 || currentRecipe == null || currentRecipe.raceData == null)
            {
                return false;
            }

            UMADnaBase[] allDna = currentRecipe.GetAllDna();
			for (int i = 0; i < allDna.Length; i++)
			{
				bool found = false;
				for (int ii = 0; ii < _currentDnaTypeHashes.Length; ii++)
				{
					if (_currentDnaTypeHashes[ii] == allDna[i].DNATypeHash)
                    {
                        found = true;
                    }
                }
				if (!found)
                {
                    return false;
                }
            }
			return true;
		}

		public static bool ShowOverrides;
        public static bool ShowAppliedMeshModifiers;

		public override void OnInspectorGUI()
        {
            if (dnaEditor == null)
            {
                InitializeUMADataEditor();
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				if (GUIHelper.BeginCollapsableGroup(ref ShowOverrides, "Override Info"))
                {
					EditorGUILayout.LabelField("Object ID", _umaData.GetInstanceID().ToString());
					EditorGUILayout.LabelField("TPose Override", (_umaData.OverrideTpose != null).ToString());
					EditorGUILayout.LabelField("Texture Override", (_umaData.TextureOverrides.Count != 0).ToString());

					GUIHelper.EndCollapsableGroup();
                }
				if(GUIHelper.BeginCollapsableGroup(ref showEditInfo, "Edit time info")) {
					DoEditTimeInfo();
					GUIHelper.EndCollapsableGroup();
				}
				if (dnaEditor != null)
                {
                    if (!CheckCurrentDNATypeHashes())
					{
						dnaEditor = new DNAMasterEditor(_recipe);
						SetCurrentDnaTypeHashes();
					}
                }
                if (GUILayout.Button("Rebuild"))
                {
                    DoUpdate();
                }
                base.OnInspectorGUI(); 
			}
			else
            {
                DoEditTimeInfo();
            }

            if (_umaData != null && GUILayout.Button("Open Runtime Data Viewer"))
            {
                RuntimeDataViewerWindow.Open(_umaData);
            }

            DrawAppliedMeshModifiersInfo();
        }

        private void DrawAppliedMeshModifiersInfo()
        {
            if (_umaData == null)
            {
                return;
            }

            if (!GUIHelper.BeginCollapsableGroup(ref ShowAppliedMeshModifiers, "Applied Mesh Modifiers"))
            {
                return;
            }

            var manualMeshModifiers = _umaData.ManualMeshModifiers;
            if (manualMeshModifiers == null || manualMeshModifiers.Count == 0)
            {
                EditorGUILayout.LabelField("None");
            }
            else
            {
                EditorGUILayout.IntField("Count", manualMeshModifiers.Count);
                for (int i = 0; i < manualMeshModifiers.Count; i++)
                {
                    var modifier = manualMeshModifiers[i];
                    if (modifier == null)
                    {
                        EditorGUILayout.LabelField($"{i:00}: <null>");
                        continue;
                    }

                    int adjustmentCount = modifier.adjustments != null ? modifier.adjustments.Count() : 0;
                    EditorGUILayout.LabelField($"{i:00}: {modifier.ModifierName} | Slot: {modifier.SlotName} | Adjustments: {adjustmentCount}");
                }
            }

            GUIHelper.EndCollapsableGroup();
        }

        protected void DoEditTimeInfo()
        {
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f, 1f));
            EditorGUILayout.LabelField("Edit Time Info", EditorStyles.boldLabel);
            EditorGUILayout.IntField("Instance ID", _umaData.GetInstanceID());
            EditorGUILayout.Toggle("Using 32 bit", _umaData.force32bit);
            if (_umaData.umaRecipe != null)
            {
                if (_umaData.umaRecipe.slotDataList == null)
                {
                    EditorGUILayout.LabelField("No Slot Data");
                }
                else
                {
                    EditorGUILayout.IntField("SlotCount", _umaData.umaRecipe.slotDataList.Length);
                    foreach (SlotData slot in _umaData.umaRecipe.slotDataList)
                    {
                        if (slot != null)
                        {
                            EditorGUILayout.LabelField($"{slot.vertexOffset:000000} {slot.asset.meshData.vertexCount:000000} {slot.asset.slotName}");
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("No Recipe Data");
            }
            GUIHelper.EndVerticalPadded();
        }


        protected override void DoUpdate()
        {
            _umaData.Dirty(_dnaDirty, _textureDirty, _meshDirty);
            _needsUpdate = false;
            _dnaDirty = false;
            _textureDirty = false;
            _meshDirty = false;
            Rebuild();
        }

        protected override void Rebuild()
        {
            base.Rebuild();
        }
    }

    public class RuntimeDataViewerWindow : EditorWindow
    {
        private const int MaxDepth = 8;
        private const int MaxCollectionElements = 128;

        private UMAData _target;
        private Vector2 _scrollPosition;
        private bool _pinTarget = true;
        private bool _autoRefresh = true;
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        [MenuItem("UMA/Debug/Runtime Data Viewer")]
        public static void OpenWindow()
        {
            RuntimeDataViewerWindow window = GetWindow<RuntimeDataViewerWindow>("UMA Runtime Viewer");
            window.SyncTargetFromSelection();
            window.Show();
        }

        public static void Open(UMAData target)
        {
            RuntimeDataViewerWindow window = GetWindow<RuntimeDataViewerWindow>("UMA Runtime Viewer");
            window.SetTarget(target, true);
            window.Show();
        }

        private void OnEnable()
        {
            if (_target == null)
            {
                SyncTargetFromSelection();
            }
        }

        private void OnSelectionChange()
        {
            if (_pinTarget)
            {
                return;
            }

            SyncTargetFromSelection();
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            if (_autoRefresh && (EditorApplication.isPlaying || _target != null))
            {
                Repaint();
            }
        }

        private void SetTarget(UMAData target, bool pinTarget)
        {
            _target = target;
            _pinTarget = pinTarget;
            Repaint();
        }

        private void SyncTargetFromSelection()
        {
            UMAData selected = TryGetUMAData(Selection.activeObject);
            if (selected == null && Selection.activeGameObject != null)
            {
                selected = Selection.activeGameObject.GetComponent<UMAData>();
            }
            _target = selected;
        }

        private static UMAData TryGetUMAData(UnityEngine.Object obj)
        {
            if (obj is UMAData umaData)
            {
                return umaData;
            }

            if (obj is GameObject gameObject)
            {
                return gameObject.GetComponent<UMAData>();
            }

            if (obj is Component component)
            {
                return component.GetComponent<UMAData>();
            }

            return null;
        }

        private void OnGUI()
        {
            DrawToolbar();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            try
            {
                if (_target == null)
                {
                    EditorGUILayout.HelpBox("Select a live UMAData or DynamicCharacterAvatar, or open this viewer from a UMA inspector.", MessageType.Info);
                    return;
                }

                DrawUMADataSummary();
                DrawSlotList();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            UMAData newTarget = EditorGUILayout.ObjectField(_target, typeof(UMAData), true, GUILayout.MinWidth(200)) as UMAData;
            if (newTarget != _target)
            {
                _target = newTarget;
            }

            _pinTarget = GUILayout.Toggle(_pinTarget, "Pin Target", EditorStyles.toolbarButton, GUILayout.Width(80));
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto Refresh", EditorStyles.toolbarButton, GUILayout.Width(90));

            if (GUILayout.Button("Use Selected", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                SyncTargetFromSelection();
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawUMADataSummary()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("UMAData", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Target", _target, typeof(UMAData), true);
                EditorGUILayout.ObjectField("Race", _target.umaRecipe != null ? _target.umaRecipe.raceData : null, typeof(RaceData), false);
            }

            EditorGUILayout.IntField("Slot Count", _target.umaRecipe != null && _target.umaRecipe.slotDataList != null ? _target.umaRecipe.slotDataList.Length : 0);
            EditorGUILayout.Toggle("Is Texture Dirty", _target.isTextureDirty);
            EditorGUILayout.Toggle("Is Mesh Dirty", _target.isMeshDirty);
            EditorGUILayout.Toggle("Is Shape Dirty", _target.isShapeDirty);
            EditorGUILayout.Toggle("Atlas Dirty", _target.isAtlasDirty);
            EditorGUILayout.Toggle("Dynamic Character Avatar", _target is CharacterSystem.DynamicCharacterAvatar);

            if (_target.umaRecipe == null)
            {
                EditorGUILayout.HelpBox("This UMAData has no active recipe to inspect.", MessageType.Info);
                return;
            }

            if (BeginFoldout("uma.raw", "UMAData Raw Fields"))
            {
                DrawObjectFields("UMAData", _target, "uma.raw.object", new HashSet<int>(), 0);
            }
        }

        private void DrawSlotList()
        {
            if (_target == null || _target.umaRecipe == null || _target.umaRecipe.slotDataList == null)
            {
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime Slots", EditorStyles.boldLabel);

            SlotData[] slots = _target.umaRecipe.slotDataList;
            if (slots.Length == 0)
            {
                EditorGUILayout.HelpBox("The current recipe has no SlotData entries.", MessageType.Info);
                return;
            }

            HashSet<int> stack = new HashSet<int>();
            for (int i = 0; i < slots.Length; i++)
            {
                DrawSlotDataAdapter(slots[i], "slot." + i, stack, 0, i);
            }
        }

        private void DrawSlotDataAdapter(SlotData slot, string path, HashSet<int> stack, int depth, int index)
        {
            string title = slot == null ? $"Slot {index:00}: <null>" : $"Slot {index:00}: {slot.slotName} ({slot.OverlayCount} overlays)";
            if (!BeginFoldout(path, title))
            {
                return;
            }

            if (slot == null)
            {
                EditorGUILayout.LabelField("Value", "null");
                return;
            }

            int identity = RuntimeHelpers.GetHashCode(slot);
            if (!stack.Add(identity))
            {
                EditorGUILayout.HelpBox("Cyclic SlotData reference detected.", MessageType.Info);
                return;
            }

            try
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Asset", slot.asset, typeof(SlotDataAsset), false);
                    EditorGUILayout.ObjectField("Resolved Material", slot.material, typeof(UMAMaterial), false);
                    EditorGUILayout.ObjectField("Alt Material", slot.altMaterial, typeof(UMAMaterial), false);
                    EditorGUILayout.ObjectField("Renderer Asset", slot.rendererAsset, typeof(UMARendererAsset), false);
                }

                EditorGUILayout.TextField("Slot Name", slot.slotName ?? string.Empty);
                EditorGUILayout.Toggle("Placeholder Slot", slot.isPlaceholderSlot);
                EditorGUILayout.Toggle("Suppressed", slot.Suppressed);
                EditorGUILayout.Toggle("Disabled", slot.isDisabled);
                EditorGUILayout.Toggle("Use Atlas Overlay", slot.useAtlasOverlay);
                EditorGUILayout.FloatField("Overlay Scale", slot.overlayScale);
                EditorGUILayout.Toggle("Is Baked", slot.asset != null && slot.asset.isBaked);
                EditorGUILayout.IntField("Overlay Count", slot.OverlayCount);
                EditorGUILayout.IntField("UV Set", slot.UVSet);
                EditorGUILayout.IntField("Vertex Offset", slot.vertexOffset);
                EditorGUILayout.IntField("Renderer Index", slot.skinnedMeshRenderer);
                EditorGUILayout.IntField("Submesh Index", slot.submeshIndex);
                EditorGUILayout.RectField("UV Area", slot.UVArea);
                DrawStringArray("Tags", slot.tags, path + ".tags");
                DrawStringArray("Races", slot.Races, path + ".races");
                DrawStringList("Blendshape Slots", slot.BlendshapeSlotNames, path + ".blendshapeSlots");
                DrawBlendShapeNames("Mesh Blendshapes", slot.asset != null ? slot.asset.meshData : null, path + ".meshBlendshapes");

                List<OverlayData> overlays = slot.GetOverlayList();
                if (BeginFoldout(path + ".overlays", "Overlays"))
                {
                    for (int i = 0; i < overlays.Count; i++)
                    {
                        DrawOverlayDataAdapter(overlays[i], path + ".overlay." + i, stack, depth + 1, i);
                    }
                }

                if (BeginFoldout(path + ".raw", "Raw Fields"))
                {
                    DrawObjectFields("SlotData", slot, path + ".raw.object", stack, depth + 1);
                }
            }
            finally
            {
                stack.Remove(identity);
            }
        }

        private void DrawOverlayDataAdapter(OverlayData overlay, string path, HashSet<int> stack, int depth, int index)
        {
            string title = overlay == null ? $"Overlay {index:00}: <null>" : $"Overlay {index:00}: {overlay.overlayName}";
            if (!BeginFoldout(path, title))
            {
                return;
            }

            if (overlay == null)
            {
                EditorGUILayout.LabelField("Value", "null");
                return;
            }

            int identity = RuntimeHelpers.GetHashCode(overlay);
            if (!stack.Add(identity))
            {
                EditorGUILayout.HelpBox("Cyclic OverlayData reference detected.", MessageType.Info);
                return;
            }

            try
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Asset", overlay.asset, typeof(OverlayDataAsset), false);
                    EditorGUILayout.ObjectField("Resolved Material", overlay.asset != null ? overlay.asset.GetMaterial() : null, typeof(UMAMaterial), false);
                    EditorGUILayout.ObjectField("Alpha Mask", overlay.alphaMask, typeof(Texture), false);
                    EditorGUILayout.ObjectField("Merged From Slot", overlay.mergedFromSlot != null ? overlay.mergedFromSlot.asset : null, typeof(SlotDataAsset), false);
                }

                EditorGUILayout.TextField("Overlay Name", overlay.overlayName ?? string.Empty);
                EditorGUILayout.EnumPopup("Overlay Type", overlay.overlayType);
                EditorGUILayout.Toggle("Suppressed", overlay.Supressed);
                EditorGUILayout.Toggle("Empty", overlay.isEmpty);
                EditorGUILayout.Toggle("Instance Transformed", overlay.instanceTransformed);
                EditorGUILayout.IntField("UV Set", overlay.UVSet);
                EditorGUILayout.IntField("Channel Count", overlay.ChannelCount);
                EditorGUILayout.RectField("Rect", overlay.rect);
                EditorGUILayout.Vector2Field("Scale", overlay.Scale);
                EditorGUILayout.Vector2Field("Translate", overlay.Translate);
                EditorGUILayout.FloatField("Rotation", overlay.Rotation);
                EditorGUILayout.TextField("Merged From Recipe", overlay.mergedFromRecipe ?? string.Empty);
                DrawStringArray("Tags", overlay.tags, path + ".tags");
                DrawTextureArray("Textures", overlay.textureArray, path + ".textures");

                if (BeginFoldout(path + ".raw", "Raw Fields"))
                {
                    DrawObjectFields("OverlayData", overlay, path + ".raw.object", stack, depth + 1);
                }
            }
            finally
            {
                stack.Remove(identity);
            }
        }

        private void DrawObjectFields(string label, object value, string path, HashSet<int> stack, int depth)
        {
            if (value == null)
            {
                EditorGUILayout.LabelField(label, "null");
                return;
            }

            if (depth > MaxDepth)
            {
                EditorGUILayout.LabelField(label, "<max depth reached>");
                return;
            }

            Type type = value.GetType();
            FieldInfo[] fields = GetAllFields(type);
            if (fields.Length == 0)
            {
                EditorGUILayout.LabelField(label, type.Name + " has no instance fields.");
                return;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic)
                {
                    continue;
                }

                object fieldValue;
                try
                {
                    fieldValue = field.GetValue(value);
                }
                catch (Exception ex)
                {
                    EditorGUILayout.LabelField(field.Name, "<error: " + ex.GetType().Name + ">");
                    continue;
                }

                DrawValue(field.Name, fieldValue, path + "." + field.Name, stack, depth + 1);
            }
        }

        private void DrawValue(string label, object value, string path, HashSet<int> stack, int depth)
        {
            if (value == null)
            {
                EditorGUILayout.LabelField(label, "null");
                return;
            }

            if (value is SlotData slot)
            {
                DrawSlotDataAdapter(slot, path, stack, depth, -1);
                return;
            }

            if (value is OverlayData overlay)
            {
                DrawOverlayDataAdapter(overlay, path, stack, depth, -1);
                return;
            }

            if (value is UnityEngine.Object unityObject)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(label, unityObject, unityObject != null ? unityObject.GetType() : typeof(UnityEngine.Object), true);
                }
                return;
            }

            Type type = value.GetType();
            if (IsSimple(type))
            {
                EditorGUILayout.LabelField(label, FormatSimple(value));
                return;
            }

            if (value is IDictionary dictionary)
            {
                DrawDictionary(label, dictionary, path, stack, depth);
                return;
            }

            if (value is IList list)
            {
                DrawList(label, list, path, stack, depth);
                return;
            }

            if (value is IEnumerable enumerable && !(value is string))
            {
                DrawEnumerable(label, enumerable, path, stack, depth);
                return;
            }

            int identity = RuntimeHelpers.GetHashCode(value);
            if (!stack.Add(identity))
            {
                EditorGUILayout.LabelField(label, "<cyclic reference>");
                return;
            }

            try
            {
                string header = label + " (" + type.Name + ")";
                if (!BeginFoldout(path, header))
                {
                    return;
                }

                DrawObjectFields(label, value, path + ".fields", stack, depth + 1);
            }
            finally
            {
                stack.Remove(identity);
            }
        }

        private void DrawDictionary(string label, IDictionary dictionary, string path, HashSet<int> stack, int depth)
        {
            string header = label + " [" + dictionary.Count + "]";
            if (!BeginFoldout(path, header))
            {
                return;
            }

            int index = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (index >= MaxCollectionElements)
                {
                    EditorGUILayout.LabelField("More", "Collection truncated at " + MaxCollectionElements + " items.");
                    break;
                }

                DrawValue("Key", entry.Key, path + ".key." + index, stack, depth + 1);
                DrawValue("Value", entry.Value, path + ".value." + index, stack, depth + 1);
                index++;
            }
        }

        private void DrawList(string label, IList list, string path, HashSet<int> stack, int depth)
        {
            string header = label + " [" + list.Count + "]";
            if (!BeginFoldout(path, header))
            {
                return;
            }

            int count = Mathf.Min(list.Count, MaxCollectionElements);
            for (int i = 0; i < count; i++)
            {
                DrawValue("Element " + i, list[i], path + "." + i, stack, depth + 1);
            }

            if (list.Count > MaxCollectionElements)
            {
                EditorGUILayout.LabelField("More", "Collection truncated at " + MaxCollectionElements + " items.");
            }
        }

        private void DrawEnumerable(string label, IEnumerable enumerable, string path, HashSet<int> stack, int depth)
        {
            List<object> items = new List<object>();
            foreach (object item in enumerable)
            {
                items.Add(item);
                if (items.Count > MaxCollectionElements)
                {
                    break;
                }
            }

            string header = label + " [" + items.Count + "]";
            if (!BeginFoldout(path, header))
            {
                return;
            }

            for (int i = 0; i < items.Count && i < MaxCollectionElements; i++)
            {
                DrawValue("Element " + i, items[i], path + "." + i, stack, depth + 1);
            }

            if (items.Count > MaxCollectionElements)
            {
                EditorGUILayout.LabelField("More", "Collection truncated at " + MaxCollectionElements + " items.");
            }
        }

        private void DrawStringArray(string label, string[] values, string path)
        {
            if (values == null)
            {
                EditorGUILayout.LabelField(label, "null");
                return;
            }

            if (!BeginFoldout(path, label + " [" + values.Length + "]"))
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                EditorGUILayout.TextField("Element " + i, values[i] ?? string.Empty);
            }
        }

        private void DrawStringList(string label, List<string> values, string path)
        {
            if (values == null)
            {
                EditorGUILayout.LabelField(label, "null");
                return;
            }

            if (!BeginFoldout(path, label + " [" + values.Count + "]"))
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                EditorGUILayout.TextField("Element " + i, values[i] ?? string.Empty);
            }
        }

        private void DrawTextureArray(string label, Texture[] textures, string path)
        {
            if (textures == null)
            {
                EditorGUILayout.LabelField(label, "null");
                return;
            }

            if (!BeginFoldout(path, label + " [" + textures.Length + "]"))
            {
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                for (int i = 0; i < textures.Length; i++)
                {
                    EditorGUILayout.ObjectField("Element " + i, textures[i], typeof(Texture), false);
                }
            }
        }

        private void DrawBlendShapeNames(string label, UMAMeshData meshData, string path)
        {
            if (UMAMeshData.IsNullOrEmptyMeshData(meshData) || meshData.blendShapes == null)
            {
                EditorGUILayout.LabelField(label, "null");
                return;
            }

            if (!BeginFoldout(path, label + " [" + meshData.blendShapes.Length + "]"))
            {
                return;
            }

            for (int i = 0; i < meshData.blendShapes.Length; i++)
            {
                var blendShape = meshData.blendShapes[i];
                EditorGUILayout.TextField("Element " + i, blendShape != null ? (blendShape.shapeName ?? string.Empty) : "<null>");
            }
        }

        private bool BeginFoldout(string key, string label)
        {
            bool current;
            if (!_foldouts.TryGetValue(key, out current))
            {
                current = false;
            }

            bool next = EditorGUILayout.Foldout(current, label, true);
            _foldouts[key] = next;
            return next;
        }

        private static bool IsSimple(Type type)
        {
            if (type.IsPrimitive || type.IsEnum)
            {
                return true;
            }

            return type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(Vector2Int)
                || type == typeof(Vector3Int)
                || type == typeof(Rect)
                || type == typeof(Color)
                || type == typeof(Color32)
                || type == typeof(Quaternion)
                || type == typeof(Bounds)
                || type == typeof(Matrix4x4)
                || type == typeof(AnimationCurve);
        }

        private static string FormatSimple(object value)
        {
            return value != null ? value.ToString() : "null";
        }

        private static FieldInfo[] GetAllFields(Type type)
        {
            List<FieldInfo> fields = new List<FieldInfo>();
            while (type != null && type != typeof(object))
            {
                fields.AddRange(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                type = type.BaseType;
            }
            return fields.ToArray();
        }
    }
}
#endif
