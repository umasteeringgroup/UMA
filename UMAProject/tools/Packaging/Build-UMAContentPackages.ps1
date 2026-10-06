param(
    [string]$Uma3SourceDirectory = "Assets/UMA/UMA3",
    [string]$Uma2SourceDirectory = "Assets/UMA/UMA2",
    [string]$CoreSourceDirectory = "Assets/UMA",
    [string]$OutputDirectory = "Build/Content",
    [string]$CoreStagingDirectory = "Build/CorePackage",
    [string]$Version = "",
    [switch]$UseExistingPackages,
    [switch]$SkipCoreStaging,
    [switch]$FunctionsOnly
)

$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$manifestGuids = @{
    uma3 = "fb290a2345674c8d9e0f1a2b3c4d5e6f"
    uma2 = "0c3a1b3456784d9eaf102b3c4d5e6f70"
}
$pipelineSpecificContentGuids = [ordered]@{
    "0b2db86121404754db890f4c8dfe81b2" = "URP Bloom"
    "221518ef91623a7438a71fef23660601" = "URP WhiteBalance"
    "474bcb49853aa07438625e644c072ee6" = "URP UniversalAdditionalLightData"
    "58430e2c806ea6c49aed198fc9b656f0" = "URP-only UMA cutout shader pack"
    "7dbb57796d61e3e44baf573dadcfa669" = "URP-only UMA diffuse shader pack"
    "899c54efeace73346a0a16faa3afe726" = "URP Vignette"
    "933532a4fcc9baf4fa0491de14d08ed7" = "URP Lit shader"
    "97c23e3b12dc18c42a140437e53d3951" = "URP Tonemapping"
    "a79441f348de89743a2939f4d699eac1" = "URP UniversalAdditionalCameraData"
    "aa486462e6be1764e89c788ba30e61f7" = "HDRP MaterialExternalReferences"
    "b2686e09ec7aef44bad2843e4416f057" = "HDRP DiffusionProfileSettings"
    "c01700fd266d6914ababb731e09af2eb" = "URP DepthOfField"
    "ccf1aba9553839d41ae37dd52e9ebcce" = "URP MotionBlur"
    "d0353a89b1f911e48b9e16bdc9f2e058" = "URP material AssetVersion"
    "da692e001514ec24dbc4cca1949ff7e8" = "HDRP material AssetVersion"
    "de90b6ff34ca64449b132b66875423ed" = "URP-only UMA diffuse-alpha shader pack"
    "2ead69edbcd1b7642aae643cd21fd604" = "URP-only UMA hair ShaderGraph"
    "3f55b1083749a224f96274ec3f60641a" = "URP-only UMA cutout-normal-specular shader pack"
    "5cbdbeb64df539d4999c814e70b30d65" = "URP-only UMA alpha-hair shader pack"
    "877e0087dcad7a441a4b23b3ab3f8a48" = "URP-only UMA normal-specular shader pack"
    "ab87b81d084dd6047b63be46465e63c2" = "URP-only UMA metallic-occlusion-alpha shader pack"
    "e2c95817ac413714c8f36b80020c9867" = "URP-only UMA normal-metallic shader pack"
}

# Scan raw bytes rather than decoding native Unity assets. Text-serialized
# references and the native AssetIndexer format store GUIDs as 32 ASCII bytes,
# so a four-byte prefix index catches those references inexpensively and
# without treating a native asset as text. The disposable Unity validation
# projects separately audit direct dependencies resolved from every serialized
# asset format; both gates are required for a release.
if ($null -eq ("UMAContentByteReferenceScanner" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class UMAContentByteReferenceScanner
{
    public static string[] Find(string[] paths, string[] guidPatterns)
    {
        var patternsByPrefix =
            new Dictionary<uint, List<Tuple<string, byte[]>>>();
        foreach (string value in guidPatterns)
        {
            byte[] pattern = Encoding.ASCII.GetBytes(value.ToLowerInvariant());
            if (pattern.Length != 32) continue;
            uint prefix = Prefix(pattern, 0);
            List<Tuple<string, byte[]>> patterns;
            if (!patternsByPrefix.TryGetValue(prefix, out patterns))
            {
                patterns = new List<Tuple<string, byte[]>>();
                patternsByPrefix.Add(prefix, patterns);
            }
            patterns.Add(Tuple.Create(value.ToLowerInvariant(), pattern));
        }

        var matches = new List<string>();
        foreach (string path in paths)
        {
            byte[] bytes = File.ReadAllBytes(path);
            for (int offset = 0; offset <= bytes.Length - 32; offset++)
            {
                List<Tuple<string, byte[]>> patterns;
                if (!patternsByPrefix.TryGetValue(Prefix(bytes, offset),
                        out patterns))
                    continue;
                foreach (Tuple<string, byte[]> candidate in patterns)
                {
                    bool equal = true;
                    for (int index = 4; index < 32; index++)
                    {
                        byte value = ToLowerHex(bytes[offset + index]);
                        if (value != candidate.Item2[index])
                        {
                            equal = false;
                            break;
                        }
                    }
                    if (equal)
                    {
                        matches.Add(path + " -> " + candidate.Item1);
                        break;
                    }
                }
            }
        }
        return matches.ToArray();
    }

    private static uint Prefix(byte[] bytes, int offset)
    {
        uint value = 0;
        for (int index = 0; index < 4; index++)
            value = (value << 8) | ToLowerHex(bytes[offset + index]);
        return value;
    }

    private static byte ToLowerHex(byte value)
    {
        return value >= (byte)'A' && value <= (byte)'F'
            ? (byte)(value + 32)
            : value;
    }
}
'@
}

function Resolve-ProjectPath([string]$path) {
    if ([IO.Path]::IsPathRooted($path)) {
        return [IO.Path]::GetFullPath($path)
    }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $path))
}

function Get-CompatibleCoreMaximum([string]$version) {
    $match = [regex]::Match($version, "^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)")
    if (-not $match.Success) { throw "Cannot calculate Core compatibility for $version" }
    $major = [int]$match.Groups["major"].Value
    $minor = [int]$match.Groups["minor"].Value + 1
    return "$major.$minor.0"
}

function To-ProjectPath([string]$absolutePath) {
    $projectRoot = $repoRoot.TrimEnd('\', '/')
    $resolved = [IO.Path]::GetFullPath($absolutePath)
    if (-not $resolved.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the Unity project: $resolved"
    }
    return $resolved.Substring($projectRoot.Length + 1).Replace('\', '/')
}

function Assert-SafeArtifactDirectory([string]$path) {
    $projectRoot = $repoRoot.TrimEnd('\', '/')
    $resolved = [IO.Path]::GetFullPath($path).TrimEnd('\', '/')
    if (-not $resolved.StartsWith(
            $projectRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Content-package output must stay inside the Unity project: $resolved"
    }
    $relative = $resolved.Substring($projectRoot.Length + 1).Replace('\', '/')
    if (-not ($relative.StartsWith("Build/", [StringComparison]::OrdinalIgnoreCase) -or
              $relative.StartsWith("tmp/", [StringComparison]::OrdinalIgnoreCase))) {
        throw "Content-package output must be below Build or tmp: $resolved"
    }
}

function Test-UnityYamlFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    $stream = [IO.File]::OpenRead($path)
    try {
        if ($stream.Length -lt 5) { return $false }
        $prefix = [byte[]]::new(5)
        if ($stream.Read($prefix, 0, 5) -ne 5) { return $false }
        return [Text.Encoding]::ASCII.GetString($prefix) -eq "%YAML"
    }
    finally { $stream.Dispose() }
}

function Get-MetaGuid([string]$metaPath) {
    $text = [IO.File]::ReadAllText($metaPath)
    $matches = [regex]::Matches($text, "(?m)^guid:\s*([0-9a-fA-F]{32})\s*$")
    if ($matches.Count -ne 1) {
        throw "Meta file must contain exactly one valid GUID: $metaPath"
    }
    return $matches[0].Groups[1].Value.ToLowerInvariant()
}

function Assert-SafeUnityPackagePath([string]$path, [string]$installRoot) {
    $segments = @($path.Split('/'))
    $controlCharacters = @($path.ToCharArray() |
        Where-Object { [char]::IsControl([char]$_) })
    if ([string]::IsNullOrWhiteSpace($path) -or
        $path -ne $path.Trim() -or
        -not $path.StartsWith($installRoot + "/",
            [StringComparison]::Ordinal) -or
        $path.EndsWith("/", [StringComparison]::Ordinal) -or
        $path.Contains('\') -or $path.Contains(':') -or
        $controlCharacters.Count -gt 0 -or
        @($segments | Where-Object {
            [string]::IsNullOrEmpty($_) -or $_ -eq '.' -or $_ -eq '..'
        }).Count -gt 0) {
        throw "Unsafe or non-canonical package path for $installRoot`: $path"
    }
    if ($path.EndsWith(".unitypackage", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Nested unitypackage is not allowed in content: $path"
    }
}

function Sync-UMAPackageVersion([string]$coreSource) {
    $preferred = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Assets') -Filter 'UMAProjectSettings.asset' -Recurse -File |
        Where-Object { $_.Directory.Name -eq 'Resources' })
    if ($preferred.Count -gt 1) { throw 'Multiple UMAProjectSettings resources found; resolve the active settings in Unity before building.' }
    $settingsPath = if ($preferred.Count -eq 1) { $preferred[0].FullName } else {
        Join-Path $coreSource 'InternalDataStore/InGame/Resources/UMASettings.asset'
    }
    $settingsBytes = [IO.File]::ReadAllBytes($settingsPath)
    if ($settingsBytes.Length -lt 6 -or [Text.Encoding]::ASCII.GetString($settingsBytes, 0, 6) -ne '%YAML ') {
        throw "UMASettings is not Unity YAML: $settingsPath. Build through the Unity Editor for native assets."
    }
    $settingsText = [Text.Encoding]::UTF8.GetString($settingsBytes)
    $versionField = [regex]::Match($settingsText, '(?m)^\s*UMAVersion:\s*(.+?)\s*$')
    if (-not $versionField.Success) { throw "UMASettings has no serialized UMAVersion: $settingsPath" }
    $raw = $versionField.Groups[1].Value.Trim().Trim('"', "'")
    $match = [regex]::Match($raw, '^(?:UMA(?: NextGen)?\s+)?(?<major>\d+)\.(?<minor>\d+)(?:(?<stage>[abf])(?<revision>\d+)|\.(?<patch>\d+)(?<suffix>[-+][0-9A-Za-z.-]+)?)$')
    if (-not $match.Success) { throw "Unsupported UMASettings version: $raw" }
    $prefix = $match.Groups['major'].Value + '.' + $match.Groups['minor'].Value
    $stage = $match.Groups['stage'].Value
    if (-not $stage) {
        $umaVersion = $prefix + '.' + $match.Groups['patch'].Value + $match.Groups['suffix'].Value
        $version = $umaVersion
    } else {
        $revision = $match.Groups['revision'].Value
        $umaVersion = $prefix + $stage + $revision
        $version = if ($stage -eq 'f') { $prefix + '.' + $revision } else {
            $prefix + '.0-' + $(if ($stage -eq 'a') { 'alpha' } else { 'beta' }) + '.' + $revision
        }
    }
    $packagePath = Join-Path $coreSource 'package.json'
    $original = [IO.File]::ReadAllText($packagePath)
    $versionPattern = [regex]'"version"\s*:\s*"[^"]*"'
    if (-not $versionPattern.IsMatch($original)) { throw 'UMA package.json has no version field.' }
    $updated = $versionPattern.Replace($original, ('"version": "' + $version + '"'), 1)
    $umaPattern = [regex]'"umaVersion"\s*:\s*"[^"]*"'
    if ($umaPattern.IsMatch($updated)) {
        $updated = $umaPattern.Replace($updated, ('"umaVersion": "' + $umaVersion + '"'), 1)
    } else {
        $updated = $versionPattern.Replace($updated, ('"version": "' + $version + '",' + "`n" + '  "umaVersion": "' + $umaVersion + '"'), 1)
    }
    if ($updated -ne $original) { [IO.File]::WriteAllText($packagePath, $updated, $utf8NoBom) }
    return [pscustomobject]@{ Version=$version; UmaVersion=$umaVersion; SettingsPath=$settingsPath }
}

function Test-PackageSourceExcluded([string]$relative, [string[]]$excludedRoots) {
    $relative = $relative.Replace('\', '/')
    foreach ($part in $relative.Split('/')) {
        if ($part.EndsWith('~') -or $part.EndsWith('~.meta') -or $part.StartsWith('.')) { return $true }
    }
    foreach ($root in $excludedRoots) {
        if ($relative -eq $root -or $relative -eq ($root + '.meta') -or $relative.StartsWith($root + '/', [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Get-SourceRecords([string]$sourceRoot, [string]$installRoot, [string[]]$excludedRoots = @()) {
    $records = New-Object Collections.Generic.List[object]
    $guidSet = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $pathSet = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $resolvedRoot = Resolve-ProjectPath $sourceRoot
    if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
        throw "Content source is missing: $resolvedRoot"
    }
    if (((Get-Item -LiteralPath $resolvedRoot -Force).Attributes -band
            [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Content source root cannot be a junction or symbolic link: $resolvedRoot"
    }
    if (-not (Test-Path -LiteralPath ($resolvedRoot + ".meta") -PathType Leaf)) {
        throw "Content source root has no .meta file: $resolvedRoot.meta"
    }

    # A meta-driven package list is deterministic only after proving that no
    # real source asset or folder was silently skipped because its .meta is
    # missing. Common editor/OS debris is intentionally outside the payload.
    Get-ChildItem -LiteralPath $resolvedRoot -Recurse -Force -File |
        Where-Object { $_.Extension -ne ".meta" } | ForEach-Object {
            if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Content source asset cannot be a symbolic link: $($_.FullName)"
            }
            $relative = $_.FullName.Substring($resolvedRoot.Length).TrimStart(
                [char[]]"\/").Replace('\', '/')
            if (Test-PackageSourceExcluded $relative $excludedRoots) { return }
            $ignored = $relative.Equals("package.json",
                    [StringComparison]::OrdinalIgnoreCase) -or
                $relative.Equals("UMAContentManifest.json",
                    [StringComparison]::OrdinalIgnoreCase) -or
                $relative.EndsWith("~", [StringComparison]::Ordinal) -or
                $relative.EndsWith(".user", [StringComparison]::OrdinalIgnoreCase) -or
                $relative.EndsWith("/.DS_Store", [StringComparison]::OrdinalIgnoreCase) -or
                $relative.Equals(".DS_Store", [StringComparison]::OrdinalIgnoreCase)
            if (-not $ignored -and
                -not (Test-Path -LiteralPath ($_.FullName + ".meta") -PathType Leaf)) {
                throw "Content asset has no .meta file: $($_.FullName)"
            }
        }
    Get-ChildItem -LiteralPath $resolvedRoot -Recurse -Force -Directory |
        ForEach-Object {
            $relative = $_.FullName.Substring($resolvedRoot.Length).TrimStart([char[]]"\/")
            if (Test-PackageSourceExcluded $relative $excludedRoots) { return }
            if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Content source folder cannot be a junction or symbolic link: $($_.FullName)"
            }
            if (-not (Test-Path -LiteralPath ($_.FullName + ".meta") -PathType Leaf)) {
                throw "Content folder has no .meta file: $($_.FullName)"
            }
        }

    [string[]]$metaPaths = @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse `
        -Force -File -Filter "*.meta" | ForEach-Object { $_.FullName })
    [Array]::Sort($metaPaths, [StringComparer]::Ordinal)
    foreach ($metaPath in $metaPaths) {
            $metaFile = Get-Item -LiteralPath $metaPath
            $assetPath = $metaFile.FullName.Substring(0, $metaFile.FullName.Length - 5)
            $relative = $assetPath.Substring($resolvedRoot.Length).TrimStart([char[]]"\/")
            if (Test-PackageSourceExcluded $relative $excludedRoots) { continue }
            if ([string]::IsNullOrEmpty($relative)) { continue }
            $relativeNormalized = $relative.Replace('\', '/')
            if ($relativeNormalized.Equals("package.json", [StringComparison]::OrdinalIgnoreCase) -or
                $relativeNormalized.Equals("UMAContentManifest.json", [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            if ($relativeNormalized.EndsWith(".unitypackage", [StringComparison]::OrdinalIgnoreCase)) {
                throw "Nested unitypackage is not allowed in content: $assetPath"
            }

            $pathname = "$installRoot/$relativeNormalized"
            Assert-SafeUnityPackagePath $pathname $installRoot
            $guid = Get-MetaGuid $metaFile.FullName
            if (-not $guidSet.Add($guid)) { throw "Duplicate content GUID: $guid" }
            if (-not $pathSet.Add($pathname)) { throw "Duplicate content path: $pathname" }

            $isDirectory = Test-Path -LiteralPath $assetPath -PathType Container
            if (-not $isDirectory -and -not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
                throw "Meta file has no asset or folder: $($metaFile.FullName)"
            }
            $assetBytes = 0
            $assetHash = ""
            if (-not $isDirectory) {
                $assetBytes = (Get-Item -LiteralPath $assetPath).Length
                if ($assetBytes -eq 0) {
                    throw "Zero-byte content assets are unsupported because the manifest " +
                        "uses zero bytes to identify folder records: $assetPath"
                }
                $assetHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            $metaBytes = $metaFile.Length
            $metaHash = (Get-FileHash -LiteralPath $metaFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $records.Add([pscustomobject]@{
                Path = $pathname
                Guid = $guid
                Asset = if ($isDirectory) { $null } else { $assetPath }
                Meta = $metaFile.FullName
                Bytes = [long]$assetBytes
                Sha256 = $assetHash
                MetaBytes = [long]$metaBytes
                MetaSha256 = $metaHash
            })
        }
    # Unity does not materialize a leaf folder record that has no packaged
    # asset below it. Omitting those records keeps the ownership manifest
    # aligned with the tree Unity can actually import. Parent folders with at
    # least one packaged descendant remain explicit so their GUIDs survive.
    $packagedFilePaths = @($records | Where-Object { $null -ne $_.Asset } |
        ForEach-Object { $_.Path })
    return @($records | Where-Object {
        if ($null -ne $_.Asset) { return $true }
        $prefix = $_.Path.TrimEnd('/') + "/"
        return $null -ne ($packagedFilePaths | Where-Object {
            $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)
    })
}

function Write-UnityPackage(
    [string]$contentId,
    [string]$contentVersion,
    [string]$installRoot,
    [string[]]$dependencies,
    [string[]]$requiredPaths,
    $records,
    [string]$outputPath,
    [string]$workRoot,
    [int]$RequiredPluginApiVersion = 0) {

    $stage = Join-Path $workRoot ("stage-" + $contentId)
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $ownedPaths = New-Object Collections.Generic.List[string]
    foreach ($record in $records) { $ownedPaths.Add($record.Path) }
    $manifestPath = "$installRoot/UMAContentManifest.json"
    $ownedPaths.Add($manifestPath)

    $manifestAssets = @($records | ForEach-Object {
        [ordered]@{
            path = $_.Path
            guid = $_.Guid
            bytes = $_.Bytes
            sha256 = $_.Sha256
            metaBytes = $_.MetaBytes
            metaSha256 = $_.MetaSha256
        }
    })
    [string[]]$sortedOwnedPaths = $ownedPaths.ToArray()
    [Array]::Sort($sortedOwnedPaths, [StringComparer]::Ordinal)
    $manifest = [ordered]@{
        formatVersion = 2
        requiredPluginApiVersion = $RequiredPluginApiVersion
        contentId = $contentId
        contentVersion = $contentVersion
        umaVersion = $script:umaReleaseVersion
        requiredCoreVersion = $contentVersion
        minimumCoreVersion = $contentVersion
        maximumCoreVersionExclusive = Get-CompatibleCoreMaximum $contentVersion
        installRoot = $installRoot
        dependencies = $dependencies
        requiredPaths = $requiredPaths
        ownedPaths = $sortedOwnedPaths
        assets = $manifestAssets
    }
    $manifestJson = ($manifest | ConvertTo-Json -Depth 6) + "`n"

    foreach ($record in $records) {
        $destination = Join-Path $stage $record.Guid
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        if ($null -ne $record.Asset) {
            Copy-Item -LiteralPath $record.Asset -Destination (Join-Path $destination "asset")
        }
        Copy-Item -LiteralPath $record.Meta -Destination (Join-Path $destination "asset.meta")
        [IO.File]::WriteAllText((Join-Path $destination "pathname"),
            $record.Path, $utf8NoBom)
    }

    $manifestGuid = $manifestGuids[$contentId]
    $manifestDirectory = Join-Path $stage $manifestGuid
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $manifestDirectory "asset"),
        $manifestJson, $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path $manifestDirectory "asset.meta"),
        "fileFormatVersion: 2`nguid: $manifestGuid`nDefaultImporter:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n",
        $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path $manifestDirectory "pathname"),
        $manifestPath, $utf8NoBom)

    $fixedTimestamp = [DateTime]::SpecifyKind(
        [DateTime]::ParseExact("2000-01-01T00:00:00", "s", $null),
        [DateTimeKind]::Utc)
    Get-ChildItem -LiteralPath $stage -Recurse -Force | ForEach-Object {
        $_.LastWriteTimeUtc = $fixedTimestamp
    }
    (Get-Item -LiteralPath $stage).LastWriteTimeUtc = $fixedTimestamp

    $tarPath = $outputPath + ".tar"
    $listPath = Join-Path $workRoot ("tar-list-" + $contentId + ".txt")
    [string[]]$tarEntries = @(Get-ChildItem -LiteralPath $stage -Recurse -File |
        ForEach-Object {
            $_.FullName.Substring($stage.Length).TrimStart([char[]]"\/").Replace('\', '/')
        })
    [Array]::Sort($tarEntries, [StringComparer]::Ordinal)
    [IO.File]::WriteAllLines($listPath, $tarEntries, $utf8NoBom)
    # A sorted file list and normalized mtimes make the tar payload reproducible.
    # The unitypackage contains only record files; directory entries are unnecessary.
    & tar -cf $tarPath --format ustar --uid 0 --gid 0 --uname root --gname root `
        -C $stage -T $listPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $tarPath)) {
        throw "Could not create tar archive for $outputPath"
    }
    try {
        $input = [IO.File]::OpenRead($tarPath)
        $output = [IO.File]::Create($outputPath)
        $gzip = New-Object IO.Compression.GZipStream(
            $output, [IO.Compression.CompressionMode]::Compress, $true)
        try { $input.CopyTo($gzip) }
        finally { $gzip.Dispose(); $output.Dispose(); $input.Dispose() }
    }
    finally {
        Remove-Item -LiteralPath $tarPath -Force -ErrorAction SilentlyContinue
    }
    return $manifest
}

function Expand-UnityPackage([string]$packagePath, [string]$destination) {
    $members = @(& tar -tf $packagePath)
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect $packagePath" }
    if ($members.Count -eq 0) { throw "Archive has no members: $packagePath" }
    $memberSet = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($member in $members) {
        if ($member -notmatch '^[0-9a-fA-F]{32}/(asset|asset\.meta|pathname)$') {
            throw "Unsafe or unexpected unitypackage member: $member"
        }
        if (-not $memberSet.Add($member)) {
            throw "Duplicate unitypackage member: $member"
        }
    }
    $verboseMembers = @(& tar -tvf $packagePath)
    if ($LASTEXITCODE -ne 0 -or $verboseMembers.Count -ne $members.Count) {
        throw "Could not inspect unitypackage member types: $packagePath"
    }
    foreach ($entry in $verboseMembers) {
        if ([string]::IsNullOrEmpty($entry) -or $entry[0] -ne '-') {
            throw "Unitypackage contains a non-file archive member: $entry"
        }
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    & tar -xf $packagePath -C $destination
    if ($LASTEXITCODE -ne 0) { throw "Could not extract $packagePath" }
}

function Assert-UnityPackage(
    [string]$packagePath,
    [string]$contentId,
    [string]$installRoot,
    [string]$expectedVersion,
    [string[]]$expectedDependencies,
    [string[]]$expectedRequiredPaths,
    [string]$validationRoot) {

    Expand-UnityPackage $packagePath $validationRoot
    $pathSet = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $guidSet = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $byPath = @{}
    foreach ($directory in Get-ChildItem -LiteralPath $validationRoot -Directory) {
        $pathnameFile = Join-Path $directory.FullName "pathname"
        if (-not (Test-Path -LiteralPath $pathnameFile)) { continue }
        $rawPath = [IO.File]::ReadAllText($pathnameFile)
        Assert-SafeUnityPackagePath $rawPath $installRoot
        if (-not $pathSet.Add($rawPath)) { throw "Duplicate archive path: $rawPath" }
        if (-not $guidSet.Add($directory.Name)) { throw "Duplicate archive GUID: $($directory.Name)" }
        $metaPath = Join-Path $directory.FullName "asset.meta"
        if (-not (Test-Path -LiteralPath $metaPath)) { throw "Missing meta: $rawPath" }
        $metaGuid = Get-MetaGuid $metaPath
        if ($metaGuid -ne $directory.Name.ToLowerInvariant()) {
            throw "Archive directory/meta GUID mismatch: $rawPath"
        }
        $byPath[$rawPath] = [pscustomobject]@{
            Guid = $directory.Name.ToLowerInvariant()
            Asset = Join-Path $directory.FullName "asset"
            Meta = $metaPath
        }
    }

    $manifestPath = "$installRoot/UMAContentManifest.json"
    if (-not $byPath.ContainsKey($manifestPath)) { throw "Missing $manifestPath" }
    $manifest = [IO.File]::ReadAllText($byPath[$manifestPath].Asset) | ConvertFrom-Json
    if ($manifest.formatVersion -ne 2 -or $manifest.contentId -ne $contentId -or
        $manifest.installRoot -ne $installRoot) {
        throw "$contentId package has the wrong manifest identity"
    }
    $expectedMaximum = Get-CompatibleCoreMaximum $expectedVersion
    if ([string]$manifest.contentVersion -ne $expectedVersion -or
        [string]$manifest.requiredCoreVersion -ne $expectedVersion -or
        [string]$manifest.minimumCoreVersion -ne $expectedVersion -or
        [string]$manifest.maximumCoreVersionExclusive -ne $expectedMaximum) {
        throw "$contentId package version or Core compatibility range does not match $expectedVersion"
    }
    $actualDependencies = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($dependency in $manifest.dependencies) {
        if (-not $actualDependencies.Add([string]$dependency)) {
            throw "$contentId package has duplicate dependency $dependency"
        }
    }
    $requiredDependencies = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($dependency in $expectedDependencies) { [void]$requiredDependencies.Add($dependency) }
    if (-not $actualDependencies.SetEquals($requiredDependencies)) {
        throw "$contentId package dependencies do not match: $($expectedDependencies -join ', ')"
    }
    $owned = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $manifest.ownedPaths) {
        Assert-SafeUnityPackagePath ([string]$path) $installRoot
        if (-not $owned.Add([string]$path)) {
            throw "$contentId manifest contains duplicate owned path: $path"
        }
    }
    if (-not $owned.SetEquals($pathSet)) { throw "$contentId manifest does not match archive paths" }
    $manifestAssetPaths = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $actualRequiredPaths = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($required in $manifest.requiredPaths) {
        Assert-SafeUnityPackagePath ([string]$required) $installRoot
        if (-not $actualRequiredPaths.Add([string]$required)) {
            throw "$contentId manifest contains duplicate required path: $required"
        }
        if (-not $pathSet.Contains([string]$required)) { throw "Missing required path: $required" }
    }
    $requiredRequiredPaths = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($required in $expectedRequiredPaths) {
        [void]$requiredRequiredPaths.Add([string]$required)
    }
    if ($actualRequiredPaths.Count -eq 0 -or
        -not $actualRequiredPaths.SetEquals($requiredRequiredPaths)) {
        throw "$contentId required paths do not match the release contract"
    }
    foreach ($asset in $manifest.assets) {
        $path = [string]$asset.path
        if (-not $manifestAssetPaths.Add($path)) { throw "Duplicate manifest asset: $path" }
        if (-not $byPath.ContainsKey($path)) { throw "Manifest asset missing: $path" }
        $record = $byPath[$path]
        if ($record.Guid -ne [string]$asset.guid) { throw "Manifest GUID mismatch: $path" }
        $assetFile = $record.Asset
        if ([long]$asset.bytes -eq 0) {
            if (Test-Path -LiteralPath $assetFile) { throw "Folder record has asset bytes: $path" }
        }
        else {
            if (-not (Test-Path -LiteralPath $assetFile)) { throw "Missing archive asset: $path" }
            if ((Get-Item -LiteralPath $assetFile).Length -ne [long]$asset.bytes -or
                (Get-FileHash -LiteralPath $assetFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne
                    [string]$asset.sha256) {
                throw "Archive asset hash mismatch: $path"
            }
        }
        if ((Get-Item -LiteralPath $record.Meta).Length -ne [long]$asset.metaBytes -or
            (Get-FileHash -LiteralPath $record.Meta -Algorithm SHA256).Hash.ToLowerInvariant() -ne
                [string]$asset.metaSha256) {
            throw "Archive meta hash mismatch: $path"
        }
    }
    if ($manifestAssetPaths.Count + 1 -ne $pathSet.Count) {
        throw "$contentId manifest asset records do not exactly describe the archive"
    }
    foreach ($asset in $manifest.assets) {
        if ([long]$asset.bytes -ne 0) { continue }
        $prefix = ([string]$asset.path).TrimEnd('/') + "/"
        $hasDescendant = $false
        foreach ($candidate in $manifestAssetPaths) {
            if ($candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                $hasDescendant = $true
                break
            }
        }
        if (-not $hasDescendant) {
            throw "$contentId manifest contains an unimportable empty folder: $($asset.path)"
        }
    }
    return [pscustomobject]@{
        Count = $pathSet.Count
        Manifest = $manifest
        Guids = $guidSet
    }
}

function Assert-CrossPackageGuidOwnership($uma3Validation, $uma2Validation) {
    $duplicates = New-Object Collections.Generic.List[string]
    foreach ($guid in $uma3Validation.Guids) {
        if ($uma2Validation.Guids.Contains($guid)) { $duplicates.Add($guid) }
    }
    if ($duplicates.Count -gt 0) {
        throw "UMA3 and UMA2 archives contain duplicate GUID ownership:`n$($duplicates -join "`n")"
    }
}

function Assert-CoreGuidOwnership([string]$coreRoot, $uma3Validation,
    $uma2Validation) {
    $contentGuids = New-Object 'Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($guid in $uma3Validation.Guids) { [void]$contentGuids.Add($guid) }
    foreach ($guid in $uma2Validation.Guids) { [void]$contentGuids.Add($guid) }
    $duplicates = New-Object Collections.Generic.List[string]
    foreach ($file in Get-CoreSourceFiles $coreRoot) {
        if (-not $file.Name.EndsWith(".meta",
                [StringComparison]::OrdinalIgnoreCase)) { continue }
        $guid = Get-MetaGuid $file.FullName
        if ($contentGuids.Contains($guid)) {
            $duplicates.Add("$guid -> $(To-ProjectPath $file.FullName)")
        }
    }
    if ($duplicates.Count -gt 0) {
        throw "Core and editable content contain duplicate GUID ownership:`n" +
            ($duplicates -join "`n")
    }
}

function Assert-NoCoreContentByteReferences([string]$coreRoot,
    $uma3Records, $uma2Records) {
    [string[]]$contentGuids = @(
        @($uma3Records | ForEach-Object { [string]$_.Guid }) +
        @($uma2Records | ForEach-Object { [string]$_.Guid }) |
        Sort-Object -Unique)
    [string[]]$coreFiles = @(Get-CoreSourceFiles $coreRoot |
        Where-Object {
            -not $_.Name.EndsWith(".unitypackage",
                [StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object { $_.FullName })
    [string[]]$matches = [UMAContentByteReferenceScanner]::Find(
        $coreFiles, $contentGuids)
    if ($matches.Count -gt 0) {
        $shown = @($matches | Select-Object -First 200)
        $suffix = if ($matches.Count -gt $shown.Count) {
            "`n... and $($matches.Count - $shown.Count) more."
        } else { "" }
        throw "Core contains raw serialized references to editable content GUIDs:`n" +
            ($shown -join "`n") + $suffix
    }
}

function Assert-NoCorePipelineSpecificByteReferences([string]$coreRoot) {
    [string[]]$pipelineGuids = @(
        $pipelineSpecificContentGuids.Keys | Sort-Object -Unique)
    [string[]]$coreFiles = @(Get-CoreSourceFiles $coreRoot |
        Where-Object {
            $relative = $_.FullName.Substring($coreRoot.Length).
                TrimStart([char[]]"\/").Replace('\', '/')
            -not $_.Name.EndsWith(".unitypackage",
                [StringComparison]::OrdinalIgnoreCase) -and
            -not $relative.Equals(
                "Editor/PackageSupport/UMASrpPackageArchiveValidator.cs",
                [StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object { $_.FullName })
    [string[]]$matches = [UMAContentByteReferenceScanner]::Find(
        $coreFiles, $pipelineGuids)
    if ($matches.Count -gt 0) {
        $shown = @($matches | Select-Object -First 200)
        $suffix = if ($matches.Count -gt $shown.Count) {
            "`n... and $($matches.Count - $shown.Count) more."
        } else { "" }
        throw "Core contains pipeline-specific serialized references. " +
            "Pipeline-owned assets must be installed by the URP or HDRP package:`n" +
            ($shown -join "`n") + $suffix
    }
}

function Assert-NoUma3Uma2ByteReferences($uma3Records, $uma2Records) {
    [string[]]$uma2Guids = @($uma2Records |
        ForEach-Object { [string]$_.Guid } | Sort-Object -Unique)
    $nativeExtensions = @(
        ".anim", ".asset", ".controller", ".lighting", ".mat", ".prefab", ".unity")
    [string[]]$uma3SerializedFiles = @($uma3Records | ForEach-Object {
        [string]$meta = $_.Meta
        if (-not [string]::IsNullOrEmpty($meta)) { $meta }
        [string]$asset = $_.Asset
        if (-not [string]::IsNullOrEmpty($asset) -and
            $nativeExtensions -contains [IO.Path]::GetExtension($asset).ToLowerInvariant()) {
            $asset
        }
    } | Sort-Object -Unique)
    [string[]]$matches = [UMAContentByteReferenceScanner]::Find(
        $uma3SerializedFiles, $uma2Guids)
    if ($matches.Count -gt 0) {
        $shown = @($matches | Select-Object -First 200)
        $suffix = if ($matches.Count -gt $shown.Count) {
            "`n... and $($matches.Count - $shown.Count) more."
        } else { "" }
        throw "UMA3 contains raw serialized references to optional UMA2 GUIDs:`n" +
            ($shown -join "`n") + $suffix
    }
}

function Assert-ArchiveMatchesSource([string]$contentId, $records, $validation) {
    $manifestByPath = @{}
    foreach ($asset in $validation.Manifest.assets) {
        $manifestByPath[[string]$asset.path] = $asset
    }
    if ($manifestByPath.Count -ne $records.Count) {
        throw "$contentId archive/source asset count mismatch: archive " +
            "$($manifestByPath.Count), source $($records.Count)"
    }
    foreach ($record in $records) {
        if (-not $manifestByPath.ContainsKey($record.Path)) {
            throw "$contentId archive is stale; source path is missing: $($record.Path)"
        }
        $asset = $manifestByPath[$record.Path]
        if ([string]$asset.guid -ne $record.Guid -or
            [long]$asset.bytes -ne $record.Bytes -or
            [string]$asset.sha256 -ne $record.Sha256 -or
            [long]$asset.metaBytes -ne $record.MetaBytes -or
            [string]$asset.metaSha256 -ne $record.MetaSha256) {
            throw "$contentId archive is stale for source path: $($record.Path)"
        }
    }
}

function Assert-TextDependencyBoundaries([string]$coreRoot, [string]$uma3Root, [string]$uma2Root) {
    $owners = @{}
    foreach ($spec in @(
        [pscustomobject]@{ Name = "UMA3"; Root = $uma3Root },
        [pscustomobject]@{ Name = "UMA2"; Root = $uma2Root })) {
        Get-ChildItem -LiteralPath $spec.Root -Recurse -File -Filter "*.meta" | ForEach-Object {
            $guid = Get-MetaGuid $_.FullName
            if ($owners.ContainsKey($guid)) {
                throw "Duplicate content GUID $guid is owned by both $($owners[$guid]) and $($spec.Name): $($_.FullName)"
            }
            $owners[$guid] = $spec.Name
        }
    }
    $native = @(".anim", ".asset", ".controller", ".lighting", ".mat", ".prefab", ".unity")
    $plain = @(".asmdef", ".asmref", ".compute", ".cginc", ".cs", ".hlsl", ".json", ".shader", ".shadergraph", ".txt", ".uss", ".uxml")
    $failures = New-Object Collections.Generic.List[string]
    Get-ChildItem -LiteralPath $coreRoot -Recurse -File | ForEach-Object {
        $coreRelativePath = $_.FullName.Substring($coreRoot.Length + 1)
        if (-not (Test-CoreFileIncluded $coreRelativePath)) { return }
        $normalized = $_.FullName.Replace('\', '/')
        if ($normalized.StartsWith($uma3Root.Replace('\', '/') + "/",
                [StringComparison]::OrdinalIgnoreCase) -or
            $normalized.StartsWith($uma2Root.Replace('\', '/') + "/",
                [StringComparison]::OrdinalIgnoreCase)) { return }
        $extension = $_.Extension.ToLowerInvariant()
        $canRead = $plain -contains $extension
        if (-not $canRead -and $native -contains $extension) {
            $canRead = Test-UnityYamlFile $_.FullName
        }
        if (-not $canRead) { return }
        $text = [IO.File]::ReadAllText($_.FullName)
        foreach ($match in [regex]::Matches($text, "guid:\s*([0-9a-fA-F]{32})")) {
            $guid = $match.Groups[1].Value.ToLowerInvariant()
            if (-not $owners.ContainsKey($guid)) { continue }
            $owner = $owners[$guid]
            $projectPath = To-ProjectPath $_.FullName
            $allowedUma3Scene = $owner -eq "UMA3" -and
                $projectPath.StartsWith("Assets/UMA/SRP/Samples/Scenes/",
                    [StringComparison]::OrdinalIgnoreCase)
            if (-not $allowedUma3Scene) {
                $failures.Add("$projectPath -> $owner GUID $guid")
            }
        }
    }
    if ($failures.Count -gt 0) {
        throw "Core has reverse content dependencies:`n$($failures -join "`n")"
    }

    Get-ChildItem -LiteralPath $uma3Root -Recurse -File | ForEach-Object {
        $extension = $_.Extension.ToLowerInvariant()
        $canRead = $plain -contains $extension
        if (-not $canRead -and $native -contains $extension) {
            $canRead = Test-UnityYamlFile $_.FullName
        }
        if (-not $canRead) { return }
        $text = [IO.File]::ReadAllText($_.FullName)
        foreach ($match in [regex]::Matches($text, "guid:\s*([0-9a-fA-F]{32})")) {
            $guid = $match.Groups[1].Value.ToLowerInvariant()
            if ($owners.ContainsKey($guid) -and $owners[$guid] -eq "UMA2") {
                $failures.Add("$(To-ProjectPath $_.FullName) -> UMA2 GUID $guid")
            }
        }
    }
    if ($failures.Count -gt 0) {
        throw "UMA3 has reverse UMA2 dependencies:`n$($failures -join "`n")"
    }
}

function Assert-NoPipelineSpecificContentReferences([string]$contentRoot, [string]$label) {
    $native = @(".anim", ".asset", ".controller", ".lighting", ".mat", ".prefab", ".unity")
    $plain = @(".asmdef", ".asmref", ".compute", ".cginc", ".cs", ".hlsl", ".json", ".shader", ".shadergraph", ".txt", ".uss", ".uxml")
    $failures = New-Object Collections.Generic.List[string]
    Get-ChildItem -LiteralPath $contentRoot -Recurse -File | ForEach-Object {
        $extension = $_.Extension.ToLowerInvariant()
        $canRead = $plain -contains $extension
        if (-not $canRead -and $native -contains $extension) {
            $canRead = Test-UnityYamlFile $_.FullName
        }
        if (-not $canRead) { return }

        $text = [IO.File]::ReadAllText($_.FullName)
        foreach ($guid in $pipelineSpecificContentGuids.Keys) {
            if ($text.IndexOf($guid, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $failures.Add("$(To-ProjectPath $_.FullName) -> $guid ($($pipelineSpecificContentGuids[$guid]))")
            }
        }
    }
    if ($failures.Count -gt 0) {
        throw "$label content contains pipeline-specific serialized references. " +
            "Use assets whose GUIDs are shared by both SRP archives and remove " +
            "pipeline-only serialized components before packaging:`n" +
            ($failures -join "`n")
    }
}

function Publish-ArtifactSetSafely($artifacts) {
    $transactionId = [Guid]::NewGuid().ToString("N")
    $states = New-Object Collections.Generic.List[object]
    $success = $false
    try {
        foreach ($artifact in $artifacts) {
            $source = [IO.Path]::GetFullPath([string]$artifact.Source)
            $destination = [IO.Path]::GetFullPath([string]$artifact.Destination)
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Artifact candidate is missing: $source"
            }
            if (Test-Path -LiteralPath $destination -PathType Container) {
                throw "Artifact destination is a directory: $destination"
            }
            if (Test-Path -LiteralPath $destination -PathType Leaf) {
                $existingArtifact = Get-Item -LiteralPath $destination -Force
                if (($existingArtifact.Attributes -band
                        [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Artifact destination cannot be a symbolic link: $destination"
                }
            }
            $parent = Split-Path $destination -Parent
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
            $stage = $destination + ".stage-" + $transactionId
            $backup = $destination + ".backup-" + $transactionId
            $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
            $state = [pscustomobject]@{
                Destination = $destination
                Stage = $stage
                Backup = $backup
                HadPrevious = [bool](Test-Path -LiteralPath $destination -PathType Leaf)
                PreviousMoved = $false
                Published = $false
                SourceHash = $sourceHash
            }
            $states.Add($state)
            Copy-Item -LiteralPath $source -Destination $stage
            if ((Get-Item -LiteralPath $source).Length -ne
                    (Get-Item -LiteralPath $stage).Length -or
                (Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash -ne
                    $sourceHash) {
                throw "Artifact staging verification failed: $destination"
            }
        }

        foreach ($state in $states) {
            if ($state.HadPrevious) {
                Move-Item -LiteralPath $state.Destination -Destination $state.Backup
                $state.PreviousMoved = $true
            }
        }
        foreach ($state in $states) {
            Move-Item -LiteralPath $state.Stage -Destination $state.Destination
            $state.Published = $true
        }
        foreach ($state in $states) {
            if ((Get-FileHash -LiteralPath $state.Destination -Algorithm SHA256).Hash -ne
                    $state.SourceHash) {
                throw "Published artifact verification failed: $($state.Destination)"
            }
        }
        $success = $true
    }
    catch {
        $originalFailure = $_
        $recoveryErrors = New-Object Collections.Generic.List[string]
        foreach ($state in $states) {
            if ($state.Published -and
                (Test-Path -LiteralPath $state.Destination -PathType Leaf)) {
                try {
                    $failed = $state.Destination + ".failed-" + $transactionId
                    Move-Item -LiteralPath $state.Destination -Destination $failed
                }
                catch { $recoveryErrors.Add($_.Exception.Message) }
            }
        }
        foreach ($state in $states) {
            if (Test-Path -LiteralPath $state.Backup -PathType Leaf) {
                try {
                    Move-Item -LiteralPath $state.Backup -Destination $state.Destination
                }
                catch { $recoveryErrors.Add($_.Exception.Message) }
            }
        }
        if ($recoveryErrors.Count -gt 0) {
            throw "Artifact publication failed: $($originalFailure.Exception.Message) " +
                "Recovery also reported: $($recoveryErrors -join '; ')"
        }
        throw $originalFailure
    }
    finally {
        foreach ($state in $states) {
            if (Test-Path -LiteralPath $state.Stage -PathType Leaf) {
                Remove-Item -LiteralPath $state.Stage -Force
            }
            if ($success -and (Test-Path -LiteralPath $state.Backup -PathType Leaf)) {
                Remove-Item -LiteralPath $state.Backup -Force
            }
        }
    }
}

function Test-SamePath([string]$left, [string]$right) {
    return [string]::Equals(
        [IO.Path]::GetFullPath($left).TrimEnd('\', '/'),
        [IO.Path]::GetFullPath($right).TrimEnd('\', '/'),
        [StringComparison]::OrdinalIgnoreCase)
}

function Test-PathWithin([string]$candidate, [string]$parent) {
    $resolvedCandidate = [IO.Path]::GetFullPath($candidate).TrimEnd('\', '/')
    $resolvedParent = [IO.Path]::GetFullPath($parent).TrimEnd('\', '/')
    return $resolvedCandidate.StartsWith(
        $resolvedParent + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}

function Test-CoreFileIncluded([string]$relativePath) {
    $normalized = $relativePath.Replace('\', '/')
    $leaf = [IO.Path]::GetFileName($normalized)
    if ($normalized.EndsWith("~", [StringComparison]::Ordinal) -or
        $normalized.EndsWith(".user", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.EndsWith(".suo", [StringComparison]::OrdinalIgnoreCase) -or
        $leaf.Equals(".DS_Store", [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    if ($normalized.Equals("UMADismemberment.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("UMADismemberment/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals("HairCards.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("HairCards/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals("OverlayPainter.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("OverlayPainter/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals("OverlayPainterExamples.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("OverlayPainterExamples/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals("OverlayPainterTests.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("OverlayPainterTests/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals("UMA3.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("UMA3/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals("UMA2.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("UMA2/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals("Settings.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("Settings/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals("Temp.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("Temp/", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals("Tasks.meta", [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith("Tasks/", [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    if ($normalized.StartsWith("SRP/", [StringComparison]::OrdinalIgnoreCase)) {
        return @(
            "SRP/UMAURP.unitypackage",
            "SRP/UMAURP.unitypackage.meta",
            "SRP/UMAHDRP.unitypackage",
            "SRP/UMAHDRP.unitypackage.meta") -contains $normalized
    }
    return $true
}

function Get-CoreSourceFiles([string]$source) {
    return @(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object {
        $relative = $_.FullName.Substring($source.Length).TrimStart([char[]]"\/")
        Test-CoreFileIncluded $relative
    })
}

function Assert-SafeCoreStagingDestination([string]$source, [string]$destination) {
    $projectRoot = $repoRoot
    $driveRoot = [IO.Path]::GetPathRoot($destination)
    if (Test-SamePath $destination $driveRoot) {
        throw "Core staging cannot target a drive root: $destination"
    }
    if ((Test-SamePath $destination $source) -or
        (Test-PathWithin $destination $source) -or
        (Test-PathWithin $source $destination) -or
        (Test-SamePath $destination $projectRoot) -or
        (Test-PathWithin $projectRoot $destination)) {
        throw "Unsafe Core staging destination: $destination"
    }
    foreach ($protected in @("Assets", "Packages", "ProjectSettings", "Library")) {
        $protectedPath = Join-Path $projectRoot $protected
        if ((Test-SamePath $destination $protectedPath) -or
            (Test-PathWithin $destination $protectedPath) -or
            (Test-PathWithin $protectedPath $destination)) {
            throw "Core staging cannot replace protected project path $protectedPath"
        }
    }
    if (Test-Path -LiteralPath $destination) {
        $existingDestination = Get-Item -LiteralPath $destination -Force
        if (($existingDestination.Attributes -band
                [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Core staging destination cannot be a junction or symbolic link: $destination"
        }
        $existingPackageJson = Join-Path $destination "package.json"
        if (-not (Test-Path -LiteralPath $existingPackageJson -PathType Leaf)) {
            throw "Existing staging destination is not an UMA Core package: $destination"
        }
        $existingPackage = [IO.File]::ReadAllText($existingPackageJson) | ConvertFrom-Json
        if ([string]$existingPackage.name -ne "com.umasteeringgroup.uma") {
            throw "Existing staging destination belongs to a different package: $destination"
        }
    }
}

function Assert-CoreStaging([string]$source, [string]$destination, $sourceFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $destination "package.json") -PathType Leaf)) {
        throw "Core staging has no package.json: $destination"
    }
    $package = [IO.File]::ReadAllText((Join-Path $destination "package.json")) |
        ConvertFrom-Json
    if ([string]$package.name -ne "com.umasteeringgroup.uma") {
        throw "Core staging has the wrong package identity"
    }
    foreach ($forbidden in @("UMADismemberment", "UMADismemberment.meta", "HairCards", "HairCards.meta", "OverlayPainter", "OverlayPainter.meta", "OverlayPainterExamples", "OverlayPainterExamples.meta", "OverlayPainterTests", "OverlayPainterTests.meta", "UMA3", "UMA3.meta", "UMA2", "UMA2.meta",
            "Settings", "Settings.meta",
            "Temp", "Temp.meta", "Tasks", "Tasks.meta",
            "SRP/UMAURPManifest.json", "SRP/UMAHDRPManifest.json")) {
        if (Test-Path -LiteralPath (Join-Path $destination $forbidden)) {
            throw "Core staging contains excluded content: $forbidden"
        }
    }

    $expected = @{}
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($source.Length).TrimStart([char[]]"\/").Replace('\', '/')
        $expected[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    $actual = @(Get-ChildItem -LiteralPath $destination -Recurse -File)
    if ($actual.Count -ne $expected.Count) {
        throw "Core staging file count mismatch: expected $($expected.Count), found $($actual.Count)"
    }
    foreach ($file in $actual) {
        $relative = $file.FullName.Substring($destination.Length).TrimStart([char[]]"\/").Replace('\', '/')
        if (-not $expected.ContainsKey($relative)) {
            throw "Core staging contains unexpected file: $relative"
        }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($hash -ne $expected[$relative]) {
            throw "Core staging hash mismatch: $relative"
        }
    }
    return $actual.Count
}

function Stage-Core([string]$sourceRoot, [string]$destinationRoot) {
    $source = Resolve-ProjectPath $sourceRoot
    $destination = Resolve-ProjectPath $destinationRoot
    if (((Get-Item -LiteralPath $source -Force).Attributes -band
            [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Core source cannot be a junction or symbolic link: $source"
    }
    $sourceReparsePoints = @(Get-ChildItem -LiteralPath $source -Recurse -Force |
        Where-Object {
            ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
        })
    if ($sourceReparsePoints.Count -gt 0) {
        throw "Core source contains a junction or symbolic link: " +
            $sourceReparsePoints[0].FullName
    }
    Assert-SafeCoreStagingDestination $source $destination
    $parent = Split-Path $destination -Parent
    $leaf = Split-Path $destination -Leaf
    if ([string]::IsNullOrWhiteSpace($parent) -or [string]::IsNullOrWhiteSpace($leaf)) {
        throw "Core staging destination must have a parent and folder name: $destination"
    }
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $stage = Join-Path $parent (".uma-core-stage-" + $leaf + "-" +
        [Guid]::NewGuid().ToString("N"))
    $backup = $null
    $sourceFiles = Get-CoreSourceFiles $source
    try {
        New-Item -ItemType Directory -Path $stage | Out-Null
        foreach ($file in $sourceFiles) {
            $relative = $file.FullName.Substring($source.Length).TrimStart([char[]]"\/")
            $target = Join-Path $stage $relative
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
        $fileCount = Assert-CoreStaging $source $stage $sourceFiles

        if (Test-Path -LiteralPath $destination) {
            $timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
            $backup = $destination + ".backup-" + $timestamp
            if (Test-Path -LiteralPath $backup) {
                $backup += "-" + [Guid]::NewGuid().ToString("N")
            }
            Move-Item -LiteralPath $destination -Destination $backup
        }
        try {
            Move-Item -LiteralPath $stage -Destination $destination
            [void](Assert-CoreStaging $source $destination $sourceFiles)
        }
        catch {
            if (Test-Path -LiteralPath $destination) {
                $failed = $destination + ".failed-" + [Guid]::NewGuid().ToString("N")
                Move-Item -LiteralPath $destination -Destination $failed
            }
            if ($null -ne $backup -and (Test-Path -LiteralPath $backup)) {
                Move-Item -LiteralPath $backup -Destination $destination
            }
            throw
        }
        return [pscustomobject]@{
            Destination = $destination
            Backup = $backup
            FileCount = $fileCount
        }
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
}

if ($FunctionsOnly) { return }

$uma3Source = Resolve-ProjectPath $Uma3SourceDirectory
$uma2Source = Resolve-ProjectPath $Uma2SourceDirectory
$coreSource = Resolve-ProjectPath $CoreSourceDirectory
$outputRoot = Resolve-ProjectPath $OutputDirectory
Assert-SafeArtifactDirectory $outputRoot
$releaseVersion = Sync-UMAPackageVersion $coreSource
$script:umaReleaseVersion = $releaseVersion.UmaVersion
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $releaseVersion.Version }
elseif ($Version -ne $releaseVersion.Version) { throw "Requested version $Version does not match installed UMASettings $($releaseVersion.UmaVersion)." }
if ($Version -notmatch "^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$") {
    throw "Invalid content version: $Version"
}

Assert-TextDependencyBoundaries $coreSource $uma3Source $uma2Source
Assert-NoPipelineSpecificContentReferences $uma3Source "UMA3"
Assert-NoPipelineSpecificContentReferences $uma2Source "UMA2"
$oldPathFiles = @(Get-ChildItem -LiteralPath $uma2Source -Recurse -File | Where-Object {
    $extension = $_.Extension.ToLowerInvariant()
    $read = @(".asmdef", ".cs", ".json", ".md", ".txt") -contains $extension
    if (-not $read -and @(".asset", ".prefab", ".unity", ".mat") -contains $extension) {
        $read = Test-UnityYamlFile $_.FullName
    }
    $read -and [IO.File]::ReadAllText($_.FullName).Contains("Assets/UMA2")
})
if ($oldPathFiles.Count -gt 0) {
    throw "UMA2 content still contains old Assets/UMA2 paths:`n$($oldPathFiles.FullName -join "`n")"
}

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("uma-content-build-" + [Guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
    $uma3Output = Join-Path $outputRoot "UMA3Content-$Version.unitypackage"
    $uma2Output = Join-Path $outputRoot "UMA2Content-$Version.unitypackage"
    $uma3ValidationPath = $uma3Output
    $uma2ValidationPath = $uma2Output
    $uma3Records = Get-SourceRecords $uma3Source "Assets/UMA/UMA3"
    $uma2Records = Get-SourceRecords $uma2Source "Assets/UMA/UMA2"
    Assert-NoCoreContentByteReferences $coreSource $uma3Records $uma2Records
    Assert-NoCorePipelineSpecificByteReferences $coreSource
    Assert-NoUma3Uma2ByteReferences $uma3Records $uma2Records
    if (-not $UseExistingPackages) {
        $uma3ValidationPath = Join-Path $workRoot "UMA3Content-$Version.unitypackage"
        $uma2ValidationPath = Join-Path $workRoot "UMA2Content-$Version.unitypackage"
        [void](Write-UnityPackage "uma3" $Version "Assets/UMA/UMA3" @("core", "srp") @(
            "Assets/UMA/UMA3/UMA3.Samples.asmdef",
            "Assets/UMA/UMA3/Races",
            "Assets/UMA/UMA3/Textures",
            "Assets/UMA/UMA3/Wearables") $uma3Records $uma3ValidationPath $workRoot)
        [void](Write-UnityPackage "uma2" $Version "Assets/UMA/UMA2" @("core", "srp", "uma3") @(
            "Assets/UMA/UMA2/UMA2.Content.asmdef",
            "Assets/UMA/UMA2/Races",
            "Assets/UMA/UMA2/Wearables") $uma2Records $uma2ValidationPath $workRoot)
    }
    elseif (-not (Test-Path -LiteralPath $uma3Output) -or
            -not (Test-Path -LiteralPath $uma2Output)) {
        throw "-UseExistingPackages requires both versioned archives in $outputRoot"
    }

    $uma3Validation = Assert-UnityPackage $uma3ValidationPath "uma3" `
        "Assets/UMA/UMA3" $Version @("core", "srp") @(
            "Assets/UMA/UMA3/UMA3.Samples.asmdef",
            "Assets/UMA/UMA3/Races",
            "Assets/UMA/UMA3/Textures",
            "Assets/UMA/UMA3/Wearables") `
        (Join-Path $workRoot "validate-uma3")
    $uma2Validation = Assert-UnityPackage $uma2ValidationPath "uma2" `
        "Assets/UMA/UMA2" $Version @("core", "srp", "uma3") @(
            "Assets/UMA/UMA2/UMA2.Content.asmdef",
            "Assets/UMA/UMA2/Races",
            "Assets/UMA/UMA2/Wearables") `
        (Join-Path $workRoot "validate-uma2")
    Assert-CrossPackageGuidOwnership $uma3Validation $uma2Validation
    Assert-CoreGuidOwnership $coreSource $uma3Validation $uma2Validation
    Assert-ArchiveMatchesSource "UMA3" $uma3Records $uma3Validation
    Assert-ArchiveMatchesSource "UMA2" $uma2Records $uma2Validation
    $uma3Count = $uma3Validation.Count
    $uma2Count = $uma2Validation.Count

    $coreStage = $null
    if (-not $SkipCoreStaging) {
        $coreStage = Stage-Core $coreSource $CoreStagingDirectory
    }
    $releaseManifest = [ordered]@{
        formatVersion = 2
        umaVersion = $Version
        minimumCoreVersion = $Version
        maximumCoreVersionExclusive = Get-CompatibleCoreMaximum $Version
        generatedUtc = [DateTime]::UtcNow.ToString("O")
        coreStaging = if ($null -eq $coreStage) { $null } else { [ordered]@{
            path = $coreStage.Destination
            previousBackup = $coreStage.Backup
            files = $coreStage.FileCount
        }}
        packages = @(
            [ordered]@{
                contentId = "uma3"
                file = [IO.Path]::GetFileName($uma3Output)
                bytes = (Get-Item -LiteralPath $uma3ValidationPath).Length
                sha256 = (Get-FileHash -LiteralPath $uma3ValidationPath -Algorithm SHA256).Hash.ToLowerInvariant()
                entries = $uma3Count
            },
            [ordered]@{
                contentId = "uma2"
                file = [IO.Path]::GetFileName($uma2Output)
                bytes = (Get-Item -LiteralPath $uma2ValidationPath).Length
                sha256 = (Get-FileHash -LiteralPath $uma2ValidationPath -Algorithm SHA256).Hash.ToLowerInvariant()
                entries = $uma2Count
            }
        )
    }
    $releaseManifestCandidate = Join-Path $workRoot "ReleaseManifest.json"
    [IO.File]::WriteAllText($releaseManifestCandidate,
        ($releaseManifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
        $utf8NoBom)
    $publishSet = New-Object Collections.Generic.List[object]
    if (-not $UseExistingPackages) {
        $publishSet.Add([pscustomobject]@{
            Source = $uma3ValidationPath; Destination = $uma3Output })
        $publishSet.Add([pscustomobject]@{
            Source = $uma2ValidationPath; Destination = $uma2Output })
    }
    $publishSet.Add([pscustomobject]@{
        Source = $releaseManifestCandidate
        Destination = Join-Path $outputRoot "ReleaseManifest.json"
    })
    Publish-ArtifactSetSafely $publishSet

    if (-not $UseExistingPackages) {
        Get-ChildItem -LiteralPath $outputRoot -Filter "UMA3Content-*.unitypackage" -File |
            Where-Object { -not (Test-SamePath $_.FullName $uma3Output) } |
            Remove-Item -Force
        Get-ChildItem -LiteralPath $outputRoot -Filter "UMA2Content-*.unitypackage" -File |
            Where-Object { -not (Test-SamePath $_.FullName $uma2Output) } |
            Remove-Item -Force
    }
    Write-Output "Built and validated UMA3 Content $Version ($uma3Count paths)."
    Write-Output "Built and validated UMA2 Content $Version ($uma2Count paths)."
    if (-not $SkipCoreStaging) {
        Write-Output "Staged Core without raw SRP/UMA2/UMA3 content at $($coreStage.Destination)."
        if (-not [string]::IsNullOrEmpty([string]$coreStage.Backup)) {
            Write-Output "Retained the previous Core staging tree at $($coreStage.Backup)."
        }
    }
}
finally {
    $resolvedWorkRoot = [IO.Path]::GetFullPath($workRoot)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedWorkRoot.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path $resolvedWorkRoot -Leaf).StartsWith("uma-content-build-")) {
        Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
