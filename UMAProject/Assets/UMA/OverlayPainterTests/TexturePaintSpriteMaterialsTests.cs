#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public class TexturePaintSpriteMaterialsTests
    {
        [Test]
        public void SpriteMaterialCategoriesRoundTripAndDetermineDropType()
        {
            var set = ScriptableObject.CreateInstance<OverlayPainterSpriteSet>();
            var copy = ScriptableObject.CreateInstance<OverlayPainterSpriteSet>();
            try
            {
                Assert.IsTrue(set.HasCategory(OverlayPainterSpriteSet.FillMaterial));
                set.categories = new[] { "projection", "Path" };
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(set), copy);
                Assert.IsTrue(copy.HasCategory(OverlayPainterSpriteSet.Projection));
                Assert.IsTrue(copy.HasCategory(OverlayPainterSpriteSet.Path));
                Assert.IsFalse(copy.HasCategory(OverlayPainterSpriteSet.FillMaterial));
                Assert.AreEqual(TexturePaintLayerKind.Fill, TexturePaintStageWindow.SpriteMaterialLayerKind(OverlayPainterSpriteSet.FillMaterial));
                Assert.AreEqual(TexturePaintLayerKind.Projection, TexturePaintStageWindow.SpriteMaterialLayerKind(OverlayPainterSpriteSet.Projection));
                Assert.AreEqual(TexturePaintLayerKind.Spline, TexturePaintStageWindow.SpriteMaterialLayerKind(OverlayPainterSpriteSet.Path));
            }
            finally { Object.DestroyImmediate(set); Object.DestroyImmediate(copy); }
        }

        [Test]
        public void SpriteMaterialIndividualSpritesRetainOrderAndChannelAlignment()
        {
            var set = ScriptableObject.CreateInstance<OverlayPainterSpriteSet>();
            var image = new Texture2D(4,4);
            var a = Sprite.Create(image, new Rect(0,0,2,2), Vector2.one*.5f);
            var b = Sprite.Create(image, new Rect(2,2,2,2), Vector2.one*.5f);
            try
            {
                set.spriteSheets.Add(new OverlayPainterSpriteSheet { channel = TexturePaintChannel.NormalControl, sprites = new() { b,a } });
                set.spriteSheets.Add(new OverlayPainterSpriteSheet { channel = TexturePaintChannel.Roughness, inverted = true, sprites = new() { a,b } });
                Assert.AreEqual(2, OverlayPainterSpriteSetEditorUtility.GetCommonSpriteCount(set));
                Assert.IsTrue(OverlayPainterSpriteSetEditorUtility.TryGetSprite(set.spriteSheets[0],0,out var selected));
                Assert.AreSame(b, selected);
                Assert.IsTrue(OverlayPainterSpriteSetEditorUtility.TryGetSprite(set.spriteSheets[1],0,out selected));
                Assert.AreSame(a, selected);
                Assert.IsFalse(OverlayPainterSpriteSetEditorUtility.TryGetSprite(set.spriteSheets[0],2,out _));
                var settings = TexturePaintStageWindow.CreateSpriteSetPathSourceSettings(b,true,TexturePaintNormalConvention.OpenGL);
                Assert.AreSame(b, settings.sourceSprite); Assert.IsTrue(settings.invert);
            }
            finally { Object.DestroyImmediate(set); Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(image); }
        }

        [Test]
        public void SpriteMaterialPathDropCreatesOneAnchoredPoint()
        {
            using var set = new TextureSet { persistentId = "face-tile" };
            using var layer = new TexturePaintLayer { kind = TexturePaintLayerKind.Spline, spline = new TexturePaintSpline() };
            var hit = new RaycastHit { point = new Vector3(.1f,1.6f,.2f), normal = Vector3.forward, barycentricCoordinate = new Vector3(.2f,.3f,.5f) };
            TexturePaintStageWindow.AddSpriteMaterialFirstPoint(layer,set,new ReconstructedSurface { index = 4 },hit);
            Assert.AreEqual(1, layer.spline.PointCount);
            Assert.IsTrue(layer.spline.worldSpace);
            Assert.AreEqual("face-tile",layer.spline.anchors[0].surfaceId);
            Assert.AreEqual(hit.barycentricCoordinate,layer.spline.anchors[0].barycentric);
            Assert.AreEqual(hit.normal,layer.spline.anchors[0].normal);
        }

        [Test]
        public void SpriteMaterialSphereUsesRoughnessAndReleasesPreviewScene()
        {
            var set = ScriptableObject.CreateInstance<OverlayPainterSpriteSet>();
            var texture = new Texture2D(4,4,TextureFormat.RGBA32,false,true);
            var sprite = Sprite.Create(texture,new Rect(0,0,4,4),Vector2.one*.5f);
            Texture2D rough = null, smooth = null;
            int scenes = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
            try
            {
                texture.SetPixels(Enumerable.Repeat(Color.white,16).ToArray()); texture.Apply();
                set.spriteSheets.Add(new OverlayPainterSpriteSheet { channel = TexturePaintChannel.Metallic, sprites = new() { sprite } });
                set.spriteSheets.Add(new OverlayPainterSpriteSheet { channel = TexturePaintChannel.Roughness, sprites = new() { sprite } });
                rough = SpriteMaterialThumbnails.Render(set,0);
                set.spriteSheets[1].inverted = true;
                smooth = SpriteMaterialThumbnails.Render(set,0);
                Assert.AreEqual(SpriteMaterialThumbnails.Size,rough.width);
                var a = rough.GetPixels(); var b = smooth.GetPixels();
                double change = a.Zip(b,(x,y)=>Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)).Average();
                Assert.Greater(change,.01,"Roughness inversion must change the actual URP Lit response.");
                Assert.AreEqual(scenes,UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount);
            }
            finally
            {
                if(rough!=null)Object.DestroyImmediate(rough); if(smooth!=null)Object.DestroyImmediate(smooth);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); Object.DestroyImmediate(set);
            }
        }
    }
}
#endif
