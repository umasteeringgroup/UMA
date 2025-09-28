#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UMA.Decals
{
    [CustomEditor(typeof(CreateDecal), true)]
    public class CreateDecalEditor : Editor
    {
        private SerializedProperty _orbitCameraProp;
        private SerializedProperty _avatarProp;
        private SerializedProperty _meshDecalOverlayProp;
        private SerializedProperty _textureDecalOverlayProp;
        private SerializedProperty _stampFieldProp;
        private SerializedProperty _drawRenderTexturesImmediatelyProp;
        private SerializedProperty _autoAddOverlaysProp;

        private Editor _stampFieldEditor;
        private Object _lastStampObj;
        private static bool _showStampSlot;

        private void OnEnable()
        {
            _orbitCameraProp         = serializedObject.FindProperty("OrbitCamera");
            _avatarProp              = serializedObject.FindProperty("Avatar");
            _meshDecalOverlayProp    = serializedObject.FindProperty("MeshDecalOverlay");
            _textureDecalOverlayProp = serializedObject.FindProperty("TextureDecalOverlay");
            _stampFieldProp          = serializedObject.FindProperty("StampField");
            _drawRenderTexturesImmediatelyProp = serializedObject.FindProperty("DrawRenderTexturesImmediately");
            _autoAddOverlaysProp     = serializedObject.FindProperty("AutoAddOverlays");
            CreateOrUpdateInnerEditor();
        }

        private void OnDisable()
        {
            DestroyInnerEditor();
        }

        public override void OnInspectorGUI()
        {
            if (target == null) return;

            serializedObject.Update();

            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_orbitCameraProp);
            EditorGUILayout.PropertyField(_avatarProp);
            EditorGUILayout.PropertyField(_meshDecalOverlayProp);
            EditorGUILayout.HelpBox("RenderTexture decals",MessageType.None);
            EditorGUILayout.PropertyField(_textureDecalOverlayProp);
            EditorGUILayout.PropertyField(_autoAddOverlaysProp);
            EditorGUILayout.HelpBox("When Auto Add Overlays is true, affected overlays are automatically added to the generated overlay set on the Stamp Slot. Otherwise, they are only added the first time and should be edited.", MessageType.None);
            EditorGUILayout.PropertyField(_drawRenderTexturesImmediatelyProp);
            EditorGUILayout.HelpBox("When Draw RenderTextures Immediately is true, the decal is stamped on the render texture as-is. This is faster, but may not represent the end result. Turn this off to trigger a build that will show the result obeying the layers.", MessageType.None);
            EditorGUILayout.PropertyField(_stampFieldProp);



            if (_stampFieldProp != null && _stampFieldProp.objectReferenceValue != null)
            {
                EditorGUILayout.Space();
                bool newShow = EditorGUILayout.Foldout(_showStampSlot, "Decal RT Stamp Slot", true);
                if (newShow != _showStampSlot) _showStampSlot = newShow;

                if (_showStampSlot)
                {
                    if (_stampFieldEditor == null)
                        CreateOrUpdateInnerEditor();

                    if (_stampFieldEditor != null)
                    {
                        EditorGUI.indentLevel++;
                        _stampFieldEditor.OnInspectorGUI();
                        EditorGUI.indentLevel--;
                    }
                }
            }

            EditorGUILayout.Space();

            DrawPropertiesExcluding(serializedObject,
                "OrbitCamera",
                "Avatar",
                "MeshDecalOverlay",
                "TextureDecalOverlay",
                "StampField",
                "AutoAddOverlays",
                "DrawRenderTexturesImmediately"
            );

            if (serializedObject.ApplyModifiedProperties())
                CreateOrUpdateInnerEditor();
        }

        private void CreateOrUpdateInnerEditor()
        {
            Object current = _stampFieldProp != null ? _stampFieldProp.objectReferenceValue : null;
            if (current == _lastStampObj) return;

            DestroyInnerEditor();
            _lastStampObj = current;
            if (current != null)
                _stampFieldEditor = CreateEditor(current);
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
