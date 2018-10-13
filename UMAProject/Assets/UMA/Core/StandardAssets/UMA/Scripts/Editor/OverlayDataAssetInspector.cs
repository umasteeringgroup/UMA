#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
	[CustomEditor(typeof(OverlayDataAsset))]
	public class OverlayDataAssetInspector : Editor
	{
		//DelayedFields ony trigger GUI.changed when the user selects another field. This means if the user changes a value but never changes the selected field it does not ever save.
		//Instead add a short delay on saving so that the asset doesn't save while the user is typing in a field
		private float lastActionTime = 0;
		private bool doSave = false;

		void OnEnable()
		{
			EditorApplication.update += DoDelayedSave;
		}

		void OnDestroy()
		{
			EditorApplication.update -= DoDelayedSave;
		}

		void DoDelayedSave()
		{
			if (doSave && Time.realtimeSinceStartup > (lastActionTime + 0.5f))
			{
				doSave = false;
				Debug.Log("Saved OverlayDataAsset lastActionTime = " + lastActionTime + " realTime = " + Time.realtimeSinceStartup);
				lastActionTime = Time.realtimeSinceStartup;
				EditorUtility.SetDirty(target);
				AssetDatabase.SaveAssets();
			}
		}

		public override void OnInspectorGUI()
		{
			if (lastActionTime == 0)
				lastActionTime = Time.realtimeSinceStartup;

			EditorGUI.BeginChangeCheck();
			base.OnInspectorGUI();
			if (EditorGUI.EndChangeCheck())
			{
				lastActionTime = Time.realtimeSinceStartup;
				doSave = true;
			}
		}
	}
}
#endif
