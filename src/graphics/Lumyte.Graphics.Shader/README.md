# Lumyte shader package tooling

`Lumyte.Graphics.Shader` is build/tooling code. It deterministically sorts and serializes shader artifacts into the MessagePack
schema owned by `Lumyte.Graphics`. The dependency direction is:

```text
Lumyte.Graphics.Shader / future Lumyte.Graphics.Shader.Offline -> Lumyte.Graphics
Lumyte.Graphics -X-> Lumyte.Graphics.Shader
```

The writer accepts already compiled payloads. A future Slang adapter belongs in `Lumyte.Graphics.Shader.Offline`; compiler
process invocation, source parsing, and target selection do not belong in the runtime graphics assembly.
