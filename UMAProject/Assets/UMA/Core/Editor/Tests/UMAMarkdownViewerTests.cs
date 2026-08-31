#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace UMA.Editors.Tests
{
    public sealed class UMAMarkdownViewerTests
    {
        private static MethodInfo convertInlineMarkdown;
        private static MethodInfo applyLinkPresentation;
        private static Type linkTargetListType;

        [OneTimeSetUp]
        public void FindConverter()
        {
            convertInlineMarkdown = typeof(UMAMarkdownViewer).GetMethod(
                "ConvertInlineMarkdown",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(convertInlineMarkdown);
            applyLinkPresentation = typeof(UMAMarkdownViewer).GetMethod(
                "ApplyLinkPresentation",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(applyLinkPresentation);

            Type linkTargetType = typeof(UMAMarkdownViewer).GetNestedType(
                "LinkTarget",
                BindingFlags.NonPublic);
            Assert.NotNull(linkTargetType);
            linkTargetListType = typeof(List<>).MakeGenericType(linkTargetType);
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

        [Test]
        [Category("UMA")]
        [Category("MarkdownViewer")]
        public void AnchorLinkRendersLabelOnceAsClickableRichText()
        {
            const string label = "The essential mental model";
            string rendered = ConvertWithLinks(
                "[" + label + "](#the-essential-mental-model)",
                out int linkCount);

            Assert.AreEqual(1, linkCount);
            Assert.AreEqual(
                "<link=\"0\"><color=#2f75c0><b>" + label + "</b></color></link>",
                rendered);
            Assert.AreEqual(
                rendered.IndexOf(label, StringComparison.Ordinal),
                rendered.LastIndexOf(label, StringComparison.Ordinal));
        }

        [Test]
        [Category("UMA")]
        [Category("MarkdownViewer")]
        public void MultipleLinksReceiveDistinctRichTextTargets()
        {
            string rendered = ConvertWithLinks("[First](#first) and [Second](#second)", out int linkCount);

            Assert.AreEqual(2, linkCount);
            StringAssert.Contains("<link=\"0\"><color=#2f75c0><b>First</b></color></link>", rendered);
            StringAssert.Contains("<link=\"1\"><color=#2f75c0><b>Second</b></color></link>", rendered);
        }

        [Test]
        [Category("UMA")]
        [Category("MarkdownViewer")]
        public void HoverPresentationHighlightsAndUnderlinesOnlyHoveredLink()
        {
            string rendered = ConvertWithLinks("[First](#first) and [Second](#second)", out int linkCount);
            string presented = PresentLinks(rendered, linkCount, 0, -1, 100);

            StringAssert.Contains(
                "<link=\"0\"><u><mark=#2f75c038><color=#2f75c0><b>First</b></color></mark></u></link>",
                presented);
            StringAssert.Contains(
                "<link=\"1\"><color=#2f75c0><b>Second</b></color></link>",
                presented);
        }

        [Test]
        [Category("UMA")]
        [Category("MarkdownViewer")]
        public void ClickPresentationAppliesBounceSizeOnlyToClickedLink()
        {
            string rendered = ConvertWithLinks("[First](#first) and [Second](#second)", out int linkCount);
            string presented = PresentLinks(rendered, linkCount, -1, 1, 114);

            StringAssert.Contains(
                "<link=\"0\"><color=#2f75c0><b>First</b></color></link>",
                presented);
            StringAssert.Contains(
                "<link=\"1\"><size=114%><color=#2f75c0><b>Second</b></color></size></link>",
                presented);
        }

        private static string Convert(string markdown)
        {
            return (string)convertInlineMarkdown.Invoke(
                null,
                new object[] { markdown, null });
        }

        private static string ConvertWithLinks(string markdown, out int linkCount)
        {
            object links = Activator.CreateInstance(linkTargetListType);
            string rendered = (string)convertInlineMarkdown.Invoke(
                null,
                new[] { (object)markdown, links });
            linkCount = ((ICollection)links).Count;
            return rendered;
        }

        private static string PresentLinks(
            string richText,
            int linkCount,
            int hoveredLinkIndex,
            int animatedLinkIndex,
            int animatedSizePercent)
        {
            return (string)applyLinkPresentation.Invoke(
                null,
                new object[]
                {
                    richText,
                    linkCount,
                    hoveredLinkIndex,
                    animatedLinkIndex,
                    animatedSizePercent
                });
        }
    }
}

#endif
