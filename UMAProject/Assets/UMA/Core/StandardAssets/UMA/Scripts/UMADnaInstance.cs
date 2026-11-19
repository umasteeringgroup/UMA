using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Legacy container for a <see cref="DNAInstanceCollection"/>. It is used for serialization of DNA values.
	/// This contains a reference to a DNAInstanceCollection which holds the actual DNA values.
    /// </summary>
    [System.Serializable]
	public class UMADnaInstance : UMADnaBase
	{
		public UMADnaInstance(DNAInstanceCollection dnaCollection)
		{
			DNAInstances = dnaCollection;
        }

		[SerializeField]
        public DNAInstanceCollection DNAInstances;
		public override int Count 
		{
			get { return DNAInstances.InstanceCount; }
		}


        public static UMADnaInstance LoadInstance(string data)
        {
            var dnac = UnityEngine.JsonUtility.FromJson<DNAInstanceCollection>(data);
            // Text saved by older versions can hold a group written as an instance ID. That number only
            // meant something in the session that wrote it; here it would point at nothing, or at some
            // other object. Groups are found again by DNA name.
            if (dnac != null && dnac.dnaInstances != null)
            {
                for (int i = 0; i < dnac.dnaInstances.Count; i++)
                {
                    if (dnac.dnaInstances[i] != null)
                    {
                        dnac.dnaInstances[i].parentGroup = null;
                    }
                }
            }
			return new UMADnaInstance(dnac);
        }

        public static string SaveInstance(UMADnaInstance instance)
        {
            // Save names, values and enabled flags only. JsonUtility writes an object reference as an
            // instance ID, so a group saved here would make the same character save differently in
            // every session and could not be read back in another one.
            var saved = new DNAInstanceCollection();
            var source = instance != null && instance.DNAInstances != null ? instance.DNAInstances.dnaInstances : null;
            if (source != null)
            {
                saved.dnaInstances.Capacity = source.Count;
                for (int i = 0; i < source.Count; i++)
                {
                    DNAInstance dna = source[i];
                    if (dna != null)
                    {
                        saved.dnaInstances.Add(new DNAInstance(dna.Name, dna.Value, null) { enabled = dna.enabled });
                    }
                }
            }
            return UnityEngine.JsonUtility.ToJson(saved);
        }

        public override float[] Values
		{
			get
			{
				return DNAInstances.GetValues();
			}
			set
			{
				DNAInstances.SetValues(value);
            }
        }

		public override string[] Names
		{
			get
			{
				return DNAInstances.GetNames();
			}
		}

		public override float GetValue(int idx)
        {
			if (idx < 0 || idx >= DNAInstances.InstanceCount)
			{
				return 0.0f;
			}
			return DNAInstances.dnaInstances[idx].Value;
        }

		public override void SetValue(int idx, float value)
        {
			if (idx < 0 || idx >= DNAInstances.InstanceCount)
			{
				return;
			}
			DNAInstances.dnaInstances[idx].Value = value;
        }

		public void SetValue(string name, float value)
		{
			for (int i = 0; i < DNAInstances.InstanceCount; i++)
			{
				if (DNAInstances.dnaInstances[i].Name == name)
				{
					DNAInstances.dnaInstances[i].Value = value;
					return;
				}
			}
        }

        public float GetValue(string name)
		{
			for (int i = 0; i < DNAInstances.InstanceCount; i++)
			{
				if (DNAInstances.dnaInstances[i].Name == name)
				{
					return DNAInstances.dnaInstances[i].Value;
				}
			}
			return 0f;
		}

        public override int DNATypeHash
        {
            get { return dnaTypeHash; }
			set { dnaTypeHash = 0; }	
        }
	}
}
