[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [ValidatePattern('^$|^[0-9A-Fa-f]{40}$')]
    [string]$SigningCertificateThumbprint = "",
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$dotnet = Join-Path $projectRoot ".dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

$output = Join-Path $projectRoot "artifacts\publish\$Runtime"
& $dotnet restore (Join-Path $projectRoot "NetPulseMonitor.sln") `
    --configfile (Join-Path $projectRoot "NuGet.Config") -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
& $dotnet restore (Join-Path $projectRoot "NetPulseMonitor.csproj") -r $Runtime `
    --configfile (Join-Path $projectRoot "NuGet.Config") -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "Runtime restore failed." }
& $dotnet build (Join-Path $projectRoot "NetPulseMonitor.sln") -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
& $dotnet publish (Join-Path $projectRoot "NetPulseMonitor.csproj") -c Release -r $Runtime --no-restore `
    --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
    -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$executable = Join-Path $output "NetPulseMonitor.exe"
if ($SigningCertificateThumbprint) {
    & (Join-Path $projectRoot "sign-release.ps1") `
        -Files $executable `
        -CertificateThumbprint $SigningCertificateThumbprint
    if ($LASTEXITCODE -ne 0) { throw "Executable signing failed." }
}

$packageFolder = Join-Path $projectRoot "artifacts\packages"
New-Item -ItemType Directory -Path $packageFolder -Force | Out-Null
$package = Join-Path $packageFolder "NetPulse-Monitor-$Runtime.zip"
Compress-Archive -LiteralPath $executable -DestinationPath $package -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant()
$hashFile = "$package.sha256"
Set-Content -LiteralPath $hashFile -Value "$hash  $(Split-Path $package -Leaf)" `
    -Encoding ascii

if ($BuildInstaller) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if (-not $compiler) {
        $defaultCompiler = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
        if (Test-Path -LiteralPath $defaultCompiler) {
            $compiler = Get-Item -LiteralPath $defaultCompiler
        }
    }
    if (-not $compiler) {
        throw "Inno Setup 6 was not found. Install it or omit -BuildInstaller."
    }
    $compilerPath = if ($compiler.Source) { $compiler.Source } else { $compiler.FullName }
    & $compilerPath (Join-Path $projectRoot "installer\NetPulseMonitor.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
    $installer = Get-ChildItem -LiteralPath (Join-Path $projectRoot "artifacts\installer") `
        -Filter "NetPulse-Monitor-Setup-*-win-x64.exe" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (-not $installer) { throw "The installer output was not created." }
    if ($SigningCertificateThumbprint) {
        & (Join-Path $projectRoot "sign-release.ps1") `
            -Files $installer.FullName `
            -CertificateThumbprint $SigningCertificateThumbprint
        if ($LASTEXITCODE -ne 0) { throw "Installer signing failed." }
    }
    $installerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer.FullName).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$($installer.FullName).sha256" `
        -Value "$installerHash  $($installer.Name)" -Encoding ascii
    Write-Host "Installer created at $($installer.FullName)"
}

Write-Host "Release created at $output"
Write-Host "Portable package: $package"
Write-Host "SHA-256: $hash"
