#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using UMA.Editors.PackageSupport;
using UnityEditor;

namespace UMA.Editors.Tests
{
    // The installer keeps an unfocused Editor ticking with Unity's internal EditorApplication.SignalTick.
    // These tests pin the fallback: if Unity removes or changes that method, or it throws, the helper
    // switches itself off instead of breaking the Editor loop.
    public sealed class UMAEditorKeepTickingTests
    {
        private static readonly Type Helper = typeof(UMAContentPackageInstaller).Assembly
            .GetType("UMA.Editors.PackageSupport.UMAEditorKeepTicking", true);

        private static Action Resolve(Type type) =>
            (Action)Helper.GetMethod("ResolveWake", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { type });

        private static bool TryInvoke(ref Action wake)
        {
            object[] args = { wake };
            bool result = (bool)Helper.GetMethod("TryInvoke", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, args);
            wake = (Action)args[0];
            return result;
        }

        private static class NoSignalTick { public static void Other() { } }
        private static class SignalTickWithArgument { internal static void SignalTick(int frames) { } }
        private static class SignalTickReturnsValue { internal static bool SignalTick() => true; }
        private static class InstanceSignalTick { }
        private static class ThrowingSignalTick
        {
            internal static int Calls;
            internal static void SignalTick() { Calls++; throw new InvalidOperationException("removed"); }
        }
        private static class WorkingSignalTick
        {
            internal static int Calls;
            internal static void SignalTick() => Calls++;
        }

        [Test]
        public void MissingOrChangedSignalTickResolvesToNothing()
        {
            Assert.That(Resolve(typeof(NoSignalTick)), Is.Null);
            Assert.That(Resolve(typeof(SignalTickWithArgument)), Is.Null);
            Assert.That(Resolve(typeof(SignalTickReturnsValue)), Is.Null);
            Assert.That(Resolve(typeof(InstanceSignalTick)), Is.Null);
            Assert.That(Resolve(null), Is.Null);
        }

        [Test]
        public void ThrowingSignalTickIsDroppedAfterTheFirstFailure()
        {
            ThrowingSignalTick.Calls = 0;
            Action wake = Resolve(typeof(ThrowingSignalTick));
            Assert.That(wake, Is.Not.Null);
            Assert.That(TryInvoke(ref wake), Is.False);
            Assert.That(wake, Is.Null, "A failing wake-up must not be called again.");
            Assert.That(TryInvoke(ref wake), Is.False);
            Assert.That(ThrowingSignalTick.Calls, Is.EqualTo(1));
        }

        [Test]
        public void WorkingSignalTickIsCalledEachTime()
        {
            WorkingSignalTick.Calls = 0;
            Action wake = Resolve(typeof(WorkingSignalTick));
            Assert.That(TryInvoke(ref wake), Is.True);
            Assert.That(TryInvoke(ref wake), Is.True);
            Assert.That(wake, Is.Not.Null);
            Assert.That(WorkingSignalTick.Calls, Is.EqualTo(2));
        }

        [Test]
        public void UnityProvidesSignalTick()
        {
            // Not a failure when a future Unity removes it: installs then fall back to Unity's
            // normal background rate. Reported so maintainers notice.
            if (Resolve(typeof(EditorApplication)) == null)
                Assert.Inconclusive("This Unity version has no EditorApplication.SignalTick; plugin installs " +
                                    "only progress at Unity's background rate while the Editor is unfocused.");
        }

        [Test]
        public void ResumeWorkRunsOnceFromEditorUpdateNotDelayCall()
        {
            // delayCall never runs in an unfocused Editor, so post-reload resumes must be update-driven.
            int calls = 0;
            Action action = () => calls++;
            Helper.GetMethod("RunWhenEditorReady", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { action });
            Delegate runner = FindRunner(action);
            Assert.That(runner, Is.Not.Null, "The resume must be registered on EditorApplication.update.");
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    Assert.Inconclusive("The Editor is compiling or importing; the runner waits for that by design.");
                runner.DynamicInvoke();
                runner.DynamicInvoke();
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(FindRunner(action), Is.Null, "The runner must remove itself after running.");
            }
            finally { EditorApplication.update -= (EditorApplication.CallbackFunction)runner; }
        }

        [Test]
        public void SrpImportResumeIsScheduledOnEditorUpdateNotDelayCall()
        {
            // The Welcome window's SRP install resumes after reloads and import callbacks; delayCall
            // never runs in an unfocused Editor, so it must be update-driven.
            const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo schedule = typeof(UMA.WelcomeToUMA).GetMethod("ScheduleSrpImportResume", any);
            MethodInfo resume = typeof(UMA.WelcomeToUMA).GetMethod("ResumePendingSrpImport", any);
            Assert.That(schedule, Is.Not.Null);
            var callback = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(
                typeof(EditorApplication.CallbackFunction), resume);
            bool wasRegistered = Registered("update", resume);
            try
            {
                schedule.Invoke(null, null);
                schedule.Invoke(null, null);
                Assert.That(Count("update", resume), Is.EqualTo(1), "Scheduling twice must not run the resume twice per update.");
                Assert.That(Registered("delayCall", resume), Is.False);
            }
            finally
            {
                if (!wasRegistered) EditorApplication.update -= callback;
            }
        }

        private static bool Registered(string eventName, MethodInfo method) => Count(eventName, method) > 0;

        private static int Count(string eventName, MethodInfo method)
        {
            var list = typeof(EditorApplication).GetField(eventName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as Delegate;
            if (list == null) return 0;
            int count = 0;
            foreach (Delegate d in list.GetInvocationList())
                if (d.Method == method) count++;
            return count;
        }

        private static Delegate FindRunner(Action action)
        {
            var update = typeof(EditorApplication).GetField("update",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as Delegate;
            if (update == null) return null;
            foreach (Delegate candidate in update.GetInvocationList())
            {
                object closure = candidate.Target;
                if (closure == null || closure.GetType().DeclaringType != Helper) continue;
                foreach (FieldInfo field in closure.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (ReferenceEquals(field.GetValue(closure), action)) return candidate;
            }
            return null;
        }

        [Test]
        public void NoInstallWorkMeansNoTicking()
        {
            bool active = (bool)Helper.GetMethod("IsInstallWorkActive", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
            bool expected = UMAContentPackageInstaller.IsInstallingAllPlugins ||
                            System.IO.File.Exists("Library/UMA/ContentInstaller/pending.json") ||
                            UMAPluginPackageDownload.IsActive ||
                            System.IO.File.Exists("Library/UMA/PluginRemoval/pending.json");
            Assert.That(active, Is.EqualTo(expected));
        }
    }
}
#endif
