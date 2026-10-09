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

        [Test]
        public void WholeBatchApprovalSurvivesReloadAndOnlyAppliesToCurrentPackage()
        {
            object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCardsTests);
            BatchType.GetField("confirmed").SetValue(batch, true);
            Assert.That(Approved(batch, UMAContentKind.HairCards), Is.False, "Approval is scoped to an active package.");
            Advance(batch, false, _ => UMAContentInstallationState.Missing);
            batch = JsonUtility.FromJson(JsonUtility.ToJson(batch), BatchType);
            Assert.That(Approved(batch, UMAContentKind.HairCards), Is.True);
            Assert.That(Approved(batch, UMAContentKind.HairCardsTests), Is.False);
            Assert.That(Approved(batch, UMAContentKind.Uma3), Is.False);
            Advance(batch, false, kind => kind == UMAContentKind.HairCards
                ? UMAContentInstallationState.Installed : UMAContentInstallationState.Missing);
            Assert.That(Approved(batch, UMAContentKind.HairCardsTests), Is.True);
            Assert.That(Approved(batch, UMAContentKind.HairCards), Is.False);
        }

        [Test]
        public void UnconfirmedLegacyBatchDoesNotSuppressIndividualConfirmations()
        {
            object batch = CreateBatch(UMAContentKind.HairCards);
            Advance(batch, false, _ => UMAContentInstallationState.Missing);
            Assert.That(Approved(batch, UMAContentKind.HairCards), Is.False);
            Assert.That(Approved(null, UMAContentKind.HairCards), Is.False);
        }

        [Test]
        public void ReportDistinguishesNewInstallsFromAlreadyInstalledPackagesAfterReload()
        {
            object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCardsExamples, UMAContentKind.HairCardsTests);
            Advance(batch, false, kind => kind == UMAContentKind.HairCards
                ? UMAContentInstallationState.Installed : UMAContentInstallationState.Missing);
            batch = JsonUtility.FromJson(JsonUtility.ToJson(batch), BatchType);
            Assert.That(Advance(batch, false, _ => UMAContentInstallationState.Installed), Is.EqualTo("Complete"));
            string report = Report(batch);
            Assert.That(report, Does.Contain("Already installed — Hair Card Editor"));
            Assert.That(report, Does.Contain("Installed — Hair Card Examples"));
            Assert.That(report, Does.Contain("Already installed — Hair Card Tests"));
            Assert.That(report, Does.Not.Contain("Not attempted"));
        }

        [Test]
        public void ReportRetainsFailedPackageFilenameAndUnattemptedCompanions()
        {
            object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCardsTests);
            Advance(batch, false, _ => UMAContentInstallationState.Missing);
            BatchType.GetField("failure").SetValue(batch, "Invalid package: C:/Packages/HairCards-3.1.2.unitypackage");
            batch = JsonUtility.FromJson(JsonUtility.ToJson(batch), BatchType);
            // A rollback may restore an older valid installation. That is still a failed attempt.
            Assert.That(Advance(batch, false, _ => UMAContentInstallationState.Installed), Is.EqualTo("Failed"));
            string report = Report(batch);
            Assert.That(report, Does.Contain("Failed / cancelled — Hair Card Editor"));
            Assert.That(report, Does.Contain("Not attempted — Hair Card Tests"));
            Assert.That(report, Does.Contain("C:/Packages/HairCards-3.1.2.unitypackage"));
        }

        private static bool Approved(object batch, UMAContentKind kind) =>
            (bool)typeof(UMAContentPackageInstaller).GetMethod("IsApprovedBatchPackage", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { batch, kind });

        [Test]
        public void FreshConfirmedBatchCanStartAfterItsFirstSerialization()
        {
            object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCardsTests);
            BatchType.GetField("confirmed").SetValue(batch, true);
            // Reproduce the initial SaveInstallBatch/load boundary, before any Advance call.
            batch = JsonUtility.FromJson(JsonUtility.ToJson(batch), BatchType);
            Assert.That(Advance(batch, false, _ => UMAContentInstallationState.Missing), Is.EqualTo("Install"));
            Assert.That(Approved(batch, UMAContentKind.HairCards), Is.True);
            Assert.That(((string[])BatchType.GetField("statuses").GetValue(batch)).Length, Is.EqualTo(2));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        public void RestoredStatusArraysAreResizedWithoutLosingCompletedResults(int length)
        {
            object batch = CreateBatch(UMAContentKind.HairCards, UMAContentKind.HairCardsTests);
            var statuses = new string[length];
            if (length > 0) statuses[0] = "Installed";
            BatchType.GetField("statuses").SetValue(batch, statuses);
            BatchType.GetField("nextIndex").SetValue(batch, 1);
            batch = JsonUtility.FromJson(JsonUtility.ToJson(batch), BatchType);
            Assert.That(Advance(batch, false, _ => UMAContentInstallationState.Missing), Is.EqualTo("Install"));
            var repaired = (string[])BatchType.GetField("statuses").GetValue(batch);
            Assert.That(repaired.Length, Is.EqualTo(2));
            if (length > 0) Assert.That(repaired[0], Is.EqualTo("Installed"));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(99)]
        public void FinishMalformedBatchStopsUpdatesAndQueuesOneReport(int index)
        {
            Assert.That(UMAContentPackageInstaller.IsInstallingAllPlugins, Is.False, "Do not interrupt a real installation.");
            const string queueKey = "UMA.ContentInstaller.InstallAllPlugins";
            const string reportKey = "UMA.ContentInstaller.InstallAllPluginsReport";
            string oldReport = UnityEditor.SessionState.GetString(reportKey, "");
            string path = System.IO.Path.GetFullPath("Library/UMA/ContentInstaller/LastPluginInstallReport.txt");
            byte[] oldFile = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
            object batch = CreateBatch(UMAContentKind.HairCards, (UMAContentKind)999);
            BatchType.GetField("statuses").SetValue(batch, Array.Empty<string>());
            BatchType.GetField("nextIndex").SetValue(batch, index);
            var type = typeof(UMAContentPackageInstaller);
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var update = (UnityEditor.EditorApplication.CallbackFunction)Delegate.CreateDelegate(
                typeof(UnityEditor.EditorApplication.CallbackFunction), type.GetMethod("UpdateAllPluginInstallation", flags));
            var showReport = (UnityEditor.EditorApplication.CallbackFunction)Delegate.CreateDelegate(
                typeof(UnityEditor.EditorApplication.CallbackFunction), type.GetMethod("ShowInstallBatchReport", flags));
            try
            {
                UnityEditor.SessionState.SetString(queueKey, JsonUtility.ToJson(batch));
                UnityEditor.EditorApplication.update += update;
                Assert.DoesNotThrow(() => type.GetMethod("FinishInstallBatch", flags)
                    .Invoke(null, new object[] { batch, "Invalid saved queue" }));
                Assert.That(UMAContentPackageInstaller.IsInstallingAllPlugins, Is.False);
                Assert.That(UnityEditor.EditorApplication.update.GetInvocationList().Contains(update), Is.False);
                string report = UnityEditor.SessionState.GetString(reportKey, "");
                Assert.That(report, Does.Contain("Invalid saved queue"));
                Assert.That(report, Does.Contain("Unknown package (999)"));
                Assert.DoesNotThrow(() => type.GetMethod("UpdateAllPluginInstallation", flags).Invoke(null, null));
                Assert.That(UnityEditor.SessionState.GetString(reportKey, ""), Is.EqualTo(report));
            }
            finally
            {
                UnityEditor.EditorApplication.update -= update;
                UnityEditor.EditorApplication.update -= showReport;
                UnityEditor.SessionState.EraseString(queueKey);
                UnityEditor.SessionState.SetString(reportKey, oldReport);
                if (!string.IsNullOrEmpty(oldReport)) UnityEditor.EditorApplication.update += showReport;
                if (oldFile == null) System.IO.File.Delete(path);
                else System.IO.File.WriteAllBytes(path, oldFile);
            }
        }

        private static string Report(object batch) =>
            (string)typeof(UMAContentPackageInstaller).GetMethod("FormatInstallBatchReport", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new[] { batch });

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
