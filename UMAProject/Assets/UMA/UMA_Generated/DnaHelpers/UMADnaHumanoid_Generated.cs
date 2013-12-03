// UMA Auto genered code, DO NOT MODIFY!!!
// All changes to this file will be destroyed without warning or confirmation!
// Use double { to escape a single curly bracket
//
// template junk executed per dna Field , the accumulated content is available through the {0:ID} tag
//
//#TEMPLATE GetValues UmaDnaChild_GetIndex_Fragment.cs.txt
//#TEMPLATE SetValues UmaDnaChild_SetIndex_Fragment.cs.txt
//#TEMPLATE GetNames UmaDnaChild_GetNames_Fragment.cs.txt
//
// Byte Serialization Handling
// 
//#TEMPLATE Byte_Fields UmaDnaChild_Byte_Fields_Fragment.cs.txt
//#TEMPLATE Byte_ToDna UmaDnaChild_Byte_ToDna_Fragment.cs.txt
//#TEMPLATE Byte_FromDna UmaDnaChild_Byte_FromDna_Fragment.cs.txt
//

namespace UMA
{
	public partial class UMADnaHumanoid
	{
		public override int Count { get { return 46; } }
		public override float[] Values
		{ 
			get 
			{
				return new float[] 
				{
					height,
				headSize,
				headWidth,
				neckThickness,
				armLength,
				forearmLength,
				armWidth,
				forearmWidth,
				handsSize,
				feetSize,
				legSeparation,
				upperMuscle,
				lowerMuscle,
				upperWeight,
				lowerWeight,
				legsSize,
				belly,
				waist,
				gluteusSize,
				earsSize,
				earsPosition,
				earsRotation,
				noseSize,
				noseCurve,
				noseWidth,
				noseInclination,
				nosePosition,
				nosePronounced,
				noseFlatten,
				chinSize,
				chinPronounced,
				chinPosition,
				mandibleSize,
				jawsSize,
				jawsPosition,
				cheekSize,
				cheekPosition,
				lowCheekPronounced,
				lowCheekPosition,
				foreheadSize,
				foreheadPosition,
				lipsSize,
				mouthSize,
				eyeRotation,
				eyeSize,
				breastSize,

				};
			}
			set
			{
				height = value[0];
			headSize = value[1];
			headWidth = value[2];
			neckThickness = value[3];
			armLength = value[4];
			forearmLength = value[5];
			armWidth = value[6];
			forearmWidth = value[7];
			handsSize = value[8];
			feetSize = value[9];
			legSeparation = value[10];
			upperMuscle = value[11];
			lowerMuscle = value[12];
			upperWeight = value[13];
			lowerWeight = value[14];
			legsSize = value[15];
			belly = value[16];
			waist = value[17];
			gluteusSize = value[18];
			earsSize = value[19];
			earsPosition = value[20];
			earsRotation = value[21];
			noseSize = value[22];
			noseCurve = value[23];
			noseWidth = value[24];
			noseInclination = value[25];
			nosePosition = value[26];
			nosePronounced = value[27];
			noseFlatten = value[28];
			chinSize = value[29];
			chinPronounced = value[30];
			chinPosition = value[31];
			mandibleSize = value[32];
			jawsSize = value[33];
			jawsPosition = value[34];
			cheekSize = value[35];
			cheekPosition = value[36];
			lowCheekPronounced = value[37];
			lowCheekPosition = value[38];
			foreheadSize = value[39];
			foreheadPosition = value[40];
			lipsSize = value[41];
			mouthSize = value[42];
			eyeRotation = value[43];
			eyeSize = value[44];
			breastSize = value[45];

			}
		}
		public static string[] GetNames()
		{
			return new string[]
			{
				"height",
			"headSize",
			"headWidth",
			"neckThickness",
			"armLength",
			"forearmLength",
			"armWidth",
			"forearmWidth",
			"handsSize",
			"feetSize",
			"legSeparation",
			"upperMuscle",
			"lowerMuscle",
			"upperWeight",
			"lowerWeight",
			"legsSize",
			"belly",
			"waist",
			"gluteusSize",
			"earsSize",
			"earsPosition",
			"earsRotation",
			"noseSize",
			"noseCurve",
			"noseWidth",
			"noseInclination",
			"nosePosition",
			"nosePronounced",
			"noseFlatten",
			"chinSize",
			"chinPronounced",
			"chinPosition",
			"mandibleSize",
			"jawsSize",
			"jawsPosition",
			"cheekSize",
			"cheekPosition",
			"lowCheekPronounced",
			"lowCheekPosition",
			"foreheadSize",
			"foreheadPosition",
			"lipsSize",
			"mouthSize",
			"eyeRotation",
			"eyeSize",
			"breastSize",

			};
		}
		public override string[] Names
		{
			get
			{
				return GetNames();
			}
		}
		public static UMADnaHumanoid LoadInstance(string data)
	    {
	        return LitJson.JsonMapper.ToObject<UMADnaHumanoid_Byte>(data).ToDna();
	    }
		public static string SaveInstance(UMADnaHumanoid instance)
		{
			return LitJson.JsonMapper.ToJson(UMADnaHumanoid_Byte.FromDna(instance));
		}
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


		public UMADnaHumanoid ToDna()
		{
			var res = new UMADnaHumanoid();
			res.height = height * (1f / 255f);
		res.headSize = headSize * (1f / 255f);
		res.headWidth = headWidth * (1f / 255f);
		res.neckThickness = neckThickness * (1f / 255f);
		res.armLength = armLength * (1f / 255f);
		res.forearmLength = forearmLength * (1f / 255f);
		res.armWidth = armWidth * (1f / 255f);
		res.forearmWidth = forearmWidth * (1f / 255f);
		res.handsSize = handsSize * (1f / 255f);
		res.feetSize = feetSize * (1f / 255f);
		res.legSeparation = legSeparation * (1f / 255f);
		res.upperMuscle = upperMuscle * (1f / 255f);
		res.lowerMuscle = lowerMuscle * (1f / 255f);
		res.upperWeight = upperWeight * (1f / 255f);
		res.lowerWeight = lowerWeight * (1f / 255f);
		res.legsSize = legsSize * (1f / 255f);
		res.belly = belly * (1f / 255f);
		res.waist = waist * (1f / 255f);
		res.gluteusSize = gluteusSize * (1f / 255f);
		res.earsSize = earsSize * (1f / 255f);
		res.earsPosition = earsPosition * (1f / 255f);
		res.earsRotation = earsRotation * (1f / 255f);
		res.noseSize = noseSize * (1f / 255f);
		res.noseCurve = noseCurve * (1f / 255f);
		res.noseWidth = noseWidth * (1f / 255f);
		res.noseInclination = noseInclination * (1f / 255f);
		res.nosePosition = nosePosition * (1f / 255f);
		res.nosePronounced = nosePronounced * (1f / 255f);
		res.noseFlatten = noseFlatten * (1f / 255f);
		res.chinSize = chinSize * (1f / 255f);
		res.chinPronounced = chinPronounced * (1f / 255f);
		res.chinPosition = chinPosition * (1f / 255f);
		res.mandibleSize = mandibleSize * (1f / 255f);
		res.jawsSize = jawsSize * (1f / 255f);
		res.jawsPosition = jawsPosition * (1f / 255f);
		res.cheekSize = cheekSize * (1f / 255f);
		res.cheekPosition = cheekPosition * (1f / 255f);
		res.lowCheekPronounced = lowCheekPronounced * (1f / 255f);
		res.lowCheekPosition = lowCheekPosition * (1f / 255f);
		res.foreheadSize = foreheadSize * (1f / 255f);
		res.foreheadPosition = foreheadPosition * (1f / 255f);
		res.lipsSize = lipsSize * (1f / 255f);
		res.mouthSize = mouthSize * (1f / 255f);
		res.eyeRotation = eyeRotation * (1f / 255f);
		res.eyeSize = eyeSize * (1f / 255f);
		res.breastSize = breastSize * (1f / 255f);

			return res;
		}
		public static UMADnaHumanoid_Byte FromDna(UMADnaHumanoid dna)
		{
			var res = new UMADnaHumanoid_Byte();
			res.height = (System.Byte)(dna.height * 255f+0.5f);
		res.headSize = (System.Byte)(dna.headSize * 255f+0.5f);
		res.headWidth = (System.Byte)(dna.headWidth * 255f+0.5f);
		res.neckThickness = (System.Byte)(dna.neckThickness * 255f+0.5f);
		res.armLength = (System.Byte)(dna.armLength * 255f+0.5f);
		res.forearmLength = (System.Byte)(dna.forearmLength * 255f+0.5f);
		res.armWidth = (System.Byte)(dna.armWidth * 255f+0.5f);
		res.forearmWidth = (System.Byte)(dna.forearmWidth * 255f+0.5f);
		res.handsSize = (System.Byte)(dna.handsSize * 255f+0.5f);
		res.feetSize = (System.Byte)(dna.feetSize * 255f+0.5f);
		res.legSeparation = (System.Byte)(dna.legSeparation * 255f+0.5f);
		res.upperMuscle = (System.Byte)(dna.upperMuscle * 255f+0.5f);
		res.lowerMuscle = (System.Byte)(dna.lowerMuscle * 255f+0.5f);
		res.upperWeight = (System.Byte)(dna.upperWeight * 255f+0.5f);
		res.lowerWeight = (System.Byte)(dna.lowerWeight * 255f+0.5f);
		res.legsSize = (System.Byte)(dna.legsSize * 255f+0.5f);
		res.belly = (System.Byte)(dna.belly * 255f+0.5f);
		res.waist = (System.Byte)(dna.waist * 255f+0.5f);
		res.gluteusSize = (System.Byte)(dna.gluteusSize * 255f+0.5f);
		res.earsSize = (System.Byte)(dna.earsSize * 255f+0.5f);
		res.earsPosition = (System.Byte)(dna.earsPosition * 255f+0.5f);
		res.earsRotation = (System.Byte)(dna.earsRotation * 255f+0.5f);
		res.noseSize = (System.Byte)(dna.noseSize * 255f+0.5f);
		res.noseCurve = (System.Byte)(dna.noseCurve * 255f+0.5f);
		res.noseWidth = (System.Byte)(dna.noseWidth * 255f+0.5f);
		res.noseInclination = (System.Byte)(dna.noseInclination * 255f+0.5f);
		res.nosePosition = (System.Byte)(dna.nosePosition * 255f+0.5f);
		res.nosePronounced = (System.Byte)(dna.nosePronounced * 255f+0.5f);
		res.noseFlatten = (System.Byte)(dna.noseFlatten * 255f+0.5f);
		res.chinSize = (System.Byte)(dna.chinSize * 255f+0.5f);
		res.chinPronounced = (System.Byte)(dna.chinPronounced * 255f+0.5f);
		res.chinPosition = (System.Byte)(dna.chinPosition * 255f+0.5f);
		res.mandibleSize = (System.Byte)(dna.mandibleSize * 255f+0.5f);
		res.jawsSize = (System.Byte)(dna.jawsSize * 255f+0.5f);
		res.jawsPosition = (System.Byte)(dna.jawsPosition * 255f+0.5f);
		res.cheekSize = (System.Byte)(dna.cheekSize * 255f+0.5f);
		res.cheekPosition = (System.Byte)(dna.cheekPosition * 255f+0.5f);
		res.lowCheekPronounced = (System.Byte)(dna.lowCheekPronounced * 255f+0.5f);
		res.lowCheekPosition = (System.Byte)(dna.lowCheekPosition * 255f+0.5f);
		res.foreheadSize = (System.Byte)(dna.foreheadSize * 255f+0.5f);
		res.foreheadPosition = (System.Byte)(dna.foreheadPosition * 255f+0.5f);
		res.lipsSize = (System.Byte)(dna.lipsSize * 255f+0.5f);
		res.mouthSize = (System.Byte)(dna.mouthSize * 255f+0.5f);
		res.eyeRotation = (System.Byte)(dna.eyeRotation * 255f+0.5f);
		res.eyeSize = (System.Byte)(dna.eyeSize * 255f+0.5f);
		res.breastSize = (System.Byte)(dna.breastSize * 255f+0.5f);

			return res;
		}
	}
}