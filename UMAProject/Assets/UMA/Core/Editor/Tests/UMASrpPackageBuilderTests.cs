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
