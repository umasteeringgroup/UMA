#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class MaskAndMeshMapTests
    {
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
