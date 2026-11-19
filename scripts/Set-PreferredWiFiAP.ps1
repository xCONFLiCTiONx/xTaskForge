<#
.SYNOPSIS
    Location-safe Wi-Fi roaming compatibility script.
.DESCRIPTION
    This script intentionally does not scan for nearby networks, query a
    connected BSSID, or request a connection to a specific BSSID. Windows
    WLAN AutoConfig and the wireless adapter driver select and roam between
    access points advertising the saved Wi-Fi profile.
#>

[CmdletBinding()]
param(
    [string]$InterfaceName = 'Wi-Fi',
    [string]$ProfileName = 'jimosrvpark',
    [string[]]$PreferredBssids = @(
        'f0:9f:c2:d2:3e:0e',
        'f0:9f:c2:d2:3e:07'
    ),
    [ValidateRange(5, 120)]
    [int]$ConnectionTimeoutSeconds = 30,
    [ValidateRange(1, 10)]
    [int]$PollIntervalSeconds = 2
)

$ErrorActionPreference = 'Stop'

$LogDirectory = Join-Path $env:LOCALAPPDATA 'xTaskForge'
$LogFile = Join-Path $LogDirectory 'Set-PreferredWiFiAP.log'
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null

$Message = @"
[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] [INFO] No BSSID scan or forced BSSID association was performed. Windows WLAN AutoConfig and the Wi-Fi adapter driver manage roaming for profile '$ProfileName' on interface '$InterfaceName'. This avoids location-sensitive BSSID APIs.
"@

Add-Content -LiteralPath $LogFile -Value $Message -Encoding UTF8
Write-Output $Message.Trim()
exit 0
