using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
	/// <summary>
	/// DNA converter using a set of blendshapeDna assets.
	/// </summary>
	public class BlendShapeDnaConverterBehaviour : DynamicDNAConverterBehaviourBase //DnaConverterBehaviour 
	{
		[SerializeField]
		public BlendShapeDnaAsset blendShapeDna;

		public BlendShapeDnaConverterBehaviour()
		{
			ApplyDnaAction = ApplyDNA;
			DNAType = typeof(DynamicUMADna);
		}

		public override void Prepare()
		{
		}

		/// <summary>
		/// DNA Type hash for supported DNA
		/// </summary>
		/// <returns></returns>
		///
		public override int DNATypeHash
		{
			set {
				dnaTypeHash = value;
			}
			get
			{
				if (dnaAsset != null)
				{
					dnaTypeHash = dnaAsset.dnaTypeHash;
					return dnaTypeHash;
				}
				else
				{
					Debug.LogWarning("BlendShapeDnaConverterBehaviour " +this.name + " did not have a DNA Asset assigned.");
					return 0;
				}
			}
		}

		public void ApplyDNA(UMAData data, UMASkeleton skeleton)
		{
			if (dnaAsset == null) 
			{
				Debug.LogError ("BlendShapeDnaConverterBehaviour: dnaAsset not set!");
				return;
			}

			UMADnaBase activeDNA = data.GetDna (DNATypeHash);

			if (activeDNA == null)
			{
				Debug.LogError("Could not get DNA values for: "+ this.name);
				return;
			}

			//Make the DNAAssets match if they dont already...
			if(activeDNA != null)
			if (((DynamicUMADnaBase)activeDNA).dnaAsset != dnaAsset)
			{
				((DynamicUMADnaBase)activeDNA).dnaAsset = dnaAsset;
			}
				
			string[] dnaNames = activeDNA.Names;
			for (int i = 0; i < blendShapeDna.blendShapeDnaList.Length; i++) {
				if ((blendShapeDna.blendShapeDnaList [i].dnaEntryName == null) || (blendShapeDna.blendShapeDnaList [i].dnaEntryName.Length == 0))
					continue;

				int dnaIndex = System.Array.IndexOf (dnaNames, blendShapeDna.blendShapeDnaList [i].dnaEntryName);
				if (dnaIndex < 0) {
					continue;
				}

				float dnaValue = activeDNA.GetValue (dnaIndex);
				//dnaValue gets clamped between 0-1 in SetBlendShape
				data.SetBlendShape (blendShapeDna.blendShapeDnaList [i].blendShapeName, dnaValue);
			}
		}
	}
}
