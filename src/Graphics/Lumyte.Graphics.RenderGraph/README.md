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

## Shader inputs and execution

The pass itself owns its declared inputs. UseTexture, UseBuffer, UseShaderDataBuffer and UseSampler are the only declaration source; there is no resource-set factory, attachment step, member-name value bag or required artifact/root pin. Obtain IGpuRef values through the AddPass callback and reuse the ordinary IShaderArguments type and codec. A reference cached from callback B may also be used in A when A declares the native resource, range and access and the native table is compatible; callback origin, registration group and slot are not use permissions.

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

**The capacity unit is the entire pass's declared shader inputs, not each individual Draw.** If their aggregate exceeds effective native slot/binding/range/remap limits, split AddPass. Admission fails before callbacks or native table writes. Built-in Drawing2D prepares bounded pass segments at known command boundaries; arbitrary custom Record callbacks are never replayed or transparently split.

- Sampled shader-read UseTexture admits the default view. UseTexture(texture, viewDesc, TextureState.Sampled, scope) admits an exact view. Attachment/copy-only declarations do not create sampled bindings.
- Whole-buffer shader declarations admit the whole range. UseBuffer(buffer, offset, count, scope) and UseShaderDataBuffer(buffer, offset, count, scope) admit exact element ranges. Shader read/write permission comes from scope, not a duplicate registration argument. Copy-only/upload uses do not consume shader bindings.
- UseSampler(desc) declares a graph-owned immutable sampler; UseSampler(sampler) declares a borrowed identity. Samplers create neither GPU barriers nor write roots. GetGpuRef accepts the same desc or object key.
- Distinct views/ranges retain their exact identities, while hazards and culling remain conservatively allocation-wide. GetGpuRef only looks up admitted keys during the declaring callback; it cannot append slots or widen access.

SetArguments(in arguments) invokes the ordinary codec once through the selected backend’s internal IShaderValueWriter and writes a new command-owned final uniform generation. The same backend writer serves typed CopyFrom; it owns bounds, position and cleared stack-local 16-byte encoding and writes directly to storage, not through a Shared encoding callback. The reference-only Graph preparation hook below is unchanged and is not a codec/encoding callback. Graph caches one PrepareArguments delegate per graph encoder, installs it through existing native IRenderEncoder/IComputeEncoder.SetArgumentValidation only around ordinary SetArguments, and clears it in try/finally and before End. RenderGraphContext.SelectArguments delegates root checks to GraphShaderResourceOwner.SelectArguments: each reference must fit the pass’s existing allowed native resource identity, byte offset/size and access. BindResources automatically selects the native table before publishing the new root. EnsureActive rejects reentrant End, nested Set and pipeline mutation while SetArguments is active. There is no public Graph SetArgumentTable; native manual SetArgumentTable remains available. No numeric snapshot or legacy fallback is used. Numeric bytes remain frozen until another successful selection. Failed encoding/schema/preparation/native replacement cleans up unpublished storage, preserves the original exception and leaves the previous root selection unchanged. Switching pipelines requires a new selection unless switching back before another successful SetArguments; there is one last selection, not a per-pipeline cache. Numeric-only arguments and argument-free programs need no dummy resource declaration.

Preparation returns the single detached Action cached per prepared pass. It captures only an immutable allowed array, never the owner, context, pass or original input. ShaderRootArguments retains it; every native Draw/Dispatch refreshes the complete root/transitive reference set through its latest command-local CopyFrom/CopyBuffer metadata overlay and checks it once against the same native resource/range/access permissions. Numeric bytes are not snapshotted or re-encoded. Metadata owners and collections, native root buffers and cached delegates still exist; Browser interop still performs span copies. This does not claim zero allocation, zero GPU copy or new measured performance. A failed admission blocks native recording; it does not promise rollback of CPU state.

This minimal hook is needed even with device validation disabled. JoinTable can merge native table identity across passes, but that identity does not describe a pass declaration-range boundary. Sharing a table does not itself declare resource use. Graph cannot directly observe native command-local metadata, so table affinity and declared resource/range/access admission remain separate checks. Ordinary native SetArguments remains direct, with no hook allocation on the null path. IShaderRootEncoder, IShaderArgumentScope, GraphRootArgumentScope and the nested ReferenceAdmission class are removed; no new public types, friend access, InternalsVisibleTo or Graph copy-overlay proxy are added.

Every execution validates the pass generation, native table and complete root/transitive reference set. Shader-data producer/copy/consumer ranges keep encoded table identities coherent without moving native capture earlier than ordinary command-local transfer semantics. Each pass's own binding budget remains independent; connected data that cannot share a representable slot/remap table is rejected rather than silently repacked. External already-encoded table references are not automatically migrated.

Actual BeginRenderPass attachments must match declared roles/access and cannot contain duplicates. Capacity preflight conservatively reserves all color attachments declared by the pass. Draw, DrawIndexed, both indirect variants and compute Dispatch/DispatchIndirect retain ordinary interface signatures and defaults. End/callback guards and caller-owned submission/completion are unchanged.

Built-in payload mapping/finalization uses an assembly-private bridge driven by the same exact view/range/sampler declarations. No additional module resource factory is part of the public Graph API. Direct Canvas remains graph-independent. Existing raw Commands remain the low-level interoperability escape; do not mix raw passes with an open dedicated graph encoder.
