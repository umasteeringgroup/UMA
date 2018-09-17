#if UNITY_2017_1_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Playables;
using UMA.CharacterSystem;

namespace UMA.Timeline
{
    [Serializable]
    public class UmaRaceBehaviour : PlayableBehaviour
    {
        public string raceToChangeTo = "";

        [HideInInspector]
        public bool isAdded = false;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            DynamicCharacterAvatar avatar = playerData as DynamicCharacterAvatar;

            if (!isAdded)
            {
                isAdded = true;
                avatar.ChangeRace(raceToChangeTo);
            }
        }
    }
}
#endif