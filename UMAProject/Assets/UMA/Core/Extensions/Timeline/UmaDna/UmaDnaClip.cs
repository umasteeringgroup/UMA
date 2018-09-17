#if UNITY_2017_1_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace UMA.Timeline
{
    [Serializable]
    public class UmaDnaClip : PlayableAsset, ITimelineClipAsset
    {
        public UmaDnaBehaviour template = new UmaDnaBehaviour();

        public ClipCaps clipCaps
        {
            get { return ClipCaps.Blending; }
        }

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<UmaDnaBehaviour>.Create(graph, template);
            return playable;
        }
    }
}
#endif