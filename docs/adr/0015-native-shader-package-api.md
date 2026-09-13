# ADR 0015: Native Shader の設計、package 入力型と GPU 初期化 API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

- [ADR 0002: Native Graphics API](0002-native-graphics-api.md) の device、code format と capability。
- [ADR 0008: Native Descriptor API](0008-native-descriptor-api.md) の descriptor heap ABI。
- [ADR 0009: Native Shader API](0009-native-shader-api.md) の raw code/program と直接 root。
- [ADR 0010: Native Pipeline State API](0010-native-pipeline-state-api.md) の pipeline 作成と code の保持。

## 決定

Native 専用の Slang entry／resource module、offline compiler、package、生成 host 構造体と loader を用意する。公開 namespace は `Lumyte.Graphics.Native.Shaders` とする。DirectX 12 向け DXIL と Vulkan 向け SPIR-V を事前に生成し、GPU 初期化時に device と一致する artifact を選ぶ。値を受け取って値を返す計算 module は Portable と source を共有できるが、Native の GPU ABI と artifact はこの library が独立して生成する。

Portable の entry、package、GPU 入力構造体と loader は別の契約とする。共有 source に root、descriptor、binding、GPU pointer、host から転送する構造体や stage 固有の実行モデルを持ち込まない。この shader library を直接使う caller は、選択した artifact と対応する入力構造体を使う。共通 RenderGraph の利用者は機能 pass の CPU 入力と論理 I/O を渡し、shader の program ID、生成 GPU 構造体や loader を扱わない。Native の機能 pass 作者が package と対応する入力構造体を所有し、Portable の実装とは独立して shader を選択・準備する。

[NoGraphicsAPI の Slang 契約](https://github.com/sebbbi/NoGraphicsAPI/blob/main/docs/slang.md) を基礎に、Native の root は直接渡す小さな値とし、線形 data は target が対応する GPU pointer または buffer descriptor、texture と sampler は descriptor index で参照する。descriptor index と GPU address は異なる型・意味のまま扱う。

## API

この文書は `Lumyte.Graphics.Native.Shaders` の全公開 API と生成物の契約を担当する。compiler は開発時ツール、loader と program は runtime のライブラリとする。ファイル取得、container の読取り・展開と cache のロードは `Lumyte.Resources` の分野であり、Graphics は GPU 初期化へ渡すメモリ上の型を定義する。`Read(bytes)`／`Read(stream)` や URI resolver は公開しない。

| API | 契約 |
| --- | --- |
| `NativeShaderTarget` | `DirectX12/Vulkan`。artifact の対象であり、Portable を含めない。 |
| `NativeShaderBuildTarget` | target、shader compiler の target 設定、要求 capability と descriptor heap ABI の組。Vulkan の device 由来の統一 stride 方針や、数値を埋め込む場合の固定 slot stride など code に影響する値を含む。 |
| `NativeShaderEntryPoint` | source 内の entry 名と `Vertex/Pixel/Compute/Mesh/Amplification` stage。Amplification は Vulkan の task に対応する。 |
| `NativeShaderBuildRequest(SourcePath, EntryPoints, Targets, HostNamespace, RootParameterName, ParameterTypes, IncludeDirectories, Defines)` | Slang source と build 設定。root の global 名と、storage buffer element として反映する parameter 型名を指定する。offset や binding 番号は入力しない。program は Vertex＋任意 Pixel、Mesh＋任意 Amplification＋任意 Pixel、または Compute。root 名の null は root なしを表し、実際の宣言が残る場合は拒否する。 |
| `NativeShaderCompiler(compilerPath, downstreamCompilerDirectory).BuildAsync(request, cancellationToken)` | Slang を offline に実行し、target artifact、入力配置情報と C# source を生成する。DXC の探索先は子 process だけに適用する。runtime device と pipeline を作らない。 |
| `NativeShaderBuildResult.Package`／`PackageBytes`／`HostSourceFiles`／`ResourceInputFiles`／`CompilerVersion` | 同じ build の展開済み package、JSON container、host C#、管理入力 XML、compiler の版。辞書のキーは平坦な出力ファイル名。host source は `ShaderPackage.Create()` と target 別の型を含む。 |
| `NativeShaderPackage(Version, Artifacts)` | 一つの論理 program の展開済み artifact 群を持つ不変の GPU 初期化入力。`CurrentVersion = 1`。`Artifacts` は不変の `NativeShaderArtifact` 列とし、CPU 側で所有する。device、ファイル名や stream を持たない。 |
| `NativeShaderArtifact(Target, CodeFormat, Stages, RequiredCapabilities, DescriptorHeapAbi, RootLayout, ParameterLayouts, AbiHash)` | target 用の stage 列、device 選択条件、入力配置、生成型の識別情報。`Stages` は不変の `NativeShaderStageArtifact` 列とし、layout も不変とする。 |
| `NativeShaderStageArtifact(Stage, EntryPoint, Code)` | 一つの stage の entry 名と、その stage に対応する所有済みの不変 raw code bytes。artifact は build request と同じ排他的 stage 構成を持つ。Mesh の artifact は `MeshShaders`、Amplification を含む artifact は加えて `AmplificationShaders` を要求する。 |
| `NativeShaderCapabilities` | artifact 選択に使う `None/RawShaderPointers/BufferDescriptors/MeshShaders/AmplificationShaders` の flags。GPU 操作全般の validation 条件ではなく、program が必要とする参照方式と stage の要件。 |
| `NativeShaderDescriptorHeapAbiKind`／`NativeShaderDescriptorHeapAbi` | version と heap の index 解釈を持つ。`None` は heap 非使用、`DirectX12` は opaque heap index、`VulkanUnified` は device 由来の統一 stride、`VulkanFixed(layout)` は `NativeGpuDescriptorLimits` に埋め込んだ配置との完全一致。ABI の `Kind/Version/Layout` は不変値。 |
| `NativeShaderLoader(native)` | 一つの `INativeGpuBackend` を非所有で参照し、その code format と ABI に対応する artifact を選ぶ。 |
| `NativeShaderLoader.Load(package, expectedAbiHash = null)` | 対応 version、target、code format、要求 capability と descriptor ABI が一致する artifact 一つを選ぶ。該当なし・複数候補は失敗する。任意の expectedAbiHash は選択後の ABI 識別子と ordinal 比較し、生成入力との不一致を拒否する。I/O、デシリアライズ、runtime compilation は行わない。 |
| `NativeShaderProgram.Code` | 既存の `NativeGpuShaderProgram`。選択済み raw code と entry point を pipeline 作成へ渡す。 |
| `NativeShaderProgram.Target`／`AbiHash` | 選択済み artifact の target と生成入力に対応する不透明な ABI 識別子。hash から shader の合法性を推定しない。 |
| `NativeShaderProgram.RootLayout`／`ParameterLayouts` | 選択した artifact の root と、root から参照する data の byte 配置。`NativeShaderInputLayout` の値を使う。 |
| `NativeShaderInputLayout`／`NativeShaderInputField`／`NativeShaderInputFieldKind` | layout は `AbiId/Size/Alignment/Fields`、field は `Name/Kind/Offset/Size/ResourceKind` を持つ。kind は `Scalar/Vector/Matrix/GpuAddress/DescriptorIndex`。byte 範囲と名前を保持し、型変換、resource の所有や到達先の列挙を表さない。 |
| `NativeShaderResourceKind` | `None/Buffer/View/Sampler`。compiler の明示 annotation から管理入力型の参照種別を確定する metadata。整数の名前や値から推測せず、runtime の依存追跡を追加しない。 |
| `NativeShaderProgram.Dispose()` | 選択 artifact の host 側保持を解消する。GPU pipeline の破棄や GPU wait を行わない。 |

生成された C# source は Native 専用 namespace に unmanaged な root/input 構造体を定義する。field offset、size、padding と row-major matrix の配置を compiler の出力へ一致させる。各生成物は対応する package/ABI 識別子を持ち、build で組にして扱う。bool は共有配置が明確な整数表現にする。

loader は生成された任意の host 構造体を探索・変換しない。この library の呼出し側が、選択した artifact と同じ ABI の生成構造体を渡す。Native 内でも DirectX 12 と Vulkan で root の参照方式・配置が異なる場合は、それぞれの構造体を生成し、型と artifact の対応を明示する。

共通 RenderGraph との接続は機能 pass の単位で行う。pass 作者は共通の入力・出力契約に対し、Native と Portable の独立した実装を用意する。Native の pass 本体が `BuildAsync` で shader を準備し、その pass 専用の GPU 構造体、address／descriptor 参照、内部 graph を構築する。host が `Lumyte.Resources` で準備した package を pass factory に渡し、program の初期化・GPU cache の時機は pass 実装が管理する。pass の `BuildAsync` はファイルのロードや package の展開を行わず、共通の shader schema から target 間の変換コードを生成する契約は設けない。下位 shader compiler／package／loader が RenderGraph の型を参照する逆依存は作らない。

## Source と package 形式

source は Native の descriptor heap と直接 root を前提とする Slang とする。要求する参照方式を target ごとに明示し、buffer descriptor で読む variant と raw GPU pointer で読む variant を同一 ABI として扱わない。

BRDF、色変換、filter、座標や変形の計算は、共有 Slang module を import して再利用できる。entry と Native resource 参照の処理が値を取り出して共有関数へ渡す。interface／generic を使う場合は offline に具体化し、shader の動的 dispatch や共通 host layout を要求しない。import 閉包の source、compiler の版、target 設定は build の依存情報に含め、共有 source が変われば自系統の artifact と生成 host source を組で再生成する。

package は Native 専用の形式識別子と version を持つ container とし、次を格納する。

- program の stage/entry point と target ごとの raw artifact。
- target code format、要求 capability、descriptor heap ABI と compiler 設定の識別情報。
- root と Parameter Data の field 配置、および対応する生成 host source の ABI 識別子。
- artifact の識別・破損検出に使う hash。

mesh raster は従来の vertex raster と別の program とする。Slang の `mesh`／`amplification` entry を対象 stage として compile し、DirectX 12 の mesh shader と Vulkan の `VK_EXT_mesh_shader` 用 artifact を生成する。meshlet の出力配列、primitive topology、thread 数と amplification→mesh payload は shader の契約とし、compiler/native の linkage と制限の診断を使う。command の root はこれらの stage へ直接渡され、payload を command が生成・upload する契約は加えない。Slang の stage と intrinsic の対応は [公式の mesh／amplification 記述](https://docs.shader-slang.org/en/stable/coming-from-glsl.html#mesh-shader) と [DispatchMesh の target 要件](https://docs.shader-slang.org/en/stable/external/core-module-reference/global-decls/dispatchmesh-08.html) に基づく。

mesh 非対応 device のための vertex program は pass 作者が別途用意する。loader が mesh artifact を vertex artifact へ変換したり、meshlet から index buffer を生成したりしない。vertex と mesh の program 間で共有できるのは計算 module であり、異なる stage の GPU 命令列まで同一にする要件はない。

Vulkan の descriptor index は ADR 0008 の固定 slot stride で解釈する。固定とは一つの heap 内で slot 間隔が一定という意味で、全 GPU に同じ数値を埋め込む要件ではない。`VulkanUnified` version 1 は Slang の `-spirv-unified-descriptor-heap-stride` に対応し、SPIR-V の `OpConstantSizeOfEXT` と `ArrayStrideIdEXT` で resource の `max(imageDescriptorSize, bufferDescriptorSize)`、sampler の `samplerDescriptorSize` を使う。loader はこの式の結果と device が公開する slot stride の一致を確認する。backend の alignment 計算を再実装して artifact の式と同じとみなさない。[Slang の公式 compiler option](https://docs.shader-slang.org/en/stable/external/slang/docs/command-line-slangc-reference.html#spirv-unified-descriptor-heap-stride)。

特定の数値配置を埋め込む artifact は `VulkanFixed(layout)` とし、device の配置に一致する場合だけ選ぶ。loader は SPIR-V の stride を書き換えず、一つの artifact がすべての device に適合するとは要求しない。これらは Lumyte の heap と artifact の ABI 対応の確認であり、descriptor の合法性や native の feature validation を再実装するものではない。

DirectX 12 で raw shader pointer が提供されない場合は、buffer descriptor を使う Native variant を事前に用意する。loader が pointer を descriptor index に読み替えたり、root layout を書き換えたりしない。要求する variant がなければ program の初期化は失敗する。

build では Slang の target compiler と target の公式検証ツールを使う。container の破損・形式は CPU resource のデシリアライズ時に扱う。Graphics runtime は渡された data の byte 範囲と artifact 選択に必要な version／ABI 識別を扱い、shader の合法性や native feature 条件を網羅する別の validator は作らない。

## Root と Parameter Data

root の全 bytes を root constants または push data へ直接渡す。root は artifact の配置と Native device の容量に従い、容量を超える root を GPU buffer へ退避する経路は設けない。大きな data は最初から別の Parameter Data として設計し、caller が root に明示した pointer/index から shader が参照する。

Parameter Data の生成、保存先、upload と寿命は caller または Native Resources の責務である。機能 pass は準備済みの CPU upload data と pass 入力から、自身の shader が要求する buffer data を明示的に準備・upload する。consumer に shader 用の data schema を要求せず、Native pointer/index を含む bytes を他系統の buffer にそのまま共用しない。command と shader loader は root の中身を解釈して Parameter Data を自動生成・upload・保持しない。入力配置情報から全到達 resource を推定せず、GPU pointer/index が参照する資源の保持は上位側が明示する。

## GPU 初期化と所有権

`NativeShaderPackage` は CPU 側の不変 data である。CPU 資産の eviction 後も参照中の artifact bytes が有効なように、data を所有するか、移譲・コピー済みの memory を使う。loader は必要な artifact bytes を `NativeShaderProgram` に保持させ、loader と program は native device を所有しない。program の `Code` を入力として使う間は program を生存させる。

pipeline 作成 API は、戻った後に必要な code と entry point を自身で保持する。DirectX 12 の提出時 PSO 生成にもこの保持済み code を使うため、pipeline 作成後の program の Dispose が未コンパイル PSO の入力を失わせない。pipeline の破棄と GPU 利用期間は caller または Native Resources が管理する。

更新では `Lumyte.Resources` 側が新しい package を渡し、Graphics 側が新しい program を作る。既存 program や pipeline を使用中に書き換えず、新しい世代の利用先への切替えと旧世代の退役は上位側が扱う。

## コード配置

以下は repository root 相対の配置とする。runtime と offline の `Lumyte.Graphics.Native.Shaders.Offline`、それぞれに隣接する xUnit project を実装した。既存の `src/graphics/Lumyte.Graphics.Shader/` と `tools/Lumyte.Graphics.Shader.Offline/` は移植元とし、新しい tool から旧共通 package／ABI への互換経路は作らない。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native.Shaders/Packages/` | `NativeShaderPackage`、artifact／stage artifact、target、capability と descriptor heap ABI の展開済み入力型。 |
| `src/graphics/Lumyte.Graphics.Native.Shaders/Layouts/`、`src/graphics/Lumyte.Graphics.Native.Shaders/Programs/` | 前者は root／parameter layout の不変型、後者は `NativeShaderLoader` の artifact 選択と `NativeShaderProgram` の host 側保持。 |
| `tools/Lumyte.Graphics.Native.Shaders.Offline/Compiler/` | build target／request／result、Slang の offline 呼出し、DXIL／SPIR-V の生成と Vulkan の slot stride に対応する ABI lowering。 |
| `tools/Lumyte.Graphics.Native.Shaders.Offline/Generation/` | compiler が確定した配置から C# root/input 構造体、管理入力 XML、package factory、JSON container と ABI 識別子を生成する。 |
| `tools/Lumyte.Graphics.Native.Shaders.Offline/Build/` と `.targets` | request JSON を読む CLI と MSBuild の生成入力登録。出力 inventory の CPU 処理は `tools/Lumyte.Graphics.Shaders.Offline.Shared/` を source link する。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/<Feature>/Shaders/` | 新設予定の Native pass project 内で、ImageProcessing／Models／TwoD 等の所有者ごとに Slang source と build 入力を置く。独自 pass はその実装 project 内に source を持つ。 |
| `src/graphics/Shaders/Shared/` | Native と Portable の build が import する計算用 Slang module。source の共有に限定し、共通 runtime assembly、GPU 構造体や shader package は置かない。 |
| 利用 project の `obj/<Configuration>/<TargetFramework>/Shaders/Native/` | 生成 C#、DXIL／SPIR-V と package artifact の build 出力先。追跡する shader source とは分け、生成 C# はその利用 project でコンパイルする。 |
| `src/graphics/Lumyte.Graphics.Native.Shaders.Tests/Packages/`、`src/graphics/Lumyte.Graphics.Native.Shaders.Tests/Programs/` | 不変入力の所有、artifact 選択、program の保持・破棄について、GPU を使わない unit test。 |
| `tools/Lumyte.Graphics.Native.Shaders.Offline.Tests/Compiler/`、`tools/Lumyte.Graphics.Native.Shaders.Offline.Tests/Packaging/`、`tools/Lumyte.Graphics.Native.Shaders.Offline.Tests/Generation/` | build の入力・出力契約と ABI 識別を検証する unit test。生成 C# は consumer としてコンパイル・実行し、field 配置等の振る舞いを確認する。 |
| `tools/Lumyte.Graphics.Native.Shaders.Offline.Tests/Fixtures/`、`tools/Lumyte.Graphics.Native.Shaders.Offline.Tests/Integration/` | 前者は直接 root、共有計算と mesh／amplification の小さな shader fixture、後者は固定版の実 compiler／公式検証ツールを起動する試験。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/` | 準備済み artifact を package に保持し、loader から pipeline を作って直接 root と GPU 出力を確認する実機試験。各 backend の fixture と GPU 排他を共有する。生成型との適合は offline tool の追加後に拡張する。 |

offline tool は runtime の入力型を参照できるが、runtime library は offline tool を参照しない。ファイル取得、container のデシリアライズと cache のロードは `Lumyte.Resources` 側が担当し、Graphics に新しい loader／decoder を追加する意味の配置ではない。`NativeShaderLoader` はメモリ上の package からの artifact 選択だけを担う。

## 使用例

`package` は `Lumyte.Resources` 側で展開・準備した compute 用 `NativeShaderPackage`、`native` は初期化済み device とする。この例では GPU work を提出しない。

```csharp
using Lumyte.Graphics.Native.Shaders;

var loader = new NativeShaderLoader(native);

using var program = loader.Load(package);
var pipeline = native.CreateComputePipeline(program.Code);
program.Dispose(); // pipeline は戻った時点で必要な code を保持している。

native.DestroyComputePipeline(pipeline);
```

この低層 library を直接使用する dispatch では、package と組で生成した Native root 構造体の bytes を渡す。共通 RenderGraph の利用では Native の機能 pass 本体がこの呼出しと root の構築を担当する。Portable の同名 shader に同じ field があっても、その GPU 構造体を再利用しない。

## 検証方針

offline の ABI 生成と artifact の組、target 選択、host bytes の所有、直接 root の配置を検証する。mesh は固定 toolchain で DXIL／SPIR-V の stage、topology と payload を compile し、対応 GPU で amplification あり／なしと Pixel なしの raster、直接 root の readback を確認する。共有計算は Native 内の両 target で数値結果を確認する。native shader/pipeline の適合性は compiler と native の診断に委ねる。resource の到達先を runtime reflection で列挙する検証や shader bytes の独自再解析は行わない。

## 採用差分と未実装範囲

直接 root、Native Bindless と caller-owned GPU data は NoGraphicsAPI を基礎とする。C# 構造体生成、Native package と loader は Lumyte の上位ライブラリとして追加する。DirectX 12 の buffer descriptor variant は raw shader pointer の部分採用であり、同等の pointer 機能を提供したとは扱わない。

メモリ上の package/artifact 型、root／parameter layout、version・target・capability・descriptor ABI による選択、任意の生成入力 ABI 照合と program の host 側保持を実装した。package は入力配列と code をコピーし、低レベル Code の変更を package や別 program へ反映しない。pipeline 作成後に program を破棄して実行する経路、直接 root と Vulkan の統一 descriptor stride の検証範囲は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。Mesh／Amplification は package の stage 構成と必須 capability を扱う。

Slang 2026.17 の target 別 reflection から artifact、root／parameter 配置、host C#、管理入力 XML と JSON container を生成する offline compiler を実装した。`LumyteResources` module の `LumyteResource("GpuAddress"/"View"/"Sampler")` 属性から参照種別を取得し、parameter 型は同じ設定の `StructuredBuffer<T>` probe で反映する。MSBuild は shader の request JSON を受け取り、XML と生成 C# を consumer へ渡す。全 compile 成功後に出力 inventory を更新し、削除された入力を除去する。現時点は毎 build で compiler を呼ぶため、import の変更も再反映される。依存 fingerprint による compile cache は未実装である。

実 Slang による DXIL／SPIR-V の生成、target 別の異なる root 配置、C# と管理入力 generator の consumer 実行を確認した。matrix と非 float vector は正確な byte storage とし、matrix の要素 serializer は未実装。VulkanFixed の生成、container の独立した破損検出、生成入力を使う実 GPU 適合試験、Mesh／Amplification package の実機試験、機能 pass への接続は後続とする。既存 runtime の実機 fixture と、今回の compiler／host ABI 試験は別の検証範囲である。source の部分共有は GPU ABI の共通化を意味しない。

ファイルロードと container のデシリアライズは `Lumyte.Resources` の責務とし、この API の採用範囲に含めない。機能 pass が所有する shader 準備・cache と内部 graph への接続も未実装である。runtime shader compilation、Portable artifact の読み込み、任意 GPU ABI 構造体どうしの相互変換、GPU 生成 root、ray tracing／tessellation 等の追加 stage はこの設計の範囲に含めない。
