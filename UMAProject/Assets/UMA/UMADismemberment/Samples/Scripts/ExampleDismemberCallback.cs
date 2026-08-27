using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UMA.Dismemberment;
using UMA.Dynamics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Shows how to augment the rich multi-renderer dismemberment result.</summary>
public sealed class ExampleDismemberCallback : MonoBehaviour
{
    public string boneName;
    public SkinnedMeshRenderer gibSplit;
    public Material gibSplitMaterial;
    public SkinnedMeshRenderer gibSource;
    public Material gibSourceMaterial;

    [Header("Detached Physics")]
    [Tooltip("Build a partial ragdoll on the detached skeleton from the assigned UMA physics " +
        "definitions.")]
    public bool ragdollDismemberedParts;
    [Tooltip("Fallback definitions for legacy or generic Transform cuts. Humanoid cuts use " +
        "Detached Physics Definitions from their Sliceable Human Bones row when assigned.")]
    [InspectorName("Fallback Physics Definitions")]
    public List<UMAPhysicsElement> physicsDefinitions = new List<UMAPhysicsElement>();
    [Range(0, 31), Tooltip("Layer assigned to detached ragdoll bones. This should use the same " +
        "collision matrix as the U3 ragdoll sample.")]
    public int ragdollLayer = 8;
    [Tooltip("Camera whose view direction pushes the detached part. Camera.main is used when " +
        "this is empty.")]
    public Camera viewCamera;
    [Min(0f), Tooltip("Small momentum impulse in kilogram-meters per second, applied away from " +
        "the view through the body.")]
    public float separationImpulse = 0.5f;

    [Header("Blood")]
    [Tooltip("Particle prefab spawned at the cut. The U3 ragdoll sample Blood prefab is " +
        "compatible and destroys itself when emission finishes.")]
    public GameObject bloodParticleEmitter;
    [Tooltip("Optional non-destructive GPU surface-fluid profile. When assigned, the example " +
        "starts a fadeable flow from the actual cut UV boundary without rebuilding UMA.")]
    public UMASurfaceFluidProfile surfaceFluidProfile;

    [Header("Click-to-Bleed Bullet Decal")]
    [Tooltip("Adds the U3-Decals-style left-click wound demo to this sample avatar. The handler " +
        "uses the cached decal UVs to start standalone surface bleeding.")]
    public bool enableClickToBleedBulletDecals = true;
    [Tooltip("Optional override. When empty, the handler resolves the U3-Decals DecalOverlay " +
        "through the UMA asset index.")]
    public OverlayDataAsset bulletDecalOverlay;
    [Tooltip("Optional destination overlay group for DecalRTStampSlot atlas-event replay. The " +
        "sample controller retains its wound by recorded slot identity, so this can remain empty.")]
    public string bulletTargetOverlayGroup;
    [Tooltip("Optional click camera. View Camera and then Camera.main are used as fallbacks.")]
    public Camera bulletDecalCamera;
    [Tooltip("Optional puncture-specific fluid settings. When empty, the sample uses a narrow " +
        "millimeter-scale seeping profile instead of reusing the broader cut profile.")]
    public UMASurfaceFluidProfile bulletFluidProfile;
    [Min(0.001f)] public float bulletRadiusMeters = 0.035f;
    [Min(0f)] public float bulletEdgeFudgeMeters = 0.004f;
    public bool randomizeBulletRotation = true;
    [Range(0f, 360f)] public float bulletRotationDegrees;
    [Range(0, 16)] public int bulletDilationPixels = 2;
    [Range(0f, 4f)] public float bulletUvExpansionPixels = 0.75f;

    [Header("Legacy Simple Physics")]
    [Tooltip("Fallback single-body sample physics. It is used when detached ragdoll physics is " +
        "off, or when ragdoll construction fails.")]
    public bool addPhysics = true;

    private UmaDismemberment dismemberment;
    private UMARuntimeSurfaceDecalController surfaceDecals;
    private DynamicCharacterAvatar avatar;
    private UMASurfaceFluidProfile ownedDefaultBulletFluidProfile;
    private bool warnedMissingBulletOverlay;

    private void OnEnable()
    {
        dismemberment = GetComponent<UmaDismemberment>();
        avatar = GetComponent<DynamicCharacterAvatar>();
        surfaceDecals = GetComponent<UMARuntimeSurfaceDecalController>();
        if ((surfaceFluidProfile != null || enableClickToBleedBulletDecals) &&
            surfaceDecals == null)
            surfaceDecals = gameObject.AddComponent<UMARuntimeSurfaceDecalController>();
        if (dismemberment != null)
            dismemberment.DismembermentCompleted.AddListener(DismemberedCallback);
    }

    private void OnDisable()
    {
        if (dismemberment != null)
            dismemberment.DismembermentCompleted.RemoveListener(DismemberedCallback);
    }

    private void OnDestroy()
    {
        if (ownedDefaultBulletFluidProfile == null) return;
        if (Application.isPlaying) Destroy(ownedDefaultBulletFluidProfile);
        else DestroyImmediate(ownedDefaultBulletFluidProfile);
        ownedDefaultBulletFluidProfile = null;
    }

    private void Update()
    {
        if (!enableClickToBleedBulletDecals) return;
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        TryPlaceBleedingBullet(mouse.position.ReadValue());
    }

    public bool TryPlaceBleedingBullet(Vector2 screenPosition)
    {
        if (avatar == null) avatar = GetComponent<DynamicCharacterAvatar>();
        Camera camera = bulletDecalCamera != null ? bulletDecalCamera :
            viewCamera != null ? viewCamera : Camera.main;
        if (avatar == null || avatar.umaData == null || camera == null) return false;
        OverlayDataAsset overlay = ResolveBulletOverlay();
        if (overlay == null)
        {
            if (!warnedMissingBulletOverlay)
            {
                warnedMissingBulletOverlay = true;
                Debug.LogWarning("Click-to-bleed needs an OverlayDataAsset. Assign one or keep " +
                    "the U3-Decals sample DecalOverlay indexed.", this);
            }
            return false;
        }

        float angle = randomizeBulletRotation
            ? Random.Range(0f, 360f) : bulletRotationDegrees;
        var options = new DecalRenderTexture.DecalRTOptions
        {
            layerMask = ~0,
            facingThreshold = 0.15f,
            enableDebug = false,
            forceLinearSampling = false,
            useHitNormalForProjection = true,
            uvExpandPixels = bulletUvExpansionPixels,
            bleedPixels = bulletDilationPixels,
            targetOverlayGroup = ResolveBulletTargetOverlayGroup(overlay),
            allowTransientStampWithoutOverlayGroup = true
        };
        DecalRenderTexture.DecalLayerResult? result = DecalRenderTexture.CreateDecalLayer(
            avatar, camera.ScreenPointToRay(screenPosition), bulletRadiusMeters,
            bulletEdgeFudgeMeters, angle, avatar.umaData, overlay, options);
        if (!result.HasValue || !result.Value.success) return false;

        if (surfaceDecals == null)
            surfaceDecals = GetComponent<UMARuntimeSurfaceDecalController>();
        if (surfaceDecals == null)
            surfaceDecals = gameObject.AddComponent<UMARuntimeSurfaceDecalController>();
        DecalRTStampAsset stamp = DecalRenderTexture.LastStamp;
        RuntimeDecalHandle woundHandle = surfaceDecals.AddPersistentStamp(stamp);
        RuntimeDecalHandle bleedHandle = surfaceDecals.StartBleedFromDecal(
            stamp, ResolveBulletFluidProfile(), result.Value);
        if (woundHandle.IsValid && bleedHandle.IsValid) return true;

        IReadOnlyList<string> diagnostics = surfaceDecals.Diagnostics;
        string detail = diagnostics.Count > 0 ? diagnostics[diagnostics.Count - 1] :
            "No compatible generated UMA material was found.";
        string failedPart = !woundHandle.IsValid && !bleedHandle.IsValid
            ? "the persistent wound and bleeding"
            : !woundHandle.IsValid ? "the persistent wound" : "bleeding";
        Debug.LogWarning("The bullet hit was created, but " + failedPart +
            " could not start: " + detail, this);
        return false;
    }

    private OverlayDataAsset ResolveBulletOverlay()
    {
        if (bulletDecalOverlay != null) return bulletDecalOverlay;
        if (UMAAssetIndexer.Instance == null) return null;
        bulletDecalOverlay = UMAAssetIndexer.Instance.GetAsset<OverlayDataAsset>("DecalOverlay");
        return bulletDecalOverlay;
    }

    private string ResolveBulletTargetOverlayGroup(OverlayDataAsset overlay)
    {
        if (!string.IsNullOrWhiteSpace(bulletTargetOverlayGroup))
            return bulletTargetOverlayGroup.Trim();
        return overlay != null && !string.IsNullOrWhiteSpace(overlay.overlayGroup)
            ? overlay.overlayGroup.Trim() : null;
    }

    private UMASurfaceFluidProfile ResolveBulletFluidProfile()
    {
        if (bulletFluidProfile != null) return bulletFluidProfile;
        if (ownedDefaultBulletFluidProfile != null) return ownedDefaultBulletFluidProfile;
        ownedDefaultBulletFluidProfile =
            ScriptableObject.CreateInstance<UMASurfaceFluidProfile>();
        ownedDefaultBulletFluidProfile.name = "Dismemberment Sample Runtime Blood";
        ownedDefaultBulletFluidProfile.hideFlags = HideFlags.HideAndDontSave;
        // A puncture should seep from a millimeter-scale source and leave a narrow residue,
        // rather than inject across the full visible wound decal.
        ownedDefaultBulletFluidProfile.emissionDuration = 2.5f;
        ownedDefaultBulletFluidProfile.emissionRate = 0.0006f;
        ownedDefaultBulletFluidProfile.emissionRadiusMeters = 0.0015f;
        ownedDefaultBulletFluidProfile.fallSpeedMetersPerSecond = 0.045f;
        ownedDefaultBulletFluidProfile.maximumTravelMeters = 0.75f;
        ownedDefaultBulletFluidProfile.viscosity = 0.58f;
        ownedDefaultBulletFluidProfile.adhesion = 0.5f;
        ownedDefaultBulletFluidProfile.lateralSpread = 0.018f;
        ownedDefaultBulletFluidProfile.pooling = 0.35f;
        ownedDefaultBulletFluidProfile.trailDepositionPerMeter = 4.5f;
        return ownedDefaultBulletFluidProfile;
    }

    private void DismemberedCallback(DismembermentResult result)
    {
        if (result?.targetBone == null) return;
        IReadOnlyList<UMAPhysicsElement> resolvedDefinitions = physicsDefinitions;
        bool hasPerCutDefinitions = false;
        DismemberedPhysicsMode physicsMode = DismemberedPhysicsMode.Automatic;
        if (ragdollDismemberedParts)
        {
            resolvedDefinitions = ResolvePhysicsDefinitions(result, out hasPerCutDefinitions,
                out physicsMode);
            resolvedDefinitions = DismemberedRagdollBuilder.FilterDefinitionsForCutSubtree(
                result.targetBone, resolvedDefinitions);
        }
        if (!hasPerCutDefinitions && result.targetBone.name != boneName) return;
        Vector3 cutPosition = result.targetBone.position;
        SpawnBlood(cutPosition);
        if (surfaceFluidProfile != null && surfaceDecals != null)
            surfaceDecals.StartBleed(result, surfaceFluidProfile);

        DismemberedRagdollBuildResult detachedPhysics = null;
        bool addedDetachedColliders = false;
        bool suppressPhysics = ragdollDismemberedParts &&
            physicsMode == DismemberedPhysicsMode.None;
        if (ragdollDismemberedParts && !suppressPhysics)
        {
            DismemberedPhysicsMode resolvedMode = DismemberedRagdollBuilder.ResolvePhysicsMode(
                physicsMode, resolvedDefinitions);
            string physicsError;
            bool built = resolvedMode == DismemberedPhysicsMode.Rigid
                ? DismemberedRagdollBuilder.TryBuildRigid(result.root, resolvedDefinitions,
                    ragdollLayer, out detachedPhysics, out physicsError)
                : DismemberedRagdollBuilder.TryBuild(result.root, resolvedDefinitions,
                    ragdollLayer, out detachedPhysics, out physicsError);
            if (!built)
            {
                Debug.LogWarning($"Could not create detached physics for " +
                    $"'{result.targetBone.name}': {physicsError}", this);
            }
            else
            {
                SetDetachedRenderersAlwaysUpdate(result.detachedRenderers);
                detachedPhysics.ApplyImpulse(ResolveViewImpulse());
                addedDetachedColliders = detachedPhysics.colliders != null &&
                    detachedPhysics.colliders.Length > 0;
            }
        }

        if (!suppressPhysics && detachedPhysics == null && addPhysics)
        {
            Rigidbody simpleBody = AddSimplePhysics(result);
            addedDetachedColliders = simpleBody != null;
            if (ragdollDismemberedParts && simpleBody != null)
                simpleBody.AddForce(ResolveViewImpulse(), ForceMode.Impulse);
        }

        if (addedDetachedColliders && dismemberment != null)
            dismemberment.SuspendSourceRagdollColliders(result.sourceTargetBone);

        SkinnedMeshRenderer detachedTarget = FirstRenderer(result.detachedRenderers);
        SkinnedMeshRenderer sourceTarget = FirstRenderer(result.sourceRenderers);
        SkinnedMeshRenderer detachedGib = CreateChildRenderer(gibSplit, detachedTarget);
        if (detachedGib != null && gibSplitMaterial != null)
            detachedGib.sharedMaterial = gibSplitMaterial;
        SkinnedMeshRenderer sourceGib = CreateChildRenderer(gibSource, sourceTarget);
        if (sourceGib != null && gibSourceMaterial != null)
            sourceGib.sharedMaterial = gibSourceMaterial;
    }

    private IReadOnlyList<UMAPhysicsElement> ResolvePhysicsDefinitions(
        DismembermentResult result, out bool hasPerCutDefinitions,
        out DismemberedPhysicsMode physicsMode)
    {
        hasPerCutDefinitions = false;
        physicsMode = DismemberedPhysicsMode.Automatic;
        if (dismemberment != null && result.humanBone != HumanBodyBones.LastBone &&
            dismemberment.TryGetBoneSettings(result.humanBone,
                out UmaDismemberment.BoneInfo settings) &&
            ContainsDefinition(settings.physicsDefinitions))
        {
            hasPerCutDefinitions = true;
            physicsMode = settings.physicsMode;
            return settings.physicsDefinitions;
        }
        return physicsDefinitions;
    }

    private static bool ContainsDefinition(IReadOnlyList<UMAPhysicsElement> definitions)
    {
        if (definitions == null) return false;
        for (int i = 0; i < definitions.Count; i++)
            if (definitions[i] != null) return true;
        return false;
    }

    private Rigidbody AddSimplePhysics(DismembermentResult result)
    {
        Rigidbody body = result.root.GetComponent<Rigidbody>();
        if (body == null) body = result.root.gameObject.AddComponent<Rigidbody>();
        SphereCollider collider = result.targetBone.GetComponent<SphereCollider>();
        if (collider == null)
            collider = result.targetBone.gameObject.AddComponent<SphereCollider>();
        collider.center = new Vector3(-0.22f, 0f, 0.05f);
        collider.radius = 0.12f;
        return body;
    }

    private Vector3 ResolveViewImpulse()
    {
        if (separationImpulse <= 0f) return Vector3.zero;
        Camera camera = viewCamera != null ? viewCamera : Camera.main;
        Vector3 direction = camera != null ? camera.transform.forward : transform.forward;
        if (direction.sqrMagnitude <= 0.000001f) direction = Vector3.forward;
        return direction.normalized * separationImpulse;
    }

    private void SpawnBlood(Vector3 position)
    {
        if (bloodParticleEmitter != null)
            Instantiate(bloodParticleEmitter, position, Quaternion.identity);
    }

    private static void SetDetachedRenderersAlwaysUpdate(SkinnedMeshRenderer[] renderers)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].updateWhenOffscreen = true;
    }

    private static SkinnedMeshRenderer FirstRenderer(SkinnedMeshRenderer[] renderers)
    {
        if (renderers == null) return null;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) return renderers[i];
        return null;
    }

    private static SkinnedMeshRenderer CreateChildRenderer(SkinnedMeshRenderer prefab,
        SkinnedMeshRenderer target)
    {
        if (prefab == null || target == null) return null;
        SkinnedMeshRenderer renderer = Instantiate(prefab, target.transform);
        var targetByHash = new System.Collections.Generic.Dictionary<int, Transform>();
        var stack = new Stack<Transform>();
        if (target.rootBone != null) stack.Push(target.rootBone);
        while (stack.Count > 0)
        {
            Transform bone = stack.Pop();
            targetByHash[UMAUtils.StringToHash(bone.name)] = bone;
            for (int child = bone.childCount - 1; child >= 0; child--)
                stack.Push(bone.GetChild(child));
        }
        Transform[] targetBones = target.bones;
        for (int i = 0; i < targetBones.Length; i++)
        {
            Transform bone = targetBones[i];
            if (bone != null) targetByHash[UMAUtils.StringToHash(bone.name)] = bone;
        }
        Transform[] remapped = renderer.bones;
        for (int i = 0; i < remapped.Length; i++)
        {
            Transform sourceBone = remapped[i];
            if (sourceBone != null && targetByHash.TryGetValue(
                UMAUtils.StringToHash(sourceBone.name), out Transform mapped)) remapped[i] = mapped;
        }
        renderer.bones = remapped;
        if (renderer.rootBone != null && targetByHash.TryGetValue(
            UMAUtils.StringToHash(renderer.rootBone.name), out Transform mappedRoot))
            renderer.rootBone = mappedRoot;
        else renderer.rootBone = target.rootBone;
        return renderer;
    }
}
