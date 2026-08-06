#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintReleaseIntegrationTests
    {
        private const string Folder = "Assets/UMA/OverlayPainter/GeneratedReleaseTests";
        private readonly List<Object> ownedObjects = new List<Object>();
        private readonly List<TextureStore> ownedStores = new List<TextureStore>();
        private string indexerAssetPath;
        private byte[] indexerAssetBytes;

        [SetUp]
        public void SetUp()
        {
            indexerAssetPath = AssetDatabase.GetAssetPath(UMAAssetIndexer.Instance);
            indexerAssetBytes = !string.IsNullOrEmpty(indexerAssetPath)
                ? File.ReadAllBytes(Path.GetFullPath(indexerAssetPath)) : null;
            AssetDatabase.DeleteAsset(Folder);
            EnsureFolder(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            string[] overlays = AssetDatabase.FindAssets("t:OverlayDataAsset", new[] { Folder });
            for (int i = 0; indexer != null && i < overlays.Length; i++)
            {
                OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(
                    AssetDatabase.GUIDToAssetPath(overlays[i]));
                if (overlay != null) indexer.RemoveAsset(typeof(OverlayDataAsset), overlay.overlayName, false);
            }
            if (indexer != null && overlays.Length > 0)
            {
                indexer.RebuildIndex();
                EditorUtility.SetDirty(indexer);
                AssetDatabase.SaveAssetIfDirty(indexer);
            }
            for (int i = 0; i < ownedStores.Count; i++) ownedStores[i]?.Dispose();
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
                if (ownedObjects[i] != null) Object.DestroyImmediate(ownedObjects[i]);
            ownedStores.Clear();
            ownedObjects.Clear();
            AssetDatabase.DeleteAsset(Folder);
            if (indexerAssetBytes != null && !string.IsNullOrEmpty(indexerAssetPath))
            {
                File.WriteAllBytes(Path.GetFullPath(indexerAssetPath), indexerAssetBytes);
                AssetDatabase.ImportAsset(indexerAssetPath, ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
            }
        }

        [Test]
        public void LayerCompositionMatchesOrderedOpacityAndVisibilityReference()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, new Color(0.1f, 0.2f, 0.3f, 0.25f));
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Composition", 16, RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Assert.That(compositor.IsAvailable, Is.True, "Layer compositor GPU path is unavailable.");

            TexturePaintLayer lower = set.AddFillLayer("Lower", TexturePaintChannel.Albedo,
                new Color(0.9f, 0.1f, 0.2f, 0.5f));
            lower.opacity = 0.5f;
            lower.GetChannelSettings(TexturePaintChannel.Albedo).opacity = 0.5f;
            TexturePaintLayer hidden = set.AddFillLayer("Hidden", TexturePaintChannel.Albedo, Color.green);
            hidden.visible = false;
            AssertColor(ReadCenter(lower.channels[TexturePaintChannel.Albedo].Front),
                new Color(0.9f, 0.1f, 0.2f, 0.5f), 0.004f);
            Assert.That(lower.GetChannelSettings(TexturePaintChannel.Albedo).enabled, Is.True);
            set.RecomposeAll();

            float alpha = 0.5f * 0.5f * 0.5f;
            Color expected = Color.Lerp(new Color(0.1f, 0.2f, 0.3f, 0.25f),
                new Color(0.9f, 0.1f, 0.2f, 0.5f), alpha);
            expected.a = alpha + 0.25f * (1f - alpha);
            Color actual = ReadCenter(channel.composite);
            AssertColor(actual, expected, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void LayerEffectsCompositeStrokeAndColorOverlayWithoutChangingLayerPixels()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Layer Effects", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Assert.That(compositor.EffectsAvailable, Is.True, "Layer effects GPU path is unavailable.");

            Texture2D source = Own(new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true));
            Color[] pixels = new Color[16 * 16];
            for (int y = 6; y <= 9; y++)
            for (int x = 6; x <= 9; x++) pixels[y * 16 + x] = Color.green;
            source.SetPixels(pixels);
            source.Apply(false, false);

            TexturePaintLayer layer = set.AddLayer("Effect Source");
            EditableTextureTarget layerTarget = set.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay);
            layerTarget.Reset(source, Color.clear);
            layer.effects.stroke.enabled = true;
            layer.effects.stroke.channel = TexturePaintChannel.Albedo;
            layer.effects.stroke.color = Color.red;
            layer.effects.stroke.width = 2f;
            layer.effects.stroke.smoothness = 0f;
            layer.effects.colorOverlay.enabled = true;
            layer.effects.colorOverlay.channel = TexturePaintChannel.Albedo;
            layer.effects.colorOverlay.color = Color.blue;
            layer.effects.colorOverlay.level = 0.5f;
            layer.effects.colorOverlay.blendMode = TexturePaintBlendMode.Normal;

            set.RecomposeAll();

            AssertColor(ReadPixel(channel.composite, 8, 8), new Color(0f, 0.5f, 0.5f, 1f), 0.02f);
            AssertColor(ReadPixel(channel.composite, 4, 8), Color.red, 0.02f);
            AssertColor(ReadPixel(channel.composite, 1, 1), Color.clear, 0.02f);
            AssertColor(ReadPixel(layerTarget.Front, 8, 8), Color.green, 0.004f);
            AssertColor(ReadPixel(layerTarget.Front, 4, 8), Color.clear, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void ActivePaintLayerOwnsStrokeWhenLegacyBaseDestinationIsRequested()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black);
            TexturePaintLayer layer = fixture.set.AddLayer("Only Paint Layer");
            BrushPreset brush = fixture.CreateBrush();
            using PaintingEngine engine = new PaintingEngine(null, null, null);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green);

                Assert.That(PaintingEngine.ResolveDestinationMode(TexturePaintSourceMode.SourceTexture,
                    fixture.set), Is.EqualTo(TexturePaintSourceMode.SourceOverlay));
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                Assert.That(layer.channels.ContainsKey(TexturePaintChannel.Albedo), Is.True,
                    "An active paint layer must receive the editable target.");
                Assert.That(layer.strokes, Has.Count.EqualTo(1),
                    "Stroke metadata must be owned by the active layer.");
                Assert.That(fixture.set.baseStrokes, Is.Empty,
                    "A stale base-destination setting must not silently make a layer stroke destructive.");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RemovingOnlyLayerRecomposesUntouchedBaseWithoutPreviewMaterial()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            Color baseColor = new Color(0.13f, 0.31f, 0.67f, 1f);
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, baseColor);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Delete Layer Composite", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            TexturePaintLayer layer = set.AddLayer("Only Paint Layer");
            set.GetPaintTarget(TexturePaintChannel.Albedo, TexturePaintSourceMode.SourceOverlay)
                .Reset(null, Color.green);
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), Color.green, 0.004f);

            Assert.That(set.RemoveLayerAt(set.layers.IndexOf(layer)), Is.True);

            Assert.That(set.layers, Is.Empty);
            AssertColor(ReadCenter(channel.composite), baseColor, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void FlatTextureFillGeneratesAndCachesTiledLayerPixels()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/UMA/OverlayPainter/Shaders/FillLayer.shader");
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            source.filterMode = FilterMode.Point;
            source.SetPixels(new[] { Color.red, Color.blue, Color.red, Color.blue });
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Flat,
                tiling = new Vector2(2f, 1f)
            };

            TexturePaintLayer layer = fixture.set.AddFillLayer("Tiled", TexturePaintChannel.Albedo, settings);

            Assert.That(layer, Is.Not.Null);
            Color[] generated = TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front);
            int y = TexturePaintGpuTestFixture.Size / 2;
            AssertColor(generated[y * TexturePaintGpuTestFixture.Size + 8], Color.red, 0.02f);
            AssertColor(generated[y * TexturePaintGpuTestFixture.Size + 24], Color.blue, 0.02f);
            AssertColor(generated[y * TexturePaintGpuTestFixture.Size + 40], Color.red, 0.02f);
            AssertColor(generated[y * TexturePaintGpuTestFixture.Size + 56], Color.blue, 0.02f);

            source.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green });
            source.Apply(false, false);
            Color cached = TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front)[y * TexturePaintGpuTestFixture.Size + 8];
            AssertColor(cached, Color.red, 0.02f);
        }

        [Test]
        public void TextureFillGeneratesOpaquePaddingOutsideUVIsland()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            fixture.mesh.uv = new[]
            {
                new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f),
                new Vector2(0.75f, 0.75f), new Vector2(0.25f, 0.75f)
            };
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/UMA/OverlayPainter/Shaders/FillLayer.shader");
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            source.SetPixels(new[] { Color.blue, Color.blue, Color.blue, Color.blue });
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Flat,
                tiling = Vector2.one
            };

            TexturePaintLayer layer = fixture.set.AddFillLayer("Padded", TexturePaintChannel.Albedo, settings);

            Assert.That(layer, Is.Not.Null);
            Color[] generated = TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front);
            int y = TexturePaintGpuTestFixture.Size / 2;
            Color padded = generated[y * TexturePaintGpuTestFixture.Size + 14];
            Assert.That(padded.a, Is.GreaterThan(0.95f),
                "Fill coverage must extend two texels beyond the UV island for bilinear preview filtering.");
            AssertColor(padded, Color.blue, 0.02f);
        }

        [Test]
        public void FlatTextureFillKeepsSourceYOrientation()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/UMA/OverlayPainter/Shaders/FillLayer.shader");
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            source.filterMode = FilterMode.Point;
            // Texture2D pixel arrays are bottom row first: red below, blue above.
            source.SetPixels(new[] { Color.red, Color.red, Color.blue, Color.blue });
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Flat,
                tiling = Vector2.one
            };

            TexturePaintLayer layer = fixture.set.AddFillLayer("Oriented", TexturePaintChannel.Albedo, settings);

            Assert.That(layer, Is.Not.Null);
            Color[] generated = TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front);
            int x = TexturePaintGpuTestFixture.Size / 2;
            AssertColor(generated[8 * TexturePaintGpuTestFixture.Size + x], Color.red, 0.02f);
            AssertColor(generated[56 * TexturePaintGpuTestFixture.Size + x], Color.blue, 0.02f);
        }

        [Test]
        public void TriplanarCrossFadeBlendsProjectionAxes()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/UMA/OverlayPainter/Shaders/FillLayer.shader");
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Vector3 diagonal = Vector3.one.normalized;
            fixture.mesh.normals = new[] { diagonal, diagonal, diagonal, diagonal };
            const int sourceSize = 32;
            Texture2D source = Own(new Texture2D(sourceSize, sourceSize, TextureFormat.RGBAFloat, false, true));
            Color[] gradient = new Color[sourceSize * sourceSize];
            for (int y = 0; y < sourceSize; y++)
            for (int x = 0; x < sourceSize; x++)
                gradient[y * sourceSize + x] = new Color(x / (sourceSize - 1f), y / (sourceSize - 1f), 0f, 1f);
            source.SetPixels(gradient);
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Triplanar,
                tiling = Vector2.one,
                triplanarBlend = TexturePaintTriplanarBlend.Hard
            };
            TexturePaintLayer hard = fixture.set.AddFillLayer("Hard", TexturePaintChannel.Albedo, settings);
            Assert.That(hard, Is.Not.Null);
            Color hardCenter = TexturePaintGpuTestFixture.ReadPixels(
                hard.channels[TexturePaintChannel.Albedo].Front)[32 * TexturePaintGpuTestFixture.Size + 32];

            settings.triplanarBlend = TexturePaintTriplanarBlend.CrossFade;
            settings.blendSharpness = 1f;
            TexturePaintLayer blended = fixture.set.AddFillLayer("Cross Fade", TexturePaintChannel.Albedo, settings);
            Assert.That(blended, Is.Not.Null);
            Color blendedCenter = TexturePaintGpuTestFixture.ReadPixels(
                blended.channels[TexturePaintChannel.Albedo].Front)[32 * TexturePaintGpuTestFixture.Size + 32];

            Assert.That(Mathf.Abs(blendedCenter.r - hardCenter.r) + Mathf.Abs(blendedCenter.g - hardCenter.g),
                Is.GreaterThan(0.1f), "Cross Fade should combine axes instead of selecting the hard dominant projection.");
        }

        [Test]
        public void FillProjectionSettingsAndCachedPixelsSurviveDocumentRoundTrip()
        {
            Material material = Own(new Material(Shader.Find("Standard")) { name = "Fill Persistence Material" });
            Mesh mesh = Own(CreateQuadMesh());
            TextureSet original = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            TextureStore originalStore = CreateStore(original);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Color,
                color = new Color(0.31f, 0.57f, 0.83f, 0.72f),
                projection = TexturePaintFillProjection.Triplanar,
                tiling = new Vector2(2.5f, 4.25f),
                triplanarBlend = TexturePaintTriplanarBlend.CrossFade,
                blendOffset = 0.17f,
                blendSharpness = 7f
            };
            Assert.That(original.AddFillLayer("Projected Fill", TexturePaintChannel.Albedo, settings), Is.Not.Null);
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            AssetDatabase.CreateAsset(document, Folder + "/Fill Round Trip.asset");
            TexturePaintDocumentStorage.Save(document, originalStore, new TexturePaintMaskStack());

            TextureSet restored = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            TextureStore restoredStore = CreateStore(restored);
            TexturePaintDocumentStorage.Restore(document, restoredStore);

            Assert.That(restored.layers, Has.Count.EqualTo(1));
            TexturePaintLayer fill = restored.layers[0];
            Assert.That(fill.kind, Is.EqualTo(TexturePaintLayerKind.Fill));
            Assert.That(fill.fillSettings.source, Is.EqualTo(TexturePaintBrushSource.Color));
            Assert.That(fill.fillSettings.projection, Is.EqualTo(TexturePaintFillProjection.Triplanar));
            Assert.That(fill.fillSettings.tiling, Is.EqualTo(new Vector2(2.5f, 4.25f)));
            Assert.That(fill.fillSettings.triplanarBlend, Is.EqualTo(TexturePaintTriplanarBlend.CrossFade));
            Assert.That(fill.fillSettings.blendOffset, Is.EqualTo(0.17f).Within(0.0001f));
            Assert.That(fill.fillSettings.blendSharpness, Is.EqualTo(7f).Within(0.0001f));
            AssertColor(ReadCenter(fill.channels[TexturePaintChannel.Albedo].Front), settings.color, 0.004f);
        }

        [Test]
        public void PaintLayerToolSettingsSurviveDocumentRoundTrip()
        {
            Material material = Own(new Material(Shader.Find("Standard")) { name = "Paint Settings Material" });
            Mesh mesh = Own(CreateQuadMesh());
            TextureSet original = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            TextureStore originalStore = CreateStore(original);
            TexturePaintLayer layer = original.AddLayer("Remembered Paint");
            layer.paintSettings = new TexturePaintLayerSettings
            {
                tool = TexturePaintTool.Clone,
                channel = TexturePaintChannel.Albedo,
                source = TexturePaintBrushSource.Texture,
                destination = TexturePaintSourceMode.SourceOverlay,
                color = new Color(0.2f, 0.4f, 0.6f, 0.8f),
                strength = 0.37f,
                mirrorX = true,
                stabilization = 0.22f,
                directionSmoothing = 0.63f,
                projectionDepth = 1.25f,
                normalAngleLimit = 54f,
                paintBackfaces = true,
                pressureAffectsFlow = false,
                pressureAffectsSize = true
            };
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            AssetDatabase.CreateAsset(document, Folder + "/Paint Settings Round Trip.asset");
            TexturePaintDocumentStorage.Save(document, originalStore, new TexturePaintMaskStack());

            TextureSet restored = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            TextureStore restoredStore = CreateStore(restored);
            TexturePaintDocumentStorage.Restore(document, restoredStore);

            Assert.That(restored.layers, Has.Count.EqualTo(1));
            TexturePaintLayerSettings settings = restored.layers[0].paintSettings;
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.tool, Is.EqualTo(TexturePaintTool.Clone));
            Assert.That(settings.source, Is.EqualTo(TexturePaintBrushSource.Texture));
            Assert.That(settings.strength, Is.EqualTo(0.37f).Within(0.0001f));
            Assert.That(settings.mirrorX, Is.True);
            Assert.That(settings.directionSmoothing, Is.EqualTo(0.63f).Within(0.0001f));
            Assert.That(settings.projectionDepth, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(settings.normalAngleLimit, Is.EqualTo(54f).Within(0.0001f));
            Assert.That(settings.paintBackfaces, Is.True);
            Assert.That(settings.pressureAffectsFlow, Is.False);
            Assert.That(settings.pressureAffectsSize, Is.True);
        }

        [Test]
        public void SrgbOutputChannelKeepsUmaGeneratedAtlasInLinearWorkingSpace()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            Color atlasValue = new Color(0.18f, 0.42f, 0.73f, 0.91f);
            Texture2D generatedAtlas = Own(new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true));
            generatedAtlas.SetPixels(new[] { atlasValue, atlasValue, atlasValue, atlasValue });
            generatedAtlas.Apply(false, false);

            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.sRGB = true;
            channel.sourceTexture = generatedAtlas;
            channel.editable.Reset(generatedAtlas, Color.black);
            channel.composite = CreateRenderTexture("Texture Paint Linear Color Composite", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;

            set.RecomposeAll();

            Assert.That(channel.sRGB, Is.True, "Albedo should still export/import as sRGB.");
            Assert.That(channel.editable.Front.sRGB, Is.False,
                "Compute-writable paint targets must not use sRGB storage.");
            Assert.That(channel.composite.sRGB, Is.False,
                "The material preview must sample the composited linear values without a second decode.");
            AssertColor(ReadCenter(channel.editable.Front), atlasValue, 0.004f);
            AssertColor(ReadCenter(channel.composite), atlasValue, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void FirstFollowStrokeStampUsesResolvedWorldDirectionWithoutPreviousUv()
        {
            StrokeSample first = new StrokeSample(Vector3.zero, Vector3.forward, new Vector2(0.5f, 0.5f), 0, 0)
            {
                previousUV = new Vector2(0.5f, 0.5f),
                direction = Vector3.up
            };
            BrushProjection projection = new BrushProjection
            {
                uvToBrush = new Vector4(4f, 0f, 0f, 4f),
                worldTangent = Vector3.right,
                worldBitangent = Vector3.up,
                valid = true
            };

            Vector2 motion = TexturePaintStageWindow.ResolveFollowStrokeMotion(first, projection);

            Assert.That(motion.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(motion.y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void SplineAuthoringOverlayOnlyBelongsToTheActivePathLayer()
        {
            TextureSet set = new TextureSet();
            TexturePaintLayer path = set.AddSplineLayer("Path");
            path.visible = false;
            TexturePaintLayer paint = set.AddLayer("Paint");
            // Unity can materialize an inline serializable class even when a null spline was
            // saved. Editing mode must remain driven by the explicit layer kind.
            paint.spline = new TexturePaintSpline { name = "Stale Serialized Payload" };

            Assert.That(TexturePaintStageWindow.IsActiveSplineAuthoringLayer(set, 0), Is.False,
                "An inactive path must leave only its rasterized layer result visible.");
            Assert.That(TexturePaintStageWindow.IsActiveSplineAuthoringLayer(set, 1), Is.False);

            set.activeLayerIndex = 0;
            Assert.That(TexturePaintStageWindow.IsActiveSplineAuthoringLayer(set, 0), Is.True,
                "The selected path remains authorable even when its raster-result visibility is off.");

            set.activeLayerIndex = set.layers.IndexOf(paint);
            Assert.That(TexturePaintStageWindow.IsActiveSplineAuthoringLayer(set, 0), Is.False);
            Assert.That(paint.IsSplineLayer, Is.False);
        }

        [Test]
        public void RibbonSegmentsShareExactCrossSectionsAtBends()
        {
            List<StrokeSample> centerline = new List<StrokeSample>
            {
                new StrokeSample(new Vector3(0f, 0f, 0f), Vector3.forward, Vector2.zero, 0, 0),
                new StrokeSample(new Vector3(0f, 1f, 0f), Vector3.forward, Vector2.zero, 0, 0),
                new StrokeSample(new Vector3(1f, 1f, 0f), Vector3.forward, Vector2.zero, 0, 0)
            };

            List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                centerline, 0.25f, 0.5f);

            Assert.That(segments, Has.Count.EqualTo(2));
            Assert.That(Vector3.Distance(
                    new Vector3(segments[0].leftEndAlong.x, segments[0].leftEndAlong.y,
                        segments[0].leftEndAlong.z),
                    new Vector3(segments[1].leftStartAlong.x, segments[1].leftStartAlong.y,
                        segments[1].leftStartAlong.z)),
                Is.LessThan(0.000001f));
            Assert.That(Vector3.Distance(
                    new Vector3(segments[0].rightEndFlow.x, segments[0].rightEndFlow.y,
                        segments[0].rightEndFlow.z),
                    new Vector3(segments[1].rightStartFlow.x, segments[1].rightStartFlow.y,
                        segments[1].rightStartFlow.z)),
                Is.LessThan(0.000001f));
            Assert.That(segments[0].leftEndAlong.w,
                Is.EqualTo(segments[1].leftStartAlong.w).Within(0.000001f));
            Assert.That(segments[1].leftEndAlong.w % 1f, Is.EqualTo(0f).Within(0.000001f),
                "The fitted source repeat must terminate on a complete tile boundary.");
        }

        [TestCase(-179f, -180)]
        [TestCase(-100f, -90)]
        [TestCase(-44f, 0)]
        [TestCase(46f, 90)]
        [TestCase(150f, 180)]
        public void RibbonRotationSnapsToTheOrientationsExposedByTheUI(float input, int expected)
        {
            Assert.That(TexturePaintStageWindow.SnapRibbonRotation(input), Is.EqualTo(expected));
        }

        [TestCase(599.9f, false)]
        [TestCase(600f, true)]
        [TestCase(900f, true)]
        public void LayerRowExtendedControlsRespondToAvailableWidth(float width, bool expected)
        {
            Assert.That(TexturePaintStageWindow.ShouldShowLayerRowExtendedControls(width),
                Is.EqualTo(expected));
        }

        [Test]
        public void RibbonProjectionRasterizesOneContinuousWorldStripIntoLayerUVs()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddLayer("Ribbon Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.2f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/UMA/OverlayPainter/Shaders/RibbonProjection.shader");
            Assert.That(ribbonShader, Is.Not.Null);
            Assert.That(ribbonShader.isSupported, Is.True);
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(0.5f, 0f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0f), 0, 0) { color = Color.green, hasColor = true },
                    new StrokeSample(new Vector3(0.5f, 1f, 0f), Vector3.forward,
                        new Vector2(0.5f, 1f), 0, 1) { color = Color.green, hasColor = true }
                };
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                AssertColor(pixels[(size / 2) * size + size / 2], Color.green, 0.02f);
                Assert.That(pixels[(size / 2) * size + 2].a, Is.LessThan(0.02f));
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonProjectionPreservesContributionWithSharedMirroredUVs()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);

            // The first quad is beneath the ribbon. The second is far outside its projection
            // depth, but occupies the exact same texels with horizontally mirrored UVs. Pants
            // and other symmetrical slots commonly use this layout. The later non-contributing
            // triangles must not overwrite the first quad's valid paint result.
            fixture.mesh.Clear();
            fixture.mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, 2f), new Vector3(1f, 0f, 2f),
                new Vector3(1f, 1f, 2f), new Vector3(0f, 1f, 2f)
            };
            fixture.mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
            };
            fixture.mesh.uv = new[]
            {
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                Vector2.right, Vector2.zero, Vector2.up, Vector2.one
            };
            fixture.mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7
            };
            fixture.mesh.RecalculateBounds();
            fixture.set.surface.triangleSlotNames = new[] { "Body", "Body", "Body", "Body" };
            fixture.set.surface.triangleIslands = new[] { 0, 0, 0, 0 };

            TexturePaintLayer layer = fixture.set.AddLayer("Shared UV Ribbon Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.2f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/UMA/OverlayPainter/Shaders/RibbonProjection.shader");
            Assert.That(ribbonShader, Is.Not.Null);
            Assert.That(ribbonShader.isSupported, Is.True);
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 0.25f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(0.5f, 0f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0f), 0, 0) { color = Color.green, hasColor = true },
                    new StrokeSample(new Vector3(0.5f, 1f, 0f), Vector3.forward,
                        new Vector2(0.5f, 1f), 0, 1) { color = Color.green, hasColor = true }
                };
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Color center = pixels[(size / 2) * size + size / 2];
                Assert.That(center.g, Is.EqualTo(Color.green.g).Within(0.02f),
                    "A mirrored UV owner outside the ribbon must not erase the contributing owner.");
                AssertColor(center, Color.green, 0.02f);
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonProjectionDoesNotSplitTwistedRibbonQuadsIntoPlanarCoverage()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            const int grid = 17;
            Vector3[] vertices = new Vector3[grid * grid];
            Vector3[] normals = new Vector3[grid * grid];
            Vector2[] uvs = new Vector2[grid * grid];
            int[] triangles = new int[(grid - 1) * (grid - 1) * 6];
            for (int y = 0; y < grid; y++)
            {
                float v = y / (float)(grid - 1);
                for (int x = 0; x < grid; x++)
                {
                    float u = x / (float)(grid - 1);
                    int vertex = y * grid + x;
                    float worldX = Mathf.Lerp(0.25f, 0.75f, u);
                    float worldZ = v * Mathf.Lerp(0.08f, -0.08f, u);
                    vertices[vertex] = new Vector3(worldX, v, worldZ);
                    uvs[vertex] = new Vector2(worldX, v);
                    Vector3 derivativeAcross = new Vector3(0.5f, 0f, -0.16f * v);
                    Vector3 derivativeAlong = new Vector3(0f, 1f, Mathf.Lerp(0.08f, -0.08f, u));
                    normals[vertex] = Vector3.Cross(derivativeAcross, derivativeAlong).normalized;
                }
            }
            int triangleOffset = 0;
            for (int y = 0; y < grid - 1; y++)
                for (int x = 0; x < grid - 1; x++)
                {
                    int a = y * grid + x;
                    int b = a + 1;
                    int c = a + grid;
                    int d = c + 1;
                    triangles[triangleOffset++] = a;
                    triangles[triangleOffset++] = b;
                    triangles[triangleOffset++] = d;
                    triangles[triangleOffset++] = a;
                    triangles[triangleOffset++] = d;
                    triangles[triangleOffset++] = c;
                }
            fixture.mesh.Clear();
            fixture.mesh.vertices = vertices;
            fixture.mesh.normals = normals;
            fixture.mesh.uv = uvs;
            fixture.mesh.triangles = triangles;
            fixture.mesh.RecalculateBounds();
            int surfaceTriangleCount = triangles.Length / 3;
            fixture.set.surface.triangleSlotNames = new string[surfaceTriangleCount];
            fixture.set.surface.triangleIslands = new int[surfaceTriangleCount];
            for (int triangle = 0; triangle < surfaceTriangleCount; triangle++)
                fixture.set.surface.triangleSlotNames[triangle] = "Body";

            TexturePaintLayer layer = fixture.set.AddLayer("Twisted Ribbon Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.2f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/UMA/OverlayPainter/Shaders/RibbonProjection.shader");
            Assert.That(ribbonShader, Is.Not.Null);
            Assert.That(ribbonShader.isSupported, Is.True);
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 0.025f;
                context.normalAngleLimit = 90f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(0.5f, 0f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0f), 0, 0) { color = Color.green, hasColor = true },
                    new StrokeSample(new Vector3(0.5f, 1f, 0f), Vector3.forward,
                        new Vector2(0.5f, 1f), 0, 1) { color = Color.green, hasColor = true }
                };
                List<TexturePaintRibbonSegment> segments = new List<TexturePaintRibbonSegment>
                {
                    new TexturePaintRibbonSegment
                    {
                        leftStartAlong = new Vector4(0.25f, 0f, 0f, 0f),
                        rightStartFlow = new Vector4(0.75f, 0f, 0f, 1f),
                        leftEndAlong = new Vector4(0.25f, 1f, 0.08f, 1f),
                        rightEndFlow = new Vector4(0.75f, 1f, -0.08f, 1f),
                        normalStartPressure = new Vector4(0f, 0f, 1f, 1f),
                        normalEndPressure = new Vector4(0f, 0f, 1f, 1f),
                        colorStart = Color.green,
                        colorEnd = Color.green
                    }
                };

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                for (int y = 4; y < size - 4; y++)
                    for (int x = 17; x < 47; x++)
                        Assert.That(pixels[y * size + x].a, Is.GreaterThan(0.98f),
                            $"Twisted ribbon coverage hole at ({x}, {y}).");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void OpenRibbonDoesNotSmearSourceRowsBeyondItsEndpoints()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            fixture.mesh.vertices = new[]
            {
                new Vector3(0f, -0.25f, 0f), new Vector3(1f, -0.25f, 0f),
                new Vector3(1f, 1.25f, 0f), new Vector3(0f, 1.25f, 0f)
            };
            fixture.mesh.RecalculateBounds();

            TexturePaintLayer layer = fixture.set.AddLayer("Open Ribbon Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.2f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/UMA/OverlayPainter/Shaders/RibbonProjection.shader");
            Assert.That(ribbonShader, Is.Not.Null);
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 1f;
                context.normalAngleLimit = 90f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>();
                for (int sampleIndex = 0; sampleIndex <= 10; sampleIndex++)
                {
                    float y = sampleIndex * 0.1f;
                    centerline.Add(new StrokeSample(new Vector3(0.5f, y, 0f), Vector3.forward,
                        new Vector2(0.5f, (y + 0.25f) / 1.5f), 0, sampleIndex)
                    { color = Color.green, hasColor = true });
                }
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Assert.That(pixels[10 * size + size / 2].a, Is.LessThan(0.02f),
                    "An open ribbon must not extend before its first cross-section.");
                Assert.That(pixels[53 * size + size / 2].a, Is.LessThan(0.02f),
                    "An open ribbon must not extend beyond its final cross-section.");
                Assert.That(pixels[(size / 2) * size + size / 2].a, Is.GreaterThan(0.98f));
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonBendRotationBeginsBeforeCornerAcrossThreeTileWindow()
        {
            List<StrokeSample> centerline = new List<StrokeSample>();
            for (int i = 0; i <= 4; i++)
                centerline.Add(new StrokeSample(new Vector3(0f, i * 0.5f, 0f), Vector3.forward,
                    Vector2.zero, 0, 0));
            for (int i = 1; i <= 4; i++)
                centerline.Add(new StrokeSample(new Vector3(i * 0.5f, 2f, 0f), Vector3.forward,
                    Vector2.zero, 0, 0));

            List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                centerline, 0.25f, 1f);

            Vector3 preCornerCrossSection = new Vector3(
                segments[2].rightEndFlow.x - segments[2].leftEndAlong.x,
                segments[2].rightEndFlow.y - segments[2].leftEndAlong.y,
                segments[2].rightEndFlow.z - segments[2].leftEndAlong.z).normalized;
            Assert.That(Mathf.Abs(preCornerCrossSection.y), Is.GreaterThan(0.05f),
                "The cross section before the corner should already share the turn deformation.");
        }

        [Test]
        public void ClearModificationsRestoresSourceAndRemovesLayersAndBaseStrokes()
        {
            Color sourceColor = new Color(0.12f, 0.34f, 0.78f, 0.91f);
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true));
            source.SetPixels(new[] { sourceColor, sourceColor, sourceColor, sourceColor });
            source.Apply(false, false);
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.sourceTexture = source;
            channel.editable.Reset(null, Color.red);
            set.baseStrokes.Add(new TexturePaintStrokeRecord());
            TexturePaintLayer paint = set.AddFillLayer("Paint", TexturePaintChannel.Albedo, Color.green);
            paint.strokes.Add(new TexturePaintStrokeRecord());

            set.ClearModifications();

            Assert.That(set.layers, Is.Empty);
            Assert.That(set.activeLayerIndex, Is.EqualTo(-1));
            Assert.That(set.baseStrokes, Is.Empty);
            AssertColor(ReadCenter(channel.editable.Front), sourceColor, 0.004f);
            AssertColor(ReadCenter(channel.editable.Back), sourceColor, 0.004f);
        }

        [Test]
        public void CrossSlotPathCreatesAReusableLinkedResultLayer()
        {
            TextureSet torso = CreateSet(TexturePaintChannel.Albedo, Color.white);
            TextureStore store = CreateStore(torso);
            TextureSet legs = CreateSet(TexturePaintChannel.Albedo, Color.white);
            legs.persistentId = "release-legs";
            AddSet(store, legs);
            TexturePaintLayer path = torso.AddSplineLayer("Seam Path");
            TexturePaintLayer existingLegPaint = legs.AddLayer("Leg Detail");
            int previousLegLayer = legs.activeLayerIndex;
            const string key = "texture-paint-spline:seam-path";

            Dictionary<TextureSet, int> previous = TexturePaintStageWindow.ActivateSplineResultLayers(
                torso, path, key, new[] { torso, legs });

            Assert.That(path.proceduralGroupKey, Is.EqualTo(key));
            Assert.That(previous[legs], Is.EqualTo(previousLegLayer));
            Assert.That(legs.layers[legs.activeLayerIndex], Is.Not.SameAs(existingLegPaint));
            Assert.That(legs.layers[legs.activeLayerIndex].proceduralGroupKey, Is.EqualTo(key));
            int linkedLayerCount = legs.layers.Count;
            TexturePaintStageWindow.ActivateSplineResultLayers(torso, path, key, new[] { torso, legs });
            Assert.That(legs.layers.Count, Is.EqualTo(linkedLayerCount), "Reapplying a path must reuse its linked layer.");
        }

        [Test]
        public void DocumentSaveReopenRestoresPixelsLayersSplinesMasksAndPluginProvenance()
        {
            Material material = Own(new Material(Shader.Find("Standard")) { name = "Persistence Material" });
            Mesh mesh = Own(CreateQuadMesh());
            TextureSet originalSet = CreateSet(TexturePaintChannel.Albedo,
                new Color(0.17f, 0.37f, 0.71f, 0.83f), material, mesh);
            TextureStore originalStore = CreateStore(originalSet);
            TexturePaintLayer paint = originalSet.AddLayer("Paint Detail");
            paint.logicalLayerId = "logical-skin-detail";
            paint.paintTargetId = "udim:body";
            paint.pluginId = "com.uma.tests.v2";
            paint.pluginVersion = "2.4.1";
            paint.pluginParametersJson = "{\"amount\":0.75}";
            paint.effects.outerShadow.enabled = true;
            paint.effects.outerShadow.channel = TexturePaintChannel.Albedo;
            paint.effects.outerShadow.color = new Color(0.12f, 0.23f, 0.34f, 0.8f);
            paint.effects.outerShadow.width = 17f;
            paint.effects.outerShadow.offset = new Vector2(3f, -4f);
            paint.effects.outerShadow.curve = AnimationCurve.Linear(0f, 1f, 1f, 0.2f);
            EditableTextureTarget paintPixels = originalSet.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay);
            paintPixels.Reset(null, new Color(0.77f, 0.23f, 0.11f, 0.61f));
            TexturePaintLayer splineLayer = originalSet.AddSplineLayer("Surface Path");
            splineLayer.spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            splineLayer.spline.AddPoint(Vector3.one, Vector2.one, 0, 1, Vector3.forward);
            splineLayer.spline.SetWorldControl(0, false, new Vector3(0.3f, 0.7f), new Vector2(0.3f, 0.7f));
            splineLayer.masks.Add(new TexturePaintMask
            {
                name = "Path Island",
                kind = TexturePaintMaskKind.UVIsland,
                uvIslandIndices = new List<int> { 0 }
            });
            TexturePaintMaskStack globalMasks = new TexturePaintMaskStack();
            globalMasks.Add(new TexturePaintMask { name = "Body Slot", kind = TexturePaintMaskKind.Slot, surfaceIndex = 0 });

            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            AssetDatabase.CreateAsset(document, Folder + "/Round Trip Document.asset");
            TexturePaintDocumentStorage.Save(document, originalStore, globalMasks);
            string documentId = document.documentId;
            string revision = document.revisionId;
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(Folder + "/Round Trip Document.asset", ImportAssetOptions.ForceSynchronousImport);
            TexturePaintDocument reopened = AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(
                Folder + "/Round Trip Document.asset");

            TextureSet restoredSet = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            TextureStore restoredStore = CreateStore(restoredSet);
            TexturePaintDocumentStorage.Restore(reopened, restoredStore);
            TexturePaintMaskStack restoredMasks = new TexturePaintMaskStack();
            TexturePaintDocumentStorage.RestoreMasks(reopened, restoredMasks);

            Assert.That(reopened.documentId, Is.EqualTo(documentId));
            Assert.That(reopened.revisionId, Is.EqualTo(revision));
            AssertColor(ReadCenter(restoredSet.GetChannel(TexturePaintChannel.Albedo).Texture),
                new Color(0.17f, 0.37f, 0.71f, 0.83f), 0.004f);
            Assert.That(restoredSet.layers, Has.Count.EqualTo(2));
            Assert.That(restoredSet.layers[0].kind, Is.EqualTo(TexturePaintLayerKind.Paint));
            Assert.That(restoredSet.layers[0].IsSplineLayer, Is.False,
                "A domain/asset reload must not classify a Paint layer from an inline spline payload.");
            Assert.That(restoredSet.layers[0].spline, Is.Null);
            Assert.That(restoredSet.layers[0].pluginId, Is.EqualTo("com.uma.tests.v2"));
            Assert.That(restoredSet.layers[0].logicalLayerId, Is.EqualTo("logical-skin-detail"));
            Assert.That(restoredSet.layers[0].paintTargetId, Is.EqualTo("udim:body"));
            Assert.That(restoredSet.layers[0].pluginParametersJson, Does.Contain("0.75"));
            Assert.That(restoredSet.layers[0].effects.outerShadow.enabled, Is.True);
            Assert.That(restoredSet.layers[0].effects.outerShadow.width, Is.EqualTo(17f));
            Assert.That(restoredSet.layers[0].effects.outerShadow.offset,
                Is.EqualTo(new Vector2(3f, -4f)));
            Assert.That(restoredSet.layers[0].effects.outerShadow.curve.Evaluate(1f),
                Is.EqualTo(0.2f).Within(0.001f));
            AssertColor(ReadCenter(restoredSet.layers[0].channels[TexturePaintChannel.Albedo].Front),
                new Color(0.77f, 0.23f, 0.11f, 0.61f), 0.004f);
            Assert.That(restoredSet.layers[1].spline.PointCount, Is.EqualTo(2));
            Assert.That(restoredSet.layers[1].spline.worldOutControls[0].y, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(restoredSet.layers[1].masks, Has.Count.EqualTo(1));
            Assert.That(restoredMasks.Masks, Has.Count.EqualTo(1));
        }

        [TestCase(TexturePaintExportBitDepth.Eight, 0.012f)]
        [TestCase(TexturePaintExportBitDepth.Sixteen, 0.003f)]
        [TestCase(TexturePaintExportBitDepth.HalfFloat, 0.004f)]
        public void ExportRoundTripPreservesDeclaredPrecision(TexturePaintExportBitDepth bitDepth, float tolerance)
        {
            Color source = bitDepth == TexturePaintExportBitDepth.HalfFloat
                ? new Color(1.75f, 0.5678f, 0.8765f, 0.4321f)
                : new Color(0.1234f, 0.5678f, 0.8765f, 0.4321f);
            TextureSet set = CreateSet(TexturePaintChannel.Metallic, source);
            TextureStore store = CreateStore(set);
            TexturePaintExportTemplate template = CreateTemplate(bitDepth);
            ConfigureExportDescriptor(set, bitDepth, new UMAMaterial.TextureChannelLayout
            {
                mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                red = UMAMaterial.TextureChannelUsage.Metallic,
                green = UMAMaterial.TextureChannelUsage.Unused,
                blue = UMAMaterial.TextureChannelUsage.Unused,
                alpha = UMAMaterial.TextureChannelUsage.Unused
            });

            TexturePaintExportResult result = TexturePaintExporter.Export(store, set, null, template, null);
            Assert.That(result.texturePaths, Has.Count.EqualTo(1));
            Assert.That(result.texturePaths[0], Does.EndWith(bitDepth == TexturePaintExportBitDepth.HalfFloat ? ".exr" : ".png"));
            Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(result.texturePaths[0]);
            Assert.That(imported, Is.Not.Null);
            AssertColor(ReadTextureCenter(imported), new Color(source.r, 0f, 0f, 0f), tolerance);
        }

        [Test]
        public void CustomPackedMapRoundTripPreservesSemanticComponents()
        {
            TextureSet set = CreateSet(TexturePaintChannel.Metallic, new Color(0.2f, 0f, 0f, 1f));
            AddChannel(set, TexturePaintChannel.AmbientOcclusion, new Color(0.65f, 0f, 0f, 1f));
            AddChannel(set, TexturePaintChannel.Roughness, new Color(0.8f, 0f, 0f, 1f));
            TextureStore store = CreateStore(set);
            TexturePaintExportTemplate template = CreateTemplate(TexturePaintExportBitDepth.Sixteen);
            ConfigureExportDescriptor(set, TexturePaintExportBitDepth.Sixteen,
                new UMAMaterial.TextureChannelLayout
                {
                    mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                    red = UMAMaterial.TextureChannelUsage.Metallic,
                    green = UMAMaterial.TextureChannelUsage.Smoothness,
                    blue = UMAMaterial.TextureChannelUsage.AmbientOcclusion,
                    alpha = UMAMaterial.TextureChannelUsage.Unused
                });
            RenderTexture packed = CreateRenderTexture("Release Packed Output", 16,
                RenderTextureFormat.ARGBHalf);
            Graphics.Blit(Texture2D.whiteTexture, packed);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = packed;
            GL.Clear(false, true, new Color(0.2f, 0.2f, 0.65f, 1f));
            RenderTexture.active = previous;
            set.physicalChannelGroups["_MetallicGlossMap"] = new TexturePhysicalChannelGroup
            {
                materialProperty = "_MetallicGlossMap",
                packed = packed
            };

            TexturePaintExportResult result = TexturePaintExporter.Export(store, set, null, template, null);
            Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(result.texturePaths[0]);
            Color actual = ReadTextureCenter(imported);
            AssertColor(actual, new Color(0.2f, 0.2f, 0.65f, 0f), 0.004f);
        }

        [Test]
        public void UmaMaterialLayoutUnpacksAndRepacksCustomPhysicalComponents()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            Material preview = Own(new Material(Shader.Find("Standard")) { name = "Custom Packed Preview" });
            Texture2D source = CreateSolidTexture(new Color(0.2f, 0.65f, 0.37f, 0.25f));
            preview.SetTexture("_MetallicGlossMap", source);
            UMAMaterial umaMaterial = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            umaMaterial.material = preview;
            umaMaterial.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.Texture,
                    textureFormat = RenderTextureFormat.ARGB32,
                    materialPropertyName = "_MetallicGlossMap",
                    sourceTextureName = "_MetallicGlossMap",
                    DownSample = 1,
                    textureChannelLayout = new UMAMaterial.TextureChannelLayout
                    {
                        mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                        red = UMAMaterial.TextureChannelUsage.Metallic,
                        green = UMAMaterial.TextureChannelUsage.AmbientOcclusion,
                        alpha = UMAMaterial.TextureChannelUsage.Smoothness
                    }
                }
            };

            Mesh mesh = Own(CreateQuadMesh());
            MeshReconstructionResult reconstruction = new MeshReconstructionResult();
            reconstruction.surfaces.Add(new ReconstructedSurface
            {
                index = 0,
                mesh = mesh,
                previewMaterial = preview,
                sourceMaterial = preview,
                umaMaterial = umaMaterial,
                slotName = "Body",
                slotNames = new List<string> { "Body" },
                triangleSlotNames = new[] { "Body", "Body" },
                triangleIslands = new[] { 0, 0 }
            });

            TextureStore store = new TextureStore();
            ownedStores.Add(store);
            store.Initialize(reconstruction, 128,
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"),
                TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute"));
            TextureSet set = store.Sets[0];

            Assert.That(set.physicalChannelGroups.ContainsKey("_MetallicGlossMap"), Is.True);
            Assert.That(ReadCenter(set.GetChannel(TexturePaintChannel.Metallic).Texture).r,
                Is.EqualTo(0.2f).Within(0.01f));
            Assert.That(ReadCenter(set.GetChannel(TexturePaintChannel.AmbientOcclusion).Texture).r,
                Is.EqualTo(0.65f).Within(0.01f));
            Assert.That(ReadCenter(set.GetChannel(TexturePaintChannel.Roughness).Texture).r,
                Is.EqualTo(0.75f).Within(0.01f));

            Texture2D metallic = CreateSolidTexture(new Color(0.8f, 0.8f, 0.8f, 1f));
            Texture2D occlusion = CreateSolidTexture(new Color(0.3f, 0.3f, 0.3f, 1f));
            Texture2D roughness = CreateSolidTexture(new Color(0.4f, 0.4f, 0.4f, 1f));
            set.GetChannel(TexturePaintChannel.Metallic).editable.Reset(metallic, Color.black);
            set.GetChannel(TexturePaintChannel.AmbientOcclusion).editable.Reset(occlusion, Color.black);
            set.GetChannel(TexturePaintChannel.Roughness).editable.Reset(roughness, Color.white);
            set.BindPreviewTextures();

            Color packed = ReadCenter(set.physicalChannelGroups["_MetallicGlossMap"].packed);
            AssertColor(packed, new Color(0.8f, 0.3f, 0.37f, 0.6f), 0.01f);
        }

        [Test]
        public void StableSurfaceIdentityDistinguishesSharedSlotsAndSeparateMaterials()
        {
            Material shared = Own(new Material(Shader.Find("Standard")) { name = "Shared Material" });
            Material separate = Own(new Material(Shader.Find("Standard")) { name = "Separate Material" });
            Mesh mesh = Own(CreateQuadMesh());
            TextureSet torso = CreateSet(TexturePaintChannel.Albedo, Color.white, shared, mesh);
            ConfigureSlot(torso, "Torso", 0);
            TextureSet legs = CreateSet(TexturePaintChannel.Albedo, Color.white, shared, mesh);
            ConfigureSlot(legs, "Legs", 1);
            TextureSet torsoSeparate = CreateSet(TexturePaintChannel.Albedo, Color.white, separate, mesh);
            ConfigureSlot(torsoSeparate, "Torso", 2);
            TextureStore store = CreateStore(torso);
            AddSet(store, legs);
            AddSet(store, torsoSeparate);

            TexturePaintDocumentStorage.AssignStableSurfaceIds(store);

            Assert.That(torso.persistentId, Is.Not.EqualTo(legs.persistentId));
            Assert.That(torso.persistentId, Is.Not.EqualTo(torsoSeparate.persistentId));
            Assert.That(legs.persistentId, Is.Not.EqualTo(torsoSeparate.persistentId));
            string firstIdentity = torso.persistentId;
            TexturePaintDocumentStorage.AssignStableSurfaceIds(store);
            Assert.That(torso.persistentId, Is.EqualTo(firstIdentity));
        }

        [Test]
        public void ToolRailUsesTheFirstElevenOrderedSpriteSheetSlices()
        {
            for (int index = 0; index < 11; index++)
            {
                Sprite sprite = TexturePaintStageWindow.GetToolRailIcon(index);
                Assert.That(sprite, Is.Not.Null, "Missing TexturePaintIcons sprite at index " + index + ".");
                Assert.That(sprite.name, Is.EqualTo("TexturePaintIcons_" + index));
                Assert.That(sprite.texture, Is.Not.Null);
            }
        }

        private TexturePaintExportTemplate CreateTemplate(TexturePaintExportBitDepth bitDepth)
        {
            TexturePaintExportTemplate template = Own(ScriptableObject.CreateInstance<TexturePaintExportTemplate>());
            template.outputFolder = Folder + "/Export";
            template.filenamePattern = "roundtrip_{channel}_{resolution}";
            template.scope = TexturePaintExportScope.AllMaterials;
            template.overwritePolicy = TexturePaintOverwritePolicy.Overwrite;
            template.resolution = 32;
            template.bitDepth = bitDepth;
            template.padding = 0;
            template.exportLogicalChannels = true;
            template.exportMaterialPacking = false;
            template.createOrUpdateOverlay = false;
            template.createMaterialOverride = false;
            template.updateRecipeReferences = false;
            return template;
        }

        private void ConfigureExportDescriptor(TextureSet set, TexturePaintExportBitDepth bitDepth,
            UMAMaterial.TextureChannelLayout layout)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = Own(new Material(shader) { name = "Release Export Material" });
            Texture2D source = CreateSolidTexture(Color.black);
            material.SetTexture("_MetallicGlossMap", source);
            UMAMaterial uma = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            uma.name = "Release Export UMA Material";
            uma.material = material;
            uma.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.Texture,
                    textureFormat = RenderTextureFormat.ARGBHalf,
                    materialPropertyName = "_MetallicGlossMap",
                    sourceTextureName = "MaskMap",
                    DownSample = 1,
                    textureChannelLayout = layout,
                    textureChannelOutput = new UMAMaterial.TextureChannelOutputSettings
                    {
                        mode = UMAMaterial.TextureChannelOutputMode.Custom,
                        encoding = bitDepth == TexturePaintExportBitDepth.HalfFloat
                            ? UMAMaterial.TextureChannelOutputEncoding.ExrHalf
                            : bitDepth == TexturePaintExportBitDepth.Sixteen
                                ? UMAMaterial.TextureChannelOutputEncoding.Png16
                                : UMAMaterial.TextureChannelOutputEncoding.Png8,
                        importerType = UMAMaterial.TextureChannelImporterType.Default,
                        colorSpace = UMAMaterial.TextureChannelColorSpace.Linear,
                        alphaSource = UMAMaterial.TextureChannelAlphaSource.FromInput,
                        compression = UMAMaterial.TextureChannelImportCompression.Uncompressed,
                        normalConvention = UMAMaterial.TextureChannelNormalConvention.OpenGL,
                        generateMipMaps = false,
                        filterMode = FilterMode.Bilinear,
                        anisoLevel = 1,
                        maxTextureSize = 8192,
                        platformOverrides = System.Array.Empty<UMAMaterial.TextureChannelPlatformOverrideSettings>()
                    }
                }
            };
            set.umaMaterial = uma;
            set.surface.sourceMaterial = material;
            set.surface.previewMaterial = material;
            foreach (TextureChannelTarget target in set.channels.Values)
            {
                target.materialProperty = "_MetallicGlossMap";
                target.umaChannelIndex = 0;
                target.sourceTexture = source;
            }
            set.materialCapability = TexturePaintMaterialCapabilityService.Compile(uma, material,
                new Texture[] { source }, true);
        }

        private TextureSet CreateSet(TexturePaintChannel channel, Color clear, Material material = null, Mesh mesh = null)
        {
            TextureSet set = new TextureSet
            {
                persistentId = "release-surface",
                surface = new ReconstructedSurface
                {
                    index = 0,
                    rendererIndex = 0,
                    sourceSubmeshIndex = 0,
                    sourceMaterial = material,
                    mesh = mesh,
                    slotName = "Body",
                    slotNames = new List<string> { "Body" },
                    triangleSlotNames = new[] { "Body", "Body" },
                    triangleIslands = new[] { 0, 0 }
                }
            };
            AddChannel(set, channel, clear);
            return set;
        }

        private static void AddChannel(TextureSet set, TexturePaintChannel channel, Color clear)
        {
            EditableTextureTarget editable = new EditableTextureTarget("Texture Paint Release " + channel,
                16, 16, RenderTextureFormat.ARGBHalf, null, clear);
            set.channels.Add(channel, new TextureChannelTarget
            {
                channel = channel,
                materialProperty = "_" + channel,
                sourceKeyword = channel.ToString(),
                format = RenderTextureFormat.ARGBHalf,
                sRGB = false,
                editable = editable
            });
        }

        private TextureStore CreateStore(TextureSet set)
        {
            TextureStore store = new TextureStore();
            FieldInfo setsField = typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic);
            ((List<TextureSet>)setsField.GetValue(store)).Add(set);
            ownedStores.Add(store);
            return store;
        }

        private static void AddSet(TextureStore store, TextureSet set)
        {
            FieldInfo setsField = typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic);
            ((List<TextureSet>)setsField.GetValue(store)).Add(set);
        }

        private static void ConfigureSlot(TextureSet set, string slot, int surfaceIndex)
        {
            set.surface.index = surfaceIndex;
            set.surface.slotName = slot;
            set.surface.slotNames = new List<string> { slot };
            set.surface.triangleSlotNames = new[] { slot, slot };
        }

        private static RenderTexture CreateRenderTexture(string name, int size, RenderTextureFormat format)
        {
            RenderTexture texture = new RenderTexture(new RenderTextureDescriptor(size, size, format, 0)
            {
                enableRandomWrite = true,
                sRGB = false
            }) { name = name, hideFlags = HideFlags.HideAndDontSave };
            texture.Create();
            return texture;
        }

        private static Color ReadTextureCenter(Texture texture)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, temporary);
            Color result = ReadCenter(temporary);
            RenderTexture.ReleaseTemporary(temporary);
            return result;
        }

        private static Color ReadCenter(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            int x = texture.width / 2, y = texture.height / 2;
            readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
            readback.Apply(false, false);
            Color result = readback.GetPixel(0, 0);
            Object.DestroyImmediate(readback);
            RenderTexture.active = previous;
            return result;
        }

        private static Color ReadPixel(RenderTexture texture, int x, int y)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
            readback.Apply(false, false);
            Color result = readback.GetPixel(0, 0);
            Object.DestroyImmediate(readback);
            RenderTexture.active = previous;
            return result;
        }

        private static void AssertColor(Color actual, Color expected, float tolerance)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), "red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), "green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), "blue");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance), "alpha");
        }

        private static Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "Texture Paint Persistence Quad",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[]
                {
                    new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1),
                    new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1)
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private T Own<T>(T value) where T : Object
        {
            ownedObjects.Add(value);
            return value;
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
