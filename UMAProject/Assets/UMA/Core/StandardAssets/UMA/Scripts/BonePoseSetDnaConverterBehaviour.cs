using UnityEngine;
using System;
using UMA.PoseTools;

namespace UMA
{
	/// <summary>
	/// DNA converter using a set of bone poses.
	/// </summary>
	public class BonePoseSetDnaConverterBehaviour : DnaConverterBehaviour
	{
		public BonePoseSetDnaAsset bonePoseSet;

		public BonePoseSetDnaConverterBehaviour()
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
				if (bonePoseSet != null)
					return bonePoseSet.dnaTypeHash;
				
				return dnaTypeHash;
			}
		}

		public void ApplyDNA(UMAData data, UMASkeleton skeleton)
		{
			if (bonePoseSet == null)
			{
				Debug.LogError("Missing morph set asset for: " + this.name);
				return;
			}

			UMADnaBase activeDNA = data.GetDna(this.dnaTypeHash);
			if (activeDNA == null)
			{
				Debug.LogError("Could not get DNA values for: "+ this.name);
				return;
			}

			if (bonePoseSet.startingPose != null)
			{
				bonePoseSet.startingPose.ApplyPose(skeleton, 1f);
			}

			if (activeDNA.Count == bonePoseSet.posePairs.Length)
			{
				float[] dnaValues = activeDNA.Values;
				for (int i = 0; i < dnaValues.Length; i++)
				{
					float dnaValue = dnaValues[i];
					BonePoseSetDnaAsset.PosePair posePair = bonePoseSet.posePairs[i];
					if (dnaValue > 0.51f)
					{
						float poseWeight = (dnaValue - 0.5f) * 2f;
						if (posePair.poseOne != null)
							posePair.poseOne.ApplyPose(skeleton, poseWeight);
					}
					else if (dnaValue < 0.49f)
					{
						float poseWeight = (0.5f - dnaValue) * 2f;
						if (posePair.poseZero != null)
							posePair.poseZero.ApplyPose(skeleton, poseWeight);
					}
				}
			}
			else
			{
				// HACK
				// Need to update based on the dna asset version remap
			}
		}
	}
}
