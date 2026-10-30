<#
.SYNOPSIS
  Dev helper for MTI Storage Pick ↔ IT.Storage (WWKS2 on port 6050).

.EXAMPLE
  .\scripts\run-storage-pick-dev.ps1
  Interactive menu.

.EXAMPLE
  .\scripts\run-storage-pick-dev.ps1 -Action Robot
  Start seeded robot in a new window.

.EXAMPLE
  .\scripts\run-storage-pick-dev.ps1 -Action Smoke
  Run WWKS smoke test (robot must already be running).

.EXAMPLE
  .\scripts\run-storage-pick-dev.ps1 -Action Mti
  Start MTI web app in a new window.

.EXAMPLE
  .\scripts\run-storage-pick-dev.ps1 -Action All
  Robot (new window) → wait → smoke test → print MTI URL.
#>
param(
    [ValidateSet("Menu", "Robot", "Smoke", "Mti", "All")]
    [string] $Action = "Menu"
)

$ErrorActionPreference = "Stop"
$MtiRoot = Split-Path $PSScriptRoot -Parent
$WwksRoot = Join-Path (Split-Path $MtiRoot -Parent) "Test-WWKS"
$SeedRobotProj = Join-Path $WwksRoot "tools\SeedRobotHost\SeedRobotHost.csproj"
$SmokeProj = Join-Path $MtiRoot "tools\StoragePickSmoke\StoragePickSmoke.csproj"
$MtiProj = Join-Path $MtiRoot "ManufacturingTimeTracking.csproj"
$MtiUrl = "http://localhost:80/StoragePick"

function Test-Port6050 {
    $line = netstat -ano | Select-String ":6050\s" | Select-String "LISTENING" | Select-Object -First 1
    if ($line) { return $true }
    return $false
}

function Start-RobotHost {
    if (-not (Test-Path $SeedRobotProj)) {
        Write-Host "Missing: $SeedRobotProj" -ForegroundColor Red
        return
    }
    Write-Host "Starting seeded robot on port 6050 (new window)..." -ForegroundColor Cyan
    Start-Process powershell -ArgumentList @(
        "-NoExit", "-Command",
        "dotnet run --project `"$SeedRobotProj`""
    ) | Out-Null
}

function Invoke-SmokeTest {
    if (-not (Test-Path $SmokeProj)) {
        Write-Host "Missing: $SmokeProj" -ForegroundColor Red
        return 1
    }
    if (-not (Test-Port6050)) {
        Write-Host "Nothing listening on port 6050. Start the robot first (option 1 or -Action Robot)." -ForegroundColor Yellow
        return 1
    }
    Write-Host "Running Storage Pick smoke test..." -ForegroundColor Cyan
    Push-Location $MtiRoot
    try {
        dotnet run --project $SmokeProj
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

function Start-MtiApp {
    Write-Host "Starting MTI (new window)..." -ForegroundColor Cyan
    Start-Process powershell -ArgumentList @(
        "-NoExit", "-Command",
        "Set-Location `"$MtiRoot`"; dotnet run --project `"$MtiProj`""
    ) | Out-Null
    Write-Host "When ready, open: $MtiUrl" -ForegroundColor Green
    Write-Host "Login: inventory@test.com / Inventory123! (or Admin)" -ForegroundColor Gray
}

function Show-Menu {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  MTI Storage Pick — dev test" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "1. Start robot (seeded, port 6050)"
    Write-Host "2. Smoke test (WWKS, no browser)"
    Write-Host "3. Start MTI web app"
    Write-Host "4. Full flow: robot → smoke → show URL"
    Write-Host "Q. Quit"
    Write-Host ""
}

switch ($Action) {
    "Robot" { Start-RobotHost }
    "Smoke" { exit (Invoke-SmokeTest) }
    "Mti" { Start-MtiApp }
    "All" {
        Start-RobotHost
        Write-Host "Waiting 8 seconds for robot startup..." -ForegroundColor Gray
        Start-Sleep -Seconds 8
        $code = Invoke-SmokeTest
        Write-Host ""
        Write-Host "Next: start MTI and open $MtiUrl" -ForegroundColor Green
        Write-Host "  .\scripts\run-storage-pick-dev.ps1 -Action Mti" -ForegroundColor Gray
        exit $code
    }
    default {
        do {
            Show-Menu
            $c = Read-Host "Choice"
            switch ($c.ToUpper()) {
                "1" { Start-RobotHost }
                "2" { Invoke-SmokeTest | Out-Null }
                "3" { Start-MtiApp }
                "4" {
                    Start-RobotHost
                    Start-Sleep -Seconds 8
                    Invoke-SmokeTest | Out-Null
                    Start-MtiApp
                }
                "Q" { break }
                default { Write-Host "Invalid choice." -ForegroundColor Red }
            }
            if ($c.ToUpper() -ne "Q") {
                Write-Host ""
                Read-Host "Press Enter to continue"
            }
        } while ($c.ToUpper() -ne "Q")
    }
}
