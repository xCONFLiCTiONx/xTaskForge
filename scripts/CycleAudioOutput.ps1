<#
.SYNOPSIS
    Cycle Audio Output Device script for xTaskForge
.DESCRIPTION
    Cycles through enabled Windows audio output devices via xTaskForge local Windows Named Pipe IPC.
#>

$ErrorActionPreference = 'Stop'
$pipePath = '\\.\pipe\xTaskForgePipe'

try {
    Write-Output "[$(Get-Date -Format 'HH:mm:ss')] Requesting audio output device cycle via local IPC..."

    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "xTaskForgePipe", [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(2000)

    $writer = New-Object System.IO.StreamWriter($pipe, [System.Text.Encoding]::UTF8)
    $reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)

    $writer.WriteLine('{"command":"audio.cycle"}')
    $writer.Flush()

    $responseJson = $reader.ReadLine()
    $pipe.Close()

    if ($responseJson) {
        $res = $responseJson | ConvertFrom-Json
        if ($res.Success) {
            Write-Output "[$(Get-Date -Format 'HH:mm:ss')] Successfully cycled audio output device: $($res.Data.deviceName)"
        }
        else {
            Write-Output "[ERROR] Audio cycle failed: $($res.Error)"
        }
    }
}
catch {
    Write-Output "[ERROR] Failed to communicate with xTaskForge IPC Pipe: $($_.Exception.Message)"
}
