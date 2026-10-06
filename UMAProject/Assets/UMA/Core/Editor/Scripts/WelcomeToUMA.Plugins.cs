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
        private GUIStyle pluginTitleStyle;
        private bool pluginActionQueued;

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
            RefreshPluginStatus();
        }

        private void OnInspectorUpdate()
        {
            if (currentButton != PluginsPage || EditorApplication.timeSinceStartup < nextPluginStatusRefresh)
                return;
            RefreshPluginStatus();
            Repaint();
        }

        private void RefreshPluginStatus()
        {
            pluginCards.Clear();
            foreach (UMAContentKind kind in UMAContentCatalog.Plugins)
            {
                bool canRemove = UMAPluginPackageRemoval.CanRemove(kind, out _, out string reason);
                pluginCards.Add(new PluginCard
                {
                    kind = kind,
                    state = UMAContentPackageInstaller.GetState(kind),
                    version = UMAContentPackageInstaller.GetInstalledVersion(kind),
                    canRemove = canRemove,
                    removalReason = reason
                });
            }
            nextPluginStatusRefresh = EditorApplication.timeSinceStartup + 1;
        }

        private void DrawPluginsPage()
        {
            if (pluginCards.Count == 0) RefreshPluginStatus();
            pluginTitleStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true,
                clipping = TextClipping.Clip
            };

            GUILayout.Label("Plugins", EditorStyles.largeLabel);
            GUILayout.Label("Packages are located automatically. If none is found, choose a .unitypackage file. Use ? for details.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Space(8);

            bool busy = pluginActionQueued || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                File.Exists("Library/UMA/ContentInstaller/pending.json") ||
                File.Exists("Library/UMA/PluginRemoval/pending.json");
            if (busy)
                EditorGUILayout.HelpBox("Waiting for the current package operation or Unity import to finish.", MessageType.Info);

            pluginsScroll = EditorGUILayout.BeginScrollView(pluginsScroll);
            float availableWidth = Mathf.Max(520, ContentRect.width - 48);
            float textWidth = (availableWidth - 194) * .5f;
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
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.MinHeight(58));
            GUILayout.Label(UMAContentCatalog.DisplayName(card.kind), pluginTitleStyle,
                GUILayout.Width(textWidth), GUILayout.Height(42));
            string status = card.state switch
            {
                UMAContentInstallationState.Missing => "Not installed",
                UMAContentInstallationState.Installed => "Installed",
                UMAContentInstallationState.Installing => "Installing...",
                _ => "Present - needs verification"
            };
            if (!string.IsNullOrEmpty(card.version)) status += "\n" + card.version;
            string detail = card.state == UMAContentInstallationState.Unmanaged
                ? "Files are present, but the manifest, version, or dependencies could not be verified. Reinstall to repair or adopt them."
                : status;
            if (!card.canRemove) detail += "\n" + card.removalReason;
            GUILayout.Label(new GUIContent(status, detail), EditorStyles.wordWrappedLabel,
                GUILayout.Width(textWidth), GUILayout.Height(42));
            if (GUILayout.Button(new GUIContent("?", "About this plugin"), GUILayout.Width(24), GUILayout.Height(26)))
            {
                EditorUtility.DisplayDialog(UMAContentCatalog.DisplayName(card.kind),
                    UMAContentCatalog.Description(card.kind) + "\n\nRequires: " +
                    string.Join(", ", UMAContentCatalog.Dependencies(card.kind)) +
                    "\n\nInstall location: " + UMAContentCatalog.Root(card.kind), "OK");
            }
            using (new EditorGUI.DisabledScope(busy))
            {
                string label = card.state == UMAContentInstallationState.Missing ? "Install" : "Reinstall";
                if (GUILayout.Button(new GUIContent(label, "Find a matching package automatically, or browse for one."), GUILayout.Width(76), GUILayout.Height(26)))
                    QueuePluginAction(card.kind, false);
            }
            using (new EditorGUI.DisabledScope(busy || !card.canRemove))
            {
                if (GUILayout.Button(new GUIContent("Remove", card.canRemove
                    ? "Remove unchanged package files; preserve modified files and user data."
                    : card.removalReason), GUILayout.Width(62), GUILayout.Height(26)))
                    QueuePluginAction(card.kind, true);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void QueuePluginAction(UMAContentKind kind, bool remove)
        {
            // Imports and removals can reload assemblies. Run outside the active GUI layout.
            pluginActionQueued = true;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (remove) UMAPluginPackageRemoval.RemoveInteractive(kind);
                    else UMAContentPackageInstaller.InstallFromFile(kind);
                }
                finally
                {
                    if (this != null)
                    {
                        pluginActionQueued = false;
                        RefreshPluginStatus();
                        Repaint();
                    }
                }
            };
        }
    }
}
