#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
	[CustomEditor(typeof(OverlayDataAsset))]
	[CanEditMultipleObjects]
	public class OverlayDataAssetInspector : Editor
	{
		//DelayedFields ony trigger GUI.changed when the user selects another field. This means if the user changes a value but never changes the selected field it does not ever save.
		//Instead add a short delay on saving so that the asset doesn't save while the user is typing in a field
		private float lastActionTime = 0;
		private bool doSave = false;

		private SerializedProperty _overlayName;
		private SerializedProperty _overlayType;
		private SerializedProperty _umaMaterial;
		private SerializedProperty _textureList;
		private SerializedProperty _channels;
		private SerializedProperty _rect;
		private SerializedProperty _alphaMask;
		private SerializedProperty _tags;
		private SerializedProperty _occlusionEntries;

		private bool additionalFoldout = false;


		void OnEnable()
		{
			_overlayName = serializedObject.FindProperty("overlayName");
			_overlayType = serializedObject.FindProperty("overlayType");
			_umaMaterial = serializedObject.FindProperty("material");
			_textureList = serializedObject.FindProperty("textureList");
			_rect = serializedObject.FindProperty("rect");
			_alphaMask = serializedObject.FindProperty("alphaMask");
			_tags = serializedObject.FindProperty("tags");
			_occlusionEntries = serializedObject.FindProperty("OcclusionEntries");

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
				Debug.Log("Saved OverlayDataAsset lastActionTime = " + lastActionTime + " realTime = " + Time.realtimeSinceStartup);
				lastActionTime = Time.realtimeSinceStartup;
				EditorUtility.SetDirty(target);
				AssetDatabase.SaveAssets();
			}
		}

		public override void OnInspectorGUI()
		{
			if (lastActionTime == 0)
				lastActionTime = Time.realtimeSinceStartup;

			serializedObject.Update();

			EditorGUI.BeginChangeCheck();

			EditorGUILayout.PropertyField(_overlayName);
			EditorGUILayout.PropertyField(_overlayType);
			EditorGUILayout.PropertyField(_rect);

			EditorGUILayout.PropertyField(_umaMaterial);

			if (_umaMaterial != null && _umaMaterial.objectReferenceValue != null)
			{
				int textureChannelCount = 0;
				SerializedObject tempObj = new SerializedObject(_umaMaterial.objectReferenceValue);
				_channels = tempObj.FindProperty("channels");

				if (_channels == null)
					EditorGUILayout.HelpBox("Channels not found!", MessageType.Error);
				else
					textureChannelCount = _channels.arraySize;

				if (_textureList.arraySize <= 0 || _textureList.arraySize != textureChannelCount)
					_textureList.arraySize = textureChannelCount;

				EditorGUILayout.PropertyField(_textureList);

				if (_textureList.isExpanded)
				{
					EditorGUI.indentLevel++;
					for (int i = 0; i < _textureList.arraySize; i++)
					{
						SerializedProperty channel = _channels.GetArrayElementAtIndex(i);
						string materialName = channel.FindPropertyRelative("materialPropertyName").stringValue;
						EditorGUILayout.PropertyField(_textureList.GetArrayElementAtIndex(i), new GUIContent(materialName));
					}
					EditorGUI.indentLevel--;
				}

				if (!_textureList.hasMultipleDifferentValues)
				{
					bool allValid = true;
					for (int i = 0; i < _textureList.arraySize; i++)
					{
						if (_textureList.GetArrayElementAtIndex(i).objectReferenceValue == null)
							allValid = false;
					}
					if (!allValid)
						EditorGUILayout.HelpBox("Not all textures in Texture List set!", MessageType.Error);
				}
			}
			else
				EditorGUILayout.HelpBox("No UMA Material selected!", MessageType.Warning);

			GUILayout.Space(20f);
			additionalFoldout = EditorGUILayout.Foldout(additionalFoldout, "Additional Parameters");

			if (additionalFoldout)
			{
				EditorGUI.indentLevel++;
				EditorGUILayout.PropertyField(_alphaMask);
				EditorGUILayout.PropertyField(_tags, true);
				EditorGUILayout.PropertyField(_occlusionEntries, true);
				EditorGUI.indentLevel--;
			}

			serializedObject.ApplyModifiedProperties();
			if (EditorGUI.EndChangeCheck())
			{
				lastActionTime = Time.realtimeSinceStartup;
				doSave = true;
			}
		}
	}
}
#endif
