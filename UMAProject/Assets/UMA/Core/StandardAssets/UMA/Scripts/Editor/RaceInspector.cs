#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using UnityEditor;
using UMA;

namespace UMAEditor
{
	[CustomEditor(typeof(RaceData))]
	public partial class RaceInspector : Editor 
	{
	    [MenuItem("Assets/Create/UMA/Core/RaceData")]
	    public static void CreateRaceMenuItem()
	    {
	        CustomAssetUtility.CreateAsset<RaceData>();
	    }

		protected RaceData race;
        protected bool _needsUpdate;
        protected string _errorMessage;
		
		public void OnEnable() {
			race = target as RaceData;
		}

		/// <summary>
		/// Add to PreInspectorGUI in any derived editors to allow editing of new properties added to races.
		/// </summary>
		partial void PreInspectorGUI(ref bool result);

	    public override void OnInspectorGUI()
	    { 
			race.raceName = EditorGUILayout.TextField("Race Name", race.raceName);
            race.umaTarget = (UMA.RaceData.UMATarget)EditorGUILayout.EnumPopup("UMA Target", race.umaTarget);
            race.genericRootMotionTransformName = EditorGUILayout.TextField("Root Motion Transform", race.genericRootMotionTransformName);
			race.TPose = EditorGUILayout.ObjectField("TPose", race.TPose, typeof(UmaTPose), false) as UmaTPose;
			race.expressionSet = EditorGUILayout.ObjectField("Expression Set", race.expressionSet, typeof(UMA.PoseTools.UMAExpressionSet), false) as UMA.PoseTools.UMAExpressionSet;

			EditorGUILayout.Space();

			SerializedProperty dnaConverters = serializedObject.FindProperty("dnaConverterList");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(dnaConverters, true);
			if(EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
			}

			SerializedProperty dnaRanges = serializedObject.FindProperty("dnaRanges");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(dnaRanges, true);
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

            try {
				PreInspectorGUI(ref _needsUpdate);
				if(_needsUpdate == true){
						DoUpdate();
				}
			}catch (UMAResourceNotFoundException e){
				_errorMessage = e.Message;
			}
			
            if (GUI.changed)
            {
                EditorUtility.SetDirty(race);
                AssetDatabase.SaveAssets();
            }
		}

	    /// <summary>
		/// Add to this method in extender editors if you need to do anything extra when updating the data.
		/// </summary>
		partial void DoUpdate();
	}
}
#endif