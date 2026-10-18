function Initialize-KeyTileEnvironment {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)
    $taskRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
    $taskSdkRoot = Join-Path $taskRoot '.tools\dotnet'
    $taskDotnet = Join-Path $taskSdkRoot 'dotnet.exe'
    $taskVersion = (Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'global.json') | ConvertFrom-Json).sdk.version
    if (-not (Test-Path -LiteralPath $taskDotnet) -or -not (Test-Path -LiteralPath (Join-Path $taskSdkRoot "sdk\$taskVersion\MSBuild.dll"))) {
        throw "Project-local .NET SDK $taskVersion is missing. Restore .tools\dotnet with the project, or follow the SDK setup instructions in README.md."
    }
    # Child apphosts and test fixtures must resolve the runtime from this same toolchain.
    $env:DOTNET_ROOT = $taskSdkRoot
    $env:DOTNET_ROOT_X64 = $taskSdkRoot
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli-home'
    $env:NUGET_PACKAGES = Join-Path $taskRoot '.tools\nuget\packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    if (($env:PATH -split ';') -notcontains $taskSdkRoot) { $env:PATH = "$taskSdkRoot;$env:PATH" }
    New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES | Out-Null
    return $taskDotnet
}

function Assert-KeyTileStopped {
    if (@(Get-Process -Name KeyTile -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'Exit KeyTile from its panel or tray menu before running tests; the tests need its four shortcuts.'
    }
}
