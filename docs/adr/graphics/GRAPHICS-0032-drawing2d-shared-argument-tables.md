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

Graph の独自描画では、利用者は論理入力と実際の描画先を宣言し、root 引数を渡して draw/dispatch を記録する。表の存在、slot、容量、partition、seal/materialize は graph の公開 API に出さない。既存の raw Commands は backend 相互接続の入口として維持する。

次の例で BeginDraw は描画先・pipeline を設定した encoder を返す利用側 helper、ImageArguments は画像参照を持つ利用側 IShaderArguments、source/target は graph の画像、sampleRead/colorWrite は対応する BarrierScope とする。これらの定義、Submit/Wait と失敗時の owner 解放は両例で省略する。

変更前は利用側が表を作り、slot と寿命を管理する。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

IArgumentTable table = device.CreateArgumentTable(new() { TextureCapacity = 1 });
graph.AddPass("sample", context =>
{
    IGpuRef<IGraphicsTextureView> image = table.WriteTexture(0, context.CreateTextureView(source));
    IRenderEncoder render = BeginDraw(context);
    render.SetArgumentTable(table);
    var args = new ImageArguments(image);
    render.SetArguments(in args);
    render.Draw(3);
    render.End();
}).UseTexture(source, TextureState.Sampled, sampleRead)
  .UseTexture(target, TextureState.ColorAttachment, colorWrite);
// table は GPU 完了と command disposal 後まで保持して解放する。
```

変更後は論理入力を接続し、Draw が準備済み入力と root 引数を一緒に適用する。表の作成・選択・解放は graph が担当する。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

RenderGraphShaderResources inputs = graph.CreateShaderResources();
ArgumentRegistration<IGraphicsTextureView> image = inputs.ReadTexture(source);
graph.AddPass("sample", context =>
{
    IRenderEncoder render = BeginDraw(context);
    var args = new ImageArguments(image.Reference);
    inputs.Draw(render, in args, 3);
    render.End();
}).UseTexture(source, TextureState.Sampled, sampleRead)
  .UseTexture(target, TextureState.ColorAttachment, colorWrite)
  .UseShaderResources(inputs, target);
```

UseShaderResources の最後の引数は実際の color attachment 群であり、容量の指定ではない。対象は同じ pass で ColorAttachment/ColorWrite として宣言しておく。出力を指定しない接続は compute とし、Dispatch を使う。render は Draw/DrawIndexed、compute は Dispatch だけを受け付け、root の設定と実行に必要な準備を一操作で適用する。一つの入力集合は一 pass の一実行単位に接続し、別の実行単位は CreateSibling で作る。

独自 Drawing2D renderer は context.ArgumentPlan で不透明な ShaderResources を作る。Graph の module 間接続でも table plan や reservation を返さず、実際に module が所有する GPU root buffer だけを型付きで宣言する。任意の外部 buffer は既存の bindings と宣言が必要で、privateBuffers のような公開検査除外は設けない。

### 容量と予約の契約

予約は一 draw/dispatch の全 root buffer、shader data 内の参照、texture、sampler を含む。一つの予約を二つの表に割り当てない。現在の表に加えると backend の直接 slot span、remap サイズ、種類別・combined binding budget を超える場合は、予約全体を空の次の表へ移す。一予約が単独でも収まらない場合は、native table の作成や payload 公開前に NotSupportedException とし、描画を勝手に分けない。

各表は種類別の登録数ちょうどの ArgumentTableDesc を使う。未使用の種類は容量 0 とし、fallback や sampler の固定空き slot を予約しない。BufferCapacity は exact range の論理登録数であり、IGpuRef の要素参照数や VRAM、device-address buffer の descriptor 数ではない。

重複除去は選ばれた表の中だけで行う。texture view と caller-owned sampler は object identity、buffer は resource、型、raw/shader-data の別、read/write 用途、正確な基点・範囲・count・stride を key にする。重なるだけの range を統合しない。別表でも同じ資源が必要なら新しい opaque reference を作り、その予約の Table に属させる。

DeviceCaps の ArgumentTableBindingLimits は論理 capacity ではなく、backend が有効にした helper の制約を報告する。Vulkan は stage/set の sampled image・sampler 上限、最低一個の texture/sampler layout、root uniform と color attachment の combined 消費を含める。sampler と device-address buffer は Vulkan の maxPerStageResources に加算しない。DXIL は native helper の種類別 slot span を報告する。WGSL は compact する draw 単位の資源数と、root/remap の予約、group entry 数、range alignment/size、remap の slot span を別々に報告する。WGSL の logical table を per-draw resource 数へ切り詰めない。

### Materialize と所有

すべての active 予約を解決・検査してから、greedy partition の全 native table を作り、全 Write の成功後だけ実 IGpuRef を handle へ公開する。その後に payload を pack/upload する。失敗・取消では新規 generation 全体を無効化し、途中の native table と owner の cleanup retry を保持する。古い登録の上書き・Release、seal 後の append、他 generation の参照への読み替えはしない。

表の lease は登録資源を所有しない。[利用者が管理する寿命](GRAPHICS-0014-caller-managed-resource-validation.md#利用者が保証する条件)と同期を維持し、stale reference の検出を新たに保証しない。[保持描画](GRAPHICS-0025-retained-drawing-content.md#device-資産と-frame-の所有権)の cached asset は元の表と payload を保持し、新しい準備で再割当てしない。新規資産だけを実行順に予約する。

Direct/retained の fallback は同じ新規 plan の初回 record で一度だけ clear し、その frame 全体の GPU 完了と command disposal 後にだけ資産を resident/idle に返す。以後の独立した sibling 再利用では書き換えない。矩形の buffer pool、資格判定、1024個の run 境界と専用 shader を維持する。表は pooled buffer と別の使用権として管理する。

### RenderGraph の所有と遅延資源

非公開の graph coordinator は既存 GetOrAddExtension と Retain で graph-local に一つ作り、成功した Compile で CompiledRenderGraph に所有を移す。caller が Submit/Wait を行う契約と、完了・破棄した command に対する既存 Dispose guard は変更しない。global table pool や別 session、cross-frame mutable cache は追加しない。

Record は cull 後の live pass が使う一時資源をまず割り当て、IRenderGraphPreparation を実行してから従来の barrier/pass sweep へ進む。hook は Commands を公開せず、固定された live execution identity と、そのコンパイルで当該 pass が実際に宣言した資源だけを解決できる。宣言検査は view cache lookup より前に行い、未宣言・foreign・culled pass の解決を拒否する。

Graph の logical handle と normalized view desc から作る view は plan 所有として共用する。caller が別々に作った view を descriptor の値だけで同一化しない。graph 予約は実際の live pass 順と pass 内の draw 順へ並べ、culled 予約の resolver・登録・finalizer は実行しない。表の全内容を全 pass の依存へ追加せず、sampled source/original/geometry/instruction など当該 pass が使う資源だけを宣言する。opaque Drawing2D batch の GPU root buffer は batch が所有し、実際の copy-write と shader-read を pass-local access として宣言する。borrowed allocation と一時 allocation の所有区分を変えず、pass-local の書込みだけを graph 出力の副作用として扱わない。別 pass の使用、export、出力への昇格を拒否し、既存の global import を local に変換しない。内部 copy/barrier と初期化の責任は batch に残り、宣言検査を省略しない。

composite の GPU instruction と geometry は graph 一時資源とし、明示 copy-write producer の後で draw が読む。新しい CreateShaderDataBuffer は artifact/count/Automatic を保持し、事前 Validate で未割当て object を参照せず、最初の read に producer を要求する。普通の描画、layer/composite と ImageEffects は同じ graph allocator の予約を使う。fallback も graph transient texture とし、clear producer と sampled consumer の宣言で同期・culling する。

同期 Record を維持するため、staging の MapAsync は AppendAsync 中に await し、まだ payload を書かず owner を保持する。materialize 後の WhenReady は同期の Fill/CopyFrom/Unmap のみを行う。すべての live callback を同じ generation の失敗境界内で実行し、一つでも失敗すれば全新規参照を無効化する。packing 中は参照を読めるが、全 callback の成功前に Draw/Dispatch は実行できない。Browser/UI thread で未完了の非同期 map を GetResult しない。未記録・culled・部分失敗時の mapped owner も cleanup する。

### API 差分

基準 SHA に対する判断対象の追加を抜粋する。既存の直接描画・graph Record/Submit/Wait の呼出し API は削除しない。新しい friend 宣言や実行時 reflection を production の接続に使わない。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public sealed record DeviceCaps
     {
+        // backend の slot span、種類別/combined budget、helper reserve、range 制約。
+        public ArgumentTableBindingLimits ArgumentTableBindingLimits { get; init; }
     }
+    public sealed class ArgumentTablePlan : IDisposable
+    {
+        public ArgumentTablePlan(IGraphicDevice device);
+        public ArgumentTableReservation Reserve(uint colorAttachments = 1);
+        // 全予約を解決し、重複・foreign・rolled-back scope を拒否。null は収集順。
+        public void Materialize(IReadOnlyList<ArgumentTableReservation>? execution = null, CancellationToken cancellationToken = default);
+        public IDisposable Retain();
+        public void Dispose();
+    }
+    public sealed class ArgumentTableReservation
+    {
+        public IArgumentTable Table { get; }
+        public ArgumentRegistration<IGraphicsTextureView> RegisterTexture(IGraphicsTextureView view);
+        public ArgumentRegistration<IGraphicsTextureView> RegisterTexture(Func<IGraphicsTextureView> resolve);
+        public ArgumentRegistration<IGraphicsSampler> RegisterSampler(IGraphicsSampler sampler);
+        public ArgumentRegistration<T> RegisterBuffer<T>(BufferSlice<T> range, ArgumentBufferAccess access = ArgumentBufferAccess.Read) where T : unmanaged;
+        public ArgumentRegistration<T> RegisterBuffer<T>(ShaderDataSlice<T> range) where T : struct, IShaderData;
+    }
+    public sealed class ArgumentRegistration<T>
+    {
+        // 全 native Write 成功前、失敗後、owner 解放後は使用不可。
+        public IGpuRef<T> Reference { get; }
+    }
 }
 namespace Lumyte.Graphics.Drawing2D
 {
     public sealed class RenderPreparationContext2D
     {
+        public DrawingArgumentPlan2D ArgumentPlan { get; }
+        // device が違う場合は ArgumentException。target と resource owner を維持。
+        public RenderPreparationContext2D WithArgumentPlan(DrawingArgumentPlan2D argumentPlan);
     }
 }
 namespace Lumyte.Graphics.Abstractions
 {
+    // 不透明な shader 入力と実行。表・slot・所有 plan は公開しない。
+    public class ShaderResources
+    {
+        protected ShaderResources(ShaderResources resources);
+        public virtual ShaderResources CreateSibling();
+        public bool IsReady { get; }
+        public void WhenReady(Action finalize);
+        public IDisposable Retain();
+        public void Cancel();
+        public void Draw<T>(IRenderEncoder encoder, in T arguments, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) where T : struct, IShaderArguments;
+        public void DrawIndexed<T>(IRenderEncoder encoder, in T arguments, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) where T : struct, IShaderArguments;
+        public void Dispatch<T>(IComputeEncoder encoder, in T arguments, uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1) where T : struct, IShaderArguments;
+    }
 }
 namespace Lumyte.Graphics.RenderGraph
 {
+    public interface IRenderGraphPreparation : IRenderGraphLifetime
+    {
+        void Prepare(RenderGraphPreparationContext context);
+    }
     public sealed class RenderGraphPass
     {
+        // 借用 storage の作用はこの pass に限定。別 pass の使用・export・出力化を拒否。
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
+        // actual color outputs を渡す。none は compute。foreign/重複/未宣言の出力は拒否。
+        public static RenderGraphPass UseShaderResources(this RenderGraphPass pass, ShaderResources resources, params RenderGraphTexture[] colorAttachments);
+        public static IGraphicsSampler GetSampler(this RenderGraph graph, SamplerDesc desc);
+    }
+    public sealed class RenderGraphShaderResources : ShaderResources
+    {
+        public override RenderGraphShaderResources CreateSibling();
+        public ArgumentRegistration<IGraphicsTextureView> ReadTexture(RenderGraphTexture texture, TextureViewDesc? desc = null);
+        public ArgumentRegistration<T> ReadBuffer<T>(RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access = ArgumentBufferAccess.Read) where T : unmanaged;
+        public ArgumentRegistration<T> ReadBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count) where T : struct, IShaderData;
+        public ArgumentRegistration<IGraphicsSampler> Sampler(SamplerDesc desc);
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

標準描画と明示参加する renderer は、painter order と root/間接参照の所属を保ちながら、実際の上限まで異種命令の登録を共有できる。必要な native buffer 数や draw 数、時間短縮はこの判断だけでは保証しない。scope-local handle、二段階の CPU metadata、owner の使用権が必要となる。

境界は device、既に seal 済みの owner generation、独立した送信/退役、実際の helper slot/binding/range 上限である。graph は遅延資源を正常な materialization 時点で接続するが、Submit/wait を代行しない。型・clip・pipeline の切替だけでは境界にならない。

検証は exact/+1、種類別上限、combined/helper reserve、WGSL の大きい論理表、単独過大 draw の事前拒否、partition rollback、異種画像・sampler・typed range、table identity と painter order を含む。graph の undeclared/foreign 拒否、culling、transient shader-data の producer、普通描画と composite/effect の共用、view identity、未記録/未提出/完了後の cleanup retry を確認する。
