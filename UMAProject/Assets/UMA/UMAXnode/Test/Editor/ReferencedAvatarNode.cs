using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using UMA.XNode;

public class ReferencedAvatarNode : Node {

	public enum AvatarFields
	{
		Race,
		User,
		Animator
	}

	public DynamicCharacterAvatar avatar;
	public AvatarFields field;
	[Output] public string output;

	// Use this for initialization
	protected override void Init() {
		base.Init();
		
	}

	// Return the correct value of an output port when requested
	public override object GetValue(NodePort port) 
	{
		if (avatar == null)
            return "None";
		if (field == AvatarFields.Race)
            return avatar.activeRace.name;
		if (field == AvatarFields.User)
			return avatar.userInformation;

		return ""; // Replace this
	}
}