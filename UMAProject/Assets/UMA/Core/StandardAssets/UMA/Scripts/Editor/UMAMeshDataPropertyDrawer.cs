using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomPropertyDrawer(typeof(UMAMeshData))]
	public class UMAMeshDataPropertyDrawer : PropertyDrawer
	{
		private bool foldout = false;

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return 0;//Let's override this to zero and use GUILayout. //foldout ? (lineHeight * num) : lineHeight;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			//EditorGUI.BeginProperty(position, label, property);

			foldout = EditorGUILayout.Foldout(foldout, "MeshData");
			if (foldout)
			{
				EditorGUI.indentLevel++;
				SerializedProperty vertexCount = PropertyCheck(property, "vertexCount");
				SerializedProperty normals = PropertyCheck(property, "normals");
				SerializedProperty tangents = PropertyCheck(property, "tangents");
				SerializedProperty colors32 = PropertyCheck(property, "colors32");
				SerializedProperty uv = PropertyCheck(property, "uv");
				SerializedProperty uv2 = PropertyCheck(property, "uv2");
				SerializedProperty uv3 = PropertyCheck(property, "uv3");
				SerializedProperty uv4 = PropertyCheck(property, "uv4");
				SerializedProperty clothSkinning = PropertyCheck(property, "clothSkinningSerialized");
				SerializedProperty subMeshCount = PropertyCheck(property, "subMeshCount");
				SerializedProperty umaBoneCount = PropertyCheck(property, "umaBoneCount");
				SerializedProperty rootBoneName = PropertyCheck(property, "RootBoneName");
				SerializedProperty blendshapes = PropertyCheck(property, "blendShapes");

				EditorGUILayout.LabelField( "Vertex Count", vertexCount.intValue.ToString());
				EditorGUILayout.LabelField("Normals Count", normals.arraySize.ToString());
				EditorGUILayout.LabelField("Tangents Count", tangents.arraySize.ToString());
				EditorGUILayout.LabelField("Colors32 Count", colors32.arraySize.ToString());
				EditorGUILayout.LabelField("UV Count", uv.arraySize.ToString());
				EditorGUILayout.LabelField("UV2 Count", uv2.arraySize.ToString());
				EditorGUILayout.LabelField("UV3 Count", uv3.arraySize.ToString());
				EditorGUILayout.LabelField("UV4 Count", uv4.arraySize.ToString());
				EditorGUILayout.LabelField("ClothSkinning Count", clothSkinning.arraySize.ToString());
				EditorGUILayout.LabelField("Submesh Count", subMeshCount.intValue.ToString());
				EditorGUILayout.LabelField("UMABone Count", umaBoneCount.intValue.ToString());
				EditorGUILayout.LabelField("RootBoneName", rootBoneName.stringValue);
				EditorGUILayout.LabelField("BlendShape Count", blendshapes.arraySize.ToString());
				EditorGUILayout.PropertyField( blendshapes, true );

				EditorGUI.indentLevel--;
			}

			//EditorGUI.EndProperty();
		}

		private SerializedProperty PropertyCheck(SerializedProperty property, string relativeName)
		{
			SerializedProperty prop = property.FindPropertyRelative(relativeName);
			if (prop == null)
				Debug.LogError(string.Format("{0} property not found!", relativeName));
			return prop;
		}
	}
}
