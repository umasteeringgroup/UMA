using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Tests
{
    public sealed class AvatarStartupTests
    {
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void EditorStartIgnoresStaleGuardsAndRespectsPreviewSettings(bool buildEnabled, bool previewEnabled)
        {
            var root = new GameObject("Avatar startup regression test");
            root.SetActive(false);
            var avatar = root.AddComponent<DynamicCharacterAvatar>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            bool paused = DynamicCharacterAvatar.EditorGenerationPaused;
            try
            {
                DynamicCharacterAvatar.EditorGenerationPaused = false;
                avatar.BuildCharacterEnabled = buildEnabled;
                avatar.editorTimeGeneration = previewEnabled;
                avatar.StartGuard = true;
                typeof(DynamicCharacterAvatar).GetField("npcStartupHandled", flags).SetValue(avatar, true);
                avatar.Start();
                Assert.That(avatar.StartGuard, Is.True);
                Assert.That(typeof(DynamicCharacterAvatar).GetField("editorGenerationQueued", flags).GetValue(avatar),
                    Is.EqualTo(buildEnabled && previewEnabled));
            }
            finally
            {
                // Destroy before delayCall dispatches; no full character generation is needed here.
                Object.DestroyImmediate(root);
                DynamicCharacterAvatar.EditorGenerationPaused = paused;
            }
        }

        [Test]
        public void StartupGuardIsNotSerialized()
        {
            var root = new GameObject("Avatar guard serialization test");
            root.SetActive(false);
            try
            {
                var avatar = root.AddComponent<DynamicCharacterAvatar>();
                avatar.StartGuard = true;
                using (var serialized = new SerializedObject(avatar))
                    Assert.That(serialized.FindProperty("StartGuard"), Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
