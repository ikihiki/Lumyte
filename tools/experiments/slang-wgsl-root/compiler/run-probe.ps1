# Requires PowerShell 7. Compiler acceptance and emitted text are observations;
# they are not a substitute for target-language validation or GPU execution.
param(
    [string] $CompilerPath = (Join-Path $PSScriptRoot '..\..\..\..\.packages\slang\2026.7.1\bin\slangc.exe'),
    [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..')).Path
$version = (& $CompilerPath -version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'The compiler did not report its version successfully.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $versionPath = $version -replace '[^a-zA-Z0-9._-]', '_'
    $OutputDirectory = Join-Path $repository ('artifacts\experiments\slang-wgsl-root\compiler\results-' + $versionPath)
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
# Every invocation has fresh paths. Failed compilations cannot reuse old files.
$runDirectory = Join-Path $OutputDirectory ('run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$version | Set-Content -LiteralPath (Join-Path $runDirectory 'version.txt') -Encoding utf8
$records = @()
$targets = @(
    @{ Name = 'wgsl'; Extension = 'wgsl'; Options = @() },
    @{ Name = 'spirv-asm'; Extension = 'spvasm'; Options = @('-profile', 'sm_6_6', '-emit-spirv-directly') },
    @{ Name = 'hlsl'; Extension = 'hlsl'; Options = @('-profile', 'sm_6_6') }
)
$fixturesDirectory = Join-Path $PSScriptRoot 'fixtures'
foreach ($fixture in Get-ChildItem -LiteralPath $fixturesDirectory -Filter '*.slang' | Where-Object BaseName -ne 'shared_math' | Sort-Object Name) {
    foreach ($target in $targets) {
        $stem = $fixture.BaseName + '.' + $target.Name
        $artifact = Join-Path $runDirectory ($stem + '.' + $target.Extension)
        $reflection = Join-Path $runDirectory ($stem + '.reflection.json')
        $arguments = @($fixture.FullName, '-entry', 'main', '-stage', 'compute', '-target', $target.Name, '-I', $fixturesDirectory, '-matrix-layout-column-major', '-reflection-json', $reflection, '-o', $artifact) + $target.Options
        $diagnostic = (& $CompilerPath @arguments 2>&1 | Out-String)
        $exitCode = $LASTEXITCODE
        $diagnostic | Set-Content -LiteralPath (Join-Path $runDirectory ($stem + '.log')) -Encoding utf8
        $artifactAvailable = $exitCode -eq 0 -and (Test-Path -LiteralPath $artifact)
        $reflectionAvailable = $exitCode -eq 0 -and (Test-Path -LiteralPath $reflection)
        $emitted = if ($artifactAvailable) { Get-Content -LiteralPath $artifact -Raw } else { '' }
        $records += [pscustomobject]@{
            Fixture = $fixture.BaseName
            Target = $target.Name
            ExitCode = $exitCode
            Compiler = $CompilerPath
            Arguments = $arguments
            FixtureSha256 = (Get-FileHash -LiteralPath $fixture.FullName -Algorithm SHA256).Hash
            Artifact = if ($artifactAvailable) { $artifact } else { $null }
            ArtifactSha256 = if ($artifactAvailable) { (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash } else { $null }
            Reflection = if ($reflectionAvailable) { $reflection } else { $null }
            ReflectionSha256 = if ($reflectionAvailable) { (Get-FileHash -LiteralPath $reflection -Algorithm SHA256).Hash } else { $null }
            HasImmediate = [bool]($emitted -match 'var\s*<\s*immediate\s*>')
            HasUniform = [bool]($emitted -match 'var\s*<\s*uniform\s*>')
            HasPushConstant = [bool]($emitted -match 'PushConstant')
            Diagnostic = $diagnostic.Trim()
        }
    }
}
[pscustomobject]@{
    Version = $version
    CompilerSha256 = (Get-FileHash -LiteralPath $CompilerPath -Algorithm SHA256).Hash
    SharedModuleSha256 = (Get-FileHash -LiteralPath (Join-Path $fixturesDirectory 'shared_math.slang') -Algorithm SHA256).Hash
    Results = $records
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'results.json') -Encoding utf8
$records | Select-Object Fixture, Target, ExitCode, HasImmediate, HasUniform, HasPushConstant | Format-Table -AutoSize
Write-Output ('Results: ' + (Join-Path $runDirectory 'results.json'))
