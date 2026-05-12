using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(MeshHideAsset))]
	public class MeshHideInspector : Editor
	{
		public override void OnInspectorGUI()
		{
			EditorGUILayout.HelpBox("This asset is used to store triangle hide information for a slot. It is not intended to be edited directly. To edit the mesh hide information, create a character with the slot you are targettings, open the Utilities section, and drop the Mesh Hide Asset into the drop area where indicated. This will open a new scene where you can select triangles to hide. When you save and exit the editor scene, the Mesh Hide Asset will be updated with the new triangle information.", MessageType.Info);
			GUILayout.Space(10);
			DrawDefaultInspector();
		}
	}
}
