using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public class NewUMAGUIPresetTests
    {
        private static readonly System.Reflection.FieldInfo IndexerField = typeof(UMAAssetIndexer).GetField(
            "theIndexer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        private object previousIndexer;
        private UMASettings previousSettings;
        private UMASettings testSettings;
        private UMAAssetIndexer testIndexer;
        private RaceData raceA;
        private RaceData raceB;

        [SetUp]
        public void SetUp()
        {
            previousIndexer = IndexerField.GetValue(null);
            previousSettings = UMASettings.instance;
            testSettings = ScriptableObject.CreateInstance<UMASettings>();
            testSettings.autoRepairIndex = false;
            UMASettings.instance = testSettings;
            testIndexer = ScriptableObject.CreateInstance<UMAAssetIndexer>();
            IndexerField.SetValue(null, testIndexer);
            raceA = ScriptableObject.CreateInstance<RaceData>();
            raceA.name = "Race A";
            raceA._friendlyName = "Friendly Race A";
            raceB = ScriptableObject.CreateInstance<RaceData>();
            raceB.name = "Race B";
            raceB._friendlyName = "Friendly Race B";
            testIndexer.SerializedItems.Clear();
            testIndexer.SerializedItems.Add(new AssetItem(typeof(RaceData), raceA.name, "", raceA));
            testIndexer.SerializedItems.Add(new AssetItem(typeof(RaceData), raceB.name, "", raceB));
            testIndexer.DoInitialDictionaryLoad();
        }

        [TearDown]
        public void TearDown()
        {
            IndexerField.SetValue(null, previousIndexer);
            UMASettings.instance = previousSettings;
            Object.DestroyImmediate(raceA);
            Object.DestroyImmediate(raceB);
            Object.DestroyImmediate(testIndexer);
            Object.DestroyImmediate(testSettings);
        }

        [Test]
        public void PresetTileDisplaysIconCallsSelectionAndReleasesOnlyOwnedSprite()
        {
            var root = new GameObject("Tile", typeof(RectTransform), typeof(ItemEffector));
            var imageObject = new GameObject("ItemImage", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            imageObject.transform.SetParent(root.transform, false);
            var preset = ScriptableObject.CreateInstance<UMAPreset>();
            var icon = new Texture2D(8, 8);
            Sprite sprite = null;
            try
            {
                preset.Icon = icon;
                UMAPreset selected = null;
                var tile = root.GetComponent<ItemEffector>();
                tile.SetupPreset(preset, value => selected = value);
                var image = imageObject.GetComponent<UnityEngine.UI.Image>();
                sprite = image.sprite;
                Assert.That(sprite.texture, Is.SameAs(icon));
                Assert.That(image.preserveAspect, Is.True);
                tile.ImageClicked();
                Assert.That(selected, Is.SameAs(preset));
                // Non-ExecuteAlways behaviours do not receive runtime destruction messages in Edit Mode.
                typeof(ItemEffector).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(tile, null);
                Object.DestroyImmediate(root);
                Assert.That(sprite == null, Is.True);
                Assert.That(icon != null, Is.True);
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                Object.DestroyImmediate(icon);
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void MissingIconUsesNameAndNullPresetNeverClearsWardrobe()
        {
            var root = new GameObject("Tile", typeof(RectTransform), typeof(ItemEffector));
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            labelObject.transform.SetParent(root.transform, false);
            var preset = ScriptableObject.CreateInstance<UMAPreset>();
            try
            {
                preset.name = "Short";
                var tile = root.GetComponent<ItemEffector>();
                tile.SetupPreset(preset, null);
                Assert.That(labelObject.GetComponent<UnityEngine.UI.Text>().text, Is.EqualTo("Short"));
                tile.SetupPreset(null, null);
                Assert.DoesNotThrow(tile.ImageClicked);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(preset); }
        }

        [Test]
        public void PresetCompatibilityAllowsAnyIndexedRaceAndLegacyUnrestrictedPresets()
        {
            var root = new GameObject("UI");
            root.SetActive(false);
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.SetActive(false);
            var preset = ScriptableObject.CreateInstance<UMAPreset>();
            try
            {
                var gui = root.AddComponent<NewUMAGUI>();
                gui.avatar = avatarRoot.AddComponent<DynamicCharacterAvatar>();
                gui.avatar.activeRace.name = raceB.raceName;
                gui.avatar.activeRace.data = raceB;
                preset.Definition = new AvatarDefinition { RaceName = raceA.raceName };
                Assert.That(gui.CanApplyPreset(preset), Is.True);
                preset.Definition = new AvatarDefinition { RaceName = "Missing race" };
                Assert.That(gui.CanApplyPreset(preset), Is.False);
                preset.Definition = new AvatarDefinition();
                Assert.That(gui.CanApplyPreset(preset), Is.True);
                Assert.That(gui.CanApplyPreset(null), Is.False);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(avatarRoot); Object.DestroyImmediate(preset); }
        }

        [Test]
        public void PresetTilesAreGroupedUnderRaceFriendlyNamesInFirstSeenOrder()
        {
            var root = new GameObject("UI");
            root.SetActive(false);
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.SetActive(false);
            var headerPrefab = new GameObject("Header", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            var gridPrefab = new GameObject("Grid", typeof(RectTransform));
            var itemPrefab = new GameObject("Item", typeof(RectTransform), typeof(ItemEffector));
            var container = new GameObject("Container", typeof(RectTransform));
            container.transform.SetParent(root.transform, false);
            var a1 = NewPreset("A1", raceA.raceName);
            var b1 = NewPreset("B1", raceB.raceName);
            var a2 = NewPreset("A2", raceA.raceName);
            try
            {
                var gui = root.AddComponent<NewUMAGUI>();
                gui.avatar = avatarRoot.AddComponent<DynamicCharacterAvatar>();
                gui.avatar.activeRace.name = raceB.raceName;
                gui.avatar.activeRace.data = raceB;
                gui.ColorLabel = headerPrefab;
                gui.ItemContainer = gridPrefab;
                gui.Item = itemPrefab;
                gui.Presets.AddRange(new[] { a1, b1, a2 });

                typeof(NewUMAGUI).GetMethod("AddPresetItems",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(gui, new object[] { container });

                Assert.That(container.transform.childCount, Is.EqualTo(4));
                Assert.That(container.transform.GetChild(0).GetComponent<UnityEngine.UI.Text>().text,
                    Is.EqualTo(raceA.friendlyName));
                Assert.That(container.transform.GetChild(1).childCount, Is.EqualTo(2));
                Assert.That(container.transform.GetChild(2).GetComponent<UnityEngine.UI.Text>().text,
                    Is.EqualTo(raceB.friendlyName));
                Assert.That(container.transform.GetChild(3).childCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatarRoot);
                Object.DestroyImmediate(headerPrefab);
                Object.DestroyImmediate(gridPrefab);
                Object.DestroyImmediate(itemPrefab);
                Object.DestroyImmediate(a1);
                Object.DestroyImmediate(b1);
                Object.DestroyImmediate(a2);
            }
        }

        [Test]
        public void ColorButtonsUseCurrentRacePaletteAndKeepTheCategorySharedColorName()
        {
            var root = new GameObject("UI");
            root.SetActive(false);
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.SetActive(false);
            var header = new GameObject("Header", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            var grid = new GameObject("Grid", typeof(RectTransform));
            var button = new GameObject("Color", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(ColorEffector));
            var container = new GameObject("Container", typeof(RectTransform));
            container.transform.SetParent(root.transform, false);
            var fallback = ScriptableObject.CreateInstance<SharedColorTable>();
            var alias = ScriptableObject.CreateInstance<SharedColorTable>();
            var exact = ScriptableObject.CreateInstance<SharedColorTable>();
            try
            {
                var gui = root.AddComponent<NewUMAGUI>();
                gui.avatar = avatarRoot.AddComponent<DynamicCharacterAvatar>();
                gui.ColorLabel = header;
                gui.GridContainer = grid;
                gui.ColorSelector = button;
                fallback.sharedColorName = "Beard";
                fallback.colors = new[] { new OverlayColorData(3) { name = "Red", displayColor = Color.red } };
                alias.sharedColorName = "Hair";
                alias.AlsoAppliesTo = new[] { "Beard" };
                alias.colors = new[] { new OverlayColorData(3) { name = "Blue", displayColor = Color.blue } };
                exact.sharedColorName = "Beard";
                exact.colors = new[] { new OverlayColorData(3) { name = "Green", displayColor = Color.green } };
                var addColors = typeof(NewUMAGUI).GetMethod("AddColors",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                void CheckButtons(SharedColorTable expected)
                {
                    while (container.transform.childCount > 0)
                        Object.DestroyImmediate(container.transform.GetChild(0).gameObject);
                    addColors.Invoke(gui, new object[] { container, fallback });
                    Assert.That(container.transform.GetChild(0).GetComponent<UnityEngine.UI.Text>().text, Is.EqualTo("Beard"));
                    var choices = container.GetComponentsInChildren<ColorEffector>(true);
                    Assert.That(choices.Length, Is.EqualTo(expected.colors.Length));
                    Assert.That(choices[0].color, Is.SameAs(expected.colors[0]));
                    Assert.That(choices[0].colorName, Is.EqualTo("Beard"));
                    Assert.That(choices[0].colorEffector, Is.SameAs(gui));
                    var label = container.transform.GetChild(0).GetComponent<UnityEngine.UI.Text>();
                    choices[0].OnPointerEnter(null);
                    Assert.That(label.text, Is.EqualTo(expected.colors[0].name));
                    choices[0].OnPointerExit(null);
                    Assert.That(label.text, Is.EqualTo("Beard"));
                }

                gui.avatar.activeRace.name = raceA.name;
                gui.avatar.activeRace.data = raceA;
                CheckButtons(fallback);
                raceA.colorLists.AddRange(new[] { null, alias });
                CheckButtons(alias);
                raceA.colorLists.Add(exact);
                CheckButtons(exact);
                gui.avatar.activeRace.name = raceB.name;
                gui.avatar.activeRace.data = raceB;
                CheckButtons(fallback);
                gui.avatar = null;
                CheckButtons(fallback);
                Assert.That(fallback.colors[0].displayColor, Is.EqualTo(Color.red));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatarRoot);
                Object.DestroyImmediate(header);
                Object.DestroyImmediate(grid);
                Object.DestroyImmediate(button);
                Object.DestroyImmediate(fallback);
                Object.DestroyImmediate(alias);
                Object.DestroyImmediate(exact);
            }
        }

        private static UMAPreset NewPreset(string name, string raceName)
        {
            var value = ScriptableObject.CreateInstance<UMAPreset>();
            value.name = name;
            value.Definition = new AvatarDefinition { RaceName = raceName };
            return value;
        }
    }
}
