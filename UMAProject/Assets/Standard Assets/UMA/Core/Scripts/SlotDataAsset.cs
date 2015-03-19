using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
	[System.Serializable]
	public partial class SlotDataAsset : ScriptableObject, ISerializationCallbackReceiver
	{
		public string slotName;
		[System.NonSerialized]
		public int nameHash;

		public SkinnedMeshRenderer meshRenderer;

		public Material materialSample;
		public float overlayScale = 1.0f;
		public Transform[] animatedBones = new Transform[0];

#if THIS_WOULD_BE_COOL
		public struct ChannelDefinition
		{
			public string shaderVariableName;
			public bool isNormalMap;
		}
		public ChannelDefinition[] textureChannels;
#endif

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
		public UMADataEvent CharacterBegun;
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
		public void OnAfterDeserialize()
		{
			nameHash = UMASkeleton.StringToHash(slotName);
		}
		public void OnBeforeSerialize() { }
	}
}
