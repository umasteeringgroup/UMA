using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
	[System.Serializable]
	public partial class SlotDataAsset : ScriptableObject
	{
		public string slotName;
		public int slotNameHash;

		public SkinnedMeshRenderer meshRenderer;

		public Material materialSample;
		public float overlayScale = 1.0f;
		public Transform[] animatedBones = new Transform[0];
		public string[] textureNameList;
		public DnaConverterBehaviour slotDNA;
		public UMAMeshData meshData;
		public int subMeshIndex;
		/// <summary>
		/// Use this to identify slots that serves the same purpose
		/// Eg. ChestArmor, Helmet, etc.
		/// </summary>
		public string slotGroup;
		/// <summary>
		/// Use this to identify what kind of overlays fit this slotData
		/// Eg. BaseMeshSkin, BaseMeshOverlays, GenericPlateArmor01
		/// </summary>
		public string[] tags;
		public UMADataSlotMaterialRectEvent SlotAtlassed;
		public UMADataEvent DNAApplied;
		public UMADataEvent CharacterCompleted;
        
       
		public SlotDataAsset()
		{
            
		}

		public int GetTextureChannelCount(UMAGeneratorBase generator)
		{
			if (textureNameList != null && textureNameList.Length > 0)
			{
				if (string.IsNullOrEmpty(textureNameList[0]))
					return 0;
				return textureNameList.Length;
			}
			if (generator != null)
			{
				return generator.textureNameList.Length;
			}
			return 2; // UMA built in default
		}
        
		public override string ToString()
		{
			return "SlotData: " + slotName;
		}

#if UNITY_EDITOR
		public void UpdateMeshData(SkinnedMeshRenderer meshRenderer)
		{
			meshData = new UMAMeshData();
			meshData.RetrieveDataFromUnityMesh(meshRenderer);
			UnityEditor.EditorUtility.SetDirty(this);
		}
#endif
	}
}
