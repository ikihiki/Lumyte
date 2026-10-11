# Lumyte.Graphics.RenderGraph

呼び出し側が所有する texture／型付き buffer を登録し、pass のアクセス宣言から実行順と barrier を生成するライブラリです。`Lumyte.Graphics.Abstractions` だけに依存し、Wgpu／Vulkan／Browser の共通 command API へ記録します。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

// texture はこの例の前に生成する。既存データを保持する場合は実際の state／scope を指定する。
var graph = new RenderGraph();
RenderGraphTexture target = graph.ImportTexture(texture, TextureState.Undefined, default);
// view は submission の完了まで保持する。
using IGraphicsTextureView view = texture.CreateView(new TextureViewDesc
{
    Dimension = TextureViewDimension.D2,
    MipLevelCount = 1,
    ArrayLayerCount = 1,
});
graph.AddPass("clear", context =>
{
    using IRenderEncoder encoder = context.Commands.BeginRenderPass(new RenderPassDesc
    {
        ColorAttachments = new[]
        {
            new RenderColorAttachmentDesc
            {
                View = view,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                ClearValue = new ClearColor(0, 0, 0, 1),
            },
        },
    });
}).UseTexture(target, TextureState.ColorAttachment,
    new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));

CompiledRenderGraph plan = graph.Compile();
using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new CommandBufferDesc());
plan.Record(commands);
commands.Finish();
using IGraphicsSubmission submission = device.Queue.Submit(new[] { commands });
await submission.WaitAsync();
```

各 pass は render／compute／copy 命令を callback 内で記録します。encoder は callback が返る前に閉じます。callback から Finish／Submit／Wait／allocation の解放を行わず、pass 間の追加 barrier はグラフに任せます。view／pipeline／Argument Table の寿命は従来どおり管理します。backend が GPU 完了まで必要とする allocation は完了まで保持してください。

## アクセスと順序

- `ImportTexture`／`ImportBuffer<T>` は allocation 全体を登録します。同じ allocation の二重登録、別グラフの handle、pass 内の同一 resource の重複宣言を拒否します。
- `UseTexture`／`UseBuffer` の `BarrierScope.Access` から読み書きを判定します。attachment の Load は read と write、Clear は write を指定します。shader arguments、index／indirect buffer、copy 元・先も漏れなく宣言してください。buffer usage／texture state と access、stage の整合性を検証します。
- resource が競合する pass は追加順を保持します。read→write、write→read、write→write と texture layout の変更が依存関係になります。独立した pass は `DependsOn` で順序を指定でき、実行可能な pass のうち追加順が早いものを選びます。循環は Compile 時に拒否します。
- texture 全 mip／layer と buffer 全要素を barrier 範囲にします。同じ texture state の連続 write にも barrier を発行し、同じ state の read は scope を合併して次の write／layout 変更へ渡します。
- write／layout 変更から read へ移る barrier の After には、次の write／layout 変更までの全 reader stage を含めます。異なる stage の後続 read にも結果を可視化します。
- Undefined texture の初回アクセスに read を含む場合は Compile 時に拒否します。write を宣言しても実際に読み出す領域が初期化される保証は callback の責任です。attachment StoreOp.Discard 後の内容は使用しないでください。

## 記録と寿命

Compile は GPU 操作をせず、宣言と callback を snapshot します。compile 後の builder の変更は既存 plan に影響しません。ただし callback が capture するアプリケーションの mutable 値は snapshot しません。

plan の Record は一度だけです。失敗した場合も再利用できず、途中まで記録した command buffer を破棄します。callback が保持した context は callback 終了後に無効です。GetTexture／GetBuffer は当該 pass が宣言した resource だけを返します。command API を通じた実アクセスを横取りして検証するものではないため、callback は宣言を守る必要があります。

`GetFinalTextureState` と `GetFinalScope` は正常に GPU 実行された場合の予定値です。次のグラフの import や外部 command の同期へ引き継げます。実際の GPU 状態を query する値ではなく、未実行／失敗した plan の予定値を実状態として使わないでください。

初期版は単一 queue、allocation 全体の追跡、外部所有 resource の登録を扱います。resource の自動確保、pool／alias、subresource ごとの追跡、pass culling、shader data buffer、複数 queue は後続の拡張です。内部同期はなく、同時操作と同一 device の確認、resource の生存期間と submission の同期は利用者が管理します。

設計案は [GRAPHICS-0015](../../../docs/adr/graphics/GRAPHICS-0015-render-graph.md)、GPU 不要の検証は `dotnet test tests/Graphics/Lumyte.Graphics.RenderGraph.Tests -c Release` を参照してください。
