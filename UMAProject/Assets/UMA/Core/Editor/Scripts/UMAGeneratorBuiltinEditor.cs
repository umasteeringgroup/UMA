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
		SerializedProperty InterFrameDelay;
		SerializedProperty MaxMultiStepWorkMilliseconds;
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
		SerializedProperty showInHierarchy;
		SerializedProperty Use32BitBuffers;
		SerializedProperty alwaysRegenerateRenderers;
		SerializedProperty AutomaticScaling;
		SerializedProperty ScaleGPUMemoryCutoffMB;
		SerializedProperty ScaleSystemMemoryCutoffMB;

        public static bool showGenerationSettings = false;
		public static bool showAdvancedSettings = false;
		public static bool showStatistics = true;
		public static bool showEditTimeSettings = false;
		public static bool showRuntimeTuningSettings = false;

        private static bool IsEditorBusy()
		{
			return EditorApplication.isCompiling || EditorApplication.isUpdating;
		}

		/// <summary>
		/// Rebuilds every DynamicCharacterAvatar in the active scene that has
		/// editor-time generation enabled.
		/// </summary>
		internal static void RebuildAllEditorUMA()
		{
			if (IsEditorBusy()) return;
			Scene scene = SceneManager.GetActiveScene();
			if (scene == null) return;

			GameObject[] sceneObjs = scene.GetRootGameObjects();
			foreach (GameObject go in sceneObjs)
			{
				DynamicCharacterAvatar[] dcas = go.GetComponentsInChildren<DynamicCharacterAvatar>(false);
				if (dcas.Length == 0) continue;

				foreach (DynamicCharacterAvatar dca in dcas)
				{
					if (dca != null && dca.editorTimeGeneration)
					{
						// This method is only invoked by explicit editor commands, so it
						// remains available while automatic editor generation is paused.
						dca.GenerateSingleUMA(false, true);
					}
				}
			}
		}

		private void OnBeforeAssemblyReload()
		{
			// No event subscriptions in this editor, but keep hook for parity and future safety
		}

#pragma warning disable 0108
		public override void OnEnable()
		{
			base.OnEnable();

			// Defer initialization until the editor is stable and target is valid
			if (IsEditorBusy() || target == null || serializedObject == null)
			{
				EditorApplication.delayCall += () =>
				{
					if (this != null) OnEnable();
				};
				return;
			}

			AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
			AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

			// Find properties (guard against absent properties)
			textureMerge = serializedObject.FindProperty("textureMerge");
			meshCombiner = serializedObject.FindProperty("meshCombiner");
			InitialScaleFactor = serializedObject.FindProperty("InitialScaleFactor");
			IterationCount = serializedObject.FindProperty("IterationCount");
			InterFrameDelay = serializedObject.FindProperty("InterFrameDelay");
			MaxMultiStepWorkMilliseconds = serializedObject.FindProperty("MaxMultiStepWorkMilliseconds");
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
			showInHierarchy = serializedObject.FindProperty("showInHierarchy");
			Use32BitBuffers = serializedObject.FindProperty("Use32BitBuffers");
			alwaysRegenerateRenderers = serializedObject.FindProperty("alwaysRegenerateRenderers");
			AutomaticScaling = serializedObject.FindProperty("AutomaticScaling");
			ScaleGPUMemoryCutoffMB = serializedObject.FindProperty("ScaleGPUMemoryCutoffMB");
			ScaleSystemMemoryCutoffMB = serializedObject.FindProperty("ScaleSystemMemoryCutoffMB");
        }
#pragma warning restore 0108

		private void OnDisable()
		{
			AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
		}

		private static void DrawIfPresent(SerializedProperty prop, string explicitLabel = null)
		{
			if (prop != null)
			{
				if (string.IsNullOrEmpty(explicitLabel))
					EditorGUILayout.PropertyField(prop);
				else
					EditorGUILayout.PropertyField(prop, new GUIContent(explicitLabel));
			}
		}

		private static string FormatStopwatchMilliseconds(long ticks)
		{
			return string.Format(
				"{0:F3} ms",
				UMATime.StopwatchTicksToMilliseconds(ticks));
		}

		public override void OnInspectorGUI()
		{
			if (IsEditorBusy())
			{
				EditorGUILayout.HelpBox("Compiling/Updating...", MessageType.Info);
				return;
			}
			if (target == null || serializedObject == null || serializedObject.targetObject == null)
			{
				EditorGUILayout.HelpBox("Inspector target is not available (domain reload).", MessageType.Info);
				return;
			}

			base.OnInspectorGUI();

			serializedObject.Update();

			showGenerationSettings = EditorGUILayout.Foldout(showGenerationSettings, "Generation Settings");
			if (showGenerationSettings)
			{
				DrawIfPresent(MaxQueuedConversionsPerFrame);
				DrawIfPresent(InitialScaleFactor);
				DrawIfPresent(IterationCount);
				DrawIfPresent(InterFrameDelay);
				DrawIfPresent(MaxMultiStepWorkMilliseconds, "Max Multi-Step Work (ms)");
				DrawIfPresent(collectGarbage);
				DrawIfPresent(garbageCollectionRate);
				DrawIfPresent(processAllPending);

				

				var saveRestoreIgnored = serializedObject.FindProperty("SaveAndRestoreIgnoredItems");
				DrawIfPresent(saveRestoreIgnored);
				DrawIfPresent(showInHierarchy);
			}
			showRuntimeTuningSettings = EditorGUILayout.Foldout(showRuntimeTuningSettings, "Runtime Tuning Settings");
			if (showRuntimeTuningSettings)
			{
				EditorGUILayout.HelpBox("Automatic scaling options to help manage memory usage on constrained devices.", MessageType.None);
				DrawIfPresent(AutomaticScaling);
				DrawIfPresent(ScaleGPUMemoryCutoffMB, "GPU Memory Cutoff (MB)");
				DrawIfPresent(ScaleSystemMemoryCutoffMB, "System Memory Cutoff (MB)");
			}

            showEditTimeSettings = EditorGUILayout.Foldout(showEditTimeSettings, "Edit Time Settings");
			if (showEditTimeSettings)
			{
				EditorGUILayout.HelpBox("Edit time generation options. Keep the atlas size down and the scale factor high to address possible problems loading large scene files.", MessageType.None);
				DrawIfPresent(editorAtlasResolution);
				DrawIfPresent(EditorInitialScaleFactor);
			}

			showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings");
			if (showAdvancedSettings)
			{
				GUILayout.Space(20);
				EditorGUILayout.LabelField("Advanced Configuration", centeredLabel);
				EditorGUILayout.HelpBox("Use Apply Inline when you want converted rendertextures to apply immediately on your platform", MessageType.None);
				DrawIfPresent(applyInline);
				EditorGUILayout.HelpBox("The default renderer asset is used to set rendering parameters for the generated SkinnedMeshRenderer. This is only used if no other renderer asset is specified on the character, slot, or renderer manager.", MessageType.None);
				DrawIfPresent(defaultRendererAsset);
				EditorGUILayout.HelpBox("The default overlay asset is used when an overay is not specified on a slot. This is for testing only.", MessageType.None);
				DrawIfPresent(defaultOverlayAsset);
				DrawIfPresent(alwaysRegenerateRenderers, "Always Regenerate Renderers");
                DrawIfPresent(Use32BitBuffers);
				DrawIfPresent(showInHierarchy);
				DrawIfPresent(textureMerge);
				DrawIfPresent(meshCombiner);
			}

			showStatistics = EditorGUILayout.Foldout(showStatistics, "Statistics");
			if (showStatistics)
			{
				var generator = target as UMAGeneratorBuiltin;
				EditorGUILayout.Space(10);
				EditorGUILayout.LabelField("Generation Metrics", centeredLabel);
				if (Application.isPlaying && generator != null)
				{
					EditorGUILayout.LabelField("Generator Work Time", FormatStopwatchMilliseconds(generator.ElapsedTicks));
					EditorGUILayout.LabelField("Validation Time", FormatStopwatchMilliseconds(generator.validationTicks));
					EditorGUILayout.LabelField("Mesh Processing Time", FormatStopwatchMilliseconds(generator.meshpreprocessTicks));
					EditorGUILayout.LabelField("Begun Events Time", FormatStopwatchMilliseconds(generator.BegunEventsTicks));
					EditorGUILayout.LabelField("Pre Apply Time", FormatStopwatchMilliseconds(generator.preapplyTicks));
					EditorGUILayout.LabelField("Texture Processing Time", FormatStopwatchMilliseconds(generator.textureprocessingTicks));
					EditorGUILayout.LabelField("Successful Mesh Work Time", FormatStopwatchMilliseconds(generator.meshUpdatesTicks));
					EditorGUILayout.LabelField("Skeleton Updates Time", FormatStopwatchMilliseconds(generator.skeletonUpdatesTicks));
					EditorGUILayout.LabelField("Race Blendshapes Time", FormatStopwatchMilliseconds(generator.raceblendshapesTicks));
					EditorGUILayout.LabelField("End Events Time", FormatStopwatchMilliseconds(generator.endEventsTicks));
					EditorGUILayout.LabelField("Average Mesh Time", string.Format("{0:F4} ms", generator.averageMeshUpdatesTime));
					EditorGUILayout.LabelField("Average Texture Time", string.Format("{0:F4} ms", generator.averageTextureProcessingTime));
					EditorGUILayout.LabelField("Average Skeleton Time", string.Format("{0:F4} ms", generator.averageSkeletonUpdatesTime));

					EditorGUILayout.Space(10);
					EditorGUILayout.LabelField("Incremental Compiler Metrics", centeredLabel);
					EditorGUILayout.LabelField("Active Stage",
						string.IsNullOrEmpty(generator.ActiveMultiStepStage)
							? "Idle"
							: generator.ActiveMultiStepStage);
					EditorGUILayout.LabelField(
						"Active Progress",
						string.Format("{0:P1}", generator.ActiveMultiStepProgress));
					EditorGUILayout.LabelField(
						"Last Atomic Step",
						string.Format("{0:F3} ms", generator.lastMultiStepAtomicStepMilliseconds));
					EditorGUILayout.LabelField(
						"Maximum Atomic Step",
						string.Format("{0:F3} ms", generator.maximumMultiStepAtomicStepMilliseconds));
					EditorGUILayout.LabelField(
						"Last Generation Latency",
						FormatStopwatchMilliseconds(generator.lastMultiStepGenerationLatencyTicks));
					EditorGUILayout.LabelField(
						"Maximum Generation Latency",
						FormatStopwatchMilliseconds(generator.maximumMultiStepGenerationLatencyTicks));
					EditorGUILayout.LabelField(
						"Discarded Mesh Work",
						FormatStopwatchMilliseconds(generator.multiStepDiscardedMeshTicks));
					EditorGUILayout.LabelField(
						"Budget Overruns",
						generator.multiStepBudgetOverrunCount.ToString());
					EditorGUILayout.LabelField(
						"Async Waits",
						generator.multiStepWaitingForAsyncCount.ToString());
					EditorGUILayout.LabelField(
						"Restarts",
						generator.multiStepRestartCount.ToString());
					EditorGUILayout.LabelField(
						"Cancellations",
						generator.multiStepCancellationCount.ToString());
					EditorGUILayout.LabelField(
						"Failures",
						generator.multiStepFailureCount.ToString());
				}
				else
				{
					EditorGUILayout.LabelField("Generator Work Time", "N/A");
				}
				

				if (generator != null)
				{
					EditorGUILayout.LabelField("Pending UMAs", string.Format("{0}", generator.pendingUmas));
					EditorGUILayout.LabelField("Shape Dirty", string.Format("{0}", generator.DnaChanged));
					EditorGUILayout.LabelField("Texture Dirty", string.Format("{0}", generator.TextureChanged));
					EditorGUILayout.LabelField("Mesh Dirty", string.Format("{0}", generator.SlotsChanged));
                }

				if (convertRenderTexture != null && convertRenderTexture.boolValue == true)
				{
					EditorGUILayout.Space(10);
					EditorGUILayout.LabelField("Texture Metrics", centeredLabel);
					if (generator != null)
					{
						EditorGUILayout.LabelField("Textures Processed", string.Format("{0}", generator.TexturesProcessed));
					}
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
                }

				if (GUILayout.Button("Reset editor statistics") && generator != null)
				{
					generator.ResetStatistics();
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

                SerializedProperty umaDatasGenerated = serializedObject.FindProperty("umaDatasGenerated");

                if (umaDatasGenerated != null)
                {
					if (GUILayout.Button("Make All UMA's visible in Hierarchy"))
					{
						long numberofUmas = umaDatasGenerated.arraySize;
						for (int i = 0; i < umaDatasGenerated.arraySize; i++)
						{
							SerializedProperty umaDataProp = umaDatasGenerated.GetArrayElementAtIndex(i);
							if (umaDataProp != null)
							{
								UMAData umaData = umaDataProp.objectReferenceValue as UMAData;
								if (umaData != null && umaData.gameObject != null)
								{
									umaData.gameObject.hideFlags = HideFlags.None;
								}
							}
						}
					}
                    EditorGUILayout.PropertyField(umaDatasGenerated);
                }

            }

			if (!EditorApplication.isPlaying)
			{
				if (GUILayout.Button("Rebuild all editor UMA"))
				{
					RebuildAllEditorUMA();
				}
			}
			serializedObject.ApplyModifiedProperties();
		}
	}
}
