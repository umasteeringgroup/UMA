using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{

	[Serializable]
	public class UMAUVAttachedItemBlendshapeAdjuster
	{
		public string BlendshapeName;
		public string RaceName;
		public Vector3 newOffset;
		public Vector3 newOrientation;

		public UMAUVAttachedItemBlendshapeAdjuster(string blendshapeName, string raceName, Vector3 newOffset, Vector3 newOrientation)
		{
			BlendshapeName = blendshapeName;
			RaceName = raceName;
			this.newOffset = newOffset;
			this.newOrientation = newOrientation;
		}

		public UMAUVAttachedItemBlendshapeAdjuster(UMAUVAttachedItemBlendshapeAdjuster src)
		{
			this.newOffset = src.newOffset;
			this.newOrientation = src.newOrientation;
			this.BlendshapeName = src.BlendshapeName;
			this.RaceName = src.RaceName;
		}
	}
}