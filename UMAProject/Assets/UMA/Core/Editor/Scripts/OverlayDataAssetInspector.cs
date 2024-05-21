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
		private SerializedProperty _overlayName;
		private SerializedProperty _overlayType;
		private SerializedProperty _umaMaterial;
		private SerializedProperty _textureList;
		private SerializedProperty _blendList;
		private SerializedProperty _channels;
		private SerializedProperty _rect;
		private SerializedProperty _alphaMask;
		private SerializedProperty _tags;
		private SerializedProperty _occlusionEntries;


		void OnEnable()
		{
			_overlayName = serializedObject.FindProperty("overlayName");
			_overlayType = serializedObject.FindProperty("overlayType");
			_umaMaterial = serializedObject.FindProperty("material");
			_textureList = serializedObject.FindProperty("textureList");
			_blendList =   serializedObject.FindProperty("overlayBlend");
			_rect = serializedObject.FindProperty("rect");
			_alphaMask = serializedObject.FindProperty("alphaMask");
			_tags = serializedObject.FindProperty("tags");
			_occlusionEntries = serializedObject.FindProperty("OcclusionEntries");
			(target as OverlayDataAsset).tagsList = GUIHelper.InitTagsList("tags",serializedObject);

			EditorApplication.update += DoDelayedSave;
		}

		void OnDestroy()
		{
			EditorApplication.update -= DoDelayedSave;
		}

		void DoDelayedSave()
		{
			OverlayDataAsset od = target as OverlayDataAsset; 
			
			if (od.doSave && Time.realtimeSinceStartup > (od.lastActionTime + 0.5f))
			{
				od.doSave = false;
				od.lastActionTime = Time.realtimeSinceStartup;
				EditorUtility.SetDirty(target);
				//AssetDatabase.SaveAssets();
				UMAUpdateProcessor.UpdateOverlay(target as OverlayDataAsset);
			}
		}

		public override void OnInspectorGUI()
		{
			OverlayDataAsset od = target as OverlayDataAsset;
			if (od.lastActionTime == 0)
            {
                od.lastActionTime = Time.realtimeSinceStartup;
            }

            od.ValidateBlendList();
			serializedObject.Update();

			EditorGUI.BeginChangeCheck();

			EditorGUILayout.PropertyField(_overlayName);
			EditorGUILayout.PropertyField(_overlayType);
			EditorGUILayout.PropertyField(_rect);
			EditorGUILayout.LabelField("Note: It is recommended to use UV coordinates (0.0 -> 1.0) in 2.10+ for rect fields.", EditorStyles.helpBox);

			EditorGUILayout.PropertyField(_umaMaterial);

			if (_umaMaterial != null && _umaMaterial.objectReferenceValue != null)
			{
				int textureChannelCount = 0;
				SerializedObject tempObj = new SerializedObject(_umaMaterial.objectReferenceValue);
				_channels = tempObj.FindProperty("channels");

				if (_channels == null)
                {
                    EditorGUILayout.HelpBox("Channels not found!", MessageType.Error);
                }
                else
                {
                    textureChannelCount = _channels.arraySize;
                }

                od.textureFoldout = GUIHelper.FoldoutBar(od.textureFoldout, $"Texture Channels ({textureChannelCount}) Material Channels ({_textureList.arraySize})");

				if (od.textureFoldout)
				{
					GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
					EditorGUILayout.PropertyField(_textureList.FindPropertyRelative("Array.size"));
					_blendList.arraySize = _textureList.arraySize;
					for (int i = 0; i < _textureList.arraySize; i++)
					{
						SerializedProperty textureElement = _textureList.GetArrayElementAtIndex(i);
						SerializedProperty blendElement = _blendList.GetArrayElementAtIndex(i);
						string materialName = "Unknown";

						if (i < _channels.arraySize)
						{
							SerializedProperty channel = _channels.GetArrayElementAtIndex(i);
							if (channel != null)
							{
								SerializedProperty materialPropertyName = channel.FindPropertyRelative("materialPropertyName");
								if (materialPropertyName != null)
								{
									materialName = materialPropertyName.stringValue;
								}
							}
						}
						GUILayout.BeginHorizontal();
						EditorGUILayout.PropertyField(textureElement, new GUIContent(materialName), GUILayout.ExpandWidth(true));
						EditorGUILayout.PropertyField(blendElement, new GUIContent(""), GUILayout.Width(110));
						GUILayout.EndHorizontal();
					}
					GUIHelper.EndVerticalPadded(10);
				}

				if ( _textureList.arraySize != textureChannelCount)
				{
					EditorGUILayout.HelpBox($"Overlay Texture count {_textureList.arraySize} and UMA Material channel count {textureChannelCount} don't match!", MessageType.Error);
				}

				if (!_textureList.hasMultipleDifferentValues)
				{
					bool allValid = true;
					for (int i = 0; i < _textureList.arraySize; i++)
					{
						if (_textureList.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        {
                            allValid = false;
                        }
                    }
					if (!allValid)
                    {
                        EditorGUILayout.HelpBox("Not all textures in Texture List set. This overlay will only work as an additional overlay in a recipe", MessageType.Warning);
                    }
                }
			}
			else
            {
                EditorGUILayout.HelpBox("No UMA Material selected!", MessageType.Warning);
            }

			od.additionalFoldout = GUIHelper.FoldoutBar(od.additionalFoldout, "Alpha mask Parameters");
			if (od.additionalFoldout)
			{
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
				EditorGUILayout.HelpBox("The alpha mask is optional. If it is not set the texture[0].alpha is used instead.", MessageType.Info);
				EditorGUILayout.PropertyField(_alphaMask);
				GUIHelper.EndVerticalPadded(10);
			}

			od.tagsFoldout = GUIHelper.FoldoutBar(od.tagsFoldout, "Tags");
			if (od.tagsFoldout)
			{
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                // EditorGUILayout.PropertyField(_tags, true);
                (target as OverlayDataAsset).tagsList.DoLayoutList();
                GUIHelper.EndVerticalPadded(10);
			}

			od.occlusionFoldout = GUIHelper.FoldoutBar(od.occlusionFoldout, "Occlusion");
			if (od.occlusionFoldout)
			{
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
				EditorGUILayout.PropertyField(_occlusionEntries, true);
				GUIHelper.EndVerticalPadded(10);
			}
            serializedObject.ApplyModifiedProperties();
			if (EditorGUI.EndChangeCheck())
			{
				od.lastActionTime = Time.realtimeSinceStartup;
				od.doSave = true;
			}
		}
	}
}
#endif
