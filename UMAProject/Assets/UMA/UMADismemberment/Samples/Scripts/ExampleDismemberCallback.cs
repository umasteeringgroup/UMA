using UMA;
using UMA.Dismemberment;
using UnityEngine;

/// <summary>Shows how to augment the rich multi-renderer dismemberment result.</summary>
public sealed class ExampleDismemberCallback : MonoBehaviour
{
    public string boneName;
    public SkinnedMeshRenderer gibSplit;
    public Material gibSplitMaterial;
    public SkinnedMeshRenderer gibSource;
    public Material gibSourceMaterial;
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
        if (result?.targetBone == null || result.targetBone.name != boneName) return;
        if (addPhysics)
        {
            if (result.root.GetComponent<Rigidbody>() == null)
                result.root.gameObject.AddComponent<Rigidbody>();
            SphereCollider collider = result.targetBone.GetComponent<SphereCollider>();
            if (collider == null) collider = result.targetBone.gameObject.AddComponent<SphereCollider>();
            collider.center = new Vector3(-0.22f, 0f, 0.05f);
            collider.radius = 0.12f;
        }

        SkinnedMeshRenderer detachedTarget = FirstRenderer(result.detachedRenderers);
        SkinnedMeshRenderer sourceTarget = FirstRenderer(result.sourceRenderers);
        SkinnedMeshRenderer detachedGib = CreateChildRenderer(gibSplit, detachedTarget);
        if (detachedGib != null && gibSplitMaterial != null)
            detachedGib.sharedMaterial = gibSplitMaterial;
        SkinnedMeshRenderer sourceGib = CreateChildRenderer(gibSource, sourceTarget);
        if (sourceGib != null && gibSourceMaterial != null)
            sourceGib.sharedMaterial = gibSourceMaterial;
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
        Transform[] targetBones = target.bones;
        var targetByHash = new System.Collections.Generic.Dictionary<int, Transform>();
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
