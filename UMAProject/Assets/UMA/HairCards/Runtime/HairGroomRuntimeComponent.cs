using UnityEngine;

namespace UMA.HairCards.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class HairGroomRuntimeComponent : MonoBehaviour
    {
        [SerializeField] private HairGroomAsset groom;
        [SerializeField, Min(0)] private int lodLevel;
        [SerializeField] private bool generateOnEnable = true;
        [SerializeField] private Material fallbackMaterial;
        [SerializeField] private bool includeChildren = true;

        private HairCardMeshBuildResult currentBuild;
        private HairEvaluationResult currentEvaluation;

        public HairGroomAsset Groom => groom;
        public HairEvaluationResult CurrentEvaluation => currentEvaluation;
        public Mesh CurrentMesh => currentBuild?.mesh;

        private void OnEnable()
        {
            if (generateOnEnable) Regenerate();
        }

        private void OnDisable()
        {
            ReleaseGeneratedMesh();
        }

        public void SetGroom(HairGroomAsset value, bool regenerate = true)
        {
            groom = value;
            if (regenerate) Regenerate();
        }

        public bool Regenerate()
        {
            ReleaseGeneratedMesh();
            if (groom == null) return false;
            currentEvaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions
            {
                lodLevel = lodLevel,
                includeChildren = includeChildren,
                includeGuideCards = true,
                applyConstraints = true,
                applyModifiers = true,
                applySculptLayers = true
            });
            currentBuild = HairCardMeshGenerator.Build(currentEvaluation, $"{groom.name} Runtime Hair");
            MeshFilter filter = GetComponent<MeshFilter>();
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            filter.sharedMesh = currentBuild.mesh;
            Material[] materials = new Material[Mathf.Max(1, currentBuild.materials.Count)];
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = i < currentBuild.materials.Count ? currentBuild.materials[i] : null;
                materials[i] = source != null ? source : fallbackMaterial;
            }
            renderer.sharedMaterials = materials;
            return currentBuild.mesh != null && currentBuild.cardCount > 0;
        }

        public HairValidationReport ValidateCurrent(HairValidationOptions options = null)
        {
            return HairValidator.Validate(groom, currentEvaluation, currentBuild, options);
        }

        public void ReleaseGeneratedMesh()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter != null && currentBuild != null && filter.sharedMesh == currentBuild.mesh)
            {
                filter.sharedMesh = null;
            }
            currentBuild?.Dispose();
            currentBuild = null;
            currentEvaluation = null;
        }
    }
}
