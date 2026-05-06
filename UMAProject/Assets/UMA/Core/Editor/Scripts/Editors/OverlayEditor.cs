#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public class OverlayEditor
    {
        public static Dictionary<string, bool> OverlayExpanded = new Dictionary<string, bool>();
        private readonly UMAData.UMARecipe _recipe;
        protected readonly SlotData _slotData;
        private readonly OverlayData _overlayData;
        private readonly UnityEngine.Object _recipeContext;
        private OverlayDataAsset _baseOverlayData;
        private readonly TextureEditor[] _textures;
        private ColorEditor[] _colors;
        private bool isUV = false;

        private bool _popupRectChanged;


        public OverlayData Overlay
        {
            get { return _overlayData; }
        }

        private bool _foldout = true;

        public bool Delete { get; private set; }

        public int move;
        private static OverlayData showExtendedRangeForOverlay;
        private static readonly HashSet<EntityId> PopupChangedOverlayAssetIds = new HashSet<EntityId>();

        public void EnsureEntry(string overlayName)
        {
            if (OverlayExpanded.ContainsKey(overlayName))
            {
                return;
            }

            OverlayExpanded.Add(overlayName, true);
        }

        public OverlayEditor(UMAData.UMARecipe recipe, SlotData slotData, OverlayData overlayData, OverlayDataAsset baseOverlayDataAsset = null, UnityEngine.Object recipeContext = null)
        {
            _recipe = recipe;
            _overlayData = overlayData;
            _slotData = slotData;
            _recipeContext = recipeContext;
            _baseOverlayData = baseOverlayDataAsset;
            EnsureEntry(overlayData.overlayName);

            if ((_overlayData.rect.x <= 1.0f) && (_overlayData.rect.y <= 1.0f) && (_overlayData.rect.width <= 1.0f) && (_overlayData.rect.height <= 1.0f))
            {
                isUV = true;
            }

            // Sanity check the colors
            if (_recipe.sharedColors == null)
            {
                _recipe.sharedColors = new OverlayColorData[0];
            }
            else
            {
                for (int i = 0; i < _recipe.sharedColors.Length; i++)
                {
                    OverlayColorData ocd = _recipe.sharedColors[i];
                    if (!ocd.HasName())
                    {
                        ocd.name = "Shared Color " + (i + 1);
                    }
                }
            }

            _textures = new TextureEditor[overlayData.asset.textureCount];
            for (int i = 0; i < overlayData.asset.textureCount; i++)
            {
                _textures[i] = new TextureEditor(overlayData.textureArray[i], i, overlayData);
            }

            BuildColorEditors();

        }

        private void BuildColorEditors()
        {
            _overlayData.Validate();

            if (_overlayData.colorData == null || _overlayData.colorData.channelMask == null)
            {
                return;
            }

            _colors = new ColorEditor[_overlayData.colorData.channelMask.Length * 2];

            for (int i = 0; i < _overlayData.colorData.channelMask.Length; i++)
            {
                _colors[i * 2] = new ColorEditor(
                   _overlayData.colorData.channelMask[i],
                   string.Format(i == 0
                      ? "Color multiplier"
                      : "Texture {0} multiplier", i));

                _colors[(i * 2) + 1] = new ColorEditor(
                   _overlayData.colorData.channelAdditiveMask[i],
                   string.Format(i == 0
                      ? "Color additive"
                      : "Texture {0} additive", i));
            }
        }

        private bool InIndex(OverlayData _overlayData)
        {
            return UMAAssetIndexer.Instance.HasOverlay(_overlayData.overlayName);
        }

        private void OpenPositioningPopup()
        {
            OverlayRectPositionWindow.Open(this, _slotData, _overlayData, _baseOverlayData);
        }

        internal void ApplyPopupRect(Rect rect, bool updateAsset)
        {
            _overlayData.rect = rect;
            _popupRectChanged = true;
            if (_overlayData.asset != null)
            {
                PopupChangedOverlayAssetIds.Add(_overlayData.asset.GetEntityId());
            }

            if (updateAsset && _overlayData.asset != null)
            {
                _overlayData.asset.rect = rect;
                _overlayData.asset.lastActionTime = Time.realtimeSinceStartup;
                _overlayData.asset.doSave = true;
                EditorUtility.SetDirty(_overlayData.asset);
                UMAUpdateProcessor.UpdateOverlay(_overlayData.asset);
            }

            if (_recipeContext != null)
            {
                EditorUtility.SetDirty(_recipeContext);
                AssetDatabase.SaveAssetIfDirty(_recipeContext);
            }

            ForceUpdateSceneAvatarsUsingOverlay();

            RepaintEditorViews();
        }

        internal void PreviewPopupTransform(bool instanceTransformed, float rotation, Vector2 scale, Vector2 translate)
        {
            _overlayData.instanceTransformed = instanceTransformed;
            _overlayData.Rotation = rotation;
            _overlayData.Scale = scale;
            _overlayData.Translate = translate;
            _popupRectChanged = true;

            if (_overlayData.asset != null)
            {
                PopupChangedOverlayAssetIds.Add(_overlayData.asset.GetEntityId());
            }

            RepaintEditorViews();
        }

        internal void ApplyPopupTransform(bool instanceTransformed, float rotation, Vector2 scale, Vector2 translate)
        {
            _overlayData.instanceTransformed = instanceTransformed;
            _overlayData.Rotation = rotation;
            _overlayData.Scale = scale;
            _overlayData.Translate = translate;
            _popupRectChanged = true;

            if (_overlayData.asset != null)
            {
                PopupChangedOverlayAssetIds.Add(_overlayData.asset.GetEntityId());
            }

            if (_recipeContext != null)
            {
                EditorUtility.SetDirty(_recipeContext);
                AssetDatabase.SaveAssetIfDirty(_recipeContext);
            }

            ForceUpdateSceneAvatarsUsingOverlay();

            RepaintEditorViews();
        }

        internal UMAWardrobeRecipe ResolveWardrobeRecipeContext()
        {
            if (_recipeContext is UMAWardrobeRecipe wardrobeRecipe)
            {
                return wardrobeRecipe;
            }

            return Selection.activeObject as UMAWardrobeRecipe;
        }

        internal void ForceUpdateSceneAvatarsUsingOverlay()
        {
            if (_overlayData == null || _overlayData.asset == null)
            {
                return;
            }

            DynamicCharacterAvatar[] avatars = UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < avatars.Length; i++)
            {
                DynamicCharacterAvatar avatar = avatars[i];
                if (avatar == null || avatar.gameObject == null)
                {
                    continue;
                }

                if (EditorUtility.IsPersistent(avatar) || !avatar.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (!AvatarUsesOverlay(avatar, _overlayData.asset))
                {
                    continue;
                }

                avatar.ForceUpdate(false, true, false);
            }
        }

        private static bool AvatarUsesOverlay(DynamicCharacterAvatar avatar, OverlayDataAsset overlayAsset)
        {
            if (avatar == null || overlayAsset == null || avatar.umaRecipe == null)
            {
                return false;
            }

            SlotData[] slots = avatar.umaRecipe.GetAllSlots();
            if (slots == null)
            {
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                List<OverlayData> overlays = slot.GetOverlayList();
                if (overlays == null)
                {
                    continue;
                }

                for (int j = 0; j < overlays.Count; j++)
                {
                    OverlayData overlay = overlays[j];
                    if (overlay != null && overlay.asset == overlayAsset)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal void RestorePopupRects(Rect recipeRect, Rect assetRect)
        {
            _overlayData.rect = recipeRect;
            _popupRectChanged = true;

            if (_overlayData.asset != null)
            {
                _overlayData.asset.rect = assetRect;
                _overlayData.asset.lastActionTime = Time.realtimeSinceStartup;
                _overlayData.asset.doSave = true;
                EditorUtility.SetDirty(_overlayData.asset);
                UMAUpdateProcessor.UpdateOverlay(_overlayData.asset);
            }

            RepaintEditorViews();
        }

        internal void RestorePopupTransforms(bool instanceTransformed, float rotation, Vector2 scale, Vector2 translate)
        {
            _overlayData.instanceTransformed = instanceTransformed;
            _overlayData.Rotation = rotation;
            _overlayData.Scale = scale;
            _overlayData.Translate = translate;
            _popupRectChanged = true;

            if (_overlayData.asset != null)
            {
                PopupChangedOverlayAssetIds.Add(_overlayData.asset.GetEntityId());
            }

            RepaintEditorViews();
        }

        private bool ConsumePopupRectChanged()
        {
            bool popupChanged = _popupRectChanged;

            if (_overlayData != null && _overlayData.asset != null)
            {
                EntityId assetId = _overlayData.asset.GetEntityId();
                if (PopupChangedOverlayAssetIds.Contains(assetId))
                {
                    popupChanged = true;
                    PopupChangedOverlayAssetIds.Remove(assetId);
                }
            }

            _popupRectChanged = false;
            return popupChanged;
        }

        private static void RepaintEditorViews()
        {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        public bool OnGUI()
        {
            List<string> buttons = new List<string>() { "Inspect","Mat","UMat" };
            List<bool> pressed = new List<bool>() { false, false, false };
            bool delete;

            _foldout = OverlayExpanded[_overlayData.overlayName];

            if (_overlayData.asset.material == null)
            {
                Debug.LogError($"Error - No material set in Overlay {_overlayData.overlayName}");
            }

            int queue = 0;
            string matName = "Unknown";
            if (_overlayData.asset.material != null)
            {
                matName = _overlayData.asset.material.name;
                if (Overlay.asset.material.material != null)
                {
                    queue = _overlayData.asset.material.material.renderQueue;
                }
                else
                {
                    Debug.LogError($"Error - No material set in Overlay {_overlayData.overlayName} in UMAMaterial {_overlayData.asset.material.name}");
                }
            }
            else
                {
                Debug.LogError($"Error - No UMAMaterial set in Overlay {_overlayData.overlayName}");
            }


            GUIHelper.FoldoutBarButton(ref _foldout, $"{_overlayData.asset.overlayName} ( {matName} Q:{queue})", buttons,out pressed, out move, out delete);

            if (pressed[0])
            {
                EditorGUIUtility.PingObject(_overlayData.asset.GetEntityId());
                InspectorUtlity.InspectTarget(_overlayData.asset);
            }

            if (pressed[1])
            {
                EditorGUIUtility.PingObject(_overlayData.asset.material.material.GetEntityId());
                InspectorUtlity.InspectTarget(_overlayData.asset.material.material);
            }

            if (pressed[2])
            {
                EditorGUIUtility.PingObject(_overlayData.asset.material.GetEntityId());
                InspectorUtlity.InspectTarget(_overlayData.asset.material);
            }


            OverlayExpanded[_overlayData.overlayName] = _foldout;
            Delete = delete;

            bool popupchanged = ConsumePopupRectChanged();

            if (!_foldout)
            {
                return popupchanged;
            }

            GUIHelper.BeginHorizontalPadded(10, Color.white);
            GUILayout.BeginVertical();



            if (!InIndex(_overlayData))
            {
                EditorGUILayout.HelpBox("Overlay " + _overlayData.asset.name + " is not indexed!", MessageType.Error);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Add to Global Index"))
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(OverlayDataAsset), _overlayData.asset);
                    UMAAssetIndexer.Instance.ForceSave();
                }
                GUILayout.EndHorizontal();
            }

            _overlayData.Validate();

            bool changed = false;

            if (!isUV)
            {
                EditorGUILayout.HelpBox("Overlay " + _overlayData.asset.name + " is not using UV coordinates! Convert?", MessageType.Error);
                _overlayData.editorReferenceTextureSize = EditorGUILayout.Vector2Field("Reference Texture Size", _overlayData.editorReferenceTextureSize);
                if (_overlayData.editorReferenceTextureSize.magnitude != 0.0f)
                { 
                    if (GUILayout.Button("Convert to UV"))
                    {
                        _overlayData.rect = new Rect(_overlayData.rect.x / _overlayData.editorReferenceTextureSize.x, _overlayData.rect.y / _overlayData.editorReferenceTextureSize.y, _overlayData.rect.width / _overlayData.editorReferenceTextureSize.x, _overlayData.rect.height / _overlayData.editorReferenceTextureSize.y);
                        changed = true;
                    }
                }
            }

            if (GUILayout.Button("Position Overlay..."))
            {
                OpenPositioningPopup();
            }

            bool hasBackingSlotAsset = _slotData != null && !_slotData.isPlaceholderSlot && _slotData.asset != null;
            if (_slotData != null && _slotData.isPlaceholderSlot)
            {
                EditorGUILayout.HelpBox("This overlay belongs to a placeholder wildcard slot. Slot-asset material checks are unavailable because the slot has no backing asset.", MessageType.Info);
            }

            if (hasBackingSlotAsset && _slotData.material != null && _overlayData.asset.material != null)
            {
                if (_overlayData.asset.material.name != _slotData.material.name)
                {
                    if (_overlayData.asset.material.channels.Length == _slotData.material.channels.Length)
                    {
                        EditorGUILayout.HelpBox("Material " + _overlayData.asset.material.name + " does not match slot material: " + _slotData.material.name, MessageType.Error);
                        if (GUILayout.Button("Copy Slot Material to Overlay"))
                        {
                            _overlayData.asset.material = _slotData.material;
                            EditorUtility.SetDirty(_overlayData.asset);
                            string path = AssetDatabase.GetAssetPath(_overlayData.asset.GetEntityId());
                            AssetDatabase.ImportAsset(path);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Material " + _overlayData.asset.material.name + " does not match slot material: " + _slotData.material.name + " and Channel count is not the same. Overlay must be removed or fixed manually", MessageType.Error);
                    }
                    if (GUILayout.Button("Select Slot in Project"))
                    {
                        Selection.activeObject = _slotData.asset;
                    }

                    if (GUILayout.Button("Select Overlay in Project"))
                    {
                        Selection.activeObject = _overlayData.asset;
                    }
                }
            }

            changed |= OnColorGUI();
            changed |= OnTagsGUI();

            if (_overlayData.asset.material != null && _overlayData.asset.material.materialType == UMAMaterial.MaterialType.UseExistingTextures)
            {
                int useUV = EditorGUILayout.Popup("UV Set for this overlay", _overlayData.UVSet, new string[] { "No Change", "UV Set 1", "UV Set 2", "UV Set 3" });
                if (useUV != _overlayData.UVSet)
                {
                    _overlayData.UVSet = useUV;
                    changed = true;
                }
            }
            else
            {
                if (_overlayData.UVSet != 0) 
                {
                    _overlayData.UVSet = 0;
                    changed = true;
                }
            }


            GUILayout.BeginHorizontal();
            GUILayout.Label("Textures");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var texture in _textures)
            {
                changed |= texture.OnGUI(true);
            }
            GUILayout.EndHorizontal();


            GUILayout.EndVertical();

            GUIHelper.EndVerticalPadded(10);


            changed |= popupchanged;
            if (changed)
            {
                MarkRecipeContextDirty();
            }
            return changed;
        }

        private void MarkRecipeContextDirty()
        {
            if (_recipeContext == null)
            {
                return;
            }

            EditorUtility.SetDirty(_recipeContext);
            if (EditorUtility.IsPersistent(_recipeContext))
            {
                AssetDatabase.SaveAssetIfDirty(_recipeContext);
            }
        }

        private sealed class OverlayRectPositionWindow : EditorWindow
        {
            private const float HandleSize = 12f;
            private const float MinRectSize = 0.02f;
            private const float PreviewPadding = 16f;

            private OverlayEditor _owner;
            private SlotData _slotData;
            private OverlayData _overlayData;
            private Rect _originalRecipeRect;
            private Rect _originalAssetRect;
            private Rect _workingRect;
            private bool _workingInstanceTransformed;
            private float _workingRotation;
            private Vector2 _workingScale;
            private Vector2 _workingTranslate;
            private bool _originalInstanceTransformed;
            private float _originalRotation;
            private Vector2 _originalScale;
            private Vector2 _originalTranslate;
            private bool _useUvRect;
            private List<BaseOverlayChoice> _slotBaseChoices = new List<BaseOverlayChoice>();
            private string[] _slotBaseChoiceNames = Array.Empty<string>();
            private int _selectedSlotBaseChoice;
            private List<BaseOverlayChoice> _raceBaseChoices = new List<BaseOverlayChoice>();
            private string[] _raceBaseChoiceNames = Array.Empty<string>();
            private int _selectedRaceBaseChoice;
            private List<BaseOverlayChoice> _sceneAvatarBaseChoices = new List<BaseOverlayChoice>();
            private string[] _sceneAvatarBaseChoiceNames = Array.Empty<string>();
            private int _selectedSceneAvatarBaseChoice;
            private List<string> _raceLookupMessages = new List<string>();
            private BaseOverlaySource _baseOverlaySource;
            private OverlayDataAsset _pickedBaseOverlayAsset;
            private DragMode _dragMode;
            private Rect _dragStartDisplayRect;
            private Vector2 _dragStartMouse;
            private bool _rectChanged;
            private RenderTexture _previewRenderTexture;
            private float _dragStartRotation;
            private Vector2 _dragStartScale;
            private Vector2 _dragPivot;
            private readonly HashSet<string> _baseTextureLookupLogMessages = new HashSet<string>();
            private static readonly Color PreviewClear = new Color(0f, 0f, 0f, 0f);
            private const int SlotDiagnosticLimit = 24;

            private enum BaseOverlaySource
            {
                None,
                SlotOverlay,
                RaceRecipeOverlay,
                SceneAvatarRaceOverlay,
                AssetPicker
            }

            private sealed class BaseOverlayChoice
            {
                public string Label;
                public OverlayData Overlay;
            }

            private enum DragMode
            {
                None,
                Move,
                TopLeft,
                TopRight,
                BottomLeft,
                BottomRight,
                Rotate,
                Scale
            }

            public static void Open(OverlayEditor owner, SlotData slotData, OverlayData overlayData, OverlayDataAsset preferredBaseOverlay)
            {
                if (owner == null || slotData == null || overlayData == null || overlayData.asset == null)
                {
                    return;
                }

                OverlayRectPositionWindow window = CreateInstance<OverlayRectPositionWindow>();
                window.titleContent = new GUIContent("Overlay Positioner");
                window.minSize = new Vector2(700f, 760f);
                window.Initialize(owner, slotData, overlayData, preferredBaseOverlay);
                window.ShowUtility();
            }

            private void Initialize(OverlayEditor owner, SlotData slotData, OverlayData overlayData, OverlayDataAsset preferredBaseOverlay)
            {
                _owner = owner;
                _slotData = slotData;
                _overlayData = overlayData;
                _originalRecipeRect = overlayData.rect;
                _originalAssetRect = overlayData.asset != null ? overlayData.asset.rect : overlayData.rect;
                _workingRect = overlayData.rect;
                _originalInstanceTransformed = overlayData.instanceTransformed;
                _originalRotation = overlayData.Rotation;
                _originalScale = overlayData.Scale;
                _originalTranslate = overlayData.Translate;
                _workingInstanceTransformed = overlayData.instanceTransformed;
                _workingRotation = overlayData.Rotation;
                _workingScale = overlayData.Scale;
                _workingTranslate = overlayData.Translate;
                _useUvRect = LooksLikeUvRect(_workingRect);
                _pickedBaseOverlayAsset = preferredBaseOverlay;
                BuildBaseChoices(preferredBaseOverlay);
                LogBaseTextureLookup("Opened overlay positioner lookup diagnostics for " + GetOverlayLookupContext() + ".");
            }

            private void BuildBaseChoices(OverlayDataAsset preferredBaseOverlay)
            {
                BuildSlotBaseChoices(preferredBaseOverlay);
                BuildRaceBaseChoices(preferredBaseOverlay);
                BuildSceneAvatarBaseChoices(preferredBaseOverlay);
                SetDefaultBaseOverlaySource(preferredBaseOverlay);
            }

            private void BuildSceneAvatarBaseChoices(OverlayDataAsset preferredBaseOverlay)
            {
                _sceneAvatarBaseChoices.Clear();
                List<string> labels = new List<string> { "<None>" };
                _selectedSceneAvatarBaseChoice = 0;

                DynamicCharacterAvatar[] avatars = UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (avatars == null || avatars.Length == 0)
                {
                    _sceneAvatarBaseChoiceNames = labels.ToArray();
                    return;
                }

                DynamicCharacterAvatar avatar = avatars[0];
                if (avatar == null || avatar.activeRace == null || avatar.activeRace.data == null || avatar.activeRace.data.baseRaceRecipe == null)
                {
                    _sceneAvatarBaseChoiceNames = labels.ToArray();
                    return;
                }

                UMAData.UMARecipe raceRecipe = avatar.activeRace.data.baseRaceRecipe.GetCachedRecipe();
                if (raceRecipe == null)
                {
                    _sceneAvatarBaseChoiceNames = labels.ToArray();
                    return;
                }

                SlotData[] slots = raceRecipe.GetAllSlots();
                if (slots == null)
                {
                    _sceneAvatarBaseChoiceNames = labels.ToArray();
                    return;
                }

                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    SlotData slot = slots[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }

                    List<OverlayData> overlays = slot.GetOverlayList();
                    if (overlays == null)
                    {
                        continue;
                    }

                    for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
                    {
                        OverlayData choice = overlays[overlayIndex];
                        if (choice == null || choice.asset == null)
                        {
                            continue;
                        }

                        _sceneAvatarBaseChoices.Add(new BaseOverlayChoice
                        {
                            Label = avatar.name + " / " + slot.slotName + " / " + choice.overlayName,
                            Overlay = choice.Duplicate()
                        });
                        labels.Add(avatar.name + " / " + slot.slotName + " / " + choice.overlayName);

                        if (preferredBaseOverlay != null && choice.asset == preferredBaseOverlay)
                        {
                            _selectedSceneAvatarBaseChoice = _sceneAvatarBaseChoices.Count;
                        }
                    }
                }

                _sceneAvatarBaseChoiceNames = labels.ToArray();
            }

            private void BuildSlotBaseChoices(OverlayDataAsset preferredBaseOverlay)
            {
                _slotBaseChoices.Clear();
                List<string> labels = new List<string> { "<None>" };

                List<OverlayData> slotOverlays = _slotData.GetOverlayList();
                OverlayData defaultChoice = null;
                int currentIndex = slotOverlays.IndexOf(_overlayData);

                if (currentIndex > 0 && slotOverlays.Count > 0)
                {
                    OverlayData firstOverlay = slotOverlays[0];
                    if (firstOverlay != null && firstOverlay != _overlayData)
                    {
                        defaultChoice = firstOverlay;
                    }
                }

                for (int i = 0; i < slotOverlays.Count; i++)
                {
                    OverlayData choice = slotOverlays[i];
                    if (choice == null || choice == _overlayData || choice.asset == null)
                    {
                        continue;
                    }

                    _slotBaseChoices.Add(new BaseOverlayChoice
                    {
                        Label = choice.overlayName + " (slot index " + i + ")",
                        Overlay = choice
                    });
                    labels.Add(choice.overlayName + " (" + i + ")");
                }

                _slotBaseChoiceNames = labels.ToArray();
                _selectedSlotBaseChoice = 0;

                if (preferredBaseOverlay != null)
                {
                    for (int i = 0; i < _slotBaseChoices.Count; i++)
                    {
                        if (_slotBaseChoices[i].Overlay.asset == preferredBaseOverlay)
                        {
                            _selectedSlotBaseChoice = i + 1;
                            return;
                        }
                    }
                }

                if (defaultChoice != null)
                {
                    for (int i = 0; i < _slotBaseChoices.Count; i++)
                    {
                        if (_slotBaseChoices[i].Overlay == defaultChoice)
                        {
                            _selectedSlotBaseChoice = i + 1;
                            return;
                        }
                    }
                }
            }

            private void BuildRaceBaseChoices(OverlayDataAsset preferredBaseOverlay)
            {
                _raceBaseChoices.Clear();
                _raceLookupMessages.Clear();
                List<string> labels = new List<string> { "<None>" };
                _selectedRaceBaseChoice = 0;

                UMAWardrobeRecipe wardrobeRecipe = _owner.ResolveWardrobeRecipeContext();
                if (wardrobeRecipe == null || wardrobeRecipe.compatibleRaces == null)
                {
                    _raceLookupMessages.Add("No UMAWardrobeRecipe context was available for race recipe overlay lookup.");
                    _raceBaseChoiceNames = labels.ToArray();
                    return;
                }

                if (wardrobeRecipe.compatibleRaces.Count == 0)
                {
                    _raceLookupMessages.Add("The current wardrobe recipe has no compatible races.");
                }

                for (int i = 0; i < wardrobeRecipe.compatibleRaces.Count; i++)
                {
                    string raceName = wardrobeRecipe.compatibleRaces[i];
                    if (string.IsNullOrWhiteSpace(raceName))
                    {
                        _raceLookupMessages.Add("Encountered an empty compatible race entry.");
                        continue;
                    }

                    RaceData raceData = ResolveRaceDataByRaceName(raceName, out string resolutionMessage);
                    _raceLookupMessages.Add(resolutionMessage);

                    if (raceData == null)
                    {
                        continue;
                    }

                    if (raceData.baseRaceRecipe == null)
                    {
                        _raceLookupMessages.Add("Race '" + raceData.raceName + "' was found, but has no baseRaceRecipe.");
                        continue;
                    }

                    UMAData.UMARecipe raceRecipe = raceData.baseRaceRecipe.GetCachedRecipe();
                    if (raceRecipe == null)
                    {
                        _raceLookupMessages.Add("Race '" + raceData.raceName + "' baseRaceRecipe could not be loaded.");
                        continue;
                    }

                    SlotData[] slots = raceRecipe.GetAllSlots();
                    if (slots == null)
                    {
                        _raceLookupMessages.Add("Race '" + raceData.raceName + "' baseRaceRecipe returned no slots.");
                        continue;
                    }

                    int overlaysAddedForRace = 0;

                    for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                    {
                        SlotData slot = slots[slotIndex];
                        if (slot == null)
                        {
                            continue;
                        }

                        List<OverlayData> overlays = slot.GetOverlayList();
                        if (overlays == null)
                        {
                            continue;
                        }

                        for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
                        {
                            OverlayData choice = overlays[overlayIndex];
                            if (choice == null || choice.asset == null)
                            {
                                continue;
                            }

                            _raceBaseChoices.Add(new BaseOverlayChoice
                            {
                                Label = raceName + " / " + slot.slotName + " / " + choice.overlayName,
                                Overlay = choice.Duplicate()
                            });
                            labels.Add(raceName + " / " + slot.slotName + " / " + choice.overlayName);
                            overlaysAddedForRace++;

                            if (preferredBaseOverlay != null && choice.asset == preferredBaseOverlay)
                            {
                                _selectedRaceBaseChoice = _raceBaseChoices.Count;
                            }
                        }
                    }

                    _raceLookupMessages.Add("Race '" + raceData.raceName + "' contributed " + overlaysAddedForRace + " overlay option(s).");
                }

                _raceBaseChoiceNames = labels.ToArray();
            }

            private static RaceData ResolveRaceDataByRaceName(string raceName, out string resolutionMessage)
            {
                resolutionMessage = "Race lookup not attempted.";
                if (string.IsNullOrWhiteSpace(raceName))
                {
                    resolutionMessage = "Race lookup skipped because the compatible race name was empty.";
                    return null;
                }

                UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
                if (indexer == null)
                {
                    resolutionMessage = "Race '" + raceName + "' could not be resolved because UMAAssetIndexer.Instance is null.";
                    return null;
                }

                RaceData raceData = indexer.GetAsset<RaceData>(raceName, false, true);
                if (raceData != null)
                {
                    resolutionMessage = "Race lookup '" + raceName + "' resolved directly to asset '" + raceData.name + "' with raceName '" + raceData.raceName + "'.";
                    return raceData;
                }

                List<RaceData> allRaces = indexer.GetAllAssets<RaceData>();
                for (int i = 0; i < allRaces.Count; i++)
                {
                    RaceData candidate = allRaces[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (string.Equals(candidate.raceName, raceName, StringComparison.OrdinalIgnoreCase))
                    {
                        resolutionMessage = "Race lookup '" + raceName + "' resolved by scanning indexed RaceData assets. Asset='" + candidate.name + "', raceName='" + candidate.raceName + "'.";
                        return candidate;
                    }
                }

                resolutionMessage = "Race lookup '" + raceName + "' failed in UMAAssetIndexer. No indexed RaceData with matching raceName was found.";
                return null;
            }

            private void SetDefaultBaseOverlaySource(OverlayDataAsset preferredBaseOverlay)
            {
                _baseOverlaySource = BaseOverlaySource.None;

                if (preferredBaseOverlay != null)
                {
                    if (_selectedSlotBaseChoice > 0)
                    {
                        _baseOverlaySource = BaseOverlaySource.SlotOverlay;
                        return;
                    }

                    if (_selectedRaceBaseChoice > 0)
                    {
                        _baseOverlaySource = BaseOverlaySource.RaceRecipeOverlay;
                        return;
                    }

                    if (_selectedSceneAvatarBaseChoice > 0)
                    {
                        _baseOverlaySource = BaseOverlaySource.SceneAvatarRaceOverlay;
                        return;
                    }

                    _baseOverlaySource = BaseOverlaySource.AssetPicker;
                    return;
                }

                if (_selectedSlotBaseChoice > 0)
                {
                    _baseOverlaySource = BaseOverlaySource.SlotOverlay;
                    return;
                }

                if (_selectedRaceBaseChoice > 0)
                {
                    _baseOverlaySource = BaseOverlaySource.RaceRecipeOverlay;
                    return;
                }

                if (_selectedSceneAvatarBaseChoice > 0)
                {
                    _baseOverlaySource = BaseOverlaySource.SceneAvatarRaceOverlay;
                }
            }

            private OverlayData SelectedBaseOverlay
            {
                get
                {
                    switch (_baseOverlaySource)
                    {
                        case BaseOverlaySource.SlotOverlay:
                            if (_selectedSlotBaseChoice > 0 && _selectedSlotBaseChoice - 1 < _slotBaseChoices.Count)
                            {
                                return _slotBaseChoices[_selectedSlotBaseChoice - 1].Overlay;
                            }
                            break;
                        case BaseOverlaySource.RaceRecipeOverlay:
                            if (_selectedRaceBaseChoice > 0 && _selectedRaceBaseChoice - 1 < _raceBaseChoices.Count)
                            {
                                return _raceBaseChoices[_selectedRaceBaseChoice - 1].Overlay;
                            }
                            break;
                        case BaseOverlaySource.SceneAvatarRaceOverlay:
                            if (_selectedSceneAvatarBaseChoice > 0 && _selectedSceneAvatarBaseChoice - 1 < _sceneAvatarBaseChoices.Count)
                            {
                                return _sceneAvatarBaseChoices[_selectedSceneAvatarBaseChoice - 1].Overlay;
                            }
                            break;
                        case BaseOverlaySource.AssetPicker:
                            if (_pickedBaseOverlayAsset != null)
                            {
                                return new OverlayData(_pickedBaseOverlayAsset);
                            }
                            break;
                    }

                    return null;
                }
            }

            private void OnGUI()
            {
                if (_owner == null || _slotData == null || _overlayData == null || _overlayData.asset == null)
                {
                    Close();
                    return;
                }

                EditorGUILayout.LabelField("Position Overlay", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Drag inside the rectangle to move the overlay. Drag the corner handles to resize it against the selected base overlay preview.", MessageType.Info);

                EditorGUILayout.LabelField("Overlay", _overlayData.overlayName);

                BaseOverlaySource newSource = (BaseOverlaySource)EditorGUILayout.EnumPopup("Base Overlay Source", _baseOverlaySource);
                if (newSource != _baseOverlaySource)
                {
                    _baseOverlaySource = newSource;
                    Repaint();
                }

                if (_baseOverlaySource == BaseOverlaySource.SlotOverlay)
                {
                    int newSlotBaseChoice = EditorGUILayout.Popup("Slot Overlay", _selectedSlotBaseChoice, _slotBaseChoiceNames);
                    if (newSlotBaseChoice != _selectedSlotBaseChoice)
                    {
                        _selectedSlotBaseChoice = newSlotBaseChoice;
                        Repaint();
                    }
                }

                if (_baseOverlaySource == BaseOverlaySource.RaceRecipeOverlay)
                {
                    using (new EditorGUI.DisabledScope(_raceBaseChoiceNames.Length <= 1))
                    {
                        int newRaceBaseChoice = EditorGUILayout.Popup("Race Base Overlay", _selectedRaceBaseChoice, _raceBaseChoiceNames);
                        if (newRaceBaseChoice != _selectedRaceBaseChoice)
                        {
                            _selectedRaceBaseChoice = newRaceBaseChoice;
                            Repaint();
                        }
                    }

                    if (_raceBaseChoiceNames.Length <= 1)
                    {
                        EditorGUILayout.HelpBox("No compatible-race base recipe overlays were found for the current wardrobe recipe context.", MessageType.Info);
                    }

                    if (_raceLookupMessages.Count > 0)
                    {
                        GUIHelper.BeginVerticalPadded(4, new Color(0.92f, 0.92f, 0.92f, 1f));
                        EditorGUILayout.LabelField("Race Lookup Diagnostics", EditorStyles.boldLabel);
                        for (int i = 0; i < _raceLookupMessages.Count; i++)
                        {
                            EditorGUILayout.LabelField("- " + _raceLookupMessages[i], EditorStyles.wordWrappedMiniLabel);
                        }
                        GUIHelper.EndVerticalPadded(4);
                    }
                }

                if (_baseOverlaySource == BaseOverlaySource.SceneAvatarRaceOverlay)
                {
                    using (new EditorGUI.DisabledScope(_sceneAvatarBaseChoiceNames.Length <= 1))
                    {
                        int newSceneAvatarBaseChoice = EditorGUILayout.Popup("Scene Avatar Base Overlay", _selectedSceneAvatarBaseChoice, _sceneAvatarBaseChoiceNames);
                        if (newSceneAvatarBaseChoice != _selectedSceneAvatarBaseChoice)
                        {
                            _selectedSceneAvatarBaseChoice = newSceneAvatarBaseChoice;
                            Repaint();
                        }
                    }

                    if (_sceneAvatarBaseChoiceNames.Length <= 1)
                    {
                        EditorGUILayout.HelpBox("No base race recipe overlays were found on the first character in the scene.", MessageType.Info);
                    }
                }

                if (_baseOverlaySource == BaseOverlaySource.AssetPicker)
                {
                    OverlayDataAsset newPickedBaseOverlay = EditorGUILayout.ObjectField("Base Overlay Asset", _pickedBaseOverlayAsset, typeof(OverlayDataAsset), false) as OverlayDataAsset;
                    if (newPickedBaseOverlay != _pickedBaseOverlayAsset)
                    {
                        _pickedBaseOverlayAsset = newPickedBaseOverlay;
                        Repaint();
                    }
                }

                _owner._baseOverlayData = SelectedBaseOverlay != null ? SelectedBaseOverlay.asset : null;

                bool delayedRectCommitted = false;
                bool delayedTransformCommitted = false;

                float rectX = EditorGUILayout.DelayedFloatField("Rect X", _workingRect.x);
                float rectY = EditorGUILayout.DelayedFloatField("Rect Y", _workingRect.y);
                float rectW = EditorGUILayout.DelayedFloatField("Rect W", _workingRect.width);
                float rectH = EditorGUILayout.DelayedFloatField("Rect H", _workingRect.height);
                Rect editedRect = new Rect(rectX, rectY, rectW, rectH);
                if (editedRect != _workingRect)
                {
                    _workingRect = editedRect;
                    _rectChanged = true;
                    delayedRectCommitted = true;
                    Repaint();
                }

                bool newInstanceTransformed = EditorGUILayout.Toggle("Transform", _workingInstanceTransformed);
                if (newInstanceTransformed != _workingInstanceTransformed)
                {
                    _workingInstanceTransformed = newInstanceTransformed;
                    _rectChanged = true;
                }

                if (_workingInstanceTransformed)
                {
                    GUIHelper.BeginVerticalPadded(5, new Color(1, 1, 1, 1));
                    EditorGUILayout.HelpBox("Warning: translating, scaling or rotation could result in writing outside the bounds of the texture on the atlas. Be sure to use only in safe areas.", MessageType.Info);

                    float newRotation = EditorGUILayout.DelayedFloatField("Rotation", _workingRotation);
                    if (!Mathf.Approximately(newRotation, _workingRotation))
                    {
                        _workingRotation = newRotation;
                        _rectChanged = true;
                        delayedTransformCommitted = true;
                    }

                    float newScaleX = EditorGUILayout.DelayedFloatField("Scale X", _workingScale.x);
                    float newScaleY = EditorGUILayout.DelayedFloatField("Scale Y", _workingScale.y);
                    Vector2 newScale = new Vector2(newScaleX, newScaleY);
                    if (newScale != _workingScale)
                    {
                        _workingScale = newScale;
                        _rectChanged = true;
                        delayedTransformCommitted = true;
                    }

                    EditorGUILayout.LabelField("Translation: ");
                    float newTranslateX = EditorGUILayout.Slider("X:", _workingTranslate.x * 100.0f, -100.0f, 100.0f) / 100.0f;
                    float newTranslateY = EditorGUILayout.Slider("Y:", _workingTranslate.y * 100.0f, -100.0f, 100.0f) / 100.0f;
                    if (!Mathf.Approximately(newTranslateX, _workingTranslate.x) || !Mathf.Approximately(newTranslateY, _workingTranslate.y))
                    {
                        _workingTranslate = new Vector2(newTranslateX, newTranslateY);
                        _rectChanged = true;
                    }

                    GUIHelper.EndVerticalPadded(5);
                }

                if (delayedRectCommitted || delayedTransformCommitted)
                {
                    if (delayedRectCommitted && delayedTransformCommitted)
                    {
                        _owner.PreviewPopupTransform(_workingInstanceTransformed, _workingRotation, _workingScale, _workingTranslate);
                        _owner.ApplyPopupRect(_workingRect, false);
                    }
                    else if (delayedRectCommitted)
                    {
                        _owner.ApplyPopupRect(_workingRect, false);
                    }
                    else
                    {
                        _owner.ApplyPopupTransform(_workingInstanceTransformed, _workingRotation, _workingScale, _workingTranslate);
                    }

                    _rectChanged = false;
                }
                else if (_rectChanged)
                {
                    _owner.PreviewPopupTransform(_workingInstanceTransformed, _workingRotation, _workingScale, _workingTranslate);
                    _rectChanged = false;
                }

                Texture baseTexture = GetEffectiveBasePreviewTexture();
                Texture overlayTexture = GetPreviewTexture(_overlayData);
                Rect previewArea = GUILayoutUtility.GetRect(10f, 10000f, 320f, 520f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                Rect previewRect = GetPreviewRect(previewArea, baseTexture, overlayTexture);

                DrawPreview(previewArea, previewRect, baseTexture, overlayTexture);
                HandlePreviewInteraction(previewRect, overlayTexture != null);

                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Update Overlay"))
                {
                    _owner.ApplyPopupTransform(_workingInstanceTransformed, _workingRotation, _workingScale, _workingTranslate);
                    _owner.ApplyPopupRect(_workingRect, true);
                    _rectChanged = false;
                }

                if (GUILayout.Button("Update Recipe"))
                {
                    _owner.ApplyPopupTransform(_workingInstanceTransformed, _workingRotation, _workingScale, _workingTranslate);
                    _owner.ApplyPopupRect(_workingRect, false);
                    _rectChanged = false;
                }

                if (GUILayout.Button("Close"))
                {
                    if (_rectChanged)
                    {
                        _owner.ApplyPopupTransform(_workingInstanceTransformed, _workingRotation, _workingScale, _workingTranslate);
                        _owner.ApplyPopupRect(_workingRect, false);
                        _rectChanged = false;
                    }
                    Close();
                }

                if (GUILayout.Button("Cancel"))
                {
                    _owner.RestorePopupTransforms(_originalInstanceTransformed, _originalRotation, _originalScale, _originalTranslate);
                    _owner.RestorePopupRects(_originalRecipeRect, _originalAssetRect);
                    Close();
                }

                GUILayout.EndHorizontal();
            }

            private void DrawPreview(Rect previewArea, Rect previewRect, Texture baseTexture, Texture overlayTexture)
            {
                EditorGUI.DrawRect(previewArea, new Color(0.15f, 0.15f, 0.15f, 1f));
                DrawCheckerboard(previewRect);

                Rect overlayDisplayRect = GetDisplayRectFromWorkingRect(previewRect);
                bool drewWithTextureMerge = false;
                if (overlayTexture != null || baseTexture != null)
                {
                    drewWithTextureMerge = DrawPreviewUsingTextureMerge(previewRect, overlayDisplayRect, baseTexture, overlayTexture);
                }

                if (!drewWithTextureMerge)
                {
                    if (baseTexture != null)
                    {
                        DrawTexture(previewRect, baseTexture, GetOverlayTint(SelectedBaseOverlay));
                    }
                    if (overlayTexture != null)
                    {
                        DrawTransformedOverlayTexture(previewRect, overlayDisplayRect, overlayTexture, GetOverlayTint(_overlayData));
                    }
                }

                DrawOverlayOutline(overlayDisplayRect);
                DrawTransformHandles(overlayDisplayRect);
            }

            private void DrawTransformHandles(Rect overlayRect)
            {
                Rect rotateHandle = GetRotateHandleRect(overlayRect);
                Rect scaleHandle = GetScaleHandleRect(overlayRect);

                DrawRotateHandle(rotateHandle);
                DrawScaleHandle(scaleHandle);
                DrawTransformHandleLabels(rotateHandle, scaleHandle);
            }

            private void DrawRotateHandle(Rect rotateHandle)
            {
                Handles.BeginGUI();
                Vector3 center = new Vector3(rotateHandle.center.x, rotateHandle.center.y, 0f);
                float radius = rotateHandle.width * 0.5f;
                Handles.color = new Color(1f, 0.76f, 0.2f, 1f);
                Handles.DrawSolidDisc(center, Vector3.forward, radius);
                Handles.color = Color.white;
                Handles.DrawWireDisc(center, Vector3.forward, radius - 1f);

                DrawArrow(center + new Vector3(radius + 6f, 0f, 0f), Vector2.right, new Color(1f, 0.76f, 0.2f, 1f));
                DrawArrow(center + new Vector3(-(radius + 6f), 0f, 0f), Vector2.left, new Color(1f, 0.76f, 0.2f, 1f));
                Handles.EndGUI();
            }

            private void DrawScaleHandle(Rect scaleHandle)
            {
                EditorGUI.DrawRect(scaleHandle, new Color(0.85f, 0.35f, 1f, 1f));
                EditorGUI.DrawRect(new Rect(scaleHandle.x + 2f, scaleHandle.y + 2f, scaleHandle.width - 4f, scaleHandle.height - 4f), Color.white);

                Handles.BeginGUI();
                Vector3 center = new Vector3(scaleHandle.center.x, scaleHandle.center.y, 0f);
                DrawArrow(center + new Vector3(scaleHandle.width * 0.75f + 6f, 0f, 0f), Vector2.right, new Color(0.85f, 0.35f, 1f, 1f));
                DrawArrow(center + new Vector3(-(scaleHandle.width * 0.75f + 6f), 0f, 0f), Vector2.left, new Color(0.85f, 0.35f, 1f, 1f));
                Handles.EndGUI();
            }

            private static void DrawTransformHandleLabels(Rect rotateHandle, Rect scaleHandle)
            {
                GUIStyle labelStyle = EditorStyles.miniLabel != null ? EditorStyles.miniLabel : EditorStyles.label;

                Rect rotateLabelRect = new Rect(rotateHandle.center.x - 24f, rotateHandle.yMin - 16f, 48f, 14f);
                Rect scaleLabelRect = new Rect(scaleHandle.center.x - 20f, scaleHandle.yMin - 16f, 40f, 14f);

                GUI.Label(rotateLabelRect, "Rotate", labelStyle);
                GUI.Label(scaleLabelRect, "Scale", labelStyle);
            }

            private static void DrawArrow(Vector3 position, Vector2 direction, Color color)
            {
                float length = 8f;
                float head = 4f;
                Vector3 dir = new Vector3(direction.x, direction.y, 0f).normalized;
                Vector3 end = position + (dir * length);
                Vector3 perp = new Vector3(-dir.y, dir.x, 0f);

                Handles.color = color;
                Handles.DrawLine(position, end);
                Handles.DrawLine(end, end - (dir * head) + (perp * (head * 0.6f)));
                Handles.DrawLine(end, end - (dir * head) - (perp * (head * 0.6f)));
            }

            private bool DrawPreviewUsingTextureMerge(Rect previewRect, Rect overlayDisplayRect, Texture baseTexture, Texture overlayTexture)
            {
                int sourceWidth = 1;
                int sourceHeight = 1;
                Texture referenceTexture = GetRuntimeBaseTexture();
                if (referenceTexture == null)
                {
                    referenceTexture = baseTexture;
                }
                if (referenceTexture == null)
                {
                    referenceTexture = overlayTexture;
                }
                if (referenceTexture != null)
                {
                    sourceWidth = Mathf.Max(1, referenceTexture.width);
                    sourceHeight = Mathf.Max(1, referenceTexture.height);
                }

                int width = sourceWidth;
                int height = sourceHeight;

                TextureMerge textureMerge = EnsurePreviewTextureMergeResources(width, height);
                if (textureMerge == null)
                {
                    return false;
                }

                if (textureMerge == null || _previewRenderTexture == null)
                {
                    return false;
                }

                textureMerge.EnsurePreviewRectCapacity(2);
                TextureMerge.TextureMergeRect[] rects = textureMerge.GetPreviewRects();
                if (rects == null || rects.Length < 2)
                {
                    return false;
                }

                rects[0].tex = baseTexture != null ? baseTexture : Texture2D.blackTexture;
                rects[0].rect = new Rect(0f, 0f, width, height);
                rects[0].transform = false;
                if (rects[0].mat == null)
                {
                    return false;
                }
                rects[0].scale = Vector3.one;
                rects[0].position = Vector2.zero;
                rects[0].rotation = 0f;
                rects[0].advancedBlending = false;
                rects[0].textureChannel = 0;
                rects[0].channelType = UMAMaterial.ChannelType.DiffuseTexture;

                Rect previewOverlayRect = GetPreviewOverlayAtlasRect(width, height, referenceTexture);
                rects[1].tex = overlayTexture != null ? overlayTexture : Texture2D.blackTexture;
                rects[1].rect = previewOverlayRect;
                rects[1].transform = _workingInstanceTransformed;
                rects[1].rotation = _workingRotation;
                rects[1].scale = new Vector3(_workingScale.x, _workingScale.y, 1f);
                float translationReferenceWidth = referenceTexture != null ? referenceTexture.width : sourceWidth;
                float translationReferenceHeight = referenceTexture != null ? referenceTexture.height : sourceHeight;
                rects[1].position = new Vector2(_workingTranslate.x * translationReferenceWidth, _workingTranslate.y * translationReferenceHeight);
                if (rects[1].mat == null)
                {
                    return false;
                }
                rects[1].advancedBlending = false;
                rects[1].textureChannel = 0;
                rects[1].channelType = UMAMaterial.ChannelType.DiffuseTexture;

                Texture baseAlpha = SelectedBaseOverlay != null && SelectedBaseOverlay.asset != null ? SelectedBaseOverlay.asset.GetAlphaMask() : null;
                Texture overlayAlpha = _overlayData != null && _overlayData.asset != null ? _overlayData.asset.GetAlphaMask() : null;
                Color baseMultiply = SelectedBaseOverlay != null && SelectedBaseOverlay.colorData != null ? SelectedBaseOverlay.colorData.GetTint(0) : Color.white;
                Color baseAdditive = SelectedBaseOverlay != null && SelectedBaseOverlay.colorData != null ? SelectedBaseOverlay.colorData.GetAdditive(0) : OverlayColorData.EmptyAdditive;
                Color overlayMultiply = _overlayData != null && _overlayData.colorData != null ? _overlayData.colorData.GetTint(0) : Color.white;
                Color overlayAdditive = _overlayData != null && _overlayData.colorData != null ? _overlayData.colorData.GetAdditive(0) : OverlayColorData.EmptyAdditive;

                PreparePreviewMaterial(rects[0].mat, rects[0].tex, baseAlpha, baseMultiply, baseAdditive);
                PreparePreviewMaterial(rects[1].mat, rects[1].tex, overlayAlpha, overlayMultiply, overlayAdditive);

                textureMerge.SetPreviewRectCount(2);

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _previewRenderTexture;
                GL.Clear(true, true, PreviewClear);
                RenderTexture.active = previous;

                Color backgroundColor = UMAMaterial.GetBackgroundColor(UMAMaterial.ChannelType.DiffuseTexture);
                textureMerge.DrawAllRects(_previewRenderTexture, width, height, backgroundColor, true);
                GUI.DrawTexture(previewRect, _previewRenderTexture, ScaleMode.StretchToFill, false);
                return true;
            }

            private Rect GetPreviewOverlayAtlasRect(int atlasWidth, int atlasHeight, Texture baseReferenceTexture)
            {
                Rect overlayRect = _workingRect;
                if (baseReferenceTexture != null && LooksLikeUvRect(overlayRect))
                {
                    overlayRect = new Rect(
                        overlayRect.x * baseReferenceTexture.width,
                        overlayRect.y * baseReferenceTexture.height,
                        overlayRect.width * baseReferenceTexture.width,
                        overlayRect.height * baseReferenceTexture.height);
                }

                return new Rect(
                    overlayRect.x,
                    atlasHeight - overlayRect.y - overlayRect.height,
                    overlayRect.width,
                    overlayRect.height);
            }

            private TextureMerge EnsurePreviewTextureMergeResources(int width, int height)
            {
                UMASettings settings = UMASettings.GetOrCreateSettings();
                if (settings == null)
                {
                    return null;
                }

                TextureMerge textureMerge = settings.textureMerge;

                if (textureMerge == null)
                {
                    return null;
                }

                textureMerge.RefreshMaterials();

                if (_previewRenderTexture == null || _previewRenderTexture.width != width || _previewRenderTexture.height != height)
                {
                    if (_previewRenderTexture != null)
                    {
                        _previewRenderTexture.Release();
                        DestroyImmediate(_previewRenderTexture);
                    }

                    _previewRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    _previewRenderTexture.Create();
                }

                return textureMerge;
            }

            private void PreparePreviewMaterial(Material material, Texture mainTex, Texture extraTex, Color tint, Color additive)
            {
                if (material == null)
                {
                    return;
                }

                tint.a = 1f;
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", mainTex);
                }
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", tint);
                }
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", tint);
                }

                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", mainTex);
                }

                if (material.HasProperty("_AdditiveColor"))
                {
                    material.SetColor("_AdditiveColor", additive);
                }

                if (material.HasProperty("_ExtraTex"))
                {
                    material.SetTexture("_ExtraTex", extraTex);
                }
            }

            private void OnDisable()
            {
                if (_previewRenderTexture != null)
                {
                    _previewRenderTexture.Release();
                    DestroyImmediate(_previewRenderTexture);
                    _previewRenderTexture = null;
                }
            }

            private void DrawTransformedOverlayTexture(Rect previewRect, Rect overlayDisplayRect, Texture overlayTexture, Color tint)
            {
                if (overlayTexture == null)
                {
                    return;
                }

                if (!_workingInstanceTransformed)
                {
                    DrawTexture(overlayDisplayRect, overlayTexture, tint);
                    return;
                }

                GUI.BeginGroup(previewRect);
                Matrix4x4 oldMatrix = GUI.matrix;
                try
                {
                    Rect localRect = new Rect(
                        overlayDisplayRect.x - previewRect.x,
                        overlayDisplayRect.y - previewRect.y,
                        overlayDisplayRect.width,
                        overlayDisplayRect.height);

                    float posX = _workingTranslate.x * previewRect.width;
                    float posY = _workingTranslate.y * previewRect.height;

                    localRect.x += posX;
                    localRect.y += posY;

                    Vector2 pivot = new Vector2(localRect.x + (localRect.width * 0.5f), localRect.y + (localRect.height * 0.5f));
                    Matrix4x4 transform = Matrix4x4.TRS(
                        pivot,
                        Quaternion.Euler(0f, 0f, _workingRotation),
                        new Vector3(_workingScale.x, _workingScale.y, 1f)) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);

                    GUI.matrix = oldMatrix * transform;

                    Color previous = GUI.color;
                    GUI.color = tint;
                    GUI.DrawTexture(localRect, overlayTexture, ScaleMode.StretchToFill, true);
                    GUI.color = previous;
                }
                finally
                {
                    GUI.matrix = oldMatrix;
                    GUI.EndGroup();
                }
            }

            private void HandlePreviewInteraction(Rect previewRect, bool hasOverlayTexture)
            {
                if (!hasOverlayTexture)
                {
                    return;
                }

                Event evt = Event.current;
                Rect overlayRect = GetDisplayRectFromWorkingRect(previewRect);
                Rect topLeft = GetHandleRect(overlayRect.xMin, overlayRect.yMin);
                Rect topRight = GetHandleRect(overlayRect.xMax, overlayRect.yMin);
                Rect bottomLeft = GetHandleRect(overlayRect.xMin, overlayRect.yMax);
                Rect bottomRight = GetHandleRect(overlayRect.xMax, overlayRect.yMax);
                Rect rotateHandle = GetRotateHandleRect(overlayRect);
                Rect scaleHandle = GetScaleHandleRect(overlayRect);

                EditorGUIUtility.AddCursorRect(topLeft, MouseCursor.ResizeUpLeft);
                EditorGUIUtility.AddCursorRect(topRight, MouseCursor.ResizeUpRight);
                EditorGUIUtility.AddCursorRect(bottomLeft, MouseCursor.ResizeUpRight);
                EditorGUIUtility.AddCursorRect(bottomRight, MouseCursor.ResizeUpLeft);
                EditorGUIUtility.AddCursorRect(overlayRect, MouseCursor.MoveArrow);
                EditorGUIUtility.AddCursorRect(rotateHandle, MouseCursor.RotateArrow);
                EditorGUIUtility.AddCursorRect(scaleHandle, MouseCursor.ScaleArrow);

                if (evt.type == EventType.MouseDown && evt.button == 0)
                {
                    if (rotateHandle.Contains(evt.mousePosition))
                    {
                        BeginDrag(DragMode.Rotate, overlayRect, evt.mousePosition);
                    }
                    else if (scaleHandle.Contains(evt.mousePosition))
                    {
                        BeginDrag(DragMode.Scale, overlayRect, evt.mousePosition);
                    }
                    else if (topLeft.Contains(evt.mousePosition))
                    {
                        BeginDrag(DragMode.TopLeft, overlayRect, evt.mousePosition);
                    }
                    else if (topRight.Contains(evt.mousePosition))
                    {
                        BeginDrag(DragMode.TopRight, overlayRect, evt.mousePosition);
                    }
                    else if (bottomLeft.Contains(evt.mousePosition))
                    {
                        BeginDrag(DragMode.BottomLeft, overlayRect, evt.mousePosition);
                    }
                    else if (bottomRight.Contains(evt.mousePosition))
                    {
                        BeginDrag(DragMode.BottomRight, overlayRect, evt.mousePosition);
                    }
                    else if (overlayRect.Contains(evt.mousePosition))
                    {
                        BeginDrag(DragMode.Move, overlayRect, evt.mousePosition);
                    }
                }

                if (evt.type == EventType.MouseDrag && _dragMode != DragMode.None)
                {
                    Vector2 delta = evt.mousePosition - _dragStartMouse;
                    Rect draggedRect = _dragStartDisplayRect;

                    switch (_dragMode)
                    {
                        case DragMode.Move:
                            draggedRect.position += delta;
                            break;
                        case DragMode.TopLeft:
                            draggedRect.xMin += delta.x;
                            draggedRect.yMin += delta.y;
                            break;
                        case DragMode.TopRight:
                            draggedRect.xMax += delta.x;
                            draggedRect.yMin += delta.y;
                            break;
                        case DragMode.BottomLeft:
                            draggedRect.xMin += delta.x;
                            draggedRect.yMax += delta.y;
                            break;
                        case DragMode.BottomRight:
                            draggedRect.xMax += delta.x;
                            draggedRect.yMax += delta.y;
                            break;
                        case DragMode.Rotate:
                            {
                                _workingInstanceTransformed = true;
                                Vector2 from = _dragStartMouse - _dragPivot;
                                Vector2 to = evt.mousePosition - _dragPivot;
                                if (from.sqrMagnitude > 0.0001f && to.sqrMagnitude > 0.0001f)
                                {
                                    float deltaAngle = Vector2.SignedAngle(from, to);
                                    _workingRotation = _dragStartRotation + deltaAngle;
                                    _rectChanged = true;
                                }
                            }
                            break;
                        case DragMode.Scale:
                            {
                                _workingInstanceTransformed = true;
                                float from = (_dragStartMouse - _dragPivot).magnitude;
                                float to = (evt.mousePosition - _dragPivot).magnitude;
                                if (from > 0.001f)
                                {
                                    float factor = Mathf.Max(0.01f, to / from);
                                    _workingScale = new Vector2(
                                        Mathf.Max(0.01f, _dragStartScale.x * factor),
                                        Mathf.Max(0.01f, _dragStartScale.y * factor));
                                    _rectChanged = true;
                                }
                            }
                            break;
                    }

                    if (_dragMode == DragMode.Move || _dragMode == DragMode.TopLeft || _dragMode == DragMode.TopRight || _dragMode == DragMode.BottomLeft || _dragMode == DragMode.BottomRight)
                    {
                        draggedRect = ClampDisplayRect(draggedRect, previewRect);
                        _workingRect = GetWorkingRectFromDisplayRect(previewRect, draggedRect);
                        _rectChanged = true;
                    }

                    evt.Use();
                    Repaint();
                }

                if ((evt.type == EventType.MouseUp || evt.type == EventType.Ignore) && _dragMode != DragMode.None)
                {
                    if (_dragMode == DragMode.Rotate || _dragMode == DragMode.Scale)
                    {
                        _owner.ApplyPopupTransform(_workingInstanceTransformed, _workingRotation, _workingScale, _workingTranslate);
                    }
                    else
                    {
                        _owner.ApplyPopupRect(_workingRect, false);
                    }
                    _rectChanged = false;
                    _dragMode = DragMode.None;
                    evt.Use();
                }
            }

            private void BeginDrag(DragMode mode, Rect overlayRect, Vector2 mousePosition)
            {
                _dragMode = mode;
                _dragStartDisplayRect = overlayRect;
                _dragStartMouse = mousePosition;
                _dragStartRotation = _workingRotation;
                _dragStartScale = _workingScale;
                _dragPivot = new Vector2(overlayRect.center.x, overlayRect.center.y);
                Event.current.Use();
            }

            private Rect GetPreviewRect(Rect previewArea, Texture baseTexture, Texture overlayTexture)
            {
                float sourceWidth = 1f;
                float sourceHeight = 1f;

                Texture referenceTexture = baseTexture != null ? baseTexture : overlayTexture;
                if (referenceTexture != null)
                {
                    sourceWidth = Mathf.Max(1f, referenceTexture.width);
                    sourceHeight = Mathf.Max(1f, referenceTexture.height);
                }

                Rect padded = new Rect(
                    previewArea.x + PreviewPadding,
                    previewArea.y + PreviewPadding,
                    Mathf.Max(1f, previewArea.width - (PreviewPadding * 2f)),
                    Mathf.Max(1f, previewArea.height - (PreviewPadding * 2f)));

                float sourceAspect = sourceWidth / sourceHeight;
                float areaAspect = padded.width / padded.height;

                if (sourceAspect > areaAspect)
                {
                    float height = padded.width / sourceAspect;
                    return new Rect(padded.x, padded.y + ((padded.height - height) * 0.5f), padded.width, height);
                }

                float width = padded.height * sourceAspect;
                return new Rect(padded.x + ((padded.width - width) * 0.5f), padded.y, width, padded.height);
            }

            private Rect GetDisplayRectFromWorkingRect(Rect previewRect)
            {
                Rect uvRect = GetWorkingUvRect();
                return new Rect(
                    previewRect.x + (uvRect.x * previewRect.width),
                    previewRect.y + ((1f - uvRect.y - uvRect.height) * previewRect.height),
                    uvRect.width * previewRect.width,
                    uvRect.height * previewRect.height);
            }

            private Rect GetDisplayRectForOverlay(Rect previewRect, OverlayData overlayData)
            {
                if (overlayData == null)
                {
                    return previewRect;
                }

                Rect uvRect = GetUvRectForOverlay(overlayData);
                return new Rect(
                    previewRect.x + (uvRect.x * previewRect.width),
                    previewRect.y + ((1f - uvRect.y - uvRect.height) * previewRect.height),
                    uvRect.width * previewRect.width,
                    uvRect.height * previewRect.height);
            }

            private Rect GetWorkingRectFromDisplayRect(Rect previewRect, Rect displayRect)
            {
                Rect uvRect = new Rect(
                    (displayRect.x - previewRect.x) / previewRect.width,
                    1f - ((displayRect.yMax - previewRect.y) / previewRect.height),
                    displayRect.width / previewRect.width,
                    displayRect.height / previewRect.height);

                uvRect = SanitizeUvRect(uvRect);
                return FromUvRect(uvRect);
            }

            private Rect GetWorkingUvRect()
            {
                if (_useUvRect)
                {
                    return SanitizeUvRect(_workingRect);
                }

                Vector2 referenceSize = GetReferenceSize();
                if (referenceSize.x <= 0f || referenceSize.y <= 0f)
                {
                    return new Rect(0f, 0f, 1f, 1f);
                }

                return SanitizeUvRect(new Rect(
                    _workingRect.x / referenceSize.x,
                    _workingRect.y / referenceSize.y,
                    _workingRect.width / referenceSize.x,
                    _workingRect.height / referenceSize.y));
            }

            private Rect FromUvRect(Rect uvRect)
            {
                if (_useUvRect)
                {
                    return uvRect;
                }

                Vector2 referenceSize = GetReferenceSize();
                return new Rect(
                    uvRect.x * referenceSize.x,
                    uvRect.y * referenceSize.y,
                    uvRect.width * referenceSize.x,
                    uvRect.height * referenceSize.y);
            }

            private Vector2 GetReferenceSize()
            {
                Texture referenceTexture = GetEffectiveBasePreviewTexture();
                if (referenceTexture == null)
                {
                    referenceTexture = GetPreviewTexture(_overlayData);
                }

                if (referenceTexture == null)
                {
                    return Vector2.one;
                }

                return new Vector2(Mathf.Max(1f, referenceTexture.width), Mathf.Max(1f, referenceTexture.height));
            }

            private Texture GetEffectiveBasePreviewTexture()
            {
                Texture explicitBaseTexture = GetPreviewTexture(SelectedBaseOverlay);
                if (explicitBaseTexture != null)
                {
                    return explicitBaseTexture;
                }

                return GetRuntimeBaseTexture();
            }

            private Texture GetRuntimeBaseTexture()
            {
                Texture selectedAvatarTexture = GetSelectedAvatarBaseTexture();
                if (selectedAvatarTexture != null)
                {
                    return selectedAvatarTexture;
                }

                LogBaseTextureLookup("Selected-avatar base texture lookup did not resolve a texture for " + GetOverlayLookupContext() + ". Falling back to the current slot overlay.");

                if (_slotData == null)
                {
                    LogBaseTextureLookup("Current slot data is null while resolving runtime base texture for " + GetOverlayLookupContext() + ".");
                    return null;
                }

                OverlayData runtimeBaseOverlay = _slotData.GetOverlay(0);
                if (runtimeBaseOverlay == null)
                {
                    LogBaseTextureLookup("Current slot '" + GetCurrentSlotNameKey() + "' has no overlay at index 0 while resolving runtime base texture.");
                    return null;
                }

                Texture runtimeBaseTexture = runtimeBaseOverlay.GetTexture(0);
                if (runtimeBaseTexture == null)
                {
                    LogBaseTextureLookup("Current slot fallback overlay '" + runtimeBaseOverlay.overlayName + "' has no texture at index 0.");
                }

                return runtimeBaseTexture;
            }

            private Texture GetSelectedAvatarBaseTexture()
            {
                DynamicCharacterAvatar selectedAvatar = GetSelectedSceneAvatar();
                Texture selectedAvatarTexture = GetAvatarBaseTexture(selectedAvatar);
                if (selectedAvatarTexture != null)
                {
                    return selectedAvatarTexture;
                }

                if (selectedAvatar != null)
                {
                    LogBaseTextureLookup("Selected avatar '" + selectedAvatar.name + "' did not provide a usable base texture for " + GetOverlayLookupContext() + ".");
                }

                DynamicCharacterAvatar[] avatars = UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (avatars == null || avatars.Length == 0)
                {
                    LogBaseTextureLookup("No scene DynamicCharacterAvatar instances were found while resolving a base texture for " + GetOverlayLookupContext() + ".");
                    return null;
                }

                for (int avatarIndex = 0; avatarIndex < avatars.Length; avatarIndex++)
                {
                    DynamicCharacterAvatar avatar = avatars[avatarIndex];
                    if (avatar == null || ReferenceEquals(avatar, selectedAvatar) || EditorUtility.IsPersistent(avatar) || avatar.gameObject == null || !avatar.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    Texture avatarTexture = GetAvatarBaseTexture(avatar);
                    if (avatarTexture != null)
                    {
                        return avatarTexture;
                    }
                }

                LogBaseTextureLookup("Scene avatar scan did not find any matching base texture for " + GetOverlayLookupContext() + ". Evaluated " + avatars.Length + " avatar(s).");

                return null;
            }

            private Texture GetAvatarBaseTexture(DynamicCharacterAvatar avatar)
            {
                if (avatar == null)
                {
                    LogBaseTextureLookup("Cannot resolve base texture because the avatar reference is null for " + GetOverlayLookupContext() + ".");
                    return null;
                }

                if (avatar.umaRecipe == null)
                {
                    LogBaseTextureLookup("Avatar '" + avatar.name + "' has a null umaRecipe while resolving a base texture for " + GetOverlayLookupContext() + ".");
                    return null;
                }

                SlotData matchedSlot = FindAvatarSlotForCurrentOverlay(avatar);
                if (matchedSlot == null)
                {
                    LogBaseTextureLookup("Avatar '" + avatar.name + "' did not contain a matching slot for " + GetOverlayLookupContext() + ".");
                    return null;
                }

                OverlayData baseOverlay = matchedSlot.GetOverlay(0);
                if (baseOverlay == null)
                {
                    LogBaseTextureLookup("Avatar '" + avatar.name + "' matched slot '" + matchedSlot.slotName + "' but that slot has no overlay at index 0.");
                    return null;
                }

                Texture baseTexture = baseOverlay.GetTexture(0);
                if (baseTexture == null)
                {
                    LogBaseTextureLookup("Avatar '" + avatar.name + "' matched slot '" + matchedSlot.slotName + "' and overlay '" + baseOverlay.overlayName + "', but texture index 0 is null.");
                }

                return baseTexture;
            }

            private SlotData FindAvatarSlotForCurrentOverlay(DynamicCharacterAvatar avatar)
            {
                if (avatar == null)
                {
                    LogBaseTextureLookup("FindAvatarSlotForCurrentOverlay was called with a null avatar for " + GetOverlayLookupContext() + ".");
                    return null;
                }

                if (avatar.umaRecipe == null)
                {
                    LogBaseTextureLookup("Avatar '" + avatar.name + "' has a null umaRecipe while attempting slot lookup for " + GetOverlayLookupContext() + ".");
                    return null;
                }

                if (_slotData == null)
                {
                    LogBaseTextureLookup("Current overlay editor slot data is null while attempting avatar slot lookup for avatar '" + avatar.name + "'.");
                    return null;
                }

                string sourceSlotKey = GetCurrentSourceSlotKey();
                string slotNameKey = GetCurrentSlotNameKey();
                if (string.IsNullOrWhiteSpace(sourceSlotKey) && string.IsNullOrWhiteSpace(slotNameKey))
                {
                    LogBaseTextureLookup("No source-slot or slot-name key could be derived from the current overlay editor slot while matching avatar '" + avatar.name + "'.");
                    return null;
                }

                SlotData[] slots = avatar.umaRecipe.GetAllSlots();
                if (slots == null || slots.Length == 0)
                {
                    LogBaseTextureLookup("Avatar '" + avatar.name + "' has no slots in umaRecipe while matching " + GetOverlayLookupContext() + ".");
                    return null;
                }

                SlotData legacyMatch = null;

                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    SlotData slot = slots[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }

                    if (SlotMatchesSourceSlot(slot, sourceSlotKey) || SlotMatchesSourceSlot(slot, slotNameKey))
                    {
                        return slot;
                    }

                    if (legacyMatch == null && (SlotMatchesSlotName(slot, sourceSlotKey) || SlotMatchesSlotName(slot, slotNameKey)))
                    {
                        legacyMatch = slot;
                    }
                }

                if (legacyMatch == null)
                {
                    LogBaseTextureLookup(
                        "Avatar '" + avatar.name + "' had no slot matching sourceSlot='" + sourceSlotKey + "' or slotName='" + slotNameKey + "'. Available slots: "
                        + DescribeAvatarSlots(slots));
                }

                return legacyMatch;
            }

            private string GetCurrentSourceSlotKey()
            {
                if (_slotData == null)
                {
                    return null;
                }

                if (_slotData.asset != null && !string.IsNullOrWhiteSpace(_slotData.asset.sourceSlot))
                {
                    return _slotData.asset.sourceSlot;
                }

                return GetCurrentSlotNameKey();
            }

            private string GetCurrentSlotNameKey()
            {
                if (_slotData == null)
                {
                    return null;
                }

                if (_slotData.isPlaceholderSlot && !string.IsNullOrWhiteSpace(_slotData.placeholderSlotName))
                {
                    return _slotData.placeholderSlotName;
                }

                return _slotData.slotName;
            }

            private static bool SlotMatchesSourceSlot(SlotData slot, string slotKey)
            {
                return slot != null
                    && slot.asset != null
                    && !string.IsNullOrWhiteSpace(slotKey)
                    && string.Equals(slot.asset.sourceSlot, slotKey, StringComparison.OrdinalIgnoreCase);
            }

            private static bool SlotMatchesSlotName(SlotData slot, string slotKey)
            {
                return slot != null
                    && !string.IsNullOrWhiteSpace(slotKey)
                    && string.Equals(slot.slotName, slotKey, StringComparison.OrdinalIgnoreCase);
            }

            private DynamicCharacterAvatar GetSelectedSceneAvatar()
            {
                GameObject selectedGameObject = Selection.activeGameObject;
                if (selectedGameObject == null)
                {
                    LogBaseTextureLookup("Selection.activeGameObject is null while resolving the selected scene avatar for " + GetOverlayLookupContext() + ". Active object is " + DescribeUnityObject(Selection.activeObject) + ".");
                    return null;
                }

                DynamicCharacterAvatar avatar = selectedGameObject.GetComponentInParent<DynamicCharacterAvatar>();
                if (avatar == null || avatar.gameObject == null)
                {
                    LogBaseTextureLookup("Selection.activeGameObject '" + selectedGameObject.name + "' does not resolve to a DynamicCharacterAvatar while looking up a base texture for " + GetOverlayLookupContext() + ".");
                    return null;
                }

                if (EditorUtility.IsPersistent(avatar) || !avatar.gameObject.scene.IsValid())
                {
                    LogBaseTextureLookup("Selected avatar '" + avatar.name + "' is persistent or not in a valid scene while resolving a base texture for " + GetOverlayLookupContext() + ".");
                    return null;
                }

                return avatar;
            }

            private void LogBaseTextureLookup(string message)
            {
                if (string.IsNullOrWhiteSpace(message) || !_baseTextureLookupLogMessages.Add(message))
                {
                    return;
                }

                Debug.Log("[Overlay Positioner] " + message);
            }

            private string GetOverlayLookupContext()
            {
                string overlayName = _overlayData != null ? _overlayData.overlayName : "<null overlay>";
                string slotName = GetCurrentSlotNameKey();
                string sourceSlot = GetCurrentSourceSlotKey();
                return "overlay='" + overlayName + "', slotName='" + (string.IsNullOrWhiteSpace(slotName) ? "<null>" : slotName) + "', sourceSlot='" + (string.IsNullOrWhiteSpace(sourceSlot) ? "<null>" : sourceSlot) + "'";
            }

            private static string DescribeUnityObject(UnityEngine.Object obj)
            {
                if (obj == null)
                {
                    return "<null>";
                }

                return "'" + obj.name + "' (" + obj.GetType().Name + ")";
            }

            private static string DescribeAvatarSlots(SlotData[] slots)
            {
                if (slots == null || slots.Length == 0)
                {
                    return "<none>";
                }

                List<string> descriptions = new List<string>();
                for (int i = 0; i < slots.Length; i++)
                {
                    SlotData slot = slots[i];
                    if (slot == null)
                    {
                        continue;
                    }

                    string slotName = string.IsNullOrWhiteSpace(slot.slotName) ? "<null>" : slot.slotName;
                    string sourceSlot = slot.asset != null && !string.IsNullOrWhiteSpace(slot.asset.sourceSlot) ? slot.asset.sourceSlot : "<null>";
                    descriptions.Add("'" + slotName + "' [sourceSlot='" + sourceSlot + "']");
                    if (descriptions.Count >= SlotDiagnosticLimit)
                    {
                        descriptions.Add("...");
                        break;
                    }
                }

                return descriptions.Count == 0 ? "<none>" : string.Join(", ", descriptions);
            }

            private static Rect GetUvRectForOverlay(OverlayData overlayData)
            {
                if (overlayData == null)
                {
                    return new Rect(0f, 0f, 1f, 1f);
                }

                if (LooksLikeUvRect(overlayData.rect))
                {
                    return SanitizeUvRect(overlayData.rect);
                }

                Texture texture = GetPreviewTexture(overlayData);
                if (texture == null || texture.width <= 0 || texture.height <= 0)
                {
                    return new Rect(0f, 0f, 1f, 1f);
                }

                Rect uvRect = new Rect(
                    overlayData.rect.x / texture.width,
                    overlayData.rect.y / texture.height,
                    overlayData.rect.width / texture.width,
                    overlayData.rect.height / texture.height);

                return SanitizeUvRect(uvRect);
            }

            private static Rect SanitizeUvRect(Rect uvRect)
            {
                float xMin = Mathf.Clamp(uvRect.xMin, 0f, 1f - MinRectSize);
                float yMin = Mathf.Clamp(uvRect.yMin, 0f, 1f - MinRectSize);
                float xMax = Mathf.Clamp(uvRect.xMax, xMin + MinRectSize, 1f);
                float yMax = Mathf.Clamp(uvRect.yMax, yMin + MinRectSize, 1f);
                return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }

            private static Rect ClampDisplayRect(Rect rect, Rect bounds)
            {
                float minSizeX = Mathf.Max(MinRectSize * bounds.width, 1f);
                float minSizeY = Mathf.Max(MinRectSize * bounds.height, 1f);

                rect.width = Mathf.Max(minSizeX, rect.width);
                rect.height = Mathf.Max(minSizeY, rect.height);

                if (rect.xMin < bounds.xMin)
                {
                    if (rect.width >= bounds.width)
                    {
                        rect.xMin = bounds.xMin;
                        rect.width = bounds.width;
                    }
                    else
                    {
                        rect.x = bounds.xMin;
                    }
                }

                if (rect.xMax > bounds.xMax)
                {
                    if (rect.width >= bounds.width)
                    {
                        rect.xMin = bounds.xMin;
                        rect.width = bounds.width;
                    }
                    else
                    {
                        rect.x = bounds.xMax - rect.width;
                    }
                }

                if (rect.yMin < bounds.yMin)
                {
                    if (rect.height >= bounds.height)
                    {
                        rect.yMin = bounds.yMin;
                        rect.height = bounds.height;
                    }
                    else
                    {
                        rect.y = bounds.yMin;
                    }
                }

                if (rect.yMax > bounds.yMax)
                {
                    if (rect.height >= bounds.height)
                    {
                        rect.yMin = bounds.yMin;
                        rect.height = bounds.height;
                    }
                    else
                    {
                        rect.y = bounds.yMax - rect.height;
                    }
                }

                return rect;
            }

            private static Rect GetHandleRect(float centerX, float centerY)
            {
                return new Rect(centerX - (HandleSize * 0.5f), centerY - (HandleSize * 0.5f), HandleSize, HandleSize);
            }

            private static Rect GetRotateHandleRect(Rect overlayRect)
            {
                return GetHandleRect(overlayRect.center.x, overlayRect.yMin - (HandleSize * 1.8f));
            }

            private static Rect GetScaleHandleRect(Rect overlayRect)
            {
                return GetHandleRect(overlayRect.xMax + (HandleSize * 1.4f), overlayRect.center.y);
            }

            private static bool LooksLikeUvRect(Rect rect)
            {
                return Mathf.Abs(rect.x) <= 1f && Mathf.Abs(rect.y) <= 1f && rect.width <= 1f && rect.height <= 1f;
            }

            private static Texture GetPreviewTexture(OverlayData overlayData)
            {
                if (overlayData == null || overlayData.textureArray == null)
                {
                    return null;
                }

                for (int i = 0; i < overlayData.textureArray.Length; i++)
                {
                    if (overlayData.textureArray[i] != null)
                    {
                        return overlayData.textureArray[i];
                    }
                }

                return null;
            }

            private static Color GetOverlayTint(OverlayData overlayData)
            {
                if (overlayData == null || overlayData.colorData == null || overlayData.colorData.channelMask == null || overlayData.colorData.channelMask.Length == 0)
                {
                    return Color.white;
                }

                return overlayData.colorData.channelMask[0];
            }

            private static void DrawTexture(Rect rect, Texture texture, Color tint)
            {
                if (texture == null)
                {
                    return;
                }

                Color previous = GUI.color;
                GUI.color = tint;
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
                GUI.color = previous;
            }

            private static void DrawCheckerboard(Rect rect)
            {
                const int squares = 8;
                float cellWidth = rect.width / squares;
                float cellHeight = rect.height / squares;
                Color dark = new Color(0.22f, 0.22f, 0.22f, 1f);
                Color light = new Color(0.32f, 0.32f, 0.32f, 1f);

                for (int y = 0; y < squares; y++)
                {
                    for (int x = 0; x < squares; x++)
                    {
                        Rect cell = new Rect(rect.x + (x * cellWidth), rect.y + (y * cellHeight), cellWidth, cellHeight);
                        EditorGUI.DrawRect(cell, ((x + y) % 2 == 0) ? dark : light);
                    }
                }
            }

            private static void DrawOverlayOutline(Rect overlayRect)
            {
                Handles.BeginGUI();
                Handles.DrawSolidRectangleWithOutline(overlayRect, new Color(1f, 1f, 1f, 0.08f), new Color(0.15f, 0.9f, 1f, 1f));
                DrawHandle(overlayRect.xMin, overlayRect.yMin);
                DrawHandle(overlayRect.xMax, overlayRect.yMin);
                DrawHandle(overlayRect.xMin, overlayRect.yMax);
                DrawHandle(overlayRect.xMax, overlayRect.yMax);
                Handles.EndGUI();
            }

            private static void DrawHandle(float centerX, float centerY)
            {
                Rect rect = GetHandleRect(centerX, centerY);
                EditorGUI.DrawRect(rect, new Color(0.15f, 0.9f, 1f, 1f));
                EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), Color.white);
            }
        }
        
        private bool OnTagsGUI()
        {
            bool changed = false;
            if (_overlayData.tags == null)
            {
                _overlayData.tags = new string[0];
            }

            if (_overlayData.tags.Length == 0)
            {
                EditorGUILayout.HelpBox("No tags defined for this overlay", MessageType.Info);
            }

            string newTag = CharacterBaseEditor.DoTagSelector(_overlayData.tags);
            if (!string.IsNullOrWhiteSpace(newTag))
            {
                changed = true;
                System.Array.Resize(ref _overlayData.tags, _overlayData.tags.Length + 1);
                _overlayData.tags[_overlayData.tags.Length - 1] = newTag;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Tags");
            if (GUILayout.Button("Add Empty"))
            {
                System.Array.Resize(ref _overlayData.tags, _overlayData.tags.Length + 1);
                _overlayData.tags[_overlayData.tags.Length - 1] = string.Empty;
                changed = true;
            }
            GUILayout.EndHorizontal();

            int deleted = -1;
            for (int i = 0; i < _overlayData.tags.Length; i++)
            {
                GUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                string updatedTag = EditorGUILayout.TextField(_overlayData.tags[i]);
                if (EditorGUI.EndChangeCheck())
                {
                    _overlayData.tags[i] = updatedTag;
                    changed = true;
                }
                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    deleted = i;
                }
                GUILayout.EndHorizontal();
            }
            if (deleted != -1)
            {
                changed = true;
                List<string> tags = new List<string>(_overlayData.tags);
                tags.RemoveAt(deleted);
                _overlayData.tags = tags.ToArray();
            }
            return changed;
        }

        public bool OnColorGUI()
        {
            bool changed = false;
            int currentsharedcol = 0;
            List<string> propertyNames = new List<string>();
            Dictionary<int, int> PropertyPosition = new Dictionary<int, int>();
            string[] sharednames = new string[_recipe.sharedColors.Length];


            if (_overlayData.isEmpty)
            {
                int foundProperty = -1;

                for (int i = 0; i < _recipe.sharedColors.Length; i++)
                {
                    if (_recipe.sharedColors[i].channelCount == 0)
                    {
                        int currentPropertyIndex = propertyNames.Count;

                        if (foundProperty == -1)
                        {
                            foundProperty = currentPropertyIndex;
                        }

                        propertyNames.Add(_recipe.sharedColors[i].name);
                        PropertyPosition.Add(currentPropertyIndex, i);
                        if (_overlayData.colorData.GetHashCode() == _recipe.sharedColors[i].GetHashCode())
                        {
                            foundProperty = currentPropertyIndex;
                        }
                    }
                }


                if (propertyNames.Count > 0)
                {
                    if (foundProperty == -1)
                    {
                        foundProperty = 0;
                        changed = true;
                    }
                    GUIHelper.BeginVerticalPadded(2f, new Color(0.75f, 0.875f, 1f));
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Select property name");
                    int newprop = EditorGUILayout.Popup(foundProperty, propertyNames.ToArray());

                    GUILayout.EndHorizontal();
                    GUIHelper.EndVerticalPadded(2f);
                    GUILayout.Space(2f);
                    if (newprop != foundProperty || changed == true)
                    {
                        changed = true;
                        int proppos = PropertyPosition[newprop];
                        _overlayData.colorData = _recipe.sharedColors[proppos];
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Add a property to the shared color above to be able to associate a name with this overlay and assign properties at runtime", MessageType.Info);
                }
                return changed;
            }

            if (_overlayData.colorData.IsASharedColor && _recipe.HasSharedColor(_overlayData.colorData))
            {

                bool found = false;
                GUIHelper.BeginVerticalPadded(2f, new Color(0.75f, 0.875f, 1f));
                GUILayout.BeginHorizontal();

                if (GUILayout.Toggle(true, "Use Shared Color") == false)
                {
                    _overlayData.colorData = _overlayData.colorData.Clone();
                    _overlayData.colorData.name = OverlayColorData.UNSHARED;
                    changed = true;
                }
                else
                {
                    for (int i = 0; i < _recipe.sharedColors.Length; i++)
                    {
                        sharednames[i] = i + ": " + _recipe.sharedColors[i].name;
                        if (_overlayData.colorData.GetHashCode() == _recipe.sharedColors[i].GetHashCode())
                        {
                            currentsharedcol = i;
                            found = true;
                        }
                    }

                    int newcol = EditorGUILayout.Popup(currentsharedcol, sharednames);
                    if (newcol != currentsharedcol || !found)
                    {
                        changed = true;
                        _overlayData.colorData = _recipe.sharedColors[newcol];
                    }
                }
                GUILayout.EndHorizontal();
                GUIHelper.EndVerticalPadded(2f);
                GUILayout.Space(2f);
                return changed;

            }
            else
            {
                GUIHelper.BeginVerticalPadded(2f, new Color(0.75f, 0.875f, 1f));
                GUILayout.BeginHorizontal();

                if (_recipe.sharedColors.Length > 0)
                {
                    if (GUILayout.Toggle(false, "Use Shared Color"))
                    {
                        _overlayData.colorData = _recipe.sharedColors[0];
                        changed = true;
                    }
                }

                GUILayout.EndHorizontal();

                bool showExtendedRanges = showExtendedRangeForOverlay == _overlayData;
                var newShowExtendedRanges = EditorGUILayout.Toggle("Show Extended Ranges", showExtendedRanges);

                if (showExtendedRanges != newShowExtendedRanges)
                {
                    if (newShowExtendedRanges)
                    {
                        showExtendedRangeForOverlay = _overlayData;
                    }
                    else
                    {
                        showExtendedRangeForOverlay = null;
                    }
                }

                for (int k = 0; k < _colors.Length; k++)
                {
                    Color color;
                    if (newShowExtendedRanges && k % 2 == 0)
                    {
                        Vector4 colorVector = new Vector4(_colors[k].color.r, _colors[k].color.g, _colors[k].color.b, _colors[k].color.a);
                        colorVector = EditorGUILayout.Vector4Field(_colors[k].description, colorVector);
                        color = new Color(colorVector.x, colorVector.y, colorVector.z, colorVector.w);
                    }
                    else
                    {
                        color = EditorGUILayout.ColorField(_colors[k].description, _colors[k].color);
                    }

                    if (color.r != _colors[k].color.r ||
                     color.g != _colors[k].color.g ||
                     color.b != _colors[k].color.b ||
                     color.a != _colors[k].color.a)
                    {
                        if (k % 2 == 0)
                        {
                            _overlayData.colorData.channelMask[k / 2] = color;
                        }
                        else
                        {
                            _overlayData.colorData.channelAdditiveMask[k / 2] = color;
                        }
                        changed = true;
                    }
                }

                GUIHelper.EndVerticalPadded(2f);
                GUILayout.Space(2f);
                return changed;
            }
        }
    }

    public class ColorEditor
    {
        public Color color;
        public string description;

        public ColorEditor(Color color, string description)
        {
            this.color = color;
            this.description = description;
        }
    }
}
#endif
