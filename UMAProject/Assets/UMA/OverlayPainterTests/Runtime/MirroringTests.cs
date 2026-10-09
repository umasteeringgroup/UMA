#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class MirroringTests
    {
        [Test]
        public void GlobalXMirrorIsAnInvolution()
        {
            Vector3 point = new Vector3(1.25f, -2f, 3f);
            Vector3 mirrored = TexturePaintMath.MirrorAcrossGlobalX(point);
            Assert.That(Vector3.Distance(mirrored, new Vector3(-1.25f, -2f, 3f)), Is.LessThan(0.00001f));
            Assert.That(Vector3.Distance(TexturePaintMath.MirrorAcrossGlobalX(mirrored), point), Is.LessThan(0.00001f));
        }
    }
}
#endif
