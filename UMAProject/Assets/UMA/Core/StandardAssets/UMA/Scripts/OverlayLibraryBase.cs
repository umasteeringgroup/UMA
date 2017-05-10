using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Base class for overlay libraries.
	/// </summary>
	public abstract class OverlayLibraryBase : MonoBehaviour
	{
		/// <summary>
		/// Add an overlay asset to the library.
		/// </summary>
		/// <param name="overlay">Overlay.</param>
		public abstract void AddOverlayAsset(OverlayDataAsset overlay);
		/// <summary>
		/// Create an overlay by name.
		/// </summary>
		/// <returns>The overlay (or null if not found in library).</returns>
		/// <param name="name">Name.</param>
		public abstract OverlayData InstantiateOverlay(string name);
		/// <summary>
		/// Create an overlay by name hash.
		/// </summary>
		/// <returns>The overlay (or null if not found in library).</returns>
		/// <param name="nameHash">Name hash.</param>
		public abstract OverlayData InstantiateOverlay(int nameHash);
		/// <summary>
		/// Create a tinted overlay by name.
		/// </summary>
		/// <returns>The overlay (or null if not found in library).</returns>
		/// <param name="name">Name.</param>
		/// <param name="color">Tint color.</param>
		public abstract OverlayData InstantiateOverlay(string name, Color color);
		/// <summary>
		/// Create a tinted overlay by name hash.
		/// </summary>
		/// <returns>The overlay (or null if not found in library).</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="color">Tint color.</param>
		public abstract OverlayData InstantiateOverlay(int nameHash, Color color);

		public abstract bool HasOverlay(string Name);
		public abstract bool HasOverlay(int NameHash);
		public abstract OverlayDataAsset[] GetAllOverlayAssets();

		public abstract void UpdateDictionary();
		public abstract void ValidateDictionary();
	}
}
