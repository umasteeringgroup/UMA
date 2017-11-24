using UnityEngine;
using System;
using UMA.PoseTools;

namespace UMA
{
	/// <summary>
	/// DNA converter using a set of blend shapes.
	/// </summary>
	public class BlendShapeSetDnaConverterBehaviour : DynamicDNAConverterBehaviourBase
	{
		public BlendShapeSetDnaAsset blendShapeSet;

		public BlendShapeSetDnaConverterBehaviour()
		{
			ApplyDnaAction = ApplyDNA;
			DNAType = typeof(DynamicUMADna);
		}

		public override void Prepare()
		{
		}

		public override int DNATypeHash
		{
			get {
				if (blendShapeSet != null)
					return blendShapeSet.dnaTypeHash;
				
				return dnaTypeHash;
			}
		}

		public void ApplyDNA(UMAData data, UMASkeleton skeleton)
		{
			if (blendShapeSet == null)
			{
				Debug.LogError("Missing blend shape set asset for: " + this.name);
				return;
			}

			UMADnaBase activeDNA = data.GetDna(this.dnaTypeHash);
			if (activeDNA == null)
			{
				Debug.LogError("Could not get DNA values for: "+ this.name);
				return;
			}

			if (!String.IsNullOrEmpty(blendShapeSet.startingBlendShape))
			{
				data.SetBlendShape(blendShapeSet.startingBlendShape, 1f);
			}

			if (activeDNA.Count == blendShapeSet.shapePairs.Length)
			{
				float[] dnaValues = activeDNA.Values;
				for (int i = 0; i < dnaValues.Length; i++)
				{
					float dnaValue = dnaValues[i];
					BlendShapeSetDnaAsset.BlendShapePair shapePair = blendShapeSet.shapePairs[i];
					if (dnaValue > 0.51f)
					{
						float shapeWeight = (dnaValue - 0.5f) * 2f;
						if (!String.IsNullOrEmpty(shapePair.blendShapeOne))
							data.SetBlendShape(shapePair.blendShapeOne, shapeWeight);
					}
					else if (dnaValue < 0.49f)
					{
						float shapeWeight = (0.5f - dnaValue) * 2f;
						if (!String.IsNullOrEmpty(shapePair.blendShapeZero))
							data.SetBlendShape(shapePair.blendShapeZero, shapeWeight);
					}
				}
			}
			else
			{
				// HACK
				// need to use the version info to get the remapping of indices
			}
		}
	}
}
