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
using System.Text;
public static class KairixWindowProbe
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static IntPtr FindWindow(uint processId, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var owner);
            if (owner != processId || !IsWindowVisible(hWnd) || !IsZoomed(hWnd)) return true;
            var text = new StringBuilder(256);
            GetWindowText(hWnd, text, text.Capacity);
            if (!string.Equals(text.ToString(), title, StringComparison.Ordinal)) return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }
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

        $handle = [KairixWindowProbe]::FindWindow([uint32]$process.Id, 'Kairix Quick A/V Sync')
        $title = if ($handle -ne [IntPtr]::Zero) { 'Kairix Quick A/V Sync' } else { '' }
        $visible = $handle -ne [IntPtr]::Zero
        $maximized = $visible
        $mainWindowReady = $visible
    } while (-not $mainWindowReady -and [DateTime]::UtcNow -lt $deadline)

    if (-not $mainWindowReady) {
        throw "Application remained alive but did not present a visible maximized titled Kairix main window within $TimeoutSeconds seconds (handle=$handle, title='$title', maximized=$maximized)."
    }

    [pscustomobject]@{
        ProcessId = $process.Id
        MainWindowHandle = ('0x{0:X}' -f $handle.ToInt64())
        MainWindowTitle = $title
        Visible = $visible
        Maximized = $maximized
    }
}
finally {
    if (-not $process.HasExited) {
        if ($handle -eq [IntPtr]::Zero) {
            Stop-Process -Id $process.Id -Force
        } else {
            if (-not [KairixWindowProbe]::PostMessage($handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) {
                Stop-Process -Id $process.Id -Force
                throw "Could not send WM_CLOSE to the verified Kairix main window (Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
            }
            if (-not $process.WaitForExit(10000)) {
                Stop-Process -Id $process.Id -Force
                throw "Application did not close after its main window was closed."
            }
        }
    }
}
