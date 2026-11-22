#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors.Tests
{
    public class UMAWrinkleMapTests
    {
        const string SSSShaderPath = "Assets/UMA/SRP/Shaders/Skin/UMA3SkinRealisticSSS.shader";
        const string WrinkleShaderPath = "Assets/UMA/SRP/Shaders/Skin/UMA3SkinRealisticSSSWrinkles.shader";

        static float[] Disk(int size, float cx, float cy, float radius)
        {
            var mask = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    mask[y * size + x] = Mathf.Clamp01((radius - d) / 3f);
                }
            return mask;
        }

        [Test]
        public void ZoneIdsSurviveEncodingAndFilteringNoise()
        {
            for (int id = 0; id < UMAWrinkleZones.IdLevels; id++)
            {
                byte encoded = UMAWrinkleZones.EncodeId(id);
                Assert.That(encoded, Is.EqualTo(id * 17));
                Assert.That(UMAWrinkleZones.DecodeId(encoded / 255f), Is.EqualTo(id));
                // Half a step either way still decodes to the same id.
                Assert.That(UMAWrinkleZones.DecodeId(Mathf.Max(0, encoded - 8) / 255f), Is.EqualTo(id));
                Assert.That(UMAWrinkleZones.DecodeId(Mathf.Min(255, encoded + 8) / 255f), Is.EqualTo(id));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => UMAWrinkleZones.EncodeId(16));
            Assert.That(UMAWrinkleZones.WeightProperty(UMAWrinkleZone.Glabella), Is.EqualTo("_WrinkleZone03"));
            Assert.That(UMAWrinkleZones.WeightProperty(UMAWrinkleZone.NeckRight), Is.EqualTo("_WrinkleZone15"));
            Assert.Throws<ArgumentOutOfRangeException>(() => UMAWrinkleZones.WeightProperty(UMAWrinkleZone.None));
        }

        [Test]
        public void OverlappingZonesGoToDifferentLayersAndIdsFillTheGaps()
        {
            const int size = 64;
            var masks = new float[16][];
            masks[(int)UMAWrinkleZone.ForeheadLeft] = Disk(size, 20, 32, 12);
            masks[(int)UMAWrinkleZone.ForeheadRight] = Disk(size, 36, 32, 12);
            var bytes = UMAWrinkleControlMapPacker.Pack(size, size, masks, out var report);
            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(report.Layer[(int)UMAWrinkleZone.ForeheadLeft], Is.Not.EqualTo(report.Layer[(int)UMAWrinkleZone.ForeheadRight]));
            Assert.That(report.Overlaps, Has.Count.EqualTo(1));
            int layerLeft = report.Layer[(int)UMAWrinkleZone.ForeheadLeft], layerRight = report.Layer[(int)UMAWrinkleZone.ForeheadRight];
            // Every texel of each layer carries that layer's only zone id, including texels far from any mask.
            for (int i = 0; i < size * size; i++)
            {
                Assert.That(bytes[i * 4 + layerLeft * 2], Is.EqualTo(UMAWrinkleZones.EncodeId((int)UMAWrinkleZone.ForeheadLeft)));
                Assert.That(bytes[i * 4 + layerRight * 2], Is.EqualTo(UMAWrinkleZones.EncodeId((int)UMAWrinkleZone.ForeheadRight)));
            }
            int centre = 32 * size + 28;
            Assert.That(bytes[centre * 4 + 1], Is.GreaterThan(0));
            Assert.That(bytes[centre * 4 + 3], Is.GreaterThan(0));
        }

        [Test]
        public void ThreeZonesOnOneTexelAreReported()
        {
            const int size = 48;
            var masks = new float[16][];
            masks[1] = Disk(size, 20, 24, 10);
            masks[2] = Disk(size, 28, 24, 10);
            masks[3] = Disk(size, 24, 30, 10);
            UMAWrinkleControlMapPacker.Pack(size, size, masks, out var report);
            Assert.That(report.Ok, Is.False);
            Assert.That(report.ConflictTexels, Is.GreaterThan(0));
        }

        [Test]
        public void ZonesOfOneLayerTooCloseTogetherAreReported()
        {
            const int size = 64;
            var masks = new float[16][];
            // Two zones that do not overlap share layer A; leave less than the required gap between them. (Not the
            // forehead halves: those may touch.)
            masks[6] = Disk(size, 20, 32, 10);
            masks[9] = Disk(size, 41.5f, 32, 10);
            UMAWrinkleControlMapPacker.Pack(size, size, masks, out var report, 4f);
            Assert.That(report.Layer[6], Is.EqualTo(report.Layer[9]));
            Assert.That(report.MinimumGap[0], Is.LessThan(4f));
            Assert.That(report.Ok, Is.False);
            // With room between them the same pair packs cleanly.
            masks[9] = Disk(size, 52f, 32, 10); // 12 empty texels: the id changes 6 from either mask
            UMAWrinkleControlMapPacker.Pack(size, size, masks, out report, 4f);
            Assert.That(report.Ok, Is.True, report.ToString());
        }

        // The forehead halves may meet on one layer without a gap (their ids are consecutive, so filtering can only show
        // one of the two). Any other pair that touches still needs the gap.
        [Test]
        public void OnlyTheForeheadHalvesMayTouchWithoutAGap()
        {
            const int size = 48;
            float[][] Halves(int left, int right)
            {
                var masks = new float[16][];
                masks[left] = new float[size * size]; masks[right] = new float[size * size];
                for (int y = 12; y < 36; y++)
                    for (int x = 8; x < 40; x++) (x < 24 ? masks[left] : masks[right])[y * size + x] = 0.7f;
                return masks;
            }
            var bytes = UMAWrinkleControlMapPacker.Pack(size, size, Halves((int)UMAWrinkleZone.ForeheadLeft, (int)UMAWrinkleZone.ForeheadRight), out var report);
            Assert.That(report.Ok, Is.True, report.ToString());
            Assert.That(report.Layer[(int)UMAWrinkleZone.ForeheadLeft], Is.EqualTo(report.Layer[(int)UMAWrinkleZone.ForeheadRight]));
            int row = 20 * size;
            int channel = report.Layer[(int)UMAWrinkleZone.ForeheadLeft] * 2;
            Assert.That(bytes[(row + 23) * 4 + channel], Is.EqualTo(UMAWrinkleZones.EncodeId((int)UMAWrinkleZone.ForeheadLeft)));
            Assert.That(bytes[(row + 24) * 4 + channel], Is.EqualTo(UMAWrinkleZones.EncodeId((int)UMAWrinkleZone.ForeheadRight)));

            UMAWrinkleControlMapPacker.Pack(size, size, Halves((int)UMAWrinkleZone.CrowsFeetRight, (int)UMAWrinkleZone.NasolabialLeft), out report);
            Assert.That(report.Ok, Is.False);
        }

        // Reads the packed map the way the GPU does (bilinear, texel centres at +0.5) and checks that wherever a
        // layer's mask is visible, its filtered id decodes to a zone that really has a mask there.
        [Test]
        public void BilinearFilteringNeverShowsAWrongZone()
        {
            const int size = 64;
            var masks = new float[16][];
            masks[4] = Disk(size, 18, 20, 9);
            masks[9] = Disk(size, 44, 20, 9);
            masks[12] = Disk(size, 31, 44, 9);
            masks[13] = Disk(size, 31, 30, 7); // overlaps 12, so it goes to the other layer
            var bytes = UMAWrinkleControlMapPacker.Pack(size, size, masks, out var report);
            Assert.That(report.Ok, Is.True, report.ToString());
            float Fetch(int x, int y, int c) { x = Mathf.Clamp(x, 0, size - 1); y = Mathf.Clamp(y, 0, size - 1); return bytes[(y * size + x) * 4 + c] / 255f; }
            float Sample(float u, float v, int c)
            {
                float px = u - 0.5f, py = v - 0.5f; int x0 = Mathf.FloorToInt(px), y0 = Mathf.FloorToInt(py); float fx = px - x0, fy = py - y0;
                return Mathf.Lerp(Mathf.Lerp(Fetch(x0, y0, c), Fetch(x0 + 1, y0, c), fx), Mathf.Lerp(Fetch(x0, y0 + 1, c), Fetch(x0 + 1, y0 + 1, c), fx), fy);
            }
            for (float v = 0.25f; v < size; v += 0.5f)
                for (float u = 0.25f; u < size; u += 0.5f)
                    for (int layer = 0; layer < 2; layer++)
                    {
                        float mask = Sample(u, v, layer * 2 + 1);
                        if (mask < 1f / 255f) continue;
                        int id = UMAWrinkleZones.DecodeId(Sample(u, v, layer * 2));
                        Assert.That(id, Is.GreaterThan(0), $"({u},{v}) layer {layer}");
                        Assert.That(report.Layer[id], Is.EqualTo(layer), $"({u},{v}) id {id}");
                        // the zone's own mask is present within one texel
                        int x = Mathf.Clamp(Mathf.FloorToInt(u), 0, size - 1), y = Mathf.Clamp(Mathf.FloorToInt(v), 0, size - 1);
                        float near = 0;
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = Mathf.Clamp(x + dx, 0, size - 1), yy = Mathf.Clamp(y + dy, 0, size - 1);
                                near = Mathf.Max(near, masks[id][yy * size + xx]);
                            }
                        Assert.That(near, Is.GreaterThan(0), $"({u},{v}) shows zone {id} where it has no mask");
                    }
        }

        // Builds the UMA 3 head at a small size: every face zone is present, the packing is clean, and each zone lies
        // on the side of the face its name says (the character's left is mesh -X).
        [Test]
        public void ProceduralHeadZonesArePackedCleanlyAndOnTheirOwnSide()
        {
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(UMAWrinkleMapBaker.UMA3HeadSlotPath);
            Assert.That(slot, Is.Not.Null, UMAWrinkleMapBaker.UMA3HeadSlotPath);
            var md = slot.meshData;
            int n = md.vertices.Length;
            var positions = new System.Numerics.Vector3[n]; var normals = new System.Numerics.Vector3[n];
            var tangents = new System.Numerics.Vector4[n]; var uvs = new System.Numerics.Vector2[n];
            for (int i = 0; i < n; i++)
            {
                positions[i] = new System.Numerics.Vector3(md.vertices[i].x, md.vertices[i].y, md.vertices[i].z);
                normals[i] = new System.Numerics.Vector3(md.normals[i].x, md.normals[i].y, md.normals[i].z);
                tangents[i] = new System.Numerics.Vector4(md.tangents[i].x, md.tangents[i].y, md.tangents[i].z, md.tangents[i].w);
                uvs[i] = new System.Numerics.Vector2(md.uv[i].x, md.uv[i].y);
            }
            const int size = 512;
            var surface = UMAWrinkleSurfaceMap.Build(positions, normals, tangents, uvs, md.submeshes[0].GetBaseTriangles(), size);
            var bake = UMAWrinkleProceduralGenerator.Generate(surface, UMAWrinkleFaceLandmarks.UMA3Head(), 1, 1);
            UMAWrinkleControlMapPacker.Pack(size, size, bake.ZoneMasks, out var report, 1f);
            Assert.That(report.Ok, Is.True, report.ToString());
            for (int zone = 1; zone <= 13; zone++)
            {
                double weight = 0, x = 0;
                var mask = bake.ZoneMasks[zone];
                for (int i = 0; i < mask.Length; i++) if (surface.Covered[i]) { weight += mask[i]; x += mask[i] * surface.Position[i].X; }
                Assert.That(weight, Is.GreaterThan(10), ((UMAWrinkleZone)zone).ToString());
                string name = ((UMAWrinkleZone)zone).ToString();
                double meanX = x / weight;
                if (name.EndsWith("Left")) Assert.That(meanX, Is.LessThan(-0.01), name);
                else if (name.EndsWith("Right")) Assert.That(meanX, Is.GreaterThan(0.01), name);
                else Assert.That(Math.Abs(meanX), Is.LessThan(0.004), name);
            }
            // Away from every zone the wrinkle map is flat and empty.
            for (int i = 0; i < size * size; i++)
            {
                bool inZone = false;
                for (int zone = 1; zone <= 13 && !inZone; zone++) inZone = bake.ZoneMasks[zone][i] > 0;
                if (inZone) continue;
                Assert.That(bake.WrinkleMap[i * 4], Is.EqualTo(0.5f).Within(1e-4f));
                Assert.That(bake.WrinkleMap[i * 4 + 1], Is.EqualTo(0.5f).Within(1e-4f));
                Assert.That(bake.WrinkleMap[i * 4 + 2], Is.EqualTo(0f));
            }
        }

        [Test]
        public void WrinkleShaderKeepsEverySkinInputAndAddsTheZoneWeights()
        {
            var sss = AssetDatabase.LoadAssetAtPath<Shader>(SSSShaderPath);
            var wrinkles = AssetDatabase.LoadAssetAtPath<Shader>(WrinkleShaderPath);
            Assert.That(wrinkles, Is.Not.Null);
            Assert.That(wrinkles.name, Is.EqualTo(UMAWrinkleZones.ShaderName));
            Assert.That(ShaderUtil.ShaderHasError(wrinkles), Is.False);
            for (int i = 0; i < sss.GetPropertyCount(); i++)
            {
                int index = wrinkles.FindPropertyIndex(sss.GetPropertyName(i));
                Assert.That(index, Is.GreaterThanOrEqualTo(0), sss.GetPropertyName(i));
                Assert.That(wrinkles.GetPropertyType(index), Is.EqualTo(sss.GetPropertyType(i)), sss.GetPropertyName(i));
            }
            foreach (UMAWrinkleZone zone in Enum.GetValues(typeof(UMAWrinkleZone)))
            {
                if (zone == UMAWrinkleZone.None) continue;
                int index = wrinkles.FindPropertyIndex(UMAWrinkleZones.WeightProperty(zone));
                Assert.That(index, Is.GreaterThanOrEqualTo(0), zone.ToString());
                Assert.That(wrinkles.GetPropertyType(index), Is.EqualTo(ShaderPropertyType.Range));
                Assert.That(wrinkles.GetPropertyRangeLimits(index), Is.EqualTo(new Vector2(-1, 1)));
                Assert.That(wrinkles.GetPropertyDefaultFloatValue(index), Is.EqualTo(0f));
            }
            Assert.That(wrinkles.FindPropertyIndex(UMAWrinkleZones.WrinkleMapProperty), Is.GreaterThanOrEqualTo(0));
            Assert.That(wrinkles.FindPropertyIndex(UMAWrinkleZones.ControlMapProperty), Is.GreaterThanOrEqualTo(0));
            // The production shader never compiles the wrinkle code.
            Assert.That(sss.FindPropertyIndex(UMAWrinkleZones.ControlMapProperty), Is.LessThan(0));
            StringAssert.DoesNotContain("UMA_SKIN_WRINKLES", System.IO.File.ReadAllText(SSSShaderPath));
            StringAssert.Contains("#define UMA_SKIN_WRINKLES 1", System.IO.File.ReadAllText(WrinkleShaderPath));
            // The scatter lookup texture is bound to the new shader too.
            var sssImporter = (ShaderImporter)AssetImporter.GetAtPath(SSSShaderPath);
            var wrinkleImporter = (ShaderImporter)AssetImporter.GetAtPath(WrinkleShaderPath);
            Assert.That(wrinkleImporter.GetNonModifiableTexture("_SkinScatterLUT"), Is.EqualTo(sssImporter.GetNonModifiableTexture("_SkinScatterLUT")));
        }

        [Test]
        public void HeadSlotsMatchTheirRaceBakedCopiesOnly()
        {
            var heads = new[] { UMAWrinkleZones.UMA3HeadSlotName };
            Assert.That(UMAExpressionWrinkles.IsHeadSlotName("UMA30_Body_UDIM1001", heads), Is.True);
            Assert.That(UMAExpressionWrinkles.IsHeadSlotName("UMA30_Body_UDIM1001_baked_Human Male 3.0", heads), Is.True);
            Assert.That(UMAExpressionWrinkles.IsHeadSlotName("UMA30_Body_UDIM1002", heads), Is.False);
            Assert.That(UMAExpressionWrinkles.IsHeadSlotName("UMA30_Body_UDIM10010", heads), Is.False);
            Assert.That(UMAExpressionWrinkles.IsHeadSlotName("UMA30_Body_UDIM1001_slot", heads), Is.False);
            Assert.That(UMAExpressionWrinkles.IsHeadSlotName("", heads), Is.False);
        }

        [Test]
        public void WrinkleUMAMaterialKeepsTheSkinChannelsAndAddsUncompressedWrinkleChannels()
        {
            var source = AssetDatabase.LoadAssetAtPath<UMAMaterial>(UMAWrinkleAssets.UMA3SourceUMAMaterialPath);
            var wrinkle = AssetDatabase.LoadAssetAtPath<UMAMaterial>(UMAWrinkleAssets.UMA3UMAMaterialPath);
            Assert.That(source, Is.Not.Null);
            Assert.That(wrinkle, Is.Not.Null, "Bake the UMA 3 wrinkle maps to create the wrinkle UMA Material.");
            Assert.That(wrinkle.material.shader.name, Is.EqualTo(UMAWrinkleZones.ShaderName));
            Assert.That(wrinkle.objectName, Is.Not.EqualTo(source.objectName), "UMA treats materials with the same objectName as one.");
            Assert.That(wrinkle.channels.Length, Is.EqualTo(source.channels.Length + 2));
            for (int i = 0; i < source.channels.Length; i++)
            {
                Assert.That(wrinkle.channels[i].materialPropertyName, Is.EqualTo(source.channels[i].materialPropertyName));
                Assert.That(wrinkle.channels[i].channelType, Is.EqualTo(source.channels[i].channelType));
                Assert.That(wrinkle.channels[i].NeverCompress, Is.EqualTo(source.channels[i].NeverCompress));
            }
            foreach (var property in new[] { UMAWrinkleZones.WrinkleMapProperty, UMAWrinkleZones.ControlMapProperty })
            {
                int index = UMAWrinkleAssets.ChannelIndex(wrinkle, property);
                Assert.That(index, Is.GreaterThanOrEqualTo(source.channels.Length), property);
                var channel = wrinkle.channels[index];
                Assert.That(channel.channelType, Is.EqualTo(UMAMaterial.ChannelType.Texture), property);
                Assert.That(channel.NeverCompress, Is.True, property);
                Assert.That(channel.DownSample, Is.EqualTo(1), property);
                Assert.That(wrinkle.material.HasProperty(property), Is.True, property);
            }
            Assert.That(UMAExpressionWrinkles.HasWrinkleChannels(wrinkle), Is.True);
            Assert.That(UMAExpressionWrinkles.HasWrinkleChannels(source), Is.False);
        }

        [Test]
        public void WrinkleOverlayCarriesOnlyTheWrinkleChannels()
        {
            var overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(UMAWrinkleAssets.UMA3OverlayPath);
            Assert.That(overlay, Is.Not.Null, "Bake the UMA 3 wrinkle maps to create the wrinkle overlay.");
            Assert.That(overlay.overlayName, Is.EqualTo(UMAWrinkleZones.UMA3OverlayName));
            var material = overlay.material;
            Assert.That(UMAExpressionWrinkles.HasWrinkleChannels(material), Is.True);
            int wrinkle = UMAWrinkleAssets.ChannelIndex(material, UMAWrinkleZones.WrinkleMapProperty);
            int control = UMAWrinkleAssets.ChannelIndex(material, UMAWrinkleZones.ControlMapProperty);
            Assert.That(overlay.textureList.Length, Is.EqualTo(material.channels.Length));
            for (int i = 0; i < overlay.textureList.Length; i++)
            {
                if (i == wrinkle) Assert.That(AssetDatabase.GetAssetPath(overlay.textureList[i]), Is.EqualTo(UMAWrinkleMapBaker.UMA3WrinkleMapPath));
                else if (i == control) Assert.That(AssetDatabase.GetAssetPath(overlay.textureList[i]), Is.EqualTo(UMAWrinkleMapBaker.UMA3ControlMapPath));
                // As an additional overlay it must leave the skin's own channels alone.
                else Assert.That(overlay.textureList[i], Is.Null, "channel " + i);
            }
        }

        [Test]
        public void SignedExpressionValueIsZeroAtNeutralAndOneAtEitherEnd()
        {
            Assert.That(UMAWrinkleProfile.Signed(0.5f, 0.5f), Is.EqualTo(0f));
            Assert.That(UMAWrinkleProfile.Signed(1f, 0.5f), Is.EqualTo(1f));
            Assert.That(UMAWrinkleProfile.Signed(0f, 0.5f), Is.EqualTo(-1f));
            Assert.That(UMAWrinkleProfile.Signed(0.75f, 0.5f), Is.EqualTo(0.5f).Within(1e-6f));
            // Expressions that rest at 0, such as visemes, only go up.
            Assert.That(UMAWrinkleProfile.Signed(0f, 0f), Is.EqualTo(0f));
            Assert.That(UMAWrinkleProfile.Signed(0.4f, 0f), Is.EqualTo(0.4f).Within(1e-6f));
        }

        [Test]
        public void ProfileUsesEachDirectionsAmountAddsRulesAndClamps()
        {
            var profile = ScriptableObject.CreateInstance<UMAWrinkleProfile>();
            try
            {
                profile.rules.Add(new UMAWrinkleProfile.Rule("a", UMAWrinkleZone.Glabella, 1f, -0.5f));
                profile.rules.Add(new UMAWrinkleProfile.Rule("b", UMAWrinkleZone.Glabella, 0.8f, 0f));
                profile.rules.Add(new UMAWrinkleProfile.Rule("a", UMAWrinkleZone.Chin, 0f, 0.3f));
                var weights = profile.Evaluate(new System.Collections.Generic.Dictionary<string, float> { { "a", 0.5f } });
                Assert.That(weights[(int)UMAWrinkleZone.Glabella - 1], Is.EqualTo(0.5f).Within(1e-6f));
                Assert.That(weights[(int)UMAWrinkleZone.Chin - 1], Is.EqualTo(0f));
                weights = profile.Evaluate(new System.Collections.Generic.Dictionary<string, float> { { "a", -1f } });
                Assert.That(weights[(int)UMAWrinkleZone.Glabella - 1], Is.EqualTo(-0.5f).Within(1e-6f));
                Assert.That(weights[(int)UMAWrinkleZone.Chin - 1], Is.EqualTo(0.3f).Within(1e-6f));
                weights = profile.Evaluate(new System.Collections.Generic.Dictionary<string, float> { { "a", 1f }, { "b", 1f } });
                Assert.That(weights[(int)UMAWrinkleZone.Glabella - 1], Is.EqualTo(1f), "the sum is clamped");
                profile.strength = 0.5f;
                weights = profile.Evaluate(new System.Collections.Generic.Dictionary<string, float> { { "b", 1f } });
                Assert.That(weights[(int)UMAWrinkleZone.Glabella - 1], Is.EqualTo(0.4f).Within(1e-6f));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void UMA3ProfileReadsOnlyExpressionsOfTheUMA3Set()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UMAWrinkleProfile>(UMAWrinkleZones.UMA3ProfilePath);
            Assert.That(profile, Is.Not.Null);
            var group = AssetDatabase.LoadAssetAtPath<UMAExpressionGroup>("Assets/UMA/UMA3/Races/Expressions/DynamicExpressionSet/UMA 30 Expression Set_ExpressionGroup.asset");
            Assert.That(group, Is.Not.Null);
            Assert.That(profile.rules.Count, Is.EqualTo(UMAWrinkleAssets.UMA3ProfileRules().Length), "the asset matches UMAWrinkleAssets.UMA3ProfileRules");
            foreach (var rule in profile.rules)
            {
                UMAExpressionDefinition definition;
                Assert.That(group.TryGetDefinition(rule.expression, out definition), Is.True, rule.expression + " is not in the UMA 3 expression group");
                // Visemes exist only on the Dynamic Expression Player, where they rest at 0 and only go up.
                if (rule.expression.StartsWith("viseme_", StringComparison.Ordinal))
                {
                    Assert.That(definition.DefaultValue, Is.EqualTo(0f), rule.expression);
                    Assert.That(rule.negative, Is.EqualTo(0f), rule.expression);
                }
                else Assert.That(System.Array.IndexOf(UMA.PoseTools.ExpressionPlayer.PoseNames, rule.expression), Is.GreaterThanOrEqualTo(0), rule.expression + " is not a classic Expression Player channel");
                // The neck zones are reserved: the UMA 3 neck is not on the head tile.
                Assert.That((int)rule.zone, Is.InRange(1, (int)UMAWrinkleZone.Chin), rule.expression);
                Assert.That(Mathf.Abs(rule.positive) <= 1f && Mathf.Abs(rule.negative) <= 1f, Is.True, rule.expression);
            }
        }

        [Test]
        public void DriverFollowsAClassicExpressionPlayer()
        {
            var go = new GameObject("wrinkle driver test") { hideFlags = HideFlags.HideAndDontSave };
            var profile = ScriptableObject.CreateInstance<UMAWrinkleProfile>();
            try
            {
                profile.rules.Add(new UMAWrinkleProfile.Rule("leftBrowUp_Down", UMAWrinkleZone.ForeheadLeft, 1f, -0.5f));
                profile.rules.Add(new UMAWrinkleProfile.Rule("browsIn", UMAWrinkleZone.Glabella, 1f, 0f));
                var player = go.AddComponent<UMA.PoseTools.ExpressionPlayer>();
                var wrinkles = go.AddComponent<UMAExpressionWrinkles>();
                wrinkles.profile = profile;
                var lateUpdate = typeof(UMAExpressionWrinkles).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                player.leftBrowUp_Down = 0.6f;
                player.browsIn = 1f;
                lateUpdate.Invoke(wrinkles, null);
                Assert.That(wrinkles.IsDriven, Is.True);
                Assert.That(wrinkles.GetWeight(UMAWrinkleZone.ForeheadLeft), Is.EqualTo(0.6f).Within(1e-6f));
                Assert.That(wrinkles.GetWeight(UMAWrinkleZone.Glabella), Is.EqualTo(1f));

                player.leftBrowUp_Down = -1f;
                player.browsIn = 0f;
                lateUpdate.Invoke(wrinkles, null);
                Assert.That(wrinkles.GetWeight(UMAWrinkleZone.ForeheadLeft), Is.EqualTo(-0.5f).Within(1e-6f));
                Assert.That(wrinkles.GetWeight(UMAWrinkleZone.Glabella), Is.EqualTo(0f));

                // Without a profile the face goes back to neutral instead of freezing.
                wrinkles.profile = null;
                lateUpdate.Invoke(wrinkles, null);
                Assert.That(wrinkles.GetWeight(UMAWrinkleZone.ForeheadLeft), Is.EqualTo(0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProjectSettingAddsAnUnsavedComponentOnlyForAllAvatars()
        {
            var original = UMASettings.instance;
            var settings = ScriptableObject.CreateInstance<UMASettings>();
            var profile = ScriptableObject.CreateInstance<UMAWrinkleProfile>();
            // Inactive, so neither the avatar nor the added component starts building.
            var root = new GameObject("wrinkle project setting test") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            try
            {
                var avatar = root.AddComponent<UMA.CharacterSystem.DynamicCharacterAvatar>();
                UMASettings.instance = settings;
                settings.wrinkleProfile = profile;
                foreach (var mode in new[] { UMASettings.ExpressionWrinkleMode.PerAvatar, UMASettings.ExpressionWrinkleMode.Off })
                {
                    settings.expressionWrinkles = mode;
                    Assert.That(UMAExpressionWrinkles.AddForProjectSetting(avatar), Is.Null, mode.ToString());
                    Assert.That(root.GetComponent<UMAExpressionWrinkles>(), Is.Null, mode.ToString());
                }
                settings.expressionWrinkles = UMASettings.ExpressionWrinkleMode.AllAvatars;
                var added = UMAExpressionWrinkles.AddForProjectSetting(avatar);
                Assert.That(added, Is.Not.Null);
                Assert.That(added.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave), "never saved into the scene or prefab");
                Assert.That(added.profile, Is.SameAs(profile));
                Assert.That(UMAExpressionWrinkles.AddForProjectSetting(avatar), Is.SameAs(added), "one component per avatar");
            }
            finally
            {
                UMASettings.instance = original;
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ProjectSettingOffStopsTheDriver()
        {
            var original = UMASettings.instance;
            var settings = ScriptableObject.CreateInstance<UMASettings>();
            var profile = ScriptableObject.CreateInstance<UMAWrinkleProfile>();
            var go = new GameObject("wrinkle off test") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                UMASettings.instance = settings;
                settings.expressionWrinkles = UMASettings.ExpressionWrinkleMode.Off;
                profile.rules.Add(new UMAWrinkleProfile.Rule("browsIn", UMAWrinkleZone.Glabella, 1f, 0f));
                var player = go.AddComponent<UMA.PoseTools.ExpressionPlayer>();
                var wrinkles = go.AddComponent<UMAExpressionWrinkles>();
                wrinkles.profile = profile;
                player.browsIn = 1f;
                var lateUpdate = typeof(UMAExpressionWrinkles).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                lateUpdate.Invoke(wrinkles, null);
                Assert.That(wrinkles.GetWeight(UMAWrinkleZone.Glabella), Is.EqualTo(0f));
                settings.expressionWrinkles = UMASettings.ExpressionWrinkleMode.PerAvatar;
                lateUpdate.Invoke(wrinkles, null);
                Assert.That(wrinkles.GetWeight(UMAWrinkleZone.Glabella), Is.EqualTo(1f));
            }
            finally
            {
                UMASettings.instance = original;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ConfigurePresetsTurnWrinklesOnForFidelityAndOffForSpeed()
        {
            // An in-memory settings object: the preset code changes only what it is given.
            var settings = ScriptableObject.CreateInstance<UMASettings>();
            try
            {
                string fidelity = UMAProjectDefaults.ConfigureExpressionWrinkles(settings, false);
                Assert.That(settings.expressionWrinkles, Is.EqualTo(UMASettings.ExpressionWrinkleMode.AllAvatars), fidelity);
                Assert.That(AssetDatabase.GetAssetPath(settings.wrinkleOverlay), Is.EqualTo(UMAWrinkleAssets.UMA3OverlayPath));
                Assert.That(AssetDatabase.GetAssetPath(settings.wrinkleProfile), Is.EqualTo(UMAWrinkleZones.UMA3ProfilePath));

                UMAProjectDefaults.ConfigureExpressionWrinkles(settings, true);
                Assert.That(settings.expressionWrinkles, Is.EqualTo(UMASettings.ExpressionWrinkleMode.Off));
                Assert.That(settings.wrinkleOverlay, Is.Null, "no reference left to pull the wrinkle textures into builds");
                Assert.That(settings.wrinkleProfile, Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        [Test]
        public void BaseOverlayWithoutATextureForTheChannelDrawsNothing()
        {
            var merge = ScriptableObject.CreateInstance<TextureMerge>();
            var drawMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
            var stale = new Texture2D(4, 4);
            try
            {
                merge.material = drawMaterial;
                merge.EnsurePreviewRectCapacity(4);
                merge.Reset();
                // What an earlier build left in the first rect.
                merge.GetPreviewRects()[0].tex = stale;
                merge.GetPreviewRects()[0].transparentPrefill = true;
                var fragment = new UMAData.MaterialFragment
                {
                    baseOverlay = new UMAData.textureData { textureList = new Texture[] { stale } },
                    overlayData = new OverlayData[1],
                    channelMask = new[] { new[] { Color.white } },
                    channelAdditiveMask = new[] { new[] { Color.clear } },
                    atlasRegion = new Rect(0, 0, 4, 4),
                };
                merge.SetupMaterialAndBaseOverlay(fragment, 1, null);
                Assert.That(merge.GetPreviewRects()[0].tex, Is.Null, "a base overlay with no texture for channel 1 must not draw the previous build's texture");
                Assert.That(merge.GetPreviewRects()[0].transparentPrefill, Is.False);

                merge.Reset();
                merge.GetPreviewRects()[0].tex = stale;
                fragment.isNoTextures = true;
                merge.SetupMaterialAndBaseOverlay(fragment, 0, null);
                Assert.That(merge.GetPreviewRects()[0].tex, Is.Null, "a fragment without textures must not draw the previous build's texture");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stale);
                UnityEngine.Object.DestroyImmediate(drawMaterial);
                UnityEngine.Object.DestroyImmediate(merge);
            }
        }
    }
}
#endif
