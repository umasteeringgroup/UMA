using System;
using System.Collections.Generic;
using NUnit.Framework;
using UMA.Editors.PackageSupport;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The UMA 2 human skin slots carry baked detail coordinates in their fourth UV channel, so the skin detail
    /// map has one size over head, neck and body. Their texture layouts are uneven inside an island (the face
    /// gets far less texture per surface than the neck), so the channel is baked with unfolded islands.
    /// Without it the detail is two to three times larger on the face than on the neck.
    /// </summary>
    public sealed class UMA2SkinDetailUVTests
    {
        // Slots that carry an overlay on a UMA 2 skin material in the HumanFemale and HumanMale recipes.
        static readonly string[] SkinSlots =
        {
            "Races/HumanFemale/Slots/Female_High_Face_Slot_Reweight.asset",
            "Races/HumanFemale/Slots/Female_High_Face_Slot_Save.asset",
            "Races/HumanFemale/Slots/Female_High_Feet_Slot.asset",
            "Races/HumanFemale/Slots/Female_High_Hands_Slot.asset",
            "Races/HumanFemale/Slots/Female_High_Legs_Slot.asset",
            "Races/HumanFemale/Slots/Female_High_Torso_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Feet_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Hands_Mid_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Hands_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Head_ElvenEars_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Head_Eyes_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Head_Head_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Head_Mid_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Head_Mouth_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Head_Nose_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Legs_Slot.asset",
            "Races/HumanFemale/Slots/UMA_Human_Female_Torso_Slot.asset",
            "Races/HumanMale/Slots/Body/HighPoly/Male_High_Face_Slot.asset",
            "Races/HumanMale/Slots/Body/HighPoly/Male_High_Feet_Slot.asset",
            "Races/HumanMale/Slots/Body/HighPoly/Male_High_Hands_Slot.asset",
            "Races/HumanMale/Slots/Body/HighPoly/Male_High_Legs_Slot.asset",
            "Races/HumanMale/Slots/Body/HighPoly/Male_High_Torso_Slot.asset",
            "Races/HumanMale/Slots/Body/MaleSwappedHead/UMA_Human_Male_Head_ElvenEars_Slot.asset",
            "Races/HumanMale/Slots/Body/MaleSwappedHead/UMA_Human_Male_Head_Eyes_Slot.asset",
            "Races/HumanMale/Slots/Body/MaleSwappedHead/UMA_Human_Male_Head_Head_Slot.asset",
            "Races/HumanMale/Slots/Body/MaleSwappedHead/UMA_Human_Male_Head_Mouth_Slot.asset",
            "Races/HumanMale/Slots/Body/MaleSwappedHead/UMA_Human_Male_Head_Nose_Slot.asset",
            "Races/HumanMale/Slots/Body/MidPoly/UMA_Human_Male_Hands_Mid_Slot.asset",
            "Races/HumanMale/Slots/Body/MidPoly/UMA_Human_Male_Head_Mid_Slot.asset",
            "Races/HumanMale/Slots/Body/UMA_Human_Male_Feet_Slot.asset",
            "Races/HumanMale/Slots/Body/UMA_Human_Male_Hands_Slot.asset",
            "Races/HumanMale/Slots/Body/UMA_Human_Male_Legs_Slot.asset",
            "Races/HumanMale/Slots/Body/UMA_Human_Male_Torso_Slot.asset",
        };

        const float Target = UMASkinDetailUVUtility.DefaultSurfacePerUV;
        // Largest spread between the 5% and 95% points of the detail size inside one slot. A head is one island
        // around a closed shape and cannot be laid flat exactly; the baked heads reach 1.3 to 1.45, the small
        // eye parts 1.55, everything else 1.25 or less. The texture layouts themselves reach 3.3.
        const float LargestSpread = 1.7f;

        static string Root => UMAContentCatalog.Root(UMAContentKind.Uma2) + "/";

        /// <summary>Detail size (surface per UV unit) at the 5%, 50% and 95% points of the slot's surface.</summary>
        static void DetailSize(UMAMeshData mesh, Vector2[] coordinates, out float low, out float median, out float high)
        {
            var sizes = new List<KeyValuePair<float, float>>();
            double total = 0;
            foreach (var submesh in mesh.submeshes)
            {
                int[] triangles = submesh != null ? submesh.GetBaseTriangles() : null;
                if (triangles == null) continue;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    float surface = Vector3.Cross(mesh.vertices[b] - mesh.vertices[a], mesh.vertices[c] - mesh.vertices[a]).magnitude * 0.5f;
                    Vector2 e1 = coordinates[b] - coordinates[a], e2 = coordinates[c] - coordinates[a];
                    float uvArea = Mathf.Abs(e1.x * e2.y - e1.y * e2.x) * 0.5f;
                    if (surface < 1e-12f || uvArea < 1e-14f) continue;
                    sizes.Add(new KeyValuePair<float, float>(Mathf.Sqrt(surface / uvArea), surface));
                    total += surface;
                }
            }
            sizes.Sort((x, y) => x.Key.CompareTo(y.Key));
            low = median = high = 0;
            double sum = 0; bool gotLow = false, gotMedian = false;
            foreach (var size in sizes)
            {
                sum += size.Value;
                if (!gotLow && sum >= total * 0.05) { low = size.Key; gotLow = true; }
                if (!gotMedian && sum >= total * 0.5) { median = size.Key; gotMedian = true; }
                if (sum >= total * 0.95) { high = size.Key; break; }
            }
        }

        [Test]
        public void EverySkinSlotHasEvenDetailCoordinates()
        {
            var problems = new List<string>();
            int found = 0;
            foreach (string relative in SkinSlots)
            {
                var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(Root + relative);
                if (slot == null) continue; // optional content, or a slot that was removed
                found++;
                if (!UMASkinDetailUVUtility.HasDetailUVs(slot))
                {
                    problems.Add(relative + ": no fourth UV channel. Select the slot and run Assets > UMA > Skin > Bake Even Detail UVs On Selected Slots (Unfold Islands).");
                    continue;
                }
                var mesh = slot.meshData;
                foreach (Vector2 coordinate in mesh.uv4)
                    if (coordinate.x < 1f || coordinate.y < 1f) { problems.Add(relative + ": a detail coordinate is below 1, which the skin shaders read as 'not baked'"); break; }
                DetailSize(mesh, mesh.uv4, out float low, out float median, out float high);
                if (Mathf.Abs(median - Target) > Target * 0.06f) problems.Add(relative + ": detail size " + median.ToString("F3") + " mesh units per UV unit, expected " + Target);
                if (high / low > LargestSpread) problems.Add(relative + ": detail size varies " + (high / low).ToString("F2") + " times inside the slot (" + low.ToString("F2") + " to " + high.ToString("F2") + ")");
            }
            if (found == 0) Assert.Ignore("The UMA 2 human skin slots are not installed.");
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void TheBakeDoesNotChangeTheTextureLayout()
        {
            // The detail coordinates are unfolded, the first UV channel is not: on the head the two must differ,
            // and the first channel must still be an ordinary 0..1 layout.
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(Root + "Races/HumanFemale/Slots/UMA_Human_Female_Head_Mid_Slot.asset");
            if (slot == null) Assert.Ignore("The UMA 2 female head is not installed.");
            var mesh = slot.meshData;
            Assert.That(mesh.uv.Length, Is.EqualTo(mesh.vertices.Length));
            foreach (Vector2 coordinate in mesh.uv)
            {
                Assert.That(coordinate.x, Is.InRange(-0.001f, 1.001f));
                Assert.That(coordinate.y, Is.InRange(-0.001f, 1.001f));
            }
            DetailSize(mesh, mesh.uv, out float low, out _, out float high);
            Assert.That(high / low, Is.GreaterThan(2.5f), "the head's texture layout is expected to be uneven; that is why the detail coordinates are baked separately");
        }

        // A nose is a bulge that cannot be laid flat, so the detail map keeps some stretch there even with baked
        // coordinates. The head mask maps therefore show very little detail on the nose (blue channel = coverage).
        [TestCase("Races/HumanFemale/Textures/Face/UMA2_F_Head_SSS.AO.Detail.Gloss.png", "Races/HumanFemale/Slots/UMA_Human_Female_Head_Nose_Slot.asset")]
        [TestCase("Races/HumanMale/Textures/Face/UMA2_M_Head_SSS.AO.Detail.Gloss.png", "Races/HumanMale/Slots/Body/MaleSwappedHead/UMA_Human_Male_Head_Nose_Slot.asset")]
        public void TheNoseHasLittleDetailCoverage(string maskMap, string noseSlot)
        {
            string path = Root + maskMap;
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(Root + noseSlot);
            if (!System.IO.File.Exists(path) || slot == null) Assert.Ignore("The UMA 2 head mask map or nose slot is not installed.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(path)), Is.True, path);
                Color32[] pixels = texture.GetPixels32();
                // The nose part shares the head's texture layout: its vertices mark the nose in the mask map.
                double coverage = 0;
                foreach (Vector2 uv in slot.meshData.uv)
                {
                    int x = Mathf.Clamp((int)(uv.x * texture.width), 0, texture.width - 1);
                    int y = Mathf.Clamp((int)(uv.y * texture.height), 0, texture.height - 1);
                    coverage += pixels[y * texture.width + x].b;
                }
                coverage /= slot.meshData.uv.Length;
                Assert.That(coverage, Is.LessThan(100.0), "Mean detail coverage on the nose, of 255. It was above 215 before it was lowered.");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test]
        public void BothSkinMaterialsUseOneDetailTiling()
        {
            // With baked detail coordinates the head and the body no longer need different tiling to match.
            var head = AssetDatabase.LoadAssetAtPath<Material>("Assets/UMA/SRP/ShaderGraphs/Materials/UMA2_SkinShader_URP.mat");
            var body = AssetDatabase.LoadAssetAtPath<Material>("Assets/UMA/SRP/ShaderGraphs/Materials/UMA2_SkinShader_URP_Body.mat");
            if (head == null || body == null) Assert.Ignore("The UMA 2 skin materials are not installed.");
            Assert.That((Vector2)body.GetVector("_Detail_Tiling"), Is.EqualTo((Vector2)head.GetVector("_Detail_Tiling")));
        }
    }
}
