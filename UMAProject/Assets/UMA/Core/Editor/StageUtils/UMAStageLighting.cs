using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UMA
{
    /// <summary>
    /// Three point lighting (key, fill and rim) with matching ambient light and reflections for an
    /// editing stage's preview scene. One rig serves the Built-in pipeline, URP and HDRP: the active
    /// pipeline is detected when the rig is created, and the same lighting scenario is expressed in
    /// that pipeline's units.
    /// </summary>
    /// <remarks>
    /// The rig only touches the preview scene it was created for. Its lights are children of one
    /// object, so <see cref="Rotation"/> turns the whole setup around the model. The pipeline packages
    /// are reached by reflection, because the UMA editor assembly must compile without them.
    /// </remarks>
    public sealed class UMAStageLighting
    {
        /// <summary>One light: where it stands relative to the model, and how bright it is.</summary>
        public readonly struct LightSetup
        {
            public readonly Color color;
            /// <summary>Brightness in Built-in/URP units. HDRP gets the lux value that looks the same.</summary>
            public readonly float intensity;
            /// <summary>Degrees around the model. 0 is straight in front of it; positive is toward its left.</summary>
            public readonly float azimuth;
            /// <summary>Degrees above the horizon.</summary>
            public readonly float elevation;

            public LightSetup(Color color, float intensity, float azimuth, float elevation)
            {
                this.color = color;
                this.intensity = intensity;
                this.azimuth = azimuth;
                this.elevation = elevation;
            }
        }

        /// <summary>A lighting condition: the three lights, the ambient light and how dark shadows are.</summary>
        public sealed class Scenario
        {
            public readonly string name;
            public readonly string description;
            public readonly LightSetup key;
            public readonly LightSetup fill;
            public readonly LightSetup rim;
            public readonly Color skyColor;
            public readonly Color horizonColor;
            public readonly Color groundColor;
            public readonly float shadowStrength;

            public Scenario(string name, string description, LightSetup key, LightSetup fill, LightSetup rim,
                Color skyColor, Color horizonColor, Color groundColor, float shadowStrength)
            {
                this.name = name;
                this.description = description;
                this.key = key;
                this.fill = fill;
                this.rim = rim;
                this.skyColor = skyColor;
                this.horizonColor = horizonColor;
                this.groundColor = groundColor;
                this.shadowStrength = shadowStrength;
            }
        }

        public const float MinBrightness = 0.25f;
        public const float MaxBrightness = 2f;

        private static Color Rgb(float r, float g, float b)
        {
            return new Color(r, g, b, 1f);
        }

        /// <summary>The lighting conditions on offer. The first one is the default.</summary>
        public static readonly IReadOnlyList<Scenario> Scenarios = new[]
        {
            new Scenario("Studio", "Neutral three point lighting: a soft white key, a cool fill and a white rim light.",
                new LightSetup(Rgb(1f, 0.98f, 0.95f), 1.25f, 35f, 35f),
                new LightSetup(Rgb(0.85f, 0.9f, 1f), 0.45f, -55f, 10f),
                new LightSetup(Rgb(1f, 1f, 1f), 1f, -150f, 40f),
                Rgb(0.36f, 0.37f, 0.4f), Rgb(0.28f, 0.28f, 0.29f), Rgb(0.16f, 0.15f, 0.14f), 0.6f),
            new Scenario("Outdoors - Sunny", "Midday sun from high up, blue sky light in the shadows and warm light bounced from the ground.",
                new LightSetup(Rgb(1f, 0.96f, 0.88f), 2f, 40f, 55f),
                new LightSetup(Rgb(0.7f, 0.82f, 1f), 0.35f, -70f, 25f),
                new LightSetup(Rgb(1f, 0.95f, 0.85f), 0.5f, -140f, 30f),
                Rgb(0.45f, 0.6f, 0.85f), Rgb(0.45f, 0.5f, 0.55f), Rgb(0.25f, 0.22f, 0.18f), 0.85f),
            new Scenario("Outdoors - Overcast", "Soft grey light from the whole sky, with hardly any shadow.",
                new LightSetup(Rgb(0.9f, 0.93f, 1f), 0.6f, 15f, 70f),
                new LightSetup(Rgb(0.85f, 0.88f, 0.95f), 0.3f, -90f, 30f),
                new LightSetup(Rgb(0.85f, 0.88f, 0.95f), 0.3f, 180f, 45f),
                Rgb(0.7f, 0.72f, 0.78f), Rgb(0.5f, 0.52f, 0.56f), Rgb(0.25f, 0.25f, 0.25f), 0.25f),
            new Scenario("Outdoors - Sunset", "A low orange sun from the side, a warm glow behind and cool blue sky light in the shadows.",
                new LightSetup(Rgb(1f, 0.62f, 0.32f), 1.6f, 60f, 10f),
                new LightSetup(Rgb(0.45f, 0.5f, 0.8f), 0.35f, -60f, 30f),
                new LightSetup(Rgb(1f, 0.5f, 0.25f), 1.1f, -160f, 15f),
                Rgb(0.3f, 0.33f, 0.55f), Rgb(0.55f, 0.38f, 0.3f), Rgb(0.15f, 0.11f, 0.1f), 0.8f),
            new Scenario("Indoors", "A warm ceiling lamp, light bounced from the walls and cool daylight from a window behind.",
                new LightSetup(Rgb(1f, 0.82f, 0.62f), 1f, 30f, 50f),
                new LightSetup(Rgb(1f, 0.88f, 0.75f), 0.3f, -70f, 5f),
                new LightSetup(Rgb(0.75f, 0.85f, 1f), 0.5f, -140f, 20f),
                Rgb(0.3f, 0.25f, 0.2f), Rgb(0.22f, 0.19f, 0.16f), Rgb(0.12f, 0.1f, 0.08f), 0.6f),
            new Scenario("Night", "Dim blue moonlight, a faint warm lamp from the side and very little ambient light.",
                new LightSetup(Rgb(0.55f, 0.68f, 1f), 0.45f, -30f, 50f),
                new LightSetup(Rgb(1f, 0.6f, 0.3f), 0.12f, 70f, 5f),
                new LightSetup(Rgb(0.5f, 0.62f, 1f), 0.5f, 150f, 35f),
                Rgb(0.04f, 0.06f, 0.12f), Rgb(0.03f, 0.04f, 0.07f), Rgb(0.015f, 0.015f, 0.02f), 0.8f),
        };

        private static string[] scenarioNames;

        /// <summary>The scenario names, in the order of <see cref="Scenarios"/>.</summary>
        public static string[] ScenarioNames
        {
            get
            {
                if (scenarioNames == null)
                {
                    scenarioNames = new string[Scenarios.Count];
                    for (int i = 0; i < scenarioNames.Length; i++)
                    {
                        scenarioNames[i] = Scenarios[i].name;
                    }
                }
                return scenarioNames;
            }
        }

        /// <summary>The index of the scenario called <paramref name="name"/>, or 0 (the default) when there is none.</summary>
        public static int FindScenario(string name)
        {
            for (int i = 0; i < Scenarios.Count; i++)
            {
                if (string.Equals(Scenarios[i].name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return 0;
        }

        // HDRP lights are physical. The rig fixes the stage's exposure at this EV100 and converts the
        // scenario's unit-less brightness to the lux and sky luminance that look the same there.
        private const float HdrpExposureEV = 5f;
        private static readonly float HdrpLuminancePerUnit = 1.2f * Mathf.Pow(2f, HdrpExposureEV);
        private static readonly float HdrpLuxPerUnit = Mathf.PI * HdrpLuminancePerUnit;
        private const int ReflectionSize = 32;

        private const string CoreAssembly = "Unity.RenderPipelines.Core.Runtime";
        private const string HdrpAssembly = "Unity.RenderPipelines.HighDefinition.Runtime";
        private const string HdrpNamespace = "UnityEngine.Rendering.HighDefinition.";

        private readonly Scene scene;
        private readonly UMAUtils.PipelineType pipeline;
        private GameObject root;
        private Light keyLight;
        private Light fillLight;
        private Light rimLight;
        private Component keyHdData;
        private Cubemap reflection;
        // HDRP only: the stage's own exposure and sky.
        private ScriptableObject volumeProfile;
        private ScriptableObject gradientSky;

        private int scenarioIndex;
        private float rotation;
        private float brightness = 1f;
        private bool shadows = true;

        /// <summary>The pipeline the rig was built for.</summary>
        public UMAUtils.PipelineType Pipeline => pipeline;

        /// <summary>The object holding the three lights. Null once the rig is disposed.</summary>
        public GameObject Root => root;

        public Light KeyLight => keyLight;
        public Light FillLight => fillLight;
        public Light RimLight => rimLight;

        public Scenario CurrentScenario => Scenarios[scenarioIndex];

        /// <summary>Index into <see cref="Scenarios"/>.</summary>
        public int ScenarioIndex
        {
            get { return scenarioIndex; }
            set
            {
                int index = Mathf.Clamp(value, 0, Scenarios.Count - 1);
                if (index != scenarioIndex)
                {
                    scenarioIndex = index;
                    Apply();
                }
            }
        }

        /// <summary>Degrees the whole setup is turned around the model, 0 to 360.</summary>
        public float Rotation
        {
            get { return rotation; }
            set
            {
                rotation = Mathf.Clamp(value, 0f, 360f);
                if (root != null)
                {
                    root.transform.rotation = Quaternion.Euler(0f, rotation, 0f);
                }
            }
        }

        /// <summary>Multiplies every light and the ambient light.</summary>
        public float Brightness
        {
            get { return brightness; }
            set
            {
                float clamped = Mathf.Clamp(value, MinBrightness, MaxBrightness);
                if (!Mathf.Approximately(clamped, brightness))
                {
                    brightness = clamped;
                    Apply();
                }
            }
        }

        /// <summary>Whether the key light casts shadows. Fill and rim never do.</summary>
        public bool Shadows
        {
            get { return shadows; }
            set
            {
                if (value != shadows)
                {
                    shadows = value;
                    Apply();
                }
            }
        }

        /// <summary>Builds the rig in <paramref name="previewScene"/> and lights it with the given choices.</summary>
        public UMAStageLighting(Scene previewScene, int scenario = 0, float rotation = 0f, float brightness = 1f, bool shadows = true)
        {
            scene = previewScene;
            pipeline = UMAUtils.DetectPipeline();
            scenarioIndex = Mathf.Clamp(scenario, 0, Scenarios.Count - 1);
            this.brightness = Mathf.Clamp(brightness, MinBrightness, MaxBrightness);
            this.shadows = shadows;

            root = new GameObject("UMA Stage Lighting");
            root.hideFlags = HideFlags.DontSave;
            if (scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            keyLight = CreateLight("Key Light", out keyHdData);
            fillLight = CreateLight("Fill Light", out _);
            rimLight = CreateLight("Rim Light", out _);
            if (pipeline == UMAUtils.PipelineType.HDPipeline)
            {
                CreateHdrpVolume();
            }
            Rotation = rotation;
            Apply();
        }

        /// <summary>Removes the lights and everything else the rig created.</summary>
        public void Dispose()
        {
            if (volumeProfile != null)
            {
                // The profile's components are objects of their own.
                if (GetMember(volumeProfile, "components") is System.Collections.IEnumerable components)
                {
                    var owned = new List<UnityEngine.Object>();
                    foreach (object component in components)
                    {
                        if (component is UnityEngine.Object unityObject && unityObject != null)
                        {
                            owned.Add(unityObject);
                        }
                    }
                    foreach (UnityEngine.Object unityObject in owned)
                    {
                        UnityEngine.Object.DestroyImmediate(unityObject);
                    }
                }
                UnityEngine.Object.DestroyImmediate(volumeProfile);
                volumeProfile = null;
                gradientSky = null;
            }
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                root = null;
            }
            if (reflection != null)
            {
                UnityEngine.Object.DestroyImmediate(reflection);
                reflection = null;
            }
            keyLight = fillLight = rimLight = null;
            keyHdData = null;
        }

        /// <summary>Lights the scene with the current scenario, brightness and shadow choice.</summary>
        public void Apply()
        {
            if (root == null)
            {
                return;
            }
            Scenario scenario = CurrentScenario;
            ApplyLight(keyLight, scenario.key);
            ApplyLight(fillLight, scenario.fill);
            ApplyLight(rimLight, scenario.rim);

            bool castShadows = shadows && scenario.shadowStrength > 0f;
            keyLight.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            keyLight.shadowStrength = scenario.shadowStrength;
            if (keyHdData != null)
            {
                // HDRP reads its own value for how dark the shadow is.
                SetMember(keyHdData, "shadowDimmer", scenario.shadowStrength);
            }

            if (pipeline == UMAUtils.PipelineType.HDPipeline)
            {
                ApplyHdrpSky(scenario);
            }
            else
            {
                ApplyRenderSettings(scenario);
            }
        }

        private Light CreateLight(string name, out Component hdData)
        {
            var lightObject = new GameObject(name);
            lightObject.hideFlags = HideFlags.DontSave;
            lightObject.transform.SetParent(root.transform, false);
            var light = lightObject.AddComponent<Light>();
            // Directional lights give a model of any size the same lighting.
            light.type = LightType.Directional;
            light.useColorTemperature = false;
            light.shadows = LightShadows.None;
            hdData = null;
            if (pipeline == UMAUtils.PipelineType.HDPipeline)
            {
                Type hdDataType = Type.GetType(HdrpNamespace + "HDAdditionalLightData, " + HdrpAssembly);
                if (hdDataType != null)
                {
                    hdData = lightObject.AddComponent(hdDataType);
                    SetMember(hdData, "affectsVolumetric", false);
                }
                light.lightUnit = LightUnit.Lux;
            }
            return light;
        }

        private void ApplyLight(Light light, LightSetup setup)
        {
            if (light == null)
            {
                return;
            }
            float azimuth = setup.azimuth * Mathf.Deg2Rad;
            float elevation = setup.elevation * Mathf.Deg2Rad;
            // Where the light stands. The model faces +Z, and its left is +X.
            var position = new Vector3(
                Mathf.Sin(azimuth) * Mathf.Cos(elevation),
                Mathf.Sin(elevation),
                Mathf.Cos(azimuth) * Mathf.Cos(elevation));
            light.transform.localRotation = Quaternion.LookRotation(-position, Vector3.up);
            light.color = setup.color;
            float intensity = setup.intensity * brightness;
            light.intensity = pipeline == UMAUtils.PipelineType.HDPipeline ? intensity * HdrpLuxPerUnit : intensity;
            light.enabled = intensity > 0f;
        }

        // ---- Built-in and URP: the preview scene's own ambient light and reflections ----

        private void ApplyRenderSettings(Scenario scenario)
        {
            if (!scene.IsValid())
            {
                return;
            }
            BuildReflection(scenario);
            // RenderSettings belong to the active scene. A preview scene cannot be made active, so
            // its settings are reached the way the Scene view itself does it while drawing a stage.
            if (!UnityEditor.Unsupported.SetOverrideLightingSettings(scene))
            {
                return;
            }
            try
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = scenario.skyColor * brightness;
                RenderSettings.ambientEquatorColor = scenario.horizonColor * brightness;
                RenderSettings.ambientGroundColor = scenario.groundColor * brightness;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = reflection;
                RenderSettings.reflectionIntensity = 1f;
                RenderSettings.sun = keyLight;
                RenderSettings.fog = false;
            }
            finally
            {
                UnityEditor.Unsupported.RestoreOverrideLightingSettings();
            }
        }

        /// <summary>
        /// A small sky for reflections, in the scenario's ambient colours. Without it eyes and other
        /// smooth surfaces would reflect the default skybox whatever the scenario.
        /// </summary>
        private void BuildReflection(Scenario scenario)
        {
            if (reflection == null)
            {
                reflection = new Cubemap(ReflectionSize, TextureFormat.RGBAHalf, true)
                {
                    name = "UMA Stage Reflection",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            var pixels = new Color[ReflectionSize * ReflectionSize];
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < ReflectionSize; y++)
                {
                    float v = ((y + 0.5f) / ReflectionSize * 2f) - 1f;
                    for (int x = 0; x < ReflectionSize; x++)
                    {
                        float u = ((x + 0.5f) / ReflectionSize * 2f) - 1f;
                        Vector3 direction;
                        switch ((CubemapFace)face)
                        {
                            case CubemapFace.PositiveX: direction = new Vector3(1f, -v, -u); break;
                            case CubemapFace.NegativeX: direction = new Vector3(-1f, -v, u); break;
                            case CubemapFace.PositiveY: direction = new Vector3(u, 1f, v); break;
                            case CubemapFace.NegativeY: direction = new Vector3(u, -1f, -v); break;
                            case CubemapFace.PositiveZ: direction = new Vector3(u, -v, 1f); break;
                            default: direction = new Vector3(-u, -v, -1f); break;
                        }
                        float height = direction.normalized.y;
                        Color color = height >= 0f
                            ? Color.Lerp(scenario.horizonColor, scenario.skyColor, height)
                            : Color.Lerp(scenario.horizonColor, scenario.groundColor, -height);
                        color *= brightness;
                        color.a = 1f;
                        pixels[(y * ReflectionSize) + x] = color;
                    }
                }
                reflection.SetPixels(pixels, (CubemapFace)face);
            }
            reflection.Apply(true);
        }

        // ---- HDRP: a volume in the preview scene with a fixed exposure and a gradient sky ----

        private void CreateHdrpVolume()
        {
            Type volumeType = Type.GetType("UnityEngine.Rendering.Volume, " + CoreAssembly);
            Type profileType = Type.GetType("UnityEngine.Rendering.VolumeProfile, " + CoreAssembly);
            Type exposureType = Type.GetType(HdrpNamespace + "Exposure, " + HdrpAssembly);
            Type environmentType = Type.GetType(HdrpNamespace + "VisualEnvironment, " + HdrpAssembly);
            Type skyType = Type.GetType(HdrpNamespace + "GradientSky, " + HdrpAssembly);
            if (volumeType == null || profileType == null || exposureType == null || environmentType == null || skyType == null)
            {
                return;
            }
            MethodInfo add = profileType.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
            if (add == null)
            {
                return;
            }
            volumeProfile = ScriptableObject.CreateInstance(profileType);
            volumeProfile.name = "UMA Stage Lighting";
            volumeProfile.hideFlags = HideFlags.HideAndDontSave;

            // Only volumes in the stage's own scene reach its Scene view, so the stage needs its own
            // exposure: the lights are in lux for exactly this value.
            object exposure = add.Invoke(volumeProfile, new object[] { exposureType, false });
            SetVolumeParameter(exposure, "mode", "Fixed");
            SetVolumeParameter(exposure, "fixedExposure", HdrpExposureEV);

            // HDRP takes ambient light and reflections from the sky.
            object environment = add.Invoke(volumeProfile, new object[] { environmentType, false });
            SetVolumeParameter(environment, "skyType", 3); // SkyType.Gradient
            SetVolumeParameter(environment, "skyAmbientMode", "Dynamic");

            gradientSky = add.Invoke(volumeProfile, new object[] { skyType, false }) as ScriptableObject;
            SetVolumeParameter(gradientSky, "skyIntensityMode", "Multiplier");
            SetVolumeParameter(gradientSky, "multiplier", HdrpLuminancePerUnit);
            SetVolumeParameter(gradientSky, "gradientDiffusion", 1f);

            Component volume = root.AddComponent(volumeType);
            SetMember(volume, "isGlobal", true);
            SetMember(volume, "priority", 1000f);
            SetMember(volume, "sharedProfile", volumeProfile);
        }

        private void ApplyHdrpSky(Scenario scenario)
        {
            if (gradientSky == null)
            {
                return;
            }
            SetVolumeParameter(gradientSky, "top", scenario.skyColor * brightness);
            SetVolumeParameter(gradientSky, "middle", scenario.horizonColor * brightness);
            SetVolumeParameter(gradientSky, "bottom", scenario.groundColor * brightness);
        }

        // ---- Reflection helpers ----

        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

        /// <summary>Overrides the volume parameter held in <paramref name="field"/>. An enum may be given by name.</summary>
        private static void SetVolumeParameter(object component, string field, object value)
        {
            object parameter = GetMember(component, field);
            if (parameter == null)
            {
                return;
            }
            SetMember(parameter, "overrideState", true);
            PropertyInfo valueProperty = FindProperty(parameter.GetType(), "value");
            if (valueProperty == null)
            {
                return;
            }
            try
            {
                if (value is string enumName && valueProperty.PropertyType.IsEnum)
                {
                    value = Enum.Parse(valueProperty.PropertyType, enumName);
                }
                valueProperty.SetValue(parameter, value);
            }
            catch (Exception)
            {
                // A parameter this HDRP version does not have in this form: its default stays.
            }
        }

        private static object GetMember(object target, string name)
        {
            if (target == null)
            {
                return null;
            }
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, PublicInstance);
            if (field != null)
            {
                return field.GetValue(target);
            }
            PropertyInfo property = FindProperty(type, name);
            return property != null && property.CanRead ? property.GetValue(target) : null;
        }

        private static void SetMember(object target, string name, object value)
        {
            if (target == null)
            {
                return;
            }
            Type type = target.GetType();
            try
            {
                FieldInfo field = type.GetField(name, PublicInstance);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
                PropertyInfo property = FindProperty(type, name);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(target, value);
                }
            }
            catch (Exception)
            {
                // A member this pipeline version does not have in this form: its default stays.
            }
        }

        /// <summary>The property as the most derived class declares it; a plain lookup fails when a class redeclares it.</summary>
        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, PublicInstance | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    return property;
                }
            }
            return null;
        }
    }
}
