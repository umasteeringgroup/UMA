#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Cloth Motion: how far each vertex may swing at run time, as UMA's Unity Cloth reads it.</summary>
    public sealed class ClothMotionTests
    {
        private const string Folder = "Assets/UMAProjectData/ClothesDesigner/Tests_Temp_Motion";
        private const int Length = 21;

        private static Vector3[] Strip()
        {
            var p = new Vector3[Length * 2];
            for (int j = 0; j < Length; j++) { p[j * 2] = new Vector3(0f, -j * 0.01f, 0f); p[j * 2 + 1] = new Vector3(0.01f, -j * 0.01f, 0f); }
            return p;
        }

        private static List<int> StripTriangles()
        {
            var t = new List<int>();
            for (int j = 0; j + 1 < Length; j++)
            {
                int a = j * 2, b = j * 2 + 1, c = (j + 1) * 2, d = (j + 1) * 2 + 1;
                t.AddRange(new[] { a, c, b, b, c, d });
            }
            return t;
        }

        private static int[] NoSeams(int n)
        {
            var g = new int[n];
            for (int i = 0; i < n; i++) g[i] = i;
            return g;
        }

        [Test]
        public void MaxDistances_GrowWithTheClothBetweenAVertexAndWhereItIsHeld_UpToTheMost()
        {
            int n = Length * 2;
            var distance = new float[n];
            for (int v = 0; v < n; v++) distance[v] = v / 2 == 0 ? 0f : 0.1f;
            float[] swing = ClothMotion.MaxDistances(Strip(), NoSeams(n), StripTriangles(), distance, 0.015f, 0.3f, 0.05f);
            for (int j = 0; j < Length; j++)
            {
                float expected = Mathf.Min(0.05f, 0.3f * 0.01f * j);
                Assert.That(swing[j * 2], Is.EqualTo(expected).Within(1e-5f), "row " + j);
                Assert.That(swing[j * 2 + 1], Is.EqualTo(expected).Within(1e-5f), "row " + j);
            }
        }

        [Test]
        public void MaxDistances_ClothHeldNowhere_SwingsTheMost()
        {
            int n = Length * 2;
            var distance = new float[n];
            for (int v = 0; v < n; v++) distance[v] = 0.2f;
            foreach (float s in ClothMotion.MaxDistances(Strip(), NoSeams(n), StripTriangles(), distance, 0.015f, 0.3f, 0.05f)) Assert.That(s, Is.EqualTo(0.05f));
        }

        [Test]
        public void Serialize_GivesOneCoefficientPerPosition_InTheOrderTheyFirstAppear()
        {
            var vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 0), new Vector3(2, 0, 0) };
            Vector2[] c = ClothMotion.Serialize(vertices, new[] { 0.1f, 0.2f, 0.9f, 0.3f }, 0.004f);
            Assert.That(c.Length, Is.EqualTo(3));
            // x is the collision sphere distance and y the max distance, as UMA's SkinnedMeshCombiner.ConvertData reads them.
            Assert.That(c[0], Is.EqualTo(new Vector2(0.004f, 0.1f)));
            Assert.That(c[1], Is.EqualTo(new Vector2(0.004f, 0.2f)));
            Assert.That(c[2], Is.EqualTo(new Vector2(0.004f, 0.3f)));
        }

        [Test]
        public void Settle_FirmFabricIsStifferThanLimpFabric()
        {
            var firm = ScriptableObject.CreateInstance<UMAClothProperties>();
            var limp = ScriptableObject.CreateInstance<UMAClothProperties>();
            try
            {
                ClothMotion.Settle(firm, new FabricSettings { stretchCompliance = 1e-8f, bendCompliance = 1e-5f });
                ClothMotion.Settle(limp, new FabricSettings { stretchCompliance = 0.08f, bendCompliance = 0.1f });
                Assert.That(firm.stretchingStiffness, Is.GreaterThan(limp.stretchingStiffness));
                Assert.That(firm.bendingStiffness, Is.GreaterThan(limp.bendingStiffness));
                Assert.That(firm.useGravity, Is.True);
                Assert.That(firm.clothSolverFrequency, Is.GreaterThan(0f));
            }
            finally { Object.DestroyImmediate(firm); Object.DestroyImmediate(limp); }
        }

        [Test]
        public void Bake_WithClothMotion_GivesTheSlotCoefficientsAndARendererOfItsOwn()
        {
            var capsule = new CapsuleBodyFixture();
            ClothesPattern pattern = VestPattern.Create();
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
            try
            {
                fit.pattern = pattern;
                fit.overrideMeshLevel = true;
                fit.meshLevel = MeshLevel.Draft;
                session.Rebuild();
                session.Step(60);
                GarmentBaker.EnsureFolder(Folder);
                PublishSettings p = fit.publish;
                p.slotName = "MotionTest"; p.folder = Folder; p.material = DesignerSession.DefaultMaterial();
                p.createWardrobeRecipe = false; p.addToGlobalLibrary = false; p.makeUvCheckTexture = false; p.bakeNormalMap = false; p.addTopstitch = false;
                p.clothMotion = true; p.motionHeldCm = 0.5f; p.motionGain = 1f; p.motionMaxCm = 20f;
                GarmentBaker.Result result = GarmentBaker.Bake(session);
                SlotDataAsset slot = result.Published.Slot;
                Vector2[] c = slot.meshData.clothSkinningSerialized;
                Assert.That(c, Is.Not.Null.And.Not.Empty);
                Assert.That(c.Length, Is.EqualTo(new HashSet<Vector3>(slot.meshData.vertices).Count));
                foreach (Vector2 k in c) Assert.That(k.y, Is.InRange(0f, 0.2f + 1e-5f));
                Assert.That(slot.RendererAsset, Is.Not.Null);
                Assert.That(slot.RendererAsset.ClothProperties, Is.Not.Null);
                Assert.That(slot.RendererAsset.RendererName, Does.Contain("MotionTest"));

                // Baking again without it takes the cloth off.
                p.clothMotion = false;
                result = GarmentBaker.Bake(session);
                Assert.That(result.Published.Slot.meshData.clothSkinningSerialized, Is.Null.Or.Empty);
                Assert.That(result.Published.Slot.RendererAsset, Is.Null);
            }
            finally
            {
                session.Dispose();
                Object.DestroyImmediate(fit);
                Object.DestroyImmediate(pattern);
                capsule.Dispose();
                if (AssetDatabase.IsValidFolder(Folder))
                {
                    AssetDatabase.DeleteAsset(Folder);
                    if (AssetDatabase.FindAssets("", new[] { "Assets/UMAProjectData/ClothesDesigner" }).Length == 0) AssetDatabase.DeleteAsset("Assets/UMAProjectData/ClothesDesigner");
                }
            }
        }
    }
}
#endif
