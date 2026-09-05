# Lumyte.Graphics.Shader.Offline

Cross-platform build-time adapter for `slangc`. It discovers every entry point
declared with Slang's `[shader]` attribute, compiles it to DXIL, SPIR-V, and
WGSL, then writes a deterministic `GpuShaderPackage`.

The compiler version and each Windows, Linux, and macOS x64/ARM64 archive hash
are pinned in `SlangRelease`. The first build downloads the matching official
archive into `.packages/slang`; later builds reuse it. Set `SLANGC_PATH` to an
existing compiler to disable downloading.

WGSL emission is experimental in Slang, so backend integration tests must
continue to validate the generated WebGPU module.

Import `Lumyte.Graphics.Shader.Offline.targets` from a project to recursively compile all
of its `.slang` files and embed one `.lshp` resource per source file. Set
`EnableDefaultSlangCompileItems` to `false` and declare `SlangCompile` items
explicitly when a project needs a narrower source set.
