using UnityEngine;
using UnityEngine.Serialization;

namespace UMA
{
	/// <summary>
	/// Base class for DNA converters.
	/// </summary>
	public class DnaConverterBehaviour : MonoBehaviour, IDNAConverter
	{
		public DnaConverterBehaviour()
		{
			Prepare();
		}

		[SerializeField]
		[FormerlySerializedAs("DNAType")]
		protected System.Type _dnaType;

        [SerializeField]
		[FormerlySerializedAs("DisplayValue")]
		protected string _displayValue;


		[SerializeField]
		protected int dnaTypeHash;

		/// <summary>
		/// Called on the DNA converter to adjust avatar from DNA values before the main ApplyDNA stage.
		/// </summary>
		[FormerlySerializedAs("PreApplyDnaAction")]
		protected DNAConvertDelegate _preApplyDnaAction;
		/// <summary>
		/// Called on the DNA converter to adjust avatar from DNA values.
		/// </summary>
		/// [FormerlySerializedAs("ApplyDnaAction")]
		protected DNAConvertDelegate _applyDnaAction;

		#region IDnaConverter IMPLIMENTATION

		public System.Type DNAType
		{
			get { return _dnaType; }
			set { _dnaType = value; }
		}

		public string DisplayValue
		{
			get { return _displayValue; }
			set { _displayValue = value; }
		}

		public virtual int DNATypeHash
		{
			set
			{
				dnaTypeHash = value;
			}
			get
			{
				if (dnaTypeHash == 0)
					dnaTypeHash = UMAUtils.StringToHash(DNAType.Name);
				return dnaTypeHash;
			}
		}

		public DNAConvertDelegate PreApplyDnaAction
		{
			get { return _preApplyDnaAction; }
			set { _preApplyDnaAction = value; }
		}

		public DNAConvertDelegate ApplyDnaAction
		{
			get { return _applyDnaAction; }
			set { _applyDnaAction = value; }
		}

		public virtual void Prepare()
		{
		}

		#endregion

		//public delegate void DNAConvertDelegate(UMAData data, UMASkeleton skeleton);

	}
}
