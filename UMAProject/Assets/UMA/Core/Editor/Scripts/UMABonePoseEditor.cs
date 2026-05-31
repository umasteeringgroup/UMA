using UnityEngine;
using UnityEditor.IMGUI.Controls;
using UMA.Editors;
using System.CodeDom;
using System.Runtime.Serialization.Json;
#if UNITY_6000_2_OR_NEWER
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#endif


#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;

namespace UMA.PoseTools
{
    public class BoneWeightSaver
    {
        public BonePoseDNAConverterPlugin.BonePoseDNAConverter converter;
        public float weight;
    }

    public class BonePoseSaver
    {
        public float MasterWeight;
        public BonePoseDNAConverterPlugin BonePoseDNAConverterPlugin;
        public List<BoneWeightSaver> BoneWeights = new List<BoneWeightSaver>();
    }


    public class BoneTreeView : TreeView
    {
        public TreeViewItem RootNode;
        public int NodeCount;
        public float masterWeight = -1.0f;
        public float weight = -1.0f;

        public BoneTreeView(TreeViewState treeViewState)
            : base(treeViewState)
        {

        }

        /*
		public TreeViewItem FindNode(TreeViewItem root, string Name)
		{
			if (root.children == null)
				return null;

			foreach(TreeViewItem ti in root.children)
			{
				if (ti.displayName == Name)
					return ti;
			}
			return null;
		} */

        public List<string> GetSelectedBonesWithMirrors()
        {
            List<string> selectedBones = GetSelectedBones();
            List<string> mirroredBones = new List<string>();

            for (int i =0; i < selectedBones.Count; i++)
            {
                string bone = selectedBones[i];
                // Check if the bone has a mirror counterpart
                string mirroredBone = GetMirroredBoneName(bone);
                if (!string.IsNullOrEmpty(mirroredBone) && BoneExistsInTree(mirroredBone))
                {

                    mirroredBones.Add(mirroredBone);
                }
            }
            selectedBones.AddRange(mirroredBones);
            return selectedBones;
        }

        private bool BoneExistsInTree(string boneName)
        {
            if (RootNode == null || string.IsNullOrEmpty(boneName)) return false;
            return FindNodeByNameRecursive(RootNode, boneName) != null;
        }

        private TreeViewItem FindNodeByNameRecursive(TreeViewItem node, string boneName)
        {
            if (node == null) return null;
            if (node.displayName == boneName) return node;
            if (node.children != null)
            {
                for (int i =0; i < node.children.Count; i++)
                {
                    var child = node.children[i];
                    var found = FindNodeByNameRecursive(child, boneName);
                    if (found != null) return found;
                }
            }
            return null;
        }

        public string GetMirroredBoneName(string boneName)
        {
            // Simple example of mirroring logic based on common naming conventions
            if (boneName.EndsWith("_L"))
            {
                return boneName.Substring(0, boneName.Length -2) + "_R";
            }
            else if (boneName.EndsWith("_R"))
            {
                return boneName.Substring(0, boneName.Length -2) + "_L";
            }
            else if (boneName.StartsWith("Left"))
            {
                return "Right" + boneName.Substring(4);
            }
            else if (boneName.StartsWith("Right"))
            {
                return "Left" + boneName.Substring(5);
            }
            // Add more naming conventions as needed
            return null; // No mirror found
        }

        public List<string> GetSelectedBones()
        {
            List<string> boneNames = new List<string>();
            IList<int> boneIDs = GetSelection();
            if (boneIDs == null)
            {
                return boneNames;
            }

            if (boneIDs.Count ==0)
            {
                return boneNames;
            }

            foreach (int i in boneIDs)
            {
                TreeViewItem tvi = FindItem(i, RootNode);
                if (tvi != null)
                {
                    boneNames.Add(tvi.displayName);
                }
            }
            return boneNames;
        }

        public void Initialize(string RootName)
        {
            RootNode = new TreeViewItem(0, -1, RootName);
            NodeCount =0;
        }

        /*
		public void AddBone(string BoneName,int level)
		{
			string[] Keywords = BoneName.SplitCamelCase();
			if (Keywords.Length ==1)
			{
				TreeViewItem tv = new TreeViewItem(NodeCount++,1 , BoneName);
				RootNode.AddChild(tv);
				NodeCount++;
				return;
			}

			TreeViewItem FirstLevel = FindNode(RootNode,Keywords[0]);
			if (FirstLevel == null)
			{
				FirstLevel = new TreeViewItem(NodeCount++,1, Keywords[0]);
				RootNode.AddChild(FirstLevel);
			}

			TreeViewItem childNode = new TreeViewItem(NodeCount++,2, BoneName);
			FirstLevel.AddChild(childNode);
		}
		*/

        protected override TreeViewItem BuildRoot()
        {
            if (RootNode == null)
            {
                RootNode = new TreeViewItem(0, -1, "Root");
            }
            SetupDepthsFromParentsAndChildren(RootNode);
            return RootNode;
        }
    }

    [CustomEditor(typeof(UMABonePose), true)]
    public class UMABonePoseEditor : Editor
    {
        private static UMABonePoseEditor _livePopupEditor = null;
        private static UMABonePose _protectedBonePoseSelection = null;
        private static bool _selectionProtectionRegistered = false;
        private static bool _restoringProtectedBonePoseSelection = false;
        public static int MirrorAxis =0;
        public static string[] MirrorAxises = { "X Axis", "Y Axis (Legacy UMA)", "Z Axis" };
        public static int displayMode =0;
        public static string[] strings = { "Pose Bones", "Filtered", "All", "None" };
        public enum DisplayMode { PoseBones, Filtered, All, None };
        private enum IKMovementPlane { Free, XZ, YZ, XY }
        private enum IKMovementPlaneSpace { Global, Local }
        // Global mirroring disable switch for UI/scene edits
        public static bool disableMirroring = false;
        public static bool useTPosePreview = true;
        public static UMAData saveUMAData;
        public UMAData sourceUMA;
        public UMA.CharacterSystem.DynamicCharacterAvatar poseTarget;
        public SkinnedMeshRenderer targetSMR;
        TreeViewState treeState;
        BoneTreeView boneTreeView;

        UMABonePose targetPose = null;
        public UMABonePoseEditorContext context = null;

        const int BAD_INDEX = -1;

        private static bool IsEditorBusy => EditorApplication.isCompiling || EditorApplication.isUpdating;
        private static bool IsCompilingOrUpdating => EditorApplication.isCompiling || EditorApplication.isUpdating;

        public bool autoUpdatePreview = true;

        public bool haveValidContext
        {
            get { return ((context != null) && (context.activeUMA != null)); }
        }
        public bool haveEditTarget
        {
            get { return (editBoneIndex != BAD_INDEX); }
        }

        private float previewWeight =1.0f;

        public bool dynamicDNAConverterMode = false;

        const float addRemovePadding =20f;
        const float buttonVerticalOffset =4f;

        private int drawBoneIndex = BAD_INDEX;
        private int editBoneIndex = BAD_INDEX;
        private int activeBoneIndex = BAD_INDEX;
        private int mirrorBoneIndex = BAD_INDEX;
        private bool mirrorActive = true;
        private bool useIKEditor = false;
        private bool protectBonePoseSceneSelection = true;
        private float ikHandleBaseSize =0.2f;
        private bool ikUseBoundaryBone = false;
        private int ikBoundaryBoneIndex = BAD_INDEX;
        private string ikBoundaryBoneName = "Position";
        private string ikBoundaryBoneFilter = "";
        private Transform ikActiveJoint = null;
        private string ikStatusMessage = "";
        private IKMovementPlane ikMovementPlane = IKMovementPlane.Free;
        private IKMovementPlaneSpace ikMovementPlaneSpace = IKMovementPlaneSpace.Global;
        private GUIStyle ikPlaneLabelStyle;

        const int ikMaxIterations =12;
        const float ikSolveTolerance =0.001f;
        const float ikHandleMinViewScale =0.025f;
        const float ikHandleMaxViewScale =0.18f;
        private static readonly GUIContent[] ikMovementPlaneOptions =
        {
            new GUIContent("Free"),
            new GUIContent("X/Z"),
            new GUIContent("Y/Z"),
            new GUIContent("X/Y")
        };
        private static readonly GUIContent[] ikMovementPlaneSpaceOptions =
        {
            new GUIContent("Global"),
            new GUIContent("Local")
        };
        private static readonly string[] ikCommonBoundaryBoneNames =
        {
            "Position",
            "Global",
            "Hips",
            "Pelvis",
            "Spine",
            "Spine1",
            "Spine2",
            "Chest",
            "UpperChest",
            "Neck",
            "Head",
            "LeftShoulder",
            "RightShoulder",
            "LeftUpperArm",
            "RightUpperArm",
            "LeftForeArm",
            "RightForeArm",
            "LeftHand",
            "RightHand",
            "LeftUpLeg",
            "RightUpLeg",
            "LeftLeg",
            "RightLeg",
            "LeftFoot",
            "RightFoot",
            "LeftToeBase",
            "RightToeBase"
        };

        private bool doBoneAdd = false;
        private bool doBoneRemove = false;
        private int removeBoneIndex = BAD_INDEX;
        private int addBoneIndex = BAD_INDEX;
        const int minBoneNameLength =4;
        private string addBoneName = "";
        private List<string> addBoneNames = new List<string>();
        private Vector2 scrollPosition;
        private string filter = "";
        private string lastFilter = "";
        private bool filtered = false;
        private string BoneListFilter = "";

        List<BonePoseSaver> BonePoseSavers = new List<BonePoseSaver>();

        private static Texture warningIcon;

        private static GUIContent positionGUIContent = new GUIContent(
            "Position",
            "The change in this bone's local position when pose is applied.");
        private static GUIContent rotationGUIContent = new GUIContent(
            "Rotation",
            "The change in this bone's local rotation when pose is applied.");
        private static GUIContent scaleGUIContent = new GUIContent(
            "Scale",
            "The change in this bone's local scale when pose is applied.");
        private static GUIContent scaleWarningGUIContent = new GUIContent(
            "WARNING: Non-uniform scale.",
            "Non-uniform scaling can cause errors on bones that are animated. Use only with adjustment bones.");
        private static GUIContent removeBoneGUIContent = new GUIContent(
            "Remove Bone",
            "Remove the selected bone from the pose.");
        private static GUIContent addBoneGUIContent = new GUIContent(
            "Add Bone",
            "Add the selected bone into the pose.");
        private static GUIContent previewGUIContent = new GUIContent(
            "Preview Weight",
            "Amount to apply bone pose to preview model. Inactive while editing.");
        private static GUIContent generatePoseGUIContent = new GUIContent(
            "Generate Pose from Target SMR",
            "Generate bone pose by comparing the source UMA skeleton with the target SkinnedMeshRenderer bones. This creates pose data to transform the source UMA rig to match the target rig for clothing remapping.");
        private static GUIContent useIKEditorGUIContent = new GUIContent(
            "Use IK Editor",
            "Draw all scene joints with round handles and drag non-adjust joints with IK.");
        private static GUIContent ikHandleBaseSizeGUIContent = new GUIContent(
            "IK Handle Base Size",
            "Multiplier applied to the nearest joint distance when sizing IK joint handles.");
        private static GUIContent ikUseBoundaryGUIContent = new GUIContent(
            "Affect down to:",
            "When enabled, IK affects ancestors down from the selected boundary bone. Otherwise IK stops at the nearest natural branch/root.");
        private static GUIContent ikBoundaryBoneGUIContent = new GUIContent(
            "IK Boundary Bone",
            "Optional ancestor bone used as the root of the IK chain.");
        private static GUIContent ikBoundaryQuickPickGUIContent = new GUIContent(
            "Boundary Quick Pick",
            "Quickly choose a common IK boundary bone when it exists in the current skeleton.");
        private static GUIContent ikBoundaryFilterGUIContent = new GUIContent(
            "Boundary Filter",
            "Filter the full IK boundary bone list before choosing from it.");
        private static GUIContent ikMovementPlaneGUIContent = new GUIContent(
            "Movement Plane",
            "Constrain IK joint movement to a two-axis plane.");
        private static GUIContent ikMovementPlaneSpaceGUIContent = new GUIContent(
            "Plane Space",
            "Use world axes or the dragged joint's local axes for the movement plane.");
        private static GUIContent savePoseAnimationGUIContent = new GUIContent(
            "Save as animation",
            "Save the current visible pose to a one-frame Unity animation clip.");
        private static GUIContent resetSkeletonGUIContent = new GUIContent(
            "Reset Skeleton",
            "Reset all pose bone transforms to identity and restore the source skeleton to the base pose.");

        // Track whether any edits were made so we can restore & rebuild on exit
        private bool _poseEdited = false;
        private bool _sourcePreviewModified = false;

        public static UMABonePoseEditor livePopupEditor
        {
            get { return _livePopupEditor; }
        }

        public static void SetLivePopupEditor(UMABonePoseEditor liveUBPEditor)
        {
            if (Application.isPlaying)
            {
                _livePopupEditor = liveUBPEditor;
            }
        }

        private static void EnsureSelectionProtectionRegistered()
        {
            if (_selectionProtectionRegistered)
            {
                return;
            }

            Selection.selectionChanged += RestoreProtectedBonePoseSelection;
            _selectionProtectionRegistered = true;
        }

        private static void RestoreProtectedBonePoseSelection()
        {
            if (_restoringProtectedBonePoseSelection || _protectedBonePoseSelection == null || Selection.activeObject == _protectedBonePoseSelection)
            {
                return;
            }

            UMABonePose poseToRestore = _protectedBonePoseSelection;
            _restoringProtectedBonePoseSelection = true;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (poseToRestore != null && _protectedBonePoseSelection == poseToRestore && Selection.activeObject != poseToRestore)
                    {
                        Selection.activeObject = poseToRestore;
                    }
                }
                finally
                {
                    _restoringProtectedBonePoseSelection = false;
                }
            };
        }

        private void SetBonePoseSelectionProtection(bool enabled)
        {
            EnsureSelectionProtectionRegistered();
            if (enabled && targetPose != null)
            {
                _protectedBonePoseSelection = targetPose;
                return;
            }

            if (_protectedBonePoseSelection == targetPose)
            {
                _protectedBonePoseSelection = null;
            }
        }

        public void OnEnable()
        {
            if (IsEditorBusy || target == null)
            {
                EditorApplication.delayCall += () => { if (this != null) OnEnable(); };
                return;
            }

            useTPosePreview = true;

            if (saveUMAData != null)
            {
                sourceUMA = saveUMAData;
            }

            if (treeState == null)
            {
                treeState = new TreeViewState();
            }

            boneTreeView = new BoneTreeView(treeState);

            targetPose = target as UMABonePose;

            if (!dynamicDNAConverterMode && sourceUMA != null)
            {
                ApplySourcePreviewMode(null, true);
            }

            EditorApplication.update -= this.OnUpdate;
            EditorApplication.update += this.OnUpdate;

#if UNITY_2019_1_OR_NEWER
            SceneView.duringSceneGui -= this.DoSceneGUI;
            SceneView.duringSceneGui += this.DoSceneGUI;
#else
            SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
            SceneView.onSceneGUIDelegate += this.OnSceneGUI;
#endif

            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;

            if (warningIcon == null)
            {
                warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
            }

            SetBonePoseSelectionProtection(ShouldProtectBonePoseSceneSelection());
        }

        private void HandleBeforeAssemblyReload()
        {
            TryRestoreAndRebuildOnExit();
            try { EditorApplication.update -= this.OnUpdate; } catch { }
#if UNITY_2019_1_OR_NEWER
            try { SceneView.duringSceneGui -= this.DoSceneGUI; } catch { }
#else
            try { SceneView.onSceneGUIDelegate -= this.OnSceneGUI; } catch { }
#endif
        }

        public void OnDisable()
        {
            if (!ShouldProtectBonePoseSceneSelection())
            {
                SetBonePoseSelectionProtection(false);
            }
            TryRestoreAndRebuildOnExit();
            try { EditorApplication.update -= this.OnUpdate; } catch { }
#if UNITY_2019_1_OR_NEWER
            try { SceneView.duringSceneGui -= this.DoSceneGUI; } catch { }
#else
            try { SceneView.onSceneGUIDelegate -= this.OnSceneGUI; } catch { }
#endif
            try { AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload; } catch { }
        }

        private void ClearActiveEditState()
        {
            doBoneAdd = false;
            doBoneRemove = false;
            removeBoneIndex = BAD_INDEX;
            editBoneIndex = BAD_INDEX;
            activeBoneIndex = BAD_INDEX;
            mirrorBoneIndex = BAD_INDEX;
            ikActiveJoint = null;
            if (context != null)
            {
                context.activeTransform = null;
                context.activeTransChanged = false;
            }
        }

        private static RaceData ResolvePreviewRaceData(UMAData umaData)
        {
            if (umaData == null)
            {
                return null;
            }

            UMA.CharacterSystem.DynamicCharacterAvatar dynamicCharacterAvatar = umaData as UMA.CharacterSystem.DynamicCharacterAvatar;
            if (dynamicCharacterAvatar != null && dynamicCharacterAvatar.activeRace != null)
            {
                RaceData activeRace = dynamicCharacterAvatar.activeRace.racedata;
                if (activeRace == null)
                {
                    dynamicCharacterAvatar.activeRace.SetRaceData();
                    activeRace = dynamicCharacterAvatar.activeRace.racedata;
                }

                if (activeRace != null)
                {
                    if (umaData.umaRecipe != null && umaData.umaRecipe.raceData != null && umaData.umaRecipe.raceData != activeRace)
                    {
                        Debug.LogWarning($"[UMABonePoseEditor] Source UMA '{umaData.name}' has mismatched activeRace '{activeRace.raceName}' and recipe race '{umaData.umaRecipe.raceData.raceName}'. Using the active race for preview operations.");
                    }
                    return activeRace;
                }
            }

            return umaData.umaRecipe != null ? umaData.umaRecipe.raceData : null;
        }

        private static bool TryGetRaceData(UMAData umaData, out RaceData race)
        {
            race = ResolvePreviewRaceData(umaData);
            return race != null && race.dnaConverterList != null;
        }

        private void SetBonePoseMasterWeight(UMAData umaData, float masterWeight)
        {
            if (!TryGetRaceData(umaData, out RaceData race))
            {
                return;
            }

            foreach (var converterController in race.dnaConverterList)
            {
                var plugins = converterController.GetPlugins(typeof(BonePoseDNAConverterPlugin));
                foreach (var boneplug in plugins)
                {
                    BonePoseDNAConverterPlugin bonePosePlugin = boneplug as BonePoseDNAConverterPlugin;
                    if (bonePosePlugin != null)
                    {
                        Debug.Log($"Setting master weight {masterWeight} on BonePoseDNAConverter Plugin: {bonePosePlugin.name} on race {race.name}");
                        bonePosePlugin.masterWeight.globalWeight = masterWeight;
                    }
                }
            }
        }

        private bool BuildSourceAvatarIfAvailable(UMAData umaData)
        {
            UMA.CharacterSystem.DynamicCharacterAvatar dynamicCharacterAvatar = umaData as UMA.CharacterSystem.DynamicCharacterAvatar;
            if (dynamicCharacterAvatar == null)
            {
                return false;
            }

            dynamicCharacterAvatar.BuildNow();
            return true;
        }

        private void RegeneratePoseTargetPreviewIfNeeded()
        {
            if (!autoUpdatePreview || poseTarget == null || IsEditorBusy)
            {
                return;
            }

            poseTarget.RegenerateNow(true);
        }

        private bool TryGetPoseBoneTransform(string boneName, out Transform boneTransform)
        {
            boneTransform = null;
            if (string.IsNullOrEmpty(boneName))
            {
                return false;
            }

            var skeleton = context != null && context.activeUMA != null ? context.activeUMA.skeleton : null;
            if (skeleton == null && sourceUMA != null)
            {
                skeleton = sourceUMA.skeleton;
            }
            if (skeleton == null)
            {
                return false;
            }

            boneTransform = skeleton.GetBoneTransform(boneName);
            return boneTransform != null;
        }

        private void FocusSceneViewOnBone(Transform boneTransform)
        {
            if (boneTransform == null)
            {
                return;
            }

            Bounds bounds = new Bounds(boneTransform.position, Vector3.one * 0.3f);
            SceneView.lastActiveSceneView.Frame(bounds, false);

/*

            var activeSelection = Selection.activeObject;
            Selection.activeGameObject = boneTransform.gameObject;
            SceneView.FrameLastActiveSceneView();
            Selection.activeObject = activeSelection; */
        }

        private void RestorePreviewOverride()
        {
            if (!_sourcePreviewModified && BonePoseSavers.Count ==0)
            {
                return;
            }

            RestoreWeights();
            _sourcePreviewModified = false;
        }

        private void ApplySourceSkeletonPreview(bool includeTargetPose, bool applyBonePoseConverters = true)
        {
            if (dynamicDNAConverterMode || !haveValidContext)
            {
                return;
            }

            var uma = context.activeUMA;
            var skeleton = uma != null ? uma.skeleton : null;
            if (skeleton == null)
            {
                return;
            }

            skeleton.ResetAll();
            if (context.startingPose != null)
            {
                context.startingPose.ApplyPose(skeleton, context.startingPoseWeight);
            }

            try
            {
                var race = ResolvePreviewRaceData(uma);
                if (race != null && race.dnaConverterList != null)
                {
                    foreach (IDNAConverter id in race.dnaConverterList)
                    {
                        var dcc = id as DynamicDNAConverterController;
                        if (dcc == null)
                        {
                            continue;
                        }

                        var plugins = dcc.GetPlugins(typeof(BonePoseDNAConverterPlugin));
                        if (applyBonePoseConverters)
                        {
                            foreach (DynamicDNAPlugin ddp in plugins)
                            {
                                var bc = ddp as BonePoseDNAConverterPlugin;
                                if (bc == null || bc.poseDNAConverters == null)
                                {
                                    continue;
                                }

                                foreach (var converter in bc.poseDNAConverters)
                                {
                                    if (converter != null && converter.poseToApply != null)
                                    {
                                        converter.poseToApply.ApplyPose(skeleton, converter.startingPoseWeight);
                                    }
                                }
                            }
                        }

                        dcc.overallModifiers?.UpdateCharacter(uma, skeleton, false);
                    }
                }
            }
            catch
            {
            }

            if (!includeTargetPose || targetPose == null)
            {
                return;
            }

            if (haveEditTarget)
            {
                targetPose.ApplyPose(skeleton, 1f);
            }
            else
            {
                targetPose.ApplyPose(skeleton, previewWeight);
            }
        }

        private void ApplySourcePreviewMode(UMAData previousSource, bool recacheWeights)
        {
            if (dynamicDNAConverterMode)
            {
                return;
            }

            if (previousSource != null && previousSource != sourceUMA && _sourcePreviewModified)
            {
                RestorePreviewOverride();
                if (!BuildSourceAvatarIfAvailable(previousSource) && context != null && context.activeUMA == previousSource)
                {
                    ApplySourceSkeletonPreview(false, true);
                }
            }

            if (sourceUMA == null)
            {
                protectBonePoseSceneSelection = false;
                SetBonePoseSelectionProtection(false);
                ClearActiveEditState();
                return;
            }

            protectBonePoseSceneSelection = true;
            SetBonePoseSelectionProtection(true);

            //Debug.Log("Applying T-Pose preview mode to source UMA: " + sourceUMA.name);
            SetBonePoseMasterWeight(sourceUMA,1.0f);
            BuildSourceAvatarIfAvailable(sourceUMA);

            if (recacheWeights || BonePoseSavers.Count ==0)
            {
                SaveWeights();
            }

            ClearBonePoseWeights();
            _sourcePreviewModified = true;
            ClearActiveEditState();
            if (haveValidContext && context.activeUMA == sourceUMA)
            {
                ApplySourceSkeletonPreview(true);
            }
        }

        void OnUpdate()
        {
            if (IsEditorBusy || target == null) return;

            if (targetPose == null)
            {
                targetPose = target as UMABonePose;
                if (targetPose == null) return;
            }

            if (haveValidContext)
            {
                if (activeBoneIndex != editBoneIndex)
                {
                    activeBoneIndex = BAD_INDEX;
                    mirrorBoneIndex = BAD_INDEX;

                    if (targetPose.poses != null && editBoneIndex != BAD_INDEX && editBoneIndex >=0 && editBoneIndex < targetPose.poses.Length)
                    {
                        var skeleton = context?.activeUMA?.skeleton;
                        if (skeleton != null)
                        {
                            int boneHash = targetPose.poses[editBoneIndex].hash;
                            context.activeTransform = skeleton.GetBoneTransform(boneHash);
                            if (context.activeTransform != null)
                            {
                                activeBoneIndex = editBoneIndex;
                            }

                            if (context.mirrorTransform != null)
                            {
                                int mirrorHash = UMASkeleton.StringToHash(context.mirrorTransform.name);
                                for (int i =0; i < targetPose.poses.Length; i++)
                                {
                                    if (targetPose.poses[i].hash == mirrorHash)
                                    {
                                        mirrorBoneIndex = i;
                                        break;
                                    }
                                }
                                // Fallback to name match if hash lookup failed (stale hash safety)
                                if (mirrorBoneIndex == BAD_INDEX)
                                {
                                    string mirrorName = context.mirrorTransform.name;
                                    for (int i =0; i < targetPose.poses.Length; i++)
                                    {
                                        var pose = targetPose.poses[i];
                                        if (pose != null && pose.bone == mirrorName)
                                        {
                                            mirrorBoneIndex = i;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (context != null) context.activeTransform = null;
                    }
                }

                if (!dynamicDNAConverterMode && _sourcePreviewModified)
                {
                    ApplySourceSkeletonPreview(true);
                }
            }

            if (!Application.isPlaying)
            {
                _livePopupEditor = null;
            }
        }

        // Mirror-name derivation matching BoneTreeView.GetMirroredBoneName
        private static string DeriveMirrorName(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return null;
            if (boneName.EndsWith("_L")) return boneName.Substring(0, boneName.Length -2) + "_R";
            if (boneName.EndsWith("_R")) return boneName.Substring(0, boneName.Length -2) + "_L";
            if (boneName.StartsWith("Left")) return "Right" + boneName.Substring(4);
            if (boneName.StartsWith("Right")) return "Left" + boneName.Substring(5);
            return null;
        }

        private static Quaternion NormalizeSafe(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m > 1e-6f)
            {
                float inv = 1.0f / m;
                return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
            }
            return Quaternion.identity;
        }

        // Ensure a mirrored pose exists for the active pose; create it if needed
        private SerializedProperty EnsureMirrorPose(SerializedProperty posesRoot, SerializedProperty activePoseProp, ref int mirrorIndex)
        {
            if (disableMirroring || !mirrorActive || posesRoot == null || activePoseProp == null) return null;
            var boneProp = activePoseProp.FindPropertyRelative("bone");
            string activeName = boneProp != null ? boneProp.stringValue : null;
            string mirrorName = DeriveMirrorName(activeName);
            if (string.IsNullOrEmpty(mirrorName)) return null;

            // If index is valid, return directly
            if (mirrorIndex != BAD_INDEX && mirrorIndex < posesRoot.arraySize)
            {
                var mp = posesRoot.GetArrayElementAtIndex(mirrorIndex);
                var pb = mp.FindPropertyRelative("bone");
                if (pb != null && pb.stringValue == mirrorName)
                {
                    return mp;
                }
            }

            // Try to find by name
            for (int i = 0; i < posesRoot.arraySize; i++)
            {
                var p = posesRoot.GetArrayElementAtIndex(i);
                var pb = p.FindPropertyRelative("bone");
                if (pb != null && pb.stringValue == mirrorName)
                {
                    mirrorIndex = i;
                    return p;
                }
            }

            // Create new
            AddABone(posesRoot, mirrorName);
            mirrorIndex = posesRoot.arraySize - 1;
            return posesRoot.GetArrayElementAtIndex(mirrorIndex);
        }

        private void DrawSkeletonBones()
        {
            if (IsEditorBusy) return;
            if (context == null || context.activeUMA == null) return;

            if (displayMode == (int)DisplayMode.None) return;

            try
            {
                if (displayMode == (int)DisplayMode.PoseBones)
                {
                    DrawPoseBones();
                }
                else
                {
                    var prevHandlesColor = Handles.color;
                    if (context.activeUMA.umaRoot != null)
                    {
                        var Global = context.activeUMA.umaRoot.transform.Find("Global");
                        if (Global != null)
                        {
                            var Position = Global.Find("Position");
                            if (Position != null)
                            {
                                var Hips = Position.Find("Hips");
                                if (Hips != null)
                                {
                                    DrawSkeletonBonesRecursive(Hips, Color.white);
                                }
                            }
                        }
                    }
                    Handles.color = prevHandlesColor;
                }
            }
            catch { }
        }

        private void DrawPoseBones()
        {
            if (sourceUMA == null || sourceUMA.skeleton == null) return;
            if (serializedObject == null || serializedObject.targetObject == null) return;

            var prevHandlesColor = Handles.color;

            try
            {
                SerializedProperty poses = serializedObject.FindProperty("poses");
                if (poses == null) return;

                string filterLower = (BoneListFilter ?? string.Empty).ToLowerInvariant();

                for (int i =0; i < poses.arraySize; i++)
                {
                    SerializedProperty pose = poses.GetArrayElementAtIndex(i);
                    if (pose == null) continue;
                    SerializedProperty bone = pose.FindPropertyRelative("bone");
                    if (bone == null) continue;

                    string boneName = bone.stringValue;

                    if (string.IsNullOrEmpty(filterLower) || boneName.ToLowerInvariant().Contains(filterLower))
                    {
                        var xform = sourceUMA.skeleton.GetBoneTransform(boneName);
                        if (xform != null)
                        {
                            Transform parent = xform.parent;
                            if (parent != null)
                            {
                                Handles.color = xform == context.activeTransform ? Color.green : (xform == context.mirrorTransform ? new Color(0,0.5f,1) : Color.white);
                                Handles.DrawLine(xform.position, parent.position);
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                Handles.color = prevHandlesColor;
            }
        }

        private void DrawSkeletonBonesRecursive(Transform parentBone, Color col)
        {
            if (parentBone == null) return;

            float leaflen =0.01f;

            for (int i =0; i < parentBone.childCount; i++)
            {
                Transform child = parentBone.GetChild(i);
                if (child == null) continue;

                Color NextColor = child == context.activeTransform ? Color.green : (parentBone.GetChild(i) == context.mirrorTransform ? new Color(0,0.5f,1) : Color.white);

                Handles.color = col;
                bool boneVisible = true;
                if (displayMode == (int)DisplayMode.Filtered && BoneListFilter != "")
                {
                    string boneName = parentBone.GetChild(i).name;
                    if (!boneName.ToLower().Contains(BoneListFilter.ToLower()))
                    {
                        boneVisible = false;
                    }
                }
                if (boneVisible)
                {
                    Handles.DrawLine(parentBone.position, parentBone.GetChild(i).position);
                }
                if (parentBone.GetChild(i).childCount >0)
                {
                    DrawSkeletonBonesRecursive(parentBone.GetChild(i), NextColor);
                }
                else
                {
                    if (!child.gameObject.name.Contains("Adjust"))
                    {
                        Vector3 leafpos = child.rotation * (Vector3.one * leaflen);
                        Vector3 ends = child.position + leafpos;
                        // end cap not drawn
                    }
                }
            }
        }

        private struct IKTransformState
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;

            public IKTransformState(Transform transform)
            {
                localPosition = transform.localPosition;
                localRotation = transform.localRotation;
                localScale = transform.localScale;
            }
        }

        private static bool IsAdjustBone(string boneName)
        {
            return !string.IsNullOrEmpty(boneName) && boneName.IndexOf("adjust", System.StringComparison.OrdinalIgnoreCase) >=0;
        }

        private Transform GetIKRootTransform()
        {
            UMAData umaData = context != null ? context.activeUMA : null;
            if (umaData == null)
            {
                return null;
            }

            if (umaData.skeleton != null)
            {
                Transform root = umaData.skeleton.GetRootTransform();
                if (root != null)
                {
                    return root;
                }

                Transform global = umaData.skeleton.GetGlobalTransform();
                if (global != null)
                {
                    return global;
                }
            }

            if (umaData.umaRoot != null)
            {
                Transform global = umaData.umaRoot.transform.Find("Global");
                return global != null ? global : umaData.umaRoot.transform;
            }

            return null;
        }

        private List<Transform> CollectIKTransforms()
        {
            List<Transform> transforms = new List<Transform>();
            Transform root = GetIKRootTransform();
            CollectIKTransformsRecursive(root, transforms);
            return transforms;
        }

        private void CollectIKTransformsRecursive(Transform transform, List<Transform> transforms)
        {
            if (transform == null || transforms == null)
            {
                return;
            }

            transforms.Add(transform);
            for (int i =0; i < transform.childCount; i++)
            {
                CollectIKTransformsRecursive(transform.GetChild(i), transforms);
            }
        }

        private void DrawIKSkeleton(List<Transform> transforms)
        {
            if (transforms == null || transforms.Count ==0)
            {
                return;
            }

            HashSet<Transform> transformSet = new HashSet<Transform>(transforms);
            Color previousColor = Handles.color;
            for (int i =0; i < transforms.Count; i++)
            {
                Transform joint = transforms[i];
                if (joint == null || joint.parent == null || !transformSet.Contains(joint.parent))
                {
                    continue;
                }

                Handles.color = IsAdjustBone(joint.name) ? new Color(0.55f,0.65f,0.75f,0.65f) : new Color(0.9f,0.9f,0.9f,0.9f);
                Handles.DrawLine(joint.parent.position, joint.position);
            }
            Handles.color = previousColor;
        }

        private float GetIKHandleSize(Transform joint, HashSet<Transform> transformSet)
        {
            if (joint == null)
            {
                return 0.01f;
            }

            float nearestDistance = float.MaxValue;
            if (joint.parent != null && transformSet != null && transformSet.Contains(joint.parent))
            {
                nearestDistance = Mathf.Min(nearestDistance, Vector3.Distance(joint.position, joint.parent.position));
            }

            for (int i =0; i < joint.childCount; i++)
            {
                Transform child = joint.GetChild(i);
                if (child != null && (transformSet == null || transformSet.Contains(child)))
                {
                    nearestDistance = Mathf.Min(nearestDistance, Vector3.Distance(joint.position, child.position));
                }
            }

            float viewSize = HandleUtility.GetHandleSize(joint.position);
            if (nearestDistance == float.MaxValue || nearestDistance <= Mathf.Epsilon)
            {
                nearestDistance = viewSize *0.25f;
            }

            float size = nearestDistance * Mathf.Max(0.01f, ikHandleBaseSize);
            return Mathf.Clamp(size, viewSize * ikHandleMinViewScale, viewSize * ikHandleMaxViewScale);
        }

        private int CountNonAdjustChildren(Transform transform)
        {
            if (transform == null)
            {
                return 0;
            }

            int count =0;
            for (int i =0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && !IsAdjustBone(child.name))
                {
                    count++;
                }
            }
            return count;
        }

        private static bool IsAncestorOrSelf(Transform ancestor, Transform transform)
        {
            Transform current = transform;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private bool IsLogicalIKBoundary(Transform transform, Transform skeletonRoot)
        {
            return transform != null && (transform == skeletonRoot || CountNonAdjustChildren(transform) >1);
        }

        private Transform FindNaturalIKRoot(Transform effector)
        {
            if (effector == null)
            {
                return null;
            }

            Transform skeletonRoot = GetIKRootTransform();
            Transform current = effector;
            while (current.parent != null)
            {
                Transform parent = current.parent;
                if (IsLogicalIKBoundary(parent, skeletonRoot))
                {
                    if (!ikUseBoundaryBone && current == effector)
                    {
                        current = parent;
                        continue;
                    }

                    return current;
                }
                current = parent;
            }
            return current;
        }

        private Transform FindIKBoundaryTransform()
        {
            if (string.IsNullOrEmpty(ikBoundaryBoneName) || context == null || context.activeUMA == null || context.activeUMA.skeleton == null)
            {
                return null;
            }
            return context.activeUMA.skeleton.GetBoneTransform(ikBoundaryBoneName);
        }

        private Transform ResolveIKChainRoot(Transform effector)
        {
            Transform naturalRoot = FindNaturalIKRoot(effector);
            if (!ikUseBoundaryBone)
            {
                ikStatusMessage = "";
                return naturalRoot;
            }

            Transform boundary = FindIKBoundaryTransform();
            if (boundary != null && !IsAdjustBone(boundary.name) && IsAncestorOrSelf(boundary, effector))
            {
                ikStatusMessage = "";
                return boundary;
            }

            ikStatusMessage = "IK boundary is not a valid ancestor of the dragged joint; using the natural branch/root.";
            return naturalRoot;
        }

        private List<Transform> BuildIKChain(Transform effector)
        {
            List<Transform> reversedChain = new List<Transform>();
            if (effector == null)
            {
                return reversedChain;
            }

            Transform root = ResolveIKChainRoot(effector);
            Transform current = effector;
            while (current != null)
            {
                if (!IsAdjustBone(current.name))
                {
                    reversedChain.Add(current);
                }

                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            reversedChain.Reverse();
            return reversedChain;
        }

        private Dictionary<Transform, IKTransformState> CaptureTransformStates(List<Transform> transforms)
        {
            Dictionary<Transform, IKTransformState> states = new Dictionary<Transform, IKTransformState>();
            if (transforms == null)
            {
                return states;
            }

            for (int i =0; i < transforms.Count; i++)
            {
                Transform transform = transforms[i];
                if (transform != null && !states.ContainsKey(transform))
                {
                    states.Add(transform, new IKTransformState(transform));
                }
            }
            return states;
        }

        private void MoveIKJointToWorldPosition(Transform joint, Vector3 worldPosition)
        {
            if (joint == null)
            {
                return;
            }

            if (joint.parent != null)
            {
                joint.localPosition = joint.parent.InverseTransformPoint(worldPosition);
            }
            else
            {
                joint.position = worldPosition;
            }
        }

        private void SolveIKChainCCD(List<Transform> chain, Vector3 targetPosition)
        {
            if (chain == null || chain.Count ==0)
            {
                return;
            }

            Transform effector = chain[chain.Count -1];
            if (effector == null)
            {
                return;
            }

            if (chain.Count <2)
            {
                MoveIKJointToWorldPosition(effector, targetPosition);
                return;
            }

            float toleranceSqr = ikSolveTolerance * ikSolveTolerance;
            for (int iteration =0; iteration < ikMaxIterations; iteration++)
            {
                if ((effector.position - targetPosition).sqrMagnitude <= toleranceSqr)
                {
                    break;
                }

                for (int i = chain.Count -2; i >=0; i--)
                {
                    Transform joint = chain[i];
                    if (joint == null || IsAdjustBone(joint.name))
                    {
                        continue;
                    }

                    Vector3 toEffector = effector.position - joint.position;
                    Vector3 toTarget = targetPosition - joint.position;
                    if (toEffector.sqrMagnitude <= 0.0000001f || toTarget.sqrMagnitude <= 0.0000001f)
                    {
                        continue;
                    }

                    Quaternion swing = Quaternion.FromToRotation(toEffector, toTarget);
                    joint.rotation = NormalizeSafe(swing * joint.rotation);
                }
            }
        }

        private SerializedProperty FindPoseByBoneName(SerializedProperty poses, string boneName)
        {
            if (poses == null || string.IsNullOrEmpty(boneName))
            {
                return null;
            }

            for (int i =0; i < poses.arraySize; i++)
            {
                SerializedProperty pose = poses.GetArrayElementAtIndex(i);
                SerializedProperty bone = pose.FindPropertyRelative("bone");
                if (bone != null && bone.stringValue == boneName)
                {
                    return pose;
                }
            }
            return null;
        }

        private SerializedProperty FindOrCreatePoseByBoneName(SerializedProperty poses, string boneName)
        {
            SerializedProperty pose = FindPoseByBoneName(poses, boneName);
            if (pose != null)
            {
                return pose;
            }

            AddABone(poses, boneName);
            return FindPoseByBoneName(poses, boneName);
        }

        private bool CommitIKChainToPose(SerializedProperty poses, Dictionary<Transform, IKTransformState> previousStates)
        {
            if (poses == null || previousStates == null || previousStates.Count ==0)
            {
                return false;
            }

            bool changed = false;
            foreach (var kvp in previousStates)
            {
                Transform transform = kvp.Key;
                if (transform == null || IsAdjustBone(transform.name))
                {
                    continue;
                }

                IKTransformState previous = kvp.Value;
                Vector3 positionDelta = transform.localPosition - previous.localPosition;
                bool positionChanged = positionDelta.sqrMagnitude > 0.00000001f;
                bool rotationChanged = Quaternion.Angle(previous.localRotation, transform.localRotation) > 0.001f;
                bool scaleChanged = (transform.localScale - previous.localScale).sqrMagnitude > 0.00000001f;
                if (!positionChanged && !rotationChanged && !scaleChanged)
                {
                    continue;
                }

                SerializedProperty pose = FindOrCreatePoseByBoneName(poses, transform.name);
                if (pose == null)
                {
                    continue;
                }

                if (positionChanged)
                {
                    SerializedProperty position = pose.FindPropertyRelative("position");
                    position.vector3Value = position.vector3Value + positionDelta;
                }

                if (rotationChanged)
                {
                    SerializedProperty rotation = pose.FindPropertyRelative("rotation");
                    Quaternion localDelta = NormalizeSafe(Quaternion.Inverse(previous.localRotation) * transform.localRotation);
                    rotation.quaternionValue = NormalizeSafe(rotation.quaternionValue * localDelta);
                }

                if (scaleChanged)
                {
                    SerializedProperty scale = pose.FindPropertyRelative("scale");
                    scale.vector3Value = transform.localScale;
                }

                changed = true;
                _poseEdited = true;
            }

            return changed;
        }

        private bool ApplyIKDrag(Transform effector, Vector3 targetPosition, SerializedProperty poses)
        {
            if (effector == null || poses == null)
            {
                return false;
            }

            List<Transform> chain = BuildIKChain(effector);
            if (chain.Count ==0)
            {
                return false;
            }

            Dictionary<Transform, IKTransformState> previousStates = CaptureTransformStates(chain);
            List<UnityEngine.Object> undoObjects = new List<UnityEngine.Object>();
            undoObjects.Add(target);
            for (int i =0; i < chain.Count; i++)
            {
                if (chain[i] != null)
                {
                    undoObjects.Add(chain[i]);
                }
            }

            Undo.RecordObjects(undoObjects.ToArray(), "IK Edit Bone Pose");
            SolveIKChainCCD(chain, targetPosition);
            return CommitIKChainToPose(poses, previousStates);
        }

        private bool TryGetIKMovementPlane(Transform joint, out Vector3 axisA, out Vector3 axisB, out Vector3 normal, out Color planeColor, out string planeLabel)
        {
            axisA = Vector3.right;
            axisB = Vector3.up;
            normal = Vector3.forward;
            planeColor = Color.white;
            planeLabel = string.Empty;

            if (ikMovementPlane == IKMovementPlane.Free)
            {
                return false;
            }

            Vector3 localAxisA;
            Vector3 localAxisB;
            Vector3 localNormal;
            if (ikMovementPlane == IKMovementPlane.XZ)
            {
                localAxisA = Vector3.right;
                localAxisB = Vector3.forward;
                localNormal = Vector3.up;
                planeColor = Handles.yAxisColor;
                planeLabel = "X/Z";
            }
            else if (ikMovementPlane == IKMovementPlane.YZ)
            {
                localAxisA = Vector3.up;
                localAxisB = Vector3.forward;
                localNormal = Vector3.right;
                planeColor = Handles.xAxisColor;
                planeLabel = "Y/Z";
            }
            else
            {
                localAxisA = Vector3.right;
                localAxisB = Vector3.up;
                localNormal = Vector3.forward;
                planeColor = Handles.zAxisColor;
                planeLabel = "X/Y";
            }

            if (ikMovementPlaneSpace == IKMovementPlaneSpace.Local && joint != null)
            {
                axisA = joint.TransformDirection(localAxisA).normalized;
                axisB = joint.TransformDirection(localAxisB).normalized;
                normal = joint.TransformDirection(localNormal).normalized;
            }
            else
            {
                axisA = localAxisA;
                axisB = localAxisB;
                normal = localNormal;
            }

            return true;
        }

        private void GetIKMovementArea(Transform joint, Vector3 normal, float handleSize, out Vector3 center, out float radius)
        {
            center = joint != null ? joint.position : Vector3.zero;
            radius = Mathf.Max(handleSize *4f, 0.04f);
            if (joint == null)
            {
                return;
            }

            List<Transform> chain = BuildIKChain(joint);
            if (chain == null || chain.Count <2 || chain[0] == null)
            {
                return;
            }

            float chainReach =0f;
            for (int i =1; i < chain.Count; i++)
            {
                Transform previousJoint = chain[i -1];
                Transform currentJoint = chain[i];
                if (previousJoint != null && currentJoint != null)
                {
                    chainReach += Vector3.Distance(previousJoint.position, currentJoint.position);
                }
            }

            if (chainReach <= Mathf.Epsilon)
            {
                return;
            }

            Vector3 normalizedNormal = normal.sqrMagnitude > Mathf.Epsilon ? normal.normalized : Vector3.up;
            Transform root = chain[0];
            Vector3 rootToPlane = root.position - joint.position;
            float signedDistanceToPlane = Vector3.Dot(rootToPlane, normalizedNormal);
            center = root.position - normalizedNormal * signedDistanceToPlane;
            float distanceToPlane = Mathf.Abs(signedDistanceToPlane);
            float planeRadiusSqr = Mathf.Max(0f, chainReach * chainReach - distanceToPlane * distanceToPlane);
            radius = Mathf.Max(radius, Mathf.Sqrt(planeRadiusSqr));
        }

        private void DrawIKMovementPlaneCue(Transform joint, Vector3 axisA, Vector3 axisB, Vector3 normal, Color planeColor, string planeLabel, float handleSize)
        {
            if (joint == null)
            {
                return;
            }

            GetIKMovementArea(joint, normal, handleSize, out Vector3 center, out float movementRadius);
            Vector3 normalizedNormal = normal.sqrMagnitude > Mathf.Epsilon ? normal.normalized : Vector3.up;
            Color previousColor = Handles.color;
            Color fillColor = new Color(planeColor.r, planeColor.g, planeColor.b, 0.10f);
            Color outlineColor = new Color(planeColor.r, planeColor.g, planeColor.b, 0.55f);
            Handles.color = fillColor;
            Handles.DrawSolidDisc(center, normalizedNormal, movementRadius);
            Handles.color = outlineColor;
            Handles.DrawWireDisc(center, normalizedNormal, movementRadius);
            Handles.color = previousColor;

            if (ikPlaneLabelStyle == null)
            {
                ikPlaneLabelStyle = new GUIStyle(EditorStyles.boldLabel);
            }
            ikPlaneLabelStyle.normal.textColor = planeColor;
            Vector3 labelOffset = (axisA + axisB).sqrMagnitude > Mathf.Epsilon ? (axisA + axisB).normalized * movementRadius *1.05f : Vector3.up * movementRadius;
            Handles.Label(center + labelOffset, planeLabel, ikPlaneLabelStyle);
        }

        private Vector3 DrawIKMoveHandle(Transform joint, float handleSize, int controlId, bool isActive)
        {
            if (TryGetIKMovementPlane(joint, out Vector3 axisA, out Vector3 axisB, out Vector3 normal, out Color planeColor, out string planeLabel))
            {
                if (isActive)
                {
                    DrawIKMovementPlaneCue(joint, axisA, axisB, normal, planeColor, planeLabel, handleSize);
                }
                Handles.color = isActive ? new Color(planeColor.r, planeColor.g, planeColor.b, 1f) : new Color(0.82f,0.86f,0.9f,0.8f);
                return Handles.Slider2D(controlId, joint.position, normal, axisA, axisB, handleSize, Handles.SphereHandleCap, Vector2.zero);
            }

            Handles.color = isActive ? new Color(1f,0.9f,0.12f,1f) : new Color(1f,0.78f,0.2f,0.65f);
            return Handles.FreeMoveHandle(controlId, joint.position, handleSize, Vector3.zero, Handles.SphereHandleCap);
        }

        private bool IsIKHandleClick(int controlId)
        {
            Event currentEvent = Event.current;
            return currentEvent != null
                && currentEvent.type == EventType.MouseDown
                && currentEvent.button ==0
                && HandleUtility.nearestControl == controlId;
        }

        private void DoIKSceneGUI(SceneView scene)
        {
            if (!haveValidContext || target == null || targetPose == null)
            {
                DrawSkeletonBones();
                return;
            }

            if (serializedObject == null || serializedObject.targetObject == null)
            {
                DrawSkeletonBones();
                return;
            }

            serializedObject.Update();
            SerializedProperty poses = serializedObject.FindProperty("poses");
            if (poses == null)
            {
                DrawSkeletonBones();
                return;
            }

            List<Transform> transforms = CollectIKTransforms();
            DrawIKSkeleton(transforms);
            if (transforms.Count ==0)
            {
                return;
            }

            HashSet<Transform> transformSet = new HashSet<Transform>(transforms);
            if (ikActiveJoint != null && !transformSet.Contains(ikActiveJoint))
            {
                ikActiveJoint = null;
            }

            Color previousColor = Handles.color;
            bool changed = false;
            for (int i =0; i < transforms.Count; i++)
            {
                Transform joint = transforms[i];
                if (joint == null || IsAdjustBone(joint.name))
                {
                    continue;
                }

                float handleSize = GetIKHandleSize(joint, transformSet);
                int handleControlId = GUIUtility.GetControlID(FocusType.Passive);
                if (IsIKHandleClick(handleControlId))
                {
                    ikActiveJoint = joint;
                    Repaint();
                    SceneView.RepaintAll();
                }

                bool isActiveJoint = ikActiveJoint == joint;
                EditorGUI.BeginChangeCheck();
                Vector3 newPosition = DrawIKMoveHandle(joint, handleSize, handleControlId, isActiveJoint);
                if (GUIUtility.hotControl == handleControlId && ikActiveJoint != joint)
                {
                    ikActiveJoint = joint;
                    Repaint();
                    SceneView.RepaintAll();
                }
                if (EditorGUI.EndChangeCheck())
                {
                    ikActiveJoint = joint;
                    changed = ApplyIKDrag(joint, newPosition, poses);
                    break;
                }
            }
            Handles.color = previousColor;

            bool scenePoseChanged = serializedObject.ApplyModifiedProperties();
            if (changed || scenePoseChanged)
            {
                RegeneratePoseTargetPreviewIfNeeded();
                EditorUtility.SetDirty(target);
                Repaint();
            }
        }

        private string[] GetIKBoundaryBoneOptions()
        {
            if (context != null && context.boneList != null && context.boneList.Length >0)
            {
                return context.boneList;
            }
            return new string[] { "Position" };
        }

        private static int IndexOfIKBoundaryBoneOption(string[] options, string boneName)
        {
            if (options == null || string.IsNullOrEmpty(boneName))
            {
                return BAD_INDEX;
            }

            for (int i =0; i < options.Length; i++)
            {
                if (string.Equals(options[i], boneName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return BAD_INDEX;
        }

        private static string FindIKBoundaryBoneOption(string[] options, string boneName)
        {
            int index = IndexOfIKBoundaryBoneOption(options, boneName);
            return index != BAD_INDEX ? options[index] : null;
        }

        private static bool ContainsIKBoundaryBoneOption(List<string> options, string boneName)
        {
            if (options == null || string.IsNullOrEmpty(boneName))
            {
                return false;
            }

            for (int i =0; i < options.Count; i++)
            {
                if (string.Equals(options[i], boneName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private string[] GetIKBoundaryQuickPickOptions(string[] allOptions)
        {
            List<string> quickPickOptions = new List<string>();
            quickPickOptions.Add("Quick Pick...");
            if (allOptions == null || allOptions.Length ==0)
            {
                return quickPickOptions.ToArray();
            }

            for (int i =0; i < ikCommonBoundaryBoneNames.Length; i++)
            {
                string option = FindIKBoundaryBoneOption(allOptions, ikCommonBoundaryBoneNames[i]);
                if (!string.IsNullOrEmpty(option) && !ContainsIKBoundaryBoneOption(quickPickOptions, option))
                {
                    quickPickOptions.Add(option);
                }
            }
            return quickPickOptions.ToArray();
        }

        private string[] GetFilteredIKBoundaryBoneOptions(string[] allOptions)
        {
            if (allOptions == null || allOptions.Length ==0)
            {
                return new string[0];
            }

            string filterText = string.IsNullOrEmpty(ikBoundaryBoneFilter) ? string.Empty : ikBoundaryBoneFilter.Trim();
            if (string.IsNullOrEmpty(filterText))
            {
                return allOptions;
            }

            List<string> filteredOptions = new List<string>();
            string currentOption = FindIKBoundaryBoneOption(allOptions, ikBoundaryBoneName);
            if (!string.IsNullOrEmpty(currentOption))
            {
                filteredOptions.Add(currentOption);
            }

            for (int i =0; i < allOptions.Length; i++)
            {
                string option = allOptions[i];
                if (!string.IsNullOrEmpty(option)
                    && option.IndexOf(filterText, System.StringComparison.OrdinalIgnoreCase) >=0
                    && !ContainsIKBoundaryBoneOption(filteredOptions, option))
                {
                    filteredOptions.Add(option);
                }
            }
            return filteredOptions.ToArray();
        }

        private bool SetIKBoundaryBoneName(string[] allOptions, string boneName)
        {
            int index = IndexOfIKBoundaryBoneOption(allOptions, boneName);
            if (index == BAD_INDEX)
            {
                return false;
            }

            ikBoundaryBoneIndex = index;
            ikBoundaryBoneName = allOptions[index];
            ikStatusMessage = "";
            return true;
        }

        private void SyncIKBoundaryBoneIndex(string[] options)
        {
            if (options == null || options.Length ==0)
            {
                ikBoundaryBoneIndex = BAD_INDEX;
                ikBoundaryBoneName = "Position";
                return;
            }

            int positionIndex =0;
            for (int i =0; i < options.Length; i++)
            {
                if (options[i] == "Position")
                {
                    positionIndex = i;
                    break;
                }
            }

            if (string.IsNullOrEmpty(ikBoundaryBoneName))
            {
                ikBoundaryBoneName = "Position";
            }

            int currentIndex = IndexOfIKBoundaryBoneOption(options, ikBoundaryBoneName);
            if (currentIndex != BAD_INDEX)
            {
                ikBoundaryBoneIndex = currentIndex;
                ikBoundaryBoneName = options[ikBoundaryBoneIndex];
                return;
            }

            ikBoundaryBoneIndex = positionIndex;
            ikBoundaryBoneName = options[ikBoundaryBoneIndex];
        }

        private List<Transform> GetPoseAnimationTransforms()
        {
            List<Transform> transforms = new List<Transform>();
            if (targetPose == null || targetPose.poses == null || sourceUMA == null || sourceUMA.skeleton == null)
            {
                return transforms;
            }

            HashSet<Transform> seen = new HashSet<Transform>();
            for (int i =0; i < targetPose.poses.Length; i++)
            {
                var pose = targetPose.poses[i];
                if (pose == null || !pose.enabled || string.IsNullOrEmpty(pose.bone))
                {
                    continue;
                }

                Transform transform = sourceUMA.skeleton.GetBoneTransform(pose.hash);
                if (transform == null)
                {
                    transform = sourceUMA.skeleton.GetBoneTransform(pose.bone);
                }

                if (transform != null && seen.Add(transform))
                {
                    transforms.Add(transform);
                }
            }
            return transforms;
        }

        private static bool AllTransformsAreUnder(Transform root, List<Transform> transforms)
        {
            if (root == null || transforms == null || transforms.Count ==0)
            {
                return false;
            }

            for (int i =0; i < transforms.Count; i++)
            {
                if (transforms[i] == null || !IsAncestorOrSelf(root, transforms[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private Transform FindCommonAncestor(List<Transform> transforms)
        {
            if (transforms == null || transforms.Count ==0 || transforms[0] == null)
            {
                return null;
            }

            Transform candidate = transforms[0];
            while (candidate != null)
            {
                if (AllTransformsAreUnder(candidate, transforms))
                {
                    return candidate;
                }
                candidate = candidate.parent;
            }
            return null;
        }

        private Transform ResolveAnimationBindingRoot(List<Transform> transforms)
        {
            if (sourceUMA == null)
            {
                return null;
            }

            List<Transform> candidates = new List<Transform>();
            if (sourceUMA.animator != null)
            {
                candidates.Add(sourceUMA.animator.transform);
            }
            candidates.Add(sourceUMA.transform);
            if (sourceUMA.umaRoot != null)
            {
                candidates.Add(sourceUMA.umaRoot.transform);
            }
            if (sourceUMA.skeleton != null)
            {
                Transform root = sourceUMA.skeleton.GetRootTransform();
                if (root != null)
                {
                    candidates.Add(root);
                }
                Transform global = sourceUMA.skeleton.GetGlobalTransform();
                if (global != null)
                {
                    candidates.Add(global);
                }
            }

            for (int i =0; i < candidates.Count; i++)
            {
                Transform candidate = candidates[i];
                if (candidate != null && AllTransformsAreUnder(candidate, transforms))
                {
                    return candidate;
                }
            }

            return FindCommonAncestor(transforms);
        }

        private static AnimationCurve ConstantCurve(float value)
        {
            return new AnimationCurve(new Keyframe(0f, value));
        }

        private static void SetTransformCurve(AnimationClip clip, string path, string propertyName, float value)
        {
            EditorCurveBinding binding = new EditorCurveBinding();
            binding.path = path;
            binding.type = typeof(Transform);
            binding.propertyName = propertyName;
            AnimationUtility.SetEditorCurve(clip, binding, ConstantCurve(value));
        }

        private static string GetAssetNameFromPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return "Pose";
            }

            string fileName = assetPath;
            int slash = fileName.LastIndexOf('/');
            if (slash >=0 && slash +1 < fileName.Length)
            {
                fileName = fileName.Substring(slash +1);
            }
            if (fileName.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName.Substring(0, fileName.Length -5);
            }
            return string.IsNullOrEmpty(fileName) ? "Pose" : fileName;
        }

        private void SavePoseAsAnimation()
        {
            if (sourceUMA == null || sourceUMA.skeleton == null || targetPose == null)
            {
                EditorUtility.DisplayDialog("Save Pose Animation", "Assign a built Source UMA before saving an animation.", "OK");
                return;
            }

            if (!dynamicDNAConverterMode && haveValidContext)
            {
                ApplySourceSkeletonPreview(true);
            }

            List<Transform> transforms = GetPoseAnimationTransforms();
            if (transforms.Count ==0)
            {
                EditorUtility.DisplayDialog("Save Pose Animation", "No enabled pose bones with valid source transforms were found.", "OK");
                return;
            }

            Transform bindingRoot = ResolveAnimationBindingRoot(transforms);
            if (bindingRoot == null)
            {
                EditorUtility.DisplayDialog("Save Pose Animation", "Could not find a common transform root for the pose bones.", "OK");
                return;
            }

            string defaultName = targetPose != null && !string.IsNullOrEmpty(targetPose.name) ? targetPose.name + "_Pose" : "UMABonePose_Pose";
            string assetPath = EditorUtility.SaveFilePanelInProject("Save Pose Animation", defaultName, "anim", "Save current bone pose as a Unity animation clip.");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            bool createClip = clip == null;
            if (createClip && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
            {
                EditorUtility.DisplayDialog("Save Pose Animation", "The selected asset path already exists and is not an AnimationClip.", "OK");
                return;
            }

            if (clip == null)
            {
                clip = new AnimationClip();
            }
            else
            {
                clip.ClearCurves();
            }

            clip.name = GetAssetNameFromPath(assetPath);
            clip.frameRate =60f;

            int curveBoneCount =0;
            for (int i =0; i < transforms.Count; i++)
            {
                Transform transform = transforms[i];
                if (transform == null || !IsAncestorOrSelf(bindingRoot, transform))
                {
                    continue;
                }

                string path = transform == bindingRoot ? "" : AnimationUtility.CalculateTransformPath(transform, bindingRoot);
                Vector3 position = transform.localPosition;
                Quaternion rotation = NormalizeSafe(transform.localRotation);
                Vector3 scale = transform.localScale;

                SetTransformCurve(clip, path, "m_LocalPosition.x", position.x);
                SetTransformCurve(clip, path, "m_LocalPosition.y", position.y);
                SetTransformCurve(clip, path, "m_LocalPosition.z", position.z);
                SetTransformCurve(clip, path, "m_LocalRotation.x", rotation.x);
                SetTransformCurve(clip, path, "m_LocalRotation.y", rotation.y);
                SetTransformCurve(clip, path, "m_LocalRotation.z", rotation.z);
                SetTransformCurve(clip, path, "m_LocalRotation.w", rotation.w);
                SetTransformCurve(clip, path, "m_LocalScale.x", scale.x);
                SetTransformCurve(clip, path, "m_LocalScale.y", scale.y);
                SetTransformCurve(clip, path, "m_LocalScale.z", scale.z);
                curveBoneCount++;
            }

            if (curveBoneCount ==0)
            {
                EditorUtility.DisplayDialog("Save Pose Animation", "No pose bones were under the selected animation binding root.", "OK");
                return;
            }

            if (createClip)
            {
                AssetDatabase.CreateAsset(clip, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(clip);
            }

            AssetDatabase.SaveAssets();
            Selection.activeObject = clip;
            EditorGUIUtility.PingObject(clip);
            Debug.Log($"[UMABonePoseEditor] Saved pose animation '{assetPath}' with curves for {curveBoneCount} bones using root '{bindingRoot.name}'.");
        }

        private void ResetSkeletonToBasePose(SerializedProperty poses)
        {
            if (sourceUMA == null || sourceUMA.skeleton == null)
            {
                return;
            }

            List<UnityEngine.Object> undoObjects = new List<UnityEngine.Object>();
            if (target != null)
            {
                undoObjects.Add(target);
            }
            List<Transform> sourceTransforms = CollectIKTransforms();
            for (int i =0; i < sourceTransforms.Count; i++)
            {
                if (sourceTransforms[i] != null)
                {
                    undoObjects.Add(sourceTransforms[i]);
                }
            }
            if (undoObjects.Count >0)
            {
                Undo.RecordObjects(undoObjects.ToArray(), "Reset Bone Pose Skeleton");
            }

            if (poses != null)
            {
                for (int i =0; i < poses.arraySize; i++)
                {
                    SerializedProperty pose = poses.GetArrayElementAtIndex(i);
                    pose.FindPropertyRelative("position").vector3Value = Vector3.zero;
                    pose.FindPropertyRelative("rotation").quaternionValue = Quaternion.identity;
                    pose.FindPropertyRelative("scale").vector3Value = Vector3.one;
                }
            }
            if (serializedObject != null && serializedObject.targetObject != null)
            {
                serializedObject.ApplyModifiedProperties();
            }

            ClearActiveEditState();
            sourceUMA.skeleton.ResetAll();
            if (!dynamicDNAConverterMode && haveValidContext && context.activeUMA == sourceUMA)
            {
                ApplySourceSkeletonPreview(false, true);
            }

            _poseEdited = true;
            ikStatusMessage = "";
            RegeneratePoseTargetPreviewIfNeeded();
            EditorUtility.SetDirty(target);
            Repaint();
            SceneView.RepaintAll();
        }

        private bool ShouldProtectBonePoseSceneSelection()
        {
            return protectBonePoseSceneSelection
                && sourceUMA != null
                && targetPose != null
                && serializedObject != null
                && serializedObject.targetObject != null;
        }

        private void ProtectBonePoseSceneSelection()
        {
            bool shouldProtect = ShouldProtectBonePoseSceneSelection();
            SetBonePoseSelectionProtection(shouldProtect);
            if (shouldProtect && Event.current != null && Event.current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }
        }

        private bool DrawBonePoseSceneEditingOverlay()
        {
            if (!ShouldProtectBonePoseSceneSelection())
            {
                return false;
            }

            bool exitRequested = false;
            Handles.BeginGUI();
            Rect areaRect = new Rect(8f,8f,180f,24f);
            GUILayout.BeginArea(areaRect, EditorStyles.toolbar);
            if (GUILayout.Button("Exit Bone Pose Editing", EditorStyles.toolbarButton))
            {
                exitRequested = true;
            }
            GUILayout.EndArea();
            Handles.EndGUI();

            if (!exitRequested)
            {
                return false;
            }

            EndBonePoseSceneEditing();
            return true;
        }

        private void EndBonePoseSceneEditing()
        {
            protectBonePoseSceneSelection = false;
            SetBonePoseSelectionProtection(false);
            useIKEditor = false;
            ClearActiveEditState();
            TryRestoreAndRebuildOnExit();
            Repaint();
            SceneView.RepaintAll();
        }

        private void DoSceneGUI(SceneView scene)
        {
            if (IsEditorBusy) { DrawSkeletonBones(); return; }
            ProtectBonePoseSceneSelection();
            if (DrawBonePoseSceneEditingOverlay())
            {
                return;
            }

            if (useIKEditor)
            {
                DoIKSceneGUI(scene);
                return;
            }
            if (!haveValidContext || !haveEditTarget || target == null || targetPose == null)
            {
                DrawSkeletonBones();
                return;
            }

            try
            {
                if (serializedObject == null || serializedObject.targetObject == null)
                {
                    DrawSkeletonBones();
                    return;
                }

                serializedObject.Update();
                SerializedProperty poses = serializedObject.FindProperty("poses");
                if (poses == null) { DrawSkeletonBones(); return; }

                SerializedProperty activePose = null;
                SerializedProperty mirrorPose = null;

                if (activeBoneIndex != BAD_INDEX && activeBoneIndex < poses.arraySize)
                {
                    activePose = poses.GetArrayElementAtIndex(activeBoneIndex);
                }

                if (mirrorBoneIndex != BAD_INDEX && mirrorBoneIndex < poses.arraySize)
                {
                    mirrorPose = poses.GetArrayElementAtIndex(mirrorBoneIndex);
                }
                else if (activePose != null)
                {
                    // Fallback: auto-resolve mirror pose by name when index is unknown
                    var activeBoneProp = activePose.FindPropertyRelative("bone");
                    string activeBoneName = activeBoneProp != null ? activeBoneProp.stringValue : null;
                    string mirrorName = DeriveMirrorName(activeBoneName);
                    if (!string.IsNullOrEmpty(mirrorName))
                    {
                        for (int i = 0; i < poses.arraySize; i++)
                        {
                            var p = poses.GetArrayElementAtIndex(i);
                            var pb = p.FindPropertyRelative("bone");
                            if (pb != null && pb.stringValue == mirrorName)
                            {
                                mirrorBoneIndex = i;
                                mirrorPose = p;
                                break;
                            }
                        }
                    }
                }

                // Local helper to ensure a mirror pose exists when needed
                // remove this local function; using class-level EnsureMirrorPose instead
                // SerializedProperty EnsureMirrorPose() { }

                Transform activeTrans = context.activeTransform;
                Transform mirrorTrans = context.mirrorTransform;
                if (disableMirroring || !mirrorActive)
                {
                    mirrorTrans = null;
                }

                if (activeTrans != null)
                {
                    if (context.activeTransChanged)
                    {
                        //scene.pivot = activeTrans.position;
                        //scene.rotation = activeTrans.rotation;

                        //scene.cameraDistance = 2.0f;
                        context.activeTransChanged = false;
                    }

                    // POSITION
                    if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Position)
                    {
                        Vector3 newPos = Handles.PositionHandle(activeTrans.position, activeTrans.rotation);
                        if (newPos != activeTrans.position)
                        {
                            Vector3 newLocalPos = activeTrans.parent.InverseTransformPoint(newPos);
                            Vector3 deltaPos = newLocalPos - activeTrans.localPosition;

                            // Record Undo for transform and asset
                            Undo.RecordObject(activeTrans, "Edit Bone Pose");
                            Undo.RecordObject(target, "Edit Bone Pose");

                            activeTrans.localPosition += deltaPos;
                            if (activePose != null)
                            {
                                var position = activePose.FindPropertyRelative("position");
                                position.vector3Value = position.vector3Value + deltaPos;
                                _poseEdited = true;
                            }

                            // Apply to mirrored transform when available (using detected mirror plane)
                            if (mirrorTrans != null)
                            {
                                var transDelta = MirrorPositionOnly(deltaPos);
                                Undo.RecordObject(mirrorTrans, "Edit Bone Pose");
                                mirrorTrans.localPosition += transDelta;
                                var mp = mirrorPose ?? EnsureMirrorPose(poses, activePose, ref mirrorBoneIndex);
                                if (mp != null)
                                {
                                    mirrorPose = mp;
                                    var mPos = mirrorPose.FindPropertyRelative("position");
                                    mPos.vector3Value = mPos.vector3Value + transDelta;
                                    _poseEdited = true;
                                }
                            }
                            // Ensure mirrored pose updates even when the mirrored transform can't be resolved
                            else
                            {
                                var mp = mirrorPose ?? EnsureMirrorPose(poses, activePose, ref mirrorBoneIndex);
                                if (mp != null)
                                {
                                    mirrorPose = mp;
                                    var mirroredDelta = MirrorPositionOnly(deltaPos);
                                    var mPos = mirrorPose.FindPropertyRelative("position");
                                    mPos.vector3Value = mPos.vector3Value + mirroredDelta;
                                    _poseEdited = true;
                                }
                            }
                        }
                    }

                    // ROTATION
                    if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Rotation)
                    {
                        Quaternion newRotation = Handles.RotationHandle(activeTrans.rotation, activeTrans.position);
                        if (newRotation != activeTrans.rotation)
                        {
                            Quaternion deltaRot = Quaternion.Inverse(activeTrans.rotation) * newRotation;

                            Undo.RecordObject(activeTrans, "Edit Bone Pose");
                            Undo.RecordObject(target, "Edit Bone Pose");

                            activeTrans.localRotation *= deltaRot;
                            if (activePose != null)
                            {
                                var rotation = activePose.FindPropertyRelative("rotation");
                                var nq = NormalizeSafe(rotation.quaternionValue * deltaRot);
                                rotation.quaternionValue = nq;
                                _poseEdited = true;
                            }

                            if (mirrorTrans != null)
                            {
                                var transDelta = MirrorRotationOnly(deltaRot);

                                Undo.RecordObject(mirrorTrans, "Edit Bone Pose");
                                mirrorTrans.localRotation *= transDelta;
                                var mp = mirrorPose ?? EnsureMirrorPose(poses, activePose, ref mirrorBoneIndex);
                                if (mp != null)
                                {
                                    mirrorPose = mp;
                                    var mRot = mirrorPose.FindPropertyRelative("rotation");
                                    var nq = NormalizeSafe(mRot.quaternionValue * transDelta);
                                    mRot.quaternionValue = nq;
                                    _poseEdited = true;
                                }
                            }
                            else
                            {
                                var mp = mirrorPose ?? EnsureMirrorPose(poses, activePose, ref mirrorBoneIndex);
                                if (mp != null)
                                {
                                    mirrorPose = mp;
                                    var mirroredDeltaRot = MirrorRotationOnly(deltaRot);
                                    var mRot = mirrorPose.FindPropertyRelative("rotation");
                                    var nq = NormalizeSafe(mRot.quaternionValue * mirroredDeltaRot);
                                    mRot.quaternionValue = nq;
                                    _poseEdited = true;
                                }
                            }
                        }
                    }

                    // SCALE
                    if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Scale)
                    {
                        Vector3 newScale = Handles.ScaleHandle(activeTrans.localScale, activeTrans.position, activeTrans.rotation, HandleUtility.GetHandleSize(activeTrans.position));
                        if (newScale != activeTrans.localScale)
                        {
                            Undo.RecordObject(activeTrans, "Edit Bone Pose");
                            Undo.RecordObject(target, "Edit Bone Pose");

                            activeTrans.localScale = newScale;
                            if (activePose != null)
                            {
                                var scale = activePose.FindPropertyRelative("scale");
                                scale.vector3Value = newScale;
                                _poseEdited = true;
                            }

                            if (mirrorTrans != null)
                            {
                                Undo.RecordObject(mirrorTrans, "Edit Bone Pose");
                                mirrorTrans.localScale = newScale;
                                var mp = mirrorPose ?? EnsureMirrorPose(poses, activePose, ref mirrorBoneIndex);
                                if (mp != null)
                                {
                                    mirrorPose = mp;
                                    var mScale = mirrorPose.FindPropertyRelative("scale");
                                    mScale.vector3Value = newScale;
                                    _poseEdited = true;
                                }
                            }
                            else
                            {
                                var mp = mirrorPose ?? EnsureMirrorPose(poses, activePose, ref mirrorBoneIndex);
                                if (mp != null)
                                {
                                    mirrorPose = mp;
                                    var mScale = mirrorPose.FindPropertyRelative("scale");
                                    mScale.vector3Value = newScale;
                                    _poseEdited = true;
                                }
                            }
                        }
                    }
                }

                bool scenePoseChanged = serializedObject.ApplyModifiedProperties();
                if (scenePoseChanged)
                {
                    RegeneratePoseTargetPreviewIfNeeded();
                }
                if (_poseEdited)
                {
                    EditorUtility.SetDirty(target);
                    Repaint();
                }
            }
            catch
            {
            }

            DrawSkeletonBones();
        }


        private void AddABone(SerializedProperty poses, string boneName)
        {
            if (poses == null || string.IsNullOrEmpty(boneName)) return;

            // Prevent duplicates: if the bone already exists in the list, do not add again
            for (int i =0; i < poses.arraySize; i++)
            {
                var existing = poses.GetArrayElementAtIndex(i);
                var existingBone = existing.FindPropertyRelative("bone");
                if (existingBone != null && existingBone.stringValue == boneName)
                {
                    return; // already present
                }
            }

            int addedIndex = poses.arraySize;
            poses.InsertArrayElementAtIndex(addedIndex);
            var pose = poses.GetArrayElementAtIndex(addedIndex);
            SerializedProperty bone = pose.FindPropertyRelative("bone");
            bone.stringValue = boneName;
            SerializedProperty hash = pose.FindPropertyRelative("hash");
            hash.intValue = UMASkeleton.StringToHash(boneName);
            SerializedProperty position = pose.FindPropertyRelative("position");
            position.vector3Value = Vector3.zero;
            SerializedProperty rotation = pose.FindPropertyRelative("rotation");
            rotation.quaternionValue = Quaternion.identity;
            SerializedProperty scale = pose.FindPropertyRelative("scale");
            scale.vector3Value = Vector3.one;
            SerializedProperty enabled = pose.FindPropertyRelative("enabled");
            if (enabled != null)
            {
                enabled.boolValue = true;
            }
            _poseEdited = true;
        }

        public void SaveWeights()
        {
            if (_sourcePreviewModified && BonePoseSavers.Count >0)
            {
                RestoreWeights();
            }
            BonePoseSavers.Clear();
            if (TryGetRaceData(sourceUMA, out RaceData race))
            {
                foreach (var converterController in race.dnaConverterList)
                {
                    var plugins = converterController.GetPlugins(typeof(BonePoseDNAConverterPlugin));
                    foreach (var boneplug in plugins)
                    {
                        BonePoseDNAConverterPlugin bc = boneplug as BonePoseDNAConverterPlugin;
                        if (bc != null)
                        {
                            BonePoseSaver bps = new BonePoseSaver();
                            bps.BonePoseDNAConverterPlugin = bc;
                            bps.MasterWeight = bc.masterWeight.globalWeight;

                            foreach (BonePoseDNAConverterPlugin.BonePoseDNAConverter converter in bc.poseDNAConverters)
                            {
                                BoneWeightSaver bws = new BoneWeightSaver();
                                bws.converter = converter;
                                bws.weight = converter.startingPoseWeight;
                                bps.BoneWeights.Add(bws);
                            }
                            BonePoseSavers.Add(bps);
                        }
                    }
                }
            }
        }

        public void ClearBonePoseWeights()
        {
            if (TryGetRaceData(sourceUMA, out RaceData race))
            {
                foreach (var converterController in race.dnaConverterList)
                {
                    var plugins = converterController.GetPlugins(typeof(BonePoseDNAConverterPlugin));
                    foreach (var boneplug in plugins)
                    {
                        BonePoseDNAConverterPlugin bc = boneplug as BonePoseDNAConverterPlugin;
                        if (bc != null)
                        {
                            bc.masterWeight.globalWeight =0.0f;
                            foreach (BonePoseDNAConverterPlugin.BonePoseDNAConverter converter in bc.poseDNAConverters)
                            {
                                converter.startingPoseWeight =0.0f;
                            }
                        }
                    }
                }
            }

        }

        public void RestoreWeights()
        {
            if (BonePoseSavers.Count >0)
            {
                foreach (BonePoseSaver bps in BonePoseSavers)
                {
                    bps.BonePoseDNAConverterPlugin.masterWeight.globalWeight = bps.MasterWeight;
                    foreach (BoneWeightSaver bws in bps.BoneWeights)
                    {
                        bws.converter.startingPoseWeight = bws.weight;
                    }
                }
            }
            BonePoseSavers.Clear();
        }

        public override void OnInspectorGUI()
        {
            if (IsCompilingOrUpdating)
            {
                EditorGUILayout.HelpBox("Editor is compiling/reloading. Please wait�", MessageType.Info);
                return;
            }
            if (target == null)
            {
                EditorGUILayout.HelpBox("Target is not available.", MessageType.Info);
                return;
            }

            if (serializedObject == null || serializedObject.targetObject == null)
            {
                EditorGUILayout.HelpBox("SerializedObject is invalid (asset may be reloading). Re-select the asset when compilation completes.", MessageType.Info);
                return;
            }

            serializedObject.Update();
            SerializedProperty poses = serializedObject.FindProperty("poses");
            if (poses == null)
            {
                EditorGUILayout.HelpBox("'poses' property not found. The asset may be reloading.", MessageType.Warning);
                return;
            }

            if (doBoneAdd)
            {
                if (addBoneNames != null && addBoneNames.Count >0)
                {
                    foreach (string s in addBoneNames)
                    {
                        AddABone(poses, s);
                    }
                }
                else if (!string.IsNullOrEmpty(addBoneName))
                {
                    AddABone(poses, addBoneName);
                }

                activeBoneIndex = BAD_INDEX;
                editBoneIndex = BAD_INDEX;
                mirrorBoneIndex = BAD_INDEX;
                addBoneIndex =0;
                addBoneName = "";
                addBoneNames.Clear();
                doBoneAdd = false;
            }
            if (doBoneRemove)
            {
                if (removeBoneIndex >0 && removeBoneIndex -1 < poses.arraySize)
                {
                    poses.DeleteArrayElementAtIndex(removeBoneIndex -1);
                    _poseEdited = true;
                }

                activeBoneIndex = BAD_INDEX;
                editBoneIndex = BAD_INDEX;
                mirrorBoneIndex = BAD_INDEX;
                removeBoneIndex =0;
                doBoneRemove = false;
            }

            if (!dynamicDNAConverterMode)
            {
                EditorGUILayout.HelpBox("Select a built UMA (DynamicCharacterAvatar, DynamicAvatar, UMAData) to enable editing and addition of new bones.", MessageType.Info);
                UMAData previousSource = saveUMAData;
                sourceUMA = EditorGUILayout.ObjectField("Source UMA", sourceUMA, typeof(UMAData), true) as UMAData;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("Find UMA in scene"))
                {
                    UMAData data = GameObject.FindFirstObjectByType<UMAData>();
                    if (data != null)
                    {
                        sourceUMA = data;
                        saveUMAData = data;

                        ApplySourcePreviewMode(previousSource,true);
                        var active = Selection.activeObject;

                        Selection.activeGameObject = data.gameObject;
                        SceneView.FrameLastActiveSceneView();

                        Selection.activeObject = active;
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                poseTarget = EditorGUILayout.ObjectField("PoseTarget", poseTarget, typeof(UMA.CharacterSystem.DynamicCharacterAvatar), true) as UMA.CharacterSystem.DynamicCharacterAvatar;
                EditorGUI.BeginDisabledGroup(poseTarget == null);
                if (GUILayout.Button("Build Now", GUILayout.Width(90f)))
                {
                    poseTarget.BuildNow();
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("A Mixer Pose is used to mix a new pose in the Race Wizard's Pose Creator. It is not required for editing or generating poses in this editor, and does not affect runtime behavior.", MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mixerPose"), new GUIContent("Mixer Pose"));
                autoUpdatePreview = EditorGUILayout.Toggle("Auto-Update Preview", autoUpdatePreview );
                EditorGUILayout.HelpBox("When enabled, the preview target (below) will update in real-time as you edit the pose. Disable to improve performance when editing complex poses or when using a large source UMA.", MessageType.Info);
                targetSMR = EditorGUILayout.ObjectField("Target SkinnedMeshRenderer", targetSMR, typeof(SkinnedMeshRenderer), true) as SkinnedMeshRenderer;

                bool sourceChanged = (sourceUMA == null && saveUMAData != null)
                    || (sourceUMA != null && saveUMAData == null)
                    || (sourceUMA != null && saveUMAData != null && sourceUMA.GetEntityId() != saveUMAData.GetEntityId());
                if (sourceChanged)
                {
                    saveUMAData = sourceUMA;
                    ApplySourcePreviewMode(previousSource,true);
                }
            }
            else
            {
                if (sourceUMA != null)
                {
                    EditorGUILayout.HelpBox("Switch to 'Scene View' and you will see gizmos to help you edit the positions of the pose bones below that you choose to 'Edit'", MessageType.Info);
                }
            }
            bool allowPoseEditing = !useIKEditor;
            if (sourceUMA != null)
            {
                if (context == null)
                {
                    context = new UMABonePoseEditorContext();
                }
                if (context.activeUMA != sourceUMA)
                {
                    context.activeUMA = sourceUMA;
                    ReloadFullTree();
                }
            }

            if (haveValidContext && !dynamicDNAConverterMode)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(addRemovePadding);
                EditorGUI.BeginDisabledGroup(haveEditTarget || !allowPoseEditing);
                previewWeight = EditorGUILayout.Slider(previewGUIContent, previewWeight,0f,1f);
                EditorGUI.EndDisabledGroup();
                GUILayout.Space(addRemovePadding);
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(EditorGUIUtility.singleLineHeight /2f);

            GUIHelper.BeginVerticalPadded();
            // Global toggle to disable mirroring logic
            bool prevDisableMirroring = disableMirroring;
            disableMirroring = EditorGUILayout.Toggle("Disable Mirroring", disableMirroring);
            if (disableMirroring && !prevDisableMirroring)
            {
            // Clear mirror index so UI does not show mirroring status when disabled
            mirrorBoneIndex = BAD_INDEX;
            }
 MirrorAxis = EditorGUILayout.Popup("Mirror Axis", MirrorAxis, MirrorAxises);
 displayMode = EditorGUILayout.Popup("Bone Display Mode", displayMode, strings);

            bool previousUseIKEditor = useIKEditor;
            useIKEditor = EditorGUILayout.Toggle(useIKEditorGUIContent, useIKEditor);
            if (useIKEditor != previousUseIKEditor)
            {
                if (useIKEditor)
                {
                    protectBonePoseSceneSelection = true;
                    SetBonePoseSelectionProtection(true);
                }
                ClearActiveEditState();
                SceneView.RepaintAll();
            }

            if (useIKEditor)
            {
                allowPoseEditing = false;
                EditorGUI.indentLevel++;
                ikHandleBaseSize = EditorGUILayout.Slider(ikHandleBaseSizeGUIContent, ikHandleBaseSize,0.02f,0.6f);
                ikMovementPlane = (IKMovementPlane)EditorGUILayout.Popup(ikMovementPlaneGUIContent, (int)ikMovementPlane, ikMovementPlaneOptions);
                using (new EditorGUI.DisabledScope(ikMovementPlane == IKMovementPlane.Free))
                {
                    ikMovementPlaneSpace = (IKMovementPlaneSpace)EditorGUILayout.Popup(ikMovementPlaneSpaceGUIContent, (int)ikMovementPlaneSpace, ikMovementPlaneSpaceOptions);
                }
                ikUseBoundaryBone = EditorGUILayout.Toggle(ikUseBoundaryGUIContent, ikUseBoundaryBone);
                if (ikUseBoundaryBone)
                {
                    EditorGUI.BeginDisabledGroup(!haveValidContext);
                    string[] allBoundaryOptions = GetIKBoundaryBoneOptions();
                    SyncIKBoundaryBoneIndex(allBoundaryOptions);

                    string[] quickPickOptions = GetIKBoundaryQuickPickOptions(allBoundaryOptions);
                    using (new EditorGUI.DisabledScope(quickPickOptions.Length <=1))
                    {
                        int quickPickIndex = EditorGUILayout.Popup(ikBoundaryQuickPickGUIContent,0, quickPickOptions);
                        if (quickPickIndex >0 && quickPickIndex < quickPickOptions.Length)
                        {
                            SetIKBoundaryBoneName(allBoundaryOptions, quickPickOptions[quickPickIndex]);
                        }
                    }

                    EditorGUILayout.BeginHorizontal();
                    ikBoundaryBoneFilter = EditorGUILayout.TextField(ikBoundaryFilterGUIContent, ikBoundaryBoneFilter);
                    if (GUILayout.Button("x", GUILayout.Width(22f)))
                    {
                        ikBoundaryBoneFilter = "";
                        GUIUtility.keyboardControl =0;
                    }
                    EditorGUILayout.EndHorizontal();

                    string[] boundaryOptions = GetFilteredIKBoundaryBoneOptions(allBoundaryOptions);
                    int boundaryIndex = IndexOfIKBoundaryBoneOption(boundaryOptions, ikBoundaryBoneName);
                    if (boundaryIndex == BAD_INDEX)
                    {
                        boundaryIndex =0;
                    }

                    using (new EditorGUI.DisabledScope(boundaryOptions.Length ==0))
                    {
                        int newBoundaryIndex = EditorGUILayout.Popup(ikBoundaryBoneGUIContent, Mathf.Clamp(boundaryIndex,0, Mathf.Max(0, boundaryOptions.Length -1)), boundaryOptions);
                        if (boundaryOptions.Length >0 && newBoundaryIndex >=0 && newBoundaryIndex < boundaryOptions.Length)
                        {
                            SetIKBoundaryBoneName(allBoundaryOptions, boundaryOptions[newBoundaryIndex]);
                        }
                    }

                    if (boundaryOptions.Length ==0)
                    {
                        EditorGUILayout.HelpBox("No bones match the boundary filter.", MessageType.Info);
                    }
                    EditorGUI.EndDisabledGroup();
                }

                if (!string.IsNullOrEmpty(ikStatusMessage))
                {
                    EditorGUILayout.HelpBox(ikStatusMessage, MessageType.Info);
                }

                EditorGUI.BeginDisabledGroup(sourceUMA == null || sourceUMA.skeleton == null || targetPose == null);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(resetSkeletonGUIContent))
                {
                    ResetSkeletonToBasePose(poses);
                }
                if (GUILayout.Button(savePoseAnimationGUIContent))
                {
                    SavePoseAsAnimation();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
            }

            if (haveValidContext && !disableMirroring && context.mirrorPlane == UMABonePoseEditorContext.MirrorPlane.Mirror_None)
            {
                EditorGUILayout.HelpBox("Mirroring plane not detected; Mirror Axis still controls mirrored pose deltas when a mirror pose is updated.", MessageType.Info);
            }
            GUILayout.BeginHorizontal();
            if (sourceUMA != null)
            {
                if (GUILayout.Button("Reset UMA"))
                {
                    RestoreWeights();
                    if (sourceUMA?.skeleton != null)
                    {
                        sourceUMA.skeleton.ResetAll();
                    }
                    _poseEdited = true; // ensure rebuild
                    sourceUMA = null;
                }
            }

            if (sourceUMA != null && targetSMR != null)
            {
                EditorGUI.BeginDisabledGroup(!allowPoseEditing || targetSMR.bones == null || targetSMR.bones.Length ==0);
                if (GUILayout.Button(generatePoseGUIContent))
                {
                    GeneratePoseFromSkinnedMeshRenderer();
                    _poseEdited = true;
                }
                EditorGUI.EndDisabledGroup();

                if (targetSMR.bones == null || targetSMR.bones.Length ==0)
                {
                    EditorGUILayout.HelpBox("Target SkinnedMeshRenderer has no bones assigned.", MessageType.Warning);
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!allowPoseEditing);
            if (GUILayout.Button("Convert all Left/Right"))
            {
                for (int i =0; i < poses.arraySize; i++)
                {
                    FlipBone(poses, i);
                }
                _poseEdited = true;
            }
            if (GUILayout.Button("Mirror to opposite"))
            {
                // future: implement if needed
                _poseEdited = true;
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            BoneListFilter = EditorGUILayout.TextField("filter to bones containing: ", BoneListFilter);
            if (GUILayout.Button("x", GUILayout.Width(22)))
            {
                BoneListFilter = "";
                GUIUtility.keyboardControl =0;
            }
            GUILayout.EndHorizontal();

            GUIHelper.EndVerticalPadded();

            if (targetPose == null || targetPose.poses == null)
            {
                EditorGUILayout.HelpBox("Pose data not available.", MessageType.Info);
                return;
            }

            string[] removeBoneOptions = new string[targetPose.poses.Length +1];
            removeBoneOptions[0] = " ";
            for (int i =0; i < targetPose.poses.Length; i++)
            {
                removeBoneOptions[i +1] = targetPose.poses[i].bone;
            }
            string[] addBoneOptions = new string[1];
            if (haveValidContext)
            {
                List<string> addList = new List<string>(context.boneList);
                addList.Insert(0, " ");
                for (int i =0; i < targetPose.poses.Length; i++)
                {
                    addList.Remove(targetPose.poses[i].bone);
                }

                addBoneOptions = addList.ToArray();
            }

            if (editBoneIndex != BAD_INDEX && poses != null && editBoneIndex < poses.arraySize)
            {
                SerializedProperty editBone = poses.GetArrayElementAtIndex(editBoneIndex);
                SerializedProperty bone = editBone.FindPropertyRelative("bone");
                string boneName = bone.stringValue;
                string mirrorBoneName = "";
                if (boneName.StartsWith("Left"))
                {
                    mirrorBoneName = boneName.Replace("Left", "Right");
                }
                if (boneName.StartsWith("Right"))
                {
                    mirrorBoneName = boneName.Replace("Right", "Left");
                }
            }

            poses.isExpanded = EditorGUILayout.Foldout(poses.isExpanded, "Pose Bones (" + poses.arraySize + ")");
            if (poses.isExpanded)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Expand All"))
                {
                    for (int i = 0; i < poses.arraySize; i++)
                    {
                        SerializedProperty pose = poses.GetArrayElementAtIndex(i);
                        var boneProp = pose.FindPropertyRelative("bone");
                        if (boneProp != null) boneProp.isExpanded = true;
                    }
                    Repaint();
                }
                if (GUILayout.Button("Collapse All"))
                {
                    for (int i = 0; i < poses.arraySize; i++)
                    {
                        SerializedProperty pose = poses.GetArrayElementAtIndex(i);
                        var boneProp = pose.FindPropertyRelative("bone");
                        if (boneProp != null) boneProp.isExpanded = false;
                    }
                    Repaint();
                }
                EditorGUI.BeginDisabledGroup(!allowPoseEditing);
                if (GUILayout.Button("Sort"))
                {
                    // Clear any current edit/mirror state to avoid stale references during sorting
                    editBoneIndex = BAD_INDEX;
                    activeBoneIndex = BAD_INDEX;
                    mirrorBoneIndex = BAD_INDEX;
                    mirrorActive = false;
                    if (context != null) context.activeTransform = null;
                    if (boneTreeView != null) boneTreeView.SetSelection(new List<int>());

                    // Safely sort by copying values, sorting, then writing back in order
                    int count = poses.arraySize;
                    var items = new List<(string bone, int hash, Vector3 pos, Quaternion rot, Vector3 scale)>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var p = poses.GetArrayElementAtIndex(i);
                        var pBone = p.FindPropertyRelative("bone").stringValue;
                        var pHash = p.FindPropertyRelative("hash").intValue;
                        var pPos = p.FindPropertyRelative("position").vector3Value;
                        var pRot = p.FindPropertyRelative("rotation").quaternionValue;
                        var pScale = p.FindPropertyRelative("scale").vector3Value;
                        items.Add((pBone, pHash, pPos, pRot, pScale));
                    }

                    items.Sort((a, b) => string.Compare(a.bone, b.bone, System.StringComparison.OrdinalIgnoreCase));

                    Undo.RecordObject(target, "Sort Pose Bones");
                    for (int i = 0; i < count; i++)
                    {
                        var p = poses.GetArrayElementAtIndex(i);
                        p.FindPropertyRelative("bone").stringValue = items[i].bone;
                        p.FindPropertyRelative("hash").intValue = items[i].hash;
                        p.FindPropertyRelative("position").vector3Value = items[i].pos;
                        p.FindPropertyRelative("rotation").quaternionValue = items[i].rot;
                        p.FindPropertyRelative("scale").vector3Value = items[i].scale;
                    }

                    GUIUtility.keyboardControl = 0;
                    _poseEdited = true;
                    serializedObject.ApplyModifiedProperties();
                    RegeneratePoseTargetPreviewIfNeeded();
                    EditorUtility.SetDirty(target);
                    Repaint();
                }
                if (GUILayout.Button("Remove Unmodified"))
                {
                    List<int> toRemove = new List<int>();
                    for (int i =0; i < poses.arraySize; i++)
                    {
                        SerializedProperty pose = poses.GetArrayElementAtIndex(i);
                        SerializedProperty position = pose.FindPropertyRelative("position");
                        SerializedProperty rotation = pose.FindPropertyRelative("rotation");
                        SerializedProperty scale = pose.FindPropertyRelative("scale");
                        bool isDefaultPosition = position.vector3Value == Vector3.zero;
                        bool isDefaultRotation = rotation.quaternionValue == Quaternion.identity;
                        bool isDefaultScale = scale.vector3Value == Vector3.one;
                        if (isDefaultPosition && isDefaultRotation && isDefaultScale)
                        {
                            toRemove.Add(i);
                        }
                    }

                    // Remove from the end to avoid index shifting
                    toRemove.Sort();
                    toRemove.Reverse();
                    foreach (int index in toRemove)
                    {
                        poses.DeleteArrayElementAtIndex(index);
                    }
                    if (toRemove.Count >0)
                    {
                        _poseEdited = true;
                    }
                }
                if (GUILayout.Button("Reset All"))
                {
                    for (int i =0; i < poses.arraySize; i++)
                    {
                        SerializedProperty pose = poses.GetArrayElementAtIndex(i);
                        SerializedProperty position = pose.FindPropertyRelative("position");
                        SerializedProperty rotation = pose.FindPropertyRelative("rotation");
                        SerializedProperty scale = pose.FindPropertyRelative("scale");
                        position.vector3Value = Vector3.zero;
                        rotation.quaternionValue = Quaternion.identity;
                        scale.vector3Value = Vector3.one;
                    }
                    _poseEdited = true;
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();
                for (int i =0; i < poses.arraySize; i++)
                {
                    SerializedProperty pose = poses.GetArrayElementAtIndex(i);
                    drawBoneIndex = i;
                    SerializedProperty bone = pose.FindPropertyRelative("bone");
                    string boneName = bone.stringValue;

                    if (boneName.ToLower().Contains(BoneListFilter.ToLower()) || BoneListFilter == "")
                    {
                        PoseBoneDrawer(pose);
                    }
                }
            }

            GUILayout.Space(EditorGUIUtility.singleLineHeight);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(addRemovePadding);
            if (haveValidContext)
            {
                EditorGUI.BeginDisabledGroup(!allowPoseEditing || addBoneIndex <1);
                if (GUILayout.Button(addBoneGUIContent, GUILayout.Width(90f)))
                {
                    addBoneName = addBoneOptions[Mathf.Clamp(addBoneIndex,0, addBoneOptions.Length -1)];
                    doBoneAdd = true;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.BeginVertical();
                GUILayout.Space(buttonVerticalOffset);
                addBoneIndex = EditorGUILayout.Popup(addBoneIndex, addBoneOptions);
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUI.BeginDisabledGroup(!allowPoseEditing || addBoneName.Length < minBoneNameLength);
                if (GUILayout.Button(addBoneGUIContent, GUILayout.Width(90f)))
                {
                    doBoneAdd = true;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.BeginVertical();
                GUILayout.Space(buttonVerticalOffset);
                addBoneName = EditorGUILayout.TextField(addBoneName);
                EditorGUILayout.EndVertical();
            }
            GUILayout.Space(addRemovePadding);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(addRemovePadding);
            EditorGUI.BeginDisabledGroup(!allowPoseEditing || removeBoneIndex <1);
            if (GUILayout.Button(removeBoneGUIContent, GUILayout.Width(90f)))
            {
                doBoneRemove = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.BeginVertical();
            GUILayout.Space(buttonVerticalOffset);
            removeBoneIndex = EditorGUILayout.Popup(removeBoneIndex, removeBoneOptions);
            EditorGUILayout.EndVertical();
            GUILayout.Space(addRemovePadding);
            EditorGUILayout.EndHorizontal();

            if (boneTreeView.RootNode != null)
            {
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Expand All"))
                {
                    boneTreeView.ExpandAll();
                }
                if (GUILayout.Button("Collapse All"))
                {
                    boneTreeView.CollapseAll();
                }
                if (GUILayout.Button("Select None"))
                {
                    List<int> noselection = new List<int>();
                    boneTreeView.SetSelection(noselection);
                }
                EditorGUI.BeginDisabledGroup(!allowPoseEditing || !boneTreeView.HasSelection());
                if (GUILayout.Button("Add Selected"))
                {
                    addBoneNames = boneTreeView.GetSelectedBones();
                    doBoneAdd = true;
                }
                if (GUILayout.Button("Add + Mirror"))
                {
                    addBoneNames = boneTreeView.GetSelectedBonesWithMirrors();
                    doBoneAdd = true;
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                filter = GUILayout.TextField(filter);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyBoneTreeFilter();
                }
                if (GUILayout.Button("Clear", GUILayout.Width(80)))
                {
                    filter = "";
                    ApplyBoneTreeFilter();
                }

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(10);
                string filterstate = "Bone List (No filter)";
                if (filtered)
                {
                    filterstate = "Bone List (filter=\"" + lastFilter + "\")";
                }
                EditorGUILayout.LabelField(filterstate, EditorStyles.toolbarButton);

                Rect r = GUILayoutUtility.GetLastRect();
                scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);
                r.yMin =0;
                r.height = boneTreeView.totalHeight;

                GUILayout.Space(boneTreeView.totalHeight);

                boneTreeView.OnGUI(r);
                GUILayout.EndScrollView();
            }
            bool inspectorPoseChanged = serializedObject.ApplyModifiedProperties();
            if (inspectorPoseChanged)
            {
                RegeneratePoseTargetPreviewIfNeeded();
            }
        }

        private void GeneratePoseFromSkinnedMeshRenderer()
        {
            if (sourceUMA == null || targetSMR == null)
            {
                Debug.LogError("Both Source UMA and Target SkinnedMeshRenderer must be assigned.");
                return;
            }

            if (sourceUMA.skeleton == null)
            {
                Debug.LogError("Source UMA skeleton is null.");
                return;
            }

            if (targetSMR.bones == null || targetSMR.bones.Length ==0)
            {
                Debug.LogError("Target SkinnedMeshRenderer has no bones assigned.");
                return;
            }

            SerializedProperty poses = serializedObject.FindProperty("poses");
            poses.ClearArray();

            var targetBones = targetSMR.bones;
            var sourceRootBone = sourceUMA.skeleton.GetRootTransform();

            if (sourceRootBone == null)
            {
                Debug.LogError("Source UMA root bone is null.");
                return;
            }

            Debug.Log($"Starting bone pose generation: Source has root '{sourceRootBone.name}', Target has {targetBones.Length} bones");

            Dictionary<Transform, Transform> boneMap = new Dictionary<Transform, Transform>();
            List<string> addedBones = new List<string>();
            List<string> unmappedBones = new List<string>();

            foreach (Transform targetBone in targetBones)
            {
                if (targetBone == null)
                {
                    Debug.LogWarning("Encountered null bone in target SkinnedMeshRenderer bones array");
                    continue;
                }

                Transform sourceBone = FindBoneInHierarchy(targetBone, sourceRootBone, boneMap);

                if (sourceBone != null)
                {
                    Vector3 positionDiff = targetBone.localPosition - sourceBone.localPosition;
                    Quaternion rotationDiff = Quaternion.Inverse(sourceBone.localRotation) * targetBone.localRotation;
                    Vector3 scaleDiff = new Vector3(
                        (sourceBone.localScale.x ==0f && targetBone.localScale.x ==0f) ?1f :
                        (sourceBone.localScale.x !=0f ? targetBone.localScale.x / sourceBone.localScale.x :1f),
                        (sourceBone.localScale.y ==0f && targetBone.localScale.y ==0f) ?1f :
                        (sourceBone.localScale.y !=0f ? targetBone.localScale.y / sourceBone.localScale.y :1f),
                        (sourceBone.localScale.z ==0f && targetBone.localScale.z ==0f) ?1f :
                        (sourceBone.localScale.z !=0f ? targetBone.localScale.z / sourceBone.localScale.z :1f)
                    );

                    if (positionDiff.magnitude >0.0001f ||
                        Quaternion.Angle(Quaternion.identity, rotationDiff) >0.1f ||
                        Vector3.Distance(Vector3.one, scaleDiff) >0.0001f)
                    {
                        AddBoneToTarget(poses, targetBone.name, positionDiff, rotationDiff, scaleDiff);
                        addedBones.Add(targetBone.name);
                    }
                }
                else
                {
                    unmappedBones.Add(targetBone.name);
                }
            }

            serializedObject.ApplyModifiedProperties();
            RegeneratePoseTargetPreviewIfNeeded();
            _poseEdited = true;

            if (addedBones.Count >0)
            {
                Debug.Log($"Generated bone pose with {addedBones.Count} bones: {string.Join(", ", addedBones)}");
            }
            else
            {
                Debug.Log("No significant bone differences found between source UMA and target SkinnedMeshRenderer.");
            }

            if (unmappedBones.Count >0)
            {
                Debug.LogWarning($"Could not map {unmappedBones.Count} bones from target to source: {string.Join(", ", unmappedBones)}");
            }
        }

        private Transform FindBoneInHierarchy(Transform targetBone, Transform sourceRoot, Dictionary<Transform, Transform> boneMap)
        {
            if (targetBone == null || sourceRoot == null)
                return null;

            if (boneMap.TryGetValue(targetBone, out Transform result))
            {
                return result;
            }

            if (string.Compare(sourceRoot.name, targetBone.name, System.StringComparison.OrdinalIgnoreCase) ==0)
            {
                boneMap.Add(targetBone, sourceRoot);
                return sourceRoot;
            }

            result = FindBoneRecursive(sourceRoot, targetBone.name);
            if (result != null)
            {
                boneMap.Add(targetBone, result);
                return result;
            }

            if (targetBone.parent != null)
            {
                Transform sourceParent = FindBoneInHierarchy(targetBone.parent, sourceRoot, boneMap);
                if (sourceParent != null)
                {
                    result = sourceParent.Find(targetBone.name);
                    if (result != null)
                    {
                        boneMap.Add(targetBone, result);
                        return result;
                    }
                }
            }

            return null;
        }

        private Transform FindBoneRecursive(Transform parent, string boneName)
        {
            if (parent == null) return null;

            if (string.Compare(parent.name, boneName, System.StringComparison.OrdinalIgnoreCase) ==0)
            {
                return parent;
            }

            for (int i =0; i < parent.childCount; i++)
            {
                Transform result = FindBoneRecursive(parent.GetChild(i), boneName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void AddBoneToTarget(SerializedProperty poses, string boneName, Vector3 positionDiff, Quaternion rotationDiff, Vector3 scaleDiff)
        {
            int addedIndex = poses.arraySize;
            poses.InsertArrayElementAtIndex(addedIndex);
            var pose = poses.GetArrayElementAtIndex(addedIndex);

            SerializedProperty bone = pose.FindPropertyRelative("bone");
            bone.stringValue = boneName;

            SerializedProperty hash = pose.FindPropertyRelative("hash");
            hash.intValue = UMASkeleton.StringToHash(boneName);

            SerializedProperty position = pose.FindPropertyRelative("position");
            position.vector3Value = positionDiff;

            SerializedProperty rotation = pose.FindPropertyRelative("rotation");
            rotation.quaternionValue = rotationDiff;

            SerializedProperty scale = pose.FindPropertyRelative("scale");
            scale.vector3Value = scaleDiff;

            _poseEdited = true;
        }

        private static void FlipBone(SerializedProperty poses, int i)
        {
            if (poses == null || i <0 || i >= poses.arraySize) return;

            SerializedProperty pose = poses.GetArrayElementAtIndex(i);
            SerializedProperty bone = pose.FindPropertyRelative("bone");
            SerializedProperty position = pose.FindPropertyRelative("position");
            SerializedProperty rotation = pose.FindPropertyRelative("rotation");
            SerializedProperty scale = pose.FindPropertyRelative("scale");
            SerializedProperty hash = pose.FindPropertyRelative("hash");
            if (bone.stringValue.Contains("Left"))
            {
                bone.stringValue = bone.stringValue.Replace("Left", "Right");
                hash.intValue = UMASkeleton.StringToHash(bone.stringValue);
                FlipSingleBone(position, rotation);
            }
            else if (bone.stringValue.Contains("Right"))
            {
                bone.stringValue = bone.stringValue.Replace("Right", "Left");
                hash.intValue = UMASkeleton.StringToHash(bone.stringValue);
                FlipSingleBone(position, rotation);
            }
        }

        private static void FlipSingleBone(SerializedProperty position, SerializedProperty rotation)
        {
            Quaternion localRot = rotation.quaternionValue;
            Vector3 localPos = position.vector3Value;

            switch (MirrorAxis)
            {
                case 0:
                    localRot.x *= -1;
                    localRot.w *= -1;
                    localPos.x *= -1;
                    break;
                case 1:
                    localRot.y *= -1;
                    localRot.w *= -1;
                    localPos.y *= -1;
                    break;
                case 2:
                    localRot.z *= -1;
                    localRot.w *= -1;
                    localPos.z *= -1;
                    break;
            }

            rotation.quaternionValue = localRot;
            position.vector3Value = localPos;
        }

        // Helpers for mirroring only one component type (position or rotation)
        private static Vector3 MirrorPositionOnly(Vector3 pos)
        {
            switch (MirrorAxis)
            {
                case 0: pos.x = -pos.x; break;
                case 1: pos.y = -pos.y; break;
                case 2: pos.z = -pos.z; break;
            }
            return pos;
        }
        private static Quaternion MirrorRotationOnly(Quaternion rot)
        {
            switch (MirrorAxis)
            {
                case 0: rot.x *= -1; rot.w *= -1; break;
                case 1: rot.y *= -1; rot.w *= -1; break;
                case 2: rot.z *= -1; rot.w *= -1; break;
            }
            return rot;
        }

        private void ApplyBoneTreeFilter()
        {
            if (string.IsNullOrEmpty(filter))
            {
                ReloadFullTree();
            }
            else
            {
                ReloadFilteredTree();
            }
        }

        private void ReloadFilteredTree()
        {
            filtered = true;
            lastFilter = filter;
            if (!haveValidContext || context.activeUMA == null || context.activeUMA.umaRoot == null)
            {
                return;
            }

            boneTreeView.Initialize("Root");

            var Global = context.activeUMA.umaRoot.transform.Find("Global");
            if (Global != null)
            {
                AddFilteredNodesRecursive(boneTreeView.RootNode, Global,0, filter);
            }
            if (boneTreeView.RootNode.children == null || boneTreeView.RootNode.children.Count ==0)
            {
                boneTreeView.RootNode.AddChild(new TreeViewItem(1,0, "No bones found"));
            }
            boneTreeView.Reload();
            boneTreeView.ExpandAll();
        }



        private void ReloadFullTree()
        {
            if (!haveValidContext || context.activeUMA == null || context.activeUMA.umaRoot == null)
            {
                return;
            }

            filtered = false;
            boneTreeView.Initialize("Root");

            var Global = context.activeUMA.umaRoot.transform.Find("Global");
            if (Global != null)
            {
                AddNodeRecursive(boneTreeView.RootNode, Global);
            }
            boneTreeView.Reload();
            ExpandDepthRecursive(boneTreeView.RootNode,5);
        }

        private void ExpandDepthRecursive(TreeViewItem theNode, int depth)
        {
            if (theNode.depth <= depth)
            {
                boneTreeView.SetExpanded(theNode.id, true);
                if (theNode.children != null)
                {
                    foreach (TreeViewItem ti in theNode.children)
                    {
                        ExpandDepthRecursive(ti, depth);
                    }
                }
            }
        }

        private void AddNodeRecursive(TreeViewItem rootNode, Transform theTransform, int depth =0)
        {
            if (theTransform == null) return;
            boneTreeView.NodeCount++;
            TreeViewItem Node = new TreeViewItem(boneTreeView.NodeCount, depth, theTransform.name);
            rootNode.AddChild(Node);
            foreach (Transform t in theTransform)
            {
                AddNodeRecursive(Node, t, depth +1);
            }
        }

        private void AddFilteredNodesRecursive(TreeViewItem rootNode, Transform theTransform, int depth =0, string Filter = "")
        {
            if (theTransform == null) return;
            boneTreeView.NodeCount++;
            string needle = (Filter ?? string.Empty).ToLowerInvariant();
            if (theTransform.name.ToLowerInvariant().Contains(needle))
            {
                TreeViewItem Node = new TreeViewItem(boneTreeView.NodeCount, depth, theTransform.name);
                rootNode.AddChild(Node);
            }
            foreach (Transform t in theTransform)
            {
                AddFilteredNodesRecursive(rootNode, t, depth +1, Filter);
            }
        }

        private void PoseBoneDrawer(SerializedProperty property)
        {
            EditorGUI.indentLevel++;
            bool allowPoseEditing = !useIKEditor;

            SerializedProperty bone = property.FindPropertyRelative("bone");
            SerializedProperty enabledProp = property.FindPropertyRelative("enabled"); // new flag
            GUIContent boneGUIContent = new GUIContent(
                bone.stringValue,
                "The name of the bone being modified by pose.");
            EditorGUILayout.BeginHorizontal();
            bone.isExpanded = EditorGUILayout.Foldout(bone.isExpanded, boneGUIContent);
            Color currentColor = GUI.color;

            bool canFocusBone = TryGetPoseBoneTransform(bone.stringValue, out Transform focusBoneTransform);
            EditorGUI.BeginDisabledGroup(!canFocusBone);
            if (GUILayout.Button("Focus", EditorStyles.miniButton, GUILayout.Width(60f)))
            {
                FocusSceneViewOnBone(focusBoneTransform);
            }
            if (poseTarget != null && poseTarget.skeleton != null && poseTarget.skeleton.HasBone(bone.stringValue))
            {
                if (GUILayout.Button("P Target", EditorStyles.miniButton, GUILayout.Width(60f)))
                {
                    var boneTarget = poseTarget.skeleton.GetBoneTransform(bone.stringValue);
                    if (boneTarget != null)
                    {
                        FocusSceneViewOnBone(boneTarget);
                    }
                }
            }
            EditorGUI.EndDisabledGroup();

            // Enable/Disable toggle button (always shown)
            bool isEnabled = enabledProp != null ? enabledProp.boolValue : true;
            GUI.color = isEnabled ? Color.white : new Color(1f,0.75f,0.75f);
            string toggleLabel = isEnabled ? "Disable" : "Enable";
            EditorGUI.BeginDisabledGroup(!allowPoseEditing);
            if (GUILayout.Button(toggleLabel, EditorStyles.miniButton, GUILayout.Width(60f)))
            {
                Undo.RecordObject(target, "Toggle Pose Bone");
                if (enabledProp != null)
                {
                    enabledProp.boolValue = !isEnabled;
                    _poseEdited = true;
                    // When disabling, reset bone transform preview (so user sees the baseline)
                    if (!enabledProp.boolValue && context?.activeUMA?.skeleton != null)
                    {
                        var skel = context.activeUMA.skeleton;
                        var posesRoot = serializedObject.FindProperty("poses");
                        if (posesRoot != null)
                        {
                            int hash = property.FindPropertyRelative("hash").intValue;
                            skel.Restore(hash);
                        }
                    }
                }
            }
            EditorGUI.EndDisabledGroup();
            GUI.color = currentColor;

            if (drawBoneIndex == editBoneIndex)
            {
                GUI.color = Color.green;
                EditorGUI.BeginDisabledGroup(!allowPoseEditing);
                if (GUILayout.Button("Editing", EditorStyles.miniButton, GUILayout.Width(60f)))
                {
                    editBoneIndex = BAD_INDEX;
                    mirrorBoneIndex = BAD_INDEX;
                }
                EditorGUI.EndDisabledGroup();
            }
            else if (drawBoneIndex == mirrorBoneIndex)
            {
                Color lightBlue = Color.Lerp(Color.blue, Color.cyan,0.66f);
                if (mirrorActive)
                {
                    GUI.color = lightBlue;
                    EditorGUI.BeginDisabledGroup(!allowPoseEditing);
                    if (GUILayout.Button("Mirroring", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        mirrorActive = false;
                    }
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    GUI.color = Color.Lerp(lightBlue, Color.white,0.66f);
                    EditorGUI.BeginDisabledGroup(!allowPoseEditing);
                    if (GUILayout.Button("Mirror", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        mirrorActive = true;
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }
            else
            {
                // Existing Reset and Edit buttons
                EditorGUI.BeginDisabledGroup(!allowPoseEditing);
                if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(60f)))
                {
                    var positionProp = property.FindPropertyRelative("position");
                    positionProp.vector3Value = Vector3.zero;
                    var rotationProp = property.FindPropertyRelative("rotation");
                    rotationProp.quaternionValue = Quaternion.identity;
                    var scaleProp = property.FindPropertyRelative("scale");
                    scaleProp.vector3Value = Vector3.one;
                    _poseEdited = true;
                }
                if (GUILayout.Button("Edit", EditorStyles.miniButton, GUILayout.Width(60f)))
                {
                    protectBonePoseSceneSelection = true;
                    SetBonePoseSelectionProtection(true);
                    editBoneIndex = drawBoneIndex;
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(32)))
                {
                    removeBoneIndex = drawBoneIndex +1;
                    doBoneRemove = true;
                }
                EditorGUI.EndDisabledGroup();
            }
            GUI.color = currentColor;
            EditorGUILayout.EndHorizontal();

            if (bone.isExpanded)
            {
                bool isEditingThisBone = (drawBoneIndex == editBoneIndex);
                bool canEditThisBone = allowPoseEditing && isEditingThisBone;
                EditorGUI.BeginDisabledGroup(!canEditThisBone);
                EditorGUI.indentLevel++;
                SerializedProperty posesRoot = serializedObject.FindProperty("poses");
                string mirrorBoneName = DeriveMirrorName(bone.stringValue);
                SerializedProperty GetOrCreateMirrorPose()
                {
                    if (disableMirroring || !mirrorActive || string.IsNullOrEmpty(mirrorBoneName) || posesRoot == null)
                        return null;
                    for (int i = 0; i < posesRoot.arraySize; i++)
                    {
                        var p = posesRoot.GetArrayElementAtIndex(i);
                        var pb = p.FindPropertyRelative("bone");
                        if (pb != null && pb.stringValue == mirrorBoneName)
                        {
                            mirrorBoneIndex = i;
                            Repaint();
                            return p;
                        }
                    }
                    AddABone(posesRoot, mirrorBoneName);
                    var newIndex = posesRoot.arraySize - 1;
                    var newPose = posesRoot.GetArrayElementAtIndex(newIndex);
                    mirrorBoneIndex = newIndex;
                    Repaint();
                    return newPose;
                }
                // Position
                int controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
                var positionProp = property.FindPropertyRelative("position");
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(positionProp, positionGUIContent);
                if (GUILayout.Button("O", EditorStyles.miniButton, GUILayout.Width(32)))
                {
                    positionProp.vector3Value = Vector3.zero;
                    _poseEdited = true;
                    var mirrorPose = GetOrCreateMirrorPose();
                    if (mirrorPose != null)
                    {
                        var mPos = mirrorPose.FindPropertyRelative("position");
                        mPos.vector3Value = MirrorPositionOnly(Vector3.zero);
                    }
                }
                EditorGUILayout.EndHorizontal();
                int controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
                if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
                {
                    if (context != null)
                        context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Position;
                }
                if (EditorGUI.EndChangeCheck() && isEditingThisBone)
                {
                    _poseEdited = true;
                    var mirrorPose = GetOrCreateMirrorPose();
                    if (mirrorPose != null)
                    {
                        var mPos = mirrorPose.FindPropertyRelative("position");
                        mPos.vector3Value = MirrorPositionOnly(positionProp.vector3Value);
                    }
                }
                // Rotation
                var rotation = property.FindPropertyRelative("rotation");
                Rect rotationRect = new Rect(0, 0, 0, 0);
                EditorGUI.BeginProperty(rotationRect, GUIContent.none, rotation);
                Vector3 currentRotationEuler = ((Quaternion)rotation.quaternionValue).eulerAngles;
                Vector3 newRotationEuler = currentRotationEuler;
                EditorGUI.BeginChangeCheck();
                controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
                EditorGUILayout.BeginHorizontal();
                newRotationEuler = EditorGUILayout.Vector3Field(rotationGUIContent, newRotationEuler);
                if (GUILayout.Button("O", EditorStyles.miniButton, GUILayout.Width(32)))
                {
                    rotation.quaternionValue = Quaternion.identity;
                    _poseEdited = true;
                    if (isEditingThisBone)
                    {
                        var mirrorPose = GetOrCreateMirrorPose();
                        if (mirrorPose != null)
                        {
                            var mRot = mirrorPose.FindPropertyRelative("rotation");
                            mRot.quaternionValue = MirrorRotationOnly(Quaternion.identity);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
                if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
                {
                    if (context != null)
                        context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Rotation;
                }
                if (EditorGUI.EndChangeCheck() && canEditThisBone)
                {
                    if (newRotationEuler != currentRotationEuler)
                    {
                        rotation.quaternionValue = Quaternion.Euler(newRotationEuler);
                        _poseEdited = true;
                        if (isEditingThisBone)
                        {
                            var mirrorPose = GetOrCreateMirrorPose();
                            if (mirrorPose != null)
                            {
                                var mRot = mirrorPose.FindPropertyRelative("rotation");
                                mRot.quaternionValue = MirrorRotationOnly(rotation.quaternionValue);
                            }
                        }
                    }
                }
                EditorGUI.EndProperty();
                // Scale
                var scaleProperty = property.FindPropertyRelative("scale");
                controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(scaleProperty, scaleGUIContent);
                if (GUILayout.Button("O", EditorStyles.miniButton, GUILayout.Width(32)))
                {
                    scaleProperty.vector3Value = Vector3.one;
                    _poseEdited = true;
                    var mirrorPose = GetOrCreateMirrorPose();
                    if (mirrorPose != null)
                    {
                        var mScale = mirrorPose.FindPropertyRelative("scale");
                        mScale.vector3Value = Vector3.one;
                    }
                }
                EditorGUILayout.EndHorizontal();
                controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
                if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
                {
                    if (context != null)
                        context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Scale;
                }
                if (EditorGUI.EndChangeCheck() && isEditingThisBone)
                {
                    _poseEdited = true;
                    var mirrorPose = GetOrCreateMirrorPose();
                    if (mirrorPose != null)
                    {
                        var mScale = mirrorPose.FindPropertyRelative("scale");
                        mScale.vector3Value = scaleProperty.vector3Value;
                    }
                }
                Vector3 scaleValue = scaleProperty.vector3Value;
                if (!Mathf.Approximately(scaleValue.x, scaleValue.y) || !Mathf.Approximately(scaleValue.y, scaleValue.z))
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(EditorGUIUtility.labelWidth / 2f);
                    if (warningIcon != null)
                    {
                        scaleWarningGUIContent.image = warningIcon;
                        EditorGUILayout.LabelField(scaleWarningGUIContent, GUILayout.MinHeight(warningIcon.height + 4f));
                    }
                    else
                    {
                        EditorGUILayout.LabelField(scaleWarningGUIContent);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();
            }
            EditorGUI.indentLevel--;
        }

        // Restore pose on sourceUMA (if edited) and rebuild on exit.
        private void TryRestoreAndRebuildOnExit()
        {
            bool needsRestore = _sourcePreviewModified || BonePoseSavers.Count >0;
            if (sourceUMA == null)
            {
                if (needsRestore)
                {
                    RestorePreviewOverride();
                }
                _sourcePreviewModified = false;
                _poseEdited = false;
                return;
            }

            try
            {
                if (true)
                {
                    if (needsRestore)
                    {
                        RestorePreviewOverride();
                        if (!BuildSourceAvatarIfAvailable(sourceUMA) && context != null && context.activeUMA == sourceUMA)
                        {
                            ApplySourceSkeletonPreview(false, true);
                        }
                    }

                    // Trigger full rebuild
                    if (_poseEdited)
                    {
                        var uma = sourceUMA;
                        if (!IsEditorBusy)
                        {
                            uma.Dirty(true, true, true);
                            UMAAssetIndexer.Instance.generator.Work();
                        }
                        else
                        {
                            EditorApplication.delayCall += () =>
                            {
                                if (uma != null)
                                {
                                    uma.Dirty(true, true, true);
                                    UMAAssetIndexer.Instance.generator.Work();
                                }
                            };
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _poseEdited = false;
                _sourcePreviewModified = false;
            }
        }
    }
}