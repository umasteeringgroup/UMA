#if UNITY_EDITOR
//This cant be in an editor folder because non editor only script UMAAssetIndex need to trigger it to update itself
using UnityEngine;
using UnityEditor;
using System.Collections;
using UMA;

namespace UMAEditor
{
	public class UMAAssetIndexWindow : EditorWindow
	{
		private Editor UAIE;
		private UMAAssetIndex UAI;
		Vector2 scrollPos;


		[MenuItem("UMA/Show UMAAssetIndexWindow")]
		static void Init()
		{
			var window = (UMAAssetIndexWindow)EditorWindow.GetWindow<UMAAssetIndexWindow>("UMA Asset Index");
			window.Show();
		}

		void OnEnable()
		{
			UAI = UMAAssetIndex.Instance;
			UAI.windowInstance = this;
		}

		void OnDestroy()
		{
			UAI.windowInstance = null;
        }

		void OnGUI()
		{
			scrollPos = EditorGUILayout.BeginScrollView(scrollPos, false, true);
			EditorGUILayout.BeginVertical(GUILayout.Width(EditorGUIUtility.currentViewWidth - 20f));
			EditorGUILayout.Space();
			GUILayout.Label("UMA Asset Index", EditorStyles.boldLabel);
			Editor.CreateCachedEditor(UAI, typeof(UMAAssetIndexEditor), ref UAIE);
			UAIE.OnInspectorGUI();
			EditorGUILayout.EndVertical();
			EditorGUILayout.EndScrollView();

		}

	}
}
#endif
