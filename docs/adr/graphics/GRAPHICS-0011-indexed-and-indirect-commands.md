# ADR-GRAPHICS-0011: Indexed drawとIndirect実行

- 状態: 採用
- 日付: 2026-10-10

## 背景

[GRAPHICS-0007](GRAPHICS-0007-command-buffers-and-submission.md)のdirect draw/dispatchを、インデックス付きmeshとGPU生成の命令へ拡張する。CPUへのreadbackを要求せず、利用者が明示的に転送と同期を管理する。

## 決定

SetIndexBufferはushortまたはuintのBufferSliceを受け取り、型からIndexFormatを決める。firstIndexはslice先頭からの要素offsetとし、baseVertexはsigned offsetとする。indexの値のCPU走査は行わない。device、Index usage、非mapped状態、rangeを検証し、direct indexed drawではfirstIndexとindexCountが設定範囲内か検証する。strip topologyのStripIndexFormatは設定した型と一致させ、最大index値をprimitive restartとして扱う。nonindexed drawではStripIndexFormatを指定しない。

Indirectは1回の呼び出しで1命令を実行する。命令レコードは4byte単位の順序固定structとし、drawは16byte、indexed drawは20byte、dispatchは12byteである。命令bufferはBufferUsage.Indirectを要求し、sliceは1要素、byte offsetは4byte alignment、同じdevice・非mapped・有効寿命でなければならない。CPUからの設定にはUploadからの明示的CopyBuffer、GPU生成にはShaderWriteと利用者指定のbarrierを使う。

Indirect内容をCPUで読むことや自動補正は行わない。利用者はindex範囲・dispatch limits等を満たす内容を用意する。portable indirect drawのFirstInstanceは0とする。zero countはGPU上のno-opとして許可する。multi-draw、count bufferを使う命令、optional featureに依存するnonzero FirstInstanceは別の拡張とする。

IndexInput/DrawIndirect stageとIndexRead/IndirectRead accessを追加する。command bufferは命令buffer/index bufferも実行資源として追跡し、submit時の寿命とmapped状態を検証する。bufferの寿命・同期は利用者が管理し、暗黙のコピー、submit、待機を追加しない。

Indirectは描画回数やworkgroup数だけをGPU側で決める。pipeline、root引数、ArgumentTable、attachmentは呼び出し側で設定する。[GRAPHICS-0009](GRAPHICS-0009-shader-argument-binding.md)の依存収集は設定済みのrootと登録rangeを対象とする。GPUが選ぶ任意の参照をCPUで予測する機構は追加しない。必要な参照集合をあらかじめroot/rangeに含め、backend limitsを超えた場合は失敗にする。

### 公開API

比較元: main（5de6d36）。既存型は追加メンバーのみ示す。

```diff
 namespace Lumyte.Graphics.Abstractions;
 public interface IRenderEncoder
 {
+    // Index usage、非mapped、同じdevice。firstIndexはこの範囲からの相対offset。
+    void SetIndexBuffer(BufferSlice<ushort> indices);
+    void SetIndexBuffer(BufferSlice<uint> indices);
+    void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0);
+    // 各sliceは1要素。内容をCPUでは解析しない。
+    void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments);
+    void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments);
 }
 public interface IComputeEncoder
 {
+    void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments);
 }
 public enum BufferUsage
 {
+    Indirect = 32,
 }
 public enum PipelineStage
 {
+    DrawIndirect = 4,
+    IndexInput = 8,
 }
 public enum ResourceAccess
 {
+    IndexRead = 16,
+    IndirectRead = 32,
 }
+public struct DrawIndirectArguments
+{
+    // Specifies VertexCount.
+    public uint VertexCount;
+
+    // Specifies InstanceCount.
+    public uint InstanceCount;
+
+    // Specifies FirstVertex.
+    public uint FirstVertex;
+
+    // Specifies FirstInstance. Must be zero for portable indirect draws.
+    public uint FirstInstance;
+}
+public struct DrawIndexedIndirectArguments
+{
+    // Specifies IndexCount.
+    public uint IndexCount;
+
+    // Specifies InstanceCount.
+    public uint InstanceCount;
+
+    // Specifies FirstIndex.
+    public uint FirstIndex;
+
+    // Specifies BaseVertex.
+    public int BaseVertex;
+
+    // Specifies FirstInstance. Must be zero for portable indirect draws.
+    public uint FirstInstance;
+}
+public struct DispatchIndirectArguments
+{
+    // Specifies GroupCountX.
+    public uint GroupCountX;
+
+    // Specifies GroupCountY.
+    public uint GroupCountY;
+
+    // Specifies GroupCountZ.
+    public uint GroupCountZ;
+}
```

## 検討した代替案

IndirectをCPU readback後のdirect命令へ変換する方式はGPU生成と同期の明示性を損なう。bufferをbyte型に限定すると命令のsizeとlayoutを利用側で毎回扱うため、共通の命令structを用意する。全backendのmulti-drawやcount bufferを必須にするとportableな単一命令を超えるため、今回の範囲に含めない。

## 結果と影響

通常のindexed mesh描画とGPU生成draw/dispatchが可能になる。命令内容を読み出さないため、内容の正しさは利用者の責任になる。共通APIのみで16/32bit index、slice offset、baseVertex、indirect draw/indexed draw、GPU生成dispatchと明示的barrierを検証し、nativeとBrowserの既存CIで実行する。backend固有の実装は各READMEに記載する。
