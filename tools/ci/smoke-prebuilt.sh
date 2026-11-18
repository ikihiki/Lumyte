#!/usr/bin/env bash
# Real SDK smoke: owned workspace + owned NuGet root, no retained bin/obj/cache.
set -euo pipefail
repo=$(git rev-parse --show-toplevel)
product=${1:-fixture}
root=$(mktemp -d "$HOME/.cache/lumyte-prebuilt-smoke-XXXXXX")
export GITHUB_RUN_ID=local-smoke GITHUB_RUN_ATTEMPT=1
export NUGET_PACKAGES="$root/artifacts/dev-env/nuget-packages"
if [[ "$product" == product ]]; then
    git -C "$repo" archive HEAD | tar -x -C "$root"
    cp "$repo/Directory.Build.props" "$root/Directory.Build.props"
    cp "$repo/NuGet.config" "$root/NuGet.config"
    project=tests/Core/Lumyte.Memory.Tests
else
    project=tests/Smoke
mkdir -p "$root/src" "$root/tests/Smoke" "$root/artifacts/nuget"
cp "$repo/global.json" "$root/global.json"
cp "$repo/Directory.Build.props" "$root/Directory.Build.props"
cp "$repo/stylecop.json" "$root/stylecop.json"
cat > "$root/tests/Smoke/Smoke.csproj" <<'PROJECT'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" /><PackageReference Include="xunit" Version="2.9.3" /><PackageReference Include="xunit.runner.visualstudio" Version="3.1.1" /></ItemGroup>
</Project>
PROJECT
cat > "$root/tests/Smoke/Smoke.cs" <<'SOURCE'
using Xunit;
public sealed class Smoke { [Fact] public void ActualAdapterDiscovery() => Assert.Equal(42, 6 * 7); }
SOURCE
fi
mkdir -p "$root/artifacts/nuget"
cd "$root"
git init -q
git add .
git -c user.name=Smoke -c user.email=smoke@invalid commit -qm fixture
dotnet build "$project" -c Release -p:GenerateDocumentationFile=false -p:EnforceCodeStyleInBuild=false -p:TreatWarningsAsErrors=false -p:RunAnalyzers=false
sdk=$(dotnet --version)
python3 "$repo/tools/ci/prebuilt.py" create "$root/prebuilt.tar.gz" --rid linux-x64 --configuration Release --sdk "$sdk"
# Remove only this harness's producer outputs and private package cache.
find "$root/src" "$root/tests" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
rm -rf "$NUGET_PACKAGES"
# Simulate rerun-failed-jobs: producer attempt 1, consumer attempt 2.
export GITHUB_RUN_ATTEMPT=2
python3 "$repo/tools/ci/prebuilt.py" extract "$root/prebuilt.tar.gz" --rid linux-x64 --configuration Release --sdk "$sdk"
export LUMYTE_PREBUILT_CONSUMER=1
dotnet test "$project" -c Release --no-build --no-restore --logger 'trx;LogFileName=smoke.trx' --results-directory "$root/results"
if dotnet build "$project" -c Release --no-restore > "$root/forbidden-build.log" 2>&1; then
    echo 'ERROR: product CoreCompile was not blocked' >&2
    exit 1
fi
grep -q 'Prebuilt consumer attempted' "$root/forbidden-build.log"
echo "PASS fresh producer -> empty bin/obj/NuGet -> verified consumer; evidence: $root"
