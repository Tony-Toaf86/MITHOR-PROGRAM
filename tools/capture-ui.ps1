param(
    [string]$Output = 'build\ui-print.png',
    [switch]$KeepOpen,
    [string]$SettingsFile = '',
    [string]$AppArgs = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Get-Process MITHOR -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class MithorCapture {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);

  public static IntPtr FindMainWindow(int processId, int maxWidth, int maxHeight) {
    IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr handle, IntPtr state) {
      uint pid;
      GetWindowThreadProcessId(handle, out pid);
      if (pid != (uint)processId || !IsWindowVisible(handle)) { return true; }
      RECT rect;
      if (!GetWindowRect(handle, out rect)) { return true; }
      int width = rect.Right - rect.Left;
      int height = rect.Bottom - rect.Top;
      if (width >= 400 && width <= maxWidth && height >= 300 && height <= maxHeight) {
        found = handle;
        return false;
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@

Remove-Item 'build\data' -Recurse -Force -ErrorAction SilentlyContinue
$env:MITHOR_DATA_DIR = (Join-Path $root 'build\data')
if ($SettingsFile) {
    New-Item -ItemType Directory -Force 'build\data' | Out-Null
    Copy-Item $SettingsFile (Join-Path $root 'build\data\settings.json') -Force
}

$arguments = ''
if ($AppArgs) { $arguments = ' ' + $AppArgs }
cmd /c "start `"MITHOR`" `"$(Join-Path $root 'dist\MITHOR.exe')`"$arguments"

$process = $null
for ($i = 0; $i -lt 50; $i++) {
    Start-Sleep -Milliseconds 300
    $process = Get-Process MITHOR -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($process -and $process.MainWindowHandle -ne 0) { break }
}
if (-not $process) { throw 'MITHOR no llegó a iniciarse.' }

# Dar tiempo a que el sondeo DDC y el diseño terminen.
Start-Sleep -Milliseconds 1800
$process.Refresh()
$handle = [MithorCapture]::FindMainWindow($process.Id, 1900, 1000)
if ($handle -eq [IntPtr]::Zero) { $handle = $process.MainWindowHandle }
[void][MithorCapture]::SetForegroundWindow($handle)

$rect = New-Object MithorCapture+RECT
[void][MithorCapture]::GetWindowRect($handle, [ref]$rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

# Se mueve la ventana a una zona libre y se pone delante para poder capturarla
# con CopyFromScreen (PrintWindow no compone bien los controles WinForms).
$targetX = 60
$targetY = 60
[void][MithorCapture]::SetWindowPos($handle, [IntPtr](-1), $targetX, $targetY, $width, $height, 0x0040)
[void][MithorCapture]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds 900

$bitmap = New-Object System.Drawing.Bitmap -ArgumentList $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($targetX, $targetY, 0, 0, (New-Object System.Drawing.Size -ArgumentList $width, $height))
$graphics.Dispose()
$path = Join-Path $root $Output
$bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

Write-Host "pid=$($process.Id) rect=$($rect.Left),$($rect.Top) ${width}x${height}"
Write-Host "captura: $path ($((Get-Item $path).Length) bytes)"

if (Test-Path 'build\data\logs') {
    Get-Content 'build\data\logs\*.log' -Encoding UTF8 | Select-Object -Last 12
}

if (-not $KeepOpen) {
    [void]$process.CloseMainWindow()
    Start-Sleep -Milliseconds 800
    if (-not $process.HasExited) {
        $process.Kill()
        Start-Sleep -Milliseconds 300
    }
    Write-Host 'MITHOR cerrado.'
}
