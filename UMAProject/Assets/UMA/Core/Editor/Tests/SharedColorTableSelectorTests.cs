using System.Reflection;
using NUnit.Framework;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.Tests
{
    public class SharedColorTableSelectorTests
    {
        [TestCase("Hair", 0)]
        [TestCase("Skin", 1)]
        [TestCase("Beard", 1)]
        [TestCase("hair", -1)]
        [TestCase("Eyes", -1)]
        [TestCase("", -1)]
        [TestCase(null, -1)]
        public void RacePalettePrefersSharedColorNameThenAdditionalNames(string colorName, int expected)
        {
            var race = ScriptableObject.CreateInstance<RaceData>();
            var exact = ScriptableObject.CreateInstance<SharedColorTable>();
            var additional = ScriptableObject.CreateInstance<SharedColorTable>();
            var generic = ScriptableObject.CreateInstance<SharedColorTable>();
            try
            {
                exact.name = "Custom race palette";
                exact.sharedColorName = "Hair";
                additional.sharedColorName = "Skin";
                additional.AlsoAppliesTo = new[] { "Hair", "Beard" };
                generic.sharedColorName = string.Empty;
                race.colorLists.AddRange(new[] { null, generic, additional, exact });
                var method = typeof(OverlayColorDataPropertyDrawer).GetMethod("FindRaceColorTable", BindingFlags.Static | BindingFlags.NonPublic);
                var result = method.Invoke(null, new object[] { colorName, race });
                Assert.That(result, Is.SameAs(expected == 0 ? exact : expected == 1 ? additional : null));
                race.colorLists = null;
                Assert.That(method.Invoke(null, new object[] { colorName, race }), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(race);
                Object.DestroyImmediate(exact);
                Object.DestroyImmediate(additional);
                Object.DestroyImmediate(generic);
            }
        }

        [Test]
        public void AvatarColorPickersUseCurrentRacePaletteAndPreserveProjectFallback()
        {
            var race = ScriptableObject.CreateInstance<RaceData>();
            var first = ScriptableObject.CreateInstance<SharedColorTable>();
            var second = ScriptableObject.CreateInstance<SharedColorTable>();
            try
            {
                var picker = typeof(OverlayColorDataPropertyDrawer).GetMethod("FindAvatarColorTable", BindingFlags.Static | BindingFlags.NonPublic);
                var advancedPicker = typeof(OverlayColorDataPropertyDrawer).GetMethod("GetVisibleSharedColorTables", BindingFlags.Static | BindingFlags.NonPublic);
                var standardPicker = typeof(OverlayColorDataPropertyDrawer).GetMethod("FindStandardColorTable",
                    BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
                var projectFallback = standardPicker.Invoke(null, new object[] { "Hair" });
                Assert.That(picker.Invoke(null, new object[] { "Hair", null }), Is.SameAs(projectFallback));
                Assert.That(picker.Invoke(null, new object[] { "Hair", race }), Is.SameAs(projectFallback));

                first.sharedColorName = second.sharedColorName = "Hair";
                // Use unindexed tables and an empty palette: an explicit race assignment still wins.
                foreach (var table in new[] { first, second })
                {
                    race.colorLists.Clear();
                    race.colorLists.Add(table);
                    Assert.That(picker.Invoke(null, new object[] { "Hair", race }), Is.SameAs(table));
                    object[] args = { "Hair", true, race, null, null };
                    advancedPicker.Invoke(null, args);
                    Assert.That((SharedColorTable[])args[3], Is.EqualTo(new[] { table }));
                    Assert.That(((GUIContent[])args[4]).Length, Is.EqualTo(1));
                }
                second.sharedColorName = "Skin";
                Assert.That(picker.Invoke(null, new object[] { "Hair", race }), Is.SameAs(projectFallback));
            }
            finally
            {
                Object.DestroyImmediate(race);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void DefaultColorRoundTripsAsAnEntryInTheTable()
        {
            var table = ScriptableObject.CreateInstance<SharedColorTable>();
            var restored = ScriptableObject.CreateInstance<SharedColorTable>();
            try
            {
                Assert.That(table.DefaultColor, Is.Null);
                table.colors = new[] { new OverlayColorData(3), new OverlayColorData(3) };
                table.colors[1].name = "Brown";
                table.defaultColorIndex = 1;
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(table), restored);
                Assert.That(restored.DefaultColor, Is.SameAs(restored.colors[1]));
                restored.colors[1].name = "Dark Brown";
                Assert.That(restored.DefaultColor.name, Is.EqualTo("Dark Brown"));
                restored.colors = new OverlayColorData[0];
                restored.OnBeforeSerialize();
                Assert.That(restored.DefaultColor, Is.Null);
                Assert.That(restored.defaultColorIndex, Is.EqualTo(-1));
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(restored);
            }
        }

        [Test]
        public void DefaultColorFollowsReorderingAndClearsWhenDeleted()
        {
            var table = ScriptableObject.CreateInstance<SharedColorTable>();
            Editor editor = null;
            try
            {
                table.colors = new[] { new OverlayColorData(3), new OverlayColorData(3), new OverlayColorData(3) };
                var chosen = table.colors[1];
                table.defaultColorIndex = 1;
                editor = Editor.CreateEditor(table);
                var move = typeof(SharedColorTableEditor).GetMethod("MoveColor", BindingFlags.NonPublic | BindingFlags.Instance);
                var delete = typeof(SharedColorTableEditor).GetMethod("DeleteColor", BindingFlags.NonPublic | BindingFlags.Static);
                move.Invoke(editor, new object[] { table, 0, 1 });
                Assert.That(table.DefaultColor, Is.SameAs(chosen));
                move.Invoke(editor, new object[] { table, 0, 2 });
                Assert.That(table.DefaultColor, Is.SameAs(chosen));
                delete.Invoke(null, new object[] { table, 0 });
                Assert.That(table.DefaultColor, Is.SameAs(chosen));
                Assert.That(table.defaultColorIndex, Is.EqualTo(1));
                delete.Invoke(null, new object[] { table, 1 });
                Assert.That(table.DefaultColor, Is.Null);
                Assert.That(table.defaultColorIndex, Is.EqualTo(-1));
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void RaceColorListsSupportSerializedInspectorEditingAndRoundTrip()
        {
            var race = ScriptableObject.CreateInstance<RaceData>();
            var restored = ScriptableObject.CreateInstance<RaceData>();
            var table = ScriptableObject.CreateInstance<SharedColorTable>();
            string tablePath = "Assets/__RaceColorPaletteTest_" + System.Guid.NewGuid().ToString("N") + ".asset";
            try
            {
                AssetDatabase.CreateAsset(table, tablePath);
                using (var serialized = new SerializedObject(race))
                {
                    var lists = serialized.FindProperty("colorLists");
                    Assert.That(lists, Is.Not.Null);
                    lists.arraySize = 1;
                    var entry = lists.GetArrayElementAtIndex(0);
                    entry.objectReferenceValue = table;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Assert.That(race.colorLists[0], Is.SameAs(table));
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(race), restored);
                Assert.That(restored.colorLists.Count, Is.EqualTo(1));
                Assert.That(restored.colorLists[0], Is.SameAs(table));
            }
            finally
            {
                Object.DestroyImmediate(race);
                Object.DestroyImmediate(restored);
                AssetDatabase.DeleteAsset(tablePath);
                if (table != null) Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void StandardPalettePrefersExactNameThenAdditionalNameThenDefault()
        {
            var exact = ScriptableObject.CreateInstance<SharedColorTable>();
            var additional = ScriptableObject.CreateInstance<SharedColorTable>();
            var fallback = ScriptableObject.CreateInstance<SharedColorTable>();
            try
            {
                exact.name = "EyebrowsColors";
                additional.name = "HairColors";
                additional.AlsoAppliesTo = new[] { "Eyebrows", "Beard" };
                fallback.name = "DefaultColors";
                var method = typeof(OverlayColorDataPropertyDrawer).GetMethod("FindStandardColorTable",
                    BindingFlags.Static | BindingFlags.NonPublic, null,
                    new[] { typeof(string), typeof(System.Collections.Generic.IEnumerable<SharedColorTable>) }, null);
                var tables = new[] { fallback, additional, exact };
                Assert.That(method.Invoke(null, new object[] { "Eyebrows", tables }), Is.SameAs(exact));
                Assert.That(method.Invoke(null, new object[] { "Beard", tables }), Is.SameAs(additional));
                Assert.That(method.Invoke(null, new object[] { "Skin", tables }), Is.SameAs(fallback));
                additional.AlsoAppliesTo = null;
                Assert.That(method.Invoke(null, new object[] { "Beard", tables }), Is.SameAs(fallback));
            }
            finally
            {
                Object.DestroyImmediate(exact);
                Object.DestroyImmediate(additional);
                Object.DestroyImmediate(fallback);
            }
        }

        [TestCase("Beard", true)]
        [TestCase("Eyebrows", true)]
        [TestCase("beard", false)]
        [TestCase("Skin", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void AdditionalSharedColorsParticipateInCompatibility(string colorName, bool expected)
        {
            var table = ScriptableObject.CreateInstance<SharedColorTable>();
            try
            {
                table.sharedColorName = "Hair";
                table.AlsoAppliesTo = new[] { null, "", "Eyebrows", "Beard" };
                Assert.That(CompatibilityMethod.Invoke(null, new object[] { table, colorName }), Is.EqualTo(expected));
                table.AlsoAppliesTo = null;
                Assert.That(CompatibilityMethod.Invoke(null, new object[] { table, colorName }), Is.False);
            }
            finally { Object.DestroyImmediate(table); }
        }

        [Test]
        public void StandardPaletteUsesAssetNameAndFallsBackOnlyWhenMissing()
        {
            var specific = ScriptableObject.CreateInstance<SharedColorTable>();
            var fallback = ScriptableObject.CreateInstance<SharedColorTable>();
            try
            {
                specific.name = "HairColors";
                specific.sharedColorName = "Unrelated metadata";
                fallback.name = "DefaultColors";
                var method = typeof(OverlayColorDataPropertyDrawer).GetMethod("FindStandardColorTable",
                    BindingFlags.Static | BindingFlags.NonPublic, null,
                    new[] { typeof(string), typeof(System.Collections.Generic.IEnumerable<SharedColorTable>) }, null);
                var tables = new[] { fallback, specific };
                Assert.That(method.Invoke(null, new object[] { "Hair", tables }), Is.SameAs(specific));
                Assert.That(method.Invoke(null, new object[] { "Skin", tables }), Is.SameAs(fallback));
                Assert.That(method.Invoke(null, new object[] { "Skin", new[] { specific } }), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(specific);
                Object.DestroyImmediate(fallback);
            }
        }

        [Test]
        public void StandardPaletteCopiesChannelsWithoutRenamingSharedColorOrSharingData()
        {
            var current = new OverlayColorData(4) { name = "Hair", isBaseColor = true };
            var palette = new OverlayColorData(1) { name = "Red", displayColor = Color.red };
            palette.channelMask[0] = Color.red;
            palette.channelAdditiveMask[0] = Color.blue;
            palette.PropertyBlock = new UMAMaterialPropertyBlock { alwaysUpdate = true };
            var method = typeof(UMA.CharacterSystem.Editors.DynamicCharacterAvatarEditor)
                .GetMethod("CopyPaletteColor", BindingFlags.Static | BindingFlags.NonPublic);
            var copy = (OverlayColorData)method.Invoke(null, new object[] { current, palette });
            Assert.That(copy.name, Is.EqualTo("Hair"));
            Assert.That(copy.isBaseColor, Is.True);
            Assert.That(copy.channelMask, Is.EqualTo(palette.channelMask));
            Assert.That(copy.channelAdditiveMask, Is.EqualTo(palette.channelAdditiveMask));
            Assert.That(copy.displayColor, Is.EqualTo(Color.red));
            Assert.That(copy.PropertyBlock.alwaysUpdate, Is.True);
            Assert.That(copy.PropertyBlock, Is.Not.SameAs(palette.PropertyBlock));
            copy.channelMask[0] = Color.green;
            copy.channelAdditiveMask[0] = Color.white;
            Assert.That(palette.channelMask[0], Is.EqualTo(Color.red));
            Assert.That(palette.channelAdditiveMask[0], Is.EqualTo(Color.blue));
        }

        private static readonly MethodInfo CompatibilityMethod =
            typeof(OverlayColorDataPropertyDrawer).GetMethod("IsSharedColorTableCompatible",
                BindingFlags.Static | BindingFlags.NonPublic);

        [TestCase("Skin", "Skin", true)]
        [TestCase("Skin", "Hair", false)]
        [TestCase("Skin", "skin", false)]
        [TestCase("", "Skin", true)]
        [TestCase("   ", "Skin", true)]
        public void TableCompatibilityUsesSharedColorNameAndAllowsGenericTables(
            string tableSharedColorName, string sharedColorName, bool expected)
        {
            Assert.That(CompatibilityMethod, Is.Not.Null);
            SharedColorTable table = ScriptableObject.CreateInstance<SharedColorTable>();
            try
            {
                table.sharedColorName = tableSharedColorName;
                bool compatible = (bool)CompatibilityMethod.Invoke(null,
                    new object[] { table, sharedColorName });
                Assert.That(compatible, Is.EqualTo(expected));
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }
    }
}
