param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\Environment.ps1')
$taskDotnet = Initialize-KeyTileEnvironment -ProjectRoot $PSScriptRoot
Assert-KeyTileStopped
$taskVersion = ([xml](Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'KeyTile.csproj'))).Project.PropertyGroup.Version
$taskRelease = Join-Path $PSScriptRoot 'artifacts\release'
$taskPortable = Join-Path $taskRelease 'portable'
$taskWix = Join-Path $PSScriptRoot '.tools\wix\wix.exe'
New-Item -ItemType Directory -Force -Path $taskPortable | Out-Null
Push-Location -LiteralPath $PSScriptRoot
try {
    if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'Test.ps1') }
    & $taskDotnet publish (Join-Path $PSScriptRoot 'KeyTile.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false -p:DebugType=embedded -p:DebugSymbols=false -o $taskPortable --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Standalone publish failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $taskPortable
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VERIFICATION.md') -Destination $taskPortable
    Copy-Item -LiteralPath (Join-Path $env:DOTNET_ROOT 'LICENSE.txt') -Destination (Join-Path $taskPortable 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $env:DOTNET_ROOT 'ThirdPartyNotices.txt') -Destination (Join-Path $taskPortable 'DOTNET-NOTICES.txt')
    if (-not (Test-Path -LiteralPath $taskWix)) {
        & $taskDotnet tool install wix --version 6.0.2 --tool-path (Split-Path -Parent $taskWix) --allow-roll-forward
        if ($LASTEXITCODE -ne 0) { throw 'Could not install the pinned WiX packaging tool.' }
    }
    $taskWixVersion = & $taskWix --version
    if ($LASTEXITCODE -ne 0 -or $taskWixVersion -notmatch '^6\.0\.2\b') { throw 'Expected WiX 6.0.2 in .tools\wix.' }
    $taskInstaller = Join-Path $taskRelease "KeyTile-$taskVersion-win-x64.msi"
    & $taskWix build (Join-Path $PSScriptRoot 'installer\Package.wxs') -arch x64 `
        -d "AppVersion=$taskVersion" -d "PublishDir=$taskPortable" -d "ProjectDir=$PSScriptRoot" `
        -intermediateFolder (Join-Path $taskRelease 'obj') -o $taskInstaller
    if ($LASTEXITCODE -ne 0) { throw 'Windows installer build failed.' }
    $taskZip = Join-Path $taskRelease "KeyTile-$taskVersion-win-x64-portable.zip"
    $taskPackageFiles = @('KeyTile.exe', 'README.md', 'VERIFICATION.md', 'DOTNET-LICENSE.txt', 'DOTNET-NOTICES.txt') |
        ForEach-Object { Join-Path $taskPortable $_ }
    Compress-Archive -LiteralPath $taskPackageFiles -DestinationPath $taskZip -Force
    Get-FileHash -LiteralPath $taskInstaller, $taskZip -Algorithm SHA256 |
        Select-Object @{Name='File';Expression={Split-Path -Leaf $_.Path}}, Hash |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRelease 'SHA256.json')
    Write-Output "Installer: $taskInstaller"
    Write-Output "Portable executable: $(Join-Path $taskPortable 'KeyTile.exe')"
    Write-Output "Portable archive: $taskZip"
} finally { Pop-Location }
