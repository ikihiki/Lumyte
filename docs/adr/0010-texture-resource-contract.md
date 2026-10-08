# ADR-0010: テクスチャ・ビュー・転送の利用 API とバックエンド実装契約

- 状態: 提案
- 日付: 2026-10-07

## 背景

Texture を単なる画像 handle として扱うと、mip／layer／aspect、format、sample count、用途、コピーの pitch と読み戻しの意味が不明になる。[ADR-0006](0006-render-encoder.md) の attachment と [ADR-0005](0005-shader-compilation-and-data-interop.md) のシェーダー参照が同じ subresource を扱える設計が必要である。

本 ADR を TextureDesc、TextureViewDesc、TextureCopyDesc の正本とする。Sampler は [ADR-0011](0011-sampler-resource-contract.md)、Buffer は [ADR-0009](0009-buffer-resource-contract.md) に分離する。以下は広い共通 API の提案であり、実装済みの単一 RGBA8 target は [ADR-0008](0008-wgpu-first-backend.md) に従う。

## 決定

### 利用側の公開 API

Core 型は `Lumyte.Graphics`。Readback は利用者が staging buffer へのコピーと送信・完了待機を行い、ADR-0009 の `BufferSlice<byte>.CopyTo` で CPU bytes を読む。IGraphicsTexture と IGraphicsTextureView は所有 interface とし、backend の具象 class が公開 interface を直接実装する。Device／CreateView はその instance を返す。native image／view、所属 Device、属性、世代、lease は具象 class の内部で管理し、共通層に個別の token 登録表を置かない。利用者による具象 class の直接 constructor、native image／view handle の取得、CPU map は提供しない。

API 差分の比較元は origin/main（Graphics API は未導入）。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed class GraphicsDevice : IDisposable
+    {
+        // Desc と ViewFormats を snapshot
+        // 形式・用途・寸法・容量を検証して生成
+        public Result<IGraphicsTexture> CreateTexture(TextureDesc desc);
+    }
+
+    public interface IGraphicsTexture : IDisposable
+    {
+        // 確定した論理属性
+        // 生成後は不変
+        public TextureDimension Dimension { get; }
+
+        // 確定した論理属性
+        // 生成後は不変
+        public Extent3D Size { get; }
+
+        // 確定した論理属性
+        // 生成後は不変
+        public TextureFormat Format { get; }
+
+        // 確定した論理属性
+        // 生成後は不変
+        public TextureUsage Usage { get; }
+
+        // 確定した論理属性
+        // 生成後は不変
+        public uint MipLevels { get; }
+
+        // 確定した論理属性
+        // 生成後は不変
+        public uint SampleCount { get; }
+
+        // 所属 texture の互換な subresource view を生成
+        // null の count／format を解決して保持
+        public Result<IGraphicsTextureView> CreateView(TextureViewDesc desc);
+
+        // 範囲内 mip の寸法
+        // D3 の depth は縮小、D2 の array layer 数は一定
+        public Extent3D GetMipSize(uint mipLevel);
+
+        // idle 時の即時解放、idempotent
+        // 使用中の解放は拒否
+        public void Dispose();
+    }
+
+    public interface IGraphicsTextureView : IDisposable
+    {
+        // 元 texture と正規化済みの形式・dimension・aspect・mip／layer 範囲
+        // 物理 descriptor は公開しない
+        public IGraphicsTexture Texture { get; }
+
+        // 元 texture と正規化済みの形式・dimension・aspect・mip／layer 範囲
+        // 物理 descriptor は公開しない
+        public TextureViewInfo Info { get; }
+
+        // idle 時の即時解放、idempotent
+        // 使用中の解放は拒否
+        public void Dispose();
+    }
+
+    public sealed class DeviceCaps
+    {
+        // sampled、filterable、comparison-sampleable、storage read／write、renderable、blendable、sample count、互換 view format
+        public FormatCapabilities GetFormatCapabilities(TextureFormat format);
+    }
+
+    public sealed class CommandEncoder : IDisposable
+    {
+        // source CopySource、destination CopyDestination
+        // パス外、同じ Device
+        public void RecordCopyBufferToTexture(BufferSlice<byte> source, IGraphicsTexture destination, TextureCopyDesc desc);
+
+        // 逆方向のコピー
+        // source CopySource、destination CopyDestination
+        // 同じ layout 検証
+        public void RecordCopyTextureToBuffer(IGraphicsTexture source, BufferSlice<byte> destination, TextureCopyDesc desc);
+
+        // 同 format、同 extent、sample count 1 の color
+        // 範囲、用途、alias を検証
+        public void RecordCopyTexture(IGraphicsTexture source, TextureRegion sourceRegion, IGraphicsTexture destination, TextureRegion destinationRegion);
+    }
+}
+
```

```diff
+namespace Lumyte.Graphics
+{
+    // TextureViewInfo は immutable record
+    // Format、Dimension、Aspect、BaseMipLevel、解決済み MipLevelCount、BaseArrayLayer、解決済み ArrayLayerCount を持つ
+    // TextureRegion は init-only record で uint MipLevel = 0、Origin3D Origin = (0,0,0)、required Extent3D Extent を持つ
+    // 初期コピーは color の Aspect.All のみで、aspect field は追加しない
+    public sealed record TextureViewInfo
+    {
+        public TextureFormat Format { get; }
+        public TextureViewDimension Dimension { get; }
+        public TextureAspect Aspect { get; }
+        public uint BaseMipLevel { get; }
+        public uint MipLevelCount { get; }
+        public uint BaseArrayLayer { get; }
+        public uint ArrayLayerCount { get; }
+    }
+
+    public sealed record TextureRegion
+    {
+        public uint MipLevel { get; init; } = 0;
+        public Origin3D Origin { get; init; } = new Origin3D(0, 0, 0);
+        public required Extent3D Extent { get; init; }
+    }
+}
```

### 明示的なテクスチャ Readback

利用者が TextureCopyDesc の pitch／offset／必要 bytes に適合する Memory=Readback／Usage=CopyDestination の `IGraphicsBuffer<byte>` を確保し、CommandEncoder で依存と RecordCopyTextureToBuffer を記録する。Finish／Submit とその Submission の完了観測も利用者が行い、その後 `BufferSlice<byte>.CopyTo` で staging の格納 bytes を読む。専用の ReadTextureAsync は提供しない。

bytes には指定した BytesPerRow／RowsPerImage の padding が残る。行・layer の抽出、padding 除去、CPU destination の確保と成果物の構築は利用者が行う。sRGB の格納 bytes を線形値に変換せず、深度／stencil のコピー対象制約も変わらない。backend の native footprint 変換は記録したコピー命令の実装であり、別のコピーや送信を CPU CopyFrom／CopyTo が追加する根拠にはしない。CPU コピーと Submission 待機の契約は ADR-0009 に従う。

### TextureDesc

全 Desc は `string? Label = null`、init-only property を持つ。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record TextureDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public TextureDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // D1／D2／D3
+        // Cube は view の dimension
+        public TextureDimension Dimension { get; init; } = TextureDimension.D2;
+
+        // uint Width／Height／DepthOrArrayLayers、すべて正数
+        public required Extent3D Size { get; init; }
+
+        // Undefined／不明 enum は拒否、用途に必要な capability を確認
+        public required TextureFormat Format { get; init; }
+
+        // CopySource／CopyDestination／Sampled／StorageRead／StorageWrite／RenderAttachment
+        // None／不明 bit は拒否
+        public required TextureUsage Usage { get; init; }
+
+        // 1〜floor(log2(max spatial dimension))+1
+        // array layer 数は除外
+        public uint MipLevels { get; init; } = 1;
+
+        // 初期共通契約は 1 または 4、format／device の対応が必要
+        public uint SampleCount { get; init; } = 1;
+
+        // 追加の互換 view format
+        // 元 Format は暗黙に含み、重複は拒否
+        public IReadOnlyList<TextureFormat> ViewFormats { get; init; } = Array.Empty<TextureFormat>();
+    }
+}
```

D1 は height／depth = 1、D2 の depth は array layer 数、D3 の depth は空間寸法。mip の各空間寸法は `max(1, base >> mip)`。Cube は square な D2 の 6 layer、CubeArray は 6 の正の倍数を view に選ぶ。空間軸、mip 数、array layer 数を DeviceCaps の `MaxTextureDimension1D/2D/3D`、`MaxTextureArrayLayers` に照合する。shader binding には MaxSampledTexturesPerStage／MaxStorageTexturesPerStage と profile の合計 binding 上限も適用する。

4 samples は D2、layer 数 1、mip 数 1、RenderAttachment 用途を必須とし、Storage と Copy 用途を禁止する。Sampled は対応する multisampled shader 型に限る。depth／stencil の Storage と Copy は初期契約で禁止する。mip は生成時に自動生成されず、各使用範囲の内容を利用側が用意する。

公開 Format は R8Unorm、Rg8Unorm、Rgba8Unorm、Rgba8Srgb、Bgra8Unorm、Bgra8Srgb、R32Float、R32Sint、R32Uint、Rg32Float、Rgba16Float、Rgba32Float、Depth16Unorm、Depth32Float、Depth24UnormStencil8、Depth32FloatStencil8。列挙の存在は backend の対応保証ではない。深度 format を含め、別 format への暗黙置換は禁止する。圧縮 format は後続とする。

color コピーの bytesPerPixel は順に R8=1、Rg8=2、RGBA8／BGRA8／R32=4、Rg32／Rgba16=8、Rgba32=16。depth／stencil の物理配置や bytesPerPixel を公開コピー規約に含めない。sRGB の compatible view は同 channel／bit 構成の Unorm と Srgb の組に限定し、capability と ViewFormats の両方が許す場合だけ作る。

### TextureViewDesc とシェーダー引数

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record TextureViewDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public TextureViewDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // 元 format または許可済み ViewFormats
+        public TextureFormat? Format { get; init; } = null;
+
+        // D1／D2／D2Array／Cube／CubeArray／D3
+        // 元 texture と適合
+        public TextureViewDimension Dimension { get; init; } = TextureViewDimension.D2;
+
+        // All／DepthOnly／StencilOnly、存在する aspect のみ
+        public TextureAspect Aspect { get; init; } = TextureAspect.All;
+
+        // mip 範囲内
+        public uint BaseMipLevel { get; init; } = 0;
+
+        // null は残り全 mip、それ以外は正数で範囲内
+        public uint? MipLevelCount { get; init; } = null;
+
+        // D2 の開始 layer
+        // D1／D3 は 0
+        public uint BaseArrayLayer { get; init; } = 0;
+
+        // D2=1、D2Array=残り、Cube=6、CubeArray=残りの 6 の倍数、D1／D3=1
+        public uint? ArrayLayerCount { get; init; } = null;
+    }
+}
```

D3 の depth slice を array layer として選択しない。view dimension の対応は D1 texture→D1、D2 texture→D2／D2Array／Cube／CubeArray、D3 texture→D3 のみとする。Cube の face 順序は +X、-X、+Y、-Y、+Z、-Z、CubeArray はこの 6 枚単位とし、backend と Slang helper が方向規約を一致させる。MSAA は単一 D2 view のみ。attachment は D2、単一 mip／layer、RenderAttachment 用途で、選択 mip の寸法を使用する。

depth／stencil の sampled view は depth aspect のみを共通経路に含め、複合 depth／stencil format では DepthOnly を要求する。StencilOnly の sampled view は後続拡張とする。Sampled view は shader の dimension、numeric category、sample count、depth／color と適合する。Storage view は SampleCount=1、単一 mip、color format、shader access と StorageRead／Write 用途の一致を要求し、format 変更は不可。view の生成だけではアクセス権を付与しない。Storage の view dimension は D1／D2／D2Array／D3 のみで、Cube／CubeArray は拒否する。storage-read 対応などは backend／format capability に照合する。

利用者は生成済み引数型の TextureView field に view を設定し、FrameContext.CreateArguments で pack する。binding 番号、native descriptor、bindless index を指定しない。Slang reflection の resource category／dimension／sample type／access と実 view を検証する。sampler との組合せは ADR-0011 に従う。

### TextureCopyDesc と依存関係

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record TextureCopyDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public TextureCopyDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // 単一 mip、選択 mip の範囲内
+        public uint MipLevel { get; init; } = 0;
+
+        // 選択 mip の範囲内
+        public Origin3D Origin { get; init; } = new Origin3D(0, 0, 0);
+
+        // 正の extent、選択 mip の範囲内
+        public required Extent3D Extent { get; init; }
+
+        // BufferSlice 先頭からの相対 byte offset
+        public ulong BufferOffset { get; init; } = 0;
+
+        // width × bytesPerPixel 以上、CopyBytesPerRowAlignment の倍数
+        public required uint BytesPerRow { get; init; }
+
+        // height 以上
+        // layer／depth の row 間隔
+        public required uint RowsPerImage { get; init; }
+    }
+}
```

buffer の絶対 offset は checked で Slice.Offset + BufferOffset を求め、CopyBufferOffsetAlignment と format の texel size に照合する。必要 bytes は checked で `BufferOffset + (depthOrLayers - 1) * BytesPerRow * RowsPerImage + (height - 1) * BytesPerRow + width * bytesPerPixel` とし、Slice.Length 以下を要求する。最終 row の後ろの padding は読み書き対象に含めない。Core は pitch を補正せず、Runtime の helper が padding を用意する。

Texture→Texture は同じ texture の同じ mip では、空間／layer のコピー領域が重なる場合に拒否する。異なる mip や非重複領域は backend の resource scope が許す場合だけ使用可能とする。backend が実現できない同一 resource コピーを黙って記録しない。

使用 subresource とアクセスは ResourceDependency に登録する。render 書き込み→sample、storage 書き込み→copy、copy→sample の依存を Barrier で宣言する。attachment と引数、resolve target の重複は ADR-0006 に従って拒否する。Load／Discard は内容の定義状態を変え、初期生成内容を一律 zero として利用する保証は与えない。backend は API の安全性要件として必要な初期化を実施するが、アプリケーションの content 初期化は省略しない。

### バックエンドが実装するもの

```diff
+namespace Lumyte.Graphics.Implementation
+{
+    // Device factory が support の照会と allocation の生成・失敗 rollback を担う。
+    internal interface IGraphicsDriver
+    {
+        FormatCapabilities GetFormatCapabilities(TextureFormat format);
+        IGraphicsTexture CreateTexture(TextureDesc normalizedDesc);
+    }
+
+    // GPU コピー、依存遷移、attachment resolve はコマンド側の責務。
+    // 利用者の明示的な操作と範囲を、native footprint／pitch／layout／access へ変換する。
+    internal interface ICommandBufferBackendContract
+    {
+        void RecordCopyBufferToTexture(EncoderToken encoder, BufferRange source, IGraphicsTexture destination, TextureCopyDesc desc);
+        void RecordCopyTextureToBuffer(EncoderToken encoder, IGraphicsTexture source, BufferRange destination, TextureCopyDesc desc);
+        void RecordCopyTexture(EncoderToken encoder, IGraphicsTexture source, TextureRegion sourceRegion, IGraphicsTexture destination, TextureRegion destinationRegion);
+        void Barrier(EncoderToken encoder, ResourceDependency dependency);
+        void ResolveColorAttachment(EncoderToken encoder, IGraphicsTextureView source, IGraphicsTextureView target);
+    }
+}
```

Texture／view の具象 class は公開 IGraphicsTexture／IGraphicsTextureView を実装し、native image／allocation／view、所属 Device、世代、lease、正規化済み属性を内部に保持する。view は元 texture と別の native resource を所有し、Dispose で元 texture の lease を返す。コマンド backend は受け取った公開 interface の具象 instance に対して実装・Device 所属・有効性を検証して native データを使う。コピー命令とバリアはコマンド側に置く。

Slang の型・用途・Device・寿命の検証と descriptor／binding への解決は shader 引数構築を扱う backend の内部処理であり、texture／view の公開 interface に native 参照の解決 API を追加しない。初期 wgpu の公開 texture 契約は Width／Height／Usage／Format と引数なし CreateView、view 契約は元 Texture と Dispose に限定し、追加属性・一般的な反射解決は提案である。

| backend | 実装上の対応 |
| --- | --- |
| managed wgpu | Ahjo texture／view、copy layout、usage scope と limit。depth／storage／filterable の optional support を確認。独自 .Native は追加しない |
| DirectX | image resource、view descriptor、subresource index、copy footprint と state transition。指定 pitch を native footprint で直接扱えなければ内部 staging で実現し、共通 bytes を維持 |
| Vulkan | image／memory／view、aspect mask、layout とアクセス依存。row／image stride を texel 単位へ検証して変換し、必要なら repack |
| Browser WebGPU | texture／view、copy の row alignment、使用 scope、mapAsync。ホスト Native handle を公開しない |

DirectX／Vulkan の Native 境界は uint32 の寸法／mip／layer／format／usage と、uint64 の buffer offset、opaque resource token を使用する。view format 配列は呼び出し中に snapshot し、managed 配列 pointer を保存しない。native image／descriptor の handle と GPU 使用の状態は具象 backend が所有する。共通 TextureToken／TextureViewToken は設けない。完全な POD 宣言は後続の ABI 設計で定める。

### 所有権と失敗

TextureView は独自の native view を所有し、その存続中は元 Texture を lease する。TextureView は GPU コマンドの使用終了後に Dispose し、その後 Texture を Dispose する。非所有の参照関係という説明は texture memory の所有権移譲を意味しないが、元 texture の早期解放は拒否する。ShaderArguments の使用は view と元 texture を GPU 完了まで保持する。

引数・enum・範囲・別 Device は引数例外、破棄済みは ObjectDisposedException、使用中解放・パス状態違反は InvalidOperationException。有効な生成要求の未対応 format／機能、確保失敗、DeviceLost は Result の GraphicsError。コピーの native failure は Encoder を Faulted にする。非同期 validation error は関連 Submission に伝え、Readback を成功として返さない。スレッドとキャンセルは ADR-0009 と同じ。

### 利用例

以下は提案 API。Result の成否を確認して得た `texture`、`view`、`commands` を使う。

```csharp
// texture: Rgba8Srgb、Sampled | CopyDestination、mip 数を指定して生成済み
// 利用者が Memory=Upload／Usage=CopySource の IGraphicsBuffer<byte> uploadBuffer を生成済み。
var staging = uploadBuffer.Slice(0, (ulong)paddedRgbaBytes.Length);
staging.CopyFrom(paddedRgbaBytes); // CPU memory へのコピーのみ。
commands.RecordCopyBufferToTexture(staging, texture, new TextureCopyDesc {
    Extent = new Extent3D(width, height, 1),
    BytesPerRow = paddedPitch,
    RowsPerImage = height,
});
commands.Barrier(uploadToSampleDependency);
var args = frame.CreateArguments(layout, new MaterialArguments { Albedo = view, Sampling = sampler });
render.Draw(args, new DrawDesc { VertexCount = 3 });
// Barrier と BeginRenderPass はパス外。各 mip は個別に Upload する。
```

material の GPU buffer から sampled view と sampler の組を選択する経路は [ADR-0012](0012-material-buffer-texture-resolution.md) に従う。view 自身を storage buffer に格納せず、finite binding 集合と不透明な wire 参照を使う。

## 検討した代替案

### Texture と sampler を一つの所有 resource にする

同じ画像を異なる sampling で共有できず、storage／attachment でも不要な sampler が付く。独立した TextureView と Sampler を引数で組み合わせる。

### native footprint と layout を利用側へ公開する

backend 条件が Upload code に広がる。共通 pitch／region を定め、native への変換を backend に閉じ込める。

## 結果と影響

- mip／layer／aspect と用途を view、copy、attachment、shader が共有できる。
- subresource と依存の追跡、format capabilities、staging repack の実装コストが必要。
- 一部 backend では同一 image の異なる subresource 利用も制限される。capability と検証で明示する。
- 現行 TextureDesc.Width／Height と引数なし CreateView は詳細 Desc へ移行する。texture の読み戻しは明示的な staging コピーと共通の CPU 読み出しへ移行する。初期実装のコピー記録 API は RecordCopyTextureToBuffer に改称する。RGBA8Unorm／Srgb の sampling と RecordCopyBufferToTexture は実装済み。一般的な format／view の追加機能は未実装。

## 検証方針

共通 API のテストで mip／array／3D／Cube 範囲、aspect、sRGB view、numeric category、storage access、MSAA の制約を確認する。異なる pitch と複数 layer／depth の Upload→Readback を bytes で比較し、padding、origin、最終 row、overflow を検証する。render→sample、storage→copy、resolve→readback、alias の拒否と view lease を backend ごとに確認する。追加契約の動作と性能は今回未検証。

## 別途決定する事項

- 圧縮 format、depth／stencil の copy／resolve、mipmap 生成 helper。
- swapchain／external image、sparse／alias memory、複数キュー、texture memory budget。

## 参考資料

- [RenderEncoder と attachment](0006-render-encoder.md)
- [バッファ](0009-buffer-resource-contract.md)
- [サンプラー](0011-sampler-resource-contract.md)
- [WebGPU textures](https://www.w3.org/TR/webgpu/#textures)
