# ADR-GRAPHICS-0023: 明示的な操作を保つGraphics APIの補助

- 状態: 採用
- 日付: 2026-10-26

## 背景

raw bufferとshader data bufferは要素範囲を扱うが、SliceとSliceElements、MapAsyncのキャンセル引数が異なる。
byte単位と要素単位の操作も同じoffsetという引数名を使う。
描画では利用しないblendとstencilの値もpassごとに明示する必要があり、同じ初期化が繰り返される。
既存の明示的な同期と所有権を維持しながら、単位と操作の意図を読み取りやすくする。

## 決定

既存interfaceのメンバーと型名を維持し、Abstractionsに拡張メソッドを追加する。
新しいinterfaceメンバーを必須にせず、外部backend実装と既存binaryの契約を保つ。
raw bufferはhost layout、shader data bufferはshader ABIのpackingという違いを維持する。

比較元はmain `16d856a`。公開APIの追加は次のとおり。

```diff
 namespace Lumyte.Graphics.Abstractions;
+public static class GraphicsBufferExtensions
+{
+    // どちらも要素単位。非所有で、空・範囲外は既存Sliceと同じ例外。
+    public static BufferSlice<T> SliceElements<T>(this IGraphicsBuffer<T> buffer, ulong elementOffset, ulong elementCount) where T : unmanaged;
+    public static ShaderDataSlice<T> Slice<T>(this IGraphicsShaderDataBuffer<T> buffer, ulong elementOffset, ulong elementCount) where T : struct, IShaderData;
+    // byte単位の明示名。既存のmapping・範囲・memory方向の条件を維持。
+    public static void ValidateByteRange<T>(this IGraphicsBuffer<T> buffer, ulong byteOffset, ulong byteLength) where T : unmanaged;
+    public static void CopyBytesFrom<T>(this IGraphicsBuffer<T> buffer, ReadOnlySpan<byte> source, ulong byteOffset, ulong byteLength) where T : unmanaged;
+    public static void CopyBytesTo<T>(this IGraphicsBuffer<T> buffer, Span<byte> destination, ulong byteOffset, ulong byteLength) where T : unmanaged;
+    // native mapping完了後にキャンセルを確認し、成功したmappingをUnmapしてからキャンセルを返す。
+    // 待機だけを中断して進行中のmappingを残さない。
+    public static ValueTask MapAsync<T>(this IGraphicsShaderDataBuffer<T> buffer, CancellationToken cancellationToken) where T : struct, IShaderData;
+}
+public static class GraphicsDeviceExtensions
+{
+    // artifactのABIを使うCreateBufferの明示名。所有権・制約は変更しない。
+    public static IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(this IGraphicDevice device, ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic) where T : struct, IShaderData;
+}
+public static class RenderEncoderExtensions
+{
+    // 呼び出した時に4つのdynamic stateを明示設定する。後続の個別変更も可能。
+    public static void SetDynamicState(this IRenderEncoder encoder, Viewport viewport, ScissorRect scissor, BlendConstant blendConstant = default, uint stencilReference = 0);
+}
 public sealed record GraphicsRenderStateDesc
 {
+    // 指定個数の既定color stateを持つ。0はdepth-only、負数はArgumentOutOfRangeException。
+    public static GraphicsRenderStateDesc ForColorTargets(int colorAttachmentCount);
 }
```

SetDynamicStateは既存setterの順に処理し、失敗した場合はそれ以前の設定を巻き戻さない。
passの自動開始、GPU copy、barrier、submit、GPU待機は行わない。
IGraphicDeviceは操作を借用するinterfaceとして維持し、所有者による解放をXML documentationで明示する。

### Argument tableの役割

[GRAPHICS-0009](GRAPHICS-0009-shader-argument-binding.md)のtable選択に関する決定だけを置き換える。
SetArgumentTableは互換性のために残す現在操作の検査であり、後続の参照解決を切り替えない。
bindingは引数に含まれるIGpuRefが保持する登録情報から生成する。
一回のdraw/dispatchで参照する登録が同じdeviceの同じtableに属し、有効であることは利用者が保証する。
[GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)に従い、検証専用の参照再走査は追加しない。
root引数がないpipelineへの切り替えでは以前のsnapshotを使わない。
root引数がある別pipelineにはSetArgumentsを再度必要とし、既存の誤使用検出を維持する。

## 検討した代替案

- 既存型名・メソッド名を一斉変更する: binaryと外部backendの移行が必要となるため、明示名を追加する。
- 各passに暗黙のdynamic stateを挿入する: 初期化の有無が見えなくなるため、利用者が呼ぶ補助APIとする。
- bufferの共通基底interfaceを作る: raw layoutとshader ABI、readback対応の違いを隠すため採用しない。
- SetArgumentTableに登録検証を復活させる: GRAPHICS-0014の寿命・登録管理方針と矛盾する。

## 結果と影響

既存コードを変更せず、要素単位・byte単位・shader ABIという操作意図を新規コードで明示できる。
拡張メソッドを利用するにはAbstractions名前空間が必要になる。
mappingキャンセルはnative完了まで返らない点をraw bufferと揃える。
CPUのfake実装でキャンセル前後のmapping状態、範囲の単位、描画初期化を検証する。
backendの描画設定とpipeline切り替えは別途backend契約テストで確認する。
