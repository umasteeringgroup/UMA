#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [Test]
        public void BakeReferenceValidationBlocksOnlyIncompatibleReferencesUsedByTheOutput()
        {
            Material wrongType = new Material(Shader.Find("Standard"));
            OverlayDataAsset validOverlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                var settings = groom.BakeSettings;
                settings.umaMaterial = settings.overlayTemplate = settings.raceData = wrongType;
                settings.createOverlay = settings.createWardrobeRecipe = true;
                var report = new HairValidationReport();
                HairBakePipeline.ValidateBakeReferences(settings, report);
                Assert.That(report.ErrorCount, Is.EqualTo(3));
                Assert.That(report.issues.All(issue => issue.code == HairValidationCode.InvalidBakeReference
                    && issue.fixId == "edit-bake-settings"), Is.True);

                settings.createOverlay = settings.createWardrobeRecipe = false;
                report = new HairValidationReport();
                HairBakePipeline.ValidateBakeReferences(settings, report);
                Assert.That(report.CanBake, Is.True, "Unused UMA assignments must not block mesh-only output.");
                Assert.That(report.WarningCount, Is.EqualTo(3));
                Assert.That(settings.umaMaterial, Is.SameAs(wrongType));

                settings.createOverlay = true;
                settings.overlayTemplate = validOverlay;
                settings.raceData = null;
                report = new HairValidationReport();
                HairBakePipeline.ValidateBakeReferences(settings, report);
                Assert.That(report.CanBake, Is.True, "An existing overlay bypasses the standalone UMA Material setting.");
                Assert.That(report.WarningCount, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(validOverlay); Object.DestroyImmediate(wrongType); }
        }

        [Test]
        public void ValidBakeReferencesAndEmptyOptionalAssignmentsHaveNoTypeWarnings()
        {
            UMAMaterial material = ScriptableObject.CreateInstance<UMAMaterial>();
            OverlayDataAsset overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            RaceData race = ScriptableObject.CreateInstance<RaceData>();
            try
            {
                Assert.That(HairBakePipeline.GetReferenceTypeWarning<UMAMaterial>("UMA Material", null), Is.Null);
                Assert.That(HairBakePipeline.GetReferenceTypeWarning<UMAMaterial>("UMA Material", material), Is.Null);
                Assert.That(HairBakePipeline.GetReferenceTypeWarning<OverlayDataAsset>("Existing Overlay", overlay), Is.Null);
                Assert.That(HairBakePipeline.GetReferenceTypeWarning<RaceData>("Compatible Race", race), Is.Null);
                Assert.That(HairBakePipeline.GetReferenceTypeWarning<UMAMaterial>("UMA Material", overlay),
                    Does.Contain("OverlayDataAsset").And.Contain("requires UMAMaterial"));
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(overlay); Object.DestroyImmediate(race); }
        }

        [Test]
        public void ReleaseAndDryRunReportIncompatibleBakeReferencesBeforeWritingAssets()
        {
            Material wrongType = new Material(Shader.Find("Standard"));
            try
            {
                groom.BakeSettings.umaMaterial = wrongType;
                groom.BakeSettings.createOverlay = true;
                var release = HairBakePipeline.ValidateRelease(groom);
                Assert.That(release.issues.Any(issue => issue.code == HairValidationCode.InvalidBakeReference
                    && issue.severity == HairValidationSeverity.Error), Is.True);
                Assert.That(HairBakePipeline.DryRun(groom).validation.CanBake, Is.False);
                var bake = HairBakePipeline.Bake(groom);
                Assert.That(bake.succeeded, Is.False);
                Assert.That(bake.assets, Is.Empty);
                Assert.That(groom.BakeSettings.umaMaterial, Is.SameAs(wrongType));
            }
            finally { Object.DestroyImmediate(wrongType); }
        }

        [Test]
        public void IncompatibleBakeReferenceIssueNavigatesToOutputProperties()
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.NavigateToIssue(new HairValidationIssue { fixId = "edit-bake-settings" });
                Assert.That(stage.WorkflowStep, Is.EqualTo(HairWorkflowStep.ValidateAndBake));
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Output));
            }
            finally { DestroyEditingStage(stage); }
        }

        [UnityTest]
        public IEnumerator ValidateAndBakeHandlesIncompatibleObjectReferencesWithoutChangingSettings()
        {
            HairCardStage stage = CreateEditingStage();
            HairGroomWorkspace window = ScriptableObject.CreateInstance<HairGroomWorkspace>();
            Material wrongType = new Material(Shader.Find("Standard"));
            FieldInfo activeStage = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previousStage = activeStage.GetValue(null);
            try
            {
                activeStage.SetValue(null, stage);
                groom.BakeSettings.umaMaterial = wrongType;
                groom.BakeSettings.overlayTemplate = wrongType;
                groom.BakeSettings.raceData = wrongType;
                stage.WorkflowStep = HairWorkflowStep.ValidateAndBake;
                string before = EditorJsonUtility.ToJson(groom);
                window.position = new Rect(0f, 0f, 900f, 950f);
                window.Show();
                foreach (float width in new[] { 900f, 380f })
                {
                    window.position = new Rect(0f, 0f, width, 950f);
                    window.Repaint();
                    yield return null;
                    yield return null;
                    LogAssert.NoUnexpectedReceived();
                    Assert.That(groom.BakeSettings.umaMaterial, Is.SameAs(wrongType));
                    Assert.That(groom.BakeSettings.overlayTemplate, Is.SameAs(wrongType));
                    Assert.That(groom.BakeSettings.raceData, Is.SameAs(wrongType));
                    Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before),
                        "Drawing an invalid assignment must not silently clear it or dirty the groom.");
                }
            }
            finally
            {
                window.Close();
                activeStage.SetValue(null, previousStage);
                DestroyEditingStage(stage);
                Object.DestroyImmediate(wrongType);
            }
        }
    }
}
#endif
