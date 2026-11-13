# Lumyte.Graphics.Generators

Ordinary unmanaged structs do not need IShaderData, IShaderArguments, partial,
a generator reference, or registration. Use `device.CreateShaderDataBuffer<T>`(artifact,
count) to obtain `IGraphicsBuffer<T>`; CopyFrom copies the complete span without conversion.
Use `BufferSlice<T>`, `BufferBarrierDesc<T>`, CopyBuffer and WriteBuffer, including
RenderGraph's raw buffer paths (its `CreateShaderDataBuffer<T>` overload returns
`RenderGraphBuffer<T>`). Resources use the existing `GraphicsBufferDefinition<T>`. System.Numerics elements work directly as well.
SetArguments(in value) accepts ordinary unmanaged root structs on native and graph encoders.
The C# type name is the default shader structure name (intrinsic numeric types use
their shader names, e.g. Vector3 maps to float3); root calls can override it.

The generator is for reference-containing classes, or explicitly opted-in legacy
IShaderArguments/IShaderData implementations on partial types.
There is no codec registry, per-numeric-field serialization, or reflected repacking.

- Unmanaged structs (including record structs) transfer their **entire C# representation**
  with one writer.WriteBytes(in this) call. Nested structs and padding are not rewritten.
- Values containing IGpuRef are partial classes with one **private unmanaged storage
  struct**. That explicitly authored struct determines field order, packing, padding
  and stride. A same-named ShaderReferenceSlot reserves 16 bytes per reference.
  The generator copies storage once, then records offset-only reference fixups with
  writer.WriteReference(offset, reference); no shader path is passed at runtime.
- Numeric properties can forward to private storage normally, or be public read/write
  partial properties implemented by the generator (.NET 10).
- Static TransferSize and TransferFields describe C# wire layout without creating an
  instance. Generated unsafe field accessors calculate actual managed byte offsets;
  they do not inspect or repack shader values.
- Shader layout diagnostics run only in Debug. Release transfers original bytes even
  for incompatible shaders. Reference bounds, ABI size, same-device admission
  and registration usage-capability checks remain active. Shader reference kind/type
  consistency is Debug-only. Access comes from the argument-table registration,
  not the shader or BufferUsage: raw WriteBuffer defaults to ShaderBufferAccess.ReadOnly;
  explicitly select ReadWrite for a writable storage view. GetElement shares that
  registration; IGpuRef remains opaque without read/write variants. Callers own resource lifetime and
  CPU/GPU synchronization under GRAPHICS-0014; no lifetime rescan is introduced.

Example (using System.Numerics and System.Runtime.InteropServices):

    public sealed partial class DrawArguments : IShaderArguments
    {
        private WireData _data;

        public partial Matrix4x4 Transform { get; set; }
        public IGpuRef<Vertex> Vertices { get; set; } = null!;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct WireData
        {
            public Matrix4x4 Transform;
            public ShaderReferenceSlot Vertices;
        }
    }

Keep shader declarations in the same order and with matching offsets. The generator
never rearranges fields or chooses packing. GPU references remain opaque and non-owning.
Snapshots copy wire bytes and reference identities, so later property writes do not change
recorded values. Reference-containing classes themselves are never copied to GPU memory.

Project reference:

    <ProjectReference Include="path/to/Lumyte.Graphics.Generators.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />

Generic/nested generator targets, managed structs, missing private unmanaged storage,
and missing or mistyped reference slots produce LUMG001 diagnostics. Handwritten
implementations use the same bulk transfer contract.

For ordinary structs, built-in backends diagnose named storage layouts only after
explicit per-device EnableValidation(), in both Debug and Release. Cached managed
field reflection and offset probes never choose or rewrite transferred bytes.
Where dynamic code is unavailable (Browser or AOT), names and sizes remain
checkable while unavailable field offsets are skipped. Ordinary capture stores
a type identity, not a diagnostic callback. Generated classes retain an unboxed
metadata provider and initialize diagnostic field tables only when explicitly
requested; packing never invokes the provider. The disabled path has no field
reflection, diagnostic table initialization, or runtime code generation.

The generated `Write<TWriter>` seam also supports direct reference-bearing uploads.
CopyFrom uses a streaming writer, not an implicit Capture: private unmanaged bytes
are copied synchronously and only immutable resource identities remain on the CPU.
Explicit Capture still creates an independent CPU value snapshot. SetArguments
keeps Set-time value semantics by writing directly into a new command-owned final
uniform generation; it does not allocate an intermediate numeric snapshot.
A failed upload may partially modify bytes and invalidates the requested reference
metadata range; it does not roll back values or retain a hidden numeric history.
