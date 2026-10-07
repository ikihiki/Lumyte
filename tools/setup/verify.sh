#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
source "$script_dir/activate.sh"
cd "$repo_root"
mise run check-native-format
mise run check-markdown

sdk_version="$(python3 -c 'import json; print(json.load(open("global.json"))["sdk"]["version"])')"
[[ "$(dotnet --version)" == "$sdk_version" ]]
cmake_version="$(cmake --version)"
[[ "${cmake_version%%$'\n'*}" == "cmake version $(mise current http:cmake)" ]]
[[ "$(ninja --version)" == "$(mise current http:ninja)" ]]
[[ "$(git -C "$VCPKG_ROOT" rev-parse HEAD)" == "$LUMYTE_VCPKG_COMMIT" ]]
[[ -f "$LUMYTE_LAVAPIPE_ICD" ]]
export VK_DRIVER_FILES="$LUMYTE_LAVAPIPE_ICD"
export VK_ICD_FILENAMES="$LUMYTE_LAVAPIPE_ICD"
export XDG_RUNTIME_DIR="$LUMYTE_ENV_ROOT/xdg-runtime"
mkdir -p "$XDG_RUNTIME_DIR" "$LUMYTE_NUGET_FEED"
chmod 700 "$XDG_RUNTIME_DIR"
vulkaninfo --summary

# Fresh generated projects prevent results from an earlier run being mistaken
# for validation. vcpkg's verified downloads remain cached across runs.
smoke_root="$(mktemp -d "$LUMYTE_ENV_ROOT/smoke.XXXXXX")"
trap 'rm -rf -- "$smoke_root"' EXIT
cp -R "$script_dir/smoke/." "$smoke_root/"
cp "$repo_root/Directory.Build.props" "$repo_root/.editorconfig" "$repo_root/stylecop.json" "$smoke_root/"
slangc "$smoke_root/shader.slang" -entry main -stage compute -target spirv \
    -o "$smoke_root/shader.spv"
python3 - "$smoke_root/shader.spv" <<'CHECK'
import pathlib, struct, sys
data = pathlib.Path(sys.argv[1]).read_bytes()
assert len(data) > 20 and len(data) % 4 == 0, 'Slang produced invalid SPIR-V size'
assert struct.unpack_from('<I', data)[0] == 0x07230203, 'Slang produced invalid SPIR-V magic'
CHECK
case "$(uname -m)" in
    x86_64) rid=linux-x64; triplet=x64-linux ;;
    aarch64) rid=linux-arm64; triplet=arm64-linux ;;
    *) echo 'Unsupported CPU architecture.' >&2; exit 1 ;;
esac
cmake_args=(-S "$smoke_root" -B "$smoke_root/build" -G Ninja
    "-DCMAKE_TOOLCHAIN_FILE=$VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake"
    "-DVCPKG_TARGET_TRIPLET=$triplet" -DCMAKE_BUILD_TYPE=Release)
if [[ -n "${LUMYTE_VULKAN_INCLUDE_DIR:-}" ]]; then
    cmake_args+=("-DVulkan_INCLUDE_DIR=$LUMYTE_VULKAN_INCLUDE_DIR"
        "-DVulkan_LIBRARY=$LUMYTE_VULKAN_LIBRARY")
fi
cmake "${cmake_args[@]}"
cmake --build "$smoke_root/build" --parallel 2

# Include a new version on each run to avoid reusing a cached NuGet package.
smoke_version="0.0.0-smoke.$(date +%s).$$"
dotnet pack "$smoke_root/Native/Native.csproj" --configuration Release -warnaserror \
    --output "$LUMYTE_NUGET_FEED" \
    "-p:PackageVersion=$smoke_version" "-p:RuntimeIdentifier=$rid" \
    "-p:RestoreSources=$LUMYTE_NUGET_FEED%3Bhttps://api.nuget.org/v3/index.json"
dotnet restore "$smoke_root/Managed/Managed.csproj" -warnaserror \
    --source "$LUMYTE_NUGET_FEED" \
    --source https://api.nuget.org/v3/index.json \
    "-p:SmokeVersion=$smoke_version"
dotnet format whitespace "$smoke_root/Managed/Managed.csproj" --verify-no-changes --no-restore
dotnet format style "$smoke_root/Managed/Managed.csproj" --verify-no-changes --no-restore --severity warn
dotnet build "$smoke_root/Managed/Managed.csproj" \
    --configuration Release --no-restore -warnaserror \
    "-p:SmokeVersion=$smoke_version" --no-self-contained
dotnet run --project "$smoke_root/Managed/Managed.csproj" \
    --configuration Release --no-build --no-restore \
    "-p:SmokeVersion=$smoke_version" --no-self-contained
echo 'PASS: Slang SPIR-V compilation, C#, C++, vcpkg zlib, native NuGet/PInvoke, and lavapipe Vulkan queue/readback.'
