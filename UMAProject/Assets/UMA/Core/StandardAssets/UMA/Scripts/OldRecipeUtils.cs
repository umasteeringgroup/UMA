using UnityEngine;

namespace UMA
{
	public class OldRecipeUtils : MonoBehaviour
	{
		public static UMADnaBase LoadInstance(System.String className, System.String data)
		{
			if (className == "UMADnaHumanoid")
			{
				return UnityEngine.JsonUtility.FromJson<UMADnaHumanoid_Byte>(data).ToDna();
			}
			if (className == "UMADnaTutorial")
			{
				return UnityEngine.JsonUtility.FromJson<UMADnaTutorial_Byte>(data).ToDna();
			}

			return null;
		}

		[System.Serializable]
		public class UMADnaHumanoid_Byte
		{
			public System.Byte height;
			public System.Byte headSize;
			public System.Byte headWidth;
			public System.Byte neckThickness;
			public System.Byte armLength;
			public System.Byte forearmLength;
			public System.Byte armWidth;
			public System.Byte forearmWidth;
			public System.Byte handsSize;
			public System.Byte feetSize;
			public System.Byte legSeparation;
			public System.Byte upperMuscle;
			public System.Byte lowerMuscle;
			public System.Byte upperWeight;
			public System.Byte lowerWeight;
			public System.Byte legsSize;
			public System.Byte belly;
			public System.Byte waist;
			public System.Byte gluteusSize;
			public System.Byte earsSize;
			public System.Byte earsPosition;
			public System.Byte earsRotation;
			public System.Byte noseSize;
			public System.Byte noseCurve;
			public System.Byte noseWidth;
			public System.Byte noseInclination;
			public System.Byte nosePosition;
			public System.Byte nosePronounced;
			public System.Byte noseFlatten;
			public System.Byte chinSize;
			public System.Byte chinPronounced;
			public System.Byte chinPosition;
			public System.Byte mandibleSize;
			public System.Byte jawsSize;
			public System.Byte jawsPosition;
			public System.Byte cheekSize;
			public System.Byte cheekPosition;
			public System.Byte lowCheekPronounced;
			public System.Byte lowCheekPosition;
			public System.Byte foreheadSize;
			public System.Byte foreheadPosition;
			public System.Byte lipsSize;
			public System.Byte mouthSize;
			public System.Byte eyeRotation;
			public System.Byte eyeSize;
			public System.Byte breastSize;

			public UMADnaBase ToDna()
			{
				var dna = UMAContextBase.Instance.InstantiateDNA("UMADnaHumanoid");

				if (dna != null)
				{
					// HACK - this is crazy inefficient
					dna.SetValue("height", height * (1f / 255f));
					dna.SetValue("headSize", headSize * (1f / 255f));
					dna.SetValue("headWidth", headWidth * (1f / 255f));
					dna.SetValue("neckThickness", neckThickness * (1f / 255f));
					dna.SetValue("armLength", armLength * (1f / 255f));
					dna.SetValue("forearmLength", forearmLength * (1f / 255f));
					dna.SetValue("armWidth", armWidth * (1f / 255f));
					dna.SetValue("forearmWidth", forearmWidth * (1f / 255f));
					dna.SetValue("handsSize", handsSize * (1f / 255f));
					dna.SetValue("feetSize", feetSize * (1f / 255f));
					dna.SetValue("legSeparation", legSeparation * (1f / 255f));
					dna.SetValue("upperMuscle", upperMuscle * (1f / 255f));
					dna.SetValue("lowerMuscle", lowerMuscle * (1f / 255f));
					dna.SetValue("upperWeight", upperWeight * (1f / 255f));
					dna.SetValue("lowerWeight", lowerWeight * (1f / 255f));
					dna.SetValue("legsSize", legsSize * (1f / 255f));
					dna.SetValue("belly", belly * (1f / 255f));
					dna.SetValue("waist", waist * (1f / 255f));
					dna.SetValue("gluteusSize", gluteusSize * (1f / 255f));
					dna.SetValue("earsSize", earsSize * (1f / 255f));
					dna.SetValue("earsPosition", earsPosition * (1f / 255f));
					dna.SetValue("earsRotation", earsRotation * (1f / 255f));
					dna.SetValue("noseSize", noseSize * (1f / 255f));
					dna.SetValue("noseCurve", noseCurve * (1f / 255f));
					dna.SetValue("noseWidth", noseWidth * (1f / 255f));
					dna.SetValue("noseInclination", noseInclination * (1f / 255f));
					dna.SetValue("nosePosition", nosePosition * (1f / 255f));
					dna.SetValue("nosePronounced", nosePronounced * (1f / 255f));
					dna.SetValue("noseFlatten", noseFlatten * (1f / 255f));
					dna.SetValue("chinSize", chinSize * (1f / 255f));
					dna.SetValue("chinPronounced", chinPronounced * (1f / 255f));
					dna.SetValue("chinPosition", chinPosition * (1f / 255f));
					dna.SetValue("mandibleSize", mandibleSize * (1f / 255f));
					dna.SetValue("jawsSize", jawsSize * (1f / 255f));
					dna.SetValue("jawsPosition", jawsPosition * (1f / 255f));
					dna.SetValue("cheekSize", cheekSize * (1f / 255f));
					dna.SetValue("cheekPosition", cheekPosition * (1f / 255f));
					dna.SetValue("lowCheekPronounced", lowCheekPronounced * (1f / 255f));
					dna.SetValue("lowCheekPosition", lowCheekPosition * (1f / 255f));
					dna.SetValue("foreheadSize", foreheadSize * (1f / 255f));
					dna.SetValue("foreheadPosition", foreheadPosition * (1f / 255f));
					dna.SetValue("lipsSize", lipsSize * (1f / 255f));
					dna.SetValue("mouthSize", mouthSize * (1f / 255f));
					dna.SetValue("eyeRotation", eyeRotation * (1f / 255f));
					dna.SetValue("eyeSize", eyeSize * (1f / 255f));
					dna.SetValue("breastSize", breastSize * (1f / 255f));
				}

				return dna;
			}
		}

		[System.Serializable]
		public class UMADnaTutorial_Byte
		{
			public System.Byte eyeSpacing;

			public UMADnaBase ToDna()
			{
				var dna = UMAContextBase.Instance.InstantiateDNA("UMADnaTutorial");

				if (dna != null)
				{
					// HACK - this is crazy inefficient
					dna.SetValue("eyeSpacing", eyeSpacing * (1f / 255f));
				}

				return dna;
			}
		}
	}
}
