using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.CharacterSystem
{
    [DisallowMultipleComponent]
    public class UMAFbxRouteRuntime : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private SkinnedMeshRenderer sourceRenderer;
        [SerializeField, HideInInspector]
        private GameObject baseInstanceRoot;
        [SerializeField, HideInInspector]
        private SkinnedMeshRenderer baseRendererInstance;

        private const string BaseInstanceNameSuffix = " (FBX Route)";
        private static readonly Dictionary<Mesh, int[][]> originalTrianglesByMesh = new Dictionary<Mesh, int[][]>();

        private Mesh originalBaseMesh;
        private Mesh hiddenBaseMesh;
        private int[][] originalSubMeshTriangles;
        private Transform originalRootBone;
        private Transform[] originalBones;
        private TransformState[] originalTransformStates;
        private Material[] runtimeMaterials;
        private readonly List<Object> ownedObjects = new List<Object>();

        private struct TransformState
        {
            public Transform Transform;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;

            public TransformState(Transform transform)
            {
                Transform = transform;
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
            }

            public void Restore()
            {
                if (Transform == null)
                {
                    return;
                }

                Transform.localPosition = LocalPosition;
                Transform.localRotation = LocalRotation;
                Transform.localScale = LocalScale;
            }
        }

        public SkinnedMeshRenderer BaseRenderer
        {
            get { return baseRendererInstance; }
        }

        public Transform SkeletonRoot
        {
            get
            {
                if (baseRendererInstance == null)
                {
                    return null;
                }
                return baseRendererInstance.rootBone != null ? baseRendererInstance.rootBone : baseRendererInstance.transform;
            }
        }

        public bool Prepare(RaceData raceData, UMAData umaData)
        {
            if (raceData == null || !raceData.HasValidFbxRoute)
            {
                Teardown(umaData);
                return false;
            }

            if (sourceRenderer != null && sourceRenderer != raceData.baseFbxRenderer)
            {
                Teardown(umaData);
            }
            sourceRenderer = raceData.baseFbxRenderer;

            if (baseInstanceRoot != null && sourceRenderer != null && !IsRouteInstanceName(baseInstanceRoot.name, GetBaseInstanceName(sourceRenderer.transform.root.gameObject)))
            {
                Teardown(umaData);
                sourceRenderer = raceData.baseFbxRenderer;
            }

            if (!HasUsableBaseInstance(sourceRenderer))
            {
                if (!TryAdoptExistingBaseInstance(sourceRenderer) && !CreateBaseInstance(sourceRenderer))
                {
                    return false;
                }
            }
            else
            {
                RemoveDuplicateBaseInstances(sourceRenderer, baseInstanceRoot);
            }

            if (originalBaseMesh == null || originalSubMeshTriangles == null || originalTransformStates == null)
            {
                SetBaseInstanceState(sourceRenderer, baseInstanceRoot, baseRendererInstance);
            }

            RestoreBaseRendererState();
            PruneGeneratedBones();
            RestoreOriginalTransformStates();
            EnsureRuntimeMaterialAndTextureCopies();

            if (umaData != null)
            {
                umaData.SetExternalSkeletonRoot(SkeletonRoot, baseRendererInstance);
            }

            return SkeletonRoot != null;
        }

        public void ApplyBaseMeshHides(RaceData raceData, Dictionary<string, List<MeshHideAsset>> meshHideDictionary, int lod)
        {
            if (baseRendererInstance == null || originalBaseMesh == null || raceData == null || meshHideDictionary == null)
            {
                return;
            }

            RestoreBaseRendererState();

            if (raceData.fbxBaseMeshHideBindings == null || raceData.fbxBaseMeshHideBindings.Count == 0)
            {
                return;
            }

            Mesh workingMesh = null;
            for (int i = 0; i < raceData.fbxBaseMeshHideBindings.Count; i++)
            {
                RaceData.FbxBaseMeshHideBinding binding = raceData.fbxBaseMeshHideBindings[i];
                if (binding == null || string.IsNullOrEmpty(binding.slotName))
                {
                    continue;
                }

                if (!meshHideDictionary.TryGetValue(binding.slotName, out List<MeshHideAsset> hideAssets) || hideAssets == null || hideAssets.Count == 0)
                {
                    continue;
                }

#if UNITY_6000_2_OR_NEWER
                BitArray[] masks = MeshHideAsset.GenerateMask(hideAssets, Mathf.Max(0, lod));
#else
                BitArray[] masks = MeshHideAsset.GenerateMask(hideAssets);
#endif
                int maskSubMeshIndex = Mathf.Max(0, binding.maskSubMeshIndex);
                int fbxSubMeshIndex = Mathf.Max(0, binding.fbxSubMeshIndex);
                if (masks == null || maskSubMeshIndex >= masks.Length || masks[maskSubMeshIndex] == null)
                {
                    continue;
                }
                if (fbxSubMeshIndex >= originalBaseMesh.subMeshCount)
                {
                    continue;
                }

                if (workingMesh == null)
                {
                    workingMesh = GetOrCreateHiddenBaseMesh();
                    if (workingMesh == null)
                    {
                        return;
                    }
                    baseRendererInstance.sharedMesh = workingMesh;
                }

                ApplyHideMaskToSubMesh(workingMesh, fbxSubMeshIndex, masks[maskSubMeshIndex], originalSubMeshTriangles[fbxSubMeshIndex]);
            }
        }

        public void SetBaseRendererVisible(bool visible)
        {
            if (baseRendererInstance != null)
            {
                baseRendererInstance.enabled = visible;
            }
        }

        public void Teardown(UMAData umaData)
        {
            if (umaData != null)
            {
                umaData.ClearExternalSkeletonRoot(baseRendererInstance);
            }

            DestroyOwnedObjects();
            runtimeMaterials = null;
            originalBaseMesh = null;
            originalSubMeshTriangles = null;
            originalRootBone = null;
            originalBones = null;
            originalTransformStates = null;
            baseRendererInstance = null;

            if (baseInstanceRoot != null)
            {
                DestroyUnityObject(baseInstanceRoot);
                baseInstanceRoot = null;
            }
            sourceRenderer = null;
        }

        public bool OwnsRendererGameObject(GameObject rendererObject)
        {
            if (rendererObject == null || baseInstanceRoot == null)
            {
                return false;
            }

            Transform rendererTransform = rendererObject.transform;
            return rendererTransform == baseInstanceRoot.transform || rendererTransform.IsChildOf(baseInstanceRoot.transform);
        }

        private void OnDestroy()
        {
            Teardown(null);
        }

        private bool CreateBaseInstance(SkinnedMeshRenderer source)
        {
            if (source == null)
            {
                return false;
            }

            GameObject sourceRoot = source.transform.root.gameObject;
            SkinnedMeshRenderer[] sourceRenderers = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (sourceRenderers.Length != 1 || sourceRenderers[0] != source)
            {
                return false;
            }

            baseInstanceRoot = Instantiate(sourceRoot, transform, false);
            baseInstanceRoot.name = GetBaseInstanceName(sourceRoot);
            SkinnedMeshRenderer[] instanceRenderers = baseInstanceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (instanceRenderers.Length != 1)
            {
                DestroyUnityObject(baseInstanceRoot);
                ClearBaseInstanceState();
                return false;
            }

            SetBaseInstanceState(source, baseInstanceRoot, instanceRenderers[0]);
            if (originalBaseMesh == null)
            {
                DestroyUnityObject(baseInstanceRoot);
                ClearBaseInstanceState();
                return false;
            }

            RemoveDuplicateBaseInstances(source, baseInstanceRoot);
            return true;
        }

        private bool HasUsableBaseInstance(SkinnedMeshRenderer source)
        {
            if (source == null || baseInstanceRoot == null || baseRendererInstance == null)
            {
                return false;
            }

            string expectedName = GetBaseInstanceName(source.transform.root.gameObject);
            return IsRouteInstanceName(baseInstanceRoot.name, expectedName) && IsRendererForSource(baseRendererInstance, source);
        }

        private bool TryAdoptExistingBaseInstance(SkinnedMeshRenderer source)
        {
            if (source == null)
            {
                return false;
            }

            string expectedName = GetBaseInstanceName(source.transform.root.gameObject);
            GameObject adoptedRoot = null;
            SkinnedMeshRenderer adoptedRenderer = null;
            List<GameObject> duplicateRoots = null;
            Transform[] candidates = transform.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                Transform candidateRoot = candidates[i];
                if (candidateRoot == null || candidateRoot == transform || !IsRouteInstanceName(candidateRoot.gameObject.name, expectedName))
                {
                    continue;
                }

                SkinnedMeshRenderer candidateRenderer = FindRendererForSource(candidateRoot, source);

                if (adoptedRoot == null && candidateRenderer != null)
                {
                    adoptedRoot = candidateRoot.gameObject;
                    adoptedRenderer = candidateRenderer;
                }
                else
                {
                    if (duplicateRoots == null)
                    {
                        duplicateRoots = new List<GameObject>();
                    }
                    duplicateRoots.Add(candidateRoot.gameObject);
                }
            }

            DestroyDuplicateRoots(duplicateRoots, adoptedRoot);

            if (adoptedRoot == null || adoptedRenderer == null)
            {
                return false;
            }

            SetBaseInstanceState(source, adoptedRoot, adoptedRenderer);
            return true;
        }

        private void RemoveDuplicateBaseInstances(SkinnedMeshRenderer source, GameObject keepRoot)
        {
            if (source == null)
            {
                return;
            }

            string expectedName = GetBaseInstanceName(source.transform.root.gameObject);
            List<GameObject> duplicateRoots = null;
            Transform[] candidates = transform.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                Transform candidateRoot = candidates[i];
                if (candidateRoot == null || candidateRoot == transform || candidateRoot.gameObject == keepRoot || !IsRouteInstanceName(candidateRoot.gameObject.name, expectedName))
                {
                    continue;
                }

                if (duplicateRoots == null)
                {
                    duplicateRoots = new List<GameObject>();
                }
                duplicateRoots.Add(candidateRoot.gameObject);
            }

            DestroyDuplicateRoots(duplicateRoots, keepRoot);
        }

        private static void DestroyDuplicateRoots(List<GameObject> duplicateRoots, GameObject keepRoot)
        {
            if (duplicateRoots == null)
            {
                return;
            }

            for (int i = 0; i < duplicateRoots.Count; i++)
            {
                GameObject duplicateRoot = duplicateRoots[i];
                if (duplicateRoot == null || duplicateRoot == keepRoot)
                {
                    continue;
                }

                DestroyUnityObject(duplicateRoot);
            }
        }

        private static SkinnedMeshRenderer FindRendererForSource(Transform root, SkinnedMeshRenderer source)
        {
            if (root == null)
            {
                return null;
            }

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsRendererForSource(renderers[i], source))
                {
                    return renderers[i];
                }
            }

            return null;
        }

        private static bool IsRendererForSource(SkinnedMeshRenderer renderer, SkinnedMeshRenderer source)
        {
            if (renderer == null || source == null || renderer == source)
            {
                return false;
            }

            Mesh sourceMesh = source.sharedMesh;
            Mesh rendererMesh = renderer.sharedMesh;
            if (sourceMesh == null || rendererMesh == null)
            {
                return false;
            }

            return rendererMesh == sourceMesh || rendererMesh.name == sourceMesh.name + " (FBX Route Hidden)";
        }

        private static string GetBaseInstanceName(GameObject sourceRoot)
        {
            return sourceRoot.name + BaseInstanceNameSuffix;
        }

        private static bool IsRouteInstanceName(string objectName, string expectedName)
        {
            return objectName == expectedName || (!string.IsNullOrEmpty(objectName) && objectName.StartsWith(expectedName + " (", System.StringComparison.Ordinal));
        }

        private void SetBaseInstanceState(SkinnedMeshRenderer source, GameObject instanceRoot, SkinnedMeshRenderer rendererInstance)
        {
            baseInstanceRoot = instanceRoot;
            baseRendererInstance = rendererInstance;
            originalBaseMesh = source != null ? source.sharedMesh : rendererInstance.sharedMesh;
            originalRootBone = rendererInstance.rootBone;
            originalBones = rendererInstance.bones;
            CacheOriginalTriangleLists();
            CaptureOriginalTransformStates();
        }

        private void ClearBaseInstanceState()
        {
            baseInstanceRoot = null;
            baseRendererInstance = null;
            originalBaseMesh = null;
            originalSubMeshTriangles = null;
            originalRootBone = null;
            originalBones = null;
            originalTransformStates = null;
        }

        private void RestoreBaseRendererState()
        {
            if (baseRendererInstance == null)
            {
                return;
            }

            if (hiddenBaseMesh != null)
            {
                RestoreOriginalTriangles(hiddenBaseMesh);
                baseRendererInstance.sharedMesh = hiddenBaseMesh;
            }
            else if (originalBaseMesh != null)
            {
                baseRendererInstance.sharedMesh = originalBaseMesh;
            }
            if (originalRootBone != null)
            {
                baseRendererInstance.rootBone = originalRootBone;
            }
            if (originalBones != null)
            {
                baseRendererInstance.bones = originalBones;
            }
        }

        private Mesh GetOrCreateHiddenBaseMesh()
        {
            if (originalBaseMesh == null)
            {
                return null;
            }

            CacheOriginalTriangleLists();
            if (originalSubMeshTriangles == null)
            {
                return null;
            }

            if (hiddenBaseMesh == null)
            {
                hiddenBaseMesh = Instantiate(originalBaseMesh);
                hiddenBaseMesh.name = originalBaseMesh.name + " (FBX Route Hidden)";
                ownedObjects.Add(hiddenBaseMesh);
            }

            RestoreOriginalTriangles(hiddenBaseMesh);
            return hiddenBaseMesh;
        }

        private void CacheOriginalTriangleLists()
        {
            if (originalBaseMesh == null)
            {
                originalSubMeshTriangles = null;
                return;
            }

            if (!originalTrianglesByMesh.TryGetValue(originalBaseMesh, out originalSubMeshTriangles) || originalSubMeshTriangles == null || originalSubMeshTriangles.Length != originalBaseMesh.subMeshCount)
            {
                originalSubMeshTriangles = new int[originalBaseMesh.subMeshCount][];
                for (int i = 0; i < originalBaseMesh.subMeshCount; i++)
                {
                    originalSubMeshTriangles[i] = originalBaseMesh.GetTriangles(i);
                }
                originalTrianglesByMesh[originalBaseMesh] = originalSubMeshTriangles;
            }
        }

        private void RestoreOriginalTriangles(Mesh mesh)
        {
            if (mesh == null || originalSubMeshTriangles == null)
            {
                return;
            }

            if (mesh.subMeshCount != originalSubMeshTriangles.Length)
            {
                mesh.subMeshCount = originalSubMeshTriangles.Length;
            }

            for (int i = 0; i < originalSubMeshTriangles.Length; i++)
            {
                mesh.SetTriangles(originalSubMeshTriangles[i], i);
            }
        }

        private void CaptureOriginalTransformStates()
        {
            Transform root = SkeletonRoot;
            if (root == null)
            {
                originalTransformStates = null;
                return;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            originalTransformStates = new TransformState[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                originalTransformStates[i] = new TransformState(transforms[i]);
            }
        }

        private void RestoreOriginalTransformStates()
        {
            if (originalTransformStates == null)
            {
                return;
            }

            for (int i = 0; i < originalTransformStates.Length; i++)
            {
                originalTransformStates[i].Restore();
            }
        }

        private void EnsureRuntimeMaterialAndTextureCopies()
        {
            if (baseRendererInstance == null)
            {
                return;
            }

            Material[] sharedMaterials = baseRendererInstance.sharedMaterials;
            if (runtimeMaterials != null && runtimeMaterials.Length == sharedMaterials.Length)
            {
                baseRendererInstance.sharedMaterials = runtimeMaterials;
                return;
            }

            if (runtimeMaterials != null)
            {
                DestroyRuntimeMaterials();
            }

            runtimeMaterials = new Material[sharedMaterials.Length];
            Dictionary<Texture, Texture> copiedTextures = new Dictionary<Texture, Texture>();
            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material sourceMaterial = sharedMaterials[i];
                if (sourceMaterial == null)
                {
                    continue;
                }

                Material materialCopy = new Material(sourceMaterial)
                {
                    name = sourceMaterial.name + " (FBX Route Copy)"
                };
                runtimeMaterials[i] = materialCopy;
                ownedObjects.Add(materialCopy);
                CopyMaterialTextures(materialCopy, copiedTextures);
            }

            baseRendererInstance.sharedMaterials = runtimeMaterials;
        }

        private void CopyMaterialTextures(Material material, Dictionary<Texture, Texture> copiedTextures)
        {
            Shader shader = material.shader;
            if (shader == null)
            {
                return;
            }

            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                {
                    continue;
                }

                string propertyName = shader.GetPropertyName(i);
                Texture sourceTexture = material.GetTexture(propertyName);
                if (sourceTexture == null)
                {
                    continue;
                }

                if (!copiedTextures.TryGetValue(sourceTexture, out Texture textureCopy) || textureCopy == null)
                {
                    textureCopy = Instantiate(sourceTexture);
                    textureCopy.name = sourceTexture.name + " (FBX Route Copy)";
                    copiedTextures[sourceTexture] = textureCopy;
                    ownedObjects.Add(textureCopy);
                }

                material.SetTexture(propertyName, textureCopy);
            }
        }

        private void DestroyRuntimeMaterials()
        {
            if (runtimeMaterials == null)
            {
                return;
            }

            for (int i = 0; i < runtimeMaterials.Length; i++)
            {
                if (runtimeMaterials[i] != null)
                {
                    ownedObjects.Remove(runtimeMaterials[i]);
                    DestroyUnityObject(runtimeMaterials[i]);
                }
            }
            runtimeMaterials = null;
        }

        private void DestroyOwnedObjects()
        {
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
            {
                DestroyUnityObject(ownedObjects[i]);
            }
            ownedObjects.Clear();
            hiddenBaseMesh = null;
        }

        private void PruneGeneratedBones()
        {
            Transform root = SkeletonRoot;
            if (root == null)
            {
                return;
            }

            UMAGeneratedBone[] generatedBones = root.GetComponentsInChildren<UMAGeneratedBone>(true);
            for (int i = generatedBones.Length - 1; i >= 0; i--)
            {
                if (generatedBones[i] != null)
                {
                    Transform generatedTransform = generatedBones[i].transform;
                    generatedTransform.SetParent(null, false);
                    DestroyUnityObject(generatedTransform.gameObject);
                }
            }
        }

        private static void ApplyHideMaskToSubMesh(Mesh mesh, int subMeshIndex, BitArray mask, int[] sourceTriangles)
        {
            int[] triangles = sourceTriangles;
            if (triangles == null)
            {
                return;
            }

            int triangleCount = triangles.Length / 3;
            if (mask.Length != triangleCount)
            {
                return;
            }

            List<int> visibleTriangles = new List<int>(triangles.Length);
            for (int i = 0; i < triangleCount; i++)
            {
                if (mask[i])
                {
                    continue;
                }

                int triangleIndex = i * 3;
                visibleTriangles.Add(triangles[triangleIndex]);
                visibleTriangles.Add(triangles[triangleIndex + 1]);
                visibleTriangles.Add(triangles[triangleIndex + 2]);
            }

            mesh.SetTriangles(visibleTriangles, subMeshIndex);
        }

        private static void DestroyUnityObject(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
