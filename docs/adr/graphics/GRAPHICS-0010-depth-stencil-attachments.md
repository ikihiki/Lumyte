# ADR-GRAPHICS-0010: Depth／Stencil attachment

- 状態: 採用
- 日付: 2026-10-10

## 背景

3D描画にはdepthによる遮蔽とstencilによる領域制御が必要である。[GRAPHICS-0003](GRAPHICS-0003-textures-and-views.md)のtexture/viewと[GRAPHICS-0007](GRAPHICS-0007-command-buffers-and-submission.md)のrender scopeを拡張し、[GRAPHICS-0008](GRAPHICS-0008-pipeline-programs-and-render-state.md)のdepth/stencil状態を実行可能にする。

## 決定

RenderPassへ任意のdepth/stencil attachmentを追加する。colorを省略したdepth-only passも許可する。少なくとも一つのattachmentが必要で、すべて単一mip・単一layerのD2 viewで寸法が一致する。color slotにはcolor format、depth/stencil slotにはdepth formatのみ許可する。

Depth32FloatとDepth24Stencil8を用意する。後者のdepth精度は24bit以上で、stencilは8bitとする。正確なdepthのメモリ表現を共通契約にしない。今回のdepth textureはRenderAttachment用途に限定し、samplingとcopyは対応するaspect・shader ABIを設計する際に追加する。不対応の用途は生成時に拒否する。

load/storeはdepthとstencilで独立する。depth clearは有限の0..1、stencil clear/reference/maskは0..255。Depth32Floatではstencil operationは使わず、stencil testは拒否する。depth/stencil attachmentのないpassでdepth test/writeやstencil testを有効にしたdrawは拒否する。

DepthStencilAttachment状態とdepth/stencil stage/accessを追加する。利用者がpass開始前にtexture barrierを記録し、実行終了までviewとtextureを保持する。暗黙のbarrier、submit、待機、内部同期を追加しない。既存のPSOと描画状態の分離を保ち、backendは描画時のattachment formatをpipeline variantの条件へ含める。backend固有の対応は各プロジェクトREADMEへ記載する。

### 公開API

比較元: main（5de6d36）。既存型は変更対象メンバーのみ示す。

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

既存のdepth/stencil状態が通常の3D描画とstencil maskに利用できる。depth-only passを許可することでshadow描画の基盤になる。format、用途、寸法、pipeline状態の組み合わせをbackendが検証する必要がある。共通APIのみを使う遮蔽・stencil・depth-onlyのGPUテストを既存CIで実行する。
