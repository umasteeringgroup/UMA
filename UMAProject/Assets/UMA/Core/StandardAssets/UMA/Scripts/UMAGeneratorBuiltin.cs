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
		private UMAGeneratorCoroutine activeGeneratorCoroutine;
		public UMAMeshCombiner meshCombiner;

        /// <summary>
        /// 
        /// </summary>
        [Tooltip("Increase scale factor to decrease texture usage. A value of 1 means the textures will not be downsampled. Values greater than 1 will result in texture savings. The size of the texture is divided by this value.")]
        public int InitialScaleFactor = 1;

		[Tooltip("Number of iterations to process each frame")]
		public int IterationCount = 1;

		/// <summary>
		/// If true, generate in a single update.
		/// </summary>
        [Tooltip("Set Fast Generation to true to have the UMA Avatar generated in a single update. Otherwise, generation can span multiple frames.")]
		public bool fastGeneration = true;

		[Tooltip("Enable Process All Pending to force the generate to process all pending UMA during the next frame")]
		public bool processAllPending = false;

		private int forceGarbageCollect;
        /// <summary>
        /// Number of character updates before triggering System garbage collect.
        /// </summary>
        [Tooltip("Number of character updates before triggering garbage collection.")]
        public int garbageCollectionRate = 8;
		private System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();

		[NonSerialized]
		public long ElapsedTicks;
		[NonSerialized]
		public long DnaChanged;
		[NonSerialized]
		public long TextureChanged;
		[NonSerialized]
		public long SlotsChanged;

		public virtual void OnEnable()
		{
			activeGeneratorCoroutine = null;
		}

		public virtual void Awake()
		{
			activeGeneratorCoroutine = null;

			if (atlasResolution == 0)
				atlasResolution = 256;

			if (defaultOverlayAsset != null)
				_defaultOverlayData = new OverlayData (defaultOverlayAsset);

			if (!textureMerge)
			{
				if (Debug.isDebugBuild)
					Debug.LogError("No TextureMerge set!");
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
				return; // if render textures needs rebuild we'll not do anything else

			if (forceGarbageCollect > garbageCollectionRate)
			{
				GC.Collect();
				forceGarbageCollect = 0;
				if (garbageCollectionRate < 1) garbageCollectionRate = 1;
			}
			else
			{
				Work();
			}
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
					return rt;
				iteratorNode = iteratorNode.Next;
			}
			return null;
		}

		public override void Work()
		{
			if (!IsIdle())
			{
				stopWatch.Reset();
				stopWatch.Start();
				int count = IterationCount;

				// If processAllPending is set, process as many are in the queue right now.
				// We get the count (and multiply by two for slow gen) in case bad events add more items to the queue.
				if (processAllPending)
				{
					count = umaDirtyList.Count;
					if (!fastGeneration) count *= 2;
				}

				for (int i = 0; i < count; i++)
				{
					OnDirtyUpdate();
					if (IsIdle())
						break;
				}
				ElapsedTicks += stopWatch.ElapsedTicks;
#if UNITY_EDITOR
				UnityEditor.EditorUtility.SetDirty(this);
#endif
				stopWatch.Stop();
				UMATime.ReportTimeSpendtThisFrameTicks(stopWatch.ElapsedTicks);
			}
		}

#pragma warning disable 618
		public void RebuildAllRenderTextures()
		{
			var activeUmaData = umaData;
			var storedGeneratorCoroutine = activeGeneratorCoroutine;


			var iteratorNode = cleanUmas.First;
			while (iteratorNode != null)
			{
				RebuildRenderTexture(iteratorNode.Value);
				iteratorNode = iteratorNode.Next;
			}

			umaData = activeUmaData;
			activeGeneratorCoroutine = storedGeneratorCoroutine;
		}

		private void RebuildRenderTexture(UMAData data)
		{
			var rt = data.GetFirstRenderTexture();
			if (rt != null && !rt.IsCreated())
			{
				umaData = data;
				TextureProcessBaseCoroutine textureProcessCoroutine;
				textureProcessCoroutine = new TextureProcessPROCoroutine();
				textureProcessCoroutine.Prepare(data, this);

				activeGeneratorCoroutine = new UMAGeneratorCoroutine();
				activeGeneratorCoroutine.Prepare(this, umaData, textureProcessCoroutine, true, InitialScaleFactor);

				while (!activeGeneratorCoroutine.Work()) ;

				activeGeneratorCoroutine = null;
				TextureChanged++;
			}
		}

		public virtual bool HandleDirtyUpdate(UMAData data)
		{
			if (data == null)
				return true;

			if (umaData != data)
			{
				umaData = data;

				if (!umaData.Validate())
					return true;

				if (meshCombiner != null)
				{
					meshCombiner.Preprocess(umaData);
				}
				umaData.FireCharacterBegunEvents();
				PreApply(umaData);
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
					//Debug.Log("workDone = " + workDone + " fastGeneration = " + fastGeneration + " umaData.isMeshDirty = " + umaData.isMeshDirty);
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
					return false;
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
			return true;
		}

		public virtual void OnDirtyUpdate()
		{
			try
			{
				if (HandleDirtyUpdate(umaDirtyList[0]))
				{
					umaDirtyList.RemoveAt(0);
					umaData.MoveToList(cleanUmas);
					umaData = null;
				}
				else if (fastGeneration && HandleDirtyUpdate(umaDirtyList[0]))
				{
					umaDirtyList.RemoveAt(0);
					umaData.MoveToList(cleanUmas);
					umaData = null;
				}
			}
			catch (Exception ex)
			{
				if (Debug.isDebugBuild)
					UnityEngine.Debug.LogException(ex);
			}
			//anything more than 166,000 is too long (166,000 is 1 frame @ 60fps)
			//the demo alien is about 65,000 on average- this is a big chunk of the available time though and my machine is fast
			//Human Male DCS using pre plugins dna is about 45,000 on average but then its only doing 'skeletonModifiers' and 1 bonepose
			//where as elfOrAlien demo is doing SkeletonModifiers + 3 BonePoses + 2 Blendshapes + 7 ColorDNAs
			//if(charName != "")
			//Debug.Log(charName + " DirtyUpdate took " + DirtyStopwatch.ElapsedTicks);
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
					Debug.LogError("UMAGenerator.UpdateUMAMesh, no MeshCombiner specified", gameObject);
			}
		}


        /// <inheritdoc/>
        public override bool updatePending(UMAData umaToCheck)
        {
            if (umaDirtyList.Count < 2)
                return false;

            int val = umaDirtyList.IndexOf(umaToCheck, 1);
            return val != -1;
        }

        /// <inheritdoc/>
        public override bool updateProcessing(UMAData umaToCheck)
        {
            if (umaDirtyList.Count > 0)
            {
                if (umaDirtyList[0] == umaToCheck)
                    return true;
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

		/// <inheritdoc/>
		public override bool IsIdle()
		{
			return umaDirtyList.Count == 0;
		}

		/// <inheritdoc/>
		public override int QueueSize()
		{
			return umaDirtyList.Count;
		}

		public virtual void UMAReady()
		{
			if (umaData)
			{
				umaData.Show();
				umaData.FireUpdatedEvent(false);
				umaData.FireCharacterCompletedEvents();
				if (umaData.skeleton.boneCount > 300)
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning("Skeleton has " + umaData.skeleton.boneCount + " bones, may be an error with slots!");
				}
			}
		}

		public virtual void PreApply(UMAData umaData)
		{
			if (umaData)
				umaData.PreApplyDNA();
		}

		public virtual void UpdateUMABody(UMAData umaData)
		{
			if (umaData)
			{
				umaData.skeleton.ResetAll();
				// Put the skeleton into TPose so rotations will be valid for generating avatar
				umaData.GotoTPose();
				umaData.ApplyDNA();
				umaData.FireDNAAppliedEvents();
				umaData.skeleton.EndSkeletonUpdate();
				UpdateAvatar(umaData);
			}
		}
#pragma warning restore 618
	}
}
