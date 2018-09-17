#if UNITY_2017_1_OR_NEWER
using UnityEngine.Timeline;
using UMA.CharacterSystem;

namespace UMA.Timeline
{
    [TrackColor(0.2f, 0.2f, 0.2f)]
    [TrackClipType(typeof(UmaRaceClip))]
    [TrackBindingType(typeof(DynamicCharacterAvatar))]
    public class UmaRaceTrack : TrackAsset
    {
    }
}
#endif