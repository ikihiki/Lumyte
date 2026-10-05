# Dot-source before invoking mise. Project tool versions remain in mise.toml.
$LumyteRepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $env:LUMYTE_ENV_ROOT) {
    $env:LUMYTE_ENV_ROOT = Join-Path $LumyteRepoRoot 'artifacts/dev-env'
}
$env:LUMYTE_ENV_ROOT = [IO.Path]::GetFullPath($env:LUMYTE_ENV_ROOT)
$directories = @{
    MISE_DATA_DIR = 'mise/data'; MISE_CACHE_DIR = 'mise/cache'
    MISE_STATE_DIR = 'mise/state'; MISE_CONFIG_DIR = 'mise/config'
    DOTNET_CLI_HOME = 'dotnet-home'; NUGET_PACKAGES = 'nuget-packages'
    VCPKG_ROOT = 'vcpkg'; VCPKG_DOWNLOADS = 'vcpkg-downloads'
    VCPKG_DEFAULT_BINARY_CACHE = 'vcpkg-binary-cache'
    MESA_SHADER_CACHE_DIR = 'mesa-cache'; LUMYTE_LAVAPIPE_ICD = 'lavapipe.json'
}
foreach ($entry in $directories.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($entry.Key, (Join-Path $env:LUMYTE_ENV_ROOT $entry.Value), 'Process')
}
$env:MISE_DISABLE_UPDATE_WARNING = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:VCPKG_DISABLE_METRICS = '1'
if (-not $env:VCPKG_MAX_CONCURRENCY) { $env:VCPKG_MAX_CONCURRENCY = '2' }
$env:LUMYTE_NUGET_FEED = Join-Path $LumyteRepoRoot 'artifacts/nuget'

function Assert-LumyteWindows {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
        -not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
        throw 'This setup requires a 64-bit PowerShell session on Windows x64.'
    }
}

function Invoke-LumyteCommand {
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$Arguments = @())
    $null = Get-Command $FilePath -ErrorAction Stop
    # Native tools often report progress to stderr. Preserve their exit status
    # without treating ordinary stderr output as a PowerShell exception.
    $ErrorActionPreference = 'Continue'
    $PSNativeCommandUseErrorActionPreference = $false
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$FilePath exited with code $LASTEXITCODE" }
}

function Get-LumyteVerifiedDownload {
    param([string]$Url, [string]$Destination, [string]$Sha256)
    if (-not $Url.StartsWith('https://')) { throw 'Downloads require HTTPS.' }
    $protocol = [Net.ServicePointManager]::SecurityProtocol
    try {
        [Net.ServicePointManager]::SecurityProtocol = $protocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing -ErrorAction Stop
    } finally {
        [Net.ServicePointManager]::SecurityProtocol = $protocol
    }
    if ($Sha256 -and (Get-FileHash $Destination -Algorithm SHA256).Hash -ne $Sha256) {
        throw "SHA-256 verification failed: $Destination"
    }
}

function Get-LumyteVisualStudio {
    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $null }
    $data = Invoke-LumyteCommand $vswhere @('-latest', '-products', '*', '-requires',
        'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-format', 'json')
    return ($data -join "`n" | ConvertFrom-Json | Select-Object -First 1)
}

function Initialize-LumyteCompiler {
    $installation = Get-LumyteVisualStudio
    if (-not $installation) { throw 'MSVC C++ Build Tools are missing. Run tools/setup/setup.ps1.' }
    $module = Join-Path $installation.installationPath 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll'
    Import-Module $module -ErrorAction Stop
    Enter-VsDevShell -VsInstallPath $installation.installationPath -SkipAutomaticLocation `
        -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
    $null = Get-Command cl.exe -ErrorAction Stop
    if (-not $env:WindowsSdkDir -or -not $env:WindowsSDKVersion) { throw 'Windows SDK is missing.' }
    $header = Join-Path $env:WindowsSdkDir ('Include/' + $env:WindowsSDKVersion.TrimEnd('\') + '/um/d3d12.h')
    if (-not (Test-Path $header)) { throw "Windows SDK Direct3D headers are missing: $header" }
}
