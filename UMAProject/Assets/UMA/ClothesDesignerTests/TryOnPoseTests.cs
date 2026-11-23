#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The try-on's raised arms: split between collarbone and upper arm as a body moves.</summary>
    public sealed class TryOnPoseTests
    {
        [TestCase(0f, 150f), TestCase(45f, 150f), TestCase(90f, 150f), TestCase(90f, 180f), TestCase(90f, 20f), TestCase(45f, 90f)]
        public void Split_AddsUpToTheWholeMove(float from, float to)
        {
            DesignerSession.SplitArmRaise(from, to, true, out float shoulder, out float arm);
            Assert.That(shoulder + arm, Is.EqualTo(to - from).Within(1e-4f));
        }

        [Test]
        public void Shoulder_TakesNothingLow_AThirdHigher_AndFiftyAtTheTop()
        {
            Assert.That(DesignerSession.ShoulderPart(0f), Is.EqualTo(0f));
            Assert.That(DesignerSession.ShoulderPart(30f), Is.EqualTo(0f));
            Assert.That(DesignerSession.ShoulderPart(90f), Is.EqualTo(20f).Within(1e-4f));
            Assert.That(DesignerSession.ShoulderPart(180f), Is.EqualTo(50f).Within(1e-4f));
            // From a level (T-pose) start to straight up: 30 at the collarbone, 60 at the shoulder joint.
            DesignerSession.SplitArmRaise(90f, 180f, true, out float shoulder, out float arm);
            Assert.That(shoulder, Is.EqualTo(30f).Within(1e-4f));
            Assert.That(arm, Is.EqualTo(60f).Within(1e-4f));
        }

        [Test]
        public void WithoutShoulderBones_TheArmDoesItAll()
        {
            DesignerSession.SplitArmRaise(90f, 150f, false, out float shoulder, out float arm);
            Assert.That(shoulder, Is.EqualTo(0f));
            Assert.That(arm, Is.EqualTo(60f));
        }

        [Test]
        public void TheAngleStopsAtStraightUp()
        {
            DesignerSession.SplitArmRaise(90f, 220f, true, out float shoulder, out float arm);
            Assert.That(shoulder + arm, Is.EqualTo(90f).Within(1e-4f));
        }
    }
}
#endif
