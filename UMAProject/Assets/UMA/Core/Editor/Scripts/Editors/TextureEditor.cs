#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public class TextureEditor
    {
        private Texture _texture;
        private readonly int _channel;
        private readonly OverlayData _overlay;
        private float origLabelWidth;
        private int origIndentLevel;
        private int _pickerControlId = -1;
        private static TextureEditor _activePickerOwner = null; // ensures only one editor handles the picker

        public TextureEditor(Texture texture, int channel, OverlayData overlay)
        {
            _texture = texture;
            _channel = channel;
            _overlay = overlay;
        }

        public bool OnGUI(bool allowEdits = true)
        {
            bool changed = false;

            InitEditor();

            GUILayout.BeginVertical(GUILayout.Width(102f));

            // Get material property name for this channel (if available)
            string propName = GetMaterialPropertyName();
            string displayName = string.IsNullOrEmpty(propName) ? $"Channel {_channel + 1}" : propName.Replace("_", "");
            Rect labelRect = GUILayoutUtility.GetRect(102f, EditorGUIUtility.singleLineHeight, GUILayout.Width(100f));
            EditorGUI.LabelField(labelRect, displayName, EditorStyles.miniLabel);

            // Reserve area for the texture preview (100x100)
            Rect previewRect = GUILayoutUtility.GetRect(102f, 100f, GUILayout.Width(100f), GUILayout.Height(100f));

            previewRect = new Rect(previewRect.x + 2, previewRect.y + 2, previewRect.width - 6, previewRect.height - 4);

            // Draw background
            EditorGUI.DrawRect(previewRect, new Color(0.2f, 0.2f, 0.2f, 1f));

            previewRect = new Rect(previewRect.x + 2, previewRect.y + 2, previewRect.width - 4, previewRect.height - 4);

            // Draw texture preview if available
            if (_texture != null)
            {
                GUI.DrawTexture(previewRect, _texture, ScaleMode.ScaleToFit, true);
            }
            else
            {
                // Draw placeholder text
                var centered = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(previewRect, "None", centered);
            }

            // Handle drag and drop, select button, and object picker
            Event evt = Event.current;
            Rect selectBtnRect = Rect.zero;
            if (allowEdits)
            {
                if (previewRect.Contains(evt.mousePosition))
                {
                    if (evt.type == EventType.DragUpdated)
                    {
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is Texture)
                            {
                                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                                evt.Use();
                                break;
                            }
                        }
                    }
                    else if (evt.type == EventType.DragPerform)
                    {
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is Texture tex)
                            {
                                DragAndDrop.AcceptDrag();
                                SetTexture(tex, ref changed);
                                evt.Use();
                                break;
                            }
                        }
                    }
                }

                // Draw a floating Select mini button at the bottom-left of the preview
                float btnW = 50f;
                float btnH = EditorGUIUtility.singleLineHeight;
                selectBtnRect = new Rect(previewRect.x + 2, previewRect.yMax - (btnH + 2), btnW, btnH);
                if (GUI.Button(selectBtnRect, "Select", EditorStyles.miniButton))
                {
                    // Mark this editor as the active picker owner and open the picker with a unique control id
                    _activePickerOwner = this;
                    _pickerControlId = GUIUtility.GetControlID(FocusType.Passive);
                    EditorGUIUtility.ShowObjectPicker<Texture>(_texture, false, string.Empty, _pickerControlId);
                }

                // Handle the object picker selection; only the active owner processes the event
                if ((_activePickerOwner == this) &&
                    (evt.commandName == "ObjectSelectorUpdated" || evt.commandName == "ObjectSelectorClosed") &&
                    EditorGUIUtility.GetObjectPickerControlID() == _pickerControlId)
                {
                    Texture picked = EditorGUIUtility.GetObjectPickerObject() as Texture;
                    if (picked != null && picked != _texture)
                    {
                        SetTexture(picked, ref changed);
                    }
                    // Release ownership on close, and consume non-layout events to prevent layout mismatches
                    if (evt.commandName == "ObjectSelectorClosed")
                    {
                        _activePickerOwner = null;
                        _pickerControlId = -1;
                    }
                    if (Event.current.type != EventType.Layout)
                    {
                        Event.current.Use();
                    }
                }
            }

            // Click-to-ping the texture in the Project window (does not require edits enabled)
            if (_texture != null && previewRect.Contains(evt.mousePosition) && evt.type == EventType.MouseUp && evt.button == 0)
            {
                // Avoid ping if the click was on the Select button
                if (selectBtnRect == Rect.zero || !selectBtnRect.Contains(evt.mousePosition))
                {
                    EditorGUIUtility.PingObject(_texture);
                    evt.Use();
                }
            }

            GUILayout.EndVertical();

            RestoreEditor();

            return changed;
        }

        private string GetMaterialPropertyName()
        {
            if (_overlay == null || _overlay.asset == null || _overlay.asset.material == null)
            {
                return string.Empty;
            }
            var mat = _overlay.asset.material;
            if (mat.channels == null || _channel < 0 || _channel >= mat.channels.Length)
            {
                return string.Empty;
            }
            return mat.channels[_channel].materialPropertyName;
        }

        private void SetTexture(Texture newTexture, ref bool changed)
        {
            _texture = newTexture;

            // Update only the runtime overlay texture array for the current overlay instance
            if (_overlay != null && _channel >= 0)
            {
                var runtimeTextures = _overlay.textureArray;
                if (runtimeTextures != null && _channel < runtimeTextures.Length)
                {
                    runtimeTextures[_channel] = newTexture;
                }

                // Also persist the change to the underlying OverlayDataAsset and save it
                var asset = _overlay.asset;
                if (asset != null)
                {
                    try
                    {
                        var so = new SerializedObject(asset);
                        var texList = so.FindProperty("_textureList");
                        if (texList != null && _channel >= 0 && _channel < texList.arraySize)
                        {
                            // Record for undo and assign new texture to the asset at the corresponding channel
                            Undo.RecordObject(asset, "Set Overlay Texture");
                            texList.GetArrayElementAtIndex(_channel).objectReferenceValue = newTexture;
                            so.ApplyModifiedPropertiesWithoutUndo();

                            EditorUtility.SetDirty(asset);
                            AssetDatabase.SaveAssetIfDirty(asset);
                        }
                    }
                    catch { /* ignore editor-time exceptions during reload */ }
                }
            }

            changed = true;
        }

        private void RestoreEditor()
        {
            EditorGUI.indentLevel = origIndentLevel;
            EditorGUIUtility.labelWidth = origLabelWidth;
        }

        private void InitEditor()
        {
            origLabelWidth = EditorGUIUtility.labelWidth;
            origIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = 0;
        }

        public bool OnBlendGUI()
        {
            // Deprecated: blend now handled in OnGUI for alignment
            return false;
        }

        public bool OnTileGUI()
        {
            // Deprecated: tiling now handled in OnGUI for alignment
            return false;
        }
    }
}
#endif
