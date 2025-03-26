using UnityEditor;
using UnityEngine;

namespace UMA
{

	public class FindComponentUsagesWindow : EditorWindow
	{
		private MonoScript targetMonoScript;
		private Vector2 scrollPos;

		[MenuItem("UMA/Tools/Find Component Usages")]
		public static void ShowWindow()
		{
			GetWindow<FindComponentUsagesWindow>(true, "Find Component Usages", true);
		}

		void OnGUI()
		{
			targetMonoScript = (MonoScript)EditorGUILayout.ObjectField(targetMonoScript, typeof(MonoScript), false);
			if (targetMonoScript != null)
			{
				scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
				System.Type targetType = targetMonoScript.GetClass();
				if (targetType != null && targetType.IsSubclassOf(typeof(MonoBehaviour)))
				{
					Object[] allMonoscriptsAsObjects = Resources.FindObjectsOfTypeAll(targetType);
					foreach (Object monoscriptAsObject in allMonoscriptsAsObjects)
					{
						GameObject prefab = ((MonoBehaviour)monoscriptAsObject).gameObject;
						if (GUILayout.Button(prefab.name))
						{
							Selection.activeObject = prefab;
						}
					}
				}
				else
				{
					EditorGUILayout.LabelField($"{targetMonoScript.name} is not a subclass of MonoBehavior");
				}
				EditorGUILayout.EndScrollView();
			}
		}
	}
}