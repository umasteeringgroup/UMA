using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    [CreateAssetMenu(menuName = "UMA/Hair Cards/Hair Groom", fileName = "HairGroom")]
    public sealed class HairGroomAsset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string groomId;
        [SerializeField] private Mesh sourceMesh;
        [SerializeField] private string sourceMeshId;
        [SerializeField] private string sourceRace;
        [SerializeField] private string sourceSlot;
        [SerializeField] private string sourceTopologySignature;
        [SerializeField] private bool symmetryEnabled = true;
        [SerializeField] private Vector3 symmetryPlaneNormal = Vector3.right;
        [SerializeField] private Vector3 symmetryPlanePoint;
        [SerializeField] private List<HairGroup> groups = new List<HairGroup>();
        [SerializeField] private List<HairHelper> sharedHelpers = new List<HairHelper>();
        [SerializeField] private List<HairLodSettings> lods = new List<HairLodSettings>();
        [SerializeField] private HairBakeSettings bakeSettings = new HairBakeSettings();

        public int SchemaVersion => schemaVersion;
        public string GroomId => groomId;
        public Mesh SourceMesh => sourceMesh;
        public string SourceMeshId => sourceMeshId;
        public string SourceRace => sourceRace;
        public string SourceSlot => sourceSlot;
        public string SourceTopologySignature => sourceTopologySignature;
        public bool SymmetryEnabled { get => symmetryEnabled; set => symmetryEnabled = value; }
        public Vector3 SymmetryPlaneNormal => symmetryPlaneNormal;
        public Vector3 SymmetryPlanePoint => symmetryPlanePoint;
        public List<HairGroup> Groups => groups;
        public List<HairHelper> SharedHelpers => sharedHelpers;
        public List<HairLodSettings> Lods => lods;
        public HairBakeSettings BakeSettings => bakeSettings;
        public int SourceVertexCount => sourceMesh != null ? sourceMesh.vertexCount : 0;

        public void SetSource(Mesh mesh, string stableMeshId, string raceName = null, string slotName = null)
        {
            sourceMesh = mesh;
            sourceMeshId = string.IsNullOrWhiteSpace(stableMeshId)
                ? CreateMeshFallbackId(mesh)
                : stableMeshId;
            sourceRace = raceName ?? string.Empty;
            sourceSlot = slotName ?? string.Empty;
            sourceTopologySignature = HairMeshUtility.ComputeTopologySignature(mesh);
            EnsureIntegrity();
        }

        public void SetSymmetryPlane(Vector3 point, Vector3 normal)
        {
            symmetryPlanePoint = point;
            symmetryPlaneNormal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.right;
        }

        public HairGroup CreateGroup(string groupName, HairGroupRole role)
        {
            groups ??= new List<HairGroup>();
            HairGroup group = new HairGroup
            {
                name = string.IsNullOrWhiteSpace(groupName) ? role.ToString() : groupName,
                role = role,
                color = GetDefaultGroupColor(groups.Count)
            };
            group.EnsureIntegrity(SourceVertexCount);
            groups.Add(group);
            return group;
        }

        public HairGroup FindGroup(string groupId)
        {
            return groups?.Find(group => group != null && group.Id == groupId);
        }

        public HairGuide FindGuide(string guideId, out HairGroup owner)
        {
            owner = null;
            if (groups == null || string.IsNullOrEmpty(guideId)) return null;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                HairGroup group = groups[groupIndex];
                HairGuide guide = group?.guides?.Find(candidate => candidate != null && candidate.Id == guideId);
                if (guide == null) continue;
                owner = group;
                return guide;
            }
            return null;
        }

        public HairHelper FindHelper(string helperId)
        {
            return sharedHelpers?.Find(helper => helper != null && helper.Id == helperId);
        }

        public IEnumerable<HairGuide> EnumerateGuides(bool visibleEnabledGroupsOnly = true)
        {
            if (groups == null) yield break;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                HairGroup group = groups[groupIndex];
                if (group == null || (visibleEnabledGroupsOnly && (!group.visible || !group.enabled))) continue;
                if (group.guides == null) continue;
                for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
                {
                    HairGuide guide = group.guides[guideIndex];
                    if (guide != null && (!visibleEnabledGroupsOnly || guide.enabled)) yield return guide;
                }
            }
        }

        public void EnsureIntegrity()
        {
            schemaVersion = CurrentSchemaVersion;
            HairStableId.Ensure(ref groomId);
            if (string.IsNullOrEmpty(sourceMeshId) && sourceMesh != null)
            {
                sourceMeshId = CreateMeshFallbackId(sourceMesh);
            }
            if (string.IsNullOrEmpty(sourceTopologySignature) && sourceMesh != null)
            {
                sourceTopologySignature = HairMeshUtility.ComputeTopologySignature(sourceMesh);
            }

            symmetryPlaneNormal = symmetryPlaneNormal.sqrMagnitude > 1e-8f
                ? symmetryPlaneNormal.normalized
                : Vector3.right;
            groups ??= new List<HairGroup>();
            sharedHelpers ??= new List<HairHelper>();
            lods ??= new List<HairLodSettings>();
            bakeSettings ??= new HairBakeSettings();
            bakeSettings.triangleBudget = Mathf.Max(1, bakeSettings.triangleBudget);
            bakeSettings.cardBudget = Mathf.Max(1, bakeSettings.cardBudget);
            if (string.IsNullOrWhiteSpace(bakeSettings.outputFolder))
                bakeSettings.outputFolder = "Assets/UMAProjectData/HairCards/Generated";
            if (string.IsNullOrWhiteSpace(bakeSettings.assetName)) bakeSettings.assetName = "HairCards";

            if (groups.Count == 0)
            {
                CreateGroup("Coverage", HairGroupRole.Coverage);
            }
            for (int i = 0; i < groups.Count; i++) groups[i]?.EnsureIntegrity(SourceVertexCount);
            for (int i = 0; i < sharedHelpers.Count; i++) sharedHelpers[i]?.EnsureIntegrity();

            if (lods.Count == 0)
            {
                lods.Add(new HairLodSettings { name = "LOD 0", level = 0, screenRelativeHeight = 0.6f });
            }
            for (int i = 0; i < lods.Count; i++)
            {
                if (lods[i] == null) lods[i] = new HairLodSettings { name = $"LOD {i}", level = i };
                lods[i].EnsureIntegrity();
            }
            lods.Sort((left, right) => left.level.CompareTo(right.level));
        }

        public bool SourceTopologyMatches()
        {
            return sourceMesh != null &&
                   string.Equals(sourceTopologySignature,
                       HairMeshUtility.ComputeTopologySignature(sourceMesh), StringComparison.Ordinal);
        }

        public void AcceptCurrentSourceTopology()
        {
            sourceTopologySignature = HairMeshUtility.ComputeTopologySignature(sourceMesh);
            EnsureIntegrity();
        }

        private void OnEnable()
        {
            EnsureIntegrity();
        }

        private void OnValidate()
        {
            EnsureIntegrity();
        }

        private static string CreateMeshFallbackId(Mesh mesh)
        {
            if (mesh == null) return string.Empty;
            return $"mesh:{mesh.name}:{HairMeshUtility.ComputeTopologySignature(mesh)}";
        }

        private static Color GetDefaultGroupColor(int index)
        {
            Color[] colors =
            {
                new Color(0.22f, 0.65f, 1f, 1f),
                new Color(1f, 0.48f, 0.24f, 1f),
                new Color(0.54f, 0.82f, 0.32f, 1f),
                new Color(0.82f, 0.42f, 0.9f, 1f),
                new Color(1f, 0.78f, 0.22f, 1f),
                new Color(0.2f, 0.85f, 0.76f, 1f)
            };
            return colors[Mathf.Abs(index) % colors.Length];
        }
    }
}
