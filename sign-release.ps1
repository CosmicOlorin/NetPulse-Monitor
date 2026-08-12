[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$CertificateThumbprint,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitsRoot) {
        $candidate = Get-ChildItem -LiteralPath $kitsRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }
    throw "signtool.exe was not found. Install the Windows 10/11 SDK signing tools."
}

$signTool = Find-SignTool
$thumbprint = $CertificateThumbprint.ToUpperInvariant()
foreach ($file in $Files) {
    $resolved = (Resolve-Path -LiteralPath $file -ErrorAction Stop).Path
    & $signTool sign /sha1 $thumbprint /fd SHA256 /tr $TimestampServer /td SHA256 $resolved
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $resolved" }
    & $signTool verify /pa /v $resolved
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $resolved" }
    Write-Host "Signed and verified: $resolved"
}
