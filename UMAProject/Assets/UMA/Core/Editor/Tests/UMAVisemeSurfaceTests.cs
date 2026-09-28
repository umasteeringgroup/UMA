#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Tests the skinned surface, not just bone pivots: lip bones can be separated
    /// while the weighted lip surfaces still seal the mouth (the original U bug).
    /// A small CPU depth rasterizer measures the visible inner mouth without a GPU,
    /// lighting, textures, image thresholds, or a render-pipeline dependency.
    /// </summary>
    public sealed class UMAVisemeSurfaceTests
    {
        const string SlotFolder = "Assets/UMA/UMA3/Races/Slots/";
        const int Resolution = 160;
        const float Field = .09f;

        [TestCase("HumanFemale30", false)]
        [TestCase("HumanFemale30", true)]
        [TestCase("HumanMale30", false)]
        [TestCase("HumanMale30", true)]
        public void SpeechShapesHaveVisibleAperturesAndDistinctRounding(string raceName, bool mild)
        {
            using (var fixture = new SurfaceFixture(raceName, mild))
            {
                var measurements = new Dictionary<string, Mouth>();
                foreach (string name in UMAVisemePoseBuilder.Names)
                {
                    fixture.Player.ResetAllExpressions(ExpressionSource.Manual);
                    fixture.Player.SetExpression("viseme_" + name, 1);
                    fixture.Player.EvaluateExpressionsNow();
                    fixture.Player.ApplyRigExpressionsNow();
                    var mouth = fixture.Measure();
                    measurements.Add(name, mouth);
                    TestContext.WriteLine($"{raceName} {(mild ? "mild" : "full")} {name}: " +
                        $"visible inner mouth {mouth.Area:F1} mm2, central opening {mouth.Opening:F2} mm, width {mouth.Width:F2} mm");
                }

                // Silence and PP are intentionally not open vowels. Every other
                // target must expose a real mouth aperture, even in the mild set.
                foreach (string name in UMAVisemePoseBuilder.Names.Where(n => n != "sil" && n != "PP"))
                {
                    Assert.That(measurements[name].Area, Is.GreaterThan(12f), name + " must expose the inner mouth");
                    Assert.That(measurements[name].Opening, Is.GreaterThan(1.5f), name + " must not seal the central lips");
                }
                Assert.That(measurements["U"].Opening, Is.GreaterThan(3f), "U is an open, rounded vowel, not a closed pout");
                Assert.That(measurements["sil"].Opening, Is.InRange(.5f, 2.5f), "Silence must have a subtle visible lip parting, not a seal or vowel opening");
                Assert.That(measurements["sil"].Opening, Is.GreaterThan(measurements["PP"].Opening + .4f));
                Assert.That(measurements["U"].Opening, Is.GreaterThan(measurements["sil"].Opening + 1.5f));
                Assert.That(measurements["PP"].Opening, Is.LessThan(1.5f), "PP must retain its lip seal");
                Assert.That(measurements["aa"].Opening, Is.GreaterThan(measurements["U"].Opening * 1.3f));
                Assert.That(measurements["U"].Width, Is.LessThan(measurements["I"].Width * .85f));
                Assert.That(measurements["O"].Width, Is.LessThan(measurements["I"].Width * .9f));
                Assert.That(measurements["CH"].Width, Is.LessThan(measurements["SS"].Width * .9f));
            }
        }

        internal struct Mouth
        {
            public float Area, Opening, Width;
        }

        internal sealed class SurfaceFixture : IDisposable
        {
            public readonly UMAData Data;
            public readonly DynamicExpressionPlayer Player;
            readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
            readonly List<Mesh> sourceMeshes = new List<Mesh>();
            readonly Vector3 origin, up, right, forward;

            public SurfaceFixture(string raceName, bool mild)
            {
                var race = AssetDatabase.LoadAssetAtPath<RaceData>("Assets/UMA/UMA3/Races/" + raceName + ".asset");
                Assert.That(race, Is.Not.Null);
                Data = new GameObject("Viseme surface regression").AddComponent<UMAData>();
                Data.umaRecipe = new UMAData.UMARecipe();
                Data.umaRecipe.SetRace(race);
                Data.umaRecipe.InitializeDNA();
                Data.SetupSkeleton();
                var paths = Enumerable.Range(1001, 5)
                    .Select(tile => SlotFolder + "UMA30_Body/UMA30_Body_UDIM" + tile + "_slot.asset")
                    .Concat(new[] { SlotFolder + "UMA30_InnerMouth_slot.asset" }).ToArray();
                // Resolve the inner-mouth asset by type, not its importer filename.
                paths[5] = AssetDatabase.FindAssets("UMA30_InnerMouth_slot t:SlotDataAsset", new[] { SlotFolder.TrimEnd('/') })
                    .Select(AssetDatabase.GUIDToAssetPath).Single();
                var slots = paths.Select(AssetDatabase.LoadAssetAtPath<SlotDataAsset>).ToArray();
                foreach (var slot in slots)
                {
                    Assert.That(slot, Is.Not.Null);
                    foreach (var bone in slot.meshData.umaBones) Data.skeleton.EnsureBone(bone);
                }
                Data.skeleton.EnsureBoneHierarchy();
                foreach (var slot in slots)
                {
                    var md = slot.meshData;
                    var go = new GameObject(slot.name); go.transform.SetParent(Data.transform, false);
                    var skin = go.AddComponent<SkinnedMeshRenderer>();
                    skin.sharedMesh = new Mesh(); sourceMeshes.Add(skin.sharedMesh);
                    md.CopyDataToUnityMesh(skin);
                    skin.bones = Data.skeleton.HashesToTransforms(md.boneNameHashes);
                    skin.rootBone = Data.skeleton.GetRootTransform();
                    var vertices = skin.sharedMesh.vertices;
                    foreach (var shape in race.PrebakedBlendshapes)
                    {
                        var blend = md.blendShapes?.FirstOrDefault(b => b != null && b.shapeName == shape.BlendShape);
                        if (blend == null) continue;
                        var frame = blend.frames[blend.frames.Length - 1];
                        for (int i = 0; i < vertices.Length; i++) vertices[i] += frame.deltaVertices[i] * shape.value;
                    }
                    skin.sharedMesh.vertices = vertices;
                    skins.Add(skin);
                }
                Data.ResetToTPoseAndApplyDNA(); Data.skeleton.EndSkeletonUpdate();
                var head = Data.skeleton.GetBoneTransform("Head");
                var upper = Data.skeleton.GetBoneTransform("LipsSuperior").position;
                var lower = Data.skeleton.GetBoneTransform("LipsInferior").position;
                origin = (upper + lower) * .5f;
                up = head.up.normalized;
                forward = Vector3.ProjectOnPlane(upper - head.position, up).normalized;
                right = Vector3.Cross(up, forward).normalized;
                Player = Data.gameObject.AddComponent<DynamicExpressionPlayer>();
                Player.expressionGroupOverride = AssetDatabase.LoadAssetAtPath<UMAExpressionGroup>(mild ? UMAVisemePoseBuilder.MildGroupPath : UMAVisemePoseBuilder.GroupPath);
                Player.EnableBlinking = false; Player.EnableSaccades = false; Player.EnableLookAt = false;
                Player.processDistance = 0; Player.Rebind();
            }

            public Mouth Measure()
            {
                var depths = Enumerable.Repeat(float.NegativeInfinity, Resolution * Resolution).ToArray();
                var inner = new bool[depths.Length];
                var baked = new Mesh();
                try
                {
                    foreach (var skin in skins)
                    {
                        skin.BakeMesh(baked);
                        var points = baked.vertices.Select(v => Project(skin.transform.TransformPoint(v))).ToArray();
                        var triangles = baked.triangles;
                        bool mouth = skin.name.Contains("InnerMouth");
                        for (int i = 0; i < triangles.Length; i += 3)
                            Raster(points[triangles[i]], points[triangles[i + 1]], points[triangles[i + 2]], mouth, depths, inner);
                    }
                }
                finally { Object.DestroyImmediate(baked); }
                int count = 0, minX = Resolution, maxX = -1, centerMin = Resolution, centerMax = -1;
                for (int y = 0; y < Resolution; y++)
                for (int x = 0; x < Resolution; x++)
                {
                    if (!inner[y * Resolution + x]) continue;
                    count++; minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    if (Mathf.Abs(x + .5f - Resolution * .5f) <= 2)
                    { centerMin = Mathf.Min(centerMin, y); centerMax = Mathf.Max(centerMax, y); }
                }
                float pixelMm = Field * 1000 / Resolution;
                return new Mouth { Area = count * pixelMm * pixelMm,
                    Opening = Mathf.Max(0, centerMax - centerMin + 1) * pixelMm,
                    Width = Mathf.Max(0, maxX - minX + 1) * pixelMm };
            }

            Vector3 Project(Vector3 world)
            {
                Vector3 p = world - origin;
                return new Vector3((Vector3.Dot(p, right) / Field + .5f) * Resolution,
                    (Vector3.Dot(p, up) / Field + .5f) * Resolution, Vector3.Dot(p, forward));
            }

            static float Cross(Vector3 a, Vector3 b) => a.x * b.y - a.y * b.x;
            static void Raster(Vector3 a, Vector3 b, Vector3 c, bool mouth, float[] depths, bool[] inner)
            {
                float area = Cross(b - a, c - a);
                if (Mathf.Abs(area) < 1e-6f) return;
                int x0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.x, b.x, c.x) - .5f));
                int x1 = Mathf.Min(Resolution - 1, Mathf.FloorToInt(Mathf.Max(a.x, b.x, c.x) - .5f));
                int y0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.y, b.y, c.y) - .5f));
                int y1 = Mathf.Min(Resolution - 1, Mathf.FloorToInt(Mathf.Max(a.y, b.y, c.y) - .5f));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector3(x + .5f, y + .5f, 0);
                    float u = Cross(b - p, c - p) / area;
                    float v = Cross(c - p, a - p) / area;
                    float w = 1 - u - v;
                    if (u < 0 || v < 0 || w < 0) continue;
                    float z = u * a.z + v * b.z + w * c.z;
                    int index = y * Resolution + x;
                    if (z <= depths[index]) continue;
                    depths[index] = z; inner[index] = mouth;
                }
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Data.gameObject);
                foreach (var mesh in sourceMeshes) Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif
