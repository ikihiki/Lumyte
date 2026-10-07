# Dot-source this file in each new PowerShell session.
param([switch]$RequireCompiler)
. "$PSScriptRoot/environment.ps1"
Assert-LumyteWindows
$miseBin = Join-Path $env:LUMYTE_ENV_ROOT 'mise-bin/mise.exe'
if (-not (Test-Path $miseBin)) { throw 'Run tools/setup/setup.ps1 before activation.' }
if ($RequireCompiler -or (Get-LumyteVisualStudio)) { Initialize-LumyteCompiler }
# mise's tools precede any CMake/Ninja bundled with Visual Studio.
$env:PATH = (Split-Path $miseBin -Parent) + ';' + $env:VCPKG_ROOT + ';' + $env:PATH
Push-Location $LumyteRepoRoot
try {
    $miseEnv = Invoke-LumyteCommand $miseBin @('env', '--shell', 'pwsh')
    Invoke-Expression ($miseEnv -join "`n")
    $env:DOTNET_ROOT = (Invoke-LumyteCommand $miseBin @('where', 'http:dotnet') | Select-Object -Last 1).Trim()
} finally {
    Pop-Location
}
