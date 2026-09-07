#Requires -Version 5.1
<#
.SYNOPSIS
  ARC cross-machine prerequisite checker (read-only; does not install or modify the machine).

.DESCRIPTION
  Validates SDK, solution layout, NuGet restore, private feed reachability, ports, and
  optional configuration hints. Never prints secrets.

  Status values: PASS | WARNING | BLOCKED | TBC
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [int]$ApiPort = 5187,
    [int]$WebPort = 5100,
    [switch]$SkipRestore
)

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $RepoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
}

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$results = [System.Collections.Generic.List[object]]::new()

function Write-StatusLine {
    param(
        [string]$Component,
        [ValidateSet("PASS", "WARNING", "BLOCKED", "TBC")]
        [string]$Status,
        [string]$Detail
    )
    $color = switch ($Status) {
        "PASS"    { "Green" }
        "WARNING" { "Yellow" }
        "BLOCKED" { "Red" }
        "TBC"     { "Cyan" }
    }
    Write-Host ("[{0}] {1,-28} {2}" -f $Status, $Component, $Detail) -ForegroundColor $color
    $results.Add([pscustomobject]@{ Component = $Component; Status = $Status; Detail = $Detail })
}

function Test-TcpPortFree {
    param([int]$Port)
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        $listener.Stop()
        return $true
    }
    catch {
        return $false
    }
}

Write-Host ""
Write-Host "ARC prerequisite check" -ForegroundColor Cyan
Write-Host "Repo: $RepoRoot"
Write-Host ""

# PowerShell
Write-StatusLine -Component "PowerShell" -Status "PASS" -Detail ("Version {0}" -f $PSVersionTable.PSVersion)

# .NET SDK
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-StatusLine -Component ".NET SDK" -Status "BLOCKED" -Detail "dotnet not found on PATH"
}
else {
    $sdkLine = (& dotnet --list-sdks 2>$null | Select-String "^\s*9\.0\." | Select-Object -Last 1)
    if ($sdkLine) {
        Write-StatusLine -Component ".NET 9 SDK" -Status "PASS" -Detail ($sdkLine.ToString().Trim())
    }
    else {
        Write-StatusLine -Component ".NET 9 SDK" -Status "BLOCKED" -Detail "Install .NET 9 SDK (9.0.x) from https://dotnet.microsoft.com/download"
    }

    $rtLine = (& dotnet --list-runtimes 2>$null | Select-String "Microsoft.AspNetCore.App 9\.0" | Select-Object -First 1)
    if ($rtLine) {
        Write-StatusLine -Component "ASP.NET Core 9" -Status "PASS" -Detail ($rtLine.ToString().Trim())
    }
    else {
        Write-StatusLine -Component "ASP.NET Core 9" -Status "WARNING" -Detail "ASP.NET Core 9 runtime not listed; SDK install usually includes it"
    }
}

# Solution
$sln = Join-Path $RepoRoot "ARC.sln"
if (Test-Path $sln) {
    Write-StatusLine -Component "ARC.sln" -Status "PASS" -Detail $sln
}
else {
    Write-StatusLine -Component "ARC.sln" -Status "BLOCKED" -Detail "Solution file not found"
}

# NuGet sources
$mccFeed = "https://nuget.pkg.github.com/MCCITGIT/index.json"
$feedConfigured = $false
if ($dotnet) {
    $sources = & dotnet nuget list source 2>$null
    if ($sources -match "MCCITGIT|nuget\.pkg\.github\.com/MCCITGIT") {
        $feedConfigured = $true
        Write-StatusLine -Component "MCC NuGet feed" -Status "PASS" -Detail "MCCITGIT source registered (machine NuGet.Config)"
    }
    else {
        Write-StatusLine -Component "MCC NuGet feed" -Status "BLOCKED" -Detail "Register $mccFeed and authenticate (see docs/TEAMMATE_SETUP.md)"
    }
}

# Restore
if (-not $SkipRestore -and (Test-Path $sln) -and $dotnet) {
    Push-Location $RepoRoot
    try {
        $restoreOut = & dotnet restore ARC.sln 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) {
            Write-StatusLine -Component "dotnet restore" -Status "PASS" -Detail "ARC.sln restore succeeded"
        }
        elseif ($restoreOut -match "NU1301|401|Unable to find package MCC\.Foundation") {
            Write-StatusLine -Component "dotnet restore" -Status "BLOCKED" -Detail "MCC.Foundation.Guardrails restore failed - configure GitHub Packages auth"
        }
        else {
            Write-StatusLine -Component "dotnet restore" -Status "BLOCKED" -Detail "Restore failed (run manually for full log)"
        }
    }
    finally {
        Pop-Location
    }
}
elseif ($SkipRestore) {
    Write-StatusLine -Component "dotnet restore" -Status "TBC" -Detail "Skipped (-SkipRestore)"
}

# MCC package presence (after restore attempt)
$assets = Join-Path $RepoRoot "src\ARC.Guardrails\obj\project.assets.json"
if (Test-Path $assets) {
    if (Select-String -Path $assets -Pattern "MCC.Foundation.Guardrails/1.0.1" -Quiet) {
        Write-StatusLine -Component "MCC.Foundation.Guardrails" -Status "PASS" -Detail "1.0.1 present in restore graph"
    }
    else {
        Write-StatusLine -Component "MCC.Foundation.Guardrails" -Status "BLOCKED" -Detail "Package 1.0.1 not in assets - restore/auth required"
    }
}
else {
    Write-StatusLine -Component "MCC.Foundation.Guardrails" -Status "TBC" -Detail "Run dotnet restore ARC.sln first"
}

# Ports
if (Test-TcpPortFree -Port $ApiPort) {
    Write-StatusLine -Component "API port $ApiPort" -Status "PASS" -Detail "Available for ARC.Api http profile"
}
else {
    Write-StatusLine -Component "API port $ApiPort" -Status "WARNING" -Detail "In use - stop existing ARC.Api or change launchSettings"
}

if (Test-TcpPortFree -Port $WebPort) {
    Write-StatusLine -Component "Web port $WebPort" -Status "PASS" -Detail "Available for ARC.Web http profile"
}
else {
    Write-StatusLine -Component "Web port $WebPort" -Status "WARNING" -Detail "In use - stop existing ARC.Web or change launchSettings"
}

# Optional configuration (presence only; never print values)
$devApi = Join-Path $RepoRoot "src\ARC.Api\appsettings.Development.json"
if (Test-Path $devApi) {
    Write-StatusLine -Component "API dev config" -Status "WARNING" -Detail "appsettings.Development.json exists - replace host-specific values for your machine"
}
else {
    Write-StatusLine -Component "API dev config" -Status "PASS" -Detail "No Development override (base appsettings.json is Shadow-safe)"
}

$sqlPasswordEnv = [Environment]::GetEnvironmentVariable("AppSettings__DBServerPassword")
if ($sqlPasswordEnv) {
    Write-StatusLine -Component "ODOS SQL password" -Status "PASS" -Detail "AppSettings__DBServerPassword environment variable is set"
}
else {
    Write-StatusLine -Component "ODOS SQL password" -Status "TBC" -Detail "Not set - required only for live ODOS reads (user-secrets or env var)"
}

$cosmosEnv = [Environment]::GetEnvironmentVariable("ARC_COSMOS_CONNECTION_STRING")
if ($cosmosEnv) {
    Write-StatusLine -Component "Cosmos (integration)" -Status "PASS" -Detail "ARC_COSMOS_CONNECTION_STRING is set"
}
else {
    Write-StatusLine -Component "Cosmos (integration)" -Status "TBC" -Detail "Optional - 10 integration tests need emulator or ARC_COSMOS_CONNECTION_STRING"
}

# HTTPS dev cert (informational)
if ($dotnet) {
    $httpsCheck = & dotnet dev-certs https --check 2>&1 | Out-String
    if ($httpsCheck -match "valid|trusted|A valid") {
        Write-StatusLine -Component "HTTPS dev cert" -Status "PASS" -Detail "Present (https launch profiles only; demo uses http)"
    }
    else {
        Write-StatusLine -Component "HTTPS dev cert" -Status "WARNING" -Detail "Not trusted - run: dotnet dev-certs https --trust (only if using https profile)"
    }
}

Write-Host ""
$blocked = @($results | Where-Object Status -eq "BLOCKED")
$warnings = @($results | Where-Object Status -eq "WARNING")
if ($blocked.Count -gt 0) {
    Write-Host "Overall: BLOCKED ($($blocked.Count) blocker(s))" -ForegroundColor Red
    exit 2
}
if ($warnings.Count -gt 0) {
    Write-Host "Overall: WARNING ($($warnings.Count) warning(s)) - demo may still work" -ForegroundColor Yellow
    exit 1
}
Write-Host "Overall: PASS" -ForegroundColor Green
exit 0
