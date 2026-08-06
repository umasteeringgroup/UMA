[CmdletBinding()]
param(
    [string]$UnityPath,
    [string]$ProjectPath,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    # Unity may clear the project Temp folder between clean-process phases.
    $OutputDirectory = Join-Path $ProjectPath 'Logs\TexturePaintReleaseGate'
}

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $versionFile = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
    $versionLine = Get-Content -LiteralPath $versionFile | Select-Object -First 1
    $editorVersion = ($versionLine -replace '^m_EditorVersion:\s*', '').Trim()
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity executable was not found at '$UnityPath'. Pass -UnityPath explicitly."
}
if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath 'Assets') -PathType Container)) {
    throw "'$ProjectPath' is not a Unity project."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$preflightPath = Join-Path $OutputDirectory 'preflight.json'
$editResults = Join-Path $OutputDirectory 'editmode-results.xml'
$playResults = Join-Path $OutputDirectory 'playmode-results.xml'
$summaryJson = Join-Path $OutputDirectory 'release-gate-summary.json'
$summaryMarkdown = Join-Path $OutputDirectory 'release-gate-summary.md'

function Invoke-UnityGate {
    param([string]$Name, [string[]]$Arguments, [string]$LogPath)
    Write-Host "[$Name] Starting Unity..."
    $allArguments = $Arguments + @('-logFile', $LogPath)
    $argumentLine = ($allArguments | ForEach-Object {
        '"' + ($_ -replace '"', '\"') + '"'
    }) -join ' '
    $process = Start-Process -FilePath $UnityPath -ArgumentList $argumentLine -Wait -PassThru -NoNewWindow
    $exitCode = $process.ExitCode
    Write-Host "[$Name] Unity exited with code $exitCode. Log: $LogPath"
    return $exitCode
}

function Read-TestResult {
    param([string]$Name, [string]$Path, [int]$ExitCode)
    $result = [ordered]@{
        name = $Name
        exitCode = $ExitCode
        total = 0
        passed = 0
        failed = 0
        skipped = 0
        durationSeconds = 0
        resultsPath = $Path
    }
    if (Test-Path -LiteralPath $Path) {
        [xml]$xml = Get-Content -LiteralPath $Path -Raw
        $run = $xml.SelectSingleNode('//test-run')
        if ($null -ne $run) {
            $result.total = [int]$run.total
            $result.passed = [int]$run.passed
            $result.failed = [int]$run.failed
            $result.skipped = [int]$run.skipped
            $result.durationSeconds = [double]$run.duration
        }
    }
    return [pscustomobject]$result
}

# Keep the graphics device enabled: GPU golden images are a blocking release check.
$common = @('-batchmode', '-projectPath', $ProjectPath)
$preflightLog = Join-Path $OutputDirectory 'preflight.log'
$preflightExit = Invoke-UnityGate -Name 'Preflight' -LogPath $preflightLog -Arguments ($common + @(
    '-quit', '-executeMethod', 'UMA.TexturePaint.Editor.TexturePaintReleaseGate.RunBatchPreflight',
    '-texturePaintGateReport', $preflightPath
))

$editLog = Join-Path $OutputDirectory 'editmode.log'
$editExit = Invoke-UnityGate -Name 'EditMode' -LogPath $editLog -Arguments ($common + @(
    '-runTests', '-testPlatform', 'EditMode', '-testFilter', 'UMA.TexturePaint.Editor.Tests',
    '-testResults', $editResults
))

$playLog = Join-Path $OutputDirectory 'playmode.log'
$playExit = Invoke-UnityGate -Name 'PlayMode' -LogPath $playLog -Arguments ($common + @(
    '-runTests', '-testPlatform', 'PlayMode', '-testFilter', 'UMA.TexturePaint.Tests',
    '-testResults', $playResults
))

$edit = Read-TestResult -Name 'EditMode' -Path $editResults -ExitCode $editExit
$play = Read-TestResult -Name 'PlayMode' -Path $playResults -ExitCode $playExit
$preflight = $null
if (Test-Path -LiteralPath $preflightPath) {
    $preflight = Get-Content -LiteralPath $preflightPath -Raw | ConvertFrom-Json
}

$passing = $preflightExit -eq 0 -and $editExit -eq 0 -and $playExit -eq 0 -and
    $null -ne $preflight -and $preflight.failed -eq 0 -and
    $edit.total -gt 0 -and $edit.failed -eq 0 -and $edit.skipped -eq 0 -and
    $play.total -gt 0 -and $play.failed -eq 0 -and $play.skipped -eq 0

$summary = [ordered]@{
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    passing = $passing
    unityPath = $UnityPath
    projectPath = $ProjectPath
    preflight = $preflight
    suites = @($edit, $play)
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryJson -Encoding utf8

$preflightText = if ($null -eq $preflight) { 'missing' } else {
    "$($preflight.passed) passed / $($preflight.warnings) warnings / $($preflight.failed) failed"
}
$markdown = @"
# Overlay Painter Release Gate

- Result: **$(if ($passing) { 'PASS' } else { 'FAIL' })**
- Generated: $($summary.generatedUtc)
- Unity: $UnityPath
- Preflight: $preflightText
- EditMode: $($edit.passed)/$($edit.total) passed, $($edit.failed) failed, $($edit.skipped) skipped
- PlayMode: $($play.passed)/$($play.total) passed, $($play.failed) failed, $($play.skipped) skipped

Artifacts are stored beside this summary. GPU golden failures additionally write expected, actual, and amplified-difference PNGs under `Temp/TexturePaintGoldenFailures`.
"@
$markdown | Set-Content -LiteralPath $summaryMarkdown -Encoding utf8

Write-Host "Release gate result: $(if ($passing) { 'PASS' } else { 'FAIL' })"
Write-Host "Summary: $summaryMarkdown"
if (-not $passing) { exit 1 }
