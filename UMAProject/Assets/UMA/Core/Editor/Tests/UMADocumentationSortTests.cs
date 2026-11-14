using System.Collections.Generic;
using NUnit.Framework;

namespace UMA.Editors.Tests
{
    public sealed class UMADocumentationSortTests
    {
        [Test]
        public void SortPutsFavoritesFirstAndOrdersBothGroupsByNameIgnoringCase()
        {
            var favorites = new HashSet<string> { "Docs/Wardrobe.md", "Docs/atlas.md" };
            var paths = new List<string>
            {
                "Docs/zebra.md", "Docs/Wardrobe.md", "Plugin/Bones.md", "Docs/atlas.md", "Docs/apple.md", "Docs/Getting Started.md",
            };

            UMADocumentationWindow.SortAlphabetically(paths, favorites.Contains);

            Assert.That(paths, Is.EqualTo(new[]
            {
                "Docs/atlas.md", "Docs/Wardrobe.md",
                "Docs/apple.md", "Plugin/Bones.md", "Docs/Getting Started.md", "Docs/zebra.md",
            }));
        }
    }
}
