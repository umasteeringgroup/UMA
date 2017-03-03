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
		bool needsReenable = false;

		[MenuItem("UMA/UMA Global Library")]
		public static void Init()
		{
			var window = (UMAAssetIndexWindow)EditorWindow.GetWindow<UMAAssetIndexWindow>("UMA Global Library");
			window.Show();
		}

		void OnEnable()
		{
			GetUAI();
        }

		void OnDestroy()
		{
			if(UAI != null)
				UAI.windowInstance = null;
			needsReenable = false;
        }

		void GetUAI()
		{
			UAI = UMAAssetIndex.Instance;
			if (UAI != null)
			{
				UAI.windowInstance = this;
				needsReenable = false;
			}
			else
			{
				needsReenable = true;
			}
		}

		void OnGUI()
		{
			if (needsReenable)
			{
				GetUAI();
				//If we are still waiting
				if (needsReenable)
				{
					return;
				}
			}
			scrollPos = EditorGUILayout.BeginScrollView(scrollPos, false, true);
			EditorGUILayout.BeginVertical(GUILayout.Width(EditorGUIUtility.currentViewWidth - 20f));
			EditorGUILayout.Space();
			var BoldCenteredHelpBox = new GUIStyle(EditorStyles.helpBox);
			BoldCenteredHelpBox.fontSize = EditorStyles.boldLabel.fontSize;
			BoldCenteredHelpBox.fontStyle = FontStyle.Bold;
			BoldCenteredHelpBox.alignment = TextAnchor.MiddleCenter;
			GUILayout.Label("UMA Global Library", BoldCenteredHelpBox);
			Editor.CreateCachedEditor(UAI, typeof(UMAAssetIndexEditor), ref UAIE);
			UAIE.OnInspectorGUI();
			EditorGUILayout.EndVertical();
			EditorGUILayout.EndScrollView();

		}

	}
}
#endif
