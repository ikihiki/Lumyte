# Lumyte shader package runtime

`Lumyte.Graphics.Shader` owns the validated MessagePack asset container and deterministic writer. It reads a multi-target
package, verifies hashes and ABI metadata, then selects `GpuShaderBinary` values before calling a backend. The dependency direction is:

```text
tools/Lumyte.Graphics.Shader.Offline -> Lumyte.Graphics.Shader -> Lumyte.Graphics
Lumyte.Graphics.RenderGraph -------------------------------> Lumyte.Graphics
GPU backends ----------------------------------------------> Lumyte.Graphics
```

The writer accepts already compiled payloads. Slang process invocation and target compilation live under
`tools/Lumyte.Graphics.Shader.Offline`; compiler dependencies do not enter runtime assemblies.
