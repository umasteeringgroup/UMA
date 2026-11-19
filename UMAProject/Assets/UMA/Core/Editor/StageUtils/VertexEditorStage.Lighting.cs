using System;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// The stage's lighting: three point lighting (key, fill, rim) in a choice of scenarios, which
    /// can be turned around the model. See <see cref="UMAStageLighting"/>.
    /// </summary>
    /// <remarks>
    /// The choices have their own EditorPrefs keys, like Wireframe, so every editor that uses this
    /// stage opens with the lighting last used. They are only written when changed in the panel.
    /// </remarks>
    public partial class VertexEditorStage
    {
        private const string LightingScenarioPrefsKey = "UMA.VertexEditor.Lighting.Scenario";
        private const string LightingRotationPrefsKey = "UMA.VertexEditor.Lighting.Rotation";
        private const string LightingBrightnessPrefsKey = "UMA.VertexEditor.Lighting.Brightness";
        private const string LightingShadowsPrefsKey = "UMA.VertexEditor.Lighting.Shadows";
        private const string LightingSectionPrefsKey = "UMA.VertexEditor.Lighting.SectionOpen";

        [NonSerialized] private UMAStageLighting stageLighting;
        [NonSerialized] private bool showLightingSection = true;
        [NonSerialized] private GUIContent[] lightingScenarioContents;

        /// <summary>The stage's lights. Null before the stage opens and after it closes.</summary>
        public UMAStageLighting StageLighting => stageLighting;

        private void CreateStageLighting()
        {
            DestroyStageLighting();
            showLightingSection = EditorPrefs.GetBool(LightingSectionPrefsKey, true);
            // Stored by name, so a reordered or extended list still restores the right scenario.
            int scenario = UMAStageLighting.FindScenario(EditorPrefs.GetString(LightingScenarioPrefsKey, string.Empty));
            float rotation = EditorPrefs.GetFloat(LightingRotationPrefsKey, 0f);
            float brightness = EditorPrefs.GetFloat(LightingBrightnessPrefsKey, 1f);
            if (float.IsNaN(rotation) || float.IsInfinity(rotation))
            {
                rotation = 0f;
            }
            if (float.IsNaN(brightness) || float.IsInfinity(brightness))
            {
                brightness = 1f;
            }
            stageLighting = new UMAStageLighting(
                scene, scenario, rotation, brightness, EditorPrefs.GetBool(LightingShadowsPrefsKey, true));
        }

        private void DestroyStageLighting()
        {
            if (stageLighting != null)
            {
                stageLighting.Dispose();
                stageLighting = null;
            }
        }

        /// <summary>The Lighting section of the tools panel.</summary>
        private void DrawLightingOptions()
        {
            if (stageLighting == null)
            {
                return;
            }
            bool wasOpen = showLightingSection;
            if (DrawSectionFoldout(ref showLightingSection, "Lighting") != wasOpen)
            {
                EditorPrefs.SetBool(LightingSectionPrefsKey, showLightingSection);
            }
            if (!showLightingSection)
            {
                return;
            }
            if (lightingScenarioContents == null)
            {
                lightingScenarioContents = new GUIContent[UMAStageLighting.Scenarios.Count];
                for (int i = 0; i < lightingScenarioContents.Length; i++)
                {
                    lightingScenarioContents[i] = new GUIContent(UMAStageLighting.Scenarios[i].name);
                }
            }

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            int scenario = EditorGUILayout.Popup(
                new GUIContent("Scenario", stageLighting.CurrentScenario.description),
                stageLighting.ScenarioIndex,
                lightingScenarioContents);
            float rotation = EditorGUILayout.Slider(
                new GUIContent("Rotation", "Turn the key, fill and rim lights around the model (degrees)."),
                stageLighting.Rotation, 0f, 360f);
            float brightness = EditorGUILayout.Slider(
                new GUIContent("Brightness", "Make all the lights and the ambient light brighter or dimmer. 1 is the scenario as designed."),
                stageLighting.Brightness, UMAStageLighting.MinBrightness, UMAStageLighting.MaxBrightness);
            bool shadows = EditorGUILayout.Toggle(
                new GUIContent("Shadows", "The key light casts shadows. Fill and rim lights never do."),
                stageLighting.Shadows);
            if (EditorGUI.EndChangeCheck())
            {
                stageLighting.ScenarioIndex = scenario;
                stageLighting.Rotation = rotation;
                stageLighting.Brightness = brightness;
                stageLighting.Shadows = shadows;
                EditorPrefs.SetString(LightingScenarioPrefsKey, stageLighting.CurrentScenario.name);
                EditorPrefs.SetFloat(LightingRotationPrefsKey, stageLighting.Rotation);
                EditorPrefs.SetFloat(LightingBrightnessPrefsKey, stageLighting.Brightness);
                EditorPrefs.SetBool(LightingShadowsPrefsKey, stageLighting.Shadows);
                SceneView.RepaintAll();
            }
            EditorGUI.indentLevel--;
        }
    }
}
