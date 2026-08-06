#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class MaskAndMeshMapTests
    {
        [Test]
        public void TextureMasksCombineAndRespectSurfaceOwnership()
        {
            Texture2D bitmap = new Texture2D(2, 2, TextureFormat.R8, false, true);
            bitmap.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            bitmap.Apply(false, false);
            TexturePaintMaskStack stack = new TexturePaintMaskStack();
            stack.Add(new TexturePaintMask
            {
                kind = TexturePaintMaskKind.Bitmap,
                grayscaleTexture = bitmap,
                ownerSurfaceId = "4",
                operation = TexturePaintMaskOperation.Intersect
            });
            stack.Add(new TexturePaintMask
            {
                kind = TexturePaintMaskKind.Black,
                ownerSurfaceId = "4",
                operation = TexturePaintMaskOperation.Subtract
            });

            Assert.That(stack.EvaluateTextureMasks(4, "4", Vector2.one * 0.5f), Is.EqualTo(1f));
            Assert.That(stack.EvaluateTextureMasks(5, "5", Vector2.one * 0.5f), Is.EqualTo(1f));
            Assert.That(stack.AllowsStructural(5, 0, 0), Is.True);
            Object.DestroyImmediate(bitmap);
        }

        [Test]
        public void TriangleRestrictedPaintingSkipsRedundantGeometryMask()
        {
            TexturePaintMaskStack empty = new TexturePaintMaskStack();
            TexturePaintMaskStack structural = new TexturePaintMaskStack();
            structural.Add(new TexturePaintMask { kind = TexturePaintMaskKind.UVIsland });
            TexturePaintMaskStack bitmap = new TexturePaintMaskStack();
            bitmap.Add(new TexturePaintMask { kind = TexturePaintMaskKind.Bitmap });

            Assert.That(PaintingEngine.RequiresGeometryMask(empty, true), Is.False);
            Assert.That(PaintingEngine.RequiresGeometryMask(structural, true), Is.False,
                "Structural masks are evaluated per contacted triangle before dispatch.");
            Assert.That(PaintingEngine.RequiresGeometryMask(bitmap, true), Is.True,
                "Per-pixel masks still require a mask texture.");
            Assert.That(PaintingEngine.RequiresGeometryMask(empty, false), Is.True,
                "Unrestricted projection still needs the mesh coverage mask.");
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
                opacity = 0.75f
            };

            TexturePaintLayerChannelSettings clone = original.Clone();

            Assert.That(clone.locked, Is.True);
            Assert.That(clone.contribution, Is.EqualTo(0.35f));
            Assert.That(clone.enabled, Is.False);
            Assert.That(clone.opacity, Is.EqualTo(0.75f));
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
