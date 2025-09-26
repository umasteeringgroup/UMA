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
            EditorGUILayout.PropertyField(_textureDecalOverlayProp);
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
                "StampField"
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
