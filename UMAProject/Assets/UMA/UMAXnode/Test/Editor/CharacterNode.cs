using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA.XNode;

[CreateNodeMenu("Output/Character")]
public class CharacterOutputNode : Node {

	public enum SlotName
	{
        Head,
        Arms,
        Legs,
        Torso,
        Hands,
        Feet
    }

	public SlotName destination;

	//public string[] Slots;

	[Input] public RaceNode Race;
	[Input] public SlotNode Slot;

	 

	// Use this for initialization
	protected override void Init() {
		base.Init();
		
	}

	// Return the correct value of an output port when requested
	public override object GetValue(NodePort port) {
		return null; // Replace this
	}
}