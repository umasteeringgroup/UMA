#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UMA.Editors.PackageSupport;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAPluginInstallAllTests
    {
        private static readonly Type BatchType = typeof(UMAContentPackageInstaller)
            .GetNestedType("PluginInstallBatch", BindingFlags.NonPublic);

        [Test]
        public void BatchSkipsInstalledPackagesAndStartsTheMissingParentBeforeItsCompanions()
        {
            UMAContentKind[] kinds = UMAContentCatalog.PluginDisplayOrder.ToArray();
            object batch = CreateBatch(kinds);
            Assert.That(kinds.Contains(UMAContentKind.Uma3), Is.False);
            foreach (UMAContentKind kind in kinds)
            {
                UMAContentKind? parent = UMAContentCatalog.ParentPlugin(kind);
                if (parent.HasValue)
                    Assert.That(Array.IndexOf(kinds, parent.Value), Is.LessThan(Array.IndexOf(kinds, kind)));
            }

            string step = Advance(batch, false, kind => kind == UMAContentKind.HairCards ||
                UMAContentCatalog.ParentPlugin(kind) == UMAContentKind.HairCards
                ? UMAContentInstallationState.Missing : UMAContentInstallationState.Installed);
            Assert.That(step, Is.EqualTo("Install"));
            Assert.That(kinds[Index(batch)], Is.EqualTo(UMAContentKind.HairCards));
            Assert.That(Awaiting(batch), Is.True);
        }

        [Test]
        public void SerializedBatchWaitsForAsyncCompletionBeforeStartingTheNextPackage()
        {
            object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCardsExamples);
            Assert.That(Advance(batch, false, _ => UMAContentInstallationState.Missing), Is.EqualTo("Install"));

            // SessionState stores this same JSON across an assembly reload.
            batch = JsonUtility.FromJson(JsonUtility.ToJson(batch), BatchType);
            Assert.That(Awaiting(batch), Is.True);
            Assert.That(Advance(batch, true, _ => throw new AssertionException("Busy queues must not inspect or start packages.")),
                Is.EqualTo("Wait"));
            Assert.That(Index(batch), Is.Zero);

            Assert.That(Advance(batch, false, kind => kind == UMAContentKind.HairCards
                ? UMAContentInstallationState.Installed : UMAContentInstallationState.Missing), Is.EqualTo("Install"));
            Assert.That(Index(batch), Is.EqualTo(1));
            Assert.That(Awaiting(batch), Is.True);
        }

        [TestCase(UMAContentInstallationState.Missing)]
        [TestCase(UMAContentInstallationState.Unmanaged)]
        public void CancelledOrFailedPackageStopsBeforeAnyFurtherInstallPrompt(UMAContentInstallationState result)
        {
            object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCardsExamples);
            Assert.That(Advance(batch, false, _ => UMAContentInstallationState.Missing), Is.EqualTo("Install"));
            for (int i = 0; i < 2; i++)
                Assert.That(Advance(batch, false, _ => result), Is.EqualTo("Failed"));
            Assert.That(Index(batch), Is.Zero);
        }

        [Test]
        public void CompletedBatchSkipsAlreadyInstalledRemainingPackages()
        {
            object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCardsExamples, UMAContentKind.HairCardsTests);
            Assert.That(Advance(batch, false, _ => UMAContentInstallationState.Missing), Is.EqualTo("Install"));
            Assert.That(Advance(batch, false, _ => UMAContentInstallationState.Installed), Is.EqualTo("Complete"));
            Assert.That(Index(batch), Is.EqualTo(3));
            Assert.That(Awaiting(batch), Is.False);
        }

        private static object CreateBatch(params UMAContentKind[] kinds)
        {
            object batch = Activator.CreateInstance(BatchType, true);
            BatchType.GetField("kinds").SetValue(batch, kinds);
            return batch;
        }

        private static int Index(object batch) => (int)BatchType.GetField("nextIndex").GetValue(batch);
        private static bool Awaiting(object batch) => (bool)BatchType.GetField("awaitingInstallation").GetValue(batch);

        private static string Advance(object batch, bool busy, Func<UMAContentKind, UMAContentInstallationState> getState) =>
            typeof(UMAContentPackageInstaller).GetMethod("AdvanceInstallBatch", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { batch, busy, getState }).ToString();
    }
}
#endif
