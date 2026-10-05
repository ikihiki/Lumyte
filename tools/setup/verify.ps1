$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/activate.ps1" -RequireCompiler
Push-Location $LumyteRepoRoot
$previousDriverFiles = $env:VK_DRIVER_FILES
$previousIcdFiles = $env:VK_ICD_FILENAMES
$previousPath = $env:PATH
$smokeRoot = Join-Path $env:LUMYTE_ENV_ROOT ('smoke.' + [IO.Path]::GetRandomFileName())
try {
    $sdk = (Get-Content 'global.json' -Raw | ConvertFrom-Json).sdk.version
    if ((Invoke-LumyteCommand dotnet @('--version') | Select-Object -Last 1).Trim() -ne $sdk) { throw 'Incorrect .NET SDK.' }
    $cmakeVersion = (Invoke-LumyteCommand mise @('current', 'http:cmake') | Select-Object -Last 1).Trim()
    $ninjaVersion = (Invoke-LumyteCommand mise @('current', 'http:ninja') | Select-Object -Last 1).Trim()
    if ((Invoke-LumyteCommand cmake @('--version') | Select-Object -First 1).Trim() -ne "cmake version $cmakeVersion") { throw 'Incorrect CMake version.' }
    if ((Invoke-LumyteCommand ninja @('--version') | Select-Object -Last 1).Trim() -ne $ninjaVersion) { throw 'Incorrect Ninja version.' }
    $commit = (Invoke-LumyteCommand git @('-C', $env:VCPKG_ROOT, 'rev-parse', 'HEAD') | Select-Object -Last 1).Trim()
    if ($commit -ne $env:LUMYTE_VCPKG_COMMIT) { throw 'Incorrect vcpkg commit.' }
    if (-not (Test-Path $env:LUMYTE_LAVAPIPE_ICD)) { throw 'lavapipe ICD is missing.' }
    $env:VK_DRIVER_FILES = $env:LUMYTE_LAVAPIPE_ICD
    $env:VK_ICD_FILENAMES = $env:LUMYTE_LAVAPIPE_ICD
    $mesaBin = Join-Path $env:LUMYTE_ENV_ROOT "mesa-$($env:LUMYTE_WINDOWS_MESA_VERSION)/x64"
    $env:PATH = $mesaBin + ';' + $env:PATH
    $null = New-Item -ItemType Directory -Path $smokeRoot
    Copy-Item "$PSScriptRoot/smoke/*" $smokeRoot -Recurse
    $build = Join-Path $smokeRoot 'build'
    Invoke-LumyteCommand cmake @('-S', $smokeRoot, '-B', $build, '-G', 'Ninja',
        "-DCMAKE_TOOLCHAIN_FILE=$($env:VCPKG_ROOT)/scripts/buildsystems/vcpkg.cmake",
        '-DVCPKG_TARGET_TRIPLET=x64-windows-static', '-DCMAKE_BUILD_TYPE=Release')
    Invoke-LumyteCommand cmake @('--build', $build, '--parallel', '2')
    # Bundle the vcpkg-built Vulkan loader with the temporary native NuGet.
    $loader = Join-Path $build 'vcpkg_installed/x64-windows-static/bin/vulkan-1.dll'
    if (-not (Test-Path $loader)) { throw 'vcpkg-built Vulkan loader DLL is missing.' }
    Copy-Item $loader (Join-Path $build 'vulkan-1.dll') -Force
    $version = '0.0.0-smoke.' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + '.' + $PID
    $nativeProject = Join-Path $smokeRoot 'Native/Native.csproj'
    $managedProject = Join-Path $smokeRoot 'Managed/Managed.csproj'
    Invoke-LumyteCommand dotnet @('pack', $nativeProject, '--configuration', 'Release',
        '--output', $env:LUMYTE_NUGET_FEED, "-p:PackageVersion=$version", '-p:RuntimeIdentifier=win-x64',
        "-p:RestoreSources=$($env:LUMYTE_NUGET_FEED)")
    Invoke-LumyteCommand dotnet @('restore', $managedProject, '--source', $env:LUMYTE_NUGET_FEED, "-p:SmokeVersion=$version")
    Invoke-LumyteCommand dotnet @('run', '--project', $managedProject, '--configuration', 'Release',
        '--no-restore', "-p:SmokeVersion=$version", '--no-self-contained')
    Write-Host 'PASS: C#, MSVC C++, vcpkg, native NuGet/PInvoke, lavapipe Vulkan readback, and Direct3D 12 WARP.'
} finally {
    [Environment]::SetEnvironmentVariable('VK_DRIVER_FILES', $previousDriverFiles, 'Process')
    [Environment]::SetEnvironmentVariable('VK_ICD_FILENAMES', $previousIcdFiles, 'Process')
    $env:PATH = $previousPath
    if (Test-Path $smokeRoot) { Remove-Item $smokeRoot -Recurse -Force }
    Pop-Location
}
