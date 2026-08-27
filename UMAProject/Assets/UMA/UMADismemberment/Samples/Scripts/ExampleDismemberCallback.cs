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

    [Header("Click Surface Effects")]
    [Tooltip("Normal left-click places a bleeding bullet decal. Hold Shift, press the left " +
        "button for the cut start, drag, and release for the cut end.")]
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

    [Header("Click-to-Surface Cut")]
    [Tooltip("Optional cut appearance and distributed-bleed settings. Logical runtime defaults " +
        "are used when this is empty.")]
    public UMASurfaceCutProfile surfaceCutProfile;
    [Tooltip("Color of the temporary line shown while Shift-dragging a surface cut.")]
    public Color surfaceCutPreviewColor = new Color(1f, 0f, 0f, 0.95f);
    [Min(0.0005f), Tooltip("World-space width of the temporary cut line in meters.")]
    public float surfaceCutPreviewWidthMeters = 0.0015f;
    [Tooltip("Writes selection and creation diagnostics to the Console.")]
    public bool logSurfaceCutPlacement = true;

    [Header("Legacy Simple Physics")]
    [Tooltip("Fallback single-body sample physics. It is used when detached ragdoll physics is " +
        "off, or when ragdoll construction fails.")]
    public bool addPhysics = true;

    private UmaDismemberment dismemberment;
    private UMARuntimeSurfaceDecalController surfaceDecals;
    private DynamicCharacterAvatar avatar;
    private UMASurfaceFluidProfile ownedDefaultBulletFluidProfile;
    private UMASurfaceCutSystem surfaceCutSystem;
    private SurfaceCutPoint pendingSurfaceCutStart;
    private Vector2 pendingSurfaceCutStartScreen;
    private bool hasPendingSurfaceCutStart;
    private LineRenderer surfaceCutPreview;
    private Material ownedSurfaceCutPreviewMaterial;
    private bool warnedMissingBulletOverlay;

    private void OnEnable()
    {
        dismemberment = GetComponent<UmaDismemberment>();
        avatar = GetComponent<DynamicCharacterAvatar>();
        surfaceDecals = GetComponent<UMARuntimeSurfaceDecalController>();
        if ((surfaceFluidProfile != null || enableClickToBleedBulletDecals) &&
            surfaceDecals == null)
            surfaceDecals = gameObject.AddComponent<UMARuntimeSurfaceDecalController>();
        surfaceCutSystem = GetComponent<UMASurfaceCutSystem>();
        if (dismemberment != null)
            dismemberment.DismembermentCompleted.AddListener(DismemberedCallback);
    }

    private void OnDisable()
    {
        CancelPendingSurfaceCut();
        if (dismemberment != null)
            dismemberment.DismembermentCompleted.RemoveListener(DismemberedCallback);
    }

    private void OnDestroy()
    {
        DestroyOwned(ownedDefaultBulletFluidProfile);
        if (surfaceCutPreview != null)
            DestroyOwned(surfaceCutPreview.gameObject);
        DestroyOwned(ownedSurfaceCutPreviewMaterial);
        ownedDefaultBulletFluidProfile = null;
        surfaceCutPreview = null;
        ownedSurfaceCutPreviewMaterial = null;
    }

    private void Update()
    {
        if (!enableClickToBleedBulletDecals) return;
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        if ((mouse != null && mouse.rightButton.wasPressedThisFrame) ||
            (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
        {
            CancelPendingSurfaceCut();
            return;
        }
        if (mouse == null) return;
        bool pointerOverUi = EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject();
        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (pointerOverUi) return;
            if (IsShiftPressed(keyboard))
                BeginSurfaceCut(mouse.position.ReadValue());
            else
            {
                CancelPendingSurfaceCut();
                TryPlaceBleedingBullet(mouse.position.ReadValue());
            }
            return;
        }
        if (hasPendingSurfaceCutStart && mouse.leftButton.isPressed)
            UpdateSurfaceCutPreview(mouse.position.ReadValue());
        if (hasPendingSurfaceCutStart && mouse.leftButton.wasReleasedThisFrame)
        {
            if (pointerOverUi)
                CancelPendingSurfaceCut();
            else
                CompleteSurfaceCut(mouse.position.ReadValue());
        }
    }

    public bool BeginSurfaceCut(Vector2 screenPosition)
    {
        Camera camera = bulletDecalCamera != null ? bulletDecalCamera :
            viewCamera != null ? viewCamera : Camera.main;
        if (camera == null) return false;
        if (surfaceCutSystem == null) surfaceCutSystem = GetComponent<UMASurfaceCutSystem>();
        if (surfaceCutSystem == null)
            surfaceCutSystem = gameObject.AddComponent<UMASurfaceCutSystem>();
        if (!surfaceCutSystem.TryGetSurfacePoint(camera.ScreenPointToRay(screenPosition),
            out SurfaceCutPoint point))
        {
            if (logSurfaceCutPlacement)
                Debug.LogWarning("Surface Cut: mouse-down did not hit a facing UMA surface.", this);
            return false;
        }

        pendingSurfaceCutStart = point;
        pendingSurfaceCutStartScreen = screenPosition;
        hasPendingSurfaceCutStart = true;
        ShowSurfaceCutPreview(point.WorldPosition);
        Debug.DrawRay(point.WorldPosition, point.WorldNormal * 0.025f,
            new Color(1f, 0.35f, 0.35f), 10f, false);
        if (logSurfaceCutPlacement)
            Debug.Log("Surface Cut: drag across the same body or armor material and release " +
                "the left mouse button; right-click or Escape cancels.", this);
        return true;
    }

    public bool CompleteSurfaceCut(Vector2 screenPosition)
    {
        if (!hasPendingSurfaceCutStart) return false;
        Camera camera = bulletDecalCamera != null ? bulletDecalCamera :
            viewCamera != null ? viewCamera : Camera.main;
        if (camera == null || surfaceCutSystem == null)
        {
            CancelPendingSurfaceCut();
            return false;
        }
        if (!surfaceCutSystem.TryGetSurfacePoint(camera.ScreenPointToRay(screenPosition),
            out SurfaceCutPoint point))
        {
            CancelPendingSurfaceCut();
            if (logSurfaceCutPlacement)
                Debug.LogWarning("Surface Cut: mouse-up did not hit a facing UMA surface.", this);
            return false;
        }

        SurfaceCutPoint start = pendingSurfaceCutStart;
        Vector2 startScreen = pendingSurfaceCutStartScreen;
        hasPendingSurfaceCutStart = false;
        pendingSurfaceCutStart = default;
        pendingSurfaceCutStartScreen = default;
        HideSurfaceCutPreview();
        bool created = surfaceCutSystem.TryCreateProjectedCut(start, point, camera,
            startScreen, screenPosition, surfaceCutProfile, out SurfaceCutResult result,
            out string error);
        if (!created)
        {
            if (logSurfaceCutPlacement)
                Debug.LogWarning("Surface Cut: " + error, this);
            return false;
        }
        Debug.DrawLine(start.WorldPosition, point.WorldPosition,
            new Color(0.8f, 0.05f, 0.08f), 10f, false);
        if (logSurfaceCutPlacement)
            Debug.Log($"Surface Cut: created a {result.LengthMeters:F3} meter cut with " +
                $"{result.BleedSourceCount} bleed source(s).", this);
        return true;
    }

    private static bool IsShiftPressed(Keyboard keyboard)
    {
        return keyboard != null &&
            (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
    }

    public void CancelPendingSurfaceCut()
    {
        pendingSurfaceCutStart = default;
        pendingSurfaceCutStartScreen = default;
        hasPendingSurfaceCutStart = false;
        HideSurfaceCutPreview();
    }

    private void ShowSurfaceCutPreview(Vector3 start)
    {
        EnsureSurfaceCutPreview();
        if (surfaceCutPreview == null) return;
        surfaceCutPreview.startColor = surfaceCutPreviewColor;
        surfaceCutPreview.endColor = surfaceCutPreviewColor;
        float width = Mathf.Max(0.0005f, surfaceCutPreviewWidthMeters);
        surfaceCutPreview.startWidth = width;
        surfaceCutPreview.endWidth = width;
        surfaceCutPreview.SetPosition(0, start);
        surfaceCutPreview.SetPosition(1, start);
        surfaceCutPreview.enabled = true;
    }

    private void UpdateSurfaceCutPreview(Vector2 screenPosition)
    {
        if (surfaceCutPreview == null || !surfaceCutPreview.enabled) return;
        Camera camera = bulletDecalCamera != null ? bulletDecalCamera :
            viewCamera != null ? viewCamera : Camera.main;
        if (camera == null) return;
        Vector3 start = pendingSurfaceCutStart.WorldPosition;
        Vector3 projected = camera.WorldToScreenPoint(start);
        if (projected.z <= 0f) return;
        Vector3 end = camera.ScreenToWorldPoint(
            new Vector3(screenPosition.x, screenPosition.y, projected.z));
        surfaceCutPreview.SetPosition(0, start);
        surfaceCutPreview.SetPosition(1, end);
    }

    private void HideSurfaceCutPreview()
    {
        if (surfaceCutPreview != null) surfaceCutPreview.enabled = false;
    }

    private void EnsureSurfaceCutPreview()
    {
        if (surfaceCutPreview != null) return;
        Shader shader = Resources.Load<Shader>(
            "UMA/Dismemberment/SurfaceCutPreview");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            if (logSurfaceCutPlacement)
                Debug.LogWarning("Surface Cut: preview shader could not be loaded.", this);
            return;
        }
        ownedSurfaceCutPreviewMaterial = new Material(shader)
        {
            name = "UMA Surface Cut Preview",
            hideFlags = HideFlags.HideAndDontSave
        };
        if (ownedSurfaceCutPreviewMaterial.HasProperty("_Color"))
            ownedSurfaceCutPreviewMaterial.SetColor("_Color", Color.white);
        var previewObject = new GameObject("UMA Surface Cut Preview")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        previewObject.transform.SetParent(transform, false);
        surfaceCutPreview = previewObject.AddComponent<LineRenderer>();
        surfaceCutPreview.sharedMaterial = ownedSurfaceCutPreviewMaterial;
        surfaceCutPreview.useWorldSpace = true;
        surfaceCutPreview.positionCount = 2;
        surfaceCutPreview.alignment = LineAlignment.View;
        surfaceCutPreview.textureMode = LineTextureMode.Stretch;
        surfaceCutPreview.numCapVertices = 2;
        surfaceCutPreview.numCornerVertices = 2;
        surfaceCutPreview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        surfaceCutPreview.receiveShadows = false;
        surfaceCutPreview.enabled = false;
    }

    private static void DestroyOwned(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
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
