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
        public static int MirrorAxis =0;
        public static string[] MirrorAxises = { "X Axis", "Y Axis (Legacy UMA)", "Z Axis" };
        public static int displayMode =0;
        public static string[] strings = { "Pose Bones", "Filtered", "All", "None" };
        public enum DisplayMode { PoseBones, Filtered, All, None };
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
                ClearActiveEditState();
                return;
            }

            Debug.Log("Applying T-Pose preview mode to source UMA: " + sourceUMA.name);
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

        private void DoSceneGUI(SceneView scene)
        {
            if (IsEditorBusy) { DrawSkeletonBones(); return; }
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
            bool allowPoseEditing = true;
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
 
            if (haveValidContext && !disableMirroring && context.mirrorPlane == UMABonePoseEditorContext.MirrorPlane.Mirror_None)
            {
                EditorGUILayout.HelpBox("Mirroring plane not detected; Mirror Axis still controls mirrored pose deltas when a mirror pose is updated.", MessageType.Info);
            }
 
 GUILayout.BeginHorizontal();

            if (GUILayout.Button("Find UMA in scene"))
            {
                UMAData data = GameObject.FindFirstObjectByType<UMAData>();
                if (data != null)
                {
                    UMAData previousSource = saveUMAData;
                    sourceUMA = data;
                    saveUMAData = data;

                    ApplySourcePreviewMode(previousSource,true);
                    var active = Selection.activeObject;

                    Selection.activeGameObject = data.gameObject;
                    SceneView.FrameLastActiveSceneView();

                    Selection.activeObject = active;

                }
            }
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

                filter = GUILayout.TextField(filter);
                if (GUILayout.Button("Filter", GUILayout.Width(80)))
                {
                    ReloadFilteredTree();
                }
                if (GUILayout.Button("Clear", GUILayout.Width(80)))
                {
                    filter = "";
                    ReloadFullTree();
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
            bool allowPoseEditing = true;

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
                    editBoneIndex = drawBoneIndex;
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