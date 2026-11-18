$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/activate.ps1" -RequireCompiler
Push-Location $LumyteRepoRoot
try {
    Invoke-LumyteCommand dotnet @('run', '--project', 'build', '-c', 'Release', '--', '--target', 'Verify', '--verify-scope', 'environment')
} finally {
    Pop-Location
}
