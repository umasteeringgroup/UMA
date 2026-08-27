using System.Collections.Generic;
using UMA;
using UMA.Dismemberment;
using UMA.Dynamics;
using UnityEngine;

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

    [Header("Legacy Simple Physics")]
    [Tooltip("Fallback single-body sample physics. It is used when detached ragdoll physics is " +
        "off, or when ragdoll construction fails.")]
    public bool addPhysics = true;

    private UmaDismemberment dismemberment;

    private void OnEnable()
    {
        dismemberment = GetComponent<UmaDismemberment>();
        if (dismemberment != null)
            dismemberment.DismembermentCompleted.AddListener(DismemberedCallback);
    }

    private void OnDisable()
    {
        if (dismemberment != null)
            dismemberment.DismembermentCompleted.RemoveListener(DismemberedCallback);
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
