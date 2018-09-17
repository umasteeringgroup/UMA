using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UMA;
using UMA.CharacterSystem;

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
