# ADR-GRAPHICS-0009: Shader arguments and C#-authoritative bulk transfer

- 状態: 採用
- 日付: 2026-11-13
- 基準コミット: `ef61258f27ec0f19240a5333d4388f1a3ac9de50`
- Resource validation policy: [GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md).
- Explicit convenience APIs: [GRAPHICS-0023](GRAPHICS-0023-explicit-api-convenience.md).

## 背景

Per-member numeric serialization made shader reflection an implicit authority over
C# values and required numeric reconstruction. This migration makes the authored
unmanaged C# representation the sole wire contract.

## 決定

### 利用者の変更前・変更後

Before, applications declared reference-containing argument structs and wrote each
numeric member separately. After, numeric-only unmanaged structs transfer in one
WriteBytes call; reference-containing arguments are partial classes with private
unmanaged storage and ordinary public properties. Existing capture, explicit copy,
submit, completion and disposal boundaries remain unchanged.

C# unmanaged struct definitions are the wire ABI authority. Capture copies their entire
representation, including actual padding, in one operation. No numeric member is serialized
separately, reordered, transposed, converted, or repacked to reflection offsets. Nested
structs and Matrix4x4 remain unmodified bytes. Shaders must match C# field order, offsets,
packing and stride; changing either side is an ABI change.

IShaderArguments and IShaderData accept unmanaged partial structs for numeric-only values.
A type containing IGpuRef must instead be a partial class. Its numeric bytes live in one
private unmanaged storage struct; callers see ordinary read/write properties, including
.NET 10 partial properties implemented by the generator. That authored struct includes
same-named 16-byte ShaderReferenceSlot fields for reference properties. The generator
copies the private storage once and records reference identities plus their C# slot offsets.
Managed object memory is never uploaded.

TransferSize is static and determines shader-data buffer stride and allocation size.
Buffer creation never constructs a value or captures default(T). Static TransferFields
metadata describes actual managed offsets for Debug diagnostics, generated without managed
reflection. No schema-derived stride may overwrite the C# stride.

The old per-field writer and reflection-driven numeric packer are removed. The only numeric
writer operation is WriteBytes; Values contains only reference fixups. ShaderDataLayout.Pack
clones snapshot bytes and patches exactly 16 bytes at each C# reference offset. Reference
kind (read-only vs writable storage, texture, sampler) remains operational shader metadata.

Root uniforms use portable uint4 wire lanes rather than backend-specific aggregate uniform
packing. Shader code initializes its logical root value once per invocation by reinterpreting
those bytes in natural scalar layout. The compiler retains normal identifier scopes; no
root-name token replacement is performed. Reflection retains the logical schema. Physical
uniform allocation/binding capacity rounds up to a complete 16-byte lane, without changing
TransferSize, numeric offsets, snapshot bytes or shader-data element stride.

## Debug versus Release

Debug compares C# names, sizes, field types, offsets, nested paths and reference slots with
shader schemas. Cross-stage ValidateProgram calls in all four pipelines and ModelVertex
layout validation (including ModelMesh calls) are Debug-only.

Release does not reject numeric schema mismatch or repair bytes. Wrong shader declarations
can render incorrectly. Host bounds checks, reference ABI length/bounds, integer overflow,
resource ownership, selected-table membership, registration identity, lifetime and binding
usage checks remain active. Debug diagnostics never provide host memory safety.

## Reference ABI

All backends share a 16-byte reference slot but preserve their existing encodings:

- Vulkan: physical buffer device address.
- Wgpu and Browser: logical registration slot and registration-relative byte offset.
- Direct3D12: descriptor identity and absolute buffer byte offset.

References stay opaque, non-owning and typed. Neither a reference nor an argument value
acquires ownership of a GPU registration. No raw alias or shader-write route is added to
shader-data buffers.

## Snapshots, transfer and lifetime

SetArguments and mapped upload CopyFrom capture immutable byte copies and reference
identities immediately. Mutating a source class afterward cannot change recorded snapshots.
All elements are captured and encoded before staging bytes or metadata are replaced.
Numeric bytes and reference metadata travel together through explicit copies.

MemoryPreference.Upload buffers are CPU staging; Automatic buffers are GPU shader data.
Map, write, unmap, barriers, copy, submit, completion and reuse synchronization remain explicit.
Recording a copy creates command-local metadata overlays only. Metadata is published to the
GPU buffer only after successful submit; abandoned/unsubmitted commands publish nothing.
Partial copies replace only their destination elements. Source mutation and resource lifetime
rules remain those of the command and registration contracts.

Root snapshots recursively collect transferred shader-data reference dependencies. Visited
buffer/element pairs terminate self-reference and mutual cycles. Same-table and resource
checks apply transitively, not merely to direct root references. Buffer-reference access
kind still comes from the shader; reference fixup placement never does.

## Verification

Tests cover single bulk writes, nested structs, matrices, actual padding and C# stride,
private class storage and property writes, snapshot isolation, reference fixup bounds,
static sizing without instance construction, Debug mismatches and Release unchanged bytes.
Backend smoke tests additionally exercise explicit transfer, registration-relative offsets,
cycles and submit-only metadata publication. GPU/Browser/Direct3D12 coverage must be reported
separately from CPU test pass counts.

## 検討した代替案

- Keep per-numeric writers: rejected because they reconstruct bytes and duplicate the ABI.
- Repack reflection offsets in Release: rejected because incompatible declarations must
  not silently change the C# representation.
- Upload managed class memory: rejected; only private unmanaged storage is transferable.

## 結果と影響

This is a breaking shader argument/source-generation contract. Application callers migrate
reference-containing structs to classes and maintain matching shader declarations. Debug
provides layout diagnostics; Release preserves original numeric bytes. Ownership, resource
validation and explicit GPU synchronization remain required.
