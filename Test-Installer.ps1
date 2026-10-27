param([string]$Installer)
$ErrorActionPreference = 'Stop'
if (-not $Installer) {
    $taskVersion = ([xml](Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'KeyTile.csproj'))).Project.PropertyGroup.Version
    $Installer = Join-Path $PSScriptRoot "artifacts\release\KeyTile-$taskVersion-win-x64.msi"
}
$taskInstaller = (Resolve-Path -LiteralPath $Installer).Path
$taskArtifacts = Join-Path $PSScriptRoot 'artifacts\installer-smoke'
$taskInstall = Join-Path $taskArtifacts ('app test-' + [Guid]::NewGuid().ToString('N'))
$taskRunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$taskStartupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'KeyTile.lnk'
$taskDesktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'KeyTile.lnk'
$taskStartMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'KeyTile\KeyTile.lnk'
if (@(Get-Process KeyTile -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Exit KeyTile before testing its installer.' }
if ((Get-ItemProperty -LiteralPath 'HKCU:\Software\KeyTile' -Name Installed -ErrorAction SilentlyContinue) `
    -or (Get-ItemProperty -LiteralPath $taskRunKey -Name KeyTile -ErrorAction SilentlyContinue) `
    -or (Test-Path -LiteralPath $taskStartupLink) -or (Test-Path -LiteralPath $taskDesktop) -or (Test-Path -LiteralPath $taskStartMenu)) {
    throw 'Installer smoke tests require no existing KeyTile installation, startup registration, or shortcuts.'
}
if (Test-Path -LiteralPath $taskInstall) { throw "The smoke-test install folder already exists: $taskInstall" }
New-Item -ItemType Directory -Force -Path $taskArtifacts | Out-Null

function Invoke-KeyTileMsi {
    param([string]$Mode, [string]$Log, [string[]]$Properties = @())
    $taskArguments = @($Mode, ('"' + $taskInstaller + '"'), '/qn', '/norestart', '/l*v', ('"' + $Log + '"')) + $Properties
    $taskMsiProcess = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList $taskArguments -PassThru -Wait -WindowStyle Hidden
    $taskMsiProcess.WaitForExit()
    if ($taskMsiProcess.ExitCode -ne 0) { throw "Windows Installer failed with $($taskMsiProcess.ExitCode). See $Log" }
}
function Assert-InstallerCheck {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    Write-Output "PASS $Message"
}
$taskInstalled = $false
$taskApp = $null
try {
    Invoke-KeyTileMsi '/i' (Join-Path $taskArtifacts 'install.log') @('INSTALLFOLDER="' + $taskInstall + '"')
    $taskInstalled = $true
    $taskExecutable = Join-Path $taskInstall 'KeyTile.exe'
    Assert-InstallerCheck (Test-Path -LiteralPath $taskExecutable) 'Installer deploys the standalone executable'
    Assert-InstallerCheck ((Test-Path -LiteralPath $taskDesktop) -and (Test-Path -LiteralPath $taskStartMenu)) 'Installer creates desktop and Start menu shortcuts'
    $taskShell = New-Object -ComObject WScript.Shell
    $taskStartup = $taskShell.CreateShortcut($taskStartupLink)
    Assert-InstallerCheck ($taskStartup.TargetPath -eq $taskExecutable -and $taskStartup.Arguments -eq '--tray') 'Sign-in shortcut points to the installed executable in quiet tray mode'
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskStartup)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskShell)
    $taskLaunch = [System.Diagnostics.ProcessStartInfo]::new($taskExecutable, '--tray')
    $taskLaunch.UseShellExecute = $false
    $taskLaunch.CreateNoWindow = $true
    foreach ($taskName in @($taskLaunch.EnvironmentVariables.Keys | Where-Object { $_ -like 'DOTNET_*' })) { $taskLaunch.EnvironmentVariables.Remove($taskName) }
    $taskLaunch.EnvironmentVariables['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
    $taskApp = [System.Diagnostics.Process]::Start($taskLaunch)
    [void]$taskApp.WaitForInputIdle(15000)
    Assert-InstallerCheck (-not $taskApp.HasExited) 'Installed app runs without project runtime environment variables'
    $taskRuntimeTimer = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $taskApp.Refresh()
        $taskModules = @($taskApp.Modules)
        $taskBundledRuntime = @($taskModules | Where-Object ModuleName -eq 'wpfgfx_cor3.dll')
        if ($taskBundledRuntime.Count -eq 1 -or $taskApp.HasExited) { break }
        Start-Sleep -Milliseconds 100
    } while ($taskRuntimeTimer.ElapsedMilliseconds -lt 15000)
    $taskRuntimePaths = @($taskModules | Where-Object { $_.FileName -like '*\.net\KeyTile\*' } | ForEach-Object FileName)
    $taskRuntimePaths | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskArtifacts 'runtime-paths.json')
    Assert-InstallerCheck ($taskBundledRuntime.Count -eq 1 -and $taskBundledRuntime[0].FileName -like '*\.net\KeyTile\*') 'Installed app loads its bundled WPF runtime components'
    Assert-InstallerCheck (@($taskModules | Where-Object { $_.FileName -like '*\.tools\dotnet\*' -or $_.FileName -like '*\Program Files\dotnet\*' -or $_.FileName -like '*\Program Files (x86)\dotnet\*' }).Count -eq 0) 'Installed app does not load a project or machine-wide .NET runtime'
    $taskApp.Refresh()
    Assert-InstallerCheck ($taskApp.MainWindowHandle -eq 0) 'Sign-in launch keeps the panel hidden'
    $taskDuplicate = [System.Diagnostics.Process]::Start($taskLaunch)
    try { Assert-InstallerCheck ($taskDuplicate.WaitForExit(10000) -and $taskDuplicate.ExitCode -eq 0) 'A second tray launch exits cleanly' }
    finally { $taskDuplicate.Dispose() }
    $taskApp.Refresh()
    Assert-InstallerCheck ($taskApp.MainWindowHandle -eq 0) 'A second tray launch does not open the panel'
    $taskApp.Kill()
    $taskApp.WaitForExit()
    $taskApp.Dispose()
    $taskApp = $null
    Remove-Item -LiteralPath $taskStartupLink
    Invoke-KeyTileMsi '/fvomus' (Join-Path $taskArtifacts 'repair-disabled.log')
    Assert-InstallerCheck (-not (Test-Path -LiteralPath $taskStartupLink)) 'Repair preserves a disabled startup preference'
    $taskShell = New-Object -ComObject WScript.Shell
    $taskStartup = $taskShell.CreateShortcut($taskStartupLink)
    $taskStartup.TargetPath = $taskExecutable
    $taskStartup.Arguments = '--tray'
    $taskStartup.Save()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskStartup)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskShell)
    Invoke-KeyTileMsi '/fvomus' (Join-Path $taskArtifacts 'repair-enabled.log')
    Assert-InstallerCheck (Test-Path -LiteralPath $taskStartupLink) 'Repair honors an enabled startup preference'
    Invoke-KeyTileMsi '/x' (Join-Path $taskArtifacts 'uninstall.log')
    $taskInstalled = $false
    Assert-InstallerCheck (-not (Test-Path -LiteralPath $taskExecutable)) 'Uninstall removes the installed executable'
    Assert-InstallerCheck (-not (Test-Path -LiteralPath $taskDesktop) -and -not (Test-Path -LiteralPath $taskStartMenu)) 'Uninstall removes both shortcuts'
    Assert-InstallerCheck (-not (Test-Path -LiteralPath $taskStartupLink)) 'Uninstall removes sign-in startup registration'
    Assert-InstallerCheck (-not (Get-ItemProperty -LiteralPath 'HKCU:\Software\KeyTile' -Name Installed -ErrorAction SilentlyContinue)) 'Uninstall removes installation registration'
    Invoke-KeyTileMsi '/i' (Join-Path $taskArtifacts 'install-opt-out.log') @(('INSTALLFOLDER="' + $taskInstall + '"'), 'STARTUPENABLED=0')
    $taskInstalled = $true
    Assert-InstallerCheck ((Test-Path -LiteralPath $taskExecutable) -and -not (Test-Path -LiteralPath $taskStartupLink)) 'Installer supports opting out of startup'
    Invoke-KeyTileMsi '/fvomus' (Join-Path $taskArtifacts 'repair-opt-out.log')
    Assert-InstallerCheck (-not (Test-Path -LiteralPath $taskStartupLink)) 'Repair preserves the initial startup opt-out'
    $taskShell = New-Object -ComObject WScript.Shell
    $taskStartup = $taskShell.CreateShortcut($taskStartupLink)
    $taskStartup.TargetPath = $taskExecutable
    $taskStartup.Arguments = '--tray'
    $taskStartup.Save()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskStartup)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskShell)
    Invoke-KeyTileMsi '/x' (Join-Path $taskArtifacts 'uninstall-opt-out.log')
    $taskInstalled = $false
    Assert-InstallerCheck (-not (Test-Path -LiteralPath $taskExecutable) -and -not (Test-Path -LiteralPath $taskDesktop) -and -not (Test-Path -LiteralPath $taskStartMenu)) 'Uninstall also cleans an installation with startup disabled'
    Assert-InstallerCheck (-not (Test-Path -LiteralPath $taskStartupLink)) 'Uninstall removes startup enabled after opting out during setup'
} finally {
    if ($taskApp) { if (-not $taskApp.HasExited) { $taskApp.Kill(); $taskApp.WaitForExit() }; $taskApp.Dispose() }
    if ($taskInstalled) { Invoke-KeyTileMsi '/x' (Join-Path $taskArtifacts 'cleanup.log') }
}
