#Requires -Version 5.1
<#
.SYNOPSIS
  Start ARC.Api and ARC.Web for local Shadow demo (http profiles).

.DESCRIPTION
  Runs prerequisite checks, restores packages, starts API then Web in separate
  processes, waits for API /health, prints demo URLs. Does not modify source,
  appsettings, or install software. Does not contain credentials.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [int]$ApiPort = 5187,
    [int]$WebPort = 5100,
    [int]$HealthTimeoutSeconds = 90,
    [switch]$SkipPrereqCheck,
    [switch]$SkipRestore
)

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $RepoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
}
else {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
}

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Stop-ArcDemoProcesses {
    Get-Process -Name "ARC.Api", "ARC.Web" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Wait-ForHealth {
    param([string]$Url, [int]$TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300) {
                return $true
            }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    return $false
}

Push-Location $RepoRoot
try {
    if (-not $SkipPrereqCheck) {
        Write-Host "Running prerequisite check..." -ForegroundColor Cyan
        & (Join-Path $scriptDir "check-prerequisites.ps1") -RepoRoot $RepoRoot -SkipRestore
        if ($LASTEXITCODE -eq 2) {
            throw "Prerequisite check reported BLOCKED status. Fix blockers before starting the demo."
        }
    }

    if (-not $SkipRestore) {
        Write-Host "Restoring packages..." -ForegroundColor Cyan
        & dotnet restore ARC.sln
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
    }

    Write-Host "Building Release..." -ForegroundColor Cyan
    & dotnet build ARC.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    # Avoid DLL lock errors if a previous demo is still running
    Stop-ArcDemoProcesses
    Start-Sleep -Seconds 1

    $apiUrl = "http://localhost:$ApiPort"
    $webUrl = "http://localhost:$WebPort"
    $healthUrl = "$apiUrl/health"

    Write-Host "Starting ARC.Api ($apiUrl)..." -ForegroundColor Cyan
    $apiProc = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", "src/ARC.Api", "-c", "Release", "--launch-profile", "http", "--no-build") `
        -WorkingDirectory $RepoRoot `
        -PassThru `
        -WindowStyle Normal

    Write-Host "Waiting for $healthUrl ..."
    if (-not (Wait-ForHealth -Url $healthUrl -TimeoutSeconds $HealthTimeoutSeconds)) {
        Stop-ArcDemoProcesses
        throw "ARC.Api did not respond on $healthUrl within $HealthTimeoutSeconds seconds"
    }
    Write-Host "ARC.Api healthy." -ForegroundColor Green

    Write-Host "Starting ARC.Web ($webUrl)..." -ForegroundColor Cyan
    $webProc = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", "src/ARC.Web", "-c", "Release", "--launch-profile", "http", "--no-build") `
        -WorkingDirectory $RepoRoot `
        -PassThru `
        -WindowStyle Normal

    Start-Sleep -Seconds 5

    Write-Host ""
    Write-Host "ARC Shadow demo is running." -ForegroundColor Green
    Write-Host "  API:    $apiUrl  (health: $healthUrl)"
    Write-Host "  Web UI: $webUrl"
    Write-Host ""
    Write-Host "Stop: close the API/Web console windows, or run:"
    Write-Host "  Get-Process -Name ARC.Api,ARC.Web | Stop-Process -Force"
    Write-Host ""
    Write-Host "API PID: $($apiProc.Id)  Web PID: $($webProc.Id)"
}
finally {
    Pop-Location
}
