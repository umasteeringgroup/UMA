using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class SharedColorParameterEditorTests
    {
        private readonly List<Object> objects = new List<Object>();
        private Material material;

        [SetUp]
        public void SetUp()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Assume.That(lit, Is.Not.Null, "URP Lit is needed for a material with _Smoothness and _BaseColor.");
            material = new Material(lit);
            material.SetFloat("_Smoothness", 0.25f);
            material.SetColor("_BaseColor", new Color(0.8f, 0.6f, 0.4f, 1f));
            objects.Add(material);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in objects)
            {
                if (obj == null) continue;
                Undo.ClearUndo(obj);
                Object.DestroyImmediate(obj);
            }
            objects.Clear();
        }

        private SharedColorTable Table(params OverlayColorData[] colors)
        {
            var table = ScriptableObject.CreateInstance<SharedColorTable>();
            table.channelCount = 1;
            table.colors = colors;
            objects.Add(table);
            return table;
        }

        private static OverlayColorData MakeColor(string name, params UMAProperty[] properties)
        {
            var color = new OverlayColorData(1) { name = name };
            if (properties.Length > 0)
            {
                color.PropertyBlock = new UMAMaterialPropertyBlock();
                color.PropertyBlock.shaderProperties = new List<UMAProperty>(properties);
            }
            else
            {
                color.PropertyBlock = null;
            }
            return color;
        }

        private static List<UMAProperty> Named(OverlayColorData color, string name)
        {
            var result = new List<UMAProperty>();
            if (color.PropertyBlock?.shaderProperties == null) return result;
            foreach (UMAProperty p in color.PropertyBlock.shaderProperties) if (p != null && p.name == name) result.Add(p);
            return result;
        }

        private static float SingleFloat(OverlayColorData color, string name)
        {
            List<UMAProperty> found = Named(color, name);
            Assert.That(found.Count, Is.EqualTo(1), $"{color.name} should have exactly one {name}.");
            Assert.That(found[0], Is.TypeOf<UMAFloatProperty>(), $"{color.name}'s {name} should be a float.");
            return ((UMAFloatProperty)found[0]).Value;
        }

        [Test]
        public void FixedValueReachesEveryColorAndLeavesOneFloatEach()
        {
            var tint = new UMAColorProperty { name = "_BaseColor", Value = new Color(0.1f, 0.2f, 0.3f, 1f) };
            var table = Table(
                MakeColor("Has", new UMAFloatProperty { name = "_Smoothness", Value = 0.5f }, tint),
                MakeColor("None"),
                MakeColor("Duplicates", new UMAFloatProperty { name = "_Smoothness", Value = 0.1f }, new UMAFloatProperty { name = "_Smoothness", Value = 0.9f }),
                MakeColor("WrongType", new UMAIntProperty { name = "_Smoothness", Value = 1 }));
            var edit = new ColorParameterEdit { parameterName = "_Smoothness", kind = ColorParameterKind.Float, source = ColorParameterValueSource.FixedValue, floatValue = 0.3f };

            int changed = SharedColorParameterEditor.Apply(new[] { table }, edit, material);

            Assert.That(changed, Is.EqualTo(4));
            foreach (OverlayColorData color in table.colors) Assert.That(SingleFloat(color, "_Smoothness"), Is.EqualTo(0.3f));
            Assert.That(Named(table.colors[0], "_BaseColor"), Is.EqualTo(new List<UMAProperty> { tint }), "Other parameters stay as they were.");
        }

        [Test]
        public void ScaledParameterMultipliesExistingValueAndUsesMaterialValueWhenMissing()
        {
            var table = Table(MakeColor("Has", new UMAFloatProperty { name = "_Smoothness", Value = 0.5f }), MakeColor("None"));
            var edit = new ColorParameterEdit
            {
                parameterName = "_Smoothness", kind = ColorParameterKind.Float, source = ColorParameterValueSource.ScaledParameter,
                sourceParameter = "_Smoothness", multiplier = 1.2f, whenSourceMissing = MissingSourceAction.UseMaterialValue
            };

            SharedColorParameterEditor.Apply(new[] { table }, edit, material);

            Assert.That(SingleFloat(table.colors[0], "_Smoothness"), Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(SingleFloat(table.colors[1], "_Smoothness"), Is.EqualTo(0.3f).Within(1e-5f), "The material's 0.25, times 1.2.");
        }

        [Test]
        public void MissingSourceCanUseTheFixedValueOrSkipTheColor()
        {
            var fixedTable = Table(MakeColor("Has", new UMAFloatProperty { name = "_Smoothness", Value = 0.5f }), MakeColor("None"));
            var edit = new ColorParameterEdit
            {
                parameterName = "_Smoothness", kind = ColorParameterKind.Float, source = ColorParameterValueSource.ScaledParameter,
                sourceParameter = "_Smoothness", multiplier = 2f, floatValue = 0.7f, whenSourceMissing = MissingSourceAction.UseFixedValue
            };
            SharedColorParameterEditor.Apply(new[] { fixedTable }, edit, material);
            Assert.That(SingleFloat(fixedTable.colors[0], "_Smoothness"), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(SingleFloat(fixedTable.colors[1], "_Smoothness"), Is.EqualTo(0.7f), "The fixed value is not multiplied.");

            var skipTable = Table(MakeColor("Has", new UMAFloatProperty { name = "_Smoothness", Value = 0.5f }), MakeColor("None"));
            edit.whenSourceMissing = MissingSourceAction.SkipColor;
            int changed = SharedColorParameterEditor.Apply(new[] { skipTable }, edit, material);
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(Named(skipTable.colors[1], "_Smoothness"), Is.Empty, "A skipped color gets no value.");
        }

        [Test]
        public void ColorFromAnotherParameterIsScaledAndKeepsAlpha()
        {
            var table = Table(MakeColor("Has", new UMAColorProperty { name = "_BaseColor", Value = new Color(0.4f, 0.6f, 0.8f, 0.5f) }));
            var edit = new ColorParameterEdit
            {
                parameterName = "_RootColor", kind = ColorParameterKind.Color, source = ColorParameterValueSource.ScaledParameter,
                sourceParameter = "_BaseColor", multiplier = 0.5f
            };

            SharedColorParameterEditor.Apply(new[] { table }, edit, material);

            List<UMAProperty> root = Named(table.colors[0], "_RootColor");
            Assert.That(root.Count, Is.EqualTo(1));
            Color value = ((UMAColorProperty)root[0]).Value;
            Assert.That(new Vector4(value.r, value.g, value.b, value.a), Is.EqualTo(new Vector4(0.2f, 0.3f, 0.4f, 0.5f)));
            Assert.That(Named(table.colors[0], "_BaseColor").Count, Is.EqualTo(1), "The source stays.");
        }

        [Test]
        public void IncompatibleSourceIsSkippedWithAReason()
        {
            var table = Table(MakeColor("Tinted", new UMAColorProperty { name = "_BaseColor", Value = UnityEngine.Color.red }));
            var edit = new ColorParameterEdit
            {
                parameterName = "_Smoothness", kind = ColorParameterKind.Float, source = ColorParameterValueSource.ScaledParameter,
                sourceParameter = "_BaseColor", multiplier = 1f
            };

            List<ColorParameterChange> preview = SharedColorParameterEditor.Preview(new[] { table }, edit, material);

            Assert.That(preview.Count, Is.EqualTo(1));
            Assert.That(preview[0].Skipped, Is.True);
            StringAssert.Contains("Color", preview[0].Note);
            Assert.That(SharedColorParameterEditor.Apply(new[] { table }, edit, material), Is.EqualTo(0));
            Assert.That(Named(table.colors[0], "_Smoothness"), Is.Empty);
        }

        [Test]
        public void PreviewChangesNothingAndASecondApplyFindsNothingToDo()
        {
            var table = Table(MakeColor("Has", new UMAFloatProperty { name = "_Smoothness", Value = 0.5f }), MakeColor("None"));
            var edit = new ColorParameterEdit { parameterName = "_Smoothness", kind = ColorParameterKind.Float, source = ColorParameterValueSource.FixedValue, floatValue = 0.3f };

            List<ColorParameterChange> preview = SharedColorParameterEditor.Preview(new[] { table }, edit, material);
            Assert.That(preview.Count, Is.EqualTo(2));
            Assert.That(preview[0].Before, Is.EqualTo("0.5"));
            Assert.That(preview[1].Before, Is.EqualTo("(none)"));
            Assert.That(SingleFloat(table.colors[0], "_Smoothness"), Is.EqualTo(0.5f));
            Assert.That(Named(table.colors[1], "_Smoothness"), Is.Empty);

            Assert.That(SharedColorParameterEditor.Apply(new[] { table }, edit, material), Is.EqualTo(2));
            Assert.That(SharedColorParameterEditor.Apply(new[] { table }, edit, material), Is.EqualTo(0));
        }

        [Test]
        public void ApplyCanBeUndone()
        {
            var table = Table(MakeColor("Has", new UMAFloatProperty { name = "_Smoothness", Value = 0.5f }));
            var edit = new ColorParameterEdit { parameterName = "_Smoothness", kind = ColorParameterKind.Float, source = ColorParameterValueSource.FixedValue, floatValue = 0.3f };

            Undo.IncrementCurrentGroup();
            SharedColorParameterEditor.Apply(new[] { table }, edit, material);
            Assert.That(SingleFloat(table.colors[0], "_Smoothness"), Is.EqualTo(0.3f));
            Undo.PerformUndo();

            Assert.That(SingleFloat(table.colors[0], "_Smoothness"), Is.EqualTo(0.5f));
        }

        [Test]
        public void MaterialValuesAreReadByShaderPropertyType()
        {
            Assert.That(SharedColorParameterEditor.TryGetMaterialValue(material, "_Smoothness", out ColorParameterKind kind, out Vector4 value), Is.True);
            Assert.That(kind, Is.EqualTo(ColorParameterKind.Float));
            Assert.That(value.x, Is.EqualTo(0.25f));
            Assert.That(SharedColorParameterEditor.TryGetMaterialValue(material, "_BaseColor", out kind, out value), Is.True);
            Assert.That(kind, Is.EqualTo(ColorParameterKind.Color));
            Assert.That(SharedColorParameterEditor.TryGetMaterialValue(material, "_NotAProperty", out _, out _), Is.False);
            Assert.That(SharedColorParameterEditor.TryGetMaterialValue(material, "_BaseMap", out _, out _), Is.False, "Textures are not color parameters.");
        }
    }
}
