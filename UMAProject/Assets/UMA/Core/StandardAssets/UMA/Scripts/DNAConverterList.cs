using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// A list that can hold both DnaConverterBahviours (legacy) and DynamicDNAConverterControllers or any other type that impliments IDNAConverter interface
    /// It returns entries as IDNAConverters
    /// </summary>
    [System.Serializable]
	public class DNAConverterList
	{
		//Hmmm not sure this Object list is going to work because in 'Debug Mode' you can anything you like in there
		//Also the prefab gets added as a prefab rather than the component on it
		//We can Validate but that might be slow
		[SerializeField]
		//	private List<UnityEngine.Object> _converters = new List<Object>();
		private List<DynamicDNAConverterController> _converters = new List<DynamicDNAConverterController>();


		#region CONSTRUCTOR

		public DNAConverterList()
		{
		}

		public DNAConverterList(DNAConverterList other)
		{
			other.Validate();
			_converters = new List<DynamicDNAConverterController>(other._converters);
		}

		public DNAConverterList(DynamicDNAConverterController[] dnaConverters)
		{
			_converters.Clear();
			for (int i = 0; i < dnaConverters.Length; i++)
			{
				_converters.Add(dnaConverters[i]);
			}
		}

		public DNAConverterList(List<DynamicDNAConverterController> dnaConverters)
		{
			_converters.Clear();
			for (int i = 0; i < dnaConverters.Count; i++)
			{
				_converters.Add(dnaConverters[i]);
			}
		}
		#endregion

		private void Validate()
		{
			/*
			List<UnityEngine.Object> validConverters = new List<Object>();
			for(int i = 0; i < _converters.Count; i++)
			{
				if (_converters[i] is IDNAConverter)
					validConverters.Add(_converters[i]);
			}
			_converters = validConverters; */
		}

		#region PASSTHRU LIST METHODS

		public DynamicDNAConverterController this[int key]
		{
			get {
				if (_converters[key] is IDNAConverter)//will this check be fast?
                {
                    return _converters[key];
                }
                else
                {
                    return null;
                }
            }
			set {
				_converters[key] = value;
			}
		}

		public int Length
		{
			get {
				Validate();//will this be fast enough?
				return _converters.Count;
			}
		}

		public int Count
		{
			get {
				Validate();//will this be fast enough?
				return _converters.Count;
			}
		}

		/// <summary>
		/// Adds the given object to the converters list. The object must inherit from IDNAConverter in order to be added
		/// </summary>
		/// <param name="converter"></param>
	/*	public void Add(UnityEngine.Object converter)
		{
			if (converter == null)
				return;
			if (converter.GetType() == typeof(GameObject))
			{
				var idc = (converter as GameObject).GetComponent<IDNAConverter>();
				if (idc != null)
					converter = idc as DynamicDNAConverterController;
				else
					converter = null;
			}
			if (converter is DynamicDNAConverterController && !_converters.Contains(converter as DynamicDNAConverterController))
				_converters.Add(converter as DynamicDNAConverterController);
		}

		public void Remove(DynamicDNAConverterController converter)
		{
			if (converter == null)
				return;
			if (converter.GetType() == typeof(GameObject))
			{
				var idc = (converter as GameObject).GetComponent<IDNAConverter>();
				if (idc != null)
					converter = idc as UnityEngine.Object;
				else
					converter = null;
			}
			if (_converters.Contains(converter as DynamicDNAConverterController))
				_converters.Remove(converter as DynamicDNAConverterController);
		} */

		public void Add(DynamicDNAConverterController converter)
		{
			if (converter == null)
            {
                return;
            }

            if (!_converters.Contains(converter))
            {
                _converters.Add(converter);
            }
        }

		/*
		public void Remove(IDNAConverter converter)
		{
			if (converter == null)
				return;
			if (_converters.Contains(converter as UnityEngine.Object))
				_converters.Remove(converter as UnityEngine.Object);
		}*/

		public void AddRange(IEnumerable<DynamicDNAConverterController> converters)
		{
			foreach (DynamicDNAConverterController converter in converters)
            {
                Add(converter);
            }
        }

		public bool Contains(DynamicDNAConverterController converter)
		{
			return _converters.Contains(converter);
		}


		public void Clear()
		{
			_converters.Clear();
		}

		public int IndexOf(UnityEngine.Object converter)
		{
			if (converter == null)
            {
                return -1;
            }

            if (converter.GetType() == typeof(GameObject))
			{
				var idc = (converter as GameObject).GetComponent<IDNAConverter>();
				if (idc != null)
                {
                    converter = idc as UnityEngine.Object;
                }
                else
                {
                    converter = null;
                }
            }
			Validate();//will this be fast enough?
			for (int i = 0; i < _converters.Count; i++)
			{
				if (_converters[i] == converter)
                {
                    return i;
                }
            }
			return -1;
		}


		public DynamicDNAConverterController[] ToArray()
		{
			return _converters.ToArray();
		}

		#endregion

	}
}
