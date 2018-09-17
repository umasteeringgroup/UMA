using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UMA;
using UMA.CharacterSystem;

[Serializable]
public class UmaColorBehaviour : PlayableBehaviour 
{
	public string sharedColorName = "";
	public Color color = Color.white;
}
