using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Gloal container for various UMA objects in the scene. Marked as partial so the developer can add to this if necessary
	/// </summary>
	public partial class UMAContext : MonoBehaviour
	{
		/// <summary>
		/// The DynamicCharacterSystem
		/// </summary>
		public UMA.CharacterSystem.DynamicCharacterSystemBase dynamicCharacterSystem;
	}
}
