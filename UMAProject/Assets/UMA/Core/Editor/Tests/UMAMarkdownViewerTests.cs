#if UNITY_EDITOR

using System.Reflection;
using NUnit.Framework;

namespace UMA.Editors.Tests
{
    public sealed class UMAMarkdownViewerTests
    {
        private static MethodInfo convertInlineMarkdown;

        [OneTimeSetUp]
        public void FindConverter()
        {
            convertInlineMarkdown = typeof(UMAMarkdownViewer).GetMethod(
                "ConvertInlineMarkdown",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(convertInlineMarkdown);
        }

        [Test]
        [Category("UMA")]
        [Category("MarkdownViewer")]
        public void InlineCodePreservesMenuAndPathPunctuation()
        {
            string rendered = Convert("`Assets > Create/UMA/Core\\Material`");

            Assert.AreEqual(
                "<color=#c7254e>Assets > Create/UMA/Core\\Material</color>",
                rendered);
            Assert.IsFalse(rendered.Contains("&gt;"));
        }

        [Test]
        [Category("UMA")]
        [Category("MarkdownViewer")]
        public void PlainWindowsPathPreservesBackslashSeparators()
        {
            const string path = "C:\\GitHub\\UMA\\Assets";

            Assert.AreEqual(path, Convert(path));
        }

        [Test]
        [Category("UMA")]
        [Category("MarkdownViewer")]
        public void LiteralAngleBracketsDoNotUseHtmlEntities()
        {
            string rendered = Convert("minimum < value > maximum");

            Assert.AreEqual(
                "minimum <noparse><</noparse> value > maximum",
                rendered);
            Assert.IsFalse(rendered.Contains("&lt;"));
            Assert.IsFalse(rendered.Contains("&gt;"));
        }

        [Test]
        [Category("UMA")]
        [Category("MarkdownViewer")]
        public void MarkdownBackslashEscapeStillRemovesEscapablePrefix()
        {
            Assert.AreEqual("Assets > Create", Convert("Assets \\> Create"));
        }

        private static string Convert(string markdown)
        {
            return (string)convertInlineMarkdown.Invoke(
                null,
                new object[] { markdown, null });
        }
    }
}

#endif
