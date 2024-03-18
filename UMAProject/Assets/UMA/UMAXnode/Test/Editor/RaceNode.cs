using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEngine;
using UMA.XNode;

public class RaceNode : TitledNode {
	public RaceData race;
	[Output] public RaceData output;

    public override string GetTitle()
    {
		if (race != null)
			return race.name;
        return base.GetTitle();
    }


    // Use this for initialization
    protected override void Init() {
		base.Init();
		
	}

	// Return the correct value of an output port when requested
	public override object GetValue(NodePort port) {
		return race; // Replace this
	}
}