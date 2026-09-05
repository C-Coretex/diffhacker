<#
.SYNOPSIS
    Launches DiffHacker, captures the screen, and closes it. Windows only.

.DESCRIPTION
    Evidence, not a gate. The self-test run is what proves the bridge works; this exists so a
    human can see how WebView2, WKWebView and WebKitGTK each render the same bundle. A failure
    here must never fail the build.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $HostExecutable,
    [Parameter(Mandatory = $true)][string] $OutputPath,
    [int] $SettleSeconds = 8
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null

$process = Start-Process -FilePath $HostExecutable -PassThru
try {
    Start-Sleep -Seconds $SettleSeconds

    Add-Type -AssemblyName System.Windows.Forms, System.Drawing

    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        }
        finally {
            $graphics.Dispose()
        }

        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }

    Write-Host "Screenshot written to $OutputPath"
}
finally {
    if (-not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (-not $process.HasExited) {
            $process.Kill()
        }
    }
}
