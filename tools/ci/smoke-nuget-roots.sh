#!/usr/bin/env bash
# Verify producer restoration, not consumer assets rewriting or fallback omission.
set -euo pipefail
repo=$(git rev-parse --show-toplevel)
root=$(mktemp -d "$HOME/.cache/lumyte-nuget-roots-XXXXXX")
export NUGET_PACKAGES="$root/packages"
mkdir -p "$root/machine-fallback" "$root/checkout/artifacts/nuget"
cat > "$root/NuGet.config" <<CONFIG
<configuration><fallbackPackageFolders><add key="machine" value="$root/machine-fallback" /></fallbackPackageFolders></configuration>
CONFIG
cp "$repo/global.json" "$root/checkout/global.json"
cat > "$root/checkout/Fixture.csproj" <<'PROJECT'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
PROJECT
cd "$root/checkout"
dotnet restore Fixture.csproj
python3 - <<'PY'
import json
folders = json.load(open('obj/project.assets.json'))['packageFolders']
assert len(folders) == 2, folders
print('Inherited fallback recorded:', list(folders))
PY
cp "$repo/NuGet.config" NuGet.config
dotnet restore Fixture.csproj --force
python3 - <<'PY'
import json, os
from pathlib import Path
folders = json.load(open('obj/project.assets.json'))['packageFolders']
assert {Path(p) for p in folders} == {Path(os.environ['NUGET_PACKAGES'])}, folders
print('Repository clear yields canonical producer package root:', list(folders))
PY
echo "PASS inherited unused fallback removed by supported producer restore: $root"
