using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace UMA.Dismemberment
{
    [Serializable]
    public class Dismembered : UnityEvent<Transform, Transform> { }

    [Serializable]
    public sealed class DismembermentCompletedEvent : UnityEvent<DismembermentResult> { }

    public enum DismembermentFailureReason
    {
        None,
        NotInitialized,
        InvalidBone,
        BoneNotSliceable,
        AlreadyDismembered,
        MissingCapMaterial,
        UnsupportedRenderer,
        NoAffectedGeometry,
        InvalidMesh,
        SkeletonCloneFailed,
        InternalError
    }

    public enum DetachedPieceRebuildPolicy
    {
        DestroyDetachedPieces,
        KeepDetachedPieces
    }

    [Serializable]
    public struct DismembermentPipelineMaterial
    {
        [Tooltip("Use this cap material when this exact render-pipeline asset is active.")]
        public RenderPipelineAsset pipeline;
        public Material material;
    }

    [Serializable]
    public sealed class DismembermentResult
    {
        public Transform root;
        public Transform targetBone;
        public HumanBodyBones humanBone = HumanBodyBones.LastBone;
        public int boneHash;
        public SkinnedMeshRenderer[] detachedRenderers = Array.Empty<SkinnedMeshRenderer>();
        public SkinnedMeshRenderer[] sourceRenderers = Array.Empty<SkinnedMeshRenderer>();
    }

    /// <summary>
    /// Runtime UMA 3 dismemberment. The component owns cloned source meshes, never mutates a mesh
    /// owned by UMA, and releases those clones before the next avatar generation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DynamicCharacterAvatar))]
    public class UmaDismemberment : MonoBehaviour
    {
        [Serializable]
        public struct DismemberedInfo
        {
            public Transform root;
            public Transform targetBone;
            public HumanBodyBones humanBone;
            public int boneHash;
            public SkinnedMeshRenderer[] detachedRenderers;
            public SkinnedMeshRenderer[] sourceRenderers;
        }

        [Serializable]
        public struct BoneInfo
        {
            public HumanBodyBones humanBone;
            [Range(0.01f, 1f)] public float threshold;

            public static BoneInfo CreateDefault(HumanBodyBones bone = HumanBodyBones.Head)
            {
                return new BoneInfo { humanBone = bone, threshold = 0.5f };
            }
        }

        [Header("Events")]
        [Tooltip("Invoke the legacy Transform/Transform event after a successful slice.")]
        public bool useEvents;
        public Dismembered DismemberedEvent = new Dismembered();
        [Tooltip("Rich UMA 3 result containing every affected source and detached renderer.")]
        public DismembermentCompletedEvent DismembermentCompleted =
            new DismembermentCompletedEvent();

        [Header("Cap")]
        [Tooltip("Fallback cap material. It is also used by the Built-in Render Pipeline.")]
        public Material sliceFill;
        [Tooltip("Optional exact render-pipeline overrides. This avoids hard dependencies on URP or HDRP.")]
        public List<DismembermentPipelineMaterial> pipelineSliceFillOverrides =
            new List<DismembermentPipelineMaterial>();
        [Tooltip("Create a closed cap when the cut boundary is a valid manifold loop.")]
        public bool generateCaps = true;
        [Tooltip("Reject a cut rather than leave a hole when its cap boundary is open or non-manifold.")]
        public bool requireClosedCaps = true;
        [Min(0.001f), Tooltip("Physical cap UV scale using Unity's standard 1 unit = 1 meter convention.")]
        public float capUvMetersPerTile = 0.25f;

        [Header("Bone Selection")]
        [Range(0.01f, 1f)]
        [Tooltip("Accumulated target-subtree weight required for a vertex to follow the detached piece.")]
        public float globalThreshold = 0.5f;
        [Tooltip("When enabled, HumanBodyBones slices are restricted to this list.")]
        public bool useSliceable = true;
        public List<BoneInfo> sliceableHumanBones = new List<BoneInfo>();
        [Tooltip("Include all descendants of the selected bone in its accumulated skin weight.")]
        public bool includeChildBones = true;

        [Header("Lifecycle")]
        [Tooltip("Detached-piece behavior when UMA starts rebuilding the avatar.")]
        public DetachedPieceRebuildPolicy rebuildPolicy =
            DetachedPieceRebuildPolicy.DestroyDetachedPieces;

        // Kept public for source compatibility. Stable decisions use bone hashes instead.
        [NonSerialized] public HashSet<Transform> hasSplit = new HashSet<Transform>();

        public string LastFailure { get; private set; }
        public DismembermentFailureReason LastFailureReason { get; private set; }
        public bool IsReady => currentData != null && currentData.GetRenderers() != null;

        private sealed class OwnedSourceRenderer
        {
            public SkinnedMeshRenderer renderer;
            public Mesh umaOwnedMesh;
            public Mesh dismembermentOwnedMesh;
            public Material[] originalMaterials;
            public Bounds originalLocalBounds;
            public int capSubmeshIndex = -1;
        }

        private sealed class PendingRenderer
        {
            public SkinnedMeshRenderer source;
            public DismembermentMeshBuildResult build;
            public Material[] materials;
            public OwnedSourceRenderer existingState;
        }

        private sealed class SourceCommitSnapshot
        {
            public OwnedSourceRenderer state;
            public bool stateWasNew;
            public Mesh previousOwnedMesh;
            public int previousCapSubmesh;
            public Mesh previousRendererMesh;
            public Material[] previousRendererMaterials;
            public Bounds previousRendererBounds;
        }

        private DynamicCharacterAvatar avatar;
        private UMAData currentData;
        private Animator animator;
        private bool subscribed;
        private uint observedGenerationVersion;
        private readonly HashSet<int> splitBoneHashes = new HashSet<int>();
        private readonly List<OwnedSourceRenderer> ownedSourceRenderers =
            new List<OwnedSourceRenderer>();
        private readonly List<GameObject> detachedPieces = new List<GameObject>();

        private void OnEnable()
        {
            avatar = GetComponent<DynamicCharacterAvatar>();
            DismemberedEvent ??= new Dismembered();
            DismembermentCompleted ??= new DismembermentCompletedEvent();
            hasSplit ??= new HashSet<Transform>();
            Subscribe();
            if (avatar != null && avatar.umaData != null) HandleCharacterUpdated(avatar.umaData);
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetDismemberment(true);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ResetDismemberment(true);
        }

        private void OnValidate()
        {
            globalThreshold = Mathf.Clamp(globalThreshold, 0.01f, 1f);
            capUvMetersPerTile = Mathf.Max(0.001f, capUvMetersPerTile);
            if (sliceableHumanBones == null) sliceableHumanBones = new List<BoneInfo>();
            for (int i = 0; i < sliceableHumanBones.Count; i++)
            {
                BoneInfo info = sliceableHumanBones[i];
                if (info.threshold <= 0f) info.threshold = 0.5f;
                info.threshold = Mathf.Clamp(info.threshold, 0.01f, 1f);
                sliceableHumanBones[i] = info;
            }
        }

        private void Subscribe()
        {
            if (subscribed || avatar == null) return;
            avatar.CharacterBegun ??= new UMADataEvent();
            avatar.CharacterCreated ??= new UMADataEvent();
            avatar.CharacterUpdated ??= new UMADataEvent();
            avatar.CharacterBegun.AddListener(HandleCharacterBegun);
            avatar.CharacterCreated.AddListener(HandleCharacterUpdated);
            avatar.CharacterUpdated.AddListener(HandleCharacterUpdated);
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || avatar == null) return;
            avatar.CharacterBegun?.RemoveListener(HandleCharacterBegun);
            avatar.CharacterCreated?.RemoveListener(HandleCharacterUpdated);
            avatar.CharacterUpdated?.RemoveListener(HandleCharacterUpdated);
            subscribed = false;
        }

        private void HandleCharacterBegun(UMAData data)
        {
            ReleaseOwnedSourceMeshes();
            splitBoneHashes.Clear();
            hasSplit.Clear();
            if (rebuildPolicy == DetachedPieceRebuildPolicy.DestroyDetachedPieces)
                DestroyDetachedPieces();
            currentData = data;
        }

        private void HandleCharacterUpdated(UMAData data)
        {
            if (data == null) return;
            if (currentData != null && observedGenerationVersion != 0 &&
                observedGenerationVersion != data.GenerationRequestVersion &&
                ownedSourceRenderers.Count > 0)
            {
                ReleaseOwnedSourceMeshes();
                splitBoneHashes.Clear();
                hasSplit.Clear();
            }
            currentData = data;
            observedGenerationVersion = data.GenerationRequestVersion;
            animator = data.animator != null ? data.animator : GetComponent<Animator>();
        }

        public Material ResolveSliceFillMaterial()
        {
            RenderPipelineAsset activePipeline = GraphicsSettings.currentRenderPipeline;
            if (pipelineSliceFillOverrides != null)
            {
                for (int i = 0; i < pipelineSliceFillOverrides.Count; i++)
                {
                    DismembermentPipelineMaterial candidate = pipelineSliceFillOverrides[i];
                    if (candidate.pipeline == activePipeline && candidate.material != null)
                        return candidate.material;
                }
            }
            return sliceFill;
        }

        public void Slice(HumanBodyBones humanBone, out DismemberedInfo info,
            bool useGlobalThreshold = false)
        {
            TrySlice(humanBone, out info, out _, useGlobalThreshold);
        }

        public void Slice(HumanBodyBones humanBone, bool hasNotSplit,
            out DismemberedInfo info, bool useGlobalThreshold = false)
        {
            TrySlice(humanBone, out info, out _, useGlobalThreshold, hasNotSplit);
        }

        public void Slice(Transform bone, out DismemberedInfo info)
        {
            TrySlice(bone, globalThreshold, out info, out _);
        }

        public void Slice(Transform bone, float threshold, out DismemberedInfo info)
        {
            TrySlice(bone, threshold, out info, out _);
        }

        public bool TrySlice(HumanBodyBones humanBone, out DismemberedInfo info,
            out string failure, bool useGlobalThreshold = false, bool preventRepeatedSlice = true)
        {
            info = default;
            if (!TryResolveHumanBone(humanBone, out Transform bone, out failure)) return false;
            float threshold = globalThreshold;
            if (useSliceable)
            {
                int index = ContainsBone(humanBone);
                if (index < 0)
                    return Fail(DismembermentFailureReason.BoneNotSliceable,
                        $"{humanBone} is not in the Sliceable Human Bones list.", out failure);
                if (!useGlobalThreshold) threshold = sliceableHumanBones[index].threshold;
            }
            bool success = TrySliceInternal(bone, threshold, humanBone, preventRepeatedSlice,
                out info, out failure);
            return success;
        }

        public bool TrySlice(Transform bone, float threshold, out DismemberedInfo info,
            out string failure, bool preventRepeatedSlice = true)
        {
            return TrySliceInternal(bone, Mathf.Clamp(threshold, 0.01f, 1f),
                HumanBodyBones.LastBone, preventRepeatedSlice, out info, out failure);
        }

        public void ResetDismemberment(bool destroyDetachedPieces = true)
        {
            ReleaseOwnedSourceMeshes();
            splitBoneHashes.Clear();
            hasSplit?.Clear();
            if (destroyDetachedPieces) DestroyDetachedPieces();
            ClearFailure();
        }

        private bool TrySliceInternal(Transform bone, float threshold, HumanBodyBones humanBone,
            bool preventRepeatedSlice, out DismemberedInfo info, out string failure)
        {
            info = default;
            failure = string.Empty;
            ClearFailure();
            if (bone == null)
                return Fail(DismembermentFailureReason.InvalidBone, "The target bone is null.", out failure);
            if (currentData == null && avatar != null) HandleCharacterUpdated(avatar.umaData);
            SkinnedMeshRenderer[] renderers = currentData?.GetRenderers();
            if (currentData == null || renderers == null || renderers.Length == 0)
                return Fail(DismembermentFailureReason.NotInitialized,
                    "UMA has not finished generating any renderers.", out failure);

            int boneHash = UMAUtils.StringToHash(bone.name);
            if (preventRepeatedSlice && splitBoneHashes.Contains(boneHash))
                return Fail(DismembermentFailureReason.AlreadyDismembered,
                    $"Bone '{bone.name}' has already been dismembered in this UMA generation.",
                    out failure);

            Material capMaterial = ResolveSliceFillMaterial();
            if (generateCaps && (capMaterial == null || capMaterial.shader == null ||
                !capMaterial.shader.isSupported))
                return Fail(DismembermentFailureReason.MissingCapMaterial,
                    "A supported cap material is required when Generate Caps is enabled. " +
                    "Assign a fallback or active render-pipeline override.", out failure);

            CollectBoneSubtree(bone, includeChildBones, out HashSet<Transform> includedTransforms,
                out HashSet<int> includedHashes);
            var pending = new List<PendingRenderer>();
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                if (renderer == null || renderer.sharedMesh == null) continue;
                Transform[] rendererBones = renderer.bones;
                bool[] includedBones = BuildRendererBoneMask(rendererBones, includedTransforms,
                    includedHashes, out bool rendererUsesTarget);
                if (!rendererUsesTarget) continue;
                if (renderer.GetComponent<Cloth>() != null)
                {
                    DestroyPending(pending);
                    return Fail(DismembermentFailureReason.UnsupportedRenderer,
                        $"Renderer '{renderer.name}' has a Cloth component. Runtime topology changes " +
                        "require cloth coefficients to be rebuilt and are not performed implicitly.",
                        out failure);
                }

                OwnedSourceRenderer existingState = FindOwnedState(renderer);
                int existingCap = existingState?.capSubmeshIndex ?? -1;
                var options = new DismembermentMeshBuildOptions(threshold, existingCap,
                    generateCaps, requireClosedCaps, capUvMetersPerTile);
                DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(
                    renderer.sharedMesh, includedBones, options,
                    out DismembermentMeshBuildResult build, out string buildError);
                if (status == DismembermentMeshBuildStatus.NoAffectedTriangles) continue;
                if (status != DismembermentMeshBuildStatus.Success)
                {
                    DestroyPending(pending);
                    return Fail(DismembermentFailureReason.InvalidMesh,
                        $"Renderer '{renderer.name}' could not be sliced: {buildError}", out failure);
                }
                pending.Add(new PendingRenderer
                {
                    source = renderer,
                    build = build,
                    materials = renderer.sharedMaterials,
                    existingState = existingState
                });
            }

            if (pending.Count == 0)
                return Fail(DismembermentFailureReason.NoAffectedGeometry,
                    $"No UMA renderer contains geometry above the {threshold:0.###} weight threshold " +
                    $"for '{bone.name}'.", out failure);

            GameObject detachedRoot = null;
            var detachedRenderers = new List<SkinnedMeshRenderer>(pending.Count);
            var detachedMeshes = new List<Mesh>(pending.Count);
            try
            {
                if (!TryCreateDetachedHierarchy(currentData, bone, pending, capMaterial,
                    out detachedRoot, out Transform detachedTargetBone, detachedRenderers,
                    detachedMeshes, out string cloneError))
                {
                    DestroyPending(pending);
                    DestroyOwnedObject(detachedRoot);
                    DestroyMeshList(detachedMeshes);
                    return Fail(DismembermentFailureReason.SkeletonCloneFailed, cloneError,
                        out failure);
                }
                DismemberedPiece piece = detachedRoot.AddComponent<DismemberedPiece>();
                piece.Initialize(detachedTargetBone, detachedRenderers, detachedMeshes);
            }
            catch (Exception exception)
            {
                DestroyOwnedObject(detachedRoot);
                DestroyPending(pending);
                DestroyMeshList(detachedMeshes);
                return Fail(DismembermentFailureReason.InternalError,
                    $"Could not create the detached renderers: {exception.Message}", out failure);
            }

            var commits = new List<SourceCommitSnapshot>(pending.Count);
            try
            {
                for (int i = 0; i < pending.Count; i++)
                    commits.Add(CommitSourceRenderer(pending[i], capMaterial));
            }
            catch (Exception exception)
            {
                RollBackSourceCommits(commits);
                DestroyOwnedObject(detachedRoot);
                DestroyPending(pending);
                return Fail(DismembermentFailureReason.InternalError,
                    $"Could not commit the source renderers: {exception.Message}", out failure);
            }
            FinalizeSourceCommits(commits);

            var sourceRenderers = new SkinnedMeshRenderer[pending.Count];
            for (int i = 0; i < pending.Count; i++) sourceRenderers[i] = pending[i].source;
            detachedPieces.Add(detachedRoot);
            splitBoneHashes.Add(boneHash);
            hasSplit.Add(bone);
            info = new DismemberedInfo
            {
                root = detachedRoot.transform,
                targetBone = detachedRoot.GetComponent<DismemberedPiece>().TargetBone,
                humanBone = humanBone,
                boneHash = boneHash,
                detachedRenderers = detachedRenderers.ToArray(),
                sourceRenderers = sourceRenderers
            };
            var richResult = new DismembermentResult
            {
                root = info.root,
                targetBone = info.targetBone,
                humanBone = humanBone,
                boneHash = boneHash,
                detachedRenderers = info.detachedRenderers,
                sourceRenderers = info.sourceRenderers
            };
            InvokeCompletionEvents(info, richResult);
            return true;
        }

        private bool TryResolveHumanBone(HumanBodyBones humanBone, out Transform bone,
            out string failure)
        {
            bone = null;
            failure = string.Empty;
            if (currentData == null && avatar != null) HandleCharacterUpdated(avatar.umaData);
            animator = currentData?.animator != null ? currentData.animator : GetComponent<Animator>();
            if (animator == null)
                return Fail(DismembermentFailureReason.NotInitialized,
                    "The UMA avatar has no Animator.", out failure);
            if (!animator.isHuman)
                return Fail(DismembermentFailureReason.InvalidBone,
                    "HumanBodyBones slicing requires a humanoid Animator. Use the Transform overload " +
                    "for a generic UMA rig.", out failure);
            if (humanBone == HumanBodyBones.LastBone)
                return Fail(DismembermentFailureReason.InvalidBone,
                    "LastBone is not a sliceable humanoid bone.", out failure);
            bone = animator.GetBoneTransform(humanBone);
            if (bone == null)
                return Fail(DismembermentFailureReason.InvalidBone,
                    $"The current humanoid avatar does not map {humanBone}.", out failure);
            return true;
        }

        private static void CollectBoneSubtree(Transform root, bool includeChildren,
            out HashSet<Transform> transforms, out HashSet<int> hashes)
        {
            transforms = new HashSet<Transform>();
            hashes = new HashSet<int>();
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current == null || !transforms.Add(current)) continue;
                hashes.Add(UMAUtils.StringToHash(current.name));
                if (!includeChildren) continue;
                for (int child = current.childCount - 1; child >= 0; child--)
                    stack.Push(current.GetChild(child));
            }
        }

        private static bool[] BuildRendererBoneMask(Transform[] rendererBones,
            HashSet<Transform> includedTransforms, HashSet<int> includedHashes,
            out bool usesTarget)
        {
            rendererBones ??= Array.Empty<Transform>();
            var mask = new bool[rendererBones.Length];
            usesTarget = false;
            for (int i = 0; i < rendererBones.Length; i++)
            {
                Transform rendererBone = rendererBones[i];
                bool included = rendererBone != null &&
                    (includedTransforms.Contains(rendererBone) ||
                     includedHashes.Contains(UMAUtils.StringToHash(rendererBone.name)));
                mask[i] = included;
                usesTarget |= included;
            }
            return mask;
        }

        private static bool TryCreateDetachedHierarchy(UMAData data, Transform sourceTargetBone,
            List<PendingRenderer> pending, Material capMaterial, out GameObject detachedRoot,
            out Transform detachedTargetBone, List<SkinnedMeshRenderer> detachedRenderers,
            List<Mesh> detachedMeshes, out string error)
        {
            detachedRoot = null;
            detachedTargetBone = null;
            error = string.Empty;
            Transform sourceSkeletonRoot = data.GetGlobalTransform();
            if (sourceSkeletonRoot == null)
            {
                error = "UMAData.GetGlobalTransform() returned null.";
                return false;
            }

            var required = new HashSet<Transform>();
            if (!AddRequiredPath(required, sourceTargetBone, sourceSkeletonRoot))
            {
                error = $"Target bone '{sourceTargetBone.name}' is not below UMA's active skeleton root " +
                    $"'{sourceSkeletonRoot.name}'.";
                return false;
            }
            for (int i = 0; i < pending.Count; i++)
            {
                Transform[] bones = pending[i].source.bones;
                for (int bone = 0; bone < bones.Length; bone++)
                {
                    if (bones[bone] != null && !AddRequiredPath(required, bones[bone], sourceSkeletonRoot))
                    {
                        error = $"Renderer '{pending[i].source.name}' uses bone '{bones[bone].name}' " +
                            "outside UMA's active skeleton root.";
                        return false;
                    }
                }
                Transform rootBone = pending[i].source.rootBone;
                if (rootBone != null && !AddRequiredPath(required, rootBone, sourceSkeletonRoot))
                {
                    error = $"Renderer '{pending[i].source.name}' has a root bone outside UMA's " +
                        "active skeleton root.";
                    return false;
                }
            }

            detachedRoot = new GameObject(sourceTargetBone.name + " Dismembered");
            Transform sourceParent = sourceSkeletonRoot.parent;
            if (sourceParent != null)
            {
                detachedRoot.transform.SetParent(sourceParent, false);
                detachedRoot.transform.localPosition = Vector3.zero;
                detachedRoot.transform.localRotation = Quaternion.identity;
                detachedRoot.transform.localScale = Vector3.one;
            }
            var boneMap = new Dictionary<Transform, Transform>();
            CloneRequiredHierarchy(sourceSkeletonRoot, detachedRoot.transform, required, boneMap);
            detachedRoot.transform.SetParent(null, true);
            if (!boneMap.TryGetValue(sourceTargetBone, out detachedTargetBone))
            {
                error = "The detached skeleton did not contain the target bone.";
                return false;
            }

            for (int i = 0; i < pending.Count; i++)
            {
                PendingRenderer item = pending[i];
                GameObject rendererObject = new GameObject(item.source.name + " Detached");
                rendererObject.layer = item.source.gameObject.layer;
                rendererObject.transform.SetParent(detachedRoot.transform, true);
                CopyWorldTransform(item.source.transform, rendererObject.transform,
                    detachedRoot.transform);
                SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                Transform[] sourceBones = item.source.bones;
                var mappedBones = new Transform[sourceBones.Length];
                for (int bone = 0; bone < sourceBones.Length; bone++)
                {
                    if (sourceBones[bone] == null) continue;
                    if (!boneMap.TryGetValue(sourceBones[bone], out mappedBones[bone]))
                    {
                        error = $"Could not map bone '{sourceBones[bone].name}' for renderer " +
                            $"'{item.source.name}'.";
                        return false;
                    }
                }
                renderer.bones = mappedBones;
                renderer.rootBone = item.source.rootBone != null &&
                    boneMap.TryGetValue(item.source.rootBone, out Transform mappedRoot)
                    ? mappedRoot : boneMap[sourceSkeletonRoot];
                renderer.sharedMesh = item.build.detachedMesh;
                Material[] materials = BuildMaterialArray(item.materials,
                    item.build.detachedMesh.subMeshCount, item.build.capSubmeshIndex, capMaterial);
                CopyRendererState(item.source, renderer, materials);
                CopyBlendShapeWeights(item.source, renderer);
                detachedRenderers.Add(renderer);
                detachedMeshes.Add(item.build.detachedMesh);
                item.build.detachedMesh = null;
            }
            return true;
        }

        private static bool AddRequiredPath(HashSet<Transform> required, Transform transform,
            Transform root)
        {
            Transform current = transform;
            while (current != null)
            {
                required.Add(current);
                if (current == root) return true;
                current = current.parent;
            }
            return false;
        }

        private static Transform CloneRequiredHierarchy(Transform source, Transform parent,
            HashSet<Transform> required, Dictionary<Transform, Transform> map)
        {
            if (!required.Contains(source)) return null;
            GameObject cloneObject = new GameObject(source.name);
            cloneObject.layer = source.gameObject.layer;
            Transform clone = cloneObject.transform;
            clone.SetParent(parent, false);
            clone.localPosition = source.localPosition;
            clone.localRotation = source.localRotation;
            clone.localScale = source.localScale;
            map.Add(source, clone);
            for (int child = 0; child < source.childCount; child++)
                CloneRequiredHierarchy(source.GetChild(child), clone, required, map);
            return clone;
        }

        private static void CopyWorldTransform(Transform source, Transform destination,
            Transform destinationParent)
        {
            destination.SetPositionAndRotation(source.position, source.rotation);
            Vector3 parentScale = destinationParent != null ? destinationParent.lossyScale : Vector3.one;
            Vector3 sourceScale = source.lossyScale;
            destination.localScale = new Vector3(
                SafeDivide(sourceScale.x, parentScale.x),
                SafeDivide(sourceScale.y, parentScale.y),
                SafeDivide(sourceScale.z, parentScale.z));
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) > 0.000001f ? value / divisor : value;
        }

        private static void CopyRendererState(SkinnedMeshRenderer source,
            SkinnedMeshRenderer destination, Material[] materials)
        {
            destination.sharedMaterials = materials;
            destination.enabled = source.enabled;
            destination.quality = source.quality;
            destination.updateWhenOffscreen = source.updateWhenOffscreen;
            destination.skinnedMotionVectors = source.skinnedMotionVectors;
            // Preserve UMA's authored/skinned bounds. Static vertex bounds are not conservative
            // enough for animation or blend-shape deformation and can cause detached limbs to
            // disappear while still inside the camera frustum.
            destination.localBounds = source.localBounds;
            destination.shadowCastingMode = source.shadowCastingMode;
            destination.receiveShadows = source.receiveShadows;
            destination.lightProbeUsage = source.lightProbeUsage;
            destination.reflectionProbeUsage = source.reflectionProbeUsage;
            destination.probeAnchor = source.probeAnchor;
            destination.lightProbeProxyVolumeOverride = source.lightProbeProxyVolumeOverride;
            destination.motionVectorGenerationMode = source.motionVectorGenerationMode;
            destination.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            destination.sortingLayerID = source.sortingLayerID;
            destination.sortingOrder = source.sortingOrder;
            destination.renderingLayerMask = source.renderingLayerMask;
            destination.forceRenderingOff = source.forceRenderingOff;

            var block = new MaterialPropertyBlock();
            source.GetPropertyBlock(block);
            destination.SetPropertyBlock(block);
            int materialCount = Mathf.Min(source.sharedMaterials.Length, materials.Length);
            for (int material = 0; material < materialCount; material++)
            {
                block.Clear();
                source.GetPropertyBlock(block, material);
                destination.SetPropertyBlock(block, material);
            }
        }

        private static void CopyBlendShapeWeights(SkinnedMeshRenderer source,
            SkinnedMeshRenderer destination)
        {
            Mesh sourceMesh = source.sharedMesh;
            Mesh destinationMesh = destination.sharedMesh;
            if (sourceMesh == null || destinationMesh == null) return;
            for (int shape = 0; shape < destinationMesh.blendShapeCount; shape++)
            {
                string name = destinationMesh.GetBlendShapeName(shape);
                int sourceIndex = sourceMesh.GetBlendShapeIndex(name);
                if (sourceIndex >= 0)
                    destination.SetBlendShapeWeight(shape, source.GetBlendShapeWeight(sourceIndex));
            }
        }

        private SourceCommitSnapshot CommitSourceRenderer(PendingRenderer pending,
            Material capMaterial)
        {
            OwnedSourceRenderer state = pending.existingState;
            bool stateWasNew = state == null;
            if (state == null)
            {
                state = new OwnedSourceRenderer
                {
                    renderer = pending.source,
                    umaOwnedMesh = pending.source.sharedMesh,
                    originalMaterials = pending.source.sharedMaterials,
                    originalLocalBounds = pending.source.localBounds
                };
                ownedSourceRenderers.Add(state);
            }
            var snapshot = new SourceCommitSnapshot
            {
                state = state,
                stateWasNew = stateWasNew,
                previousOwnedMesh = state.dismembermentOwnedMesh,
                previousCapSubmesh = state.capSubmeshIndex,
                previousRendererMesh = pending.source.sharedMesh,
                previousRendererMaterials = pending.source.sharedMaterials,
                previousRendererBounds = pending.source.localBounds
            };
            state.dismembermentOwnedMesh = pending.build.outerMesh;
            state.capSubmeshIndex = pending.build.capSubmeshIndex;
            pending.build.outerMesh = null;
            pending.source.sharedMesh = state.dismembermentOwnedMesh;
            pending.source.sharedMaterials = BuildMaterialArray(pending.materials,
                state.dismembermentOwnedMesh.subMeshCount, state.capSubmeshIndex, capMaterial);
            pending.source.localBounds = snapshot.previousRendererBounds;
            return snapshot;
        }

        private void RollBackSourceCommits(List<SourceCommitSnapshot> commits)
        {
            for (int i = commits.Count - 1; i >= 0; i--)
            {
                SourceCommitSnapshot snapshot = commits[i];
                Mesh failedMesh = snapshot.state.dismembermentOwnedMesh;
                if (snapshot.state.renderer != null)
                {
                    snapshot.state.renderer.sharedMesh = snapshot.previousRendererMesh;
                    snapshot.state.renderer.sharedMaterials = snapshot.previousRendererMaterials;
                    snapshot.state.renderer.localBounds = snapshot.previousRendererBounds;
                }
                snapshot.state.dismembermentOwnedMesh = snapshot.previousOwnedMesh;
                snapshot.state.capSubmeshIndex = snapshot.previousCapSubmesh;
                if (snapshot.stateWasNew) ownedSourceRenderers.Remove(snapshot.state);
                DestroyOwnedObject(failedMesh);
            }
        }

        private static void FinalizeSourceCommits(List<SourceCommitSnapshot> commits)
        {
            for (int i = 0; i < commits.Count; i++) DestroyOwnedObject(commits[i].previousOwnedMesh);
        }

        private static Material[] BuildMaterialArray(Material[] source, int submeshCount,
            int capSubmesh, Material capMaterial)
        {
            source ??= Array.Empty<Material>();
            var materials = new Material[submeshCount];
            Array.Copy(source, materials, Mathf.Min(source.Length, materials.Length));
            if ((uint)capSubmesh < (uint)materials.Length) materials[capSubmesh] = capMaterial;
            return materials;
        }

        private OwnedSourceRenderer FindOwnedState(SkinnedMeshRenderer renderer)
        {
            for (int i = 0; i < ownedSourceRenderers.Count; i++)
                if (ownedSourceRenderers[i].renderer == renderer) return ownedSourceRenderers[i];
            return null;
        }

        private void ReleaseOwnedSourceMeshes()
        {
            for (int i = 0; i < ownedSourceRenderers.Count; i++)
            {
                OwnedSourceRenderer state = ownedSourceRenderers[i];
                if (state.renderer != null)
                {
                    if (state.renderer.sharedMesh == state.dismembermentOwnedMesh)
                    {
                        state.renderer.sharedMesh = state.umaOwnedMesh;
                        state.renderer.sharedMaterials = state.originalMaterials ??
                            Array.Empty<Material>();
                        state.renderer.localBounds = state.originalLocalBounds;
                    }
                }
                DestroyOwnedObject(state.dismembermentOwnedMesh);
                state.dismembermentOwnedMesh = null;
            }
            ownedSourceRenderers.Clear();
        }

        private void DestroyDetachedPieces()
        {
            for (int i = 0; i < detachedPieces.Count; i++) DestroyOwnedObject(detachedPieces[i]);
            detachedPieces.Clear();
        }

        private static void DestroyPending(List<PendingRenderer> pending)
        {
            if (pending == null) return;
            for (int i = 0; i < pending.Count; i++) pending[i].build?.DestroyMeshes();
            pending.Clear();
        }

        private static void DestroyMeshList(List<Mesh> meshes)
        {
            if (meshes == null) return;
            for (int i = 0; i < meshes.Count; i++) DestroyOwnedObject(meshes[i]);
            meshes.Clear();
        }

        private void InvokeCompletionEvents(DismemberedInfo info,
            DismembermentResult richResult)
        {
            try
            {
                if (useEvents) DismemberedEvent?.Invoke(info.root, info.targetBone);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            try
            {
                DismembermentCompleted?.Invoke(richResult);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        private bool Fail(DismembermentFailureReason reason, string message, out string failure)
        {
            LastFailureReason = reason;
            LastFailure = message;
            failure = message;
            if (Debug.isDebugBuild) Debug.LogWarning($"UMA Dismemberment: {message}", this);
            return false;
        }

        private void ClearFailure()
        {
            LastFailureReason = DismembermentFailureReason.None;
            LastFailure = string.Empty;
        }

        private int ContainsBone(HumanBodyBones humanBone)
        {
            if (sliceableHumanBones == null) return -1;
            for (int i = 0; i < sliceableHumanBones.Count; i++)
                if (sliceableHumanBones[i].humanBone == humanBone) return i;
            return -1;
        }
    }
}
