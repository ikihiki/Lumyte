# Lumyte.Graphics.Generators

Ordinary unmanaged structs do not need IShaderArguments, partial,
a generator reference, or registration. Use `device.CreateShaderDataBuffer<T>`(artifact,
count) to obtain `IGraphicsBuffer<T>`; CopyFrom copies the complete span without conversion.
Use `BufferSlice<T>`, `BufferBarrierDesc<T>`, CopyBuffer and WriteBuffer, including
RenderGraph's raw buffer paths (its `CreateShaderDataBuffer<T>` overload returns
`RenderGraphBuffer<T>`). Resources use the existing `GraphicsBufferDefinition<T>`. System.Numerics elements work directly as well.
SetArguments(in value) accepts ordinary unmanaged root structs on native and graph encoders.
The C# type name is the default shader structure name (intrinsic numeric types use
their shader names, e.g. Vector3 maps to float3); root calls can override it.

The generator is for reference-containing classes, or explicitly opted-in
IShaderArguments implementations on partial types.
There is no codec registry, per-numeric-field serialization, or reflected repacking.

- Unmanaged structs (including record structs) transfer their **entire C# representation**
  through one `writer.WriteValue` over the whole unmanaged representation.
  Nested structs and padding are not rewritten.
- Values containing IGpuRef are partial classes with one **private unmanaged storage
  struct**. That explicitly authored struct determines field order, packing, padding
  and stride. A same-named ShaderReferenceSlot reserves 16 bytes per reference.
  The generator takes a value copy of storage at the beginning of Write, then passes
  raw ranges from that local to WriteValue, interleaved with value-only
  WriteReference calls encoding 16-byte slots inline; no shader path is passed at runtime.
- Numeric properties can forward to private storage normally, or be public read/write
  partial properties implemented by the generator (.NET 10).
- Static TransferSize and TransferFields describe C# wire layout without creating an
  instance. Generated unsafe field accessors calculate actual managed byte offsets;
  they do not inspect or repack shader values.
- Shader layout and reference kind/type diagnostics require explicit per-device
  EnableValidation() in both Debug and Release; disabled transfers preserve original
  bytes even for incompatible shaders. Reference bounds, ABI size, same-device
  admission and registration usage-capability checks remain active.
  Access comes from the argument-table registration,
  not the shader or BufferUsage: raw WriteBuffer defaults to ShaderBufferAccess.ReadOnly;
  explicitly select ReadWrite for a writable storage view. GetElement shares that
  registration; IGpuRef remains opaque without read/write variants. Callers own resource lifetime and
  CPU/GPU synchronization. Explicit device validation adds lifetime and registration
  diagnostics under GRAPHICS-0035; it does not take resource ownership.

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
Explicit inspection snapshots copy wire bytes and reference identities; native Set
instead writes a fresh command-owned final uniform generation. Neither is changed by
later property writes. Reference-containing classes themselves are never copied to GPU memory.

Project reference:

    <ProjectReference Include="path/to/Lumyte.Graphics.Generators.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />

Generic/nested generator targets, managed structs, missing private unmanaged storage,
and missing or mistyped reference slots produce LUMG001 diagnostics. Handwritten
implementations must implement the same mandatory value-only writer contract.

For ordinary structs, built-in backends diagnose named storage layouts only after
explicit per-device EnableValidation(), in both Debug and Release. Cached managed
field reflection and offset probes never choose or rewrite transferred bytes.
Where dynamic code is unavailable (Browser or AOT), names and sizes remain
checkable while unavailable field offsets are skipped. Ordinary capture stores
a type identity, not a diagnostic callback. Generated classes retain an unboxed
metadata provider and initialize diagnostic field tables only when explicitly
requested; native transfer never invokes the provider when diagnostics are disabled. The disabled path has no field
reflection, diagnostic table initialization, or runtime code generation.

The generated `Write<TWriter>` seam also supports direct reference-bearing uploads.
CopyFrom uses a streaming writer, not an implicit Capture: private unmanaged bytes
are copied synchronously and only immutable resource identities remain on the CPU.
Explicit Capture remains a CPU-only inspection/diagnostic API: its writer copies
raw ranges into independent bytes and records logical references, clearing each
16-byte reference slot rather than exposing its source representation. Snapshots
are not native write inputs or upload backdoors. SetArguments
keeps Set-time value semantics by writing directly into a new command-owned final
uniform generation; it does not allocate an intermediate numeric snapshot.
A failed upload may partially modify bytes and invalidates the requested reference
metadata range; it does not roll back values or retain a hidden numeric history.

## Mandatory value-only writer

IShaderValueWriter has exactly two mandatory abstract methods, each with one value parameter:

    void WriteValue(ReadOnlySpan<byte> value);
    void WriteReference<T>(IGpuRef<T>? value);

There is no offset, size, path, seek, skip or WriteAt parameter. Root/upload writers
own their implicit position and expectedTransferSize. They guard remaining capacity
by subtraction and require position == expectedTransferSize at Complete. References
are always encoded inline as 16 bytes; borrowed spans never escape. Numeric-only
codecs call WriteValue once over the complete unmanaged value, including padding.

Reference-containing classes first copy the complete private unmanaged wire to a
local value. A private static plan uses actual C# offsets only for physical order
and slot validity, independently of optional diagnostic field tables. Invalid,
overlapping or out-of-range slots are rejected before getters. Raw ranges alternate
with WriteReference calls; no emitter structs are generated. IShaderWireSink,
IShaderReferenceCursor, IShaderReferenceEmitter, ShaderWire, ShaderWireCursor and
WriteWire are removed without compatibility aliases.

Reference fields, auto-properties, virtual properties and custom getters all use
this path. Each getter runs once in physical slot order after the local wire copy.
Getter mutations of numeric storage do not change this transfer; mutations of
reference properties remain visible to subsequent getters. The local preserves
padding and is borrowed synchronously, not retained as a CPU snapshot. Handwritten
IShaderArguments implementations use the same two writer methods. IShaderArguments
is the sole transfer interface; the old IShaderData marker is removed.

Explicit Capture uses a size-constructed ShaderSnapshotWriter implementing these
same methods, storing zeroed reference slots and logical identities. It remains
read-only inspection, never a native write backdoor. SetArguments(ShaderValueSnapshot),
CreateSnapshot and Pack/SharedPackRaw remain removed. Native Set writes a fresh
final uniform generation and preserves the old selection if encoding or preparation
fails; it does not re-run the codec on Draw/Dispatch.

Graph caches one preparation delegate per graph encoder and installs it through
existing IRenderEncoder/IComputeEncoder.SetArgumentValidation only around ordinary
SetArguments, clearing it in finally and before End. Preparation checks root
references against declared native resource identity, byte range and access, then
selects the native table before publishing the new root. One detached Action cached
per prepared pass captures only an immutable allowed array, never the owner,
context, pass or input. Each native execution refreshes the full root/transitive
set, including command-local copy overlays, and applies the same check once.
Set-time reentrancy cannot End or mutate the pipeline. A reference cached from
callback B can be used in A when A declares its native resource, range and access
and the native table is compatible. Callback origin, registration group and slot
do not define use permission; JoinTable alone declares no use. Admission remains
active with validation disabled. No extra root
encoder/scope types, public types, friend access or graph copy overlay proxy is needed.
A failed admission blocks native recording, not a promise of CPU rollback. Native
manual SetArgumentTable remains available; Graph selects its table automatically.

Inspection Fields/reflection remain diagnostics only; ModelVertex checks size and
Fields without Capture. ShaderBindingSnapshot.Capture(rootArguments, …) gathers
reference metadata only, with no numeric Root. Ordinary unmanaged buffer memcpy
and GPU copy commands are unchanged; no performance measurements are claimed.
