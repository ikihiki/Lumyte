# GitHub-hosted Windows runners run elevated; Vulkan ignores ICD overrides there.
# Install system dependencies first, then verify as an ordinary temporary user.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This helper is only for GitHub Actions runners.' }
. "$PSScriptRoot/environment.ps1"
Assert-LumyteWindows
$userName = 'lumyte-smoke'
$password = 'Aa!' + [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
Write-Host "::add-mask::$password"
$securePassword = ConvertTo-SecureString $password -AsPlainText -Force
$account = "$env:COMPUTERNAME\$userName"
$credential = New-Object Management.Automation.PSCredential($account, $securePassword)
$created = $false
try {
    $null = New-LocalUser -Name $userName -Password $securePassword -AccountNeverExpires
    $created = $true
    Start-Service seclogon
    Invoke-LumyteCommand icacls.exe @($LumyteRepoRoot, '/grant', "${account}:(OI)(CI)M", '/T', '/Q')
    $ciRoot = Join-Path $env:LUMYTE_ENV_ROOT 'ci'
    $null = New-Item -ItemType Directory $ciRoot -Force
    $childScript = Join-Path $ciRoot 'smoke.ps1'
    $escapedRoot = $LumyteRepoRoot.Replace("'", "''")
    @"
`$ErrorActionPreference = 'Stop'
Set-Location '$escapedRoot'
. './tools/setup/activate.ps1' -RequireCompiler
Invoke-LumyteCommand mise @('run', 'verify')
"@ | Set-Content $childScript -Encoding ASCII
    $stdout = Join-Path $ciRoot 'stdout.log'
    $stderr = Join-Path $ciRoot 'stderr.log'
    $process = Start-Process powershell.exe -Credential $credential -LoadUserProfile `
        -WorkingDirectory $LumyteRepoRoot -ArgumentList @('-NoProfile', '-NonInteractive', '-File', ('"' + $childScript + '"')) `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -Wait -PassThru
    Get-Content $stdout
    Get-Content $stderr
    if ($process.ExitCode -ne 0) { throw "Non-administrator smoke test exited with code $($process.ExitCode)" }
} finally {
    if ($created) { Remove-LocalUser -Name $userName }
}
