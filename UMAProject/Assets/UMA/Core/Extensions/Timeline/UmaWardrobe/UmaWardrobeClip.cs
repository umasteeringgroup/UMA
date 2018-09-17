using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[Serializable]
public class UmaWardrobeClip : PlayableAsset, ITimelineClipAsset
{
	public UmaWardrobeBehaviour template = new UmaWardrobeBehaviour();

	public ClipCaps clipCaps
	{
		get { return ClipCaps.None; }
	}

	public override Playable CreatePlayable (PlayableGraph graph, GameObject owner)
	{
		var playable = ScriptPlayable<UmaWardrobeBehaviour>.Create (graph, template);
		return playable;    
	}
}
