using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
	public class HeightRadiusMassDNAConverterPlugin : DynamicDNAPlugin
	{

		/*[System.Serializable]
		public class DNASizeAdjustment
		{
			public DNAEvaluator dnaForScale;
			public DNAEvaluator dnaForRadius;
			public DNAEvaluator dnaForMass;
		}*/

		//public List<DNASizeAdjustment> sizeAdjusters = new List<DNASizeAdjustment>();


		//GOING ROUND IN CIRCLES AGAIN I HAVE FOUND
		//-almost all of this can be in the converterBehaviour because the adjustments are largely irrelevant
		//the new way of calculating based on the bone positions pretty much nails it- its only to make things 'exactly' like
		//the hardcoded versions that youd need any mods, so I'm happy to supply them but I dont care about them
		//the only thing that IS important is overallScale and this DOES need to be able to be hooked up to a dna
		//So I've made a Plugin that just does that. If its not used the converter will still 'just work' using its own scale value

		public List<BaseCharacterModifier> _baseCharacterModifiers = new List<BaseCharacterModifier>();

		//Possibly the bounds shit is done by the behaviour
		//- only problem is adjusting which needs params which maybe different depending on dna
		[Tooltip("Should the bounds be updated when the dna changes. Turn this on if you are permitting large dna changes on your character.")]
		[SerializeField]
		private bool _updateBounds = true;

		[Tooltip("Checking this will make the bounds tight to the characters head/feet. You can manually adjust the bounds futher using 'Adjust Bounds' below.")]
		[SerializeField]
		private bool _tightenBounds = true;

		[Tooltip("The base scale bone that will be used by this converter for all its calculations. If you also need to perform Height/Radius/Mass calculations using another bone, you can add another set of converters.")]
		[SerializeField]
		private string _baseScaleBone = "Position";

		[SerializeField]
		private int _baseScaleBoneHash = -1084586333;//hash of the Position Bone



		private Dictionary<string, List<int>> _indexesForDnaNames = new Dictionary<string, List<int>>();

		private bool _boundsAdjustmentApplied = false;

		[System.NonSerialized]
		private Dictionary<string, Transform> _mechanimBoneDict = new Dictionary<string, Transform>();

		[System.NonSerialized]
		private string _lastRace = null;

		public override Dictionary<string, List<int>> IndexesForDnaNames
		{
			get
			{
				var dict = new Dictionary<string, List<int>>();
				/*for (int i = 0; i < _baseCharacterModifiers.Count; i++)
				{
					for (int ci = 0; ci < _baseCharacterModifiers[i].UsedDNANames.Count; ci++)
					{
						if (!dict.ContainsKey(_baseCharacterModifiers[i].UsedDNANames[ci]))
							dict.Add(_baseCharacterModifiers[i].UsedDNANames[ci], new List<int>());

						dict[_baseCharacterModifiers[i].UsedDNANames[ci]].Add(i);
					}
				}*/
				return dict;
			}
		}

		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			/*var umaDna = umaData.GetDna(dnaTypeHash);
			var masterWeightCalc = masterWeight.GetWeight(umaDna);
			//bail if we are disabled
			if (masterWeightCalc == 0)
				return;
			List<BaseCharacterModifier> evaluatedModifiers = new List<BaseCharacterModifier>();
			for (int i = 0; i < _baseCharacterModifiers.Count; i++)
			{
				evaluatedModifiers.Add(_baseCharacterModifiers[i].GetEvaluatedModifier(umaData, skeleton, umaDna));
			}
			//Deal with scale?
			bool doScale = false;
			for (int i = 0; i < evaluatedModifiers.Count; i++)
			{
				if (evaluatedModifiers[i].adjustScale)
				{
					doScale = true;
					break;
				}
			}
			if (doScale)
			{
				var scaleAdjustDict = new Dictionary<int, List<float>>();
				for (int i = 0; i < evaluatedModifiers.Count; i++)
				{
					if (!scaleAdjustDict.ContainsKey(evaluatedModifiers[i].scaleBoneHash))
						scaleAdjustDict.Add(evaluatedModifiers[i].scaleBoneHash, new List<float>());
					scaleAdjustDict[evaluatedModifiers[i].scaleBoneHash].Add(evaluatedModifiers[i].scale);
				}
				foreach (KeyValuePair<int, List<float>> kp in scaleAdjustDict)
				{
					var baseScaleTrans = skeleton.GetBoneTransform(kp.Key);
					if (baseScaleTrans != null)
					{
						var scaleadj = 0f;
						for (int si = 0; si < kp.Value.Count; si++)
							scaleadj += kp.Value[si];
						scaleadj = scaleadj / kp.Value.Count;
						//DEAL WITH MASTERWEIGHT- I think we need to lerp but I couldnt make that work before
						float overallScaleCalc = baseScaleTrans.localScale.x * scaleadj;//hmm why does this work- its supposed to be +
						skeleton.SetScale(kp.Key, new Vector3(overallScaleCalc, overallScaleCalc, overallScaleCalc));
					}
				}
			}
			//we cant go any further without a renderer so check that
			var baseRenderer = GetBaseRenderer(umaData);
			if (baseRenderer == null)
				return;

			//Deal with bounds?
			bool doAdjustBounds = false;
			for (int i = 0; i < evaluatedModifiers.Count; i++)
			{
				if (evaluatedModifiers[i].adjustBounds)
				{
					doAdjustBounds = true;
					break;
				}
			}
			var newBounds = baseRenderer.bounds;
			if (doAdjustBounds)
			{
				Vector3 finalBoundsAdjust = Vector3.zero;
				var boundsAdjustList = new List<Vector3>();
				for (int i = 0; i < evaluatedModifiers.Count; i++)
				{
					finalBoundsAdjust += evaluatedModifiers[i].boundsAdjust;
				}
				finalBoundsAdjust = finalBoundsAdjust / evaluatedModifiers.Count;
				//apply masterWeight
				finalBoundsAdjust = finalBoundsAdjust * masterWeightCalc;
				newBounds = DoBoundsModifications(baseRenderer, umaData, finalBoundsAdjust);
			}
			//deal with characterHeight/Mass/Radius?
			bool doHeightMassRadius = false;
			for (int i = 0; i < evaluatedModifiers.Count; i++)
			{
				if (evaluatedModifiers[i].adjustHeight || evaluatedModifiers[i].adjustRadius || evaluatedModifiers[i].adjustMass)
				{
					doHeightMassRadius = true;
					break;
				}
			}*/
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
		/// Performs the bounds adjustment operations (updateBounds, tightenBounds, AdjustBounds) if enabled.
		/// </summary>
		/// <param name="targetRenderer">The renderer to perform the adjustments on.</param>
		/// <param name="umaData"></param>
		/// <returns>The updated bounds</returns>
		private Bounds DoBoundsModifications(SkinnedMeshRenderer targetRenderer, UMAData umaData, Vector3 boundsAdjustment)
		{
			var umaTransform = umaData.transform;
			var originalParent = umaData.transform.parent;
			var originalRot = umaData.transform.localRotation;
			var originalPos = umaData.transform.localPosition;
			Collider umaCollider = null;
			bool prevColliderEnabled = false;
			bool prevUpdateWhenOffScreen = false;
			Bounds newBounds = targetRenderer.bounds;

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
				_boundsAdjustmentApplied = false;
			}

			//somehow the bounds end up beneath the floor i.e. newBounds.center.y - newBounds.extents.y is actually a minus number
			//tighten bounds fixes this
			//does this need to be done with the uma in the root?
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
			if (boundsAdjustment != Vector3.zero && !_boundsAdjustmentApplied)
			{
				newBounds.Expand(boundsAdjustment);
				_boundsAdjustmentApplied = true;
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

		/// <summary>
		/// Builds a dictionary of mechanim bones that can be used when doing the characterHeight/Radius/Mass calculations
		/// </summary>
		private void UpdateMechanimBoneDict(UMAData umaData, UMASkeleton skeleton)
		{
			//"Head" is obligatory for mechanim so we can check that too to tell us when we have switched back to this race and the skeleton was rebuilt but this Behaviour hadnt been garbage collected yet
			if ((_lastRace == null || umaData.umaRecipe.raceData.raceName != _lastRace) || (!_mechanimBoneDict.ContainsKey("Head") || _mechanimBoneDict["Head"] == null) && umaData.umaRecipe.raceData.umaTarget == RaceData.UMATarget.Humanoid)
			{
				_lastRace = umaData.umaRecipe.raceData.raceName;

				_mechanimBoneDict.Clear();

				var umaTPose = umaData.umaRecipe.raceData.TPose;
				if (umaTPose == null)
				{
					_lastRace = null;
					return;
				}
				_mechanimBoneDict.Add("Head", skeleton.GetBoneTransform(UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "Head").boneName))));
				_mechanimBoneDict.Add("LeftEye", skeleton.GetBoneTransform(UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "LeftEye").boneName))));//optionalBone
				_mechanimBoneDict.Add("RightEye", skeleton.GetBoneTransform(UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "RightEye").boneName))));//optionalBone
				_mechanimBoneDict.Add("Hips", skeleton.GetBoneTransform(UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "Hips").boneName))));
				_mechanimBoneDict.Add("Neck", skeleton.GetBoneTransform(UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "Neck").boneName))));//OptionalBone
				_mechanimBoneDict.Add("LeftUpperArm", skeleton.GetBoneTransform(UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "LeftUpperArm").boneName))));
				_mechanimBoneDict.Add("RightUpperArm", skeleton.GetBoneTransform(UMAUtils.StringToHash((Array.Find(umaTPose.humanInfo, entry => entry.humanName == "RightUpperArm").boneName))));
			}
		}


		/// <summary>
		/// Updates the characterHeightMassRadius after all other changes have been made by the converters
		/// </summary>
		private void UpdateCharacterHeightMassRadius(UMAData umaData, UMASkeleton skeleton, Bounds newBounds, bool adjustHeight, bool adjustRadius, bool adjustMass, Vector2 heightAdjust, Vector2 radiusAdjust, Vector3 massAdjust)
		{
			//adjust height is only really for mechanim charcaters- otherwise the bounds are used
			float charHeight = 0f;
			float charWidth = 0f;
			float chinHeight = 0f;
			float headHeight = 0f;
			float headWidth = 0f;
			//If the UMA is a Humanoid we can use the mechanim bones to calculate the height/radius
			if (umaData.umaRecipe.raceData.umaTarget == RaceData.UMATarget.Humanoid)
			{
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

				//Classically a human is apprx 7.5 heads tall, but we only know the height to the base of the head bone
				//usually head bone is actually usually in line with the lips, making the base of the chin, 1/3rd down the neck
				//so if we have the optional neck bone use that to estimate the base of the chin
				chinHeight = _mechanimBoneDict["Head"].position.y;
				if (_mechanimBoneDict["Neck"] != null)
				{
					chinHeight = _mechanimBoneDict["Neck"].position.y + (((_mechanimBoneDict["Head"].position.y - _mechanimBoneDict["Neck"].position.y) / 3f) * 2f);
				}
				//apply the headRatio
				chinHeight = (chinHeight / 6.5f) * (heightAdjust.x - 1);
				//so classically chinbase is 6.5 headHeights from the floor so headHeight is..
				headHeight = (chinHeight / (heightAdjust.x - 1));
				//but bobble headed charcaters (toons), children or dwarves etc have bigger heads proportionally 
				//so their overall height will greater than (chinHeight / 6.5) * 7.5
				//If we have the eyes we can use those to calculate the size of the head better because classically the distance from the chin to the eyes will be half the head height
				if (_mechanimBoneDict["LeftEye"] != null || _mechanimBoneDict["RightEye"] != null)
				{
					var eyeHeight = 0f;
					//if we have both eyes get the average
					if (_mechanimBoneDict["LeftEye"] != null && _mechanimBoneDict["RightEye"] != null)
						eyeHeight = (_mechanimBoneDict["LeftEye"].position.y + _mechanimBoneDict["RightEye"].position.y) / 2f;
					else if (_mechanimBoneDict["LeftEye"] != null)
						eyeHeight = _mechanimBoneDict["LeftEye"].position.y;
					else if (_mechanimBoneDict["RightEye"] != null)
						eyeHeight = _mechanimBoneDict["RightEye"].position.y;

					headHeight = ((eyeHeight - chinHeight) * 2f);
				}

				//So finally
				//Debug.Log("chinHeight was " + chinHeight+" headHeight was "+headHeight);
				//Debug.Log("Classical Height from Head = " + (headHeight * 7.5f));
				//Debug.Log("Classical Height from Chin = " + ((chinHeight / 6.5f) * 7.5f));
				charHeight = chinHeight + headHeight;

				//classically the width of the body is 3* headwidth for a strong character
				float shouldersWidth = Mathf.Abs(_mechanimBoneDict["LeftUpperArm"].position.x - _mechanimBoneDict["RightUpperArm"].position.x);
				headWidth = shouldersWidth / 2.75f;
				//but bobble headed charcaters (toons), children or dwarves etc have bigger heads proportionally and the head can be wider than the shoulders
				//so if we have eye bones use them to calculate head with
				if (_mechanimBoneDict["LeftEye"] != null && _mechanimBoneDict["RightEye"] != null)
				{
					//clasically a face is 5* the width of the eyes where the distance between the pupils is 2 * eye width
					var eyeWidth = Mathf.Abs(_mechanimBoneDict["LeftEye"].position.x - _mechanimBoneDict["RightEye"].position.x) / 2;
					headWidth = eyeWidth * 5f;
				}
				charWidth = shouldersWidth > headWidth ? shouldersWidth : headWidth;
				//we might also want to take into account the z depth between the hips and the head, 
				//because if the character has been made into a horse or something it might be on all fours (and still be mechanim.Humanoid [if that even works!])
				//capsule colliders break down in this scenario though (switch race to SkyCar to see what I mean), so would we want to change the orientation of the collider or the type?
				//does the collider orientation have any impact on physics?

				//Set the UMA back
				umaTransform.SetParent(originalParent, false);
				umaTransform.localRotation = originalRot;
				umaTransform.localPosition = originalPos;

				//set any collider to its original setting
				if (umaCollider)
					umaCollider.enabled = prevColliderEnabled;
			}
			else //if its a car or a castle or whatever use the bounds
			{
				charHeight = newBounds.size.y;
				charWidth = newBounds.size.x;
			}

			//get the scale of the overall scale bone if set
			float overallScale = 1f;
			if (skeleton.GetBoneTransform(_baseScaleBoneHash) != null)
				overallScale = (skeleton.GetScale(_baseScaleBoneHash)).x;

			//characterHeight is what we calculated plus any radius y the user has added
			umaData.characterHeight = charHeight * (1 + (heightAdjust.y * 2));

			//characterRadius is the average of the width we calculated plus the z-depth from the bounds / 2
			umaData.characterRadius = (charWidth + newBounds.size.z * (radiusAdjust.x * 2)) / 2;
			Debug.Log("umaData.characterRadius["+ umaData.characterRadius+"] = (charWidth["+ charWidth+"] + newBounds.size.z["+ newBounds.size.z+"] * (radiusAdjust.x["+ radiusAdjust.x+"] * 2)) / 2;");
			//characterMass is... what? still dont understand this quite
			var massZModified = (umaData.characterRadius * 2) * overallScale;
			var massYModified = ((1 + radiusAdjust.y) * 0.5f) * overallScale;
			umaData.characterMass = (massAdjust.x * overallScale) + massAdjust.y * massYModified + massAdjust.z * massZModified;

			//in the fixed UMA converters HumanMales mass is actually less than HumanFemales, with this thats solved
			//but the characters are lighter than stock by a about 5 when at they standard size and heaver than stock by about 7 when at max height
			//I dont know how much this matters? It can easily be fixed by tweaking the calcs, I just dont know what the numbers mean again...

		}
#if UNITY_EDITOR

		public override float GetPluginEntryHeight(SerializedObject pluginSO, int entryIndex, SerializedProperty entry)
		{
			return base.GetPluginEntryHeight(pluginSO, entryIndex, entry);
		}

		public override GUIContent GetPluginEntryLabel(SerializedProperty entry, SerializedObject pluginSO, int entryIndex)
		{
			return base.GetPluginEntryLabel(entry, pluginSO, entryIndex);
		}

		public override float GetListHeaderHeight
		{
			get
			{
				return ((EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 2) + (EditorGUIUtility.standardVerticalSpacing *2);
			}
		}

		public override bool DrawElementsListHeaderContent(Rect rect, SerializedObject pluginSO)
		{
			EditorGUI.indentLevel++;
			var prevIndent = EditorGUI.indentLevel;
			rect = EditorGUI.IndentedRect(rect);
			rect.height = rect.height - (EditorGUIUtility.standardVerticalSpacing * 2);
			var updateBoundsRect = new Rect(rect.xMin, rect.yMin, rect.width, rect.height / 2);
			var tightenBoundsRect = new Rect(rect.xMin, updateBoundsRect.yMax, rect.width, rect.height / 2);
			EditorGUI.indentLevel = 0;
			//var baseCharacterModifierProp = pluginSO.FindProperty("_baseCharacterModifier");
			//EditorGUI.PropertyField(rect, baseCharacterModifierProp);
			var updateBoundsProp = pluginSO.FindProperty("_updateBounds");
			var tightenBoundsProp = pluginSO.FindProperty("_tightenBounds");
			EditorGUI.PropertyField(updateBoundsRect, updateBoundsProp);
			EditorGUI.PropertyField(tightenBoundsRect, tightenBoundsProp);
			EditorGUI.indentLevel = prevIndent;
			EditorGUI.indentLevel--;
			return false;
		}

		public override void OnAddEntryCallback(SerializedObject pluginSO, int entryIndex)
		{
			if (entryIndex == 0)
			{
				var listProp = pluginSO.FindProperty("_baseCharacterModifiers");
				var newEl = listProp.GetArrayElementAtIndex(entryIndex);
				//when stuff is added to a list in the inspector the last entry is copied. 
				//This is fine except when its an empty list, in that case I want default values added
				//so that users have something to go on to begin with. In that case default to UMA HumanMale values

				newEl.FindPropertyRelative("_adjustScale").boolValue = true;
				newEl.FindPropertyRelative("_adjustHeight").boolValue = true;
				newEl.FindPropertyRelative("_adjustRadius").boolValue = true;
				newEl.FindPropertyRelative("_adjustMass").boolValue = true;
				newEl.FindPropertyRelative("_adjustBounds").boolValue = true;

				newEl.FindPropertyRelative("_scale").floatValue = 0.88f;
				newEl.FindPropertyRelative("_bone").stringValue = "Position";
				newEl.FindPropertyRelative("_scaleBoneHash").intValue = -1084586333;//hash of the Position Bone

				newEl.FindPropertyRelative("_headRatio").floatValue = 7.5f;
				newEl.FindPropertyRelative("_radiusAdjustY").floatValue = 0.01f;
				newEl.FindPropertyRelative("_radiusAdjust").vector2Value = new Vector2(0.159f, 0f);
				newEl.FindPropertyRelative("_massAdjust").vector3Value = new Vector3(42f, 28f, 22f);
				newEl.FindPropertyRelative("_boundsAdjust").vector3Value = new Vector3(-0.36f, 0.06f, 0f);
				pluginSO.ApplyModifiedProperties();
			}

			base.OnAddEntryCallback(pluginSO, entryIndex);
		}

#endif

	}
}
