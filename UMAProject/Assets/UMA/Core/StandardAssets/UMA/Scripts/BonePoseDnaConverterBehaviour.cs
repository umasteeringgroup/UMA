using UnityEngine;
using System.Collections;
using UMA.PoseTools;

namespace UMA
{
	/// <summary>
	/// DNA converter using a set of bone pose assets.
	/// </summary>
	public class BonePoseDnaConverterBehaviour : DnaConverterBehaviour
	{
		[System.Serializable]
		public class DNASizeAdjustment
		{
			public float heightRatio = 1f;
			public float massRatio = 1f;
			public float radiusRatio = 1f;
		}

		[System.Serializable]
		public class DNAPosePair
		{
			public string dnaEntryName;
			public UMABonePose poseZero;
			public UMABonePose poseOne;
			public DNASizeAdjustment sizeZero;
			public DNASizeAdjustment sizeOne;
		}
			
		[SerializeField]
		protected UMABonePose startingPose;

		[SerializeField]
		protected DNAPosePair[] dnaPoses;

		public BonePoseDnaConverterBehaviour()
		{
			ApplyDnaAction = ApplyDNA;
			DNAType = typeof(DynamicUMADna);

			dnaPoses = new DNAPosePair[1];
		}

		public override void Prepare()
		{
		}

		public void ApplyDNA(UMAData data, UMASkeleton skeleton)
		{
			UMADnaBase activeDNA = data.GetDna(this.dnaTypeHash);

			if (activeDNA == null)
			{
				Debug.LogError("Could not get DNA values for: "+ this.name);
				return;
			}

			if (startingPose != null)
			{
				startingPose.ApplyPose(skeleton, 1f);
			}

			if (activeDNA.Count == dnaPoses.Length)
			{
				float[] dnaValues = activeDNA.Values;
				for (int i = 0; i < dnaValues.Length; i++)
				{
					float dnaValue = dnaValues[i];
					if ((dnaValue > 0.5f) && (dnaPoses[i].poseOne != null))
					{
						float poseWeight = (dnaValue - 0.5f) * 2f;
						dnaPoses[i].poseOne.ApplyPose(skeleton, poseWeight);
					}
					else if ((dnaValue < 0.5f) && (dnaPoses[i].poseZero != null))
					{
						float poseWeight = (0.5f - dnaValue) * 2f;
						dnaPoses[i].poseOne.ApplyPose(skeleton, poseWeight);
					}
				}
			}
			else
			{
				Debug.LogWarning("DNA length mismatch, trying names. This is SLOW!");
				string[] dnaNames = activeDNA.Names;
				for (int i = 0; i < dnaPoses.Length; i++)
				{
					if ((dnaPoses[i].dnaEntryName == null) || (dnaPoses[i].dnaEntryName.Length == 0))
						continue;
					
					int dnaIndex = System.Array.IndexOf(dnaNames, dnaPoses[i].dnaEntryName);
					if (dnaIndex < 0)
						continue;
					
					float dnaValue = activeDNA.GetValue(dnaIndex);
					if ((dnaValue > 0.5f) && (dnaPoses[i].poseOne != null))
					{
						float poseWeight = (dnaValue - 0.5f) * 2f;
						dnaPoses[i].poseOne.ApplyPose(skeleton, poseWeight);
					}
					else if ((dnaValue < 0.5f) && (dnaPoses[i].poseZero != null))
					{
						float poseWeight = (0.5f - dnaValue) * 2f;
						dnaPoses[i].poseOne.ApplyPose(skeleton, poseWeight);
					}
				}
			}
		}
	}
}
