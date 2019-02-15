using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;
using UMA.PoseTools;

namespace UMA.CharacterSystem
{
	public class DynamicDNAConverterCustomizer : MonoBehaviour
	{

#if UNITY_EDITOR
		public GameObject dynamicDnaConverterPrefab;//used for saving dnaConverter as new//TODO check this is needed any more since UMAUtils can make/clone a prefab now
		public RuntimeAnimatorController TposeAnimatorController;
		public RuntimeAnimatorController AposeAnimatorController;
		public RuntimeAnimatorController MovementAnimatorController;
		public UMAAvatarBase targetUMA;
		/*Texture2D targetAlphaTex;
        float targetAlpha = 1;
        bool targetAlphaSet = true;*/
		public UMAAvatarBase guideUMA;
		/*Texture2D guideAlphaTex;
        float guideAlpha = 0.5f;
        bool guideAlphaSet = false;*/
		[System.NonSerialized]
		UMAAvatarBase activeUMA;
		[System.NonSerialized]
		string activeUMARace;

		[SerializeField]//Why-Because they need to be available to the editor-this wont work with IDNAConverter tho I dont expect
		public List<IDNAConverter> availableConverters = new List<IDNAConverter>();

		[SerializeField]//Why-because they need to be available to the editor-this wont work with IDNAConverter tho I dont expect
		public IDNAConverter selectedConverter;

		GameObject converterBackupsFolder = null;

		//Dont think customizer is going to do this now- skeletonModifiers in the old behaviour have import tools of their own and using controller duplicating the behaviour makes no sense
		public DynamicDNAConverterBehaviour converterToImport;

		//I think backups will be handled by the controller in the new world
		Dictionary<string, DynamicDNAConverterBehaviour> converterBackups = new Dictionary<string, DynamicDNAConverterBehaviour>();
		Dictionary<string, string[]> dnaAssetNamesBackups = new Dictionary<string, string[]>();
		Dictionary<string, UMABonePose.PoseBone[]> poseBonesBackups = new Dictionary<string, UMABonePose.PoseBone[]>();

		public bool drawBoundsGizmo = true;

		private string bonePoseSaveName;
		private GameObject tempAvatarPreDNA;
		private GameObject tempAvatarPostDNA;
		//used as the 'epsilon' value when comparing bones during a 'create starting Pose from Current DNA' operation
		//decent values are between around 0.000005f and 0.0005f
		public float bonePoseAccuracy = 0.00005f;

		public UnityEvent BonesCreated = new UnityEvent();

		//UndoRedoDelegate
		void OnUndo()
		{
			UpdateUMA();
		}

		public void StartListeningForUndo()
		{
			Undo.undoRedoPerformed += OnUndo;
		}
		public void StopListeningForUndo()
		{
			Undo.undoRedoPerformed -= OnUndo;
		}

		// Use this for initialization
		void Start()
		{
			//TODO make the guide/target semi transparent
			/*targetAlphaTex = new Texture2D(2,2);
            guideAlphaTex = new Texture2D(2,2);
            var AlphaColor = new Color(guideAlpha, guideAlpha, guideAlpha, 1f);
            guideAlphaTex.SetPixel(0, 0, AlphaColor);
            guideAlphaTex.SetPixel(0, 1, AlphaColor);
            guideAlphaTex.SetPixel(1, 0, AlphaColor);
            guideAlphaTex.SetPixel(1, 1, AlphaColor);*/
			if (targetUMA != null)
			{
				activeUMA = targetUMA;
				activeUMA.CharacterUpdated.AddListener(SetAvailableConverters);
			}
		}
		// Update is called once per frame
		void Update()
		{
			if (activeUMA != targetUMA)
			{
				activeUMA = targetUMA;
				if (activeUMA.umaData != null)
					SetAvailableConverters(activeUMA.umaData);
				else
					availableConverters.Clear();
			}
			if (activeUMA != null)
				if (activeUMA.umaData != null)
					if (activeUMA.umaData.umaRecipe != null)
						if (activeUMA.umaData.umaRecipe.raceData != null)
							if (activeUMA.umaData.umaRecipe.raceData.raceName != activeUMARace)
							{
								activeUMARace = activeUMA.umaData.umaRecipe.raceData.raceName;
								SetAvailableConverters(activeUMA.umaData);
							}
			//TODO make the guide /target semi transparent...

		}

		public void SetAvatar(GameObject newAvatarObject)
		{
			if (guideUMA != null)
				if (newAvatarObject == guideUMA.gameObject)
				{
					//reset guide transparency one we have sussed out how to do this
					guideUMA = null;
				}
			if (targetUMA == null || newAvatarObject != targetUMA.gameObject)
			{
				if (newAvatarObject.GetComponent<UMAAvatarBase>() != null)
				{
					targetUMA = newAvatarObject.GetComponent<UMAAvatarBase>();
					activeUMA = newAvatarObject.GetComponent<UMAAvatarBase>();
					SetAvailableConverters(activeUMA.umaData);
					selectedConverter = null;
				}
				else
				{
					targetUMA = null;
					activeUMA = null;
					availableConverters.Clear();
					selectedConverter = null;
				}
			}
		}

		void OnApplicationQuit()
		{
			RestoreBackupVersion();
		}

		public void SetAvailableConverters(UMAData umaData)
		{
			activeUMA.CharacterUpdated.RemoveListener(SetAvailableConverters);
			if (activeUMARace == "")
				activeUMARace = umaData.umaRecipe.raceData.raceName;
			availableConverters.Clear();
			foreach (IDNAConverter converter in umaData.umaRecipe.raceData.dnaConverterList)
			{
				if (converter is IDynamicDNAConverter)
				{
					availableConverters.Add(converter);
				}
			}
			//slots might also have converters
			foreach(SlotData slot in umaData.umaRecipe.GetAllSlots())
			{
				if (slot.asset.slotDNA != null && slot.asset.slotDNA.GetType() == typeof(DynamicDNAConverterBehaviour))
					availableConverters.Add(slot.asset.slotDNA as DynamicDNAConverterBehaviour);
			}
		}

		public void SetTPoseAni()
		{
			if (TposeAnimatorController == null)
				return;
			SwapAnimator(TposeAnimatorController);
		}


		public void SetAPoseAni()
		{
			if (AposeAnimatorController == null)
				return;
			SwapAnimator(AposeAnimatorController);
		}


		public void SetMovementAni()
		{
			if (MovementAnimatorController == null)
				return;
			SwapAnimator(MovementAnimatorController);
		}

		private void SwapAnimator(RuntimeAnimatorController animatorToUse)
		{
			//changing the animationController in 5.6 resets the rotation of this game object so store the rotation and set it back
			if (guideUMA != null)
			{
				if (guideUMA.gameObject.GetComponent<Animator>())
				{
					var guideOriginalRot = Quaternion.identity;
					if (guideUMA.umaData != null)
						guideOriginalRot = guideUMA.umaData.transform.localRotation;
					guideUMA.gameObject.GetComponent<Animator>().runtimeAnimatorController = animatorToUse;
					if (guideUMA.umaData != null)
						guideUMA.umaData.transform.localRotation = guideOriginalRot;
				}
			}
			if (activeUMA != null)
			{
				if (activeUMA.gameObject.GetComponent<Animator>())
				{
					var originalRot = Quaternion.identity;
					if (activeUMA.umaData != null)
						originalRot = activeUMA.umaData.transform.localRotation;
					activeUMA.gameObject.GetComponent<Animator>().runtimeAnimatorController = animatorToUse;
					if (activeUMA.umaData != null)
						activeUMA.umaData.transform.localRotation = originalRot;
				}
			}
			UpdateUMA();
		}

		void OnDrawGizmos()
		{
			if (drawBoundsGizmo && activeUMA != null)
			{
				if (activeUMA.umaData == null || activeUMA.umaData.rendererCount == 0)
					return;
				Gizmos.color = Color.white;
				Gizmos.DrawWireCube(activeUMA.umaData.GetRenderer(0).bounds.center, activeUMA.umaData.GetRenderer(0).bounds.size);
			}
		}

		/// <summary>
		/// Aligns the guide UMA to the Target UMA's position
		/// </summary>
		public void AlignGuideToTarget()
		{
			if (guideUMA == null || activeUMA == null)
			{
				Debug.LogWarning("Both the Gude UMA and the UMA to Customize need to be set to align them to each other!");
				return;
			}
			var activeUMAPosition = activeUMA.gameObject.transform.position;
			guideUMA.gameObject.transform.position = activeUMAPosition;
		}

		#region Value Modification Methods
		/// <summary>
		/// Set the Target UMA's DNA to match the Guide UMA's dna
		/// </summary>
		public void ImportGuideDNAValues()
		{
			if (guideUMA == null)
			{
				Debug.LogWarning("No Guide UMA was set to get DNA from!");
				return;
			}
			UMADnaBase[] activeUmaDNA = activeUMA.umaData.GetAllDna();
			UMADnaBase[] guideUmaDNA = guideUMA.umaData.GetAllDna();
			foreach (UMADnaBase gdna in guideUmaDNA)
			{
				foreach (UMADnaBase dna in activeUmaDNA)
				{
					if (dna is DynamicUMADnaBase)
					{
						((DynamicUMADnaBase)dna).ImportUMADnaValues(gdna);
					}
				}
			}
			UpdateUMA();
		}

		/*
		/// <summary>
		/// Imports the settings and assets from another DynamicDNAConverterBehaviour
		/// </summary>
		/// <returns></returns>
		public bool ImportConverterValues()
		{
			if (converterToImport == null)
			{
				Debug.LogWarning("There was no converter to import from");
				return false;
			}
			if (selectedConverter == null)
			{
				Debug.LogWarning("There was no converter to import to");
				return false;
			}
			selectedConverter.startingPose = converterToImport.startingPose;
			selectedConverter.startingPoseWeight = converterToImport.startingPoseWeight;
			selectedConverter.dnaAsset = converterToImport.dnaAsset;
			selectedConverter.skeletonModifiers = converterToImport.skeletonModifiers;
			//Getting Rid of Hash List
			//selectedConverter.hashList = converterToImport.hashList;
			selectedConverter.overallModifiersEnabled = converterToImport.overallModifiersEnabled;
			//.heightModifiers = converterToImport.heightModifiers;
			selectedConverter.radiusAdjust = converterToImport.radiusAdjust;
			selectedConverter.massModifiers = converterToImport.massModifiers;
			Debug.Log("Imported " + converterToImport.name + " settings into " + selectedConverter.name);
			return true;
		}*/

		private bool LocalTransformsMatch(Transform t1, Transform t2)
		{
			if ((t1.localPosition - t2.localPosition).sqrMagnitude > bonePoseAccuracy) return false;
			if ((t1.localScale - t2.localScale).sqrMagnitude > bonePoseAccuracy) return false;
			if (t1.localRotation != t2.localRotation) return false;

			return true;
		}

#pragma warning disable 618 //disable obsolete warning

		private bool _applyAndResetOnCreateBP = false;

		private void CreateBonePoseCallback(UMAData umaData)
		{
			UMA.PoseTools.UMABonePose bonePose = CreatePoseAsset("", bonePoseSaveName);
			//I dont think this should have ever overwritten the existing one
			/*if (selectedConverter.startingPose == null)
			{
				bonePose = CreatePoseAsset("", bonePoseSaveName);
			}
			else
			{
				bonePose = selectedConverter.startingPose;
				bonePose.poses = new UMABonePose.PoseBone[1];
			}*/

			UMASkeleton skeletonPreDNA = tempAvatarPreDNA.GetComponent<UMADynamicAvatar>().umaData.skeleton;
			UMASkeleton skeletonPostDNA = tempAvatarPostDNA.GetComponent<UMADynamicAvatar>().umaData.skeleton;

			Transform transformPreDNA;
			Transform transformPostDNA;
			bool transformDirty;
			int parentHash;
			foreach (int boneHash in skeletonPreDNA.BoneHashes)
			{
				skeletonPreDNA.TryGetBoneTransform(boneHash, out transformPreDNA, out transformDirty, out parentHash);
				skeletonPostDNA.TryGetBoneTransform(boneHash, out transformPostDNA, out transformDirty, out parentHash);

				if ((transformPreDNA == null) || (transformPostDNA == null))
				{
					Debug.LogWarning("Bad bone hash in skeleton: " + boneHash);
					continue;
				}

				if (!LocalTransformsMatch(transformPreDNA, transformPostDNA))
				{
					bonePose.AddBone(transformPreDNA, transformPostDNA.localPosition, transformPostDNA.localRotation, transformPostDNA.localScale);
				}
			}	
			
			UMAUtils.DestroySceneObject(tempAvatarPreDNA);
			UMAUtils.DestroySceneObject(tempAvatarPostDNA);
			

			// This can be very helpful for testing
			/*
			bonePose.ApplyPose(skeletonPreDNA, 1.0f);
			*/

			EditorUtility.SetDirty(bonePose);
			AssetDatabase.SaveAssets();

			if (_applyAndResetOnCreateBP)
			{
				DynamicDNAConverterController converterController = (selectedConverter is DynamicDNAConverterController) ? (selectedConverter as DynamicDNAConverterController) : null;
				DynamicDNAConverterBehaviour converterBehaviour = (selectedConverter is DynamicDNAConverterBehaviour) ? (selectedConverter as DynamicDNAConverterBehaviour) : null;
				//UMA2.8+ fixDNAPrefabs Removed the converterBehaviour.ConverterController field, it should be directly assigned to the Races/Slots now
				//if (converterBehaviour.ConverterController != null)
				//	converterController = converterBehaviour.ConverterController;
				if (converterController != null)
				{
					//find the first BonePoseDNAConverterPlugin and add the pose to it
					var existingBPCPs = converterController.GetPlugins(typeof(BonePoseDNAConverterPlugin));
					BonePoseDNAConverterPlugin thisBPCP;
					if (existingBPCPs.Count > 0)
					{
						thisBPCP = existingBPCPs[0] as BonePoseDNAConverterPlugin;
						//Turn off any other starting poses?
						for (int i = 0; i < existingBPCPs.Count; i++)
						{
							for (int bi = 0; bi < (existingBPCPs[i] as BonePoseDNAConverterPlugin).poseDNAConverters.Count; bi++)
							{
								(existingBPCPs[i] as BonePoseDNAConverterPlugin).poseDNAConverters[bi].startingPoseWeight = 0f;
							}
						}
					}
					else
					{
						//if there isn't one create it
						thisBPCP = converterController.AddPlugin(typeof(BonePoseDNAConverterPlugin)) as BonePoseDNAConverterPlugin;
					}
					thisBPCP.poseDNAConverters.Add(new BonePoseDNAConverterPlugin.BonePoseDNAConverter(bonePose, 1f));
					Debug.Log(bonePose.name + " added as a starting pose to " + thisBPCP.name);
				}
				else if(converterBehaviour != null)
				{
					// Set this asset as the converters pose asset
					converterBehaviour.startingPose = bonePose;
					//make sure its fully applied
					converterBehaviour.startingPoseWeight = 1f;
				}

				// Reset all the DNA values for target Avatar to default
				UMADnaBase[] targetDNA = activeUMA.umaData.GetAllDna();
				foreach (UMADnaBase dnaEntry in targetDNA)
				{
					for (int i = 0; i < dnaEntry.Values.Length; i++)
					{
						dnaEntry.SetValue(i, 0.5f);
					}
				}

				// Optionally clear the DNA from the base recipe,
				// since it's now included in the new starting pose
				UMARecipeBase baseRaceRecipe = activeUMA.umaData.umaRecipe.GetRace().baseRaceRecipe;
				if (baseRaceRecipe != null)
				{
					if (EditorUtility.DisplayDialog("Base Recipe Cleanup", "Starting Pose created. Remove DNA from base recipe of active race? Choose 'RemoveDNA' if your intention is to replace modifications made by a recipes starting DNA values with the created pose.", "Remove DNA", "Keep DNA"))
					{
						UMAData.UMARecipe baseRecipeData = new UMAData.UMARecipe();
						baseRaceRecipe.Load(baseRecipeData, activeUMA.context);
						baseRecipeData.ClearDna();
						baseRaceRecipe.Save(baseRecipeData, activeUMA.context);
					}
				}
			}
		}
#pragma warning restore 618

		/// <summary>
		/// Calculates the required poses necessary for an UMABonePose asset to render the Avatar in its current post DNA state, 
		/// adds these to the selected converters 'Starting Pose' asset- creating one if necessary and resets current Dna values to 0.
		/// </summary>
		public bool CreateBonePosesFromCurrentDna(string createdAssetName = "", bool applyAndReset = false)
		{
			if (activeUMA == null || selectedConverter == null)
			{
				Debug.LogWarning("activeUMA == null || selectedConverter == null");
				return false;
			}

			bonePoseSaveName = createdAssetName;
			//we need to close any dna editing panels because these will need to update after this process completes
			/*UMA.CharacterSystem.Examples.TestCustomizerDD[] charCustomizerDDs = FindObjectsOfType<UMA.CharacterSystem.Examples.TestCustomizerDD>();
			for (int i = 0; i < charCustomizerDDs.Length; i++)
				charCustomizerDDs[i].CloseAllPanels();*/
			if (BonesCreated != null)
				BonesCreated.Invoke();
			// Build a temporary version of the Avatar with no DNA to get original state
			UMADnaBase[] activeDNA = activeUMA.umaData.umaRecipe.GetAllDna();
			SlotData[] activeSlots = activeUMA.umaData.umaRecipe.GetAllSlots();
			int slotIndex;

			tempAvatarPreDNA = new GameObject("Temp Raw Avatar");
			//tempAvatarPreDNA.transform.parent = activeUMA.transform.parent;
			tempAvatarPreDNA.transform.localPosition = Vector3.zero;
			tempAvatarPreDNA.transform.localRotation = Quaternion.identity;

			UMADynamicAvatar tempAvatar = tempAvatarPreDNA.AddComponent<UMADynamicAvatar>();
			tempAvatar.umaGenerator = activeUMA.umaGenerator;
			tempAvatar.Initialize();
			tempAvatar.umaData.umaRecipe = new UMAData.UMARecipe();
			tempAvatar.umaData.umaRecipe.raceData = activeUMA.umaData.umaRecipe.raceData;
			slotIndex = 0;
			foreach (SlotData slotEntry in activeSlots)
			{
				if ((slotEntry == null) || slotEntry.dontSerialize) continue;
				tempAvatar.umaData.umaRecipe.SetSlot(slotIndex++, slotEntry);
			}
			tempAvatar.Show();

			tempAvatarPostDNA = new GameObject("Temp DNA Avatar");
			//tempAvatarPostDNA.transform.parent = activeUMA.transform.parent;
			tempAvatarPostDNA.transform.localPosition = Vector3.zero;
			tempAvatarPostDNA.transform.localRotation = Quaternion.identity;

			UMADynamicAvatar tempAvatar2 = tempAvatarPostDNA.AddComponent<UMADynamicAvatar>();
			tempAvatar2.umaGenerator = activeUMA.umaGenerator;
			tempAvatar2.Initialize();
			tempAvatar2.umaData.umaRecipe = new UMAData.UMARecipe();
			tempAvatar2.umaData.umaRecipe.raceData = activeUMA.umaData.umaRecipe.raceData;
			tempAvatar2.umaData.umaRecipe.slotDataList = activeUMA.umaData.umaRecipe.slotDataList;
			slotIndex = 0;
			foreach (SlotData slotEntry in activeSlots)
			{
				if ((slotEntry == null) || slotEntry.dontSerialize) continue;
				tempAvatar2.umaData.umaRecipe.SetSlot(slotIndex++, slotEntry);
			}
			foreach (UMADnaBase dnaEntry in activeDNA)
			{
				tempAvatar2.umaData.umaRecipe.AddDna(dnaEntry);
			}
			_applyAndResetOnCreateBP = applyAndReset;
			tempAvatar2.umaData.OnCharacterCreated += CreateBonePoseCallback;
			tempAvatar2.Show();

			return true;

		}
		#endregion

		#region Save and Backup Methods

		//TODO THIS ALL NEEDS SOME WORK- Use UnityUndo!

#pragma warning disable 618 //disable obsolete warning
		/// <summary>
		/// Makes a backup of the currently selected converter whose values are restored to the current converter when the Application stops playing (unless you Save the changes)
		/// </summary>
		/// <param name="converterToBU"></param>
		public void BackupConverter(DynamicDNAConverterBehaviour converterToBU = null)
		{
			if (converterToBU == null && selectedConverter is DynamicDNAConverterBehaviour)
			{
				converterToBU = (DynamicDNAConverterBehaviour)selectedConverter;
			}
			if (converterToBU != null)
			{
				if (converterBackupsFolder == null)
				{
					converterBackupsFolder = new GameObject();
					converterBackupsFolder.name = "CONVERTER BACKUPS DO NOT DELETE";
				}
				if (!converterBackups.ContainsKey(converterToBU.name))
				{
					var thisConverterBackup = Instantiate<DynamicDNAConverterBehaviour>(converterToBU);
					thisConverterBackup.transform.parent = converterBackupsFolder.transform;
					converterBackups[converterToBU.name] = thisConverterBackup;
				}
				if (converterToBU.dnaAsset != null)
				{
					dnaAssetNamesBackups[converterToBU.dnaAsset.name] = (string[])converterToBU.dnaAsset.Names.Clone();
				}
				if (converterToBU.startingPose != null)
				{
					poseBonesBackups[converterToBU.startingPose.name] = DeepPoseBoneClone(converterToBU.startingPose.poses);
				}
			}
		}

		private UMABonePose.PoseBone[] DeepPoseBoneClone(UMABonePose.PoseBone[] posesToCopy)
		{
			var poseBonesCopy = new UMABonePose.PoseBone[posesToCopy.Length];
			for (int i = 0; i < posesToCopy.Length; i++)
			{
				poseBonesCopy[i] = new UMABonePose.PoseBone();
				poseBonesCopy[i].bone = posesToCopy[i].bone;
				poseBonesCopy[i].hash = posesToCopy[i].hash;
				poseBonesCopy[i].position = new Vector3(posesToCopy[i].position.x, posesToCopy[i].position.y, posesToCopy[i].position.z);
				poseBonesCopy[i].rotation = new Quaternion(posesToCopy[i].rotation.x, posesToCopy[i].rotation.y, posesToCopy[i].rotation.z, posesToCopy[i].rotation.w);
				poseBonesCopy[i].scale = new Vector3(posesToCopy[i].scale.x, posesToCopy[i].scale.y, posesToCopy[i].scale.z);
			}
			return poseBonesCopy;
		}

		/// <summary>
		/// Restores the converters values back to the original or saved values. Also called when the application stops playing so that any changes made while the game is running are not saved (unless the user calls the SaveChanges method previously).
		/// </summary>
		/// <param name="converterName"></param>
		public void RestoreBackupVersion(string converterName = "")
		{
			if (availableConverters.Count > 0)
			{
				for (int i = 0; i < availableConverters.Count; i++)
				{
					DynamicDNAConverterBehaviour buConverter;
					if (availableConverters[i] is DynamicDNAConverterBehaviour && converterBackups.TryGetValue(availableConverters[i].name, out buConverter))
					{
						if (converterName == "" || converterName == availableConverters[i].name)
						{
							((DynamicDNAConverterBehaviour)availableConverters[i]).dnaAsset = buConverter.dnaAsset;
							if (((DynamicDNAConverterBehaviour)availableConverters[i]).dnaAsset != null)
							{
								string[] buNames;
								if (dnaAssetNamesBackups.TryGetValue(((DynamicDNAConverterBehaviour)availableConverters[i]).dnaAsset.name, out buNames))
								{
									((DynamicDNAConverterBehaviour)availableConverters[i]).dnaAsset.Names = buNames;
								}
							}
							//we need to restore these regardless of whether the converter had a startingPose or not when we started playing
							if (((DynamicDNAConverterBehaviour)availableConverters[i]).startingPose != null)
							{
								UMABonePose.PoseBone[] buPoses;
								if (poseBonesBackups.TryGetValue(((DynamicDNAConverterBehaviour)availableConverters[i]).startingPose.name, out buPoses))
								{
									((DynamicDNAConverterBehaviour)availableConverters[i]).startingPose.poses = buPoses;
									EditorUtility.SetDirty(((DynamicDNAConverterBehaviour)availableConverters[i]).startingPose);
									AssetDatabase.SaveAssets();
								}
							}
							((DynamicDNAConverterBehaviour)availableConverters[i]).startingPose = buConverter.startingPose;
							//
							((DynamicDNAConverterBehaviour)availableConverters[i]).skeletonModifiers = buConverter.skeletonModifiers;
							//availableConverters[i].hashList = buConverter.hashList;
							((DynamicDNAConverterBehaviour)availableConverters[i]).overallModifiersEnabled = buConverter.overallModifiersEnabled;
							//new
							((DynamicDNAConverterBehaviour)availableConverters[i]).tightenBounds = buConverter.tightenBounds;
							((DynamicDNAConverterBehaviour)availableConverters[i]).boundsAdjust = buConverter.boundsAdjust;
							//end new
							((DynamicDNAConverterBehaviour)availableConverters[i]).overallScale = buConverter.overallScale;
							//availableConverters[i].heightModifiers = buConverter.heightModifiers;
							((DynamicDNAConverterBehaviour)availableConverters[i]).radiusAdjust = buConverter.radiusAdjust;
							((DynamicDNAConverterBehaviour)availableConverters[i]).massModifiers = buConverter.massModifiers;
						}
					}
				}
			}
		}

		/// <summary>
		/// Saves the current changes to the converter by removing the backup that would otherwise reset the converter when the Application stops playing.
		/// </summary>
		/// <param name="all"></param>
		public void SaveChanges(bool all = false)
		{
			bool doSave = true;
#if UNITY_EDITOR
			doSave = EditorUtility.DisplayDialog("Confirm Save", "This will overwrite the values in the currently selected dna converter. Are you sure?", "Save", "Cancel");
#endif
			if (doSave)
			{
				if (all)
				{
					foreach (KeyValuePair<string, DynamicDNAConverterBehaviour> kp in converterBackups)
					{
						if (kp.Value.dnaAsset != null && dnaAssetNamesBackups.ContainsKey(kp.Value.dnaAsset.name))
						{
							EditorUtility.SetDirty(kp.Value.dnaAsset);
							dnaAssetNamesBackups.Remove(kp.Value.dnaAsset.name);
						}
						if (kp.Value.startingPose != null && poseBonesBackups.ContainsKey(kp.Value.startingPose.name))
						{
							EditorUtility.SetDirty(kp.Value.startingPose);
							poseBonesBackups.Remove(kp.Value.startingPose.name);
						}
					}
					if (availableConverters.Count > 0)
					{
						for (int i = 0; i < availableConverters.Count; i++)
						{
							if(availableConverters[i] is DynamicDNAConverterBehaviour)
								EditorUtility.SetDirty(((DynamicDNAConverterBehaviour)availableConverters[i]));
						}
					}
					AssetDatabase.SaveAssets();
					foreach (KeyValuePair<string, DynamicDNAConverterBehaviour> kp in converterBackups)
					{
						Destroy(kp.Value);
					}
					converterBackups.Clear();
					if (availableConverters.Count > 0)
					{
						for (int i = 0; i < availableConverters.Count; i++)
						{
							if(availableConverters[i] is DynamicDNAConverterBehaviour)
								BackupConverter(((DynamicDNAConverterBehaviour)availableConverters[i]));
						}
					}
				}
				else
				{
					if (selectedConverter != null)
					{
						if ((selectedConverter is DynamicDNAConverterBehaviour))
						{
							if (converterBackups.ContainsKey(selectedConverter.name))
							{
								if (converterBackups[selectedConverter.name].dnaAsset != null)
								{
									EditorUtility.SetDirty(converterBackups[selectedConverter.name].dnaAsset);
									dnaAssetNamesBackups.Remove(converterBackups[selectedConverter.name].dnaAsset.name);
								}
								if (converterBackups[selectedConverter.name].startingPose != null)
								{
									EditorUtility.SetDirty(converterBackups[selectedConverter.name].startingPose);
									poseBonesBackups.Remove(converterBackups[selectedConverter.name].startingPose.name);
								}
								EditorUtility.SetDirty((selectedConverter as DynamicDNAConverterBehaviour));
								AssetDatabase.SaveAssets();
								Destroy(converterBackups[selectedConverter.name]);
								converterBackups.Remove(selectedConverter.name);
								BackupConverter();
							}
						}
					}
				}
			}
		}
#if UNITY_EDITOR
		/// <summary>
		/// Creates a new Converter Behaviour Prefab with a converter on it that has the current settings. This can then be applied to a Race's Dna Converters.
		/// </summary>
		public void SaveChangesAsNew()
		{
			if (dynamicDnaConverterPrefab == null)
			{
				Debug.LogWarning("There was no prefab set up in the DynamicDnaConverterCustomizer. This must be set in order to save a new prefab.");
				return;
			}
			if (selectedConverter == null || !(selectedConverter is DynamicDNAConverterBehaviour))
			{
				if(selectedConverter == null)
					Debug.LogWarning("No converter was selected to save!");
				return;
			}
			var selectedDCB = (selectedConverter as DynamicDNAConverterBehaviour);
			var fullPath = EditorUtility.SaveFilePanel("Save New DynamicDnaConverterBehaviour", Application.dataPath, "", "prefab");
			var path = fullPath.Replace(Application.dataPath, "Assets");
			var filename = System.IO.Path.GetFileNameWithoutExtension(path);
			var thisNewPrefabGO = Instantiate(dynamicDnaConverterPrefab);
			thisNewPrefabGO.name = filename;
			var newPrefabConverter = thisNewPrefabGO.GetComponent<DynamicDNAConverterBehaviour>();
			if (newPrefabConverter != null)
			{
				newPrefabConverter.dnaAsset = selectedDCB.dnaAsset;
				newPrefabConverter.startingPose = selectedDCB.startingPose;
				newPrefabConverter.skeletonModifiers = selectedDCB.skeletonModifiers;
				//Getting Rid Of Hash List
				//newPrefabConverter.hashList = selectedConverter.hashList;
				newPrefabConverter.overallModifiersEnabled = selectedDCB.overallModifiersEnabled;
				newPrefabConverter.overallScale = selectedDCB.overallScale;
				//newPrefabConverter.heightModifiers = selectedConverter.heightModifiers;
				newPrefabConverter.radiusAdjust = selectedDCB.radiusAdjust;
				newPrefabConverter.massModifiers = selectedDCB.massModifiers;
			}
#if UNITY_2018_3_OR_NEWER
			var newPrefab = PrefabUtility.SaveAsPrefabAsset(thisNewPrefabGO, path);
#else
			var newPrefab = PrefabUtility.CreatePrefab(path, thisNewPrefabGO);//couldn't create asset try instantiating first
#endif
			if (newPrefab != null)
			{
				EditorUtility.SetDirty(newPrefab);
				AssetDatabase.SaveAssets();
				Debug.Log("Saved your changes to a new converter prefab at " + path);
				Destroy(thisNewPrefabGO);
			}
		}
#endif
#pragma warning restore 618 //restore obsolete warning
		#endregion

		#region Asset Creation
		public UMABonePose CreatePoseAsset(string assetFolder = "", string assetName = "")
		{
			if (assetFolder == "")
			{
				assetFolder = AssetDatabase.GetAssetPath(selectedConverter as UnityEngine.Object);
				assetFolder = assetFolder.Substring(0, assetFolder.LastIndexOf('/'));
			}
			if (assetName == "")
			{
				assetName = selectedConverter.name + "StartingPose";
				var uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetFolder + "/" + assetName + ".asset");
				assetName = uniquePath.Replace(assetFolder + "/", "").Replace(".asset", "");
			}

			if (!System.IO.Directory.Exists(assetFolder))
			{
				System.IO.Directory.CreateDirectory(assetFolder);
			}

			UMABonePose asset = ScriptableObject.CreateInstance<UMABonePose>();
			AssetDatabase.CreateAsset(asset, assetFolder + "/" + assetName + ".asset");
			AssetDatabase.SaveAssets();
			return asset;
		}

		public DynamicUMADnaAsset CreateDNAAsset(string assetFolder = "", string assetName = "")
		{
			if (assetFolder == "")
			{
				assetFolder = AssetDatabase.GetAssetPath(selectedConverter as UnityEngine.Object);
				assetFolder = assetFolder.Substring(0, assetFolder.LastIndexOf('/'));
			}
			if (assetName == "")
			{
				assetName = selectedConverter.name + "DNAAsset";
				var uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetFolder + "/" + assetName + ".asset");
				assetName = uniquePath.Replace(assetFolder + "/", "").Replace(".asset", "");
			}

			if (!System.IO.Directory.Exists(assetFolder))
			{
				System.IO.Directory.CreateDirectory(assetFolder);
			}

			DynamicUMADnaAsset asset = ScriptableObject.CreateInstance<DynamicUMADnaAsset>();
			AssetDatabase.CreateAsset(asset, assetFolder + "/" + assetName + ".asset");
			AssetDatabase.SaveAssets();
			return asset;
		}
		#endregion

		#region UMA Update Methods
		/// <summary>
		/// Updates the current Target UMA so any changes are shown.
		/// </summary>
		public void UpdateUMA()
		{
			if (activeUMA)
			{
				StopCoroutine("UpdateUMACoroutine");
				StartCoroutine("UpdateUMACoroutine");
			}
		}
		IEnumerator UpdateUMACoroutine()//Trying to stop things slowing down after lots of modifications- helps a little bit
		{
			yield return null;//wait for a frame
			if (activeUMA)
			{
				//dna can change textures now too
				activeUMA.umaData.Dirty(true, true, false);
			}
		}
		#endregion
#endif
	}
}
