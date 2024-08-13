using System.Collections.Generic;
using System.Linq;
using System;
#if UNITY_EDITOR
#endif
namespace UMA
{
    [Serializable]
	public class DnaValue
	{
		public string Name;
		public float Value;
		public DnaValue(string name, float value)
		{
			Name = name;
			Value = value;
		}
	}

	[Serializable]
	public class UMAPredefinedDNA
	{
		/// <summary>
		/// This class is used for preloading DNA on DynamicCharacterAvatars  
		/// </summary>
		/// <remarks>
		/// UMAPredefinedDNA is assigned to a DynamicCharacterAvatar to allow it to preload DNA when it is built.
		/// Without this, it is not possible to pregenerate random DNA because the DNA is loaded from the Race at build time.
		/// After the UMAData is created, this DNA will be applied to the UMA as part of the build process, so you don't have
		/// to build the DCA twice to get randomized data/.
		/// </remarks>

		public List<DnaValue> PreloadValues = new List<DnaValue>();

		public int Count
        {
			get
            {
				return PreloadValues.Count;
            }
        }

		public void RemoveDNA(string Name)
		{
			PreloadValues.RemoveAll(x => x.Name == Name);
		}

		public bool ContainsName(string Name)
        {
			return PreloadValues.Count(x => x.Name == Name) > 0;
        }
		public void AddRange(UMAPredefinedDNA newDNA)
		{
			PreloadValues.AddRange(newDNA.PreloadValues);
		}

		public void AddDNA(string Name, float Value)
		{
            for (int i = 0; i < PreloadValues.Count; i++)
            {
                DnaValue value = PreloadValues[i];
                if (value.Name.Equals(Name))
                {
                    value.Value = Value;
                    return; 
                }
            }
            // If not found, add new one
			PreloadValues.Add(new DnaValue(Name, Value));
		}
		public void Clear()
        {
			PreloadValues.Clear();
        }

        public float GetValue(string Name)
        {
            if (ContainsName(Name))
            {
                for (int i = 0; i < PreloadValues.Count; i++)
                {
                    DnaValue value = PreloadValues[i];
                    if (value.Name == Name)
                    {
                        return value.Value;
                    }
                }
            }
            return 0;
        }

		public UMAPredefinedDNA Clone()
        {
			UMAPredefinedDNA newdna = new UMAPredefinedDNA();
            for (int i = 0; i < PreloadValues.Count; i++)
            {
                DnaValue d = PreloadValues[i];
                newdna.AddDNA(d.Name, d.Value);
            }
			return newdna;
        }
	}
}
