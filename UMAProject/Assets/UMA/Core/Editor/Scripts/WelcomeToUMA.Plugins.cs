using System;
using System.Collections.Generic;
using System.IO;
using UMA.Editors.PackageSupport;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    public partial class WelcomeToUMA
    {
        private const int PluginsPage = 11;
        private Vector2 navigationScroll;
        private readonly List<PluginCard> pluginCards = new();
        private Vector2 pluginsScroll;
        private double nextPluginStatusRefresh;
        private bool pluginStatusDirty = true;
        private bool pluginStatusWasBusy;
        private bool navigationPackageStatusDirty = true;
        private SrpSupport navigationInstalledSrp;
        private bool navigationSrpUpdateAvailable;
        private GUIStyle pluginTitleStyle;
        private GUIStyle pluginRowStyle;
        private bool pluginActionQueued;
        private bool canRemoveAllPlugins;
        private string removeAllPluginsReason;

        private sealed class PluginCard
        {
            public UMAContentKind kind;
            public UMAContentInstallationState state;
            public string version;
            public bool canRemove;
            public string removalReason;
        }

        private void ShowPluginsPage()
        {
            ClearLog();
            currentButton = PluginsPage;
            pluginsScroll = Vector2.zero;
            InvalidatePluginStatus();
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.timeSinceStartup < nextPluginStatusRefresh)
                return;
            nextPluginStatusRefresh = EditorApplication.timeSinceStartup + .25;
            bool busy = IsPluginOperationBusy();
            if (!busy && navigationPackageStatusDirty)
            {
                navigationInstalledSrp = GetInstalledSrpSupport();
                navigationSrpUpdateAvailable = IsInstalledSrpUpdateAvailable();
                navigationPackageStatusDirty = false;
                Repaint();
            }
            if (currentButton != PluginsPage) return;
            if (busy != pluginStatusWasBusy)
            {
                pluginStatusWasBusy = busy;
                pluginStatusDirty = true;
                Repaint();
            }
            // Manifest validation and recursive ownership checks are disk work, not
            // an idle UI poll. Refresh only after changes, once imports have settled.
            if (!busy && pluginStatusDirty)
            {
                RefreshPluginStatus();
                Repaint();
            }
            else if (UMAPluginPackageDownload.IsActive) Repaint();
        }

        private bool IsPluginOperationBusy() => pluginActionQueued ||
            UMAContentPackageInstaller.IsInstallingAllPlugins || UMAPluginPackageDownload.IsActive ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            File.Exists("Library/UMA/ContentInstaller/pending.json") ||
            File.Exists("Library/UMA/PluginRemoval/pending.json");

        private void InvalidatePluginStatus()
        {
            pluginStatusDirty = true;
            navigationPackageStatusDirty = true;
            nextPluginStatusRefresh = EditorApplication.timeSinceStartup + .25;
        }

        private void RefreshPluginStatus()
        {
            pluginCards.Clear();
            foreach (UMAContentKind kind in UMAContentCatalog.PluginDisplayOrder)
            {
                string reason;
                bool canRemove = UMAContentCatalog.ParentPlugin(kind).HasValue
                    ? UMAPluginPackageRemoval.CanRemove(kind, out _, out reason)
                    : UMAPluginPackageRemoval.CanRemoveAll(kind, out reason);
                pluginCards.Add(new PluginCard
                {
                    kind = kind,
                    state = UMAContentPackageInstaller.GetState(kind),
                    version = UMAContentPackageInstaller.GetInstalledVersion(kind),
                    canRemove = canRemove,
                    removalReason = reason
                });
            }
            canRemoveAllPlugins = UMAPluginPackageRemoval.CanRemoveAll(out removeAllPluginsReason);
            pluginStatusDirty = false;
        }

        private void DrawPluginsPage()
        {
            pluginTitleStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            pluginRowStyle ??= new GUIStyle(EditorStyles.label) { wordWrap = false, clipping = TextClipping.Clip };

            bool busy = IsPluginOperationBusy() || pluginCards.Count == 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Plugins", EditorStyles.largeLabel, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(busy || !pluginCards.Exists(card => card.state != UMAContentInstallationState.Installed)))
                    if (GUILayout.Button(new GUIContent("Install all plugins", "Install all missing or unverified plugins, including their Examples and Tests packages."), GUILayout.ExpandWidth(false)))
                        QueueAllPluginActions(false);
                using (new EditorGUI.DisabledScope(busy || !canRemoveAllPlugins))
                    if (GUILayout.Button(new GUIContent("Remove all plugins", canRemoveAllPlugins
                            ? "Remove all installed plugins and their Examples and Tests packages."
                            : removeAllPluginsReason), GUILayout.ExpandWidth(false)))
                        QueueAllPluginActions(true);
            }
            GUILayout.Label("Packages are located locally first, then downloaded from the matching UMA GitHub release if needed. Use ? for details.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Space(8);

            if (busy)
                EditorGUILayout.HelpBox(UMAPluginPackageDownload.IsActive ? "Downloading the plugin package. You can cancel in the progress dialog."
                    : "Waiting for the current package operation or Unity import to finish.", MessageType.Info);

            var failure = UMAPluginPackageDownload.LastFailure;
            if (failure != null)
            {
                EditorGUILayout.HelpBox(UMAContentCatalog.DisplayName(failure.Kind) + ": " + failure.Message +
                    "\nDownload manually from: " + failure.DownloadPage +
                    (failure.Location == null ? "" : "\nPackage: " + failure.Location.FileName), MessageType.Warning);
                using (new EditorGUI.DisabledScope(busy))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Retry")) QueuePluginAction(failure.Kind, false);
                    if (GUILayout.Button("Open Download Page")) Application.OpenURL(failure.DownloadPage);
                    if (GUILayout.Button("Locate Local Package")) QueuePluginAction(failure.Kind, false, true);
                    if (GUILayout.Button("Dismiss")) UMAPluginPackageDownload.DismissFailure();
                }
            }

            pluginsScroll = EditorGUILayout.BeginScrollView(pluginsScroll);
            float availableWidth = Mathf.Max(520, ContentRect.width - 48);
            float textWidth = (availableWidth - 212) * .5f;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Plugin", EditorStyles.boldLabel, GUILayout.Width(textWidth));
            GUILayout.Label("Install status", EditorStyles.boldLabel, GUILayout.Width(textWidth));
            GUILayout.Label("Actions", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            foreach (PluginCard card in pluginCards)
                DrawPluginRow(card, textWidth, busy);
            EditorGUILayout.EndScrollView();
        }

        private void DrawPluginRow(PluginCard card, float textWidth, bool busy)
        {
            float height = EditorGUIUtility.singleLineHeight + 6;
            Rect row = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            GUI.Box(row, GUIContent.none, EditorStyles.helpBox);
            bool companion = UMAContentCatalog.ParentPlugin(card.kind).HasValue;
            float indent = companion ? 20 : 0;
            Rect nameRect = new Rect(row.x + 5 + indent, row.y + 3, textWidth - indent, height - 6);
            string name = companion ? (UMAContentCatalog.IsTests(card.kind) ? "Tests" : "Examples") : UMAContentCatalog.DisplayName(card.kind);
            GUI.Label(nameRect, new GUIContent(name, UMAContentCatalog.DisplayName(card.kind)), companion ? pluginRowStyle : pluginTitleStyle);
            string status = card.state switch
            {
                UMAContentInstallationState.Missing => "Not installed",
                UMAContentInstallationState.Installed => "Installed",
                UMAContentInstallationState.Installing => "Installing...",
                _ => "Present - needs verification"
            };
            if (UMAPluginPackageDownload.ActiveKind == card.kind) status = "Downloading...";
            if (!string.IsNullOrEmpty(card.version)) status += " · " + card.version;
            string detail = card.state == UMAContentInstallationState.Unmanaged
                ? "Files are present, but the manifest, version, or dependencies could not be verified. Reinstall to repair or adopt them."
                : status;
            if (!card.canRemove) detail += "\n" + card.removalReason;
            Rect cell = new Rect(row.x + textWidth + 9, row.y + 3, textWidth, height - 6);
            GUI.Label(cell, new GUIContent(status, detail), pluginRowStyle);
            cell.x += textWidth + 4; cell.width = 24;
            if (GUI.Button(cell, new GUIContent("?", "About this package")))
            {
                EditorUtility.DisplayDialog(UMAContentCatalog.DisplayName(card.kind),
                    UMAContentCatalog.Description(card.kind) + "\n\nRequires: " +
                    string.Join(", ", UMAContentCatalog.Dependencies(card.kind)) +
                    "\n\nInstall location: " + UMAContentCatalog.Root(card.kind), "OK");
            }
            using (new EditorGUI.DisabledScope(busy))
            {
                string label = card.state == UMAContentInstallationState.Missing ? "Install" : "Reinstall";
                cell.x += cell.width + 4; cell.width = 76;
                if (GUI.Button(cell, new GUIContent(label, "Use a compatible local package, or download it from this UMA version's GitHub release.")))
                    QueuePluginAction(card.kind, false);
            }
            using (new EditorGUI.DisabledScope(busy || !card.canRemove))
            {
                cell.x += cell.width + 4; cell.width = 80;
                if (GUI.Button(cell, new GUIContent(companion ? "Remove" : "Remove All", card.canRemove
                    ? (companion ? "Remove unchanged package files; preserve modified files and user data."
                        : "Remove installed Examples and Tests first, then this plugin; preserve modified files and user data.")
                    : card.removalReason)))
                    QueuePluginAction(card.kind, true);
            }
        }

        private void QueuePluginAction(UMAContentKind kind, bool remove, bool locate = false)
        {
            QueuePluginAction(() =>
            {
                if (remove)
                {
                    if (UMAContentCatalog.ParentPlugin(kind).HasValue) UMAPluginPackageRemoval.RemoveInteractive(kind);
                    else UMAPluginPackageRemoval.RemoveAllInteractive(kind);
                }
                else if (locate) UMAContentPackageInstaller.LocatePackage(kind);
                else UMAContentPackageInstaller.InstallFromFile(kind);
            });
        }

        private void QueueAllPluginActions(bool remove)
        {
            QueuePluginAction(() =>
            {
                if (remove) UMAPluginPackageRemoval.RemoveAllInteractive();
                else UMAContentPackageInstaller.InstallAllPlugins();
            });
        }

        private void QueuePluginAction(Action action)
        {
            // Imports and removals can reload assemblies. Run outside the active GUI layout.
            pluginActionQueued = true;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                }
                finally
                {
                    if (this != null)
                    {
                        pluginActionQueued = false;
                        InvalidatePluginStatus();
                        Repaint();
                    }
                }
            };
        }
    }
}
