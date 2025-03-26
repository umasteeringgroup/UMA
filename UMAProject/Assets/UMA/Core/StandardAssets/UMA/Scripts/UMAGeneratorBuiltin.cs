using System;
using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Default UMA character generator.
	/// </summary>
	public abstract class UMAGeneratorBuiltin : UMAGeneratorBase
	{
		[NonSerialized]
		protected UMAData umaData;
		[NonSerialized]
		protected List<UMAData> umaDirtyList = new List<UMAData>();

		private LinkedList<UMAData> cleanUmas = new LinkedList<UMAData>();
		private LinkedList<UMAData> dirtyUmas = new LinkedList<UMAData>();
		public UMAMeshCombiner meshCombiner;
		private HashSet<string> raceNames;

		/// <summary>
		/// 
		/// </summary>
		[Range(1.0f, 16.0f)]
		[Tooltip("Increase scale factor to decrease texture usage. A value of 1 means the textures will not be downsampled. Values greater than 1 will result in texture savings. The size of the texture is divided by this value.")]
        public int InitialScaleFactor = 1;

		[Range(1.0f,16.0f)]
		[Tooltip("Scale factor for edit-time builds. Increase scale factor to decrease texture usage. A value of 1 means the textures will not be downsampled. Values greater than 1 will result in texture savings. The size of the texture is divided by this value.")]
		public int editorInitialScaleFactor = 4;

		[Tooltip("Number of iterations to process each frame")]
		public int IterationCount = 1;

		[Tooltip("Enable Process All Pending to force the generate to process all pending UMA during the next frame")]
		public bool processAllPending = false;

		[Tooltip("When enable, the texture will be applied right away during the conversion process")]
		public bool applyInline = false;

        private int forceGarbageCollect;
        /// <summary>
        /// Number of character updates before triggering System garbage collect.
        /// </summary>
        [Tooltip("Number of character updates before triggering garbage collection.")]
		[Range(0.0f, 128.0f)]
		public int garbageCollectionRate = 8;

		public bool collectGarbage = true;
		private System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();

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

        public virtual void OnEnable()
		{
		}

		public virtual void Awake()
		{

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

		public static uint WorkCount = 0;
		public override void Work()
		{
			RenderTexToCPU.ApplyInline = applyInline;
            if (!IsIdle())
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
                    GC.Collect();
                    forceGarbageCollect = 0;
                }

                stopWatch.Reset();
				stopWatch.Start();
				int count = IterationCount;

				// If processAllPending is set, process as many are in the queue right now.
				// We get the count (and multiply by two for slow gen) in case bad events add more items to the queue.
				if (processAllPending)
				{
					count = umaDirtyList.Count;
                }

				if (hasPendingUMAS())
				{
					for (int i = 0; i < count; i++)
					{
						OnDirtyUpdate();
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
                if (garbageCollectionRate == 0)
                {
                    GC.Collect(0);
                }
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




		public bool GenerateSingleUMA(UMAData data, bool fireEvents)
		{
#if DEBUG_TIMING
            System.Diagnostics.Stopwatch gstopWatch = System.Diagnostics.Stopwatch.StartNew();
            gstopWatch.Start();
#endif
            if (data == null)
            {
                return true;
            }

            data.umaGenerator = this;
			FreezeTime = true;
			umaData = data;

			if (umaData.RebuildSkeleton)
			{
				if (umaData.umaRoot != null)
                {
                    SaveMountedItems(umaData);
                }
                DestroyImmediate(umaData.umaRoot, false);
				umaData.umaRoot = null;
				umaData.RebuildSkeleton = false;
				umaData.isShapeDirty = true;
			}

			if (!umaData.Validate())
            {
                return true;
            }
#if DEBUG_TIMING
            long validation = gstopWatch.ElapsedTicks;
            gstopWatch.Restart();
#endif
            RenderTexture rbackup = RenderTexture.active;

			if (meshCombiner != null)
			{
				meshCombiner.Preprocess(umaData);
			}

#if DEBUG_TIMING
            long meshpreprocess = gstopWatch.ElapsedTicks;
            gstopWatch.Restart();
#endif
            umaData.FireCharacterBegunEvents();

#if DEBUG_TIMING
            long BegunEvents = gstopWatch.ElapsedTicks;
            gstopWatch.Restart();
#endif
            if (!umaData.rawAvatar)
			{
				PreApply(umaData);
			}

#if DEBUG_TIMING
            long preapply = gstopWatch.ElapsedTicks;
            gstopWatch.Restart();
#endif

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
            gstopWatch.Restart();
#endif
            if (umaData.isMeshDirty)
			{
				UpdateUMAMesh(umaData.isAtlasDirty);
				umaData.isAtlasDirty = false;
				umaData.isMeshDirty = false;
				SlotsChanged++;
				forceGarbageCollect++;
			}
#if DEBUG_TIMING
            long meshUpdates = gstopWatch.ElapsedTicks;
            gstopWatch.Restart();
#endif

            if (umaData.isShapeDirty)
			{
				if (!umaData.skeleton.isUpdating)
				{
					umaData.skeleton.BeginSkeletonUpdate();
				}
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
            gstopWatch.Restart();
#endif

			/* here, set any race specific blendshapes */
			SkinnedMeshRenderer[] renderers = umaData.GetRenderers();
		
			if (autoSetRaceBlendshapes)
			{
				if (raceNames == null && UMAContextBase.Instance != null)
				{
					RaceData[] races = UMAContextBase.Instance.GetAllRaces();
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

#if DEBUG_TIMING
            long raceblendshapes = gstopWatch.ElapsedTicks;
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
            gstopWatch.Stop();
#endif
			FreezeTime = false;
#if DEBUG_TIMING
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
            return true;
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


		public virtual bool OldHandleDirtyUpdate(UMAData data)
		{
            /*
			UMAContextBase.IgnoreTag = ignoreTag;
			if (data == null)
            {
                return true;
            }

            if (umaData != data)
			{
				umaData = data;

				if (!umaData.Validate())
                {
                    return true;
                }

                if (meshCombiner != null)
				{
					meshCombiner.Preprocess(umaData);
				}
				umaData.FireCharacterBegunEvents();
				PreApply(umaData);
			}
			if (umaData.RebuildSkeleton)
			{
				SaveMountedItems(umaData);
				DestroyImmediate(umaData.umaRoot, false);
				umaData.umaRoot = null;
				umaData.RebuildSkeleton = false;
				umaData.isShapeDirty = true;
			}


			if (umaData.isTextureDirty)
			{
				bool meshWasDirty = umaData.isMeshDirty;
				if (activeGeneratorCoroutine == null)
				{
					TextureProcessBaseCoroutine textureProcessCoroutine;
					textureProcessCoroutine = new TextureProcessPROCoroutine();
					textureProcessCoroutine.Prepare(data, this);

					activeGeneratorCoroutine = new UMAGeneratorCoroutine();
					activeGeneratorCoroutine.Prepare(this, umaData, textureProcessCoroutine, !umaData.isMeshDirty, InitialScaleFactor);
				}

				bool workDone = activeGeneratorCoroutine.Work();
				if (workDone)
				{
					activeGeneratorCoroutine = null;
					umaData.isTextureDirty = false;
					umaData.isAtlasDirty |= umaData.isMeshDirty;
					TextureChanged++;
				}

				//shouldn't this only cause another loop if this part MADE the mesh dirty?
				if (!workDone || !fastGeneration || (!meshWasDirty && umaData.isMeshDirty))
				{
					return false;
				}
			}


			if (umaData.isMeshDirty)
			{
				UpdateUMAMesh(umaData.isAtlasDirty);
				umaData.isAtlasDirty = false;
				umaData.isMeshDirty = false;
				SlotsChanged++;
				forceGarbageCollect++;

				if (!fastGeneration)
                {
                    return false;
                }
            }

			if (umaData.isShapeDirty)
			{
				if (!umaData.skeleton.isUpdating)
				{
					umaData.skeleton.BeginSkeletonUpdate();
				}
				UpdateUMABody(umaData);
				umaData.isShapeDirty = false;
				DnaChanged++;
			}
			else if (umaData.skeleton.isUpdating)
			{
				umaData.skeleton.EndSkeletonUpdate();
			}

			UMAReady();
			*/
            return true;
		}

		public virtual void OnDirtyUpdate()
		{
			try
			{
                if (umaDirtyList.Count < 1)
				{
					return;
				}

				UMAData umaData = umaDirtyList[0];
				try
				{
					GenerateSingleUMA(umaDirtyList[0], true);
				}
				catch (Exception ex)
				{
					if (Debug.isDebugBuild)
					{
						Debug.LogException(ex);
					}
				}
                umaDirtyList.RemoveAt(0);
				umaData.MoveToList(cleanUmas);
				umaData = null;
				return;
			}
			catch (Exception ex)
			{
				if (Debug.isDebugBuild)
				{
					UnityEngine.Debug.LogException(ex);
				}
			}
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

        /// <inheritdoc/>
        public override void removeUMA(UMAData umaToRemove)
        {
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
                // guard against duplicates
                if (!updatePending(umaToAdd))
                {
                    umaDirtyList.Add(umaToAdd);
                    umaToAdd.MoveToList(dirtyUmas);
                }
			}
		}

		public void Clear()
        {
			umaDirtyList.Clear();
        }

		/// <inheritdoc/>
		public override bool IsIdle()
		{
			return (umaDirtyList.Count == 0);// && RenderTexToCPU.PendingCopies() == 0);
        }

		public bool hasPendingUMAS()
        {
            return umaDirtyList.Count > 0;
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
				if (umaData.skeleton.boneCount > 600)
				{
					if (Debug.isDebugBuild)
                    {
                        Debug.LogWarning("Skeleton has " + umaData.skeleton.boneCount + " bones, may be an error with slots!");
                    }
                }
			}
		}

		public virtual void PreApply(UMAData umaData)
		{
			if (umaData)
            {
                umaData.PreApplyDNA();
            }
        }

		public virtual void UpdateUMABody(UMAData umaData)
		{
			if (umaData)
			{
				umaData.FirePreUpdateUMABody();

				umaData.skeleton.ResetAll();    // I don't think this needs to be called, because we overwrite all that in the next call.
												// Put the skeleton into TPose so rotations will be valid for generating avatar
				if (!umaData.rawAvatar)
				{
					umaData.GotoTPose();
					umaData.ApplyDNA();
				}
				umaData.RestoreSavedItems();
				// This has to happen for some reason, or the default models heads cave in.
				umaData.skeleton.EndSkeletonUpdate();
				UpdateAvatar(umaData);
				// Blendshape DNA must be applied after the avatar is reset on the animator
				umaData.PostApplyDNA();
				umaData.FireDNAAppliedEvents();
			}
		}
#pragma warning restore 618
	}
}
