# ADR 0023: Portable Shader の言語・package 入力型・GPU 初期化 API

## 状態

採用（目標設計）。`Lumyte.Graphics.Portable.Shaders` の Portable 専用 shader toolchain、準備済み package の入力型と GPU program の初期化を定義する。

## 依存 ADR

- [0016 Portable Graphics API](0016-portable-api.md): 独立した target と直接入力。
- [0020 View API](0020-view-api.md): resource 入力の値。
- [0021 Binding Layout](0021-binding-layout-api.md): shader が要求する group 宣言。
- [0022 Resource Binding](0022-binding-api.md): 実 resource の対応付け。

## 決定

Portable の実行・package 言語は WGSL とする。作者は Slang から WGSL を生成する経路と、直接 WGSL を記述する経路を選べる。Slang を使う場合は Native と計算用 source module を共有できるが、entry point、resource 宣言と GPU 入力の配置は Portable 側で定義する。build 時に最終 WGSL、entry point、binding 宣言、root/parameter 構造体をまとめ、Portable 専用 package と C# の GPU 入力構造体を生成する。Native の shader binary、構造体 layout、package、loader を共有しない。共通 RenderGraph の利用者は Model、Blur、2D 等の機能 pass の CPU 入力と論理 I/O を渡す。Portable の pass 作者が専用 shader、GPU 構造体、loader と cache を管理する。同じ consumer binary の実行を保証する境界は機能 pass の契約であり、consumer に program ID や shader の ABI を要求しない。

shader は `@group`／`@binding` で宣言した buffer、texture、sampler を参照する。Bindless の global index や、型別の全 resource 候補を展開する compiler pass は設けない。vertex pulling を使う場合も明示した storage buffer binding から読み出す。

root data は `var<immediate>` の直接入力であり、program ごとに byte 数と構造体 layout を定める。WGSL の language feature `immediate_address_space` と、runtime の直接入力経路を要求する。uniform/storage の Parameter Data は明示した resource とし、shader が root に含む index/offset 等から参照位置を計算する。command や loader が root を解析して Parameter Data の生成・転送を行わない。

## API

### 低レベル module と pipeline 入力

以下の型と操作は `Lumyte.Graphics.Portable` に置く。上位の package、BindingSchema、AbiHash を低レベル backend に参照させず、GPU 初期化と pipeline に必要な構成値だけを渡す。

| API | 説明 |
| --- | --- |
| `GpuShaderModuleHandle` | device に属する WGSL module の非所有 identity。 |
| `IPortableGpuBackend.CreateShaderModule(wgsl)`／`DestroyShaderModule(module)` | WGSL から module を生成し、未提出記録・pipeline・GPU 利用の終了後に破棄する。source の処理と検証は WebGPU に委ねる。 |
| `GpuShaderEntryPoint(Module, Stage, Name)` | module、Portable の `GpuShaderStage`（Vertex／Pixel／Compute）と entry 名の不変の組。Pixel を WebGPU の fragment stage へ接続する。 |
| `GpuShaderProgramDescription(EntryPoints, BindingLayouts, ImmediateSize)` | entry と group 順の binding layout handle、直接入力 byte 数を持つ不変の非所有構成値。package の schema／ABI metadata は含めない。 |

description は GPU の状態照会ではなく、caller が準備した入力を保持する。コピーしても module／layout の寿命を延ばさず、構成値から shader package を逆引きしない。

`GpuShaderModuleHandle` は public abstract 型と protected constructor で外部 backend が実装する。`CreateShaderModule(string wgsl)` は呼出し中に source を消費する。`GpuShaderEntryPoint` は readonly record struct、`GpuShaderProgramDescription` は entry／layout の ReadOnlySpan をコピーし、読取り専用の `EntryPoints`／`BindingLayouts` と `uint ImmediateSize` を公開する。元配列の変更は description に反映されない。native host の文字列変換は不正な UTF-16 を置換して別の source にせず、変換失敗として報告する。

### Build と package

| API | 説明 |
| --- | --- |
| `PortableShaderSourceLanguage` | build 入力の `Slang/Wgsl`。どちらも出力は WGSL であり、runtime の code format を増やさない。 |
| `PortableShaderSource` | Language、source module、依存 module 入力、entry point と生成型の名前。Slang の依存 module は import に使い、直接 root を持つ場合は `RootDeclaration` に専用の WGSL root 型・`var<immediate>` の宣言 text を渡す。offline tool がここから accessor module を生成し、Slang の import 入力に加える。Wgsl は root を含む完結した module text を渡し、RootDeclaration は指定しない。raster は vertex/pixel、compute は compute entry を指定する。Mesh／Amplification は受け付けない。 |
| `PortableShaderCompileOptions` | 必要な WGSL feature、target language version、固定した compiler/toolchain の版、生成 C# namespace と optimization 設定。Slang の target は WGSL とし、Native target の code format は指定しない。 |
| `PortableShaderCompiler.Compile(source, options)` | Slang 入力は公式 compiler で WGSL へ生成し、Wgsl 入力と同じ WGSL toolchain へ渡す。最終 module に対応する Lumyte package／入力 schema を生成する。戻り値は `PortableShaderBuildResult`。 |
| `PortableShaderBuildResult` | `Package`、`GeneratedSources` と build diagnostics。shader の resource usage や device limit の独自 validator は生成しない。 |
| `PortableShaderPackage(Version, Module, EntryPoints, RequiredFeatures, GroupLayouts, RootLayout, ParameterLayouts, BindingSchema, AbiHash)` | GPU 初期化に渡す展開済みの不変 data。`Module` は所有する WGSL text、各 entry/layout/schema は不変の値と列である。device、ファイル名や stream を持たない。 |
| `PortableShaderBindingSchema` | binding の意味名、group、binding、型の対応。下位 resource 入力と、任意の上位連携コードの生成に用いる。 |
| 生成 Root／Parameter 構造体 | WGSL の host layout に一致する Portable 専用の値型。member offset、padding、matrix layout を明示する。Native の同名型との byte 互換は要求しない。 |
| 生成 BindingInputs 構造体 | group ごとに `GpuBufferRange`、`GpuTextureView`、`GpuSamplerDescription` を意味名で渡す低層入力を生成する。group と binding 番号を package の schema に対応させる。 |

ファイル取得、package の保存・読取り・展開と資産 cache のロードは `Lumyte.Resources` の分野とする。Graphics の実行時 API は準備済みの `PortableShaderPackage` を受け取り、`Read(stream)`／`Write(stream)` や URI resolver は設けない。offline compiler は build 入力から同じ package data を生成できる。

package は実行対象の WGSL を保持し、device object や resource handle、実 material 値を保持しない。ABI hash は package と生成コードの対応を確認する値であり、shader の合法性を保証する証明ではない。

Slang の入力 module、import 閉包、toolchain の版、Portable 専用 root accessor の版と target 設定は build の依存情報へ含める。共有 module の変更では Native と Portable を独立して再生成し、それぞれの package と生成 host 型の対応を保つ。Slang の IR や Native の artifact を Portable runtime に渡して変換する経路は設けない。

上位の管理 resource を持つ入力は、Resources 連携側の generator がこの Portable 専用 schema から別途生成する。機能 pass の CPU 入力は pass 作者が機能の契約として定義し、shader schema から生成しない。Portable の pass 本体が必要な root、buffer range、view、sampler と group ごとの binding を明示して構築する。Native と同じ shader 構造や計算手順を要求せず、共通の shader 入力を target ABI へ変換する adapter は設けない。shader library は上位 resource manager や RenderGraph の型へ依存しない。

### GPU 初期化

| API | 説明 |
| --- | --- |
| `PortableShaderLoader(backend)` | `IPortableGpuBackend` に結び付けた loader。backend の所有権は取得しない。 |
| `PortableShaderLoader.Load(package)` | 展開済みの Portable package を選択 device 用の `PortableShaderProgram` にする。I/O とデシリアライズは行わない。必要な shader module と binding layout を生成し、要求 feature の不足を報告する。 |
| `PortableShaderProgram` | device に属する program。`Kind`（Raster／Compute）、`EntryPoints`、`BindingLayouts`、`ImmediateSize`、`BindingSchema`、`AbiHash` を提供する。 |
| `PortableShaderProgram.Description` | 作成時に確定した低レベルの GpuShaderProgramDescription。pipeline の生成へ渡す非所有値であり、取得時に native API の照会や GPU 処理は行わない。 |
| `PortableShaderProgram.Dispose()` | 所有する module/layout を解放する。参照する pipeline、binding、未提出記録と GPU 利用を先に終了する。 |

loader は WGSL/compiler/runtime に委ねる validation を再実装しない。Lumyte 固有の入力 data 種別・version・生成型との対応は確認する。container の形式・破損は CPU resource 側のデシリアライズで扱う。別系統の package を受け取った場合に変換を試みる shader loader は設けない。共通 RenderGraph に追加した機能 pass は、host が `Lumyte.Resources` で準備して pass factory に渡した package を使い、Portable 本体の `BuildAsync` で GPU program を初期化して内部 graph を構築する。shader の選択と準備済み program の cache は pass 実装が管理し、consumer に shader の管理を要求しない。pass 本体はファイル取得や package 展開を行わない。

material 等の CPU data から WGSL の buffer 配置への変換は、準備済み CPU upload data を受ける pass 実装の明示的な GPU 配置／upload 操作で行う。Native の pointer/index を埋め込んだ GPU bytes をそのまま取り込まず、その pass 専用の Portable payload と参照を構築する。consumer へ shader 用の data schema を公開しない。command や shader loader が root の解釈から Parameter Data を生成・転送する経路は加えない。

package の WGSL text と metadata は data 自身が所有するか、移譲・コピーにより不変の内容を保持する。元の CPU 資産 cache が eviction されても GPU 初期化中の入力を無効にしない。

`ImmediateSize` は program 固有で、device の `MaxImmediateSize` 以下にする。対応しない runtime では初期化を、limit を満たさない program では作成を失敗させる。root を隠れた uniform/storage buffer に置き換えない。

## Slang の共有範囲と採用条件

共有する source は BRDF、色変換、filter の係数、座標計算、skinning 等の値を受け取って値を返す処理を中心とする。Slang の module、interface と generic は offline に具体化し、shader 内の仮想呼出しや共通 GPU ABI を要求しない。resource の取得、直接 root、binding、stage 入出力と workgroup の構成は Portable 専用 module に置く。Slang の WGSL 出力では明示した binding 指定を WGSL の group/binding に接続できる。mesh stage は WGSL target の対象外である。[Slang の WGSL target](https://shader-slang.org/slang/user-guide/wgsl-target-specific)、[Slang の module](https://shader-slang.org/slang/user-guide/modules)、[specialization](https://shader-slang.org/docs/compilation-api/)。

2026-09-10 の実験で、Slang 2026.7.1／2026.17 の通常の `ConstantBuffer`、`[push_constant]`、`[[vk::push_constant]]` と entry の `uniform` は WGSL の `var<immediate>` を生成しないと確認した。一方、Portable 専用の `__requirePrelude` と `__intrinsic_asm` による直接 root の生成は可能である。2026.17 の生成物を変更せず Edge 152.0.4191.66／NVIDIA GPU で実行し、16 byte の root と padding を含む 32 byte の root を異なる値で dispatch して期待値を読み戻した。compiler の標準的な root lowering と、相互運用機能を使った経路を区別する。[実験結果と再現手順](../designs/slang-wgsl-root-investigation.md)、[固定版の WGSL emitter](https://github.com/shader-slang/slang/blob/v2026.17/source/slang/slang-emit-wgsl.cpp#L172-L177)。

この結果に基づき、直接 root を使う Slang program にも Portable 専用の root accessor を採用する。初期の検証基準版を Slang 2026.17 とし、次の範囲に相互運用機能を閉じ込める。

- offline tool が Portable の root 宣言から、自己完結した WGSL の root 型・`var<immediate>` と、それを読む Slang の scalar／vector accessor を用意する。pass の計算本体は通常の Slang の値を使う。root の WGSL 型を Slang の生成型名や member 名へ直接 alias しない。実験では型名だけの alias が member 名の変換と食い違った。
- 必要な `requires immediate_address_space;` は module の宣言より前へ置く。toolchain は `RequiredFeatures` に対応する directive を公開 compiler API の `setLanguagePrelude` でまとめ、root 宣言と accessor は専用 module に閉じる。global session の prelude を並行 build の途中で変更せず、feature 集合を固定する。CLI の最小実験は一つの prelude に directive と root 宣言をまとめたものとする。`__requireTargetExtension` はこの版で `enable` を出力するため、`requires` の代用にしない。
- prelude の root は Slang の通常の root reflection に現れない。`RootLayout`、`ImmediateSize` と生成 C# の offset／padding は、最終 WGSL を処理した公式 frontend の型・配置情報から決める。残存する Native 宣言や Native の reflection を Portable package の根拠にしない。これは ABI metadata の生成であり、WGSL の合法性の独自検証は行わない。
- compiler の版と accessor を一緒に固定し、更新時には生成物を公式 WGSL frontend と実 runtime に渡して再確認する。相互運用機能は公式文書に記載されているが内部機能であり、将来の source 互換を前提にしない。root の buffer 退避、生成 WGSL の文字列書換え、compiler 失敗時の入力方式の暗黙切替えは行わない。

この小さな WGSL 接続を除く計算を Slang で共有するため、純粋な Slang の標準 root 宣言だけで成立したとは扱わない。対象の stage／型／機能を扱えない program では作者が直接 WGSL を build 入力として明示的に選ぶ。root を持たない program の `ImmediateSize = 0` は元々の入力設計であり、共有するために root を Parameter Data へ移すことはしない。直接 WGSL の variant も同じ機能の数値・描画結果で適合を確認する。[Slang の相互運用機能](https://docs.shader-slang.org/en/latest/external/slang/docs/user-guide/a1-04-interop.html)、[公開 prelude API](https://github.com/shader-slang/slang/blob/v2026.17/include/slang.h#L4183-L4192)、[WGSL extension 出力](https://github.com/shader-slang/slang/blob/v2026.17/source/slang/slang-extension-tracker.cpp#L17-L24)。

build では Slang の WGSL 出力を target 対応の公式 WGSL frontend（Tint 等）と runtime の診断へ渡し、独自の shader validator は作らない。生成 C# は最終 WGSL の buffer／immediate 配置に対応させる。matrix の向き、padding、binding 番号と direct root は小さな consumer fixture と GPU readback で確認し、Native の reflection 結果や生成 C# を流用しない。compiler の成功だけで target の全 shader feature、bindless、mesh shader の互換性を保証したとは扱わない。

## コード配置

以下は repository root からの配置で、未実装部分の目標配置を含む。Portable の低層と隣接するテスト project は実装済みで、shader runtime／offline tool とそれぞれのテスト project は新設予定とする。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Shaders/` | GpuShaderModuleHandle、GpuShaderEntryPoint、GpuShaderProgramDescription と module の生成・破棄契約。上位の package、compiler と生成器への依存は持たせない。 |
| `src/graphics/Lumyte.Graphics.Portable.Shaders/Packages/` | 準備済み `PortableShaderPackage`、binding schema、root/parameter layout と ABI metadata の不変入力型。ファイル、stream、URI のロード処理は置かない。 |
| `src/graphics/Lumyte.Graphics.Portable.Shaders/Programs/` | `PortableShaderLoader`、`PortableShaderProgram` と GPU module/layout の初期化・所有処理。 |
| `tools/Lumyte.Graphics.Portable.Shaders.Offline/Compiler/`、`tools/Lumyte.Graphics.Portable.Shaders.Offline/Packaging/`、`tools/Lumyte.Graphics.Portable.Shaders.Offline/Generation/` | Slang／WGSL source と compile options、Slang の WGSL target と WGSL toolchain の呼出し、package data の構築、Root／Parameter／BindingInputs の C# 生成。build result と diagnostics も offline tool 側に置く。 |
| `tools/Lumyte.Graphics.Portable.Shaders.Offline/Compiler/WgslRoot/` | 固定した Slang の prelude 設定、Portable root 宣言と scalar／vector accessor の生成。compiler 内部機能への依存をこの build 用処理に限定する。 |
| `src/graphics/Lumyte.Graphics.Portable.Passes/<Feature>/Shaders/` | 新設予定の pass 実装 project が所有する Slang entry／resource module と、直接記述する WGSL variant。`<Feature>` は `ImageProcessing`、`Models`、`TwoD` 等とし、各 pass の GPU ABI と source をその実装の隣に置く。 |
| `src/graphics/Lumyte.Graphics.Portable.Passes/<Feature>/Shaders/*.root.wgsl` | Slang program の RootDeclaration に渡す Portable 専用 root 宣言。Slang entry の隣に作者が置く build 入力であり、Native と共有しない。生成 accessor と host 型は `obj/` 側へ出力する。 |
| `src/graphics/Shaders/Shared/` | Native と source のみ共有する Slang の計算 module。build が import 入力として参照し、runtime assembly、共通 package や共通 GPU 構造体は生成しない。 |
| `<利用 project>/obj/<Configuration>/<TargetFramework>/Shaders/Portable/` | build で生成した C# と shader artifact。source と区別した中間出力とし、Native の生成物とも分離する。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Shaders/`、`src/graphics/Lumyte.Graphics.WebGPU.Browser/Shaders/` | 既存 project を改編し、WGSL module・直接入力の runtime 接続と、必要なブラウザー interop をそれぞれ置く。ブラウザー専用 shader library は新設しない。 |
| `src/graphics/Lumyte.Graphics.Portable.Shaders.Tests/Packages/`、`src/graphics/Lumyte.Graphics.Portable.Shaders.Tests/Programs/` | 新設予定の xUnit project。準備済み package の保持と、fake backend による program 初期化・終了を検証する。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Shaders/` | 隣接 xUnit project。低レベル構成値と上位 program を必要としない API の使用を確認する。 |
| `tools/Lumyte.Graphics.Portable.Shaders.Offline.Tests/Compiler/`、`tools/Lumyte.Graphics.Portable.Shaders.Offline.Tests/Generation/` | 新設予定の xUnit project。compiler の結果と、生成した C# を実際に compile・実行する consumer 試験。ABI 適合確認用の shader source は同 project の `Fixtures/`、Slang→WGSL と公式 frontend の外部 process が必要な試験は `Integration/` に置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Shaders/` | 既存 xUnit project に置く実 runtime/device の WGSL、binding と直接入力の適合試験。 |

runtime shader library は offline tool を参照しない。参照方向は Portable.Shaders → Portable とし、pipeline の生成には program.Description を渡す。Portable の backend が上位 PortableShaderProgram 型を受け取る循環を作らない。既存の `src/graphics/Lumyte.Graphics.Shader/` と `tools/Lumyte.Graphics.Shader.Offline/` は移植元とし、旧 API の互換層は残さない。package の保存・取得・復号は `Lumyte.Resources` 側に置く。

## 使用例

以下はこの shader library を直接使う例とする。`package` は `Lumyte.Resources` 側で展開・準備した `PortableShaderPackage`、`backend` は Portable device とし、型生成は build 時に完了している。共通 RenderGraph の consumer はこの呼出しや Portable 専用 root 型を持たず、Portable の機能 pass 本体が担当する。

```csharp
using Lumyte.Graphics.Portable.Shaders;

var loader = new PortableShaderLoader(backend);
using var program = loader.Load(package);

Console.WriteLine(program.ImmediateSize);
var root = new LightingRoot { MaterialIndex = 7 };
// root は対応する program の直接入力として command に渡す。
```

## 参考文献

- [WGSL: Language Extensions](https://gpuweb.github.io/gpuweb/wgsl/#language-extensions): `immediate_address_space` と language feature の宣言。
- [WGSL: Address Spaces](https://gpuweb.github.io/gpuweb/wgsl/#address-spaces): uniform、storage、immediate の区別。
- [WebGPU: Immediate Data](https://gpuweb.github.io/gpuweb/#immediate-data): pipeline の immediate size と command からの入力。
- [Slang: WGSL target](https://shader-slang.org/slang/user-guide/wgsl-target-specific): resource／binding と stage の対応。WGSL 出力対応と direct root の適合を別々に確認する。
- [Slang: Target-Specific Interoperation](https://docs.shader-slang.org/en/latest/external/slang/docs/user-guide/a1-04-interop.html): prelude／intrinsic と、内部機能としての制約。
- [Dawn／Tint](https://github.com/google/dawn#readme): WGSL frontend と shader compiler。採用する版で必要な WGSL feature を扱えることを確認する。

## 採用範囲と未実装事項

Portable 専用の準備済み WGSL package 入力型、GPU 構造体生成、binding schema と GPU program の初期化を採用する。source の Slang 共有は部分採用とし、対応を確認できた計算 module と program に限る。直接 WGSL の経路も正式な build 入力とする。ファイルロードと container のデシリアライズは `Lumyte.Resources` の責務であり、この API の採用範囲に含めない。

低レベルの raw WGSL module、entry point と不変の program description を実装した。native host の WebGPU では module の生成診断を保持し、raster/compute pipeline と実際の提出へ引き継ぐ。Vertex／Pixel の直接 root、明示した resource binding と compute の直接入力を同じ低レベル module API で扱う。package、loader、compiler と C# 生成器はまだ使わず、手動で準備した WGSL と入力値を低レベル契約へ直接渡す。実機検証の範囲は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。

Slang 2026.17 による直接 root の生成と、一つの browser／GPU 環境での compute 実行は実験済みである。二系統への toolchain 分離、root accessor の生成器、公開 `setLanguagePrelude` API の統合・適合試験、公式 WGSL frontend からの ABI metadata／C# 生成、package 形式、機能 pass 本体での shader 準備と cache、全 pass の適合試験は未実装である。実験 fixture の host 配置は手動で与えたもので、生成器の完成を示さない。Slang による raster variant、matrix／array 等の全入力型、C# の WebGPU 接続と他 runtime／GPU でのこの経路の適合は別途確認する。既存 offline compiler に残る WGSL の文字列書換えは目標設計の実装として数えず、移行時に廃止する。mesh／amplification の WGSL 変換と Native GPU ABI の移植は採用範囲外とする。
