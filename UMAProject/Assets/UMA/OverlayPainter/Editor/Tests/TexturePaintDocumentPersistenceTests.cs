#if UNITY_INCLUDE_TESTS
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintDocumentPersistenceTests
    {
        private const string Folder = "Assets/UMAProjectData/Tests/OverlayPainter/GeneratedPersistenceTests";
        private string recoveryKey;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Folder);
            UMAPathUtility.EnsureAssetFolder(Folder);
            TexturePaintRecoveryStore.RecoveryFolderOverride = Folder + "/Recovery";
            recoveryKey = "test-" + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                TexturePaintRecoveryStore.Delete(recoveryKey);
                AssetDatabase.DeleteAsset(Folder);
            }
            finally { TexturePaintRecoveryStore.RecoveryFolderOverride = null; }
        }

        [Test]
        public void TransientDocumentIsNotAProjectAsset()
        {
            TexturePaintDocument document = TexturePaintDocumentStorage.CreateTransient(null);
            try
            {
                Assert.That(AssetDatabase.GetAssetPath(document), Is.Empty);
                Assert.That(document.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
                Assert.That(document.createdUtc, Is.Not.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }

        [Test]
        public void RecoveryJournalRoundTripsExternalPixelBlob()
        {
            TexturePaintDocument source = CreateDocumentWithPixels(new byte[] { 4, 8, 15, 16, 23, 42 });
            Sprite sourceSprite = CreateSpriteAsset(Folder + "/Channel Source.png");
            var savedChannel = new TexturePaintDocumentLayerChannel
            {
                channel = TexturePaintChannel.Normal,
                settings = new TexturePaintLayerChannelSettings
                {
                    channel = TexturePaintChannel.Normal,
                    enabled = false,
                    locked = true,
                    contribution = 0.37f,
                    opacity = 0.63f,
                    blendMode = TexturePaintBlendMode.Overlay
                }
            };
            savedChannel.SetSourceSettings(new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceSprite = sourceSprite,
                normalConvention = TexturePaintNormalConvention.DirectX,
                invert = true,
                tiling = new Vector2(2.5f, 3.5f),
                offset = new Vector2(-0.2f, 0.4f),
                rotation = -63f,
                projection = TexturePaintFillProjection.Triplanar,
                triplanarBlend = TexturePaintTriplanarBlend.Hard,
                blendOffset = 0.21f,
                blendSharpness = 7f
            });
            source.surfaces[0].layers.Add(new TexturePaintDocumentLayer
            {
                name = "Multi Channel Layer",
                channels = new System.Collections.Generic.List<TexturePaintDocumentLayerChannel>
                    { savedChannel }
            });
            try
            {
                TexturePaintRecoveryStore.SaveImmediate(source, recoveryKey);
                Assert.That(TexturePaintRecoveryStore.HasRecovery(recoveryKey), Is.True);
                Assert.That(TexturePaintRecoveryStore.RecoveryAssetPath,
                    Is.EqualTo(Folder + "/Recovery/painter_recovery.asset"));
                TexturePaintDocument recoveryAsset = AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(
                    TexturePaintRecoveryStore.RecoveryAssetPath);
                Assert.That(recoveryAsset, Is.Not.Null);
                Assert.That(recoveryAsset.recoverySnapshot, Is.True);
                Assert.That(recoveryAsset.recoveryContextKey, Is.EqualTo(recoveryKey));
                TexturePaintPixelData storedPixels = recoveryAsset.surfaces[0].baseChannels[0].pixels;
                Assert.That(storedPixels.compressedBytes, Is.Null.Or.Empty);
                Assert.That(storedPixels.dataAsset, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(storedPixels.dataAsset),
                    Does.StartWith(Folder + "/Recovery/painter_recovery Data/"));
                TexturePaintRecoveryStore.SaveImmediate(source, recoveryKey);
                Assert.That(AssetDatabase.FindAssets("t:TextAsset",
                    new[] { TexturePaintRecoveryStore.RecoveryDataFolder }).Length, Is.EqualTo(1),
                    "Saving an unchanged recovery should reuse its content-addressed data asset.");
                Assert.That(TexturePaintRecoveryStore.TryLoad(recoveryKey, out TexturePaintDocument restored,
                    out string error), Is.True, error);
                try
                {
                    TexturePaintPixelData pixels = restored.surfaces[0].baseChannels[0].pixels;
                    Assert.That(pixels.compressedBytes, Is.EqualTo(new byte[] { 4, 8, 15, 16, 23, 42 }));
                    Assert.That(pixels.recoveryBlobKey, Is.Null.Or.Empty);
                    Assert.That(AssetDatabase.GetAssetPath(restored), Is.Empty);
                    TexturePaintDocumentLayerChannel restoredChannel =
                        restored.surfaces[0].layers[0].channels[0];
                    TexturePaintChannelSourceSettings restoredSource = restoredChannel.GetSourceSettings();
                    Assert.That(restoredChannel.settings.enabled, Is.False);
                    Assert.That(restoredChannel.settings.locked, Is.True);
                    Assert.That(restoredChannel.settings.contribution, Is.EqualTo(0.37f));
                    Assert.That(restoredChannel.settings.opacity, Is.EqualTo(0.63f));
                    Assert.That(restoredChannel.settings.blendMode, Is.EqualTo(TexturePaintBlendMode.Overlay));
                    Assert.That(restoredSource.sourceSprite, Is.SameAs(sourceSprite));
                    Assert.That(restoredSource.normalConvention,
                        Is.EqualTo(TexturePaintNormalConvention.DirectX));
                    Assert.That(restoredSource.invert, Is.True);
                    Assert.That(restoredSource.tiling, Is.EqualTo(new Vector2(2.5f, 3.5f)));
                    Assert.That(restoredSource.offset, Is.EqualTo(new Vector2(-0.2f, 0.4f)));
                    Assert.That(restoredSource.rotation, Is.EqualTo(-63f));
                    Assert.That(restoredSource.projection,
                        Is.EqualTo(TexturePaintFillProjection.Triplanar));
                    Assert.That(restoredSource.triplanarBlend,
                        Is.EqualTo(TexturePaintTriplanarBlend.Hard));
                    Assert.That(restoredSource.blendOffset, Is.EqualTo(0.21f));
                    Assert.That(restoredSource.blendSharpness, Is.EqualTo(7f));
                }
                finally { UnityEngine.Object.DestroyImmediate(restored); }
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void RecoveryAssetIsOnlyOfferedAndDeletedForItsContext()
        {
            TexturePaintDocument source = CreateDocumentWithPixels(new byte[] { 2, 4, 6, 8 });
            try
            {
                TexturePaintRecoveryStore.SaveImmediate(source, recoveryKey);
                string otherKey = "other-" + Guid.NewGuid().ToString("N");
                Assert.That(TexturePaintRecoveryStore.HasRecovery(otherKey), Is.False);
                TexturePaintRecoveryStore.Delete(otherKey);
                Assert.That(AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(
                    TexturePaintRecoveryStore.RecoveryAssetPath), Is.Not.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void ProjectSaveExternalizesPixelsAndSaveAsOwnsIndependentBlob()
        {
            TexturePaintDocument source = CreateDocumentWithPixels(new byte[] { 1, 3, 3, 7 });
            TexturePaintDocument copiedSnapshot = null;
            try
            {
                string firstPath = Folder + "/First.asset";
                using (TexturePaintProjectSaveOperation first =
                    new TexturePaintProjectSaveOperation(source, null, firstPath))
                {
                    Complete(first);
                    Assert.That(first.HasError, Is.False, first.Error);
                    Assert.That(first.SavedDocument, Is.Not.Null);
                    TexturePaintPixelData firstPixels = first.SavedDocument.surfaces[0].baseChannels[0].pixels;
                    Assert.That(firstPixels.compressedBytes, Is.Null.Or.Empty);
                    Assert.That(firstPixels.dataAsset, Is.Not.Null);
                    string firstBlobPath = AssetDatabase.GetAssetPath(firstPixels.dataAsset);
                    Assert.That(firstBlobPath, Does.StartWith(Folder + "/First Data/"));

                    copiedSnapshot = UnityEngine.Object.Instantiate(first.SavedDocument);
                    copiedSnapshot.hideFlags = HideFlags.HideAndDontSave;
                    string secondPath = Folder + "/Second.asset";
                    using TexturePaintProjectSaveOperation second =
                        new TexturePaintProjectSaveOperation(copiedSnapshot, null, secondPath);
                    Complete(second);
                    Assert.That(second.HasError, Is.False, second.Error);
                    TexturePaintPixelData secondPixels = second.SavedDocument.surfaces[0].baseChannels[0].pixels;
                    string secondBlobPath = AssetDatabase.GetAssetPath(secondPixels.dataAsset);
                    Assert.That(secondBlobPath, Does.StartWith(Folder + "/Second Data/"));
                    Assert.That(secondBlobPath, Is.Not.EqualTo(firstBlobPath));
                    Assert.That(secondPixels.dataAsset.bytes, Is.EqualTo(firstPixels.dataAsset.bytes));
                }
            }
            finally
            {
                if (copiedSnapshot != null) UnityEngine.Object.DestroyImmediate(copiedSnapshot);
                if (source != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source)))
                    UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CompletedCaptureTransitionsToOneRecoveryWriterAndCompletes()
        {
            TexturePaintDocument source = TexturePaintDocumentStorage.CreateTransient(null);
            TextureStore store = new TextureStore();
            TexturePaintStageController controller = new TexturePaintStageController();
            TexturePaintStageWindow stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            string key = "transition-" + Guid.NewGuid().ToString("N");
            try
            {
                TexturePaintDocumentStorage.CaptureOperation capture = TexturePaintDocumentStorage.BeginCapture(
                    source, store, new System.Collections.Generic.Dictionary<EditableTextureTarget, long>(), true);
                Assert.That(capture.IsDone, Is.True);
                SetField(stage, "controller", controller);
                SetField(stage, "document", source);
                SetField(stage, "recoveryContextKey", key);
                SetField(stage, "persistenceCapture", capture);
                FieldInfo intentField = Field("persistenceIntent");
                intentField.SetValue(stage, Enum.Parse(intentField.FieldType, "Recovery"));

                Invoke(stage, "PersistenceUpdate");
                TexturePaintRecoveryWriteOperation firstWriter =
                    (TexturePaintRecoveryWriteOperation)Field("recoveryWrite").GetValue(stage);
                Assert.That(firstWriter, Is.Not.Null);

                Invoke(stage, "PersistenceUpdate");
                Assert.That(Field("recoveryWrite").GetValue(stage), Is.SameAs(firstWriter),
                    "A completed capture must not recreate its commit writer every editor update.");

                firstWriter.CompleteSynchronously();
                Invoke(stage, "PersistenceUpdate");
                Assert.That(Field("persistenceCapture").GetValue(stage), Is.Null);
                Assert.That(Field("recoveryWrite").GetValue(stage), Is.Null);
            }
            finally
            {
                TexturePaintRecoveryStore.Delete(key);
                TexturePaintDocument current = Field("document").GetValue(stage) as TexturePaintDocument;
                if (current != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(current)))
                    UnityEngine.Object.DestroyImmediate(current);
                if (source != null && source != current && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source)))
                    UnityEngine.Object.DestroyImmediate(source);
                controller.Dispose();
                store.Dispose();
                UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        [Test]
        public void CloseDuringBackgroundPersistenceIsAcceptedAndCompletesAfterThatSave()
        {
            TexturePaintDocument source = TexturePaintDocumentStorage.CreateTransient(null);
            TextureStore store = new TextureStore();
            TexturePaintStageWindow stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            TexturePaintDocumentStorage.CaptureOperation capture = null;
            try
            {
                capture = TexturePaintDocumentStorage.BeginCapture(source, store,
                    new System.Collections.Generic.Dictionary<EditableTextureTarget, long>(), true);
                SetField(stage, "persistenceCapture", capture);

                Assert.That(stage.RequestCloseStage(), Is.True,
                    "Closing the dock should be accepted while an autosave is running.");
                Assert.That(Field("closeAfterSave").GetValue(stage), Is.True,
                    "The in-flight autosave should become the final close save.");
                Assert.That(Field("persistenceStatus").GetValue(stage),
                    Is.EqualTo("Finishing save before closing…"));
            }
            finally
            {
                capture?.Cancel();
                TexturePaintDocument snapshot = capture?.Snapshot;
                if (snapshot != null && snapshot != source)
                    UnityEngine.Object.DestroyImmediate(snapshot);
                store.Dispose();
                UnityEngine.Object.DestroyImmediate(stage);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ExternalOperationCompletionRestartsAutosaveDebounce()
        {
            TexturePaintStageWindow stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            try
            {
                SetField(stage, "nextAutosaveTime", 0d);
                double before = EditorApplication.timeSinceStartup;

                stage.DeferAutosaveAfterExternalOperation();

                double deadline = (double)Field("nextAutosaveTime").GetValue(stage);
                Assert.That(deadline, Is.GreaterThanOrEqualTo(before + 29.9d));
            }
            finally { UnityEngine.Object.DestroyImmediate(stage); }
        }

        [Test]
        public void ProjectDocumentRoundTripsAllLayerSettingsAndEffects()
        {
            TexturePaintDocument source = TexturePaintDocumentStorage.CreateTransient(null);
            try
            {
                Sprite endpointSprite =
                    CreateSpriteAsset(Folder + "/Ribbon Endpoint Texture.png");
                Assert.That(endpointSprite, Is.Not.Null);
                Texture2D endpointTexture = endpointSprite.texture;
                Assert.That(endpointTexture, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(endpointTexture),
                    Is.EqualTo(Folder + "/Ribbon Endpoint Texture.png"));
                Texture2D overlayTexture2 = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Layer Effect Texture 2"
                };
                AssetDatabase.CreateAsset(overlayTexture2, Folder + "/Layer Effect Texture 2.asset");
                TexturePaintLayerEffects effects = CreateNonDefaultEffects();
                effects.textureOverlay.texture1 = endpointTexture;
                effects.textureOverlay.texture2 = overlayTexture2;
                TexturePaintFillSettings fill = new TexturePaintFillSettings
                {
                    generatorRevision = 7,
                    source = TexturePaintBrushSource.Texture,
                    invert = true,
                    color = new Color(0.11f, 0.22f, 0.33f, 0.44f),
                    normalConvention = TexturePaintNormalConvention.DirectX,
                    projection = TexturePaintFillProjection.Triplanar,
                    tiling = new Vector2(2.5f, -3.25f),
                    useFirstChannelTransform = true,
                    triplanarBlend = TexturePaintTriplanarBlend.Hard,
                    blendOffset = 0.17f,
                    blendSharpness = 11f
                };
                TexturePaintLayerSettings paint = new TexturePaintLayerSettings
                {
                    tool = TexturePaintTool.Clone,
                    channel = TexturePaintChannel.Emission,
                    source = TexturePaintBrushSource.Texture,
                    destination = TexturePaintSourceMode.SourceOverlay,
                    brushShape = BrushPreset.Shape.Stamp,
                    brushSize = 0.123f,
                    brushHardness = 0.27f,
                    brushFlow = 0.61f,
                    brushSpacing = 1.7f,
                    brushRotation = -90f,
                    brushBlendMode = TexturePaintBlendMode.Screen,
                    brushMirrorStroke = true,
                    brushAlignToStroke = true,
                    brushStampSprite = endpointSprite,
                    brushRandomizationVersion = 1,
                    brushRandomRotation = true,
                    brushRandomSizeVariation = true,
                    brushRandomSizeShrink = 0.21f,
                    brushRandomSizeGrow = 0.44f,
                    brushSplatter = true,
                    brushSplatterDistance = 0.73f,
                    brushRandomStrength = true,
                    brushFade = true,
                    brushAutoFade = true,
                    brushTaper = true,
                    brushAutoTaper = true,
                    brushFadeTaperLength = 0.91f,
                    color = new Color(0.8f, 0.2f, 0.6f, 0.4f),
                    normalConvention = TexturePaintNormalConvention.DirectX,
                    strength = 0.73f,
                    limitStrokeCoverage = true,
                    mirrorX = true,
                    stabilization = 0.31f,
                    directionSmoothing = 0.82f,
                    projectionDepth = 0.19f,
                    normalAngleLimit = 47f,
                    paintBackfaces = true,
                    pressureAffectsFlow = false,
                    pressureAffectsSize = true
                };
                TexturePaintSplineSettings path = new TexturePaintSplineSettings
                {
                    editorSettingsVersion = TexturePaintSplineSettings.CurrentEditorSettingsVersion,
                    editMode = TexturePaintPathEditMode.Adjust,
                    autoUpdate = false,
                    tool = TexturePaintTool.Paint,
                    channel = TexturePaintChannel.Roughness,
                    source = TexturePaintBrushSource.Color,
                    destination = TexturePaintSourceMode.SourceOverlay,
                    brushShape = BrushPreset.Shape.Square,
                    brushSize = 0.234f,
                    brushHardness = 0.38f,
                    brushFlow = 0.72f,
                    brushSpacing = 2.3f,
                    brushRotation = 90f,
                    brushBlendMode = TexturePaintBlendMode.Multiply,
                    brushMirrorStroke = true,
                    brushAlignToStroke = true,
                    brushStampSprite = endpointSprite,
                    brushRandomizationVersion = 1,
                    brushRandomRotation = true,
                    brushRandomSizeVariation = true,
                    brushRandomSizeShrink = 0.19f,
                    brushRandomSizeGrow = 0.52f,
                    brushSplatter = true,
                    brushSplatterDistance = 1.42f,
                    brushRandomStrength = true,
                    brushFade = true,
                    brushAutoFade = true,
                    brushTaper = true,
                    brushAutoTaper = true,
                    brushFadeTaperLength = 1.37f,
                    ribbonBeginningTexture = endpointTexture,
                    ribbonEndSprite = endpointSprite,
                    color = new Color(0.7f, 0.4f, 0.1f, 0.9f),
                    normalConvention = TexturePaintNormalConvention.DirectX,
                    strength = 0.64f,
                    limitStrokeCoverage = true,
                    mirrorX = true,
                    stabilization = 0.42f,
                    directionSmoothing = 0.67f,
                    projectionDepth = 0.28f,
                    normalAngleLimit = 63f,
                    paintBackfaces = true,
                    pressureAffectsFlow = false,
                    pressureAffectsSize = true,
                    pathMode = TexturePaintPathMode.Ribbon,
                    orientation = TexturePaintPathOrientation.FixedAxis,
                    startCap = TexturePaintPathCap.Butt,
                    endCap = TexturePaintPathCap.Square,
                    radialSymmetry = 5,
                    symmetryAxis = new Vector3(0.2f, 0.8f, 0.5f).normalized
                };
                TexturePaintDocumentSurface surface = new TexturePaintDocumentSurface
                {
                    stableId = "all-settings",
                    activeLayer = 2
                };
                surface.layers.Add(new TexturePaintDocumentLayer
                {
                    name = "Fill",
                    kind = TexturePaintLayerKind.Fill,
                    visible = false,
                    opacity = 0.43f,
                    blendMode = TexturePaintBlendMode.Multiply,
                    effects = effects.Clone(),
                    fillChannel = TexturePaintChannel.Metallic,
                    fillColor = fill.color,
                    fillSettings = fill
                });
                surface.layers.Add(new TexturePaintDocumentLayer
                {
                    name = "Paint",
                    kind = TexturePaintLayerKind.Paint,
                    opacity = 0.54f,
                    blendMode = TexturePaintBlendMode.Screen,
                    effects = effects.Clone(),
                    paintSettings = paint,
                    channels = new System.Collections.Generic.List<TexturePaintDocumentLayerChannel>
                    {
                        new TexturePaintDocumentLayerChannel
                        {
                            channel = TexturePaintChannel.Emission,
                            settings = new TexturePaintLayerChannelSettings
                            {
                                channel = TexturePaintChannel.Emission,
                                enabled = false,
                                locked = true,
                                contribution = 0.35f,
                                opacity = 0.46f,
                                blendMode = TexturePaintBlendMode.Add,
                                sourceSettings = new TexturePaintChannelSourceSettings
                                {
                                    source = TexturePaintBrushSource.Color,
                                    color = new Color(0.2f, 0.4f, 0.6f, 0.8f),
                                    invert = true,
                                    tiling = new Vector2(2f, 3f)
                                }
                            }
                        }
                    }
                });
                surface.layers.Add(new TexturePaintDocumentLayer
                {
                    name = "Path",
                    kind = TexturePaintLayerKind.Spline,
                    opacity = 0.65f,
                    blendMode = TexturePaintBlendMode.Overlay,
                    effects = effects.Clone(),
                    spline = new TexturePaintSpline { name = "Saved Ribbon", closed = true, worldSpace = true },
                    splineSettings = path,
                    pluginId = "test.plugin",
                    pluginVersion = "2.1",
                    pluginParametersJson = "{\"amount\":0.75}",
                    proceduralGroupKey = "saved-path"
                });
                var pluginParameters = new TexturePaintPluginParameterSet();
                pluginParameters.Get("amount", true).number = 0.75f;
                pluginParameters.Get("texture", true).texture = endpointTexture;
                pluginParameters.Get("sprite", true).sprite = endpointSprite;
                pluginParameters.Get("curve", true).curve = new AnimationCurve(
                    new Keyframe(0f, 0f), new Keyframe(0.5f, 0.8f), new Keyframe(1f, 1f));
                pluginParameters.Stripes("stripes").Add(new TexturePaintStripeDefinition
                {
                    direction = TexturePaintStripeDirection.Horizontal,
                    position = .35f,
                    width = .12f,
                    softness = .025f,
                    opacity = .8f,
                    color = new Color(.2f, .4f, .8f, 1f)
                });
                TexturePaintDocumentLayer maskedLayer = surface.layers[1];
                maskedLayer.hasMask = true;
                maskedLayer.maskPluginId = "com.uma.texturepaint.filter.levels-curves";
                maskedLayer.maskPluginVersion = "1.0.0";
                maskedLayer.maskPluginParameters = pluginParameters.Clone();
                maskedLayer.maskPluginParametersJson = JsonUtility.ToJson(
                    maskedLayer.maskPluginParameters);
                maskedLayer.maskPluginStale = false;
                surface.layers.Add(new TexturePaintDocumentLayer
                {
                    name = "Agify",
                    kind = TexturePaintLayerKind.Plugin,
                    pluginId = "com.uma.texturepaint.agify",
                    pluginVersion = "1.0.0",
                    pluginParameters = pluginParameters,
                    pluginParametersJson = JsonUtility.ToJson(pluginParameters),
                    pluginStale = false
                });
                source.surfaces.Add(surface);

                string pathName = Folder + "/All Settings.asset";
                using TexturePaintProjectSaveOperation operation =
                    new TexturePaintProjectSaveOperation(source, null, pathName);
                Complete(operation);
                Assert.That(operation.HasError, Is.False, operation.Error);
                TexturePaintDocument saved = operation.SavedDocument;
                Assert.That(saved, Is.Not.Null);
                Assert.That(saved.surfaces[0].activeLayer, Is.EqualTo(2));
                AssertJsonEqual(fill, saved.surfaces[0].layers[0].fillSettings);
                AssertJsonEqual(paint, saved.surfaces[0].layers[1].paintSettings);
                AssertJsonEqual(path, saved.surfaces[0].layers[2].splineSettings);
                Assert.That(saved.surfaces[0].layers[2].splineSettings.editMode,
                    Is.EqualTo(TexturePaintPathEditMode.Adjust));
                Assert.That(saved.surfaces[0].layers[2].splineSettings.AutoUpdateEnabled, Is.False);
                AssertJsonEqual(effects, saved.surfaces[0].layers[2].effects);
                AssertJsonEqual(surface.layers[1].channels[0].settings,
                    saved.surfaces[0].layers[1].channels[0].settings);
                AssertJsonEqual(surface.layers[1].channels[0].settings.sourceSettings,
                    saved.surfaces[0].layers[1].channels[0].GetSourceSettings());
                Assert.That(saved.surfaces[0].layers[2].pluginParametersJson,
                    Is.EqualTo("{\"amount\":0.75}"));
                Assert.That(saved.surfaces[0].layers[3].kind,
                    Is.EqualTo(TexturePaintLayerKind.Plugin));
                Assert.That(saved.surfaces[0].layers[3].pluginParameters.Float("amount"),
                    Is.EqualTo(0.75f));
                Assert.That(saved.surfaces[0].layers[3].pluginParameters.Texture("texture"),
                    Is.SameAs(endpointTexture));
                Assert.That(saved.surfaces[0].layers[3].pluginParameters.Sprite("sprite"),
                    Is.SameAs(endpointSprite));
                Assert.That(saved.surfaces[0].layers[3].pluginParameters.Curve("curve").Evaluate(0.5f),
                    Is.EqualTo(0.8f).Within(0.001f));
                Assert.That(saved.surfaces[0].layers[3].pluginParameters.Stripes("stripes"),
                    Has.Count.EqualTo(1));
                Assert.That(saved.surfaces[0].layers[3].pluginParameters.Stripes("stripes")[0].direction,
                    Is.EqualTo(TexturePaintStripeDirection.Horizontal));
                Assert.That(saved.surfaces[0].layers[3].pluginParameters.Stripes("stripes")[0].width,
                    Is.EqualTo(.12f).Within(.0001f));
                Assert.That(saved.surfaces[0].layers[3].pluginStale, Is.False);
                Assert.That(saved.surfaces[0].layers[1].maskPluginId,
                    Is.EqualTo("com.uma.texturepaint.filter.levels-curves"));
                Assert.That(saved.surfaces[0].layers[1].maskPluginParameters.Curve("curve").Evaluate(0.5f),
                    Is.EqualTo(0.8f).Within(0.001f));
                Assert.That(saved.surfaces[0].layers[1].maskPluginStale, Is.False);
            }
            finally
            {
                if (source != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source)))
                    UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void SelectingRestoredPathAppliesSavedRibbonRotationAndBrushValues()
        {
            TexturePaintStageWindow stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            BrushPreset transient = ScriptableObject.CreateInstance<BrushPreset>();
            using TextureSet set = new TextureSet();
            try
            {
                SetField(stage, "transientBrush", transient);
                TexturePaintSplineSettings settings = new TexturePaintSplineSettings
                {
                    brushRotation = 90f,
                    brushBlendMode = TexturePaintBlendMode.Multiply,
                    brushMirrorStroke = true,
                    brushAlignToStroke = true,
                    brushSplatter = true,
                    brushSplatterDistance = 1.25f,
                    brushRandomStrength = true,
                    brushFade = true,
                    brushAutoFade = true,
                    brushTaper = true,
                    brushAutoTaper = true,
                    symmetryAxis = Vector3.forward
                };
                TexturePaintLayer layer = set.AddSplineLayer("Restored Ribbon");
                layer.splineSettings = settings;
                set.activeLayerIndex = set.layers.IndexOf(layer);
                MethodInfo restore = typeof(TexturePaintStageWindow).GetMethod("SyncActiveLayerSelection",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(restore, Is.Not.Null);
                restore.Invoke(stage, new object[] { set });

                Assert.That(transient.rotation, Is.EqualTo(90f));
                Assert.That(transient.blendMode, Is.EqualTo(TexturePaintBlendMode.Multiply));
                Assert.That(transient.mirrorStroke, Is.True);
                Assert.That(transient.alignToStroke, Is.True);
                Assert.That(transient.splatter, Is.True);
                Assert.That(transient.splatterDistance, Is.EqualTo(1.25f));
                Assert.That(transient.randomStrength, Is.True);
                Assert.That(transient.fade, Is.True);
                Assert.That(transient.autoFade, Is.True);
                Assert.That(transient.taper, Is.True);
                Assert.That(transient.autoTaper, Is.True);
                Assert.That(Field("radialSymmetryAxis").GetValue(stage), Is.EqualTo(Vector3.forward));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(transient);
                UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        [Test]
        public void TextureOverlayEditsDoNotRequireRibbonReprojection()
        {
            MethodInfo changed = typeof(TexturePaintStageWindow).GetMethod(
                "RibbonProjectionEffectsChanged", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(changed, Is.Not.Null);
            TexturePaintLayerEffects before = new TexturePaintLayerEffects();
            TexturePaintLayerEffects after = before.Clone();
            after.textureOverlay.enabled = true;
            after.textureOverlay.textureTiling1 = new Vector2(7f, 3f);
            after.textureOverlay.textureOpacity2 = 0.42f;

            Assert.That((bool)changed.Invoke(null, new object[] { before, after }), Is.False,
                "UV-space texture overlays should update through compositing without rerendering a ribbon.");

            after.imageAdjustments.enabled = true;
            after.imageAdjustments.saturation = 1.4f;
            after.imageAdjustments.brightness = -0.2f;
            after.imageAdjustments.contrast = 0.35f;
            after.imageAdjustments.hue = 72f;
            Assert.That((bool)changed.Invoke(null, new object[] { before, after }), Is.False,
                "Image adjustments should update through compositing without rerendering a ribbon.");

            after.innerGlow.enabled = true;
            Assert.That((bool)changed.Invoke(null, new object[] { before, after }), Is.True,
                "Ribbon-local effects still require world-space path reprojection.");
        }

        private static TexturePaintLayerEffects CreateNonDefaultEffects()
        {
            TexturePaintLayerEffects effects = new TexturePaintLayerEffects();
            TexturePaintLayerEffectSettings[] values =
            {
                effects.stroke, effects.innerShadow, effects.outerShadow, effects.innerGlow,
                effects.outerGlow, effects.colorOverlay, effects.edgeFade, effects.bevelEdge,
                effects.proceduralStitch, effects.textureOverlay, effects.imageAdjustments
            };
            for (int i = 0; i < values.Length; i++)
            {
                TexturePaintLayerEffectSettings effect = values[i];
                effect.enabled = true;
                effect.channel = (TexturePaintChannel)(i % 6);
                effect.color = new Color(0.1f * (i + 1), 0.07f * i, 0.04f * i, 0.9f);
                effect.width = 3f + i;
                effect.smoothness = 0.1f * i;
                effect.curve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.4f, 0.7f),
                    new Keyframe(1f, 0.2f));
                effect.curve.preWrapMode = WrapMode.PingPong;
                effect.curve.postWrapMode = WrapMode.Loop;
                effect.offset = new Vector2(i + 0.25f, -i - 0.5f);
                effect.blendMode = TexturePaintBlendMode.Screen;
                effect.level = Mathf.Clamp01(0.2f + i * 0.1f);
                effect.edgeFadeStart = 0.15f + i * 0.05f;
                effect.edgeFadeSize = 0.3f + i * 0.05f;
                effect.ribbonSide = (TexturePaintRibbonSide)(i % 3);
                effect.secondaryColor = new Color(0.03f * i, 0.08f * i, 0.11f * i, 0.7f);
                effect.ribbonLeftTone = i % 2 == 0
                    ? TexturePaintRibbonBevelTone.Light : TexturePaintRibbonBevelTone.Dark;
                effect.ribbonRightTone = i % 2 == 0
                    ? TexturePaintRibbonBevelTone.Dark : TexturePaintRibbonBevelTone.Light;
                effect.ribbonLeftOffset = i + 0.75f;
                effect.ribbonRightOffset = -i - 0.25f;
                effect.stitchRows = i % 2 == 0
                    ? TexturePaintRibbonStitchRows.Single : TexturePaintRibbonStitchRows.Double;
                effect.stitchThreadSize = 0.01f + i * 0.005f;
                effect.stitchLength = 0.04f + i * 0.03f;
                effect.stitchInset = 0.02f + i * 0.02f;
                effect.textureTiling1 = new Vector2(1.25f + i, -2.5f - i);
                effect.textureTiling2 = new Vector2(-3.75f - i, 4.5f + i);
                effect.textureOffset1 = new Vector2(0.05f * i, -0.07f * i);
                effect.textureOffset2 = new Vector2(-0.09f * i, 0.11f * i);
                effect.textureRotation1 = 7.5f * i;
                effect.textureRotation2 = -11.25f * i;
                effect.textureOpacity1 = 0.15f + i * 0.05f;
                effect.textureOpacity2 = 0.2f + i * 0.04f;
                effect.secondaryBlendMode = TexturePaintBlendMode.Subtract;
                effect.saturation = 0.5f + i * 0.1f;
                effect.brightness = -0.4f + i * 0.06f;
                effect.contrast = -0.3f + i * 0.05f;
                effect.hue = -90f + i * 17f;
            }
            return effects;
        }

        private static void AssertJsonEqual(object expected, object actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(JsonUtility.ToJson(actual), Is.EqualTo(JsonUtility.ToJson(expected)));
        }

        private static TexturePaintDocument CreateDocumentWithPixels(byte[] bytes)
        {
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.name = "Persistence Test";
            document.createdUtc = DateTime.UtcNow.ToString("O");
            document.surfaces.Add(new TexturePaintDocumentSurface
            {
                stableId = "surface",
                baseChannels = new System.Collections.Generic.List<TexturePaintDocumentChannel>
                {
                    new TexturePaintDocumentChannel
                    {
                        channel = TexturePaintChannel.Albedo,
                        pixels = new TexturePaintPixelData
                        {
                            width = 1,
                            height = 1,
                            uncompressedByteCount = 4,
                            storageKey = "surface/base/Albedo",
                            compressedBytes = bytes
                        }
                    }
                }
            });
            return document;
        }

        private static Sprite CreateSpriteAsset(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            texture.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void Complete(TexturePaintProjectSaveOperation operation)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (!operation.IsDone && timeout.Elapsed < TimeSpan.FromSeconds(10))
            {
                operation.Tick();
                if (!operation.IsDone) Thread.Sleep(5);
            }
            Assert.That(operation.IsDone, Is.True, "Timed out waiting for the project document save operation.");
        }

        private static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(TexturePaintStageWindow).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing TexturePaintStageWindow field: " + name);
            return field;
        }

        private static void SetField(TexturePaintStageWindow stage, string name, object value)
        {
            Field(name).SetValue(stage, value);
        }

        private static void Invoke(TexturePaintStageWindow stage, string name)
        {
            MethodInfo method = typeof(TexturePaintStageWindow).GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Missing TexturePaintStageWindow method: " + name);
            method.Invoke(stage, null);
        }
    }
}
#endif
