using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UMA.CharacterSystem;
using System.Timers;
using System.Collections.Generic;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAGeneratorBuiltin))]
	public class UMAGeneratorBuiltinEditor : UMAGeneratorBaseEditor
	{
		private readonly UMAInspectorView inspectorView =
			new UMAInspectorView(typeof(UMAGeneratorBuiltin));
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
		private readonly List<UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic>
			multiStepBudgetOverrunStatistics =
				new List<UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic>();
		private readonly List<UMAGeneratorBuiltin.MultiStepAtomicStepStatistic>
			multiStepAtomicStepStatistics =
				new List<UMAGeneratorBuiltin.MultiStepAtomicStepStatistic>();

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

		private static string FormatAtomicStep(
			string stepName,
			float milliseconds)
		{
			return string.IsNullOrEmpty(stepName)
				? string.Format("{0:F3} ms", milliseconds)
				: string.Format(
					"{0}: {1:F3} ms",
					stepName,
					milliseconds);
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

			if (!inspectorView.DrawSelector())
			{
				DrawStandardInspector();
				return;
			}

			using (inspectorView.Section("Atlas and conversion",
				"Advanced atlas packing and render-texture conversion controls. These are the complete legacy generator settings; Standard View presents the common subset grouped by workflow."))
				base.OnInspectorGUI();

			serializedObject.Update();
            DrawBoneLifecycle();
			using (inspectorView.Section("Advanced generation and diagnostics",
				"Generation Settings control queue scheduling, Runtime Tuning manages automatic memory scaling, Edit Time Settings constrain editor previews, Advanced Settings expose renderer and combiner internals, and Statistics contains the complete runtime timing and queue report."))
			{

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
						FormatAtomicStep(
							generator.lastMultiStepAtomicStepName,
							generator.lastMultiStepAtomicStepMilliseconds));
					EditorGUILayout.LabelField(
						"Longest Atomic Step",
						FormatAtomicStep(
							generator.maximumMultiStepAtomicStepName,
							generator.maximumMultiStepAtomicStepMilliseconds));
					generator.GetMultiStepAtomicStepStatistics(
						multiStepAtomicStepStatistics);
					if (multiStepAtomicStepStatistics.Count > 0)
					{
						EditorGUILayout.LabelField(
							"Step and Phase Times",
							EditorStyles.miniBoldLabel);
						EditorGUI.indentLevel++;
						foreach (UMAGeneratorBuiltin.MultiStepAtomicStepStatistic
								 statistic in multiStepAtomicStepStatistics)
						{
							EditorGUILayout.LabelField(
								statistic.StepName,
								string.Format(
									"{0}  |  avg {1:F3} ms  |  max {2:F3} ms  |  total {3:F3} ms",
									statistic.Count,
									statistic.AverageMilliseconds,
									statistic.MaximumMilliseconds,
									statistic.TotalMilliseconds));
						}
						EditorGUI.indentLevel--;
					}
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
					if (generator.multiStepBudgetOverrunCount > 0)
					{
						EditorGUILayout.LabelField(
							"Last Budget Overrun",
							string.Format(
								"{0}: {1:F3} ms (+{2:F3} ms)",
								generator.lastMultiStepBudgetOverrunStepName,
								generator.lastMultiStepBudgetOverrunStepMilliseconds,
								generator.lastMultiStepBudgetOverrunAmountMilliseconds));
						generator.GetMultiStepBudgetOverrunStatistics(
							multiStepBudgetOverrunStatistics);
						EditorGUILayout.LabelField(
							"Budget Overrun Steps",
							EditorStyles.miniBoldLabel);
						EditorGUI.indentLevel++;
						foreach (UMAGeneratorBuiltin.MultiStepBudgetOverrunStatistic
								 statistic in multiStepBudgetOverrunStatistics)
						{
							EditorGUILayout.LabelField(
								statistic.StepName,
								string.Format(
									"{0}  |  max {1:F3} ms  |  max +{2:F3} ms",
									statistic.Count,
									statistic.MaximumStepMilliseconds,
									statistic.MaximumOverrunMilliseconds));
						}
						EditorGUI.indentLevel--;
					}
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
					EditorGUILayout.LabelField(
						"Source Validation Cache Hits",
						SkinnedMeshCombinerMeshAPI
							.SourceValidationCacheHits.ToString());
					EditorGUILayout.LabelField(
						"Source Validation Cache Misses",
						SkinnedMeshCombinerMeshAPI
							.SourceValidationCacheMisses.ToString());
					EditorGUILayout.LabelField(
						"Source Validation Cache Bypasses",
						SkinnedMeshCombinerMeshAPI
							.SourceValidationCacheBypasses.ToString());
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
			}
			serializedObject.ApplyModifiedProperties();
		}

        private void DrawBoneLifecycle()
        {
            using (inspectorView.Section("Rig lifetime",
                "Cleanup Unused Bones runs only after a successful build. It retains the effective T-pose, current slot/renderer dependencies and connecting ancestors. UMAIgnore protects a whole subtree. UMAKeepChain preserves identity but does not exempt unused bones. External skeletons are never pruned. Measure Cleanup records CPU time; counters are session-only."))
            {
                inspectorView.Field(serializedObject, "CleanupUnusedBones", "Cleanup Unused Bones");
                inspectorView.Field(serializedObject, "MeasureBoneCleanup", "Measure Cleanup");
                var generator = target as UMAGeneratorBuiltin;
                if (generator != null)
                {
                    EditorGUILayout.LabelField("Cleanup runs / bones removed", $"{generator.BoneCleanupRuns} / {generator.BoneCleanupRemoved}");
                    EditorGUILayout.LabelField("Cleanup last / total (ms)", $"{generator.BoneCleanupLastMilliseconds:F3} / {generator.BoneCleanupTotalMilliseconds:F3}");
                    if (GUILayout.Button("Reset Cleanup Counters"))
                    {
                        generator.BoneCleanupRuns = generator.BoneCleanupRemoved = 0;
                        generator.BoneCleanupLastMilliseconds = generator.BoneCleanupTotalMilliseconds = 0;
                    }
                }
            }
        }

		private void DrawStandardInspector()
		{
			serializedObject.Update();
            DrawBoneLifecycle();
			using (inspectorView.Section("Atlas generation",
				"Atlas Resolution is the maximum generated atlas size. Fit Atlas scales packed content when it exceeds that size. Source UV Cropping removes unused source-texture area when both the slot and overlay permit it. Overflow Fit Method, percentage steps and Sharper Fit control how quality is reduced when content does not fit."))
			{
				inspectorView.Field(serializedObject, "atlasResolution",
					"Atlas Resolution");
				inspectorView.Field(serializedObject, "fitAtlas", "Fit Atlas");
				inspectorView.Field(serializedObject,
					"enableSourceUVCropping", "Source UV Cropping");
				SerializedProperty cropping = inspectorView.Property(
					serializedObject, "enableSourceUVCropping");
				if (cropping != null && cropping.boolValue)
					inspectorView.Field(serializedObject,
						"sourceUVCropPadding", "Crop Padding");
				inspectorView.Field(serializedObject,
					"AtlasOverflowFitMethod", "Overflow Fit Method");
				inspectorView.Field(serializedObject,
					"FitPercentageDecrease", "Fit Reduction Step");
				inspectorView.Field(serializedObject,
					"SharperFitTextures", "Prefer Sharper Fit");
				inspectorView.Field(serializedObject, "convertMipMaps",
					"Generate Mip Maps");
			}
			using (inspectorView.Section("Scheduling",
				"Initial Scale Factor lowers texture work during generation. Iteration Count and Inter-Frame Delay control how work is distributed. Max Multi-Step Work is the desired per-frame CPU budget, while Max Queued Conversions limits outstanding texture readbacks. Process All Pending favors throughput over frame consistency."))
			{
				DrawIfPresent(InitialScaleFactor, "Initial Scale Factor");
				DrawIfPresent(IterationCount, "Iterations Per Frame");
				DrawIfPresent(InterFrameDelay, "Inter-Frame Delay");
				DrawIfPresent(MaxMultiStepWorkMilliseconds,
					"Multi-Step Budget (ms)");
				DrawIfPresent(MaxQueuedConversionsPerFrame,
					"Maximum Queued Conversions");
				DrawIfPresent(processAllPending, "Process All Pending");
				inspectorView.Field(serializedObject, "useAsyncConversion",
					"Asynchronous Texture Conversion");
			}
			using (inspectorView.Section("Memory management",
				"Automatic Scaling increases the atlas scale factor when reported GPU or system memory is below the configured cutoffs. Garbage Collection Rate schedules cleanup after a number of generated avatars; zero disables periodic cleanup. 32-bit buffers support meshes above the 16-bit index limit at a higher memory cost."))
			{
				DrawIfPresent(AutomaticScaling, "Automatic Scaling");
				if (AutomaticScaling != null && AutomaticScaling.boolValue)
				{
					DrawIfPresent(ScaleGPUMemoryCutoffMB,
						"GPU Memory Cutoff (MB)");
					DrawIfPresent(ScaleSystemMemoryCutoffMB,
						"System Memory Cutoff (MB)");
				}
				DrawIfPresent(collectGarbage, "Collect Garbage");
				DrawIfPresent(garbageCollectionRate,
					"Garbage Collection Rate");
				DrawIfPresent(Use32BitBuffers, "Use 32-bit Mesh Buffers");
			}
			using (inspectorView.Section("Edit-time preview",
				"Editor Atlas Resolution and Editor Initial Scale Factor keep scene previews responsive and scene files manageable. Rebuild All Editor UMA regenerates avatars that have editor-time generation enabled."))
			{
				DrawIfPresent(editorAtlasResolution,
					"Editor Atlas Resolution");
				DrawIfPresent(EditorInitialScaleFactor,
					"Editor Initial Scale Factor");
				DrawIfPresent(showInHierarchy,
					"Show Generated Objects In Hierarchy");
				if (!EditorApplication.isPlaying &&
					GUILayout.Button("Rebuild All Editor UMA"))
					RebuildAllEditorUMA();
			}
			using (inspectorView.Section("Diagnostics",
				"These counters summarize current generator load and the slowest incremental step. Advanced View exposes the complete timing table, conversion counters, renderer defaults and low-level generator references."))
			{
				UMAGeneratorBuiltin generator = target as UMAGeneratorBuiltin;
				if (generator != null)
				{
					EditorGUILayout.LabelField("Pending Avatars",
						generator.pendingUmas.ToString());
					EditorGUILayout.LabelField("Active Stage",
						string.IsNullOrEmpty(generator.ActiveMultiStepStage)
							? "Idle" : generator.ActiveMultiStepStage);
					EditorGUILayout.LabelField("Longest Atomic Step",
						FormatAtomicStep(
							generator.maximumMultiStepAtomicStepName,
							generator.maximumMultiStepAtomicStepMilliseconds));
					EditorGUILayout.LabelField("Generator Work",
						FormatStopwatchMilliseconds(generator.ElapsedTicks));
					if (GUILayout.Button("Reset Statistics"))
						generator.ResetStatistics();
				}
			}
			serializedObject.ApplyModifiedProperties();
		}
	}
}
