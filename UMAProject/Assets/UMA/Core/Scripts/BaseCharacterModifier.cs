using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Deals with base modifications to the character, user can enable scale, height, radius, mass and bounds modifications.
	/// </summary>
	//This replaces the fields and methods in DynamicDNAConverterBehaviour pre-DynamicDNAPlugins. 
	//The available modifications are the same but each kind can be enabled or disabled for more granular control
	//The method for getting the character height is now based solely on the resulting bone structure of the mechanim bones (for humanoid characters)
	//so it no longer assumes certain dna or bones are available
	//the adjustment numbers should now be considered to be more like 'padding' since none of them (apart from 'Head Ratio') are actually required in order to perform any calculations
	[System.Serializable]
	public class BaseCharacterModifier
	{
		#region FIELDS

		[Tooltip("If true the characters 'base scale' will be adjusted using the given scale value on the given bone. For rigs based on the standard UMA Rig, this is usually the 'Position' bone.")]
		[SerializeField]
		private bool _adjustScale;

		[Tooltip("Adjust the height calculation for the character. Head Ratio is how many 'heads high' the character is (classically proportioned character's total height = 7.5 * head height). Bigger heads have a smaller ratio. You can make further manual adjustments using the 'Y' setting. The playmode 'Height Debug' tools in the Converter Customiser scene will help you.")]
		[SerializeField]
		private bool _adjustHeight;

		[Tooltip("Manually adds an X and Z amount when calculating the characters radius.")]
		[SerializeField]
		private bool _adjustRadius;

		//I think the x value here is like 'base weight' and the y  and z values are how much any extra height or width should influence this
		//but I'm not sure- in the scripted UMA converters Human Male ends up weighing less than HumanFemale, so its never been right
		[SerializeField]
		private bool _adjustMass;

		[Tooltip("Should the bounds be updated when the dna changes. Turn this on if you are permitting large dna changes on your character.")]
		[SerializeField]
		private bool _updateBounds;

		[Tooltip("Checking this will make the bounds tight to the characters head/feet. You can manually adjust the bounds futher using 'Adjust Bounds' below.")]
		[SerializeField]
		private bool _tightenBounds;

		[Tooltip("Manually adds extra padding to the characters bounds")]
		[SerializeField]
		private bool _adjustBounds;

		[SerializeField]
		private float _scale = 1f;

		//This is the field thats assigned in the editor, but we use the hash
#pragma warning disable 0414
		[SerializeField]
		private string _bone = "Position";
#pragma warning restore 0414

		[SerializeField]
		private int _scaleBoneHash = -1084586333;//hash of the Position Bone

		[SerializeField]
		private float _headRatio = 7.5f;

		//when importing this will be the Y value of the radius
		[SerializeField]
		private float _radiusAdjustY = 0.01f;

		//when importing this will be the x value of radius adjust (there never has been a z but there should be)
		[SerializeField]
		private Vector2 _radiusAdjust = new Vector2(0.23f, 0);

		[Tooltip("This is used to adjust the characters mass.")]
		[SerializeField]
		private Vector3 _massAdjust = new Vector3(46f, 26f, 26f);

		[SerializeField]
		private Vector3 _boundsAdjust = Vector3.zero;

		#endregion

		#region NON-SERIALIZED FIELDS
		//I know private fields are not supposed to be serialized but somehow setting these values at playtime still means they are here after
		//(not that flagging as [Non-Serialized] helps I now discover...

		[System.NonSerialized]
		private Dictionary<string, int> _mechanimBoneDict = new Dictionary<string, int>();

		[System.NonSerialized]
		private string _lastRace = null;

		[System.NonSerialized]
		private bool boundsAdjustmentApplied = false;

		[System.NonSerialized]
		private float _liveScale = -1f;

		#endregion

		#region PUBLIC PROPERTIES

		//do we need 'sets' for these in order to be backwards compatible?

		public bool adjustScale
		{
			get { return _adjustScale; }
		}

		public bool adjustHeight
		{
			get { return _adjustHeight; }
		}

		public bool adjustRadius
		{
			get { return _adjustRadius; }
		}

		public bool adjustMass
		{
			get { return _adjustMass; }
		}

		public bool updateBounds
		{
			get { return _updateBounds; }
		}

		public bool tightenBounds
		{
			get { return _tightenBounds; }
		}

		public bool adjustBounds
		{
			get { return _adjustBounds; }
		}

		public float scale
		{
			get { return _scale; }
		}

		public int scaleBoneHash
		{
			get { return _scaleBoneHash; }
		}

		public float headRatio
		{
			get { return _headRatio; }
		}

		public float radiusAdjustY
		{
			get { return _radiusAdjustY; }
		}

		public Vector2 radiusAdjust
		{
			get { return _radiusAdjust; }
		}

		public Vector3 massAdjust
		{
			get { return _massAdjust; }
		}

		public Vector3 boundsAdjust
		{
			get { return _boundsAdjust; }
		}
		/// <summary>
		/// Changes the characters base scale at runtime
		/// </summary>
		public float liveScale
		{
			get { return _liveScale; }
			set { _liveScale = value; }
		}

		#endregion

		#region CTOR

		public BaseCharacterModifier() { }

		//an epic constructor for backwards compatibility when re-deserializing
		public BaseCharacterModifier(bool overallModifiersEnabled, float overallScale,
			string overallScaleBone, int overallScaleBoneHash,
			bool tightenBounds, Vector3 boundsAdjust,
			Vector2 radiusAdjust, Vector3 massModifiers)
		{
			if (!overallModifiersEnabled)
			{
				this._adjustScale = false;
				this._adjustHeight = false;
				this._adjustRadius = false;
				this._adjustMass = false;
				this._updateBounds = false;
				this._tightenBounds = false;
				this._adjustBounds = false;
			}
			else
			{
				this._adjustScale = (!string.IsNullOrEmpty(overallScaleBone) && overallScaleBoneHash != -1 && overallScale != 1f) ? true : false;
				this._adjustHeight = true;
				this._adjustRadius = true;
				this._adjustMass = true;
				this._updateBounds = true;
				this._tightenBounds = tightenBounds;
				this._adjustBounds = boundsAdjust != Vector3.zero ? true : false;
			}
			this._scale = overallScale;
			this._bone = overallScaleBone;
			this._scaleBoneHash = overallScaleBoneHash;
			this._radiusAdjustY = radiusAdjust.y;
			this._radiusAdjust = new Vector2(radiusAdjust.x, 0f);
			this._massAdjust = new Vector3(massModifiers.x, massModifiers.y, massModifiers.z);
			this._boundsAdjust = new Vector3(boundsAdjust.x, boundsAdjust.y, boundsAdjust.z);
		}

		#endregion

		#region PUBLIC METHODS

#if UNITY_EDITOR

		public void ImportSettings(BaseCharacterModifier other)
		{
			this._adjustBounds = other._adjustBounds;
			this._adjustHeight = other._adjustHeight;
			this._adjustMass = other._adjustMass;
			this._adjustRadius = other._adjustRadius;
			this._adjustScale = other.adjustScale;
			this._bone = other._bone;
			this._boundsAdjust = other._boundsAdjust;
			this._headRatio = other._headRatio;
			this._massAdjust = other._massAdjust;
			this._radiusAdjust = other._radiusAdjust;
			this._radiusAdjustY = other._radiusAdjustY;
			this._scale = other._scale;
			this._scaleBoneHash = other._scaleBoneHash;
			this._tightenBounds = other._tightenBounds;
			this._updateBounds = other._updateBounds;
		}


#endif

		public void AdjustScale(UMASkeleton skeleton)
		{
			if (_adjustScale)
			{
				if (skeleton.HasBone(scaleBoneHash))
				{
					var liveScaleResult = _liveScale != -1f ? _liveScale : _scale;
					float finalOverallScale = skeleton.GetScale(_scaleBoneHash).x * liveScaleResult;//hmm why does this work- its supposed to be +
					skeleton.SetScale(_scaleBoneHash, new Vector3(finalOverallScale, finalOverallScale, finalOverallScale));
				}
			}
		}

		public void UpdateCharacterHeightMassRadius(UMAData umaData, UMASkeleton skeleton)
		{
			if (_adjustHeight || _adjustMass || _adjustRadius || _adjustBounds)
			{
				var baseRenderer = GetBaseRenderer(umaData);
				if (baseRenderer == null)
					return;
				var newBounds = DoBoundsModifications(baseRenderer, umaData);
				UpdateCharacterHeightMassRadius(umaData, skeleton, newBounds);
			}
		}

		public void UpdateCharacter(UMAData umaData, UMASkeleton skeleton, bool asReset)
		{
			AdjustScale(skeleton);
			UpdateCharacterHeightMassRadius(umaData, skeleton);
		}

		#endregion

		#region PRIVATE METHODS

		/// <summary>
		/// Performs the bounds adjustment operations (updateBounds, tightenBounds, AdjustBounds) if enabled.
		/// </summary>
		/// <param name="targetRenderer">The renderer to perform the adjustments on.</param>
		/// <param name="umaData"></param>
		/// <returns>The updated bounds</returns>
		private Bounds DoBoundsModifications(SkinnedMeshRenderer targetRenderer, UMAData umaData)
		{
			//we cant tighten or adjust bounds unless we also update
			//otherwise they end up in the center of the world
			var umaTransform = umaData.transform;
			var originalParent = umaData.transform.parent;
			var originalRot = umaData.transform.localRotation;
			var originalPos = umaData.transform.localPosition;
			Collider umaCollider = null;
			bool prevColliderEnabled = false;
			bool prevUpdateWhenOffScreen = false;
			Bounds newBounds = targetRenderer.bounds;
			targetRenderer.rootBone = null;

			if (_updateBounds)
			{
				umaCollider = umaData.gameObject.GetComponent<Collider>();
				prevColliderEnabled = umaCollider != null ? umaCollider.enabled : false;
				prevUpdateWhenOffScreen = targetRenderer.updateWhenOffscreen;

				//if there is a collider, disable it before we move anything
				if (umaCollider)
					umaCollider.enabled = false;

				//Move UMA into the root, we do this because it might be inside a scaled object
				umaTransform.SetParent(null, false);
				umaTransform.localRotation = Quaternion.identity;
				umaTransform.localPosition = Vector3.zero;

				//Get the new bounds from the renderer set to always update (updateWhenOffScreen)		
				targetRenderer.updateWhenOffscreen = true;
				newBounds = new Bounds(targetRenderer.localBounds.center, targetRenderer.localBounds.size);
				targetRenderer.updateWhenOffscreen = prevUpdateWhenOffScreen;
				boundsAdjustmentApplied = false;
			}

			//somehow the bounds end up beneath the floor i.e. newBounds.center.y - newBounds.extents.y is actually a minus number
			//tighten bounds fixes this
			if (_tightenBounds)
			{
				Vector3 newCenter = new Vector3(newBounds.center.x, newBounds.center.y, newBounds.center.z);
				Vector3 newSize = new Vector3(newBounds.size.x, newBounds.size.y, newBounds.size.z);
				if (newBounds.center.y - newBounds.extents.y < 0)
				{
					var underAmount = newBounds.center.y - newBounds.extents.y;
					newSize.y = (newBounds.center.y * 2) - underAmount;
					newCenter.y = newSize.y / 2;
				}
				newCenter.x = umaData.umaRoot.transform.position.x;
				newCenter.z = umaData.umaRoot.transform.position.z;
				Bounds modifiedBounds = new Bounds(newCenter, newSize);
				newBounds = modifiedBounds;
			}

			//the user has tools for expanding the bounds aswell so do those here too if they havent been done already (to this race?)
			if (_adjustBounds && !boundsAdjustmentApplied)
			{
				newBounds.Expand(_boundsAdjust);
				boundsAdjustmentApplied = true;
			}

			//if we moved the character move it back again
			if (_updateBounds)
			{
				umaTransform.SetParent(originalParent, false);
				umaTransform.localRotation = originalRot;
				umaTransform.localPosition = originalPos;

				//set any collider to its original setting
				if (umaCollider)
					umaCollider.enabled = prevColliderEnabled;
			}
			targetRenderer.localBounds = newBounds;
			return newBounds;
		}

		// UMAs can have lots of renderers, but this should return the one that we should use when calculating umaData.characterHeight/Radius/Mass- will that always be umaData.GetRenderer(0)?
		private SkinnedMeshRenderer GetBaseRenderer(UMAData umaData, int rendererToGet = 0)
		{
			if (umaData.rendererCount == 0 || umaData.GetRenderer(rendererToGet) == null)
			{
				return null;
			}
			return umaData.GetRenderer(rendererToGet);
		}

		/// <summary>
		/// Builds a dictionary of mechanim bones that can be used when doing the characterHeight/Radius/Mass calculations
		/// </summary>
		private void UpdateMechanimBoneDict(UMAData umaData, UMASkeleton skeleton)
		{
			//"Head" is obligatory for mechanim so we can check that too to tell us when we have switched back to this race and the skeleton was rebuilt but this Behaviour hadnt been garbage collected yet
			if ((_lastRace == null || umaData.umaRecipe.raceData.raceName != _lastRace) || (!_mechanimBoneDict.ContainsKey("Head") || !skeleton.BoneExists(_mechanimBoneDict["Head"])) && umaData.umaRecipe.raceData.umaTarget == RaceData.UMATarget.Humanoid)
			{
				_lastRace = umaData.umaRecipe.raceData.raceName;

				_mechanimBoneDict.Clear();

				var umaTPose = umaData.umaRecipe.raceData.TPose;
				if (umaTPose == null)
				{
					_lastRace = null;
					return;
				}
				_mechanimBoneDict.Add("Head", UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "Head").boneName)));
				_mechanimBoneDict.Add("LeftEye", UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "LeftEye").boneName)));//optionalBone
				_mechanimBoneDict.Add("RightEye", UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "RightEye").boneName)));//optionalBone
				_mechanimBoneDict.Add("Hips", UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "Hips").boneName)));
				_mechanimBoneDict.Add("Neck", UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "Neck").boneName)));//OptionalBone
				_mechanimBoneDict.Add("LeftUpperArm", UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "LeftUpperArm").boneName)));
				_mechanimBoneDict.Add("RightUpperArm", UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "RightUpperArm").boneName)));
				_mechanimBoneDict.Add("LeftUpperLeg", UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "LeftUpperLeg").boneName)));
				_mechanimBoneDict.Add("RightUpperLeg", UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "RightUpperLeg").boneName)));
			}
		}

		/// <summary>
		/// Updates the characterHeightMassRadius after all other changes have been made by the converters
		/// </summary>
		private void UpdateCharacterHeightMassRadius(UMAData umaData, UMASkeleton skeleton, Bounds newBounds)
		{
			float charHeight = newBounds.size.y;
			float charWidth = newBounds.size.x;

			if (umaData.umaRecipe.raceData.umaTarget == RaceData.UMATarget.Humanoid)
			{
				//adjusting is only about tweaking the result
				if (_adjustHeight || _adjustRadius)
				{
					float chinHeight = 0f;
					float headHeight = 0f;
					float headWidth = 0f;

					UpdateMechanimBoneDict(umaData, skeleton);

					//character needs to be moved into the root again with no rotation applied
					var umaTransform = umaData.transform;
					var originalParent = umaData.transform.parent;
					var originalRot = umaData.transform.localRotation;
					var originalPos = umaData.transform.localPosition;
					var umaCollider = umaData.gameObject.GetComponent<Collider>();
					bool prevColliderEnabled = umaCollider != null ? umaCollider.enabled : false;

					//if there is a collider, disable it before we move anything
					if (umaCollider)
						umaCollider.enabled = false;

					umaTransform.SetParent(null, false);
					umaTransform.localRotation = Quaternion.identity;
					umaTransform.localPosition = Vector3.zero;

					if (_adjustHeight)
					{
						//Classically a human is apprx 7.5 heads tall, but we only know the height to the base of the head bone
						//usually head bone is actually usually in line with the lips, making the base of the chin, 1/3rd down the neck
						//so if we have the optional neck bone use that to estimate the base of the chin
						chinHeight = skeleton.GetRelativePosition(_mechanimBoneDict["Head"]).y;
						if (skeleton.BoneExists(_mechanimBoneDict["Neck"]))
						{
							chinHeight = skeleton.GetRelativePosition(_mechanimBoneDict["Neck"]).y + (((skeleton.GetRelativePosition(_mechanimBoneDict["Head"]).y - skeleton.GetRelativePosition(_mechanimBoneDict["Neck"]).y) / 3f) * 2f);
						}
						//apply the headRatio (by default this is 7.5)
						chinHeight = (chinHeight / 6.5f) * (_headRatio - 1);
						//so classically chinbase is 6.5 headHeights from the floor so headHeight is..
						headHeight = (chinHeight / (_headRatio - 1));
						//but bobble headed charcaters (toons), children or dwarves etc have bigger heads proportionally 
						//so their overall height will greater than (chinHeight / 6.5) * 7.5
						//If we have the eyes we can use those to calculate the size of the head better because classically the distance from the chin to the eyes will be half the head height
						if (skeleton.BoneExists(_mechanimBoneDict["LeftEye"]) || skeleton.BoneExists(_mechanimBoneDict["RightEye"]))
						{
							var eyeHeight = 0f;
							//if we have both eyes get the average
							if (skeleton.BoneExists(_mechanimBoneDict["LeftEye"]) && skeleton.BoneExists(_mechanimBoneDict["RightEye"]))
								eyeHeight = (skeleton.GetRelativePosition(_mechanimBoneDict["LeftEye"]).y + skeleton.GetRelativePosition(_mechanimBoneDict["RightEye"]).y) / 2f;
							else if (skeleton.BoneExists(_mechanimBoneDict["LeftEye"]))
								eyeHeight = skeleton.GetRelativePosition(_mechanimBoneDict["LeftEye"]).y;
							else if (skeleton.BoneExists(_mechanimBoneDict["RightEye"]))
								eyeHeight = skeleton.GetRelativePosition(_mechanimBoneDict["RightEye"]).y;

							headHeight = ((eyeHeight - chinHeight) * 2f);
							//because we do this the actual headRatio doesnt *feel* right
							//i.e. with toon the head ratio is more like 3, but the correct value for calcs is 6.5
							//I'd prefer it if these calcs made 3 deliver the correct result
						}

						//So finally
						//Debug.Log("chinHeight was " + chinHeight+" headHeight was "+headHeight);
						//Debug.Log("Classical Height from Head = " + (headHeight * 7.5f));
						//Debug.Log("Classical Height from Chin = " + ((chinHeight / 6.5f) * 7.5f));
						charHeight = chinHeight + headHeight;
					}

					if (_adjustRadius)
					{
						float shouldersWidth = Mathf.Abs(skeleton.GetRelativePosition(_mechanimBoneDict["LeftUpperArm"]).x - skeleton.GetRelativePosition(_mechanimBoneDict["RightUpperArm"]).x);
						//Also female charcaters tend to have hips wider than their shoulders, so check that
						float hipsWidth = Mathf.Abs(skeleton.GetRelativePosition(_mechanimBoneDict["LeftUpperLeg"]).x - skeleton.GetRelativePosition(_mechanimBoneDict["RightUpperLeg"]).x);
						//the outerWidth of the hips is larger than this because the thigh muscles are so big so make this 1/3rd bigger
						hipsWidth = (hipsWidth / 2) * 3;

						//classically the width of the body is 3* headwidth for a strong character so headwidth will be
						headWidth = shouldersWidth / 2.75f;
						//but bobble headed charcaters (toons), children or dwarves etc have bigger heads proportionally and the head can be wider than the shoulders
						//so if we have eye bones use them to calculate head with
						if (skeleton.BoneExists(_mechanimBoneDict["LeftEye"]) && skeleton.BoneExists(_mechanimBoneDict["RightEye"]))
						{
							//clasically a face is 5* the width of the eyes where the distance between the pupils is 2 * eye width
							var eyeWidth = Mathf.Abs(skeleton.GetRelativePosition(_mechanimBoneDict["LeftEye"]).x - skeleton.GetRelativePosition(_mechanimBoneDict["RightEye"]).x) / 2;
							headWidth = eyeWidth * 5f;
						}
						charWidth = (shouldersWidth > headWidth || hipsWidth > headWidth) ? (shouldersWidth > hipsWidth ? shouldersWidth : hipsWidth) : headWidth;
						//we might also want to take into account the z depth between the hips and the head, 
						//because if the character has been made into a horse or something it might be on all fours (and still be mechanim.Humanoid [if that even works!])
						//capsule colliders break down in this scenario though (switch race to SkyCar to see what I mean), so would we want to change the orientation of the collider or the type?
						//does the collider orientation have any impact on physics?
					}
					//Set the UMA back
					umaTransform.SetParent(originalParent, false);
					umaTransform.localRotation = originalRot;
					umaTransform.localPosition = originalPos;

					//set any collider to its original setting
					if (umaCollider)
						umaCollider.enabled = prevColliderEnabled;
				}
			}
			else //if its a car or a castle or whatever use the bounds
			{
				charHeight = newBounds.size.y;
				charWidth = newBounds.size.x;
			}

			//get the scale of the overall scale bone if set
			float overallScale = 1f;
			if (skeleton.GetBoneTransform(_scaleBoneHash) != null)
				overallScale = (skeleton.GetScale(_scaleBoneHash)).x;

			if (_adjustHeight)
			{
				//characterHeight is what we calculated plus any radius y the user has added
				umaData.characterHeight = charHeight * (1 + (_radiusAdjustY * 2));
			}
			else
			{
				umaData.characterHeight = charHeight;
			}

			if (_adjustRadius)
			{
				//characterRadius is the average of the width we calculated plus the z-depth from the bounds / 2
				//we need to include the new _radiusAdjust.y (which is now an adjust for the z axis as radiusAdjustY is now its own field (used for heightAdjust)
				umaData.characterRadius = (charWidth + newBounds.size.z * (_radiusAdjust.x * 2)) / 2;
				//Debug.Log("BCM umaData.characterRadius[" + umaData.characterRadius + "] = (charWidth[" + charWidth + "] + newBounds.size.z[" + newBounds.size.z + "] * (radiusAdjust.x[" + _radiusAdjust.x + "] * 2)) / 2;");
			}
			else
			{
				umaData.characterRadius = (charWidth + newBounds.size.z) / 2;
			}

			if (_adjustMass)
			{
				//characterMass is... what? still dont understand this quite
				var massZModified = (umaData.characterRadius * 2) * overallScale;
				var massYModified = ((1 + _radiusAdjustY) * 0.5f) * overallScale;
				umaData.characterMass = (_massAdjust.x * overallScale) + _massAdjust.y * massYModified + _massAdjust.z * massZModified;
			}
			else
			{
				//HUMANMALE umaData.characterMass[66.68236] = raceMass[50] * overallScale[0.907451] + 28f * umaDna.upperWeight[0.5019608] + 22f * umaDna.lowerWeight[0.3176471]
				umaData.characterMass = 66.68236f;//? Thats the result
			}
			//in the fixed UMA converters HumanMales mass is actually less than HumanFemales, with this thats solved
			//but the characters are lighter than stock by a about 5 when at they standard size and heaver than stock by about 7 when at max height
			//I dont know how much this matters? It can easily be fixed by tweaking the calcs, I just dont know what the numbers mean again...

			//Debug.Log(umaData.transform.gameObject.name + " umaData.characterMass was "+ umaData.characterMass+" characterHeight was " + umaData.characterHeight + " characterRadius was " + umaData.characterRadius);

#if UNITY_EDITOR
			/*if (_heightDebugToolsEnabled)
				AddDebugBoxes(umaData, chinHeight, headHeight, headWidth);
			else
				RemoveDebugBoxes(umaData);*/
#endif

		}
		#endregion

		#region ATTRIBUTES

		[System.AttributeUsage(System.AttributeTargets.Field)]
		public class ConfigAttribute : System.Attribute
		{
			public bool alwaysExpanded = false;

			public ConfigAttribute(bool alwaysExpanded)
			{
				this.alwaysExpanded = alwaysExpanded;
			}

		}

		#endregion
	}
}
