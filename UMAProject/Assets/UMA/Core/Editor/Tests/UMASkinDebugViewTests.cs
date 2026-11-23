#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public class UMASkinDebugViewTests
    {
        const string SkinFolder = "Assets/UMA/SRP/Shaders/Skin/";

        static Shader Production => Shader.Find(UMARealisticSkinUtility.SSSShaderName);
        static Shader Debug => Shader.Find(UMASkinDebugView.DebugShaderName);

        // "DetailUVChecker" -> "DETAIL_UV_CHECKER"
        static string DefineName(UMASkinDebugMap view)
        {
            string name = Regex.Replace(view.ToString(), "([a-z0-9])([A-Z])", "$1_$2");
            name = Regex.Replace(name, "([A-Z]+)([A-Z][a-z])", "$1_$2");
            return "SKIN_DEBUG_" + name.ToUpperInvariant();
        }

        [Test]
        public void DebugShaderHasEveryInputOfTheProductionShader()
        {
            Assert.That(Production, Is.Not.Null);
            Assert.That(Debug, Is.Not.Null);
            for (int i = 0; i < Production.GetPropertyCount(); i++)
            {
                string name = Production.GetPropertyName(i);
                int index = Debug.FindPropertyIndex(name);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), name);
                var type = Production.GetPropertyType(i);
                Assert.That(Debug.GetPropertyType(index), Is.EqualTo(type), name);
                Assert.That(Debug.GetPropertyDescription(index), Is.EqualTo(Production.GetPropertyDescription(i)), name);
                if (type == ShaderPropertyType.Range)
                {
                    Assert.That(Debug.GetPropertyRangeLimits(index), Is.EqualTo(Production.GetPropertyRangeLimits(i)), name);
                    Assert.That(Debug.GetPropertyDefaultFloatValue(index), Is.EqualTo(Production.GetPropertyDefaultFloatValue(i)), name);
                }
                else if (type == ShaderPropertyType.Float)
                    Assert.That(Debug.GetPropertyDefaultFloatValue(index), Is.EqualTo(Production.GetPropertyDefaultFloatValue(i)), name);
                else if (type == ShaderPropertyType.Vector || type == ShaderPropertyType.Color)
                    Assert.That(Debug.GetPropertyDefaultVectorValue(index), Is.EqualTo(Production.GetPropertyDefaultVectorValue(i)), name);
            }
            // The only addition is the view selector, and it starts on normal rendering.
            Assert.That(Debug.GetPropertyCount(), Is.EqualTo(Production.GetPropertyCount() + 1));
            int view = Debug.FindPropertyIndex(UMASkinDebugView.ViewProperty);
            Assert.That(view, Is.GreaterThanOrEqualTo(0));
            Assert.That(Debug.GetPropertyDefaultFloatValue(view), Is.Zero);
            Assert.That(Production.FindPropertyIndex(UMASkinDebugView.ViewProperty), Is.LessThan(0));
        }

        [Test]
        public void ProductionShaderNeverCompilesTheDebugCode()
        {
            string production = File.ReadAllText(SkinFolder + "UMA3SkinRealisticSSS.shader");
            string debug = File.ReadAllText(SkinFolder + "UMA3SkinRealisticSSSDebug.shader");
            Assert.That(production, Does.Not.Contain("UMA_SKIN_DEBUG_MAPS"));
            Assert.That(debug, Does.Contain("#define UMA_SKIN_DEBUG_MAPS 1"));
            // Every use of the selector in the shared code sits inside the debug-only blocks.
            foreach (string file in new[] { "UMA3SkinSSSInput.hlsl", "UMA3SkinSSSLighting.hlsl" })
            {
                int depth = 0, line = 0;
                foreach (string text in File.ReadAllLines(SkinFolder + file))
                {
                    line++;
                    string trimmed = text.Trim();
                    if (trimmed.StartsWith("#ifdef UMA_SKIN_DEBUG_MAPS", StringComparison.Ordinal)) { depth = 1; continue; }
                    if (depth > 0 && trimmed.StartsWith("#if", StringComparison.Ordinal)) depth++;
                    else if (depth > 0 && trimmed.StartsWith("#endif", StringComparison.Ordinal)) depth--;
                    else if (depth == 0)
                        Assert.That(text.Contains("SkinDebugIs") || text.Contains("_SkinDebugMap") || text.Contains("SKIN_DEBUG_"), Is.False, file + ":" + line);
                }
            }
        }

        [Test]
        public void EveryViewHasItsNumberInTheShader()
        {
            string input = File.ReadAllText(SkinFolder + "UMA3SkinSSSInput.hlsl");
            var views = (UMASkinDebugMap[])Enum.GetValues(typeof(UMASkinDebugMap));
            Assert.That(views.Select(v => (int)v), Is.EqualTo(Enumerable.Range(0, views.Length)), "views are numbered without gaps");
            foreach (var view in views)
            {
                Assert.That(Regex.IsMatch(input, @"#define\s+" + DefineName(view) + @"\s+" + (int)view + @"\s"), Is.True, DefineName(view) + " " + (int)view);
                Assert.That(UMASkinDebugView.GetMenuPath(view), Is.Not.EqualTo(view.ToString()), view + " has a menu label");
            }
            Assert.That(Regex.Matches(input, @"#define\s+SKIN_DEBUG_[A-Z_]+\s+\d+").Count, Is.EqualTo(views.Length));
            Assert.That(views.Select(UMASkinDebugView.GetMenuPath).Distinct().Count(), Is.EqualTo(views.Length));
        }

        [Test]
        public void ApplyMovesSceneInstancesToTheDebugShaderAndBack()
        {
            if (Production == null || Debug == null) Assert.Ignore("The Realistic Skin SSS shaders are not installed.");
            // A plain scene object: objects flagged DontSave are not found by scene searches, as intended.
            var holder = GameObject.CreatePrimitive(PrimitiveType.Quad);
            holder.name = "Skin debug view test quad";
            var instance = new Material(Production) { name = "Skin debug view test instance" };
            var other = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Hidden/InternalErrorShader"));
            var asset = AssetDatabase.LoadAssetAtPath<Material>(SkinFolder + "Examples/UMA3_SkinRealisticSSS_Natural.mat");
            var before = UMASkinDebugView.Current;
            try
            {
                instance.SetFloat("_ScatterRadiusMM", 3.25f);
                Shader otherShader = other.shader;
                holder.GetComponent<MeshRenderer>().sharedMaterials = asset != null ? new[] { instance, other, asset } : new[] { instance, other };

                Assert.That(UMASkinDebugView.Apply(UMASkinDebugMap.MaskOcclusion), Is.GreaterThanOrEqualTo(1));
                Assert.That(instance.shader, Is.EqualTo(Debug));
                Assert.That(instance.GetFloat(UMASkinDebugView.ViewProperty), Is.EqualTo((float)UMASkinDebugMap.MaskOcclusion));
                Assert.That(instance.GetFloat("_ScatterRadiusMM"), Is.EqualTo(3.25f), "inputs survive the switch");
                Assert.That(other.shader, Is.EqualTo(otherShader), "other shaders are left alone");
                if (asset != null)
                {
                    Assert.That(asset.shader, Is.EqualTo(Production), "material assets are never switched");
                    Assert.That(EditorUtility.IsDirty(asset), Is.False);
                    Assert.That(UMASkinDebugView.SkippedAssetCount, Is.GreaterThanOrEqualTo(1));
                }

                UMASkinDebugView.Apply(UMASkinDebugMap.NormalMap);
                Assert.That(instance.GetFloat(UMASkinDebugView.ViewProperty), Is.EqualTo((float)UMASkinDebugMap.NormalMap));

                UMASkinDebugView.Apply(UMASkinDebugMap.Lit);
                Assert.That(instance.shader, Is.EqualTo(Production));
                Assert.That(instance.GetFloat("_ScatterRadiusMM"), Is.EqualTo(3.25f));
                Assert.That(UMASkinDebugView.ActiveMaterialCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(other);
                UMASkinDebugView.Apply(before);
            }
        }

        [Test]
        public void DebugShaderPassesCompile()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device: do not pass -nographics.");
            if (Debug == null) Assert.Ignore("The debug shader is not installed.");
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var material = new Material(Debug);
            try
            {
                foreach (string pass in new[] { "ForwardSkin", "ShadowCaster", "DepthOnly", "DepthNormals", "Meta", "MotionVectors", "XRMotionVectors" })
                {
                    int index = material.FindPass(pass);
                    Assert.That(index, Is.GreaterThanOrEqualTo(0), pass);
                    Assert.That(material.SetPass(index), Is.True, pass);
                }
                Assert.That(ShaderUtil.GetShaderMessages(Debug).Where(m => m.severity.ToString() == "Error").Select(m => m.message), Is.Empty);
                Assert.That(ShaderUtil.GetShaderMessages(Production).Where(m => m.severity.ToString() == "Error").Select(m => m.message), Is.Empty);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = oldAsync;
                Object.DestroyImmediate(material);
            }
        }
    }
}
#endif
