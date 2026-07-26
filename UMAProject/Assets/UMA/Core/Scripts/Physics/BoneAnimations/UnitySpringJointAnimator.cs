using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    /// <summary>
    /// Builds Unity SpringJoint chains directly on an UMA skeleton.
    /// Components created by this asset are tagged so repeated UMA builds can reuse
    /// them without modifying artist-authored rigidbodies or colliders.
    /// </summary>
    public class UnitySpringJointAnimator : BaseUpdatedObject
    {
        [Serializable]
        public class ChainDefinition
        {
            [Tooltip("The root of this spring chain. Descendants are animated unless Spring Bone Names is populated.")]
            public string AnchorBoneName;

            [Tooltip("Optional ordered list of spring bones. Leave empty to use all registered descendants of the anchor.")]
            public List<string> SpringBoneNames = new List<string>();

            [Tooltip("Bones to omit during automatic discovery. An excluded bone's entire subtree is skipped.")]
            public List<string> ExcludedBoneNames = new List<string>();
        }

        private struct BoneLink
        {
            public Transform Bone;
            public Transform Parent;

            public BoneLink(Transform bone, Transform parent)
            {
                Bone = bone;
                Parent = parent;
            }
        }

#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/UnitySpringJointAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<UnitySpringJointAnimator>();
        }
#endif

        [Header("Chains (multi-chain mode)")]
        [Tooltip("One entry per independent chain. When populated, the legacy fields below are ignored.")]
        public List<ChainDefinition> Chains = new List<ChainDefinition>();

        [Header("Legacy (single-chain mode)")]
        [Tooltip("Root bone used when Chains is empty.")]
        public string AnchorBoneName;

        [Tooltip("Optional ordered spring bones used with the legacy anchor. Leave empty to animate all registered descendants.")]
        public List<string> SwingBoneNames = new List<string>();

        [Header("Automatic Chain Discovery")]
        [Min(0)]
        [Tooltip("Maximum hierarchy depth below an anchor. Zero includes the complete descendant hierarchy.")]
        public int MaxDepth;

        [Tooltip("Only add physics to transforms registered in the UMA skeleton.")]
        public bool RegisteredBonesOnly = true;

        [Header("Spring Joint")]
        [Min(0f)]
        [Tooltip("Force used to return each bone to its initial distance from the preceding bone.")]
        public float Spring = 50f;

        [Min(0f)]
        [Tooltip("Resistance applied to spring oscillation.")]
        public float Damper = 5f;

        [Min(0f)]
        [Tooltip("Minimum-distance offset Unity applies relative to the segment's initial distance.")]
        public float MinDistance;

        [Min(0f)]
        [Tooltip("Maximum-distance offset Unity applies relative to the segment's initial distance.")]
        public float MaxDistance;

        [Min(0f)]
        [Tooltip("Maximum distance error tolerated by the joint solver.")]
        public float Tolerance = 0.025f;

        [Tooltip("Allow adjacent bodies in a chain to collide.")]
        public bool EnableConnectedBodyCollision;

        [Tooltip("Enable Unity's joint preprocessing.")]
        public bool EnablePreprocessing = true;

        [Header("Rigidbodies")]
        [Min(0.0001f)]
        public float BoneMass = 0.1f;

        [Min(0f)]
        public float LinearDamping = 0.15f;

        [Min(0f)]
        public float AngularDamping = 0.15f;

        public bool UseGravity = true;
        public bool Interpolate = true;
        public CollisionDetectionMode CollisionDetection = CollisionDetectionMode.Discrete;
        public RigidbodyConstraints BoneConstraints = RigidbodyConstraints.None;

        [Min(0f)]
        [Tooltip("Zero leaves Unity's current project default unchanged.")]
        public float MaxAngularVelocity = 20f;

        [Min(0f)]
        [Tooltip("Zero leaves Unity's current project default unchanged.")]
        public float MaxDepenetrationVelocity = 3f;

        [Header("Colliders")]
        [Tooltip("Add owned sphere colliders to animated bones. Existing colliders are not changed.")]
        public bool AddBoneColliders;

        [Min(0.0001f)]
        public float BoneColliderRadius = 0.025f;

        [Tooltip("Add owned sphere colliders to fixed chain anchors. Existing colliders are not changed.")]
        public bool AddAnchorColliders;

        [Min(0.0001f)]
        public float AnchorColliderRadius = 0.04f;

        public Vector3 AnchorColliderCenter = Vector3.zero;

        [Range(-1, 31)]
        [Tooltip("Layer assigned to configured bones. Use -1 to preserve their layers.")]
        public int BoneLayer = -1;

        public override void Initialize(UMAData umaData, SlotData sd)
        {
            base.Initialize(umaData, sd);
            initialized = false;
            wasWarned = false;

            if (umaData == null || umaData.skeleton == null)
            {
                Debug.LogError("[UnitySpringJointAnimator] Cannot initialize without an UMAData skeleton.");
                return;
            }

            List<ChainDefinition> configuredChains = GetConfiguredChains();
            if (configuredChains.Count == 0)
            {
                Debug.LogError("[UnitySpringJointAnimator] No spring chains are configured.", this);
                CleanupStaleMarkers(umaData, new HashSet<UnitySpringJointAnimatorBone>());
                return;
            }

            var anchors = new HashSet<Transform>();
            var linksByBone = new Dictionary<Transform, BoneLink>();
            for (int i = 0; i < configuredChains.Count; i++)
            {
                ResolveChain(umaData, configuredChains[i], anchors, linksByBone);
            }

            if (linksByBone.Count == 0)
            {
                Debug.LogWarning("[UnitySpringJointAnimator] No valid spring bones were found below the configured anchors.", this);
                CleanupStaleMarkers(umaData, new HashSet<UnitySpringJointAnimatorBone>());
                return;
            }

            // A chain root can itself be a child of another chain. In that case it
            // remains dynamic while serving as the connection point for its children.
            var dynamicBones = new HashSet<Transform>(linksByBone.Keys);
            var allBodies = new HashSet<Transform>(anchors);
            foreach (KeyValuePair<Transform, BoneLink> entry in linksByBone)
            {
                allBodies.Add(entry.Key);
                allBodies.Add(entry.Value.Parent);
            }

            var desiredMarkers = new HashSet<UnitySpringJointAnimatorBone>();
            var bodies = new Dictionary<Transform, Rigidbody>();

            foreach (Transform bone in allBodies)
            {
                if (bone == null)
                {
                    continue;
                }

                bool isDynamic = dynamicBones.Contains(bone);
                UnitySpringJointAnimatorBone marker = GetOrCreateMarker(bone, umaData);
                Rigidbody body = GetOrCreateRigidbody(marker, isDynamic);
                if (body == null)
                {
                    continue;
                }

                if (!isDynamic && marker.OwnedJoint != null)
                {
                    DestroyOwnedObject(marker.OwnedJoint);
                    marker.OwnedJoint = null;
                }

                ConfigureCollider(marker, isDynamic);
                ApplyLayer(marker);
                desiredMarkers.Add(marker);
                bodies[bone] = body;
            }

            int jointCount = 0;
            foreach (KeyValuePair<Transform, BoneLink> entry in linksByBone)
            {
                BoneLink link = entry.Value;
                if (!bodies.TryGetValue(link.Bone, out Rigidbody body) ||
                    !bodies.TryGetValue(link.Parent, out Rigidbody connectedBody))
                {
                    continue;
                }

                UnitySpringJointAnimatorBone marker = FindMarker(link.Bone, umaData);
                if (marker == null)
                {
                    continue;
                }

                SpringJoint joint = marker.OwnedJoint;
                if (joint == null)
                {
                    joint = link.Bone.gameObject.AddComponent<SpringJoint>();
                    marker.OwnedJoint = joint;
                }

                ConfigureJoint(joint, connectedBody);
                desiredMarkers.Add(marker);
                jointCount++;
            }

            CleanupStaleMarkers(umaData, desiredMarkers);
            initialized = jointCount > 0;
        }

        private List<ChainDefinition> GetConfiguredChains()
        {
            if (Chains != null && Chains.Count > 0)
            {
                return Chains;
            }

            var legacyChains = new List<ChainDefinition>();
            if (!string.IsNullOrWhiteSpace(AnchorBoneName))
            {
                legacyChains.Add(new ChainDefinition
                {
                    AnchorBoneName = AnchorBoneName,
                    SpringBoneNames = SwingBoneNames != null
                        ? new List<string>(SwingBoneNames)
                        : new List<string>()
                });
            }

            return legacyChains;
        }

        private void ResolveChain(
            UMAData data,
            ChainDefinition chain,
            HashSet<Transform> anchors,
            Dictionary<Transform, BoneLink> linksByBone)
        {
            if (chain == null || string.IsNullOrWhiteSpace(chain.AnchorBoneName))
            {
                return;
            }

            Transform anchor = data.skeleton.GetBoneTransform(chain.AnchorBoneName);
            if (anchor == null)
            {
                Debug.LogError(
                    $"[UnitySpringJointAnimator] Anchor bone '{chain.AnchorBoneName}' was not found in the UMA skeleton.",
                    this);
                return;
            }

            int countBefore = linksByBone.Count;
            anchors.Add(anchor);

            if (chain.SpringBoneNames != null && chain.SpringBoneNames.Count > 0)
            {
                ResolveExplicitChain(data, chain, anchor, linksByBone);
            }
            else
            {
                HashSet<Transform> exclusions = ResolveExclusions(data, chain);
                ResolveDescendants(data, anchor, anchor, exclusions, 1, linksByBone);
            }

            if (linksByBone.Count == countBefore)
            {
                anchors.Remove(anchor);
                Debug.LogWarning(
                    $"[UnitySpringJointAnimator] Chain '{chain.AnchorBoneName}' contains no valid spring bones.",
                    this);
            }
        }

        private void ResolveExplicitChain(
            UMAData data,
            ChainDefinition chain,
            Transform anchor,
            Dictionary<Transform, BoneLink> linksByBone)
        {
            Transform precedingBone = anchor;
            for (int i = 0; i < chain.SpringBoneNames.Count; i++)
            {
                string boneName = chain.SpringBoneNames[i];
                if (string.IsNullOrWhiteSpace(boneName))
                {
                    continue;
                }

                Transform bone = data.skeleton.GetBoneTransform(boneName);
                if (bone == null)
                {
                    Debug.LogWarning(
                        $"[UnitySpringJointAnimator] Spring bone '{boneName}' was not found for chain '{chain.AnchorBoneName}'.",
                        this);
                    continue;
                }

                if (bone == anchor || !bone.IsChildOf(anchor))
                {
                    Debug.LogWarning(
                        $"[UnitySpringJointAnimator] Spring bone '{boneName}' is not a descendant of anchor '{chain.AnchorBoneName}' and was skipped.",
                        this);
                    continue;
                }

                if (TryAddLink(bone, precedingBone, linksByBone, chain.AnchorBoneName))
                {
                    precedingBone = bone;
                }
            }
        }

        private void ResolveDescendants(
            UMAData data,
            Transform hierarchyParent,
            Transform physicalParent,
            HashSet<Transform> exclusions,
            int depth,
            Dictionary<Transform, BoneLink> linksByBone)
        {
            if (MaxDepth > 0 && depth > MaxDepth)
            {
                return;
            }

            for (int i = 0; i < hierarchyParent.childCount; i++)
            {
                Transform child = hierarchyParent.GetChild(i);
                if (exclusions.Contains(child))
                {
                    continue;
                }

                Transform nextPhysicalParent = physicalParent;
                if (!RegisteredBonesOnly || IsRegisteredBone(data, child))
                {
                    if (TryAddLink(child, physicalParent, linksByBone, hierarchyParent.name))
                    {
                        nextPhysicalParent = child;
                    }
                }

                ResolveDescendants(
                    data,
                    child,
                    nextPhysicalParent,
                    exclusions,
                    depth + 1,
                    linksByBone);
            }
        }

        private static bool IsRegisteredBone(UMAData data, Transform transform)
        {
            return data.skeleton.GetBoneTransform(transform.name) == transform;
        }

        private bool TryAddLink(
            Transform bone,
            Transform parent,
            Dictionary<Transform, BoneLink> linksByBone,
            string chainName)
        {
            if (bone == null || parent == null)
            {
                return false;
            }

            if (linksByBone.TryGetValue(bone, out BoneLink existing))
            {
                if (existing.Parent != parent)
                {
                    Debug.LogWarning(
                        $"[UnitySpringJointAnimator] Bone '{bone.name}' has multiple configured parents. " +
                        $"The first connection is retained while resolving chain '{chainName}'.",
                        this);
                }
                return false;
            }

            linksByBone.Add(bone, new BoneLink(bone, parent));
            return true;
        }

        private HashSet<Transform> ResolveExclusions(UMAData data, ChainDefinition chain)
        {
            var exclusions = new HashSet<Transform>();
            if (chain.ExcludedBoneNames == null)
            {
                return exclusions;
            }

            for (int i = 0; i < chain.ExcludedBoneNames.Count; i++)
            {
                string boneName = chain.ExcludedBoneNames[i];
                if (string.IsNullOrWhiteSpace(boneName))
                {
                    continue;
                }

                Transform excluded = data.skeleton.GetBoneTransform(boneName);
                if (excluded == null)
                {
                    Debug.LogWarning(
                        $"[UnitySpringJointAnimator] Excluded bone '{boneName}' was not found for chain '{chain.AnchorBoneName}'.",
                        this);
                    continue;
                }

                exclusions.Add(excluded);
            }

            return exclusions;
        }

        private UnitySpringJointAnimatorBone GetOrCreateMarker(Transform bone, UMAData data)
        {
            UnitySpringJointAnimatorBone marker = FindMarker(bone, data);
            if (marker == null)
            {
                marker = bone.gameObject.AddComponent<UnitySpringJointAnimatorBone>();
                marker.Owner = this;
                marker.UMAData = data;
            }

            return marker;
        }

        private UnitySpringJointAnimatorBone FindMarker(Transform bone, UMAData data)
        {
            UnitySpringJointAnimatorBone[] markers =
                bone.GetComponents<UnitySpringJointAnimatorBone>();
            for (int i = 0; i < markers.Length; i++)
            {
                UnitySpringJointAnimatorBone marker = markers[i];
                if (marker != null && marker.Owner == this && marker.UMAData == data)
                {
                    return marker;
                }
            }

            return null;
        }

        private Rigidbody GetOrCreateRigidbody(UnitySpringJointAnimatorBone marker, bool isDynamic)
        {
            Rigidbody body = marker.OwnedRigidbody;
            if (body == null)
            {
                body = marker.GetComponent<Rigidbody>();
                if (body == null)
                {
                    body = marker.gameObject.AddComponent<Rigidbody>();
                    marker.OwnedRigidbody = body;
                }
            }

            // Use pre-existing bodies as connection points, but keep their settings
            // under the content author's control.
            if (marker.OwnedRigidbody != body)
            {
                return body;
            }

            body.isKinematic = !isDynamic;
            body.useGravity = isDynamic && UseGravity;
            body.mass = Mathf.Max(0.0001f, BoneMass);
#if UNITY_6000_0_OR_NEWER
            body.linearDamping = Mathf.Max(0f, LinearDamping);
            body.angularDamping = Mathf.Max(0f, AngularDamping);
#else
            body.drag = Mathf.Max(0f, LinearDamping);
            body.angularDrag = Mathf.Max(0f, AngularDamping);
#endif
            body.interpolation = isDynamic && Interpolate
                ? RigidbodyInterpolation.Interpolate
                : RigidbodyInterpolation.None;
            body.collisionDetectionMode = isDynamic
                ? CollisionDetection
                : CollisionDetectionMode.Discrete;
            body.constraints = isDynamic ? BoneConstraints : RigidbodyConstraints.FreezeAll;

            if (MaxAngularVelocity > 0f)
            {
                body.maxAngularVelocity = MaxAngularVelocity;
            }
            if (MaxDepenetrationVelocity > 0f)
            {
                body.maxDepenetrationVelocity = MaxDepenetrationVelocity;
            }

            return body;
        }

        private void ConfigureJoint(SpringJoint joint, Rigidbody connectedBody)
        {
            joint.connectedBody = connectedBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = Vector3.zero;
            joint.spring = Mathf.Max(0f, Spring);
            joint.damper = Mathf.Max(0f, Damper);
            joint.minDistance = Mathf.Max(0f, MinDistance);
            joint.maxDistance = Mathf.Max(joint.minDistance, MaxDistance);
            joint.tolerance = Mathf.Max(0f, Tolerance);
            joint.enableCollision = EnableConnectedBodyCollision;
            joint.enablePreprocessing = EnablePreprocessing;
        }

        private void ConfigureCollider(UnitySpringJointAnimatorBone marker, bool isDynamic)
        {
            bool shouldHaveCollider = isDynamic ? AddBoneColliders : AddAnchorColliders;
            if (!shouldHaveCollider)
            {
                if (marker.OwnedCollider != null)
                {
                    DestroyOwnedObject(marker.OwnedCollider);
                    marker.OwnedCollider = null;
                }
                return;
            }

            SphereCollider collider = marker.OwnedCollider;
            if (collider == null)
            {
                collider = marker.gameObject.AddComponent<SphereCollider>();
                marker.OwnedCollider = collider;
            }

            collider.radius = isDynamic
                ? Mathf.Max(0.0001f, BoneColliderRadius)
                : Mathf.Max(0.0001f, AnchorColliderRadius);
            collider.center = isDynamic ? Vector3.zero : AnchorColliderCenter;
        }

        private void ApplyLayer(UnitySpringJointAnimatorBone marker)
        {
            if (BoneLayer < 0)
            {
                if (marker.LayerWasChanged)
                {
                    marker.gameObject.layer = marker.OriginalLayer;
                    marker.LayerWasChanged = false;
                }
                return;
            }

            if (!marker.LayerWasChanged)
            {
                marker.OriginalLayer = marker.gameObject.layer;
                marker.LayerWasChanged = true;
            }

            marker.gameObject.layer = BoneLayer;
        }

        private void CleanupStaleMarkers(
            UMAData data,
            HashSet<UnitySpringJointAnimatorBone> desiredMarkers)
        {
            UnitySpringJointAnimatorBone[] markers =
                data.GetComponentsInChildren<UnitySpringJointAnimatorBone>(true);
            for (int i = 0; i < markers.Length; i++)
            {
                UnitySpringJointAnimatorBone marker = markers[i];
                if (marker == null ||
                    marker.Owner != this ||
                    marker.UMAData != data ||
                    desiredMarkers.Contains(marker))
                {
                    continue;
                }

                CleanupMarker(marker);
            }
        }

        private static void CleanupMarker(UnitySpringJointAnimatorBone marker)
        {
            if (marker.LayerWasChanged)
            {
                marker.gameObject.layer = marker.OriginalLayer;
            }

            DestroyOwnedObject(marker.OwnedJoint);
            DestroyOwnedObject(marker.OwnedCollider);
            DestroyOwnedObject(marker.OwnedRigidbody);
            DestroyOwnedObject(marker);
        }

        private static void DestroyOwnedObject(UnityEngine.Object ownedObject)
        {
            if (ownedObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(ownedObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(ownedObject);
            }
        }

    }

#if UNITY_EDITOR
    [CustomEditor(typeof(UnitySpringJointAnimator))]
    public class UnitySpringJointAnimatorEditor : Editor
    {
        private SlotDataAsset _slotDataAsset;
        private string[] _boneNames = Array.Empty<string>();
        private int _selectedBoneIndex;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add Chain From Slot", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _slotDataAsset = (SlotDataAsset)EditorGUILayout.ObjectField(
                "Slot Data Asset",
                _slotDataAsset,
                typeof(SlotDataAsset),
                false);
            DrawSlotDropArea();
            if (EditorGUI.EndChangeCheck())
            {
                RebuildBoneNames();
            }

            if (_slotDataAsset == null)
            {
                EditorGUILayout.HelpBox(
                    "Drop or assign a SlotDataAsset to select a spring-chain anchor from its mesh bones.",
                    MessageType.Info);
                return;
            }

            if (_boneNames.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "The selected SlotDataAsset has no UMA bone data.",
                    MessageType.Warning);
                return;
            }

            _selectedBoneIndex = Mathf.Clamp(
                _selectedBoneIndex,
                0,
                _boneNames.Length - 1);
            _selectedBoneIndex = EditorGUILayout.Popup(
                "Anchor Bone",
                _selectedBoneIndex,
                _boneNames);

            if (GUILayout.Button("Add a chain for this bone"))
            {
                AddChainForSelectedBone();
            }
        }

        private void DrawSlotDropArea()
        {
            Rect dropRect = GUILayoutUtility.GetRect(
                0f,
                42f,
                GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drop SlotDataAsset Here", EditorStyles.helpBox);

            Event currentEvent = Event.current;
            if (!dropRect.Contains(currentEvent.mousePosition) ||
                (currentEvent.type != EventType.DragUpdated &&
                 currentEvent.type != EventType.DragPerform))
            {
                return;
            }

            SlotDataAsset droppedSlot = GetDraggedSlotDataAsset();
            DragAndDrop.visualMode = droppedSlot != null
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;

            if (currentEvent.type == EventType.DragPerform && droppedSlot != null)
            {
                DragAndDrop.AcceptDrag();
                _slotDataAsset = droppedSlot;
                RebuildBoneNames();
                GUI.changed = true;
            }

            currentEvent.Use();
        }

        private static SlotDataAsset GetDraggedSlotDataAsset()
        {
            UnityEngine.Object[] objectReferences = DragAndDrop.objectReferences;
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
            if (_slotDataAsset == null ||
                UMAMeshData.IsNullOrEmptyMeshData(_slotDataAsset.meshData) ||
                _slotDataAsset.meshData.umaBones == null)
            {
                _boneNames = Array.Empty<string>();
                return;
            }

            var boneNames = new List<string>();
            UMATransform[] umaBones = _slotDataAsset.meshData.umaBones;
            for (int i = 0; i < umaBones.Length; i++)
            {
                UMATransform bone = umaBones[i];
                if (bone == null ||
                    string.IsNullOrEmpty(bone.name) ||
                    boneNames.Contains(bone.name))
                {
                    continue;
                }

                boneNames.Add(bone.name);
            }

            boneNames.Sort(StringComparer.Ordinal);
            _boneNames = boneNames.ToArray();
        }

        private void AddChainForSelectedBone()
        {
            UnitySpringJointAnimator animator =
                (UnitySpringJointAnimator)target;
            string boneName = _boneNames[_selectedBoneIndex];

            if (animator.Chains == null)
            {
                animator.Chains =
                    new List<UnitySpringJointAnimator.ChainDefinition>();
            }

            for (int i = 0; i < animator.Chains.Count; i++)
            {
                UnitySpringJointAnimator.ChainDefinition chain =
                    animator.Chains[i];
                if (chain != null && chain.AnchorBoneName == boneName)
                {
                    EditorUtility.DisplayDialog(
                        "Chain Already Exists",
                        $"A spring chain for '{boneName}' already exists.",
                        "OK");
                    return;
                }
            }

            Undo.RecordObject(animator, "Add Unity Spring Joint Chain");
            animator.Chains.Add(
                new UnitySpringJointAnimator.ChainDefinition
                {
                    AnchorBoneName = boneName
                });
            EditorUtility.SetDirty(animator);
            serializedObject.Update();
        }
    }
#endif
}
