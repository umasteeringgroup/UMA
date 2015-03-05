using UnityEngine;
using System.Collections;
using UnityEditor;
using UMA;

namespace UMAEditor
{
	[CustomEditor(typeof(RaceData))]
	public class RaceInspector : Editor 
	{
	    [MenuItem("Assets/Create/UMA Race")]
	    public static void CreateRaceMenuItem()
	    {
	        CustomAssetUtility.CreateAsset<RaceData>();
	    }

		protected RaceData race;
		
		public void OnEnable() {
			race = target as RaceData;
		}

	    public override void OnInspectorGUI()
	    {
            EditorGUIUtility.LookLikeControls();
			
			race.raceName = EditorGUILayout.TextField("Race Name", race.raceName);
            race.umaTarget = (UMA.RaceData.UMATarget)EditorGUILayout.EnumPopup("UMA Target", race.umaTarget);
            race.genericRootMotionTransformName = EditorGUILayout.TextField("Root Motion Transform", race.genericRootMotionTransformName);
            race.racePrefab = EditorGUILayout.ObjectField("Prefab", race.racePrefab, typeof(GameObject), false) as GameObject;
			race.TPose = EditorGUILayout.ObjectField("TPose", race.TPose, typeof(UmaTPose), false) as UmaTPose;
			race.expressionSet = EditorGUILayout.ObjectField("Expression Set", race.expressionSet, typeof(UMA.PoseTools.UMAExpressionSet), false) as UMA.PoseTools.UMAExpressionSet;

			if (race.baseSlot != null) {
				race.baseSlot = EditorGUILayout.ObjectField("Base Slot", race.baseSlot, typeof(SlotData), false) as SlotData;
			}
			else {
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Base Slot");
				if (race.racePrefab == null) {
					GUI.enabled = false;
				}

				if (GUILayout.Button("Create")) {
					UMAData[] umaDataSet = race.racePrefab.GetComponentsInChildren<UMAData>(true);

					if (umaDataSet.Length == 1) {
#pragma warning disable 618
						UMAData umaData = umaDataSet[0];
						SlotData newSlot = ScriptableObject.CreateInstance<SlotData>();
						newSlot.slotName = race.raceName + "Base";
						int boneCount = umaData.tempBoneData.Length;
						newSlot.umaBoneData = new Transform[boneCount];
						for (int i = 0; i < boneCount; i++) {
							newSlot.umaBoneData[i] = umaData.tempBoneData[i].boneTransform;
						}
						boneCount = umaData.animatedBones.Length;
						newSlot.animatedBones = new Transform[boneCount];
						System.Array.Copy(umaData.animatedBones, newSlot.animatedBones, boneCount);
						if (race.AnimatedBones != null) {
							Debug.LogWarning("AnimatedBones may be missing from base slot!");
						}
//						newSlot.meshRenderer = race.racePrefab.GetComponentInChildren<SkinnedMeshRenderer>();
						string assetPath = AssetDatabase.GetAssetPath(race);
						string assetFolder = assetPath.Substring(0, assetPath.LastIndexOf('/') + 1);
						AssetDatabase.CreateAsset(newSlot, assetFolder + race.name + " Base.asset");
						AssetDatabase.SaveAssets();

						race.baseSlot = newSlot;
#pragma warning restore 618
					}
					else if (umaDataSet.Length > 1) {
						Debug.LogWarning("More than 1 UMAData found in race prefab!");
					}
					else {
						Debug.LogWarning("No UMAData found in race prefab!");
					}
				}

				EditorGUILayout.EndHorizontal();
				GUI.enabled = true;
			}

			EditorGUILayout.Space();

			SerializedProperty dnaConverters = serializedObject.FindProperty("dnaConverterList");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(dnaConverters, true);
			if(EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
			}

			foreach (var field in race.GetType().GetFields())
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
                EditorUtility.SetDirty(race);
                AssetDatabase.SaveAssets();
            }
		}
	    
	}
}
