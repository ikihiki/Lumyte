# ADR-GRAPHICS-0032: 描画順を保ち、GPU 上限で共有登録表を分割する

- 状態: 採用
- 日付: 2026-11-12
- 基準コミット: `05ea97fc387d7ca04293c767d04cadf4faed9ed8`

## 背景

異なる図形・クリップ・エフェクトを続けて描く利用者は、同じ画像やサンプラーの登録を共有したい。基準コードは命令や effect pass ごとに表を作り、graph の一時資源を pass 実行時に固定 slot へ登録するため、共有・容量・所有期間を一つの準備処理で決められない。

[引数への接続](GRAPHICS-0009-shader-argument-binding.md#drawdispatchでの解決)は論理登録容量と一 draw の物理 binding 上限を分けている。一方、基準の Vulkan/DXIL wire は slot を直接格納し、WGSL の compact binding も到達する slot span に応じた remap buffer を必要とする。基準 SHA の `VulkanShaderReferenceEncoding`、`Direct3D12ShaderBinding`、`Shared/ShaderBindingData` が根拠である。間接 shader-data の upload 済み wire を、root の変更だけで別 table へ読み替えることはできない。

## 決定

直接描画では一 draw、RenderGraph では **一 pass の shader 資源宣言全体**を不可分な容量単位とし、実行順に登録表を共有する。次の単位全体が実効 GPU 上限に収まらなければ次の表へ移る。Graph 利用者は資源セットを作らず、標準の Use 宣言と callback の context だけを使う。

### 利用者の変更前・変更後

標準の直接描画は引き続き `await renderer.PrepareAsync(drawing, target)`、`Record`、GPU 完了と command disposal 後の解放を使う。Canvas に Graph を必須化せず、painter order、clip、pipeline だけを理由に表を分割しない。

基準 SHA の `src/Graphics/Lumyte.Graphics.ImageEffects/EffectGpuPass.cs` は table を作り、pass 内で texture を登録し、既存の内部 `EffectArguments` を渡す。次は同じ opacity 操作の抜粋である。pipeline/state 設定、資源生成、using 指示、Submit/Wait と owner の失敗時解放は省略する。

```csharp
IArgumentTable table = owner.Add(device.CreateArgumentTable(new() { TextureCapacity = 2, SamplerCapacity = 1 }));
IGpuRef<IGraphicsSampler> samplerReference = table.WriteSampler(0, sampler);
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

変更後は pass が Use 宣言から必要な shader 資源を認識し、callback の context が実際の IGpuRef を返す。次は同じ ImageEffects 内部の既存 EffectArguments を再利用する例である。一般の利用者も直接描画で使う自分の IShaderArguments 型と生成 codec をそのまま使える。pipeline、宣言済み source/target、samplerDesc、画像範囲の bounds、opacity と BarrierScope は呼出し側から与える。

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

資源セットの factory・接続・別名 wrapper、member 名による numeric bag、artifact/root の別途指定は設けない。Graph 専用の ImageArguments や pending registration 型も作らない。引数に参照がなければ、資源の dummy 宣言は不要である。

### Pass 宣言、容量、参照の契約

**各 draw が単独で収まっても、同じ pass の shader 宣言の合計が上限を超えれば、その pass を拒否する。利用者は AddPass を分割する。** callback を再実行して引数を調べたり、closure を解析したり、draw を暗黙に並べ替えたりしない。組込み Drawing2D は、準備時に分かる命令境界と保守的な資源上限から複数 pass を作る。任意の custom batch の Record を内部で分割できるとは保証しない。

UseTexture の Sampled/ShaderRead から、既定または明示した exact view を登録する。attachment/copy-only 宣言から sampled binding を作らない。UseBuffer/UseShaderDataBuffer は shader access を含む場合だけ whole buffer または明示した element range を登録し、read/write は scope から決める。sampler は UseSampler の値または借用 object identity で宣言し、barrier や write root を作らない。

異なる view/range は同じ allocation に対して宣言できる。binding は個別に保持し、hazard・culling・lifetime は allocation 単位で保守的に集約する。同じ exact range の shader access は一つの scope にまとめる。これは subresource hazard の最適化ではない。

GetGpuRef は callback の有効期間内だけ使え、frozen pass 宣言に対応する既存参照を解決する。未知の view/range/sampler、別 graph、未宣言資源、copy/attachment-only の shader 利用を拒否する。lookup は新しい slot/table を作らず、公開 API に容量・partition・table を出さない。BeginRenderPass は実際の attachment の state/access、個数、重複を検査する。容量準備の color 予約は pass の attachment 宣言の全個数を保守的な上限とする。

物理上限は logical capacity と区別する。Vulkan/DXIL の直接 slot span、WGSL の compact resource 数と remap span、stage/set/combined、root/remap/floor/attachment の予約、range alignment/size を検査する。Vulkan の sampler と device-address buffer は maxPerStageResources に加算しない。登録数ちょうどの ArgumentTableDesc を使い、未使用の種類の容量は 0 とする。

重複除去は同じ表の中だけで行う。texture view と借用 sampler は object identity、buffer は resource・型・raw/shader-data・用途・exact base/range/count/stride を key にする。重なるだけの range や、値が等しい別の借用 view は同一化しない。

### 引数選択と専用 encoder

SetArguments の通常 codec を一度実行し、numeric bytes を固定する。selected pipeline の compiled root schema で root/type/member/reference kind を検査し、失敗した設定は最後の有効な選択を保持する。繰返し Draw/Dispatch は再 capture しない。別 pipeline への切替は再設定が必要で、別の設定が成功する前に元へ戻した場合だけ最後の選択を再利用できる。pipeline 別の cache は作らない。

各命令で、pass 所有の参照・table・generation の有効性を検査する。間接 shader-data は通常 native と同じ command-local transfer snapshot の時点で収集し、全 root/間接参照が当該 pass の宣言で覆われることを検査する。標準 Pack の custom reference encoder の契約を狭めず、strict な snapshot/reference 検査は native admission に残す。

shader-data の producer/copy/consumer は、宣言済みの重なる element range から非公開の table coherence を計画する。同じ wire を読む間に table identity を変えず、各 pass の binding budget を個別に検査する。接続全体が表の slot/remap 範囲に収まらない場合は、native Write 前に拒否する。外部 table の参照を既に含む buffer を新しい table へ自動で repack/rebase する保証は追加しない。間接参照先も pass の標準宣言を必要とする。

専用 encoder は native encoder を非公開に保持し、SetArguments と実行を分離する。Draw/DrawIndexed/indirect と Dispatch/DispatchIndirect の型・順序・名前・既定値は通常 interface と同じである。context ごとに同時に一つだけ開き、End は一度、Dispose は終了済みなら何もしない。callback/End 後の操作を拒否し、cleanup failure は元 callback exception を置換しない。既存 raw Commands は相互接続用の低レベル入口として維持し、専用 encoder と混在させない。

### Materialize、module、所有期間

Record は live allocation を解決し、frozen な Use 宣言から全 live pass の登録と容量を準備してから、従来の barrier/callback sweep を行う。全単位の admission が成功するまで native table を作らない。全 Write 成功後に全内部参照を install し、参照読取可能な状態へ進め、全 resolution callback、全 finalizer の順に実行する。その全成功後だけ recording ready とし、失敗・取消では新規 generation 全体を無効化する。

直接描画と retained は同じ graph 非依存 allocator を使う。既に seal 済みの cached asset を新しい準備へ再登録せず、元の table/payload を保持する。fallback は新規 plan の初回 record で一度だけ初期化し、使用中の sibling 再利用で上書きしない。buffer の統合、run の暗黙分割、clip/pipeline だけを理由とする partition は行わない。

Graph の table は compiled plan が所有し、GPU 完了または command 破棄前の Dispose を拒否する。通常 pass の自動計画は compiled generation に属し、借用だけの builder の既存再 Compile 契約を不必要に変更しない。culled pass の view/range resolver・finalizer・table allocation は行わない。table の全内容を全 pass の依存へ追加しない。

Drawing2D の内部 payload 接続は公開 factory ではなく、標準の view/range/sampler 宣言に従う assembly-private bridge とする。Graph から Drawing2D.RenderGraph/ImageEffects へ限定した friend access を設ける。fault injection 用の Drawing2D.Tests は別の friend とし、独立 consumer の RenderGraph.Tests には付与しない。module の登録は宣言済み view/sampler/range/access と照合してから materialize し、未宣言 binding を追加する入口にはしない。

同期 Record を維持するため、staging の MapAsync は AppendAsync 中に await し、finalizer は同期 Fill/CopyFrom/Unmap だけを行う。普通描画・composite・ImageEffects の root と間接 metadata を同じ所有表に所属させる。composite の GPU instruction/geometry は明示 copy producer の後で shader が読む。module 所有の私的 storage の copy-write を graph 出力の副作用にせず、既存 pass-local affinity と borrowed/transient の区別を維持する。

[利用者が管理する寿命](GRAPHICS-0014-caller-managed-resource-validation.md#利用者が保証する条件)を維持し、新しい stale-reference 検出や Submit 時の再検証を保証しない。表の lease は登録資源を所有しない。失敗時の unmap/disposal は retry 可能な owner に残す。

### API 差分

基準 SHA に対する判断対象の追加を抜粋する。直接/backend の IShaderArguments と既存 codec は維持し、Graph の資源セット API は追加しない。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public sealed record DeviceCaps
     {
+        public ArgumentTableBindingLimits ArgumentTableBindingLimits { get; init; }
     }
+    public sealed class ArgumentTablePlan : IDisposable
+    {
+        public ArgumentTablePlan(IGraphicDevice device);
+        public ArgumentTableReservation Reserve(uint colorAttachments = 1);
+        public void Materialize(IReadOnlyList<ArgumentTableReservation>? execution = null, CancellationToken cancellationToken = default);
+        public IDisposable Retain();
+        public void Dispose();
+    }
     public interface IRenderEncoder
     {
+        // 低レベル相互接続用。numeric bytes と内部 execution 制約を防御的にコピー。
+        void SetArguments(ShaderValueSnapshot arguments);
     }
     public interface IComputeEncoder
     {
+        void SetArguments(ShaderValueSnapshot arguments);
     }
     public sealed record ShaderValueSnapshot
     {
+        // native command-local capture 完了後だけ host 制約を適用。通常 snapshot は追加制約なし。
+        public void ValidateExecutionReferences(IReadOnlyCollection<IShaderReference> references);
     }
 }
 namespace Lumyte.Graphics.Drawing2D
 {
+    public sealed class DrawingArgumentPlan2D : IDisposable
+    {
+        // Direct/backend module 向け。Graph-only factory callback は公開しない。
+        public DrawingArgumentPlan2D(IGraphicDevice device, ArgumentTablePlan? registrations = null);
+    }
     public interface IDrawingResourceDeclarations2D
     {
+        void UseTextureView(IGraphicsTextureView view, BarrierScope scope);
+        void UseSampler(IGraphicsSampler sampler);
+        void UseBuffer<T>(BufferSlice<T> range, BarrierScope scope) where T : unmanaged;
+        void UseShaderDataBuffer<T>(ShaderDataSlice<T> range, BarrierScope scope) where T : struct, IShaderData;
     }
 }
 namespace Lumyte.Graphics.RenderGraph
 {
     public sealed class RenderGraphPass
     {
-        // allocation の state/access 宣言。
+        // sampled shader-read の既定 view も pass の容量単位へ含める。
         public RenderGraphPass UseTexture(RenderGraphTexture texture, TextureState state, BarrierScope scope, bool discard = false);
-        // whole-buffer の state/access 宣言。
+        // shader access を含む場合は whole range を admit。copy-only は binding を作らない。
         public RenderGraphPass UseBuffer<T>(RenderGraphBuffer<T> buffer, BarrierScope scope, bool discard = false) where T : unmanaged;
+        // 同じ shader/copy 分類。producer/copy/consumer の metadata coherence も保持する。
         public RenderGraphPass UseShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, BarrierScope scope, bool discard = false) where T : struct, IShaderData;
+        // 色出力を除く shader 宣言全体が一つの容量単位。超過時は AddPass を分割する。
+        public RenderGraphPass UseTexture(RenderGraphTexture texture, TextureViewDesc view, TextureState state, BarrierScope scope);
+        public RenderGraphPass UseBuffer<T>(RenderGraphBuffer<T> buffer, ulong offset, ulong count, BarrierScope scope) where T : unmanaged;
+        public RenderGraphPass UseShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count, BarrierScope scope) where T : struct, IShaderData;
+        public RenderGraphPass UseSampler(IGraphicsSampler sampler);
+        public RenderGraphPass UseSampler(SamplerDesc desc);
+        public RenderGraphPass UseLocalBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope) where T : unmanaged;
+        public RenderGraphPass UseLocalShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope) where T : struct, IShaderData;
     }
     public sealed class RenderGraph
     {
+        public RenderGraphShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, string? name = null) where T : struct, IShaderData;
     }
     public sealed class RenderGraphContext
     {
+        public RenderGraphRenderEncoder BeginRenderPass(RenderGraphRenderPassDesc desc);
+        public RenderGraphComputeEncoder BeginComputePass(string? label = null);
+        // callback 内だけ、事前宣言の既存参照を返す。lookup で登録・容量を変更しない。
+        public IGpuRef<IGraphicsTextureView> GetGpuRef(RenderGraphTexture texture, TextureViewDesc? desc = null);
+        public IGpuRef<IGraphicsSampler> GetGpuRef(SamplerDesc desc);
+        public IGpuRef<IGraphicsSampler> GetGpuRef(IGraphicsSampler sampler);
+        public IGpuRef<T> GetGpuRef<T>(RenderGraphBuffer<T> buffer, ulong offset = 0, ulong? count = null) where T : unmanaged;
+        public IGpuRef<T> GetGpuRef<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset = 0, ulong? count = null) where T : struct, IShaderData;
     }
+    public sealed class RenderGraphRenderEncoder : IDisposable
+    {
+        // selected pipeline の通常 codec を一度 snapshot。失敗時は以前の選択を保持。
+        public void SetArguments<T>(in T arguments) where T : struct, IShaderArguments;
+        public void SetPipeline(IGraphicsPipeline pipeline);
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

render pass/attachment desc と dynamic/index state の設定は通常 backend の役割を保つ。専用 encoder に native/table getter や手動 SetResources を設けない。

## 検討した代替案

- 利用者に資源セットを作らせる: 一命令の容量単位を明示できるが、pass の Use 宣言と二重管理になる。Graph の公開契約から除き、pass 自身を容量単位にする。
- callback を試行実行して draw ごとの入力を学習する: 任意の副作用、once-only recording と失敗契約を維持できない。
- root だけ別 table へ rebase する: upload 済みの間接 wire と table identity が一致しない。ABI と shader indirection の変更ではなく、宣言済み pass の事前 admission を選ぶ。
- table 容量を per-draw 上限へ一律固定する: WGSL の有効な大きい logical table まで分割し、device-address buffer と descriptor 数も混同する。
- 全 table 内容を全 pass の依存にする: false hazard と culling 抑止を起こす。依存は当該 pass の Use 宣言だけにする。

## 結果と影響

利用者は Use 宣言だけで参照を取得し、既存引数型の SetArguments と通常の Draw/Dispatch を使える。表の共有は painter order と root/間接参照の所属を保つが、native buffer 数・draw 数や時間短縮をこの判断だけでは保証しない。

Graph では pass の宣言合計が容量単位となるため、個々の draw が収まっても pass 分割が必要になる。型付き view/range/sampler 宣言、coherent な shader-data producer/copy/consumer、completion-safe ownership が必要である。Direct/retained の単位は引き続き一 draw であり、既存資産と新規 generation の境界を混ぜない。

検証は容量 0/exact/+1、種類別・combined/root/remap/floor/attachment 上限、WGSL logical span、native Write 前の全体拒否、失敗 rollback、pass 分割と shader-data coherence、frozen 宣言・undeclared/foreign/range/view/sampler 拒否、culling、once-only callback、typed snapshot と失敗後の旧選択、GPU pixel、in-flight/未提出/完了後の cleanup retry を含む。Windows/D3D12 と Browser は compile と実行を区別する。
