using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    /// <summary>
    /// UMA bone animator asset that drives a UMAChainJiggle component at runtime.
    /// Create via Assets &gt; Create &gt; UMA &gt; Physics &gt; UMAChainJiggleAnimator.
    /// Assign to a SlotDataAsset's BoneAnimator list to make ponytails, tails,
    /// ropes, skirts, and hanging clothing bones sway.
    /// </summary>
    public class UMAChainJiggleAnimator : BaseUpdatedObject
    {
        [System.Serializable]
        public class ChainDefinition
        {
            [Tooltip("Name of the root bone for this chain. This bone and all its children will be animated.")]
            public string AnchorBoneName;

            [Tooltip("Optional bone names to exclude from this chain. Excluded bones and their children are skipped.")]
            public List<string> ExcludedBoneNames = new List<string>();
        }

#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/UMAChainJiggleAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<UMAChainJiggleAnimator>();
        }
#endif

        [Header("Chains")]
        [Tooltip("One entry per independent chain. Use one entry for a single chain, or one entry per side for pigtails.")]
        public List<ChainDefinition> Chains = new List<ChainDefinition>();

        [Header("Terminal Bones")]
        [Tooltip("Virtual child length added to leaf bones so the last real bone can rotate.")]
        public float endLength = 0.15f;

        [Tooltip("Explicit local offset for virtual leaf children. Zero follows the parent-to-leaf direction.")]
        public Vector3 endOffset = Vector3.zero;

        [Header("Physics")]
        [Range(0f, 1f)]
        [Tooltip("Spring strength pulling particles back to their animated/rest pose.")]
        public float stiffness = 0.15f;

        [Range(0.001f, 5f)]
        [Tooltip("Resistance to acceleration.")]
        public float mass = 0.9f;

        [Range(0f, 1f)]
        [Tooltip("Velocity damping. Higher values settle faster.")]
        public float damping = 0.15f;

        [Range(0f, 2f)]
        [Tooltip("Downward world-space acceleration.")]
        public float gravity = 0.1f;

        [Range(0f, 5f)]
        [Tooltip("How much root/parent movement pushes particles in the opposite direction. Values >1 allow exaggerated lag.")]
        public float inertia = 0.65f;

        [Range(0f, 25f)]
        [Tooltip("Global motion multiplier. Scales inertia response and gravity. Increase for more swing. Tunable at runtime.")]
        public float forceMultiplier = 15f;

        [Tooltip("Maximum world-space distance each particle can move from its rest target.")]
        public float maxDistance = 0.35f;

        [Range(1, 8)]
        [Tooltip("Number of length-constraint passes. Longer chains usually need 2-4.")]
        public int constraintIterations = 3;

        [Header("Smoothing")]
        [Range(0f, 1f)]
        [Tooltip("Low-pass for parent motion delta. Higher=smoother.")]
        public float targetSmoothing = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Low-pass for final bone rotation.")]
        public float rotationSmoothing = 0.5f;

        [Range(0f, 20f)]
        [Tooltip("Caps velocity to prevent snapping. 0=disabled.")]
        public float maxVelocity = 5f;

        [Header("Bone Output")]
        [Range(0f, 1f)]
        [Tooltip("How much bones rotate toward simulated child particles. Use 1 for normal chains.")]
        public float rotationWeight = 1f;

        [Range(0f, 1f)]
        [Tooltip("Optional direct joint translation. Use 0 for normal skinned bone chains.")]
        public float positionWeight = 0f;

        [Header("Freeze Axes")]
        [Tooltip("Freeze movement on X axis in world space.")]
        public bool freezeX;

        [Tooltip("Freeze movement on Y axis in world space.")]
        public bool freezeY;

        [Tooltip("Freeze movement on Z axis in world space.")]
        public bool freezeZ;

        private readonly List<UMAChainJiggle> _chainJiggles = new List<UMAChainJiggle>();

        public override void Initialize(UMAData umaData, SlotData sd)
        {
            base.Initialize(umaData, sd);

            _chainJiggles.Clear();

            if (Chains != null)
            {
                for (int i = 0; i < Chains.Count; i++)
                {
                    AddChainJiggle(umaData, Chains[i]);
                }
            }

            initialized = _chainJiggles.Count > 0;
        }

        private void AddChainJiggle(UMAData umaData, ChainDefinition chain)
        {
            if (umaData == null || umaData.skeleton == null || chain == null || string.IsNullOrEmpty(chain.AnchorBoneName))
            {
                return;
            }

            Transform anchorTransform = umaData.skeleton.GetBoneTransform(chain.AnchorBoneName);
            if (anchorTransform == null)
            {
                Debug.LogError($"[UMAChainJiggleAnimator] Anchor bone '{chain.AnchorBoneName}' not found in skeleton.");
                return;
            }

            UMAChainJiggle chainJiggle = anchorTransform.GetComponent<UMAChainJiggle>();
            if (chainJiggle == null)
            {
                chainJiggle = anchorTransform.gameObject.AddComponent<UMAChainJiggle>();
            }

            List<Transform> exclusions = ResolveExclusions(umaData, chain);
            chainJiggle.Setup(
                chainRoot: anchorTransform,
                stiffnessValue: stiffness,
                massValue: mass,
                dampingValue: damping,
                gravityValue: gravity,
                inertiaValue: inertia,
                maxDistanceValue: maxDistance,
                constraintIterationsValue: constraintIterations,
                rotationWeightValue: rotationWeight,
                positionWeightValue: positionWeight,
                endLengthValue: endLength,
                endOffsetValue: endOffset,
                freezeXValue: freezeX,
                freezeYValue: freezeY,
                freezeZValue: freezeZ,
                exclusionTransforms: exclusions,
                forceMultiplierValue: forceMultiplier,
                targetSmoothingValue: targetSmoothing,
                rotationSmoothingValue: rotationSmoothing,
                maxVelocityValue: maxVelocity);

            if (!_chainJiggles.Contains(chainJiggle))
            {
                _chainJiggles.Add(chainJiggle);
            }
        }

        private List<Transform> ResolveExclusions(UMAData umaData, ChainDefinition chain)
        {
            List<Transform> exclusions = null;
            if (chain.ExcludedBoneNames == null || chain.ExcludedBoneNames.Count == 0)
            {
                return exclusions;
            }

            for (int i = 0; i < chain.ExcludedBoneNames.Count; i++)
            {
                string boneName = chain.ExcludedBoneNames[i];
                if (string.IsNullOrEmpty(boneName))
                {
                    continue;
                }

                Transform exclusion = umaData.skeleton.GetBoneTransform(boneName);
                if (exclusion == null)
                {
                    Debug.LogWarning($"[UMAChainJiggleAnimator] Excluded bone '{boneName}' not found in skeleton for chain '{chain.AnchorBoneName}'.");
                    continue;
                }

                if (exclusions == null)
                {
                    exclusions = new List<Transform>();
                }
                exclusions.Add(exclusion);
            }

            return exclusions;
        }

        public override void DoUpdate(UMAData umaData, float step)
        {
            if (!initialized)
            {
                return;
            }

            for (int i = 0; i < _chainJiggles.Count; i++)
            {
                UMAChainJiggle chainJiggle = _chainJiggles[i];
                if (chainJiggle != null)
                {
                    chainJiggle.DoSimulateStep(step);
                }
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(UMAChainJiggleAnimator))]
    public class UMAChainJiggleAnimatorEditor : Editor
    {
        private SlotDataAsset _slotDataAsset;
        private string[] _boneNames = new string[0];
        private int _selectedBoneIndex;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add Chain From Slot", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _slotDataAsset = (SlotDataAsset)EditorGUILayout.ObjectField("Slot Data Asset", _slotDataAsset, typeof(SlotDataAsset), false);
            DrawSlotDropArea();
            if (EditorGUI.EndChangeCheck())
            {
                RebuildBoneNames();
            }

            if (_slotDataAsset == null)
            {
                EditorGUILayout.HelpBox("Drop or assign a SlotDataAsset to choose an anchor bone from its mesh bones.", MessageType.Info);
                return;
            }

            if (_boneNames == null || _boneNames.Length == 0)
            {
                EditorGUILayout.HelpBox("The selected SlotDataAsset has no UMA bone data.", MessageType.Warning);
                return;
            }

            _selectedBoneIndex = Mathf.Clamp(_selectedBoneIndex, 0, _boneNames.Length - 1);
            _selectedBoneIndex = EditorGUILayout.Popup("Anchor Bone", _selectedBoneIndex, _boneNames);

            if (GUILayout.Button("Add a chain for this bone"))
            {
                AddChainForSelectedBone();
            }
        }

        private void DrawSlotDropArea()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drop SlotDataAsset Here", EditorStyles.helpBox);

            Event currentEvent = Event.current;
            if (!dropRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
            {
                return;
            }

            SlotDataAsset droppedSlot = GetDraggedSlotDataAsset();
            DragAndDrop.visualMode = droppedSlot != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (currentEvent.type == EventType.DragPerform && droppedSlot != null)
            {
                DragAndDrop.AcceptDrag();
                _slotDataAsset = droppedSlot;
                RebuildBoneNames();
                GUI.changed = true;
            }

            currentEvent.Use();
        }

        private SlotDataAsset GetDraggedSlotDataAsset()
        {
            Object[] objectReferences = DragAndDrop.objectReferences;
            for (int i = 0; i < objectReferences.Length; i++)
            {
                if (objectReferences[i] is SlotDataAsset slotDataAsset)
                {
                    return slotDataAsset;
                }
            }

            return null;
        }

        private void RebuildBoneNames()
        {
            _selectedBoneIndex = 0;
            if (_slotDataAsset == null || UMAMeshData.IsNullOrEmptyMeshData(_slotDataAsset.meshData) || _slotDataAsset.meshData.umaBones == null)
            {
                _boneNames = new string[0];
                return;
            }

            List<string> boneNames = new List<string>();
            UMATransform[] umaBones = _slotDataAsset.meshData.umaBones;
            for (int i = 0; i < umaBones.Length; i++)
            {
                UMATransform bone = umaBones[i];
                if (bone == null || string.IsNullOrEmpty(bone.name) || boneNames.Contains(bone.name))
                {
                    continue;
                }

                boneNames.Add(bone.name);
            }

            boneNames.Sort();
            _boneNames = boneNames.ToArray();
        }

        private void AddChainForSelectedBone()
        {
            UMAChainJiggleAnimator animator = (UMAChainJiggleAnimator)target;
            string boneName = _boneNames[_selectedBoneIndex];

            if (animator.Chains == null)
            {
                animator.Chains = new List<UMAChainJiggleAnimator.ChainDefinition>();
            }

            for (int i = 0; i < animator.Chains.Count; i++)
            {
                UMAChainJiggleAnimator.ChainDefinition chain = animator.Chains[i];
                if (chain != null && chain.AnchorBoneName == boneName)
                {
                    EditorUtility.DisplayDialog("Chain Already Exists", $"A chain for '{boneName}' already exists.", "OK");
                    return;
                }
            }

            Undo.RecordObject(animator, "Add UMA Chain Jiggle Chain");
            animator.Chains.Add(new UMAChainJiggleAnimator.ChainDefinition { AnchorBoneName = boneName });
            EditorUtility.SetDirty(animator);
            serializedObject.Update();
        }
    }
#endif
}
