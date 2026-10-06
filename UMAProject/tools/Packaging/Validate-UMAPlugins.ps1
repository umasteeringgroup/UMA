param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe',
    [string]$ValidationProject = 'tmp/UMAPluginCoreValidation',
    [string[]]$Phases = @('Absent','Install','Installed','Update','Remove','Removed','Reinstall','Reinstalled')
)
$ErrorActionPreference = 'Stop'
$Phases = @(($Phases -join ',') -split ',')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = [IO.Path]::GetFullPath((Join-Path $repo $ValidationProject))
$allowed = [IO.Path]::GetFullPath((Join-Path $repo 'tmp')).TrimEnd('\') + '\'
if (-not $project.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation project must be below this checkout/tmp.' }
if (Test-Path -LiteralPath (Join-Path $project 'Temp/UnityLockfile')) {
    try { $lock = [IO.File]::Open((Join-Path $project 'Temp/UnityLockfile'), 'Open', 'ReadWrite', 'None'); $lock.Dispose() }
    catch { throw 'Validation editor is already running.' }
}
$core = Join-Path $repo 'Build/CorePackage'
if (-not (Test-Path -LiteralPath (Join-Path $core 'package.json'))) { throw 'Build plugin packages and core staging first.' }
if ($Phases[0] -eq 'Absent' -and (Test-Path -LiteralPath (Join-Path $project 'Assets'))) {
    $assets = [IO.Path]::GetFullPath((Join-Path $project 'Assets'))
    if (-not (Test-Path -LiteralPath (Join-Path $assets 'Editor/UMAPluginMigrationSmoke.cs')) -and
        @(Get-ChildItem -LiteralPath $assets -Force).Count -gt 0) {
        throw 'This validation folder contains another project. Choose a new ValidationProject.'
    }
    if (Test-Path -LiteralPath (Join-Path $project 'Library/UMA/ContentInstaller/pending.json')) {
        throw 'Resolve the previous validation import transaction before starting a fresh cycle.'
    }
    $backup = [IO.Path]::GetFullPath((Join-Path $project ('PreviousAssets-' + [Guid]::NewGuid().ToString('N'))))
    $projectPrefix = $project.TrimEnd('\') + '\'
    if (-not $assets.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not $backup.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe validation reset paths.' }
    if (((Get-Item -LiteralPath $assets).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        @(Get-ChildItem -LiteralPath $assets -Recurse -Force | Where-Object {
            ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
        }).Count -gt 0) { throw 'Validation reset does not follow junctions or symbolic links.' }
    Move-Item -LiteralPath $assets -Destination $backup
}
foreach ($folder in @('Assets/Editor','Packages/com.umasteeringgroup.uma','ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $project $folder) -Force | Out-Null
}
Copy-Item -Path (Join-Path $core '*') -Destination (Join-Path $project 'Packages/com.umasteeringgroup.uma') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repo 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Force
if (-not (Test-Path -LiteralPath (Join-Path $project 'Packages/manifest.json'))) {
    Copy-Item -LiteralPath (Join-Path $repo 'Packages/manifest.json') -Destination (Join-Path $project 'Packages/manifest.json')
}
if ($Phases -contains 'CoreTests') {
    $manifestPath = Join-Path $project 'Packages/manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $testables = @($manifest.testables) + @('com.umasteeringgroup.uma')
    $manifest | Add-Member -NotePropertyName testables -NotePropertyValue @($testables | Where-Object { $_ } | Select-Object -Unique) -Force
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10))
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Validation/UMAPluginMigrationSmoke.cs') -Destination (Join-Path $project 'Assets/Editor/UMAPluginMigrationSmoke.cs') -Force
$settings = Join-Path $repo 'Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset'
$bytes = [IO.File]::ReadAllBytes($settings)
if ([Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(10,$bytes.Length)) -notlike '%YAML 1.1*') { throw 'Expected text-serialized UMASettings for version verification.' }
$umaVersion = [regex]::Match([IO.File]::ReadAllText($settings), '(?m)^\s*UMAVersion:\s*(.+)$').Groups[1].Value.Trim()
$unityVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'ProjectSettings/ProjectVersion.txt')), 'm_EditorVersion:\s*([^\r\n]+)').Groups[1].Value.Trim()
$package = Get-Content -LiteralPath (Join-Path $core 'package.json') -Raw | ConvertFrom-Json
$archive = Join-Path $repo ('Build/Plugins/OverlayPainter-' + $package.version + '.unitypackage')
foreach ($phase in $Phases) {
    $log = Join-Path $project ('plugin-' + $phase + '.log')
    $phaseArchive = if ($phase -eq 'InstallTests') { Join-Path $repo ('Build/Plugins/OverlayPainterTests-' + $package.version + '.unitypackage') } else { $archive }
    $arguments = @('-batchmode','-nographics','-projectPath',('"'+$project+'"'),'-executeMethod','UMAPluginMigrationSmoke.Run',
        '-umaPluginPhase',$phase,'-umaPluginArchive',('"'+$phaseArchive+'"'),'-umaExpectedUnity',$unityVersion,
        '-umaExpectedUma',('"'+$umaVersion+'"'),'-logFile',('"'+$log+'"'))
    if ($phase -eq 'PersistenceTests' -or $phase -eq 'CoreTests') {
        $resultXml = Join-Path $project ('Library/plugin-' + $phase + '-tests.xml')
        $filter = if ($phase -eq 'CoreTests') { 'UMA.Editors.Tests.UMAPlugin' } else { 'UMA.TexturePaint.Editor.Tests.TexturePaintDocumentPersistenceTests' }
        $arguments = @('-batchmode','-projectPath',('"'+$project+'"'),'-runTests','-testPlatform','EditMode',
            '-testFilter',$filter,
            '-testResults',('"'+$resultXml+'"'),'-logFile',('"'+$log+'"'))
    }
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(15*60*1000)) { $process.Kill(); throw "Plugin validation timed out: $phase" }
    if ($phase -eq 'PersistenceTests' -or $phase -eq 'CoreTests') {
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultXml)) { throw "Persistence tests failed. See $log" }
        [xml]$tests = Get-Content -LiteralPath $resultXml -Raw
        $run = $tests.'test-run'
        if ([int]$run.failed -ne 0 -or [int]$run.passed -eq 0) { throw "Persistence tests did not pass: $resultXml" }
        Write-Output "PASS $phase; passed=$($run.passed); skipped=$($run.skipped)"
        continue
    }
    $result = Join-Path $project ('Library/plugin-' + $phase + '.txt')
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result)) { throw "Plugin validation failed: $phase. See $log" }
    Get-Content -LiteralPath $result
}
