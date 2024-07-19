using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UMA.CharacterSystem;

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
		SerializedProperty EditorInitialScaleFactor;
		SerializedProperty editorAtlasResolution;
		SerializedProperty collectGarbage;
		SerializedProperty defaultRendererAsset;
        SerializedProperty defaultOverlayAsset;


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
			EditorInitialScaleFactor = serializedObject.FindProperty("editorInitialScaleFactor");
			editorAtlasResolution = serializedObject.FindProperty("editorAtlasResolution");
			collectGarbage = serializedObject.FindProperty("collectGarbage");
            defaultRendererAsset = serializedObject.FindProperty("defaultRendererAsset");
            defaultOverlayAsset = serializedObject.FindProperty("defaultOverlayAsset");
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
			EditorGUILayout.HelpBox("The default renderer asset is used to set rendering parameters for the generated SkinnedMeshRenderer. This is only used if no other renderer asset is specified on the character, slot, or renderer manager.", MessageType.None);
			EditorGUILayout.PropertyField(defaultRendererAsset);
			EditorGUILayout.HelpBox("The default overlay asset is used when an overay is not specified on a slot. This is for testing only.", MessageType.None);
            EditorGUILayout.PropertyField(defaultOverlayAsset);
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
			else {
				if (GUILayout.Button("Rebuild all editor UMA")) {
					Scene scene = SceneManager.GetActiveScene();
					if(scene != null) {
						GameObject[] sceneObjs = scene.GetRootGameObjects();
						foreach(GameObject go in sceneObjs) 
						{
							DynamicCharacterAvatar[] dcas = go.GetComponentsInChildren<DynamicCharacterAvatar>(false);
							if(dcas.Length > 0) {
								foreach(DynamicCharacterAvatar dca in dcas) 
								{
									if (dca.editorTimeGeneration)
                                    {
                                        dca.GenerateSingleUMA();
                                    }
                                }
							}
						}
					}
				}
			}
			serializedObject.ApplyModifiedProperties();
		}
	}
}