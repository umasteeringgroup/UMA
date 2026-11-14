using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    /// <summary>
    /// UMA bone animator asset that adds <see cref="UMAChainJiggle"/> chains to a generated
    /// character. Create via Assets &gt; Create &gt; UMA &gt; Physics &gt; UMAChainJiggleAnimator and
    /// add it to a SlotDataAsset's bone animator list to make ponytails, pigtails, tails and
    /// soft anatomy sway and bounce.
    ///
    /// The asset only describes the chains. Each character gets its own UMAChainJiggle
    /// components, which simulate themselves after animation, so one asset can be shared by
    /// any number of characters.
    /// </summary>
    public class UMAChainJiggleAnimator : BaseUpdatedObject
    {
        [System.Serializable]
        public class ChainDefinition
        {
            [Tooltip("Name of the first bone of this chain. This bone and all its children are simulated; the bone itself stays attached to its parent.")]
            public string AnchorBoneName;

            [Tooltip("Optional bone names to exclude from this chain. Excluded bones and their children are skipped.")]
            public List<string> ExcludedBoneNames = new List<string>();
        }

        [System.Serializable]
        public class ColliderDefinition
        {
            [Tooltip("Name of the bone the collider follows, for example Head, Neck or LeftShoulder.")]
            public string BoneName;

            [Tooltip("Centre of the collider in the bone's local space.")]
            public Vector3 Center = Vector3.zero;

            [Min(0f)]
            [Tooltip("Radius in metres.")]
            public float Radius = 0.08f;

            [Min(0f)]
            [Tooltip("Total height of a capsule in metres. 0 is a sphere.")]
            public float Height = 0f;

            [Tooltip("Local axis a capsule runs along.")]
            public UMAChainJiggleCollider.Direction Direction = UMAChainJiggleCollider.Direction.Y;
        }

#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/UMAChainJiggleAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<UMAChainJiggleAnimator>();
        }
#endif

        [Header("Chains")]
        [Tooltip("One entry per independent chain. Use one entry for a ponytail, or one entry per side for pigtails and anatomy.")]
        public List<ChainDefinition> Chains = new List<ChainDefinition>();

        [Tooltip("Optional spheres and capsules the chains cannot pass through.")]
        public List<ColliderDefinition> Colliders = new List<ColliderDefinition>();

        [Tooltip("Race names this animator leaves alone. Use it when several races share one base recipe and only some of them should move.")]
        public List<string> ExcludedRaces = new List<string>();

        public UMAChainJiggleSettings settings = new UMAChainJiggleSettings();

        /// <summary>True when <see cref="ExcludedRaces"/> names the race of this character.</summary>
        public bool IsExcluded(UMAData umaData)
        {
            if (ExcludedRaces == null || ExcludedRaces.Count == 0 || umaData == null || umaData.umaRecipe == null || umaData.umaRecipe.raceData == null)
            {
                return false;
            }

            return ExcludedRaces.Contains(umaData.umaRecipe.raceData.raceName);
        }

        public override void Initialize(UMAData umaData, SlotData sd)
        {
            base.Initialize(umaData, sd);

            if (IsExcluded(umaData))
            {
                // Nothing to add for this race. A chain left over from an earlier build of the
                // same character notices it was not set up again and removes itself.
                initialized = true;
                return;
            }

            int chainCount = 0;
            if (Chains != null && umaData != null && umaData.skeleton != null)
            {
                List<UMAChainJiggleCollider> colliders = ResolveColliders(umaData);
                for (int i = 0; i < Chains.Count; i++)
                {
                    if (AddChainJiggle(umaData, Chains[i], colliders))
                    {
                        chainCount++;
                    }
                }
            }

            initialized = chainCount > 0;
        }

        private bool AddChainJiggle(UMAData umaData, ChainDefinition chain, List<UMAChainJiggleCollider> colliders)
        {
            if (chain == null || string.IsNullOrEmpty(chain.AnchorBoneName))
            {
                return false;
            }

            Transform anchorTransform = umaData.skeleton.GetBoneTransform(chain.AnchorBoneName);
            if (anchorTransform == null)
            {
                Debug.LogError($"[UMAChainJiggleAnimator] '{name}': anchor bone '{chain.AnchorBoneName}' not found in skeleton.", this);
                return false;
            }

            UMAChainJiggle chainJiggle = anchorTransform.GetComponent<UMAChainJiggle>();
            if (chainJiggle == null)
            {
                chainJiggle = anchorTransform.gameObject.AddComponent<UMAChainJiggle>();
            }

            chainJiggle.Setup(anchorTransform, settings, ResolveExclusions(umaData, chain), colliders, umaData);
            chainJiggle.enabled = true;
            return true;
        }

        private List<UMAChainJiggleCollider> ResolveColliders(UMAData umaData)
        {
            if (Colliders == null || Colliders.Count == 0)
            {
                return null;
            }

            List<UMAChainJiggleCollider> colliders = new List<UMAChainJiggleCollider>(Colliders.Count);
            for (int i = 0; i < Colliders.Count; i++)
            {
                ColliderDefinition definition = Colliders[i];
                if (definition == null || string.IsNullOrEmpty(definition.BoneName))
                {
                    continue;
                }

                Transform bone = umaData.skeleton.GetBoneTransform(definition.BoneName);
                if (bone == null)
                {
                    Debug.LogWarning($"[UMAChainJiggleAnimator] '{name}': collider bone '{definition.BoneName}' not found in skeleton.", this);
                    continue;
                }

                colliders.Add(new UMAChainJiggleCollider
                {
                    bone = bone,
                    center = definition.Center,
                    radius = definition.Radius,
                    height = definition.Height,
                    direction = definition.Direction
                });
            }

            return colliders;
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
                    Debug.LogWarning($"[UMAChainJiggleAnimator] '{name}': excluded bone '{boneName}' not found in skeleton for chain '{chain.AnchorBoneName}'.", this);
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
            // Nothing to do here. The physics loop runs before animation and at its own rate,
            // which made the chains stutter; each UMAChainJiggle steps itself in LateUpdate.
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
