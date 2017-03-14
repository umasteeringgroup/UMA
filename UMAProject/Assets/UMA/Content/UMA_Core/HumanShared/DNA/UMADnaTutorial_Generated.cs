// UMA Auto genered code, DO NOT MODIFY!!!
// All changes to this file will be destroyed without warning or confirmation!
// Use double { to escape a single curly bracket
//
// template junk executed per dna Field , the accumulated content is available through the {0:ID} tag
//
//#TEMPLATE GetValues UmaDnaChild_GetIndex_Fragment.cs.txt
//#TEMPLATE SetValues UmaDnaChild_SetIndex_Fragment.cs.txt
//#TEMPLATE GetValue UmaDnaChild_GetValue_Fragment.cs.txt
//#TEMPLATE SetValue UmaDnaChild_SetValue_Fragment.cs.txt
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
	public partial class UMADnaTutorial
	{
		public override int Count { get { return 1; } }
		public override float[] Values
		{ 
			get 
			{
				return new float[] 
				{
					eyeSpacing,

				};
			}
			set
			{
				eyeSpacing = value[0];

			}
		}
		public override float GetValue(int idx)
		{
			switch(idx)
			{
				case 0: return eyeSpacing;

			}
			throw new System.ArgumentOutOfRangeException();
		}
		public override void SetValue(int idx, float value)
		{
			switch(idx)
			{
                case 0: eyeSpacing = value; return;

			}
			throw new System.ArgumentOutOfRangeException();
		}

		public static string[] GetNames()
		{
			return new string[]
			{
				"eyeSpacing",

			};
		}
		public override string[] Names
		{
			get
			{
				return GetNames();
			}
		}
		public static UMADnaTutorial LoadInstance(string data)
	    {
	        return UnityEngine.JsonUtility.FromJson<UMADnaTutorial_Byte>(data).ToDna();
	    }
		public static string SaveInstance(UMADnaTutorial instance)
		{
			return UnityEngine.JsonUtility.ToJson(UMADnaTutorial_Byte.FromDna(instance));
		}
	}

	[System.Serializable]
	public class UMADnaTutorial_Byte
	{
			public System.Byte eyeSpacing;


		public UMADnaTutorial ToDna()
		{
			var res = new UMADnaTutorial();
			res.eyeSpacing = eyeSpacing * (1f / 255f);

			return res;
		}
		public static UMADnaTutorial_Byte FromDna(UMADnaTutorial dna)
		{
			var res = new UMADnaTutorial_Byte();
			res.eyeSpacing = (System.Byte)(dna.eyeSpacing * 255f+0.5f);

			return res;
		}
	}
}
