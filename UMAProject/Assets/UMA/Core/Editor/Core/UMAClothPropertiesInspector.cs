using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAClothProperties))]
	public class UMAClothPropertiesInspector : Editor
	{
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			EditorGUILayout.Space();
			var cloth = EditorGUILayout.ObjectField("From Cloth", null, typeof(Cloth), true);
			if (cloth != null)
			{
				(target as UMAClothProperties).ReadValues(cloth as Cloth);
				EditorUtility.SetDirty(target);
			}

			EditorGUILayout.Space();
			cloth = EditorGUILayout.ObjectField("To Cloth", null, typeof(Cloth), true);
			if (cloth != null)
			{
				(target as UMAClothProperties).ApplyValues(cloth as Cloth);
				EditorUtility.SetDirty(cloth);
			}
		}
	}
}