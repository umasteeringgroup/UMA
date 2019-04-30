using UnityEngine;
using UnityEditor;

namespace UMA.Integrations.PowerTools
{
	[CustomEditor(typeof(UMALODConversionSet), isFallback = true)]
	public class UMALODConversionSetEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			GUILayout.Label("Conversion Sets are used by the Power Tools LOD", EditorStyles.boldLabel);
		}
	}
}