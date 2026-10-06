using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace UMA.Editors.PackageSupport
{
    public static class UMAPackageVersionUtility
    {
        public static string Normalize(string value, out string umaVersion)
        {
            var match = Regex.Match(value?.Trim() ?? "",
                @"^(?:UMA(?: NextGen)?\s+)?(?<major>\d+)\.(?<minor>\d+)(?:(?<stage>[abf])(?<revision>\d+)|\.(?<patch>\d+)(?<suffix>[-+][0-9A-Za-z.-]+)?)$");
            if (!match.Success) throw new InvalidDataException("Unsupported UMASettings version: " + value);
            string prefix = match.Groups["major"].Value + "." + match.Groups["minor"].Value;
            string stage = match.Groups["stage"].Value;
            if (stage.Length == 0)
            {
                umaVersion = prefix + "." + match.Groups["patch"].Value + match.Groups["suffix"].Value;
                return umaVersion;
            }
            string revision = match.Groups["revision"].Value;
            umaVersion = prefix + stage + revision;
            return stage == "f" ? prefix + "." + revision :
                prefix + ".0-" + (stage == "a" ? "alpha" : "beta") + "." + revision;
        }

        public static string SyncFromInstalledSettings(out string umaVersion)
        {
            var settings = UMASettings.GetSettings();
            if (settings == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(settings)))
                throw new InvalidOperationException("Package builds require a saved, installed UMASettings asset.");
            string version = Normalize(settings.UMAVersion, out umaVersion);
            string path = UMAPathUtility.ResolveAbsolutePath(UMAPathUtility.ResolveInstallAssetPath("package.json"));
            string original = File.ReadAllText(path);
            var versionField = new Regex("\"version\"\\s*:\\s*\"[^\"]*\"");
            if (!versionField.IsMatch(original)) throw new InvalidDataException("UMA package.json has no version field.");
            string updated = versionField.Replace(original, "\"version\": \"" + version + "\"", 1);
            var umaField = new Regex("\"umaVersion\"\\s*:\\s*\"[^\"]*\"");
            updated = umaField.IsMatch(updated) ? umaField.Replace(updated, "\"umaVersion\": \"" + umaVersion + "\"", 1) :
                versionField.Replace(updated, "\"version\": \"" + version + "\",\n  \"umaVersion\": \"" + umaVersion + "\"", 1);
            if (updated != original) File.WriteAllText(path, updated, new UTF8Encoding(false));
            return version;
        }
    }
}
