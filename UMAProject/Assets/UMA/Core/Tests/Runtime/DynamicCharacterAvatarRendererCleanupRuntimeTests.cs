using System.Collections;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class DynamicCharacterAvatarRendererCleanupRuntimeTests
    {
        [UnityTest]
        public IEnumerator AwakePreservesAttachmentsAndRetiresOrphansBeforeDeferredDestruction()
        {
            var root = new GameObject("Avatar cleanup startup test");
            var orphanObject = new GameObject("Generated orphan");
            var mesh = new Mesh { name = "UMAMesh" };
            try
            {
                var authored = new GameObject("Authored hair");
                authored.transform.SetParent(root.transform, false);
                authored.AddComponent<SkinnedMeshRenderer>();
                var ignored = new GameObject("UMARenderer 1");
                ignored.transform.SetParent(root.transform, false);
                ignored.tag = UMASettings.GetIgnoreTag();
                ignored.AddComponent<SkinnedMeshRenderer>();
                ignored.AddComponent<UMAGeneratedRenderer>();
                orphanObject.transform.SetParent(root.transform, false);
                orphanObject.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
                orphanObject.AddComponent<UMAGeneratedRenderer>();

                // Awake must clean serialized preview output before Start builds again.
                var avatar = root.AddComponent<DynamicCharacterAvatar>();
                avatar.editorTimeGeneration = false;
                avatar.enabled = false;

                Assert.That(authored != null && ignored != null, Is.True);
                Assert.That(orphanObject != null, Is.True, "Destruction is deferred in Play Mode.");
                Assert.That(orphanObject.transform.parent, Is.Null, "Retire the old animation path immediately.");
                Assert.That(orphanObject.activeSelf, Is.False);
                avatar.HideAndCleanup(false); // Repeated cleanup must not double-release the orphan.

                yield return null;

                Assert.That(orphanObject == null, Is.True);
                Assert.That(mesh == null, Is.True);
                Assert.That(authored != null && ignored != null, Is.True);
            }
            finally
            {
                if (root != null) Object.Destroy(root);
                if (orphanObject != null) Object.Destroy(orphanObject);
                if (mesh != null) Object.Destroy(mesh);
            }
            yield return null;
        }
    }
}
