using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomEditor(typeof(UMABoneBakingMeshCombiner))]
	public class UMABoneBakingMeshCombinerEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
			EditorGUILayout.LabelField("Cached Arrays", (target as UMABoneBakingMeshCombiner).CachedBoneWeights.ToString());
			EditorGUILayout.LabelField("Entries", (target as UMABoneBakingMeshCombiner).CachedBoneWeightEntries.ToString());
		}
	}
}
