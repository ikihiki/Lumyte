param([switch]$Fix)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repoRoot
try {
    $patterns = @('*.c', '*.cc', '*.cpp', '*.cxx', '*.h', '*.hh', '*.hpp', '*.hxx', '*.inl')
    $fileList = (& git ls-files --cached --others --exclude-standard -z -- @patterns) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate C/C++ sources.' }
    $arguments = if ($Fix) { @('-i') } else { @('--dry-run', '--Werror') }
    foreach ($file in $fileList.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        & clang-format "--style=file:$repoRoot/.clang-format" @arguments -- $file
        if ($LASTEXITCODE -ne 0) { throw "clang-format failed: $file" }
    }
} finally {
    Pop-Location
}
