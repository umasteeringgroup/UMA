#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;

namespace UMA.TexturePaint.Editor.Tests
{
    /// <summary>Scripted sessions: the painter opened by a script neither asks about nor writes recovery assets.</summary>
    public sealed class TexturePaintScriptedSessionTests
    {
        private static bool Opening() => (bool)typeof(TexturePaintStageWindow)
            .GetField("openingScripted", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        [Test]
        public void OpenScripted_MarksOnlyThePainterOpenedInsideIt()
        {
            Assert.That(Opening(), Is.False);
            bool inside = TexturePaintStageWindow.OpenScripted(() => Opening());
            Assert.That(inside, Is.True, "A painter opened inside it starts as a scripted session.");
            Assert.That(Opening(), Is.False, "And one opened after it does not.");
            Assert.Throws<InvalidOperationException>(() => TexturePaintStageWindow.OpenScripted<bool>(() => throw new InvalidOperationException()));
            Assert.That(Opening(), Is.False, "Not left on when opening fails.");
        }

        [Test]
        public void APainterOpenedTheOrdinaryWay_IsNotAScriptedSession()
        {
            var stage = UnityEngine.ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            try { Assert.That(stage.IsScriptedSession, Is.False); }
            finally { UnityEngine.Object.DestroyImmediate(stage); }
        }
    }
}
#endif
