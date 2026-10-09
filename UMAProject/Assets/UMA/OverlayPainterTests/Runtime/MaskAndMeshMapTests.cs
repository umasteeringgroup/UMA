#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class MaskAndMeshMapTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void MeshMapsCoverFullUvRectangleAtTexelCenters(bool mirrored)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = mirrored ? new[] { 2, 1, 0, 3, 2, 0 } : new[] { 0, 1, 2, 0, 2, 3 }
            };
            try
            {
                using var maps = ProceduralMeshMapBuilder.Build(new ReconstructedSurface { mesh = mesh }, 16, 16);
                for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    Assert.That(maps.id.GetPixel(x, y).a, Is.EqualTo(1f), $"Coverage at {x},{y}");
                    Color position = maps.position.GetPixel(x, y);
                    Assert.That(position.r, Is.EqualTo((x + .5f) / 16).Within(.00001f));
                    Assert.That(position.g, Is.EqualTo((y + .5f) / 16).Within(.00001f));
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [TestCase(0, false, 16)]
        [TestCase(0, true, 32)]
        [TestCase(1, false, 32)]
        [TestCase(1, true, 64)]
        public async Task CreatureCoversDiagonalAndMirroredBoundariesWithCoarserMeshMaps(
            int projection, bool mirrored, int outputSize)
        {
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(10, 10, 7), new Vector3(11, 10, 7), new Vector3(10, 11, 7) },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                uv = new[] { new Vector2(.11f, .13f), new Vector2(.89f, .19f), new Vector2(.23f, .87f) },
                triangles = mirrored ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 }
            };
            mesh.RecalculateBounds();
            var surface = new ReconstructedSurface { mesh = mesh, triangleIslands = new[] { 0 } };
            var plugin = ScriptableObject.CreateInstance<CreatureSkinGeneratorPlugin>();
            var mask = TexturePaintGeometryMask.Build(surface, outputSize, outputSize, null, -1, null);
            try
            {
                using var host = new PluginHost();
                using var maps = ProceduralMeshMapBuilder.Build(surface, 16, 16);
                var inputs = new Dictionary<string, TexturePaintReadOnlyMeshMap>();
                AddMap(TexturePaintMeshMap.SurfaceId, maps.id);
                AddMap(TexturePaintMeshMap.WorldPosition, maps.position);
                AddMap(TexturePaintMeshMap.Thickness, maps.thickness);
                var info = new Dictionary<string, TexturePaintReadOnlyChannelInfo>();
                foreach (TexturePaintChannel channel in System.Enum.GetValues(typeof(TexturePaintChannel)))
                    if (plugin.Descriptor.Declares(channel))
                        info.Add(TexturePaintReadContextV2.Key("seam", channel),
                            new TexturePaintReadOnlyChannelInfo("seam", channel, outputSize, outputSize,
                                channel == TexturePaintChannel.Albedo));
                var source = new TexturePaintReadContextV2(null, info, inputs, null, new List<string> { "seam" });
                var parameters = host.CreateParameters(plugin);
                parameters.Get("surfaceMode").number = 1;
                parameters.Get("skinMaskStrength").number = 1;
                parameters.Get("projection").number = projection;
                var context = new TexturePaintCommandContextV2(plugin.Descriptor, source, parameters,
                    CancellationToken.None, null, 64L * 1024 * 1024);
                await plugin.ExecuteAsync(context);
                var coverage = mask.GetPixels32();
                int checkedChannels = 0;
                foreach (var command in context.SealAndSnapshot())
                {
                    command.MaterializeCompactPixels();
                    try
                    {
                        for (int i = 0; i < coverage.Length; i++)
                        {
                            if (coverage[i].r == 0) continue;
                            Assert.That(command.GetPixel(i).a, Is.EqualTo(1f).Within(1f / 255),
                                $"{command.channel}: missing boundary pixel {i % outputSize},{i / outputSize}");
                            float u = (i % outputSize + .5f) / outputSize;
                            float v = (i / outputSize + .5f) / outputSize;
                            Assert.That(inputs[TexturePaintReadContextV2.MeshKey("seam", TexturePaintMeshMap.WorldPosition)]
                                .GetPixelBilinear(u, v).b, Is.EqualTo(7f).Within(.00001f),
                                "Boundary positions must not blend with the empty map's origin.");
                        }
                        checkedChannels++;
                    }
                    finally { command.ReleaseMaterializedCompactPixels(); }
                }
                Assert.That(checkedChannels, Is.GreaterThanOrEqualTo(3));
                Assert.That(maps.id.GetPixel(15, 15).a, Is.Zero, "Padding must stay local to the island.");

                void AddMap(TexturePaintMeshMap kind, Texture2D texture) =>
                    inputs.Add(TexturePaintReadContextV2.MeshKey("seam", kind),
                        new TexturePaintReadOnlyMeshMap("seam", kind, texture.width, texture.height, texture.GetPixels()));
            }
            finally
            {
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(plugin);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void GeometrySelectorsRestrictPolygonsAndUvIslands()
        {
            TexturePaintGeometrySelection stack = new TexturePaintGeometrySelection();
            stack.Add(new TexturePaintGeometrySelector
            {
                kind = TexturePaintGeometrySelectorKind.Polygon,
                surfaceIndex = 4,
                triangleIndices = { 3 }
            });
            stack.Add(new TexturePaintGeometrySelector
            {
                kind = TexturePaintGeometrySelectorKind.UVIsland,
                uvIslandIndices = { 2 }
            });

            Assert.That(stack.AllowsStructural(4, 3, 2), Is.True);
            Assert.That(stack.AllowsStructural(4, 1, 2), Is.False);
            Assert.That(stack.AllowsStructural(4, 3, 1), Is.False);
        }

        [Test]
        public void TriangleRestrictedPaintingSkipsRedundantGeometryMask()
        {
            TexturePaintGeometrySelection empty = new TexturePaintGeometrySelection();
            TexturePaintGeometrySelection structural = new TexturePaintGeometrySelection();
            structural.Add(new TexturePaintGeometrySelector
                { kind = TexturePaintGeometrySelectorKind.UVIsland });

            Assert.That(PaintingEngine.RequiresGeometryMask(empty, true), Is.False);
            Assert.That(PaintingEngine.RequiresGeometryMask(structural, true), Is.False,
                "Transient selectors are evaluated per contacted triangle before dispatch.");
            Assert.That(PaintingEngine.RequiresGeometryMask(empty, false), Is.True,
                "Unrestricted projection still needs the mesh coverage mask.");
            Assert.That(PaintingEngine.RequiresGeometryMask(empty, false, true), Is.False,
                "A direct 2D texture-space brush must not consult mesh coverage.");
        }

        [Test]
        public void LayerChannelCloneRetainsLocksAndContribution()
        {
            TexturePaintLayerChannelSettings original = new TexturePaintLayerChannelSettings
            {
                channel = TexturePaintChannel.Roughness,
                enabled = false,
                locked = true,
                contribution = 0.35f,
                opacity = 0.75f,
                sourceSettings = new TexturePaintChannelSourceSettings
                {
                    source = TexturePaintBrushSource.Texture,
                    invert = true,
                    tiling = new Vector2(2f, 3f)
                }
            };

            TexturePaintLayerChannelSettings clone = original.Clone();

            Assert.That(clone.locked, Is.True);
            Assert.That(clone.contribution, Is.EqualTo(0.35f));
            Assert.That(clone.enabled, Is.False);
            Assert.That(clone.opacity, Is.EqualTo(0.75f));
            Assert.That(clone.sourceSettings, Is.Not.SameAs(original.sourceSettings));
            Assert.That(clone.sourceSettings.invert, Is.True);
            Assert.That(clone.sourceSettings.tiling, Is.EqualTo(new Vector2(2f, 3f)));
        }

        [Test]
        public void ProceduralMeshMapCacheBuildsAllRequiredMaps()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateBounds();
            ReconstructedSurface surface = new ReconstructedSurface
            {
                index = 6,
                mesh = mesh,
                triangleIslands = new[] { 2 }
            };

            TextureSet set = new TextureSet { surface = surface };
            ProceduralMeshMaps maps = set.GetProceduralMeshMaps(16);

            Assert.That(maps.position, Is.Not.Null);
            Assert.That(maps.worldNormal, Is.Not.Null);
            Assert.That(maps.curvature, Is.Not.Null);
            Assert.That(maps.ambientOcclusion, Is.Not.Null);
            Assert.That(maps.thickness, Is.Not.Null);
            Assert.That(maps.id, Is.Not.Null);
            Assert.That(maps.id.width, Is.EqualTo(16));
            Assert.That(set.GetProceduralMeshMaps(16), Is.SameAs(maps));
            set.Dispose();
            Object.DestroyImmediate(mesh);
        }
    }
}
#endif
