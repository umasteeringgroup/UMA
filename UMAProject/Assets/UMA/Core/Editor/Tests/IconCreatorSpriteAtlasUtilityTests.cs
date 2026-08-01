#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;

namespace UMA.Editors.Tests
{
    public sealed class IconCreatorSpriteAtlasUtilityTests
    {
        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void AtlasFolderIsCreatedUnderTheIconRoot()
        {
            Assert.AreEqual(
                "Assets/UMA/UMA3/Wearables/Icons/SpriteAtlases",
                IconCreatorSpriteAtlasUtility.GetAtlasFolder(
                    "Assets/UMA/UMA3/Wearables/Icons"));
        }

        [Test]
        [Category("UMA")]
        [Category("IconCreator")]
        public void AtlasFolderRejectsPathsOutsideAssets()
        {
            Assert.Throws<ArgumentException>(() =>
                IconCreatorSpriteAtlasUtility.GetAtlasFolder(Path.GetTempPath()));
        }
    }
}
#endif
