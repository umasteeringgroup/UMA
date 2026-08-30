#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;

namespace UMA.Editors.Tests
{
    public sealed class WelcomeToUMAStartupTests
    {
        private static MethodInfo shouldShowAutomatically;

        [OneTimeSetUp]
        public void FindStartupDecision()
        {
            shouldShowAutomatically = typeof(WelcomeToUMA).GetMethod(
                "ShouldShowAutomatically",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(shouldShowAutomatically, Is.Not.Null);
        }

        [Test]
        [Category("UMA")]
        public void EnabledWelcomeOpensDuringFirstSessionCheck()
        {
            Assert.That(ShouldShow(true, true, false, false, false), Is.True);
        }

        [Test]
        [Category("UMA")]
        public void EnabledWelcomeStaysClosedAfterDomainReload()
        {
            Assert.That(ShouldShow(true, false, false, false, false), Is.False);
        }

        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        [Category("UMA")]
        public void RequiredSetupCanStillOpenAfterDomainReload(
            bool requiresContent, bool requiresSrp, bool hasSrpUpdate)
        {
            Assert.That(ShouldShow(false, false, requiresContent, requiresSrp,
                hasSrpUpdate), Is.True);
        }

        private static bool ShouldShow(bool showAtStartup, bool isStartupCheck,
            bool requiresContent, bool requiresSrp, bool hasSrpUpdate)
        {
            return (bool)shouldShowAutomatically.Invoke(null, new object[]
            {
                showAtStartup,
                isStartupCheck,
                requiresContent,
                requiresSrp,
                hasSrpUpdate
            });
        }
    }
}
#endif
