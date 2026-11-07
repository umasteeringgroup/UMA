using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace UMA.Editors
{
    /// <summary>A durable event journal, readable as XML even before the test run finishes.</summary>
    public sealed class UMATestRunLog
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly byte[] Footer = Utf8.GetBytes("</uma-test-log>");
        public string FilePath { get; }

        public UMATestRunLog(string filePath)
        {
            FilePath = Path.GetFullPath(filePath);
        }

        public void BeginRun(IReadOnlyDictionary<string, string> details)
        {
            string directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            if (File.Exists(FilePath))
            {
                string archiveDirectory = Path.Combine(directory, "UMA-TestLogs");
                Directory.CreateDirectory(archiveDirectory);
                string archive = Path.Combine(archiveDirectory,
                    File.GetLastWriteTimeUtc(FilePath).ToString("yyyyMMdd'T'HHmmss.fffffff'Z'") +
                    "-" + Guid.NewGuid().ToString("N") + ".xml");
                File.Copy(FilePath, archive);
                using (var saved = new FileStream(archive, FileMode.Open, FileAccess.Write, FileShare.Read))
                    saved.Flush(true);
            }

            byte[] document = Serialize(writer =>
            {
                writer.WriteStartElement("uma-test-log");
                writer.WriteAttributeString("schema-version", "1");
                writer.WriteAttributeString("run-id", Guid.NewGuid().ToString("N"));
                writer.WriteString("\n");
                writer.WriteFullEndElement();
            });
            using (var stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                byte[] declaration = Utf8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
                stream.Write(declaration, 0, declaration.Length);
                stream.Write(document, 0, document.Length);
                stream.Flush(true);
            }
            Append("run-start", details);
        }

        public void Append(string eventName, IReadOnlyDictionary<string, string> details = null,
            string message = null, string stackTrace = null)
        {
            byte[] entry = Serialize(writer =>
            {
                writer.WriteWhitespace("  ");
                writer.WriteStartElement(eventName);
                writer.WriteAttributeString("utc", DateTime.UtcNow.ToString("O"));
                if (details != null)
                    foreach (var pair in details)
                        writer.WriteAttributeString(pair.Key, XmlText(pair.Value));
                if (!string.IsNullOrEmpty(message)) writer.WriteElementString("message", XmlText(message));
                if (!string.IsNullOrEmpty(stackTrace)) writer.WriteElementString("stack-trace", XmlText(stackTrace));
                writer.WriteEndElement();
                writer.WriteWhitespace("\n");
            });

            // Only replace the closing tag, keeping cost proportional to this event rather
            // than rewriting the accumulated log after every test. Never hold the file open
            // across callbacks, so it survives domain reloads and can be inspected live.
            using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                if (stream.Length < Footer.Length) throw new InvalidDataException("Test log is truncated: " + FilePath);
                long end = stream.Length - Footer.Length;
                stream.Position = end;
                for (int i = 0; i < Footer.Length; i++)
                    if (stream.ReadByte() != Footer[i])
                        throw new InvalidDataException("Test log has an incomplete closing tag: " + FilePath);
                stream.Position = end;
                stream.Write(entry, 0, entry.Length);
                stream.Write(Footer, 0, Footer.Length);
                stream.SetLength(stream.Position);
                stream.Flush(true);
            }
        }

        private static byte[] Serialize(Action<XmlWriter> write)
        {
            using (var buffer = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
                {
                    Encoding = Utf8, OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment,
                    NewLineHandling = NewLineHandling.Entitize, CloseOutput = false
                })) write(writer);
                return buffer.ToArray();
            }
        }

        private static string XmlText(string value)
        {
            if (value == null) return string.Empty;
            var result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    result.Append(c).Append(value[++i]);
                }
                else result.Append(XmlConvert.IsXmlChar(c) ? c : '\uFFFD');
            }
            return result.ToString();
        }
    }
}
