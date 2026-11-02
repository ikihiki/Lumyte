# ADR-GRAPHICS-0010: Depth／Stencil attachment

- 状態: 採用
- 日付: 2026-10-10
- 基準コミット: `5de6d3661ec4517938285f4a687244fe19e0362d`

## 背景

3D描画の利用者は、手前の物体が奥の物体を隠す深度判定と、描画領域を制限するステンシル判定を必要とする。基準コミットでは判定を設定する型はあるが、描画先を指定する `RenderPassDesc` がカラー画像だけを受け取るため、これらの判定を有効にした描画はできない。

## 決定

描画処理のまとまりである render pass に、深度・ステンシルを保存する任意の描画先を追加する。利用者は描画先と判定条件を別々に設定し、同じ pass の描画で共有できる。カラーと独立した読み込み・保存方法を指定できるよう、カラー描画先の配列とは別のプロパティにする。

### 利用者の変更前・変更後

変更前は、`5de6d36` の `src/Graphics/Lumyte.Graphics.Abstractions/RenderPassDesc.cs` の `ColorAttachments` でカラー描画先だけを指定する。同ディレクトリの `TextureFormat.cs` に深度形式はなく、`DepthStencilStateDesc.cs` の判定条件を有効にしても、対応する深度画像を pass に渡す方法がない。

```csharp
var pass = new RenderPassDesc { ColorAttachments = [colorAttachment] };
var state = new GraphicsRenderStateDesc
{
    ColorTargets = [new()],
    DepthStencil = new() { DepthTestEnable = false, DepthWriteEnable = false },
};
```

変更後は、以下の提案のように深度描画先を指定して判定を有効にする。`depthView` はカラー描画先と同じ寸法の `Depth32Float` 画像から作る単一 mip・単一 layer の D2 view とする。深度を1で初期化し、より手前または同じ深度の画素を描画して深度を書き込む。

```csharp
var pass = new RenderPassDesc
{
    ColorAttachments = [colorAttachment],
    DepthStencilAttachment = new()
    {
        View = depthView,
        DepthLoadOp = AttachmentLoadOp.Clear,
        DepthStoreOp = AttachmentStoreOp.Store,
        DepthClearValue = 1,
    },
};
var state = new GraphicsRenderStateDesc
{
    ColorTargets = [new()],
    DepthStencil = new()
    {
        DepthTestEnable = true,
        DepthWriteEnable = true,
        DepthCompare = CompareFunction.LessOrEqual,
    },
};
```

両例は pass と描画状態の設定部分に絞り、画像・view・command の生成と解放を省略している。変更後は pass 開始前に深度画像を `DepthStencilAttachment` 状態へ遷移させ、`BeginRenderPass(pass)` と `SetRenderState(state)` で使用する。pipeline と動的な描画状態の設定、描画、pass 終了、送信と完了待機は利用者が行い、view と画像は GPU 使用完了まで保持する。

### 描画先と判定の契約

[GRAPHICS-0003](GRAPHICS-0003-textures-and-views.md) の「範囲と責務」、[GRAPHICS-0007](GRAPHICS-0007-command-buffers-and-submission.md) の「Render／compute pass」、[GRAPHICS-0008](GRAPHICS-0008-pipeline-programs-and-render-state.md) の「初期のresource境界」を拡張する。colorを省略したdepth-only passも許可する。少なくとも一つのattachmentが必要で、すべて単一mip・単一layerのD2 viewで寸法が一致する。color slotにはcolor format、depth/stencil slotにはdepth formatのみ許可する。

Depth32FloatとDepth24Stencil8を用意する。後者のdepth精度は24bit以上で、stencilは8bitとする。正確なdepthのメモリ表現を共通契約にしない。今回のdepth textureはRenderAttachment用途に限定し、samplingとcopyは対象外とする。不対応の用途は生成時に拒否する。

load/storeはdepthとstencilで独立する。depth clearは有限の0..1、stencil clear/reference/maskは0..255。Depth32Floatではstencil operationは使わず、stencil testは拒否する。depth/stencil attachmentのないpassでdepth test/writeやstencil testを有効にしたdrawは拒否する。

DepthStencilAttachment状態とdepth/stencil stage/accessを追加する。利用者がpass開始前にtexture barrierを記録し、実行終了までviewとtextureを保持する。暗黙のbarrier、submit、待機、内部同期を追加しない。既存のshader programと描画状態の分離を保ち、backendは描画時のattachment formatをネイティブpipelineの生成・再利用条件へ含める。

### 公開API

比較元は基準コミット。既存型は変更対象メンバーのみ示す。

```diff
 namespace Lumyte.Graphics.Abstractions;
 public sealed record RenderPassDesc
 {
-    public required IReadOnlyList<RenderColorAttachmentDesc> ColorAttachments { get; init; }
+    // 既定は空。depth-only passでは空を許可する。
+    public IReadOnlyList<RenderColorAttachmentDesc> ColorAttachments { get; init; } = [];
+    public RenderDepthStencilAttachmentDesc? DepthStencilAttachment { get; init; }
 }
 public enum TextureFormat
 {
+    Depth32Float,
+    // depthは24bit以上、stencilは8bit。
+    Depth24Stencil8,
 }
 public enum TextureState
 {
+    DepthStencilAttachment,
 }
 public enum PipelineStage
 {
+    // early/late depth/stencil tests。AllGraphics/AllCommandsにも含める。
+    DepthStencil = 256,
 }
 public enum ResourceAccess
 {
+    DepthStencilRead = 1024,
+    DepthStencilWrite = 2048,
 }
+public sealed record RenderDepthStencilAttachmentDesc
+{
+    // Gets the single-mip, single-layer depth or depth/stencil view.
+    public required IGraphicsTextureView View { get; init; }
+
+    // Gets the depth load operation.
+    public AttachmentLoadOp DepthLoadOp { get; init; } = AttachmentLoadOp.Clear;
+
+    // Gets the depth store operation.
+    public AttachmentStoreOp DepthStoreOp { get; init; } = AttachmentStoreOp.Store;
+
+    // Gets the finite clear depth in the inclusive zero to one range.
+    public float DepthClearValue { get; init; } = 1;
+
+    // Gets the stencil load operation, used only for stencil formats.
+    public AttachmentLoadOp StencilLoadOp { get; init; } = AttachmentLoadOp.Clear;
+
+    // Gets the stencil store operation, used only for stencil formats.
+    public AttachmentStoreOp StencilStoreOp { get; init; } = AttachmentStoreOp.Store;
+
+    // Gets the eight-bit stencil clear value.
+    public uint StencilClearValue { get; init; }
+}
```

## 検討した代替案

ColorAttachmentsにdepthを混在させる方式は、color locationとdepth/stencilの独立したload/storeの契約を曖昧にするため採用しない。描画ごとのdepth texture指定はpassのattachment寿命とload/storeの単位を失うため採用しない。

## 結果と影響

既存のdepth/stencil状態が通常の3D描画とstencil maskに利用できる。depth-only passを許可することでshadow描画の基盤になる。利用者は深度画像の確保、状態遷移、寿命を管理し、backendはformat、用途、寸法、pipeline状態の組み合わせを検証する必要がある。

共通APIを使うGPUテストで、重なった物体の遮蔽、ステンシルによる描画範囲の制限、カラー描画先のない深度書き込みを確認する。深度画像のsamplingとcopyは今回の検証範囲に含めない。
