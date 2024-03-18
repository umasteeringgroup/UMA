using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEditor;
using UnityEngine;
using UMA.XNode;

[CreateNodeMenu("Flow/Mesh Decision")]
public class MeshDecisionNode : Node {
	[Input] public MeshNode Mesh1;
    [Input] public MeshNode Mesh2;
	[Output] public MeshNode Mesh;

	public enum CompareType
	{
		   Equal,
		   NotEqual,
		   Greater,
		   Less,
		   GreaterOrEqual,
		   LessOrEqual,
		   Contains,
		   NotContains
	}

    [Input] public string Value1;
	public CompareType Evaluator;
    [Input] public string Value2;

	// Use this for initialization
	protected override void Init() {
		base.Init();
		
	}

	// Return the correct value of an output port when requested
	public override object GetValue(NodePort port) 
	{
		return Mesh2;
	}
}