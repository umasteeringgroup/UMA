using System.Reflection;
using NUnit.Framework;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class OverlayAssetPreviewTests
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void MissingShaderFeatureKeywordIsIgnoredWithoutLogging()
        {
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var messages = new System.Collections.Generic.List<string>();
            var diagnostics = new System.Collections.Generic.List<string>();
            Application.LogCallback capture = (message, stack, type) => messages.Add(message);
            Application.logMessageReceived += capture;
            try
            {
                var enable = typeof(OverlayDataAssetInspector).GetMethod("EnablePreviewKeyword", Static);
                enable.Invoke(null, new object[] { material, "UMA_PREVIEW_UNDECLARED_KEYWORD", diagnostics, null });
                enable.Invoke(null, new object[] { material, "UMA_PREVIEW_UNDECLARED_KEYWORD", diagnostics, null });
                enable.Invoke(null, new object[] { material, "UMA_PREVIEW_ANOTHER_MISSING_KEYWORD", diagnostics, null });
                Assert.That(material.IsKeywordEnabled("UMA_PREVIEW_UNDECLARED_KEYWORD"), Is.False);
                Assert.That(messages, Is.Empty);
                CollectionAssert.AreEqual(new[] {
                    "Local keyword UMA_PREVIEW_UNDECLARED_KEYWORD doesn't exist in the shader.",
                    "Local keyword UMA_PREVIEW_ANOTHER_MISSING_KEYWORD doesn't exist in the shader."
                }, diagnostics);
            }
            finally { Application.logMessageReceived -= capture; Object.DestroyImmediate(material); }
        }

        [Test]
        public void CubeHasFullTextureCoordinatesAndOutwardTangentFramesOnEveryFace()
        {
            var mesh = (Mesh)typeof(OverlayDataAssetInspector).GetMethod("CreateOverlayPreviewCube", Static).Invoke(null, null);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(24));
                Assert.That(mesh.triangles.Length, Is.EqualTo(36));
                var expectedUV = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                for (int face = 0; face < 6; face++)
                {
                    for (int corner = 0; corner < 4; corner++)
                    {
                        int index = face * 4 + corner;
                        Assert.That(mesh.uv[index], Is.EqualTo(expectedUV[corner]));
                        Assert.That(mesh.uv2[index], Is.EqualTo(expectedUV[corner]));
                        Assert.That(Vector3.Dot(mesh.normals[index], (Vector3)mesh.tangents[index]), Is.EqualTo(0).Within(0.0001f));
                        Assert.That(mesh.tangents[index].w, Is.EqualTo(1));
                    }
                    int t = face * 6;
                    var a = mesh.vertices[mesh.triangles[t]];
                    var b = mesh.vertices[mesh.triangles[t + 1]];
                    var c = mesh.vertices[mesh.triangles[t + 2]];
                    Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a).normalized, mesh.normals[face * 4]), Is.GreaterThan(0.99f));
                }
                Assert.That(mesh.bounds.size, Is.EqualTo(Vector3.one));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void PreviewBindsChannelsAndRefreshesWithoutChangingSourceAssets()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var uma = ScriptableObject.CreateInstance<UMAMaterial>();
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            Material preview = null;
            var method = typeof(OverlayDataAssetInspector).GetMethod("UpdateOverlayPreviewMaterial", Static);
            try
            {
                string baseProperty = source.HasTexture("_BaseMap") ? "_BaseMap" : "_MainTex";
                source.SetTexture(baseProperty, Texture2D.grayTexture);
                source.SetTextureScale(baseProperty, new Vector2(2, 3));
                uma.material = source;
                uma.channels = new[] {
                    new UMAMaterial.MaterialChannel { materialPropertyName = baseProperty },
                    new UMAMaterial.MaterialChannel { materialPropertyName = "_BumpMap", channelType = UMAMaterial.ChannelType.NormalMap },
                    new UMAMaterial.MaterialChannel { materialPropertyName = baseProperty, NonShaderTexture = true },
                    new UMAMaterial.MaterialChannel { materialPropertyName = "_DoesNotExist" },
                    new UMAMaterial.MaterialChannel { materialPropertyName = "_Color", channelType = UMAMaterial.ChannelType.MaterialColor }
                };
                overlay.material = uma;
                overlay.textureList = new Texture[] { Texture2D.whiteTexture, Texture2D.normalTexture, Texture2D.blackTexture, Texture2D.blackTexture, Texture2D.blackTexture };
                string sourceBefore = EditorJsonUtility.ToJson(source), umaBefore = EditorJsonUtility.ToJson(uma), overlayBefore = EditorJsonUtility.ToJson(overlay);
                var diagnostics = new System.Collections.Generic.List<string>();
                object[] args = { preview, source, overlay, diagnostics };
                method.Invoke(null, args); preview = (Material)args[0];
                Assert.That(preview, Is.Not.SameAs(source));
                Assert.That(preview.shader, Is.SameAs(source.shader));
                Assert.That(preview.GetTexture(baseProperty), Is.SameAs(Texture2D.whiteTexture));
                Assert.That(preview.GetTexture("_BumpMap"), Is.SameAs(Texture2D.normalTexture));
                Assert.That(preview.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(preview.GetTextureScale(baseProperty), Is.EqualTo(new Vector2(2, 3)));
                CollectionAssert.Contains(diagnostics, "Texture parameter _DoesNotExist doesn't exist in the shader.");
                Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(sourceBefore));
                Assert.That(EditorJsonUtility.ToJson(uma), Is.EqualTo(umaBefore));
                Assert.That(EditorJsonUtility.ToJson(overlay), Is.EqualTo(overlayBefore));
                overlay.textureList[0] = Texture2D.blackTexture;
                overlay.textureList[1] = null;
                args[0] = preview; method.Invoke(null, args);
                Assert.That(args[0], Is.SameAs(preview));
                Assert.That(preview.GetTexture(baseProperty), Is.SameAs(Texture2D.blackTexture));
                Assert.That(preview.GetTexture("_BumpMap"), Is.Null);
                args[1] = null; method.Invoke(null, args);
                Assert.That(args[0], Is.Null);
                Assert.That(preview == null, Is.True, "Removing a pass must destroy its temporary material.");
                preview = null;
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                Object.DestroyImmediate(overlay); Object.DestroyImmediate(uma); Object.DestroyImmediate(source);
            }
        }
    }
}
