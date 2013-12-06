using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace UMA
{
	public class UMAEditorGenerator
	{
		public UMAMeshCombiner meshCombiner;
		public string[] textureNameList;

		public UMAEditorGenerator(string[] textureNameList, UMADefaultMeshCombiner meshCombiner)
		{
			this.textureNameList = textureNameList;
			this.meshCombiner = meshCombiner;
		}

		public virtual void UpdateUMAMesh(UMAData umaData)
		{
			meshCombiner.UpdateUMAMesh(false, umaData, textureNameList, 1);
		}

		public virtual void UpdateUMABody(UMAData umaData)
		{
			umaData.ApplyDNA();
		}
	}
}
