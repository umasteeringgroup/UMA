using NUnit.Framework;
using System.Collections.Generic;
using UMA.Examples;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA.Editors.Tests
{
    public class UMASimpleLODDonorTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void OnlyDrawnSlotsContributeInternalLodTriangles(bool targetHasLods)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var target = ScriptableObject.CreateInstance<SlotDataAsset>();
            var donor = ScriptableObject.CreateInstance<SlotDataAsset>();
            var mesh = new Mesh();
            try
            {
                var root = new GameObject("Internal LOD test");
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                var data = root.AddComponent<UMAData>();
                var lod = root.AddComponent<UMASimpleLOD>();
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                var full = new[] { 0, 1, 2, 2, 1, 3 };
                var reduced = new[] { 0, 1, 3 };
                var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
                mesh.vertices = vertices;
                mesh.triangles = full;
                var deltas = new Vector3[vertices.Length];
                deltas[0] = Vector3.up;
                mesh.AddBlendShapeFrame("Smile", 100, deltas, null, null);
                renderer.sharedMesh = mesh;
                data.SetRenderers(new[] { renderer });
                data.SetRendererAssets(new UMARendererAsset[1]);

                target.name = "LodTestTarget";
                donor.name = "LodTestDonor";
                target.meshData = MakeMeshData(targetHasLods, full, reduced);
                donor.meshData = MakeMeshData(true, full, reduced);
                var source = new SlotData(donor) { blendShapeTargetSlot = target.slotName };
                data.umaRecipe = new UMAData.UMARecipe { slotDataList = new[] { new SlotData(target), source } };

                // The donor has valid triangles and LOD ranges, but no offset in the combined mesh.
                foreach (int level in new[] { 1, 0 })
                {
                    lod.DoManualLODCheck(level);
                    lod.UpdateInternalLOD();
                    CollectionAssert.AreEqual(level == 1 && targetHasLods ? reduced : full, mesh.triangles);
                    Assert.That(lod.SlotLodStatuses.ContainsKey(donor.slotName), Is.False);
                    Assert.That(data.umaRecipe.slotDataList[1], Is.SameAs(source));
                    Assert.That(mesh.GetBlendShapeIndex("Smile"), Is.EqualTo(0));
                }
                data.SetRenderers(System.Array.Empty<SkinnedMeshRenderer>());
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (mesh != null) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(donor);
                Object.DestroyImmediate(target);
            }
        }

        private static UMAMeshData MakeMeshData(bool withLods, int[] full, int[] reduced)
        {
            var indices = new List<int>(full);
            var submesh = new SubMeshTriangles(full);
            if (withLods)
            {
                indices.AddRange(reduced);
                submesh.SetTriangles(indices.ToArray());
                submesh.SetLodRanges(new List<UMALodRange>
                {
                    new UMALodRange(0, (uint)full.Length),
                    new UMALodRange((uint)full.Length, (uint)reduced.Length)
                });
            }
            return new UMAMeshData { vertexCount = 4, subMeshCount = 1, submeshes = new[] { submesh } };
        }
    }
}
