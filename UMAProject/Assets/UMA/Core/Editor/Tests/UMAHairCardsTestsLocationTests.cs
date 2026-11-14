using NUnit.Framework;
using UMA.Editors.PackageSupport;

namespace UMA.Editors.Tests
{
    public sealed class UMAHairCardsTestsLocationTests
    {
        [Test]
        public void HairCardTestsLiveBesideThePluginAndTheOldFolderStaysACompanionPath()
        {
            string root = UMAContentCatalog.Root(UMAContentKind.HairCardsTests);
            string legacy = UMAContentCatalog.LegacyRoot(UMAContentKind.HairCardsTests);
            Assert.That(root, Is.EqualTo("Assets/UMA/HairCardsTests"));
            Assert.That(legacy, Is.EqualTo("Assets/UMA/HairCards/Editor/Tests"));
            Assert.That(root.StartsWith(UMAContentCatalog.Root(UMAContentKind.HairCards) + "/"), Is.False);

            // A Hair Cards install or removal must never treat an old tests copy as its own files.
            Assert.That(UMAContentCatalog.IsCompanionPath(UMAContentKind.HairCards, legacy + "/HairCoreTests.cs"), Is.True);
            Assert.That(UMAContentCatalog.IsCompanionPath(UMAContentKind.HairCards, legacy + ".meta"), Is.True);
            Assert.That(UMAContentCatalog.OwnsPluginPath(UMAContentKind.HairCards, legacy + "/HairCoreTests.cs"), Is.False);
            Assert.That(UMAContentCatalog.OwnsPluginPath(UMAContentKind.HairCards, "Assets/UMA/HairCards/Editor/HairCardStage.cs"), Is.True);
            Assert.That(UMAContentCatalog.LegacyRoot(UMAContentKind.OverlayPainterTests), Is.Null);
        }
    }
}
