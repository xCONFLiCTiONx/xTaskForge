param([string]$Mode = 'Check')
$log = Join-Path $env:LOCALAPPDATA 'xTaskForge\example.log'
"[{0}] Example script ran. Mode={1}" -f (Get-Date -Format o), $Mode | Add-Content -LiteralPath $log -Encoding utf8
