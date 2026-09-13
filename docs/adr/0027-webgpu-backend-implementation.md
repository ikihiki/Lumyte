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
| `WebGpuBackend.CreateAsync(options = null)` | native host 用。配布した Dawn runtime の instance／adapter／device を自身で作成・所有する。Portable interface を直接実装した `WebGpuBackend` を非同期に返す。 |
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

### Native host の非同期接続

native host は WebGPUSharp 0.5.7 の同梱 Dawn C API を直接呼ぶ。既存の Silk descriptor を変換する `ModernWebGpuApi` は旧実装の一部であり、新しい Portable backend は経由しない。WGSL の `ImmediateAddressSpace` を instance で確認し、要求した feature／limit を device 作成へ渡す。`GpuRequiredLimits` の null だけを C API の undefined sentinel に変換し、その sentinel と同じ明示値は表現不能として拒否する。能力の比較や適合性は runtime に委ね、有効値は `DeviceGetLimits`／`DeviceHasFeature` から取得する。[WebGPUSharp](https://github.com/EmilSV/WebGPUSharp)

adapter／device 要求、map、error scope と device loss は `AllowSpontaneous` callback で受ける。callback 内では文字列をコピーして managed 結果を通知し、native API を再入呼出ししない。continuation は callback の外で実行する。callback の userdata は対応する callback まで保持し、device event の共有 userdata は device loss 通知で解放する。C API は loss 後に uncaptured error を呼ばないと保証している。[WebGPU C API の非同期操作](https://webgpu-native.github.io/webgpu-headers/Asynchronous-Operations.html)

`AllowSpontaneous` は callback の呼出しを許す設定であり、runtime が GPU の完了を発見する処理まで保証しない。native host は instance ごとに一つのイベント進行 thread を持ち、未完了の native future がある間だけ `InstanceWaitAny` を実行する。instance に `TimedWaitAny` を要求し、一度に一つの future を有限時間だけ待つことで、異なる発生源を同時に待つ C API の制約を避ける。未完了の future がなければ thread は休止し、登録または終了通知で再開する。公開の `CreateAsync`／`MapBufferAsync` は ValueTask を返して呼出し元を待たせず、backend の終了時には進行処理を終了してから instance を解放する。[C API の待機と発生源](https://webgpu-native.github.io/webgpu-headers/Asynchronous-Operations.html)

この内部保持は実行中の native future に限る。application resource の寿命管理、全 GPU work の暗黙待機や frame の進行制御は追加しない。Browser は JavaScript の Promise とホストのイベント処理へ接続し、この native thread を共通 Portable 契約へ持ち込まない。

native host は Dawn の `ImplicitDeviceSynchronization` feature も device 作成時に要求する。Dawn はこの feature の有効時だけ device の host mutex を作るため、イベント進行と利用者の native 呼出しを並行させるために必要である。これは Dawn 内部 object の host 操作を保護する設定で、GPU の queue 間依存や application resource の寿命を自動管理する機能ではない。Portable の GPU capability には加えず、この runtime 接続の条件とする。[Dawn の device 初期化](https://dawn.googlesource.com/dawn/+/refs/heads/main/src/dawn/native/Device.cpp)

Read mapping は const mapped range を `ReadOnlyMemory<byte>` に、Write mapping は writable mapped range を `Memory<byte>` に接続する。managed copy と後日の暗黙 write-back を用いず、native mapping を直接包む。`MemoryManager` が unmap 後の Span／Pin の再取得を拒否するが、すでに取得した Span／pointer の利用終了は caller が保証する。失敗した map の cleanup でも、成功していない二重 map を理由に既存 mapping を unmap しない。

map の callback が未完了のまま device loss で公開待機を終了した場合も、callback 用の native 参照は完了まで保持して解放する。これは実行中の interop 操作の保持であり、application resource を追跡・延命する registry ではない。

## Binding と shader

group ごとの layout を `GPUBindGroupLayout` へ、immutable binding set を `GPUBindGroup` へ接続する。texture view と sampler の cache は内部 object の再利用であり、global descriptor domain の管理ではない。binding object の利用終了後に内部参照を解放する。application resource の所有は caller のままとする。

layout と binding の入力 span は呼出し中にコピーする。生成後に caller が入力配列を書き換えても、作成済み object は変化しない。view の cache key は元 Texture の identity、省略値を解決した description と sampled／storage の用途とする。sampler は description を key にする。両 cache は生存する Bindings の参照だけを保持し、最後の参照を解放すると entry と native object を除く。途中の生成失敗では、その呼出しが取得した参照を戻す。caller の resource を自動破棄したり、利用終了後の object を無期限に保持したりしない。

view の format／dimension／mip／layer の省略値を元 description から解決し、明示した無効値は runtime の検証へ渡す。view の native usage は実際の binding 用途に限定し、元 Texture の attachment 用途をそのまま継承しない。Portable の `Depth24PlusStencil8` と `DepthOnly`／`StencilOnly` の組は、native の aspect 専用 format へ写す。公開 API に許可 ViewFormats の列を追加しない。

Buffer range の null length は C API の whole-size、view の未解決 count は undefined に写す。それらの sentinel と衝突する明示値、native 型で表現できない sampler anisotropy、未知の enum だけを変換境界で拒否する。binding の番号重複、size／offset／usage、format と layout の適合性は runtime が検証する。public `Normalize` は caller 向けの値計算であり、backend の native validation の前段には挿入しない。

layout、元 resource、内部 view／sampler と bind group 自身の生成診断を、当該 Bindings の依存として保持する。cache の再利用時も元の診断を引き継ぎ、別 object の失敗を混ぜない。同期生成の復帰だけでは成功を確定せず、後続の提出で実際に参照する object の診断を観測する。

Portable package から WGSL module と group layout、entry point を読み込む。root の構造体は package の immediate layout に従い、pipeline layout の `immediateSize` と command の `setImmediates` に接続する。`MaxImmediateSize` の有効値に従い、共通の固定 64 byte ABI は設けない。[WebGPU の直接入力仕様](https://gpuweb.github.io/gpuweb/#immediate-data)

WGSL の `immediate_address_space` と runtime の直接入力経路を初期化要件にする。仕様上存在する機能でも、選択した runtime が未実装なら対応済みと扱わない。root data の uniform/storage buffer 化は行わない。[WGSL の language feature](https://gpuweb.github.io/gpuweb/wgsl/#language-extensions)

Parameter Data は明示した buffer binding から shader が参照する。root に含む index/offset を shader で使用し、backend は byte 列を解析して resource 選択や upload を行わない。

## Pipeline と command

Portable の immutable description と program から、実際の提出で必要になった `GPURenderPipeline`／`GPUComputePipeline` を作る。固定 depth/stencil/blend を WebGPU pipeline へ含め、同じ論理 pipeline は生成済み object を再利用する。

native host の compute は raw WGSL module と group layout、ImmediateSize から内部 pipeline layout を作る。dispatch に使う論理 pipeline だけを最初の Submit で実体化する。native pipeline とその layout は論理 handle が所有し、全利用終了後の DestroyComputePipeline で解放する。module／group layout の元の生成診断も pipeline の診断に引き継ぐ。

提出時に必要な pipeline を揃え、各 command を encoder と render/compute pass に変換する。`SetBindings`／`SetComputeBindings` は対象 pass の `setBindGroup`、root 設定は `setImmediates` に変換する。copy は pass の外側で encode する。すべての encode を終えてから queue に提出する。

pass 内の usage 制約を独自に検証したり、見えない pass 分割で違反を修正したりしない。resource state の推移は WebGPU runtime が管理する。Native の barrier command や stage mask を解釈する層は設けない。

記録時に root bytes と dynamic offsets をコピーする。root は program の全 byte 列として扱い、各 dispatch に対して最後に指定した長さと ImmediateSize の一致を提出前に確認する。これは native setImmediates の部分更新を公開しない契約であり、root の解析や buffer 化ではない。pipeline の再設定でも wrapper による zero-fill を追加しない。Buffer copy の null length は元の生成値から解決し、logical range の同長と indirect range の12 byteだけを確認する。

## Completion と失敗

queue への提出後、その提出を含む `onSubmittedWorkDone` を caller 指定の timeline 値へ対応付ける。これは GPU 利用終了を知る入口であり、処理成功は別に確定する。timeline は CPU 観測用とし、GPU の semaphore や queue 間 wait を偽装しない。[WebGPU の queue completion](https://gpuweb.github.io/gpuweb/#dom-gpuqueue-onsubmittedworkdone)

native host は全記録を一つの QueueSubmit に渡し、後半の encode 失敗で前半だけを提出しない。timeline の内部領域を提出前に確保し、受理された値だけを照会対象にする。QueueOnSubmittedWorkDone の future は既存の instance event driver で進行させる。成功 callback 後に内部 native command buffer と記録 memory を回収し、診断が未確定でも GPU 利用終了を観測できるようにする。受理の有無や完了を確認できない interop 障害は device の共有失敗へ接続し、残った内部 command は device 終了時に解放する。application resource の registry は追加しない。

timeline の照会・待機は提出の native 呼出し gate と分離する。成功 batch は発行済み整数値の区間へ集約し、間にある未発行値を補完しない。失敗した batch の診断はその値に保持し、独立した後続 batch の成功へ混ぜない。待機取消しは当該 await だけを終了し、GPU work と他の待機を取り消さない。

### 診断を object と batch に帰属させる

同期形の WebGPU 呼出しで object が返っても、内部処理と診断は未完了の場合がある。backend は runtime の error scope を使い、診断の Promise と、それを発生させた操作の対応を内部に保持する。shader module、resource、layout、binding 等の生成診断は生成した object に、提出時 pipeline 作成・encode・submit の診断はその batch に結び付ける。再利用する pipeline の生成診断も、その pipeline の内部記録から参照できるようにする。[WebGPU の非同期 object 作成](https://gpuweb.github.io/gpuweb/#invalid-internal-objects-and-contagious-invalidity)

一つの runtime 呼出し区間を `validation`／`out-of-memory`／`internal` の error scope で囲み、全 scope を pop してから非同期に結果を待つ。同じ device への区間は backend の runtime 呼出し口で直列化し、scope を開いたまま await したり、別の並行呼出しの scope を横取りしたりしない。GPU 完了までこの直列化を維持する必要はない。shader の compilation info など付加診断を取得する場合も対応する object に結び付け、warning だけを実行失敗にはしない。[WebGPU の error scope](https://gpuweb.github.io/gpuweb/#error-scopes)

batch は実際に参照する object の生成診断と、自身の pipeline・encode・submit 診断をまとめて観測する。生成が別の提出より前でも、必要な診断を失わない。無関係な object や batch のエラーを「最後に提出した batch」へ付け替えない。scope が返した runtime の診断を保持し、wrapper が binding・format・usage・shader の validator を複製する処理は設けない。

提出を伴わない resource 操作は `GpuOperationException` に操作名と runtime の診断列を保持する。buffer mapping は生成診断と native map の両方が揃ってから成功する。要求した feature／limit を runtime が拒否した device request も、callback の status と message をそのまま診断へ写す。adapter がない、または必須の直接入力機能がない場合は `NotSupportedException` とし、buffer fallback へ進まない。

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

以下は repository root からの目標配置を含む。既存の WebGPU、WebGPU.Browser と WebGPU.Tests を改編して Portable 契約へ接続する。新しい `WebGpuBackend` は Portable を直接実装する。未移行の旧 `WebGpuDevice` と描画系は既存 source として残し、その factory だけを `Legacy/WebGpuBackend.cs` の `Lumyte.Graphics.WebGPU.Legacy` へ移す。Portable の互換経路にはせず、移行完了後に旧実装を除く。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.WebGPU/Device/` | `WebGpuBackend.CreateAsync`、device 所有、feature/limit と診断の接続。`IPortableGpuBackend` を直接実装する。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Interop/`、`src/graphics/Lumyte.Graphics.WebGPU/Diagnostics/` | Dawn C API の callback／userdata lifetime、instance の非同期イベント進行と、object ごとの診断・device loss の接続。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Buffers/`、`src/graphics/Lumyte.Graphics.WebGPU/Textures/` | 内部 handle の所有、memory を含む resource の生成・破棄、mapping と copy 用の接続。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Views/`、`src/graphics/Lumyte.Graphics.WebGPU/Bindings/` | view/sampler の実体化、内部 cache と bind group。group layout の実装は `Bindings/Layouts/` に置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Shaders/`、`src/graphics/Lumyte.Graphics.WebGPU/Pipelines/` | WGSL module と直接入力の接続、論理 pipeline と提出時に生成する実 pipeline の cache。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Commands/`、`src/graphics/Lumyte.Graphics.WebGPU/Submission/`、`src/graphics/Lumyte.Graphics.WebGPU/Synchronization/` | 記録の encode、queue 提出、CPU timeline と completion を分離する。object／batch の診断記録と、利用終了・成功の確定を接続する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Browser/Runtime/` と同 project の `Buffers/`、`Shaders/`、`Commands/`、`Submission/`、`Synchronization/` | ブラウザー固有の adapter/device、mapped memory、shader/encoder 呼出しと promise の interop。JavaScript 型を共通 Portable 契約へ漏らさない。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/` | 既存 xUnit project。production と対応する分野別ディレクトリに fake runtime を使う高速試験を置く。診断／queue 完了の到着順、別 batch と共有 object への診断帰属、device loss を個別に検証する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/` | 実 runtime/device を使う適合試験。無効な pipeline／command の提出を成功した upload として報告しないことも確認し、通常の unit test と分離する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Browser.Tests/Integration/` | 新設予定の隣接 xUnit project。ブラウザー process と GPU を要する interop 試験を隔離する。 |

低層の公開契約は `src/graphics/Lumyte.Graphics.Portable/`、準備済み shader package と program の API は新設予定の `src/graphics/Lumyte.Graphics.Portable.Shaders/` が所有する。既存 WebGPU backend に Native adapter や file loader を追加しない。

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

WebGPU の通常の binding model に直接接続する。Bindless エミュレーションと Native への adapter は採用しない。native host の独立した Portable backend、Dawn の直接入力を要求する非同期初期化、有効 feature／limits、Buffer／Texture の生成・破棄、非同期 mapping、Binding Layout／Bindings、view／sampler の内部再利用と object ごとの依存診断を実装した。raw WGSL、compute pipeline の提出時生成、直接 root／dynamic offsets／直接・間接 dispatch、buffer copy と CPU completion も接続した。内部公開は WebGPU.Tests 向けだけで、Portable 側は public／protected 契約から実装する。

WGSL package/loader、raster pipeline／attachment／draw、texture copy と Browser の runtime 借用形 factory は未実装である。Slang の build toolchain と生成 host 型の統合も後続とする。実機試験結果と検証できていない失敗経路は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。
