using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>The Mesh Hide Asset inspector's preview: the slot upright, hidden triangles in their own submesh.</summary>
    public sealed class MeshHideInspectorPreviewTests
    {
        private static MeshHideAsset Find(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:MeshHideAsset"))
            {
                var hide = AssetDatabase.LoadAssetAtPath<MeshHideAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (hide != null && hide.name == name) return hide;
            }
            Assert.Ignore("The project has no Mesh Hide Asset named " + name);
            return null;
        }

        // A UMA 3 body tile, and a UMA 2 slot whose rig lies along another axis at the rig root.
        [TestCase("MeshHideAssets_UMA30_Body_UDIM1002")]
        [TestCase("SweatPantAnklesMeshHideAsset")]
        public void ThePreviewSplitsHiddenTrianglesFromVisibleOnesAndStandsTheSlotUpright(string hideName)
        {
            MeshHideAsset hide = Find(hideName);
            SlotDataAsset slot = hide.asset;
            Assert.IsNotNull(slot, "the hide's slot must be in the Global Library");
            Assert.IsTrue(hide.IsCompatibleWithSlot(slot, out string reason), reason);

            Mesh mesh = MeshHideInspector.BuildPreviewMesh(hide, slot, out int hidden, out int total, out string problem);
            try
            {
                Assert.IsNotNull(mesh, problem);
                Assert.IsNull(problem);
                Assert.That(hidden, Is.GreaterThan(0), "the test hides must hide something");
                Assert.AreEqual(hide.HiddenCount, hidden);
                Assert.AreEqual(hide.TriangleCount, total);
                Assert.AreEqual(2, mesh.subMeshCount);
                Assert.AreEqual(hidden * 3, (int)mesh.GetIndexCount(1), "submesh 1 is the hidden (red) triangles");
                Assert.AreEqual((total - hidden) * 3, (int)mesh.GetIndexCount(0), "submesh 0 is the visible (white) triangles");
                Vector3 size = mesh.bounds.size;
                Assert.That(size.y, Is.GreaterThan(size.z), "upright: taller than it is deep");
                Assert.That(mesh.bounds.center.y, Is.GreaterThan(0.2f), "standing on the ground, above the feet");
            }
            finally
            {
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AHideWithoutASlotExplainsInsteadOfPreviewing()
        {
            var hide = ScriptableObject.CreateInstance<MeshHideAsset>();
            try
            {
                Assert.IsNull(MeshHideInspector.BuildPreviewMesh(hide, null, out _, out _, out string problem));
                Assert.IsNotNull(problem);
                Assert.IsNull(MeshHideInspector.BuildPreviewMesh(null, null, out _, out _, out problem));
                Assert.IsNotNull(problem);
            }
            finally { Object.DestroyImmediate(hide); }
        }
    }
}
