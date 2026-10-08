$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/activate.ps1" -RequireCompiler
$architecture = Get-LumyteWindowsArchitecture
$runtimeIdentifier = "win-$architecture"
$triplet = "$architecture-windows-static"
Push-Location $LumyteRepoRoot
$previousDriverFiles = $env:VK_DRIVER_FILES
$previousIcdFiles = $env:VK_ICD_FILENAMES
$previousPath = $env:PATH
$smokeRoot = Join-Path $env:LUMYTE_ENV_ROOT ('smoke.' + [IO.Path]::GetRandomFileName())
try {
    Invoke-LumyteCommand mise @('run', 'check-native-format')
    Invoke-LumyteCommand mise @('run', 'check-markdown')
    $sdk = (Get-Content 'global.json' -Raw | ConvertFrom-Json).sdk.version
    if ((Invoke-LumyteCommand dotnet @('--version') | Select-Object -Last 1).Trim() -ne $sdk) { throw 'Incorrect .NET SDK.' }
    $cmakeVersion = (Invoke-LumyteCommand mise @('current', 'http:cmake') | Select-Object -Last 1).Trim()
    $ninjaVersion = (Invoke-LumyteCommand mise @('current', 'http:ninja') | Select-Object -Last 1).Trim()
    if ((Invoke-LumyteCommand cmake @('--version') | Select-Object -First 1).Trim() -ne "cmake version $cmakeVersion") { throw 'Incorrect CMake version.' }
    if ((Invoke-LumyteCommand ninja @('--version') | Select-Object -Last 1).Trim() -ne $ninjaVersion) { throw 'Incorrect Ninja version.' }
    # CI may install tools as administrator and verify as an ordinary user.
    $commit = (Invoke-LumyteCommand git @('-c', "safe.directory=$($env:VCPKG_ROOT)", '-C', $env:VCPKG_ROOT, 'rev-parse', 'HEAD') | Select-Object -Last 1).Trim()
    if ($commit -ne $env:LUMYTE_VCPKG_COMMIT) { throw 'Incorrect vcpkg commit.' }
    if (-not (Test-Path $env:LUMYTE_LAVAPIPE_ICD)) { throw 'lavapipe ICD is missing.' }
    $env:VK_DRIVER_FILES = $env:LUMYTE_LAVAPIPE_ICD
    $env:VK_ICD_FILENAMES = $env:LUMYTE_LAVAPIPE_ICD
    $mesaBin = Split-Path ((Get-Content $env:LUMYTE_LAVAPIPE_ICD -Raw | ConvertFrom-Json).ICD.library_path) -Parent
    $env:PATH = $mesaBin + ';' + $env:PATH
    $null = New-Item -ItemType Directory -Path $smokeRoot
    Copy-Item "$PSScriptRoot/smoke/*" $smokeRoot -Recurse
    foreach ($config in @('Directory.Build.props', '.editorconfig', 'stylecop.json')) {
        Copy-Item (Join-Path $LumyteRepoRoot $config) $smokeRoot
    }
    $shaderOutput = Join-Path $smokeRoot 'shader.spv'
    Invoke-LumyteCommand slangc @((Join-Path $smokeRoot 'shader.slang'),
        '-entry', 'main', '-stage', 'compute', '-target', 'spirv', '-o', $shaderOutput)
    $spirv = [IO.File]::ReadAllBytes($shaderOutput)
    if ($spirv.Length -le 20 -or $spirv.Length % 4 -ne 0 -or
        [BitConverter]::ToUInt32($spirv, 0) -ne 0x07230203) {
        throw 'Slang produced invalid SPIR-V.'
    }
    $build = Join-Path $smokeRoot 'build'
    Invoke-LumyteCommand cmake @('-S', $smokeRoot, '-B', $build, '-G', 'Ninja',
        "-DCMAKE_TOOLCHAIN_FILE=$($env:VCPKG_ROOT)/scripts/buildsystems/vcpkg.cmake",
        "-DVCPKG_TARGET_TRIPLET=$triplet", "-DVCPKG_HOST_TRIPLET=$triplet", '-DCMAKE_BUILD_TYPE=Release')
    Invoke-LumyteCommand cmake @('--build', $build, '--parallel', '2')
    # Bundle the vcpkg-built Vulkan loader with the temporary native NuGet.
    $loader = Join-Path $build "vcpkg_installed/$triplet/bin/vulkan-1.dll"
    if (-not (Test-Path $loader)) { throw 'vcpkg-built Vulkan loader DLL is missing.' }
    Copy-Item $loader (Join-Path $build 'vulkan-1.dll') -Force
    $version = '0.0.0-smoke.' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + '.' + $PID
    $nativeProject = Join-Path $smokeRoot 'Native/Native.csproj'
    $managedProject = Join-Path $smokeRoot 'Managed/Managed.csproj'
    # Keep feed URLs in NuGet configuration instead of Windows CLI path arguments.
    $nugetConfig = Join-Path $LumyteRepoRoot 'NuGet.config'
    Invoke-LumyteCommand dotnet @('restore', $nativeProject, '-warnaserror', '--configfile', $nugetConfig,
        "-p:RuntimeIdentifier=$runtimeIdentifier")
    Invoke-LumyteCommand dotnet @('pack', $nativeProject, '--configuration', 'Release', '--no-restore', '-warnaserror',
        '--output', $env:LUMYTE_NUGET_FEED, "-p:PackageVersion=$version", "-p:RuntimeIdentifier=$runtimeIdentifier")
    Invoke-LumyteCommand dotnet @('restore', $managedProject, '-warnaserror', '--configfile', $nugetConfig,
        "-p:SmokeVersion=$version")
    Invoke-LumyteCommand dotnet @('format', 'whitespace', $managedProject, '--verify-no-changes', '--no-restore')
    Invoke-LumyteCommand dotnet @('format', 'style', $managedProject, '--verify-no-changes', '--no-restore', '--severity', 'warn')
    Invoke-LumyteCommand dotnet @('build', $managedProject, '--configuration', 'Release', '--no-restore',
        '-warnaserror', "-p:SmokeVersion=$version", '--no-self-contained')
    Invoke-LumyteCommand dotnet @('run', '--project', $managedProject, '--configuration', 'Release',
        '--no-build', '--no-restore', "-p:SmokeVersion=$version", '--no-self-contained')
    Write-Host 'PASS: Slang SPIR-V compilation, C#, MSVC C++, vcpkg, native NuGet/PInvoke, lavapipe Vulkan readback, and Direct3D 12 WARP.'
} finally {
    [Environment]::SetEnvironmentVariable('VK_DRIVER_FILES', $previousDriverFiles, 'Process')
    [Environment]::SetEnvironmentVariable('VK_ICD_FILENAMES', $previousIcdFiles, 'Process')
    $env:PATH = $previousPath
    if (Test-Path $smokeRoot) { Remove-Item $smokeRoot -Recurse -Force }
    Pop-Location
}
