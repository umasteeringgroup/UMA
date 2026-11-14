using System;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;

namespace UMA.Editors.PackageSupport
{
    /// <summary>
    /// Keeps the Editor loop running while plugin packages are installed, downloaded or removed.
    /// Those steps are chained through <see cref="EditorApplication.update"/>, which Unity throttles
    /// while its window is in the background. Steps that resume after a domain reload must use
    /// <see cref="RunWhenEditorReady"/>, not delayCall: an unfocused Editor does not run delayCall at all.
    ///
    /// Waking the loop uses Unity's internal <c>EditorApplication.SignalTick</c> (the same call the
    /// Unity Pipeline package's auto-tick uses), found by reflection. If it is missing, has another
    /// signature or throws, this helper switches itself off and installs simply run at Unity's normal
    /// background rate, as they did before. It only runs while install work is active and stops when
    /// the work finishes. A minimized or fully covered Editor window may still not tick.
    /// </summary>
    [InitializeOnLoad]
    internal static class UMAEditorKeepTicking
    {
        internal const long TickIntervalMilliseconds = 16;
        private const long BusyCheckIntervalMilliseconds = 250;

        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static long lastTick = long.MinValue / 2;
        private static long lastBusyCheck = long.MinValue / 2;
        private static bool busy;
        private static bool resolved;
        private static Action signalTick;

        static UMAEditorKeepTicking()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            // A domain reload in the middle of a batch: start ticking again straight away.
            if (IsInstallWorkActive())
            {
                busy = true;
                Wake();
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> once, on the first Editor update after compiling and importing
        /// have finished. Use this instead of <see cref="EditorApplication.delayCall"/> for work that must
        /// continue while Unity is in the background: Unity only runs delayCall after the inspectors
        /// repaint, which an unfocused Editor does not do, so a batch resumed that way waits for focus.
        /// </summary>
        internal static void RunWhenEditorReady(Action action)
        {
            if (action == null)
                return;
            EditorApplication.CallbackFunction runner = null;
            bool ran = false;
            runner = () =>
            {
                // Unity may still call a callback from the update snapshot it already took.
                if (ran || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return;
                ran = true;
                EditorApplication.update -= runner;
                action();
            };
            EditorApplication.update += runner;
        }

        /// <summary>True while installer, download or removal work is driven from the Editor loop.</summary>
        internal static bool IsInstallWorkActive()
        {
            try
            {
                return UMAContentPackageInstaller.IsInstallingAllPlugins ||
                       UMAContentPackageInstaller.HasPendingImport ||
                       UMAPluginPackageDownload.IsActive ||
                       UMAPluginPackageRemoval.HasPendingRemoval;
            }
            catch
            {
                // Never let a state probe break the Editor update loop.
                return false;
            }
        }

        /// <summary>True when the internal wake-up call is available in this Unity version.</summary>
        internal static bool IsAvailable
        {
            get
            {
                EnsureResolved();
                return signalTick != null;
            }
        }

        private static void Update()
        {
            long now = clock.ElapsedMilliseconds;
            if (now - lastBusyCheck >= BusyCheckIntervalMilliseconds)
            {
                lastBusyCheck = now;
                busy = IsInstallWorkActive();
            }
            if (!busy || now - lastTick < TickIntervalMilliseconds)
                return;
            lastTick = now;
            Wake();
        }

        private static void Wake()
        {
            EnsureResolved();
            if (!TryInvoke(ref signalTick))
                resolved = true; // Stay off for the rest of the session; the next reload looks again.
        }

        private static void EnsureResolved()
        {
            if (resolved)
                return;
            signalTick = ResolveWake(typeof(EditorApplication));
            resolved = true;
        }

        /// <summary>
        /// Finds a static, parameterless, void <c>SignalTick</c> method on <paramref name="editorApplication"/>.
        /// Returns null when Unity no longer provides it in that form.
        /// </summary>
        internal static Action ResolveWake(Type editorApplication)
        {
            try
            {
                MethodInfo method = editorApplication?.GetMethod("SignalTick",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters)
                    return null;
                return (Action)Delegate.CreateDelegate(typeof(Action), method, false);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Calls <paramref name="wake"/> when set. If it throws, clears it so it is not called again,
        /// and returns false. Returns false when there is nothing to call.
        /// </summary>
        internal static bool TryInvoke(ref Action wake)
        {
            if (wake == null)
                return false;
            try
            {
                wake();
                return true;
            }
            catch
            {
                wake = null;
                return false;
            }
        }
    }
}
