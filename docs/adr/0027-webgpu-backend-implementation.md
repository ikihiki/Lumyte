# ADR 0027: WebGPU Backend 実装

## 状態

採用（目標設計）。Portable の低層契約を WebGPU へ接続する実装を定義する。

## 依存 ADR

- [0016 Portable Graphics API](0016-portable-api.md): 独立した Portable device。
- [0017 Resource のメモリ所有](0017-resource-memory-model.md): resource と内部 memory の一体生成・破棄。
- [0018 Buffer](0018-buffer-api.md)、[0019 Texture](0019-texture-api.md)、[0020 View](0020-view-api.md): resource と解釈。
- [0021 Binding Layout](0021-binding-layout-api.md)、[0022 Binding](0022-binding-api.md): 明示した group。
- [0023 Shader](0023-shader-design-and-api.md)、[0024 Pipeline](0024-pipeline-state-api.md): WGSL package と pipeline。
- [0025 Recording](0025-command-recording-api.md)、[0026 Submission / Completion](0026-command-submission-and-synchronization.md): 記録、提出と完了。

## 決定

WebGPU backend は `IPortableGpuBackend` を直接実装する。WebGPU runtime が内部でどの GPU API を使うかは runtime の責務であり、Lumyte Native の backend を経由しない。

resource は `GPUBuffer`／`GPUTexture`、binding は `GPUBindGroupLayout`／`GPUBindGroup`、shader は WGSL を使う。device 全体の Bindless descriptor 空間を作らず、shader の index を有限候補の分岐へ変換しない。material/pass が明示した group に必要な resource だけを設定する。

## API

| API | 説明 |
| --- | --- |
| `WebGpuBackend.CreateAsync(runtime, options)` | runtime の adapter/device 初期化を行い、`IPortableGpuBackend` を返す。`runtime` のホスト環境は借用し、作成した device は backend が所有する。 |
| `GpuBackendOptions` | 要求 feature と limit を device 作成へ渡す。要求した直接入力機能を満たせない環境では初期化を失敗させる。 |
| `Capabilities`／`Limits` | 作成した device で有効な機能と上限。 |
| `Dispose()` | application の全利用終了後に内部 object と device を終了する。借用 runtime のホスト環境は終了しない。 |

resource、binding、pipeline と queue の API は各依存 ADR をそのまま実装し、WebGPU の利用者だけが必要とする公開 slot 設定 API は追加しない。

## Resource と memory

| Portable の操作 | WebGPU への接続 |
| --- | --- |
| `CreateBuffer`／`CreateTexture` | `GPUDevice.createBuffer`／`createTexture` で必要な memory を含む resource を生成する。明示 usage を渡す。 |
| `DestroyBuffer`／`DestroyTexture` | `GPUBuffer.destroy`／`GPUTexture.destroy` と内部 handle の解放に接続し、resource と内部 memory の所有を終了する。 |
| `MapBufferAsync`／mapped range の終了 | `mapAsync`、mapped range の取得、`unmap`。copy staging は明示 resource とする。 |
| texture view の値 | binding または attachment の実体化時に `GPUTextureView` を作る。 |
| sampler description | binding 実体化時に `GPUSampler` を作る。 |
| `MutableFormat` | backend 内で当該基礎 format に対する互換 view format を生成 description へ設定する。public に許可 format 列を要求しない。 |

`MutableFormat = false` は基礎 format と既定の aspect 解釈を使い、true の場合も WebGPU が許す format 再解釈だけを有効にする。backend が公開の format 能力照会や独自 validator を追加する理由にはしない。

package は複数 WebGPU resource の所有と寿命をまとめ、pool は利用終了後の resource object を再利用する。memory の取得と配置は WebGPU runtime に任せ、backend が resource 生成の前に別の heap を用意する経路は設けない。

## Binding と shader

group ごとの layout を `GPUBindGroupLayout` へ、immutable binding set を `GPUBindGroup` へ接続する。texture view と sampler の cache は内部 object の再利用であり、global descriptor domain の管理ではない。binding object の利用終了後に内部参照を解放する。application resource の所有は caller のままとする。

Portable package から WGSL module と group layout、entry point を読み込む。root の構造体は package の immediate layout に従い、pipeline layout の `immediateSize` と command の `setImmediates` に接続する。`MaxImmediateSize` の有効値に従い、共通の固定 64 byte ABI は設けない。[WebGPU の直接入力仕様](https://gpuweb.github.io/gpuweb/#immediate-data)

WGSL の `immediate_address_space` と runtime の直接入力経路を初期化要件にする。仕様上存在する機能でも、選択した runtime が未実装なら対応済みと扱わない。root data の uniform/storage buffer 化は行わない。[WGSL の language feature](https://gpuweb.github.io/gpuweb/wgsl/#language-extensions)

Parameter Data は明示した buffer binding から shader が参照する。root に含む index/offset を shader で使用し、backend は byte 列を解析して resource 選択や upload を行わない。

## Pipeline と command

Portable の immutable description と program から、実際の提出で必要になった `GPURenderPipeline`／`GPUComputePipeline` を作る。固定 depth/stencil/blend を WebGPU pipeline へ含め、同じ論理 pipeline は生成済み object を再利用する。

提出時に必要な pipeline を揃え、各 command を encoder と render/compute pass に変換する。`SetBindings`／`SetComputeBindings` は対象 pass の `setBindGroup`、root 設定は `setImmediates` に変換する。copy は pass の外側で encode する。すべての encode を終えてから queue に提出する。

pass 内の usage 制約を独自に検証したり、見えない pass 分割で違反を修正したりしない。resource state の推移は WebGPU runtime が管理する。Native の barrier command や stage mask を解釈する層は設けない。

## Completion と失敗

queue への提出後、その提出を含む `onSubmittedWorkDone` を caller 指定の timeline 値へ対応付ける。これは GPU 利用終了を知る入口であり、処理成功は別に確定する。timeline は CPU 観測用とし、GPU の semaphore や queue 間 wait を偽装しない。[WebGPU の queue completion](https://gpuweb.github.io/gpuweb/#dom-gpuqueue-onsubmittedworkdone)

### 診断を object と batch に帰属させる

同期形の WebGPU 呼出しで object が返っても、内部処理と診断は未完了の場合がある。backend は runtime の error scope を使い、診断の Promise と、それを発生させた操作の対応を内部に保持する。shader module、resource、layout、binding 等の生成診断は生成した object に、提出時 pipeline 作成・encode・submit の診断はその batch に結び付ける。再利用する pipeline の生成診断も、その pipeline の内部記録から参照できるようにする。[WebGPU の非同期 object 作成](https://gpuweb.github.io/gpuweb/#invalid-internal-objects-and-contagious-invalidity)

一つの runtime 呼出し区間を `validation`／`out-of-memory`／`internal` の error scope で囲み、全 scope を pop してから非同期に結果を待つ。同じ device への区間は backend の runtime 呼出し口で直列化し、scope を開いたまま await したり、別の並行呼出しの scope を横取りしたりしない。GPU 完了までこの直列化を維持する必要はない。shader の compilation info など付加診断を取得する場合も対応する object に結び付け、warning だけを実行失敗にはしない。[WebGPU の error scope](https://gpuweb.github.io/gpuweb/#error-scopes)

batch は実際に参照する object の生成診断と、自身の pipeline・encode・submit 診断をまとめて観測する。生成が別の提出より前でも、必要な診断を失わない。無関係な object や batch のエラーを「最後に提出した batch」へ付け替えない。scope が返した runtime の診断を保持し、wrapper が binding・format・usage・shader の validator を複製する処理は設けない。

### 利用終了と成功を別々に確定する

batch ごとに GPU 利用終了と診断確定を別々に記録し、両方が揃ってエラーがない場合だけ `WaitAsync` を正常完了させる。`onSubmittedWorkDone` と `popErrorScope` 等の Promise の到着順を仮定しない。低層 `Submit` は Promise を同期的に待たず復帰するため、その時点では runtime に渡した work の成功は未確定である。[WebGPU の Promise 順序](https://gpuweb.github.io/gpuweb/#promise-ordering)

device 作成直後から `device.lost` を監視し、completion と同じ runtime 接続で障害を処理する。device loss 時には通常なら成功を示す Promise も正常解決し得るため、runtime の loss 通知を無視して Promise の正常解決だけを成功へ変換しない。loss が確定した device の内容世代と object は再利用対象から外す。[WebGPU の device loss](https://gpuweb.github.io/gpuweb/#dom-gpudevice-lost)

| 観測した結果 | Portable への通知 |
| --- | --- |
| GPU 利用終了、診断未確定 | `IsComplete` は true、`WaitAsync` は pending。内容の成功をまだ公開しない。 |
| GPU 利用終了、診断が成功 | 当該 signal value の `WaitAsync` を正常完了する。 |
| 診断が失敗、GPU 利用継続中 | 失敗を保持して利用終了を待つ。診断だけで内部記録を回収しない。 |
| GPU 利用終了、診断が失敗 | `GpuExecutionException` を返す。内部記録は回収できるが、upload／出力内容は成功と扱わない。 |
| device loss または interop 障害で利用終了を確認できない | `GpuDeviceLostException` で未確定の待機を終了する。通常の pool 再利用へ進まず device 停止処理へ保持を引き継ぐ。 |

上位 manager／graph はこの成功結果を package 公開、export、GPU 内容世代へ接続する。受理済みで成功未確定の内容を後続処理が参照する場合、後続自身の queue 到達だけで成功を決めず、明示した内容依存の失敗も引き継ぐ。依存の宣言は上位が所有し、低層 backend が shader の読み書きから推測しない。

`uncapturederror` を発生源不明のまま任意の batch の成功へ混ぜない。backend が所有する呼出しに帰属できない障害は runtime 接続の障害として報告し、未確定の結果を成功に変換しない。device loss は同様に pending wait と終了処理へ接続する。これらの失敗は、GPU が終了したことの代わりにはならない。

## コード配置

以下は repository root からの目標配置である。既存の WebGPU、WebGPU.Browser と WebGPU.Tests を改編して Portable 契約へ接続する。表中の分野別ディレクトリは目標配置とし、同じ実装を旧・新 API に二重保持しない。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.WebGPU/Device/` | `WebGpuBackend.CreateAsync`、device 所有、feature/limit と診断の接続。`IPortableGpuBackend` を直接実装する。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Resources/`、`src/graphics/Lumyte.Graphics.WebGPU/Buffers/`、`src/graphics/Lumyte.Graphics.WebGPU/Textures/` | 内部 handle の所有、memory を含む resource の生成・破棄、mapping と copy 用の接続。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Views/`、`src/graphics/Lumyte.Graphics.WebGPU/Bindings/` | view/sampler の実体化、内部 cache と bind group。group layout の実装は `Bindings/Layouts/` に置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Shaders/`、`src/graphics/Lumyte.Graphics.WebGPU/Pipelines/` | WGSL module と直接入力の接続、論理 pipeline と提出時に生成する実 pipeline の cache。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Commands/`、`src/graphics/Lumyte.Graphics.WebGPU/Submission/`、`src/graphics/Lumyte.Graphics.WebGPU/Synchronization/` | 記録の encode、queue 提出、CPU timeline と completion を分離する。object／batch の診断記録と、利用終了・成功の確定を接続する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Browser/Runtime/` と同 project の `Buffers/`、`Shaders/`、`Commands/`、`Submission/`、`Synchronization/` | ブラウザー固有の adapter/device、mapped memory、shader/encoder 呼出しと promise の interop。JavaScript 型を共通 Portable 契約へ漏らさない。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/` | 既存 xUnit project。production と対応する分野別ディレクトリに fake runtime を使う高速試験を置く。診断／queue 完了の到着順、別 batch と共有 object への診断帰属、device loss を個別に検証する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/` | 実 runtime/device を使う適合試験。無効な pipeline／command の提出を成功した upload として報告しないことも確認し、通常の unit test と分離する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Browser.Tests/Integration/` | 新設予定の隣接 xUnit project。ブラウザー process と GPU を要する interop 試験を隔離する。 |

低層の公開契約は新設予定の `src/graphics/Lumyte.Graphics.Portable/`、準備済み shader package と program の API は新設予定の `src/graphics/Lumyte.Graphics.Portable.Shaders/` が所有する。既存 WebGPU backend に Native adapter や file loader を追加しない。

## 使用例

`runtime` は利用可能な WebGPU ホスト環境、`options` は直接入力と必要 limit を要求する設定、`package` は `Lumyte.Resources` が読み込みと復号を完了した `PortableShaderPackage` とする。

```csharp
using Lumyte.Graphics.Portable.Shaders;

using var backend = await WebGpuBackend.CreateAsync(runtime, options);
var loader = new PortableShaderLoader(backend);
using var program = loader.Load(package);

Console.WriteLine(program.BindingLayouts.Count);
```

## 採用範囲と未実装事項

WebGPU の通常の binding model に直接接続する。Bindless エミュレーションと Native への adapter は採用しない。独立した Portable backend、WGSL package/loader、binding set、pipeline の提出時生成、直接入力対応 runtime の選定、object／batch 診断と利用終了・成功の接続は未実装である。
