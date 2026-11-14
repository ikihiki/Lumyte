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

The generator accepts non-ref partial structs/record structs implementing
IShaderArguments, including managed outer structs with typed IGpuRef members.
There is no codec registry, per-numeric-field serialization, or reflected repacking.

Example (using System.Numerics and System.Runtime.InteropServices):

    public partial record struct DrawArguments : IShaderArguments
    {
        private WireData _data;

        public partial Matrix4x4 Transform { readonly get; set; }
        public IGpuRef<Vertex> Vertices { get; set; }

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
instead writes a fresh command-owned backend-owned opaque root generation. Neither is changed by
later property writes. Reference-containing structs themselves are never copied to GPU memory.

Project reference:

    <ProjectReference Include="path/to/Lumyte.Graphics.Generators.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />

Class/record-class/ref-struct targets (even with handwritten Write), generic/nested targets, missing private unmanaged storage,
and missing or mistyped reference slots produce LUMG001 diagnostics. Handwritten
implementations must implement the same mandatory value-only writer contract.

For ordinary structs, built-in backends diagnose named storage layouts only after
explicit per-device EnableValidation(), in both Debug and Release. Cached managed
field reflection and offset probes never choose or rewrite transferred bytes.
Where dynamic code is unavailable (Browser or AOT), names and sizes remain
checkable while unavailable raw reflected field offsets are skipped. Raw unmanaged
Capture stores a type identity. Argument Capture instead retains DiagnosticType and
a cached static generic DiagnosticFields callback, not the input struct, boxing or
an instance closure. Capture and Copy never invoke this callback. The first explicit
GetDiagnosticFields initializes `ArgumentDiagnosticCache<T>` through direct
T.TransferFields static dispatch; `Fields<T>(in T)` uses the same AOT-safe path.
Generated static metadata therefore needs neither DynamicMethod nor MakeGenericMethod
on Browser/NativeAOT. Type-only inspection of an arbitrary unknown T and raw
unmanaged reflected-field fallback are unavailable on AOT. On JIT, handwritten
unmanaged IShaderArguments with default empty TransferFields retain reflected-field
fallback. Managed outer structs without static metadata return empty diagnostics,
not numeric transfer of their managed layout. ClassDiagnosticSource/GetTransferFields
are removed. The disabled transfer path has no managed field
reflection, diagnostic table initialization, or runtime code generation. This does
not remove shader/artifact reflection: mandatory capacity preflight still reads
logical root metadata before native shader/pipeline creation.

The generated `Write<TWriter>` seam also supports direct reference-bearing uploads.
CopyFrom uses a streaming writer, not an implicit Capture: private unmanaged bytes
are copied synchronously; 検証ONのupload metadataはbuffer全体の不変なresource identity unionだけを保持します。
Explicit Capture remains a CPU-only inspection/diagnostic API: its writer copies
raw ranges into independent bytes and records logical references, clearing each
16-byte reference slot rather than exposing its source representation. Snapshots
are not native write inputs or upload backdoors. SetArguments
keeps Set-time value semantics by writing directly into a new command-owned opaque root
generation; it does not allocate an intermediate numeric snapshot.
失敗時は部分byte更新を許し、検証ONのbuffer全体の依存を無効にします。数値履歴やrollbackは提供しません。

## struct制約と明示的な要素更新

Every interface-consuming native/root, buffer/slice, factory, Graph and resource
API requires `where T : struct, IShaderArguments`, not unmanaged: typed managed
IGpuRef fields are allowed. Manual classes can implement the interface in C#, but
consumer generics reject them with CS0453 even without this generator.
Generated Write is readonly; numeric partial properties can use readonly get with
a mutable set. Generated partial properties preserve the exact declaration readonly
modifiers for C# partial-member compatibility; a non-readonly get stays non-readonly. Generic in/ref readonly dispatch may copy the outer struct; there
is no zero-outer-copy guarantee. Only private unmanaged wire is serialized raw.

Edit array elements by ref or write a modified local back. Callers select complete
element ranges for CopyFrom and explicitly CopyBuffer matching slices; there is no
field-range transfer, dirty tracking, automatic coalescing or hidden numeric cache.
Keep upload sources alive and unchanged until GPU consumption. default(T) and
array initialization skip constructors; references remain null until filled by the
caller, and null default-root IGpuRef is rejected. DrawingInstruction new() preserves
resource indices of -1 and initializes references with default!, which only suppresses
nullable analysis. Browser build success does not establish AOT publication or
runtime AOT execution; neither has been tested.

## 値だけを渡す必須writer

IShaderValueWriter has exactly two mandatory abstract methods, each with one value parameter:

    void WriteValue(ReadOnlySpan<byte> value);
    void WriteReference<T>(IGpuRef<T>? value);

There is no offset, size, path, seek, skip or WriteAt parameter. The internal VulkanShaderValueWriter, Direct3D12ShaderValueWriter,
WgpuShaderValueWriter and BrowserShaderValueWriter each own their implicit
position and expectedTransferSize for both root and upload writes. Their backend
ShaderTransfer helpers call value.Write(ref writer) directly for Set and typed
CopyFrom, not through a shared writer or reference-encoding callback. They guard remaining capacity
by subtraction and require position == expectedTransferSize at Complete. References
are encoded inline into a cleared stackalloc byte[16], then passed directly to
the Vulkan/D3D12 root span, Wgpu/Browser root storage.Write, or a checked upload batch view; borrowed spans never escape. Numeric-only
codecs call WriteValue once over the complete unmanaged value, including padding.

Reference-containing structs first copy the complete private unmanaged wire to a
local value. A private static plan uses actual C# offsets only for physical order
and slot validity, independently of optional diagnostic field tables. Invalid,
overlapping or out-of-range slots are rejected before getters. Raw ranges alternate
with WriteReference calls; no emitter structs are generated. IShaderWireSink,
IShaderReferenceCursor, IShaderReferenceEmitter, ShaderWire, ShaderWireCursor and
WriteWire are removed without compatibility aliases.

Reference fields, auto-properties, custom getters all use
this path. Each getter runs once in physical slot order after the local wire copy.
Generated Write is readonly and copies only wire storage before getters. Numeric
changes after that copy do not affect these raw bytes. Non-readonly custom getters
may execute on defensive outer-struct copies; their mutations need not propagate
to the original value or subsequent getters. The local preserves
padding and is borrowed synchronously, not retained as a CPU snapshot. Handwritten
IShaderArguments implementations use the same two writer methods. IShaderArguments
is the sole transfer interface; the old IShaderData marker is removed.

Explicit Capture uses a size-constructed ShaderSnapshotWriter implementing these
same methods, storing zeroed reference slots and logical identities. It remains
read-only inspection, never a native write backdoor. SetArguments(ShaderValueSnapshot),
CreateSnapshot and Pack/SharedPackRaw remain removed. Native Set writes a fresh
backend-owned opaque root generation and preserves the old selection if encoding or preparation
fails; it does not re-run the codec on Draw/Dispatch.

Native optional reference diagnostics pass Type, reference and current position
directly, without constructing ShaderValue records or per-reference byte arrays.
Explicit Capture may still allocate logical inspection records in its separate
ShaderSnapshotWriter. 検証ONまたは明示prepare時の参照配列と不変metadata owner、 backend-private root
payloads and delegates remain; Browser interop still copies spans to JS. No
zero-allocation, zero-copy or measured performance improvement is implied.

Inspection Fields/reflection remain diagnostics only; ModelVertex checks size and
Fields without Capture. ShaderBindingDependencies.Collect(rootArguments, …) gathers
reference metadata only, with no numeric Root. Ordinary unmanaged buffer memcpy
and GPU copy commands are unchanged; no performance measurements are claimed.

## root転送方式とcodec ABIの分離

The same two-method writer and 16-byte reference ABI feed backend-private opaque
root payloads. Vulkan/D3D12 ref-struct writers use cleared bounded stack spans
of ceil(wireSize/4) words and emit native push/root constants before Set returns.
Complete, optional diagnostics, reference preparation, metadata creation all precede emission. The driver command buffer owns the numeric copy,
not the transient stack. Draw/Dispatch never re-emit it; selected roots hold immutable
metadata（収集時だけreferences／admission）を保持し、CPU数値配列やArrayPool rentalは保持しません。
記録済みbindingは機能上必要な登録とtable identityを保持し、検証ONの場合だけ追加の依存revisionを保持します。 No root
metadata KeepBinding or IDisposable is required at Set. Wgpu/Browser keep their
existing private concrete IDisposable RootStorage, which owns its uniform and
once-initialized Arguments; bindings Seal it directly and commands KeepBinding
the owner until GPU release.
There is no per-Set GPU uniform/CBV or oversized-root fallback. Wgpu/Browser keep uniform emulation entirely private.
Shader target emitters necessarily choose VkPushConstants, D3DRoot32BitConstants or
UniformBuffer metadata; common runtime state, metadata and lifetimes stay neutral.
The uint4 declaration is padded, but Load reads only ceil(sizeof(T)/4) scalar words.
A 232-byte DXIL root may declare 240 bytes yet loads 58 DWORDs, not 60; logical
TransferSize, numeric padding and the reference ABI are unchanged. Backend-private
DWORD rounding also handles 1- and 3-byte tails. Shared metadata has no storage,
Allocate, Seal, Dispose or IDisposable contract; IShaderRootStorage is removed
without a public replacement. Wgpu/Browser writers use their existing private
concrete RootStorage directly; capacity, writes and Seal remain backend-owned.
Artifact preflight is mandatory ABI
safety, not optional validation tracking: Vulkan checks the owning device before
shader-module creation, and all D3D12 pipeline/partial paths check before native
pipeline creation. Both inspect every app ConstantBuffer using the actual root
name and logical ShaderDataLayout.Root.Size, excluding lumyteDrawInfo and
__lumyte_schema_ entries. No zero-allocation
or zero-copy claim follows: metadata, retained owners, execute-time bindings and
descriptor heaps still allocate; Wgpu/Browser keep existing uniform owners.

`Write<TWriter>` uses `where TWriter : IShaderValueWriter, allows ref struct` to
accept stack-only writers without boxing. Handwritten and generated implementations
require C# 13+ and the pinned .NET 10 SDK. Only the anti-constraint is added;
the public writer retains exactly two one-value-parameter methods and no offsets.
Vulkan fixes offset-zero, physical-capacity push ranges per bind point: Compute
for every compute variant, Vertex|Fragment for every graphics/partial variant.
Set emits exactly the selected range’s stage mask, preserving separate native
compute/graphics push state. Descriptor visibility can independently include all
three stages; differing descriptor counts do not change this fixed root contract.
A backend host guard rejects roots above 64 KiB
without fallback on pathological physical limits; 256 bytes is device-specific.
D3D12 initializes one thread-safe cached root signature per device before Set,
using 58 app + 1 + 1 tables + 2 draw + 2 SRV = 64 DWORDs. Separate graphics/compute
root pointer tracking conditionally binds this same signature for PSO/SetProgram
changes without resetting constants. Validation/preparation failures emit no native
constants and preserve the old selection; issued driver commands are not rollbackable.

Vulkan/D3D12 render and compute encoders guard codec/preparation with
_settingArguments: reentrant Set, pipeline changes, End and Draw/Dispatch are
rejected. SetArgumentTable remains allowed for Graph preparation’s autobind.
After callbacks, the captured pipeline’s ValidateAlive, owner.ValidatePass and
pipeline-identity check all precede native emission, rejecting
raw command-buffer or pipeline disposal before native use. Host failure preserves
the old root selection, but cannot undo caller-requested resource disposal, table
autobinding, or already-issued driver commands.

## 選択table全体のbindingと任意の依存検証

bindingは選択したArgumentTableの書き込み済み登録すべてから構築します。capacityの未登録holeはentryではありません。Vulkanは全texture／sampler登録をdescriptorへ投影し、buffer参照は保存済みGPU addressへoffsetを加えてinline符号化します。Direct3D12は全texture／sampler／ReadOnly SRV／ReadWrite UAV登録を投影し、各上限128／16／64／64を適用します。Wgpu／Browserは全登録から有限のbinding poolとremapを構築し、WGSLの個数はcapacityではなく実際の書き込み済み登録を基にします。native binding数・storage範囲・remap容量へ収まらなければ明確に拒否し、巨大な無効binding、追加feature・package・pool・cacheで回避しません。rootなしのshaderも明示選択tableの全登録をbindし、Wgpuは未使用のroot uniform binding 8を省略します。

全登録をbinding可能にすることは、全登録をGPU使用済みとして記録することではありません。backend-privateな登録・remapは検証OFFでも必要な機能状態ですが、同期の所有権や使用hazardを追加しません。resource寿命とCPU／GPU同期は利用者が管理します。

Graphのbarrier・依存順・cullingは、必要なresourceの正確で完全なpass宣言だけを使い、従来どおりallocation全体を保守的に同期します。検証OFFでは宣言の真実性・完全性が利用者の事前条件です。root走査、依存query、準備hook、許可集合、admission callbackの確保を行いません。table共有だけでは使用宣言になりません。

検証ONの場合だけ、Graphはrootとbuffer全体の推移的依存を、buffer allocationまたはtextureの元imageを正規化したresource object identityと宣言read／write権限の合併で照合します。native handle値の衝突判定、要素・byte範囲・view・callback取得元による許可判定は行いません。同じbufferの別宣言範囲から保存した参照も許可されますが、未宣言の別resourceとread-only宣言に対するReadWrite参照は拒否します。

GetGpuRefの具体化はこの粗い許可判定とは別です。要求offset／countと一致する登録を選び、単一要素なら既存登録からGetElementで導出できます。未登録の複数要素範囲に、より広いCountの参照を返しません。新しいslice APIや自動dirty trackingは追加しません。

## buffer全体の読み取り専用依存query

共通境界はIShaderDataSource.TryGetDependenciesだけです。ReadReferences(index)、SetTransferredReferences、SharedのShaderDataTransferStateおよびShaderBindingDependencies.Elementsは削除し、共通の書き込みAPIは設けません。

    using System.Diagnostics.CodeAnalysis;
    using Lumyte.Graphics.Abstractions;

    // IShaderDataSourceの宣言抜粋。手書きbackendも実装する。
    public interface IShaderDataSource
    {
        bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies);
    }

既存の有効なsourceに対する呼び出し例です。

    // falseを既知の空集合として扱わない。
    if (source.TryGetDependencies(out IShaderReferenceMetadata? dependencies))
    {
        foreach (IShaderReference reference in dependencies.References)
        {
            // 追跡済みのbuffer全体の依存を読み取る。
        }
    }
    else
    {
        // 追跡利用不可。呼び出し側が宣言と同期を保証する。
    }

検証OFFでは全backendが数値だけのReferenceCount = 0を含めてfalse／nullを返します。これは追跡利用不可であり、既知の空集合ではありません。登録・remapなどの非公開機能状態は残ります。検証ONではbuffer全体の不変な依存unionを返し、ReferenceCount = 0は既知の空集合としてtrueです。参照付きbufferの未初期化・失敗後の状態は無効で、queryはInvalidOperationExceptionとなり、buffer全体の明示再転送で回復します。部分転送だけでは回復しません。

既知状態への部分更新は旧依存とのunionを保持でき、buffer寿命内で保守的な偽陽性が残ります。全体書き直しは現在のunionを置き換えられますが、記録済みcommandが保持する旧revisionは不変です。要素ごとの配列や毎drawの行走査は不要で、検証ONのShaderBindingDependencies.Buffersが各bufferを一回だけ訪問します。

CPU CopyFromの更新はbackendが所有します。GPU copyは検証ONだけでbackend-privateなbuffer単位のcommand overlayを作り、native copy記録成功後に更新し、queue Submit成功後にだけ公開します。失敗・未送信・破棄したcommandから公開せず、隠れたreadback・待機・数値履歴も持ちません。

### 厳密な参照数とmapped batchの契約

IShaderArgumentsのstatic abstract ReferenceCountは重複も数えた物理16-byte slot数です。backend writerは全modeで0 <= N <= wireSize / 16、WriteReferenceの超過とCompleteの不足、位置・容量・型・範囲を検査します。rootの直接参照配列を収集するのは検証ONまたは利用者が明示したprepare hookがある場合だけです。それ以外はscalar countと単一の暗黙table hintだけを保持します。数値だけの追跡rootは共有empty metadataであり、未収集のnullとは区別します。

ShaderRootArgumentsはstorageを持たない不変metadata classです。Referencesはnullableでnullは未収集を表し、Createにprepareを渡す場合は利用可能なmetadataが必須です。rootの単一table hintは自動選択を補助しますが、明示SelectedTableが優先します。異なるtableが混在すればnative Set発行前に拒否します。

native CopyFromはbatch全体のmapped spanを一度だけ範囲・mapping方向・overflow検査し、row sliceへ直接書きます。Browserは一つの検査済みUint8Array viewへJS.setでコピーし、finallyでviewだけを解放します。検証ONだけHostWriteLeaseで再入書き込み・Map／Unmap／Disposeを診断します。失敗時は部分byte更新を許し、追跡buffer全体を無効にして元例外を保持します。OFFでは利用者がmappingと寿命・並行操作を保証し、追加lock・待機・pinは設けず、無効pointerを実行する試験もしません。

VulkanとD3D12はbuffer作成時の保存済みaddressへchecked offsetを加え、参照ごとのdriver queryやglobal cacheを作りません。native root即時定数、固定signature／stage、pipeline・End・callback後の再検査は継続します。残るbinding確保やBrowserコピーを含めzero allocation／zero copyは保証しません。c1bdf69以前の固定checkpoint測定はこの全table・任意追跡方式の性能結果ではありません。
