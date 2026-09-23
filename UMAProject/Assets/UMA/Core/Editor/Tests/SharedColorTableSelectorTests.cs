using System.Reflection;
using NUnit.Framework;
using UMA.Editors;
using UnityEngine;

namespace UMA.Tests
{
    public class SharedColorTableSelectorTests
    {
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
