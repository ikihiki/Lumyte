$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/environment.ps1"
Assert-LumyteWindows
$architecture = Get-LumyteWindowsArchitecture
$mesaHash = if ($architecture -eq 'arm64') {
    $env:LUMYTE_WINDOWS_MESA_ARM64_SHA256
} else { $env:LUMYTE_WINDOWS_MESA_SHA256 }
if (-not $env:LUMYTE_VCPKG_COMMIT -or -not $env:LUMYTE_WINDOWS_MESA_VERSION -or -not $mesaHash) {
    throw 'Run this script through mise run setup-native.'
}
foreach ($directory in @($env:VCPKG_DOWNLOADS, $env:VCPKG_DEFAULT_BINARY_CACHE)) {
    $null = New-Item -ItemType Directory -Path $directory -Force
}
if (-not (Test-Path (Join-Path $env:VCPKG_ROOT '.git'))) {
    if (Test-Path $env:VCPKG_ROOT) { throw "Refusing to overwrite $env:VCPKG_ROOT" }
    Invoke-LumyteCommand git @('init', $env:VCPKG_ROOT)
    Invoke-LumyteCommand git @('-C', $env:VCPKG_ROOT, 'remote', 'add', 'origin', 'https://github.com/microsoft/vcpkg.git')
}
# Fetch only when the pinned object is absent; failed cat-file is expected here.
$ErrorActionPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $false
& git -C $env:VCPKG_ROOT cat-file -e "$($env:LUMYTE_VCPKG_COMMIT)^{commit}" 2>$null
$hasCommit = $LASTEXITCODE -eq 0
$ErrorActionPreference = 'Stop'
if (-not $hasCommit) {
    Invoke-LumyteCommand git @('-C', $env:VCPKG_ROOT, 'fetch', '--depth', '1', 'origin', $env:LUMYTE_VCPKG_COMMIT)
}
$changes = @(Invoke-LumyteCommand git @('-C', $env:VCPKG_ROOT, 'status', '--porcelain'))
if ($changes.Count) { throw 'vcpkg checkout has local changes.' }
Invoke-LumyteCommand git @('-C', $env:VCPKG_ROOT, 'checkout', '--detach', $env:LUMYTE_VCPKG_COMMIT)
Invoke-LumyteCommand (Join-Path $env:VCPKG_ROOT 'bootstrap-vcpkg.bat') @('-disableMetrics')
$vcpkg = Join-Path $env:VCPKG_ROOT 'vcpkg.exe'

$mesaRoot = Join-Path $env:LUMYTE_ENV_ROOT "mesa-$($env:LUMYTE_WINDOWS_MESA_VERSION)"
if ($architecture -eq 'arm64') {
    $mesaRoot += '-arm64'
    $mesaSubdirectory = '.'
    $mesaUrl = "https://github.com/mmozeiko/build-mesa/releases/download/$($env:LUMYTE_WINDOWS_MESA_VERSION)/mesa-lavapipe-arm64-$($env:LUMYTE_WINDOWS_MESA_VERSION).7z"
} else {
    $mesaSubdirectory = 'x64'
    $mesaUrl = "https://github.com/pal1000/mesa-dist-win/releases/download/$($env:LUMYTE_WINDOWS_MESA_VERSION)/mesa3d-$($env:LUMYTE_WINDOWS_MESA_VERSION)-release-msvc.7z"
}
if (-not (Test-Path $mesaRoot)) {
    $archive = "$mesaRoot.7z"
    Get-LumyteVerifiedDownload -Url $mesaUrl -Destination $archive -Sha256 $mesaHash
    # vcpkg's pinned tool metadata supplies the verified 7zip executable.
    $sevenZip = (Invoke-LumyteCommand $vcpkg @('fetch', '7zip') | Select-Object -Last 1).Trim().Trim('"')
    if (-not (Test-Path $sevenZip)) { throw 'vcpkg did not return a usable 7zip executable.' }
    $staging = Join-Path $env:LUMYTE_ENV_ROOT ([IO.Path]::GetRandomFileName())
    try {
        Invoke-LumyteCommand $sevenZip @('x', $archive, "-o$staging", '-y')
        $driver = Join-Path (Join-Path $staging $mesaSubdirectory) 'vulkan_lvp.dll'
        if (-not (Test-Path $driver)) { throw "Mesa archive lacks the $architecture lavapipe driver." }
        Set-Content -Path (Join-Path $staging '.lumyte-sha256') -Value $mesaHash -Encoding ASCII
        Move-Item $staging $mesaRoot
        Remove-Item $archive
    } finally {
        if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    }
}
$marker = Join-Path $mesaRoot '.lumyte-sha256'
if (-not (Test-Path $marker) -or (Get-Content $marker -Raw).Trim() -ne $mesaHash) {
    throw "Unverified Mesa installation at $mesaRoot. Preserve it and choose a clean LUMYTE_ENV_ROOT."
}
$mesaBin = Join-Path $mesaRoot $mesaSubdirectory
$icdFiles = @(Get-ChildItem $mesaBin -Filter 'lvp_icd*.json')
if ($icdFiles.Count -ne 1) { throw 'Expected exactly one lavapipe ICD manifest.' }
$icd = Get-Content $icdFiles[0].FullName -Raw | ConvertFrom-Json
$icd.ICD.library_path = [IO.Path]::GetFullPath((Join-Path $mesaBin 'vulkan_lvp.dll'))
$json = $icd | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($env:LUMYTE_LAVAPIPE_ICD, $json, (New-Object Text.UTF8Encoding($false)))
Invoke-LumyteCommand $vcpkg @('version')
