#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Tests
{
    public sealed partial class PluginApiV2Tests
    {
        private sealed class QuiltSheets : IDisposable
        {
            private readonly string folder = "Assets/QuiltSpriteTest_" + Guid.NewGuid().ToString("N");
            public readonly OverlayPainterSpriteSet asset;
            public QuiltSheets()
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                asset = ScriptableObject.CreateInstance<OverlayPainterSpriteSet>();
                AssetDatabase.CreateAsset(asset, folder + "/Set.asset");
            }
            public void Add(TexturePaintChannel channel, Color[] colors, bool inverted = false)
            {
                var texture = new Texture2D(colors.Length * 4, 4, TextureFormat.RGBA32, false, true);
                var pixels = new Color[texture.width * texture.height];
                for (int y = 0; y < 4; y++) for (int x = 0; x < texture.width; x++) pixels[y * texture.width + x] = colors[x / 4];
                texture.SetPixels(pixels); texture.Apply();
                string path = folder + "/" + channel + ".asset";
                AssetDatabase.CreateAsset(texture, path);
                // Reverse creation order verifies that channels use numeric tile order, not subasset order.
                for (int i = colors.Length - 1; i >= 0; i--)
                {
                    var sprite = Sprite.Create(texture, new Rect(i * 4, 0, 4, 4), Vector2.one * .5f);
                    sprite.name = channel + "_" + i;
                    AssetDatabase.AddObjectToAsset(sprite, texture);
                }
                asset.spriteSheets.Add(new OverlayPainterSpriteSheet { channel = channel, spriteSheet = texture, inverted = inverted });
                EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets();
            }
            public void Dispose() => AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public async Task QuiltSpriteSetCapturesAllChannelsAndUsesOnlyEnabledTile()
        {
            using var sheets = new QuiltSheets();
            sheets.Add(TexturePaintChannel.Albedo, new[] { Color.red, Color.green, Color.blue });
            sheets.Add(TexturePaintChannel.Roughness, new[] { Color.gray, Color.white, new Color(.8f,.8f,.8f,1) }, true);
            sheets.Add(TexturePaintChannel.Metallic, new[] { Color.black, Color.black, new Color(.7f,.7f,.7f,1) });
            sheets.Add(TexturePaintChannel.Normal, new[] { new Color(.5f,.5f,1,1), new Color(.5f,.5f,1,1), new Color(.8f,.5f,.9f,1) });
            sheets.Add(TexturePaintChannel.NormalControl, new[] { Color.gray, Color.gray, Color.gray });
            sheets.Add(TexturePaintChannel.AmbientOcclusion, new[] { Color.white, Color.white, new Color(.6f,.6f,.6f,1) });
            sheets.Add(TexturePaintChannel.Custom, new[] { Color.red, Color.green, new Color(.2f,.4f,.6f,1) });
            sheets.Add(TexturePaintChannel.SkinColorMask, new[] { Color.red, Color.green, new Color(.2f,.4f,.6f,1) });
            sheets.Add(TexturePaintChannel.Thickness, new[] { Color.black, Color.black, new Color(.3f,.3f,.3f,1) });
            sheets.Add(TexturePaintChannel.DetailMask, new[] { Color.black, Color.black, new Color(.9f,.9f,.9f,1) });
            sheets.Add(TexturePaintChannel.Emission, new[] { Color.red, Color.green, new Color(.2f,.4f,.6f,1) });
            set.channels.Add(TexturePaintChannel.Emission, MakeChannel(TexturePaintChannel.Emission, true, Color.black));
            var plugin = ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            try
            {
                var p = QuiltPuffParameters(plugin, 0, 4);
                p.Get("colorAmount").number = 0; p.Get("shadingStrength").number = 0;
                p.Get("outputMetallic").boolean = true;
                var input = p.Get("quiltSpriteSet"); input.spriteSet = sheets.asset;
                input.spriteSetSelectionExplicit = true; input.enabledSpriteIndices.Add(2);
                await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                var layer = set.layers[0];
                Color Pixel(TexturePaintChannel c) => Read(layer.channels[c].Front, 2, 2);
                Assert.That(layer.channels.Count, Is.EqualTo(11));
                Assert.That(Pixel(TexturePaintChannel.Albedo).b, Is.GreaterThan(.98f));
                Assert.That(Pixel(TexturePaintChannel.Albedo).r, Is.LessThan(.02f));
                Assert.That(Pixel(TexturePaintChannel.Roughness).r, Is.EqualTo(.2f).Within(.015f), "Invert smoothness into roughness.");
                Assert.That(Pixel(TexturePaintChannel.Metallic).r, Is.EqualTo(.7f).Within(.015f));
                Assert.That(Pixel(TexturePaintChannel.Normal).r, Is.EqualTo(.8f).Within(.015f));
                Assert.That(Pixel(TexturePaintChannel.NormalControl).r, Is.GreaterThan(.54f), "Neutral tile height must retain the quilt puff.");
                Assert.That(Pixel(TexturePaintChannel.AmbientOcclusion).r, Is.EqualTo(.6f).Within(.015f));
                foreach (var c in new[] { TexturePaintChannel.Custom, TexturePaintChannel.SkinColorMask, TexturePaintChannel.Emission })
                {
                    var pixel = Pixel(c);
                    Assert.That(pixel.r, Is.EqualTo(.2f).Within(.015f), c.ToString());
                    Assert.That(pixel.g, Is.EqualTo(.4f).Within(.015f), c.ToString());
                    Assert.That(pixel.b, Is.EqualTo(.6f).Within(.015f), c.ToString());
                }
                Assert.That(Pixel(TexturePaintChannel.Thickness).r, Is.EqualTo(.3f).Within(.015f));
                Assert.That(Pixel(TexturePaintChannel.DetailMask).r, Is.EqualTo(.9f).Within(.015f));
            }
            finally { Object.DestroyImmediate(plugin); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void QuiltSpriteSetRandomChoiceStaysWithinEnabledTilesAndAlignsChannels(int pattern)
        {
            var plugin = ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            try
            {
                var p = QuiltPuffParameters(plugin, pattern, 4);
                var tiles = new Dictionary<TexturePaintChannel, Dictionary<int, TexturePaintReadOnlyParameterTexture>>();
                foreach (var channel in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Roughness })
                    tiles[channel] = new Dictionary<int, TexturePaintReadOnlyParameterTexture>
                    {
                        [0] = new TexturePaintReadOnlyParameterTexture("tile", 1, 1, false, new[] { channel == TexturePaintChannel.Albedo ? Color.red : new Color(.2f,.2f,.2f,1) }),
                        [2] = new TexturePaintReadOnlyParameterTexture("tile", 1, 1, false, new[] { channel == TexturePaintChannel.Albedo ? Color.blue : new Color(.8f,.8f,.8f,1) })
                    };
                var source = new TexturePaintReadOnlySpriteSet(3, new[] { 0, 2 }, tiles);
                Type engine = typeof(TextileSurfaceGeneratorPlugin).Assembly.GetType("UMA.TexturePaint.Examples.TextileSurfaceEngine", true);
                var settings = engine.GetNestedType("Settings", BindingFlags.NonPublic).GetConstructor(new[] { typeof(TexturePaintPluginParameterSet) }).Invoke(new object[] { p });
                var sample = engine.GetMethod("SpriteSetPanel", BindingFlags.Static | BindingFlags.NonPublic);
                Color ReadPanel(Vector2 q, TexturePaintChannel channel)
                {
                    Vector2 uv = pattern == 1 ? new Vector2(q.x-q.y,q.x+q.y)*.70710678f : pattern == 2 ? new Vector2(q.x*.5f-q.y,q.x*.5f+q.y) : q;
                    return (Color)sample.Invoke(null, new object[] { settings, source, uv, channel, Color.black, 1f, 1f });
                }
                int red = 0, blue = 0;
                for (int x = -4; x < 4; x++) for (int y = -4; y < 4; y++)
                {
                    var q = new Vector2(x+.5f,y+.5f);
                    var color = ReadPanel(q, TexturePaintChannel.Albedo);
                    if (color.r > .9f) red++; else { Assert.That(color.b, Is.GreaterThan(.9f)); blue++; }
                    foreach (var offset in new[] { Vector2.zero, new Vector2(-.49f,.4f), new Vector2(.49f,-.4f) })
                    {
                        Assert.That(ReadPanel(q+offset,TexturePaintChannel.Albedo), Is.EqualTo(color));
                        Assert.That(ReadPanel(q+offset,TexturePaintChannel.Roughness).r, Is.EqualTo(color.r > .9f ? .2f : .8f).Within(.001f));
                    }
                }
                Assert.That(red, Is.GreaterThan(0)); Assert.That(blue, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(plugin); }
        }

        [Test]
        public void QuiltSpriteSetSelectionClonesAndSerializesWithoutChangingSharedAsset()
        {
            using var sheets = new QuiltSheets();
            sheets.Add(TexturePaintChannel.Albedo, new[] { Color.red, Color.blue });
            var p = new TexturePaintPluginParameterSet();
            var value = p.Get("quiltSpriteSet", true); value.spriteSet = sheets.asset;
            value.spriteSetSelectionExplicit = true; value.enabledSpriteIndices.Add(1);
            var clone = p.Clone(); clone.Get("quiltSpriteSet").enabledSpriteIndices.Add(0);
            Assert.That(value.enabledSpriteIndices, Is.EqualTo(new[] { 1 }));
            var restored = JsonUtility.FromJson<TexturePaintPluginParameterSet>(JsonUtility.ToJson(p));
            Assert.That(restored.Get("quiltSpriteSet").spriteSet, Is.SameAs(sheets.asset));
            Assert.That(TexturePaintSpriteSetSource.ResolveEnabled(restored.Get("quiltSpriteSet"), 2), Is.EqualTo(new[] { 1 }));
            Assert.That(TexturePaintSpriteSetSource.Resolve(sheets.asset)[TexturePaintChannel.Albedo].Count, Is.EqualTo(2));
        }

        [Test]
        public void QuiltSpriteSetRejectsMismatchedSheetsAndEmptySelection()
        {
            using var sheets = new QuiltSheets();
            sheets.Add(TexturePaintChannel.Albedo, new[] { Color.red, Color.blue });
            sheets.Add(TexturePaintChannel.Roughness, new[] { Color.gray });
            Assert.That(() => TexturePaintSpriteSetSource.Resolve(sheets.asset), Throws.InvalidOperationException.With.Message.Contains("matching tile counts"));
            var value = new TexturePaintPluginParameterValue { spriteSetSelectionExplicit = true };
            Assert.That(() => TexturePaintSpriteSetSource.ResolveEnabled(value, 2), Throws.InvalidOperationException.With.Message.Contains("at least one"));
            value.enabledSpriteIndices.Add(3);
            Assert.That(() => TexturePaintSpriteSetSource.ResolveEnabled(value, 2), Throws.InvalidOperationException.With.Message.Contains("missing"));
        }
    }
}
#endif
