[CmdletBinding()]
param(
    [string]$ProjectPath,
    [string]$UnityPath,
    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $PSScriptRoot '../../../..'
}
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$sourceRoot = Join-Path $ProjectPath 'Assets/UMA/HairCards'
if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw "Hair Cards source is missing: $sourceRoot. Prebuilt assemblies are not a release validation."
}

# Check import metadata before Unity can ignore a script or regenerate a missing GUID.
# Meta sidecars are text; do not read or rewrite native Unity assets.
$issues = [Collections.Generic.List[string]]::new()
$hairGuids = @{}
foreach ($meta in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.meta') {
    $guidLines = @(Select-String -LiteralPath $meta.FullName -Pattern '^guid:\s*(\S+)\s*$')
    if ($guidLines.Count -ne 1 -or $guidLines[0].Matches[0].Groups[1].Value -cnotmatch '^[0-9a-f]{32}$') {
        $issues.Add("Invalid GUID (must be exactly 32 lowercase hex characters): $($meta.FullName)")
        continue
    }
    $guid = $guidLines[0].Matches[0].Groups[1].Value
    if ($hairGuids.ContainsKey($guid)) { $issues.Add("Duplicate GUID: $($meta.FullName) and $($hairGuids[$guid])") }
    else { $hairGuids[$guid] = $meta.FullName }
}
$scripts = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.cs')
if ($scripts.Count -eq 0) { $issues.Add('No Hair Cards C# source files were found.') }
foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Where-Object { $_.Extension -in '.cs', '.asmdef', '.asmref' }) {
    if (-not (Test-Path -LiteralPath ($file.FullName + '.meta') -PathType Leaf)) {
        $issues.Add("Missing import metadata: $($file.FullName).meta")
    }
}
# Detect collisions with assets outside Hair Cards as well.
foreach ($meta in Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Assets') -Recurse -File -Filter '*.meta') {
    foreach ($line in Select-String -LiteralPath $meta.FullName -Pattern '^guid:\s*([0-9a-f]{32})\s*$') {
        $guid = $line.Matches[0].Groups[1].Value
        if ($hairGuids.ContainsKey($guid) -and $hairGuids[$guid] -ne $meta.FullName) {
            $issues.Add("Hair Cards GUID collision: $($meta.FullName) and $($hairGuids[$guid])")
        }
    }
}
if ($issues.Count -gt 0) { throw ($issues -join [Environment]::NewLine) }
Write-Host "Metadata preflight passed: $($scripts.Count) scripts. This alone is NOT a compilation/test pass."
if ($PreflightOnly) { return }

# Never close the user's editor or launch another instance against an open project.
$lockPath = Join-Path $ProjectPath 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lockPath) {
    try { $lockProbe = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $lockProbe.Dispose() }
    catch { throw 'Close Unity for this project before running the full release gate. No editor was closed.' }
}
if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $versionLine = Select-String -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion:\s*(\S+)'
    $editorVersion = $versionLine.Matches[0].Groups[1].Value
    $UnityPath = "C:/Program Files/Unity/Hub/Editor/$editorVersion/Editor/Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity not found: $UnityPath" }
# A unique result directory prevents old passing XML from satisfying a failed new run.
$output = Join-Path $ProjectPath ('Logs/HairCardsReleaseGate/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$testXml = Join-Path $output 'editmode.xml'
$logPath = Join-Path $output 'unity.log'
$arguments = @('-batchmode', '-projectPath', $ProjectPath, '-runTests', '-testPlatform', 'EditMode',
    '-testFilter', 'UMA.HairCards.Editor.Tests', '-testResults', $testXml, '-logFile', $logPath)
$argumentLine = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
Write-Host "Importing and compiling actual project sources, then running Hair Cards tests. Log: $logPath"
# Graphics stay enabled because the suite exercises editor windows and material previews.
$process = Start-Process -FilePath $UnityPath -ArgumentList $argumentLine -WindowStyle Hidden -PassThru
while (-not $process.WaitForExit(1000)) { }
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $testXml)) {
    throw "Unity compile/test run failed (exit $($process.ExitCode)). See $logPath"
}
if (Select-String -LiteralPath $logPath -Pattern 'error CS\d+|does not have a valid GUID|GUID.*cannot be extracted|GUID.*[Cc]onflict|Asset file will be ignored' -Quiet) {
    throw "Unity reported a source import or compilation error. See $logPath"
}
[xml]$results = Get-Content -LiteralPath $testXml -Raw
$run = $results.'test-run'
$sourceTest = $results.SelectSingleNode("//test-case[@name='HairCardScriptsAreImportedFromSourceIntoTheirExpectedAssemblies']")
if ($run.result -ne 'Passed' -or [int]$run.total -le 0 -or [int]$run.failed -ne 0 -or
    [int]$run.skipped -ne 0 -or $null -eq $sourceTest -or $sourceTest.result -ne 'Passed') {
    throw "Release gate failed: require passing source-import coverage and all Hair Cards tests, with no skips. See $testXml"
}
Write-Host "RELEASE GATE PASSED: Unity source import + compilation + $($run.passed) tests. Results: $testXml"
