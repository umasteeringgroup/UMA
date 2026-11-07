#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace UMA.Editors.Tests
{
    public sealed class UMATestRunLogTests
    {
        private string directory;
        private string path;
        private UMATestRunLog log;

        [SetUp]
        public void SetUp()
        {
            directory = Path.GetFullPath(Path.Combine("Temp", "UMATestRunLogTests_" + Guid.NewGuid().ToString("N")));
            path = Path.Combine(directory, "UMA-TestLog.xml");
            log = new UMATestRunLog(path);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void UnfinishedTestIsReadableImmediatelyAndSurvivesWriterRecreation()
        {
            log.BeginRun(new Dictionary<string, string> { ["name"] = "test run" });
            string runId = (string)XDocument.Load(path).Root.Attribute("run-id");
            log.Append("test-start", new Dictionary<string, string> { ["id"] = "42", ["name"] = "A.Test" });
            var unfinished = XDocument.Load(path);
            Assert.That((string)unfinished.Root.Elements().Last().Attribute("name"), Is.EqualTo("A.Test"));
            Assert.That(unfinished.Descendants("test-finish"), Is.Empty);
            Assert.That(unfinished.Descendants("run-finish"), Is.Empty);

            // A reloaded callback has a new writer, with no buffered state from the old one.
            log = new UMATestRunLog(path);
            log.Append("test-finish", new Dictionary<string, string> { ["id"] = "42", ["result"] = "Passed" });
            log.Append("run-finish");
            var finished = XDocument.Load(path);
            Assert.That((string)finished.Root.Attribute("run-id"), Is.EqualTo(runId));
            Assert.That(finished.Root.Elements().Select(e => e.Name.LocalName),
                Is.EqualTo(new[] { "run-start", "test-start", "test-finish", "run-finish" }));
            Assert.That(finished.Root.Elements().All(e => DateTime.TryParse((string)e.Attribute("utc"), out _)), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NextRunArchivesPreviousEvidenceBeforeReplacingCurrentLog(bool completed)
        {
            log.BeginRun(null);
            log.Append("test-start", new Dictionary<string, string> { ["name"] = "Previous.Test" });
            if (completed) log.Append("run-finish");
            byte[] previous = File.ReadAllBytes(path);
            new UMATestRunLog(path).BeginRun(new Dictionary<string, string> { ["name"] = "Next run" });
            string archive = Directory.GetFiles(Path.Combine(directory, "UMA-TestLogs"), "*.xml").Single();
            Assert.That(File.ReadAllBytes(archive), Is.EqualTo(previous));
            Assert.That(XDocument.Load(path).Descendants("test-start"), Is.Empty);
            Assert.That((string)XDocument.Load(path).Descendants("run-start").Single().Attribute("name"), Is.EqualTo("Next run"));
        }

        [Test]
        public void FailureDetailsEscapeXmlAndReplaceInvalidCharacters()
        {
            log.BeginRun(null);
            string name = "Case(\"a<&b\", \"\uD83D\uDE80\")";
            log.Append("test-finish", new Dictionary<string, string> { ["name"] = name, ["result"] = "Failed:Error" },
                "Message <&>\u0001", "Line 1\nLine 2\uD800");
            var entry = XDocument.Load(path).Descendants("test-finish").Single();
            Assert.That((string)entry.Attribute("name"), Is.EqualTo(name));
            Assert.That(entry.Element("message").Value, Is.EqualTo("Message <&>\uFFFD"));
            Assert.That(entry.Element("stack-trace").Value, Is.EqualTo("Line 1\nLine 2\uFFFD"));
        }

        [Test]
        public void TruncatedLogIsPreservedInsteadOfOverwrittenByLaterEvents()
        {
            log.BeginRun(null);
            byte[] bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes.Take(bytes.Length - 3).ToArray());
            byte[] interrupted = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => log.Append("test-finish"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(interrupted));
        }
    }
}
#endif
