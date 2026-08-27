using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA.Dynamics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

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

    public enum DismembermentCapUvMode
    {
        MeterScaledTiled,
        CenteredFit
    }

    public enum DismemberedPhysicsMode
    {
        Automatic,
        None,
        Rigid,
        ArticulatedRagdoll
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
        public Transform sourceTargetBone;
        public HumanBodyBones humanBone = HumanBodyBones.LastBone;
        public int boneHash;
        public bool mainBodyRagdollRequested;
        public bool mainBodyRagdollActivated;
        public SkinnedMeshRenderer[] detachedRenderers = Array.Empty<SkinnedMeshRenderer>();
        public SkinnedMeshRenderer[] sourceRenderers = Array.Empty<SkinnedMeshRenderer>();
        public DismembermentCutSurface[] cutSurfaces = Array.Empty<DismembermentCutSurface>();
    }

    /// <summary>
    /// Runtime UMA 3 dismemberment. The component owns cloned source meshes, never mutates a mesh
    /// owned by UMA, and releases those clones before the next avatar generation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DynamicCharacterAvatar))]
    public class UmaDismemberment : MonoBehaviour
    {
        public const float DefaultCenteredCapUvPadding = 0.02f;

        [Serializable]
        public struct DismemberedInfo
        {
            public Transform root;
            public Transform targetBone;
            public Transform sourceTargetBone;
            public HumanBodyBones humanBone;
            public int boneHash;
            public bool mainBodyRagdollRequested;
            public bool mainBodyRagdollActivated;
            public SkinnedMeshRenderer[] detachedRenderers;
            public SkinnedMeshRenderer[] sourceRenderers;
            public DismembermentCutSurface[] cutSurfaces;
        }

        [Serializable]
        public struct BoneInfo
        {
            public HumanBodyBones humanBone;
            [Range(0.01f, 1f)] public float threshold;
            [Tooltip("Meter Scaled Tiled preserves the legacy physical tiling. Centered Fit " +
                "maps the cap center to UV 0.5,0.5 without tiling.")]
            public DismembermentCapUvMode capUvMode;
            [Range(0.001f, 0.25f), Tooltip("Inset from every UV0 edge when using Centered Fit. " +
                "A value of 0.02 fits the cap inside 0.02 to 0.98.")]
            public float centeredCapUvPadding;
            [Tooltip("UMA physics definitions to build on the piece detached by this cut. " +
                "Include each severed-side bone that needs a collider or joint.")]
            public List<UMAPhysicsElement> physicsDefinitions;
            [Tooltip("Automatic makes a single-definition piece rigid and a multi-definition " +
                "piece articulated. None suppresses detached physics for this cut.")]
            public DismemberedPhysicsMode physicsMode;
            [FormerlySerializedAs("trimDetachedBonePalette")]
            [Tooltip("After cross-cut weights are sanitized, compact the renderer bone palette " +
                "and remove cloned skeleton branches outside the cut subtree. The Global-to-cut " +
                "transform path and the complete cut subtree are retained.")]
            public bool trimDetachedRig;
            [Tooltip("Ragdoll the surviving character after this cut succeeds. Use this for " +
                "incapacitating cuts such as the head or upper leg. The character must have a " +
                "configured UMAPhysicsAvatar.")]
            public bool ragdollMainBody;

            public static BoneInfo CreateDefault(HumanBodyBones bone = HumanBodyBones.Head)
            {
                return new BoneInfo
                {
                    humanBone = bone,
                    threshold = 0.5f,
                    capUvMode = DismembermentCapUvMode.MeterScaledTiled,
                    centeredCapUvPadding = DefaultCenteredCapUvPadding,
                    physicsDefinitions = new List<UMAPhysicsElement>(),
                    physicsMode = DismemberedPhysicsMode.Automatic,
                    trimDetachedRig = false,
                    ragdollMainBody = false
                };
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
        [Min(0.000001f), Tooltip("Maximum distance in meters for treating duplicated seam " +
            "vertices as the same cap-loop vertex. The default is 0.1 millimeters.")]
        public float seamWeldTolerance = DismembermentMeshBuildOptions.DefaultSeamWeldTolerance;

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

        private sealed class SuspendedSourceCollider
        {
            public Collider collider;
            public bool wasEnabled;
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
        private readonly List<SuspendedSourceCollider> suspendedSourceColliders =
            new List<SuspendedSourceCollider>();

        private void OnEnable()
        {
            DismemberedEvent ??= new Dismembered();
            DismembermentCompleted ??= new DismembermentCompletedEvent();
            hasSplit ??= new HashSet<Transform>();
            EnsureInitialized();
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
            seamWeldTolerance = seamWeldTolerance > 0f
                ? Mathf.Max(0.000001f, seamWeldTolerance)
                : DismembermentMeshBuildOptions.DefaultSeamWeldTolerance;
            if (sliceableHumanBones == null) sliceableHumanBones = new List<BoneInfo>();
            for (int i = 0; i < sliceableHumanBones.Count; i++)
            {
                BoneInfo info = sliceableHumanBones[i];
                if (info.threshold <= 0f) info.threshold = 0.5f;
                info.threshold = Mathf.Clamp(info.threshold, 0.01f, 1f);
                info.centeredCapUvPadding = NormalizeCenteredCapUvPadding(
                    info.centeredCapUvPadding);
                info.physicsDefinitions ??= new List<UMAPhysicsElement>();
                if (!Enum.IsDefined(typeof(DismemberedPhysicsMode), info.physicsMode))
                    info.physicsMode = DismemberedPhysicsMode.Automatic;
                sliceableHumanBones[i] = info;
            }
        }

        public bool TryGetBoneSettings(HumanBodyBones humanBone, out BoneInfo settings)
        {
            int index = ContainsBone(humanBone);
            if (index >= 0)
            {
                settings = sliceableHumanBones[index];
                return true;
            }
            settings = default;
            return false;
        }

        /// <summary>
        /// Disables UMAPhysicsAvatar-owned colliders on the source cut bone and its descendants.
        /// Their original enabled states are restored by reset, undo, disable, destruction, or
        /// the next UMA generation. Non-ragdoll gameplay colliders are not changed.
        /// </summary>
        public int SuspendSourceRagdollColliders(Transform sourceCutBone)
        {
            if (sourceCutBone == null) return 0;
            UMAPhysicsAvatar physicsAvatar = FindMainBodyPhysicsAvatar(this);
            if (physicsAvatar == null) return 0;

            int suspendedCount = 0;
            List<BoxCollider> boxColliders = physicsAvatar.BoxColliders;
            if (boxColliders != null)
            {
                for (int i = 0; i < boxColliders.Count; i++)
                    SuspendSourceCollider(boxColliders[i], sourceCutBone,
                        ref suspendedCount);
            }

            List<ClothSphereColliderPair> sphereColliders = physicsAvatar.SphereColliders;
            if (sphereColliders != null)
            {
                for (int i = 0; i < sphereColliders.Count; i++)
                {
                    ClothSphereColliderPair pair = sphereColliders[i];
                    SuspendSourceCollider(pair.first, sourceCutBone, ref suspendedCount);
                    SuspendSourceCollider(pair.second, sourceCutBone, ref suspendedCount);
                }
            }

            List<CapsuleCollider> capsuleColliders = physicsAvatar.CapsuleColliders;
            if (capsuleColliders != null)
            {
                for (int i = 0; i < capsuleColliders.Count; i++)
                    SuspendSourceCollider(capsuleColliders[i], sourceCutBone,
                        ref suspendedCount);
            }
            return suspendedCount;
        }

        private void SuspendSourceCollider(Collider collider, Transform sourceCutBone,
            ref int suspendedCount)
        {
            if (collider == null || !collider.enabled) return;
            Transform colliderTransform = collider.transform;
            if (colliderTransform != sourceCutBone &&
                !colliderTransform.IsChildOf(sourceCutBone)) return;
            for (int i = 0; i < suspendedSourceColliders.Count; i++)
                if (suspendedSourceColliders[i].collider == collider) return;

            suspendedSourceColliders.Add(new SuspendedSourceCollider
            {
                collider = collider,
                wasEnabled = collider.enabled
            });
            collider.enabled = false;
            suspendedCount++;
        }

        private void RestoreSourceRagdollColliders()
        {
            for (int i = suspendedSourceColliders.Count - 1; i >= 0; i--)
            {
                SuspendedSourceCollider state = suspendedSourceColliders[i];
                if (state.collider != null) state.collider.enabled = state.wasEnabled;
            }
            suspendedSourceColliders.Clear();
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

        private void EnsureInitialized()
        {
            if (avatar == null) avatar = GetComponent<DynamicCharacterAvatar>();
            if (avatar == null) return;
            Subscribe();
            if (currentData == null && avatar.umaData != null)
                HandleCharacterUpdated(avatar.umaData);
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
            RestoreSourceRagdollColliders();
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
                (ownedSourceRenderers.Count > 0 || suspendedSourceColliders.Count > 0))
            {
                RestoreSourceRagdollColliders();
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
            DismembermentCapUvMode capUvMode = DismembermentCapUvMode.MeterScaledTiled;
            float centeredCapUvPadding = DefaultCenteredCapUvPadding;
            bool trimDetachedRig = false;
            bool ragdollMainBody = false;
            if (useSliceable)
            {
                int index = ContainsBone(humanBone);
                if (index < 0)
                    return Fail(DismembermentFailureReason.BoneNotSliceable,
                        $"{humanBone} is not in the Sliceable Human Bones list.", out failure);
                BoneInfo settings = sliceableHumanBones[index];
                if (!useGlobalThreshold) threshold = settings.threshold;
                capUvMode = settings.capUvMode;
                centeredCapUvPadding = NormalizeCenteredCapUvPadding(
                    settings.centeredCapUvPadding);
                trimDetachedRig = settings.trimDetachedRig;
                ragdollMainBody = settings.ragdollMainBody;
            }
            bool success = TrySliceInternal(bone, threshold, humanBone, preventRepeatedSlice,
                capUvMode, centeredCapUvPadding, trimDetachedRig, ragdollMainBody,
                out info, out failure);
            return success;
        }

        public bool TrySlice(Transform bone, float threshold, out DismemberedInfo info,
            out string failure, bool preventRepeatedSlice = true)
        {
            return TrySliceInternal(bone, Mathf.Clamp(threshold, 0.01f, 1f),
                HumanBodyBones.LastBone, preventRepeatedSlice,
                DismembermentCapUvMode.MeterScaledTiled, DefaultCenteredCapUvPadding,
                false, false, out info, out failure);
        }

        public void ResetDismemberment(bool destroyDetachedPieces = true)
        {
            GetComponent<UMARuntimeSurfaceDecalController>()?.ClearForDismembermentReset();
            RestoreSourceRagdollColliders();
            ReleaseOwnedSourceMeshes();
            splitBoneHashes.Clear();
            hasSplit?.Clear();
            if (destroyDetachedPieces) DestroyDetachedPieces();
            ClearFailure();
        }

        /// <summary>
        /// Restores the source renderers, destroys every tracked detached piece, exits the
        /// character ragdoll, and optionally asks UMA to rebuild the current avatar recipe.
        /// </summary>
        public bool TryUndoDismemberment(out string failure, bool rebuildAvatar = true)
        {
            failure = string.Empty;
            EnsureInitialized();
            DynamicCharacterAvatar targetAvatar = avatar != null
                ? avatar : GetComponent<DynamicCharacterAvatar>();

            // Restore the owned source meshes before changing animation/physics state. This is
            // immediate even though detached GameObjects use Unity's end-of-frame Destroy in play.
            ResetDismemberment(true);

            string ragdollFailure = string.Empty;
            UMAPhysicsAvatar physicsAvatar = FindMainBodyPhysicsAvatar(this);
            if (Application.isPlaying && physicsAvatar != null && physicsAvatar.ragdolled)
            {
                try
                {
                    physicsAvatar.ragdolled = false;
                }
                catch (Exception exception)
                {
                    ragdollFailure = "The main-body ragdoll could not be disabled: " +
                        exception.Message;
                }
            }

            if (!rebuildAvatar)
            {
                if (string.IsNullOrEmpty(ragdollFailure)) return true;
                return Fail(DismembermentFailureReason.InternalError,
                    "The dismembered meshes and limbs were reset, but " + ragdollFailure,
                    out failure);
            }
            if (targetAvatar == null)
                return Fail(DismembermentFailureReason.NotInitialized,
                    "The dismemberment was reset, but no DynamicCharacterAvatar was found to " +
                    "rebuild." + FormatUndoRagdollFailure(ragdollFailure), out failure);

            try
            {
                targetAvatar.BuildCharacter(true, !targetAvatar.BundleCheck);
            }
            catch (Exception exception)
            {
                return Fail(DismembermentFailureReason.InternalError,
                    $"The dismemberment was reset, but UMA could not rebuild the avatar: " +
                    exception.Message + FormatUndoRagdollFailure(ragdollFailure), out failure);
            }
            if (string.IsNullOrEmpty(ragdollFailure)) return true;
            return Fail(DismembermentFailureReason.InternalError,
                "The dismemberment was reset and the UMA rebuild was requested, but " +
                ragdollFailure, out failure);
        }

        private static string FormatUndoRagdollFailure(string ragdollFailure)
        {
            return string.IsNullOrEmpty(ragdollFailure)
                ? string.Empty : " Additionally, " + ragdollFailure;
        }

        private bool TrySliceInternal(Transform bone, float threshold, HumanBodyBones humanBone,
            bool preventRepeatedSlice, DismembermentCapUvMode capUvMode,
            float centeredCapUvPadding, bool trimDetachedRig, bool ragdollMainBody,
            out DismemberedInfo info, out string failure)
        {
            info = default;
            failure = string.Empty;
            ClearFailure();
            EnsureInitialized();
            if (bone == null)
                return Fail(DismembermentFailureReason.InvalidBone, "The target bone is null.", out failure);
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
                int fallbackBoneIndex = FindDetachedFallbackBone(rendererBones, includedBones,
                    bone, boneHash);
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
                float effectiveSeamTolerance = seamWeldTolerance > 0f
                    ? seamWeldTolerance
                    : DismembermentMeshBuildOptions.DefaultSeamWeldTolerance;
                var options = new DismembermentMeshBuildOptions(threshold, existingCap,
                    generateCaps, requireClosedCaps, capUvMetersPerTile, effectiveSeamTolerance,
                    capUvMode, centeredCapUvPadding, fallbackBoneIndex);
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
                    trimDetachedRig,
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
            DismembermentCutSurface[] cutSurfaces = CreateCutSurfaces(pending);
            detachedPieces.Add(detachedRoot);
            splitBoneHashes.Add(boneHash);
            hasSplit.Add(bone);
            bool mainBodyRagdollActivated = ragdollMainBody &&
                TryActivateMainBodyRagdoll(humanBone, bone);
            info = new DismemberedInfo
            {
                root = detachedRoot.transform,
                targetBone = detachedRoot.GetComponent<DismemberedPiece>().TargetBone,
                sourceTargetBone = bone,
                humanBone = humanBone,
                boneHash = boneHash,
                mainBodyRagdollRequested = ragdollMainBody,
                mainBodyRagdollActivated = mainBodyRagdollActivated,
                detachedRenderers = detachedRenderers.ToArray(),
                sourceRenderers = sourceRenderers,
                cutSurfaces = cutSurfaces
            };
            var richResult = new DismembermentResult
            {
                root = info.root,
                targetBone = info.targetBone,
                sourceTargetBone = bone,
                humanBone = humanBone,
                boneHash = boneHash,
                mainBodyRagdollRequested = ragdollMainBody,
                mainBodyRagdollActivated = mainBodyRagdollActivated,
                detachedRenderers = info.detachedRenderers,
                sourceRenderers = info.sourceRenderers,
                cutSurfaces = cutSurfaces
            };
            InvokeCompletionEvents(info, richResult);
            return true;
        }

        private DismembermentCutSurface[] CreateCutSurfaces(List<PendingRenderer> pending)
        {
            var surfaces = new List<DismembermentCutSurface>();
            for (int rendererIndex = 0; rendererIndex < pending.Count; rendererIndex++)
            {
                PendingRenderer item = pending[rendererIndex];
                DismembermentBoundaryLoopData[] loops = item.build?.boundaryLoops;
                if (item.source == null || loops == null) continue;
                Material[] rendererMaterials = item.source.sharedMaterials;
                for (int loopIndex = 0; loopIndex < loops.Length; loopIndex++)
                {
                    DismembermentBoundaryLoopData loop = loops[loopIndex];
                    if (loop == null || loop.boundaryUV == null || loop.boundaryUV.Length < 3)
                        continue;
                    int submesh = loop.sourceSubmeshIndex;
                    Material sourceMaterial = (uint)submesh < (uint)rendererMaterials.Length
                        ? rendererMaterials[submesh] : null;
                    SlotData slot = ResolveBoundarySlot(loop.sourceVertexIndices);
                    OverlayDataAsset overlay = ResolveFirstOverlayAsset(slot);
                    string[] overlayGroups = ResolveOverlayGroups(slot);
                    CalculateBoundaryFrame(loop.boundaryLocalPositions,
                        out Vector3 center, out Vector3 normal);
                    surfaces.Add(new DismembermentCutSurface
                    {
                        sourceRenderer = item.source,
                        sourceSubmeshIndex = submesh,
                        sourceMaterial = sourceMaterial,
                        sourceVertexIndices = (int[])loop.sourceVertexIndices.Clone(),
                        boundaryUV = (Vector2[])loop.boundaryUV.Clone(),
                        boundaryLocalPositions =
                            (Vector3[])loop.boundaryLocalPositions.Clone(),
                        loopStarts = new[] { 0 },
                        loopCounts = new[] { loop.boundaryUV.Length },
                        uvBounds = CalculateUVBounds(loop.boundaryUV),
                        localCenter = center,
                        localNormal = normal,
                        slotName = slot?.slotName,
                        slotGroup = slot?.asset != null ? slot.asset.slotGroup : null,
                        overlayGroup = overlay != null ? overlay.overlayGroup : null,
                        overlayGroups = overlayGroups,
                        umaMaterialName = slot?.material != null ? slot.material.name : null
                    });
                }
            }
            return surfaces.ToArray();
        }

        private SlotData ResolveBoundarySlot(int[] sourceIndices)
        {
            if (currentData?.umaRecipe?.slotDataList == null || sourceIndices == null ||
                sourceIndices.Length == 0) return null;
            SlotData resolved = null;
            for (int i = 0; i < sourceIndices.Length; i++)
            {
                SlotData candidate = FindSlotForVertexSafe(
                    currentData.umaRecipe.slotDataList, sourceIndices[i]);
                if (candidate == null) continue;
                if (resolved == null) resolved = candidate;
                else if (resolved != candidate) return null;
            }
            return resolved;
        }

        private static SlotData FindSlotForVertexSafe(SlotData[] slots, int vertex)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot?.asset?.meshData == null || vertex < slot.vertexOffset) continue;
                int localVertex = vertex - slot.vertexOffset;
                if (localVertex < slot.asset.meshData.vertexCount) return slot;
            }
            return null;
        }

        private static OverlayDataAsset ResolveFirstOverlayAsset(SlotData slot)
        {
            List<OverlayData> overlays = slot?.GetOverlayList();
            if (overlays == null) return null;
            for (int i = 0; i < overlays.Count; i++)
                if (overlays[i]?.asset != null) return overlays[i].asset;
            return null;
        }

        private static string[] ResolveOverlayGroups(SlotData slot)
        {
            List<OverlayData> overlays = slot?.GetOverlayList();
            if (overlays == null || overlays.Count == 0) return Array.Empty<string>();
            var groups = new List<string>();
            for (int i = 0; i < overlays.Count; i++)
            {
                string group = overlays[i]?.asset != null
                    ? overlays[i].asset.overlayGroup : null;
                if (!string.IsNullOrEmpty(group) && !groups.Contains(group)) groups.Add(group);
            }
            return groups.ToArray();
        }

        private static Rect CalculateUVBounds(Vector2[] uv)
        {
            Vector2 minimum = uv[0];
            Vector2 maximum = uv[0];
            for (int i = 1; i < uv.Length; i++)
            {
                minimum = Vector2.Min(minimum, uv[i]);
                maximum = Vector2.Max(maximum, uv[i]);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        private static void CalculateBoundaryFrame(Vector3[] points,
            out Vector3 center, out Vector3 normal)
        {
            center = Vector3.zero;
            normal = Vector3.zero;
            if (points == null || points.Length == 0)
            {
                normal = Vector3.forward;
                return;
            }
            for (int i = 0; i < points.Length; i++) center += points[i];
            center /= points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 current = points[i] - center;
                Vector3 next = points[(i + 1) % points.Length] - center;
                normal += Vector3.Cross(current, next);
            }
            normal = normal.sqrMagnitude > 0.0000000001f
                ? normal.normalized : Vector3.forward;
        }

        private bool TryActivateMainBodyRagdoll(HumanBodyBones humanBone, Transform cutBone)
        {
            UMAPhysicsAvatar physicsAvatar = FindMainBodyPhysicsAvatar(this);
            string cutName = humanBone != HumanBodyBones.LastBone
                ? humanBone.ToString()
                : cutBone != null ? cutBone.name : "unknown cut";
            if (physicsAvatar == null)
            {
                Debug.LogWarning($"UMA Dismemberment: '{cutName}' is configured to ragdoll the " +
                    "main body, but no UMAPhysicsAvatar was found on the character. The cut " +
                    "succeeded, but the main body was not ragdolled.", this);
                return false;
            }
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"UMA Dismemberment: '{cutName}' requested a main-body " +
                    "ragdoll outside Play Mode. The cut succeeded, but ragdoll activation is " +
                    "runtime-only.", this);
                return false;
            }

            try
            {
                if (!physicsAvatar.ragdolled) physicsAvatar.ragdolled = true;
                return physicsAvatar.ragdolled;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"UMA Dismemberment: The '{cutName}' cut succeeded, but the " +
                    $"main body could not be ragdolled: {exception.Message}", this);
                return false;
            }
        }

        internal static UMAPhysicsAvatar FindMainBodyPhysicsAvatar(Component source)
        {
            if (source == null) return null;
            UMAPhysicsAvatar physicsAvatar = source.GetComponent<UMAPhysicsAvatar>();
            if (physicsAvatar == null)
                physicsAvatar = source.GetComponentInParent<UMAPhysicsAvatar>(true);
            if (physicsAvatar == null)
                physicsAvatar = source.GetComponentInChildren<UMAPhysicsAvatar>(true);
            return physicsAvatar;
        }

        private bool TryResolveHumanBone(HumanBodyBones humanBone, out Transform bone,
            out string failure)
        {
            bone = null;
            failure = string.Empty;
            EnsureInitialized();
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

        private static int FindDetachedFallbackBone(Transform[] rendererBones,
            bool[] includedBones, Transform targetBone, int targetBoneHash)
        {
            rendererBones ??= Array.Empty<Transform>();
            includedBones ??= Array.Empty<bool>();
            for (int i = 0; i < rendererBones.Length; i++)
            {
                Transform candidate = rendererBones[i];
                if (candidate == targetBone || candidate != null &&
                    UMAUtils.StringToHash(candidate.name) == targetBoneHash) return i;
            }
            for (int i = 0; i < includedBones.Length; i++)
                if (includedBones[i]) return i;
            return -1;
        }

        private static bool TryCreateDetachedHierarchy(UMAData data, Transform sourceTargetBone,
            List<PendingRenderer> pending, Material capMaterial, bool trimDetachedRig,
            out GameObject detachedRoot,
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
                if (trimDetachedRig && !TryCompactDetachedBonePalette(
                    item.build.detachedMesh, mappedBones, out mappedBones,
                    out string paletteError))
                {
                    error = $"Could not compact detached renderer '{item.source.name}': " +
                        paletteError;
                    return false;
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
            if (trimDetachedRig)
                TrimDetachedHierarchy(boneMap[sourceSkeletonRoot], detachedTargetBone);
            return true;
        }

        internal static void TrimDetachedHierarchy(Transform detachedSkeletonRoot,
            Transform detachedTargetBone)
        {
            var retained = new HashSet<Transform>();
            var stack = new Stack<Transform>();
            stack.Push(detachedTargetBone);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current == null || !retained.Add(current)) continue;
                for (int child = current.childCount - 1; child >= 0; child--)
                    stack.Push(current.GetChild(child));
            }
            Transform ancestor = detachedTargetBone.parent;
            while (ancestor != null)
            {
                retained.Add(ancestor);
                if (ancestor == detachedSkeletonRoot) break;
                ancestor = ancestor.parent;
            }
            PruneDetachedBranches(detachedSkeletonRoot, retained);
        }

        private static void PruneDetachedBranches(Transform parent,
            HashSet<Transform> retained)
        {
            for (int childIndex = parent.childCount - 1; childIndex >= 0; childIndex--)
            {
                Transform child = parent.GetChild(childIndex);
                if (retained.Contains(child))
                {
                    PruneDetachedBranches(child, retained);
                    continue;
                }
                child.SetParent(null, true);
                DestroyOwnedObject(child.gameObject);
            }
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

        internal static bool TryCompactDetachedBonePalette(Mesh mesh, Transform[] sourceBones,
            out Transform[] compactBones, out string error)
        {
            compactBones = sourceBones;
            error = string.Empty;
            if (mesh == null || sourceBones == null || sourceBones.Length == 0) return true;

            NativeArray<byte> sourceCounts = default;
            NativeArray<BoneWeight1> sourceWeights = default;
            NativeArray<byte> compactCounts = default;
            NativeArray<BoneWeight1> compactWeights = default;
            try
            {
                // Unity owns the arrays returned by these Mesh accessors. They are non-owning
                // views: never Dispose them, and copy their data before modifying this Mesh.
                sourceCounts = mesh.GetBonesPerVertex();
                sourceWeights = mesh.GetAllBoneWeights();
                if (sourceCounts.Length != mesh.vertexCount)
                {
                    error = $"the mesh has {sourceCounts.Length} bone-count entries for " +
                        $"{mesh.vertexCount} vertices.";
                    return false;
                }

                int expectedWeightCount = 0;
                for (int i = 0; i < sourceCounts.Length; i++)
                    expectedWeightCount += sourceCounts[i];
                if (expectedWeightCount != sourceWeights.Length)
                {
                    error = $"the mesh bone counts reference {expectedWeightCount} weights, " +
                        $"but the mesh contains {sourceWeights.Length}.";
                    return false;
                }

                var used = new bool[sourceBones.Length];
                for (int i = 0; i < sourceWeights.Length; i++)
                {
                    int boneIndex = sourceWeights[i].boneIndex;
                    if ((uint)boneIndex >= (uint)sourceBones.Length)
                    {
                        error = $"weight {i} references bone index {boneIndex}, but the renderer " +
                            $"has {sourceBones.Length} bones.";
                        return false;
                    }
                    if (sourceWeights[i].weight > 0f) used[boneIndex] = true;
                }

                int usedCount = 0;
                for (int i = 0; i < used.Length; i++) if (used[i]) usedCount++;
                if (usedCount == sourceBones.Length) return true;
                if (usedCount == 0)
                {
                    error = "the detached mesh contains no positive bone weights.";
                    return false;
                }

                Matrix4x4[] sourceBindposes = mesh.bindposes;
                if (sourceBindposes.Length != sourceBones.Length)
                {
                    error = $"the mesh has {sourceBindposes.Length} bind poses but the renderer " +
                        $"has {sourceBones.Length} bones.";
                    return false;
                }

                var remap = new int[sourceBones.Length];
                Array.Fill(remap, -1);
                compactBones = new Transform[usedCount];
                var compactBindposes = new Matrix4x4[usedCount];
                int write = 0;
                for (int oldIndex = 0; oldIndex < sourceBones.Length; oldIndex++)
                {
                    if (!used[oldIndex]) continue;
                    remap[oldIndex] = write;
                    compactBones[write] = sourceBones[oldIndex];
                    compactBindposes[write] = sourceBindposes[oldIndex];
                    write++;
                }
                compactCounts = new NativeArray<byte>(sourceCounts.Length, Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                compactWeights = new NativeArray<BoneWeight1>(sourceWeights.Length, Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                NativeArray<byte>.Copy(sourceCounts, compactCounts);
                for (int i = 0; i < sourceWeights.Length; i++)
                {
                    BoneWeight1 weight = sourceWeights[i];
                    weight.boneIndex = remap[weight.boneIndex];
                    compactWeights[i] = weight;
                }

                // Drop the non-owning views before SetBoneWeights changes the Mesh's native
                // storage. Only the independent Temp arrays cross this boundary.
                sourceCounts = default;
                sourceWeights = default;

                mesh.SetBoneWeights(compactCounts, compactWeights);
                mesh.bindposes = compactBindposes;
                return true;
            }
            finally
            {
                // sourceCounts/sourceWeights are non-owning Mesh views in Unity 6.3.
                if (compactCounts.IsCreated) compactCounts.Dispose();
                if (compactWeights.IsCreated) compactWeights.Dispose();
            }
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
            RebindRendererMesh(pending.source, state.dismembermentOwnedMesh);
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
                    RebindRendererMesh(snapshot.state.renderer, snapshot.previousRendererMesh);
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
                        RebindRendererMesh(state.renderer, state.umaOwnedMesh);
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

        private static void RebindRendererMesh(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            if (renderer == null) return;
            bool wasEnabled = renderer.enabled;
            renderer.enabled = false;
            renderer.sharedMesh = null;
            renderer.sharedMesh = mesh;
            renderer.enabled = wasEnabled;
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

        private static float NormalizeCenteredCapUvPadding(float padding)
        {
            if (padding <= 0f) padding = DefaultCenteredCapUvPadding;
            return Mathf.Clamp(padding, 0.001f, 0.25f);
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
