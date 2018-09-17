#if UNITY_2017_1_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace UMA.Timeline
{
    [Serializable]
    public class UmaColorClip : PlayableAsset, ITimelineClipAsset
    {
        public UmaColorBehaviour template = new UmaColorBehaviour();

        public ClipCaps clipCaps
        {
            get { return ClipCaps.Blending; }
        }

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<UmaColorBehaviour>.Create(graph, template);
            return playable;
        }
    }
}
#endif
