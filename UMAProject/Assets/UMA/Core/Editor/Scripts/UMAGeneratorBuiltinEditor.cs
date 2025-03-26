using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UMA.CharacterSystem;
using System.Timers;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAGeneratorBuiltin))]
	public class UMAGeneratorBuiltinEditor : UMAGeneratorBaseEditor
	{
		SerializedProperty textureMerge;
		SerializedProperty meshCombiner;
		SerializedProperty InitialScaleFactor;
		SerializedProperty IterationCount;
		SerializedProperty garbageCollectionRate;
		SerializedProperty processAllPending;
		SerializedProperty applyInline;
        SerializedProperty MaxQueuedConversionsPerFrame;
		SerializedProperty EditorInitialScaleFactor;
		SerializedProperty editorAtlasResolution;
		SerializedProperty collectGarbage;
		SerializedProperty defaultRendererAsset;
		SerializedProperty defaultOverlayAsset;
		SerializedProperty convertRenderTexture;

        public static bool showGenerationSettings = false;
		public static bool showAdvancedSettings = false;
		public static bool showStatistics = true;
		public static bool showEditTimeSettings = false;


#pragma warning disable 0108
        public override void OnEnable()
		{
			base.OnEnable();
			textureMerge = serializedObject.FindProperty("textureMerge");
			meshCombiner = serializedObject.FindProperty("meshCombiner");
			InitialScaleFactor = serializedObject.FindProperty("InitialScaleFactor");
			IterationCount = serializedObject.FindProperty("IterationCount");
			processAllPending = serializedObject.FindProperty("processAllPending");
            applyInline = serializedObject.FindProperty("applyInline");
            garbageCollectionRate = serializedObject.FindProperty("garbageCollectionRate");
			EditorInitialScaleFactor = serializedObject.FindProperty("editorInitialScaleFactor");
			editorAtlasResolution = serializedObject.FindProperty("editorAtlasResolution");
			collectGarbage = serializedObject.FindProperty("collectGarbage");
			defaultRendererAsset = serializedObject.FindProperty("defaultRendererAsset");
			defaultOverlayAsset = serializedObject.FindProperty("defaultOverlayAsset");
			MaxQueuedConversionsPerFrame = serializedObject.FindProperty("MaxQueuedConversionsPerFrame");
			convertRenderTexture = serializedObject.FindProperty("convertRenderTexture");

		}
#pragma warning restore 0108

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			serializedObject.Update();

            showGenerationSettings = EditorGUILayout.Foldout(showGenerationSettings, "Generation Settings");
			if (showGenerationSettings)
			{
				EditorGUILayout.PropertyField(MaxQueuedConversionsPerFrame);
				EditorGUILayout.PropertyField(InitialScaleFactor);
				EditorGUILayout.PropertyField(IterationCount);
				EditorGUILayout.PropertyField(collectGarbage);
				EditorGUILayout.PropertyField(garbageCollectionRate);
				EditorGUILayout.PropertyField(processAllPending);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("SaveAndRestoreIgnoredItems"));
			}
            showEditTimeSettings = EditorGUILayout.Foldout(showEditTimeSettings, "Edit Time Settings");
			if (showEditTimeSettings)
			{
                EditorGUILayout.HelpBox("Edit time generation options. Keep the atlas size down and the scale factor high to address possible problems loading large scene files.", MessageType.None);
                EditorGUILayout.PropertyField(editorAtlasResolution);
                EditorGUILayout.PropertyField(EditorInitialScaleFactor);
            }

            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings");
			if (showAdvancedSettings)
			{
				GUILayout.Space(20);
				EditorGUILayout.LabelField("Advanced Configuration", centeredLabel);
				EditorGUILayout.HelpBox("Use Apply Inline when you want converted rendertextures to apply immediately on your platform",MessageType.None);
				EditorGUILayout.PropertyField(applyInline);
                EditorGUILayout.HelpBox("The default renderer asset is used to set rendering parameters for the generated SkinnedMeshRenderer. This is only used if no other renderer asset is specified on the character, slot, or renderer manager.", MessageType.None);
				EditorGUILayout.PropertyField(defaultRendererAsset);
				EditorGUILayout.HelpBox("The default overlay asset is used when an overay is not specified on a slot. This is for testing only.", MessageType.None);
				EditorGUILayout.PropertyField(defaultOverlayAsset);
				EditorGUILayout.PropertyField(textureMerge);
				EditorGUILayout.PropertyField(meshCombiner);
			}

            showStatistics = EditorGUILayout.Foldout(showStatistics, "Statistics");
			if (showStatistics)
			{
				var generator = target as UMAGeneratorBuiltin;
				EditorGUILayout.Space(10);
				EditorGUILayout.LabelField("Generation Metrics", centeredLabel);
				if (Application.isPlaying)
				{
					EditorGUILayout.LabelField("Elapsed Time", string.Format("{0} ms", generator.ElapsedTicks / 10000));
				}
				else
				{
					EditorGUILayout.LabelField("Elapsed Time", "N/A");
				}
				EditorGUILayout.LabelField("Shape Dirty", string.Format("{0}", generator.DnaChanged));
				EditorGUILayout.LabelField("Texture Dirty", string.Format("{0}", generator.TextureChanged));
				EditorGUILayout.LabelField("Mesh Dirty", string.Format("{0}", generator.SlotsChanged));
				if (convertRenderTexture.boolValue == true)
				{
					EditorGUILayout.Space(10);
					EditorGUILayout.LabelField("Texture Metrics", centeredLabel);
					EditorGUILayout.LabelField("Textures Processed", string.Format("{0}", generator.TexturesProcessed));
					EditorGUILayout.LabelField("Copies Enqueued", string.Format("{0}", RenderTexToCPU.copiesEnqueued));
					EditorGUILayout.LabelField("Copies Dequeued", string.Format("{0}", RenderTexToCPU.copiesDequeued));
					EditorGUILayout.LabelField("Unable to Queue", string.Format("{0}", RenderTexToCPU.unableToQueue));
					EditorGUILayout.LabelField("Missed Uploads", string.Format("{0}", RenderTexToCPU.misseduploads));
					EditorGUILayout.LabelField("Error Uploads", string.Format("{0}", RenderTexToCPU.errorUploads));
					EditorGUILayout.LabelField("Textures Uploaded", string.Format("{0}", RenderTexToCPU.texturesUploaded));
					EditorGUILayout.Space(10);
					EditorGUILayout.LabelField("RenderTextures Cleaned", centeredLabel);
					EditorGUILayout.LabelField("UMAData Cleanup", string.Format("{0}", RenderTexToCPU.renderTexturesCleanedUMAData));
					EditorGUILayout.LabelField("Applied Cleanup", string.Format("{0}", RenderTexToCPU.renderTexturesCleanedApplied));
					EditorGUILayout.LabelField("Not Applied Cleanup", string.Format("{0}", RenderTexToCPU.renderTexturesCleanedMissed));
					EditorGUILayout.LabelField("Total Cleanup", string.Format("{0}", RenderTexToCPU.renderTexturesCleanedUMAData + RenderTexToCPU.renderTexturesCleanedApplied + RenderTexToCPU.renderTexturesCleanedMissed));
                    if (GUILayout.Button("Reset editor statistics"))
					{
						generator.ElapsedTicks = 0;
						generator.DnaChanged = 0;
						generator.TextureChanged = 0;
						generator.SlotsChanged = 0;
						generator.TexturesProcessed = 0;
						RenderTexToCPU.copiesEnqueued = 0;
						RenderTexToCPU.copiesDequeued = 0;
						RenderTexToCPU.unableToQueue = 0;
						RenderTexToCPU.misseduploads = 0;
						RenderTexToCPU.errorUploads = 0;
						RenderTexToCPU.texturesUploaded = 0;
						RenderTexToCPU.renderTexturesCleanedUMAData = 0;
						RenderTexToCPU.renderTexturesCleanedApplied = 0;
						RenderTexToCPU.renderTexturesCleanedMissed = 0;
					}
				}
			}

			if (!EditorApplication.isPlaying)
			{
				if (GUILayout.Button("Rebuild all editor UMA"))
				{
					Scene scene = SceneManager.GetActiveScene();
					if (scene != null)
					{
						GameObject[] sceneObjs = scene.GetRootGameObjects();
						foreach (GameObject go in sceneObjs)
						{
							DynamicCharacterAvatar[] dcas = go.GetComponentsInChildren<DynamicCharacterAvatar>(false);
							if (dcas.Length > 0)
							{
								foreach (DynamicCharacterAvatar dca in dcas)
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