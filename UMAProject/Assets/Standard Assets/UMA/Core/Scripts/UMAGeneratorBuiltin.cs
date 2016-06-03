using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace UMA
{
	/// <summary>
	/// Default UMA character generator.
	/// </summary>
	public abstract class UMAGeneratorBuiltin : UMAGeneratorBase
	{
		public UMAData umaData;
		[NonSerialized]
		public List<UMAData> umaDirtyList = new List<UMAData>();
		private UMAGeneratorCoroutine activeGeneratorCoroutine;
		public Transform textureMergePrefab;
		public UMAMeshCombiner meshCombiner;
		/// <summary>
		/// If true, generate in a single update.
		/// </summary>
		public bool fastGeneration = true;
		private int forceGarbageCollect;
		/// <summary>
		/// Number of character updates before triggering System garbage collect.
		/// </summary>
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

			if (!textureMerge)
			{
				Transform tempTextureMerger = Instantiate(textureMergePrefab, Vector3.zero, Quaternion.identity) as Transform;
				textureMerge = tempTextureMerger.GetComponent("TextureMerge") as TextureMerge;
				textureMerge.transform.parent = transform;
				textureMerge.gameObject.SetActive(false);
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

		void Update()
		{
			stopWatch.Reset();
			stopWatch.Start();
			if (forceGarbageCollect > garbageCollectionRate)
			{
				GC.Collect();
				forceGarbageCollect = 0;
				if (garbageCollectionRate < 1) garbageCollectionRate = 1;
			}
			else if (umaDirtyList.Count > 0)
			{
				OnDirtyUpdate();
				ElapsedTicks += stopWatch.ElapsedTicks;
#if UNITY_EDITOR
				UnityEditor.EditorUtility.SetDirty(this);
#endif
			}
			stopWatch.Stop();
			UMATime.ReportTimeSpendtThisFrameTicks(stopWatch.ElapsedTicks);
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
			}

			if (umaData.isTextureDirty)
			{
				if (activeGeneratorCoroutine == null)
				{
					TextureProcessBaseCoroutine textureProcessCoroutine;
					textureProcessCoroutine = new TextureProcessPROCoroutine();
					textureProcessCoroutine.Prepare(data, this);

					activeGeneratorCoroutine = new UMAGeneratorCoroutine();
					activeGeneratorCoroutine.Prepare(this, umaData, textureProcessCoroutine, !umaData.isMeshDirty);
				}

				bool workDone = activeGeneratorCoroutine.Work();
				if (workDone)
				{
					activeGeneratorCoroutine = null;
					umaData.isTextureDirty = false;
					umaData.isAtlasDirty |= umaData.isMeshDirty;
					TextureChanged++;
				}

				if (!workDone || !fastGeneration || umaData.isMeshDirty)
					return false;
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
				UpdateUMABody(umaData);
				umaData.isShapeDirty = false;
				DnaChanged++;
			}
			UMAReady();
			return true;
		}

		public virtual void OnDirtyUpdate()
		{
			if (HandleDirtyUpdate(umaDirtyList[0]))
			{
				umaDirtyList.RemoveAt(0);
				umaData = null;
			}
			else if (fastGeneration && HandleDirtyUpdate(umaDirtyList[0]))
			{
				umaDirtyList.RemoveAt(0);
				umaData = null;
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
				Debug.LogError("UMAGenerator.UpdateUMAMesh, no MeshCombiner specified", gameObject);
			}
		}

		/// <inheritdoc/>
		public override void addDirtyUMA(UMAData umaToAdd)
		{
			if (umaToAdd)
			{
				umaDirtyList.Add(umaToAdd);
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
				umaData.myRenderer.enabled = true;
				umaData.FireUpdatedEvent(false);
				umaData.FireCharacterCompletedEvents();
				if (umaData.skeleton.boneCount > 300)
				{
					Debug.LogWarning("Skeleton has " + umaData.skeleton.boneCount + " bones, may be an error with slots!");
				}
			}
		}

		public virtual void UpdateUMABody(UMAData umaData)
		{
			if (umaData)
			{
				umaData.skeleton.ResetAll();
				umaData.ApplyDNA();
				umaData.FireDNAAppliedEvents();
				umaData.skeleton.EndSkeletonUpdate();
				UpdateAvatar(umaData);
			}
		}
	}
}
