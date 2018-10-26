using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections.Generic;
using UMA.PoseTools;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace UMA.CharacterSystem
{
	//A serialized version of DNAConverterBehaviour, so that we can include these settings in assetBundles, which cannot include their own scripts...
	//Uses DynamicUmaDna which can have Dynamic DNA Names based on a DynamicUmaDnaAsset
	//DynamicDNAPlugins UPDATE moved all the overall modifier variables and thier apply method into a  BaseCharacterModifier. ISerializationCallbackReciever handles the upgrade
	//The former fields still exist in the #BACKWARDS COMPATIBILITY Section but been marked private and obsolete and will be removed in a future update
	public class DynamicDNAConverterBehaviour : DynamicDNAConverterBehaviourBase, ISerializationCallbackReceiver
	{

		[SerializeField]
		private DynamicDNAConverterAsset _converterController;

		[SerializeField]
		private BaseCharacterModifier _overallModifiers = new BaseCharacterModifier();

		#region NON-SERIALIZED PRIVATE FIELDS

		private Dictionary<string, List<UnityAction<string, float>>> _dnaCallbackDelegates = new Dictionary<string, List<UnityAction<string, float>>>();

		#endregion

		#region PUBLIC PROPERTIES

		public DynamicDNAConverterAsset ConverterController
		{
			get { return _converterController; }
			set
			{
				_converterController = value;
				_converterController.converterBehaviour = this;
			}
		}

		public BaseCharacterModifier overallModifiers
		{
			get { return _overallModifiers; }
		}
		/// <summary>
		/// Gets the base scale as set in the 'overall modifiers section of this converter
		/// </summary>
		public float baseScale
		{
			get { return _overallModifiers.scale; }
		}
		/// <summary>
		/// Changes the characters base scale at runtime
		/// </summary>
		public float liveScale
		{
			get { return _overallModifiers.liveScale; }
			set { _overallModifiers.liveScale = value; }
		}

		#endregion

		#region CTOR

		public DynamicDNAConverterBehaviour()
		{
			ApplyDnaAction = ApplyDynamicDnaAction;
			DNAType = typeof(DynamicUMADna);
		}

		#endregion

		#region DNA CONVERTER BEHAVIOUR OVERRIDES

		public override void Prepare()
		{
			//nothing to prepare now we dont use the hash list
		}

		/// <summary>
		/// Returns the dnaTypeHash from the assigned dnaAsset or 0 if no dnaAsset is set
		/// </summary>
		/// <returns></returns>
		public override int DNATypeHash
		{
			get
			{
				if (dnaAsset != null)
					return dnaAsset.dnaTypeHash;
				else
					Debug.LogWarning(this.name + " did not have a DNA Asset assigned. This is required for DynamicDnaConverters.");
				return 0;
			}
		}

		#endregion

		public bool AddDnaCallbackDelegate(UnityAction<string, float> callback, string targetDnaName)
		{
			bool added = false;

			if (!_dnaCallbackDelegates.ContainsKey(targetDnaName))
				_dnaCallbackDelegates.Add(targetDnaName, new List<UnityAction<string, float>>());

			if (!_dnaCallbackDelegates[targetDnaName].Contains(callback))
			{
				_dnaCallbackDelegates[targetDnaName].Add(callback);
				added = true;
			}
			return added;
		}

		public bool RemoveDnaCallbackDelegate(UnityAction<string, float> callback, string targetDnaName)
		{
			bool removed = false;

			if (!_dnaCallbackDelegates.ContainsKey(targetDnaName))
			{
				removed = true;
			}
			else
			{
				if (_dnaCallbackDelegates[targetDnaName].Contains(callback))
				{
					_dnaCallbackDelegates[targetDnaName].Remove(callback);
					removed = true;
				}
				if (_dnaCallbackDelegates[targetDnaName].Count == 0)
				{
					_dnaCallbackDelegates.Remove(targetDnaName);
				}
			}
			return removed;
		}

		public void ApplyDynamicDnaAction(UMAData umaData, UMASkeleton skeleton)
		{
			ApplyDynamicDnaAction(umaData, skeleton, false);
		}

		public void ApplyDynamicDnaAction(UMAData umaData, UMASkeleton skeleton, bool asReset)
		{
			UMADnaBase umaDna = null;

			//reset the overallScaleCalc
			//_overallScaleCalc = overallScale;
			//reset the live scale on the overallModifiers ready for any adjustments any plugins might make
			liveScale = -1;
			if (!asReset)
			{
				umaDna = umaData.GetDna(DNATypeHash);
				//Make the DNAAssets match if they dont already, can happen when some parts are in bundles and others arent
				if (((DynamicUMADnaBase)umaDna).dnaAsset != dnaAsset)
					((DynamicUMADnaBase)umaDna).dnaAsset = dnaAsset;
			}
			//hmm how do we deal with 'asReset' without forcing plugins to deal with it
			//I guess the converter could cherry pick the skeletonModifiers out of it and just apply those
			//although its only going to be skeletonModifiers with a hardcoded 'value' override that will do anything when dna is null
			if (_converterController != null)
			{
				_converterController.converterBehaviour = this;
				_converterController.ApplyDNA(umaData, skeleton, DNATypeHash);
			}
			else
			{
				ApplySkeletonModifiers(umaData, umaDna, skeleton);
				if (!asReset)
				{
					ApplyStartingPose(skeleton);
				}			
			}
			_overallModifiers.UpdateCharacter(umaData, skeleton, asReset);
			ApplyDnaCallbackDelegates(umaData);
		}

		public void ApplyDnaCallbackDelegates(UMAData umaData)
		{
			if (_dnaCallbackDelegates.Count == 0)
				return;
			UMADnaBase umaDna;
			//need to use the typehash
			umaDna = umaData.GetDna(DNATypeHash);
			if (umaDna.Count == 0)
				return;
			foreach (KeyValuePair<string, List<UnityAction<string, float>>> kp in _dnaCallbackDelegates)
			{
				for (int i = 0; i < kp.Value.Count; i++)
				{
					kp.Value[i].Invoke(kp.Key, (umaDna as DynamicUMADna).GetValue(kp.Key, true));
				}
			}
		}

		/// <summary>
		/// Method to temporarily remove any dna from a Skeleton. Useful for getting bone values for pre and post dna (since the skeletons own unmodified values often dont *quite* match for some reason- I think because a recipes 0.5 values end up as 0.5019608 when they come out of binary)
		/// Or it could be that I need to change the way this outputs values maybe?
		/// </summary>
		/// <param name="umaData"></param>
		public void RemoveDNAChangesFromSkeleton(UMAData umaData)
		{
			ApplyDynamicDnaAction(umaData, umaData.skeleton, true);
		}

		#region ISERIALIZATIONCALLBACKRECIEVER

		public void OnBeforeSerialize()
		{
			//do nothing
		}

		public void OnAfterDeserialize()
		{
			//a big if to determine if we need to upgrade
			//- we want to preserve the users settings even if overallModifiersEnabled was turned off
			if (overallModifiersEnabled == true || overallScale != 1f || !String.IsNullOrEmpty(overallScaleBone)
				|| boundsAdjust != Vector3.zero || radiusAdjust != Vector2.zero || massModifiers != Vector3.zero)
			{
				_overallModifiers = new BaseCharacterModifier(overallModifiersEnabled, overallScale, overallScaleBone,
					overallScaleBoneHash, tightenBounds, boundsAdjust, radiusAdjust, massModifiers);
				overallModifiersEnabled = false;
				overallScale = 1f;
				overallScaleBone = "";
				boundsAdjust = Vector3.zero;
				radiusAdjust = Vector2.zero;
				massModifiers = Vector3.zero;
			}
		}

		#endregion


#if UNITY_EDITOR

		#region BACKUP AND UPGRADE

		public bool BackupAndUpgrade()
		{
			if (_converterController != null)
				return false;
			GameObject backup = null;
			DynamicDNAConverterAsset newController = null;
			DynamicDNAPlugin skelModsPlug = null;
			DynamicDNAPlugin startingPosePlug = null;
			//only backup if there is any data to back up
			if (_skeletonModifiers.Count > 0 || _startingPose != null)
			{
				backup = CustomAssetUtility.ClonePrefab(this.gameObject, this.gameObject.name + "BU");
				if(backup == null)
				{
					//bail if we needed a backup but couldn't make one
					Debug.LogWarning("DynamicDNAConverterBehaviour BackupAndUpgrade failed because it was unable to create a backup of the existing behaviour.");
					return false;
				}
			}
			var newControllerName = this.name.Replace("DynamicDNAConverterBehaviour", "").Replace("DNAConverterBehaviour", "").Replace("ConverterBehaviour", "");
			newControllerName += "ConverterController";
			var path = AssetDatabase.GetAssetPath(this.gameObject);
			path = path.Replace("/" + Path.GetFileName(AssetDatabase.GetAssetPath(this.gameObject)), "");
			var assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/" + newControllerName + ".asset");
			newController = DynamicDNAConverterAsset.CreateDynamicDNAConverterAsset(assetPathAndName, false);
			if(newController == null)
			{
				//bail if the converterController was not created
				Debug.LogWarning("DynamicDNAConverterBehaviour BackupAndUpgrade failed because it was unable to create the new ConverterController.");
				return false;
			}
			if (_skeletonModifiers.Count > 0)
			{
				skelModsPlug = newController.AddPlugin(typeof(SkeletonModifiersDNAConverterPlugin));
				if(!((SkeletonModifiersDNAConverterPlugin)skelModsPlug).ImportSettings(this.gameObject, 0))
				{
					Debug.LogWarning("Your SkeletonModifiers did not import correctly into the new plugin. Please try importing then manually");//mustnt ever happen
				}
				else
				{
					_skeletonModifiers.Clear();
				}
			}
			if(_startingPose != null)
			{
				startingPosePlug = newController.AddPlugin(typeof(BonePoseDNAConverterPlugin));
				if(((BonePoseDNAConverterPlugin)startingPosePlug).ImportSettings(this.gameObject, 0))
				{
					Debug.LogWarning("Your StartingPose did not import correctly into the new plugin. Please try importing it manually");//mustnt ever happen
				}
				else
				{
					_startingPose = null;
				}
			}
			//Set this last because the backwards compatible public properties get values from it if its set
			_converterController = newController;
			_converterController.converterBehaviour = this;
			EditorUtility.SetDirty(newController);
			if (skelModsPlug != null)
				EditorUtility.SetDirty(skelModsPlug);
			if (startingPosePlug != null)
				EditorUtility.SetDirty(startingPosePlug);
			EditorUtility.SetDirty(this.gameObject);
			AssetDatabase.SaveAssets();
			return true;
		}

		#endregion

#endif

		#region DEBUG HEIGHTRADIUS

#if UNITY_EDITOR
		//Not happy with these- just want lines really at the feet, chin and top of the head
		//they also get screwed up if the character is inside a scaled object- but they are just helpers anyway
		//maybe I need gizmos or something?
		//This is now something that needs to be done by BCM- just keeping here cos I need to make it work
		/*private void AddDebugBoxes(UMAData umaData, float chinHeight, float headHeight, float headWidth)
		{
			if (_mechanimBoneDict.ContainsKey("Head") && _mechanimBoneDict["Head"] != null)
			{
				//Somehow these are still here even when they arent(i.e. you change race and change race back again bodyBox != null)
				var bodyBox = umaData.umaRoot.transform.parent.transform.Find("BodyBox");
				var headBox = umaData.umaRoot.transform.parent.transform.Find("HeadBox");
				//I need to add boxes I think so I can see these calcs
				if (bodyBox == null)
				{
					bodyBox = new GameObject().transform;
					bodyBox.name = "BodyBox";
					var bodyBoxCube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
					bodyBoxCube.name = "BodyBoxCube";
					bodyBox.parent = umaData.umaRoot.transform.parent;
					bodyBoxCube.parent = bodyBox;
					var material = bodyBoxCube.GetComponent<Renderer>().material;
					material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
					material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
					material.SetInt("_ZWrite", 0);
					material.DisableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_ALPHABLEND_ON");
					material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = 3000;
					bodyBoxCube.GetComponent<Renderer>().material.color = new Color(1, 1, 1, 0.3f);
					bodyBoxCube.GetComponent<Collider>().enabled = false;
				}
				if (headBox == null)//headbox needs an empty gameobject with a cube inside it because scaling the game object fucks the position
				{
					headBox = new GameObject().transform;
					headBox.name = "HeadBox";
					var headBoxCube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
					headBoxCube.name = "HeadBoxCube";
					headBox.parent = umaData.umaRoot.transform.parent;
					headBoxCube.parent = headBox;
					var material = headBoxCube.GetComponent<Renderer>().material;
					material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
					material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
					material.SetInt("_ZWrite", 0);
					material.DisableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_ALPHABLEND_ON");
					material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = 3000;
					headBoxCube.GetComponent<Renderer>().material.color = new Color(1, 1, 1, 0.3f);
					headBoxCube.GetComponent<Collider>().enabled = false;
				}
				bodyBox.transform.position = new Vector3(umaData.umaRoot.transform.position.x, umaData.umaRoot.transform.position.y + (chinHeight / 2f), umaData.umaRoot.transform.position.z);
				bodyBox.Find("BodyBoxCube").transform.localScale = new Vector3(umaData.characterRadius, chinHeight, umaData.characterRadius);
				headBox.transform.position = new Vector3(umaData.umaRoot.transform.position.x, umaData.umaRoot.transform.position.y + chinHeight + (headHeight / 2f), (umaData.umaRoot.transform.position.z));//wtf isnt _mechanimBoneDict["Head"].position.z the position of the head bone at z??!!??
				headBox.Find("HeadBoxCube").transform.localScale = new Vector3(headWidth, headHeight, headWidth);
			}
		}

		private void RemoveDebugBoxes(UMAData umaData)
		{
			var bodyBox = umaData.umaRoot.transform.parent.transform.Find("BodyBox");
			var headBox = umaData.umaRoot.transform.parent.transform.Find("HeadBox");
			if (bodyBox != null)
				Destroy(bodyBox.gameObject);
			if (headBox != null)
				Destroy(headBox.gameObject);

		}*/
#endif

		#endregion


		#region BACKWARDS COMPATIBILITY
		//Obsolete, we dont need this since we get the UMASkeleton in the ApplyDnaAction anyway
		//public List<HashListItem> hashList = new List<HashListItem>();

		//this is a legacy field for _skeletonModifiers. Once the user upgrades this data is contained in the SkeletonModifiersDNAConverter plugin in the convertersController asset
		[SerializeField]
		[FormerlySerializedAs("skeletonModifiers")]
		private List<SkeletonModifier> _skeletonModifiers = new List<SkeletonModifier>();

		[Tooltip("The Overall Scale for the object this converter is controling. For the stock UMA HumanMale/Female races this is 0.88f for male and 0.81f for female, this may be different for custom races. This value is sent to converters that perform bone modifications to the character and should be considered the 'base scale'")]
		[FormerlySerializedAs("overallScale")]
		public float overallScale = 1f;

		[Tooltip("The bone the overall scale will be applied to. For rigs based on the standard UMA Rig, this is usually the 'Position' bone.")]
		[FormerlySerializedAs("overallScaleBone")]
		public string overallScaleBone = "Position";

		[FormerlySerializedAs("overallScaleBoneHash")]
		public int overallScaleBoneHash = -1084586333;//hash of the Position Bone

		[Tooltip("Should this converter update the characterHeight/Radius/Mass (which are used for calculating the size of its collder and its weight for physics). Usually you only enable this for the charcaters 'base' converter, but some slots, like a massive gun, may also make the character heavier for example.")]
		[FormerlySerializedAs("updateCharacterHeightRadiusMass")]
		public bool overallModifiersEnabled = true;

		[Tooltip("Tweaks the character Radius that is used to calculate the fitting of the collider.")]
		[FormerlySerializedAs("radiusAdjust")]
		public Vector2 radiusAdjust = new Vector2(0.23f, 0);

		[FormerlySerializedAs("massModifiers")]
		public Vector3 massModifiers = new Vector3(46f, 26f, 26f);

		[Tooltip("When Unity calculates the bounds it uses bounds based on the animation included when the mesh was exported. This can mean the bounds are not very 'tight' or go below the characters feet. Checking this will make the bounds tight to the characters head/feet. You can then use the 'BoundsPadding' option below to add/remove extra space.")]
		[FormerlySerializedAs("tightenBounds")]
		public bool tightenBounds = true;

		[Tooltip("You can pad or tighten your bounds with these controls")]
		[FormerlySerializedAs("boundsAdjust")]
		public Vector3 boundsAdjust = Vector3.zero;

		[Obsolete("Please inspect this behaviour to 'Upgrade' it. You can then access its skeletonModifiers via this assets 'ConverterController'")]
		public List<SkeletonModifier> skeletonModifiers
		{
			get
			{
				if (_converterController != null)
					return _converterController.SkeletonModifiersFirst;
				return _skeletonModifiers;
			}
			set
			{
				if (_converterController != null)
					_converterController.SkeletonModifiersFirst = value;
				else
					_skeletonModifiers = value;
			}
		}

		[SerializeField]
		[FormerlySerializedAs("startingPose")]
		private UMABonePose _startingPose = null;

		[Obsolete("You can have multiple starting poses now. Please inspect this behaviour 'Upgrade' it. You can then access its starting poses via this assets 'ConverterController'")]
		public UMABonePose startingPose
		{
			get
			{
				if (_converterController != null)
					return _converterController.StartingPoseFirst;
				return _startingPose;
			}
			set
			{
				if (_converterController != null)
					_converterController.StartingPoseFirst = value;
				else
					_startingPose = value;
			}
		}

		[Range(0f, 1f)]
		[Tooltip("Adjust the influence the StartingPose has on the mesh. This is the 'default' weight for the pose and affects all avatars that use this converter behaviour")]
		public float startingPoseWeight = 1f;

		/*int GetHash(string hashName)
        {
            return hashList[hashList.FindIndex(s => s.hashName == hashName)].hash;
        }*/

		[Obsolete("This method will be removed in future versions. Please call ApplyDynamicDnaAction instead")]
		public void UpdateDynamicUMADnaBones(UMAData umaData, UMASkeleton skeleton, bool asReset = false)
		{
			ApplyDynamicDnaAction(umaData, skeleton, asReset);
		}

		//The legacy method for applying skeletonModifiers defined inside this converter
		private void ApplySkeletonModifiers(UMAData umaData, UMADnaBase umaDna, UMASkeleton skeleton)
		{
			for (int i = 0; i < _skeletonModifiers.Count; i++)
			{
				_skeletonModifiers[i].umaDNA = umaDna;
				_skeletonModifiers[i].masterWeight = 1f;
				//getting rid of BoneHashes list - when a bone name is added in the editor the skeleton modifier always generates the hash

				var thisHash = (_skeletonModifiers[i].hash != 0) ? _skeletonModifiers[i].hash : UMAUtils.StringToHash(_skeletonModifiers[i].hashName);

				//These are a Vector3 where Value?.x is the calculated value and Value?.y is min and Value?.z is max
				var thisValueX = _skeletonModifiers[i].CalculateValueX(umaDna, 1f);
				var thisValueY = _skeletonModifiers[i].CalculateValueY(umaDna, 1f);
				var thisValueZ = _skeletonModifiers[i].CalculateValueZ(umaDna, 1f);

				//use the overallScaleBoneHash property instead so the user can define the bone that is used here (by default its the 'Position' bone in an UMA Rig)
				/*if (_skeletonModifiers[i].hash == overallScaleBoneHash && _skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Scale)
				{
					var calcVal = thisValueX.x - _skeletonModifiers[i].valuesX.val.value + overallScale;//effectively (when dna is 0)1-1+0.88
					Debug.Log("DCSUMA overallScale calcVal[" + calcVal + "] =  (thisValueX.x [" + thisValueX.x + "] - _skeletonModifiers[i].valuesX.val.value[" + _skeletonModifiers[i].valuesX.val.value + "] + overallScale[" + overallScale+"]");
					var overallScaleCalc = Mathf.Clamp(calcVal, thisValueX.y, thisValueX.z);
					skeleton.SetScale(_skeletonModifiers[i].hash, new Vector3(overallScaleCalc, overallScaleCalc, overallScaleCalc));
				}
				else*/ if (_skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Position)
				{
					skeleton.SetPositionRelative(thisHash,
						new Vector3(
							Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
							Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
							Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z)));
				}
				else if (_skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Rotation)
				{
					skeleton.SetRotationRelative(thisHash,
						Quaternion.Euler(new Vector3(
							Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
							Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
							Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z))), 1f);
				}
				else if (_skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Scale)
				{
					skeleton.SetScale(thisHash,
						new Vector3(
							Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
							Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
							Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z)));
				}

			}
		}
		//the legacy method for applying the starting pose that was defined in this converter
		private void ApplyStartingPose(UMASkeleton skeleton)
		{
			if (_startingPose != null)
			{
				for (int i = 0; i < _startingPose.poses.Length; i++)
				{
					skeleton.Morph(_startingPose.poses[i].hash, _startingPose.poses[i].position, _startingPose.poses[i].scale, _startingPose.poses[i].rotation, startingPoseWeight);
				}
			}
		}
		#endregion

		#region SPECIAL TYPES

		/*
		// OBSOLETE a class for Bone hashes
		[Serializable]
        public class HashListItem
        {
            public string hashName = "";
            public int hash = 0;

			public HashListItem() { }

			public HashListItem(string nameToAdd)
			{
				hashName = nameToAdd;
				hash = UMAUtils.StringToHash(nameToAdd);
            }
			public HashListItem(string nameToAdd, int hashToAdd)
			{
				hashName = nameToAdd;
				var thisHash = hashToAdd;
				if(thisHash == 0)
					thisHash = UMAUtils.StringToHash(nameToAdd);
				hash = thisHash;
			}
		}*/

		#endregion
	}
}
