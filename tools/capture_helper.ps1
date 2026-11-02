# SekoBot Win32 capture helper — Windows PowerShell 5.1 (bundled with Windows).
# Runs in a separate process and communicates only via local JSON and PNG files.
param(
    [Parameter(Mandatory = $true)]
    [string]$Session
)

$ErrorActionPreference = "Stop"
$commandPath = Join-Path $Session "command.json"
$statusPath = Join-Path $Session "status.json"
$utf8 = New-Object System.Text.UTF8Encoding($false)
$state = @{
    windows = @()
    scan_done = -1
    frame_no = 0
    frame_file = ""
    error = ""
    capture = $false
    ready = $false
}

function Publish-Status {
    try {
        $json = ConvertTo-Json -InputObject $state -Depth 8 -Compress
        $temp = $statusPath + ".new"
        [System.IO.File]::WriteAllText($temp, $json, $utf8)
        if ([System.IO.File]::Exists($statusPath)) {
            [System.IO.File]::Replace($temp, $statusPath, $null)
        }
        else {
            [System.IO.File]::Move($temp, $statusPath)
        }
    }
    catch {
        # Transient file locks are retried on the next command/capture iteration.
    }
}

try {
    Add-Type -Path (Join-Path $PSScriptRoot "WindowNative.cs") -ReferencedAssemblies "System.Drawing"
    [SekoBot.WindowNative]::InitializeDpi()
    $state.ready = $true
}
catch {
    $state.error = "Windows yardımcı modülü yüklenemedi: " + $_.Exception.Message
    try {
        [System.IO.File]::WriteAllText((Join-Path $Session "helper-error.txt"),
            ($_ | Out-String), $utf8)
    } catch {}
    Publish-Status
    exit 1
}

Publish-Status
$lastScan = -1
$lastScanAt = [DateTime]::MinValue
$frameNumber = 0
$nextFrameAt = [DateTime]::MinValue
$previousCapture = $false

while ($true) {
    try {
        if (-not [System.IO.File]::Exists($commandPath)) {
            Start-Sleep -Milliseconds 60
            continue
        }

        # Godot replaces command.json between polling intervals.
        # A transient missing/locked command is not a fatal capture failure.
        try {
            $json = [System.IO.File]::ReadAllText($commandPath, $utf8)
        }
        catch [System.IO.IOException] {
            Start-Sleep -Milliseconds 40
            continue
        }
        catch [System.UnauthorizedAccessException] {
            Start-Sleep -Milliseconds 40
            continue
        }
        $command = ConvertFrom-Json -InputObject $json
        if ($null -eq $command) {
            Start-Sleep -Milliseconds 60
            continue
        }
        if ($command.quit -eq $true) { break }

        $now = [DateTime]::UtcNow
        $requestedScan = [long]$command.scan
        if (($requestedScan -ne $lastScan) -or
            ($now - $lastScanAt).TotalSeconds -ge 4.0) {
            $state.windows = @([SekoBot.WindowNative]::ListWindows())
            $state.scan_done = $requestedScan
            $lastScan = $requestedScan
            $lastScanAt = $now
            Publish-Status
        }

        if ($command.capture -eq $true) {
            if (-not $previousCapture) {
                $state.error = ""
                $nextFrameAt = [DateTime]::MinValue
            }
            if ($now -ge $nextFrameAt) {
                $frameNumber += 1
                $name = "frame_{0}.png" -f $frameNumber
                $destination = Join-Path $Session $name
                $errorText = [SekoBot.WindowNative]::Capture(
                    [long]$command.hwnd, [long]$command.pid, $destination
                )
                if (-not [string]::IsNullOrEmpty($errorText)) {
                    $state.error = $errorText
                    $state.capture = $false
                }
                else {
                    $state.error = ""
                    $state.frame_no = $frameNumber
                    $state.frame_file = $name
                    $state.capture = $true
                }
                Publish-Status
                if ($frameNumber -gt 3) {
                    $obsolete = Join-Path $Session ("frame_{0}.png" -f ($frameNumber - 3))
                    if ([System.IO.File]::Exists($obsolete)) {
                        try { [System.IO.File]::Delete($obsolete) } catch {}
                    }
                }

                $fps = [Math]::Max(1, [Math]::Min(15, [int]$command.fps))
                $nextFrameAt = [DateTime]::UtcNow.AddMilliseconds(1000.0 / $fps)
            }
            $previousCapture = $true
        }
        else {
            if ($previousCapture) {
                $state.capture = $false
                $state.error = ""
                Publish-Status
            }
            $previousCapture = $false
        }
        Start-Sleep -Milliseconds 30
    }
    catch {
        $state.error = "Windows yardımcı işlem hatası: " + $_.Exception.Message
        try {
            [System.IO.File]::WriteAllText((Join-Path $Session "helper-error.txt"),
                ($_ | Out-String), $utf8)
        } catch {}
        Publish-Status
        Start-Sleep -Milliseconds 150
    }
}
