using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAGeneratorBuiltin))]
	public class UMAGeneratorBuiltinEditor : UMAGeneratorBaseEditor
	{
		SerializedProperty textureMergePrefab;
		SerializedProperty meshCombiner;
		SerializedProperty InitialScaleFactor;
		SerializedProperty fastGeneration;
		SerializedProperty garbageCollectionRate;

#pragma warning disable 0108
		public void OnEnable()
		{
			base.OnEnable();
			textureMergePrefab = serializedObject.FindProperty("textureMergePrefab");
			meshCombiner = serializedObject.FindProperty("meshCombiner");
			InitialScaleFactor = serializedObject.FindProperty("InitialScaleFactor");
			fastGeneration = serializedObject.FindProperty("fastGeneration");
			garbageCollectionRate = serializedObject.FindProperty("garbageCollectionRate");
		}
#pragma warning restore 0108

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			serializedObject.Update();

			EditorGUILayout.PropertyField(InitialScaleFactor);
			EditorGUILayout.PropertyField(fastGeneration);
			EditorGUILayout.PropertyField(garbageCollectionRate);

			GUILayout.Space(20);
			EditorGUILayout.LabelField("Advanced Configuation", centeredLabel);
			EditorGUILayout.PropertyField(textureMergePrefab);
			EditorGUILayout.PropertyField(meshCombiner);

			var generator = target as UMAGeneratorBuiltin;
			if (EditorApplication.isPlaying)
			{
				EditorGUILayout.LabelField("Time spendt", string.Format("{0} ms", generator.ElapsedTicks / 10000));
				EditorGUILayout.LabelField("Shape Dirty", string.Format("{0}", generator.DnaChanged));
				EditorGUILayout.LabelField("Texture Dirty", string.Format("{0}", generator.TextureChanged));
				EditorGUILayout.LabelField("Mesh Dirty", string.Format("{0}", generator.SlotsChanged));
			}

			serializedObject.ApplyModifiedProperties();
		}
	}
}

