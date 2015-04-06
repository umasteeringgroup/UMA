#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UMA;

namespace UMAEditor
{
	//[CustomEditor(typeof(SlotDataAsset))]
    public class SlotInspector : Editor
    {
        [MenuItem("Assets/Create/UMA Slot Asset")]
        public static void CreateSlotMenuItem()
        {
            CustomAssetUtility.CreateAsset<SlotDataAsset>();
        }

        static private void RecurseTransformsInPrefab(Transform root, List<Transform> transforms)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                transforms.Add(child);
                RecurseTransformsInPrefab(child, transforms);
            }
        }

        static protected Transform[] GetTransformsInPrefab(Transform prefab)
        {
            List<Transform> transforms = new List<Transform>();

            RecurseTransformsInPrefab(prefab, transforms);

            return transforms.ToArray();
        }

		protected SlotDataAsset slot;
        protected bool showBones;
        protected Vector2 boneScroll = new Vector2();
		protected Transform[] umaBoneData;

        public void OnEnable()
        {
			slot = target as SlotDataAsset;
#pragma warning disable 618
			if (slot.meshData != null)
			{
				//if (slot.meshData.rootBoneHash != null)
				//{
				//    umaBoneData = GetTransformsInPrefab(slot.meshData.rootBone);
				//}
				//else
				//{
					umaBoneData = new Transform[0];
				//}
			} 
#if !UMA2_LEAN_AND_CLEAN
			else  if (slot.meshRenderer != null)
			{
				umaBoneData = GetTransformsInPrefab(slot.meshRenderer.rootBone);
			}
#endif
			else
			{
				umaBoneData = new Transform[0];
			}
#pragma warning restore 618
		}

        public override void OnInspectorGUI()
        {
            EditorGUIUtility.LookLikeControls();

            slot.slotName = EditorGUILayout.TextField("Slot Name", slot.slotName);
            slot.slotDNA = EditorGUILayout.ObjectField("DNA Converter", slot.slotDNA, typeof(DnaConverterBehaviour), false) as DnaConverterBehaviour;

            EditorGUILayout.Space();

            slot.subMeshIndex = EditorGUILayout.IntField("Sub Mesh Index", slot.subMeshIndex);
			if (GUI.changed)
			{
				EditorUtility.SetDirty(slot);
			}

            EditorGUILayout.Space();

            if (umaBoneData == null)
            {
                showBones = false;
                GUI.enabled = false;
            }
            showBones = EditorGUILayout.Foldout(showBones, "Bones");
            if (showBones)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Name");
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Animated");
                GUILayout.Space(40f);
                EditorGUILayout.EndHorizontal();

                boneScroll = EditorGUILayout.BeginScrollView(boneScroll);
                EditorGUILayout.BeginVertical();

                foreach (Transform bone in umaBoneData)
                {
                    bool wasAnimated = ArrayUtility.Contains<Transform>(slot.animatedBones, bone);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(bone.name);
                    bool animated = EditorGUILayout.Toggle(wasAnimated, GUILayout.Width(40f));
                    if (animated != wasAnimated)
                    {
                        if (animated)
                        {
                            ArrayUtility.Add<Transform>(ref slot.animatedBones, bone);
							EditorUtility.SetDirty(slot);
						}
                        else
                        {
                            ArrayUtility.Remove<Transform>(ref slot.animatedBones, bone);
							EditorUtility.SetDirty(slot);
						}
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                EditorGUI.indentLevel--;

                if (GUILayout.Button("Clear Animated Bones"))
                {
                    slot.animatedBones = new Transform[0];
					EditorUtility.SetDirty(slot);
                }
            }

            GUI.enabled = true;

            EditorGUILayout.Space();

            slot.slotGroup = EditorGUILayout.TextField("Slot Group", slot.slotGroup);
            var textureNameList = serializedObject.FindProperty("textureNameList");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(textureNameList, true);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }

            SerializedProperty tags = serializedObject.FindProperty("tags");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(tags, true);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
			
			SerializedProperty begunCallback = serializedObject.FindProperty("CharacterBegun");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(begunCallback, true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
			
			SerializedProperty atlasCallback = serializedObject.FindProperty("SlotAtlassed");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(atlasCallback, true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}

			SerializedProperty dnaAppliedCallback = serializedObject.FindProperty("DNAApplied");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(dnaAppliedCallback, true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}

			SerializedProperty characterCompletedCallback = serializedObject.FindProperty("CharacterCompleted");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(characterCompletedCallback, true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
			
			foreach (var field in slot.GetType().GetFields())
			{
				foreach (var attribute in System.Attribute.GetCustomAttributes(field))
				{
					if (attribute is UMAAssetFieldVisible)
					{
						SerializedProperty serializedProp = serializedObject.FindProperty(field.Name);
						EditorGUI.BeginChangeCheck();
						EditorGUILayout.PropertyField(serializedProp);
						if (EditorGUI.EndChangeCheck())
						{
							serializedObject.ApplyModifiedProperties();
						}
						break;
					}
				}
			}

			EditorGUIUtility.LookLikeControls();
            if (GUI.changed)
            {
                EditorUtility.SetDirty(slot);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
#endif
