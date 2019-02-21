using System.Collections;
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
		private List<UnityEngine.Object> _converters = new List<Object>();

		#region CONSTRUCTOR

		public DNAConverterList()
		{
		}

		public DNAConverterList(DNAConverterList other)
		{
			other.Validate();
			_converters = new List<Object>(other._converters);
		}

		public DNAConverterList(IDNAConverter[] dnaConverters)
		{
			_converters.Clear();
			for (int i = 0; i < dnaConverters.Length; i++)
			{
				_converters.Add(dnaConverters[i] as UnityEngine.Object);
			}
		}

		public DNAConverterList(List<IDNAConverter> dnaConverters)
		{
			_converters.Clear();
			for (int i = 0; i < dnaConverters.Count; i++)
			{
				_converters.Add(dnaConverters[i] as UnityEngine.Object);
			}
		}
		#endregion

		private void Validate()
		{
			List<UnityEngine.Object> validConverters = new List<Object>();
			for(int i = 0; i < _converters.Count; i++)
			{
				if (_converters[i] is IDNAConverter)
					validConverters.Add(_converters[i]);
			}
			_converters = validConverters;
		}

		#region PASSTHRU LIST METHODS

		public IDNAConverter this[int key]
		{
			get {
				if (_converters[key] is IDNAConverter)//will this check be fast?
					return _converters[key] as IDNAConverter;
				else
					return null;
			}
			set {
				_converters[key] = value as UnityEngine.Object;
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
		public void Add(UnityEngine.Object converter)
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
			if (converter is IDNAConverter && !_converters.Contains(converter as UnityEngine.Object))
				_converters.Add(converter as UnityEngine.Object);
		}

		public void Remove(UnityEngine.Object converter)
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
			if (_converters.Contains(converter))
				_converters.Remove(converter);
		}

		public void Add(IDNAConverter converter)
		{
			if (converter == null)
				return;
			if (!_converters.Contains(converter as UnityEngine.Object))
			_converters.Add(converter as UnityEngine.Object);
		}

		public void Remove(IDNAConverter converter)
		{
			if (converter == null)
				return;
			if (_converters.Contains(converter as UnityEngine.Object))
				_converters.Remove(converter as UnityEngine.Object);
		}

		public void AddRange(IEnumerable<IDNAConverter> converters)
		{
			foreach (IDNAConverter converter in converters)
				Add(converter);
		}

		public bool Contains(UnityEngine.Object converter)
		{
			if (converter == null)
				return false;
			if (converter.GetType() == typeof(GameObject))
			{
				var idc = (converter as GameObject).GetComponent<IDNAConverter>();
				if (idc != null)
					converter = idc as UnityEngine.Object;
				else
					converter = null;
			}
			if (converter == null)
				return false;
			return _converters.Contains(converter);
		}

		public bool Contains(IDNAConverter converter)
		{
			if (converter == null)
				return false;
			return _converters.Contains(converter as UnityEngine.Object);
		}

		public bool Contains(DnaConverterBehaviour converter)
		{
			if (converter == null)
				return false;
			for(int i = 0; i < _converters.Count; i++)
			{
				if (_converters[i] is DnaConverterBehaviour && _converters[i] == converter)
					return true;
			}
			return false;
		}

		public void Clear()
		{
			_converters.Clear();
		}

		public int IndexOf(UnityEngine.Object converter)
		{
			if (converter == null)
				return -1;
			if (converter.GetType() == typeof(GameObject))
			{
				var idc = (converter as GameObject).GetComponent<IDNAConverter>();
				if (idc != null)
					converter = idc as UnityEngine.Object;
				else
					converter = null;
			}
			Validate();//will this be fast enough?
			for (int i = 0; i < _converters.Count; i++)
			{
				if (_converters[i] == converter)
					return i;
			}
			return -1;
		}

		public int IndexOf(IDNAConverter converter)
		{
			if (converter == null)
				return -1;
			Validate();//will this be fast enough?
			for (int i = 0; i < _converters.Count; i++)
			{
				if (_converters[i] == converter as Object)
					return i;
			}
			return -1;
		}

		public void Insert(int index, IDNAConverter converter)
		{
			if (converter == null)
				return;
			_converters.Insert(index, converter as Object);
		}

		public void InsertRange(int index, IEnumerable<IDNAConverter> converters)
		{
			var convertersToAdd = new List<Object>();
			foreach (IDNAConverter converter in converters)
			{
				if (converter != null)
					convertersToAdd.Add(converter as UnityEngine.Object);
			}
			_converters.InsertRange(index, convertersToAdd);
		}

		public void RemoveAt(int index)
		{
			_converters.RemoveAt(index);
		}

		public void RemoveRange(int index, int count)
		{
			_converters.RemoveRange(index, count);
		}

		public bool Replace(IDNAConverter oldConverter, IDNAConverter newConverter)
		{
			if (oldConverter == null || newConverter == null)
				return false;
			var replaceIndex = _converters.IndexOf(oldConverter as UnityEngine.Object);
			if (replaceIndex != -1)
			{
				_converters[replaceIndex] = newConverter as UnityEngine.Object;
				return true;
			}
			return false;
		}

		public IDNAConverter[] ToArray()
		{
			//If there is no way to add to the list directly (debug mode maybe) we wont need to validate
			Validate();
			var ret = new IDNAConverter[_converters.Count];
			for (int i = 0; i < _converters.Count; i++)
				ret[i] = _converters[i] as IDNAConverter;
			return ret;
		}

		#endregion

	}
}
