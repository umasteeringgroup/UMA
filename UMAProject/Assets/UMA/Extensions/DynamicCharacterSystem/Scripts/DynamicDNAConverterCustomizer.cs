using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using UMA.PoseTools;

namespace UMA
{
    public class DynamicDNAConverterCustomizer : MonoBehaviour
    {
        public GameObject dynamicDnaConverterPrefab;//used for saving dnaConverter as new
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
        [SerializeField]
        public List<DynamicDNAConverterBehaviour> availableConverters = new List<DynamicDNAConverterBehaviour>();
        [SerializeField]
        public DynamicDNAConverterBehaviour selectedConverter;

        GameObject converterBackupsFolder = null;

        public DynamicDNAConverterBehaviour converterToImport;

        Dictionary<string, DynamicDNAConverterBehaviour> converterBackups = new Dictionary<string, DynamicDNAConverterBehaviour>();
        Dictionary<string, string[]> dnaAssetNamesBackups = new Dictionary<string, string[]>();
        Dictionary<string, UMABonePose.PoseBone[]> poseBonesBackups = new Dictionary<string, UMABonePose.PoseBone[]>();

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
                SetAvailableConverters(activeUMA.umaData);
            }
            if (activeUMA != null)
                if(activeUMA.umaData != null)
                    if (activeUMA.umaData.umaRecipe.raceData.raceName != activeUMARace)
                    {
                        activeUMARace = activeUMA.umaData.umaRecipe.raceData.raceName;
                        SetAvailableConverters(activeUMA.umaData);
                    }
            //TODO make the guide /target semi transparent...
            /*if(guideUMA != null)
            {
                if(guideUMA.umaData != null && guideAlphaSet == false)
                {
                    foreach(SlotData slot in guideUMA.umaData.umaRecipe.GetAllSlots())
                    {
                        foreach(OverlayData overlay in slot.GetOverlayList())
                        {
                            overlay.asset.alphaMask = guideAlphaTex;
                        }
                    }
                    guideAlphaSet = true;
                    guideUMA.umaData.Dirty(false, true, false);
                }
            }*/
        }

        public void SetAvatar(GameObject newAvatarObject)
        {
            if(guideUMA != null)
            if (newAvatarObject == guideUMA.gameObject)
            {
                //reset guide transparency one we have sussed out how to do this
                guideUMA = null;
            }
            if (newAvatarObject != targetUMA.gameObject)
            {
                if (newAvatarObject.GetComponent<UMAAvatarBase>() != null)
                {
                    targetUMA = newAvatarObject.GetComponent<UMAAvatarBase>();
                    activeUMA = newAvatarObject.GetComponent<UMAAvatarBase>();
                    SetAvailableConverters(activeUMA.umaData);
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
            foreach (DnaConverterBehaviour converter in umaData.umaRecipe.raceData.dnaConverterList)
            {
                if(converter.GetType() == typeof(DynamicDNAConverterBehaviour))
                {
                    availableConverters.Add(converter as DynamicDNAConverterBehaviour);
                }
            }
        }

        public void SetTPoseAni()
        {
            if (TposeAnimatorController == null)
                return;
            if (guideUMA != null)
            {
                if (guideUMA.gameObject.GetComponent<Animator>())
                {
                    guideUMA.gameObject.GetComponent<Animator>().runtimeAnimatorController = TposeAnimatorController;
                }
            }
            if(activeUMA != null)
            {
                if (activeUMA.gameObject.GetComponent<Animator>())
                {
                    activeUMA.gameObject.GetComponent<Animator>().runtimeAnimatorController = TposeAnimatorController;
                }
            }
            UpdateUMA();
        }


        public void SetAPoseAni()
        {
            if (AposeAnimatorController == null)
                return;
            if (guideUMA != null)
            {
                if (guideUMA.gameObject.GetComponent<Animator>())
                {
                    guideUMA.gameObject.GetComponent<Animator>().runtimeAnimatorController = AposeAnimatorController;
                }
            }
            if (activeUMA != null)
            {
                if (activeUMA.gameObject.GetComponent<Animator>())
                {
                    activeUMA.gameObject.GetComponent<Animator>().runtimeAnimatorController = AposeAnimatorController;
                }
            }
            UpdateUMA();
        }


        public void SetMovementAni()
        {
            if (MovementAnimatorController == null)
                return;
            if (guideUMA != null)
            {
                if (guideUMA.gameObject.GetComponent<Animator>())
                {
                    guideUMA.gameObject.GetComponent<Animator>().runtimeAnimatorController = MovementAnimatorController;
                }
            }
            if (activeUMA != null)
            {
                if (activeUMA.gameObject.GetComponent<Animator>())
                {
                    activeUMA.gameObject.GetComponent<Animator>().runtimeAnimatorController = MovementAnimatorController;
                }
            }
            UpdateUMA();
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
                    if (dna.GetType().ToString().IndexOf("DynamicUMADna") > -1)
                    {
                        ((DynamicUMADna)dna).ImportUMADnaValues(gdna);
                    }
                }
            }
            UpdateUMA();
        }

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
            selectedConverter.hashList = converterToImport.hashList;
            selectedConverter.overallModifiersEnabled = converterToImport.overallModifiersEnabled;
            selectedConverter.heightModifiers = converterToImport.heightModifiers;
            selectedConverter.radiusModifier = converterToImport.radiusModifier;
            selectedConverter.massModifiers = converterToImport.massModifiers;
            Debug.Log("Imported " + converterToImport.name + " settings into " + selectedConverter.name);
            return true;
        }

        /// <summary>
        /// Calculates the required poses necessary for an UMABonePose asset to render the Avatar in its current post DNA state, 
        /// adds these to the selected converters 'Starting Pose' asset- creating one if necessary and resets current Dna values to 0.
        /// </summary>
        public bool CreateBonePosesFromCurrentDna(string createdAssetName = "")
        {
            if ( activeUMA == null || selectedConverter == null)
                return false;
            //make a list of poses based on the current dna settings and reset all the dna values to zero.
            //overwrite the existing Assets poses with these if there is an asset or create a new asset with these settings.
            Vector3[] dnaAffectedPositions = new Vector3[activeUMA.umaData.skeleton.BoneNames.Length];
            Vector3[] dnaAffectedScales = new Vector3[activeUMA.umaData.skeleton.BoneNames.Length];
            Quaternion[] dnaAffectedRotations = new Quaternion[activeUMA.umaData.skeleton.BoneNames.Length];
            Dictionary<int, Vector3> positionsToAdd = new Dictionary<int, Vector3>();
            Dictionary<int, Vector3> scalesToAdd = new Dictionary<int, Vector3>();
            Dictionary<int, Quaternion> rotationsToAdd = new Dictionary<int, Quaternion>();
            //We need to set the 'overallScale' modifier to 1f before we do anything since this messes everything up
            var currentOverallScale = 1f;
            foreach(DynamicDNAConverterBehaviour converter in availableConverters)
            {
                if (converter.overallModifiersEnabled)
                {
                    currentOverallScale = converter.overallScale;
                    converter.overallScale = 1f;
                    converter.ApplyDynamicDnaAction(activeUMA.umaData, activeUMA.umaData.skeleton);
                }
            }
            var skeleton = activeUMA.umaData.skeleton;
            var index = 0;
            foreach (int boneHash in skeleton.BoneHashes)
            {
                dnaAffectedPositions[index] = skeleton.GetPosition(boneHash);
                dnaAffectedScales[index] = skeleton.GetScale(boneHash);
                dnaAffectedRotations[index] = skeleton.GetRotation(boneHash);
                index++;
            }
            //umaData.skeleton.ResetAll();//this does not give us the right results, the differences are still significant enough that many bones dont say they are equal.
            //this method does work but I am not sure whether this means I need to make the converter calculate values more accurately or if this is a float precision issue or a conversion from binary issue or somethomg else
            foreach(DynamicDNAConverterBehaviour converter in availableConverters)//this wont remove any dna from non dynamicDNAConverters tho...
            {
                converter.RemoveDNAChangesFromSkeleton(activeUMA.umaData);
            }
            index = 0;
            foreach (int boneHash in skeleton.BoneHashes)
            {
                //we always need position and lower back I think- still not right though really...
                if(skeleton.BoneNames[index] == "Position" || skeleton.BoneNames[index] == "LowerBack")
                {
                    scalesToAdd.Add(boneHash, dnaAffectedScales[index]);
                    positionsToAdd.Add(boneHash, dnaAffectedPositions[index]);
                    rotationsToAdd.Add(boneHash, dnaAffectedRotations[index]);
                }
                //position equality is not sensitive enough and scale equality is too sensitive
                //cant get this to work 100% not sure if its an issue with the comparing or things not getting reset right
                else if ((skeleton.GetPosition(boneHash)) != (dnaAffectedPositions[index]) || (Vector3.SqrMagnitude(skeleton.GetPosition(boneHash) - dnaAffectedPositions[index]) > 0.00000001) || skeleton.GetRotation(boneHash) != dnaAffectedRotations[index] || skeleton.GetScale(boneHash) != dnaAffectedScales[index])
                {
                    if (skeleton.GetScale(boneHash) != dnaAffectedScales[index])
                    {
                        if (Vector3.SqrMagnitude(skeleton.GetScale(boneHash) - dnaAffectedScales[index]) > 0.0001)//we need to be a little less sensitive with scale
                        {
                            scalesToAdd.Add(boneHash, dnaAffectedScales[index]);
                            positionsToAdd.Add(boneHash, dnaAffectedPositions[index]);
                            rotationsToAdd.Add(boneHash, dnaAffectedRotations[index]);
                        }
                    }
                    else
                    {
                        scalesToAdd.Add(boneHash, dnaAffectedScales[index]);
                        positionsToAdd.Add(boneHash, dnaAffectedPositions[index]);
                        rotationsToAdd.Add(boneHash, dnaAffectedRotations[index]);
                    }
                }
                index++;
            }
            if (positionsToAdd.Count > 0)
            {
                UMA.PoseTools.UMABonePose bonePose = null;
                if (selectedConverter.startingPose == null)
                {
                    bonePose = CreatePoseAsset("", createdAssetName);//UMA.PoseTools.UMABonePoseBuildWindow is not available because its Editor Only...
                }
                else
                {
                    bonePose = selectedConverter.startingPose;
                }
                skeleton.ResetAll();//seems to mean the settings when the pose gets added are more accurate...
                if (bonePose.poses.Length > 0)//we want to remove any bones we are going to add again
                {
                    List<UMA.PoseTools.UMABonePose.PoseBone> unaffectedBones = new List<UMA.PoseTools.UMABonePose.PoseBone>();
                    for (int i = 0; i < bonePose.poses.Length; i++)
                    {
                        if (!positionsToAdd.ContainsKey(bonePose.poses[i].hash))
                        {
                            unaffectedBones.Add(bonePose.poses[i]);
                        }
                    }
                    bonePose.poses = unaffectedBones.ToArray();
                }
                List<Transform> trashTransforms = new List<Transform>();
                foreach (KeyValuePair<int, Vector3> kp in positionsToAdd)
                {
                    Transform thisBoneTransform = new GameObject().transform;
                    thisBoneTransform.localPosition = skeleton.GetPosition(kp.Key);
                    thisBoneTransform.localScale = skeleton.GetScale(kp.Key);
                    thisBoneTransform.localRotation = skeleton.GetRotation(kp.Key);
                    thisBoneTransform.name = skeleton.GetBoneGameObject(kp.Key).name;
                    trashTransforms.Add(thisBoneTransform);
                    bonePose.AddBone(skeleton.GetBoneGameObject(kp.Key).transform, positionsToAdd[kp.Key], rotationsToAdd[kp.Key], scalesToAdd[kp.Key]);
                }
                foreach (Transform trash in trashTransforms)
                {
                    Destroy(trash.gameObject);
                }
                EditorUtility.SetDirty(bonePose);
                AssetDatabase.SaveAssets();
                //then set this asset as the converters pose asset
                selectedConverter.startingPose = bonePose;
                //and reset all the dna Values for this Avatar to zero (aka 0.5f)
                UMADnaBase[] DNA = activeUMA.umaData.GetAllDna();
                foreach (UMADnaBase d in DNA)
                {
                    for (int i = 0; i < d.Values.Length; i++)
                    {
                        d.SetValue(i, 0.5f);
                    }
                }
                //reset the overallscale to the converter that had it
                foreach (DynamicDNAConverterBehaviour converter in availableConverters)
                {
                    if (converter.overallModifiersEnabled)
                    {
                        converter.overallScale = currentOverallScale;
                    }
                }
                activeUMA.umaData.Dirty(true, false, false);
                return true;
            }
            else
            {
                Debug.LogWarning("No differences between the unmodified model an the DNA modified model were found to convert. No asset created.");
            }
            return false;
        }
        #endregion

        #region Save and Backup Methods
        /// <summary>
        /// Makes a backup of the currently selected converter whose values are restored to the current converter when the Application stops playing (unless you Save the changes)
        /// </summary>
        /// <param name="converterToBU"></param>
        public void BackupConverter(DynamicDNAConverterBehaviour converterToBU = null)
        {
            if(converterToBU == null)
            {
                converterToBU = selectedConverter;
            }
            if(converterBackupsFolder == null)
            {
                converterBackupsFolder = new GameObject();
                converterBackupsFolder.name = "CONVERTER BACKUPS DO NOT DELETE";
            }
            if(converterToBU != null)
            {
                if (!converterBackups.ContainsKey(converterToBU.name))
                {
                    var thisConverterBackup = Instantiate<DynamicDNAConverterBehaviour>(converterToBU);
                    thisConverterBackup.transform.parent = converterBackupsFolder.transform;
                    converterBackups[converterToBU.name] = thisConverterBackup;
                    if(converterToBU.dnaAsset != null)
                    {
                        dnaAssetNamesBackups[converterToBU.dnaAsset.name] = (string[])converterToBU.dnaAsset.Names.Clone();
                    }
                    if (converterToBU.startingPose != null)
                    {
                        poseBonesBackups[converterToBU.startingPose.name] = DeepPoseBoneClone(converterToBU.startingPose.poses);
                    }
                }  
            }
        }

        private UMABonePose.PoseBone[] DeepPoseBoneClone(UMABonePose.PoseBone[] posesToCopy)
        {
            var poseBonesCopy = new UMABonePose.PoseBone[posesToCopy.Length];
            for(int i = 0; i < posesToCopy.Length; i++)
            {
                poseBonesCopy[i] = new UMABonePose.PoseBone();
                poseBonesCopy[i].bone = posesToCopy[i].bone;
                poseBonesCopy[i].hash = posesToCopy[i].hash;
                poseBonesCopy[i].position = posesToCopy[i].position;
                poseBonesCopy[i].rotation = posesToCopy[i].rotation;
                poseBonesCopy[i].scale = posesToCopy[i].scale;
            }
            return poseBonesCopy;
        }
        
        /// <summary>
        /// Restores the converters values back to the original or saved values. Also called when the application stops playing so that any changes made while the game is running are not saved (unless the user calls the SaveChanges method previously).
        /// </summary>
        /// <param name="converterName"></param>
        public void RestoreBackupVersion(string converterName = "")
        {
            if(availableConverters.Count > 0)
            {
                for (int i = 0; i < availableConverters.Count; i++)
                {
                    DynamicDNAConverterBehaviour buConverter;
                    if (converterBackups.TryGetValue(availableConverters[i].name, out buConverter))
                    {
                        if(converterName == "" || converterName == availableConverters[i].name)
                        {
                            availableConverters[i].dnaAsset = buConverter.dnaAsset;
                            if(availableConverters[i].dnaAsset != null)
                            {
                                string[] buNames;
                                if(dnaAssetNamesBackups.TryGetValue(availableConverters[i].dnaAsset.name, out buNames))
                                {
                                    availableConverters[i].dnaAsset.Names = buNames;
                                }
                            }
                            availableConverters[i].startingPose = buConverter.startingPose;
                            if(availableConverters[i].startingPose != null)
                            {
                                UMABonePose.PoseBone[] buPoses;
                                if (poseBonesBackups.TryGetValue(availableConverters[i].startingPose.name, out buPoses))
                                {
                                    availableConverters[i].startingPose.poses = buPoses;
                                }
                            }
                            availableConverters[i].skeletonModifiers = buConverter.skeletonModifiers;
                            availableConverters[i].hashList = buConverter.hashList;
                            availableConverters[i].overallModifiersEnabled = buConverter.overallModifiersEnabled;
                            availableConverters[i].overallScale = buConverter.overallScale;
                            availableConverters[i].heightModifiers = buConverter.heightModifiers;
                            availableConverters[i].radiusModifier = buConverter.radiusModifier;
                            availableConverters[i].massModifiers = buConverter.massModifiers;
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
                    foreach(KeyValuePair<string, DynamicDNAConverterBehaviour> kp in converterBackups)
                    {
                        if (kp.Value.dnaAsset != null && dnaAssetNamesBackups.ContainsKey(kp.Value.dnaAsset.name))
                        {
                            EditorUtility.SetDirty(kp.Value.dnaAsset);
                            dnaAssetNamesBackups.Remove(kp.Value.dnaAsset.name);
                        }
                        if (kp.Value.startingPose != null && poseBonesBackups.ContainsKey(kp.Value.startingPose.name))
                        {
                            EditorUtility.SetDirty(kp.Value.dnaAsset);
                            poseBonesBackups.Remove(kp.Value.startingPose.name);
                        }
                    }
                    if (availableConverters.Count > 0)
                    {
                        for (int i = 0; i < availableConverters.Count; i++)
                        {
                            EditorUtility.SetDirty(availableConverters[i]);
                        }
                    }
                    AssetDatabase.SaveAssets();
                    foreach (KeyValuePair<string, DynamicDNAConverterBehaviour> kp in converterBackups)
                    {
                        Destroy(kp.Value);
                    }
                    converterBackups.Clear();
                    if(availableConverters.Count > 0)
                    {
                        for(int i = 0; i < availableConverters.Count; i++)
                        {
                            BackupConverter(availableConverters[i]);
                        }
                    }
                }
                else
                {
                    if (selectedConverter != null)
                    {
                        if (converterBackups.ContainsKey(selectedConverter.name))
                        {
                            if(converterBackups[selectedConverter.name].dnaAsset != null)
                            {
                                EditorUtility.SetDirty(converterBackups[selectedConverter.name].dnaAsset);
                                dnaAssetNamesBackups.Remove(converterBackups[selectedConverter.name].dnaAsset.name);
                            }
                            if (converterBackups[selectedConverter.name].startingPose != null)
                            {
                                EditorUtility.SetDirty(converterBackups[selectedConverter.name].startingPose);
                                poseBonesBackups.Remove(converterBackups[selectedConverter.name].startingPose.name);
                            }
                            EditorUtility.SetDirty(selectedConverter);
                            AssetDatabase.SaveAssets();
                            Destroy(converterBackups[selectedConverter.name]);
                            converterBackups.Remove(selectedConverter.name);
                            BackupConverter();
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
            if(dynamicDnaConverterPrefab == null)
            {
                Debug.LogWarning("There was no prefab set up in the DynamicDnaConverterCustomizer. This must be set in order to save a new prefab.");
                return;
            }
            if(selectedConverter == null)
            {
                Debug.LogWarning("No converter was selected to save!");
                return;
            }
            var fullPath = EditorUtility.SaveFilePanel("Save New DynamicDnaConverterBehaviour", Application.dataPath, "", "prefab");
            var path = fullPath.Replace(Application.dataPath,"Assets");
            var filename = System.IO.Path.GetFileNameWithoutExtension(path);
            var thisNewPrefabGO = Instantiate(dynamicDnaConverterPrefab);
            thisNewPrefabGO.name = filename;
            var newPrefabConverter = thisNewPrefabGO.GetComponent<DynamicDNAConverterBehaviour>();
            if (newPrefabConverter != null)
            {
                newPrefabConverter.dnaAsset = selectedConverter.dnaAsset;
                newPrefabConverter.startingPose = selectedConverter.startingPose;
                newPrefabConverter.skeletonModifiers = selectedConverter.skeletonModifiers;
                newPrefabConverter.hashList = selectedConverter.hashList;
                newPrefabConverter.overallModifiersEnabled = selectedConverter.overallModifiersEnabled;
                newPrefabConverter.overallScale = selectedConverter.overallScale;
                newPrefabConverter.heightModifiers = selectedConverter.heightModifiers;
                newPrefabConverter.radiusModifier = selectedConverter.radiusModifier;
                newPrefabConverter.massModifiers = selectedConverter.massModifiers;
            }
            var newPrefab = PrefabUtility.CreatePrefab(path, thisNewPrefabGO);//couldn't create asset try instantiating first
            if(newPrefab != null)
            {
                EditorUtility.SetDirty(newPrefab);
                AssetDatabase.SaveAssets();
                Debug.Log("Saved your changes to a new converter prefab at " + path);
                Destroy(thisNewPrefabGO);
            }
        }
#endif
        #endregion

        #region Asset Creation
        public UMABonePose CreatePoseAsset(string assetFolder = "", string assetName = "")
        {
            if(assetFolder == "")
            {
                assetFolder = AssetDatabase.GetAssetPath(selectedConverter);
                assetFolder = assetFolder.Substring(0, assetFolder.LastIndexOf('/'));
            }
            if(assetName == "")
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
                assetFolder = AssetDatabase.GetAssetPath(selectedConverter);
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
                activeUMA.umaData.Dirty(true, false, false);
            }
        }
        #endregion
    }
}