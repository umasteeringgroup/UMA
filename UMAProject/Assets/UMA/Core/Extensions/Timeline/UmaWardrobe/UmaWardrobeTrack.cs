#if UNITY_2017_1_OR_NEWER
using UnityEngine.Timeline;
using UMA.CharacterSystem;

namespace UMA.Timeline
{
    [TrackColor(0.2f, 0.0f, 0.2f)]
    [TrackClipType(typeof(UmaWardrobeClip))]
    [TrackBindingType(typeof(DynamicCharacterAvatar))]
    public class UmaWardrobeTrack : TrackAsset
    {
    }
}
#endif