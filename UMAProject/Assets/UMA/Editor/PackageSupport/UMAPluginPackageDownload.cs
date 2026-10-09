using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace UMA.Editors.PackageSupport
{
    /// <summary>Streams to an unpublished temporary file; disposing also removes incomplete downloads.</summary>
    public sealed class UMAPluginPackageTransfer : IDisposable
    {
        private UnityWebRequest request;
        private readonly string temporaryPath;
        public bool IsDone => request == null || request.isDone;
        public bool Succeeded => request != null && request.result == UnityWebRequest.Result.Success;
        public float Progress => request == null ? 0 : Mathf.Clamp01(request.downloadProgress);
        public ulong DownloadedBytes => request?.downloadedBytes ?? 0;
        public long ResponseCode => request?.responseCode ?? 0;
        public string Error => request?.error;

        public UMAPluginPackageTransfer(string url, string temporaryPath)
        {
            this.temporaryPath = temporaryPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath));
                request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
                request.downloadHandler = new DownloadHandlerFile(temporaryPath) { removeFileOnAbort = true };
                request.timeout = 300;
                request.redirectLimit = 8;
                request.SendWebRequest();
            }
            catch { Dispose(); throw; }
        }

        public void CloseCompletedRequest()
        {
            if (!IsDone) throw new InvalidOperationException("The download is still running.");
            request?.Dispose(); request = null;
        }

        public void Dispose()
        {
            if (request != null)
            {
                if (!request.isDone) request.Abort();
                request.Dispose(); request = null;
            }
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    [InitializeOnLoad]
    public static class UMAPluginPackageDownload
    {
        public const string ReleasesUrl = "https://github.com/umasteeringgroup/UMA/releases";

        public sealed class Location
        {
            public readonly string UmaVersion, PackageVersion, FileName, Url, ReleasePage, CachePath;
            public Location(UMAContentKind kind, string installedVersion)
            {
                if (!UMAContentCatalog.IsPlugin(kind)) throw new ArgumentException("Only optional plugins use this download source.", nameof(kind));
                PackageVersion = UMAPackageVersionUtility.Normalize(installedVersion, out string release);
                UmaVersion = release;
                FileName = UMAContentCatalog.PackageStem(kind) + "-" + PackageVersion + ".unitypackage";
                string tag = Uri.EscapeDataString("V" + UmaVersion);
                Url = ReleasesUrl + "/download/" + tag + "/" + Uri.EscapeDataString(FileName);
                ReleasePage = ReleasesUrl + "/tag/" + tag;
                CachePath = Path.GetFullPath(Path.Combine("Library/UMA/PluginDownloads", "V" + UmaVersion, FileName));
            }
        }

        public sealed class Failure
        {
            public UMAContentKind Kind;
            public Location Location;
            public string Message;
            public string DownloadPage => Location?.ReleasePage ?? ReleasesUrl;
        }

        private static UMAPluginPackageTransfer transfer;
        private static Location location;
        private static UMAContentKind kind;
        private static Action<UMAContentKind, string> onDownloaded;
        public static bool IsActive => transfer != null;
        public static UMAContentKind? ActiveKind => IsActive ? kind : (UMAContentKind?)null;
        public static Failure LastFailure { get; private set; }

        static UMAPluginPackageDownload()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }

        public static string CurrentCacheDirectory
        {
            get
            {
                try
                {
                    var settings = UMASettings.GetSettings();
                    if (settings == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(settings))) return null;
                    return Path.GetDirectoryName(new Location(UMAContentKind.OverlayPainter, settings.UMAVersion).CachePath);
                }
                catch (InvalidDataException) { return null; }
            }
        }

        public static void DismissFailure() => LastFailure = null;

        public static void Start(UMAContentKind requestedKind, Action<UMAContentKind, string> completed)
        {
            if (IsActive) return;
            LastFailure = null;
            kind = requestedKind; location = null;
            try
            {
                var settings = UMASettings.GetSettings();
                if (settings == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(settings)))
                    throw new InvalidOperationException("A saved UMASettings asset is required to choose the plugin release.");
                location = new Location(kind, settings.UMAVersion);
                transfer = new UMAPluginPackageTransfer(location.Url, location.CachePath + ".part");
                onDownloaded = completed;
                EditorApplication.update += Update;
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        public static bool ValidateDownload(string path, UMAContentKind kind, Location expected, out string error)
        {
            if (!UMAContentPackageArchiveValidator.TryValidate(path, kind, out var archive, out error)) return false;
            if (archive.Manifest.contentVersion != expected.PackageVersion || archive.Manifest.umaVersion != expected.UmaVersion)
            { error = "The downloaded package is not for UMA " + expected.UmaVersion + "."; return false; }
            if (archive.Manifest.requiredPluginApiVersion != UMAPluginApi.Version)
            { error = "The downloaded package requires a different UMA plugin API version."; return false; }
            return UMAContentPackageInstaller.IsCoreVersionCompatible(archive.Manifest, out error);
        }

        private static void Update()
        {
            if (transfer == null) return;
            Action<UMAContentKind, string> completed = null;
            string path = null;
            UMAContentKind completedKind = kind;
            try
            {
                if (EditorUtility.DisplayCancelableProgressBar("Download " + UMAContentCatalog.DisplayName(kind),
                    location.FileName + " — " + (transfer.DownloadedBytes / (1024f * 1024f)).ToString("F1") + " MB", transfer.Progress))
                { Cancel(); return; }
                if (!transfer.IsDone) return;
                if (!transfer.Succeeded)
                {
                    string reason = transfer.ResponseCode == 404 ? "This package was not found in the GitHub release." : "The package could not be downloaded.";
                    Fail(reason + "\n" + (transfer.ResponseCode > 0 ? "HTTP " + transfer.ResponseCode + ": " : "") + transfer.Error);
                    return;
                }
                transfer.CloseCompletedRequest();
                string temporary = location.CachePath + ".part";
                if (!ValidateDownload(temporary, kind, location, out string error))
                { Fail("The downloaded file failed package validation.\n" + error); return; }
                // Only complete, validated archives become discoverable on the next install attempt.
                if (File.Exists(location.CachePath)) File.Replace(temporary, location.CachePath, null);
                else File.Move(temporary, location.CachePath);
                path = location.CachePath; completed = onDownloaded;
                Cleanup();
            }
            catch (Exception ex) { Fail(ex.Message); return; }
            // The installer rechecks dependencies and pending transactions and preserves its normal prompts.
            completed?.Invoke(completedKind, path);
        }

        private static void Fail(string message)
        {
            LastFailure = new Failure { Kind = kind, Location = location, Message = message };
            Cleanup();
        }

        public static void Cancel()
        {
            if (IsActive)
                LastFailure = new Failure { Kind = kind, Location = location, Message = "Download cancelled. No package was installed. You can choose a local package instead." };
            Cleanup();
        }

        private static void Cleanup()
        {
            EditorApplication.update -= Update;
            onDownloaded = null;
            var previous = transfer; transfer = null;
            try { previous?.Dispose(); }
            catch (IOException ex) { Debug.LogWarning("[UMA Plugins] Could not remove the partial download: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Debug.LogWarning("[UMA Plugins] Could not remove the partial download: " + ex.Message); }
            finally { if (previous != null) EditorUtility.ClearProgressBar(); }
        }
    }
}
