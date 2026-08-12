using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace UMA.Editors.PackageSupport
{
    public enum UMAPackageDependencyKind
    {
        Required,
        OptionalFeature,
        Development
    }

    public sealed class UMAPackageDependency
    {
        public string PackageName { get; }
        public string DisplayName { get; }
        public UMAPackageDependencyKind Kind { get; }
        public string Purpose { get; }
        public string InstallIdentifier { get; }

        public UMAPackageDependency(
            string packageName,
            string displayName,
            UMAPackageDependencyKind kind,
            string purpose,
            string installIdentifier = null)
        {
            PackageName = packageName;
            DisplayName = displayName;
            Kind = kind;
            Purpose = purpose;
            InstallIdentifier = string.IsNullOrEmpty(installIdentifier)
                ? packageName
                : installIdentifier;
        }
    }

    public static class UMAPackageDependencyCatalog
    {
        private static readonly UMAPackageDependency[] dependencies =
        {
            new UMAPackageDependency("com.unity.burst", "Burst",
                UMAPackageDependencyKind.Required,
                "High-performance mesh generation, skinning, and recalculation jobs."),
            new UMAPackageDependency("com.unity.collections", "Collections",
                UMAPackageDependencyKind.Required,
                "Native containers used by UMA mesh and slot processing."),
            new UMAPackageDependency("com.unity.jobs", "Jobs",
                UMAPackageDependencyKind.Required,
                "Job scheduling used by UMA mesh processing.",
                "com.unity.jobs@0.70.0-preview.7"),
            new UMAPackageDependency("com.unity.mathematics", "Mathematics",
                UMAPackageDependencyKind.Required,
                "Math types used by Burst and jobified UMA code."),
            new UMAPackageDependency("com.unity.inputsystem", "Input System",
                UMAPackageDependencyKind.Required,
                "Input actions and supplied UMA character/sample controllers."),
            new UMAPackageDependency("com.unity.timeline", "Timeline",
                UMAPackageDependencyKind.Required,
                "UMA race, wardrobe, color, and DNA Timeline tracks."),
            new UMAPackageDependency("com.unity.ugui", "Unity UI",
                UMAPackageDependencyKind.Required,
                "UMA runtime UI components, inspectors, and supplied samples."),
            new UMAPackageDependency("com.unity.test-framework", "Test Framework",
                UMAPackageDependencyKind.OptionalFeature,
                "Launching EditMode tests and running Asset Validation from its review window."),

            new UMAPackageDependency("com.unity.2d.sprite", "2D Sprite",
                UMAPackageDependencyKind.OptionalFeature,
                "Sprite-sheet slicing and legacy Unity sprite-rectangle import in Overlay Painter."),
            new UMAPackageDependency("com.unity.render-pipelines.universal", "Universal Render Pipeline",
                UMAPackageDependencyKind.OptionalFeature,
                "UMA's supplied URP shaders, materials, and sample content."),
            new UMAPackageDependency("com.unity.render-pipelines.high-definition", "High Definition Render Pipeline",
                UMAPackageDependencyKind.OptionalFeature,
                "UMA's supplied HDRP shaders, materials, and sample content."),

            new UMAPackageDependency("com.unity.addressables", "Addressables",
                UMAPackageDependencyKind.OptionalFeature,
                "Optional addressable recipe and asset delivery. Enable UMA_ADDRESSABLES after installation."),
            new UMAPackageDependency("com.unity.formats.fbx", "FBX Exporter",
                UMAPackageDependencyKind.OptionalFeature,
                "Optional FBX export. Enable UMA_FBX_EXPORT after installation."),
            new UMAPackageDependency("com.unity.test-framework.performance", "Performance Testing",
                UMAPackageDependencyKind.Development,
                "UMA performance and release-development measurements; not needed by consumers.")
        };

        public static IReadOnlyList<UMAPackageDependency> All => dependencies;

        public static UMAPackageDependency Find(string packageName)
        {
            return dependencies.FirstOrDefault(item =>
                string.Equals(item.PackageName, packageName, StringComparison.Ordinal));
        }
    }

    public static class UMAPackageDependencyStatus
    {
        private static Dictionary<string, PackageManagerInfo> installed;

        public static bool IsInstalled(string packageName)
        {
            RefreshIfNeeded();
            return installed.ContainsKey(packageName);
        }

        public static string InstalledVersion(string packageName)
        {
            RefreshIfNeeded();
            return installed.TryGetValue(packageName, out PackageManagerInfo info)
                ? info.version
                : string.Empty;
        }

        public static void Invalidate()
        {
            installed = null;
        }

        private static void RefreshIfNeeded()
        {
            if (installed != null)
                return;

            installed = new Dictionary<string, PackageManagerInfo>(StringComparer.Ordinal);
            PackageManagerInfo[] packages = PackageManagerInfo.GetAllRegisteredPackages();
            if (packages == null)
                return;
            for (int i = 0; i < packages.Length; i++)
            {
                PackageManagerInfo info = packages[i];
                if (info != null && !string.IsNullOrEmpty(info.name))
                    installed[info.name] = info;
            }
        }
    }

    public sealed class UMAPackageDependencyWindow : EditorWindow
    {
        private const string WindowTitle = "UMA Package Dependencies";
        private static AddRequest addRequest;
        private static AddAndRemoveRequest bulkRequest;
        private static string[] installingPackages = Array.Empty<string>();

        private static bool IsInstalling => addRequest != null || bulkRequest != null;

        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private string selectedPackageName;
        [SerializeField] private bool showDevelopment;

        [MenuItem("UMA/Package Dependencies...", priority = 1025)]
        public static void Open()
        {
            UMAPackageDependencyWindow window = GetWindow<UMAPackageDependencyWindow>(WindowTitle);
            window.minSize = new Vector2(650f, 450f);
            window.Show();
        }

        public static void OpenAndSelect(string packageName)
        {
            Open();
            UMAPackageDependencyWindow window = GetWindow<UMAPackageDependencyWindow>(WindowTitle);
            window.selectedPackageName = packageName;
            window.Repaint();
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += ProjectChanged;
            UMAPackageDependencyStatus.Invalidate();
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= ProjectChanged;
        }

        private void ProjectChanged()
        {
            UMAPackageDependencyStatus.Invalidate();
            Repaint();
        }

        private void OnGUI()
        {
            PollInstallation();

            EditorGUILayout.LabelField("UMA Package Dependencies", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Required packages are direct compile-time dependencies of the current UMA source distribution. " +
                "Optional packages enable isolated integrations; UMA continues compiling when they are absent. " +
                "Installation changes the current project's Packages/manifest.json and always requires confirmation.",
                MessageType.Info);

            DrawMissingRequiredSummary();
            showDevelopment = EditorGUILayout.ToggleLeft(
                "Show development-only packages", showDevelopment);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawSection("Required", UMAPackageDependencyKind.Required);
            DrawSection("Optional Features", UMAPackageDependencyKind.OptionalFeature);
            if (showDevelopment)
                DrawSection("Development Only", UMAPackageDependencyKind.Development);
            EditorGUILayout.EndScrollView();
        }

        private void DrawMissingRequiredSummary()
        {
            List<UMAPackageDependency> missing = UMAPackageDependencyCatalog.All
                .Where(item => item.Kind == UMAPackageDependencyKind.Required &&
                    !UMAPackageDependencyStatus.IsInstalled(item.PackageName))
                .ToList();
            if (missing.Count == 0)
            {
                EditorGUILayout.HelpBox("All required UMA packages are installed.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "Missing required packages: " + string.Join(", ", missing.Select(item => item.DisplayName)),
                MessageType.Error);
            using (new EditorGUI.DisabledScope(IsInstalling))
            {
                if (GUILayout.Button("Install All Missing Required Packages"))
                {
                    string names = string.Join("\n", missing.Select(item => "• " + item.DisplayName));
                    if (EditorUtility.DisplayDialog("Install UMA Requirements?",
                            "Unity Package Manager will add these packages to this project:\n\n" + names,
                            "Install", "Cancel"))
                        BeginInstall(missing.Select(item => item.PackageName).ToArray());
                }
            }
        }

        private void DrawSection(string title, UMAPackageDependencyKind kind)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            foreach (UMAPackageDependency dependency in UMAPackageDependencyCatalog.All)
            {
                if (dependency.Kind != kind)
                    continue;
                DrawDependency(dependency);
            }
        }

        private void DrawDependency(UMAPackageDependency dependency)
        {
            bool installed = UMAPackageDependencyStatus.IsInstalled(dependency.PackageName);
            bool selected = string.Equals(selectedPackageName, dependency.PackageName,
                StringComparison.Ordinal);
            GUIStyle style = selected ? new GUIStyle(EditorStyles.helpBox)
            {
                normal = { background = Texture2D.grayTexture }
            } : EditorStyles.helpBox;

            using (new EditorGUILayout.VerticalScope(style))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(dependency.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (installed)
                    {
                        EditorGUILayout.LabelField(
                            "Installed " + UMAPackageDependencyStatus.InstalledVersion(dependency.PackageName),
                            GUILayout.Width(150f));
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(IsInstalling))
                        {
                            if (GUILayout.Button("Install...", GUILayout.Width(90f)) &&
                                EditorUtility.DisplayDialog("Install " + dependency.DisplayName + "?",
                                    "Unity Package Manager will add '" + dependency.PackageName +
                                    "' to this project.", "Install", "Cancel"))
                                BeginInstall(new[] { dependency.PackageName });
                        }
                    }
                }

                EditorGUILayout.LabelField(dependency.PackageName, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(dependency.Purpose, EditorStyles.wordWrappedLabel);
                if (IsInstalling && Array.IndexOf(installingPackages, dependency.PackageName) >= 0)
                    EditorGUILayout.HelpBox("Installing through Unity Package Manager...", MessageType.Info);
            }
        }

        private static void BeginInstall(string[] packageNames)
        {
            if (packageNames == null || packageNames.Length == 0 || IsInstalling)
                return;

            installingPackages = packageNames;
            if (packageNames.Length == 1)
            {
                UMAPackageDependency dependency = UMAPackageDependencyCatalog.Find(packageNames[0]);
                addRequest = Client.Add(dependency?.InstallIdentifier ?? packageNames[0]);
            }
            else
            {
                string[] identifiers = packageNames.Select(packageName =>
                {
                    UMAPackageDependency dependency = UMAPackageDependencyCatalog.Find(packageName);
                    return dependency?.InstallIdentifier ?? packageName;
                }).ToArray();
                bulkRequest = Client.AddAndRemove(identifiers, Array.Empty<string>());
            }
        }

        private void PollInstallation()
        {
            if (bulkRequest != null && bulkRequest.IsCompleted)
            {
                if (bulkRequest.Status == StatusCode.Success)
                    Debug.Log("[UMA] Required package installation completed.");
                else if (bulkRequest.Status >= StatusCode.Failure)
                    Debug.LogError("[UMA] Package installation failed: " + bulkRequest.Error.message);
                bulkRequest = null;
                InstallationFinished();
                return;
            }

            if (addRequest == null || !addRequest.IsCompleted)
                return;

            if (addRequest.Status == StatusCode.Success)
                Debug.Log("[UMA] Installed package " + addRequest.Result.packageId + ".");
            else if (addRequest.Status >= StatusCode.Failure)
                Debug.LogError("[UMA] Package installation failed: " + addRequest.Error.message);
            addRequest = null;
            InstallationFinished();
        }

        private void InstallationFinished()
        {
            installingPackages = Array.Empty<string>();
            UMAPackageDependencyStatus.Invalidate();
            Repaint();
        }
    }
}
