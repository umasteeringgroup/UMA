using UnityEngine;
using UnityEditor;
using UMA.Integration.PowerTools;

namespace UMA.Integrations.PowerTools
{
	[CustomEditor(typeof(UMALODConversionSet), isFallback = false)]
	public class UMALODConversionSetEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			GUILayout.Label("Conversion Sets are used by the Power Tools LOD", EditorStyles.boldLabel);
		}
	}
}