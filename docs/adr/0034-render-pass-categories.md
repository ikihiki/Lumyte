# ADR 0034: レンダーパスのカテゴリと標準機能

## 状態

採用（目標設計）。実装する機能 pass を用途で分類し、標準機能と追加候補を区別する。モデル描画と 2D 描画の詳細はそれぞれ独立した ADR で定める。以下の API は目標であり、新しい二系統での実装完了を示さない。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0001 Graphics](0001-graphics-api.md) | 二系統の境界、色・座標と低層への責務委譲 |
| [0030 RenderGraph](0030-render-graph-api.md) | logical resource、依存、提出、frame と presentation |
| [0033 機能 RenderPass](0033-feature-render-passes.md) | 共通 request と二系統の pass 本体、shader 所有 |

## 決定

レンダーパスを次の五カテゴリに分ける。分類は利用者が追加する機能の単位であり、raster／compute／copy の GPU command 区分とは独立する。どのカテゴリも共通 API から追加し、本体は Native／Portable ごとに用意する。

| カテゴリ | 実装する公開機能 | 主な入出力 | 担当範囲 |
| --- | --- | --- | --- |
| 初期化・転送 | Clear、Texture Copy | 値・画像 → 既存 target | この ADR。資源の初期化と画素の変更を伴わない転送 |
| 画像処理 | Blit、Blur | 画像 → 画像 | この ADR。拡大縮小と単体の画像 filter |
| 合成・表示 | Composite、ToneMap、Output | 複数画像／HDR 画像 → 合成結果／表示 target | この ADR。合成と表示色への変換 |
| モデル描画 | Model | 独立した geometry／material／変形データから成る描画項目、camera、lighting → Color／Depth | 独立したモデル描画 ADR。形式に依存しないモデル入力と、glTF を含む描画能力 |
| 2D 描画 | 2D scene | path、image、text、clip、layer → Color | 独立した 2D ADR。既存 TwoD／Text の能力を基準にする |

モデルの opaque／mask／blend、skinning、morph、2D の atlas 更新、mask、layer blur、glyph の描画などは各機能の内部 pass である。利用者にそれらを手動で並べることを要求しない。GPU upload、mipmap 作成なども、その機能の実現に必要なら各本体が内部で行う。元ファイルのロード・decode は Lumyte.Resources が済ませる。実行時のノード管理、animation／物理の評価、手続き生成は ECS／scene 等の上位が行い、Graphics へ不変の描画データを直接渡せる。

`AddDraw`、`AddFullscreen`、`AddCompute` は実装手段であり、標準の利用者向け機能カテゴリにはしない。共通 API に shader、pipeline、root、bindings を受け取る汎用 pass は設けない。独自の効果は共通 contract と二系統の本体として追加する。

## 現行実装との対応

| 現行の機能 | 新しい標準機能での扱い |
| --- | --- |
| Library の `AddClear` | Clear の振舞いを移す。target の低層 view や生成済み pipeline は公開しない |
| Library の `AddBlit`／`AddComposite` | 固有の意味と内部 shader を持つ Blit／Composite として再定義する |
| Library の `AddDraw`／`AddFullscreen`／`AddCompute` | 各系統の本体から使う実装手段にする |
| TwoD の copy／mask／filter／horizontal・vertical blur／合成 | 2D 本体の内部処理として維持する。画像処理の本体と内部 helper を共有してよい |
| RenderGraph の upload | 準備済みデータから各本体が GPU 資源と staging を準備する処理に置く |
| 汎用 Model、ToneMap、Output | 新たに機能契約と両本体を実装する。Model の必要能力に glTF 描画を含める |

削除前の Blit／Composite は caller の DrawMaterial を Fullscreen に渡す wrapper であり、本 ADR の shader を管理しない機能 pass とは異なる。新しい Blit／Composite は本契約に従って実装する。

2D に必要な描画能力は ADR 0036 の調査表に記録する。旧実装と専用テストは削除済みであり、新しい機能の適合試験を各系統の本体に追加する。

## API

以下は `Lumyte.Graphics.Passes` に置く。各 extension は対応する `XxxPassContract.Instance` と request を共通 AddPass に渡す。固定の構成は Snapshot で固定し、変化する値は ADR 0030 の `GpuGraphValue<T>` により定数または型付き input slot で渡す。値の差替えには同じ plan と新しい bindings を使い、利用側へ個別の shader ロード API は公開しない。

Blit／Blur／Composite／ToneMap／Output の初期 version は、2D、1 mip、1 layer、1 sample の color image を対象とする。それぞれの Write はこの画像全体を初期化する意味である。Clear と Copy は専用の範囲規約を使い、モデルと 2D は各 ADR が出力条件を定める。

### 初期化・転送

| API | 契約 |
| --- | --- |
| `AddClearPass(name, ClearPassRequest)` | target 全体を初期化し、`TexturePassResult` を返す |
| `ClearPassRequest(Target, Value)` | 固定の logical texture と `GpuGraphValue<TextureClearValue>`。物理 view や clear 用 pipeline は指定しない |
| `TextureClearValue.Color(value)`／`DepthStencil(depth, stencil)` | 線形 straight RGBA、または depth／stencil の値を表す CPU の選択型 |
| `AddCopyPass(name, TextureCopyPassRequest)` | 同じ外部 description の異なる texture へ内容をそのままコピーし、`TexturePassResult` を返す。拡大縮小・色変換・合成はしない |
| `TextureCopyPassRequest(Source, Target)` | 全 mip／layer を対応させてコピーする。初期の共通契約は単一 sample の texture とする |
| `TexturePassResult(Target)` | 書き込んだ logical texture。所有を増やさない |

Clear は target 全体を Write、Copy は source を Read、target 全体を Write とする。Color clear は記憶される表現に合わせて premultiply する。DepthStencil clear は target が持つ depth／stencil aspect 全体を初期化する。clear の合法性や copy の format 条件の独自 validator は作らない。

Copy の適用範囲は共通の byte／pixel 表現が成立する資源とする。モデルや 2D の実装専用 buffer の layout をコピーして、別系統で同じ意味になるとはみなさない。Buffer copy、領域 copy、CPU readback は、この最初の公開契約へ含めず、必要になった際に別の入出力契約を定める。

### 画像処理

| API | 契約 |
| --- | --- |
| `AddBlitPass(name, BlitPassRequest)` | source 全体を target 全体へ拡大縮小して書き込み、`TexturePassResult` を返す。filter、形状変換と色空間変換を分ける |
| `BlitPassRequest(Source, Target, Filter = Linear)` | 固定の別々の logical texture と `GpuGraphValue<ImageSampling>`。入力・出力は同じ意味の色領域とする |
| `ImageSampling` | `Nearest`／`Linear`。source の端は clamp する |
| `AddBlurPass(name, BlurPassRequest)` | 新しい出力 texture を宣言し、`BlurPassResult` を返す |
| `BlurPassRequest(Source, Radius)` | 固定の source と `GpuGraphValue<int>` の pixel 単位の非負整数 radius。shader、workgroup、内部 pass 数を指定しない |
| `BlurPassResult(Color)` | source と同じ extent の blur 結果。内部の中間 texture を公開しない |

Blit の座標は target pixel center を source の全範囲へ対応付ける。alpha は premultiplied のまま filter し、target 全体を初期化する。部分描画・先行 target との合成は 2D または合成機能で扱い、Blit へ暗黙の blend を加えない。

Blur version 1 は線形 premultiplied RGBA の box filter とする。`(2 × Radius + 1)` 四方を均等に平均し、範囲外の座標は端へ clamp する。radius 0 は数学的には恒等であり、格納時には出力 format への量子化を含む。Native の一回の compute と Portable の水平・垂直二段など、同じ意味を持つ別の実装を認める。2D の layer 用 Gaussian blur とは別の効果として扱う。

Blit／Blur は source を Read、target／新規出力を Write とする。処理中の結果は線形 RGBA、Blur の出力 format は `GpuFormat.Rgba16Float` とする。多段 filter の中間も出力相当以上の精度を保ち、低精度の中間格納による誤差増幅を避ける。演算と量子化の許容誤差は適合試験で固定する。mip chain、array、depth、integer texture 用の filter は追加契約とする。

### 合成・表示

| API | 契約 |
| --- | --- |
| `AddCompositePass(name, CompositePassRequest)` | 指定順に画像を SourceOver で合成し、`TexturePassResult` を返す |
| `CompositePassRequest(Target, Layers, Content = Preserve, ClearColor = Transparent)` | 固定の target、layer 集合と Content。ClearColor は線形 straight RGBA の `GpuGraphValue<Vector4>`。target と source の extent は一致させる |
| `CompositeLayer(Source, Opacity = 1)` | 固定の logical source と `GpuGraphValue<float>` の不透明度（0..1）。GPU material は受け取らない |
| `TargetContent` | `Preserve` は既存内容を残す。`Clear` は target 全体を ClearColor で初期化する |
| `AddToneMapPass(name, ToneMapPassRequest)` | scene-linear HDR を display-linear SDR へ変換し、`ToneMapPassResult` を返す |
| `ToneMapPassRequest(Source, ExposureStops = 0)` | 固定の HDR source と `GpuGraphValue<float>` の exposure。最初の契約では下記の Reinhard curve を固定し、別の curve は別 version／効果とする |
| `ToneMapPassResult(Color)` | 同じ extent の単一 sample `Rgba16Float` 出力。RGB の意味上の範囲は `0..1`、alpha は保持する |
| `AddOutputPass(name, OutputPassRequest)` | display-linear SDR を表示 target へ書き込み、`TexturePassResult` を返す。window の Present は行わない |
| `OutputPassRequest(Source, Target, Encoding = Srgb, AlphaMode = Opaque)` | 同じ extent の別 texture と、host が選んだ表示の色／alpha 契約。Native／Portable の型は受け取らない |
| `OutputEncoding` | `Srgb`／`Linear`。最終転送時に一度だけ符号化する |
| `OutputAlphaMode` | `Opaque` は alpha を 1 とする。`Premultiplied` は出力の色表現で RGB を premultiply する |

Composite は各 source を Read し、Content が Preserve なら target を ReadWrite、Clear なら Write とする。SourceOver の opacity は premultiplied RGB と alpha の両方へ適用する。layer 順を変える batching はしない。拡張合成 mode、path と任意 transform を持つ描画は 2D の scene に置く。

ToneMap は source を Read し、新しい出力を Write とする。alpha が正なら RGB を unpremultiply し、exposure を掛けた非負値 `x = max(rgb × 2^ExposureStops, 0)` に `x / (1 + x)` を適用して、元の alpha で premultiply する。alpha 0 の RGB は 0 とする。光量調整や tone mapping はモデル描画へ暗黙に混ぜない。

Output は source を Read、target 全体を Write とする。opaque 出力では入力が不透明であることを機能上の前提とし、透過 scene の背景合成は先に行う。Premultiplied 出力では線形 RGB を一度 unpremultiply し、指定した出力 encoding へ変換してから出力 alpha を掛ける。shader と render target の自動 sRGB 変換で二重符号化しない。出力 target の format と encoding の具体化は各本体が行う。

`AddOutputPass` は pixel の出力変換であり、acquire／Present／Discard や target の所有を持たない。提出と Present は共通 frame の API に従う。HDR display、wide gamut、ICC color management はこの最初の出力契約に含めない。

### 再利用する入力と構成

以下の入力 contract は `IGpuGraphInputContract<T>` を実装する。Snapshot は小さい値を不変に固定し、Retain は upload data を保持しないため空とする。Declare は各値を ReadInput で登録し、各本体は GetInput で今回の値を取得する。

| 入力 contract | 値／用途 |
| --- | --- |
| `ClearValueInputContract.Instance` | TextureClearValue／Clear の値 |
| `ImageSamplingInputContract.Instance` | ImageSampling／Blit の filter |
| `BlurRadiusInputContract.Instance` | int／Blur の非負整数 radius |
| `LinearColorInputContract.Instance` | Vector4／Composite の線形 straight clear 色 |
| `CompositeOpacityInputContract.Instance` | float／layer の不透明度 |
| `ExposureInputContract.Instance` | float／ToneMap の exposure stops |

Source／Target の logical resource、Composite の layer 数・順序と source、Content の ReadWrite／Write の選択、Output の表示規約は固定構成にする。それらを変える場合は新しい plan を作る。radius、filter、opacity、exposure、clear 値だけの変更は再 Compile を要求しない。毎回異なる表示用 texture は、固定の texture input の実資源 binding を替える。

同じ plan でも、Blur の内部処理や Composite の batch は今回の値に応じて変えてよい。中間資源は実行ごとに使用期間を区別し、各本体は有効な template／pipeline 記述だけを再利用する。GPU command の毎回の記録や GPU 演算を省略できるとは限らない。

### モデル描画と 2D 描画の入口

| API family | この分類で保証する境界 |
| --- | --- |
| `AddModelPass` | 保持集合の不変 snapshot を typed input で渡し、変更 draw だけ更新する。logical Color／Depth は固定する。geometry、material、変換と変形を共有し、glTF を含む能力を保つ。loader やノード／ECS 管理 API は設けない |
| `Add2DPass` | 保持 scene の変更部分を共有した不変値、画像／glyph と固定の logical Color・画像依存を渡す。scene の値だけの更新で graph を再構築しない。clip、layer、合成と既存能力を保持する |

この二つの詳細な request／result、scene 型、使用例と受入れ条件は各専用 ADR に置く。この文書で同じ API を重複して定義しない。

## 色と資源の共通契約

CPU の色値は明示した線形 straight RGBA、filter／合成に使う画像は線形 premultiplied RGBA とする。基準の色域は sRGB primaries とし、入力画素の Encoding に従う sRGB から線形値への変換と表示の encode は、描画の出入口で一度だけ行う。PNG 等のファイル復号は Lumyte.Resources の責務とする。tone mapping と alpha の扱いは各機能の意味であり、GPU command の副作用にはしない。

典型的な順序は `Model（HDR）→ ToneMap（linear SDR）→ 2D／Composite → Output → frame.SubmitAsync` とする。2D の color 表現はモデル描画に依存せず、2D 単独でも Clear → 2D → Output の順で利用できる。特定の順序を graph が自動挿入するのではなく、library が意図する機能を追加する。

共通 transient の GPU usage は各本体の内部使用から provider が決める。persistent な GPU export は、uploader が転送データの Profile に対応した用途を生成前に用意する。既存 object の usage を後から広げたり、同じ target を入力と出力にして無条件に sample／write したりしない。自己参照が必要な効果は別の logical input を用意する。

sampling、mipmap、color format の合法性は native API／WebGPU runtime に委ねる。Lumyte は機能契約の数値、初期化範囲、依存と CPU 操作を確認し、低層仕様の validator を複製しない。

## コード配置

以下は repository root からの相対パスによる目標配置である。初期化・転送、画像処理、合成・表示は同じ `ImageProcessing/` にまとめ、分類のためだけに project を分割しない。以下の feature／Hosting／test project は新設予定である。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Passes/ImageProcessing/` | Clear／Copy／Blit／Blur／Composite／ToneMap／Output の request、result、契約と追加 extension。色・sampling・出力の値型と六つの input contract もここで所有する。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/ImageProcessing/`、`src/graphics/Lumyte.Graphics.Portable.Passes/ImageProcessing/` | 七機能の系統別本体と内部 pass。Native の descriptor、Portable の binding と GPU 準備を各本体で扱う。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/ImageProcessing/Shaders/`、`src/graphics/Lumyte.Graphics.Portable.Passes/ImageProcessing/Shaders/` | Native の Slang entry と Portable の Slang／直接 WGSL entry、専用 resource／root 宣言。clear／copy を API 命令だけで実行する本体に不要な shader を要求しない。 |
| `src/graphics/Shaders/Shared/ImageProcessing/`、`src/graphics/Shaders/Shared/Color/` | filter の重み、座標・色変換、合成、ToneMap 等の計算用 Slang module。sampling と stage ごとの処理は系統別 entry に残す。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/ImageProcessing/GpuData/`、`src/graphics/Lumyte.Graphics.Portable.Passes/ImageProcessing/GpuData/` | 専用 shader 入力の組立て。生成 C# と artifact は各 project の `obj/<Configuration>/<TargetFramework>/Shaders/Native/` または `Shaders/Portable/` に分ける。 |
| `src/graphics/Lumyte.Graphics.Passes.Hosting/ImageProcessing/` | `AddImageProcessing()` と七機能・二系統の本体／shader 準備の起動登録。 |
| `src/graphics/Lumyte.Graphics.Passes.Tests/Unit/ImageProcessing/` | 共通 request、色変換の意味、input contract、固定依存の xUnit 試験。 |
| `src/graphics/Lumyte.Graphics.Native.Passes.Tests/Unit/ImageProcessing/`、`src/graphics/Lumyte.Graphics.Portable.Passes.Tests/Unit/ImageProcessing/` | 本体準備・入力更新・所有の GPU 不要な xUnit 試験。 |
| `src/graphics/Lumyte.Graphics.Native.Passes.Tests/Integration/ImageProcessing/`、`src/graphics/Lumyte.Graphics.Portable.Passes.Tests/Integration/ImageProcessing/` | 同じ consumer 入力による画素・色・alpha と更新結果の GPU 適合試験。unit suite と分離する。 |
| `benchmarks/Lumyte.Benchmarks/Graphics/ImageProcessing/` | 既存 benchmark project に追加する plan 再利用、入力変更と効果ごとの CPU／GPU 負荷の計測。 |

旧 Library と shader は削除済みであり、汎用 Draw／Dispatch API を共通 feature project に移す互換層は作らない。Models／TwoD はそれぞれ専用の機能ディレクトリが所有し、表示 window の取得・Present は RenderGraph の presentation と Hosting integration の担当に残す。

## 使用例

`frame` は取得済みの共通 frame、`source` は graph に import 済みの線形画像とする。各 pass の shader は二系統の実装 package に含まれる。

```csharp
using Lumyte.Graphics.Passes;

var graph = frame.Graph;
var blurred = graph.AddBlurPass("blur", new BlurPassRequest(source, Radius: 3));
var display = graph.AddToneMapPass("tone-map", new ToneMapPassRequest(blurred.Color));
graph.AddOutputPass("output", new OutputPassRequest(display.Color, frame.TargetResource));

using var execution = await frame.SubmitAsync(cancellationToken);
```

この例の source は不透明な HDR 画像とする。通常の SDR 画像には ToneMap を加えず、2D／Composite で必要な背景を合成して Output へ渡す。completion を待つ必要がある caller は execution の非同期待機を使う。

## 実装順と完了条件

| 段階 | 対象 | 完了の判定 |
| --- | --- | --- |
| 0 | 起動から終了までの最小統合。共通契約、二 provider、Clear／Copy、基本 Output | 一度 build した consumer を設定だけで切り替え、起動 → Clear／Copy／Output → 提出結果 → 使用保持の回収 → 終了を通す |
| 1 | 同じ plan の反復提出、bindings 更新、Blit、初回表示・resize と失敗処理 | plan を再構築せずに色や入力を変更でき、target 形状変更だけで再構築する。受理前失敗・遅延診断・取消し・device loss を区別し、失敗した内容を公開しない |
| 2 | 準備済み 2D／glyph データの二系統描画、Blur／Composite | 既存機能の対応表を満たし、境界・alpha・clip・文字・layer と順序を確認 |
| 3 | 形式に依存しない Model 描画項目、独立した転送データ、skin／morph／PBR、ToneMap | glTF の必要能力、別形式由来のデータ、上位 ECS の Component 抽出と動的更新を両実装で確認。ロードと実行時評価は各上位の責務として準備 |
| 4 | 各専用 ADR の追加提案 | 機能ごとの入出力と受入れ条件を決めてから実装 |

段階は統合と受入れの順序を示す。モデルと 2D の開発は並行できる。最初の対応 backend は Native の DirectX 12／Vulkan と Portable の WebGPU とし、shader と内部 command の一致ではなく機能の結果で比較する。

段階 0 の試験は最小の入力と出力値に絞り、Host の非同期初期化、scope／execution の返却、presentation の利用終了、runtime と device の終了までを結ぶ。CPU の起動・所有・失敗は fake で試験し、DirectX 12／Vulkan／WebGPU の実機 suite は分離する。実行できない backend は未確認として記録する。全 PBR、glyph atlas と部分更新の最適化を最初の統合の前提にしない。

段階 1 では、後続 pass の BuildAsync 失敗で先行の cache 世代が公開されないこと、queue 受理前の失敗、受理済みで未完了の世代共有、遅延した WebGPU 診断と成功判定、古い bindings の再提出を個別に確認する。Native の alias 再有効化と depth／stencil 転送は対応する下位 API の適合試験で確認し、共通 pass の機能テストに native command の一致を要求しない。

Slang 共有は、色変換など小さい計算 module と各 target の数値 fixture から始める。Portable の Slang 経路は最終 WGSL と直接 root の適合を確認してから機能へ広げる。Native の mesh／amplification の基礎試験は並行できるが、最小統合の必須 device 機能に加えない。Model の従来描画が成立した後、同じ入力で mesh 経路の適合と効果を測り、対応条件に応じて選択する。

## 追加候補

| 分野 | 候補 | この時点の扱い |
| --- | --- | --- |
| 追加の画像処理 | Gaussian blur、ColorMatrix、Bloom、mipmap／MSAA resolve の公開 pass | 2D やモデルの内部処理は実装できるが、独立した公開契約は未採用 |
| 3D の拡張表現 | shadow、SSAO、SSR、volumetric、particle、terrain | glTF core 描画の完了条件へ混ぜず、別の機能設計として検討 |
| 時間履歴 | TAA、temporal upscaling、motion blur | history の所有、reset、motion vector と frame 間依存を決めてから採用 |
| 解析・デバッグ | object picking、depth／normal の可視化、histogram、readback | 結果の取得・非同期完了を含む専用契約が必要 |
| 色管理 | HDR display、wide gamut、ICC | 共通色値と presentation の追加契約が必要 |

## 採用範囲と未実装事項

固定構成と型付きフレーム値を分けた plan 再利用を採用する。小さい入力値の差替え、未変更 snapshot の共有、provider 内の template／準備 cache／内容世代を実装した。モデルと 2D の保持集合と各専用の部分更新は未実装であり、60 FPS の実測結果を示さない。

五カテゴリと、Clear／Texture Copy／Blit／Blur／Composite／ToneMap／Output の標準機能、および独立したモデル・2D の機能群を目標として採用する。追加候補を実装済みまたは必須対応とは扱わない。

段階 0 の Clear／Texture Copy／Output について共通 request／result／contract、両系統の本体、専用 Output shader と Hosting 登録を実装した。Output version 1 の範囲は同一 extent の 2D・1 mip・1 layer・1 sample として確認し、その範囲外を全画像初期化済みとして扱わない。Copy version 1 は単一 sample とする。

Blit／Blur／Composite／ToneMap、`Rgba16Float` を含む後続機能の format、Model と新しい 2D は未実装である。同じ plan への Clear 値の差替え、内部 graph の計画 cache と内容世代の再利用は利用できる。複数フレームの実機適合は 60 FPS の性能測定とは区別する。ここで定めた GPU 機能は旧 API への互換層ではない。

適合試験は同一 consumer binary で行い、Clear／Copy の内容、Blit の pixel center、Blur の境界と半径、Composite の順序・alpha、ToneMap と Output の色変換、ReadWrite、失敗時の保持を確認する。数値 filter の tolerance は参照 CPU 計算、出力 format と演算精度に基づいて fixture ごとに定め、bit 一致を前提にしない。
