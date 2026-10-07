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
$credential = New-Object System.Management.Automation.PSCredential($account, $securePassword)
$created = $false
try {
    $null = New-LocalUser -Name $userName -Password $securePassword -AccountNeverExpires
    $created = $true
    $usersGroup = Get-LocalGroup -SID 'S-1-5-32-545'
    Add-LocalGroupMember -Group $usersGroup -Member $userName
    Start-Service seclogon
    # Inheritable permissions propagate from the checkout root. Avoid adding
    # redundant explicit ACLs recursively to every downloaded tool file.
    Invoke-LumyteCommand icacls.exe @($LumyteRepoRoot, '/grant', "${account}:(OI)(CI)M", '/Q')
    $ciRoot = Join-Path $env:LUMYTE_ENV_ROOT 'ci'
    $null = New-Item -ItemType Directory $ciRoot -Force
    $childScript = Join-Path $ciRoot 'smoke.ps1'
    $exitCodeFile = Join-Path $ciRoot ([Guid]::NewGuid().ToString('N') + '.exit-code')
    $escapedRoot = $LumyteRepoRoot.Replace("'", "''")
    $escapedExitCodeFile = $exitCodeFile.Replace("'", "''")
    @"
`$ErrorActionPreference = 'Stop'
`$exitCode = 1
try {
    Set-Location '$escapedRoot'
    . './tools/setup/activate.ps1' -RequireCompiler
    Invoke-LumyteCommand mise @('run', 'verify')
    `$exitCode = 0
} catch {
    Write-Output `$_.ToString()
} finally {
    [IO.File]::WriteAllText('$escapedExitCodeFile', `$exitCode.ToString())
}
exit `$exitCode
"@ | Set-Content $childScript -Encoding ASCII
    $stdout = Join-Path $ciRoot 'stdout.log'
    $stderr = Join-Path $ciRoot 'stderr.log'
    Write-Host '::notice title=Windows smoke test::Starting verification as an ordinary user.'
    $process = Start-Process powershell.exe -Credential $credential -LoadUserProfile `
        -WorkingDirectory $LumyteRepoRoot -ArgumentList @('-NoProfile', '-NonInteractive', '-File', ('"' + $childScript + '"')) `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    # Keep the process handle open before it exits (Windows PowerShell 5.1).
    $null = $process.Handle
    # Start-Process -Wait also waits for descendants such as MSVC's PDB server.
    # Only the verification shell's exit status determines the smoke result.
    if (-not $process.WaitForExit(600000)) {
        $process.Kill()
        throw 'Windows smoke verification exceeded 10 minutes.'
    }
    $process.Refresh()
    Get-Content $stdout
    Get-Content $stderr
    $exitCode = $process.ExitCode
    # Windows PowerShell may still return null for alternate-credential launches.
    # The child writes a unique status file only after running verification.
    if ($null -eq $exitCode) {
        if (-not (Test-Path $exitCodeFile)) { throw 'Smoke process exited without reporting its result.' }
        $exitCode = [int](Get-Content $exitCodeFile -Raw)
    }
    if ($exitCode -ne 0) { throw "Non-administrator smoke test exited with code $exitCode" }
} finally {
    if ($created) { Remove-LocalUser -Name $userName }
}
