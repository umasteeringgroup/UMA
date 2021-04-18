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
		[Range(1.0f, 16.0f)]
		[Tooltip("Increase scale factor to decrease texture usage. A value of 1 means the textures will not be downsampled. Values greater than 1 will result in texture savings. The size of the texture is divided by this value.")]
        public int InitialScaleFactor = 1;

		[Range(1.0f,16.0f)]
		[Tooltip("Scale factor for edit-time builds. Increase scale factor to decrease texture usage. A value of 1 means the textures will not be downsampled. Values greater than 1 will result in texture savings. The size of the texture is divided by this value.")]
		public int editorInitialScaleFactor = 4;

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
		[Range(0.0f, 128.0f)]
		public int garbageCollectionRate = 8;

		public bool collectGarbage = true;
		private System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();

		[Tooltip("Generates a single UMA immediately with no coroutines. This is the fastest possible path.")]
		public bool NoCoroutines=true;

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

			if (collectGarbage && (forceGarbageCollect > garbageCollectionRate))
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
			UMAContextBase.IgnoreTag = ignoreTag;
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
				if (NoCoroutines)
				{
					UMAGeneratorPro ugp = new UMAGeneratorPro();
					ugp.ProcessTexture(this, umaData, true, InitialScaleFactor);
					TextureChanged++;
                }
                else
				{
					umaData = data;
					TextureProcessBaseCoroutine textureProcessCoroutine;
					textureProcessCoroutine = new TextureProcessPROCoroutine();
					textureProcessCoroutine.Prepare(data, this);

					activeGeneratorCoroutine = new UMAGeneratorCoroutine();
					activeGeneratorCoroutine.Prepare(this, umaData, textureProcessCoroutine, true, InitialScaleFactor);

					while (!activeGeneratorCoroutine.Work()) ;

					activeGeneratorCoroutine = null; 
				}

				TextureChanged++;
			}
		}

		public void SaveMountedItems(UMAData umaData)
        {
			if (!SaveAndRestoreIgnoredItems)
				return;

			GameObject holder = null;

			foreach(Transform t in umaData.gameObject.transform)
            {
				if (t.name == "Holder")
                {
					holder = t.gameObject;
                }
            }

			if (holder == null)
            {
				holder = new GameObject("Holder");
				holder.tag = UMAContextBase.IgnoreTag;
				holder.SetActive(false);
				holder.transform.parent = umaData.gameObject.transform;
			}
			// walk through all the bones.
			// if the tag has UMAContextBase.IgnoreTag, then 
			// copy the transform
			// copy the hash of the bone it came from  
			// save the object by changing the parent.
			// the parent object should be disabled so the children don't render.
			// continue.
			SaveBonesRecursively(umaData.umaRoot.transform, holder.transform);
		}

		public void SaveBonesRecursively(Transform bone, Transform holder)
        {
			if (bone.CompareTag(UMAContextBase.IgnoreTag))
			{
				if (bone.parent != null)
                {
					umaData.AddSavedItem(bone);
					bone.SetParent(holder, false);
                }
			}
			else
			{
				for (int i = 0; i < bone.childCount; i++)
				{
					SaveBonesRecursively(bone.GetChild(i),holder);
				}
			}
        }

		public bool GenerateSingleUMA(UMAData data, bool fireEvents)
		{
			UMAContextBase.IgnoreTag = ignoreTag;
			if (data == null)
				return true;

			data.umaGenerator = this;
			FreezeTime = true;
			umaData = data;

			if (umaData.RebuildSkeleton)
			{
				if (umaData.umaRoot != null) 
					SaveMountedItems(umaData);
				DestroyImmediate(umaData.umaRoot, false);
				umaData.umaRoot = null;
				umaData.RebuildSkeleton = false;
				umaData.isShapeDirty = true;
			}

			if (!umaData.Validate())
				return true;

			if (meshCombiner != null)
			{
				meshCombiner.Preprocess(umaData);
			}
			umaData.FireCharacterBegunEvents();
			PreApply(umaData); 

			if (umaData.isTextureDirty)
			{
				UMAGeneratorPro ugp = new UMAGeneratorPro();
				ugp.ProcessTexture(this, umaData, !umaData.isMeshDirty, InitialScaleFactor);
				umaData.isTextureDirty = false;
				umaData.isAtlasDirty |= umaData.isMeshDirty;
				TextureChanged++;
			}

			if (umaData.isMeshDirty)
			{
				UpdateUMAMesh(umaData.isAtlasDirty);
				umaData.isAtlasDirty = false;
				umaData.isMeshDirty = false;
				SlotsChanged++;
				forceGarbageCollect++;
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

		public virtual bool HandleDirtyUpdate(UMAData data)
		{
			UMAContextBase.IgnoreTag = ignoreTag;
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
				if (NoCoroutines)
                {
					UMAData umaData = umaDirtyList[0];
					if (umaData.RebuildSkeleton)
                    {
						DestroyImmediate(umaData.umaRoot, false);
						umaData.umaRoot = null;
						umaData.RebuildSkeleton = false;
						umaData.isShapeDirty = true;
					} // this happens in GenerateSingleUMA now
					GenerateSingleUMA(umaDirtyList[0],true);
					umaDirtyList.RemoveAt(0);
					umaData.MoveToList(cleanUmas);
					umaData = null;
					return;
                }
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

		public void Clear()
        {
			umaDirtyList.Clear();
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
				if (umaData.skeleton.boneCount > 500)
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
				umaData.FirePreUpdateUMABody();
				umaData.skeleton.ResetAll();    // I don't think this needs to be called, because we overwrite all that in the next call.
				// Put the skeleton into TPose so rotations will be valid for generating avatar
				umaData.GotoTPose();
				umaData.ApplyDNA();
				umaData.FireDNAAppliedEvents();
				umaData.RestoreSavedItems();
				// This has to happen for some reason, or the default models heads cave in.
				umaData.skeleton.EndSkeletonUpdate();
				UpdateAvatar(umaData);
			}
		}
#pragma warning restore 618
	}
}
