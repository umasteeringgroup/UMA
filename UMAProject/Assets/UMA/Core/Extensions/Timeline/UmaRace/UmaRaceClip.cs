using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UMA;
using UMA.CharacterSystem;

[Serializable]
public class UmaRaceClip : PlayableAsset, ITimelineClipAsset
{
    public string raceToChangeTo = "";

    public ClipCaps clipCaps
    {
        get { return ClipCaps.None; }
    }

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<UmaRaceBehaviour>.Create(graph);
        playable.GetBehaviour().raceToChangeTo = raceToChangeTo;
        return playable;
    }

}
