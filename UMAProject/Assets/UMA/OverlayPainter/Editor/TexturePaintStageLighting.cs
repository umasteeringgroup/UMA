using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private const string LightRotationPreference = "UMA.OverlayPainter.LightRotation";
        private float lightRotation;
        private readonly Dictionary<SceneView, bool> previousSceneLighting = new Dictionary<SceneView, bool>();

        internal float SceneToolbarLightRotation => lightRotation;

        private void CreateStageLighting()
        {
            lightRotation = Mathf.Clamp(EditorPrefs.GetFloat(LightRotationPreference, 0f), 0f, 360f);
            lightObject = CreateLightingRig(scene, lightRotation);
        }

        internal static GameObject CreateLightingRig(Scene previewScene, float rotation)
        {
            var rig = new GameObject("Overlay Painter Lighting");
            SceneManager.MoveGameObjectToScene(rig, previewScene);
            rig.hideFlags = HideFlags.HideAndDontSave;
            // Directional lights give slots of any size the same neutral studio illumination.
            AddLight("Key", 1.2f, new Vector3(45f, -35f, 0f));
            AddLight("Fill", 0.5f, new Vector3(25f, 55f, 0f));
            AddLight("Rim", 0.8f, new Vector3(30f, 160f, 0f));
            rig.transform.rotation = Quaternion.Euler(0f, rotation, 0f);
            return rig;

            void AddLight(string name, float intensity, Vector3 angles)
            {
                var child = new GameObject(name + " Light");
                child.transform.SetParent(rig.transform, false);
                child.hideFlags = HideFlags.HideAndDontSave;
                child.transform.localRotation = Quaternion.Euler(angles);
                var light = child.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = intensity;
                light.shadows = LightShadows.None;
            }
        }

        internal void SetSceneToolbarLightRotation(float rotation)
        {
            lightRotation = Mathf.Clamp(rotation, 0f, 360f);
            if (lightObject != null)
                lightObject.transform.rotation = Quaternion.Euler(0f, lightRotation, 0f);
            EditorPrefs.SetFloat(LightRotationPreference, lightRotation);
            SceneView.RepaintAll();
        }

        internal void EnableStageLighting(SceneView view)
        {
            if (view == null || !ReferenceEquals(StageUtility.GetCurrentStage(), this) ||
                previousSceneLighting.ContainsKey(view)) return;
            previousSceneLighting.Add(view, view.sceneLighting);
            view.sceneLighting = true;
            view.Repaint();
        }

        private void DisposeStageLighting()
        {
            foreach (var entry in previousSceneLighting)
                if (entry.Key != null)
                {
                    entry.Key.sceneLighting = entry.Value;
                    entry.Key.Repaint();
                }
            previousSceneLighting.Clear();
            if (lightObject != null) DestroyImmediate(lightObject);
            lightObject = null;
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneLightRotationSlider : VisualElement
    {
        public const string Id = "UMA/OverlayPainter3D/LightRotation";
        private readonly Slider slider;

        public TexturePaintSceneLightRotationSlider()
        {
            style.width = 210f;
            style.justifyContent = Justify.Center;
            slider = new Slider("Light", 0f, 360f) { showInputField = true,
                tooltip = "Rotate the key, fill, and rim lights around the model (degrees)." };
            slider.labelElement.style.minWidth = 32f;
            slider.labelElement.style.width = 32f;
            slider.style.marginLeft = 4f;
            slider.style.marginRight = 4f;
            slider.RegisterValueChangedCallback(evt =>
                TexturePaintStageWindow.ActiveStage?.SetSceneToolbarLightRotation(evt.newValue));
            Add(slider);
            schedule.Execute(UpdateDisplay).Every(250L);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            var stage = TexturePaintStageWindow.ActiveStage;
            SetEnabled(stage?.SceneToolbarReady == true);
            slider.SetValueWithoutNotify(stage?.SceneToolbarLightRotation ?? 0f);
        }
    }
}
