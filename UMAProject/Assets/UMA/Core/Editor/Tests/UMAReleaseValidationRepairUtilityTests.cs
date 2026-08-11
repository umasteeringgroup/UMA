#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    [TestFixture]
    [Category("UMA")]
    public sealed class UMAReleaseValidationRepairUtilityTests
    {
        private const string Folder = "Assets/UMAReleaseValidationRepairTests";
        private const string CandidatePath = Folder + "/CandidateTexture.asset";
        private const string LegacyTexturePath = Folder + "/LegacyTexture.asset";
        private const string ShaderPath = Folder + "/MaterialCleanupTest.shader";
        private const string MaterialPath = Folder + "/MaterialCleanupTest.mat";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "UMAReleaseValidationRepairTests");
            var texture = new Texture2D(2, 2) { name = "CandidateTexture" };
            AssetDatabase.CreateAsset(texture, CandidatePath);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void AutoPlan_WithOnlyUma2Referrers_TargetsUma2Category()
        {
            UMAReleaseValidationReport report = CreateReport(
                "Assets/UMA2/Races/HumanMale/RaceData/Test.asset");

            var plans = UMAReleaseValidationRepairUtility.BuildAutoMovePlan(report);

            Assert.That(plans, Has.Count.EqualTo(1));
            Assert.That(plans[0].destinationScope,
                Is.EqualTo(UMAReleaseDestinationScope.UMA2));
            Assert.That(plans[0].destinationFolder, Is.EqualTo("Assets/UMA2/Textures"));
        }

        [Test]
        public void AutoPlan_WithUma2AndUma3Referrers_IsAmbiguous()
        {
            UMAReleaseValidationReport report = CreateReport(
                "Assets/UMA2/Races/HumanMale/RaceData/Test.asset",
                UMAPathUtility.ResolveInstallAssetPath("UMA3/Races/Test.asset"));

            var plans = UMAReleaseValidationRepairUtility.BuildAutoMovePlan(report);

            Assert.That(plans, Is.Empty);
        }

        [Test]
        public void AutoPlan_WithUnknownReferrer_IsConservativelySkipped()
        {
            UMAReleaseValidationReport report = CreateReport(
                "Assets/SomeOtherPackage/Test.asset");

            var plans = UMAReleaseValidationRepairUtility.BuildAutoMovePlan(report);

            Assert.That(plans, Is.Empty);
        }

        [Test]
        public void UniversalDestination_UsesUniversalCategoryFolder()
        {
            string destination = UMAReleaseValidationRepairUtility.GetDestinationFolder(
                CandidatePath, UMAReleaseDestinationScope.Universal);

            string expectedRoot = UMAPathUtility.IsPackageInstallation
                ? UMAPathUtility.ProjectDataRoot + "/Universal"
                : UMAPathUtility.ResolveInstallAssetPath("Universal");
            Assert.That(destination, Is.EqualTo(expectedRoot + "/Textures"));
        }

        [Test]
        public void MaterialCleanup_RemovesOnlyPropertiesNotDeclaredByCurrentShader()
        {
            Material material = CreateMaterialWithStaleProperties();
            var issue = new UMAReleaseValidationIssueReport
            {
                ownerAssetPath = MaterialPath,
                ownerAssetType = nameof(Material),
                kind = "Missing object reference"
            };

            Assert.That(UMAReleaseValidationRepairUtility.TryBuildMaterialCleanupPlan(
                MaterialPath, out UMAReleaseMaterialCleanupPlan plan), Is.True);
            Assert.That(plan.PropertyCount, Is.EqualTo(4));
            Assert.That(UMAReleaseValidationRepairUtility.CanRemoveNonApplicableShaderProperties(
                issue), Is.True);

            UMAReleaseRepairResult result =
                UMAReleaseValidationRepairUtility.RemoveNonApplicableShaderProperties(issue);

            Assert.That(result.succeeded, Is.True, result.message);
            Assert.That(result.updatedReferenceCount, Is.EqualTo(4));
            AssertSavedPropertyNames(material, "m_SavedProperties.m_TexEnvs",
                "_CurrentTex", "_LegacyTex");
            AssertSavedPropertyNames(material, "m_SavedProperties.m_Floats",
                "_CurrentFloat", "_LegacyFloat");
            AssertSavedPropertyNames(material, "m_SavedProperties.m_Colors",
                "_CurrentColor", "_LegacyColor");
            AssertSavedPropertyNames(material, "m_SavedProperties.m_Ints",
                "_CurrentInt", "_LegacyInt");
            Assert.That(material.GetTexture("_CurrentTex"), Is.Not.Null);
            Assert.That(material.GetFloat("_CurrentFloat"), Is.EqualTo(0.75f));
            Assert.That(material.GetColor("_CurrentColor"), Is.EqualTo(Color.cyan));
            Assert.That(material.GetInteger("_CurrentInt"), Is.EqualTo(7));
        }

        [Test]
        public void AutoMaterialCleanupPlan_CleansAffectedMaterialWithoutMovingAssets()
        {
            Material material = CreateMaterialWithStaleProperties();
            var report = new UMAReleaseValidationReport();
            report.issues.Add(new UMAReleaseValidationIssueReport
            {
                ownerAssetPath = MaterialPath,
                ownerAssetType = nameof(Material),
                kind = "Missing object reference"
            });

            List<UMAReleaseMaterialCleanupPlan> plans =
                UMAReleaseValidationRepairUtility.BuildAutoMaterialCleanupPlan(report);
            UMAReleaseRepairResult result =
                UMAReleaseValidationRepairUtility.ExecuteAutoRepair(plans,
                    Array.Empty<UMAReleaseAutoMovePlan>());

            Assert.That(plans, Has.Count.EqualTo(1));
            Assert.That(result.succeeded, Is.True, result.message);
            Assert.That(result.updatedReferenceCount, Is.EqualTo(4));
            AssertSavedPropertyNames(material, "m_SavedProperties.m_TexEnvs",
                "_CurrentTex", "_LegacyTex");
        }

        [Test]
        public void BulkMaterialCleanup_ProcessesEachMaterialOnlyOnce()
        {
            Material material = CreateMaterialWithStaleProperties();
            var report = new UMAReleaseValidationReport();
            report.issues.Add(new UMAReleaseValidationIssueReport
            {
                ownerAssetPath = MaterialPath,
                ownerAssetType = nameof(Material),
                kind = "Missing object reference"
            });
            report.issues.Add(new UMAReleaseValidationIssueReport
            {
                ownerAssetPath = MaterialPath,
                ownerAssetType = nameof(Material),
                kind = "Missing GUID reference"
            });

            List<UMAReleaseMaterialCleanupPlan> plans =
                UMAReleaseValidationRepairUtility.BuildAutoMaterialCleanupPlan(report);
            UMAReleaseRepairResult result =
                UMAReleaseValidationRepairUtility.ExecuteMaterialCleanupPlan(plans);

            Assert.That(plans, Has.Count.EqualTo(1));
            Assert.That(result.succeeded, Is.True, result.message);
            Assert.That(result.updatedReferenceCount, Is.EqualTo(4));
            AssertSavedPropertyNames(material, "m_SavedProperties.m_TexEnvs",
                "_CurrentTex", "_LegacyTex");
        }

        private static UMAReleaseValidationReport CreateReport(params string[] referrers)
        {
            var report = new UMAReleaseValidationReport();
            report.issues.Add(new UMAReleaseValidationIssueReport
            {
                scope = "UMA2",
                kind = "Out-of-package dependency",
                ownerAssetPath = referrers[0],
                referencedAssetPath = CandidatePath,
                referencedAssetGuid = AssetDatabase.AssetPathToGUID(CandidatePath)
            });
            for (int i = 0; i < referrers.Length; i++)
                report.references.Add(new UMAReleaseValidationReferenceReport
                {
                    scope = referrers[i].StartsWith("Assets/UMA2") ? "UMA2" : "UMA3",
                    sourceAssetPath = referrers[i],
                    referencedAssetPath = CandidatePath,
                    referencedAssetGuid = AssetDatabase.AssetPathToGUID(CandidatePath),
                    referenceKind = "Serialized object",
                    status = "Outside allowed folders"
                });
            return report;
        }

        private static Material CreateMaterialWithStaleProperties()
        {
            string shaderSource =
                "Shader \"Hidden/UMA/ReleaseValidationCleanupTest\" {\n" +
                "Properties {\n" +
                "_CurrentTex (\"Current Texture\", 2D) = \"white\" {}\n" +
                "_CurrentFloat (\"Current Float\", Float) = 0\n" +
                "_CurrentColor (\"Current Color\", Color) = (1,1,1,1)\n" +
                "_CurrentInt (\"Current Integer\", Integer) = 0\n" +
                "}\nSubShader { Pass {} }\n}";
            File.WriteAllText(ProjectAbsolutePath(ShaderPath), shaderSource);
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.That(shader, Is.Not.Null);

            var legacyTexture = new Texture2D(2, 2) { name = "LegacyTexture" };
            AssetDatabase.CreateAsset(legacyTexture, LegacyTexturePath);
            Texture2D currentTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(CandidatePath);
            var material = new Material(shader) { name = "MaterialCleanupTest" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            material.SetTexture("_CurrentTex", currentTexture);
            material.SetFloat("_CurrentFloat", 0.75f);
            material.SetColor("_CurrentColor", Color.cyan);
            material.SetInteger("_CurrentInt", 7);

            using (var serialized = new SerializedObject(material))
            {
                serialized.Update();
                AddSavedProperty(serialized, "m_SavedProperties.m_TexEnvs", "_LegacyTex",
                    second => second.FindPropertyRelative("m_Texture").objectReferenceValue =
                        legacyTexture);
                AddSavedProperty(serialized, "m_SavedProperties.m_Floats", "_LegacyFloat",
                    second => second.floatValue = 2f);
                AddSavedProperty(serialized, "m_SavedProperties.m_Colors", "_LegacyColor",
                    second => second.colorValue = Color.magenta);
                AddSavedProperty(serialized, "m_SavedProperties.m_Ints", "_LegacyInt",
                    second => second.intValue = 12);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void AddSavedProperty(SerializedObject serialized, string path,
            string propertyName, Action<SerializedProperty> assignValue)
        {
            SerializedProperty properties = serialized.FindProperty(path);
            Assert.That(properties, Is.Not.Null, path);
            int index = properties.arraySize;
            properties.InsertArrayElementAtIndex(index);
            SerializedProperty entry = properties.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("first").stringValue = propertyName;
            assignValue(entry.FindPropertyRelative("second"));
        }

        private static void AssertSavedPropertyNames(Material material, string path,
            string expectedName, string removedName)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            using var serialized = new SerializedObject(material);
            serialized.Update();
            SerializedProperty properties = serialized.FindProperty(path);
            Assert.That(properties, Is.Not.Null, path);
            for (int i = 0; i < properties.arraySize; i++)
                names.Add(properties.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("first").stringValue);
            Assert.That(names, Does.Contain(expectedName));
            Assert.That(names, Does.Not.Contain(removedName));
        }

        private static string ProjectAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot ?? Directory.GetCurrentDirectory(),
                assetPath));
        }
    }
}

#endif
