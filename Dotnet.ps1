# Pass ordinary dotnet commands through the project's local SDK, e.g. .\Dotnet.ps1 --info.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\Environment.ps1')
$taskDotnet = Initialize-KeyTileEnvironment -ProjectRoot $PSScriptRoot
Push-Location -LiteralPath $PSScriptRoot
try {
    & $taskDotnet @args
    $taskExitCode = $LASTEXITCODE
} finally { Pop-Location }
exit $taskExitCode
