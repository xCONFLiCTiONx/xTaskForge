<#
.SYNOPSIS
    Build (and optionally run) xTaskForge.
.EXAMPLE
    .\.build.ps1                     # Release build
    .\.build.ps1 -Clean -Run         # wipe bin/obj, rebuild, launch
    .\.build.ps1 -ResetAppData -Run  # also wipe %LOCALAPPDATA%\xTaskForge for a fresh start
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Clean,
    [switch]$ResetAppData,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$project = Join-Path $root 'src\xTaskForge.csproj'
$exe     = Join-Path $root "src\bin\$Configuration\net8.0-windows\xTaskForge.exe"

# Stop any running instance so the exe isn't locked.
Get-Process -Name xTaskForge -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if ($ResetAppData) {
    $appData = Join-Path $env:LOCALAPPDATA 'xTaskForge'
    if (Test-Path -LiteralPath $appData) {
        Remove-Item -LiteralPath $appData -Recurse -Force
        Write-Host "Removed $appData"
    }
}

if ($Clean) {
    foreach ($dir in 'bin', 'obj') {
        $path = Join-Path $root "src\$dir"
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    Write-Host 'Cleaned bin/obj'
}

dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)" }
if (-not (Test-Path -LiteralPath $exe)) { throw "Build succeeded but output not found: $exe" }
Write-Host "Built: $exe" -ForegroundColor Green

if ($Configuration -eq 'Release') {
    $deployDir = (Join-Path $env:LOCALAPPDATA "xPrograms\xTaskForge")
    $releaseBinDir = Split-Path -Parent $exe
    New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
    Copy-Item -Path "$releaseBinDir\*" -Destination $deployDir -Recurse -Force
    $exe = Join-Path $deployDir 'xTaskForge.exe'
    Write-Host "Deployed: $exe" -ForegroundColor Green
}

if ($Run) {
    Start-Process -FilePath $exe
    Write-Host 'Started xTaskForge.'
}
