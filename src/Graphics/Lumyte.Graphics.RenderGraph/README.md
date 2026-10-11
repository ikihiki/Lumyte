# Lumyte.Graphics.RenderGraph

共通 command API 上で pass の依存関係、barrier、不要 pass の除去、一時 texture／buffer の寿命を管理します。Wgpu／Vulkan／Browser が同じグラフを利用でき、core は `Lumyte.Graphics.Abstractions` だけに依存します。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

var graph = new RenderGraph(device, "frame");
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

Compile は宣言と callback を snapshot します。builder の後続変更は既存 plan に影響しません。callback が capture する mutable 値は利用者が管理します。`PassNames` は実行順、`CulledPassNames` は除去された pass、`Statistics` は固定サイズの件数です。`Snapshot` は単発要求がある場合だけ生成される nullable な診断用情報で、native resource と callback を保持しません。

Record は一度だけで、callback 失敗時も消費されます。context の Get 系 API は当該 pass の宣言だけを解決し、callback 後は無効です。encoder を End してから返し、callback 内で Finish／Submit／Wait／resource 解放を行わないでください。実命令と宣言の一致、同一 device と owner thread は利用者の契約です。

plan は一時 allocation と context が作成した view を所有します。GPU 完了後に view→allocation の順で Dispose します。Record が失敗した場合は command buffer を破棄してから plan を解放します。Dispose は GPU を待ちません。記録先が Completed／Faulted／Disposed になるまで拒否します。plan の Get 系 API が返す出力は借用で、Record 成功後から所有者の Dispose まで有効です。

外部 allocation／view／pipeline／Argument Table と command／submission の寿命は利用者が管理します。外部 command で一時出力を使う場合も、その GPU 完了まで元 plan を保持してください。

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
