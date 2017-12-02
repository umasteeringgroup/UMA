using UnityEngine;

namespace UMA
{
	/// <summary>
	/// UMA replacement for UnityEngine.ProceduralPropertyDescription, which is being removed.
	/// </summary>
	public class UMAPropertyDescription : ScriptableObject
	{
		/// <summary>
		/// The name of the ProceduralProperty. Used to get and set the values.
		/// </summary>
		//public string name;

		/// <summary>
		/// The ProceduralPropertyType describes what type of property this is.
		/// </summary>
		public UMAPropertyType type;

		/// <summary>
		/// The names of the individual components of a Vector2/3/4 ProceduralProperty.
		/// </summary>
		public string[] componentLabels;

		/// <summary>
		/// The available options for ProceduralProperties of type Enum or Bitmask.
		/// </summary>
		public string[] enumOptions;

		/// <summary>
		/// Specifies the step size of this Float or Vector property. Zero is no step.
		/// </summary>
		public float step;

		/// <summary>
		/// If true, the Float or Vector property is constrained to values within a specified range.
		/// </summary>
		public bool hasRange;

		/// <summary>
		/// If hasRange is true, minimum specifies the minimum allowed value for this Float or Vector property.
		/// </summary>
		public float minimum;

		/// <summary>
		/// If hasRange is true, maximum specifies the maximum allowed value for this Float or Vector property.
		/// </summary>
		public float maximum;

		/// <summary>
		/// The name of the GUI group. Used to display ProceduralProperties in groups.
		/// </summary>
		public string group;

		/// <summary>
		/// The label of the ProceduralProperty. Can contain space and be overall more user-friendly than the 'name' member.
		/// </summary>
		public string label;
	}

	public enum UMAPropertyType
	{
		Boolean,
		Float,
		Vector2,
		Vector3,
		Vector4,
		Color3,
		Color4,
		Enum,
		Texture,
		String,
		Bitmask
	}
}
