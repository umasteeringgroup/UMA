#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.Dismemberment.Tests
{
    public sealed class SurfaceCutTests
    {
        private readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void RaycastCapturesPosedTriangleNormalAndAtlasUv()
        {
            CreateSurface(out UMASurfaceCutSystem system, out _, Vector3.zero);

            bool hit = system.TryGetSurfacePoint(
                new Ray(new Vector3(0f, 0f, 1f), Vector3.back),
                out SurfaceCutPoint point);

            Assert.That(hit, Is.True);
            Assert.That(point.IsValid, Is.True);
            Assert.That(point.SubmeshIndex, Is.Zero);
            Assert.That(point.WorldPosition, Is.EqualTo(Vector3.zero).Using(Vector3Comparer(0.001f)));
            Assert.That(point.WorldNormal.z, Is.GreaterThan(0.99f));
            Assert.That(point.AtlasUV, Is.EqualTo(new Vector2(0.5f, 0.5f))
                .Using(Vector2Comparer(0.001f)));
        }

        [Test]
        public void CutRejectsPointsOnDifferentGeneratedRenderers()
        {
            CreateSurface(out UMASurfaceCutSystem system, out DynamicCharacterAvatar avatar,
                Vector3.zero);
            SkinnedMeshRenderer second = CreateRenderer(avatar.transform,
                new Vector3(2f, 0f, 0f));
            avatar.SetRenderers(new[] { avatar.GetRenderers()[0], second });
            Assert.That(system.TryGetSurfacePoint(
                new Ray(new Vector3(0f, 0f, 1f), Vector3.back), out SurfaceCutPoint first),
                Is.True);
            Assert.That(system.TryGetSurfacePoint(
                new Ray(new Vector3(2f, 0f, 1f), Vector3.back), out SurfaceCutPoint other),
                Is.True);

            bool created = system.TryCreateCut(first, other, null,
                out _, out string error);

            Assert.That(created, Is.False);
            StringAssert.Contains("same body or armor surface", error);
        }

        [Test]
        public void BleedSourceCountScalesWithMetricCutLength()
        {
            float[] shortCut = UMASurfaceCutSystem.CalculateBleedDistances(
                0.1f, 0.025f, 0f, 0f, 123u);
            float[] longCut = UMASurfaceCutSystem.CalculateBleedDistances(
                0.2f, 0.025f, 0f, 0f, 123u);

            Assert.That(shortCut.Length, Is.EqualTo(4));
            Assert.That(longCut.Length, Is.EqualTo(8));
            Assert.That(longCut.Length, Is.EqualTo(shortCut.Length * 2));
        }

        [Test]
        public void BleedSpacingVariationIsBoundedAndDeterministic()
        {
            const float spacing = 0.025f;
            const float variation = 0.3f;
            float[] first = UMASurfaceCutSystem.CalculateBleedDistances(
                0.3f, spacing, variation, 0.1f, 991u);
            float[] repeated = UMASurfaceCutSystem.CalculateBleedDistances(
                0.3f, spacing, variation, 0.1f, 991u);

            CollectionAssert.AreEqual(first, repeated);
            Assert.That(first.Length, Is.GreaterThan(1));
            Assert.That(first[0], Is.GreaterThanOrEqualTo(0.03f));
            Assert.That(first[first.Length - 1], Is.LessThanOrEqualTo(0.27f));
            for (int i = 1; i < first.Length; i++)
            {
                float interval = first[i] - first[i - 1];
                Assert.That(interval, Is.InRange(spacing * (1f - variation),
                    spacing * (1f + variation)));
            }
        }

        [Test]
        public void BleedSpacingSupportsDryShortAndBoundedDenseCuts()
        {
            Assert.That(UMASurfaceCutSystem.CalculateBleedDistances(
                0.2f, 0f, 0.3f, 0.1f, 1u), Is.Empty);
            Assert.That(UMASurfaceCutSystem.CalculateBleedDistances(
                0.01f, 0.025f, 0.3f, 0.1f, 1u).Length, Is.EqualTo(1));
            float[] dense = UMASurfaceCutSystem.CalculateBleedDistances(
                10f, 0.001f, 0f, 0f, 1u);
            Assert.That(dense.Length, Is.EqualTo(UMASurfaceCutSystem.MaximumBleedSources));
            Assert.That(dense[dense.Length - 1], Is.GreaterThan(9.9f));
        }

        [Test]
        public void PerDripSpeedAndSizeVariationIsBoundedAndDeterministic()
        {
            UMASurfaceCutSystem.CalculateBleedVariations(24, 0.25f, 0.4f, 8128u,
                out float[] speeds, out float[] sizes);
            UMASurfaceCutSystem.CalculateBleedVariations(24, 0.25f, 0.4f, 8128u,
                out float[] repeatedSpeeds, out float[] repeatedSizes);

            CollectionAssert.AreEqual(speeds, repeatedSpeeds);
            CollectionAssert.AreEqual(sizes, repeatedSizes);
            Assert.That(speeds, Has.Some.Not.EqualTo(1f));
            Assert.That(sizes, Has.Some.Not.EqualTo(1f));
            for (int i = 0; i < speeds.Length; i++)
            {
                Assert.That(speeds[i], Is.InRange(0.75f, 1.25f));
                Assert.That(sizes[i], Is.InRange(0.6f, 1.4f));
            }
        }

        private void CreateSurface(out UMASurfaceCutSystem system,
            out DynamicCharacterAvatar avatar, Vector3 position)
        {
            var root = Own(new GameObject("Surface Cut Test Avatar"));
            root.SetActive(false);
            avatar = root.AddComponent<DynamicCharacterAvatar>();
            system = root.AddComponent<UMASurfaceCutSystem>();
            SkinnedMeshRenderer renderer = CreateRenderer(root.transform, position);
            avatar.SetRenderers(new[] { renderer });
            root.SetActive(true);
        }

        private SkinnedMeshRenderer CreateRenderer(Transform parent, Vector3 position)
        {
            var host = Own(new GameObject("Surface"));
            host.transform.SetParent(parent, false);
            host.transform.localPosition = position;
            var renderer = host.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = Own(new Mesh { name = "Surface Cut Test Quad" });
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                Vector2.zero, Vector2.right, Vector2.up, Vector2.one
            };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            mesh.boneWeights = new[]
            {
                FullWeight(), FullWeight(), FullWeight(), FullWeight()
            };
            mesh.bindposes = new[]
            {
                parent.worldToLocalMatrix * host.transform.localToWorldMatrix
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            renderer.sharedMesh = mesh;
            renderer.rootBone = parent;
            renderer.bones = new[] { parent };
            renderer.localBounds = mesh.bounds;
            return renderer;
        }

        private static BoneWeight FullWeight() => new BoneWeight
        {
            boneIndex0 = 0,
            weight0 = 1f
        };

        private T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            return value;
        }

        private static IEqualityComparer<Vector3> Vector3Comparer(float tolerance) =>
            new ApproximateVector3Comparer(tolerance);
        private static IEqualityComparer<Vector2> Vector2Comparer(float tolerance) =>
            new ApproximateVector2Comparer(tolerance);

        private sealed class ApproximateVector3Comparer : IEqualityComparer<Vector3>
        {
            private readonly float tolerance;
            public ApproximateVector3Comparer(float tolerance) { this.tolerance = tolerance; }
            public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;
            public int GetHashCode(Vector3 value) => 0;
        }

        private sealed class ApproximateVector2Comparer : IEqualityComparer<Vector2>
        {
            private readonly float tolerance;
            public ApproximateVector2Comparer(float tolerance) { this.tolerance = tolerance; }
            public bool Equals(Vector2 x, Vector2 y) => Vector2.Distance(x, y) <= tolerance;
            public int GetHashCode(Vector2 value) => 0;
        }
    }
}
#endif
