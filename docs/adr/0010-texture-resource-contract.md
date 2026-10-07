# ADR-0010: テクスチャ・ビュー・転送の利用 API とバックエンド実装契約

- 状態: 提案
- 日付: 2026-10-07

## 背景

Texture を単なる画像 handle として扱うと、mip／layer／aspect、format、sample count、用途、コピーの pitch と読み戻しの意味が不明になる。[ADR-0006](0006-render-encoder.md) の attachment と [ADR-0005](0005-shader-compilation-and-data-interop.md) のシェーダー参照が同じ subresource を扱える設計が必要である。

本 ADR を TextureDesc、TextureViewDesc、TextureCopyDesc の正本とする。Sampler は [ADR-0011](0011-sampler-resource-contract.md)、Buffer は [ADR-0009](0009-buffer-resource-contract.md) に分離する。以下は広い共通 API の提案であり、実装済みの単一 RGBA8 target は [ADR-0008](0008-wgpu-first-backend.md) に従う。

## 決定

### 利用側の公開 API

Core 型は `Lumyte.Graphics`、Readback helper は `Lumyte.Graphics.Runtime`。Texture と TextureView は sealed な所有 class とし、利用者による直接 constructor、native image／view handle の取得、CPU map は提供しない。

| 利用側 API | 契約 |
| --- | --- |
| `Result<Texture> GraphicsDevice.CreateTexture(TextureDesc desc)` | Desc と ViewFormats を snapshot。形式・用途・寸法・容量を検証して生成 |
| `TextureDimension Texture.Dimension`／`Extent3D Size`／`TextureFormat Format`／`TextureUsage Usage`／`uint MipLevels`／`uint SampleCount` | 確定した論理属性。生成後は不変 |
| `Result<TextureView> Texture.CreateView(TextureViewDesc desc)` | 所属 texture の互換な subresource view を生成。null の count／format を解決して保持 |
| `Texture TextureView.Texture`／`TextureViewInfo Info` | 元 texture と正規化済みの形式・dimension・aspect・mip／layer 範囲。物理 descriptor は公開しない |
| `Extent3D Texture.GetMipSize(uint mipLevel)` | 範囲内 mip の寸法。D3 の depth は縮小、D2 の array layer 数は一定 |
| `FormatCapabilities DeviceCaps.GetFormatCapabilities(TextureFormat format)` | sampled、filterable、comparison-sampleable、storage read／write、renderable、blendable、sample count、互換 view format |
| `void CommandEncoder.CopyBufferToTexture(BufferSlice source, Texture destination, TextureCopyDesc desc)` | source CopySource、destination CopyDestination。パス外、同じ Device |
| `void CommandEncoder.CopyTextureToBuffer(Texture source, BufferSlice destination, TextureCopyDesc desc)` | 逆方向のコピー。source CopySource、destination CopyDestination。同じ layout 検証 |
| `void CommandEncoder.CopyTexture(Texture source, TextureRegion sourceRegion, Texture destination, TextureRegion destinationRegion)` | 同 format、同 extent、sample count 1 の color。範囲、用途、alias を検証 |
| `ValueTask<Result<TextureReadback>> GraphicsRuntime.ReadTextureAsync(Texture source, TextureRegion region, Submission lastWrite, CancellationToken cancellationToken = default)` | 最終書き込みを待ち、staging コピー・送信・完了・padding 除去を実施 |
| `void TextureView.Dispose()`／`void Texture.Dispose()` | idle 時の即時解放、idempotent。使用中の解放は拒否 |

`TextureViewInfo` は immutable record。`Format`、`Dimension`、`Aspect`、`BaseMipLevel`、解決済み `MipLevelCount`、`BaseArrayLayer`、解決済み `ArrayLayerCount` を持つ。`TextureRegion` は init-only record で `uint MipLevel = 0`、`Origin3D Origin = (0,0,0)`、required `Extent3D Extent` を持つ。初期コピーは color の Aspect.All のみで、aspect field は追加しない。

`TextureReadback` は `TextureFormat Format`、`Extent3D Size`、`uint BytesPerRow`、`uint RowsPerImage`、`ReadOnlyMemory<byte> Data` を持つ immutable な CPU 成果物とする。pitch は width × bytesPerPixel、rows は height、Data は layer／depth 順の密な独立した byte 配列。sRGB の bytes を線形値に変換せず、format の格納値を返す。array の総量と pitch の型上限を checked 検証する。lastWrite の所属、コピー用途、キャンセルと lease の扱いは ADR-0009 の Readback と同じ。

### TextureDesc

全 Desc は `string? Label = null`、init-only property を持つ。

| フィールド | 型・既定値 | 検証と意味 |
| --- | --- | --- |
| Dimension | `TextureDimension = D2` | D1／D2／D3。Cube は view の dimension |
| Size | `Extent3D`、required | uint Width／Height／DepthOrArrayLayers、すべて正数 |
| Format | `TextureFormat`、required | Undefined／不明 enum は拒否、用途に必要な capability を確認 |
| Usage | `TextureUsage`、required | CopySource／CopyDestination／Sampled／StorageRead／StorageWrite／RenderAttachment。None／不明 bit は拒否 |
| MipLevels | `uint = 1` | 1〜floor(log2(max spatial dimension))+1。array layer 数は除外 |
| SampleCount | `uint = 1` | 初期共通契約は 1 または 4、format／device の対応が必要 |
| ViewFormats | `IReadOnlyList<TextureFormat> = empty` | 追加の互換 view format。元 Format は暗黙に含み、重複は拒否 |

D1 は height／depth = 1、D2 の depth は array layer 数、D3 の depth は空間寸法。mip の各空間寸法は `max(1, base >> mip)`。Cube は square な D2 の 6 layer、CubeArray は 6 の正の倍数を view に選ぶ。空間軸、mip 数、array layer 数を DeviceCaps の `MaxTextureDimension1D/2D/3D`、`MaxTextureArrayLayers` に照合する。shader binding には MaxSampledTexturesPerStage／MaxStorageTexturesPerStage と profile の合計 binding 上限も適用する。

4 samples は D2、layer 数 1、mip 数 1、RenderAttachment 用途を必須とし、Storage と Copy 用途を禁止する。Sampled は対応する multisampled shader 型に限る。depth／stencil の Storage と Copy は初期契約で禁止する。mip は生成時に自動生成されず、各使用範囲の内容を利用側が用意する。

公開 Format は R8Unorm、Rg8Unorm、Rgba8Unorm、Rgba8Srgb、Bgra8Unorm、Bgra8Srgb、R32Float、R32Sint、R32Uint、Rg32Float、Rgba16Float、Rgba32Float、Depth16Unorm、Depth32Float、Depth24UnormStencil8、Depth32FloatStencil8。列挙の存在は backend の対応保証ではない。深度 format を含め、別 format への暗黙置換は禁止する。圧縮 format は後続とする。

color コピーの bytesPerPixel は順に R8=1、Rg8=2、RGBA8／BGRA8／R32=4、Rg32／Rgba16=8、Rgba32=16。depth／stencil の物理配置や bytesPerPixel を公開コピー規約に含めない。sRGB の compatible view は同 channel／bit 構成の Unorm と Srgb の組に限定し、capability と ViewFormats の両方が許す場合だけ作る。

### TextureViewDesc とシェーダー引数

| フィールド | 型・既定値 | 検証と意味 |
| --- | --- | --- |
| Format | `TextureFormat? = null` | 元 format または許可済み ViewFormats |
| Dimension | `TextureViewDimension = D2` | D1／D2／D2Array／Cube／CubeArray／D3。元 texture と適合 |
| Aspect | `TextureAspect = All` | All／DepthOnly／StencilOnly、存在する aspect のみ |
| BaseMipLevel | `uint = 0` | mip 範囲内 |
| MipLevelCount | `uint? = null` | null は残り全 mip、それ以外は正数で範囲内 |
| BaseArrayLayer | `uint = 0` | D2 の開始 layer。D1／D3 は 0 |
| ArrayLayerCount | `uint? = null` | D2=1、D2Array=残り、Cube=6、CubeArray=残りの 6 の倍数、D1／D3=1 |

D3 の depth slice を array layer として選択しない。view dimension の対応は D1 texture→D1、D2 texture→D2／D2Array／Cube／CubeArray、D3 texture→D3 のみとする。Cube の face 順序は +X、-X、+Y、-Y、+Z、-Z、CubeArray はこの 6 枚単位とし、backend と Slang helper が方向規約を一致させる。MSAA は単一 D2 view のみ。attachment は D2、単一 mip／layer、RenderAttachment 用途で、選択 mip の寸法を使用する。

depth／stencil の sampled view は depth aspect のみを共通経路に含め、複合 depth／stencil format では DepthOnly を要求する。StencilOnly の sampled view は後続拡張とする。Sampled view は shader の dimension、numeric category、sample count、depth／color と適合する。Storage view は SampleCount=1、単一 mip、color format、shader access と StorageRead／Write 用途の一致を要求し、format 変更は不可。view の生成だけではアクセス権を付与しない。Storage の view dimension は D1／D2／D2Array／D3 のみで、Cube／CubeArray は拒否する。storage-read 対応などは backend／format capability に照合する。

利用者は生成済み引数型の TextureView field に view を設定し、FrameContext.CreateArguments で pack する。binding 番号、native descriptor、bindless index を指定しない。Slang reflection の resource category／dimension／sample type／access と実 view を検証する。sampler との組合せは ADR-0011 に従う。

### TextureCopyDesc と依存関係

| フィールド | 型・既定値 | 検証 |
| --- | --- | --- |
| MipLevel／Origin／Extent | TextureRegion と同じ | 単一 mip、正の extent、選択 mip の範囲内 |
| BufferOffset | `ulong = 0` | BufferSlice 先頭からの相対 byte offset |
| BytesPerRow | `uint`、required | width × bytesPerPixel 以上、CopyBytesPerRowAlignment の倍数 |
| RowsPerImage | `uint`、required | height 以上。layer／depth の row 間隔 |

buffer の絶対 offset は checked で Slice.Offset + BufferOffset を求め、CopyBufferOffsetAlignment と format の texel size に照合する。必要 bytes は checked で `BufferOffset + (depthOrLayers - 1) * BytesPerRow * RowsPerImage + (height - 1) * BytesPerRow + width * bytesPerPixel` とし、Slice.Length 以下を要求する。最終 row の後ろの padding は読み書き対象に含めない。Core は pitch を補正せず、Runtime の helper が padding を用意する。

Texture→Texture は同じ texture の同じ mip では、空間／layer のコピー領域が重なる場合に拒否する。異なる mip や非重複領域は backend の resource scope が許す場合だけ使用可能とする。backend が実現できない同一 resource コピーを黙って記録しない。

使用 subresource とアクセスは ResourceDependency に登録する。render 書き込み→sample、storage 書き込み→copy、copy→sample の依存を Barrier で宣言する。attachment と引数、resolve target の重複は ADR-0006 に従って拒否する。Load／Discard は内容の定義状態を変え、初期生成内容を一律 zero として利用する保証は与えない。backend は API の安全性要件として必要な初期化を実施するが、アプリケーションの content 初期化は省略しない。

### バックエンドが実装するもの

| 内部操作 | 必須の実装責務 |
| --- | --- |
| `GetFormatCapabilities(format)` | native support と実装範囲の両方に基づく用途、filter／compare、sample count、view format の報告 |
| `CreateTexture(normalizedDesc)`／`DestroyTexture(token)` | image と memory の確保、使用 flags、format／extent、世代、失敗 rollback、idle 時解放 |
| `CreateTextureView(textureToken, normalizedDesc)`／`DestroyTextureView(token)` | 選択 subresource の native view と元 texture の lease。サブリソース情報を失わない |
| `ResolveTextureView(token, reflectedType, bindingPlan)` | shader の型・用途・device と lifetime の検証、descriptor／binding への内部変換 |
| `CopyBufferToTexture`／`CopyTextureToBuffer`／`CopyTexture` | 範囲・pitch・用途を記録前に検証。必要な native footprint へ変換し、padding の意味を維持 |
| `ApplyTextureDependency(subresources, producer, consumer)` | layout／access／使用 scope の遷移。aspect／mip／layer の追跡。必要なら保守的な texture 全体の依存へ拡張 |
| `ResolveColorAttachment(source, target)` | ADR-0006 の End で color MSAA resolve。source discard と target 保存を別に処理 |
| `ReadTextureStagingAsync` | GPU 完了、map／invalidate、native pitch からの密な CPU bytes の構築、キャンセル後の安全な回収 |

| backend | 実装上の対応 |
| --- | --- |
| managed wgpu | Ahjo texture／view、copy layout、usage scope と limit。depth／storage／filterable の optional support を確認。独自 .Native は追加しない |
| DirectX | image resource、view descriptor、subresource index、copy footprint と state transition。指定 pitch を native footprint で直接扱えなければ内部 staging で実現し、共通 bytes を維持 |
| Vulkan | image／memory／view、aspect mask、layout とアクセス依存。row／image stride を texel 単位へ検証して変換し、必要なら repack |
| Browser WebGPU | texture／view、copy の row alignment、使用 scope、mapAsync。ホスト Native handle を公開しない |

DirectX／Vulkan の Native 境界は uint32 の寸法／mip／layer／format／usage と、uint64 の buffer offset、opaque resource token を使用する。view format 配列は呼び出し中に snapshot し、managed 配列 pointer を保存しない。native image／descriptor の寿命と GPU 使用を token に結び付ける。完全な POD 宣言は後続の ABI 設計で定める。

### 所有権と失敗

TextureView は独自の native view を所有し、その存続中は元 Texture を lease する。TextureView は GPU コマンドの使用終了後に Dispose し、その後 Texture を Dispose する。非所有の参照関係という説明は texture memory の所有権移譲を意味しないが、元 texture の早期解放は拒否する。ShaderArguments の使用は view と元 texture を GPU 完了まで保持する。

引数・enum・範囲・別 Device は引数例外、破棄済みは ObjectDisposedException、使用中解放・パス状態違反は InvalidOperationException。有効な生成要求の未対応 format／機能、確保失敗、DeviceLost は Result の GraphicsError。コピーの native failure は Encoder を Faulted にする。非同期 validation error は関連 Submission に伝え、Readback を成功として返さない。スレッドとキャンセルは ADR-0009 と同じ。

### 利用例

以下は提案 API。Result の成否を確認して得た `texture`、`view`、`commands` を使う。

```csharp
// texture: Rgba8Srgb、Sampled | CopyDestination、mip 数を指定して生成済み
var staging = frame.UploadBytes(paddedRgbaBytes);
commands.CopyBufferToTexture(staging, texture, new TextureCopyDesc {
    Extent = new Extent3D(width, height, 1),
    BytesPerRow = paddedPitch,
    RowsPerImage = height,
});
commands.Barrier(uploadToSampleDependency);
var args = frame.CreateArguments(layout, new MaterialArguments { Albedo = view, Sampling = sampler });
render.Draw(args, new DrawDesc { VertexCount = 3 });
// Barrier と BeginRenderPass はパス外。各 mip は個別に Upload する。
```

## 検討した代替案

### Texture と sampler を一つの所有 resource にする

同じ画像を異なる sampling で共有できず、storage／attachment でも不要な sampler が付く。独立した TextureView と Sampler を引数で組み合わせる。

### native footprint と layout を利用側へ公開する

backend 条件が Upload code に広がる。共通 pitch／region を定め、native への変換を backend に閉じ込める。

## 結果と影響

- mip／layer／aspect と用途を view、copy、attachment、shader が共有できる。
- subresource と依存の追跡、format capabilities、staging repack の実装コストが必要。
- 一部 backend では同一 image の異なる subresource 利用も制限される。capability と検証で明示する。
- 現行 TextureDesc.Width／Height、引数なし CreateView、texture readback helper は移行対象。今回実装の拡張は行わない。

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
