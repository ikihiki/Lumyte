# Corrected wgpu-native 29.0.0.0

Ahjo.Wgpu.Native 0.7.0 is the latest available NuGet release checked on
2026-11-16. Its bundled wgpu-native v29.0.0.0 source commit is
[d2e3330](https://github.com/gfx-rs/wgpu-native/tree/d2e3330ade4ae1bb238d76b485926f067e7ee64c).

## Defect and correction

wgpuCommandEncoderFinish initializes WGPUCommandBufferImpl.open=false, but
its Drop removes the core command-buffer resource only when open=true.
Both submission entry points set it true. **Releasing a finished command
buffer without submission therefore leaks the native command pool and its
retained resources**, despite balanced public Release calls. Polling cannot
remove a live registry resource; the checked upstream trunk has the same bug.

The sole native behavioral correction initializes open=true at Finish.
Existing submit writes remain true; native ABI and core queue semantics are
unchanged. No forced GC, hidden Submit or Wait workaround is introduced.
finished-command-ownership.patch records the exact one-line correction.

build.py verifies source archive, pinned WebGPU header, Cargo.lock and
original/patched lib.rs hashes. source-info.json records the source identities;
Cargo builds use --locked --release with the original default backend features.
provenance.json records source and binary hashes, compiler versions and host RID.
The build inputs include the runner, script, patch and source metadata.

## Build and prerequisites

The Wgpu project's default source-build target reuses Build.Wgpu.cs and the
existing private PythonRuntime / RustToolchain provisioning. No global Python
or Cargo installation is required. The pinned compiler is Rust/Cargo 1.99.1;
private CPython is selected by the repository's existing checksum-verified
PythonRuntimes.json. Direct script execution requires Python 3.12+:

    python3 native/wgpu_native/build.py --output artifacts/wgpu-native

clang_runtime.py provisions checksum-pinned .NET Foundation / ClangSharp
libclang NuGet runtime packages privately under OUTPUT/tools/libclang. Live
package metadata and all six downloaded hashes were checked on 2026-11-16.
The four existing CI RIDs and osx-arm64 use 23.1.5; osx-x64 uses its latest
published runtime, 18.1.3. clang-runtimes.json pins exact archive SHA-256.
Only the named native library and LLVM license are extracted; verified caches
work offline, archive tampering fails closed, and modified extracted binaries
are restored from the verified archive. Nothing is globally installed.
These are build tools, not application runtime/package assets.

A **host C linker and C/SDK headers** are still required, already supplied by
the repository setup used in CI:

- Linux x64/arm64: tools/setup/setup-os.sh installs build-essential (GCC,
  libc development headers and linker) plus zlib1g-dev. libclang also needs
  the normal libstdc++, libgcc, glibc and zlib runtimes. The pinned linux-x64
  library requires glibc 2.34 or newer; CI uses Ubuntu 24.04. The helper queries
  cc -print-file-name=include and passes that standard-header directory to
  both the parse probe and bindgen; no clang executable is necessary.
- Windows x64/arm64: tools/setup/setup.ps1 verifies/provisions native MSVC C++
  tools, Windows SDK 26100 and matching VC++ runtime. activate.ps1 initializes
  the compiler and INCLUDE. The helper uses INCLUDE, or locates matching VS
  C++ tools plus UCRT headers with vswhere if not initialized. Rust still
  needs the ordinary native linker environment. No new CI parameter is needed.
- macOS: native Xcode command-line tools, SDK and linker are required.
  xcrun --sdk macosx --show-sdk-path selects the SDK. No macOS CI job exists.

Before Cargo, the public Clang C API parses the exact pinned WebGPU headers
and rejects missing SDK/header errors early. LIBCLANG_PATH (directory) and
BINDGEN_EXTRA_CLANG_ARGS remain explicit overrides. The script disables
rustup automatic installation; the .NET runner provisions missing Rust tools
only through the existing private-cache mechanism.

LumyteWgpuPython selects an explicit interpreter. LumyteWgpuCargo and
LumyteWgpuRustc must be supplied together when overriding the private compiler
resolver. First build fetches checksum-pinned source/header and locked crates;
verified cached inputs work offline. Outputs stay under ignored artifacts.

For separately validated target assets, set LumyteBuildWgpuNative=false and
LumyteWgpuNativePath=ABSOLUTE_PATH. LumyteWgpuNativeProvenance defaults to
adjacent provenance.json. Every output/publish copy verifies binary SHA-256,
upstream/patched source identity and target RID, including build-disabled
paths. Cross-RID source builds fail rather than relabeling host artifacts.

The fixed native library has the distinct basename lumyte_wgpu_native. The
backend installs its assembly resolver before device creation. Applications
using direct Ahjo native interop earlier must call
WgpuDevice.InitializeNativeRuntime **before all such calls**, including log
callback registration. Already-resolved native entry points cannot be rebound.
Missing fixed assets fail closed. Upstream native package assets are excluded
both locally and transitively; managed Ahjo assemblies remain dependencies.
Applications installing their own Ahjo resolver must coordinate that single
assembly-level resolver with this backend.

## Delivery and platform limits

Project-reference consumers receive the corrected library and notices in
output and publish directories. Normal packaging requires all six corrected
RID assets, their hash/RID provenance and dependency notices under
artifacts/wgpu-native/package/runtimes/RID/native. Linux/win/osx x64/arm64
must be built on their appropriate native toolchains. Only **linux-x64** was
built and hardware-validated here. Fresh private linux-x64 libclang download,
SDK parse and regenerated native build were verified without system libclang;
the corrected binary remained byte-identical. Existing Rust dependency cache
was reused, so this is not a full clean-runner CI execution. Other RIDs and
Windows command execution remain unverified. No binaries are committed.
The package gate never substitutes or relabels a host artifact.

## Regression

The xUnit project tests/Graphics/Lumyte.Graphics.Wgpu.Tests contains two
serialized, bounded subprocess regressions. Public UI stress requires
LUMYTE_UI_BENCHMARK_GPU=1 and independently audits a physical adapter. Native
registry ownership also accepts the existing LUMYTE_GRAPHICS_GPU_TESTS=1
functional-test opt-in, allowing software adapters without any performance
claim. With neither opt-in set, both skip with explicit prerequisite reasons.

- Public stress: 2500 warm plus 2500 fresh-renderer recordings; disposed
  command states, zero active retained frames, real retained reuse, unchanged
  submission count, and full warm/cold analytic pixels before and after.
- Native registry: 3000 Finish/Release cycles with AddRef/Release pairs;
  wgpuGenerateReport proves one live command before final Release and zero
  immediately after each release, without device polling. Another 50 Submit
  and 50 SubmitForIndex cases validate normal retirement and reference counts.

The executable also exposes --public-stress, --native-fixed and the explicit
--native-upstream negative-control mode. That last mode requires the absolute
original library path in LUMYTE_WGPU_UPSTREAM_NATIVE. Never run a panic-prone
native negative control without an owned subprocess timeout.

The unfixed public stress aborts 134 after the 500+500 checkpoint with
wgpuCommandEncoderFinish / In a pass parameter / Not enough memory left.
The unfixed native registry assertion fails on the first final Release:
expected=0, actual=1. No memory/time budget is asserted. Raw source hashes,
failures, successful hardware runs and delivery proofs are preserved under
artifacts/ui-fixes/native-memory. Python provenance tests are runnable with:

    python3 -m unittest discover -s native/wgpu_native -p 'test_*.py' -v

## Licenses

Unchanged upstream MIT / Apache-2.0 texts are included. The build emits
WGPU-THIRD-PARTY-NOTICES.txt from the locked dependency metadata and bundled
license texts, plus the selected Rust toolchain's COPYRIGHT-library.html as
RUST-STANDARD-LIBRARY-NOTICES.html. Both accompany native outputs and are
required per RID for packaging. The asset gate is not a claim of a completed
legal audit or cross-platform functional qualification.
