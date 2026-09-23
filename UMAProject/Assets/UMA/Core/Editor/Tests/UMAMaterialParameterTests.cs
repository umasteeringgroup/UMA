#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    public class UMAMaterialParameterTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private UMAData data;
        private UMAMaterial definition;
        private OverlayDataAsset overlayAsset;

        [SetUp] public void Setup()
        {
            var go = new GameObject("Material parameter test"); objects.Add(go);
            data = go.AddComponent<UMAData>(); data.umaRecipe = new UMAData.UMARecipe();
            definition = ScriptableObject.CreateInstance<UMAMaterial>(); objects.Add(definition);
            definition.material = new Material(Shader.Find("UMA/Atlas/AtlasDiffuseShader")); objects.Add(definition.material);
            overlayAsset = ScriptableObject.CreateInstance<OverlayDataAsset>(); objects.Add(overlayAsset);
            overlayAsset.material = definition;
        }

        private OverlayData Overlay(params UMAProperty[] properties) => new OverlayData(overlayAsset) {
            colorData = new OverlayColorData(1) { PropertyBlock = new UMAMaterialPropertyBlock {
                shaderProperties = new List<UMAProperty>(properties) } } };

        private UMAData.GeneratedMaterial Create(params OverlayData[] overlays)
        {
            var gm = new UMAData.GeneratedMaterial { umaMaterial = definition, material = UnityEngine.Object.Instantiate(definition.material) };
            objects.Add(gm.material);
            gm.materialFragments.Add(new UMAData.MaterialFragment { overlayData = overlays });
            return gm;
        }

        // Reference is the original ordered virtual setter path, independent of the fast path.
        private void ReferenceApply(UMAData.GeneratedMaterial gm, Material material)
        {
            foreach (var fragment in gm.materialFragments)
                for (int oi = 0; oi < fragment.overlayData.Length; oi++)
                {
                    var overlay = fragment.overlayData[oi];
                    foreach (var property in overlay.colorData.PropertyBlock.shaderProperties)
                    {
                        property.Apply(material, -1);
                        if (material.HasProperty("_OverlayCount"))
                            property.Apply(material, oi);
                    }
                }
            if (definition.shaderParms == null || data.umaRecipe.sharedColors == null) return;
            foreach (var mapping in definition.shaderParms)
                if (material.HasProperty(mapping.ParameterName))
                    foreach (var color in data.umaRecipe.sharedColors)
                        if (color.name == mapping.ColorName) { material.SetColor(mapping.ParameterName, color.color); break; }
        }

        [TestCase(false)] [TestCase(true)]
        public void OrderedWritesAcrossFragmentsAndMappingsMatchOriginal(bool mapped)
        {
            var a = Overlay(new UMAColorProperty { name = "_Color", Value = Color.red });
            var b = Overlay(new UMAColorProperty { name = "_Color", Value = Color.blue },
                new UMAFloatProperty { name = "_Absent", Value = 7 });
            var gm = Create(a, b);
            gm.materialFragments.Add(new UMAData.MaterialFragment { overlayData = new[] { a } });
            if (mapped)
            {
                definition.shaderParms = new[] { new UMAMaterial.ShaderParms { ParameterName = "_Color", ColorName = "Skin" },
                    new UMAMaterial.ShaderParms { ParameterName = "_Color", ColorName = "Missing" } };
                data.umaRecipe.sharedColors = new[] { new OverlayColorData(1) { name = "Skin", color = Color.yellow } };
            }
            var reference = UnityEngine.Object.Instantiate(definition.material); objects.Add(reference);
            ReferenceApply(gm, reference);
            UMAGeneratorPro.ApplyMaterialParameters(gm, data, gm.material);
            Assert.That(gm.material.GetColor("_Color"), Is.EqualTo(reference.GetColor("_Color")));
            Assert.That(gm.material.GetTexture("_MainTex"), Is.SameAs(reference.GetTexture("_MainTex")));
            a.colorData.PropertyBlock.shaderProperties[0] = new UMAColorProperty { name = "_Color", Value = Color.green };
            ReferenceApply(gm, reference);
            UMAGeneratorPro.ApplyMaterialParameters(gm, data, gm.material);
            Assert.That(gm.material.GetColor("_Color"), Is.EqualTo(reference.GetColor("_Color")), "Direct runtime value edits must be seen immediately.");
        }

        public class CustomProperty : UMAColorProperty
        {
            public int Calls;
            public override void Apply(Material material, int overlayNumber) { Calls++; base.Apply(material, overlayNumber); }
        }

        [Test] public void CustomPropertiesKeepTheirOriginalOrderAndCalls()
        {
            var custom = new CustomProperty { name = "_Color", Value = Color.blue };
            var gm = Create(Overlay(custom, new UMAColorProperty { name = "_Color", Value = Color.red }));
            UMAGeneratorPro.ApplyMaterialParameters(gm, data, gm.material);
            UMAGeneratorPro.ApplyMaterialParameters(gm, data, gm.material);
            Assert.That(custom.Calls, Is.EqualTo(2)); Assert.That(gm.material.GetColor("_Color"), Is.EqualTo(Color.red));
        }

        [Test] public void CompositorScalarWritesAndIndexedNameCollisionsMatchOriginal()
        {
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset(@"Shader ""Hidden/UMA/ParameterTest"" {
                Properties {
                    _OverlayCount (""Count"", Float) = 2
                    _Color (""Color"", Color) = (1,1,1,1)
                    _Color0 (""Color0"", Color) = (1,1,1,1)
                    _Color1 (""Color1"", Color) = (1,1,1,1)
                    _Number (""Number"", Float) = 0
                    _Number0 (""Number0"", Float) = 0
                    _Number1 (""Number1"", Float) = 0
                    _Direction (""Direction"", Vector) = (0,0,0,0)
                    _Direction0 (""Direction0"", Vector) = (0,0,0,0)
                    _Direction1 (""Direction1"", Vector) = (0,0,0,0)
                }
                SubShader { Pass {} }
            }", false);
            objects.Add(shader);
            definition.material = new Material(shader); objects.Add(definition.material);
            var first = Overlay(new UMAColorProperty { name = "_Color", Value = Color.red },
                new UMAFloatProperty { name = "_Number", Value = 1.5f },
                new UMAVectorProperty { name = "_Direction", Value = Vector4.one });
            var second = Overlay(new UMAColorProperty { name = "_Color0", Value = Color.green },
                new UMAColorProperty { name = "_Color", Value = Color.blue },
                new UMAIntProperty { name = "_Number", Value = 9 },
                new UMAVectorProperty { name = "_Direction", Value = new Vector4(2,3,4,5) });
            var gm = Create(first, second);
            gm.materialFragments.Add(new UMAData.MaterialFragment { overlayData = new[] { first, first } });
            var reference = UnityEngine.Object.Instantiate(definition.material); objects.Add(reference);
            ReferenceApply(gm, reference);
            UMAGeneratorPro.ApplyMaterialParameters(gm, data, gm.material);
            foreach (var suffix in new[] { "", "0", "1" })
            {
                Assert.That(gm.material.GetColor("_Color" + suffix), Is.EqualTo(reference.GetColor("_Color" + suffix)));
                Assert.That(gm.material.GetFloat("_Number" + suffix), Is.EqualTo(reference.GetFloat("_Number" + suffix)));
                Assert.That(gm.material.GetVector("_Direction" + suffix), Is.EqualTo(reference.GetVector("_Direction" + suffix)));
            }
        }

        [Test] public void MaterialSignatureDistinguishesArrayValuesAndPreservesCustomFallback()
        {
            var method = typeof(UMAResourceReuse).GetMethod("DescribeMaterialInputs", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var type = typeof(UMAResourceReuse).GetNestedType("AtlasSignature", System.Reflection.BindingFlags.NonPublic);
            var signature = System.Activator.CreateInstance(type, true);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var reset = type.GetMethod("Reset", flags); var keyMethod = type.GetMethod("Key", flags);
            var freeze = typeof(UMAGeneratedResourceKey).GetMethod("Freeze", flags);
            var property = new UMAFloatArrayProperty { name = "_Array", Value = new[] { 1f, 2f } };
            var gm = Create(Overlay(property));
            UMAGeneratedResourceKey Capture()
            {
                reset.Invoke(signature, null); method.Invoke(null, new object[] { signature, gm });
                return (UMAGeneratedResourceKey)freeze.Invoke(keyMethod.Invoke(signature, new object[] { "test" }), null);
            }
            var before = Capture(); property.Value[1] = 3;
            Assert.That(before.Equals(Capture()), Is.False);
            property.Value[1] = 2; Assert.That(before.Equals(Capture()), Is.True);
            gm.materialFragments[0].overlayData[0].colorData.PropertyBlock.shaderProperties[0] = new CustomProperty();
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Capture());
            Assert.That(error.InnerException, Is.TypeOf<System.NotSupportedException>(), "Unknown property implementations must not share their final material.");
        }

        [TearDown] public void Cleanup()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }
    }
}
#endif
