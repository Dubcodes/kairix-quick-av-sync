[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExePath,
    [ValidateRange(1, 30)]
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "Executable not found: $ExePath"
}

if (-not ('KairixWindowProbe' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class KairixWindowProbe
{
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
}
'@
}

$process = Start-Process -FilePath $ExePath -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) {
            throw "Application exited before presenting a window (exit code $($process.ExitCode))."
        }

        $handle = $process.MainWindowHandle
        $title = $process.MainWindowTitle
        $visible = $handle -ne [IntPtr]::Zero -and [KairixWindowProbe]::IsWindowVisible($handle)
        $mainWindowReady = $visible -and $title -eq 'Kairix Quick A/V Sync'
    } while (-not $mainWindowReady -and [DateTime]::UtcNow -lt $deadline)

    if (-not $mainWindowReady) {
        throw "Application remained alive but did not present a visible titled Kairix main window within $TimeoutSeconds seconds (handle=$handle, title='$title')."
    }

    [pscustomobject]@{
        ProcessId = $process.Id
        MainWindowHandle = ('0x{0:X}' -f $handle.ToInt64())
        MainWindowTitle = $title
        Visible = $visible
    }
}
finally {
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(10000)) {
            Stop-Process -Id $process.Id -Force
            throw "Application did not close after its main window was closed."
        }
    }
}
