using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using System.Collections.Generic;
using UMA;
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
