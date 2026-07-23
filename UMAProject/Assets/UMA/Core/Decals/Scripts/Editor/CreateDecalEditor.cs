#if UNITY_EDITOR
using UMA;
using UnityEditor;
using UnityEngine;

namespace UMA.Decals
{
    [CustomEditor(typeof(CreateDecal), true)]
    public class CreateDecalEditor : Editor
    {
        private SerializedProperty _decalMethodProp;
        private SerializedProperty _orbitCameraProp;
        private SerializedProperty _avatarProp;
        private SerializedProperty _meshDecalOverlayProp;
        private SerializedProperty _textureDecalOverlayProp;
        private SerializedProperty _targetOverlayGroupProp;
        private SerializedProperty _stampFieldProp;
        private SerializedProperty _drawRenderTexturesImmediatelyProp;

        private Editor _stampFieldEditor;
        private Object _lastStampObj;
        private static bool _showStampSlot;
        private static bool _showColors;
        private static bool _showOrbit;
        private static bool _showDebug;

        private void OnEnable()
        {
            _decalMethodProp = serializedObject.FindProperty("decalMethod");
            _orbitCameraProp = serializedObject.FindProperty("OrbitCamera");
            _avatarProp = serializedObject.FindProperty("Avatar");
            _meshDecalOverlayProp = serializedObject.FindProperty("MeshDecalOverlay");
            _textureDecalOverlayProp = serializedObject.FindProperty("TextureDecalOverlay");
            _targetOverlayGroupProp = serializedObject.FindProperty("TargetOverlayGroup");
            _stampFieldProp = serializedObject.FindProperty("StampField");
            _drawRenderTexturesImmediatelyProp = serializedObject.FindProperty("DrawRenderTexturesImmediately");
            CreateOrUpdateInnerEditor();
        }

        private void OnDisable()
        {
            DestroyInnerEditor();
        }

        public override void OnInspectorGUI()
        {
            if (target == null)
            {
                return;
            }

            serializedObject.Update();

            DrawGroup("Create Decal", () =>
            {
                EditorGUILayout.PropertyField(_decalMethodProp);
                EditorGUILayout.PropertyField(_avatarProp);
                EditorGUILayout.PropertyField(_orbitCameraProp);
            });

            DrawReferenceWarnings();

            var method = (CreateDecal.DecalMethod)_decalMethodProp.enumValueIndex;
            if (method == CreateDecal.DecalMethod.RenderTexture)
            {
                DrawRenderTextureSetup();
            }
            else
            {
                DrawSlotDecalSetup();
            }

            DrawPlacementSettings();
            DrawDebugFoldout();
            DrawColorFoldout();
            DrawOrbitFoldout();

            if (serializedObject.ApplyModifiedProperties())
            {
                CreateOrUpdateInnerEditor();
            }
        }

        private void DrawReferenceWarnings()
        {
            if (_avatarProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign the DynamicCharacterAvatar that will receive decals.", MessageType.Warning);
            }
            if (_orbitCameraProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign the camera used to orbit the avatar and raycast decal placement.", MessageType.Warning);
            }
        }

        private void DrawSlotDecalSetup()
        {
            DrawGroup("Slot Decal Setup", () =>
            {
                EditorGUILayout.HelpBox(
                    "Slot Decal mode creates a small mesh slot at the clicked surface. Assign an overlay whose UMAMaterial is compatible with the avatar surface, then tune the slot offset to avoid z-fighting.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(_meshDecalOverlayProp, new GUIContent("Decal Overlay"));
                EditorGUILayout.PropertyField(Property("slotOffset"));

                var overlay = _meshDecalOverlayProp.objectReferenceValue as OverlayDataAsset;
                if (overlay == null)
                {
                    EditorGUILayout.HelpBox("A Decal Overlay is required before a Slot Decal can be placed.", MessageType.Error);
                }
                else if (overlay.material == null)
                {
                    EditorGUILayout.HelpBox("The selected Decal Overlay has no UMAMaterial.", MessageType.Error);
                }
            });
        }

        private void DrawRenderTextureSetup()
        {
            DrawGroup("RenderTexture Decal Setup", () =>
            {
                EditorGUILayout.HelpBox(
                    "Setup checklist:\n" +
                    "1. Source Decal Overlay supplies the decal textures and channel layout.\n" +
                    "2. Target Overlay Group must match overlayGroup on the avatar overlays that should receive the decal.\n" +
                    "3. Stamp Slot stores generated stamps and must be installed through a utility slot whose Character Begun event calls DecalRTStampSlot.OnCharacterBegun.\n" +
                    "4. The target material must use composited texture channels so UMA creates atlas RenderTextures.",
                    MessageType.Info);

                EditorGUILayout.PropertyField(_textureDecalOverlayProp, new GUIContent("Source Decal Overlay"));
                EditorGUILayout.PropertyField(_targetOverlayGroupProp, new GUIContent("Target Overlay Group"));

                var source = _textureDecalOverlayProp.objectReferenceValue as OverlayDataAsset;
                string sourceGroup = source != null ? source.overlayGroup : string.Empty;
                using (new EditorGUI.DisabledScope(source == null || string.IsNullOrEmpty(sourceGroup)))
                {
                    if (GUILayout.Button("Use Source Overlay Group"))
                    {
                        _targetOverlayGroupProp.stringValue = sourceGroup;
                    }
                }

                if (source == null)
                {
                    EditorGUILayout.HelpBox("Assign the overlay whose textures will be stamped.", MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField("Source Overlay Group", string.IsNullOrEmpty(sourceGroup) ? "(not set)" : sourceGroup);
                    if (source.material == null)
                    {
                        EditorGUILayout.HelpBox("The Source Decal Overlay has no UMAMaterial.", MessageType.Error);
                    }
                    if (source.textureList == null || source.textureList.Length == 0)
                    {
                        EditorGUILayout.HelpBox("The Source Decal Overlay has no textures to stamp.", MessageType.Error);
                    }
                }

                string effectiveGroup = string.IsNullOrWhiteSpace(_targetOverlayGroupProp.stringValue)
                    ? sourceGroup
                    : _targetOverlayGroupProp.stringValue.Trim();
                if (string.IsNullOrEmpty(effectiveGroup))
                {
                    EditorGUILayout.HelpBox("Set Target Overlay Group. Without it, saved stamps cannot be matched during an atlas rebuild.", MessageType.Error);
                }
                else if (string.IsNullOrWhiteSpace(_targetOverlayGroupProp.stringValue))
                {
                    EditorGUILayout.HelpBox($"Target Overlay Group is using the source overlay fallback: '{effectiveGroup}'. Set it explicitly when the source decal and destination overlays use different groups.", MessageType.Info);
                }

                EditorGUILayout.Space(3f);
                EditorGUILayout.PropertyField(_stampFieldProp, new GUIContent("Stamp Slot"));
                if (_stampFieldProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("Assign a DecalRTStampSlot so created decals can be saved and replayed.", MessageType.Error);
                }
                DrawStampSlotInspector();
                DrawClearStampAssetsButton();
            });

            DrawGroup("RenderTexture Tuning", () =>
            {
                EditorGUILayout.PropertyField(Property("decalRTDilation"));
                EditorGUILayout.PropertyField(Property("DecalRTUVExpandPixels"));
                EditorGUILayout.PropertyField(Property("RebuildMethod"));
                if (_drawRenderTexturesImmediatelyProp.boolValue)
                {
                    EditorGUILayout.HelpBox("This component has the legacy Draw RenderTextures Immediately option enabled. Disable it to ensure the normal UMA texture rebuild and stamp replay path is used.", MessageType.Warning);
                    if (GUILayout.Button("Disable Legacy Immediate Draw"))
                    {
                        _drawRenderTexturesImmediatelyProp.boolValue = false;
                    }
                }
            });

            DrawGroup("RenderTexture Utility Slot", () =>
            {
                EditorGUILayout.HelpBox(
                    "The utility slot keeps DecalRTStampSlot alive with the recipe and subscribes it to UMA's texture events. In Play Mode, enter a unique slot name and use Generate and Save a Slot in the CreateDecal runtime panel, then add that slot to the avatar recipe or wardrobe.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(Property("GeneratedSlotName"));
                EditorGUILayout.PropertyField(Property("CurrentStamp"));
            });
        }

        private void DrawPlacementSettings()
        {
            DrawGroup("Placement", () =>
            {
                EditorGUILayout.PropertyField(Property("DecalRadius"));
                EditorGUILayout.PropertyField(Property("fudgeRadius"));
                EditorGUILayout.PropertyField(Property("DecalRotationDegrees"));
                EditorGUILayout.PropertyField(Property("randomizeRotation"));
                EditorGUILayout.PropertyField(Property("useHitNormalForProjection"));
            });
        }

        private void DrawDebugFoldout()
        {
            _showDebug = EditorGUILayout.Foldout(_showDebug, "Editing and Debug", true);
            if (!_showDebug) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(Property("PauseAvatarAnimation"));
            EditorGUILayout.PropertyField(Property("EnableTriangleDebug"));
            EditorGUILayout.PropertyField(Property("debugSpherePrefab"));
            EditorGUILayout.PropertyField(Property("debugShowSpheres"));
            EditorGUILayout.PropertyField(Property("DecalScale"));
            EditorGUI.indentLevel--;
        }

        private void DrawColorFoldout()
        {
            _showColors = EditorGUILayout.Foldout(_showColors, "Colors", true);
            if (!_showColors) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(Property("TattooColor"));
            EditorGUILayout.PropertyField(Property("EditFillKeepColor"));
            EditorGUILayout.PropertyField(Property("EditFillRemoveColor"));
            EditorGUILayout.PropertyField(Property("EditFillAddColor"));
            EditorGUILayout.PropertyField(Property("EditOutlineKeepColor"));
            EditorGUILayout.PropertyField(Property("EditOutlineRemoveColor"));
            EditorGUILayout.PropertyField(Property("EditOutlineUnusedColor"));
            EditorGUILayout.PropertyField(Property("EditOutlineAddColor"));
            EditorGUI.indentLevel--;
        }

        private void DrawOrbitFoldout()
        {
            _showOrbit = EditorGUILayout.Foldout(_showOrbit, "Mouse Orbit and Input", true);
            if (!_showOrbit) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Input is read from UMAPlayerActions. Mouse button numbers remain configurable here for placement and orbit gestures.", MessageType.Info);
            EditorGUILayout.PropertyField(Property("OrbitOffset"));
            EditorGUILayout.PropertyField(Property("OrbitSensitivityX"));
            EditorGUILayout.PropertyField(Property("OrbitSensitivityY"));
            EditorGUILayout.PropertyField(Property("MinPitch"));
            EditorGUILayout.PropertyField(Property("MaxPitch"));
            EditorGUILayout.PropertyField(Property("ZoomSensitivity"));
            EditorGUILayout.PropertyField(Property("MinDistance"));
            EditorGUILayout.PropertyField(Property("MaxDistance"));
            EditorGUILayout.PropertyField(Property("PanSensitivityY"));
            EditorGUILayout.PropertyField(Property("OrbitMouseButton"));
            EditorGUILayout.PropertyField(Property("PlaceMouseButton"));
            EditorGUI.indentLevel--;
        }

        private void DrawStampSlotInspector()
        {
            if (_stampFieldProp.objectReferenceValue == null) return;

            _showStampSlot = EditorGUILayout.Foldout(_showStampSlot, "Stamp Slot Contents", true);
            if (!_showStampSlot) return;

            if (_stampFieldEditor == null) CreateOrUpdateInnerEditor();
            if (_stampFieldEditor == null) return;

            EditorGUI.indentLevel++;
            _stampFieldEditor.OnInspectorGUI();
            EditorGUI.indentLevel--;
        }

        private void DrawClearStampAssetsButton()
        {
            var stampSlot = _stampFieldProp.objectReferenceValue as DecalRTStampSlot;
            int stampCount = CountStampAssets(stampSlot);

            using (new EditorGUI.DisabledScope(stampCount == 0))
            {
                string label = stampCount == 1
                    ? "Clear 1 Stamp Asset"
                    : $"Clear {stampCount} Stamp Assets";
                if (!GUILayout.Button(label))
                {
                    return;
                }
            }

            if (!EditorUtility.DisplayDialog(
                    "Clear RenderTexture Stamp Assets",
                    $"Remove all {stampCount} stamp asset reference(s) from '{stampSlot.name}'?\n\nThe DecalRTStampAsset files will not be deleted from the project.",
                    "Clear",
                    "Cancel"))
            {
                return;
            }

            serializedObject.ApplyModifiedProperties();
            Undo.RecordObjects(new Object[] { stampSlot, target }, "Clear RenderTexture Stamp Assets");
            stampSlot.ClearAllStamps();
            ((CreateDecal)target).CurrentStamp = null;
            stampSlot.NotifyStampsChanged();
            EditorUtility.SetDirty(stampSlot);
            EditorUtility.SetDirty(target);
            serializedObject.Update();
            Repaint();
            GUIUtility.ExitGUI();
        }

        private static int CountStampAssets(DecalRTStampSlot stampSlot)
        {
            if (stampSlot == null || stampSlot.overlayStamps == null)
            {
                return 0;
            }

            int count = 0;
            for (int setIndex = 0; setIndex < stampSlot.overlayStamps.Count; setIndex++)
            {
                var set = stampSlot.overlayStamps[setIndex];
                if (set != null && set.stamps != null)
                {
                    count += set.stamps.Length;
                }
            }
            return count;
        }

        private SerializedProperty Property(string name)
        {
            return serializedObject.FindProperty(name);
        }

        private static void DrawGroup(string title, System.Action drawContents)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            drawContents();
            EditorGUILayout.EndVertical();
        }

        private void CreateOrUpdateInnerEditor()
        {
            Object current = _stampFieldProp != null ? _stampFieldProp.objectReferenceValue : null;
            if (current == _lastStampObj) return;

            DestroyInnerEditor();
            _lastStampObj = current;
            if (current != null) _stampFieldEditor = CreateEditor(current);
        }

        private void DestroyInnerEditor()
        {
            if (_stampFieldEditor != null)
            {
                DestroyImmediate(_stampFieldEditor);
                _stampFieldEditor = null;
            }
            _lastStampObj = null;
        }
    }
}
#endif
