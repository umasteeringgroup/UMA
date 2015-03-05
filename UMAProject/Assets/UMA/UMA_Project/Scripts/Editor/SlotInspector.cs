using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UMA;

namespace UMAEditor
{
    [CustomEditor(typeof(SlotData))]
    public class SlotInspector : Editor
    {
        [MenuItem("Assets/Create/UMA Slot")]
        public static void CreateSlotMenuItem()
        {
            CustomAssetUtility.CreateAsset<SlotData>();
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

        protected SlotData slot;
        protected bool showBones;
        protected Vector2 boneScroll = new Vector2();

        public void OnEnable()
        {
            slot = target as SlotData;
        }

        public override void OnInspectorGUI()
        {
            EditorGUIUtility.LookLikeControls();

            slot.slotName = EditorGUILayout.TextField("Slot Name", slot.slotName);
            slot.slotDNA = EditorGUILayout.ObjectField("DNA Converter", slot.slotDNA, typeof(DnaConverterBehaviour), false) as DnaConverterBehaviour;

            EditorGUILayout.Space();

            SkinnedMeshRenderer renderer = EditorGUILayout.ObjectField("Renderer", slot.meshRenderer, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;
            if (renderer != slot.meshRenderer)
            {
                slot.umaBoneData = null;
                slot.animatedBones = new Transform[0];

                slot.meshRenderer = renderer;
				if (renderer != null)
				{
					slot.umaBoneData = GetTransformsInPrefab(slot.meshRenderer.rootBone);
				}
				else
				{
					slot.umaBoneData = null;
					slot.animatedBones = null;
				}
            }
            slot.subMeshIndex = EditorGUILayout.IntField("Sub Mesh Index", slot.subMeshIndex);
            Material material = EditorGUILayout.ObjectField("Material", slot.materialSample, typeof(Material), false) as Material;
            if (material != slot.materialSample)
            {
                slot.materialSample = material;
            }

            EditorGUILayout.Space();

            if (slot.umaBoneData == null)
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

                Transform deletedBone = null;
                foreach (Transform bone in slot.umaBoneData)
                {
                    bool wasAnimated = ArrayUtility.Contains<Transform>(slot.animatedBones, bone);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(bone.name);
                    bool animated = EditorGUILayout.Toggle(wasAnimated, GUILayout.Width(40f));
                    if (animated != wasAnimated)
                    {
                        if (animated)
                        {
                            Undo.RecordObject(slot, "Add Animated Bone");
                            ArrayUtility.Add<Transform>(ref slot.animatedBones, bone);
                        }
                        else
                        {
                            Undo.RecordObject(slot, "Remove Animated Bone");
                            ArrayUtility.Remove<Transform>(ref slot.animatedBones, bone);
                        }
                    }
                    if (GUILayout.Button("-", GUILayout.Width(20f)))
                    {
                        deletedBone = bone;
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (deletedBone != null)
                {
                    Undo.RecordObject(slot, "Delete Bone");
                    ArrayUtility.Remove<Transform>(ref slot.umaBoneData, deletedBone);
                    ArrayUtility.Remove<Transform>(ref slot.animatedBones, deletedBone);
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                EditorGUI.indentLevel--;

                if (GUILayout.Button("Reset Bones"))
                {
                    Undo.RecordObject(slot, "Reset Bones");
                    slot.umaBoneData = GetTransformsInPrefab(slot.meshRenderer.rootBone);
                    slot.animatedBones = new Transform[0];
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
