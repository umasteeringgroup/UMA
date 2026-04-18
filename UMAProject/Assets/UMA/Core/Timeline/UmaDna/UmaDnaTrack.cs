#if UNITY_2017_1_OR_NEWER
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UMA.CharacterSystem;

namespace UMA.Timeline
{
    [TrackColor(0.2f, 0.8f, 0.2f)]
    [TrackClipType(typeof(UmaDnaClip))]
    [TrackBindingType(typeof(DynamicCharacterAvatar))]
    public class UmaDnaTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<UmaDnaMixerBehaviour>.Create(graph, inputCount);
            return mixer;
        }
    }
}
#endif
