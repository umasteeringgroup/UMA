using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAGeneratorBuiltin))]
	public class UMAGeneratorBuiltinEditor : UMAGeneratorBaseEditor
	{
		SerializedProperty textureMerge;
		SerializedProperty meshCombiner;
		SerializedProperty InitialScaleFactor;
		SerializedProperty IterationCount;
		SerializedProperty fastGeneration;
		SerializedProperty garbageCollectionRate;
		SerializedProperty processAllPending;
		SerializedProperty NoCoroutines;
		SerializedProperty EditorInitialScaleFactor;
		SerializedProperty editorAtlasResolution;
		SerializedProperty collectGarbage;


#pragma warning disable 0108
		public override void OnEnable()
		{
			base.OnEnable();
			textureMerge = serializedObject.FindProperty("textureMerge");
			meshCombiner = serializedObject.FindProperty("meshCombiner");
			InitialScaleFactor = serializedObject.FindProperty("InitialScaleFactor");
			IterationCount = serializedObject.FindProperty("IterationCount");
			processAllPending = serializedObject.FindProperty("processAllPending");
			fastGeneration = serializedObject.FindProperty("fastGeneration");
			garbageCollectionRate = serializedObject.FindProperty("garbageCollectionRate");
			NoCoroutines = serializedObject.FindProperty("NoCoroutines");
			EditorInitialScaleFactor = serializedObject.FindProperty("editorInitialScaleFactor");
			editorAtlasResolution = serializedObject.FindProperty("editorAtlasResolution");
			collectGarbage = serializedObject.FindProperty("collectGarbage");
		}
#pragma warning restore 0108

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			serializedObject.Update();

			EditorGUILayout.PropertyField(InitialScaleFactor);
			EditorGUILayout.PropertyField(fastGeneration);
			EditorGUILayout.PropertyField(IterationCount);
			EditorGUILayout.PropertyField(collectGarbage);
			EditorGUILayout.PropertyField(garbageCollectionRate);
			EditorGUILayout.PropertyField(processAllPending);
			GUILayout.Space(20);
			EditorGUILayout.HelpBox("Edit time generation options. Keep the atlas size down and the scale factor high to address possible problems loading large scene files.",MessageType.None);
			EditorGUILayout.PropertyField(editorAtlasResolution);
			EditorGUILayout.PropertyField(EditorInitialScaleFactor);


			GUILayout.Space(20);
			EditorGUILayout.LabelField("Advanced Configuration", centeredLabel);
			EditorGUILayout.PropertyField(NoCoroutines);
			EditorGUILayout.PropertyField(textureMerge);
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

