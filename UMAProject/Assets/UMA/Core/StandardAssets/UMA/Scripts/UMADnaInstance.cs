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
			return new UMADnaInstance(dnac);
        }

        public static string SaveInstance(UMADnaInstance instance)
        {
            return UnityEngine.JsonUtility.ToJson(instance.DNAInstances);
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

        [SerializeField]
        public override int DNATypeHash
        {
            get { return dnaTypeHash; }
			set { dnaTypeHash = 0; }	
        }
	}
}
