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

Graph consumers describe named logical members of a pinned shader artifact and pass that description directly to a dedicated graph encoder. They do not implement an application argument codec or allocate, size, seal or bind argument tables.

This example uses the existing `EffectArguments` root in `Lumyte.Graphics.ImageEffects/Shaders/effects.slang` for an opacity operation. The caller supplies `fragment`, its `pipeline`, declared `source` and `target` textures, `samplerDesc`, `width`/`height`, `Vector4 bounds = new(0, 0, width, height)`, and `float opacity`. Import/creation, using directives (including System.Numerics), export, Submit/Wait and failure cleanup are omitted; retain the compiled plan and borrowed pipeline until GPU completion as described above.

```csharp
RenderGraphShaderResources inputs = graph.CreateShaderResources(fragment.Artifact)
    .ReadTexture("Input", source)
    .ReadTexture("Backdrop", source)
    .Sampler("Sampler", samplerDesc)
    .Value("InputBounds", bounds)
    .Value("BackdropBounds", bounds)
    .Value("OutputBounds", bounds)
    .Value("Settings", () => new Vector4(3, 0, 0, opacity))
    .Value("Row0", Vector4.Zero)
    .Value("Row1", Vector4.Zero)
    .Value("Row2", Vector4.Zero)
    .Value("Row3", Vector4.Zero)
    .Value("Constant", Vector4.Zero);
graph.AddPass("sample", context =>
{
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
    render.SetArguments(inputs);
    render.Draw(3);
}).UseTexture(source, TextureState.Sampled, sampleRead)
  .UseTexture(target, TextureState.ColorAttachment, colorWrite)
  .UseShaderResources(inputs, target);
```

`CreateShaderResources(fragment.Artifact, rootParameter: "arguments")` pins the exact artifact object and the device's `Caps.ShaderTarget`. All fluent calls return the same sealed graph description, not native references. The compiled root layout validates exact member paths, CLR numeric types and reference kinds/element types. Missing, duplicate, unknown or mistyped members and a different pipeline artifact are rejected before execution. `SetArguments(inputs)` evaluates each `Value(path, Func<T>)` once and freezes numeric bytes. Draw/dispatch reuse that selection until the next successful SetArguments; failed selection preserves the last valid arguments. Switching to a different argument-bearing pipeline requires SetArguments again (switching back before another successful SetArguments can reuse the one last selection; this is not a per-pipeline cache). Each execution still validates the selected resource cohort and native resource lifetime, and captures indirect shader-data metadata at the ordinary native command-recording point. Argument-free pipelines need no SetArguments.

Declare the actual color outputs in `UseShaderResources(inputs, target)` and declare matching attachment state/access on that pass. No color outputs can mean compute or depth-only graphics: the artifact stage distinguishes them. Depth-only graphics uses `RenderGraphRenderPassDesc.DepthStencilAttachment`. For compute, use `using var compute = context.BeginComputePass(); compute.SetPipeline(pipeline); compute.SetArguments(inputs); compute.Dispatch(x, y, z);` with the compute artifact and matching buffer/texture declarations.

The render encoder supports pipeline/render state, viewport, scissor, blend constants, stencil reference, index buffers, `Draw`/`DrawIndexed` and their indirect forms. Compute supports `Dispatch` and `DispatchIndirect`. Native encoders never escape these wrappers. Exactly one graph encoder can be active per context. `End` ends once; `Dispose` is idempotent after ending. Context closure cleans up an outstanding encoder and preserves the original callback exception if cleanup also fails. Contexts and encoders cannot be used after the callback. Raw `context.Commands` remains for deliberate interoperability; do not mix raw passes with an active graph encoder.

An input set belongs to one pass execution unit; create another description for an independent unit. Preparation installs all native references, runs all typed resolution callbacks, then all finalizers, and only then permits recording. Any failure invalidates the entire new generation.

Independently prepared modules use the separate `CreateModuleShaderResources()` bridge, returning an opaque Abstractions `ShaderResources` with no Table accessor. `RegisterModuleTexture(resources, handle, callback)` and typed `RegisterModuleBuffer` deliver real references during preparation. `DescribeModuleShaderResources(module, artifact)` describes that same indivisible cohort for nested metadata/composite execution; it does not create an independent reservation. Ordinary graph consumers do not need this bridge. Low-level non-graph `IShaderArguments` and static codecs remain available; native `SetArguments(ShaderValueSnapshot)` is a defensive-copy interop overload.

`UseLocalBuffer` and `UseLocalShaderDataBuffer` describe borrowed storage whose effects are confined to one pass. They retain actual resource validation and synchronization declarations, cannot be exported or promoted to outputs, and cannot be used by another pass. Existing global imports keep their original observability. Locality does not transfer allocation ownership or remove the caller completion requirement.
