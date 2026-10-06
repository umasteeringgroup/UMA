#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class NormalTouchupTests
    {
        [Test]
        public void TouchupPreservesLengthAndMovesTowardVertexNormal()
        {
            Vector3 original = new Vector3(0.8f, 0.1f, 0.55f).normalized;
            Vector3 result = TexturePaintMath.BendNormalTowardVertexNormal(original, Vector3.forward,
                new Vector4(1f, 0f, 0f, 1f), 0.5f);
            Assert.That(result.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(result.z, Is.GreaterThan(original.z));
        }

        [Test]
        public void FullStrengthProducesFlatTangentSpaceNormal()
        {
            Vector3 result = TexturePaintMath.BendNormalTowardVertexNormal(Vector3.right, Vector3.up,
                new Vector4(1f, 0f, 0f, 1f), 1f);
            Assert.That(result.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(result.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(result.z, Is.EqualTo(1f).Within(0.0001f));
        }
    }
}
#endif
