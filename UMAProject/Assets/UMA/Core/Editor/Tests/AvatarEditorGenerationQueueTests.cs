using System;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    /// <summary>
    /// The edit-time build an avatar queues must not get lost. A queue flag left set without its
    /// delayCall blocked every later edit-time build of that avatar: the rebuild after closing the
    /// Vertex Editor or Face Editor (so slots hidden there stayed hidden), the rebuild after a script
    /// reload, and the toolbar's Rebuild.
    /// </summary>
    public sealed class AvatarEditorGenerationQueueTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string BuildCallback = "InternalGenerateSingleUMA";

        [Test]
        public void ABuildWhoseCallbackWasDroppedIsQueuedAgain()
        {
            var root = new GameObject("Avatar generation queue test");
            root.SetActive(false);
            var avatar = root.AddComponent<DynamicCharacterAvatar>();
            bool paused = DynamicCharacterAvatar.EditorGenerationPaused;
            MethodInfo method = typeof(DynamicCharacterAvatar).GetMethod(BuildCallback, PrivateInstance);
            Assert.That(method, Is.Not.Null);
            var callback = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), avatar, method);
            try
            {
                DynamicCharacterAvatar.EditorGenerationPaused = false;
                // What a domain reload used to leave behind: the flag set, its callback gone.
                typeof(DynamicCharacterAvatar).GetField("editorGenerationQueued", PrivateInstance).SetValue(avatar, true);
                Assert.That(PendingBuilds(avatar), Is.EqualTo(0));

                avatar.GenerateSingleUMA();
                Assert.That(PendingBuilds(avatar), Is.EqualTo(1));

                avatar.GenerateSingleUMA();
                avatar.GenerateSingleUMA(false, true);
                Assert.That(PendingBuilds(avatar), Is.EqualTo(1), "Asking again queues no second build.");
            }
            finally
            {
                // Remove this avatar's callback only, before delayCall dispatches it.
                for (int i = PendingBuilds(avatar); i > 0; i--)
                {
                    EditorApplication.delayCall -= callback;
                }
                Object.DestroyImmediate(root);
                DynamicCharacterAvatar.EditorGenerationPaused = paused;
            }
        }

        [TestCase("editorGenerationQueued")]
        [TestCase("pendingEditorGenerationIgnoresPause")]
        [TestCase("generateWait")]
        public void QueueStateDoesNotOutliveAScriptReload(string fieldName)
        {
            FieldInfo field = typeof(DynamicCharacterAvatar).GetField(fieldName, PrivateInstance);
            Assert.That(field, Is.Not.Null, fieldName);
            // Unity keeps private fields through a domain reload, but not the delayCall they describe.
            Assert.That(field.IsNotSerialized, Is.True, fieldName);
        }

        private static int PendingBuilds(DynamicCharacterAvatar avatar)
        {
            if (EditorApplication.delayCall == null)
            {
                return 0;
            }
            int count = 0;
            foreach (Delegate pending in EditorApplication.delayCall.GetInvocationList())
            {
                if (ReferenceEquals(pending.Target, avatar) && pending.Method.Name == BuildCallback)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
