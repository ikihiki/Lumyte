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

The generator is for reference-containing classes, or explicitly opted-in
IShaderArguments/IShaderData implementations on partial types.
There is no codec registry, per-numeric-field serialization, or reflected repacking.

- Unmanaged structs (including record structs) transfer their **entire C# representation**
  through `ShaderWire.Write(in this, ref writer)` and its empty reference emitter.
  The cursor copies one raw range; nested structs and padding are not rewritten.
- Values containing IGpuRef are partial classes with one **private unmanaged storage
  struct**. That explicitly authored struct determines field order, packing, padding
  and stride. A same-named ShaderReferenceSlot reserves 16 bytes per reference.
  The generator takes a value copy of storage at the beginning of Write, then passes
  that local and a reference emitter to `ShaderWire.Write`. The cursor interleaves
  raw ranges with inline 16-byte reference encodings; no shader path is passed at runtime.
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
implementations must implement the same mandatory cursor transfer contract.

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
Explicit Capture remains a CPU-only inspection/diagnostic API: its cursor copies
raw ranges into independent bytes and records logical references, clearing each
16-byte reference slot rather than exposing its source representation. Snapshots
are not native write inputs or upload backdoors. SetArguments
keeps Set-time value semantics by writing directly into a new command-owned final
uniform generation; it does not allocate an intermediate numeric snapshot.
A failed upload may partially modify bytes and invalidates the requested reference
metadata range; it does not roll back values or retain a hidden numeric history.

## Mandatory wire cursor

Every generated Write uses ShaderWire, including numeric-only types. There is no
writer capability check, bulk WriteBytes/WriteReference contract, default throwing
implementation, or compatibility fallback. Third-party IShaderValueWriter
implementations must implement WriteWire<TWire, TEmitter>(in TWire wire,
ref TEmitter emitter), synchronously borrow the source, construct a
ShaderWireCursor over their IShaderWireSink, call emitter.Emit(ref cursor), and
complete the cursor before publishing output. Source spans and cursors must not
escape the call. The raw-only ShaderWire overload is only for values without
reference slots; it uses a private empty emitter and still copies through a cursor.

All generated reference emitters use a static functional plan sorted by actual
managed physical offsets, independent of optional diagnostic reflection. Slots
must be ascending, disjoint, and within the complete wire value. The Emit API has
no ordering flag or declaration-order alternative. Padding and numeric bytes are
copied as raw ranges, never serialized per field.

Reference fields, auto-properties, virtual properties, and custom getters all use
this same path. Getters run once per reference, in physical slot order, not source
declaration order. Before any getter runs, generated Write takes a value copy of
the complete private wire storage. Getter mutations of that storage therefore do
not change the current transfer's raw bytes. Mutations to reference properties
are observed by subsequent getters in physical order; later transfers observe
subsequent storage state. This deterministic rule intentionally replaces the old
legacy getter evaluation order; custom getters never select a fallback.

The local wire copy preserves the entire unmanaged representation, including padding.
It is borrowed synchronously, not retained as a CPU snapshot. Invalid overlapping or
out-of-range generated slot plans are rejected before any reference getter runs.
Handwritten codecs must provide a typed struct IShaderReferenceEmitter and invoke
ShaderWire.Write once; their emitters obey the same physical ordering contract.

SetArguments(ShaderValueSnapshot), CreateSnapshot, Pack/SharedPackRaw and snapshot
execution admission are removed. IRenderEncoder/IComputeEncoder require unmanaged
SetArguments implementations without a default Capture adapter; RenderGraph requires
IShaderRootEncoder and rejects unsupported encoders. Explicit inspection Fields,
reflection and handwritten static metadata helpers are diagnostics, not alternative
shader writing paths. ModelVertex checks size and Fields directly without Capture.
ShaderBindingSnapshot.Capture(rootArguments, …) captures dependency metadata only;
it has no numeric Root. Backend metadata owners retain resource identities, not wire
offsets; dynamic WGSL bindings/remaps still refresh at Draw/Dispatch. Invalid reference
encoding fails during Set without replacing the previously selected generation.
Ordinary unmanaged buffer memcpy and GPU copy commands are unchanged. No new
performance measurements are claimed by this contract update.
