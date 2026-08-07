[CmdletBinding()]
param([string]$Runtime = "win-x64")

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$dotnet = Join-Path $projectRoot ".dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

$output = Join-Path $projectRoot "artifacts\publish\$Runtime"
& $dotnet restore (Join-Path $projectRoot "NetPulseMonitor.sln") --configfile (Join-Path $projectRoot "NuGet.Config")
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
& $dotnet build (Join-Path $projectRoot "NetPulseMonitor.sln") -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
& $dotnet publish (Join-Path $projectRoot "NetPulseMonitor.csproj") -c Release -r $Runtime `
    --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
    -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "Release created at $output"
