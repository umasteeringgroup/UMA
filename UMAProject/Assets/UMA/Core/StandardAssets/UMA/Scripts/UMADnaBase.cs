using System;
using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Base class for UMA DNA.
	/// </summary>
	[System.Serializable]
	public abstract class UMADnaBase 
	{
		public static UMADnaBase CreateInstance(Type dnaType)
		{
			if (dnaType == null)
			{
				Debug.LogError("Unable to create UMA DNA: type is null.");
				return null;
			}

			if (!typeof(UMADnaBase).IsAssignableFrom(dnaType) || dnaType.IsAbstract || dnaType.IsGenericType)
			{
				Debug.LogError($"Unable to create UMA DNA: '{dnaType.FullName}' is not a concrete UMADnaBase type.");
				return null;
			}

			try
			{
				return Activator.CreateInstance(dnaType) as UMADnaBase;
			}
			catch (Exception e)
			{
				Debug.LogError($"Unable to create UMA DNA type '{dnaType.FullName}': {e.Message}");
				return null;
			}
		}

		public static T CreateInstance<T>() where T : UMADnaBase
		{
			return CreateInstance(typeof(T)) as T;
		}

		public static UMADnaBase CreateInstance(IDNAConverter converter)
		{
			if (converter == null)
			{
				Debug.LogError("Unable to create UMA DNA: converter is null.");
				return null;
			}

			UMADnaBase dna = CreateInstance(converter.DNAType);
			if (dna != null)
			{
				dna.Initialize(converter);
			}

			return dna;
		}

		public static string[] GetNames(IDNAConverter converter)
		{
			UMADnaBase dna = CreateInstance(converter);
			return dna != null && dna.Names != null ? dna.Names : Array.Empty<string>();
		}

		public static int GetCount(IDNAConverter converter)
		{
			UMADnaBase dna = CreateInstance(converter);
			return dna != null ? dna.Count : 0;
		}

		public virtual void Initialize(IDNAConverter converter)
		{
			if (converter == null)
			{
				return;
			}

			DNATypeHash = converter.DNATypeHash;
		}

		public virtual int Count { get; }
		public virtual float[] Values
		{
			get; set;
		}

		public virtual string[] Names
		{
			get;
		}

		public virtual float GetValue(int idx)
        {
			return 0.0f;
        }

		public virtual void SetValue(int idx, float value)
        {
			return;
        }

		[SerializeField]
		protected int dnaTypeHash;
        public abstract int DNATypeHash
        {
            get;
			set;
        }
	}
}
