using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA {

	/// <summary>
	/// A field that can hold DNAConverters that use the IDNAConverter interface
	/// </summary>
	[System.Serializable]
	public class DNAConverterField {

		[SerializeField]
		private UnityEngine.Object _converter;

		public IDNAConverter Value
		{
			get {
				Validate();
				if (_converter == null)
					return null;
				return _converter as IDNAConverter;
			}
			set { _converter = value as UnityEngine.Object; }
		}

		private void Validate()
		{
			if(!(_converter is IDNAConverter))
			{
				_converter = null;
			}
		}
	}
}
