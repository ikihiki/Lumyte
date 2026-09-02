# Lumyte shader package tooling

`Lumyte.Shaders` is build/tooling code. It deterministically sorts and serializes shader artifacts into the MessagePack
schema owned by `Lumyte.Graphics`. The dependency direction is:

```text
Lumyte.Shaders / future Lumyte.Shaders.Slang -> Lumyte.Graphics
Lumyte.Graphics -X-> Lumyte.Shaders
```

The writer accepts already compiled payloads. A future Slang adapter belongs in `Lumyte.Shaders.Slang`; compiler
process invocation, source parsing, and target selection do not belong in the runtime graphics assembly.
