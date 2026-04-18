#if UNITY_2017_1_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace UMA.Timeline
{
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
}
#endif