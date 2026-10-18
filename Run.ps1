param([switch]$Tray)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\Environment.ps1')
Initialize-KeyTileEnvironment -ProjectRoot $PSScriptRoot | Out-Null
$taskExecutable = Join-Path $PSScriptRoot 'artifacts\app\KeyTile.exe'
if (-not (Test-Path -LiteralPath $taskExecutable)) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -SkipTests
}
$taskLaunch = @{
    FilePath = $taskExecutable
    WorkingDirectory = $PSScriptRoot
    PassThru = $true
    WindowStyle = 'Normal'
}
if ($Tray) { $taskLaunch.ArgumentList = '--tray'; $taskLaunch.WindowStyle = 'Hidden' }
$taskProcess = Start-Process @taskLaunch
Write-Output "KeyTile launched from $taskExecutable (process $($taskProcess.Id))."
