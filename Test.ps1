param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\Environment.ps1')
$taskDotnet = Initialize-KeyTileEnvironment -ProjectRoot $PSScriptRoot
Assert-KeyTileStopped
$taskTests = Join-Path $PSScriptRoot 'tests\KeyTile.Tests.csproj'
$taskArtifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $taskArtifacts | Out-Null
Push-Location -LiteralPath $PSScriptRoot
try {
    & $taskDotnet run --project $taskTests -c $Configuration | Tee-Object -FilePath (Join-Path $taskArtifacts 'test-results.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed. See artifacts\test-results.txt.' }
} finally { Pop-Location }
