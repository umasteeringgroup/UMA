using UnityEngine;
using System;
using UMA.PoseTools;

namespace UMA
{
	/// <summary>
	/// DNA converter using a set of bone poses and/or blend shapes.
	/// </summary>
	public class MorphSetDnaConverterBehaviour : DynamicDNAConverterBehaviourBase
	{
		public MorphSetDnaAsset morphSet;

		public MorphSetDnaConverterBehaviour()
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
				if (morphSet != null)
					return morphSet.dnaTypeHash;
				
				return dnaTypeHash;
			}
		}

		public void ApplyDNA(UMAData data, UMASkeleton skeleton)
		{
			if (morphSet == null)
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

			if (morphSet.startingPose != null)
			{
				morphSet.startingPose.ApplyPose(skeleton, 1f);
			}
			if (!String.IsNullOrEmpty(morphSet.startingBlendShape))
			{
				data.SetBlendShape(morphSet.startingBlendShape, 1f);
			}

			if (activeDNA.Count == morphSet.dnaMorphs.Length)
			{
				float[] dnaValues = activeDNA.Values;
				for (int i = 0; i < dnaValues.Length; i++)
				{
					float dnaValue = dnaValues[i];
					MorphSetDnaAsset.DNAMorphSet morph = morphSet.dnaMorphs[i];

					ApplyMorph(dnaValue, data, skeleton, morph);
				}
			}
			else
			{
				Debug.LogWarning("DNA length mismatch, trying names. This is SLOW!");
				string[] dnaNames = activeDNA.Names;
				for (int i = 0; i < morphSet.dnaMorphs.Length; i++)
				{
					if (String.IsNullOrEmpty(morphSet.dnaMorphs[i].dnaEntryName))
						continue;
					
					int dnaIndex = System.Array.IndexOf(dnaNames, morphSet.dnaMorphs[i].dnaEntryName);
					if (dnaIndex < 0)
						continue;
					
					float dnaValue = activeDNA.GetValue(dnaIndex);
					MorphSetDnaAsset.DNAMorphSet morph = morphSet.dnaMorphs[i];

					ApplyMorph(dnaValue, data, skeleton, morph);
				}
			}
		}

		private void ApplyMorph(float dnaValue, UMAData data, UMASkeleton skeleton, MorphSetDnaAsset.DNAMorphSet morph)
		{
			if (dnaValue >= 0.5001f)
			{
				float morphWeight = (dnaValue - 0.5f) * 2f;
				if (morph.poseOne != null)
					morph.poseOne.ApplyPose(skeleton, morphWeight);
				if (!String.IsNullOrEmpty(morph.blendShapeOne))
					data.SetBlendShape(morph.blendShapeOne, morphWeight);

				if(!String.IsNullOrEmpty(morph.blendShapeZero))
					data.SetBlendShape(morph.blendShapeZero, 0f);
			}
			else if (dnaValue <= 0.4999f)
			{
				float morphWeight = (0.5f - dnaValue) * 2f;
				if (morph.poseZero != null)
					morph.poseZero.ApplyPose(skeleton, morphWeight);
				if (!String.IsNullOrEmpty(morph.blendShapeZero))
					data.SetBlendShape(morph.blendShapeZero, morphWeight);

				if (!String.IsNullOrEmpty(morph.blendShapeOne))
					data.SetBlendShape(morph.blendShapeOne, 0f);
			}
		}
	}
}
