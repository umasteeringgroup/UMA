using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Base class for race libraries.
	/// </summary>
    public abstract class RaceLibraryBase : MonoBehaviour
    {
		/// <summary>
		/// Add a race to the library.
		/// </summary>
		/// <param name="race">Race.</param>
        public abstract void AddRace(RaceData race);
		/// <summary>
		/// Gets a race by name.
		/// </summary>
		/// <returns>The race (or null if not in library).</returns>
		/// <param name="raceName">Name.</param>
        public abstract RaceData GetRace(string raceName);
		/// <summary>
		/// Gets a race by name hash.
		/// </summary>
		/// <returns>The race (or null if not in library).</returns>
		/// <param name="raceHash">Name hash.</param>
        public abstract RaceData GetRace(int raceHash);
		/// <summary>
		/// Array of all races in the library.
		/// </summary>
		/// <returns>The race array.</returns>
		public abstract RaceData[] GetAllRaces();

        public abstract void UpdateDictionary();
        public abstract void ValidateDictionary();
    }
}
