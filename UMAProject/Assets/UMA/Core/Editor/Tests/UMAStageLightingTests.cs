#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The editing stages' three point lighting: it lights only its own preview scene, turns as one
    /// setup, and removes everything it made.
    /// </summary>
    public sealed class UMAStageLightingTests
    {
        private Scene previewScene;
        private UMAStageLighting rig;

        [SetUp]
        public void SetUp()
        {
            previewScene = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void TearDown()
        {
            if (rig != null)
            {
                rig.Dispose();
                rig = null;
            }
            if (previewScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [Test]
        public void ScenariosHaveUniqueNamesAndAUsableKeyLight()
        {
            var names = new HashSet<string>();
            Assert.That(UMAStageLighting.Scenarios.Count, Is.GreaterThanOrEqualTo(4));
            foreach (UMAStageLighting.Scenario scenario in UMAStageLighting.Scenarios)
            {
                Assert.That(names.Add(scenario.name), Is.True, scenario.name);
                Assert.That(scenario.description, Is.Not.Empty, scenario.name);
                Assert.That(scenario.key.intensity, Is.GreaterThan(scenario.fill.intensity), scenario.name);
                Assert.That(scenario.key.intensity, Is.GreaterThan(0f), scenario.name);
            }
            Assert.That(UMAStageLighting.ScenarioNames.Length, Is.EqualTo(UMAStageLighting.Scenarios.Count));
            Assert.That(UMAStageLighting.FindScenario(UMAStageLighting.Scenarios[2].name), Is.EqualTo(2));
            Assert.That(UMAStageLighting.FindScenario("No Such Scenario"), Is.EqualTo(0));
            Assert.That(UMAStageLighting.FindScenario(null), Is.EqualTo(0));
        }

        [Test]
        public void RigIsThreeDirectionalLightsInThePreviewScene()
        {
            rig = new UMAStageLighting(previewScene);

            Assert.That(rig.Root.scene, Is.EqualTo(previewScene));
            Light[] lights = rig.Root.GetComponentsInChildren<Light>(true);
            Assert.That(lights.Length, Is.EqualTo(3));
            foreach (Light light in lights)
            {
                Assert.That(light.type, Is.EqualTo(LightType.Directional), light.name);
                Assert.That(light.transform.parent, Is.EqualTo(rig.Root.transform), light.name);
            }
            // Only the key light casts shadows, and only while Shadows is on.
            Assert.That(rig.KeyLight.shadows, Is.Not.EqualTo(LightShadows.None));
            Assert.That(rig.FillLight.shadows, Is.EqualTo(LightShadows.None));
            Assert.That(rig.RimLight.shadows, Is.EqualTo(LightShadows.None));
            rig.Shadows = false;
            Assert.That(rig.KeyLight.shadows, Is.EqualTo(LightShadows.None));

            // The key light stands in front of the model (which faces +Z) and above it, so it shines
            // back and down; the rim light stands behind it.
            Assert.That(rig.KeyLight.transform.forward.z, Is.LessThan(0f));
            Assert.That(rig.KeyLight.transform.forward.y, Is.LessThan(0f));
            Assert.That(rig.RimLight.transform.forward.z, Is.GreaterThan(0f));
        }

        [Test]
        public void RotationTurnsTheWholeSetupAndBrightnessScalesIt()
        {
            rig = new UMAStageLighting(previewScene);
            Vector3 keyBefore = rig.KeyLight.transform.forward;
            float intensityBefore = rig.KeyLight.intensity;

            rig.Rotation = 180f;
            Vector3 keyAfter = rig.KeyLight.transform.forward;
            Assert.That(keyAfter.x, Is.EqualTo(-keyBefore.x).Within(1e-4f));
            Assert.That(keyAfter.y, Is.EqualTo(keyBefore.y).Within(1e-4f));
            Assert.That(keyAfter.z, Is.EqualTo(-keyBefore.z).Within(1e-4f));
            rig.Rotation = 500f;
            Assert.That(rig.Rotation, Is.EqualTo(360f));

            rig.Brightness = 2f;
            Assert.That(rig.KeyLight.intensity, Is.EqualTo(intensityBefore * 2f).Within(intensityBefore * 1e-4f));
            rig.Brightness = 100f;
            Assert.That(rig.Brightness, Is.EqualTo(UMAStageLighting.MaxBrightness));

            // A scenario change keeps the rotation.
            rig.ScenarioIndex = UMAStageLighting.Scenarios.Count - 1;
            Assert.That(rig.Root.transform.rotation.eulerAngles.y, Is.EqualTo(0f).Within(1e-3f).Or.EqualTo(360f).Within(1e-3f));
            rig.ScenarioIndex = 1000;
            Assert.That(rig.ScenarioIndex, Is.EqualTo(UMAStageLighting.Scenarios.Count - 1));
        }

        [Test]
        public void OnlyThePreviewSceneIsLit()
        {
            AmbientMode mainMode = RenderSettings.ambientMode;
            Color mainSky = RenderSettings.ambientSkyColor;
            DefaultReflectionMode mainReflection = RenderSettings.defaultReflectionMode;
            Light mainSun = RenderSettings.sun;

            rig = new UMAStageLighting(previewScene, 1, 90f, 1f, true);
            rig.ScenarioIndex = 3;

            Assert.That(RenderSettings.ambientMode, Is.EqualTo(mainMode));
            Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(mainSky));
            Assert.That(RenderSettings.defaultReflectionMode, Is.EqualTo(mainReflection));
            Assert.That(RenderSettings.sun, Is.EqualTo(mainSun));

            if (rig.Pipeline == UMAUtils.PipelineType.HDPipeline)
            {
                // HDRP takes ambient light from the rig's own sky volume instead.
                return;
            }
            Assert.That(Unsupported.SetOverrideLightingSettings(previewScene), Is.True);
            try
            {
                UMAStageLighting.Scenario scenario = UMAStageLighting.Scenarios[3];
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
                Assert.That(RenderSettings.ambientSkyColor.r, Is.EqualTo(scenario.skyColor.r).Within(1e-4f));
                Assert.That(RenderSettings.ambientGroundColor.b, Is.EqualTo(scenario.groundColor.b).Within(1e-4f));
                Assert.That(RenderSettings.defaultReflectionMode, Is.EqualTo(DefaultReflectionMode.Custom));
                Assert.That(RenderSettings.customReflectionTexture, Is.Not.Null);
                Assert.That(RenderSettings.sun, Is.EqualTo(rig.KeyLight));
            }
            finally
            {
                Unsupported.RestoreOverrideLightingSettings();
            }
        }

        [Test]
        public void DisposeRemovesEverything()
        {
            rig = new UMAStageLighting(previewScene);
            Assert.That(previewScene.rootCount, Is.EqualTo(1));

            rig.Dispose();
            Assert.That(previewScene.rootCount, Is.EqualTo(0));
            Assert.That(rig.Root == null, Is.True);
            foreach (Cubemap cubemap in Resources.FindObjectsOfTypeAll<Cubemap>())
            {
                Assert.That(cubemap.name, Is.Not.EqualTo("UMA Stage Reflection"));
            }
            // Using or disposing it again is harmless.
            rig.ScenarioIndex = 2;
            rig.Rotation = 45f;
            rig.Dispose();
        }
    }
}
#endif
