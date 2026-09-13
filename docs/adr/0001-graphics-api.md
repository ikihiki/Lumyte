# ADR 0001: Graphics の二系統構成と共通契約

## 状態

採用（目標設計）。現行実装の完了を示さない。旧 API、shader ABI、共通 backend による互換経路は設けない。

## 依存 ADR

なし。共通化の境界、package の責務、検証と所有の原則を定義する。後続 ADR はこの境界の中で各系統の API を定める。

## 決定

**機能 pass を追加する RenderGraph API を一つの assembly に統一し、利用 library／application の再コンパイルなしに Native／Portable を選択する。** Model 描画、ブラー、2D 描画などの要求型と入出力、graph、plan、execution、completion、frame は共通とする。AddPass の内部で行う GPU 処理は二系統で実装する。

Native と Portable の低レベル API、pass 本体、shader の entry point／GPU ABI、resource 管理、内部 graph と encode は独立した実装とする。shader の計算部分は Slang module として source を共有できる。二つの RenderGraph provider が機能契約に対応する専用実装を選択する。共通の Draw／Dispatch 命令列を翻訳する経路や、Portable を Native の上へ接続する低レベル adapter は設けない。

Native は DirectX 12／Vulkan を対象とし、[NoGraphicsAPI](https://github.com/sebbbi/NoGraphicsAPI) を基礎に、Bindless、直接 root data、明示的な allocation、resource 配置、同期を扱う。線形 data と texture は共通の純粋 allocation に置き、線形 region と texture の表現を分ける。WebGPU の制約はこの契約へ持ち込まない。

mesh shader と任意の amplification shader は Native の追加機能として扱う。対応 device では mesh による raster 経路を使え、非対応 device では従来の vertex 経路を利用できる。共通の機能 pass は、これらの stage や meshlet を利用者の必須入力にしない。Portable に mesh command の実装・エミュレーションを要求せず、各本体が同じ機能の結果を提供する。

Portable は WebGPU を最初の対象とする独立 API である。Buffer／Texture object、明示的な binding、WebGPU の shader と実行モデルを使う。Bindless は必要機能でも emulation の対象でもない。device 全体の descriptor index、型別の全 slot 初期化、全候補の延命を前提にしない。将来 Portable の実装先を増やしても、Native backend を共通 interface へ押し込むことを要件にしない。

Lumyte Graphics Resources は二実装を維持する。Native は heap／region／descriptor slot、Portable は Buffer／Texture／view／binding object を管理する。RenderGraph provider は共通の resource facade を実装し、import/export や準備済み package の GPU 転送の signature からも下位の型を隠す。直接利用する caller だけが各管理ライブラリの専用型を扱う。

## API と package の境界

以下は目標 package／namespace 名である。共通 graph 型は RenderGraph assembly、機能ごとの共通要求型は機能 contract assembly に一度だけ定義する。その実装 provider と低レベル API は別の assembly に置く。共通 assembly から下位実装への参照を要求しない。

`InternalsVisibleTo` はテスト assembly にだけ使用する。backend、provider と本体 library の連携には明示した public／protected 契約を使い、新しい実装 assembly を追加するために共通 assembly の内部公開先を編集しない。backend が生成する resource は公開基底型とその非公開派生型に分け、native state を実装内へ閉じ込める。特定の上位 library だけが使う内部操作と型は、その所有 assembly に配置する。

| package／namespace | 担当 API |
| --- | --- |
| `Lumyte.Graphics` | GPU object を所有しない基礎値と座標規約。低レベル backend interface は定義しない。 |
| `Lumyte.Graphics.Native` | `INativeGpuBackend` と `NativeGpu*`。DX12／Vulkan 用の低レベル API。 |
| `Lumyte.Graphics.Portable` | `IPortableGpuBackend` と Portable 所属の `Gpu*`。Buffer／Texture と binding による低レベル API。 |
| `Lumyte.Graphics.Native.Shaders` | Native 専用 compiler、package、生成入力型、loader、program。 |
| `Lumyte.Graphics.Portable.Shaders` | Portable 専用 compiler、package、生成入力型、loader、program。 |
| `Lumyte.Graphics.Native.Resources`／`Lumyte.Graphics.Portable.Resources` | それぞれの `GpuResourceManager`、scope、resource ref、package、batch、回収。 |
| `Lumyte.Graphics.RenderGraph` | 単一の `GpuRenderGraph`、機能 contract、CPU request の登録、共通の description／ref、plan、execution、frame、`IGpuRenderRuntime` と resource facade。 |
| `Lumyte.Graphics.Passes` | Model／Blur／2D 等の共通 request、result、機能 contract と AddPass extension。shader は公開しない。 |
| `Lumyte.Graphics.TwoD.Primitives`（namespace は `Lumyte.Graphics.TwoD`） | 2D scene と glyph が共有する不変の path／paint／画像参照。両者の循環参照を避ける下位の CPU 値 assembly。 |
| `Lumyte.Graphics.TwoD`／`Lumyte.Graphics.Text` | 不変の 2D scene と準備済み glyph／text の描画データ。共有 path／paint は Primitives を参照する。font のロード・文字組み API は定義せず、共通の受け渡し型を一度だけ定義する。 |
| `Lumyte.Graphics.Native.Passes`／`Lumyte.Graphics.Portable.Passes` | 機能ごとの独立 pass 本体。準備済み専用 shader からの GPU program、生成 GPU 構造体、uploader、resource 準備と記録を所有。 |
| `Lumyte.Graphics.Native.RenderGraph`／`Lumyte.Graphics.Portable.RenderGraph` | 共通 runtime を実装する provider。機能実装の registry、専用 pass 本体の SPI、内部 graph の計画、GPU 実体の所有。 |
| `Lumyte.Graphics.Hosting` と各実装の Hosting integration | Generic Host の起動設定、DI、Options、provider／pass factory の登録と非同期初期化・終了。共通 graph や低レベル API に Hosting／DI の依存を持ち込まない。 |

### 共通にする公開契約

| API family | 単一の共通型で表すもの | provider の内側で分けるもの |
| --- | --- | --- |
| RenderGraph | 機能の CPU request と result、外部 resource の Read／Write、AddPass、Compile、SubmitAsync、execution と completion。 | 実際の Draw／Dispatch／copy、内部 pass 数、GPU usage、view、配置と同期。 |
| Graph Resources | `IGpuGraphResources`、共通 scope／ref／pin、準備済み package の upload と graph の import/export。 | それぞれの `GpuResourceManager` と下位 ref、生成・upload・view、Native の配置と Portable の直接生成、cache と retirement。 |
| 機能 library | Model／Blur／2D 等の追加 API、機能 ID／版、共通 CPU 入力、外部出力の意味。 | 二本の pass 本体、GPU algorithm／entry point、artifact／loader、GPU 構造体と pipeline、descriptor／binding。計算用 Slang source の部分共有は可能。 |
| Runtime と frame | `IGpuRenderRuntime`、設定による provider 選択、準備済み転送データ、共通の presentation target。 | device 作成、queue／semaphore、surface／canvas との接続、外部 raw resource の interop。 |

共通 `AddPass(name, contract, request)` は不変 CPU request と外部入出力を登録し、GPU 記録 callback は受け取らない。`AddModelPass`、`AddBlurPass`、`Add2DPass` はこの入口を包む。各系統の pass 作者だけが専用の記録 callback を書き、GPU address、descriptor index、binding group を扱う。

shader の authoring には両系統で Slang を使えるようにし、純粋な数値計算や材質・色・filter の関数を共有する。Native は DXIL／SPIR-V、Portable は WGSL を別々に生成し、Portable の直接 WGSL authoring も残す。resource 宣言、直接 root、stage entry、package、生成 GPU 構造体と program 初期化は各系統が管理する。Slang の型が同名でも target 間の byte ABI の一致を意味しない。

shader artifact の I/O と package 展開は Lumyte.Resources 側で済ませ、host が準備済みの各系統の package を pass factory に渡す。利用側に shader ID、生成 GPU 入力型やロード操作は公開しない。新しい機能 pass の作者は、共通 contract と Native／Portable の二つの実装を用意し、その計算 source を必要な範囲で共有する。共通の CPU 入力は shader の byte ABI ではなく、shader library 自体は RenderGraph に依存しない。Slang を使う場合も root の buffer fallback を許可せず、実際の WGSL 出力と runtime で直接入力の契約を満たすことを確認する。

同じ consumer binary で動く provider、必要な二系統の pass 実装と shader artifact を配布する。共通契約の version と CPU 実行環境が互換であることを前提とし、GPU object の別 device への移送や異なる OS／CPU 向け executable の変換は含めない。AOT でも事前登録した provider と pass 実装を選べるようにし、実行時 C# compilation を必須にしない。

共通 graph は固定の機能構成と外部資源の依存を一度 Compile し、変わる CPU 値と今回の表示先は型付き入力 slot／不変 bindings で差し替える。描画 library の保持集合は変更項目だけを編集し、snapshot の未変更部分と所有情報を共有する。各本体は部品の転送、draw の分類と内部計画を必要な範囲で更新し、毎フレームの全体再構築を要求しない。

これは描画データの保持を選ぶ上位機能であり、Native／Portable の低レベル API に ECS 走査、差分検出や資源の隠れた延命を加える方針ではない。カメラ依存の culling、透明 sort、GPU command 記録と実描画は各本体の必要な処理として残す。

### 起動設定と DI

通常の application は Generic Host の composition root で provider、機能 pass、CPU 依存と設定を一度登録する。Hosting integration が登録定義と Options の不変 snapshot を取得し、非同期の起動処理で準備済み shader package と選択 runtime を用意する。利用 library は DI された利用準備完了の入口から runtime と必要な描画 context を借用し、registry の作成・登録や runtime の初期化を繰り返さない。

`GpuRenderProviderRegistry` と各系統の pass registry は integration が使う bootstrap API として残す。登録中や DI の同期 factory で device 作成、shader の I/O、GPU upload を行わず、実行中の登録変更と設定変更の自動反映を行わない。shader の取得・展開は非同期の起動準備から `Lumyte.Resources` に委譲し、不変の専用 package だけを pass へ渡す。

`Microsoft.Extensions.Hosting`、DI と Options への依存は integration 層に限定する。共通要求型、RenderGraph、低レベル API、pass の BuildContext に `IServiceProvider` や service locator を追加しない。integration は runtime 単位の DI scope から CPU 依存を型付き factory へ渡す。GPU を持つ pass は runtime ごとに生成し、runtime が pass、manager と backend を所有する。DI は Hosting owner と CPU 依存を所有し、runtime の終了後に scope を解放する。描画処理のたびに service を解決しない。

## ロードと GPU 転送の境界

`Lumyte.Resources` がファイル取得、URI／依存の解決、形式の解釈、画像や font の復号、ロードに伴う CPU データの準備を扱う。実行時のノード階層、animation、物理、手続き生成は ECS／scene 等の上位も担当でき、動的データの供給に Resources の経由を要求しない。Graphics の ADR は loader、Entity／Component の格納や評価サービスを定義しない。

Graphics は `GpuImageUploadData`、独立したモデル geometry／material／変形データ、`TextDrawData`、`GlyphUploadData` など、準備済みデータを受け取る型を定義する。モデル描画は形式に依存しない平坦な描画項目を受け取り、上位が任意の Component から geometry、material、変換と変形を組み合わせる。glTF の描画能力は必須としつつ、glTF の node／scene 構造を共通 API の所有単位にはしない。各転送データは不変の内容世代と有効な memory を持ち、ID から再読込みする仕組みを含めない。

Native／Portable の本体はそれを専用 GPU layout へ配置し、GPU program、buffer／texture、descriptor／binding と転送を管理する。Lumyte.Resources のロード責務と、Graphics Resources の GPU memory／寿命の管理を区別する。GPU shader loader という名前を使う箇所も、準備済み package から program を生成する処理であり、ファイルを読む入口ではない。

## 基礎値

これらは表記と意味を共有できる値であり、全低レベル API が全値を受け取るという保証ではない。

| API | 説明 |
| --- | --- |
| `GpuShaderCodeFormat` | `Dxil`、`SpirV`、`Wgsl`。各 loader／backend が自分の形式だけを受け取る。 |
| `GpuShaderStage` | `Vertex`、`Pixel`、`Compute`、Native 追加機能の `Amplification`、`Mesh`。Amplification は Vulkan の task stage に対応する。Portable が受け取るのは Vertex／Pixel／Compute。 |
| `GpuFormat` | color、sampled、storage、depth/stencil の format の意味。利用条件は実際の API の診断に従う。 |
| `GpuCompareOp` | `Never`、`Less`、`Equal`、`LessEqual`、`Greater`、`NotEqual`、`GreaterEqual`、`Always`。 |
| `GpuStage` | `None`、`DrawIndirect`、`IndexInput`、`VertexShader`、`AmplificationShader`、`MeshShader`、`PixelShader`、`ComputeShader`、`ColorOutput`、`DepthStencil`、`Copy`、`AllGraphics`、`All`、`Host` の集合。AllGraphics は有効な amplification／mesh stage も含み、All はさらに compute／copy 等の GPU stage を含む。CPU access は Host で明示する。 |
| `GpuAccess` | `None`、`ShaderRead/Write`、`DescriptorRead`、`ColorRead/Write`、`DepthStencilRead/Write`、`CopyRead/Write`、`IndexRead`、`IndirectRead`、`HostRead/Write` の集合。 |
| `GpuTextureLayout` | `None`、`Undefined`、`General`、`ShaderRead`、`ColorAttachment`、`DepthStencilRead/Write`、`CopySource/Destination`、`Present`、`Common`。明示 transition を持つ経路で用いる。Common は graphics／copy queue 間の引渡しに使う queue 非依存の layout。 |
| `GpuOrigin3D`／`GpuExtent3D` | texture の texel 単位の原点 `X/Y/Z` と範囲 `Width/Height/Depth` を、それぞれ uint の不変値で表す。memory allocation や view を所有しない。 |
| `GpuResourceState(Stages, Access, Layout)` | caller が宣言する同期上の値。native の現在 state を照会・追跡する object ではない。Portable の共通入力に強制しない。 |
| `GpuDeviceLostException` | device の利用を続けられない状態。正常 completion と区別する。 |

## 検証を担当する層

native API が検証できる項目を Lumyte で独自に再検証しない。ここで native API には DirectX 12／Vulkan と WebGPU runtime を含む。この原則は両方の管理ライブラリと RenderGraph にも適用する。

| 対象 | 担当 |
| --- | --- |
| usage、format、dimension、view、sampling、copy、index、indirect の合法性 | 各 API の作成・使用結果と debug／validation。 |
| pipeline と attachment／binding の適合、shader の合法性、GPU 同期 | API、compiler、validation layer。 |
| device feature／limit | device が報告する値と生成結果。必要機能の選択は行うが、仕様全体の validator は複製しない。 |
| CPU memory へのアクセス | Lumyte が自分で行う byte コピーの範囲、長さ、overflow を確認する。 |
| Lumyte の機能 contract と shader package | registry は機能 ID／版／要求型、各 compiler／loader は専用 package と生成入力の対応を確認する。 |
| 自分で所有する記録、allocation、descriptor／binding、回収 | 各 owner の局所状態。外部 object の全使用履歴は追跡しない。 |
| graph の依存、初期化、export、alias 計画 | 共通の論理計画と各 provider の物理計画。明示した宣言を扱い、shader／GPU memory の中身を探索しない。 |

詳細な format capability、独立した memory compatibility の統合・分類・適合照会、現在 resource state を返す API は設けない。Native の明示配置で必要な size、alignment、opaque requirement の取得は残す。caller が確保に渡した requirement から実際の allocation 引数を構成する処理と、追加 validator を区別する。

## 所有、直接入力、メモリ

低レベル caller は、参照する未提出記録と提出済み GPU 利用が終わるまで resource、memory、descriptor／binding、pipeline を保持する。非 owning な値のコピーで所有権は増えない。低レベル command は pointer、index、binding の参照先を探索して自動延命しない。

管理 API は明示的に受け取った resource ref、scope、package、使用登録から所有と依存を保持する。共通 Graph は選択 runtime の ref を受け取り、SubmitAsync の準備時に provider が下位の使用保持を取得する。Native が間接的に参照する資源は package metadata または使用宣言で覆い、Portable は明示 binding に含めた資源を保持する。未知の外部使用は推測しない。

root data は各系統の shader ABI に従って直接渡す。共通の固定 byte 数は設けず、実際の入力機能と上限に従う。各 pass 実装が専用の数値と明示資源参照から root を構成する。buffer への暗黙の fallback は行わない。

command は Parameter Data を生成、upload、所有しない。各 pass 実装が受け取った転送データから自分の GPU data を構成し、明示 upload する。shader が root と resource 入力から parameter を参照・算出する。共通型で包んだ Buffer に target 固有の pointer／index／構造体 bytes をそのまま渡しても、データの互換性を保証したことにはならない。

Native のメモリ取得は線形 data と texture に共通の allocation に統一する。Portable は Buffer／Texture の生成にメモリ確保を含め、事前の heap 作成、allocation object、明示配置を公開しない。Portable の package は生成した resource 群の所有をまとめるもので、単一の物理 allocation を要求しない。

## 座標と数値の規約

clip 座標は X/Y が `-w..w`、Z が `0..w`、X の正方向を右、Y を上とする。framebuffer と texture の原点は左上、X は右、Y は下、pixel center は整数座標に `(0.5, 0.5)` を加えた位置とする。viewport の depth は `0..1` 内で指定する。

front face は clip-to-viewport 変換後の winding に対する値とする。行列の意味は共通にできるが、shader に転送する行列や構造体の byte layout は各 shader ABI が定義する。必要な座標変換は各系統で一度だけ行う。

## コード配置

以下は repository root からの相対パスによる目標配置である。既存 project は新しい責務へ改編し、新設予定の project とサブディレクトリは実装時に作る。ADR ごとに assembly を増やすのではなく、API と所有の境界で分ける。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics/Primitives/`、`Coordinates/` | 既存 project を改編。GpuFormat 等の非所有の基礎値、座標規約。本 ADR が直接担当する共通型を置く |
| `src/graphics/Shaders/Shared/` | 新設予定の build 用 Slang module。計算関数を共有し、共通 runtime assembly、GPU 入力 ABI、resource 宣言の所有元にはしない |
| `src/graphics/Lumyte.Graphics.Native/`、`src/graphics/Lumyte.Graphics.Portable/` | 独立した低レベル契約。Native は作成済み、Portable は新設予定。既存 Lumyte.Graphics の backend／command／resource API を共通基礎値から分離する |
| `src/graphics/Lumyte.Graphics.DirectX12/`、`src/graphics/Lumyte.Graphics.Vulkan/`、`src/graphics/Lumyte.Graphics.WebGPU/` | 既存 project を改編。前二者は Native、後者は Portable の実装を置く |
| `src/graphics/Lumyte.Graphics.Native.Shaders/`、`src/graphics/Lumyte.Graphics.Portable.Shaders/` | 新設予定。系統別の準備済み package と GPU program。offline tool の配置は各 shader ADR が定める |
| `src/graphics/Lumyte.Graphics.Native.Resources/`、`src/graphics/Lumyte.Graphics.Portable.Resources/` | 実装済みの系統別 GPU 管理機構。共通基礎 project に管理 API を置かない |
| `src/graphics/Lumyte.Graphics.RenderGraph/` | 既存 project を改編。単一の共通 graph／runtime 契約と GPU 転送データ |
| `src/graphics/Lumyte.Graphics.Native.RenderGraph/`、`src/graphics/Lumyte.Graphics.Portable.RenderGraph/` | 新設予定。共通契約を実装する provider と各系統の内部 graph |
| `src/graphics/Lumyte.Graphics.Passes/`、`src/graphics/Lumyte.Graphics.Native.Passes/`、`src/graphics/Lumyte.Graphics.Portable.Passes/` | 新設予定。順に共通機能契約、Native 本体、Portable 本体。機能ごとの詳細は担当 ADR に置く |
| `src/graphics/Lumyte.Graphics.TwoD.Primitives/` | 新設予定。2D と Text が共有する path／paint 等を定義する。型の namespace は Lumyte.Graphics.TwoD を保つ |
| `src/graphics/Lumyte.Graphics.TwoD/`、`src/graphics/Lumyte.Graphics.Text/` | 既存 project を不変の CPU 描画データへ改編。GPU 本体は各系統の Passes へ分離する |
| `src/graphics/Lumyte.Graphics.Hosting/`、`src/graphics/Lumyte.Graphics.Native.Hosting/`、`src/graphics/Lumyte.Graphics.Portable.Hosting/`、`src/graphics/Lumyte.Graphics.Passes.Hosting/` | 新設予定。Host の構成と DI 接続。共通 API からこれらを参照しない |
| `src/graphics/Lumyte.Graphics.Tests/` | 既存の隣接 xUnit project を改編。本 ADR の基礎値・座標規約を検証し、各 library の振舞いはその隣の `.Tests` に置く |

各 project の `.csproj` はそのディレクトリ直下に同名で置く。TwoD scene は Text を参照し、Text と TwoD は共有の TwoD.Primitives を参照する。Primitives から Text／scene を参照せず、API 名を変えずに assembly の循環を避ける。旧 `Lumyte.Graphics.Library` と `Lumyte.Graphics.Shader` は実装の移植元として扱い、新しい機能本体や二系統の API を集める場所にはしない。ファイル取得・decode は `src/resources/`、window／event loop は `src/platform/`、利用側の起動設定は application 側に保つ。

## 使用例

`runtime` は Generic Host の integration が起動・所有する選択済み runtime を借用したものとし、`renderer` は共通 assembly だけを参照するコンパイル済み library とする。provider の選択と初期化は描画 library に含めない。同じ application binary の起動設定で provider を選べる。

```csharp
using Lumyte.Graphics.RenderGraph;

await renderer.RenderAsync(runtime, cancellationToken);
```

描画 library は機能の追加 API と共通 CPU 入力だけを扱う。provider、二系統の pass 本体とその shader は選択前に配布しておく。

## 採用範囲と未実装事項

NoGraphicsAPI の考え方を適用する対象は Native である。Portable の明示 binding や device-owned resource は独立した設計であり、NoGraphicsAPI の Bindless を実装したものとして説明しない。Native でも pointer、PSO 分解、texture layout など原案どおり提供できない点は各 ADR の末尾に部分採用として記す。

単一の RenderGraph contract、Clear／Texture Copy／基本 Output の機能 request と二系統の本体、共通 resource facade、非同期提出、Native／Portable provider と Generic Host／DI integration を段階 0 として実装した。共通 consumer は独立 assembly に一度 build し、provider の設定だけを切り替える。旧描画系の移植元は `Lumyte.Graphics.RenderGraph.Legacy` へ明示分離し、新 API から旧 API へ forwarding しない。Model／2D、残る画像処理、内部 template の差分再利用と GPU 内容世代の共有は未実装である。

Native の mesh／amplification と、両系統の Slang source の部分共有を追加採用する。Native の raw pipeline、直接／間接 mesh command、直接 root と試験用 shader は実装し、両 backend の実 GPU で確認した。製品用 shader artifact の生成・配布、pass の経路選択と共有 module は未実装である。Portable の直接 root は Slang 2026.17 の専用 accessor による WGSL 生成と実 GPU の最小実験で成立を確認した。通常の push constant 宣言の自動変換ではなく、固定した toolchain の相互運用機能を使う。toolchain への統合と各 pass での適合は未実装であり、全 pass の source を共有済みとは扱わない。
