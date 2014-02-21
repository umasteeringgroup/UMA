using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
    public abstract class RaceLibraryBase : MonoBehaviour
    {
        public abstract void AddRace(RaceData race);
        public abstract RaceData GetRace(string raceName);
        public abstract RaceData GetRace(int raceHash);

        public abstract void UpdateDictionary();
        public abstract void ValidateDictionary();
    }
}
