param(
    [string]$Destination = "Build/Plugins",
    [switch]$SkipCore
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Build-UMAContentPackages.ps1") -FunctionsOnly
$output = Resolve-ProjectPath $Destination
Assert-SafeArtifactDirectory $output
New-Item -ItemType Directory -Path $output -Force | Out-Null
$releaseVersion = Sync-UMAPackageVersion (Resolve-ProjectPath 'Assets/UMA')
$version = $releaseVersion.Version
$script:umaReleaseVersion = $releaseVersion.UmaVersion
$manifestGuids['overlay-painter'] = 'baf87f8fd4ef4ae49c08b67233d50f71'
$manifestGuids['overlay-painter-examples'] = 'f3d05e75b8aa4d3bb6d02fbb6e6f34b8'
$manifestGuids['overlay-painter-tests'] = '5f78f34f3fa748d0b47ad8ba7609428f'
$manifestGuids['hair-cards'] = '6adee602b17a4f099ff31323a6c8dc4c'
$manifestGuids['hair-cards-examples'] = 'a35472ded5b94e1eb0cc5bce746180f1'
$manifestGuids['hair-cards-tests'] = '64c3ec7799ed40c4bd7a116079de4025'
$manifestGuids['dismemberment'] = '1a936e502e1b4d81b2f97b4e8ce0b563'
$manifestGuids['dismemberment-examples'] = 'd14a3c192acf4fce9caa7ef74dc87eb0'
$manifestGuids['dismemberment-tests'] = '9ea72f06bca04ee7a4d2100630cffe54'
$definitions = @(
    @{ Id='hair-cards'; Name='HairCards'; Root='Assets/UMA/HairCards'; Exclude=@('Editor/Tests'); Dependencies=@('core'); Required=@('Core/UMA.HairCards.Core.asmdef','Runtime/UMA.HairCards.Runtime.asmdef','Editor/UMA.HairCards.Editor.asmdef','Editor/HairCardsPluginActions.cs','UMAPluginDocumentation.json') },
    @{ Id='hair-cards-examples'; Name='HairCardsExamples'; Root='Assets/UMAProjectData/HairCards/Examples'; Dependencies=@('core','hair-cards','uma3','srp'); Required=@('ShortHairPart/ShortHairPart_HairGroom.asset') },
    @{ Id='hair-cards-tests'; Name='HairCardsTests'; Root='Assets/UMA/HairCards/Editor/Tests'; Dependencies=@('core','hair-cards','test-framework'); Required=@('UMA.HairCards.Editor.Tests.asmdef') },
    @{ Id='dismemberment'; Name='Dismemberment'; Root='Assets/UMA/UMADismemberment'; Exclude=@('Samples','Tests'); Dependencies=@('core'); Required=@('Runtime/UMA.Dismemberment.Runtime.asmdef','Editor/UMA.Dismemberment.Editor.asmdef','Editor/DismembermentPluginActions.cs','UMAPluginDocumentation.json') },
    @{ Id='dismemberment-examples'; Name='DismembermentExamples'; Root='Assets/UMA/UMADismemberment/Samples'; Dependencies=@('core','dismemberment','uma3','srp'); Required=@('Scripts/UMA.Dismemberment.Samples.asmdef','Materials/SliceFill.mat') },
    @{ Id='dismemberment-tests'; Name='DismembermentTests'; Root='Assets/UMA/UMADismemberment/Tests'; Dependencies=@('core','dismemberment','test-framework'); Required=@('Editor/UMA.Dismemberment.Editor.Tests.asmdef') },
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
        if (-not (Test-Path -LiteralPath $source -PathType Container)) { continue }
        if (-not @($definition.Required | Where-Object { Test-Path -LiteralPath (Join-Path $source $_) -PathType Leaf }).Count) { continue }
        $records = Get-SourceRecords $source $definition.Root $definition.Exclude
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
