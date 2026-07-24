#define DEBUG_TIMING
#define UMA_DEBUG
using System;
using UnityEngine;
using System.Collections.Generic;
using Unity.Profiling;
using static UMA.DNAInstanceCollection;

namespace UMA
{
	/// <summary>
	/// Default UMA character generator.
	/// </summary>
	public abstract class UMAGeneratorBuiltin : UMAGeneratorBase
	{
        private static readonly ProfilerMarker MultiStepAtomicStepMarker =
            new ProfilerMarker("UMA.Generator.MultiStep.AtomicStep");
        private static readonly ProfilerMarker MultiStepFinalizeMarker =
            new ProfilerMarker("UMA.Generator.MultiStep.Finalize");
        private static readonly ProfilerMarker MultiStepCancellationMarker =
            new ProfilerMarker("UMA.Generator.MultiStep.CancelOrRestart");

		[NonSerialized]
		protected UMAData umaData;

        protected List<UMAData> umaDirtyList
		{
			get
			{
				return UMAAssetIndexer.Instance.dirtyList;
			}
		}


        private LinkedList<UMAData> cleanUmas = new LinkedList<UMAData>();
		private LinkedList<UMAData> dirtyUmas = new LinkedList<UMAData>();
		public UMAMeshCombiner meshCombiner;
		private HashSet<string> raceNames;

        private sealed class MultiStepGenerationState
        {
            public UMAData Data;
            public IUMAMeshCombineOperation MeshOperation;
            public UMAMeshCombiner MeshCombiner;
            public UMAData.GeneratedMaterials PreviousGeneratedMaterials;
            public bool HasPreservedGeneratedMaterials;
            public bool PreviousNeedsMaterialClear;
            public RenderTexture PreviousActiveRenderTexture;
            public bool HasRenderTextureBackup;
            public RaceData Race;
            public bool FireEvents;
            public long MeshStepTicks;
            public bool MeshCompletionAccounted;
            public uint RequestVersion;
            public bool RestartRequested;
            public bool DiscardRequested;
            public bool CancellationIssued;
            public long StartTimestamp;
        }

        [NonSerialized]
        private MultiStepGenerationState activeMultiStepGeneration;
        [NonSerialized]
        private UMAMeshCombineTimeSlice scheduledMultiStepTimeSlice;
        [NonSerialized]
        private bool hasScheduledMultiStepTimeSlice;
        [NonSerialized]
        private UMAMeshCombineStatus lastMultiStepStatus = UMAMeshCombineStatus.Completed;

		public enum FlipDecalMode
		{
			Auto,
			Always,
			Never
		}
		public FlipDecalMode flipDecalMode;

		/// <summary>
		/// 
		/// </summary>
		[Range(1.0f, 16.0f)]
		[Tooltip("Increase scale factor to decrease texture usage. A value of 1 means the textures will not be downsampled. Values greater than 1 will result in texture savings. The size of the texture is divided by this value.")]
        public int InitialScaleFactor = 1;

        [Tooltip("Automatically adjust Atlas size based on available memory.")]
        public bool AutomaticScaling = false;
        [Tooltip("Scale down textures if GPU memory is below this value (in MB).")]
        public float ScaleGPUMemoryCutoffMB = 1024.0f;
        [Tooltip("Scale down textures if system memory is below this value (in MB).")]
        public float ScaleSystemMemoryCutoffMB = 16384.0f;

        [Range(1.0f,16.0f)]
		[Tooltip("Scale factor for edit-time builds. Increase scale factor to decrease texture usage. A value of 1 means the textures will not be downsampled. Values greater than 1 will result in texture savings. The size of the texture is divided by this value.")]
		public int editorInitialScaleFactor = 4;

		[Tooltip("Number of iterations to process each frame")]
		public int IterationCount = 1;

		[Min(0)]
		[Tooltip("Number of complete frames to wait before processing the next UMA. Values above zero limit generation to one UMA per eligible frame.")]
		public int InterFrameDelay = 0;

        [Min(0f)]
        [Tooltip("Soft main-thread budget, in milliseconds, for incremental mesh-combiner work during one Work call. Zero means unlimited. The current atomic step is allowed to finish before the generator yields.")]
        public float MaxMultiStepWorkMilliseconds = 2.0f;

		[NonSerialized]
		private int interFrameDelayRemaining;

		[Tooltip("Enable Process All Pending to force the generate to process all pending UMA during the next frame")]
		public bool processAllPending = false;

		[Tooltip("When enable, the texture will be applied right away during the conversion process")]
		public bool applyInline = false;

		[Tooltip("When true, the generator is visible in the hierarchy. useful for debugging.")]
		public bool showInHierarchy = false;

        private int forceGarbageCollect;
        /// <summary>
        /// Number of character updates before triggering System garbage collect.
        /// </summary>
        [Tooltip("Number of character updates before triggering garbage collection.")]
		[Range(0.0f, 128.0f)]
		public int garbageCollectionRate = 0;

		public bool collectGarbage = true;
		private System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
        private System.Diagnostics.Stopwatch buildStopWatch = new System.Diagnostics.Stopwatch();

        

		[Tooltip("Automatically set blendshapes based on race")]
		public bool autoSetRaceBlendshapes = false;

		[Tooltip("Allow read on generated mesh data. Will increase memory usage.")]
		public bool AllowReadFromMesh = false;

		[NonSerialized]
		public long ElapsedTicks;
		[NonSerialized]
		public long DnaChanged;
		[NonSerialized]
		public long TextureChanged;
		[NonSerialized]
		public long SlotsChanged;
		[NonSerialized]
		public long TexturesProcessed;
		[NonSerialized]
		public long pendingUmas;

        [NonSerialized]
        public long validationTicks;
        [NonSerialized]
        public long meshpreprocessTicks;
        [NonSerialized]
        public long BegunEventsTicks;
        [NonSerialized]
        public long preapplyTicks;
        [NonSerialized]
        public long textureprocessingTicks;
        [NonSerialized]
        public long meshUpdatesTicks;
        [NonSerialized]
        public long skeletonUpdatesTicks;
        [NonSerialized]
        public long raceblendshapesTicks;
        [NonSerialized]
        public long endEventsTicks;
        [NonSerialized]
        public long multiStepBudgetOverrunCount;
        [NonSerialized]
        public float lastMultiStepAtomicStepMilliseconds;
        [NonSerialized]
        public float maximumMultiStepAtomicStepMilliseconds;
        [NonSerialized]
        public long multiStepWaitingForAsyncCount;
        [NonSerialized]
        public long multiStepRestartCount;
        [NonSerialized]
        public long multiStepCancellationCount;
        [NonSerialized]
        public long multiStepFailureCount;
        [NonSerialized]
        public long lastMultiStepGenerationLatencyTicks;
        [NonSerialized]
        public long maximumMultiStepGenerationLatencyTicks;

        public string ActiveMultiStepStage =>
            activeMultiStepGeneration?.MeshOperation?.StageName ??
            (activeMultiStepGeneration != null ? "Generator Finalization" : string.Empty);

        public float ActiveMultiStepProgress =>
            activeMultiStepGeneration?.MeshOperation?.Progress ?? 0f;

        public float averageTextureProcessingTime
        {
            get
            {
                if (TextureChanged > 0)
                {
                    return (textureprocessingTicks / (float)TextureChanged) * 1000.0f / System.Diagnostics.Stopwatch.Frequency;
                }
                return 0.0f;
            }
        }

        public float averageMeshUpdatesTime
        {
            get
            {
                if (SlotsChanged > 0)
                {
                    return (meshUpdatesTicks / (float)SlotsChanged) * 1000.0f / System.Diagnostics.Stopwatch.Frequency;
                }
                return 0.0f;
            }
        }
        public float averageSkeletonUpdatesTime
        {
            get
            {
                if (DnaChanged > 0)
                {
                    return (skeletonUpdatesTicks / (float)DnaChanged) * 1000.0f / System.Diagnostics.Stopwatch.Frequency;
                }
                return 0.0f;
            }
        }

        public virtual void Awake()
		{
            if (AutomaticScaling)
            {
                if (SystemInfo.systemMemorySize < ScaleSystemMemoryCutoffMB || SystemInfo.graphicsMemorySize < ScaleGPUMemoryCutoffMB)
                {
                    InitialScaleFactor *= 2;
                    atlasResolution /= 2;
                    Debug.Log($"UMAGeneratorBuiltin: Automatic scaling applied. New InitialScaleFactor: {InitialScaleFactor}, New atlasResolution: {atlasResolution} System Memory: {SystemInfo.systemMemorySize} MB, GPU Memory: {SystemInfo.graphicsMemorySize} MB");
                }
                // don't do this again.
                AutomaticScaling = false;
            }
            if (atlasResolution == 0)
            {
                atlasResolution = 256;
            }

            if (defaultOverlayAsset != null)
            {
                _defaultOverlayData = new OverlayData (defaultOverlayAsset);
            }

            if (!textureMerge)
			{
				if (Debug.isDebugBuild)
                {
                    Debug.LogError("No TextureMerge set!");
                }
            }

			//Garbage Collection hack
			var mb = (System.GC.GetTotalMemory(false) / (1024 * 1024));
			if (mb < 10)
			{
				byte[] data = new byte[10 * 1024 * 1024];
				data[0] = 0;
				data[10 * 1024 * 1024 - 1] = 0;
			}
		}

		public virtual void Update()
        {
            if (CheckRenderTextures())
            {
                return; // if render textures needs rebuild we'll not do anything else
            }

			Work();
		}

		private bool CheckRenderTextures()
		{
			var rt = FindRenderTexture();
			if (rt != null && !rt.IsCreated())
			{
				RebuildAllRenderTextures();
				return true;
			}
			return false;
		}


		private RenderTexture FindRenderTexture()
		{
			var iteratorNode = cleanUmas.First;
			while (iteratorNode != null)
			{
				var rt = iteratorNode.Value.GetFirstRenderTexture();
				if (rt != null)
                {
                    return rt;
                }

                iteratorNode = iteratorNode.Next;
			}
			return null;
		}

		public override void Work()
		{
			RenderTexToCPU.ApplyInline = applyInline;

			int configuredInterFrameDelay = Mathf.Max(0, InterFrameDelay);
			if (configuredInterFrameDelay == 0)
			{
				interFrameDelayRemaining = 0;
			}

			bool waitingForInterFrameDelay = interFrameDelayRemaining > 0;
			if (waitingForInterFrameDelay)
			{
				interFrameDelayRemaining--;
			}

            // InterFrameDelay gates starting the next UMA. An active multi-step
            // operation must continue to be polled so worker jobs and staged
            // resources are not stranded between eligible start frames.
            if (!IsIdle() && (!waitingForInterFrameDelay || activeMultiStepGeneration != null))
			{
                // forceGarbageCollect is incremented every time the mesh/rig is built.
                // it does not increment on texture changes or rig adjustments.
                // the choice used to be "collect garbage, or build an UMA"
                // but collection is so cheap, we should just collect first
                // and then go ahead and build the same frame
                if (forceGarbageCollect > garbageCollectionRate)
                {
                    // TODO: Test this on IOS when I get it building. 
                    // GC.Collect(0, GCCollectionMode.Forced, true, true);
#if !UNITY_EDITOR
                    GC.Collect();
#endif
                    forceGarbageCollect = 0;
                }

                stopWatch.Reset();
				stopWatch.Start();
				int count = IterationCount;

				// If processAllPending is set, process as many are in the queue right now.
				// We get the count (and multiply by two for slow gen) in case bad events add more items to the queue.
				if (processAllPending)
				{
					count = Mathf.Max(
                        activeMultiStepGeneration != null ? 1 : 0,
                        umaDirtyList.Count);
                }
				pendingUmas = umaDirtyList.Count;

				if (hasPendingUMAS())
				{
                    UMAMeshCombineTimeSlice multiStepTimeSlice = CreateMultiStepTimeSlice();
					for (int i = 0; i < count; i++)
					{
                        // A shared slice covers every incremental operation
                        // advanced by this Work call. processAllPending and
                        // IterationCount may increase throughput, but never
                        // replace the configured deadline.
                        if (activeMultiStepGeneration == null &&
                            meshCombiner is IUMAMultiStepMeshCombiner &&
                            multiStepTimeSlice.IsExpired)
                        {
                            break;
                        }

                        RunDirtyUpdate(multiStepTimeSlice);
                        while (activeMultiStepGeneration != null &&
                               lastMultiStepStatus == UMAMeshCombineStatus.InProgress &&
                               !multiStepTimeSlice.IsExpired)
                        {
                            lastMultiStepStatus = ContinueDirtyUpdate(multiStepTimeSlice);
                        }

                        if (activeMultiStepGeneration != null)
                        {
                            // WaitingForAsync always yields. InProgress yields
                            // only after the shared soft deadline is exhausted.
                            break;
                        }
                        if (meshCombiner is IUMAMultiStepMeshCombiner &&
                            multiStepTimeSlice.IsExpired)
                        {
                            break;
                        }
						if (configuredInterFrameDelay > 0)
						{
							interFrameDelayRemaining = configuredInterFrameDelay;
							break;
						}
						if (IsIdle())
						{
							break;
						}
					}
				}

                ElapsedTicks += stopWatch.ElapsedTicks;
#if UNITY_EDITOR
				UnityEditor.EditorUtility.SetDirty(this);
#endif
				stopWatch.Stop();
				UMATime.ReportTimeSpendtThisFrameTicks(stopWatch.ElapsedTicks);
            }
            if (RenderTexToCPU.PendingCopies() > 0)
            {
				stopWatch.Start();
                RenderTexToCPU.ApplyQueuedCopies(MaxQueuedConversionsPerFrame);
                TexturesProcessed += MaxQueuedConversionsPerFrame > RenderTexToCPU.PendingCopies() ? RenderTexToCPU.PendingCopies() : MaxQueuedConversionsPerFrame;
				stopWatch.Stop();
                ElapsedTicks += stopWatch.ElapsedTicks;
            }
        }

#pragma warning disable 618
		public void RebuildAllRenderTextures()
		{
			var activeUmaData = umaData;
			var iteratorNode = cleanUmas.First;
			while (iteratorNode != null)
			{
				RebuildRenderTexture(iteratorNode.Value);
				iteratorNode = iteratorNode.Next;
			}

			umaData = activeUmaData;
		}

		private void RebuildRenderTexture(UMAData data)
		{
			var rt = data.GetFirstRenderTexture();
			if (rt != null && !rt.IsCreated())
			{
				UMAGeneratorPro ugp = new UMAGeneratorPro();
				ugp.ProcessTexture(this, umaData, true, InitialScaleFactor);
				TextureChanged++;
			}
		}

		public void SaveMountedItems(UMAData umaData)
        {
			if (!SaveAndRestoreIgnoredItems)
            {
                return;
            }
			umaData.SaveMountedItems();
        }

         private void CacheDefaultOverlayMaterial(UMAData data)
            {
                if (data == null || data.umaRecipe == null || data.umaRecipe.slotDataList == null)
                {
                    return;
                }

                UMAMaterial defaultMaterial = null;
                if (defaultOverlayAsset != null)
                {
                    defaultMaterial = defaultOverlayAsset.GetMaterial();
                }

                for (int i = 0; i < data.umaRecipe.slotDataList.Length; i++)
                {
                    var slot = data.umaRecipe.slotDataList[i];
                    if (slot != null)
                    {
                        slot.CacheDefaultOverlayMaterial(defaultMaterial);
                    }
                }
            }

        public bool GenerateTexturesOnly(UMAData data, bool fireEvents)
        {
            Debug.Log("GenerateTexturesOnly");
            if (data == null)
            {
                return true;
            }

            umaData = data;
            CacheDefaultOverlayMaterial(umaData);


            if (!umaData.Validate())
            {
                return true;
            }

            RenderTexture rbackup = RenderTexture.active;

            umaData.FireCharacterBegunEvents();

            if (!umaData.rawAvatar)
            {
                PreApply(umaData);
            }

            UMAGeneratorPro ugp = new UMAGeneratorPro();
            ugp.ProcessTexture(this, umaData, !umaData.isMeshDirty, InitialScaleFactor);
            umaData.isAtlasDirty |= umaData.isMeshDirty;
            TextureChanged++;

            RenderTexture.active = rbackup;

            umaData.dirty = false;
            if (fireEvents)
            {
                UMAReady();
            }
            else
            {
                umaData.Show();
            }
            FreezeTime = false;
            return true;
        }
#if UMA_DEBUG
        public List<UMAData> umaDatasGenerated = new List<UMAData>();
#endif

        public bool GenerateSingleUMA(UMAData data, bool fireEvents)
		{
#if UMA_DEBUG
            if (!umaDatasGenerated.Contains(data))
            {
                umaDatasGenerated.Add(data);
            }
#endif
#if DEBUG_TIMING
            System.Diagnostics.Stopwatch gstopWatch = System.Diagnostics.Stopwatch.StartNew();
            gstopWatch.Start();
#endif
            if (data == null)
            {
                return true;
            }

			FreezeTime = true;
			umaData = data;
            CacheDefaultOverlayMaterial(umaData);


            if (umaData.RebuildSkeletonThisBuild)
			{
				if (umaData.umaRoot != null)
                {
                    SaveMountedItems(umaData);
                }
                DestroyImmediate(umaData.umaRoot, false);
				umaData.umaRoot = null;
				umaData.RebuildSkeletonThisBuild = false;
				umaData.isShapeDirty = true;
			}
            if (!umaData.Validate())
            {
                return true;
            }
			if(umaData.isTextureDirty && umaData.needsMaterialClear) {
                Debug.Log("Cleaning Textures and Generated Materials for UMAData");
                umaData.CleanTextures();
				umaData.generatedMaterials = new UMAData.GeneratedMaterials();
				umaData.needsMaterialClear = false;
			}
#if DEBUG_TIMING
            long validation = gstopWatch.ElapsedTicks;
            validationTicks += validation;
            gstopWatch.Restart();
#endif
            RenderTexture rbackup = RenderTexture.active;

			if (meshCombiner != null)
			{
				meshCombiner.Preprocess(umaData);
			}

#if DEBUG_TIMING
            long meshpreprocess = gstopWatch.ElapsedTicks;
            meshpreprocessTicks += meshpreprocess;
            gstopWatch.Restart();
#endif
            umaData.FireCharacterBegunEvents();

#if DEBUG_TIMING
            long BegunEvents = gstopWatch.ElapsedTicks;
            BegunEventsTicks += BegunEvents;
            gstopWatch.Restart();
#endif
            if (!umaData.rawAvatar)
			{
                PreApply(umaData);
            }

#if DEBUG_TIMING
            long preapply = gstopWatch.ElapsedTicks;
            preapplyTicks += preapply;
            gstopWatch.Restart();
#endif
            RaceData race = umaData.umaRecipe.raceData;
            if (race.useNewDNA)
            {
                DNABuildType dnaUpdateFlags = umaData.NewDNAPreApply();
            }

            if (umaData.isTextureDirty)
			{
                UMAGeneratorPro ugp = new UMAGeneratorPro();
				ugp.ProcessTexture(this, umaData, !umaData.isMeshDirty, InitialScaleFactor);
				umaData.isTextureDirty = false;
				umaData.isAtlasDirty |= umaData.isMeshDirty;
				TextureChanged++;
            }

#if DEBUG_TIMING
            long textureprocessing = gstopWatch.ElapsedTicks;
            textureprocessingTicks += textureprocessing;
            gstopWatch.Restart();
#endif
            if (umaData.isMeshDirty)
            {
                umaData.force32bit = Calculate32bitness();
                UpdateUMAMesh(umaData.isAtlasDirty);
                umaData.isAtlasDirty = false;
                umaData.isMeshDirty = false;
                SlotsChanged++;
                forceGarbageCollect++;
            }
#if DEBUG_TIMING
            long meshUpdates = gstopWatch.ElapsedTicks;
            meshUpdatesTicks += meshUpdates;
            gstopWatch.Restart();
#endif

            if (umaData.isShapeDirty)
			{
                UpdateUMABody(umaData);
				umaData.isShapeDirty = false;
				DnaChanged++; 
            }
			else if (umaData.skeleton.isUpdating)
			{
                umaData.skeleton.EndSkeletonUpdate();
            }
#if DEBUG_TIMING
            long skeletonUpdates = gstopWatch.ElapsedTicks;
            skeletonUpdatesTicks += skeletonUpdates;
            gstopWatch.Restart();
#endif

			/* here, set any race specific blendshapes */
			SkinnedMeshRenderer[] renderers = umaData.GetRenderers();
		
			if (autoSetRaceBlendshapes)
			{
                if (raceNames == null)
				{
					RaceData[] races = UMAAssetIndexer.Instance.GetAllRaces();
					raceNames = new HashSet<string>();
                    for (int i = 0; i < races.Length; i++)
					{
                        RaceData r = races[i];
                        raceNames.Add(r.raceName);
					}
				}

				if (raceNames != null && raceNames.Count > 0)
				{
                    for (int i1 = 0; i1 < renderers.Length; i1++)
					{
                        SkinnedMeshRenderer smr = renderers[i1];
                        if (smr.sharedMesh.blendShapeCount > 0)
						{
							for (int i = 0; i < smr.sharedMesh.blendShapeCount;i++)
							{
								string currentBlendshape = smr.sharedMesh.GetBlendShapeName(i);
								if (currentBlendshape == umaData.umaRecipe.raceData.raceName)
								{
									smr.SetBlendShapeWeight(i, 1.0f);
								}
								else if (raceNames.Contains(currentBlendshape))
								{
									smr.SetBlendShapeWeight(i, 0.0f);
								}
							}
						}
					}
				}
            }

            // Apply manual renderer bounds if configured on RaceData
            ApplyManualRendererBounds(umaData, renderers);

            
            umaData.SetupEmbeddedPhysics();
#if DEBUG_TIMING
            long raceblendshapes = gstopWatch.ElapsedTicks;
            raceblendshapesTicks += raceblendshapes;
            gstopWatch.Restart();
#endif
            RenderTexture.active = rbackup;

			umaData.dirty = false;
			if (fireEvents)
            {
                UMAReady();
            }
			else
            {
                umaData.Show();
            }
#if DEBUG_TIMING
            long endEvents = gstopWatch.ElapsedTicks;
            endEventsTicks += endEvents;
            gstopWatch.Stop();
#endif
			FreezeTime = false;
#if DUMP_DEBUG_TIMING
            Debug.Log($"GenerateSingleUMA - Validation {ToMS(validation)} ms");
            Debug.Log($"GenerateSingleUMA - Mesh Preprocess {ToMS(meshpreprocess)} ms");
            Debug.Log($"GenerateSingleUMA - Begun Events {ToMS(BegunEvents)} ms");
            Debug.Log($"GenerateSingleUMA - Pre Apply { ToMS(preapply) } ms");
            Debug.Log($"GenerateSingleUMA - texture updates { ToMS(textureprocessing) } ms");
            Debug.Log($"GenerateSingleUMA - mesh Updates { ToMS(meshUpdates) } ms");
            Debug.Log($"GenerateSingleUMA - skeleton Updates { ToMS(skeletonUpdates) } ms");
            Debug.Log($"GenerateSingleUMA - racial blendshapes { ToMS(raceblendshapes) } ms");
            Debug.Log($"GenerateSingleUMA - end Events { ToMS(endEvents) } ms");
            Debug.Log($"Total for last UMA = {ToMS(validation + meshpreprocess + BegunEvents + preapply + textureprocessing+ meshUpdates + skeletonUpdates+raceblendshapes+endEvents)} ms");
            Debug.Log($"Ticks = {System.Diagnostics.Stopwatch.Frequency}");
#endif
            //ValidateMesh(umaData);
            return true;
		}

        /// <summary>
        /// Generates an UMA while controlling whether the rebuilt Animator should
        /// immediately evaluate its current pose. Edit-time partial builds disable
        /// evaluation so humanoid retargeting cannot overwrite the DNA skeleton.
        /// </summary>
        public bool GenerateSingleUMA(UMAData data, bool fireEvents, bool evaluateAnimatorPose)
        {
            bool previousValue = evaluateAnimatorPoseAfterAvatarUpdate;
            evaluateAnimatorPoseAfterAvatarUpdate = evaluateAnimatorPose;
            try
            {
                return GenerateSingleUMA(data, fireEvents);
            }
            finally
            {
                evaluateAnimatorPoseAfterAvatarUpdate = previousValue;
            }
        }

        private void ValidateMesh(UMAData umaData)
        {
            if (umaData == null) return;

            var renderers = umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning("[ValidateMesh] No renderers on " + umaData.name);
                return;
            }

            int errorCount = 0;
            for (int r = 0; r < renderers.Length; r++)
            {
                var smr = renderers[r];
                if (smr == null) continue;
                var mesh = smr.sharedMesh;
                if (mesh == null)
                {
                    Debug.LogWarning($"[ValidateMesh] Renderer {r} has no mesh on {umaData.name}");
                    errorCount++;
                    continue;
                }

                string prefix = $"[ValidateMesh] {umaData.name} R{r} mesh='{mesh.name}'";

                // --- Vertex count ---
                if (mesh.vertexCount == 0)
                {
                    Debug.LogWarning($"{prefix}: vertexCount=0 (empty mesh)");
                    errorCount++;
                    continue;
                }

                // --- Vertices (NaN, Inf, zero-extent) ---
                var verts = mesh.vertices;
                if (verts == null || verts.Length == 0)
                {
                    Debug.LogWarning($"{prefix}: vertices array is null/empty");
                    errorCount++;
                }
                else
                {
                    int nanVerts = 0, infVerts = 0, zeroVerts = 0;
                    Bounds vertBounds = new Bounds(verts[0], Vector3.zero);
                    bool firstValid = !IsBadVector(verts[0]);
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var v = verts[i];
                        if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)) nanVerts++;
                        if (float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)) infVerts++;
                        if (v == Vector3.zero) zeroVerts++;
                        if (!IsBadVector(v))
                        {
                            if (!firstValid) { vertBounds = new Bounds(v, Vector3.zero); firstValid = true; }
                            else vertBounds.Encapsulate(v);
                        }
                    }
                    if (nanVerts > 0) { Debug.LogWarning($"{prefix}: {nanVerts}/{verts.Length} vertices are NaN"); errorCount++; }
                    if (infVerts > 0) { Debug.LogWarning($"{prefix}: {infVerts}/{verts.Length} vertices are Infinity"); errorCount++; }
                    if (!firstValid) { Debug.LogWarning($"{prefix}: ALL vertices are NaN/Inf"); errorCount++; }
                    else if (vertBounds.size.magnitude < 0.0001f)
                    {
                        Debug.LogWarning($"{prefix}: vertex bounds collapsed (extent={vertBounds.size.magnitude:F6}, center={vertBounds.center}) — possible all-zero or degenerate");
                        errorCount++;
                    }
                }

                // --- Normals ---
                var norms = mesh.normals;
                if (norms != null && norms.Length > 0)
                {
                    int nanNorms = 0, zeroNorms = 0;
                    for (int i = 0; i < norms.Length; i++)
                    {
                        var n = norms[i];
                        if (float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z)) nanNorms++;
                        if (n == Vector3.zero) zeroNorms++;
                    }
                    if (nanNorms > 0) { Debug.LogWarning($"{prefix}: {nanNorms}/{norms.Length} normals are NaN"); errorCount++; }
                    if (zeroNorms == norms.Length) { Debug.LogWarning($"{prefix}: ALL normals are zero"); errorCount++; }
                }

                // --- Tangents ---
                var tans = mesh.tangents;
                if (tans != null && tans.Length > 0)
                {
                    int nanTans = 0;
                    for (int i = 0; i < tans.Length; i++)
                    {
                        var t = tans[i];
                        if (float.IsNaN(t.x) || float.IsNaN(t.y) || float.IsNaN(t.z) || float.IsNaN(t.w)) nanTans++;
                    }
                    if (nanTans > 0) { Debug.LogWarning($"{prefix}: {nanTans}/{tans.Length} tangents are NaN"); errorCount++; }
                }

                // --- UVs ---
                var uv = mesh.uv;
                if (uv != null && uv.Length > 0)
                {
                    int nanUV = 0;
                    for (int i = 0; i < uv.Length; i++)
                    {
                        var u = uv[i];
                        if (float.IsNaN(u.x) || float.IsNaN(u.y)) nanUV++;
                    }
                    if (nanUV > 0) { Debug.LogWarning($"{prefix}: {nanUV}/{uv.Length} UVs are NaN"); errorCount++; }
                }

                // --- Bone Weights ---
                var bw = mesh.boneWeights;
                var bwpv = mesh.GetBonesPerVertex();
                if (bwpv != null && bwpv.Length > 0)
                {
                    int zeroWeightVerts = 0;
                    int totalBoneInfluences = 0;
                    for (int i = 0; i < bwpv.Length; i++)
                    {
                        if (bwpv[i] == 0) zeroWeightVerts++;
                        totalBoneInfluences += bwpv[i];
                    }
                    if (zeroWeightVerts > 0)
                        Debug.LogWarning($"{prefix}: {zeroWeightVerts}/{bwpv.Length} vertices have ZERO bone influences");
                    if (totalBoneInfluences == 0)
                    {
                        Debug.LogWarning($"{prefix}: NO bone influences at all — mesh won't skin");
                        errorCount++;
                    }
                }
                else if (bw != null && bw.Length > 0)
                {
                    int zeroWeightVerts = 0;
                    for (int i = 0; i < bw.Length; i++)
                    {
                        var w = bw[i];
                        if (w.weight0 + w.weight1 + w.weight2 + w.weight3 <= 0f) zeroWeightVerts++;
                    }
                    if (zeroWeightVerts > 0)
                        Debug.LogWarning($"{prefix}: {zeroWeightVerts}/{bw.Length} legacy bone weights sum to zero");
                    if (zeroWeightVerts == bw.Length)
                    {
                        Debug.LogWarning($"{prefix}: ALL legacy bone weights are zero — mesh won't skin");
                        errorCount++;
                    }
                }

                // --- Bones / Bind Poses ---
                var bones = smr.bones;
                var bindPoses = mesh.bindposes;
                if (bones == null || bones.Length == 0)
                {
                    Debug.LogWarning($"{prefix}: no bones assigned to renderer");
                    errorCount++;
                }
                if (bindPoses == null || bindPoses.Length == 0)
                {
                    Debug.LogWarning($"{prefix}: no bind poses on mesh");
                    errorCount++;
                }
                else
                {
                    int nanBindPoses = 0;
                    for (int i = 0; i < bindPoses.Length; i++)
                    {
                        var m = bindPoses[i];
                        for (int row = 0; row < 4; row++)
                        {
                            float val = m[row, 0];
                            if (float.IsNaN(val) || float.IsInfinity(val)) { nanBindPoses++; break; }
                        }
                    }
                    if (nanBindPoses > 0) { Debug.LogWarning($"{prefix}: {nanBindPoses}/{bindPoses.Length} bind poses contain NaN/Inf"); errorCount++; }
                }
                if (bones != null && bindPoses != null && bones.Length != bindPoses.Length)
                {
                    Debug.LogWarning($"{prefix}: bone count mismatch — renderer has {bones.Length} bones, mesh has {bindPoses.Length} bind poses");
                    errorCount++;
                }

                // Check for null bones in renderer
                if (bones != null)
                {
                    int nullBones = 0;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bones[i] == null) nullBones++;
                    }
                    if (nullBones > 0)
                    {
                        Debug.LogWarning($"{prefix}: {nullBones}/{bones.Length} bones are NULL on renderer");
                        errorCount++;
                    }
                }

                // --- Triangles / Submeshes ---
                int subMeshCount = mesh.subMeshCount;
                if (subMeshCount == 0)
                {
                    Debug.LogWarning($"{prefix}: subMeshCount=0 (no triangles)");
                    errorCount++;
                }
                else
                {
                    int totalTris = 0;
                    for (int sm = 0; sm < subMeshCount; sm++)
                    {
                        var tris = mesh.GetTriangles(sm);
                        if (tris == null || tris.Length == 0)
                        {
                            Debug.LogWarning($"{prefix}: submesh {sm} has no triangles");
                        }
                        else
                        {
                            totalTris += tris.Length;
                            // Check for out-of-range indices
                            int maxIdx = mesh.vertexCount - 1;
                            int badIdx = 0;
                            for (int t = 0; t < tris.Length; t++)
                            {
                                if (tris[t] < 0 || tris[t] > maxIdx) badIdx++;
                            }
                            if (badIdx > 0)
                            {
                                Debug.LogWarning($"{prefix}: submesh {sm} has {badIdx}/{tris.Length} triangle indices out of range [0,{maxIdx}]");
                                errorCount++;
                            }
                        }
                    }
                    if (totalTris == 0)
                    {
                        Debug.LogWarning($"{prefix}: all submeshes have zero triangles");
                        errorCount++;
                    }
                }
            }

            if (errorCount == 0)
            {
                Debug.Log($"[ValidateMesh] {umaData.name}: PASSED — no issues detected.");
            }
            else
            {
                Debug.LogWarning($"[ValidateMesh] {umaData.name}: {errorCount} issue(s) found.");
            }
        }

        private static bool IsBadVector(Vector3 v)
        {
            return float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z);
        } 


		private static void ApplyManualRendererBounds(UMAData umaData, SkinnedMeshRenderer[] renderers)
        {
            if (umaData == null || umaData.umaRecipe == null || umaData.umaRecipe.raceData == null)
            {
                return;
            }
            var race = umaData.umaRecipe.raceData;
            if (!race.useManualRendererBounds)
            {
                return;
            }
            Vector3 baseExtents = race.manualRendererBounds;
            Vector3 baseCenter = race.manualRendererBoundsCenter;
            if (baseExtents == Vector3.zero && baseCenter == Vector3.zero)
            {
                return; // nothing to apply
            }

            // Scale using the scale from the 'Position' bone if present
            int posHash = UMAUtils.StringToHash("Position");
            Transform posBone = umaData.skeleton != null ? umaData.skeleton.GetBoneTransform(posHash) : null;
            Vector3 scaledExtents = baseExtents;
            Vector3 scaledCenter = baseCenter;
            if (posBone != null)
            {
                scaledExtents = Vector3.Scale(baseExtents, posBone.localScale);
                scaledCenter = Vector3.Scale(baseCenter, posBone.localScale);
            }

            Bounds b = new Bounds(scaledCenter, scaledExtents * 2f);
            for (int i = 0; i < renderers.Length; i++)
            {
                var smr = renderers[i];
                if (smr == null) continue;
                smr.localBounds = b;
            }
        }

		class Calc32
        {
            public int totalVerts=0;
            public int totalTris=0;
            public int totalWeights=0;
        }


        private bool Calculate32bitness()
        {
			
//#if EXPERIMENTAL_PREDICTION_OF_INDEXES
            // this needs to to group them by material if "atlas" type.
			// otherwise, we just check the slot counts individually.
			if (Use32BitBuffers == false)
            {
		
				Dictionary<string,Calc32> endMesh = new Dictionary<string, Calc32>();

                var slotList = umaData.umaRecipe.slotDataList;
                for (int i = 0; i < slotList.Length; i++)
                {
                    SlotData slot = slotList[i];

                    if (slot != null && !UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                    {
						string key = "slot:"+slot.asset.slotName;
                     var slotMaterial = slot.material;
                        if (slotMaterial != null && slotMaterial.materialType == UMAMaterial.MaterialType.Atlas)
						{
                           key = "mat:" + slotMaterial.name;
                        }

                        int submesh = slot.asset.subMeshIndex;
                        if (submesh < 0 || submesh >= slot.asset.meshData.subMeshCount)
                        {
#if UNITY_EDITOR
                            if (Debug.isDebugBuild)
                            {
                                Debug.LogError($"Slot {slot.asset.slotName} has invalid submesh index {submesh}, using 0 instead.", slot.asset);
                            }
#endif
                            // if the submesh index is invalid, use 0
                            submesh = 0;
                        }
                        UMAMeshData meshData = slot.asset.meshData;

                        if (!endMesh.TryGetValue(key, out Calc32 meshDataCalc))
                        {
                            meshDataCalc = new Calc32();
                            endMesh[key] = meshDataCalc;
                        }
						meshDataCalc = endMesh[key];

                        meshDataCalc.totalVerts += meshData.vertexCount;
                        meshDataCalc.totalTris += meshData.submeshes[submesh].GetTriangleCount();
                        meshDataCalc.totalWeights += meshData.ManagedBonesPerVertex.Length;
                    }
                }

				var values = endMesh.Values;
				foreach (var v in values)
				{
					if (v.totalVerts > 65535 || v.totalTris > 65535 || v.totalWeights > 65535)
					{
						return true;
                    }
				}
            }
//#endif
            return Use32BitBuffers;
        }

        int ToMS(long ticks)
        {
            return Convert.ToInt32((ticks * 1000) / System.Diagnostics.Stopwatch.Frequency);
        }

        public void UpdateSlots(UMAData data)
        {
            umaData = data;
            if (meshCombiner != null)
            {
                meshCombiner.Preprocess(umaData);
            }
            PreApply(umaData);
            UpdateUMAMesh(umaData.isAtlasDirty);
            umaData.isMeshDirty = false;

            if (umaData.skeleton.isUpdating)
            {
                umaData.skeleton.EndSkeletonUpdate();
            }
        }


		public virtual void OnDirtyUpdate()
		{
			try
			{
                UMAMeshCombineTimeSlice timeSlice = hasScheduledMultiStepTimeSlice
                    ? scheduledMultiStepTimeSlice
                    : UMAMeshCombineTimeSlice.Unlimited;
                lastMultiStepStatus = activeMultiStepGeneration != null
                    ? ContinueDirtyUpdate(timeSlice)
                    : StartDirtyUpdate(timeSlice);
			}
			catch (Exception ex)
			{
                if (activeMultiStepGeneration != null)
                {
                    FailDirtyUpdate(ex);
                }
                else
                {
                    if (Debug.isDebugBuild)
                    {
                        UnityEngine.Debug.LogException(ex);
                    }

                    if (umaDirtyList.Count > 0)
                    {
                        CompleteDirtyUpdate(umaDirtyList[0]);
                    }
                }
                lastMultiStepStatus = UMAMeshCombineStatus.Failed;
			}
		}

        private void RunDirtyUpdate(UMAMeshCombineTimeSlice timeSlice)
        {
            scheduledMultiStepTimeSlice = timeSlice;
            hasScheduledMultiStepTimeSlice = true;
            lastMultiStepStatus = UMAMeshCombineStatus.Completed;
            try
            {
                // Retain the virtual entry point so existing generator
                // subclasses that customize OnDirtyUpdate keep their behavior.
                OnDirtyUpdate();
            }
            finally
            {
                hasScheduledMultiStepTimeSlice = false;
                scheduledMultiStepTimeSlice = default;
            }
        }

        private UMAMeshCombineTimeSlice CreateMultiStepTimeSlice()
        {
            float configuredBudget = MaxMultiStepWorkMilliseconds;
            if (float.IsNaN(configuredBudget) ||
                float.IsInfinity(configuredBudget) ||
                configuredBudget < 0f)
            {
                configuredBudget = 0f;
            }

            return new UMAMeshCombineTimeSlice(
                configuredBudget,
                GetMultiStepTimestamp,
                GetMultiStepTimestampFrequency());
        }

        /// <summary>
        /// Monotonic timestamp hook used by deterministic scheduler tests.
        /// </summary>
        protected virtual long GetMultiStepTimestamp()
        {
            return System.Diagnostics.Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Frequency for <see cref="GetMultiStepTimestamp"/>.
        /// </summary>
        protected virtual long GetMultiStepTimestampFrequency()
        {
            return System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>
        /// Starts the current dirty UMA. Existing combiners retain the original
        /// synchronous GenerateSingleUMA path. Only an explicitly multi-step
        /// combiner creates resumable generator state.
        /// </summary>
        private UMAMeshCombineStatus StartDirtyUpdate(UMAMeshCombineTimeSlice timeSlice)
        {
            if (umaDirtyList.Count < 1)
            {
                return UMAMeshCombineStatus.Completed;
            }

            UMAData data = umaDirtyList[0];
            uint requestVersion = data != null
                ? data.GenerationRequestVersion
                : 0u;
            if (!(meshCombiner is IUMAMultiStepMeshCombiner multiStepCombiner))
            {
                try
                {
                    GenerateSingleUMA(data, true);
                }
                catch (Exception ex)
                {
                    LogGenerationException(data, ex);
                }

                CompleteDirtyUpdate(data, requestVersion);
                return UMAMeshCombineStatus.Completed;
            }

            var state = new MultiStepGenerationState
            {
                Data = data,
                FireEvents = true,
                MeshCombiner = meshCombiner,
                RequestVersion = requestVersion,
                StartTimestamp =
                    System.Diagnostics.Stopwatch.GetTimestamp()
            };
            activeMultiStepGeneration = state;

            try
            {
                if (!PrepareMultiStepGeneration(state, multiStepCombiner))
                {
                    CompleteDirtyUpdate(data, state.RequestVersion);
                    return UMAMeshCombineStatus.Completed;
                }

                if (state.MeshOperation == null)
                {
                    if (timeSlice.IsExpired)
                    {
                        return UMAMeshCombineStatus.InProgress;
                    }
                    FinishMultiStepGeneration(state);
                    CompleteDirtyUpdate(data, state.RequestVersion);
                    return UMAMeshCombineStatus.Completed;
                }

                if (timeSlice.IsExpired)
                {
                    return UMAMeshCombineStatus.InProgress;
                }

                return ContinueDirtyUpdate(timeSlice);
            }
            catch (Exception ex)
            {
                FailDirtyUpdate(ex);
                return UMAMeshCombineStatus.Failed;
            }
        }

        /// <summary>
        /// Advances one bounded unit of an active mesh operation and performs
        /// post-mesh generation only after the operation completes.
        /// </summary>
        private UMAMeshCombineStatus ContinueDirtyUpdate(UMAMeshCombineTimeSlice timeSlice)
        {
            MultiStepGenerationState state = activeMultiStepGeneration;
            if (state == null)
            {
                return UMAMeshCombineStatus.Completed;
            }

            try
            {
                if (state.DiscardRequested ||
                    state.Data == null)
                {
                    return AdvanceDiscardCancellation(state);
                }

                if (state.RestartRequested ||
                    state.RequestVersion !=
                    state.Data.GenerationRequestVersion ||
                    state.MeshCombiner != meshCombiner)
                {
                    state.RestartRequested = true;
                    return AdvanceRestartCancellation(state);
                }

                IUMAMeshCombineOperation operation = state.MeshOperation;
                if (operation == null)
                {
                    if (timeSlice.IsExpired)
                    {
                        return UMAMeshCombineStatus.InProgress;
                    }
                    FinishMultiStepGeneration(state);
                    CompleteDirtyUpdate(
                        state.Data,
                        state.RequestVersion);
                    return UMAMeshCombineStatus.Completed;
                }

                if (state.MeshCompletionAccounted)
                {
                    if (timeSlice.IsExpired)
                    {
                        return UMAMeshCombineStatus.InProgress;
                    }
                    FinishMultiStepGeneration(state);
                    CompleteDirtyUpdate(
                        state.Data,
                        state.RequestVersion);
                    return UMAMeshCombineStatus.Completed;
                }

                if (timeSlice.IsExpired &&
                    operation.Status != UMAMeshCombineStatus.Completed &&
                    operation.Status != UMAMeshCombineStatus.Failed &&
                    operation.Status != UMAMeshCombineStatus.Cancelled)
                {
                    return UMAMeshCombineStatus.InProgress;
                }

                UMAMeshCombineStepResult result;
                var meshStepStopwatch = System.Diagnostics.Stopwatch.StartNew();
                double remainingMilliseconds = timeSlice.RemainingMilliseconds;
                try
                {
                    using (MultiStepAtomicStepMarker.Auto())
                    {
                        switch (operation.Status)
                        {
                            case UMAMeshCombineStatus.Completed:
                                result = UMAMeshCombineStepResult.Completed();
                                break;
                            case UMAMeshCombineStatus.Failed:
                                result = UMAMeshCombineStepResult.Failed(
                                    operation.Error ??
                                    new InvalidOperationException("The multi-step mesh operation failed without an error."));
                                break;
                            case UMAMeshCombineStatus.Cancelled:
                                result = UMAMeshCombineStepResult.Cancelled();
                                break;
                            default:
                                result = operation.Step(timeSlice);
                                break;
                        }
                    }
                }
                finally
                {
                    meshStepStopwatch.Stop();
                    state.MeshStepTicks += meshStepStopwatch.ElapsedTicks;
                    lastMultiStepAtomicStepMilliseconds =
                        (float)(meshStepStopwatch.ElapsedTicks * 1000d /
                                System.Diagnostics.Stopwatch.Frequency);
                    maximumMultiStepAtomicStepMilliseconds = Mathf.Max(
                        maximumMultiStepAtomicStepMilliseconds,
                        lastMultiStepAtomicStepMilliseconds);
                    if (!double.IsPositiveInfinity(remainingMilliseconds) &&
                        lastMultiStepAtomicStepMilliseconds > remainingMilliseconds)
                    {
                        multiStepBudgetOverrunCount++;
                    }
                }

                switch (result.Status)
                {
                    case UMAMeshCombineStatus.InProgress:
                        return result.Status;
                    case UMAMeshCombineStatus.WaitingForAsync:
                        multiStepWaitingForAsyncCount++;
                        return result.Status;

                    case UMAMeshCombineStatus.Completed:
                        if (state.RestartRequested ||
                            state.RequestVersion !=
                            state.Data.GenerationRequestVersion ||
                            state.MeshCombiner != meshCombiner)
                        {
                            state.RestartRequested = true;
                            return AdvanceRestartCancellation(state);
                        }
                        CommitPreservedGeneratedMaterials(state);
                        AccountForCompletedMultiStepMesh(state);
                        // Generator finalization is part of the completed UMA
                        // transaction. It remains atomic so events and queue
                        // state cannot expose a half-finished avatar.
                        using (MultiStepFinalizeMarker.Auto())
                        {
                            FinishMultiStepGeneration(state);
                        }
                        CompleteDirtyUpdate(
                            state.Data,
                            state.RequestVersion);
                        return UMAMeshCombineStatus.Completed;

                    case UMAMeshCombineStatus.Cancelled:
                        CancelDirtyUpdate();
                        return UMAMeshCombineStatus.Cancelled;

                    case UMAMeshCombineStatus.Failed:
                        throw result.Error ?? operation.Error ??
                              new InvalidOperationException("The multi-step mesh operation failed without an error.");

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception ex)
            {
                FailDirtyUpdate(ex);
                return UMAMeshCombineStatus.Failed;
            }
        }

        private void AccountForCompletedMultiStepMesh(MultiStepGenerationState state)
        {
            if (state.MeshCompletionAccounted)
            {
                return;
            }

            meshUpdatesTicks += state.MeshStepTicks;
            state.Data.isAtlasDirty = false;
            state.Data.isMeshDirty = false;
            SlotsChanged++;
            forceGarbageCollect++;
            state.MeshCompletionAccounted = true;
            long latencyTicks =
                System.Diagnostics.Stopwatch.GetTimestamp() -
                state.StartTimestamp;
            lastMultiStepGenerationLatencyTicks = latencyTicks;
            maximumMultiStepGenerationLatencyTicks = Math.Max(
                maximumMultiStepGenerationLatencyTicks,
                latencyTicks);
        }

        private bool PrepareMultiStepGeneration(
            MultiStepGenerationState state,
            IUMAMultiStepMeshCombiner multiStepCombiner)
        {
            UMAData data = state.Data;
#if UMA_DEBUG
            if (data != null && !umaDatasGenerated.Contains(data))
            {
                umaDatasGenerated.Add(data);
            }
#endif
            if (data == null)
            {
                return false;
            }

            FreezeTime = true;
            umaData = data;
            var stageStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                CacheDefaultOverlayMaterial(data);

                bool hasExistingRenderer = HasExistingRenderer(data);
                if (data.RebuildSkeletonThisBuild && !hasExistingRenderer)
                {
                    if (data.umaRoot != null)
                    {
                        SaveMountedItems(data);
                    }
                    DestroyImmediate(data.umaRoot, false);
                    data.umaRoot = null;
                    data.RebuildSkeletonThisBuild = false;
                    data.isShapeDirty = true;
                }
                else if (data.RebuildSkeletonThisBuild)
                {
                    // A destructive skeleton reset would invalidate the bones
                    // used by the renderer that must remain visible during a
                    // multi-frame rebuild. Retain the current skeleton while
                    // the replacement mesh is staged.
                    data.RebuildSkeletonThisBuild = false;
                    data.isShapeDirty = true;
                }

                if (!data.Validate())
                {
                    return false;
                }

                if (data.isTextureDirty &&
                    data.isMeshDirty &&
                    hasExistingRenderer)
                {
                    PreserveGeneratedMaterials(state);
                }

                if (data.isTextureDirty && data.needsMaterialClear)
                {
                    Debug.Log("Cleaning Textures and Generated Materials for UMAData");
                    data.CleanTextures();
                    data.generatedMaterials = new UMAData.GeneratedMaterials();
                    data.needsMaterialClear = false;
                }

                validationTicks += stageStopwatch.ElapsedTicks;
                stageStopwatch.Restart();

                state.PreviousActiveRenderTexture = RenderTexture.active;
                state.HasRenderTextureBackup = true;

                meshCombiner.Preprocess(data);
                meshpreprocessTicks += stageStopwatch.ElapsedTicks;
                stageStopwatch.Restart();

                data.FireCharacterBegunEvents();
                BegunEventsTicks += stageStopwatch.ElapsedTicks;
                stageStopwatch.Restart();

                if (!data.rawAvatar)
                {
                    PreApply(data);
                }
                preapplyTicks += stageStopwatch.ElapsedTicks;
                stageStopwatch.Restart();

                state.Race = data.umaRecipe.raceData;
                if (state.Race.useNewDNA)
                {
                    data.NewDNAPreApply();
                }

                if (data.isTextureDirty)
                {
                    var textureGenerator = new UMAGeneratorPro();
                    textureGenerator.ProcessTexture(this, data, !data.isMeshDirty, InitialScaleFactor);
                    data.isTextureDirty = false;
                    data.isAtlasDirty |= data.isMeshDirty;
                    TextureChanged++;
                }
                textureprocessingTicks += stageStopwatch.ElapsedTicks;
                stageStopwatch.Restart();

                if (data.isMeshDirty)
                {
                    data.force32bit = Calculate32bitness();
                    state.MeshOperation = multiStepCombiner.BeginUpdateUMAMesh(
                        data.isAtlasDirty,
                        data,
                        atlasResolution);
                    if (state.MeshOperation == null)
                    {
                        throw new InvalidOperationException(
                            $"{meshCombiner.GetType().Name} returned a null multi-step mesh operation.");
                    }
                    state.MeshStepTicks += stageStopwatch.ElapsedTicks;
                }

                return true;
            }
            finally
            {
                // FreezeTime affects animator restoration, not background mesh
                // preparation. Do not leave this generator-global flag enabled
                // while the operation waits across frames.
                FreezeTime = false;
            }
        }

        private void FinishMultiStepGeneration(MultiStepGenerationState state)
        {
            UMAData data = state.Data;
            umaData = data;
            FreezeTime = true;
            var stageStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (data.isShapeDirty)
                {
                    UpdateUMABody(data);
                    data.isShapeDirty = false;
                    DnaChanged++;
                }
                else if (data.skeleton.isUpdating)
                {
                    data.skeleton.EndSkeletonUpdate();
                }
                skeletonUpdatesTicks += stageStopwatch.ElapsedTicks;
                stageStopwatch.Restart();

                SkinnedMeshRenderer[] renderers = data.GetRenderers();
                if (autoSetRaceBlendshapes)
                {
                    if (raceNames == null)
                    {
                        RaceData[] races = UMAAssetIndexer.Instance.GetAllRaces();
                        raceNames = new HashSet<string>();
                        for (int i = 0; i < races.Length; i++)
                        {
                            RaceData race = races[i];
                            raceNames.Add(race.raceName);
                        }
                    }

                    if (raceNames != null && raceNames.Count > 0)
                    {
                        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                        {
                            SkinnedMeshRenderer renderer = renderers[rendererIndex];
                            if (renderer.sharedMesh.blendShapeCount <= 0)
                            {
                                continue;
                            }

                            for (int shapeIndex = 0;
                                 shapeIndex < renderer.sharedMesh.blendShapeCount;
                                 shapeIndex++)
                            {
                                string shapeName = renderer.sharedMesh.GetBlendShapeName(shapeIndex);
                                if (shapeName == state.Race.raceName)
                                {
                                    renderer.SetBlendShapeWeight(shapeIndex, 1.0f);
                                }
                                else if (raceNames.Contains(shapeName))
                                {
                                    renderer.SetBlendShapeWeight(shapeIndex, 0.0f);
                                }
                            }
                        }
                    }
                }

                ApplyManualRendererBounds(data, renderers);
                data.SetupEmbeddedPhysics();
                raceblendshapesTicks += stageStopwatch.ElapsedTicks;
                stageStopwatch.Restart();

                RestoreMultiStepRenderTexture(state);

                data.dirty = false;
                if (state.FireEvents)
                {
                    UMAReady();
                }
                else
                {
                    data.Show();
                }
                endEventsTicks += stageStopwatch.ElapsedTicks;
            }
            finally
            {
                FreezeTime = false;
            }
        }

        private void CompleteDirtyUpdate(UMAData completedData)
        {
            CompleteDirtyUpdate(completedData, null);
        }

        private void CompleteDirtyUpdate(
            UMAData completedData,
            uint? completedRequestVersion)
        {
            MultiStepGenerationState state = activeMultiStepGeneration;
            activeMultiStepGeneration = null;
            try
            {
                state?.MeshOperation?.Dispose();
            }
            catch (Exception ex)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogException(ex);
                }
            }

            bool hasNewerRequest =
                completedData != null &&
                completedRequestVersion.HasValue &&
                completedData.GenerationRequestVersion !=
                completedRequestVersion.Value;
            if (hasNewerRequest)
            {
                completedData.dirty = true;
                if (!umaDirtyList.Contains(completedData))
                {
                    umaDirtyList.Add(completedData);
                }
                completedData.MoveToList(dirtyUmas);
                if (umaData == completedData)
                {
                    umaData = null;
                }
                return;
            }

            if (umaDirtyList.Count > 0 && umaDirtyList[0] == completedData)
            {
                umaDirtyList.RemoveAt(0);
            }
            else
            {
                umaDirtyList.Remove(completedData);
            }

            if (completedData != null)
            {
                completedData.MoveToList(cleanUmas);
            }
            if (umaData == completedData)
            {
                umaData = null;
            }
        }

        private UMAMeshCombineStatus AdvanceRestartCancellation(
            MultiStepGenerationState state)
        {
            using (MultiStepCancellationMarker.Auto())
            {
                return AdvanceCancellationInternal(state, true);
            }
        }

        private UMAMeshCombineStatus AdvanceDiscardCancellation(
            MultiStepGenerationState state)
        {
            using (MultiStepCancellationMarker.Auto())
            {
                return AdvanceCancellationInternal(state, false);
            }
        }

        private UMAMeshCombineStatus AdvanceCancellationInternal(
            MultiStepGenerationState state,
            bool restart)
        {
            if (restart)
            {
                return AdvanceRestartCancellationInternal(state);
            }
            return AdvanceDiscardCancellationInternal(state);
        }

        private UMAMeshCombineStatus AdvanceRestartCancellationInternal(
            MultiStepGenerationState state)
        {
            UMAMeshCombineStatus status =
                PollCancellation(state);
            if (status == UMAMeshCombineStatus.InProgress ||
                status == UMAMeshCombineStatus.WaitingForAsync)
            {
                return status;
            }

            RestartDirtyUpdate(state);
            return UMAMeshCombineStatus.Cancelled;
        }

        private UMAMeshCombineStatus AdvanceDiscardCancellationInternal(
            MultiStepGenerationState state)
        {
            UMAMeshCombineStatus status =
                PollCancellation(state);
            if (status == UMAMeshCombineStatus.InProgress ||
                status == UMAMeshCombineStatus.WaitingForAsync)
            {
                return status;
            }

            FinishDiscardedDirtyUpdate(state);
            return UMAMeshCombineStatus.Cancelled;
        }

        private static UMAMeshCombineStatus PollCancellation(
            MultiStepGenerationState state)
        {
            IUMAMeshCombineOperation operation = state.MeshOperation;
            if (operation == null)
            {
                return UMAMeshCombineStatus.Cancelled;
            }

            if (!state.CancellationIssued)
            {
                operation.Cancel();
                state.CancellationIssued = true;
            }

            UMAMeshCombineStatus status = operation.Status;
            if (status != UMAMeshCombineStatus.Cancelled &&
                status != UMAMeshCombineStatus.Completed &&
                status != UMAMeshCombineStatus.Failed)
            {
                UMAMeshCombineStepResult result =
                    operation.Step(UMAMeshCombineTimeSlice.Unlimited);
                status = result.Status;
                if (status == UMAMeshCombineStatus.Failed)
                {
                    throw result.Error ?? operation.Error ??
                        new InvalidOperationException(
                            "The stale multi-step mesh operation failed while cancelling.");
                }
            }

            if (status == UMAMeshCombineStatus.Failed)
            {
                throw operation.Error ??
                    new InvalidOperationException(
                        "The multi-step mesh operation failed while cancelling.");
            }
            return status;
        }

        private void RestartDirtyUpdate(MultiStepGenerationState state)
        {
            multiStepRestartCount++;
            activeMultiStepGeneration = null;
            try
            {
                state.MeshOperation?.Dispose();
            }
            catch (Exception ex)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogException(ex);
                }
            }

            RollbackPreservedGeneratedMaterials(state);
            RestoreMultiStepRenderTexture(state);
            FreezeTime = false;

            if (state.Data != null)
            {
                state.Data.dirty = true;
                if (!umaDirtyList.Contains(state.Data))
                {
                    umaDirtyList.Add(state.Data);
                }
                state.Data.MoveToList(dirtyUmas);
            }
            if (umaData == state.Data)
            {
                umaData = null;
            }
        }

        private void FinishDiscardedDirtyUpdate(
            MultiStepGenerationState state)
        {
            multiStepCancellationCount++;
            activeMultiStepGeneration = null;
            try
            {
                state.MeshOperation?.Dispose();
            }
            catch (Exception ex)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogException(ex);
                }
            }

            RollbackPreservedGeneratedMaterials(state);
            RestoreMultiStepRenderTexture(state);
            FreezeTime = false;
            umaDirtyList.Remove(state.Data);
            if (umaData == state.Data)
            {
                umaData = null;
            }
        }

        private void FailDirtyUpdate(Exception exception)
        {
            multiStepFailureCount++;
            MultiStepGenerationState state = activeMultiStepGeneration;
            UMAData failedData = state?.Data;
            try
            {
                state?.MeshOperation?.Cancel();
            }
            catch (Exception cancellationException)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogException(cancellationException);
                }
            }

            RestoreMultiStepRenderTexture(state);
            FreezeTime = false;
            LogGenerationException(failedData, exception);
            CompleteDirtyUpdate(
                failedData,
                state != null ? state.RequestVersion : (uint?)null);
            RollbackPreservedGeneratedMaterials(state);
        }

        private void CancelDirtyUpdate()
        {
            CancelActiveDirtyUpdate(true);
        }

        private void CancelActiveDirtyUpdate(bool moveToCleanList)
        {
            MultiStepGenerationState state = activeMultiStepGeneration;
            if (state == null)
            {
                return;
            }

            multiStepCancellationCount++;
            activeMultiStepGeneration = null;
            try
            {
                state.MeshOperation?.Cancel();
                state.MeshOperation?.Dispose();
            }
            catch (Exception ex)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogException(ex);
                }
            }

            RollbackPreservedGeneratedMaterials(state);
            RestoreMultiStepRenderTexture(state);
            FreezeTime = false;

            if (umaDirtyList.Count > 0 && umaDirtyList[0] == state.Data)
            {
                umaDirtyList.RemoveAt(0);
            }
            else
            {
                umaDirtyList.Remove(state.Data);
            }

            if (moveToCleanList && state.Data != null)
            {
                state.Data.MoveToList(cleanUmas);
            }
            if (umaData == state.Data)
            {
                umaData = null;
            }
        }

        private static void RestoreMultiStepRenderTexture(MultiStepGenerationState state)
        {
            if (state == null || !state.HasRenderTextureBackup)
            {
                return;
            }

            RenderTexture.active = state.PreviousActiveRenderTexture;
            state.HasRenderTextureBackup = false;
        }

        private static bool HasExistingRenderer(UMAData data)
        {
            if (data == null)
            {
                return false;
            }

            SkinnedMeshRenderer[] renderers = data.GetRenderers();
            if (renderers == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null &&
                    renderers[i].sharedMesh != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static void PreserveGeneratedMaterials(
            MultiStepGenerationState state)
        {
            if (state == null ||
                state.HasPreservedGeneratedMaterials ||
                state.Data == null)
            {
                return;
            }

            UMAData data = state.Data;
            state.PreviousGeneratedMaterials =
                data.generatedMaterials ??
                new UMAData.GeneratedMaterials();
            state.PreviousNeedsMaterialClear =
                data.needsMaterialClear;
            state.HasPreservedGeneratedMaterials = true;
            data.generatedMaterials =
                new UMAData.GeneratedMaterials();
        }

        private static void CommitPreservedGeneratedMaterials(
            MultiStepGenerationState state)
        {
            if (state == null ||
                !state.HasPreservedGeneratedMaterials)
            {
                return;
            }

            UMAData data = state.Data;
            UMAData.GeneratedMaterials previous =
                state.PreviousGeneratedMaterials;
            state.PreviousGeneratedMaterials = null;
            state.HasPreservedGeneratedMaterials = false;

            CleanDetachedGeneratedMaterials(data, previous);
        }

        private static void RollbackPreservedGeneratedMaterials(
            MultiStepGenerationState state)
        {
            if (state == null ||
                !state.HasPreservedGeneratedMaterials)
            {
                return;
            }

            UMAData data = state.Data;
            UMAData.GeneratedMaterials generatedDuringBuild =
                data != null ? data.generatedMaterials : null;
            if (data != null)
            {
                data.generatedMaterials =
                    state.PreviousGeneratedMaterials ??
                    new UMAData.GeneratedMaterials();
                data.needsMaterialClear =
                    state.PreviousNeedsMaterialClear;
            }

            state.PreviousGeneratedMaterials = null;
            state.HasPreservedGeneratedMaterials = false;
            CleanDetachedGeneratedMaterials(
                data,
                generatedDuringBuild);
        }

        private static void CleanDetachedGeneratedMaterials(
            UMAData data,
            UMAData.GeneratedMaterials materials)
        {
            if (data == null ||
                materials == null ||
                materials == data.generatedMaterials)
            {
                return;
            }

            UMAData.GeneratedMaterials current =
                data.generatedMaterials;
            try
            {
                data.generatedMaterials = materials;
                data.CleanTextures();
            }
            finally
            {
                data.generatedMaterials = current;
            }
        }

        private static void LogGenerationException(UMAData data, Exception exception)
        {
            if (!Debug.isDebugBuild)
            {
                return;
            }

            if (data != null)
            {
                Debug.LogError(
                    $"Exception while generating UMA {data.name}: {exception.Message}",
                    data.gameObject);
            }
            Debug.LogException(exception);
        }

		private void UpdateUMAMesh(bool updatedAtlas)
		{
			if (meshCombiner != null)
			{
				meshCombiner.UpdateUMAMesh(updatedAtlas, umaData, atlasResolution);
			}
			else
			{
				if (Debug.isDebugBuild)
                {
                    Debug.LogError("UMAGenerator.UpdateUMAMesh, no MeshCombiner specified", gameObject);
                }
            }
		}


        /// <inheritdoc/>
        public override bool updatePending(UMAData umaToCheck)
        {
            if (umaDirtyList.Count < 2)
            {
                return false;
            }

            int val = umaDirtyList.IndexOf(umaToCheck, 1);
            return val != -1;
        }

        /// <inheritdoc/>
        public override bool updateProcessing(UMAData umaToCheck)
        {
            if (umaDirtyList.Count > 0)
            {
                if (umaDirtyList[0] == umaToCheck)
                {
                    return true;
                }
            }
            return false;
        }

        public void ClearAllPending()
        {
            CancelActiveDirtyUpdate(false);
            umaDirtyList.Clear();
            cleanUmas.Clear();
            dirtyUmas.Clear();
        }


        /// <inheritdoc/>
        public override void removeUMA(UMAData umaToRemove)
        {
            if (activeMultiStepGeneration != null &&
                activeMultiStepGeneration.Data == umaToRemove)
            {
                activeMultiStepGeneration.DiscardRequested = true;
                if (!activeMultiStepGeneration.CancellationIssued)
                {
                    activeMultiStepGeneration.MeshOperation?.Cancel();
                    activeMultiStepGeneration.CancellationIssued = true;
                }
            }

            // Remove from the various lists if it exists
            umaDirtyList.Remove(umaToRemove);
            cleanUmas.Remove(umaToRemove);
            dirtyUmas.Remove(umaToRemove);
        }

        /// <inheritdoc/>
        public override void addDirtyUMA(UMAData umaToAdd)
		{
			if (umaToAdd)
			{
                umaToAdd.BeginGenerationRequest();
                if (activeMultiStepGeneration != null &&
                    activeMultiStepGeneration.Data == umaToAdd)
                {
                    activeMultiStepGeneration.RestartRequested = true;
                }

                // guard against duplicates
                if (!umaDirtyList.Contains(umaToAdd))
                {
					//Debug.Log("Adding to dirty list");
                    umaDirtyList.Add(umaToAdd);
                    umaToAdd.MoveToList(dirtyUmas);
                }
			}
		}

        protected virtual void OnDisable()
        {
            CancelActiveDirtyUpdate(false);
        }

        protected virtual void OnDestroy()
        {
            CancelActiveDirtyUpdate(false);
        }

		public void Clear()
        {
            CancelActiveDirtyUpdate(false);
			umaDirtyList.Clear();
        }

		/// <inheritdoc/>
		public override bool IsIdle()
		{
			return umaDirtyList.Count == 0 &&
                activeMultiStepGeneration == null;
        }

		public bool hasPendingUMAS()
        {
            return umaDirtyList.Count > 0 ||
                activeMultiStepGeneration != null;
        }

        /// <inheritdoc/>
        public override int QueueSize()
		{
			return umaDirtyList.Count;
		}

		public virtual void UMAReady(bool fireEvents = true)
		{
			if (umaData)
			{
				umaData.Show();
                if (fireEvents)
                {
                    umaData.FireUpdatedEvent(false);
                }

                umaData.FireCharacterCompletedEvents(fireEvents);
#if UNITY_EDITOR
                if (umaData.skeleton.boneCount > 600)
				{
					if (Debug.isDebugBuild)
                    {
                        Debug.LogWarning("Skeleton has " + umaData.skeleton.boneCount + " bones, may be an error with slots!");
                    }
                }
#endif
			}
		}

		public virtual void PreApply(UMAData umaData)
		{
			if (umaData && umaData.umaRecipe.raceData.useNewDNA == false)
            {
                umaData.PreApplyDNA();
            }
        }

		public virtual void UpdateUMABody(UMAData umaData)
		{
			if (!umaData)
                return;

            if (!umaData.skeleton.isUpdating)
            {
                umaData.skeleton.BeginSkeletonUpdate();
            }

            umaData.FirePreUpdateUMABody();

            umaData.ResetToTPoseAndApplyDNA();

            if (umaData.skeleton is UMAImprovedSkeleton)
            {
                // ResetAll clears the bone-baking preservation flags. Restore them and
                // apply the post-DNA cache only during a real shape update. Mesh-only
                // rebuilds never enter this path and therefore leave the animated pose
                // untouched.
                umaData.RestoreRegisteredAnimatedBones();
                umaData.skeleton.EnsureBoneHierarchy();
            }

            // Only restore items if enabled, as this can be expensive
            if (SaveAndRestoreIgnoredItems)
            {
                umaData.RestoreSavedItems();
            }

            // End the batched skeleton update (begun in GenerateSingleUMA when isShapeDirty)
            umaData.skeleton.EndSkeletonUpdate();

            UpdateAvatar(umaData);

            // Blendshape DNA must be applied after the avatar is reset on the animator
            if (umaData.umaRecipe.raceData.useNewDNA == false)
            {
                umaData.PostApplyDNA();
            }
            else
            {
                umaData.NewDNAPostApply();
            }
            umaData.FireDNAAppliedEvents();
        }
#pragma warning restore 618
	}
}
