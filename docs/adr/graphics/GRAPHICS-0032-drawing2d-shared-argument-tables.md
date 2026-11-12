# ADR-GRAPHICS-0032: 描画順を保ち、GPU 上限で共有登録表を分割する

- 状態: 採用
- 日付: 2026-11-12
- 基準コミット: `05ea97fc387d7ca04293c767d04cadf4faed9ed8`

## 背景

異なる図形・クリップ・エフェクトを続けて描く利用者は、同じ画像やサンプラーの登録を有効な範囲で共有したい。基準コードでは一般描画の命令や effect pass ごとに表を作り、graph の一時資源は pass 実行時に固定 slot へ登録するため、共有と所有期間を一つの準備単位で決められない。

[引数への接続](GRAPHICS-0009-shader-argument-binding.md#drawdispatchでの解決)では論理登録容量と一 draw の物理 binding 上限を分けている。一方、基準の Vulkan と DXIL helper は slot を wire に直接格納し、Vulkan の layout は到達する最大 slot + 1 を使う。WGSL は到達資源を compact するが、slot span に応じた remap buffer のサイズに上限がある。基準 SHA の `VulkanShaderReferenceEncoding`、`VulkanShaderBinding`、`Direct3D12ShaderBinding`、`Shared/ShaderBindingData` が根拠である。

## 決定

一 draw に必要な直接・間接参照を不可分の予約にまとめ、実行順に現在の登録表へ加える。実際の backend の slot span や descriptor budget に次の draw 全体が収まらない場合だけ次の表へ移り、各表の論理容量はその表に必要な種類別の実登録数から決める。

### 利用者の変更前・変更後

標準の直接描画は、どちらも `await renderer.PrepareAsync(drawing, target)`、`Record`、GPU 完了と command disposal 後の解放を使う。Canvas に RenderGraph を必須化せず、型・クリップ・pipeline だけを理由に表を分割しない。buffer の統合、draw の並べ替え、一 draw の暗黙分割も行わない。

Graph の独自描画では、利用者は論理資源の集合と実際の描画先を宣言し、AddPass の context から実際の IGpuRef を取得する。既存の引数型と codec を使い、専用 encoder の SetArguments と Draw/Dispatch を通常と同じ形で呼ぶ。表の存在、slot、容量、partition、seal/materialize は graph の公開 API に出さない。既存の raw Commands は backend 相互接続の入口として維持する。

変更前の graph には論理入力から root を構成する入口がなく、基準 SHA の `src/Graphics/Lumyte.Graphics.ImageEffects/EffectGpuPass.cs` は table を作成し、pass 内で texture を登録して内部の `EffectArguments` を渡す。次は opacity 操作に絞った呼出しの抜粋であり、pipeline/state 設定、資源生成と Submit/Wait は省略する。bounds は画像範囲の Vector4 とする。

```csharp
IArgumentTable table = owner.Add(device.CreateArgumentTable(new() { TextureCapacity = 2, SamplerCapacity = 1 }));
IGpuRef<IGraphicsSampler> samplerReference = table.WriteSampler(0, sampler);
// pass callback 内。基準実装内部の引数型と static codec を利用する。
IGpuRef<IGraphicsTextureView> inputReference = table.WriteTexture(0, context.CreateTextureView(input.Texture));
IGpuRef<IGraphicsTextureView> backdropReference = table.WriteTexture(1, context.CreateTextureView(backdrop.Texture));
var arguments = new EffectArguments(inputReference, backdropReference, samplerReference,
    bounds, bounds, bounds, new Vector4(3, 0, 0, opacity),
    Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero);
IRenderEncoder render = context.Commands.BeginRenderPass(new()
{
    ColorAttachments = [new() { View = context.CreateTextureView(output), LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }],
});
render.SetPipeline(pipeline);
render.SetArgumentTable(table);
render.SetArguments(in arguments);
render.Draw(3);
render.End();
// owner は table と sampler を GPU 完了と command disposal 後まで保持する。
```

変更後は、論理資源を graph の公開 handle で収集し、pass callback 内で context が実際の IGpuRef に変換する。次は ImageEffects 内部の同じ既存 `EffectArguments` を使う例であり、Graph 専用の引数型を追加しない。一般の利用者も直接描画で使う自分の IShaderArguments 型を再利用できる。pipeline は同じ shader を含み、source は宣言済み画像、target は描画先、bounds は画像範囲、samplerDesc は画像用設定、sampleRead/colorWrite は対応する BarrierScope とする。資源生成、using 指示、Submit/Wait と失敗時の owner 解放は省略する。

```csharp
RenderGraphShaderResources inputs = graph.CreateShaderResources()
    .ReadTexture(source)
    .Sampler(samplerDesc);
graph.AddPass("sample", context =>
{
    var arguments = new EffectArguments(
        context.GetGpuRef(inputs, source),
        context.GetGpuRef(inputs, source),
        context.GetGpuRef(inputs, samplerDesc),
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
  .UseShaderResources(inputs, target);
```

一つの資源集合は一 pass の不可分な実行単位に接続する。別の単位は `CreateShaderResources` で作る。収集時の fluent メソッドは同じ集合を返す。IGpuRef は callback 内の GetGpuRef からだけ取得し、pending registration 型を追加しない。標準の低レベル IShaderArguments と static codec は graph 外の直接描画で維持する。基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Rendering/DrawingArguments.cs` も内部型であり、graph 利用者が実装する契約ではない。

### 論理入力と専用 encoder の契約

資源集合は artifact や root member 名を持たず、texture/view、sampler、型付き buffer の exact range/access を収集する。GetGpuRef は callback の有効期間内だけ使え、同じ graph/pass に事前登録された完全一致の key を解決する。callback 内で新しい登録や slot/capacity の変更を行わない。未知の key、別 graph/pass、未宣言の資源や過大な range は実行前に拒否する。

`SetArguments<T>(in T)` は通常の IShaderArguments codec を一度実行し、数値 bytes をコピーする。selected pipeline の compiled root schema で root/type/member/reference kind を検査し、失敗した設定は最後の有効な選択を保持する。繰返し Draw/Dispatch は同じ数値 snapshot を使う。別 pipeline に切り替える場合は再設定し、別の設定が成功する前に元の pipeline へ戻した場合だけ最後の選択を使える。pipeline 別の cache は作らない。引数のない program は設定不要である。

Graph は root 内の実参照から、当該 pass に宣言された候補集合を非公開に推定する。各実行で集合と lifetime を検査して同じ native table を選ぶ。native が command-local transfer metadata を読む通常の時点で、root と間接参照の全体が一つの事前予約集合に含まれることを検査する。同じ物理 table を共有していても、別集合の部分だけを足し合わせた一命令は拒否する。共通 root の重複除去で候補が複数残る場合も、命令全体を覆う一集合が必要である。全体に参照がなければ table を推定する必要はない。アプリに SetResources や SetArgumentTable を要求せず、global な参照 registry、実行時 reflection、新しい friend 宣言を追加しない。

UseShaderResources の出力は実際の color attachment と一致させ、同じ pass に state/access を宣言する。色出力がなくても、宣言済み depth attachment を持つ pass は graphics と判定する。compute の資源集合には render attachment を宣言しない。render は通常 IRenderEncoder と同じ Draw/DrawIndexed/indirect signature、compute は通常 IComputeEncoder と同じ Dispatch/DispatchIndirect signature を持つ。

専用 encoder は native encoder を非公開に保持し、取り出す API を設けない。pipeline、render state、viewport、scissor、blend、stencil、index を設定できる。context では同時に一つだけ開き、End は一度、Dispose は終了済みなら何もしない。callback 後の操作を拒否し、context の Close は残った encoder を終了する。callback と cleanup がともに失敗した場合も元の例外を主例外として保持する。raw Commands は相互接続用に残すが、開いている専用 encoder と混在させない。

独自 Drawing2D renderer は context.ArgumentPlan で不透明な ShaderResources を作る。独立した module だけが別入口 CreateModuleShaderResources を使い、RegisterModuleTexture/Buffer の型付き callback で実参照を受け取る。DescribeModuleShaderResources は既存の同じ予約集合を key で記述するため、composite の nested metadata と root を別の表へ分離しない。module の公開集合にも Table は出さない。実際に module が所有する GPU root buffer だけを型付きで宣言し、任意の外部 buffer は既存の bindings と宣言を必要とする。

### 容量と予約の契約

予約は一 draw/dispatch の全 root buffer、shader data 内の参照、texture、sampler を含む。一つの予約を二つの表に割り当てない。現在の表に加えると backend の直接 slot span、remap サイズ、種類別・combined binding budget を超える場合は、予約全体を空の次の表へ移す。一予約が単独でも収まらない場合は、native table の作成や payload 公開前に NotSupportedException とし、描画を勝手に分けない。

各表は種類別の登録数ちょうどの ArgumentTableDesc を使う。未使用の種類は容量 0 とし、fallback や sampler の固定空き slot を予約しない。BufferCapacity は exact range の論理登録数であり、IGpuRef の要素参照数や VRAM、device-address buffer の descriptor 数ではない。

重複除去は選ばれた表の中だけで行う。texture view と caller-owned sampler は object identity、buffer は resource、型、raw/shader-data の別、read/write 用途、正確な基点・範囲・count・stride を key にする。重なるだけの range を統合しない。別表でも同じ資源が必要なら新しい opaque reference を作り、その予約の Table に属させる。

DeviceCaps の ArgumentTableBindingLimits は論理 capacity ではなく、backend が有効にした helper の制約を報告する。Vulkan は stage/set の sampled image・sampler 上限、最低一個の texture/sampler layout、root uniform と color attachment の combined 消費を含める。sampler と device-address buffer は Vulkan の maxPerStageResources に加算しない。DXIL は native helper の種類別 slot span を報告する。WGSL は compact する draw 単位の資源数と、root/remap の予約、group entry 数、range alignment/size、remap の slot span を別々に報告する。WGSL の logical table を per-draw resource 数へ切り詰めない。

### Materialize と所有

すべての active 予約を解決・検査してから、greedy partition の全 native table を作り、全 Write 成功後に全予約の内部参照を先に設置する。内部の参照読取可能状態（state 2）へ進めてから、全 resolution callback、全 finalizer の順に実行し、その全成功後だけ recording ready に進める。callback は void 登録の `Action<IGpuRef<T>>` で実参照を受け取り、finalizer が payload を pack/upload する。失敗・取消では新規 generation 全体を無効化し、途中の native table と owner の cleanup retry を保持する。古い登録の上書き・Release、seal 後の append、他 generation の参照への読み替えはしない。

表の lease は登録資源を所有しない。[利用者が管理する寿命](GRAPHICS-0014-caller-managed-resource-validation.md#利用者が保証する条件)と同期を維持し、stale reference の検出を新たに保証しない。[保持描画](GRAPHICS-0025-retained-drawing-content.md#device-資産と-frame-の所有権)の cached asset は元の表と payload を保持し、新しい準備で再割当てしない。新規資産だけを実行順に予約する。

Direct/retained の fallback は同じ新規 plan の初回 record で一度だけ clear し、その frame 全体の GPU 完了と command disposal 後にだけ資産を resident/idle に返す。以後の独立した sibling 再利用では書き換えない。矩形の buffer pool、資格判定、1024個の run 境界と専用 shader を維持する。表は pooled buffer と別の使用権として管理する。

### RenderGraph の所有と遅延資源

非公開の graph coordinator は既存 GetOrAddExtension と Retain で graph-local に一つ作り、成功した Compile で CompiledRenderGraph に所有を移す。caller が Submit/Wait を行う契約と、完了・破棄した command に対する既存 Dispose guard は変更しない。global table pool や別 session、cross-frame mutable cache は追加しない。

Record は cull 後の live pass が使う一時資源をまず割り当て、IRenderGraphPreparation を実行してから従来の barrier/pass sweep へ進む。hook は Commands を公開せず、固定された live execution identity と、そのコンパイルで当該 pass が実際に宣言した資源だけを解決できる。宣言検査は view cache lookup より前に行い、未宣言・foreign・culled pass の解決を拒否する。

Graph の logical handle と normalized view desc から作る view は plan 所有として共用する。caller が別々に作った view を descriptor の値だけで同一化しない。graph 予約は実際の live pass 順と pass 内の draw 順へ並べ、culled 予約の resolver・登録・finalizer は実行しない。表の全内容を全 pass の依存へ追加せず、sampled source/original/geometry/instruction など当該 pass が使う資源だけを宣言する。opaque Drawing2D batch の GPU root buffer は batch が所有し、実際の copy-write と shader-read を pass-local access として宣言する。borrowed allocation と一時 allocation の所有区分を変えず、pass-local の書込みだけを graph 出力の副作用として扱わない。別 pass の使用、export、出力への昇格を拒否し、既存の global import を local に変換しない。内部 copy/barrier と初期化の責任は batch に残り、宣言検査を省略しない。

composite の GPU instruction と geometry は graph 一時資源とし、明示 copy-write producer の後で draw が読む。新しい CreateShaderDataBuffer は artifact/count/Automatic を保持し、事前 Validate で未割当て object を参照せず、最初の read に producer を要求する。普通の描画、layer/composite と ImageEffects は同じ graph allocator の予約を使う。fallback も graph transient texture とし、clear producer と sampled consumer の宣言で同期・culling する。

同期 Record を維持するため、staging の MapAsync は AppendAsync 中に await し、まだ payload を書かず owner を保持する。materialize 後の WhenReady は同期の Fill/CopyFrom/Unmap のみを行う。すべての live callback を同じ generation の失敗境界内で実行し、一つでも失敗すれば全新規参照を無効化する。packing 中は参照を読めるが、全 callback の成功前に Draw/Dispatch は実行できない。Browser/UI thread で未完了の非同期 map を GetResult しない。未記録・culled・部分失敗時の mapped owner も cleanup する。

### API 差分

基準 SHA に対する判断対象の追加を抜粋する。既存の直接描画・graph Record/Submit/Wait は維持する。公開の `ArgumentRegistration<T>` と `DrawingArgumentRegistration2D<T>` は設けず、登録は void と型付き callback にする。backend に元からある非 generic の内部 registration 表現は引き続き内部契約である。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public sealed record ShaderValueSnapshot(string RootParameter, string ShaderTypeName, IReadOnlyList<ShaderValue> Values)
     {
+        // native command-local capture が全 root/間接参照を収集した後に host 制約を適用する。
+        // 通常 snapshot は追加制約なし。Copy は内部制約も保持し、Pack callback は呼び出さない。
+        public void ValidateExecutionReferences(IReadOnlyCollection<IShaderReference> references);
     }
     public sealed record DeviceCaps
     {
+        public ArgumentTableBindingLimits ArgumentTableBindingLimits { get; init; }
     }
+    public sealed class ArgumentTablePlan : IDisposable
+    {
+        public ArgumentTablePlan(IGraphicDevice device);
+        public ArgumentTableReservation Reserve(uint colorAttachments = 1);
+        // 全 native Write → 全内部参照設置 → 全 resolution callback → 全 finalizer。
+        // 全成功後だけ記録可能。失敗は generation 全体を無効化する。
+        public void Materialize(IReadOnlyList<ArgumentTableReservation>? execution = null, CancellationToken cancellationToken = default);
+        public IDisposable Retain();
+        public void Dispose();
+    }
+    public sealed class ArgumentTableReservation
+    {
+        public IArgumentTable Table { get; }
+        public void RegisterTexture(IGraphicsTextureView view, Action<IGpuRef<IGraphicsTextureView>> resolved);
+        public void RegisterTexture(Func<IGraphicsTextureView> resolve, Action<IGpuRef<IGraphicsTextureView>> resolved);
+        public void RegisterSampler(IGraphicsSampler sampler, Action<IGpuRef<IGraphicsSampler>> resolved);
+        public void RegisterBuffer<T>(BufferSlice<T> range, Action<IGpuRef<T>> resolved, ArgumentBufferAccess access = ArgumentBufferAccess.Read) where T : unmanaged;
+        public void RegisterBuffer<T>(ShaderDataSlice<T> range, Action<IGpuRef<T>> resolved) where T : struct, IShaderData;
+    }
+    // module 用の不透明な集合。Table は公開しない。
+    public class ShaderResources
+    {
+        protected ShaderResources(ShaderResources resources);
+        // module 内部の実行制約。Graph consumer には公開しない。
+        protected bool ContainsReference(IShaderReference reference);
+        protected ShaderValueSnapshot ConstrainArguments(ShaderValueSnapshot arguments, IReadOnlyList<ShaderResources> candidates);
+        public virtual ShaderResources CreateSibling();
+        public bool IsReady { get; }
+        public void RegisterTexture(IGraphicsTextureView view, Action<IGpuRef<IGraphicsTextureView>> resolved);
+        public void RegisterSampler(IGraphicsSampler sampler, Action<IGpuRef<IGraphicsSampler>> resolved);
+        public void RegisterBuffer<T>(BufferSlice<T> range, Action<IGpuRef<T>> resolved, ArgumentBufferAccess access = ArgumentBufferAccess.Read) where T : unmanaged;
+        public void WhenReady(Action finalize);
+        public IDisposable Retain();
+        public void Cancel();
+    }
     public interface IRenderEncoder
     {
         void SetArguments<T>(in T arguments) where T : struct, IShaderArguments;
+        // 低レベル相互接続用。呼出し時に防御的コピーを行う。
+        void SetArguments(ShaderValueSnapshot arguments);
     }
     public interface IComputeEncoder
     {
+        // 低レベル相互接続用。同じ防御的コピー契約。
+        void SetArguments(ShaderValueSnapshot arguments);
     }
 }
 namespace Lumyte.Graphics.Drawing2D
 {
     public sealed class RenderPreparationContext2D
     {
+        public DrawingArgumentPlan2D ArgumentPlan { get; }
+        // device の不一致は ArgumentException。target と resource owner を維持。
+        public RenderPreparationContext2D WithArgumentPlan(DrawingArgumentPlan2D argumentPlan);
     }
 }
 namespace Lumyte.Graphics.RenderGraph
 {
+    public interface IRenderGraphPreparation : IRenderGraphLifetime
+    {
+        void Prepare(RenderGraphPreparationContext context);
+    }
     public sealed class RenderGraphPass
     {
+        // 借用 storage の作用はこの pass に限定。別 pass・export・出力化を拒否。
+        public RenderGraphPass UseLocalBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope) where T : unmanaged;
+        public RenderGraphPass UseLocalShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope) where T : struct, IShaderData;
     }
     public sealed class RenderGraph
     {
+        public RenderGraphShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, string? name = null) where T : struct, IShaderData;
     }
+    public static class RenderGraphShaderResourceExtensions
+    {
+        public static RenderGraphShaderResources CreateShaderResources(this RenderGraph graph);
+        // actual color outputs。none は pass の attachment 宣言により compute/depth-only graphics。
+        public static RenderGraphPass UseShaderResources(this RenderGraphPass pass, RenderGraphShaderResources resources, params RenderGraphTexture[] colorAttachments);
+        // 独立した module 向け。一般の graph 利用例では不要。
+        public static ShaderResources CreateModuleShaderResources(this RenderGraph graph);
+        public static void RegisterModuleTexture(this RenderGraph graph, ShaderResources resources, RenderGraphTexture texture, Action<IGpuRef<IGraphicsTextureView>> resolved, TextureViewDesc? desc = null);
+        public static void RegisterModuleBuffer<T>(this RenderGraph graph, ShaderResources resources, RenderGraphBuffer<T> buffer, ulong offset, ulong count, Action<IGpuRef<T>> resolved, ArgumentBufferAccess access = ArgumentBufferAccess.Read) where T : unmanaged;
+        public static void RegisterModuleBuffer<T>(this RenderGraph graph, ShaderResources resources, RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count, Action<IGpuRef<T>> resolved) where T : struct, IShaderData;
+        // resources と同じ不可分な予約集合を記述する。
+        public static RenderGraphShaderResources DescribeModuleShaderResources(this RenderGraph graph, ShaderResources resources);
+        public static RenderGraphPass UseShaderResources(this RenderGraphPass pass, ShaderResources resources, params RenderGraphTexture[] colorAttachments);
+    }
+    // sealed な composition。ShaderResources の派生型ではない。
+    public sealed class RenderGraphShaderResources
+    {
+        public RenderGraphShaderResources ReadTexture(RenderGraphTexture texture, TextureViewDesc? desc = null);
+        public RenderGraphShaderResources Sampler(SamplerDesc desc);
+        public RenderGraphShaderResources Sampler(IGraphicsSampler sampler);
+        public RenderGraphShaderResources ReadBuffer<T>(RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access = ArgumentBufferAccess.Read) where T : unmanaged;
+        public RenderGraphShaderResources ReadBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count) where T : struct, IShaderData;
+    }
     public sealed class RenderGraphContext
     {
+        public RenderGraphRenderEncoder BeginRenderPass(RenderGraphRenderPassDesc desc);
+        public RenderGraphComputeEncoder BeginComputePass(string? label = null);
+        // callback 内だけ。事前登録済みの exact key を解決し、容量を変更しない。
+        public IGpuRef<IGraphicsTextureView> GetGpuRef(RenderGraphShaderResources resources, RenderGraphTexture texture, TextureViewDesc? desc = null);
+        public IGpuRef<IGraphicsSampler> GetGpuRef(RenderGraphShaderResources resources, SamplerDesc desc);
+        public IGpuRef<IGraphicsSampler> GetGpuRef(RenderGraphShaderResources resources, IGraphicsSampler sampler);
+        public IGpuRef<T> GetGpuRef<T>(RenderGraphShaderResources resources, RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access = ArgumentBufferAccess.Read) where T : unmanaged;
+        public IGpuRef<T> GetGpuRef<T>(RenderGraphShaderResources resources, RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count) where T : struct, IShaderData;
     }
+    public sealed record RenderGraphRenderPassDesc
+    {
+        public string? Label { get; init; }
+        public IReadOnlyList<RenderGraphColorAttachmentDesc> ColorAttachments { get; init; } = [];
+        public RenderGraphDepthStencilAttachmentDesc? DepthStencilAttachment { get; init; }
+    }
+    public sealed record RenderGraphColorAttachmentDesc
+    {
+        public required RenderGraphTexture Texture { get; init; }
+        public TextureViewDesc? ViewDesc { get; init; }
+        public required AttachmentLoadOp LoadOp { get; init; }
+        public required AttachmentStoreOp StoreOp { get; init; }
+        public ClearColor ClearValue { get; init; }
+    }
+    public sealed record RenderGraphDepthStencilAttachmentDesc
+    {
+        public required RenderGraphTexture Texture { get; init; }
+        public TextureViewDesc? ViewDesc { get; init; }
+        public AttachmentLoadOp DepthLoadOp { get; init; } = AttachmentLoadOp.Clear;
+        public AttachmentStoreOp DepthStoreOp { get; init; } = AttachmentStoreOp.Store;
+        public float DepthClearValue { get; init; } = 1;
+        public AttachmentLoadOp StencilLoadOp { get; init; } = AttachmentLoadOp.Clear;
+        public AttachmentStoreOp StencilStoreOp { get; init; } = AttachmentStoreOp.Store;
+        public uint StencilClearValue { get; init; }
+    }
+    public sealed class RenderGraphRenderEncoder : IDisposable
+    {
+        // 数値を一度 capture。失敗時は以前の選択を保持する。
+        public void SetArguments<T>(in T arguments) where T : struct, IShaderArguments;
+        public void SetPipeline(IGraphicsPipeline pipeline);
+        public void SetRenderState(GraphicsRenderStateDesc state);
+        public void SetViewport(Viewport viewport);
+        public void SetScissor(ScissorRect scissor);
+        public void SetBlendConstant(BlendConstant value);
+        public void SetStencilReference(uint reference);
+        public void SetIndexBuffer(BufferSlice<ushort> indices);
+        public void SetIndexBuffer(BufferSlice<uint> indices);
+        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);
+        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0);
+        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments);
+        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments);
+        public void End();
+        public void Dispose();
+    }
+    public sealed class RenderGraphComputeEncoder : IDisposable
+    {
+        public void SetArguments<T>(in T arguments) where T : struct, IShaderArguments;
+        public void SetPipeline(IGraphicsComputePipeline pipeline);
+        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1);
+        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments);
+        public void End();
+        public void Dispose();
+    }
 }
```

## 検討した代替案

- 表の容量を device の per-draw 上限へ一律固定する: 論理容量と物理 binding、device address と descriptor を混同し、WGSL の有効な大きい論理表も分割してしまう。
- Vulkan の全 wire を draw-local remap に置き換える: 一表の論理範囲を広げられるが、間接 shader-data の ABI と native binding を同時に変更する。既存 ABI が使える範囲での表分割を選ぶ。
- clip・型・pipeline ごとに表を分ける: 必要な device/ownership 境界より細かくなり、異種描画の共有を妨げる。
- pass callback で空き slot をその都度上書きする: 過去の参照を保持する metadata と衝突し、準備途中の失敗も atomic にできない。
- table 全登録を全 pass の依存へ加える: 未使用資源の false hazard と culling 抑止を起こす。

## 結果と影響

標準描画と明示参加する renderer は、painter order と root/間接参照の所属を保ちながら、実際の上限まで異種命令の登録を共有できる。必要な native buffer 数や draw 数、時間短縮はこの判断だけでは保証しない。型付き resolution callback、二段階の CPU metadata、owner の使用権が必要となる。

境界は device、既に seal 済みの owner generation、独立した送信/退役、実際の helper slot/binding/range 上限である。graph は遅延資源を正常な materialization 時点で接続するが、Submit/wait を代行しない。型・clip・pipeline の切替だけでは境界にならない。

検証は exact/+1、種類別上限、combined/helper reserve、WGSL の大きい論理表、単独過大 draw の事前拒否、partition rollback、異種画像・sampler・typed range、table identity と painter order を含む。graph の undeclared/foreign 拒否、culling、transient shader-data の producer、普通描画と composite/effect の共用、view identity、未記録/未提出/完了後の cleanup retry を確認する。
