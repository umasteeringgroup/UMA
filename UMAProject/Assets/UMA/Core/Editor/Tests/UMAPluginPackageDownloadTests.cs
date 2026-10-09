#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.Editors.PackageSupport;

namespace UMA.Editors.Tests
{
    public sealed class UMAPluginPackageDownloadTests
    {
        private string directory;
        [SetUp] public void SetUp()
        {
            directory = Path.GetFullPath("Library/UMA/PluginDownloadTests/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }
        [TearDown] public void TearDown()
        {
            string parent = Path.GetFullPath("Library/UMA/PluginDownloadTests") + Path.DirectorySeparatorChar;
            if (directory != null && directory.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void DownloadLocationMatchesTheProvidedReleaseRuleForEveryPluginAndCompanion()
        {
            foreach (var kind in UMAContentCatalog.Plugins)
            {
                var location = new UMAPluginPackageDownload.Location(kind, "UMA NextGen 3.1f2");
                Assert.That(location.Url, Is.EqualTo("https://github.com/umasteeringgroup/UMA/releases/download/V3.1f2/" + UMAContentCatalog.PackageStem(kind) + "-3.1.2.unitypackage"));
                Assert.That(location.ReleasePage, Is.EqualTo("https://github.com/umasteeringgroup/UMA/releases/tag/V3.1f2"));
                Assert.That(location.CachePath, Does.StartWith(Path.GetFullPath("Library/UMA/PluginDownloads") + Path.DirectorySeparatorChar));
            }
            Assert.That(new UMAPluginPackageDownload.Location(UMAContentKind.Dismemberment, "3.1f2").FileName,
                Is.EqualTo("Dismemberment-3.1.2.unitypackage"));
        }

        [TestCase("3.2b3", "V3.2b3", "3.2.0-beta.3")]
        [TestCase("3.1f12", "V3.1f12", "3.1.12")]
        public void ReleaseAndPackageVersionsRemainDistinct(string version, string tag, string packageVersion)
        {
            var location = new UMAPluginPackageDownload.Location(UMAContentKind.OverlayPainterTests, version);
            Assert.That(location.Url, Does.EndWith("/" + tag + "/OverlayPainterTests-" + packageVersion + ".unitypackage"));
        }

        [TestCase(null)] [TestCase("")] [TestCase("3.1f")] [TestCase("../../../elsewhere")]
        public void InvalidVersionCannotSelectAFallbackReleaseOrEscapeCache(string version)
            => Assert.Throws<InvalidDataException>(() => new UMAPluginPackageDownload.Location(UMAContentKind.HairCards, version));

        [TestCase(false)] [TestCase(true)]
        public async Task TransferStreamsExactBytesAndFollowsRedirects(bool redirect)
        {
            byte[] payload = Enumerable.Range(0, 150000).Select(i => (byte)(i % 251)).ToArray();
            using var server = new Server(payload, redirect: redirect);
            string temporary = Path.Combine(directory, "package.unitypackage.part");
            using (var transfer = new UMAPluginPackageTransfer(server.Url, temporary))
            {
                await WaitFor(transfer);
                Assert.That(transfer.Succeeded, Is.True, transfer.Error);
                transfer.CloseCompletedRequest();
                Assert.That(File.ReadAllBytes(temporary), Is.EqualTo(payload));
                Assert.That(Directory.GetFiles(directory, "*.unitypackage"), Is.Empty, "Unvalidated downloads must not be discoverable as packages.");
            }
            Assert.That(File.Exists(temporary), Is.False);
        }

        [Test]
        public async Task MissingReleaseReports404AndCleansTheTemporaryFile()
        {
            using var server = new Server(Encoding.UTF8.GetBytes("Not Found"), status: 404);
            string temporary = Path.Combine(directory, "missing.part");
            using (var transfer = new UMAPluginPackageTransfer(server.Url, temporary))
            {
                await WaitFor(transfer);
                Assert.That(transfer.Succeeded, Is.False);
                Assert.That(transfer.ResponseCode, Is.EqualTo(404));
            }
            Assert.That(File.Exists(temporary), Is.False);
        }

        [Test]
        public async Task CancellingAnIncompleteTransferDeletesItWithoutPublishingAPackage()
        {
            using var server = new Server(new byte[4096], stall: true);
            string temporary = Path.Combine(directory, "cancelled.part");
            using (var transfer = new UMAPluginPackageTransfer(server.Url, temporary))
            {
                for (int i = 0; i < 100 && transfer.DownloadedBytes == 0; i++) await Task.Delay(20);
                Assert.That(transfer.DownloadedBytes, Is.GreaterThan(0));
                Assert.That(transfer.IsDone, Is.False);
            }
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }

        [Test]
        public void InvalidDownloadCannotPassArchiveValidation()
        {
            string path = Path.Combine(directory, "html-error.part");
            File.WriteAllText(path, "<html>Download unavailable</html>");
            var location = new UMAPluginPackageDownload.Location(UMAContentKind.HairCards, UMASettings.GetSettings().UMAVersion);
            Assert.That(UMAPluginPackageDownload.ValidateDownload(path, UMAContentKind.HairCards, location, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void DownloadValidationRejectsWrongPluginOrReleaseAndAcceptsCurrentArchive()
        {
            var kind = UMAContentKind.HairCards;
            var location = new UMAPluginPackageDownload.Location(kind, UMASettings.GetSettings().UMAVersion);
            string path = Path.GetFullPath(Path.Combine("Build/Plugins", location.FileName));
            if (!File.Exists(path)) Assert.Ignore("Build the optional Hair Cards archive to run validation against its real payload.");
            Assert.That(UMAPluginPackageDownload.ValidateDownload(path, kind, location, out string error), Is.True, error);
            Assert.That(UMAPluginPackageDownload.ValidateDownload(path, UMAContentKind.Dismemberment, location, out error), Is.False);
            var wrongRelease = new UMAPluginPackageDownload.Location(kind, "999.1f1");
            Assert.That(UMAPluginPackageDownload.ValidateDownload(path, kind, wrongRelease, out error), Is.False);
            Assert.That(error, Does.Contain("999.1f1"));
        }

        private static async Task WaitFor(UMAPluginPackageTransfer transfer)
        {
            for (int i = 0; i < 500 && !transfer.IsDone; i++) await Task.Delay(20);
            Assert.That(transfer.IsDone, Is.True, "Local transfer did not complete within ten seconds.");
        }

        // Real loopback HTTP exercises UnityWebRequest without depending on published GitHub assets.
        private sealed class Server : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource stop = new CancellationTokenSource();
            private readonly Task serving;
            public readonly string Url;
            public Server(byte[] payload, int status = 200, bool redirect = false, bool stall = false)
            {
                listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/package";
                serving = Task.Run(async () =>
                {
                    try
                    {
                        for (int request = 0; request < (redirect ? 2 : 1); request++)
                        {
                            using var client = await listener.AcceptTcpClientAsync();
                            using var stream = client.GetStream();
                            var headers = new StringBuilder(); var b = new byte[1];
                            while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && headers.Length < 8192)
                            {
                                if (await stream.ReadAsync(b, 0, 1, stop.Token) == 0) return;
                                headers.Append((char)b[0]);
                            }
                            bool move = redirect && request == 0;
                            string response = move ? "HTTP/1.1 302 Found\r\nLocation: " + Url + "/redirected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                                : "HTTP/1.1 " + status + (status == 200 ? " OK" : " Not Found") + "\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n";
                            byte[] bytes = Encoding.ASCII.GetBytes(response);
                            await stream.WriteAsync(bytes, 0, bytes.Length, stop.Token);
                            if (!move)
                            {
                                await stream.WriteAsync(payload, 0, stall ? 1 : payload.Length, stop.Token);
                                if (stall) await Task.Delay(Timeout.Infinite, stop.Token);
                            }
                        }
                    }
                    catch (Exception) when (stop.IsCancellationRequested) { }
                });
            }
            public void Dispose()
            {
                stop.Cancel(); listener.Stop();
                try { serving.GetAwaiter().GetResult(); }
                finally { stop.Dispose(); }
            }
        }
    }
}
#endif
