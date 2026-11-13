$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Get-ChildItem "C:\Windows\Microsoft.NET\Framework64\v4.0*\csc.exe" | Select-Object -First 1 -ExpandProperty FullName

if (-not $csc) {
    throw "Could not find csc.exe in .NET Framework v4.0"
}

$outExe = Join-Path $dir "MonitorFader.exe"
$sources = @(
    (Join-Path $dir "Program.cs"),
    (Join-Path $dir "AppSettings.cs"),
    (Join-Path $dir "NativeMethods.cs"),
    (Join-Path $dir "MonitorManager.cs"),
    (Join-Path $dir "WindowHelper.cs"),
    (Join-Path $dir "HotkeyHook.cs"),
    (Join-Path $dir "OverlayManager.cs"),
    (Join-Path $dir "TitleBarButtonManager.cs"),
    (Join-Path $dir "TaskbarManager.cs"),
    (Join-Path $dir "Controls.cs"),
    (Join-Path $dir "SettingsForm.cs")
)

& $csc /nologo /target:winexe /optimize+ /platform:anycpu `
    /r:System.dll `
    /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll `
    /r:System.Runtime.Serialization.dll `
    /out:$outExe `
    @sources

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Built successfully: $outExe"
