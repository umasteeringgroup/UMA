using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    /// <summary>Draws and dispatches optional editor actions for the actual inspected targets.</summary>
    [InitializeOnLoad]
    public static class UMAPluginGUI
    {
        private sealed class HostState
        {
            internal int Leases;
            internal int AdapterLeases;
            internal int Lifetime;
            internal WeakReference<IUMAPluginHost> Adapter;
            internal bool Pending;
            internal UMAPluginAction[] Layout;
        }

        private sealed class Lease : IDisposable
        {
            private HostState state;
            // The owner keeps this lease alive. Static storage never retains the adapter.
            private IUMAPluginHost adapter;
            internal Lease(HostState state, IUMAPluginHost adapter)
            {
                this.state = state;
                this.adapter = adapter;
            }
            public void Dispose()
            {
                if (state == null) return;
                if (adapter != null && --state.AdapterLeases == 0)
                {
                    state.Adapter = null;
                    state.Lifetime++;
                }
                if (--state.Leases == 0) state.Lifetime++;
                adapter = null;
                state = null;
            }
        }

        private static readonly ConditionalWeakTable<Editor, HostState> hosts = new();
        private static readonly List<PendingAction> pending = new();
        private static bool transitioning;

        static UMAPluginGUI()
        {
            Editor.finishedDefaultHeaderGUI -= DrawHeader;
            Editor.finishedDefaultHeaderGUI += DrawHeader;
            AssemblyReloadEvents.beforeAssemblyReload += CancelPending;
            EditorApplication.quitting += CancelPending;
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }

        /// <summary>
        /// Suppresses automatic header placement for this Editor until disposed.
        /// An optional adapter owns preparation and refresh for window working data.
        /// </summary>
        public static IDisposable RegisterExplicitHost(Editor editor, IUMAPluginHost host = null)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));
            var state = hosts.GetOrCreateValue(editor);
            if (host != null)
            {
                if (!IsAlive(host)) throw new ArgumentException("The synchronization host has been destroyed.", nameof(host));
                if (state.AdapterLeases > 0 &&
                    (!state.Adapter.TryGetTarget(out var existing) || !ReferenceEquals(existing, host)))
                    throw new InvalidOperationException("This Editor already has a different UMA plugin synchronization host.");
                if (state.AdapterLeases++ == 0)
                {
                    state.Adapter = new WeakReference<IUMAPluginHost>(host);
                    state.Lifetime++;
                }
            }
            state.Leases++;
            return new Lease(state, host);
        }

        private static void DrawHeader(Editor editor)
        {
            if (editor == null || (hosts.TryGetValue(editor, out var state) && state.Leases > 0)) return;
            Draw(editor);
        }

        /// <summary>Call once at the top of a host that owns explicit placement.</summary>
        public static void Draw(Editor editor) => Draw(editor, false, null);

        // The core recipe bridge contributes existing inline plugins to the same area.
        internal static void Draw(Editor editor, bool hasLegacyContent, Action drawLegacy)
        {
            if (editor == null || Event.current == null) return;
            var state = hosts.GetOrCreateValue(editor);
            if (Event.current.type == EventType.Layout || state.Layout == null)
                state.Layout = UMAPluginRegistry.Match(editor.targets);
            if (state.Layout.Length == 0 && !hasLegacyContent) return;

            int indent = EditorGUI.indentLevel;
            bool enabled = GUI.enabled;
            Color color = GUI.color, background = GUI.backgroundColor, content = GUI.contentColor;
            try
            {
                EditorGUI.indentLevel = 0;
                EditorGUILayout.LabelField("Editors", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Plugins", EditorStyles.boldLabel);
                    bool changed = GUI.changed;
                    try
                    {
                        foreach (var action in state.Layout)
                        {
                            using (new EditorGUI.DisabledScope(state.Pending || IsBusy ||
                                !IsReady(editor, state) || !action.IsEnabled(editor)))
                            {
                                if (GUILayout.Button(action.Content)) Queue(editor, action);
                            }
                        }
                    }
                    finally { GUI.changed = changed; }
                    // Legacy data changes must remain visible to the owning inspector.
                    if (hasLegacyContent) drawLegacy?.Invoke();
                }
            }
            finally
            {
                EditorGUI.indentLevel = indent;
                GUI.enabled = enabled;
                GUI.color = color;
                GUI.backgroundColor = background;
                GUI.contentColor = content;
            }
        }

        private static bool IsBusy => transitioning || EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying;

        private static bool IsAlive(IUMAPluginHost host) => host != null &&
            (!(host is Object unityObject) || unityObject != null);

        private static bool TryGetHost(Editor editor, HostState state, out IUMAPluginHost host)
        {
            if (state.AdapterLeases > 0)
                return state.Adapter.TryGetTarget(out host) && IsAlive(host);
            host = editor as IUMAPluginHost;
            return host == null || IsAlive(host);
        }

        private static bool IsReady(Editor editor, HostState state)
        {
            try { return TryGetHost(editor, state, out var host) && (host == null || host.CanRunPluginActions); }
            catch (Exception exception)
            {
                UMAPluginDiagnostics.Report("readiness", editor.GetType(), exception);
                return false;
            }
        }

        internal static bool Queue(Editor editor, UMAPluginAction action)
        {
            if (editor == null || action == null || IsBusy) return false;
            var state = hosts.GetOrCreateValue(editor);
            if (state.Pending || pending.Count >= 128 || !IsReady(editor, state) || !action.IsEnabled(editor)) return false;
            if (!TryGetHost(editor, state, out var host)) return false;
            var invocation = new PendingAction(editor, state, action, host);
            state.Pending = true;
            pending.Add(invocation);
            EditorApplication.delayCall += invocation.Run;
            // Also wake on the next editor update: delayCall can stall in unfocused
            // inspectors and while other editor tools own the update loop.
            EditorApplication.update += invocation.Run;
            editor.Repaint();
            return true;
        }

        private sealed class PendingAction
        {
            private readonly WeakReference<Editor> editorReference;
            private readonly WeakReference<IUMAPluginHost> hostReference;
            private readonly Object[] targets;
            private readonly HostState state;
            private readonly UMAPluginAction action;
            private readonly int lifetime;
            private readonly int generation;
            private bool canceled;
            private bool started;

            internal PendingAction(Editor editor, HostState state, UMAPluginAction action, IUMAPluginHost host)
            {
                editorReference = new WeakReference<Editor>(editor);
                if (host != null) hostReference = new WeakReference<IUMAPluginHost>(host);
                targets = (Object[])editor.targets.Clone();
                this.state = state;
                this.action = action;
                lifetime = state.Lifetime;
                generation = UMAPluginRegistry.Generation;
            }

            internal void Cancel()
            {
                canceled = true;
                state.Pending = false;
                EditorApplication.delayCall -= Run;
                EditorApplication.update -= Run;
            }

            private bool HasContext(Editor editor, out IUMAPluginHost host)
            {
                host = null;
                if (canceled || editor == null || lifetime != state.Lifetime ||
                    generation != UMAPluginRegistry.Generation || !SameTargets(editor.targets, targets) ||
                    !TryGetHost(editor, state, out host)) return false;
                if (hostReference == null) return host == null;
                return hostReference.TryGetTarget(out var captured) && IsAlive(captured) && ReferenceEquals(captured, host);
            }

            internal void Run()
            {
                EditorApplication.delayCall -= Run;
                EditorApplication.update -= Run;
                if (canceled || started) return;
                started = true;
                Editor editor = null;
                IUMAPluginHost host = null;
                bool invoked = false;
                try
                {
                    if (IsBusy || !editorReference.TryGetTarget(out editor) ||
                        !HasContext(editor, out host) || !IsReady(editor, state) || !action.Matches(targets)) return;
                    if (host != null)
                    {
                        if (!host.TryPreparePluginAction()) return;
                    }
                    else if (editor.serializedObject.hasModifiedProperties)
                        editor.serializedObject.ApplyModifiedProperties();
                    if (IsBusy || !HasContext(editor, out host) || !IsReady(editor, state) || !action.IsEnabled(editor)) return;
                    invoked = true;
                    action.Invoke(editor);
                }
                catch (Exception exception) { UMAPluginDiagnostics.Report("action/preparation", action.Method, exception); }
                finally
                {
                    try
                    {
                        if (invoked && HasContext(editor, out host))
                        {
                            if (host != null) host.RefreshAfterPluginAction();
                            else editor.serializedObject.Update();
                        }
                    }
                    catch (Exception exception) { UMAPluginDiagnostics.Report("refresh", action.Method, exception); }
                    finally
                    {
                        state.Pending = false;
                        canceled = true;
                        pending.Remove(this);
                        if (editor != null) editor.Repaint();
                    }
                }
            }
        }

        internal static bool SameTargets(Object[] current, Object[] captured)
        {
            if (current.Length != captured.Length) return false;
            for (int i = 0; i < current.Length; i++)
                if (current[i] == null || captured[i] == null || !ReferenceEquals(current[i], captured[i])) return false;
            return current.Length != 0;
        }

        internal static void CancelPending()
        {
            foreach (var invocation in pending) invocation.Cancel();
            pending.Clear();
        }

        private static void PlayModeChanged(PlayModeStateChange change)
        {
            transitioning = change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode;
            CancelPending();
        }
    }
}
