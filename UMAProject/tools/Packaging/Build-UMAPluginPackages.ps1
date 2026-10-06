param(
    [string]$Destination = "Build/Plugins",
    [switch]$SkipCore
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Build-UMAContentPackages.ps1") -FunctionsOnly
$output = Resolve-ProjectPath $Destination
Assert-SafeArtifactDirectory $output
New-Item -ItemType Directory -Path $output -Force | Out-Null
$package = Get-Content -LiteralPath (Join-Path $repoRoot "Assets/UMA/package.json") -Raw | ConvertFrom-Json
$version = [string]$package.version
$manifestGuids['overlay-painter'] = 'baf87f8fd4ef4ae49c08b67233d50f71'
$manifestGuids['overlay-painter-examples'] = 'f3d05e75b8aa4d3bb6d02fbb6e6f34b8'
$manifestGuids['overlay-painter-tests'] = '5f78f34f3fa748d0b47ad8ba7609428f'
$manifestGuids['hair-cards'] = '6adee602b17a4f099ff31323a6c8dc4c'
$definitions = @(
    @{ Id='hair-cards'; Name='HairCards'; Root='Assets/UMA/HairCards'; Dependencies=@('core'); Required=@('Core/UMA.HairCards.Core.asmdef','Runtime/UMA.HairCards.Runtime.asmdef','Editor/UMA.HairCards.Editor.asmdef','Editor/HairCardsPluginActions.cs','UMAPluginDocumentation.json') },
    @{ Id='overlay-painter'; Name='OverlayPainter'; Root='Assets/UMA/OverlayPainter'; Dependencies=@('core'); Required=@('Runtime/UMA.TexturePaint.Runtime.asmdef','Editor/UMA.TexturePaint.Editor.asmdef','Editor/TexturePaintPluginActions.cs','Shaders/StrokeRasterize.compute') },
    @{ Id='overlay-painter-examples'; Name='OverlayPainterExamples'; Root='Assets/UMA/OverlayPainterExamples'; Dependencies=@('core','overlay-painter','uma3','srp'); Required=@('Samples/Overlay Painter Document.asset') },
    @{ Id='overlay-painter-tests'; Name='OverlayPainterTests'; Root='Assets/UMA/OverlayPainterTests'; Dependencies=@('core','overlay-painter','test-framework'); Required=@('UMA.TexturePaint.Editor.Tests.asmdef') }
)
$work = Join-Path $output ('.build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$ownedGuids = @{}
$reports = @()
try {
    foreach ($definition in $definitions) {
        $source = Resolve-ProjectPath $definition.Root
        $records = Get-SourceRecords $source $definition.Root
        foreach ($record in $records) {
            if ($ownedGuids.ContainsKey($record.Guid)) { throw "Shared plugin GUID: $($record.Guid)" }
            $ownedGuids[$record.Guid] = $record.Path
        }
        $required = @($definition.Required | ForEach-Object { $definition.Root + '/' + $_ })
        $archive = Join-Path $output ($definition.Name + '-' + $version + '.unitypackage')
        [void](Write-UnityPackage $definition.Id $version $definition.Root $definition.Dependencies $required $records $archive $work 1)
        [void](Assert-UnityPackage $archive $definition.Id $definition.Root $version $definition.Dependencies $required (Join-Path $work ('validate-' + $definition.Id)))
        $reports += [ordered]@{ package=$archive; entries=$records.Count+1; sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash }
    }
    $coreSource = Resolve-ProjectPath 'Assets/UMA'
    foreach ($file in (Get-CoreSourceFiles $coreSource | Where-Object { $_.Extension -eq '.meta' })) {
        $guid = Get-MetaGuid $file.FullName
        if ($ownedGuids.ContainsKey($guid)) { throw "Core and plugin both own $guid : $($file.FullName)" }
    }
    if (-not $SkipCore) { [void](Stage-Core $coreSource 'Build/CorePackage') }
    $reports | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'packages.json') -Encoding UTF8
    $reports | ConvertTo-Json -Depth 5
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    if (-not $resolvedWork.StartsWith($output.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe build cleanup: $resolvedWork"
    }
    if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
}
