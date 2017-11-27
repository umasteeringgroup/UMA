using UnityEngine;

namespace UMA
{
	/// <summary>
	/// UMA replacement for UnityEngine.ProceduralPropertyDescription, which isn't available on all platforms.
	/// </summary>
	public class UMAPropertyDescription : ScriptableObject {
		//public string name;

		public string[] componentLabels;

		public string[] enumOptions;

		public float step;

		public float maximum;

		public float minimum;

		public bool hasRange;

		public ProceduralPropertyType type;

		public string group;

		public string label;
	}

}
