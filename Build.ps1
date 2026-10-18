param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\app'),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\Environment.ps1')
$taskDotnet = Initialize-KeyTileEnvironment -ProjectRoot $PSScriptRoot
$taskProject = Join-Path $PSScriptRoot 'KeyTile.csproj'
$taskOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $SkipTests) { Assert-KeyTileStopped }
foreach ($taskRunning in @(Get-Process -Name KeyTile -ErrorAction SilentlyContinue)) {
    if ($taskRunning.Path -and [System.IO.Path]::GetDirectoryName($taskRunning.Path) -eq $taskOutput) {
        throw 'Exit KeyTile from its tray menu before replacing the published app.'
    }
}
Push-Location -LiteralPath $PSScriptRoot
try {
    & $taskDotnet build $taskProject -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'Test.ps1') -Configuration $Configuration }
    & $taskDotnet publish $taskProject -c $Configuration --no-build --no-restore --no-self-contained -o $taskOutput --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $taskOutput
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VERIFICATION.md') -Destination $taskOutput
    Write-Output "KeyTile published to: $taskOutput"
    Write-Output 'Run .\Run.ps1 to launch it.'
} finally { Pop-Location }
