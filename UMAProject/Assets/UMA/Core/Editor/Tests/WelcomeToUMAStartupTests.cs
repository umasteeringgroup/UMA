#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;

namespace UMA.Editors.Tests
{
    public sealed class WelcomeToUMAStartupTests
    {
        private static MethodInfo shouldShowAutomatically;
        private static MethodInfo shouldOpenAutomatically;

        [OneTimeSetUp]
        public void FindStartupDecision()
        {
            shouldShowAutomatically = typeof(WelcomeToUMA).GetMethod(
                "ShouldShowAutomatically",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(shouldShowAutomatically, Is.Not.Null);
            shouldOpenAutomatically = typeof(WelcomeToUMA).GetMethod(
                "ShouldOpenAutomatically",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(shouldOpenAutomatically, Is.Not.Null);
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

        [Test]
        [Category("UMA")]
        public void DismissedRequiredSetupStaysClosedForTheSameProjectState()
        {
            Assert.That(ShouldOpen(false, false, false, true, false,
                "hdrp-missing", "hdrp-missing"), Is.False);
        }

        [Test]
        [Category("UMA")]
        public void ChangedRequiredSetupCanPromptAfterAnEarlierDismissal()
        {
            Assert.That(ShouldOpen(false, false, false, true, false,
                "hdrp-missing", "hdrp-installed-content-missing"), Is.True);
        }

        [Test]
        [Category("UMA")]
        public void UndismissedRequiredSetupStillPromptsAutomatically()
        {
            Assert.That(ShouldOpen(false, false, false, true, false,
                string.Empty, "hdrp-missing"), Is.True);
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

        private static bool ShouldOpen(bool showAtStartup, bool isStartupCheck,
            bool requiresContent, bool requiresSrp, bool hasSrpUpdate,
            string dismissedSignature, string currentSignature)
        {
            return (bool)shouldOpenAutomatically.Invoke(null, new object[]
            {
                showAtStartup,
                isStartupCheck,
                requiresContent,
                requiresSrp,
                hasSrpUpdate,
                dismissedSignature,
                currentSignature
            });
        }
    }
}
#endif
