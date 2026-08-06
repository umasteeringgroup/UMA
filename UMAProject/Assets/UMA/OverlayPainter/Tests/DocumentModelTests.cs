#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class DocumentModelTests
    {
        [Test]
        public void MigrationRepairsLegacyDocumentIdentityAndCollections()
        {
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            document.schemaVersion = 0;
            document.documentId = null;
            document.surfaces = null;
            document.globalMasks = null;

            document.Migrate();

            Assert.That(document.schemaVersion, Is.EqualTo(TexturePaintDocument.CurrentSchemaVersion));
            Assert.That(document.documentId, Is.Not.Empty);
            Assert.That(document.surfaces, Is.Not.Null);
            Assert.That(document.globalMasks, Is.Not.Null);
            Object.DestroyImmediate(document);
        }

        [Test]
        public void PixelDataCanUseExternalTextAssetStorage()
        {
            TextAsset data = new TextAsset("pixel data");
            TexturePaintPixelData pixels = new TexturePaintPixelData
            {
                width = 1,
                height = 1,
                dataAsset = data
            };

            Assert.That(pixels.HasData, Is.True);
            Assert.That(pixels.GetCompressedBytes(), Is.EqualTo(data.bytes));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void EditableTextureRevisionChangesWhenContentIsReplaced()
        {
            using EditableTextureTarget target = new EditableTextureTarget("Revision Test", 4, 4,
                RenderTextureFormat.ARGB32, null, Color.clear);
            long initial = target.Revision;

            target.Reset(null, Color.red);

            Assert.That(target.Revision, Is.GreaterThan(initial));
        }

        [Test]
        public void NewChannelSettingsAreIndependentFromLayerOpacity()
        {
            TexturePaintLayer layer = new TexturePaintLayer { opacity = 0.35f };

            TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(TexturePaintChannel.Albedo);

            Assert.That(settings.opacity, Is.EqualTo(1f));
            Assert.That(layer.opacity, Is.EqualTo(0.35f));
        }

        [Test]
        public void AddingLayerToActiveGroupCreatesChild()
        {
            TextureSet set = new TextureSet();
            TexturePaintLayer group = set.AddGroup("Details");

            TexturePaintLayer child = set.AddLayer("Paint");

            Assert.That(child.parentId, Is.EqualTo(group.id));
            Assert.That(child.kind, Is.EqualTo(TexturePaintLayerKind.Paint));
        }

        [Test]
        public void NewPathLayersDefaultToRibbonApplyMode()
        {
            using TextureSet set = new TextureSet();

            TexturePaintLayer path = set.AddSplineLayer("Path");

            Assert.That(path.splineSettings.pathMode, Is.EqualTo(TexturePaintPathMode.Ribbon));
        }

        [Test]
        public void MigrationCreatesDefaultLayerEffectsForLegacyLayers()
        {
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            document.schemaVersion = 9;
            document.surfaces.Add(new TexturePaintDocumentSurface
            {
                layers = new System.Collections.Generic.List<TexturePaintDocumentLayer>
                {
                    new TexturePaintDocumentLayer { effects = null }
                }
            });

            document.Migrate();

            TexturePaintLayerEffects effects = document.surfaces[0].layers[0].effects;
            Assert.That(effects, Is.Not.Null);
            Assert.That(effects.HasEnabled, Is.False);
            Assert.That(effects.outerShadow.curve, Is.Not.Null);
            Object.DestroyImmediate(document);
        }

        [Test]
        public void LayerEffectsCloneOwnsIndependentCurves()
        {
            TexturePaintLayerEffects source = new TexturePaintLayerEffects();
            source.innerShadow.enabled = true;
            source.innerShadow.curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

            TexturePaintLayerEffects copy = source.Clone();
            copy.innerShadow.curve.MoveKey(1, new Keyframe(1f, 0.6f));

            Assert.That(copy.innerShadow, Is.Not.SameAs(source.innerShadow));
            Assert.That(copy.innerShadow.curve, Is.Not.SameAs(source.innerShadow.curve));
            Assert.That(source.innerShadow.curve.Evaluate(1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(copy.innerShadow.curve.Evaluate(1f), Is.EqualTo(0.6f).Within(0.001f));
        }

        [Test]
        public void SplineSettingsCloneRetainsAssetReferencesAndValues()
        {
            Texture2D stamp = new Texture2D(1, 1);
            TexturePaintSplineSettings source = new TexturePaintSplineSettings
            {
                brushShape = BrushPreset.Shape.Stamp,
                brushSize = 0.125f,
                brushHardness = 0.42f,
                brushStamp = stamp,
                color = Color.magenta
            };

            TexturePaintSplineSettings copy = source.Clone();

            Assert.That(copy, Is.Not.SameAs(source));
            Assert.That(copy.brushSize, Is.EqualTo(source.brushSize));
            Assert.That(copy.brushHardness, Is.EqualTo(source.brushHardness));
            Assert.That(copy.brushStamp, Is.SameAs(stamp));
            Assert.That(copy.color, Is.EqualTo(Color.magenta));
            Object.DestroyImmediate(stamp);
        }
    }
}
#endif
