# Lumyte.Graphics.RenderGraph

共通 command API 上で pass の依存関係、barrier、不要 pass の除去、一時 texture／buffer の寿命を管理します。Wgpu／Vulkan／Browser が同じグラフを利用でき、core は `Lumyte.Graphics.Abstractions` だけに依存します。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

using var graph = new RenderGraph(device, "frame");
RenderGraphTexture color = graph.CreateTexture(new TextureDesc
{
    Width = 64,
    Height = 64,
    Format = TextureFormat.Rgba8Unorm,
    Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled,
}, "color");
graph.AddPass("clear", context =>
{
    IRenderEncoder encoder = context.Commands.BeginRenderPass(new RenderPassDesc
    {
        ColorAttachments = new[]
        {
            new RenderColorAttachmentDesc
            {
                View = context.CreateTextureView(color), // plan が所有。利用者は Dispose しない。
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                ClearValue = new ClearColor(0, 0, 0, 1),
            },
        },
    });
    encoder.End();
}).UseTexture(color, TextureState.ColorAttachment,
    new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), discard: true);
graph.ExportTexture(color, TextureState.Sampled,
    new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));

using CompiledRenderGraph plan = graph.Compile();
using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new CommandBufferDesc());
plan.Record(commands);
commands.Finish();
using IGraphicsSubmission submission = device.Queue.Submit(new[] { commands });
await submission.WaitAsync();
IGraphicsTexture result = plan.GetTexture(color); // 借用。plan の Dispose まで有効。
// result を別 submission で使用するなら、その完了まで plan を保持する。
```

## 宣言と出力

- `ImportTexture`／`ImportBuffer<T>`／`ImportShaderDataBuffer<T>` は外部 allocation を借用します。実際の初期 state／scope と内容の初期化は利用者が指定します。同じ allocation の二重登録、別 graph の handle、pass 内の重複 use を拒否します。
- `CreateTexture`／`CreateBuffer<T>` は未初期化 resource を宣言します。Compile は GPU work を行わず、live pass の Record 時に descriptor どおり確保します。graph constructor に同じ device を渡してください。shader data buffer は import を提供します。
- `UseTexture`／`UseBuffer`／`UseShaderDataBuffer` の Access flags から read／write を判定します。attachment Load は read＋write、Clear は write。shader arguments、参照先、index／indirect buffer、copy 元・先も宣言してください。初回未初期化 read／read-write を Compile 時に拒否します。部分 write の初期化範囲と attachment discard 後の内容は callback の責任です。
- `MarkOutput` が最後の producer を保持します。`ExportTexture`／`ExportBuffer`／`ExportShaderDataBuffer` はさらに末尾の final barrier を指定します。readback は Host／HostRead、借用 frame texture の提示は Present／空 scope を指定します。Acquire／Submit／Present と semaphore は従来どおり利用者が管理します。
- 外部 resource への write は副作用として保持します。resource 宣言で表せない副作用は `KeepAlive()` を指定します。root の read と通常 write が必要とする直前の producer、および明示 dependency を保持し、不要な pass と allocation は除去します。
- `UseTexture`／`UseBuffer`／`UseShaderDataBuffer` の `discard` は既定で false です。部分 copy や単一 layer／mip の clear でも先行 producer を保持します。全 allocation を上書きすると保証できる場合だけ `discard: true` にすると、古い producer の値への依存を除けます。read-only／read-write と true の組合せは拒否します。上の例は単一 mip／layer 全体の clear なので true を指定できます。

## 順序と同期

resource hazard は追加順を保ち、RAW／WAR／WAW と texture state 変更で依存を生成します。独立した pass の順序は `DependsOn` で指定します。実行可能な pass のうち追加順が早いものを選び、循環を Compile 時に拒否します。

全 mip／layer／buffer 要素を同期します。同 state の write 間にも barrier を発行し、read scope は合併します。write／state 変更から read への barrier の After には、次の write／state 変更までの全 reader stage と同 state の外部 export reader を含めます。

`GetFinalTextureState`／`GetFinalScope` は正常実行後の予定値で、GPU query ではありません。次の graph には生存する allocation と成功した実行の state／scope を渡してください。import 後に同じ state の read が新しい stage／access へ広がる場合も、その可視性を確保する barrier を生成します。利用者は command の順序を保証してください。

## 記録と所有権

Compile は宣言と callback を snapshot します。拡張構成・拡張状態・Retainした所有権があるgraphは、成功したCompileで構築を閉じます。その後の追加、pass宣言の変更、再Compileは拒否します。これらを持たない従来の借用だけのgraphは複数回Compileでき、builderの後続変更は既存planに影響しません。callbackがcaptureするmutable値は利用者が管理します。`PassNames`は実行順、`CulledPassNames`は除去されたpass、`Statistics`は固定サイズの件数です。`Snapshot`は単発要求がある場合だけ生成されるnullableな診断用情報で、native resourceとcallbackを保持しません。

Record は一度だけで、callback 失敗時も消費されます。context の Get 系 API は当該 pass の宣言だけを解決し、callback 後は無効です。encoder を End してから返し、callback 内で Finish／Submit／Wait／resource 解放を行わないでください。実命令と宣言の一致、同一 device と owner thread は利用者の契約です。

planは一時allocation、contextが作成したview、graphから移譲された外部所有権を保持します。GPU完了後にview→allocation→外部所有権の順でDisposeします。Recordが失敗した場合はcommand bufferを破棄してからplanを解放します。DisposeはGPUを待ちません。記録先がCompleted／Faulted／Disposedになるまで拒否します。待機キャンセルはGPU完了ではありません。device loss時はbackendの退役確認を済ませてから子資源を解放します。planのGet系APIが返す出力は借用で、Record成功後から所有者のDisposeまで有効です。

Retainしていない外部allocation／view／pipeline／Argument Tableと、command／submissionの寿命は利用者が管理します。外部commandで一時出力を使う場合も、そのGPU完了まで元planを保持してください。

## 診断と検証

[診断 adapter](../../Diagnostics/Lumyte.Diagnostics.RenderGraph/README.md) を既存診断の execution point に登録し、scoped observer を graph constructor に渡します。通常は CPU compile／record 時間と件数を集計し、指定間隔で既存の収集・通信経路へ公開します。失敗ログには graph／run／frame の識別子を含めます。observer 未指定または `IsEnabled == false` なら計時・通知・trace scope を行いません。

`graph.Id` は builder ごとの固定 ID、`plan.Run.RunId` は Compile ごとの ID です。`Compile(frameId: 42)` の FrameId はアプリケーション側の任意の frame 番号で、0 は未指定です。同じ run を Record と失敗通知にも使います。

通常の `Compile()` は詳細 snapshot を作りません。構造を調べる一回だけ、次のように明示要求できます。

```csharp
using CompiledRenderGraph captured = graph.Compile(
    frameId: 42, capture: new RenderGraphCaptureOptions { MaxBytes = 4 * 1024 * 1024 });
if (captured.Snapshot is RenderGraphSnapshot snapshot)
{
    // snapshot.Passes／Resources／Dependencies／Barriers を検査する。
}
// 事前の推定保持 byte 数が上限を超えると Snapshot は null。実行 plan は利用できる。
```

診断経由では `request-capture` が返す `capture-id` で一回の Compile を指定し、`get-capture`／`get-pass`／`get-resource`／`get-dependency`／`get-barrier` を取得します。後続 frame が進んでも ID が指す snapshot は変わらず、revision による再取得は不要です。保管数・byte 数・有効期限を adapter の options で制限します。GPU timestamp、resource 内容、replay、複数 graph／frame をまとめる capture は扱いません。

設計と clear→copy、Present、shader data、診断登録の使用例は [GRAPHICS-0015](../../../docs/adr/graphics/GRAPHICS-0015-render-graph.md) を参照してください。

```sh
dotnet test tests/Graphics/Lumyte.Graphics.RenderGraph.Tests -c Release
LUMYTE_GRAPHICS_GPU_TESTS=1 dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --filter FullyQualifiedName~RenderGraphTests
```

GPU／Browser は共通 `RenderGraphExercise` で部分 buffer upload→temporary→readback、texture clear→copy→readback と別々の array layer への clear の値を検証します。単一 queue と allocation 全体の追跡を扱い、pool／alias、subresource、複数 queue／Timeline semaphore、再実行可能な compile cache は後続で設計します。

## 複数の拡張と所有権

`RenderGraphExtensionsBuilder`へ型ごとのfactoryを明示登録し、`BuildAsync`で準備済みの`RenderGraphExtensions`を作ります。同じ型の重複登録を拒否し、完成した構成は変更せず複数のgraphへ渡せます。登録時にはロードせず、非同期の準備はBuildAsyncが登録順に実行します。graph用のfactoryは準備済みの環境からgraph固有の状態を作ります。

```csharp
using var extensions = await new RenderGraphExtensionsBuilder()
    .Add(static graph => new AppImports(graph))
    .Add(static graph => new LayerImports(graph, graph.GetExtension<AppImports>()))
    .BuildAsync();

using var graph = new RenderGraph(device, extensions: extensions);
AppImports imports = graph.GetExtension<AppImports>();
LayerImports layers = graph.GetExtension<LayerImports>();
// importsとlayersは同じgraphへpassを追加し、必要な所有権をgraph.Retainへ渡す。
using var plan = graph.Compile();
```

非同期の準備は`AddAsync<T>(prepare)`へ登録します。prepareは`ValueTask<RenderGraphExtensionFactory<T>>`としてgraph用factoryと任意のownerを返します。BuildAsyncは開始後一度だけ実行でき、成功後は戻り値の構成を再利用します。既にキャンセル済みの場合は開始せず、builderを変更・利用できます。失敗・キャンセル時は所有権を受け取った逆順にownerを解放し、解放にも失敗した場合は両方の例外を通知します。その場合はbuilderが未解放のownerと依存先を保持するため、独自のownerを使う側はbuilderを保持してDisposeを再試行してください。Build中の登録・再Build・Disposeは拒否します。

`AppImports`と`LayerImports`は利用者の拡張型の例です。各factoryはgraph内で初回取得時に一度だけ呼ばれ、型付き状態を共有します。`GetOrAddExtension<T>(fallback)`は登録済みfactoryを優先し、未登録の場合だけモジュールの既定factoryを使います。循環するfactoryや生成失敗はそのgraphの継続利用を拒否します。明示的な型登録なのでReflectionやActivator、描画時のDI解決は不要です。拡張の登録順がGPUの実行順を決めることはなく、passの資源宣言と依存関係を使います。

factoryが返す状態やimportsは借用ビューであり、型がIDisposableでも自動では解放しません。固定GPU資源やLeaseは`IRenderGraphLifetime`として、準備を始める前に`graph.Retain`へ登録します。`graph.Compile()`が全拡張の所有権を一括してplanへ移し、Compile前の放棄は`graph.Dispose()`が返します。移譲後のgraph.Disposeはplanの資源を解放しません。factoryが保持する長期所有権は`Add(factory, owner)`の登録成功時にbuilderへ渡し、BuildAsyncの成功時に構成へ移します。既に発行したgraphのLeaseと構成の所有者は別の寿命です。

`Validate`はRetain時、Compileの移譲時、Record前に呼ばれます。所有権は登録の逆順で返し、依存先より利用側を後から登録します。解放が失敗したらそこで停止し、未解放部分と依存先を保持して同じ所有者のDisposeで再試行します。Compileの検証失敗ではgraph側に所有権が残ります。既存のraw import APIは借用のまま使え、構築済みplanへの低水準の`CompiledRenderGraph.Retain`も利用できます。

ResourceManagerからの取得・世代検証は`Lumyte.Resources.Graphics`の拡張が担当します。RenderGraph本体はその型やパッケージを参照しません。

## struct引数と明示的な要素範囲

All IShaderArguments-consuming root, buffer, slice, factory and Graph/resource APIs
require `where T : struct, IShaderArguments`, allowing managed IGpuRef fields.
Providers are mutable partial structs/record structs, not classes. Ordinary
interface-free unmanaged paths are unchanged. Generated readonly Write copies
only private unmanaged wire before getters; in/ref readonly generic dispatch may
defensively copy the outer struct. Generated partial property implementations retain
the declaration readonly modifiers rather than synthesizing readonly get. Argument
Capture retains DiagnosticType plus a cached static generic DiagnosticFields callback,
never the input struct, boxing or an instance closure. Capture/Copy do not invoke it;
explicit GetDiagnosticFields and `Fields<T>(in T)` use lazy direct T.TransferFields
static dispatch, including generated metadata on Browser/NativeAOT. Type-only
unknown-T reflection and raw unmanaged reflected offsets remain unavailable on AOT.
JIT retains reflected-field fallback for handwritten unmanaged arguments with default
empty TransferFields; managed outer structs without static metadata return empty
diagnostics, not numeric transfer of managed layout. Browser build success does not
establish AOT publication or runtime execution; both remain untested.
Graph compilation does not cache values or discover dirty elements. The caller
selects CopyFrom/CopyBuffer whole-element ranges, edits array elements by ref or
writeback, and owns upload-source lifetime and immutability until GPU consumption.
There is no field-range upload, automatic dirty tracking or coalescing. default(T)
and array elements skip constructors and keep references null until the caller fills
them; encoding rejects null root references. DrawingInstruction new() retains -1
resource indices and default! reference initialization, not valid GPU references.
ShaderRootArguments remains a reference-only metadata class, not a shader provider.

## シェーダー入力と実行

passの入力はUseTexture、UseBuffer、UseShaderDataBuffer、UseSamplerで宣言し、通常のIShaderArguments型とcodecを使います。別callbackから保存した参照の取得元は許可条件ではなく、検証ONの照合は使用passの正規化resource identityとread／write権限に従います。

The following example is inside ImageEffects and reuses its existing internal EffectArguments. Applications likewise reuse their own ordinary argument types. The caller supplies the matching pipeline, source/target, samplerDesc, bounds, opacity, width/height and sampleRead/colorWrite scopes. Creation/import, using directives including System.Numerics, export, Submit/Wait and failure cleanup are omitted. Retain the compiled plan and borrowed resources through GPU completion.

```csharp
graph.AddPass("sample", context =>
{
    var arguments = new EffectArguments(
        context.GetGpuRef(source),
        context.GetGpuRef(source),
        context.GetGpuRef(samplerDesc),
        bounds, bounds, bounds, new Vector4(3, 0, 0, opacity),
        Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero);
    using RenderGraphRenderEncoder render = context.BeginRenderPass(new()
    {
        ColorAttachments = [new()
        {
            Texture = target,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
        }],
    });
    render.SetPipeline(pipeline);
    render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
    render.SetViewport(new(0, 0, width, height, 0, 1));
    render.SetScissor(new(0, 0, width, height));
    render.SetBlendConstant(new(0, 0, 0, 0));
    render.SetStencilReference(0);
    render.SetArguments(in arguments);
    render.Draw(3);
}).UseTexture(source, TextureState.Sampled, sampleRead)
  .UseTexture(target, TextureState.ColorAttachment, colorWrite)
  .UseSampler(samplerDesc);
```

- Sampled shader-read UseTexture admits the default view. UseTexture(texture, viewDesc, TextureState.Sampled, scope) admits an exact view. Attachment/copy-only declarations do not create sampled bindings.
- UseBuffer／UseShaderDataBufferのoffset／countは具体化する登録範囲を指定します。Graphの検証ONの許可判定はallocationとaccess単位であり、登録範囲の完全一致とは分離します。copy-only／upload宣言はshader bindingを作りません。
- UseSampler(desc) declares a graph-owned immutable sampler; UseSampler(sampler) declares a borrowed identity. Samplers create neither GPU barriers nor write roots. GetGpuRef accepts the same desc or object key.
- Distinct views/ranges retain their exact identities, while hazards and culling remain conservatively allocation-wide. GetGpuRef only looks up admitted keys during the declaring callback; it cannot append slots or widen access.

各実行のpass世代とnative tableの通常契約は維持します。root／buffer全体の推移的依存の照合は検証ONだけで行います。選択table全体の登録がnative上限へ収まらなければ拒否し、外部の符号化済み参照を暗黙に移動しません。

Actual BeginRenderPass attachments must match declared roles/access and cannot contain duplicates. Capacity preflight conservatively reserves all color attachments declared by the pass. Draw, DrawIndexed, both indirect variants and compute Dispatch/DispatchIndirect retain ordinary interface signatures and defaults. End/callback guards and caller-owned submission/completion are unchanged.

Built-in payload mapping/finalization uses an assembly-private bridge driven by the same exact view/range/sampler declarations. No additional module resource factory is part of the public Graph API. Direct Canvas remains graph-independent. Existing raw Commands remain the low-level interoperability escape; do not mix raw passes with an open dedicated graph encoder.

Graph admission and metadata are root-transport-neutral. Vulkan/D3D12 record native
push/root constants during Set, after Complete, optional diagnostics, preparation,
metadata creation. A cleared bounded stack span holds
ceil(wireSize/4) words until the driver command buffer copies them; it is not
the final payload owner. 検証ONだけがDraw／Dispatchで依存を更新し、全modeで数値rootを再送しません。
Draw/Dispatch never
re-emit numeric roots. No numeric CPU heap arrays, ArrayPool rentals, per-Set GPU
uniform/CBV or oversized-root fallback remain. Shared retains only immutable root/reference/admission metadata without storage,
Allocate, Seal, Dispose or IDisposable; no replacement public storage type is added.
Wgpu/Browser retain existing private concrete uniform owners through command
KeepBinding until GPU release. Vulkan/D3D12 do not KeepBinding root metadata at Set.
記録済みbindingは全登録とtable identityを保持し、検証ONだけで依存revisionも保持します。
Set-time capacity checks reject before
allocation/publication, preserving the previous selection. Mandatory artifact
preflight is separate ABI safety, active without validation tracking: Vulkan checks
the owning device before shader-module creation; all D3D12 pipeline/partial paths
check before native pipeline creation. Every app ConstantBuffer is checked by its
actual root name and logical ShaderDataLayout.Root.Size, excluding lumyteDrawInfo
and __lumyte_schema_ entries; a logical 232-byte D3D12 root padded to 240 bytes is
valid because it loads only 58 DWORDs. Wgpu/Browser uniform emulation,
padding and native binding limits remain backend-private. Shared/Graph add no
Wgpu/Browser feature dispatch; target-aware shader emission is a separate concern.
No public hierarchy, friend assembly or immediate API is added.

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

```csharp
using System.Diagnostics.CodeAnalysis;
using Lumyte.Graphics.Abstractions;

// IShaderDataSourceの宣言抜粋。手書きbackendも実装する。
public interface IShaderDataSource
{
    bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies);
}
```

既存の有効なsourceに対する呼び出し例です。

```csharp
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
```

検証OFFでは全backendが数値だけのReferenceCount = 0を含めてfalse／nullを返します。これは追跡利用不可であり、既知の空集合ではありません。登録・remapなどの非公開機能状態は残ります。検証ONではbuffer全体の不変な依存unionを返し、ReferenceCount = 0は既知の空集合としてtrueです。参照付きbufferの未初期化・失敗後の状態は無効で、queryはInvalidOperationExceptionとなり、buffer全体の明示再転送で回復します。部分転送だけでは回復しません。

既知状態への部分更新は旧依存とのunionを保持でき、buffer寿命内で保守的な偽陽性が残ります。全体書き直しは現在のunionを置き換えられますが、記録済みcommandが保持する旧revisionは不変です。要素ごとの配列や毎drawの行走査は不要で、検証ONのShaderBindingDependencies.Buffersが各bufferを一回だけ訪問します。

CPU CopyFromの更新はbackendが所有します。GPU copyは検証ONだけでbackend-privateなbuffer単位のcommand overlayを作り、native copy記録成功後に更新し、queue Submit成功後にだけ公開します。失敗・未送信・破棄したcommandから公開せず、隠れたreadback・待機・数値履歴も持ちません。

### 厳密な参照数とmapped batchの契約

IShaderArgumentsのstatic abstract ReferenceCountは重複も数えた物理16-byte slot数です。backend writerは全modeで0 <= N <= wireSize / 16、WriteReferenceの超過とCompleteの不足、位置・容量・型・範囲を検査します。rootの直接参照配列を収集するのは検証ONまたは利用者が明示したprepare hookがある場合だけです。それ以外はscalar countと単一の暗黙table hintだけを保持します。数値だけの追跡rootは共有empty metadataであり、未収集のnullとは区別します。

ShaderRootArgumentsはstorageを持たない不変metadata classです。Referencesはnullableでnullは未収集を表し、Createにprepareを渡す場合は利用可能なmetadataが必須です。rootの単一table hintは自動選択を補助しますが、明示SelectedTableが優先します。異なるtableが混在すればnative Set発行前に拒否します。

native CopyFromはbatch全体のmapped spanを一度だけ範囲・mapping方向・overflow検査し、row sliceへ直接書きます。Browserは一つの検査済みUint8Array viewへJS.setでコピーし、finallyでviewだけを解放します。検証ONだけHostWriteLeaseで再入書き込み・Map／Unmap／Disposeを診断します。失敗時は部分byte更新を許し、追跡buffer全体を無効にして元例外を保持します。OFFでは利用者がmappingと寿命・並行操作を保証し、追加lock・待機・pinは設けず、無効pointerを実行する試験もしません。

VulkanとD3D12はbuffer作成時の保存済みaddressへchecked offsetを加え、参照ごとのdriver queryやglobal cacheを作りません。native root即時定数、固定signature／stage、pipeline・End・callback後の再検査は継続します。残るbinding確保やBrowserコピーを含めzero allocation／zero copyは保証しません。c1bdf69以前の固定checkpoint測定はこの全table・任意追跡方式の性能結果ではありません。
