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

            document.Migrate();

            Assert.That(document.schemaVersion, Is.EqualTo(TexturePaintDocument.CurrentSchemaVersion));
            Assert.That(document.documentId, Is.Not.Empty);
            Assert.That(document.surfaces, Is.Not.Null);
            Object.DestroyImmediate(document);
        }

        [Test]
        public void MigrationRepairsLayerMaskPaintSource()
        {
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            document.schemaVersion = 15;
            var layer = new TexturePaintDocumentLayer
            {
                hasMask = true,
                maskSourceSettings = null
            };
            document.surfaces.Add(new TexturePaintDocumentSurface
            {
                layers = new System.Collections.Generic.List<TexturePaintDocumentLayer> { layer }
            });

            document.Migrate();

            Assert.That(layer.maskSourceSettings, Is.Not.Null);
            Assert.That(layer.maskSourceSettings.source, Is.EqualTo(TexturePaintBrushSource.Color));
            Assert.That(layer.maskSourceSettings.color, Is.EqualTo(Color.black));
            Object.DestroyImmediate(document);
        }

        [Test]
        public void MigrationPromotesAuthoredLegacyChannelSourceSettings()
        {
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            document.schemaVersion = 13;
            var channel = new TexturePaintDocumentLayerChannel
            {
                channel = TexturePaintChannel.Roughness,
                settings = new TexturePaintLayerChannelSettings
                {
                    channel = TexturePaintChannel.Roughness,
                    sourceSettings = new TexturePaintChannelSourceSettings
                    {
                        source = TexturePaintBrushSource.Color,
                        color = new Color(0.25f, 0.25f, 0.25f, 1f),
                        invert = true,
                        tiling = new Vector2(4f, 5f),
                        offset = new Vector2(0.2f, -0.3f),
                        rotation = 52f
                    }
                }
            };
            document.surfaces.Add(new TexturePaintDocumentSurface
            {
                layers = new System.Collections.Generic.List<TexturePaintDocumentLayer>
                {
                    new TexturePaintDocumentLayer
                    {
                        channels = new System.Collections.Generic.List<TexturePaintDocumentLayerChannel>
                            { channel }
                    }
                }
            });

            document.Migrate();

            Assert.That(channel.hasSourceSettings, Is.True);
            Assert.That(channel.GetSourceSettings().color,
                Is.EqualTo(new Color(0.25f, 0.25f, 0.25f, 1f)));
            Assert.That(channel.GetSourceSettings().invert, Is.True);
            Assert.That(channel.GetSourceSettings().tiling, Is.EqualTo(new Vector2(4f, 5f)));
            Assert.That(channel.GetSourceSettings().offset, Is.EqualTo(new Vector2(0.2f, -0.3f)));
            Assert.That(channel.GetSourceSettings().rotation, Is.EqualTo(52f));
            Object.DestroyImmediate(document);
        }

        [Test]
        public void SkinAndNormalControlChannelsSurviveDocumentSerialization()
        {
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            document.surfaces.Add(new TexturePaintDocumentSurface
            {
                normalControlStrength = 5.5f,
                normalControlRadius = 3,
                normalControlInvert = true,
                baseChannels = new System.Collections.Generic.List<TexturePaintDocumentChannel>
                {
                    new TexturePaintDocumentChannel { channel = TexturePaintChannel.SkinColorMask },
                    new TexturePaintDocumentChannel { channel = TexturePaintChannel.Thickness },
                    new TexturePaintDocumentChannel { channel = TexturePaintChannel.DetailMask },
                    new TexturePaintDocumentChannel { channel = TexturePaintChannel.NormalControl }
                },
                layers = new System.Collections.Generic.List<TexturePaintDocumentLayer>
                {
                    new TexturePaintDocumentLayer
                    {
                        channels = new System.Collections.Generic.List<TexturePaintDocumentLayerChannel>
                        {
                            new TexturePaintDocumentLayerChannel
                            {
                                channel = TexturePaintChannel.SkinColorMask,
                                hasSourceSettings = true,
                                sourceColor = new Color(0.8f, 0.35f, 0.25f, 0.6f)
                            },
                            new TexturePaintDocumentLayerChannel { channel = TexturePaintChannel.Thickness },
                            new TexturePaintDocumentLayerChannel { channel = TexturePaintChannel.DetailMask },
                            new TexturePaintDocumentLayerChannel
                            {
                                channel = TexturePaintChannel.NormalControl,
                                hasSourceSettings = true,
                                sourceColor = new Color(0.72f, 0.72f, 0.72f, 0.4f)
                            }
                        }
                    }
                }
            });

            string json = JsonUtility.ToJson(document);
            TexturePaintDocument restored = ScriptableObject.CreateInstance<TexturePaintDocument>();
            JsonUtility.FromJsonOverwrite(json, restored);

            Assert.That(restored.surfaces[0].baseChannels[0].channel,
                Is.EqualTo(TexturePaintChannel.SkinColorMask));
            Assert.That(restored.surfaces[0].baseChannels[1].channel,
                Is.EqualTo(TexturePaintChannel.Thickness));
            Assert.That(restored.surfaces[0].baseChannels[2].channel,
                Is.EqualTo(TexturePaintChannel.DetailMask));
            Assert.That(restored.surfaces[0].baseChannels[3].channel,
                Is.EqualTo(TexturePaintChannel.NormalControl));
            Assert.That(restored.surfaces[0].normalControlStrength, Is.EqualTo(5.5f));
            Assert.That(restored.surfaces[0].normalControlRadius, Is.EqualTo(3));
            Assert.That(restored.surfaces[0].normalControlInvert, Is.True);
            Assert.That(restored.surfaces[0].layers[0].channels[0].sourceColor,
                Is.EqualTo(new Color(0.8f, 0.35f, 0.25f, 0.6f)));
            Assert.That(restored.surfaces[0].layers[0].channels[3].channel,
                Is.EqualTo(TexturePaintChannel.NormalControl));
            Assert.That(restored.surfaces[0].layers[0].channels[3].sourceColor,
                Is.EqualTo(new Color(0.72f, 0.72f, 0.72f, 0.4f)));
            Object.DestroyImmediate(restored);
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
            Assert.That(set.layers.IndexOf(child), Is.EqualTo(set.layers.IndexOf(group) - 1),
                "A child should appear immediately below its folder in the top-to-bottom layer UI.");
        }

        [Test]
        public void NewPathLayersDefaultToRibbonApplyMode()
        {
            using TextureSet set = new TextureSet();

            TexturePaintLayer path = set.AddSplineLayer("Path");

            Assert.That(path.splineSettings.pathMode, Is.EqualTo(TexturePaintPathMode.Ribbon));
            Assert.That(path.spline.worldSpace, Is.True,
                "New paths should start in the Scene-view authoring domain until the user selects 2D in Properties.");
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
            Assert.That(effects.textureOverlay, Is.Not.Null);
            Assert.That(effects.textureOverlay.textureTiling1, Is.EqualTo(Vector2.one));
            Assert.That(effects.textureOverlay.textureTiling2, Is.EqualTo(Vector2.one));
            Object.DestroyImmediate(document);
        }

        [Test]
        public void LayerEffectsCloneOwnsIndependentCurves()
        {
            Texture2D overlay1 = new Texture2D(1, 1);
            Texture2D overlay2 = new Texture2D(1, 1);
            TexturePaintLayerEffects source = new TexturePaintLayerEffects();
            source.innerShadow.enabled = true;
            source.innerShadow.level = 0.42f;
            source.innerShadow.curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            source.edgeFade.enabled = true;
            source.edgeFade.edgeFadeStart = 0.61f;
            source.edgeFade.edgeFadeSize = 0.84f;
            source.innerShadow.ribbonSide = TexturePaintRibbonSide.Right;
            source.bevelEdge.enabled = true;
            source.bevelEdge.secondaryColor = Color.blue;
            source.bevelEdge.ribbonLeftTone = TexturePaintRibbonBevelTone.Dark;
            source.bevelEdge.ribbonRightOffset = 3.5f;
            source.proceduralStitch.enabled = true;
            source.proceduralStitch.ribbonSide = TexturePaintRibbonSide.Both;
            source.proceduralStitch.stitchRows = TexturePaintRibbonStitchRows.Double;
            source.proceduralStitch.stitchThreadSize = 0.031f;
            source.proceduralStitch.stitchLength = 0.17f;
            source.proceduralStitch.stitchInset = 0.12f;
            source.textureOverlay.enabled = true;
            source.textureOverlay.texture1 = overlay1;
            source.textureOverlay.texture2 = overlay2;
            source.textureOverlay.textureTiling1 = new Vector2(2f, 3f);
            source.textureOverlay.textureTiling2 = new Vector2(-4f, 5f);
            source.textureOverlay.textureOpacity1 = 0.37f;
            source.textureOverlay.textureOpacity2 = 0.68f;
            source.textureOverlay.blendMode = TexturePaintBlendMode.Multiply;
            source.textureOverlay.secondaryBlendMode = TexturePaintBlendMode.Screen;
            source.textureOverlay.color = Color.red;
            source.textureOverlay.secondaryColor = Color.cyan;

            TexturePaintLayerEffects copy = source.Clone();
            copy.innerShadow.curve.MoveKey(1, new Keyframe(1f, 0.6f));

            Assert.That(copy.innerShadow, Is.Not.SameAs(source.innerShadow));
            Assert.That(copy.innerShadow.level, Is.EqualTo(0.42f));
            Assert.That(copy.edgeFade, Is.Not.SameAs(source.edgeFade));
            Assert.That(copy.edgeFade.enabled, Is.True);
            Assert.That(copy.edgeFade.edgeFadeStart, Is.EqualTo(0.61f));
            Assert.That(copy.edgeFade.edgeFadeSize, Is.EqualTo(0.84f));
            Assert.That(copy.innerShadow.ribbonSide, Is.EqualTo(TexturePaintRibbonSide.Right));
            Assert.That(copy.bevelEdge, Is.Not.SameAs(source.bevelEdge));
            Assert.That(copy.bevelEdge.secondaryColor, Is.EqualTo(Color.blue));
            Assert.That(copy.bevelEdge.ribbonLeftTone, Is.EqualTo(TexturePaintRibbonBevelTone.Dark));
            Assert.That(copy.bevelEdge.ribbonRightOffset, Is.EqualTo(3.5f));
            Assert.That(copy.proceduralStitch, Is.Not.SameAs(source.proceduralStitch));
            Assert.That(copy.proceduralStitch.stitchRows, Is.EqualTo(TexturePaintRibbonStitchRows.Double));
            Assert.That(copy.proceduralStitch.stitchThreadSize, Is.EqualTo(0.031f));
            Assert.That(copy.proceduralStitch.stitchLength, Is.EqualTo(0.17f));
            Assert.That(copy.proceduralStitch.stitchInset, Is.EqualTo(0.12f));
            Assert.That(copy.textureOverlay, Is.Not.SameAs(source.textureOverlay));
            Assert.That(copy.textureOverlay.texture1, Is.SameAs(overlay1));
            Assert.That(copy.textureOverlay.texture2, Is.SameAs(overlay2));
            Assert.That(copy.textureOverlay.textureTiling1, Is.EqualTo(new Vector2(2f, 3f)));
            Assert.That(copy.textureOverlay.textureTiling2, Is.EqualTo(new Vector2(-4f, 5f)));
            Assert.That(copy.textureOverlay.textureOpacity1, Is.EqualTo(0.37f));
            Assert.That(copy.textureOverlay.textureOpacity2, Is.EqualTo(0.68f));
            Assert.That(copy.textureOverlay.blendMode, Is.EqualTo(TexturePaintBlendMode.Multiply));
            Assert.That(copy.textureOverlay.secondaryBlendMode, Is.EqualTo(TexturePaintBlendMode.Screen));
            Assert.That(copy.textureOverlay.color, Is.EqualTo(Color.red));
            Assert.That(copy.textureOverlay.secondaryColor, Is.EqualTo(Color.cyan));
            Assert.That(copy.innerShadow.curve, Is.Not.SameAs(source.innerShadow.curve));
            Assert.That(source.innerShadow.curve.Evaluate(1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(copy.innerShadow.curve.Evaluate(1f), Is.EqualTo(0.6f).Within(0.001f));
            Object.DestroyImmediate(overlay1);
            Object.DestroyImmediate(overlay2);
        }

        [Test]
        public void StrokeOffsetMovesItsReachAcrossTheAuthoredEdge()
        {
            TexturePaintLayerEffects effects = new TexturePaintLayerEffects();
            TexturePaintLayerEffectSettings stroke = effects.stroke;
            stroke.enabled = true;
            stroke.channel = TexturePaintChannel.Albedo;
            stroke.width = 10f;

            stroke.offset.x = -4f;
            Assert.That(effects.MaximumReach(TexturePaintChannel.Albedo), Is.EqualTo(8),
                "An inward stroke should reserve its farthest edge plus compositor padding.");

            stroke.offset.x = -20f;
            Assert.That(effects.MaximumReach(TexturePaintChannel.Albedo), Is.EqualTo(22),
                "A fully inset stroke still needs enough dirty-region reach to update its inner band.");

            stroke.offset.x = 4f;
            Assert.That(effects.MaximumReach(TexturePaintChannel.Albedo), Is.EqualTo(16),
                "An outward stroke should reserve its offset, width, and compositor padding.");

            TexturePaintLayerEffects clone = effects.Clone();
            Assert.That(clone.stroke.offset.x, Is.EqualTo(4f));
        }

        [Test]
        public void LayerEffectStackPreservesOrderInstancesAndTextureTransforms()
        {
            TexturePaintLayerEffects source = new TexturePaintLayerEffects();
            TexturePaintLayerEffectSettings first = source.Add(TexturePaintLayerEffectKind.TextureOverlay);
            first.enabled = true;
            first.textureOffset1 = new Vector2(0.2f, -0.3f);
            first.textureOffset2 = new Vector2(-0.4f, 0.5f);
            first.textureRotation1 = 37f;
            first.textureRotation2 = -18f;
            TexturePaintLayerEffectSettings second = source.Add(TexturePaintLayerEffectKind.TextureOverlay);
            second.enabled = true;

            Assert.That(source.Move(source.Stack.IndexOf(second), source.Stack.IndexOf(first)), Is.True);
            TexturePaintLayerEffects copy = source.Clone();

            Assert.That(copy.Stack.Count, Is.EqualTo(source.Stack.Count));
            Assert.That(copy.Stack[source.Stack.IndexOf(second)].id, Is.EqualTo(second.id));
            TexturePaintLayerEffectSettings firstCopy = copy.Stack.Find(effect => effect.id == first.id);
            Assert.That(firstCopy, Is.Not.Null);
            Assert.That(firstCopy, Is.Not.SameAs(first));
            Assert.That(firstCopy.textureOffset1, Is.EqualTo(new Vector2(0.2f, -0.3f)));
            Assert.That(firstCopy.textureOffset2, Is.EqualTo(new Vector2(-0.4f, 0.5f)));
            Assert.That(firstCopy.textureRotation1, Is.EqualTo(37f));
            Assert.That(firstCopy.textureRotation2, Is.EqualTo(-18f));
            Assert.That(copy.Remove(firstCopy.id), Is.True);
            Assert.That(copy.Stack.Exists(effect => effect.id == first.id), Is.False);
            Assert.That(source.Stack.Exists(effect => effect.id == first.id), Is.True);
        }

        [Test]
        public void ImageAdjustmentsCloneAndNormalizeAllChannelSpecificSettings()
        {
            TexturePaintLayerEffects source = new TexturePaintLayerEffects();
            TexturePaintLayerEffectSettings adjustment = source.imageAdjustments;
            adjustment.enabled = true;
            adjustment.channel = TexturePaintChannel.Albedo;
            adjustment.saturation = 1.65f;
            adjustment.brightness = -0.24f;
            adjustment.contrast = 0.38f;
            adjustment.hue = 127f;
            adjustment.level = 0.72f;

            TexturePaintLayerEffects copy = source.Clone();
            TexturePaintLayerEffectSettings cloned = copy.imageAdjustments;

            Assert.That(cloned, Is.Not.SameAs(adjustment));
            Assert.That(cloned.kind, Is.EqualTo(TexturePaintLayerEffectKind.ImageAdjustments));
            Assert.That(cloned.channel, Is.EqualTo(TexturePaintChannel.Albedo));
            Assert.That(cloned.saturation, Is.EqualTo(1.65f));
            Assert.That(cloned.brightness, Is.EqualTo(-0.24f));
            Assert.That(cloned.contrast, Is.EqualTo(0.38f));
            Assert.That(cloned.hue, Is.EqualTo(127f));
            Assert.That(cloned.level, Is.EqualTo(0.72f));

            cloned.saturation = 5f;
            cloned.brightness = -3f;
            cloned.contrast = 4f;
            cloned.hue = 720f;
            copy.Normalize();
            Assert.That(cloned.saturation, Is.EqualTo(2f));
            Assert.That(cloned.brightness, Is.EqualTo(-1f));
            Assert.That(cloned.contrast, Is.EqualTo(1f));
            Assert.That(cloned.hue, Is.EqualTo(180f));
        }

        [Test]
        public void SplineSettingsCloneRetainsAssetReferencesAndValues()
        {
            Texture2D stamp = new Texture2D(1, 1);
            Texture2D beginning = new Texture2D(1, 1);
            Texture2D end = new Texture2D(1, 1);
            TexturePaintSplineSettings source = new TexturePaintSplineSettings
            {
                brushShape = BrushPreset.Shape.Stamp,
                brushSize = 0.125f,
                brushHardness = 0.42f,
                brushStamp = stamp,
                ribbonBeginningTexture = beginning,
                ribbonEndTexture = end,
                color = Color.magenta
            };

            TexturePaintSplineSettings copy = source.Clone();

            Assert.That(copy, Is.Not.SameAs(source));
            Assert.That(copy.brushSize, Is.EqualTo(source.brushSize));
            Assert.That(copy.brushHardness, Is.EqualTo(source.brushHardness));
            Assert.That(copy.brushStamp, Is.SameAs(stamp));
            Assert.That(copy.ribbonBeginningTexture, Is.SameAs(beginning));
            Assert.That(copy.ribbonEndTexture, Is.SameAs(end));
            Assert.That(copy.color, Is.EqualTo(Color.magenta));
            Object.DestroyImmediate(stamp);
            Object.DestroyImmediate(beginning);
            Object.DestroyImmediate(end);
        }
    }
}
#endif
