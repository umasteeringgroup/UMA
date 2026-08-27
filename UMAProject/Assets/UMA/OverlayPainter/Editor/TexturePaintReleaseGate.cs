using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint.Editor
{
    [Serializable]
    public sealed class TexturePaintReleaseGateCheck
    {
        public string category;
        public string name;
        public string status;
        public string detail;
    }

    [Serializable]
    public sealed class TexturePaintReleaseGateReport
    {
        public string generatedUtc;
        public string unityVersion;
        public string graphicsDevice;
        public string operatingSystem;
        public int passed;
        public int warnings;
        public int failed;
        public List<TexturePaintReleaseGateCheck> checks = new List<TexturePaintReleaseGateCheck>();
        public bool IsPassing => failed == 0;
    }

    public sealed class TexturePaintReleaseGate : EditorWindow
    {
        private static string Root => UMAPathUtility.ResolveInstallAssetPath("OverlayPainter") + "/";
        private TexturePaintReleaseGateReport report;
        private Vector2 scroll;

        [MenuItem("Window/UMA/Overlay Painter/Release Gate")]
        public static void Open()
        {
            TexturePaintReleaseGate window = GetWindow<TexturePaintReleaseGate>();
            window.titleContent = new GUIContent("Overlay Painter QA");
            window.minSize = new Vector2(620f, 420f);
            window.report = RunPreflight();
            window.Show();
        }

        /// <summary>CI entry point: -executeMethod UMA.TexturePaint.Editor.TexturePaintReleaseGate.RunBatchPreflight.</summary>
        public static void RunBatchPreflight()
        {
            TexturePaintReleaseGateReport result = RunPreflight();
            string output = GetCommandLineValue("-texturePaintGateReport");
            if (string.IsNullOrEmpty(output))
                output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/TexturePaintReleaseGate/preflight.json"));
            string directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Debug.Log($"Overlay Painter release preflight: {result.passed} passed, {result.warnings} warnings, " +
                $"{result.failed} failed. Report: {output}");
            if (!result.IsPassing) EditorApplication.Exit(1);
        }

        public static TexturePaintReleaseGateReport RunPreflight()
        {
            TexturePaintReleaseGateReport result = new TexturePaintReleaseGateReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
                operatingSystem = SystemInfo.operatingSystem
            };
            CheckUnity(result);
            CheckGpu(result);
            CheckAssets(result);
            CheckPipelines(result);
            CheckPluginBoundary(result);
            CheckOptionalIntegrations(result);
            return result;
        }

        private void OnGUI()
        {
            report ??= RunPreflight();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Overlay Painter Release Gate", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Preflight: {report.passed} passed, {report.warnings} warnings, {report.failed} failed. " +
                "Run the QA/Run-TexturePaintReleaseGate.ps1 entry point for isolated EditMode and PlayMode suites.",
                report.failed > 0 ? MessageType.Error : report.warnings > 0 ? MessageType.Warning : MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run Preflight", GUILayout.Width(130f))) report = RunPreflight();
                if (GUILayout.Button("Copy JSON", GUILayout.Width(100f)))
                    EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(report, true);
                if (GUILayout.Button("Select QA Guide", GUILayout.Width(130f)))
                {
                    TextAsset guide = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "QA/RELEASE_GATE.md");
                    if (guide != null) Selection.activeObject = guide;
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(Application.unityVersion, GUILayout.Width(110f));
            }
            EditorGUILayout.Space(5f);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            string previousCategory = null;
            for (int i = 0; i < report.checks.Count; i++)
            {
                TexturePaintReleaseGateCheck check = report.checks[i];
                if (!string.Equals(previousCategory, check.category, StringComparison.Ordinal))
                {
                    EditorGUILayout.Space(6f);
                    EditorGUILayout.LabelField(check.category, EditorStyles.boldLabel);
                    previousCategory = check.category;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIStyle statusStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                    statusStyle.normal.textColor = check.status == "FAIL" ? new Color(1f, 0.35f, 0.3f) :
                        check.status == "WARN" ? new Color(1f, 0.7f, 0.15f) : new Color(0.35f, 0.8f, 0.45f);
                    EditorGUILayout.LabelField(check.status, statusStyle, GUILayout.Width(42f));
                    EditorGUILayout.LabelField(check.name, GUILayout.Width(220f));
                    EditorGUILayout.LabelField(check.detail, EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void CheckUnity(TexturePaintReleaseGateReport report)
        {
            int major = 0, minor = 0;
            string[] parts = Application.unityVersion.Split('.');
            if (parts.Length > 0) int.TryParse(parts[0], out major);
            if (parts.Length > 1) int.TryParse(parts[1], out minor);
            Add(report, "Environment", "Unity 6.3 or newer", major > 6000 || major == 6000 && minor >= 3,
                Application.unityVersion);
            Add(report, "Environment", "Batch-safe project path", !Application.dataPath.Contains("\n") &&
                !Application.dataPath.Contains("\r"), Application.dataPath);
        }

        private static void CheckGpu(TexturePaintReleaseGateReport report)
        {
            Add(report, "GPU", "Compute shaders", SystemInfo.supportsComputeShaders,
                SystemInfo.supportsComputeShaders ? SystemInfo.graphicsDeviceType.ToString() : "CPU fallbacks remain available; GPU goldens cannot run.");
            Add(report, "GPU", "4K texture support", SystemInfo.maxTextureSize >= 4096,
                "Maximum texture size: " + SystemInfo.maxTextureSize);
            CheckFormat(report, RenderTextureFormat.ARGB32);
            CheckFormat(report, RenderTextureFormat.ARGBHalf);
            CheckFormat(report, RenderTextureFormat.ARGBFloat);
            CheckFormat(report, RenderTextureFormat.R8);
            CheckFormat(report, RenderTextureFormat.RFloat);
            CheckFormat(report, RenderTextureFormat.RGFloat);
        }

        private static void CheckFormat(TexturePaintReleaseGateReport report, RenderTextureFormat format)
        {
            bool supported = SystemInfo.SupportsRenderTextureFormat(format);
            Add(report, "GPU", format + " render targets", supported,
                supported ? "Supported" : "Required by color, precision, or sparse coverage tests.");
        }

        private static void CheckAssets(TexturePaintReleaseGateReport report)
        {
            CheckCompute(report, "StrokeRasterize.compute", "CSMain", "CSInPlace", "CSBatchInPlace");
            CheckCompute(report, "Blur.compute", "CSBlur");
            CheckCompute(report, "NormalTouchup.compute", "CSMain");
            CheckCompute(report, "LayerComposite.compute", "CSCopyBase", "CSCompositeLayer",
                "CSPrepareEffectSeeds", "CSJumpFloodEffectSeeds", "CSResolveEffectDistance",
                "CSCompositeLayerEffect");
            CheckCompute(report, "ChannelPack.compute", "CSExtract", "CSPackChannels",
                "CSApplyPluginTile");
            string[] documents =
            {
                "RELEASE_READINESS_PLAN.md", "PLUGIN_API_V2.md", "MILESTONE_8_WORKSPACE.md",
                "MILESTONE_9_RELEASE_GATE.md", "QA/RELEASE_GATE.md", "QA/Run-TexturePaintReleaseGate.ps1",
                "QA/Run-TexturePaintReleaseGate.cmd"
            };
            for (int i = 0; i < documents.Length; i++)
                Add(report, "Release assets", documents[i], File.Exists(Path.GetFullPath(Root + documents[i])),
                    Root + documents[i]);
        }

        private static void CheckCompute(TexturePaintReleaseGateReport report, string filename, params string[] kernels)
        {
            string path = Root + "Shaders/" + filename;
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            bool valid = shader != null;
            string missing = string.Empty;
            if (shader != null)
                for (int i = 0; i < kernels.Length; i++)
                    if (!shader.HasKernel(kernels[i])) { valid = false; missing += (missing.Length == 0 ? "" : ", ") + kernels[i]; }
            Add(report, "Release assets", filename, valid,
                shader == null ? "Asset is missing." : missing.Length == 0 ? string.Join(", ", kernels) : "Missing kernels: " + missing);
        }

        private static void CheckPipelines(TexturePaintReleaseGateReport report)
        {
            RenderPipelineAsset active = GraphicsSettings.currentRenderPipeline;
            string activeType = active != null ? active.GetType().FullName ?? active.GetType().Name : "None";
            bool activeSupported = activeType.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   activeType.IndexOf("HDRender", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   activeType.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0;
            Add(report, "Material pipelines", "Active certified pipeline", activeSupported,
                activeSupported ? activeType : "Select an URP or HDRP RenderPipelineAsset before certification.");
            Add(report, "Material pipelines", "Built-in / Standard", false,
                "Not part of the certified Overlay Painter workflow. URP and HDRP are the supported pipelines.", true);
            CheckOptionalPipeline(report, "Universal Render Pipeline", "Universal Render Pipeline/Lit", "Universal");
            CheckOptionalPipeline(report, "High Definition Render Pipeline", "HDRP/Lit", "HighDefinition");
        }

        private static void CheckOptionalPipeline(TexturePaintReleaseGateReport report, string displayName,
            string shaderName, string namespaceToken)
        {
            bool installed = false;
            foreach (Type type in TypeCache.GetTypesDerivedFrom<RenderPipelineAsset>())
                if ((type.Namespace ?? string.Empty).IndexOf(namespaceToken, StringComparison.OrdinalIgnoreCase) >= 0)
                { installed = true; break; }
            Shader shader = Shader.Find(shaderName);
            if (!installed)
                Add(report, "Material pipelines", displayName, false, "Not installed; matrix case is not applicable.", true);
            else Add(report, "Material pipelines", displayName, shader != null,
                shader != null ? shaderName : "Package is installed but its Lit shader could not be resolved.");
        }

        private static void CheckPluginBoundary(TexturePaintReleaseGateReport report)
        {
            Assembly assembly = typeof(ITexturePaintExtensionV2).Assembly;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { types = exception.Types; }
            List<string> legacy = new List<string>();
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null) continue;
                if (type.Name.EndsWith("V1", StringComparison.Ordinal) || type.Name == "ITexturePaintPlugin" ||
                    type.Name == "TexturePaintPluginContext") legacy.Add(type.FullName);
            }
            Add(report, "Plugin integrity", "API v2-only boundary", legacy.Count == 0,
                legacy.Count == 0 ? "No v1 compatibility surface is present." : string.Join(", ", legacy));
            Add(report, "Plugin integrity", "Immutable read snapshots",
                typeof(TexturePaintReadOnlyImage).GetMethod("CopyPixels") != null &&
                typeof(TexturePaintReadOnlyImage).GetProperty("pixels") == null,
                "Plugins receive copied pixel data and validated command contexts.");
        }

        private static void CheckOptionalIntegrations(TexturePaintReleaseGateReport report)
        {
            string asmdefPath = Root + "Editor/UMA.TexturePaint.Editor.asmdef";
            string fullPath = Path.GetFullPath(asmdefPath);
            string definition = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
            bool isolated = definition.Length > 0 &&
                definition.IndexOf("Unity.Addressables", StringComparison.Ordinal) < 0 &&
                definition.IndexOf("Unity.ResourceManager", StringComparison.Ordinal) < 0;
            Add(report, "Optional integrations", "Addressables-free core assembly", isolated,
                isolated
                    ? "Mark Addressable is compiled only under UMA_ADDRESSABLES; the core editor asmdef has no package references."
                    : "Remove Addressables and ResourceManager references from " + asmdefPath + ".");
        }

        private static void Add(TexturePaintReleaseGateReport report, string category, string name,
            bool passed, string detail, bool informational = false)
        {
            string status = passed ? "PASS" : informational ? "WARN" : "FAIL";
            report.checks.Add(new TexturePaintReleaseGateCheck
            {
                category = category,
                name = name,
                status = status,
                detail = detail
            });
            if (status == "PASS") report.passed++;
            else if (status == "WARN") report.warnings++;
            else report.failed++;
        }

        private static string GetCommandLineValue(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
                if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase)) return arguments[i + 1];
            return null;
        }
    }
}
