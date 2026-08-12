[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = "Stop"
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$generatedPaths = @(
    "bin",
    "obj",
    "artifacts",
    ".dotnet",
    ".dotnet-home",
    ".packages",
    "tests\NetPulseMonitor.ProtocolTests\bin",
    "tests\NetPulseMonitor.ProtocolTests\obj"
)

$removed = 0
foreach ($relativePath in $generatedPaths) {
    $target = [IO.Path]::GetFullPath((Join-Path $projectRoot $relativePath))
    if (-not $target.StartsWith(
            $projectRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the project: $target"
    }

    if ((Test-Path -LiteralPath $target) -and
        $PSCmdlet.ShouldProcess($target, "Remove generated build files")) {
        Remove-Item -LiteralPath $target -Recurse -Force
        $removed++
    }
}

Write-Host "Workspace cleanup completed. Removed $removed generated folders."
Write-Host "Source files, .git history, settings, releases and user logs were not touched."
