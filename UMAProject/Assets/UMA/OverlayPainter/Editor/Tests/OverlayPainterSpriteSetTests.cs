#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class OverlayPainterSpriteSetTests
    {
        [Test]
        public void OptionalSpriteNamesFallBackToImportedName()
        {
            var spriteSet = ScriptableObject.CreateInstance<OverlayPainterSpriteSet>();
            try
            {
                spriteSet.setName = "Leather";
                spriteSet.spriteNames.Add("Light Brown Leather");

                Assert.That(spriteSet.DisplayName, Is.EqualTo("Leather"));
                Assert.That(spriteSet.GetSpriteName(0, "Sheet_0"),
                    Is.EqualTo("Light Brown Leather"));
                Assert.That(spriteSet.GetSpriteName(1, "Sheet_1"), Is.EqualTo("Sheet_1"));
            }
            finally
            {
                Object.DestroyImmediate(spriteSet);
            }
        }

        [TestCase("LeatherStampsAlbedoMap_0", 0)]
        [TestCase("LeatherStampsAlbedoMap_15", 15)]
        [TestCase("LeatherStampsAlbedoMap", -1)]
        public void ImportedSpriteSuffixDeterminesCrossSheetIndex(string spriteName, int expected)
        {
            Assert.That(OverlayPainterSpriteSetEditorUtility.ParseTrailingIndex(spriteName),
                Is.EqualTo(expected));
        }

        [Test]
        public void LeatherSampleHasThreeAlignedSixteenSpriteSheets()
        {
            OverlayPainterSpriteSet spriteSet = AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Textures/LeatherSpriteSet.asset"));

            Assert.That(spriteSet, Is.Not.Null);
            Assert.That(spriteSet.spriteSheets, Has.Count.EqualTo(3));
            Assert.That(spriteSet.spriteNames, Has.Count.EqualTo(16));
            Assert.That(OverlayPainterSpriteSetEditorUtility.GetCommonSpriteCount(spriteSet),
                Is.EqualTo(16));
            Assert.That(spriteSet.spriteSheets[0].channel, Is.EqualTo(TexturePaintChannel.Albedo));
            Assert.That(spriteSet.spriteSheets[1].channel, Is.EqualTo(TexturePaintChannel.Roughness));
            Assert.That(spriteSet.spriteSheets[1].inverted, Is.True);
            Assert.That(spriteSet.spriteSheets[2].channel, Is.EqualTo(TexturePaintChannel.Normal));
        }
    }
}
#endif
