using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SwayRootBone : SwayBone
{
	[Tooltip("For debugging purposes. forces changes on all bones")]
	public bool UpdateChangesEachFrame = false;
	[Tooltip("Bones that you want to ignore - these and their children are not processed")]
	public List<Transform> Exclusions = new List<Transform>();
	private List<SwayBone> SwayBones = new List<SwayBone>();
	private float step = 1.0f / 60.0f;

	// Use this for initialization
	void Start ()
	{
		SetupBoneChains();
	}
	
	public void SetupBoneChains()
	{
		AddChildBones(transform,true);
	}

	private void AddChildBones(Transform transform,bool toplevel)
	{
		foreach(Transform t in transform)
		{
			if (Exclusions.Contains(t)) continue;
			SwayBone sb = t.gameObject.GetComponent<SwayBone>();
			if (sb == null)
			{
				sb = t.gameObject.AddComponent<SwayBone>();
			}
			sb.elasticity = elasticity;
			sb.inertia = inertia;
			sb.limit = limit;
			sb.OrientOnly = OrientOnly;
			sb.Reorient = Reorient;
			sb.isTopLevel = toplevel;
			sb.Initialize();
			SwayBones.Add(sb);
			if (t.childCount > 0)
			{
				AddChildBones(t,false);
			}
		}
	}

	public void FixedUpdate ()
	{
		foreach (SwayBone sb in SwayBones)
		{
			sb.DoUpdate(step);
			if (UpdateChangesEachFrame)
			{
				sb.elasticity = elasticity;
				sb.inertia = inertia;
				sb.limit = limit;
				sb.OrientOnly = OrientOnly;
				sb.Reorient = Reorient;
			}
		}
	}
}
