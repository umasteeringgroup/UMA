using System;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class UMAPresetTests
    {
        private GameObject root;
        private DynamicCharacterAvatar avatar;
        private RaceData race;
        private UMAPreset preset;
        private UMATextRecipe untouchedRecipe;
        private static readonly FieldInfo IndexerField = typeof(UMAAssetIndexer).GetField("theIndexer", BindingFlags.Static | BindingFlags.NonPublic);
        private object previousIndexer;
        private UMASettings previousSettings, testSettings;
        private UMAAssetIndexer testIndexer;
        private string savedFolderPreference;
        private bool hadFolderPreference;
        private static string FolderPreferenceKey => "UMA.Preset.LastSaveFolder." + Application.dataPath;

        [SetUp]
        public void SetUp()
        {
            hadFolderPreference = UnityEditor.EditorPrefs.HasKey(FolderPreferenceKey);
            savedFolderPreference = UnityEditor.EditorPrefs.GetString(FolderPreferenceKey);
            previousIndexer = IndexerField.GetValue(null);
            previousSettings = UMASettings.instance;
            testSettings = ScriptableObject.CreateInstance<UMASettings>();
            testSettings.autoRepairIndex = false;
            UMASettings.instance = testSettings;
            testIndexer = ScriptableObject.CreateInstance<UMAAssetIndexer>();
            IndexerField.SetValue(null, testIndexer);
            root = new GameObject("Preset test");
            root.SetActive(false);
            avatar = root.AddComponent<DynamicCharacterAvatar>();
            avatar.editorTimeGeneration = false;
            race = ScriptableObject.CreateInstance<RaceData>();
            race.name = "Preset test race";
            race.useNewDNA = false;
            testIndexer.SerializedItems.Clear();
            testIndexer.SerializedItems.Add(new AssetItem(typeof(RaceData), race.name, "", race));
            testIndexer.DoInitialDictionaryLoad();
            avatar.activeRace.name = race.raceName;
            avatar.activeRace.data = race;
            avatar.predefinedDNA = new UMAPredefinedDNA();
            avatar.predefinedDNA.AddDNA("untouched", .37f);
            avatar.characterColors.SetRawColor("Skin", new OverlayColorData(1) { name = "Skin", color = Color.red });
            avatar.characterColors.SetRawColor("Hair", new OverlayColorData(1) { name = "Hair", color = Color.blue });
            untouchedRecipe = ScriptableObject.CreateInstance<UMATextRecipe>();
            avatar.WardrobeRecipes["Chest"] = untouchedRecipe;
            preset = ScriptableObject.CreateInstance<UMAPreset>();
            preset.Definition = new AvatarDefinition { RaceName = race.raceName };
        }

        [TearDown]
        public void TearDown()
        {
            if (hadFolderPreference) UnityEditor.EditorPrefs.SetString(FolderPreferenceKey, savedFolderPreference);
            else UnityEditor.EditorPrefs.DeleteKey(FolderPreferenceKey);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(preset);
            Object.DestroyImmediate(untouchedRecipe);
            Object.DestroyImmediate(race);
            IndexerField.SetValue(null, previousIndexer);
            UMASettings.instance = previousSettings;
            Object.DestroyImmediate(testIndexer);
            Object.DestroyImmediate(testSettings);
        }

        [Test]
        public void ColorOnlyPresetPreservesDNAAndClearsWardrobeAndCopiesShaderProperties()
        {
            var definition = preset.Definition;
            definition.Colors = new[] { new SharedColorDef("Skin", 1)
            {
                channels = new[] { new ColorDef(0, ColorDef.ToUInt(Color.green), 0) },
                shaderParms = new[] { "Float;0.2500;_Smoothness" }
            }};
            preset.Definition = definition;
            string before = JsonUtility.ToJson(preset);
            preset.ApplyTo(avatar, false);
            Assert.That(avatar.GetColor("Skin").color, Is.EqualTo(Color.green));
            Assert.That(avatar.GetColor("Hair").color, Is.EqualTo(Color.blue));
            Assert.That(avatar.GetColor("Skin").PropertyBlock.shaderProperties.Count, Is.EqualTo(1));
            Assert.That(avatar.WardrobeRecipes, Is.Empty);
            Assert.That(avatar.preloadWardrobeRecipes.recipes, Is.Empty);
            Assert.That(avatar.predefinedDNA.PreloadValues.Count, Is.EqualTo(1));
            avatar.GetColor("Skin").color = Color.black;
            Assert.That(JsonUtility.ToJson(preset), Is.EqualTo(before), "Applying must not mutate the shared asset.");
        }

        [Test]
        public void DNAOnlyPresetMergesWithoutClearingExistingValues()
        {
            var definition = preset.Definition;
            definition.Dna = new[] { new DnaDef("selected", .73f) };
            preset.Definition = definition;
            preset.ApplyTo(avatar, false);
            Assert.That(avatar.predefinedDNA.PreloadValues.Exists(value => value.Name == "untouched" && Mathf.Approximately(value.Value, .37f)), Is.True);
            Assert.That(avatar.predefinedDNA.PreloadValues.Exists(value => value.Name == "selected" && Mathf.Approximately(value.Value, .73f)), Is.True);
            Assert.That(avatar.WardrobeRecipes, Is.Empty);
            Assert.That(avatar.GetColor("Hair").color, Is.EqualTo(Color.blue));
        }

        [Test]
        public void NewDNAPresetChangesOnlySelectedLiveDNA()
        {
            var group = ScriptableObject.CreateInstance<UMA.DNAGroup>();
            var selected = ScriptableObject.CreateInstance<UMA.DNA>();
            var other = ScriptableObject.CreateInstance<UMA.DNA>();
            try
            {
                selected.name = "selected"; other.name = "other";
                group.dnaList.Add(selected); group.dnaList.Add(other);
                race.useNewDNA = true;
                race.DNACollection = new UMA.DNACollection();
                race.DNACollection.DNAGroups.Add(group);
                avatar.umaRecipe = new UMAData.UMARecipe();
                avatar.dnaInstanceCollection = new UMA.DNAInstanceCollection();
                avatar.dnaInstanceCollection.Initialize(race.DNACollection);
                avatar.umaRecipe.raceData = race;
                avatar.umaRecipe.dnaInstanceCollection = avatar.dnaInstanceCollection;
                avatar.dnaInstanceCollection.dnaInstances.Add(new UMA.DNAInstance("selected", .5f, group));
                avatar.dnaInstanceCollection.dnaInstances.Add(new UMA.DNAInstance("other", .31f, group));
                var definition = preset.Definition;
                definition.Dna = new[] { new DnaDef("selected", .73f) };
                preset.Definition = definition;
                preset.ApplyTo(avatar, false);
                Assert.That(avatar.GetDNA()["selected"].Value, Is.EqualTo(.73f).Within(.0001f));
                Assert.That(avatar.GetDNA()["other"].Value, Is.EqualTo(.31f).Within(.0001f));
            }
            finally { Object.DestroyImmediate(group); Object.DestroyImmediate(selected); Object.DestroyImmediate(other); }
        }

        [Test]
        public void WardrobeReferenceReplacesCurrentAndDefaultWardrobe()
        {
            var recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            try
            {
                recipe.name = "Preset legs"; recipe.wardrobeSlot = "Legs";
                var definition = preset.Definition;
                definition.Wardrobe = new[] { recipe.name };
                preset.Definition = definition;
                preset.WardrobeRecipes = new UMATextRecipe[] { recipe };
                preset.ApplyTo(avatar, false);
                Assert.That(avatar.WardrobeRecipes["Legs"], Is.SameAs(recipe));
                Assert.That(avatar.WardrobeRecipes.ContainsKey("Chest"), Is.False);
                Assert.That(avatar.preloadWardrobeRecipes.loadDefaultRecipes, Is.True);
                Assert.That(avatar.preloadWardrobeRecipes.recipes.Count, Is.EqualTo(1));
                Assert.That(avatar.preloadWardrobeRecipes.recipes[0]._recipe, Is.SameAs(recipe));
            }
            finally { Object.DestroyImmediate(recipe); }
        }

        [Test]
        public void ApplyPresetChangesToItsRaceBeforeApplyingValues()
        {
            var targetRace = ScriptableObject.CreateInstance<RaceData>();
            try
            {
                targetRace.name = "Preset target race";
                targetRace.useNewDNA = false;
                testIndexer.SerializedItems.Add(new AssetItem(typeof(RaceData), targetRace.name, "", targetRace));
                testIndexer.DoInitialDictionaryLoad();
                var definition = preset.Definition;
                definition.RaceName = targetRace.raceName;
                definition.Dna = new[] { new DnaDef("selected", .8f) };
                preset.Definition = definition;

                preset.ApplyTo(avatar, false);

                Assert.That(avatar.activeRace.name, Is.EqualTo(targetRace.raceName));
                Assert.That(avatar.activeRace.data, Is.SameAs(targetRace));
                Assert.That(avatar.predefinedDNA.PreloadValues.Exists(value =>
                    value.Name == "selected" && Mathf.Approximately(value.Value, .8f)), Is.True);
            }
            finally { Object.DestroyImmediate(targetRace); }
        }

        [Test]
        public void ApplyPresetResolvesMatchingRaceNameWhenRaceDataIsNotInitialized()
        {
            avatar.activeRace.data = null;
            avatar.activeRace.name = race.raceName;

            preset.ApplyTo(avatar, false);

            Assert.That(avatar.activeRace.data, Is.SameAs(race));
        }

        [Test]
        public void MissingPresetRaceIsRejectedBeforeChangingCharacter()
        {
            var definition = preset.Definition;
            definition.RaceName = "Missing race";
            definition.Dna = new[] { new DnaDef("selected", .8f) };
            preset.Definition = definition;
            Assert.Throws<InvalidOperationException>(() => preset.ApplyTo(avatar, false));
            Assert.That(avatar.activeRace.data, Is.SameAs(race));
            Assert.That(avatar.predefinedDNA.PreloadValues.Count, Is.EqualTo(1));
            Assert.That(avatar.WardrobeRecipes["Chest"], Is.SameAs(untouchedRecipe));
        }

        [Test]
        public void PopupSavePersistsSelectedValuesAndIcon()
        {
            string folderName = "UMAPresetTest_" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + folderName;
            UnityEditor.AssetDatabase.CreateFolder("Assets", folderName);
            var window = ScriptableObject.CreateInstance<UMA.Editors.UMAPresetEditorWindow>();
            var icon = new Texture2D(16, 16);
            var asset = ScriptableObject.CreateInstance<UMAPreset>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = window.GetType();
            try
            {
                string path = folder + "/Preset.asset";
                UnityEditor.AssetDatabase.CreateAsset(asset, path);
                type.GetField("source", flags).SetValue(window, avatar);
                type.GetField("editing", flags).SetValue(window, asset);
                type.GetField("snapshot", flags).SetValue(window, new AvatarDefinition
                {
                    RaceName = race.raceName,
                    Dna = new[] { new DnaDef("selected", .73f), new DnaDef("omitted", .2f) }
                });
                ((System.Collections.Generic.HashSet<string>)type.GetField("dna", flags).GetValue(window)).Add("selected");
                type.GetField("capturedIcon", flags).SetValue(window, icon);
                type.GetField("status", flags).SetValue(window, new UnityEngine.UIElements.Label());
                window.ShowUtility();
                type.GetMethod("Save", flags).Invoke(window, null);
                var saved = UnityEditor.AssetDatabase.LoadAssetAtPath<UMAPreset>(path);
                Assert.That(saved.Definition.Dna.Length, Is.EqualTo(1));
                Assert.That(saved.Definition.Dna[0].Name, Is.EqualTo("selected"));
                Assert.That(saved.Icon, Is.Not.Null);
                Assert.That(UnityEditor.AssetDatabase.GetAssetPath(saved.Icon), Is.EqualTo(folder + "/Preset_Icon.png"));
                Assert.That(System.IO.File.Exists(folder + "/Preset_Icon.png"), Is.True);
            }
            finally
            {
                if (window != null) Object.DestroyImmediate(window);
                if (icon != null) Object.DestroyImmediate(icon);
                UnityEditor.AssetDatabase.DeleteAsset(folder);
            }
        }

        private static object ScreenshotMethod(string name, params object[] arguments)
        {
            return typeof(UMA.Editors.UMAPresetEditorWindow)
                .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);
        }

        [TestCase(false, 800, 450)]
        [TestCase(false, 3200, 1800)]
        [TestCase(false, 600, 1200)]
        [TestCase(false, 1000, 1000)]
        [TestCase(true, 800, 450)]
        [TestCase(true, 3200, 1800)]
        [TestCase(true, 600, 1200)]
        [TestCase(true, 1000, 1000)]
        public void ScreenshotFramingPreservesImageCoordinatesAcrossResolutionAndAvatarMovement(
            bool orthographic, int width, int height)
        {
            var cameraObject = new GameObject("Screenshot projection test");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            try
            {
                camera.transform.SetPositionAndRotation(new Vector3(0, 1.6f, -3f), Quaternion.Euler(5, 12, 3));
                camera.orthographic = orthographic;
                camera.orthographicSize = 2f;
                camera.fieldOfView = 50f;
                camera.aspect = 1600f / 900f;
                var crop = new Rect(.57f, .53f, 240f / 1600f, 240f / 900f);
                var framing = (DynamicCharacterAvatar.PresetScreenshotFraming)ScreenshotMethod(
                    "RememberFraming", avatar.transform, camera, 3f, crop);
                avatar.lastPresetScreenshot = framing;
                string json = UnityEditor.EditorJsonUtility.ToJson(avatar);
                avatar.lastPresetScreenshot = null;
                UnityEditor.EditorJsonUtility.FromJsonOverwrite(json, avatar);
                framing = avatar.lastPresetScreenshot;
                Assert.That(framing.IsValid, Is.True, "Framing must survive avatar serialization.");

                var points = new Vector3[3];
                for (int i = 0; i < points.Length; i++)
                    points[i] = avatar.transform.InverseTransformPoint(camera.ViewportToWorldPoint(
                        new Vector3(crop.x + crop.width * (.2f + .3f * i),
                            crop.y + crop.height * (.8f - .3f * i), 2f + i)));

                avatar.transform.SetPositionAndRotation(new Vector3(5, 2, -4), Quaternion.Euler(0, 63, 0));
                avatar.transform.localScale = Vector3.one * 1.7f;
                camera.transform.SetPositionAndRotation(avatar.transform.TransformPoint(framing.cameraPosition),
                    avatar.transform.rotation * framing.cameraRotation);
                camera.aspect = (float)width / height;
                camera.fieldOfView = 110f;
                camera.orthographicSize = 8f;
                Rect retake = (Rect)ScreenshotMethod("RetakeViewport", camera, avatar.transform, framing);
                Assert.That(retake.width * width, Is.EqualTo(retake.height * height).Within(.002f),
                    "The crop must remain square at every resolution and aspect ratio.");
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 projected = camera.WorldToViewportPoint(avatar.transform.TransformPoint(points[i]));
                    Assert.That((projected.x - retake.x) / retake.width, Is.EqualTo(.2f + .3f * i).Within(.002f));
                    Assert.That((projected.y - retake.y) / retake.height, Is.EqualTo(.8f - .3f * i).Within(.002f));
                }
            }
            finally { Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void ScreenshotRetakeButtonRequiresSavedValidFraming()
        {
            var window = ScriptableObject.CreateInstance<UMA.Editors.UMAPresetEditorWindow>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var controls = new UnityEngine.UIElements.VisualElement();
            var type = window.GetType();
            try
            {
                type.GetField("source", flags).SetValue(window, avatar);
                type.GetField("retakeControls", flags).SetValue(window, controls);
                var refresh = type.GetMethod("RefreshRetakeButton", flags);
                refresh.Invoke(window, null);
                Assert.That(controls.childCount, Is.Zero);
                avatar.lastPresetScreenshot = new DynamicCharacterAvatar.PresetScreenshotFraming();
                refresh.Invoke(window, null);
                Assert.That(controls.childCount, Is.Zero);
                avatar.lastPresetScreenshot.sceneSize = 1f;
                avatar.lastPresetScreenshot.projectionCrop = new Rect(-.1f, -.1f, .2f, .2f);
                refresh.Invoke(window, null);
                Assert.That(controls.childCount, Is.EqualTo(1));
                Assert.That(((UnityEngine.UIElements.Button)controls[0]).text, Is.EqualTo("Retake from last position"));
                avatar.lastPresetScreenshot = null;
                refresh.Invoke(window, null);
                Assert.That(controls.childCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ScreenshotRestoresPerspectiveSceneViewAfterResize()
        {
            return CheckSceneViewScreenshotRestore(false);
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ScreenshotRestoresOrthographicSceneViewAfterResize()
        {
            return CheckSceneViewScreenshotRestore(true);
        }

        private System.Collections.IEnumerator CheckSceneViewScreenshotRestore(bool orthographic)
        {
            var view = ScriptableObject.CreateInstance<UnityEditor.SceneView>();
            try
            {
                view.position = new Rect(50, 50, 900, 550);
                view.Show();
                view.LookAt(new Vector3(0, 1.6f, 0), Quaternion.Euler(5, 180, 0), 2f, orthographic, true);
                for (int i = 0; i < 5; i++) { view.Repaint(); yield return null; }
                Camera camera = view.camera;
                float cropHeight = .25f;
                var crop = new Rect(.54f, .52f, cropHeight / camera.aspect, cropHeight);
                var framing = (DynamicCharacterAvatar.PresetScreenshotFraming)ScreenshotMethod(
                    "RememberFraming", avatar.transform, camera, view.size, crop);
                Vector3 point = camera.ViewportToWorldPoint(new Vector3(crop.center.x, crop.center.y, 3f));
                Vector3 upperPoint = camera.ViewportToWorldPoint(new Vector3(crop.center.x, crop.yMax, 3f));
                view.position = new Rect(50, 50, 450, 850);
                view.LookAt(Vector3.zero, Quaternion.Euler(30, 45, 0), 10f, !orthographic, true);
                for (int i = 0; i < 5; i++) { view.Repaint(); yield return null; }
                ScreenshotMethod("RestoreCaptureView", view, avatar.transform, framing);
                for (int i = 0; i < 8; i++) { view.Repaint(); yield return null; }
                Assert.That(Vector3.Distance(camera.transform.position, avatar.transform.TransformPoint(framing.cameraPosition)),
                    Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, avatar.transform.rotation * framing.cameraRotation),
                    Is.LessThan(.05f));
                Assert.That(camera.orthographic, Is.EqualTo(orthographic));
                Rect retake = (Rect)ScreenshotMethod("RetakeViewport", camera, avatar.transform, framing);
                Vector3 center = camera.WorldToViewportPoint(point);
                Vector3 upper = camera.WorldToViewportPoint(upperPoint);
                Assert.That((center.x - retake.x) / retake.width, Is.EqualTo(.5f).Within(.002f));
                Assert.That((center.y - retake.y) / retake.height, Is.EqualTo(.5f).Within(.002f));
                Assert.That((upper.y - center.y) / retake.height, Is.EqualTo(.5f).Within(.002f));
                Assert.That(retake.width * camera.pixelWidth,
                    Is.EqualTo(retake.height * camera.pixelHeight).Within(.01f));
            }
            finally { view.Close(); }
        }

        [Test]
        public void CameraCompletionCopiesImageBeforeSceneViewClearsIt()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Requires a graphics device for render-texture readback.");
            var window = ScriptableObject.CreateInstance<UMA.Editors.UMAPresetEditorWindow>();
            var view = ScriptableObject.CreateInstance<UnityEditor.SceneView>();
            var rendered = new RenderTexture(32, 32, 0);
            var pattern = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = window.GetType();
            RenderTexture previousTarget = null;
            try
            {
                view.Show();
                previousTarget = view.camera.targetTexture;
                rendered.Create();
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++) pattern.SetPixel(x, y, x < 16 ? Color.red : Color.green);
                pattern.Apply();
                Graphics.Blit(pattern, rendered);
                view.camera.targetTexture = rendered;
                type.GetField("source", flags).SetValue(window, avatar);
                type.GetField("captureView", flags).SetValue(window, view);
                type.GetField("awaitingRender", flags).SetValue(window, true);
                type.GetField("captureViewport", flags).SetValue(window, new Rect(0, 0, 1, 1));
                type.GetField("preview", flags).SetValue(window, new UnityEngine.UIElements.Image());
                type.GetField("status", flags).SetValue(window, new UnityEngine.UIElements.Label());
                type.GetMethod("CaptureRenderedCamera", flags).Invoke(window,
                    new object[] { default(UnityEngine.Rendering.ScriptableRenderContext), view.camera });
                // Simulate Scene View's later clear for handles/GUI.
                RenderTexture.active = rendered;
                GL.Clear(true, true, Color.gray);
                var icon = (Texture2D)type.GetField("capturedIcon", flags).GetValue(window);
                Assert.That(icon, Is.Not.Null);
                Assert.That(avatar.lastPresetScreenshot, Is.Not.Null);
                Assert.That(avatar.lastPresetScreenshot.IsValid, Is.True);
                Assert.That(icon.width, Is.EqualTo(256));
                Assert.That(icon.height, Is.EqualTo(256));
                Assert.That(icon.GetPixel(32, 128).r, Is.GreaterThan(.9f));
                Assert.That(icon.GetPixel(32, 128).g, Is.LessThan(.1f));
                Assert.That(icon.GetPixel(224, 128).g, Is.GreaterThan(.9f));
                Assert.That((bool)type.GetField("awaitingRender", flags).GetValue(window), Is.False);
            }
            finally
            {
                RenderTexture.active = previous;
                if (view.camera != null) view.camera.targetTexture = previousTarget;
                Object.DestroyImmediate(window);
                view.Close();
                Object.DestroyImmediate(pattern);
                rendered.Release(); Object.DestroyImmediate(rendered);
            }
        }

        [TestCase(80, 25)]
        [TestCase(-80, 25)]
        [TestCase(25, -80)]
        [TestCase(-25, -80)]
        [TestCase(0, 80)]
        [TestCase(-80, 0)]
        [TestCase(0, 0)]
        public void IconSelectionIsSquareInEveryDirection(float dx, float dy)
        {
            var anchor = new Vector2(100, 100);
            var method = typeof(UMA.Editors.UMAPresetEditorWindow).GetMethod("SquareSelection", BindingFlags.Static | BindingFlags.NonPublic);
            var rect = (Rect)method.Invoke(null, new object[] { anchor, anchor + new Vector2(dx, dy) });
            float size = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            Assert.That(rect.width, Is.EqualTo(size));
            Assert.That(rect.height, Is.EqualTo(size));
            Assert.That(dx < 0 ? rect.xMax : rect.xMin, Is.EqualTo(anchor.x));
            Assert.That(dy < 0 ? rect.yMax : rect.yMin, Is.EqualTo(anchor.y));
        }

        [Test]
        public void DefinitionSurvivesScriptableObjectJsonRoundTrip()
        {
            var definition = preset.Definition;
            definition.Dna = new[] { new DnaDef("height", .63f) };
            definition.Wardrobe = new[] { "ExampleChest" };
            preset.Definition = definition;
            var copy = ScriptableObject.CreateInstance<UMAPreset>();
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(preset), copy);
                Assert.That(copy.Definition.RaceName, Is.EqualTo(race.raceName));
                Assert.That(copy.Definition.Dna[0].Value, Is.EqualTo(.63f).Within(.0001f));
                Assert.That(copy.Definition.Wardrobe, Is.EqualTo(definition.Wardrobe));
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Serializable]
        private class LegacyExport
        {
            public UMAPredefinedDNA PredefinedDNA = new UMAPredefinedDNA();
            public DynamicCharacterAvatar.ColorValueList DefaultColors = new DynamicCharacterAvatar.ColorValueList();
            public DynamicCharacterAvatar.WardrobeRecipeList DefaultWardrobe = new DynamicCharacterAvatar.WardrobeRecipeList();
        }

        [Test]
        public void LegacyConversionPreservesDNAColorsPropertiesAndEnabledWardrobe()
        {
            var legacy = new LegacyExport();
            legacy.PredefinedDNA.AddDNA("height", .73f);
            var color = new DynamicCharacterAvatar.ColorValue("Skin", Color.green);
            color.channelAdditiveMask[2] = Color.red;
            color.PropertyBlock = new UMAMaterialPropertyBlock();
            color.PropertyBlock.SetPropertyStrings(new[] { "Float;0.2500;_Smoothness" });
            legacy.DefaultColors.Colors.Add(color);
            legacy.DefaultWardrobe.recipes.Add(new DynamicCharacterAvatar.WardrobeRecipeListItem("Shirt"));
            legacy.DefaultWardrobe.recipes.Add(new DynamicCharacterAvatar.WardrobeRecipeListItem("Hidden") { _enabledInDefaultWardrobe = false });
            var converted = UMAPreset.ConvertLegacyJson(JsonUtility.ToJson(legacy), "HumanFemale");
            Assert.That(converted.RaceName, Is.EqualTo("HumanFemale"));
            Assert.That(converted.Dna[0].Value, Is.EqualTo(.73f).Within(.0001f));
            Assert.That(converted.Colors[0].channels[0].mCol, Is.EqualTo(ColorDef.ToUInt(Color.green)));
            Assert.That(converted.Colors[0].channels[2].aCol, Is.EqualTo(ColorDef.ToUInt(Color.red)));
            Assert.That(converted.Colors[0].shaderParms, Is.EqualTo(color.PropertyBlock.GetPropertyStrings()));
            Assert.That(converted.Wardrobe, Is.EqualTo(new[] { "Shirt" }));
            legacy.DefaultWardrobe.loadDefaultRecipes = false;
            Assert.That(UMAPreset.ConvertLegacyJson(JsonUtility.ToJson(legacy)).Wardrobe, Is.Empty);
        }

        [Test]
        public void LegacyConversionMigratesOriginalColorFields()
        {
            var converted = UMAPreset.ConvertLegacyJson(@"{""DefaultColors"":{""Colors"":[{""Name"":""Skin"",""Color"":{""r"":0,""g"":1,""b"":0,""a"":1},""MetallicGloss"":{""r"":1,""g"":0,""b"":0,""a"":1}}]}}");
            Assert.That(converted.Colors.Length, Is.EqualTo(1));
            Assert.That(converted.Colors[0].name, Is.EqualTo("Skin"));
            Assert.That(converted.Colors[0].channels[0].mCol, Is.EqualTo(ColorDef.ToUInt(Color.green)));
            Assert.That(converted.Colors[0].channels[2].aCol, Is.EqualTo(ColorDef.ToUInt(Color.red)));
            Assert.That(converted.RaceName, Is.Null);
        }

        [TestCase("")]
        [TestCase("{}")]
        [TestCase(@"{""unrelated"":1}")]
        public void LegacyConversionRejectsUnrecognizedFiles(string json)
        {
            Assert.Throws<ArgumentException>(() => UMAPreset.ConvertLegacyJson(json));
        }

        [Test]
        public void SaveFolderPersistsAndDeletedFoldersFallBackToAssets()
        {
            string folderName = "UMAPresetFolderTest_" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + folderName;
            UnityEditor.AssetDatabase.CreateFolder("Assets", folderName);
            try
            {
                UMA.Editors.UMAPresetAssetUtility.RememberSaveFolder(folder + "/Preset.asset");
                Assert.That(UMA.Editors.UMAPresetAssetUtility.LastSaveFolder, Is.EqualTo(folder));
                Assert.That(UnityEditor.EditorPrefs.GetString(FolderPreferenceKey), Is.EqualTo(folder));
            }
            finally { UnityEditor.AssetDatabase.DeleteAsset(folder); }
            Assert.That(UMA.Editors.UMAPresetAssetUtility.LastSaveFolder, Is.EqualTo("Assets"));
        }
    }
}
