using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace UMA.Editors
{
    [CustomPropertyDrawer(typeof(UMAMeshData))]
	public class UMAMeshDataPropertyDrawer : PropertyDrawer
	{
		private enum BoneDisplayOrder
		{
			Original,
			NameAscending,
			NameDescending,
			HashAscending
		}

		public static bool foldout = false;
		private string boneFilter = string.Empty;
		private BoneDisplayOrder boneDisplayOrder;

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return 0;//Let's override this to zero and use GUILayout. //foldout ? (lineHeight * num) : lineHeight;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			//EditorGUI.BeginProperty(position, label, property);
			foldout = EditorGUILayout.Foldout(foldout, "MeshData"); // weird. Unity things this changes the object now. Changed Foldout to static, and reset the changed value to false.
			GUI.changed = false;
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
				SerializedProperty bones = PropertyCheck(property, "umaBones");

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
				DrawUmaBones(bones);
                EditorGUI.indentLevel--;
			}

			//EditorGUI.EndProperty();
		}

		private void DrawUmaBones(SerializedProperty bones)
		{
			if (bones == null)
			{
				return;
			}

			bones.isExpanded = EditorGUILayout.Foldout(bones.isExpanded, "UMA Bones (" + bones.arraySize + ")", true);
			if (!bones.isExpanded)
			{
				return;
			}

			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(bones.FindPropertyRelative("Array.size"));
			DrawUmaBoneDisplayControls();

			List<int> visibleBoneIndices = GetVisibleBoneIndices(bones);
			EditorGUILayout.LabelField("Showing " + visibleBoneIndices.Count + " of " + bones.arraySize + " bones");
			for (int visibleBoneIndex = 0; visibleBoneIndex < visibleBoneIndices.Count; visibleBoneIndex++)
			{
				int boneIndex = visibleBoneIndices[visibleBoneIndex];
				SerializedProperty bone = bones.GetArrayElementAtIndex(boneIndex);
				SerializedProperty boneName = bone.FindPropertyRelative("name");
				string displayName = boneName != null && !string.IsNullOrEmpty(boneName.stringValue)
					? boneName.stringValue
					: "Bone " + boneIndex;

				bone.isExpanded = EditorGUILayout.Foldout(bone.isExpanded, "[" + boneIndex + "] " + displayName, true);
				if (!bone.isExpanded)
				{
					continue;
				}

				EditorGUI.indentLevel++;
				EditorGUILayout.PropertyField(bone.FindPropertyRelative("position"));
				EditorGUILayout.PropertyField(bone.FindPropertyRelative("rotation"));
				EditorGUILayout.PropertyField(bone.FindPropertyRelative("scale"));
				EditorGUILayout.PropertyField(boneName);
				EditorGUILayout.PropertyField(bone.FindPropertyRelative("hash"));
				EditorGUILayout.PropertyField(bone.FindPropertyRelative("parent"));
				EditorGUI.indentLevel--;
			}
			EditorGUI.indentLevel--;
		}

		private void DrawUmaBoneDisplayControls()
		{
			EditorGUI.BeginChangeCheck();
			boneFilter = EditorGUILayout.TextField("Filter", boneFilter);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Original"))
			{
				boneDisplayOrder = BoneDisplayOrder.Original;
			}
			if (GUILayout.Button("A-Z"))
			{
				boneDisplayOrder = BoneDisplayOrder.NameAscending;
			}
			if (GUILayout.Button("Z-A"))
			{
				boneDisplayOrder = BoneDisplayOrder.NameDescending;
			}
			if (GUILayout.Button("Hash"))
			{
				boneDisplayOrder = BoneDisplayOrder.HashAscending;
			}
			if (GUILayout.Button("Clear"))
			{
				boneFilter = string.Empty;
			}
			GUILayout.EndHorizontal();
			if (EditorGUI.EndChangeCheck())
			{
				// These values exist only in the drawer and must not dirty the SlotDataAsset.
				GUI.changed = false;
			}
		}

		private List<int> GetVisibleBoneIndices(SerializedProperty bones)
		{
			var visibleBoneIndices = new List<int>();
			string normalizedFilter = string.IsNullOrWhiteSpace(boneFilter) ? string.Empty : boneFilter.Trim();
			for (int boneIndex = 0; boneIndex < bones.arraySize; boneIndex++)
			{
				SerializedProperty bone = bones.GetArrayElementAtIndex(boneIndex);
				SerializedProperty boneName = bone.FindPropertyRelative("name");
				string name = boneName != null ? boneName.stringValue : string.Empty;
				if (string.IsNullOrEmpty(normalizedFilter) || name.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					visibleBoneIndices.Add(boneIndex);
				}
			}

			if (boneDisplayOrder != BoneDisplayOrder.Original)
			{
				visibleBoneIndices.Sort((leftIndex, rightIndex) => CompareBones(bones, leftIndex, rightIndex));
			}

			return visibleBoneIndices;
		}

		private int CompareBones(SerializedProperty bones, int leftIndex, int rightIndex)
		{
			SerializedProperty leftBone = bones.GetArrayElementAtIndex(leftIndex);
			SerializedProperty rightBone = bones.GetArrayElementAtIndex(rightIndex);
			if (boneDisplayOrder == BoneDisplayOrder.HashAscending)
			{
				int leftHash = leftBone.FindPropertyRelative("hash").intValue;
				int rightHash = rightBone.FindPropertyRelative("hash").intValue;
				int hashComparison = leftHash.CompareTo(rightHash);
				return hashComparison != 0 ? hashComparison : leftIndex.CompareTo(rightIndex);
			}

			string leftName = leftBone.FindPropertyRelative("name").stringValue ?? string.Empty;
			string rightName = rightBone.FindPropertyRelative("name").stringValue ?? string.Empty;
			int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(leftName, rightName);
			if (boneDisplayOrder == BoneDisplayOrder.NameDescending)
			{
				nameComparison = -nameComparison;
			}
			return nameComparison != 0 ? nameComparison : leftIndex.CompareTo(rightIndex);
		}

		private SerializedProperty PropertyCheck(SerializedProperty property, string relativeName)
		{
			SerializedProperty prop = property.FindPropertyRelative(relativeName);
			if (prop == null)
            {
                Debug.LogError(string.Format("{0} property not found!", relativeName));
            }

            return prop;
		}
	}
}
