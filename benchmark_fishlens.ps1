param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [string]$ProjectRoot = $PSScriptRoot,

    [string]$PythonExe = "python"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$mainPy = Join-Path $ProjectRoot "main.py"
if (-not (Test-Path $mainPy)) {
    throw "Could not find main.py at $mainPy"
}

if (-not (Test-Path $InputPath)) {
    throw "InputPath does not exist: $InputPath"
}

$videoExtensions = @(".mp4", ".avi", ".mov", ".mkv", ".asf", ".wmv", ".flv", ".webm")
$sourceVideos = Get-ChildItem -Path $InputPath -File | Where-Object { $videoExtensions -contains $_.Extension.ToLowerInvariant() }
$totalSourceVideos = @($sourceVideos).Count
if ($totalSourceVideos -eq 0) {
    throw "No supported videos found in: $InputPath"
}

$resultsRoot = Join-Path $ProjectRoot "results"
if (-not (Test-Path $resultsRoot)) {
    New-Item -ItemType Directory -Path $resultsRoot | Out-Null
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$configs = @(
    [pscustomobject]@{ Name = "auto";         Workers = "auto"; AutoTune = "1"; StrictStride = $null },
    [pscustomobject]@{ Name = "manual_safe";  Workers = "1";    AutoTune = "0"; StrictStride = "2" },
    [pscustomobject]@{ Name = "manual_fast";  Workers = "2";    AutoTune = "0"; StrictStride = "3" }
)

$originalEnv = @{
    FISHLENS_RUN_FOLDER = $env:FISHLENS_RUN_FOLDER
    FISHLENS_AUTOTUNE = $env:FISHLENS_AUTOTUNE
    FISHLENS_WORKERS = $env:FISHLENS_WORKERS
    FISHLENS_STRICT_FRAME_STRIDE = $env:FISHLENS_STRICT_FRAME_STRIDE
}

$summary = @()

Push-Location $ProjectRoot
try {
    foreach ($cfg in $configs) {
        $runFolder = Join-Path $resultsRoot ("bench_{0}_{1}" -f $stamp, $cfg.Name)
        New-Item -ItemType Directory -Path $runFolder -Force | Out-Null

        $env:FISHLENS_RUN_FOLDER = $runFolder
        $env:FISHLENS_AUTOTUNE = $cfg.AutoTune
        $env:FISHLENS_WORKERS = $cfg.Workers

        if ($null -ne $cfg.StrictStride) {
            $env:FISHLENS_STRICT_FRAME_STRIDE = $cfg.StrictStride
        }
        else {
            Remove-Item Env:FISHLENS_STRICT_FRAME_STRIDE -ErrorAction SilentlyContinue
        }

        Write-Host ""
        Write-Host "=== Running config: $($cfg.Name) ===" -ForegroundColor Cyan
        Write-Host "RUN_FOLDER=$runFolder"
        Write-Host "FISHLENS_AUTOTUNE=$($env:FISHLENS_AUTOTUNE)"
        Write-Host "FISHLENS_WORKERS=$($env:FISHLENS_WORKERS)"
        Write-Host "FISHLENS_STRICT_FRAME_STRIDE=$($env:FISHLENS_STRICT_FRAME_STRIDE)"

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & $PythonExe $mainPy $InputPath
        $exitCode = $LASTEXITCODE
        $sw.Stop()

        $runMaster = Join-Path $runFolder "run_master.csv"
        $rows = @()
        if (Test-Path $runMaster) {
            $rows = Import-Csv -Path $runMaster
        }

        $fishRows = @($rows | Where-Object {
            ($_.likely_class -ne "no_fish") -and -not [string]::IsNullOrWhiteSpace($_.video_file)
        })

        $uniqueDetectedVideos = @($fishRows | Select-Object -ExpandProperty video_file -Unique).Count
        $fishTrackRows = @($fishRows).Count

        $hours = [math]::Max(0.0001, $sw.Elapsed.TotalHours)
        $videosPerHour = [math]::Round($totalSourceVideos / $hours, 2)

        $summary += [pscustomobject]@{
            Config = $cfg.Name
            ExitCode = $exitCode
            ElapsedSec = [math]::Round($sw.Elapsed.TotalSeconds, 1)
            SourceVideos = $totalSourceVideos
            VideosPerHour = $videosPerHour
            DetectedVideos = $uniqueDetectedVideos
            FishTrackRows = $fishTrackRows
            RunMaster = $runMaster
        }
    }
}
finally {
    Pop-Location

    if ($null -eq $originalEnv.FISHLENS_RUN_FOLDER) { Remove-Item Env:FISHLENS_RUN_FOLDER -ErrorAction SilentlyContinue } else { $env:FISHLENS_RUN_FOLDER = $originalEnv.FISHLENS_RUN_FOLDER }
    if ($null -eq $originalEnv.FISHLENS_AUTOTUNE) { Remove-Item Env:FISHLENS_AUTOTUNE -ErrorAction SilentlyContinue } else { $env:FISHLENS_AUTOTUNE = $originalEnv.FISHLENS_AUTOTUNE }
    if ($null -eq $originalEnv.FISHLENS_WORKERS) { Remove-Item Env:FISHLENS_WORKERS -ErrorAction SilentlyContinue } else { $env:FISHLENS_WORKERS = $originalEnv.FISHLENS_WORKERS }
    if ($null -eq $originalEnv.FISHLENS_STRICT_FRAME_STRIDE) { Remove-Item Env:FISHLENS_STRICT_FRAME_STRIDE -ErrorAction SilentlyContinue } else { $env:FISHLENS_STRICT_FRAME_STRIDE = $originalEnv.FISHLENS_STRICT_FRAME_STRIDE }
}

Write-Host ""
Write-Host "=== Benchmark Summary ===" -ForegroundColor Green
$summary | Sort-Object ElapsedSec | Format-Table Config, ExitCode, ElapsedSec, SourceVideos, VideosPerHour, DetectedVideos, FishTrackRows -AutoSize

Write-Host ""
Write-Host "Run folders:"
$summary | ForEach-Object { Write-Host ("- {0}" -f $_.RunMaster) }
