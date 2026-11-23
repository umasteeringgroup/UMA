#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using UMA.Editors;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>
    /// A body made in code, so the tests need no race content: an upright cylinder (a torso) with a
    /// closed top (the shoulders), skinned to a lower and an upper bone by height.
    /// </summary>
    internal sealed class CapsuleBodyFixture : IDisposable
    {
        public readonly GameObject Root;
        public readonly Mesh Mesh;
        public readonly SkinnedMeshRenderer Renderer;
        public readonly Transform Lower, Upper;
        public readonly BodyReference Body;
        public const float Radius = 0.16f, Bottom = 0.7f, Top = 1.6f;

        public CapsuleBodyFixture(int segments = 48, int rings = 30)
        {
            Root = new GameObject("Capsule Body (test)") { hideFlags = HideFlags.HideAndDontSave };
            Lower = new GameObject("Lower").transform; Lower.SetParent(Root.transform, false); Lower.localPosition = new Vector3(0f, Bottom, 0f);
            Upper = new GameObject("Upper").transform; Upper.SetParent(Lower, false); Upper.localPosition = new Vector3(0f, (Top - Bottom) * 0.5f, 0f);

            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float y = Mathf.Lerp(Bottom, Top, j / (float)rings);
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    vertices.Add(new Vector3(Mathf.Cos(a) * Radius, y, Mathf.Sin(a) * Radius));
                    weights.Add(WeightAt(y));
                }
            }
            for (int j = 0; j < rings; j++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int i1 = (i + 1) % segments;
                    int p00 = j * segments + i, p10 = j * segments + i1, p01 = (j + 1) * segments + i, p11 = (j + 1) * segments + i1;
                    // Unity's front faces: these face out of the body.
                    triangles.AddRange(new[] { p00, p01, p10, p10, p01, p11 });
                }
            }
            int center = vertices.Count;
            vertices.Add(new Vector3(0f, Top, 0f));
            weights.Add(WeightAt(Top));
            int topRing = rings * segments;
            for (int i = 0; i < segments; i++) triangles.AddRange(new[] { center, topRing + (i + 1) % segments, topRing + i });

            Mesh = new Mesh { name = "Capsule Body (test)" };
            Mesh.SetVertices(vertices);
            Mesh.SetTriangles(triangles, 0);
            Mesh.boneWeights = weights.ToArray();
            Transform[] bones = { Lower, Upper };
            Mesh.bindposes = new[] { Lower.worldToLocalMatrix * Root.transform.localToWorldMatrix, Upper.worldToLocalMatrix * Root.transform.localToWorldMatrix };
            Mesh.RecalculateBounds();
            Renderer = Root.AddComponent<SkinnedMeshRenderer>();
            Renderer.sharedMesh = Mesh;
            Renderer.bones = bones;
            Renderer.rootBone = Lower;

            var skin = new UMASlotSkinTransfer.TargetData
            {
                Renderer = Renderer,
                SurfaceRenderers = new[] { Renderer },
                SourceMesh = Mesh,
                Bones = bones,
                BindPoses = Mesh.bindposes,
                BoneNameHashes = new[] { UMAUtils.StringToHash(Lower.name), UMAUtils.StringToHash(Upper.name) },
                RootBone = Lower,
                RootBoneName = Lower.name,
                RootBoneHash = UMAUtils.StringToHash(Lower.name)
            };
            UMASlotSkinTransfer.TargetSurface surface = UMASlotSkinTransfer.BuildTargetSurface(skin);
            Body = new BodyReference
            {
                Skin = skin,
                Surface = surface,
                Collider = new BodyCollider(surface.Triangles),
                ArmVertex = new bool[surface.VertexWeights.Length],
                FallbackBone = 1,
                Revision = "capsule"
            };
            Body.Bounds = Body.Collider.BodyBounds;
            Body.Landmarks = new BodyLandmarks
            {
                valid = true,
                shoulderTopY = Top,
                chestY = Top - 0.15f,
                waistY = Top - 0.4f,
                hipY = Top - 0.65f,
                torsoCenter = Vector2.zero,
                heightCm = (Top - Bottom) * 100f
            };
            Body.Landmarks.chestCm = Body.Circumference(Body.Landmarks.chestY, out _) * 100f;
            Body.Landmarks.waistCm = Body.Circumference(Body.Landmarks.waistY, out _) * 100f;
            Body.Landmarks.hipCm = Body.Circumference(Body.Landmarks.hipY, out _) * 100f;
        }

        private static BoneWeight WeightAt(float y)
        {
            float t = Mathf.Clamp01((y - Bottom) / (Top - Bottom));
            return new BoneWeight { boneIndex0 = 0, weight0 = 1f - t, boneIndex1 = 1, weight1 = t };
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh);
        }
    }
}
#endif
