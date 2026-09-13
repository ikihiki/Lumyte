# ADR 0016: 独立した Portable Graphics API

## 状態

採用（目標設計）。`Lumyte.Graphics.Portable` が提供する低層 API を定義する。現行実装の完了を示さない。

## 依存 ADR

- [0001 Graphics の層構造](0001-graphics-api.md): Native／Portable の分離、共通 RenderGraph と独立した provider、検証の委譲。

## 決定

Portable は WebGPU の resource、binding、command model を基準に実装する独立した系統とする。Native の adapter として構築せず、両系統を統合する低層 backend interface は設けない。共通 RenderGraph の利用者は Model、Blur、2D 等の機能 pass を追加する。選択された provider の Portable pass 本体が、自身の shader、GPU 入力構造体、resource、内部 graph と低層 command を構築する。共通利用者は Portable の shader や command 型を参照せず、同じコンパイル済み binary を使う。この低層 API と Portable Resources を直接使う場合は、Portable 専用の型を使う。

shader resource は有限の binding layout と明示した binding set で渡す。device 全体の descriptor index、Bindless の必須要件、Bindless のエミュレーションは設けない。material と pass が必要とする resource だけを binding に含める。

Portable の実行 stage は Vertex／Pixel／Compute とする。Native の Mesh／Amplification stage と DispatchMesh は提供せず、mesh shader を compute や indexed draw に暗黙変換する backend も作らない。同じ Model 等の機能は Portable の本体が自分の vertex／compute 経路で実装する。Slang source の部分共有は offline authoring の選択であり、この実行モデルと WGSL の受取りを変更しない。

Buffer／Texture は必要な memory を含めて生成する。事前の heap 作成、allocation の取得、配置要件の計算を caller に要求しない。resource の破棄で内部 memory の所有も終了する。論理的な package のまとまりは、複数 resource の管理単位とする。

root data は shader の直接入力とする。対応 runtime と有効 limit を初期化時に要求し、program ごとに必要な byte 数を定める。全系統に共通の固定 byte 数や、隠れた buffer への置き換えは設けない。

## API

この ADR 群の `Gpu*` 型は、特記しない限り `Lumyte.Graphics.Portable` に属する。

| API | 説明 |
| --- | --- |
| `IPortableGpuBackend` | 一つの Portable device を表す interface。resource、binding、pipeline、queue の操作は各担当 ADR で定義する。Native の backend と相互変換しない。 |
| `GpuBackendOptions` | `RequireDualSourceBlend`、`RequireIndirectFirstInstance` と `RequiredLimits` を指定する生成設定。直接 root は常に必須であり、無効化する設定はない。runtime 診断を捕捉するかどうかを任意に切り替えず、無効 object を成功扱いしない。 |
| `GpuRequiredLimits` | 任意の要求値。各 member は nullable で、null は未指定を表す。`Max*` は必要な容量の下限、`Min*OffsetAlignment` は許容できる制約の上限である。 |
| `Capabilities`／`GpuBackendCapabilities` | `DirectRootData`、`DualSourceBlend`、`IndirectFirstInstance` など、作成済み device で有効な機能を表す。架空の対応値を返さない。 |
| `Limits`／`GpuDeviceLimits` | 作成済み device の有効 limit。buffer size、uniform/storage binding size と offset alignment、texture extent/layers、color attachments、bind groups、group 内 bindings、stage ごとの resource 数、compute workgroup、`MaxImmediateSize` を含む。 |
| `Dispose()` | backend が所有する内部 object と device を終了する。caller は resource と記録、提出済み利用を先に終了する。暗黙の全 resource 探索は行わない。 |
| `GpuDiagnostic(Kind, Message)`／`GpuDiagnosticKind` | runtime が返した診断値。kind は `Validation`、`OutOfMemory`、`Internal`、これらの error type に分類されない callback status を表す `Runtime`。独自 validation の結果を混ぜない。 |
| `GpuOperationException(Operation, Diagnostics)` | device／resource 操作の失敗。操作名とコピー済みの変更不能な診断列を保持する。GPU 提出の完了や、その出力の成功を表す型ではない。device loss は共通の `GpuDeviceLostException` で通知する。 |

`DirectRootData` は Portable backend の初期化条件とする。直接入力が未対応の runtime は初期化を失敗させる。`RequiredLimits.MaxImmediateSize` が未指定なら adapter の利用可能な直接入力容量を要求し、明示指定があればその値を runtime へ渡す。0 byte の device を直接入力対応として公開しない。対応する shader の byte 数は、作成した device から取得した `Limits.MaxImmediateSize` に収める。ほかの有効 limit も adapter の最大値で置き換えない。

`GpuDeviceLimits` と `GpuRequiredLimits` の member は次のとおり。後者だけが nullable で、未指定と0を区別する。

| 分類 | Member |
| --- | --- |
| Texture | `MaxTextureDimension1D`／`2D`／`3D`、`MaxTextureArrayLayers` |
| Buffer | `MaxBufferSize`、`MaxUniformBufferBindingSize`、`MaxStorageBufferBindingSize` は ulong。`MinUniformBufferOffsetAlignment`、`MinStorageBufferOffsetAlignment` は uint |
| Binding group | `MaxBindGroups`、`MaxBindingsPerBindGroup`、`MaxDynamicUniformBuffersPerPipelineLayout`、`MaxDynamicStorageBuffersPerPipelineLayout` |
| Shader stage の resource 数 | `MaxSampledTexturesPerShaderStage`、`MaxSamplersPerShaderStage`、`MaxStorageBuffersPerShaderStage`、`MaxStorageTexturesPerShaderStage`、`MaxUniformBuffersPerShaderStage` |
| Attachment | `MaxColorAttachments`、`MaxColorAttachmentBytesPerSample` |
| Compute | `MaxComputeWorkgroupStorageSize`、`MaxComputeInvocationsPerWorkgroup`、`MaxComputeWorkgroupSizeX`／`Y`／`Z`、`MaxComputeWorkgroupsPerDimension` |
| 直接入力 | `MaxImmediateSize` |

表で ulong とした容量以外は uint とする。これらは要求値と取得済みの有効値を表すデータであり、command の合法性を再検証する validator を持たない。

feature と limit は生成時に要求を選ぶための情報である。resource、binding、pipeline、usage の合法性を毎回独自に再検証する根拠にはしない。WebGPU runtime が判断する条件は runtime に委ねる。

## コード配置

以下は repository root からの配置で、Browser 等の後続機能の目標配置を含む。`Lumyte.Graphics.Portable` とそのテスト project を設け、WebGPU project にこの契約を直接実装する backend を置く。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Device/` | 公開 `IPortableGpuBackend`、`GpuBackendOptions`、capability と limit の値。Native backend、DI、ブラウザーの JavaScript 型に依存しない。 |
| `src/graphics/Lumyte.Graphics.Portable/Diagnostics/` | runtime 診断の不変値と、device／resource 操作の `GpuOperationException`。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Device/` | 既存 project を改編し、device 初期化、feature/limit の要求と Portable 契約への接続を置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Browser/Runtime/` | 既存 project を改編し、ブラウザーの adapter/device 取得と非同期 interop を置く。環境固有の型はここへ閉じ込める。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Device/` | 隣接する xUnit project。設定、公開値、外部 assembly からの実装・利用を CPU 上で検証する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Device/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Device/` | 既存 xUnit project を改編する。fake runtime による初期化経路と、実 runtime/device の feature・limit 試験を分離する。 |

## 使用例

`factory` は選択した Portable provider の factory、`options` は要求 feature と limit を指定した設定とする。

```csharp
using Lumyte.Graphics.Portable;

using IPortableGpuBackend graphics = await factory.CreateAsync(options);
Console.WriteLine(graphics.Limits.MaxImmediateSize);
```

factory は Portable device を作る。Native device を引数に取る変換入口は設けない。

## 採用範囲と未実装事項

Portable の低層を独立させ、共通 RenderGraph の Portable provider から利用する。独立 package、外部 backend が実装できる interface、要求 feature／limit と有効値、直接入力の初期化条件、Buffer／Texture の生成・破棄と非同期 mapping、非所有の View／range／sampler、immutable Binding Layout／Bindings を実装した。native host の WebGPU は Dawn C API へ直接接続する。

raw WGSL、raster／compute pipeline、render／compute／buffer・texture copy の記録と提出、CPU timeline の非同期待機を接続した。indexed／indirect draw、直接 root、有限 binding、depth/stencil、blend、MSAA resolve を実装する。shader package／loader、Browser runtime、共通 RenderGraph provider への接続は未実装である。公開 interface には実装した責務だけを加え、未実装 member を成功したように振る舞う stub は置かない。検証結果は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。
