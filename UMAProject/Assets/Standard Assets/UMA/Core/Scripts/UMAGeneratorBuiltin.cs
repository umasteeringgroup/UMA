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
		public float unityVersion;
		private bool forceGarbageCollect;

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
				data [0] = 0;
				data [10 * 1024 * 1024 - 1] = 0;
			}
		}
        
		void Update()
		{
			bool didGC = false;
			if (forceGarbageCollect)
			{
				GC.Collect();
				forceGarbageCollect = false;
				didGC = true;
			}
			if (umaDirtyList.Count > 0)
			{
				// GC can run with texture preparation, but nothing else
				if (didGC && !umaDirtyList [0].isTextureDirty)
					return;

				OnDirtyUpdate();    
			}
			meshUpdates = 0;    
		}

		public virtual bool HandleDirtyUpdate(UMAData data)
		{
			if (umaData != data)
			{
				umaData = data;

				if (!umaData.Validate())
				{
					return true;
				}
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

				if (!workDone || umaData.isMeshDirty)
					return false;
			}

			if (umaData.isMeshDirty)
			{
				UpdateUMAMesh(umaData.isAtlasDirty);
				umaData.isAtlasDirty = false;
				umaData.isMeshDirty = false;
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
			if (HandleDirtyUpdate(umaDirtyList [0]))
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
				forceGarbageCollect = true;
				umaData.myRenderer.enabled = true;
				umaData.FireUpdatedEvent(false);
			}
		}
    
		public virtual void UpdateUMABody(UMAData umaData)
		{
			if (umaData)
			{
				umaData.GotoOriginalPose();
				umaData.skeleton = new UMASkeletonDefault(umaData.myRenderer.rootBone);
				umaData.ApplyDNA();
				umaData.FireDNAAppliedEvents();
				UpdateAvatar(umaData);
			}
		}
	}
}