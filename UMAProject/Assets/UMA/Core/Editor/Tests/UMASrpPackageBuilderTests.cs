using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UMA.Editors.PackageSupport;

namespace UMA.Tests
{
    public sealed class UMASrpPackageBuilderTests
    {
        private const string Header = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";
        private string file;
        private static readonly MethodInfo Export = typeof(UMASrpPackageBuilder).Assembly
            .GetType("UMA.Editors.PackageSupport.UMASrpPackageSource")
            .GetMethod("ExportYaml", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp() => file = Path.Combine(Path.GetTempPath(), "uma-srp-source-" + Guid.NewGuid().ToString("N") + ".asset");

        [TearDown]
        public void TearDown() { if (File.Exists(file)) File.Delete(file); }

        [TestCase("URP", "da692e001514ec24dbc4cca1949ff7e8")]
        [TestCase("HDRP", "d0353a89b1f911e48b9e16bdc9f2e058")]
        public void ExportRemovesOppositeMetadataAndLocalReferencesButPreservesSourceAndExternalReferences(string pipeline, string script)
        {
            string original = Header + "--- !u!21 &2100000\nMaterial:\n" +
                "  m_Component:\n  - component: {fileID: 99}\n" +
                "  local: {fileID: 99}\n  external: {fileID: 99, guid: 12345678901234567890123456789012, type: 2}\n" +
                "--- !u!114 &99\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: " + script + ", type: 3}\n";
            File.WriteAllText(file, original);
            string result = (string)Export.Invoke(null, new object[] { file, pipeline });
            Assert.That(result, Does.Not.Contain(script));
            Assert.That(result, Does.Not.Contain("component: {fileID: 99}"));
            Assert.That(result, Does.Contain("local: {fileID: 0}"));
            Assert.That(result, Does.Contain("external: {fileID: 99, guid:"));
            Assert.That(File.ReadAllText(file), Is.EqualTo(original));
        }

        [Test]
        public void OppositePipelineOnlyAssetIsOmittedFromExportWithoutDeletingSource()
        {
            string original = Header + "--- !u!114 &11400000\nMonoBehaviour:\n" +
                "  m_Script: {fileID: 11500000, guid: b2686e09ec7aef44bad2843e4416f057, type: 3}\n";
            File.WriteAllText(file, original);
            Assert.That(Export.Invoke(null, new object[] { file, "URP" }), Is.EqualTo(string.Empty));
            Assert.That(File.ReadAllText(file), Is.EqualTo(original));
        }

        [Test]
        public void NativeBinaryAssetIsNeverDecodedOrRewritten()
        {
            byte[] binary = { 0, 255, 128, 0, 17, 254, 99, 0, 37, 89, 65, 77, 76 };
            File.WriteAllBytes(file, binary);
            Assert.That(Export.Invoke(null, new object[] { file, "URP" }), Is.Null);
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(binary));
        }

        [TestCase("%YAML bogus\n")]
        [TestCase("%YAML 1.1\nnot a Unity tag\n")]
        public void YamlLikePrefixDoesNotAuthorizeTextTransformation(string prefix)
        {
            byte[] original = Encoding.UTF8.GetBytes(prefix + "  _HDRPMaterial: {fileID: 123}\n");
            File.WriteAllBytes(file, original);
            Assert.That(Export.Invoke(null, new object[] { file, "URP" }), Is.Null);
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(original));
        }

        [Test]
        public void HdrpKeepsDefaultMaterialWhenNoHdrpReplacementExists()
        {
            string original = Header + "--- !u!114 &11400000\nMonoBehaviour:\n" +
                "  _material: {fileID: 2100000, guid: 12345678901234567890123456789012, type: 2}\n" +
                "  _HDRPMaterial: {fileID: 0}\n";
            File.WriteAllText(file, original);
            Assert.That(Export.Invoke(null, new object[] { file, "HDRP" }), Is.Null);
        }

        [TestCase("Assets/UMA/SRP/UMAURP.unitypackage")]
        [TestCase("Assets/UMA/SRP/Samples/Old/UMAHDRP.UNITYPACKAGE")]
        [TestCase("Assets/UMA/SRP/Textures/Old.unitypackage")]
        [TestCase("Assets/UMA/SRP/ShaderPackages/Old.unitypackage")]
        [TestCase("Assets/UMA/SRP/UMAURPManifest.json")]
        [TestCase("Assets/UMA/SRP/UMAHDRPManifest.json")]
        public void ExistingArchivesAndManifestsAreNeverUsedAsSource(string path)
        {
            var include = typeof(UMASrpPackageBuilder).GetMethod("IncludeFile", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string pipeline in new[] { "URP", "HDRP" })
                Assert.That(include.Invoke(null, new object[] { path, pipeline }), Is.False);
        }

        [TestCase("Assets/UMA/SRP/ShaderGraphs/Materials/UMA3_SkinShader_URP.asset", true, true)]
        [TestCase("Assets/UMA/SRP/ShaderGraphs/Graphs/TempMat/UMA_URP_Diffuse.mat", true, true)]
        [TestCase("Assets/UMA/SRP/ShaderGraphs/Graphs/UMA3_SkinShader_HDRP.shadergraph", false, true)]
        [TestCase("Assets/UMA/SRP/HDRPSetup/UMAHDRPSetup.cs", false, true)]
        [TestCase("Assets/UMA/SRP/DiffusionProfiles/UMAEye.asset", false, true)]
        [TestCase("Assets/UMA/SRP/Settings/UMA_URP_Asset.asset", true, false)]
        [TestCase("Assets/UMA/SRP/Textures/SkinDetail_HDRPStyle.tga", true, true)]
        public void PathRulesLeaveUrpNamedFilesToTheContentCheck(string path, bool urp, bool hdrp)
        {
            // A "URP" name no longer keeps a file out of HDRP: UMAMaterials named after URP carry the HDRP
            // material too, and URP-only shaders and materials are found from their content instead.
            var include = typeof(UMASrpPackageBuilder).GetMethod("IncludeFile", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(include.Invoke(null, new object[] { path, "URP" }), Is.EqualTo(urp), "URP");
            Assert.That(include.Invoke(null, new object[] { path, "HDRP" }), Is.EqualTo(hdrp), "HDRP");
        }

        private const string Root = "Assets/UMA/SRP/";
        private const string UrpTarget = "{ \"m_Type\": \"UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget\" }";
        private const string HdrpTarget = "{ \"m_Type\": \"UnityEditor.Rendering.HighDefinition.ShaderGraph.HDTarget\" }";

        private static Dictionary<string, string> Classify(Dictionary<string, string> files,
            Dictionary<string, string> guids, Dictionary<string, string> outside = null)
        {
            var classifier = typeof(UMASrpPackageBuilder).Assembly.GetType("UMA.Editors.PackageSupport.UMASrpPipelineClassifier", true);
            var byGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in guids) byGuid[pair.Value] = pair.Key;
            if (outside != null) foreach (var pair in outside) byGuid[pair.Key] = pair.Value;
            Func<string, string> readText = path => files.TryGetValue(path, out string text) ? text : null;
            Func<string, string> guidOfPath = path => guids.TryGetValue(path, out string guid) ? guid : null;
            Func<string, string> pathOfGuid = guid => byGuid.TryGetValue(guid, out string path) ? path : null;
            var result = (System.Collections.IDictionary)classifier.GetMethod("Classify", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { files.Keys, readText, guidOfPath, pathOfGuid });
            var named = new Dictionary<string, string>();
            foreach (System.Collections.DictionaryEntry entry in result) named[(string)entry.Key] = entry.Value.ToString();
            return named;
        }

        private static string Guid32(int n) => n.ToString("x32");

        private static string MaterialUsing(string shaderGuid) => Header + "--- !u!21 &2100000\nMaterial:\n" +
            "  m_Shader: {fileID: -6465566751694194690, guid: " + shaderGuid + ", type: 3}\n";

        private static string UmaMaterial(string material, string hdrpMaterial) => Header + "--- !u!114 &11400000\nMonoBehaviour:\n" +
            "  _material: " + (material == null ? "{fileID: 0}" : "{fileID: 2100000, guid: " + material + ", type: 2}") + "\n" +
            "  _HDRPMaterial: " + (hdrpMaterial == null ? "{fileID: 0}" : "{fileID: 2100000, guid: " + hdrpMaterial + ", type: 2}") + "\n";

        [Test]
        public void ShaderGraphsAndShadersAreClassifiedByTargetsAndTags()
        {
            var files = new Dictionary<string, string>
            {
                { Root + "Graphs/Dual.shadergraph", UrpTarget + "\n\n" + HdrpTarget },
                { Root + "Graphs/UrpOnly.shadergraph", UrpTarget },
                { Root + "Graphs/HdrpOnly.shadergraph", HdrpTarget },
                { Root + "Graphs/NoSrpTarget.shadersubgraph", "{ \"m_Type\": \"UnityEditor.ShaderGraph.SubGraphOutputNode\" }" },
                { Root + "Shaders/Skin.shader", "Shader \"X\" {\n    SubShader\n    {\n        Tags { \"RenderPipeline\"=\"UniversalPipeline\" }\n    }\n}" },
                { Root + "Shaders/Mixed.shader", "Shader \"X\" {\n    SubShader\n    {\n        Tags { \"RenderPipeline\"=\"UniversalPipeline\" }\n    }\n    SubShader\n    {\n    }\n}" },
                { Root + "Shaders/Untagged.shader", "Shader \"X\" {\n    SubShader\n    {\n    }\n}" },
            };
            var result = Classify(files, new Dictionary<string, string>());
            Assert.That(result[Root + "Graphs/Dual.shadergraph"], Is.EqualTo("Both"));
            Assert.That(result[Root + "Graphs/UrpOnly.shadergraph"], Is.EqualTo("URP"));
            Assert.That(result[Root + "Graphs/HdrpOnly.shadergraph"], Is.EqualTo("HDRP"));
            Assert.That(result[Root + "Graphs/NoSrpTarget.shadersubgraph"], Is.EqualTo("Both"));
            Assert.That(result[Root + "Shaders/Skin.shader"], Is.EqualTo("URP"));
            Assert.That(result[Root + "Shaders/Mixed.shader"], Is.EqualTo("Both"), "an untagged SubShader runs anywhere");
            Assert.That(result[Root + "Shaders/Untagged.shader"], Is.EqualTo("Both"));
        }

        [Test]
        public void IncludeFilesFollowTheShadersAndGraphsThatUseThem()
        {
            string toonGuid = Guid32(1);
            var guids = new Dictionary<string, string> { { Root + "Toon/Toon.hlsl", toonGuid } };
            var files = new Dictionary<string, string>
            {
                { Root + "Shaders/Skin.shader", "Shader \"X\" {\n    SubShader\n    {\n        Tags { \"RenderPipeline\"=\"UniversalPipeline\" }\n        #include \"SkinLighting.hlsl\"\n    }\n}" },
                // The URP library include sits in a guard, so text alone would misfile it; its users decide.
                { Root + "Toon/Toon.hlsl", "#if defined(UNIVERSAL_PIPELINE_CORE_INCLUDED)\n#include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl\"\n#endif" },
                { Root + "Toon/Toon.shadergraph", UrpTarget + "\n\n" + HdrpTarget + "\n\n{ \"m_FunctionSource\": \"" + toonGuid + "\" }" },
                { Root + "Shaders/SkinLighting.hlsl", "#include \"SkinInput.hlsl\"" },
                { Root + "Shaders/SkinInput.hlsl", "float4 _BaseMap_ST;" },
                { Root + "Shaders/Unused.hlsl", "float unused;" },
                { Root + "Shaders/LoopA.hlsl", "#include \"LoopB.hlsl\"" },
                { Root + "Shaders/LoopB.hlsl", "#include \"LoopA.hlsl\"" },
            };
            var result = Classify(files, guids);
            Assert.That(result[Root + "Shaders/SkinLighting.hlsl"], Is.EqualTo("URP"));
            Assert.That(result[Root + "Shaders/SkinInput.hlsl"], Is.EqualTo("URP"), "used through another include file");
            Assert.That(result[Root + "Toon/Toon.hlsl"], Is.EqualTo("Both"));
            Assert.That(result[Root + "Shaders/Unused.hlsl"], Is.EqualTo("Both"));
            Assert.That(result[Root + "Shaders/LoopA.hlsl"], Is.EqualTo("Both"), "include cycles end without a pipeline");
        }

        [Test]
        public void MaterialsFollowTheirShaderAndTheUmaMaterialFieldThatUsesThem()
        {
            string dual = Guid32(10), urpGraph = Guid32(11), urpLit = Guid32(12);
            string skinUrp = Guid32(20), skinHdrp = Guid32(21), diffuse = Guid32(22), shared = Guid32(23), litMat = Guid32(24), sample = Guid32(25);
            var guids = new Dictionary<string, string>
            {
                { Root + "Graphs/Dual.shadergraph", dual }, { Root + "Graphs/Skin_URP.shadergraph", urpGraph },
                { Root + "Materials/Skin_URP.mat", skinUrp }, { Root + "Materials/SkinForHdrp.mat", skinHdrp },
                { Root + "Materials/TempMat_URP_Diffuse.mat", diffuse }, { Root + "Materials/Toon.mat", shared },
                { Root + "Materials/PackageLit.mat", litMat }, { Root + "Materials/Sample.mat", sample },
            };
            var files = new Dictionary<string, string>
            {
                { Root + "Graphs/Dual.shadergraph", UrpTarget + "\n\n" + HdrpTarget },
                { Root + "Graphs/Skin_URP.shadergraph", UrpTarget },
                { Root + "Materials/Skin_URP.mat", MaterialUsing(urpGraph) },
                { Root + "Materials/SkinForHdrp.mat", MaterialUsing(dual) },
                { Root + "Materials/TempMat_URP_Diffuse.mat", MaterialUsing(dual) },
                { Root + "Materials/Toon.mat", MaterialUsing(dual) },
                { Root + "Materials/PackageLit.mat", MaterialUsing(urpLit) },
                { Root + "Materials/Sample.mat", MaterialUsing(dual) },
                // The skin UMAMaterial: HDRP replaces _material, so each material serves one pipeline.
                { Root + "Materials/Skin_URP.asset", UmaMaterial(skinUrp, skinHdrp) },
                // No HDRP material: HDRP falls back to _material, so it serves both.
                { Root + "Materials/Diffuse.asset", UmaMaterial(diffuse, null) },
                { Root + "Materials/Toon.asset", UmaMaterial(shared, shared) },
                { Root + "Materials/Lit.asset", UmaMaterial(litMat, null) },
            };
            var outside = new Dictionary<string, string> { { urpLit, "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader" } };
            var result = Classify(files, guids, outside);
            Assert.That(result[Root + "Materials/Skin_URP.asset"], Is.EqualTo("Both"), "a URP-named UMAMaterial still ships to HDRP");
            Assert.That(result[Root + "Materials/Skin_URP.mat"], Is.EqualTo("URP"));
            Assert.That(result[Root + "Materials/SkinForHdrp.mat"], Is.EqualTo("HDRP"), "only reached through _HDRPMaterial");
            Assert.That(result[Root + "Materials/TempMat_URP_Diffuse.mat"], Is.EqualTo("Both"), "the HDRP fallback material");
            Assert.That(result[Root + "Materials/Toon.mat"], Is.EqualTo("Both"));
            Assert.That(result[Root + "Materials/PackageLit.mat"], Is.EqualTo("URP"), "a URP package shader");
            Assert.That(result[Root + "Materials/Sample.mat"], Is.EqualTo("Both"), "unreferenced: its shader decides");
        }

        [Test]
        public void BinaryAssetsAreNeverReadAsTextByTheBuilder()
        {
            // The builder's text reader for the classifier must refuse native assets without the YAML header.
            byte[] binary = { 0, 255, 128, 0, 17, 254, 99, 0, 37, 89, 65, 77, 76 };
            File.WriteAllBytes(file, binary);
            var classify = typeof(UMASrpPackageBuilder).GetMethod("ClassifySource", BindingFlags.Static | BindingFlags.NonPublic);
            var result = (System.Collections.IDictionary)classify.Invoke(null, new object[] { new[] { file } });
            Assert.That(result[file].ToString(), Is.EqualTo("Both"));
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(binary));
        }

        [Test]
        public void ClearingAReferenceLeavesOtherReferencesAlone()
        {
            var clear = typeof(UMASrpPackageBuilder).GetMethod("ClearReferences", BindingFlags.Static | BindingFlags.NonPublic);
            string profile = Guid32(77), other = Guid32(78);
            string yaml = Header + "--- !u!114 &1\nMonoBehaviour:\n" +
                "  sharedProfile: {fileID: 11400000, guid: " + profile + ", type: 2}\n" +
                "  other: {fileID: 11400000, guid: " + other + ", type: 2}\n";
            string result = (string)clear.Invoke(null, new object[] { yaml, new[] { profile } });
            Assert.That(result, Does.Contain("  sharedProfile: {fileID: 0}\n"));
            Assert.That(result, Does.Contain("  other: {fileID: 11400000, guid: " + other + ", type: 2}"));
        }

        [TestCase("HDRP", true)]
        [TestCase("URP", false)]
        public void TheUrpSampleProfileIsClearedFromHdrpScenesOnly(string pipeline, bool cleared)
        {
            // The URP sample volume profile lives in Settings/, which the HDRP package must not contain.
            // A URP profile does nothing on HDRP, so the HDRP export drops the reference.
            const string profilePath = "Assets/UMA/SRP/Settings/SampleSceneProfile.asset";
            if (!File.Exists(profilePath + ".meta")) Assert.Ignore("The URP sample profile is not in this project.");
            string profile = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(profilePath + ".meta"), @"(?m)^guid:\s*([0-9a-f]{32})").Groups[1].Value;
            string yaml = Header + "--- !u!114 &1\nMonoBehaviour:\n  sharedProfile: {fileID: 11400000, guid: " + profile + ", type: 2}\n";
            File.WriteAllText(file, yaml);
            var exported = new Dictionary<string, string>(StringComparer.Ordinal);
            typeof(UMASrpPackageBuilder).GetMethod("ClearPipelineOnlyReferences", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { new[] { file }, exported, pipeline });
            Assert.That(exported.ContainsKey(file), Is.EqualTo(cleared));
            if (cleared) Assert.That(exported[file], Does.Contain("sharedProfile: {fileID: 0}"));
            Assert.That(File.ReadAllText(file), Is.EqualTo(yaml), "the source file is never changed");
        }

        [TestCase("HDRP", true)]
        [TestCase("URP", false)]
        public void APipelineVariantReplacesContentAndDropsOverridesOnlyInItsPipeline(string pipeline, bool applies)
        {
            string dir = Path.Combine(Path.GetTempPath(), "uma-srp-variant-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string env = Path.Combine(dir, "Env.prefab").Replace('\\', '/');
                string envHd = Path.Combine(dir, "EnvHD.prefab").Replace('\\', '/');
                string scene = Path.Combine(dir, "Scene.unity").Replace('\\', '/');
                string guid = Guid32(90);
                string envText = Header + "--- !u!1 &100\nGameObject:\n  m_Name: Env\n";
                string variantText = Header + "--- !u!1 &100\nGameObject:\n  m_Name: EnvHD\n";
                string Mod(string property, string value) => "    - target: {fileID: 200, guid: " + guid + ", type: 3}\n" +
                    "      propertyPath: " + property + "\n      value: " + value + "\n      objectReference: {fileID: 0}\n";
                string sceneText = Header + "--- !u!1001 &5\nPrefabInstance:\n  m_Modification:\n    m_Modifications:\n" +
                    Mod("m_Name", "U3Environment") + Mod("m_Intensity", "0.5") + Mod("m_IsActive", "0") +
                    "  m_SourcePrefab: {fileID: 100100000, guid: " + guid + ", type: 3}\n";
                File.WriteAllText(env, envText);
                File.WriteAllText(env + ".meta", "fileFormatVersion: 2\nguid: " + guid + "\n");
                File.WriteAllText(envHd, variantText);
                File.WriteAllText(scene, sceneText);
                var exported = new Dictionary<string, string>(StringComparer.Ordinal);
                var variants = new (string, string, string, string[])[] { (env, "HDRP", envHd, new[] { "m_Intensity" }) };
                typeof(UMASrpPackageBuilder).GetMethod("ApplyPipelineVariants", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { new[] { env, scene }, exported, pipeline, variants });
                if (applies)
                {
                    Assert.That(exported[env], Is.EqualTo(variantText), "the variant's content ships at the original path");
                    Assert.That(exported[scene], Does.Not.Contain("m_Intensity"), "intensity overrides belong to the other pipeline");
                    Assert.That(exported[scene], Does.Contain("propertyPath: m_Name"));
                    Assert.That(exported[scene], Does.Contain("propertyPath: m_IsActive"));
                }
                else Assert.That(exported, Is.Empty);
                Assert.That(File.ReadAllText(env), Is.EqualTo(envText));
                Assert.That(File.ReadAllText(scene), Is.EqualTo(sceneText), "source files are never changed");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void TheHdrpEnvironmentVariantKeepsTheObjectsScenesPointAt()
        {
            // Scenes override the root's name and the sun and volume by file ID; the HDRP variant must keep those IDs,
            // and must not carry URP light data.
            const string urp = "Assets/UMA/SRP/Environment/U3Environment.prefab";
            const string hdrp = "Assets/UMA/SRP/ShaderGraphs/Graphs/Helpers/U3EnvironmentHDRP.prefab";
            if (!File.Exists(urp) || !File.Exists(hdrp)) Assert.Ignore("The SRP environment prefabs are not in this project.");
            string variant = File.ReadAllText(hdrp);
            Assert.That(variant, Does.StartWith("%YAML 1.1"));
            foreach (string id in new[] { "1470305259712533198", "2090661882390062220", "7934725514109054171", "7568463040674375965", "5497704625092216047" })
            {
                Assert.That(File.ReadAllText(urp), Does.Contain("&" + id), "URP prefab " + id);
                Assert.That(variant, Does.Contain("&" + id), "HDRP variant " + id);
            }
            Assert.That(variant, Does.Not.Contain("474bcb49853aa07438625e644c072ee6"), "UniversalAdditionalLightData");
        }

        [TestCase("URP", "HighDefinition", "Universal")]
        [TestCase("HDRP", "Universal", "HighDefinition")]
        public void ShaderGraphExportKeepsSelectedTargetAndPreservesSource(string pipeline, string excluded, string included)
        {
            const string path = "Assets/UMA/SRP/ShaderGraphs/Graphs/UMADiffuse.shadergraph";
            if (!File.Exists(path)) Assert.Ignore("The dual-target authoring graph is not installed.");
            byte[] original = File.ReadAllBytes(path);
            string source = Encoding.UTF8.GetString(original);
            if (!source.Contains("UnityEditor.Rendering." + excluded + ".ShaderGraph."))
                Assert.Ignore("The authoring graph already has only the selected pipeline target.");
            var export = Export.DeclaringType.GetMethod("ExportShaderGraph", BindingFlags.Static | BindingFlags.NonPublic);
            string result = (string)export.Invoke(null, new object[] { path, pipeline });
            Assert.That(result, Does.Not.Contain("UnityEditor.Rendering." + excluded + ".ShaderGraph."));
            Assert.That(result, Does.Contain("UnityEditor.Rendering." + included + ".ShaderGraph."));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        }

        [Test]
        public void MaterialOutsideTheSrpFolderIsReportedWithItsPathAndWarned()
        {
            // A rebuilt manifest only lists files under Assets/UMA/SRP; a UMAMaterial there that uses a
            // material elsewhere in the project must be reported, not found only when the package is built.
            var settings = UMA.UMASettings.GetSettings();
            string outside = settings == null ? null : UnityEditor.AssetDatabase.GetAssetPath(settings);
            if (string.IsNullOrEmpty(outside)) Assert.Ignore("An installed UMASettings asset is needed as an outside asset.");
            string outsideGuid = UnityEditor.AssetDatabase.AssetPathToGUID(outside);
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var builder = typeof(UMASrpPackageBuilder);
            var sourceType = builder.GetNestedType("Source", BindingFlags.NonPublic);
            var manifestType = builder.GetNestedType("Manifest", BindingFlags.NonPublic);
            var source = Activator.CreateInstance(sourceType, true);
            var manifest = Activator.CreateInstance(manifestType, true);
            string yaml = Header + "--- !u!114 &11400000\nMonoBehaviour:\n" +
                "  _material: {fileID: 2100000, guid: " + outsideGuid + ", type: 2}\n" +
                "  _HDRPMaterial: {fileID: 0}\n";
            File.WriteAllText(file, yaml);
            manifestType.GetField("pipeline").SetValue(manifest, "URP");
            manifestType.GetField("ownedPaths").SetValue(manifest, new[] { file });
            sourceType.GetField("manifest").SetValue(source, manifest);
            sourceType.GetField("guids").SetValue(source, new Dictionary<string, string> { { file, Guid.NewGuid().ToString("N") } });
            sourceType.GetField("exportedText").SetValue(source, new Dictionary<string, string> { { file, yaml } });

            var issues = (List<UMASrpPackageBuilder.MaterialReferenceIssue>)builder
                .GetMethod("FindMaterialReferenceIssues", flags).Invoke(null, new[] { source });
            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Path, Is.EqualTo(file));
            Assert.That(issues[0].Reason, Is.EqualTo(outside));

            string warning = (string)builder.GetMethod("MaterialReferenceWarning", flags)
                .Invoke(null, new object[] { "URP", issues.AsReadOnly() });
            Assert.That(warning, Does.Contain("outside Assets/UMA/SRP"));
            Assert.That(warning, Does.Contain(file));
            Assert.That(warning, Does.Contain(outside));
            Assert.That(File.ReadAllText(file), Is.EqualTo(yaml));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExclusionRequiresConfirmationAndNeverChangesSource(bool accept)
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var builder = typeof(UMASrpPackageBuilder);
            var sourceType = builder.GetNestedType("Source", BindingFlags.NonPublic);
            var manifestType = builder.GetNestedType("Manifest", BindingFlags.NonPublic);
            var source = Activator.CreateInstance(sourceType, true);
            var manifest = Activator.CreateInstance(manifestType, true);
            string yaml = Header + "--- !u!114 &11400000\nMonoBehaviour:\n" +
                "  _material: {fileID: 2100000, guid: " + Guid.NewGuid().ToString("N") + ", type: 2}\n" +
                "  _HDRPMaterial: {fileID: 0}\n";
            File.WriteAllText(file, yaml);
            var paths = new[] { file, "Assets/UMA/SRP/Keep.txt", "Assets/UMA/SRP/UMAURPManifest.json" };
            manifestType.GetField("pipeline").SetValue(manifest, "URP");
            manifestType.GetField("ownedPaths").SetValue(manifest, paths);
            manifestType.GetField("requiredPaths").SetValue(manifest, new[] { paths[1] });
            sourceType.GetField("manifest").SetValue(source, manifest);
            sourceType.GetField("json").SetValue(source, "unchanged");
            var guids = new Dictionary<string, string> { { file, Guid.NewGuid().ToString("N") }, { paths[1], Guid.NewGuid().ToString("N") } };
            var exported = new Dictionary<string, string> { { file, yaml } };
            sourceType.GetField("guids").SetValue(source, guids);
            sourceType.GetField("exportedText").SetValue(source, exported);
            int prompts = 0;
            Func<IReadOnlyList<UMASrpPackageBuilder.MaterialReferenceIssue>, bool> confirm = issues =>
            {
                prompts++;
                Assert.That(issues.Count, Is.EqualTo(1));
                Assert.That(issues[0].Path, Is.EqualTo(file));
                Assert.That(issues[0].Reason, Does.StartWith("Missing material GUID:"));
                return accept;
            };
            var resolve = builder.GetMethod("ResolveMaterialReferences", flags);
            if (accept)
            {
                resolve.Invoke(null, new object[] { source, confirm });
                Assert.That((string[])manifestType.GetField("ownedPaths").GetValue(manifest), Is.EqualTo(new[] { paths[1], paths[2] }));
                Assert.That(guids.ContainsKey(file), Is.False);
                Assert.That(exported.ContainsKey(file), Is.False);
                Assert.That((string)sourceType.GetField("json").GetValue(source), Does.Contain("Keep.txt"));
            }
            else
            {
                var exception = Assert.Throws<TargetInvocationException>(() => resolve.Invoke(null, new object[] { source, confirm }));
                Assert.That(exception.InnerException, Is.TypeOf<OperationCanceledException>());
                Assert.That(manifestType.GetField("ownedPaths").GetValue(manifest), Is.SameAs(paths));
                Assert.That(sourceType.GetField("json").GetValue(source), Is.EqualTo("unchanged"));
                Assert.That(guids.ContainsKey(file), Is.True);
                Assert.That(exported.ContainsKey(file), Is.True);
            }
            Assert.That(prompts, Is.EqualTo(1));
            Assert.That(File.ReadAllText(file), Is.EqualTo(yaml));
        }
    }
}
