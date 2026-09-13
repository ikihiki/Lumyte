# ADR 0033: 機能単位の RenderPass と二系統の実装契約

## 状態

採用（目標設計）。機能 pass の共通契約、作者の責務と二系統への登録を定める。モデル描画、2D 描画、画像処理など個別機能の API は、カテゴリおよび機能ごとの文書へ分離する。現行実装の完了を示さない。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0015 Native Shader](0015-native-shader-package-api.md) | Native の Slang build、stage と専用 package |
| [0023 Portable Shader](0023-shader-design-and-api.md) | Slang／WGSL build、直接 root と専用 WGSL package |
| [0030 RenderGraph](0030-render-graph-api.md) | 共通 pass 契約、外部入出力、snapshot と実行 |
| [0031 Native RenderGraph](0031-native-render-graph-implementation.md) | Native 本体の登録、準備と内部 graph |
| [0032 Portable RenderGraph](0032-portable-render-graph-implementation.md) | Portable 本体の登録、準備と内部 graph |

## 決定

共通化する単位は「Model を描く」「画像をぼかす」「2D の内容を重ねる」といった機能 pass とする。利用者は CPU 入力と logical resource を渡す。準備済み shader の選択・GPU program 生成、GPU 構造体、Draw／Dispatch、descriptor／binding の準備は各系統の pass 本体が所有する。

作者は共通の契約 library と Native／Portable の二つの実装 library を作る。同じ機能でも、一方を一つの compute pass、他方を複数の raster／compute pass として実装できる。外部の意味、入出力と依存に加え、shader 内の計算を Slang source module として部分共有する。内部の GPU 命令列、shader entry、resource 宣言と GPU ABI は各系統の本体で決める。

機能 request は固定の外部構造と `GpuGraphValue<T>` のフレーム入力を区別する。前者から一度 plan を作り、後者だけを変更した bindings で繰り返し実行できる。描画対象の増減や配置、材質、camera、2D scene、効果の半径を更新しても、外部入出力と依存が同じなら AddPass と Compile を繰り返さない。

| Assembly／namespace | 担当 |
| --- | --- |
| `Lumyte.Graphics.Passes` | 機能の CPU request／result、contract と AddPass extension。共通 RenderGraph と各機能の CPU library に依存する。 |
| `Lumyte.Graphics.Native.Passes` | Native 本体、準備済み Native shader package、生成 GPU 構造体、uploader と cache。 |
| `Lumyte.Graphics.Portable.Passes` | Portable 本体、準備済み Portable shader package、生成 GPU 構造体、uploader と cache。 |

カテゴリは機能を整理する単位であり、カテゴリごとの独立 assembly を必須にしない。モデルや 2D の API は独立した ADR で定め、共通の追加操作は同じ namespace から利用できる。独自 library もこの三つの責務を分ける。共通型を各実装 assembly に重複定義しない。

ロードと形式の解釈は Lumyte.Resources が担当する。この共通契約が追加する資産関連の型は GPU への受け渡しデータに限定し、stream、URI、lazy reader、再ロード用 callback を持たせない。scene の組立てと pass の追加は描画 API として残す。

## API

### 共通契約を定義する API

| API | 契約 |
| --- | --- |
| `IGpuRenderPassContract<TRequest, TResult>` | 共通の CPU request／result に対する機能契約。 |
| `Id`／`Version` | 機能の意味と版を識別する。shader entry point や GPU ABI の識別子にはしない。 |
| `Snapshot(request)` | 外部 resource と入力 slot を含む要求構造を不変にする。変わる値は input contract に分離する。 |
| `Declare(context, request)` | 固定の外部 resource の Read／Write／ReadWrite、CPU 入力と出力を宣言して TResult を返す。今回のフレーム値、GPU 処理、shader ロードと backend 分岐は扱わない。 |
| `GpuGraphValue<T>.Constant(value)`／`FromInput(input)` | request に定数または型付き入力 slot を渡す。同じ AddPass で単発描画と反復描画を扱う。 |
| `GpuPassDeclarationContext.ReadInput(value, inputContract)` | 値または slot と input contract をこの pass に登録する。外部 resource の依存を増やさない。 |
| `IGpuGraphInputContract<T>.Snapshot(value)` | 所有済みの不変入力へ固定する。既存の不変データと未変更部分木は共有する。 |
| `IGpuGraphInputContract<T>.Retain(context, snapshot)` | 型が持つ upload data と logical resource の直接参照を登録する。未変更部分木の保持情報を再利用する。 |
| `GpuRenderInputRetentionContext.ReadUpload(data)` | 型に従った CPU データの所有を保持する。子参照は明示し、ID 解決やロードを行わない。 |
| `GpuRenderInputRetentionContext.UseDeclared(resource)` | 入力内の logical resource が、この pass の固定 Read／ReadWrite 宣言にあることだけを確認する。 |
| `GpuPassDeclarationContext.ReadUpload(data)` | 固定 request に含む不変データの所有を保持する。フレームごとに変わる値は ReadInput と入力契約に分ける。 |
| `graph.AddPass(name, contract, request)` | snapshot と宣言を一つの機能 node として登録する。GPU 記録 callback は受け取らない。 |

各機能の `AddXxxPass` extension は、対応する contract の `Instance` と request をこの AddPass に渡す。利用者の描画コードに provider 選択の分岐は含めない。resource 作成、使用宣言、名前の分離、外部副作用の保持は ADR 0030 に従う。

request は機能が定める CPU の意味から設計し、shader の入力構造体から生成しない。色、行列、camera、メッシュ、画素、path、配置済み glyph といった入力は共有できる。GPU pointer、descriptor index、binding group、program handle は公開しない。

input contract はその機能の契約版に含め、Snapshot と Retain だけを公開する。後から ECS の「最新値」を読む callback、差分履歴や資産を取得する loader は持たない。単なる値の入力は Snapshot で値を返し、Retain を空にできる。graph.CreateInput、plan.CreateBindings、Set と Build の API と確定時点は ADR 0030 に従う。

外部 logical resource を含む 2D scene 等は、使い得る資源を request の固定部分で宣言し、input contract が UseDeclared で確認する。入力の差替えを理由に未宣言の edge を追加しない。外部入出力、依存または出力形状が変わる場合は graph を作り直す。準備済み geometry／画像等から本体が作る内部資源の増減は、共通の外部依存を変えない。

### 二系統へ登録する API

| API | 契約 |
| --- | --- |
| `NativeRenderPassRegistry.Register(contract, factory)` | composition 用の bootstrap API。共通契約に runtime ごとの `INativeRenderPass<TRequest, TResult>` factory を対応付ける。 |
| `PortableRenderPassRegistry.Register(contract, factory)` | composition 用の bootstrap API。同じ契約に runtime ごとの `IPortableRenderPass<TRequest, TResult>` factory を対応付ける。 |
| 各本体の `BuildAsync(context, request, result, cancellationToken)` | 固定 request と今回の不変入力から GPU 資源と内部 pass を準備する。内部 template と未変更部品を再利用する更新入口でもある。 |
| 各本体の `context.GetInput(value)` | ReadInput に登録した GpuGraphValue を今回の bindings または定数の不変値に解決する。 |
| 各本体の `context.RegisterContent(...)`／`TryUseContent(generation, out content)` | GPU 内容と writer の結果依存を登録し、後続の構築で使用保持と依存を伴って取得する。型と overload は各 provider の SPI に従う。 |
| 各本体の `DisposeAsync()` | runtime が構築・記録・GPU 使用の終了を保証した後、実装の所有を終了する。 |

factory、専用サービス、内部 AddPass と recording context の詳細は ADR 0031／0032 に置く。内部の callback は Native／Portable の command API を使う。共通の機能追加と内部 GPU 記録で AddPass の対象を区別する。

GPU cache を持つ作者は、CPU 側の cache key と provider の内容世代 ticket を対応付ける。RegisterContent は転送の成功通知ではない。再利用時は TryUseContent が取得した内容だけを今回の内部 graph に結び付け、取得できなければ保持した CPU snapshot から再構築する。Retain の Dispose を成功通知に使わない。consumer の AddPass や起動 registry に内容世代の登録を追加する必要はない。

機能 library は Generic Host から一度登録できる Hosting integration を用意する。integration が service collection へ登録定義と型付き factory を追加し、起動時に必要な CPU 依存と準備済み shader package を各系統の core factory へ渡す。通常の描画利用者は registry を作らず、利用準備完了の共通 runtime を借用して AddPass を呼ぶ。共通 contract と pass 本体へ `IServiceProvider` を追加せず、BuildAsync やフレーム入力から DI を解決しない。

登録集合は runtime 作成前に不変 snapshot とし、登録のたびに shader I/O や GPU 生成を行わない。shader の取得・展開は Hosting integration の非同期準備から `Lumyte.Resources` に委譲する。GPU を保持する本体は runtime ごとに生成し、DI singleton として runtime 間で共有しない。factory が返した本体の破棄は runtime が担当し、本体が借用する CPU 依存の DI scope は runtime 終了後に integration が終了する。

## コード配置

以下は repository root からの相対パスによる目標配置である。feature の共通契約、二系統の本体、Hosting integration はそれぞれ新設 project とする。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Passes/ImageProcessing/`、`src/graphics/Lumyte.Graphics.Passes/Models/`、`src/graphics/Lumyte.Graphics.Passes/TwoD/` | 機能別の公開 request／result、pass contract、input contract、`AddXxxPass` extension。モデルの不変転送データも `Models/` に置く。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/` | 共通 project と同じ機能別ディレクトリに Native 本体、内部 pass、upload と cache を置く。 |
| `src/graphics/Lumyte.Graphics.Portable.Passes/` | 同じ機能別ディレクトリに Portable 本体、内部 pass、upload と cache を置く。Native 本体の adapter は置かない。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/<Feature>/Shaders/`、`src/graphics/Lumyte.Graphics.Portable.Passes/<Feature>/Shaders/` | 作者が管理する系統別 shader entry、resource／root 宣言と build 入力。Native は Slang、Portable は Slang または直接 WGSL。`<Feature>` は `ImageProcessing`、`Models`、`TwoD` のいずれか。 |
| `src/graphics/Shaders/Shared/Math/`、`src/graphics/Shaders/Shared/Color/`、`src/graphics/Shaders/Shared/ImageProcessing/`、`src/graphics/Shaders/Shared/Models/`、`src/graphics/Shaders/Shared/TwoD/` | 計算用 Slang module。汎用の数学・色と機能別の計算に分ける。両 pass project の build が import し、runtime assembly や共通 GPU ABI の project は作らない。独自 pass library は同様の source 資産を自身で配布できる。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/obj/<Configuration>/<TargetFramework>/Shaders/Native/`、`src/graphics/Lumyte.Graphics.Portable.Passes/obj/<Configuration>/<TargetFramework>/Shaders/Portable/` | build が生成する C# GPU 入力と shader artifact。source と分け、共通契約 project へ生成しない。 |
| `src/graphics/Lumyte.Graphics.Passes.Hosting/` | 機能別の `ImageProcessing/`、`Models/`、`TwoD/` に起動用の登録 extension と準備定義を置く。共通 pass project は `Microsoft.Extensions.*` に依存しない。 |
| `src/graphics/Lumyte.Graphics.Passes.Tests/Unit/` | 隣接する新設 xUnit project。共通 request、snapshot、依存宣言と不変データの振舞いを機能別に確認する。 |
| `src/graphics/Lumyte.Graphics.Native.Passes.Tests/`、`src/graphics/Lumyte.Graphics.Portable.Passes.Tests/` | 隣接する新設 xUnit project。`Unit/<Feature>/` に準備・cache の試験、`Integration/<Feature>/` に GPU の適合試験を分ける。 |
| `src/graphics/Lumyte.Graphics.Passes.Hosting.Tests/Unit/` | 隣接する新設 xUnit project。登録、準備と所有の接続を fake で確認する。 |
| `benchmarks/Lumyte.Benchmarks/Graphics/Passes/` | 既存 benchmark project に追加する共通入力・保持と本体準備の計測。 |

`IGpuRenderPassContract`、graph の input／declaration context は `src/graphics/Lumyte.Graphics.RenderGraph/`、系統別 registry／factory／context は各系統 RenderGraph project の担当であり、feature project に複製しない。CPU scene は `Lumyte.Graphics.TwoD`、glyph／text data は `Lumyte.Graphics.Text` として新設する。共有 path／paint は新設の `src/graphics/Lumyte.Graphics.TwoD.Primitives/` に置き、Text と scene の循環参照を避ける。旧 Library／TwoD／Text は削除済みであり、互換 facade を残さない。

## 使用例

`ImageEffectContract`、`ImageEffectInputContract`、`ImageEffectRequest` と result の `Color` は、ある画像処理 library の共通契約とする。request の Data は準備済み画像データと描画設定の GpuGraphValue、OutputDescription は固定の出力形状である。Declare は Data を ReadInput で登録し、新しい Color を宣言する。

```csharp
using Lumyte.Graphics.RenderGraph;

var graph = new GpuRenderGraph();
var input = graph.CreateInput("effect.data", ImageEffectInputContract.Instance, initialData);
var request = new ImageEffectRequest(Data: input, OutputDescription: colorDescription);
var result = graph.AddPass("effect", ImageEffectContract.Instance, request);
graph.MarkOutput(result.Color);
var plan = graph.Compile();

var bindings = plan.CreateBindings();
bindings.Set(input, changedData);
using var execution = await runtime.SubmitAsync(plan, bindings.Build(), cancellationToken);
await execution.WaitForCompletionAsync(cancellationToken);
```

initialData と changedData は所有済みの入力とする。画像が同じなら変更した描画設定だけを新しくし、画像の参照と保持情報を共有する。次の frame も同じ plan と builder を使える。shader の管理は二つの本体が担当する。

以下は Hosting integration が非同期準備後に行う下位 composition の例である。Lumyte.Resources が準備した各系統の shader package を本体の factory に渡す。通常の描画利用者が書く登録コードではなく、登録時に I/O や GPU 生成は行わない。

```csharp
nativePasses.Register(ImageEffectContract.Instance,
    services => new NativeImageEffectPass(services, nativeShaderPackage));
portablePasses.Register(ImageEffectContract.Instance,
    services => new PortableImageEffectPass(services, portableShaderPackage));
```

新しい機能の作者は同じ組を提供する。共通契約だけから二つの GPU 実装が自動生成されるとは扱わない。

以下は `src/graphics/Shaders/Shared/Color/Alpha.slang` に置く共有 source の例である。関数の引数は shader 内の値であり、CPU から渡す共通 GPU 構造体ではない。

```slang
module Color.Alpha;

public float4 Premultiply(float4 straightColor)
{
    return float4(straightColor.rgb * straightColor.a, straightColor.a);
}
```

Native と Slang を使う Portable の個別 entry module は `import Color.Alpha;` で同じ計算を利用する。色を material の descriptor から読むか、明示 binding の texture から読むかはそれぞれの source に置く。この module だけを WGSL shader としてロードせず、各 entry と組にして別々の program を build する。

## 内部処理と shader の所有

各本体は準備済みの自系統 shader package と生成 GPU 構造体を利用し、shader program を GPU 上に生成する。shader のファイル取得・package 展開は Lumyte.Resources、GPU program の初期化と保持は Graphics の責務とする。Native の Bindless によるまとめ描き、Portable の binding 単位の分割などを独立して最適化する。共通 Draw／Dispatch IR や、共通 shader 入力を target 間で翻訳する adapter は設けない。

### Slang source の部分共有

source を共有すると同じ数式の修正と適合確認をまとめられる。一方で resource の取得方法と実行モデルまで同じにすると、Native の bindless／mesh や Portable の binding に適した処理単位を選びにくくなる。このため共有の境界は計算関数・値型とし、GPU の配置や実行を指定する型は各系統に残す。

| 部分 | 共有する内容 | 各本体に残す内容 |
| --- | --- | --- |
| Model／lighting | BRDF、色、法線・接線、skinning、morph と bounds の計算 | geometry／material の読出し、GPU buffer 配置、culling の配置、vertex／mesh entry、amplification payload |
| 画像処理 | filter の重み、色空間、合成・tone mapping の計算 | sampling／storage 宣言、root、workgroup、一時 texture と pass の分割 |
| 2D | coverage、distance、gradient と alpha の計算 | atlas の参照、binding、batch、tessellation と stencil の命令構成 |

共有 module は純粋な値の計算を優先し、必要な interface／generic は各 build で具体化する。resource を渡す共通 interface を新たな bindless／binding API に育てず、GPU pointer、descriptor index、group、root、meshlet 配置は隠して統一しない。Slang の module と specialization の仕組みを使う方針であり、独自の shader 言語や transpiler を作るものではない。[Slang の module](https://shader-slang.org/slang/user-guide/modules)、[Slang の specialization](https://shader-slang.org/docs/compilation-api/)。

Native の build は専用 entry と共有 module から DXIL／SPIR-V、Portable の Slang build は別の entry と同じ module から WGSL を生成する。package、生成 C#、root／parameter layout、binding schema、loader と GPU cache は別々にする。共有 module を変更した場合は両 build の依存へ反映し、同じ source revision を使う別々の artifact を配布する。共有 source の型を consumer の GPU 入力 ABI として公開しない。

Portable で使用する Slang の範囲は [ADR 0023 の採用条件](0023-shader-design-and-api.md#slang-の共有範囲と採用条件) に従う。直接 root は固定した compiler と Portable 専用 accessor によって読み出し、値を共有計算へ渡す。この経路の最小 GPU 実験は成功しており、通常の push constant 宣言をそのまま WGSL へ変換する前提にはしない。WGSL target の未対応機能では作者が直接 WGSL の variant を build 入力として選ぶ。compiler 失敗を黙って別の入力方式へ切り替えず、root の buffer 退避も行わない。直接 WGSL の variant では計算の意味と試験を共有しても、その program の Slang source 自体まで共有済みとは表示しない。

mesh shader は Native 本体の任意の最適化とする。同じ Model 契約に対し、対応 device では Mesh／任意 Amplification、非対応の Native では vertex、Portable では vertex／compute を組み合わせられる。共有する変形や BRDF の関数をそれぞれから呼べるが、mesh shader の命令や payload を WebGPU に変換することは要求しない。通常の consumer が pass 追加時に mesh shader、meshlet や shader package を選ぶ必要はない。

共有 module の初期適用は色・alpha と filter の小さな計算から始め、BRDF、変形と 2D coverage へ広げる。数値入力と期待する意味を共通にした fixture を各 target で実行し、色・座標・matrix 規約、直接 root、許容誤差内の出力を確認する。shader 全文、GPU 構造体や命令列の一致は試験しない。stage、binding、layout の合法性は compiler と各 GPU runtime の診断を使う。

内部 pass 数、workgroup、geometry、atlas、一時資源の形を一致させる必要はない。private transient と全使用・依存は専用 graph に宣言し、provider が機能間の依存と統合する。同じ機能内で別の標準効果を使う場合も、各系統の内部実装を再利用できる。

通常 data と Parameter Data は各 pass 本体が準備済みデータから明示的に構成する。転送順序、root の直接入力と下位 Submit での PSO 解決は [ADR 0030 の所有・同期規範](0030-render-graph-api.md#所有同期失敗) に従う。

shader program cache、GPU data と atlas は実装と runtime が所有する。CPU 入力の共有部分木、upload の保持情報、geometry／material、batch と派生結果を、それぞれが依存する世代に応じて再利用する。入力に変化がないのに全描画項目を再抽出・コピー・hash・列挙・転送することを反復描画の要件にしない。

snapshot と使用保持は [ADR 0030 の不変入力](0030-render-graph-api.md#不変入力と使用保持)、受理前後の共有・失敗・回収は [GPU 内容世代と提出結果](0030-render-graph-api.md#gpu-内容世代と提出結果) を正本とする。作者は各 GPU cache に固有の key と依存だけを追加し、別の提出成功規則を持たせない。

camera の変更が culling／透明 sort に影響するなど、必要な処理まで省略する保証ではない。内部 command の記録・再利用、CPU／GPU での評価と batch の分け方は本体ごとに選ぶ。共通 plan の再利用を、Native と WebGPU で同じ command 記録方式を使う要件にしない。

## 同じ binary と適合条件

両本体は、入力の意味、出力 description、更新範囲、外部副作用、色・座標・深度・合成の規約と結果の許容誤差を一致させる。利用する機能の両本体と shader 資産を配布し、契約版と CPU 実行環境が互換なら同じ consumer binary で実行する。AOT 向けの factory 事前登録を許容し、実行時の C# コンパイルを要求しない。

実装の欠如や契約版の不一致は準備前に報告する。GPU program 生成・転送の失敗は判明した時点で報告し、受理後の非同期診断は completion と内容依存へ伝える。片方だけ実装した機能を両系統対応と表示しない。GPU API の合法性は native API／WebGPU runtime／compiler に委ね、機能の意味、snapshot と依存など自分の契約だけを確認する。

## 採用範囲と未実装事項

共通機能契約、固定構造と型付きフレーム入力、差分準備、二系統の本体とその shader 所有を採用する。Slang の計算用 source module を部分共有し、entry、GPU ABI と実行モデルは共有しない。モデル描画や 2D の個別 API と対象機能は、共通実装規約から独立させる。

段階 0 として共通 feature library、Clear 入力 contract、同じ plan と不変 bindings、GetInput、二系統の registry／本体、標準画像機能の Hosting 登録を実装した。Clear と Copy は下位命令で行い、Output の shader と program は各本体が所有する。利用側は shader、pipeline、root、binding を管理しない。

内部 template と差分準備、GPU 内容世代 cache、Model／2D と残る画像機能の移植は未実装である。既存の旧 API の同様の機能を、新しい二系統の完成として数えない。

共有 Slang module と両 target の build 依存、個別 entry への移植、直接 root を含む Portable variant の conformance、Native の mesh 経路と通常 vertex 経路の選択は未実装である。Slang の WGSL 出力対応だけを全 pass の source 共通化完了とは扱わない。

適合試験では一度だけビルドした同じ consumer assembly を双方の provider で実行し、出力、依存、snapshot、所有と失敗を確認する。未変更入力の再利用、部分更新、古い bindings の再提出、重なる実行と構造変更も個別に確認する。内部 command 列や生成 shader 全文の一致は条件にしない。
