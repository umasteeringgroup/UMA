#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
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
		//we dont really want to use delayedFields because if the user does not change focus from the field in the inspector but instead selects another asset in their projects their changes dont save
		//Instead what we really want to do is set a short delay on saving so that the asset doesn't save while the user is typing in a field
		private float lastActionTime = 0;
		private bool doSave = false;
		//pRaceInspector needs to get unpacked UMATextRecipes so we might need a virtual UMAContext
		GameObject EditorUMAContext;

		public void OnEnable() {
			race = target as RaceData;
			EditorApplication.update += DoDelayedSave;
		}

		void OnDestroy()
		{
			EditorApplication.update -= DoDelayedSave;
			if (EditorUMAContext != null)
				DestroyEditorUMAContext();

		}

		void DoDelayedSave()
		{
			if (doSave && Time.realtimeSinceStartup > (lastActionTime + 0.5f))
			{
				doSave = false;
				lastActionTime = Time.realtimeSinceStartup;
				EditorUtility.SetDirty(race);
				AssetDatabase.SaveAssets();
			}
		}

		private void DestroyEditorUMAContext()
		{
			if (EditorUMAContext != null)
			{
				foreach (Transform child in EditorUMAContext.transform)
				{
					DestroyImmediate(child.gameObject);
				}
				DestroyImmediate(EditorUMAContext);
			}
		}
		/// <summary>
		/// Add to PreInspectorGUI in any derived editors to allow editing of new properties added to races.
		/// </summary>
		partial void PreInspectorGUI(ref bool result);

	    public override void OnInspectorGUI()
	    {
			if (lastActionTime == 0)
				lastActionTime = Time.realtimeSinceStartup;

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
				doSave = true;
				lastActionTime = Time.realtimeSinceStartup;
            }
		}

	    /// <summary>
		/// Add to this method in extender editors if you need to do anything extra when updating the data.
		/// </summary>
		partial void DoUpdate();
	}
}
#endif
