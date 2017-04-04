using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAGeneratorBuiltin), true)]
	public class UMAGeneratorBuiltinEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
			var generator = target as UMAGeneratorBuiltin;
			if (EditorApplication.isPlaying)
			{
				EditorGUILayout.LabelField("Time spendt", string.Format("{0} ms", generator.ElapsedTicks / 10000));
				EditorGUILayout.LabelField("Shape Dirty", string.Format("{0}", generator.DnaChanged));
				EditorGUILayout.LabelField("Texture Dirty", string.Format("{0}", generator.TextureChanged));
				EditorGUILayout.LabelField("Mesh Dirty", string.Format("{0}", generator.SlotsChanged));
			}
		}
	}
}

