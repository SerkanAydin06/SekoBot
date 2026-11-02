# Enumerate ordinary application windows using PowerShell's Get-Process.
# Use Win32 GetClientRect for accurate client-area dimensions when available.
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$windowApi = @'
using System;
using System.Runtime.InteropServices;
public static class SekoBotWindowSize {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetClientRect(IntPtr handle, out RECT rectangle);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr handle);
}
'@
$canReadSize = $false
try {
    Add-Type -TypeDefinition $windowApi -ErrorAction Stop
    $canReadSize = $true
}
catch {
    # Still list windows if native dimension inspection cannot initialize.
}

try {
    $found = @(
        foreach ($proc in (Get-Process -ErrorAction SilentlyContinue)) {
            try {
                $title = [string]$proc.MainWindowTitle
                $handle = [int64]$proc.MainWindowHandle
                if ([string]::IsNullOrWhiteSpace($title) -or $handle -eq 0) {
                    continue
                }
                $width = 0
                $height = 0
                $minimized = $false
                if ($canReadSize) {
                    $rect = New-Object 'SekoBotWindowSize+RECT'
                    if ([SekoBotWindowSize]::GetClientRect([IntPtr]$handle, [ref]$rect)) {
                        $width = [Math]::Max(0, $rect.Right - $rect.Left)
                        $height = [Math]::Max(0, $rect.Bottom - $rect.Top)
                    }
                    $minimized = [SekoBotWindowSize]::IsIconic([IntPtr]$handle)
                }
                [pscustomobject]@{
                    hwnd = $handle
                    pid = [int64]$proc.Id
                    title = $title
                    width = $width
                    height = $height
                    minimized = $minimized
                    metin2 = (($title -replace '\s', '') -match '(?i)metin2') -or ($proc.ProcessName -match '(?i)metin2')
                }
            }
            catch {
                # Protected or closing processes can be skipped.
            }
        }
    )
    $json = ConvertTo-Json -InputObject $found -Depth 4 -Compress
    Write-Output ("SEKOBOT_WINDOWS=" + $json)
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
