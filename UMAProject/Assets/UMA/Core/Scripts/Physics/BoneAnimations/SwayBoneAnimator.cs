using System.Collections.Generic;
using UMA;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    /// <summary>
    /// Lightweight bone-chain sway animator for ponytails, hair, and other short bone chains.
    /// A single asset can drive multiple independent chains (e.g. left/right pigtails) by
    /// adding entries to the Chains list. When Chains is empty, the legacy single
    /// AnchorBoneName field is used as a fallback for backwards compatibility.
    /// </summary>
    public class SwayBoneAnimator : BaseUpdatedObject
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
        [MenuItem("Assets/Create/UMA/Physics/SwayBoneAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<SwayBoneAnimator>();
        }
#endif

        [Header("Chains (multi-chain mode)")]
        [Tooltip("One entry per independent sway chain. When this list has entries, the legacy AnchorBoneName field below is ignored. Leave empty to use the single-chain fallback.")]
        public List<ChainDefinition> Chains = new List<ChainDefinition>();

        [Header("Legacy (single-chain mode)")]
        [Tooltip("The name of the root bone for this animator. Used only when the Chains list above is empty.")]
        public string AnchorBoneName;

        [Header("Physics")]
        [Range(0.0f, 1.0f)]
        [Tooltip("How much inertia each bone has — makes it more bouncy.")]
        public float inertia = 0.75f;

        [Range(1.0f, 2.0f)]
        [Tooltip("How far something can stretch — 1.0 = no stretch.")]
        public float limit = 2.0f;

        [Range(1.0f, 4.0f)]
        [Tooltip("How much it can pull away during movement.")]
        public float elasticity = 2.0f;

        private readonly List<SwayRootBone> _swayRootBones = new List<SwayRootBone>();

        public override void Initialize(UMAData umaData, SlotData sd)
        {
            base.Initialize(umaData, sd);

            _swayRootBones.Clear();

            if (Chains != null && Chains.Count > 0)
            {
                for (int i = 0; i < Chains.Count; i++)
                {
                    AddSwayChain(umaData, Chains[i]);
                }
            }
            else if (!string.IsNullOrEmpty(AnchorBoneName))
            {
                // Legacy single-chain fallback
                AddSwayChain(umaData, new ChainDefinition { AnchorBoneName = AnchorBoneName });
            }
            else
            {
                Debug.LogError("[SwayBoneAnimator] No chains configured and no legacy AnchorBoneName set. Nothing to initialize.");
                return;
            }

            initialized = _swayRootBones.Count > 0;
        }

        private void AddSwayChain(UMAData umaData, ChainDefinition chain)
        {
            if (umaData == null || umaData.skeleton == null || chain == null || string.IsNullOrEmpty(chain.AnchorBoneName))
            {
                return;
            }

            Transform anchorTransform = umaData.skeleton.GetBoneTransform(chain.AnchorBoneName);
            if (anchorTransform == null)
            {
                Debug.LogError($"[SwayBoneAnimator] Anchor bone '{chain.AnchorBoneName}' not found in UMA skeleton.");
                return;
            }

            SwayRootBone swayRootBone = anchorTransform.GetComponent<SwayRootBone>();
            if (swayRootBone == null)
            {
                swayRootBone = anchorTransform.gameObject.AddComponent<SwayRootBone>();
            }

            // Apply exclusion transforms from bone names
            if (chain.ExcludedBoneNames != null && chain.ExcludedBoneNames.Count > 0)
            {
                if (swayRootBone.Exclusions == null)
                {
                    swayRootBone.Exclusions = new List<Transform>();
                }
                else
                {
                    swayRootBone.Exclusions.Clear();
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
                        Debug.LogWarning($"[SwayBoneAnimator] Excluded bone '{boneName}' not found in skeleton for chain '{chain.AnchorBoneName}'.");
                        continue;
                    }

                    swayRootBone.Exclusions.Add(exclusion);
                }
            }

            swayRootBone.Setup(elasticity, inertia, limit);
            swayRootBone.enabled = true;

            if (!_swayRootBones.Contains(swayRootBone))
            {
                _swayRootBones.Add(swayRootBone);
            }
        }

        public override void DoUpdate(UMAData umaData, float step)
        {
            if (!initialized)
            {
                return;
            }

            for (int i = 0; i < _swayRootBones.Count; i++)
            {
                SwayRootBone swayRootBone = _swayRootBones[i];
                if (swayRootBone != null)
                {
                    swayRootBone.UpdateRootBone(step);
                }
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(SwayBoneAnimator))]
    public class SwayBoneAnimatorEditor : Editor
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
            SwayBoneAnimator animator = (SwayBoneAnimator)target;
            string boneName = _boneNames[_selectedBoneIndex];

            if (animator.Chains == null)
            {
                animator.Chains = new List<SwayBoneAnimator.ChainDefinition>();
            }

            for (int i = 0; i < animator.Chains.Count; i++)
            {
                SwayBoneAnimator.ChainDefinition chain = animator.Chains[i];
                if (chain != null && chain.AnchorBoneName == boneName)
                {
                    EditorUtility.DisplayDialog("Chain Already Exists", $"A chain for '{boneName}' already exists.", "OK");
                    return;
                }
            }

            Undo.RecordObject(animator, "Add Sway Bone Chain");
            animator.Chains.Add(new SwayBoneAnimator.ChainDefinition { AnchorBoneName = boneName });
            EditorUtility.SetDirty(animator);
            serializedObject.Update();
        }
    }
#endif
}