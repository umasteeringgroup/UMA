using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace UMA
{
	public abstract class UMAGeneratorBuiltin : UMAGeneratorBase
	{
		public UMAData umaData;
		[NonSerialized]
		public List<UMAData>
		umaDirtyList = new List<UMAData>();
		public int meshUpdates;
		public int maxMeshUpdates;
		public UMAGeneratorCoroutine umaGeneratorCoroutine;
		public UMAGeneratorCoroutine activeGeneratorCoroutine;
		public Transform textureMergePrefab;
		public Matrix4x4 tempMatrix;
		public UMAMeshCombiner meshCombiner;
		public bool fastGeneration = true;
		private int forceGarbageCollect;
		public int garbageCollectionRate = 8;
		private System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();

		public void Initialize()
		{
			umaGeneratorCoroutine = new UMAGeneratorCoroutine();
		}

		public virtual void Awake()
		{
            
			maxMeshUpdates = 1;
			if (atlasResolution == 0)
				atlasResolution = 256;
			umaGeneratorCoroutine = new UMAGeneratorCoroutine();
            
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
				if (umaDirtyList[0].isTextureDirty)
				{
					OnDirtyUpdate();
				}
				if (umaDirtyList[0].isMeshDirty)
				{
					OnDirtyUpdate();
				}
			}
			meshUpdates = 0;
			stopWatch.Stop();
			UMATime.ReportTimeSpendtThisFrameTicks(stopWatch.ElapsedTicks);
		}

		public virtual bool HandleDirtyUpdate(UMAData data)
		{
			if (umaData != data)
			{
				umaData = data;

				if (!umaData.Validate())
					return true;
			}
            
			if (umaData.isTextureDirty)
			{
				if (activeGeneratorCoroutine == null)
				{
					activeGeneratorCoroutine = umaGeneratorCoroutine;
					TextureProcessBaseCoroutine textureProcessCoroutine;
					textureProcessCoroutine = new TextureProcessPROCoroutine();
					textureProcessCoroutine.Prepare(data, this);
					activeGeneratorCoroutine.Prepare(this, umaData, textureProcessCoroutine, !umaData.isMeshDirty);
				}

				bool workDone = umaGeneratorCoroutine.Work();
				if (workDone)
				{
					activeGeneratorCoroutine = null;
					umaData.isTextureDirty = false;
					umaData.isAtlasDirty = true;
				}

				if (!workDone || !fastGeneration || umaData.isMeshDirty)
					return false;
			}

			if (umaData.isMeshDirty)
			{
				UpdateUMAMesh(umaData.isAtlasDirty);
				umaData.isAtlasDirty = false;
				umaData.isMeshDirty = false;

				if (!fastGeneration)
					return false;
			}

			if (umaData.isShapeDirty)
			{
				UpdateUMABody(umaData);
				umaData.isShapeDirty = false;
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
		}

		private void UpdateUMAMesh(bool updatedAtlas)
		{
			if (meshCombiner != null)
			{
				meshCombiner.UpdateUMAMesh(updatedAtlas, umaData, textureNameList, atlasResolution);
			} else
			{
				Debug.LogError("UMAGenerator.UpdateUMAMesh, no MeshCombiner specified", gameObject);
			}
		}

		public override void addDirtyUMA(UMAData umaToAdd)
		{   
			if (umaToAdd)
			{
				umaDirtyList.Add(umaToAdd);
			}
		}

		public override bool IsIdle()
		{
			return umaDirtyList.Count == 0;
		}

		public override int QueueSize()
		{
			return umaDirtyList.Count;
		}

		public virtual void UMAReady()
		{   
			if (umaData)
			{
				forceGarbageCollect++;
				umaData.myRenderer.enabled = true;
				umaData.FireUpdatedEvent(false);
				umaData.FireCharacterCompletedEvents();
				umaData.skeleton.EndSkeletonUpdate();
			}
		}
    
		public virtual void UpdateUMABody(UMAData umaData)
		{
			if (umaData)
			{
				umaData.skeleton.ResetAll();
				umaData.ApplyDNA();
				umaData.FireDNAAppliedEvents();
				UpdateAvatar(umaData);
			}
		}
	}
}