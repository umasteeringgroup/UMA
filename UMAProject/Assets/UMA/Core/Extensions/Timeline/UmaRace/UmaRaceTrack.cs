using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;

[TrackColor(0.2f, 0.2f, 0.2f)]
[TrackClipType(typeof(UmaRaceClip))]
[TrackBindingType(typeof(DynamicCharacterAvatar))]
public class UmaRaceTrack : TrackAsset
{
}
