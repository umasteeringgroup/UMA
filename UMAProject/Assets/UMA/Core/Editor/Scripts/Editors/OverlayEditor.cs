#if UNITY_EDITOR
using System.Collections.Generic;
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
        private OverlayDataAsset _baseOverlayData;
        private readonly TextureEditor[] _textures;
        private ColorEditor[] _colors;
        private bool isUV = false;


        public OverlayData Overlay
        {
            get { return _overlayData; }
        }

        private bool _foldout = true;

        public bool Delete { get; private set; }

        public int move;
        private static OverlayData showExtendedRangeForOverlay;

        public void EnsureEntry(string overlayName)
        {
            if (OverlayExpanded.ContainsKey(overlayName))
            {
                return;
            }

            OverlayExpanded.Add(overlayName, true);
        }

        public OverlayEditor(UMAData.UMARecipe recipe, SlotData slotData, OverlayData overlayData, OverlayDataAsset baseOverlayDataAsset = null)
        {
            _recipe = recipe;
            _overlayData = overlayData;
            _slotData = slotData;
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
                queue = _overlayData.asset.material.material.renderQueue;
            }


            GUIHelper.FoldoutBarButton(ref _foldout, $"{_overlayData.asset.overlayName} ( {matName} Q:{queue})", buttons,out pressed, out move, out delete);

            if (pressed[0])
            {
                EditorGUIUtility.PingObject(_overlayData.asset.GetInstanceID());
                InspectorUtlity.InspectTarget(_overlayData.asset);
            }

            if (pressed[1])
            {
                EditorGUIUtility.PingObject(_overlayData.asset.material.material.GetInstanceID());
                InspectorUtlity.InspectTarget(_overlayData.asset.material.material);
            }

            if (pressed[2])
            {
                EditorGUIUtility.PingObject(_overlayData.asset.material.GetInstanceID());
                InspectorUtlity.InspectTarget(_overlayData.asset.material);
            }


            OverlayExpanded[_overlayData.overlayName] = _foldout;
            Delete = delete;

            if (!_foldout)
            {
                return false;
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
            if (_slotData.asset.material != null && _overlayData.asset.material != null)
            {
                if (_overlayData.asset.material.name != _slotData.material.name)
                {
                    if (_overlayData.asset.material.channels.Length == _slotData.material.channels.Length)
                    {
                        EditorGUILayout.HelpBox("Material " + _overlayData.asset.material.name + " does not match slot material: " + _slotData.material.name, MessageType.Error);
                        if (GUILayout.Button("Copy Slot Material to Overlay"))
                        {
                            _overlayData.asset.material = _slotData.asset.material;
                            EditorUtility.SetDirty(_overlayData.asset);
                            string path = AssetDatabase.GetAssetPath(_overlayData.asset.GetInstanceID());
                            AssetDatabase.ImportAsset(path);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Material " + _overlayData.asset.material.name + " does not match slot material: " + _slotData.asset.material.name + " and Channel count is not the same. Overlay must be removed or fixed manually", MessageType.Error);
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

            bool originalInstanceTransformed = _overlayData.instanceTransformed;
            float originalRotation = _overlayData.Rotation;
            Vector2 originalScale = _overlayData.Scale;
            Vector2 originalTranslate = _overlayData.Translate;

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
            _overlayData.instanceTransformed = GUILayout.Toggle(_overlayData.instanceTransformed, "Transform");
            if (_overlayData.instanceTransformed)
            {
                GUIHelper.BeginVerticalPadded(5, new Color(1, 1, 1, 1));
                EditorGUILayout.HelpBox("Warning: translating, scaling or rotation could result in writing outside the bounds of the texture on the atlas. Be sure to use only in safe areas.", MessageType.Info);
                _overlayData.Rotation = EditorGUILayout.FloatField("Rotation", _overlayData.Rotation);
                _overlayData.Scale = EditorGUILayout.Vector2Field("Scale", _overlayData.Scale);
                EditorGUILayout.LabelField("Translation: ");
                _overlayData.Translate.x = EditorGUILayout.Slider("X:",_overlayData.Translate.x * 100.0f, -100.0f, 100.0f) / 100.0f;
                _overlayData.Translate.y = EditorGUILayout.Slider("Y:", _overlayData.Translate.y * 100.0f, -100.0f, 100.0f) / 100.0f;
                GUIHelper.EndVerticalPadded(5);
            }

            if (_overlayData.instanceTransformed != originalInstanceTransformed)
            {
                changed = true;
            }

            if (_overlayData.Rotation != originalRotation)
            {
                changed = true;
            }

            if (_overlayData.Scale != originalScale)
            {
                changed = true;
            }
            if (_overlayData.Translate != originalTranslate)
            {
                changed = true;
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

            return changed;
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
                _overlayData.tags[i] = EditorGUILayout.TextField(_overlayData.tags[i]);
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
                    _overlayData.colorData = _overlayData.colorData.Duplicate();
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
