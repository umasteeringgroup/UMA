#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The Vertex Editor remembers its workflow, brushes and brush settings between sessions, and
    /// falls back to the defaults for anything that was not stored or can no longer be used.
    /// </summary>
    public sealed class VertexEditorToolSettingsTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        // Never the real key: the tests must not change the user's own choices.
        private const string TestKey = "UMA.VertexEditor.ToolSettings.Tests";

        private static readonly string[] RestoredFields =
        {
            "currentDefineMode", "currentMode", "selectionBrushRadiusPixels",
            "sculptTool", "sculptMaskTool", "sculptFalloff", "sculptRadius", "sculptStrengthPercent",
            "sculptPinchStrength", "sculptCreaseDepth", "sculptPlaneMode", "sculptBoundaryMode", "sculptElasticMode",
            "sculptElasticVolumePreservation", "sculptSymmetryX", "sculptConnectedOnly", "sculptUpdateNormalsWhileSculpting",
            "vertexPaintTool", "vertexPaintBrushShape", "vertexPaintBrushColor", "vertexPaintBlend", "vertexPaintRadius",
            "vertexPaintStrengthPercent", "vertexPaintFalloff", "vertexPaintSmoothRadius", "vertexPaintSymmetryX",
            "vertexPaintAffectAlpha", "vertexPaintNoiseType", "vertexPaintNoiseScale", "vertexPaintNoiseAmount",
            "vertexPaintNoiseColor", "vertexPaintNoiseRepeat", "vertexPaintNoiseSeed",
            "sculptFrontFacesOnly", "showSculptNormals", "sculptNormalLength", "brushIgnoresLockedSlots",
            "sculptUVLockBorders", "sculptBeautifyEvenSpacing", "sculptBeautifyKeepShape", "sculptBeautifyKeepTexture",
            "showOriginalMaterials", "showVertexColorsView",
        };

        private readonly List<Object> objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            EditorPrefs.DeleteKey(TestKey);
        }

        [TearDown]
        public void TearDown()
        {
            EditorPrefs.DeleteKey(TestKey);
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }
            objects.Clear();
        }

        [Test]
        public void ChoicesSurviveSavingAndLoading()
        {
            VertexEditorStage before = NewStage();
            SetEnum(before, "sceneToolMode", "VertexPaint");
            SetEnum(before, "currentDefineMode", "DefineVertexState");
            SetEnum(before, "currentMode", "Deactivate");
            Set(before, "selectionBrushRadiusPixels", 80);
            SetEnum(before, "sculptTool", "CombNormals");
            SetEnum(before, "sculptMaskTool", "Erase");
            Set(before, "sculptFrontFacesOnly", true);
            Set(before, "showSculptNormals", true);
            Set(before, "sculptNormalLength", 0.02f);
            Set(before, "brushIgnoresLockedSlots", false);
            Set(before, "showOriginalMaterials", true);
            Set(before, "sculptUVLockBorders", true);
            Set(before, "sculptBeautifyEvenSpacing", 0.4f);
            Set(before, "sculptBeautifyKeepShape", false);
            Set(before, "sculptBeautifyKeepTexture", false);
            SetEnum(before, "sculptFalloff", "UserDefined");
            Set(before, "sculptCustomFalloff", AnimationCurve.Linear(0f, 1f, 1f, 0.25f));
            Set(before, "sculptRadius", 0.12f);
            Set(before, "sculptStrengthPercent", 60f);
            Set(before, "sculptPinchStrength", 1.5f);
            Set(before, "sculptCreaseDepth", 0.4f);
            SetEnum(before, "sculptPlaneMode", "Scrape");
            SetEnum(before, "sculptBoundaryMode", "Twist");
            SetEnum(before, "sculptElasticMode", "Scale");
            Set(before, "sculptElasticVolumePreservation", 0.3f);
            Set(before, "sculptSymmetryX", true);
            Set(before, "sculptConnectedOnly", true);
            Set(before, "sculptUpdateNormalsWhileSculpting", false);
            SetEnum(before, "vertexPaintTool", "Noise");
            SetEnum(before, "vertexPaintBrushShape", "Square");
            Set(before, "vertexPaintBrushColor", (Color)new Color32(10, 200, 30, 128));
            SetEnum(before, "vertexPaintBlend", "Multiply");
            Set(before, "vertexPaintRadius", 0.08f);
            Set(before, "vertexPaintStrengthPercent", 70f);
            SetEnum(before, "vertexPaintFalloff", "Sharp");
            Set(before, "vertexPaintSmoothRadius", 0.2f);
            Set(before, "vertexPaintSymmetryX", true);
            Set(before, "vertexPaintAffectAlpha", false);
            SetEnum(before, "vertexPaintNoiseType", "Smooth");
            Set(before, "vertexPaintNoiseScale", 20f);
            Set(before, "vertexPaintNoiseAmount", 0.5f);
            Set(before, "vertexPaintNoiseColor", true);
            Set(before, "vertexPaintNoiseRepeat", true);
            Set(before, "vertexPaintNoiseSeed", 1234);

            before.CaptureToolSettings().Save(TestKey);
            VertexEditorToolSettings loaded = VertexEditorToolSettings.Load(TestKey);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.workflow, Is.EqualTo("VertexPaint"));

            VertexEditorStage after = NewStage();
            after.ApplyToolSettings(loaded);

            foreach (string field in RestoredFields)
            {
                Assert.That(Get(after, field), Is.EqualTo(Get(before, field)), field);
            }
            var curve = (AnimationCurve)Get(after, "sculptCustomFalloff");
            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(0.625f).Within(1e-4f));
            Assert.That(after.CaptureToolSettings().ToJson(), Is.EqualTo(before.CaptureToolSettings().ToJson()
                .Replace("\"workflow\":\"VertexPaint\"", "\"workflow\":\"AdvancedVertexAdjustments\"")),
                "Everything but the workflow, which the editor applies itself when it opens, is restored.");
        }

        [Test]
        public void UnknownOrOutOfRangeValuesKeepTheDefaults()
        {
            const string json =
                "{\"workflow\":\"NoSuchWorkflow\",\"advancedTool\":\"Lasso\",\"defineMode\":\"DefineVertexState\",\"selectAction\":\"Add\"," +
                "\"selectionBrushRadiusPixels\":1000,\"sculptTool\":\"NoSuchBrush\",\"sculptFalloff\":\"3\",\"sculptRadius\":5.0," +
                "\"sculptStrengthPercent\":-1.0,\"sculptSymmetryX\":7,\"vertexPaintTool\":\"Paint, Erase\",\"vertexPaintBrushColor\":\"zzzzzzzz\"," +
                "\"vertexPaintBrushTextureGuid\":\"00000000000000000000000000000000\",\"vertexPaintRadius\":0.0,\"vertexPaintNoiseSeed\":-2147483648}";
            VertexEditorToolSettings settings = VertexEditorToolSettings.FromJson(json);
            Assert.That(settings, Is.Not.Null);

            VertexEditorStage defaults = NewStage();
            VertexEditorStage stage = NewStage();
            stage.ApplyToolSettings(settings);

            foreach (string field in RestoredFields)
            {
                if (field == "currentDefineMode" || field == "currentMode")
                {
                    continue;
                }
                Assert.That(Get(stage, field), Is.EqualTo(Get(defaults, field)), field);
            }
            Assert.That(Get(stage, "vertexPaintBrushTexture"), Is.Null);
            // A known mode is kept; an action from the other mode falls back to that mode's first action.
            Assert.That(Get(stage, "currentDefineMode").ToString(), Is.EqualTo("DefineVertexState"));
            Assert.That(Get(stage, "currentMode").ToString(), Is.EqualTo("ToggleState"));
            Assert.That(VertexEditorToolSettings.ReadEnum(settings.workflow, MeshModifierEditor.EditorMode.MeshModifiers),
                Is.EqualTo(MeshModifierEditor.EditorMode.MeshModifiers));
        }

        [Test]
        public void NothingStoredChangesNothing()
        {
            Assert.That(VertexEditorToolSettings.Load(TestKey), Is.Null);
            Assert.That(VertexEditorToolSettings.FromJson(null), Is.Null);
            Assert.That(VertexEditorToolSettings.FromJson(string.Empty), Is.Null);
            Assert.That(VertexEditorToolSettings.FromJson("{not json"), Is.Null);

            VertexEditorStage defaults = NewStage();
            VertexEditorStage stage = NewStage();
            stage.ApplyToolSettings(null);
            stage.ApplyToolSettings(VertexEditorToolSettings.FromJson("{}"));
            Assert.That(stage.CaptureToolSettings().ToJson(), Is.EqualTo(defaults.CaptureToolSettings().ToJson()));
        }

        [Test]
        public void ChoicesAreReadByNameOnly()
        {
            Assert.That(VertexEditorToolSettings.ReadEnum("Blendshapes", MeshModifierEditor.EditorMode.MeshModifiers),
                Is.EqualTo(MeshModifierEditor.EditorMode.Blendshapes));
            Assert.That(VertexEditorToolSettings.ReadEnum("2", MeshModifierEditor.EditorMode.MeshModifiers),
                Is.EqualTo(MeshModifierEditor.EditorMode.MeshModifiers));
            Assert.That(VertexEditorToolSettings.ReadEnum("blendshapes", MeshModifierEditor.EditorMode.MeshModifiers),
                Is.EqualTo(MeshModifierEditor.EditorMode.MeshModifiers));
            Assert.That(VertexEditorToolSettings.ReadFloat(float.NaN, 0f, 1f, 0.5f), Is.EqualTo(0.5f));
            Assert.That(VertexEditorToolSettings.ReadFloat(1.000001f, 0f, 1f, 0.5f), Is.EqualTo(1f));
            Assert.That(VertexEditorToolSettings.ReadBool(-1, true), Is.True);
            Assert.That(VertexEditorToolSettings.ReadBool(0, true), Is.False);
        }

        [Test]
        public void SteppedFalloffCurveSurvivesSaving()
        {
            VertexEditorStage before = NewStage();
            var stepped = new AnimationCurve(
                new Keyframe(0f, 1f, float.PositiveInfinity, float.PositiveInfinity),
                new Keyframe(0.5f, 0.5f, float.PositiveInfinity, float.PositiveInfinity),
                new Keyframe(1f, 0f, float.PositiveInfinity, float.PositiveInfinity));
            Set(before, "vertexPaintCustomFalloff", stepped);
            SetEnum(before, "vertexPaintTool", "Blur");

            before.CaptureToolSettings().Save(TestKey);
            VertexEditorStage after = NewStage();
            after.ApplyToolSettings(VertexEditorToolSettings.Load(TestKey));

            Assert.That(Get(after, "vertexPaintTool").ToString(), Is.EqualTo("Blur"));
            var curve = (AnimationCurve)Get(after, "vertexPaintCustomFalloff");
            Assert.That(curve.length, Is.EqualTo(3));
            Assert.That(curve.Evaluate(0.25f), Is.EqualTo(stepped.Evaluate(0.25f)).Within(1e-5f));
        }

        private VertexEditorStage NewStage()
        {
            var stage = ScriptableObject.CreateInstance<VertexEditorStage>();
            objects.Add(stage);
            return stage;
        }

        private static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(VertexEditorStage).GetField(name, PrivateInstance);
            Assert.That(field, Is.Not.Null, "VertexEditorStage." + name);
            return field;
        }

        private static object Get(VertexEditorStage stage, string name) => Field(name).GetValue(stage);

        private static void Set(VertexEditorStage stage, string name, object value) => Field(name).SetValue(stage, value);

        private static void SetEnum(VertexEditorStage stage, string name, string value)
        {
            FieldInfo field = Field(name);
            field.SetValue(stage, Enum.Parse(field.FieldType, value));
        }
    }
}
#endif
