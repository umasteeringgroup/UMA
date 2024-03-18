using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEngine;
using UMA.XNode;

public class MeshNode : Node {
	[Input(ShowBackingValue.Unconnected,ConnectionType.Multiple,TypeConstraint.Strict,true)] public OverlayNode Texture;
	[Output] public MeshNode Mesh;
	public SlotDataAsset slotData;
	// Use this for initialization
	protected override void Init() {
		base.Init();
		
	}


    // Return the correct value of an output port when requested
    public override object GetValue(NodePort port) {
		return this; // Replace this
	}
}