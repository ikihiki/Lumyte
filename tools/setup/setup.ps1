$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/environment.ps1"
Assert-LumyteWindows

foreach ($directory in @($env:LUMYTE_ENV_ROOT, $env:MISE_DATA_DIR, $env:MISE_CACHE_DIR,
    $env:MISE_STATE_DIR, $env:MISE_CONFIG_DIR, $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES,
    $env:VCPKG_DOWNLOADS, $env:VCPKG_DEFAULT_BINARY_CACHE, $env:LUMYTE_NUGET_FEED)) {
    $null = New-Item -ItemType Directory -Path $directory -Force
}

function Install-LumyteMicrosoftPackage {
    param([string]$Url, [string]$Name, [string[]]$Arguments)
    $installer = Join-Path $env:LUMYTE_ENV_ROOT $Name
    Get-LumyteVerifiedDownload -Url $Url -Destination $installer
    $signature = Get-AuthenticodeSignature $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw "Microsoft installer signature verification failed: $installer"
    }
    $process = Start-Process -FilePath $installer -ArgumentList $Arguments -Wait -PassThru
    if ($process.ExitCode -eq 3010) { throw 'Installation requires a Windows restart. Restart and run setup again.' }
    if ($process.ExitCode -ne 0) { throw "$Name exited with code $($process.ExitCode)" }
    Remove-Item $installer
}

$installation = Get-LumyteVisualStudio
$sdkInclude = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/Include'
$hasSdk = @(Get-ChildItem $sdkInclude -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'um/d3d12.h') }).Count -gt 0
$hasGit = [bool](Get-Command git.exe -ErrorAction SilentlyContinue)
$hasRuntime = Test-Path (Join-Path $env:SystemRoot 'System32/vcruntime140_1.dll')
if (-not $installation -or -not $hasSdk -or -not $hasGit -or -not $hasRuntime) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Missing system dependencies. Run tools/setup/setup.ps1 in an elevated Windows PowerShell session to install Git, MSVC/Windows SDK, and the VC++ runtime.'
    }
}
if (-not $hasGit) {
    Invoke-LumyteCommand winget.exe @('install', '--id', 'Git.Git', '--exact', '--source', 'winget',
        '--silent', '--accept-package-agreements', '--accept-source-agreements')
    $gitPath = Join-Path $env:ProgramFiles 'Git/cmd'
    $env:PATH = $gitPath + ';' + $env:PATH
    $null = Get-Command git.exe -ErrorAction Stop
}
if (-not $installation -or -not $hasSdk) {
    $arguments = @('--quiet', '--norestart', '--wait', '--add',
        'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '--add',
        'Microsoft.VisualStudio.Component.Windows11SDK.26100')
    if ($installation) {
        $arguments = @('modify', '--installPath', ('"' + $installation.installationPath + '"')) + $arguments
    }
    Install-LumyteMicrosoftPackage -Url 'https://aka.ms/vs/17/release/vs_BuildTools.exe' `
        -Name 'vs_BuildTools.exe' -Arguments $arguments
}
if (-not $hasRuntime) {
    Install-LumyteMicrosoftPackage -Url 'https://aka.ms/vs/17/release/vc_redist.x64.exe' `
        -Name 'vc_redist.x64.exe' -Arguments @('/install', '/quiet', '/norestart')
}
Initialize-LumyteCompiler

$bootstrap = Get-Content "$PSScriptRoot/mise-bootstrap.json" -Raw | ConvertFrom-Json
$miseBinDir = Join-Path $env:LUMYTE_ENV_ROOT 'mise-bin'
$null = New-Item -ItemType Directory -Path $miseBinDir -Force
$miseBin = Join-Path $miseBinDir 'mise.exe'
$version = $null
if (Test-Path $miseBin) { $version = (Invoke-LumyteCommand $miseBin @('--version') | Select-Object -First 1).Split(' ')[0] }
if ($version -ne $bootstrap.version) {
    $staging = "$miseBin.download"
    Get-LumyteVerifiedDownload -Url "https://github.com/jdx/mise/releases/download/v$($bootstrap.version)/mise-v$($bootstrap.version)-windows-x64.exe" `
        -Destination $staging -Sha256 $bootstrap.platforms.'windows-x64'.sha256
    Move-Item $staging $miseBin -Force
}
$env:PATH = $miseBinDir + ';' + $env:VCPKG_ROOT + ';' + $env:PATH
Push-Location $LumyteRepoRoot
try {
    Invoke-LumyteCommand $miseBin @('trust', (Join-Path $LumyteRepoRoot 'mise.toml'))
    Invoke-LumyteCommand $miseBin @('install', '--locked')
    Invoke-LumyteCommand $miseBin @('reshim')
    Invoke-LumyteCommand $miseBin @('run', 'setup-native')
    . "$PSScriptRoot/activate.ps1" -RequireCompiler
    $sdk = (Get-Content (Join-Path $LumyteRepoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $requestedSdk = (Invoke-LumyteCommand mise @('current', 'http:dotnet') | Select-Object -Last 1).Trim()
    $actualSdk = (Invoke-LumyteCommand dotnet @('--version') | Select-Object -Last 1).Trim()
    if ($requestedSdk -ne $sdk -or $actualSdk -ne $sdk) { throw 'mise and global.json SDK versions differ.' }
    Invoke-LumyteCommand mise @('ls', '--current')
    Write-Host 'Setup complete. Dot-source tools/setup/activate.ps1, then run mise run verify.'
} finally {
    Pop-Location
}
