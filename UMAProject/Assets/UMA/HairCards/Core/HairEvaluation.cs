using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    [Serializable]
    public struct HairCurvePoint
    {
        public Vector3 position;
        public float width;
        public float roll;

        public HairCurvePoint(Vector3 position, float width, float roll)
        {
            this.position = position;
            this.width = width;
            this.roll = roll;
        }
    }

    public sealed class HairEvaluatedCurve
    {
        public string curveId;
        public string parentGuideId;
        public string groupId;
        public bool isChild;
        public int seed;
        public Color groupColor;
        public Vector3 rootNormal = Vector3.up;
        public HairCardProfileAsset profile;
        public HairAtlasProfileAsset atlas;
        public HairAtlasRegionSelectionMode atlasRegionSelection;
        public string[] atlasRegionIds = Array.Empty<string>();
        public int samplesPerCardOverride;
        public int tubeSidesOverride;
        public readonly List<HairCurvePoint> points = new List<HairCurvePoint>();

        public float Length => HairCurveUtility.CalculateLength(points);

        public HairEvaluatedCurve Clone(string newCurveId = null)
        {
            HairEvaluatedCurve clone = new HairEvaluatedCurve
            {
                curveId = newCurveId ?? curveId,
                parentGuideId = parentGuideId,
                groupId = groupId,
                isChild = isChild,
                seed = seed,
                groupColor = groupColor,
                rootNormal = rootNormal,
                profile = profile,
                atlas = atlas,
                atlasRegionSelection = atlasRegionSelection,
                atlasRegionIds = atlasRegionIds != null ? (string[])atlasRegionIds.Clone() : Array.Empty<string>(),
                samplesPerCardOverride = samplesPerCardOverride,
                tubeSidesOverride = tubeSidesOverride
            };
            clone.points.AddRange(points);
            return clone;
        }
    }

    public sealed class HairEvaluationOptions
    {
        public int lodLevel;
        public bool includeGuideCards = true;
        public bool includeChildren = true;
        public bool applySculptLayers = true;
        public bool applyModifiers = true;
        public bool applyConstraints = true;
        public bool includeHiddenGroups = true;
        public int interactiveSampleLimit;
    }

    public sealed class HairEvaluationResult
    {
        public readonly List<HairEvaluatedCurve> evaluatedGuides = new List<HairEvaluatedCurve>();
        public readonly List<HairEvaluatedCurve> curves = new List<HairEvaluatedCurve>();
        public readonly List<string> warnings = new List<string>();
        public int guideCurveCount;
        public int childCurveCount;
        public int rejectedCurveCount;
        public int revision;

        public int CardCount => curves.Count;
    }

    public sealed class HairCardMeshBuildResult : IDisposable
    {
        public Mesh mesh;
        public readonly List<Material> materials = new List<Material>();
        public readonly List<string> materialNames = new List<string>();
        public int cardCount;
        public int vertexCount;
        public int triangleCount;
        public int degenerateTriangleCount;
        public int frameFlipCount;

        public void Dispose()
        {
            if (mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
            mesh = null;
        }
    }

    internal struct HairDeterministicRandom
    {
        private uint state;

        public HairDeterministicRandom(int seed)
        {
            state = (uint)seed;
            if (state == 0) state = 0x9e3779b9u;
        }

        public uint NextUInt()
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }

        public float Next01()
        {
            return (NextUInt() & 0x00ffffffu) / 16777215f;
        }

        public float NextSigned()
        {
            return Next01() * 2f - 1f;
        }

        public Vector2 NextInUnitDisk()
        {
            float radius = Mathf.Sqrt(Next01());
            float angle = Next01() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
    }
}
