# Simple Windows PowerShell 5.1 fallback, uses OS Get-Process without Add-Type.
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

try {
    $found = @(
        foreach ($proc in (Get-Process -ErrorAction SilentlyContinue)) {
            try {
                $title = [string]$proc.MainWindowTitle
                $handle = [int64]$proc.MainWindowHandle
                if ([string]::IsNullOrWhiteSpace($title) -or $handle -eq 0) {
                    continue
                }
                [pscustomobject]@{
                    hwnd = $handle
                    pid = [int64]$proc.Id
                    title = $title
                    width = 0
                    height = 0
                    minimized = $false
                    metin2 = (($title -replace '\s', '') -match '(?i)metin2') -or ($proc.ProcessName -match '(?i)metin2')
                }
            }
            catch {
                # Protected or closing process: skip.
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
