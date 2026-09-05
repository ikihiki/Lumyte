# Lumyte レビューと改善工程

レビュー日: 2026-09-05 / 構造・性能の確認対象: c9b9839 / API評価の確認対象: b2b9eaf（レビュー文書追加後の HEAD）。その後の共通API・CommandEncoderの方針変更を本書に統合した。

優先すべきなのは、Resources の終了競合、GPU コマンドの失敗時の寿命管理、descriptor の容量・再利用、Text キャッシュの上限である。その後に API の対応範囲を定義し、プロジェクト境界を整理する。既存の arena、RenderGraph、明示的な resource/view の分離を土台に改善する。

本書はプロジェクト構造・性能・メモリとAPIの使いやすさの評価、および改善工程をまとめたもの。製品コードの変更は含まない。全53プロジェクトの定義・依存関係を確認し、主要な公開 API、実行経路、寿命管理、テスト、ベンチマークを重点的に読んだ。すべてのメソッドを網羅する監査ではない。性能の指摘は、今回計測した数値と、ソースから判断した改善候補を区別する。

## 1. 検証結果と現在地

| 項目 | 結果 |
| --- | --- |
| プロジェクト数 | 53。テスト24、その他29（サンプル、ツール、空の Browser プロジェクトを含む） |
| 作業開始時 | git の追跡対象に変更なし |
| dotnet test Lumyte.slnx --nologo --verbosity minimal | 終了コード1。Resources のテストホストが未処理例外でクラッシュ |
| Resources | 58件合格のログがあるが、ホストクラッシュにより中止判定。成功扱いにはできない |
| その他の .NET テスト | 23プロジェクト、計893件合格、スキップ0 |
| GPU バックエンドのテスト | Vulkan 144件、WebGPU Native 143件、DirectX12 146件合格。上記893件に含む |
| ブラウザ版 Graphics | Browser プロジェクトに実装ソースがなく、ブラウザでの Graphics 実行は検証対象にできていない |
| フロントエンド | 型検査・ESLint は成功。Vitest は3ファイル10件合格 |
| 性能・メモリ | 今回は新規の実機 GPU 計測、GC/VRAM プロファイル、長時間試験を実施していない |

Resources の主要な失敗ログ:

~~~text
System.ObjectDisposedException: The CancellationTokenSource has been disposed.
at Lumyte.Resources.ResourceHotReloadManager.OnChanged(AssetChange change)
at Lumyte.Resources.FileAssetChangeSource.PublishChange(String fullPath)
at System.IO.FileSystemWatcher...
~~~

テストがあること自体は強みである。特に、backend 共通の conformance、source generator の生成 API を呼び出す consumer テスト、ManualClock/TimeProvider、RenderGraph のキャッシュ・aliasing・非同期 lifetime のテストは残すべき資産である。

## 2. プロジェクト構造と分割単位

Graphics は src/graphics、その他の本体・テストはリポジトリ直下にあり、配置規則が混在する。現在の大きな依存方向は適切で、Graphics が Platform の具体実装を参照するような逆依存もない。一方、ディレクトリ配置、実行時契約、ビルドツール、ホストアプリの境界は整理できる。

| 領域 | 現状と判断 | 推奨する分割単位 |
| --- | --- | --- |
| Core / Mathematics | Core は7ファイル173行、Mathematics は1ファイル42行。小ささだけを理由に結合する利益は小さい | 時刻・乱数と幾何の責務を保つ。まず配置・説明を統一し、独立配布の需要がなければ将来の統合を検討 |
| Input / Platform | 入力契約、OS非依存の window/input 契約、Windows/Silk 実装が分離済み | 現在の assembly 境界を維持。OS実装を core に戻さない |
| Interaction | 87ファイル4665行。Action/Binding/Gesture と Player/Window 接続が同居 | まず Actions、Bindings、Gestures、Players に内部フォルダ分割。独立利用の需要があれば PlayerInputManager 等だけ Interaction.Platform に抽出 |
| Resources | 71ファイル3588行。ResourceStore だけで1038行 | 公開 facade は維持し、load、generation/reload、collection、diagnostics を内部サービスに分ける。小さな assembly を多数作る必要はない |
| Composition / Generators | マーカーとコンパイラ実装の分離は妥当 | generator は analyzer として配布し、runtime assembly への混入を防ぐ。consumer テストを維持 |
| StateMachine / Animation | 再利用可能な小さい定義と runtime。Animation は StateMachine を利用 | 維持。計測後に hot path のみ改善する |
| Graphics | 67ファイル5497行。低レベル契約、allocation、RenderGraph、MessagePack パッケージを含む | Graphics は device/queue/handle/description/IR に絞る。RenderGraph とシェーダーコンテナは別 assembly へ段階的に抽出 |
| Vulkan / DirectX12 / WebGPU | native API ごとの assembly 分離は妥当。VulkanDevice は2233行 | assembly は維持し、Device、Resources、Bindings、Pipelines、Commands、Synchronization、Presentation に実装責務を分割 |
| TwoD | 66ファイル5340行。Renderer 1031行、RenderGraphExtensions 1026行 | CommandEncoderの状態・clip・layerを単一のscope規則へ再設計。Recording/Preparation、Scene、Geometry、GPU、Composition、GraphIntegrationに内部分割。最初から6個のassemblyにはしない |
| Text | 48ファイル5376行。TextRenderer 1005行 | FontData/Shaping と Rasterization/Cache/TwoDAdapter を内部で分離。GPUを使わない shaping 消費者が必要になった時点で Font/Shaping assembly を抽出 |
| Shader / Offline | Shader は70行の package build tooling、Offline は実行ファイル | Shader runtime/container と build tooling の名前・責務を明確化。低レベル backend は選択済み GpuShaderBinary を受け取る |
| Browser 2プロジェクト | csproj と参照だけで実装ソース0 | WebGPU Native と Browser のホスト実装を区別。未完成状態を README と build target に明記し、browser publish/run の gate を設ける |
| DevTools | Hub、Agent、Server、Host を分離済み。ただし Server が Agent の wire contract を参照し、Host にデモと収集機構が混在 | 通信契約を DevTools.Protocol に抽出。再利用する runtime hosting と samples/devtools のデモを分離 |

行数は bin/obj を除いたプロジェクト配下の C# ソースの概数。リンクされた共通テストは二重加算していない。行数は調査の入口であり、分割理由は依存方向・所有権・変更理由・独立配布で判断する。

推奨する配置:

~~~text
src/
  foundation/   Core, Mathematics, Composition, Generators
  platform/     Input, Platform, Windows, Silk
  interaction/  Interaction, StateMachine, Animation
  resources/    Resources
  graphics/     Graphics, RenderGraph, Shader runtime, 各backend, Library, TwoD, Text
  devtools/     DevTools, Protocol, Agent, Server, 再利用するHosting
tools/          Shader.Offline
samples/        graphics, devtools
benchmarks/     Lumyte.Benchmarks
docs/           architecture, api, performance, reviews
~~~

テストは AGENTS.md の指示どおり、対応する製品プロジェクトの隣に Lumyte.<Area>.Tests として置く。最初の配置変更では assembly 名・namespace を変更せず、意味変更を含む PR と分ける。

**注意:** TwoD と Graphics.Library の Offline 参照は ReferenceOutputAssembly="false" であり、実行時コンパイラ依存ではない。ここは分離済みである。問題は repository 相対の targets、全ターゲットのパッケージ埋め込み、ツール取得を含むビルド・配布経路の明示性にある。[TwoD.csproj](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/Lumyte.Graphics.TwoD.csproj:8)、[Offline.targets](E:/Lumyte/src/graphics/Lumyte.Graphics.Shader.Offline/Lumyte.Graphics.Shader.Offline.targets:23)

## 3. No Graphics API と WebGPU の設計方針

No Graphics API の主旨は、メモリ・所有権・小さいハンドル・shader input を明確にし、binding や PSO 管理の負担を減らす方向にある。原案の64-bit root pointerを、現行の全 API で同じ性能・機構として実現できるという前提にはしない。[原文](https://www.sebastianaaltonen.com/blog/no-graphics-api)

共通APIは、機能・入力範囲・shader ABI・validationを全対応backendで保証する方針とする。共通上限への制限とbackend内部での変換を使い、同じ標準描画・computeコードがbackend別の分岐なしで動作することを目標にする。実deviceのcapability/limitsは初期化、内部最適化、診断、明示的な拡張のために使う。以下の改善方針は未実装であり、現状の対応範囲とは区別する。

| 観点 | 現在の Lumyte | 改善方針 |
| --- | --- | --- |
| allocation と texture/view | 分離済み。persistent arena、alias plan も存在 | 所有権の分離を維持し、標準経路は共通allocatorに接続。明示placement/aliasingは拡張に置き、共通経路はbackend内部で適切な資源生成を選ぶ |
| WebGPU の memory model | DeviceOwnedResources を明示し、placed allocation を偽装していない | 維持。WebGPU でのメモリ削減は descriptor/resource の再利用、pool、使用期間短縮を中心にする |
| root input | 128-byte inline data が ABI に含まれる。GpuDeviceAddress は型のみで入力経路なし | raster/compute共通で最大64 bytesに制限。大きい入力は共通parameter bufferへ移行し、各backendの転送方式を内部で吸収する |
| shader resource | 5種類の論理 table があるが Native も都度 native table を構築 | 論理indexと種類ごとの共通上限を定義。Nativeはpersistent descriptor/ページ再利用、WebGPUはtable/layout変換で同じ契約を実現する |
| shader input | Backend の pipeline 作成が複数ターゲット入り GpuShaderPackage を受け取る | package の読込・選択を上位へ移し、backend-ready IR を受け取る |
| graphics state | GpuDepthStencilState は公開されるが recorder に設定経路がない。CullMode 等は pipeline に入る | 全backendで成立するstate、attachment、sample、format/usageの契約を定義。動的stateとPSOの違いは内部で吸収し、共通範囲外は共通validationで拒否する |
| barrier / graph | stage barrier、lifetime、aliasing、retirement が分離されている | 維持。API共通化のために詳細な万能 resource state enum を追加しない |

根拠: [既存DESIGN](E:/Lumyte/src/graphics/Lumyte.Graphics/DESIGN.md:1)、[GpuBackend](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuBackend.cs:18)、[binding ABI](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuShaderBindingConvention.cs:11)、[address model](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuResourceModel.cs:19)、[pipeline description](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuPipelineModel.cs:22)。DESIGN の初期目標と実装済みの契約を更新し、提案・実装済み・未対応を区別する。

推奨する依存方向（矢印は「参照する」）:

~~~mermaid
flowchart TD
  Application[Application] --> TwoD[Graphics.TwoD / Text]
  Application --> Library[Graphics.Library]
  TwoD --> Graph[Graphics.RenderGraph]
  Library --> Graph
  Graph --> GPU[Graphics: device / memory / command / IR]
  TwoD --> Shader[Shader package runtime]
  Library --> Shader
  Shader --> GPU
  Vulkan[Vulkan] --> GPU
  DX12[DirectX12] --> GPU
  WebGPU[WebGPU Native / Browser adapters] --> GPU
  Offline[Shader build tooling] --> Shader
~~~

## 4. APIの使いやすさ

**総合評価は3/5。個々の操作は読みやすいが、組み合わせたときの所有権・非同期・対応機能の扱いに学習負担がある。** 特に Input、StateMachine、Animation、2Dの描画命令は良い。一方、Graphics の初期化から描画・更新・破棄までを安全に組み立てるには、実装やDESIGN文書を読む必要がある。

ソース、公開API、サンプル、既存テストの使用例に基づく設計評価であり、ユーザビリティテストや開発時間の実測結果ではない。総合評価と各点数は改善前の実装に対するもの。改善方針を文書に反映しても、実装が完了した評価には変更しない。

### 4.1. 評価の前提

利用者を次の3種類に分ける。

| 利用者 | 主な目的 | APIに求めること |
| --- | --- | --- |
| アプリケーション開発者 | UI、文字、入力、アニメーションを実装 | 初期化が少ない、安全な標準経路、よく使う処理の発見しやすさ |
| 描画機能の開発者 | 独自pass、shader、resource、effectを追加 | 明示的な依存・所有権、低allocation経路、診断可能性 |
| backendの開発者 | native APIへの変換とメモリ管理 | 小さい契約、対応機能の明示、失敗時を含む寿命の厳密さ |

No Graphics API由来のallocation/resource/viewの分離や、小さいhandle自体を使いにくさとは評価しない。それらを必要とする層に保ちつつ、アプリケーション側に同じ整合性管理を何度も要求していないかを見る。

5は「通常利用がAPIだけで理解できる」、4は「小さな説明で扱える」、3は「設計理解が必要」、2は「組合せ・誤用時に大きな負担」、1は「主要用途を成立させにくい」という主観的な尺度。

| 評価軸 | 評価 | 理由 |
| --- | ---: | --- |
| 最初に動かすまで | 2/5 | Graphicsのdevice/surface、target、encoder、prepared data、graph、executionの組立てが多い |
| 操作の読みやすさ | 4/5 | FillRectangle、Bind、Fire、Play、Asset.Fileなどは目的が明確 |
| 誤用への強さ | 2/5 | GPU寿命、二重のresource宣言、default handle、状態stackに注意が必要 |
| 名前からの予測可能性 | 2/5 | ExecuteAsync、Prepare、Snapshot、Save/Restoreに説明が必要な差異がある |
| 機能の組合せ | 3/5 | 型付きgraphは良いが、target表現やresource bindingが層ごとに異なる |
| 効率的な利用の発見 | 3/5 | ShapedText、PreparedDisplayList、Scene、plan cacheがあるが、使い分けが分散 |
| 拡張・テストのしやすさ | 4/5 | 小さな入力interface、時計注入、型付きchannel、明示stateのcallback、backend共通testを活用できる |

### 4.2. 領域別の評価

| 領域 | 評価 | 良い点 | 主な改善点 |
| --- | ---: | --- | --- |
| Core / 時刻 | 4/5 | Duration/TimePointの区別、ManualClock、明示的な単位変換 | 標準TimeSpanとの使い分けを説明 |
| Input / Interaction | 4/5 | InputAction<T>、型付きcontrol、宣言的binding | actionの同一性、未登録action、eventとframe更新の順序を明示 |
| StateMachine | 4/5 | definition/instance分離、When/Effect/Fireが読みやすい | DSLの組立て後freezeと参照identityを明示 |
| Animation | 4/5 | typed channel、timeline合成、Play/Pause/Seek、再利用sample buffer | setupの最短例、値identity、即時適用・更新のタイミングを説明 |
| Resources | 3/5 | keyが型付き、scope/pin/lease/snapshotを表現できる | 各型の保持・世代・破棄の違いを入口で説明し、単一資源のleaseを取りやすくする |
| Graphics低レベル | 2/5 | allocationとhandle/view、shader resource IDを分離 | backend非依存の共通契約、commandの状態・終了、所属・寿命の保証 |
| RenderGraph | 3/5 | 型付きtexture/buffer、Read/Write、culling、明示state | Writeの全上書き契約、実行APIの名前、view解決に必要な実行context |
| TwoD | 命令4/5・統合2/5 | FillRectangle、DrawImage、DrawPathが直接的 | 描画前後の組立て、CommandEncoderの状態・clip・layerの再設計、Scene更新の副作用 |
| Text | 3/5 | string/再利用ShapedTextの両経路、Auto rendering | renderer引数の重複、metrics/単位、shapingとlayoutの境界 |
| Platform | 3/5 | 小さいIPlatform/IWindow、client/framebufferの区別 | 実用的なGPU表示にはnative surface操作が必要。presentation adapterの入口がほしい |
| DevTools | 4/5 | 型付きquery/command/event、登録解除をIDisposableで表現 | domainの複数登録をまとめるscope、transport/host設定の最短例 |

根拠となる良い使用例: [Input](E:/Lumyte/Lumyte.Interaction.Tests/ActionRuntimeTests.cs:14)、[StateMachine](E:/Lumyte/Lumyte.StateMachine.Tests/StateMachineTests.cs:13)、[Animation](E:/Lumyte/Lumyte.Animation.Tests/AnimationPlayerTests.cs:13)、[2D](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD.Tests/BackendConformanceTests.cs:20)、[DevTools](E:/Lumyte/Lumyte.DevTools.Host/DemoCounterDomain.cs:21)。

### 4.3. APIの改善項目

<a id="u01"></a>

#### U01 / 最優先: 非同期と破棄の意味をAPI名・型に揃える

GpuRenderGraphPlan.ExecuteAsyncはGpuRenderGraphExecutionをその場で返し、Task/ValueTaskでもawaitableでもない。CompletionもGpuSubmissionTokenで、IsCompleteと同期Waitを持つ。さらにframes-in-flight上限では、内部Submitが古いsubmissionを同期Waitする。利用者には「GPUへ送信する」「CPUを止めずに空きを待つ」「GPU完了を待つ」の区別が必要になる。[ExecuteAsync](E:/Lumyte/src/graphics/Lumyte.Graphics/RenderGraph/GpuRenderGraphPlan.cs:79)、[token](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuRetirementQueue.cs:4)、[上限時のWait](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuRetirementQueue.cs:102)

推奨する名前と契約:

| 意味 | 推奨案（未実装） |
| --- | --- |
| 記録した処理を送信し、結果・完了tokenを受け取る | Submit(context)。上限時にblockするかを明記 |
| 記録・送信・GPU完了まで同期的に実行 | ExecuteAndWait(context) |
| GPU完了をCPU threadを占有せず待つ | execution.WaitForCompletionAsync(cancellationToken) |
| frame枠などを非同期に待って送信 | 本当に待機する経路としてSubmitAsyncを提供する場合だけ、この名前を使う |

completion待機のキャンセルは、送信済みGPU処理の取り消しや使用中資源の解放を意味しない。この区別も契約に含める。

GpuRenderGraphExecution.Disposeは非同期資源をretireできるが、PreparedDisplayList.Disposeはbufferを即時破棄する。したがって両方をusingで囲んでも、非同期submissionの完了前にscopeを抜けてよいとは限らない。[Execution.Dispose](E:/Lumyte/src/graphics/Lumyte.Graphics/RenderGraph/GpuRenderGraphExecution.cs:63)、[PreparedDisplayList.Dispose](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/PreparedDisplayList.cs:62)

標準経路は、frame/executionが使用中のGPU世代をleaseし、完了後に解放する設計にする。単にmanaged objectを参照保持するだけでは、明示Disposeや同じbufferへの書換えを防げない。借用する低レベル経路も残し、ownerと完了tokenを明示する。

<a id="u02"></a>

#### U02 / 最優先: 共通APIの契約をbackend非依存にする

Graphics.Library.AddDrawはworld/view-projection行列を128-byte root dataに書き、SetRootDataを必ず呼ぶ。WebGPU実装のSetRootDataはNotSupportedExceptionになる。backend非依存に見える上位機能が、raster対応backendでも成立しない。[AddDraw.Record](E:/Lumyte/src/graphics/Lumyte.Graphics.Library/DrawRenderGraphExtensions.cs:129)、[WebGPU](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuCommands.cs:340)

**改善方針: 共通APIが受け付ける機能・値の範囲を全対応backendで保証し、利用者はbackend別の分岐を書かずに同じコードを実行できるようにする。** 共通の上限、型、shader ABI、validationを先に定め、実装差はbackend内部で吸収する。共通範囲を超える機能は明示的に選ぶ拡張として扱う。共通経路の完了条件は、対応判定の追加ではなく実際の動作保証とする。以下は改善方針であり、現在の実装済み契約ではない。

| 対象 | 採用する方針 |
| --- | --- |
| root data | raster/computeとも最大64 bytes、4-byte単位に統一。現在の128-byte契約を縮小し、backendの上限が大きくても共通APIの上限は増やさない |
| 64 bytesを超える入力 | 共通のparameter bufferと論理bindingで渡す。上位APIがupload、binding、graph依存、GPU完了までの寿命を管理する |
| attachment、MSAA、format/usage、binding個数 | 対象環境すべてで保証する組合せと上限を固定する。実装を揃えるか共通descriptionを制限し、範囲外は共通validationで同じ段階・理由で拒否する |
| shaderの入力・state | 同じ論理宣言と意味を維持し、shader出力、native binding、動的stateとpipeline stateの違いはbackend/toolchain内部で変換する |
| capabilities/実device limits | backendの初期化・内部最適化・診断・明示的な拡張に使用する。標準描画の呼出し側に問い合わせや分岐を要求しない |

root dataの64-byte制限はAPI側の共通契約である。WebGPUにはImmediatesが追加され、maxImmediateSizeの仕様既定値も64 bytesだが、64 bytesに制限するだけで旧runtimeや採用中のSilk.NET/WGPUが対応するわけではない。backendは必要なbindingとcompilerの対応を検証し、Immediatesを使えない環境ではuniform buffer + dynamic offsetで同じ64-byte契約を提供する。方式選択を利用者に公開する必要はない。[Chrome公式のImmediates解説](https://developer.chrome.com/blog/new-in-webgpu-149-150#immediates)、[WebGPU limits](https://gpuweb.github.io/gpuweb/#limits)、[WebGPU buffer binding layout](https://gpuweb.github.io/types/interfaces/GPUBufferBindingLayout.html)

既存AddDrawのWorldとViewProjectionは合計128 bytesなので、両行列を共通parameter bufferへ移す。root dataには必要な論理index/offsetなど64 bytes以内の小さい入力を置き、DrawTransformsを渡す利用者のbackend非依存な使い方を保つ。Worldを個別に使うshaderもあるため、移行時に無条件で1つの行列へ合成しない。合成済み行列だけを使う専用shaderは、別途明示した入力契約で64 bytesへ収められる。[現在の行列入力](E:/Lumyte/src/graphics/Lumyte.Graphics.Library/DrawRenderGraphExtensions.cs:146)

実装工程と完了条件:

1. 対応対象の最低device/runtime要件と共通の機能・値の範囲をADRに定義する。backend名による違いが残るdescriptionや操作を棚卸しし、共通化、内部変換、共通制限、拡張への分離のいずれかに決める。共通要件を満たさない環境はdevice初期化時に診断する。
2. RootDataSizeを64へ変更し、共通validation、shader ABIのversion/hash、DXIL/SPIR-V/WGSL生成、pipeline layoutを一緒に更新する。配置、行列layout、短い書込み、pass開始・pipeline切替時の状態を共通契約にする。旧ABIのshader packageはpipeline作成時に明確に拒否し、再生成手順を示す。
3. 共通parameter bufferとその所有権を実装し、AddDraw、shader、サンプルを移行する。root dataとparameter bufferをVulkan、DirectX12、WebGPUのraster/compute双方に接続する。数値の上限だけを変えて既存AddDrawを失敗させる状態で工程を終えない。
4. backend内部の転送領域とbind groupを再利用する。uniform経路はminUniformBufferOffsetAlignmentに従い、各入力のsnapshotをGPU完了まで保持する。同じ領域の上書きによる値の混同、drawごとのbuffer生成やGPU待機を避ける。公開データサイズ64 bytesと内部allocationのstrideを区別する。[WebGPU dynamic offsets](https://gpuweb.github.io/gpuweb/#dom-gpubindingcommandsmixin-setbindgroup)
5. 共通xUnit consumer/conformance testで、4/64 bytesの成功、68/128 bytesの共通validationによる拒否、不正な単位、短い書込みの契約、異なる入力の複数draw/dispatch、pipeline切替、複数submissionの寿命を確認する。移行したAddDrawは両行列の意味を維持することを確認する。単なる例外の有無ではなく描画結果とcompute出力を検証し、浮動小数点差の許容条件も共通化する。
6. 標準サンプルはbackend生成・ホスト接続だけを差し替えて3backendで実行する。描画・computeコードにbackend名の分岐やcapability判定を足さず、共通機能に関するNotSupportedExceptionが起きないことを受入条件にする。Browserは完成時に同じ契約と試験を適用し、未実装の現時点で対応済みとは扱わない。

GPUアドレス、明示placement、aliasingなど、全backendで同じ意味を保証できない操作は明示的な拡張に置く。標準のLibrary/TwoD/Textからその拡張を利用者に要求しない。共通契約は機能と意味を保証するものであり、同一の実行時間や内部メモリ量を保証するものではない。

<a id="u03"></a>

#### U03 / 高: 同じresourceの情報を二重に宣言させない

DrawMaterialはGpuResourceTableとDrawSampledTexture[]/DrawShaderBuffer[]を別々に受け取る。tableはshaderのdescriptor、配列はgraphの依存計画を表すが、利用者は同じ資源の対応を2箇所で維持する必要がある。[DrawMaterial](E:/Lumyte/src/graphics/Lumyte.Graphics.Library/DrawMaterial.cs:8)

tableは後から変更できる一方、materialの依存配列は構築時のcopyなので、slotだけ差し替えると対応がずれる可能性がある。現在のshader binding解決は明示descriptorをそのまま使うため、同じ長さ・slot番号であることだけでは意味の一致を保証できない。[bindings.Resolve](E:/Lumyte/src/graphics/Lumyte.Graphics/RenderGraph/GpuRenderGraphShaderBindings.cs:50)

上位APIでは「slot、view/range、stage、access」を1件のbindingとして渡し、native tableとgraph依存を生成する。Materialはimmutableなbinding集合か、revision付きの明確な更新APIを持つ。低レベルGpuResourceTableの直接編集は残す。

targetも、TwoD.RenderTargetはtexture handle＋description、Library.DrawRenderTargetはview＋description、graph内ではGpuRenderGraphTextureとなる。用途差はあるが、標準のtarget adapterを用意して同じdescriptionを繰り返し渡さずに済むようにする。[TwoD target](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/RenderTarget.cs:3)、[Library target](E:/Lumyte/src/graphics/Lumyte.Graphics.Library/DrawRenderTarget.cs:3)

<a id="u04"></a>

#### U04 / 高: 簡単な描画の前後に必要な組立てを減らす

現在の2D描画例。backend、renderer、RenderTarget型のtargetは初期化済みという前提の使用断片である。

~~~csharp
using CommandEncoder encoder = renderer.CreateCommandEncoder();
encoder.FillRectangle(new(8, 8, 120, 32), Brush.Solid(Color.White));
DisplayList displayList = encoder.Finish();

using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
var graph = new GpuRenderGraph();
graph.AddTwoD("ui", renderer, prepared, target);
using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
~~~

FillRectangle自体は十分読みやすい。負担は周囲のencoder/display list/prepared/graph/executionにある。継続描画ではcache、arena、retirement queueも必要になる。既存Vulkanサンプルの起動にもunsafeなsurface/extension操作が現れる。[Renderer](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/Renderer.cs:58)、[サンプル初期化](E:/Lumyte/samples/graphics/Lumyte.Graphics.Vulkan.Samples/Program.cs:24)

上位にRenderContext/Frame相当を設け、device・plan cache・allocator方針・retirement・presentation adapterを一度だけ構成する。その上で通常描画、prepared drawingの再利用、独自graph passの追加を選べる入口にする。既存の低レベルAPIは引き続き独立利用可能にする。

省略するのは毎回同じ配線であり、アプリケーションが必要とする所有権や明示的なmemory placementまで隠さない。フレームごとに多数のwrapper/closureを作る設計にも注意する。

<a id="u05"></a>

#### U05 / 高: Resourcesの「参照」と「保持」を入口で説明する

| 現行の型 | 何を表すか | 誤解しやすい点 |
| --- | --- | --- |
| AssetKey<T> | canonicalな取得キー | 読込済み資源そのものではない |
| ResourceId<T> | store内のtyped slot | 単体で所有・保持せず、storeと組み合わせる |
| ResourceHandle<T> | 最新世代に追従する参照 | LoadAsyncの戻り値を保持していても、資源を保持するpinにはならない |
| ResourcePin<T> | assetをロード状態に保つ | 固定世代のleaseではない |
| ResourceScope | 複数assetと依存のロード状態を保つ | scope解放後のunloadはoptionsに依存 |
| ResourceSnapshot | その時点のロード済み世代全体を保持 | 1資源を読むためでも他の世代を保持する |
| ResourceLease<T> | 特定の1世代を保持 | 現在の取得入口はsnapshot.Lease |

この区別は能力として有用である。型を1個に統合する必要はない。ただしクイックスタートではscope.LoadAsyncを標準経路として示し、直接store.LoadAsyncはborrowedな参照であると説明したい。[Handle](E:/Lumyte/Lumyte.Resources/ResourceHandle.cs:3)、[Scope](E:/Lumyte/Lumyte.Resources/ResourceScope.cs:3)、[Snapshot](E:/Lumyte/Lumyte.Resources/ResourceSnapshot.cs:3)

単一の現在世代を固定したい場合に、全資源のsnapshotを経由しないLease取得APIを検討する。ValueとGenerationを別々に読むとhot reloadをまたぎ得るため、同じ世代を必要とする処理はlease/snapshotを使うことも明示する。

default(ResourceHandle<T>)にはstoreがなく、TryGetValueでもNullReferenceExceptionになる経路がある。公開value型としてIsValidとTry系の失敗契約を整え、破棄後・未初期化・未ロードを区別すると扱いやすい。[TryGetValue](E:/Lumyte/Lumyte.Resources/ResourceHandle.cs:26)

<a id="u06"></a>

#### U06 / 高: CommandEncoderの状態・clip・layerを一貫した設計に作り直す

CommandEncoderにはSave/Restore、PushClip/PopClip、PushLayer/PopLayerの3系統がある。Saveが保存するのはStateで、activeClips/activeLayersは別管理。Clip(Rect)は変換後boundsでstate clipを更新し、PushClip(Rect)は変換された矩形の正確なclipをstackに積む。名前が近くても効果・復元方法が違う。[Save/Restore](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/CommandEncoder.cs:60)、[PushClip](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/CommandEncoder.cs:90)、[Clip](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/CommandEncoder.cs:133)

「Save → PushClip → Restore」でclipが解除されるとは限らず、Finishまで残れば未解放clipとして例外になる。**改善はCommandEncoderの公開APIと状態モデルの再設計として実施する。** コメントの補足や既存の3系統をそのまま包むscopeの追加だけでは完了としない。変換・clip・layerの適用範囲と復元順序が、同じ入れ子規則で決まる設計に変更する。以下は未実装の設計方針である。

| 対象 | 新しい設計の契約 |
| --- | --- |
| 状態の境界 | 変換、正確なclip、合成先layerを1つの論理的なscope階層で管理する。すべてのscopeは開始時の描画状態を保持し、終了時にその状態へ戻る |
| 公開API | BeginState、BeginClip、BeginLayerなどscopeを返す入口を標準にする。名前は仮案だが、usingによる終了と単一のLIFO規則を共通にする。呼出し側が3種類のSave/Popを対応付ける構造を解消する |
| clipの意味 | 矩形とpathは同じscope契約で扱い、設定時の変換を適用した正確な形状で親clipと交差させる。変換後の外接矩形は内部のculling/scissor最適化に使い、公開のclip形状を勝手に広げない |
| layerの意味 | layer終了は子の描画を確定して親へ合成する操作とし、描画状態の復元と一緒に行う。layer外のclipは合成境界、内側のclipは子の描画に適用する。境界を記録して二重適用や外側への漏れを防ぐ |
| 変換の意味 | 置換と合成をAPI名で区別し、行列の合成順序とclipを固定する時点を共通契約にする。scope内の変換変更は終了後に残らない |
| 不正な終了 | 外側を先に閉じる操作は状態を変更する前に拒否する。scopeの重複Disposeやコピーで別のscopeを閉じないよう、所有encoderとscopeの同一性を内部で検証する |
| 記録の終了 | Recording、Finished、Disposedを区別する。Finishはroot scopeで一度だけ成功し、未終了scopeがある場合は状態を変えず拒否する。未FinishのDisposeは記録を破棄し、DisplayListを暗黙に生成しない |

通常のusingによる終了では、途中の描画処理が例外になっても外側の状態へ戻り、元の例外を隠さない。scope終了は既に記録した描画の取り消しを意味しない。記録全体を破棄するときはencoderのDisposeで行う。この違いを状態遷移と実装に反映する。

実装工程と完了条件:

1. 変換・clip・layerの状態遷移と、scope終了時の復元・合成をADRに定義する。現在のSave/Restore、Clip/PushClip、PushLayer/PopLayerの呼出し箇所と描画結果を調べ、移行表を作る。
2. 単一のscope階層と共通の復元処理を実装し、公開scope APIへ接続する。描画状態と合成に必要な内部データは分けても、利用者が別々のstack規則を覚える必要がない構造にする。scopeごとの不要なheap allocationを避け、記録量と入れ子深さに対するコストを確認する。
3. rectangle/pathのclip処理とlayer境界を新しいモデルに揃える。既存の外接矩形clipを正確なclipへ変える箇所は、回転・せん断時の表示差を意図した移行として扱う。単に旧APIを改名して同じ違いを残さない。
4. Library/TwoD/Textの利用箇所、サンプル、テストを移行する。旧APIの廃止を工程に含める。互換期間を置く場合も新モデルに同じ意味で変換できる呼出しだけをadapter化し、変換できない挙動は移行診断を出す。
5. xUnitで、混在した入れ子scopeからの完全な状態復元、例外時の復元、clip内外の描画、変換の置換・合成、終了順序違反時の状態維持、重複Dispose、Finish後の操作をそれぞれ独立に検証する。GPU conformanceでは回転・せん断した矩形clip、path clip、入れ子layerのopacity/blend、clipの合成境界を描画結果で検証する。既存テストの変更は契約変更に対応させ、失敗を隠すために弱めない。

完了条件は、新APIで通常終了・例外終了のどちらでも変換・clip・layerが外へ漏れず、同じサンプルが3backendで期待どおりに描画されること。コメントやサンプルの追加だけでは、この改善を完了扱いにしない。

またAddTwoD(SceneSnapshot, ...)は登録時にsnapshot.Updateを実行する。snapshotはimmutableな撮影結果ではなく更新可能なGPUデータであり、Addという名前の操作がuploadを伴い得る。[AddTwoD](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/RenderGraphExtensions.cs:7)。自動更新を標準経路として隠すなら明記し、明示Updateを使う経路とは重複させない。

<a id="u07"></a>

#### U07 / 中: Textの通常利用と詳細制御を分ける

encoder.DrawText(textRenderer, font, text, baseline, fontSize, brush, options)は便利だが、encoderとtextRendererが同じRendererに属することを利用者が揃える必要がある。TextRendererにも同等のDrawTextがあり、通常の呼出し方が2つある。[拡張method](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/TextDrawingExtensions.cs:10)

string overloadは毎回Shapeし、FontFace.MeasureもShapeする。毎フレーム同じ文字をMeasure→Drawする利用では同じ処理を繰り返す。ShapedTextを再利用する経路はすでにあるので、これを使うUI向けサンプルを優先する。[DrawText](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/TextRenderer.cs:56)、[Measure](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/FontFace.cs:250)

ShapedText.Advanceはfont units、DrawTextのbaseline/fontSizeはlogical pixels、Measureはpixel換算したadvanceとascender-descenderを返す。単位はコメントされているが、Vector2だけではadvance、ink bounds、line heightを区別しにくい。TextMetricsのような名前付き結果、shaped runからのMeasure、共通TextStyleを検討する。

Shape(string)はrun単位でsegment propertiesを推定し、言語・方向・OpenType feature等を渡す公開optionsはない。折返し・配置・fallbackを含むTextLayoutは別の上位責務として追加すると、低レベルshapingの用途を保てる。現在のMeasureのコメントもmulti-line layoutを含まないと明記しているため、現APIを段落layoutとして紹介しない。[Shape](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/FontFace.cs:191)、[ShapedText](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/ShapedText.cs:5)

<a id="u08"></a>

#### U08 / 中: DSLの可読性を保ち、組立てと同一性を明示する

~~~csharp
State<Context> idle = State<Context>("Idle");
State<Context> active = State<Context>("Active");
var definition = Machine<Context, Trigger>(idle)[
    Transition(idle, active, Trigger.Activate)
        .When(context => context.IsAllowed)
];
var machine = definition.CreateInstance(context);
machine.Fire(Trigger.Activate);
~~~

Context、Trigger、contextは利用者が定義し、StateMachineKitをstatic importする前提の断片。definition/instance分離とWhen/Fireは読みやすく、維持したい。

一方、indexerで内容を設定するDSLは、通常の配列参照とは異なる。StateMachineは組立て時にstate/transitionをFreezeするが、ActionMapのcontent設定は配列を置き換える。文法が共通でも、再利用・変更可能性は共通ではない。[StateMachine](E:/Lumyte/Lumyte.StateMachine/StateMachine.cs:17)、[ActionMap](E:/Lumyte/Lumyte.Interaction/ActionMap.cs:21)

identityもInputAction<T>/Stateは参照に基づき、AnimationChannel<T>はrecordの値等価である。同じ文字列IDのInputActionを作り直してGetValueすると、元のactionとは別物なので通常default値が返る。未登録と「登録済みでまだ入力なし」が見分けにくい。[InputAction](E:/Lumyte/Lumyte.Interaction/InputAction.cs:3)、[GetValue](E:/Lumyte/Lumyte.Interaction/ActionRuntime.cs:92)、[AnimationChannel](E:/Lumyte/Lumyte.Animation/AnimationChannel.cs:3)

identityを一律に変えるより、Name/Idが診断名なのかキーなのかを明示し、共有するaction/channelの定義例を示す。必要ならIsRegistered/TryGetValue等を加える。DSLの別名を多数増やすより、組立てを完了する時点とfreeze規則を揃える。

<a id="u09"></a>

#### U09 / 中: RenderGraphの意味と診断を公開APIに載せる

Read/Write/ReadWriteは簡潔だが、Writeが「以前の内容を不要にする全上書き」であることがmethodのXMLコメントから分からない。部分更新でWriteを使うと、以前のproducerのcullingに影響する。Load attachmentにはReadWriteを使う、といった具体例が必要。[PassBuilder](E:/Lumyte/src/graphics/Lumyte.Graphics/RenderGraph/GpuRenderGraphPassBuilder.cs:45)

Record(queue)はtransientなしのgraphで使えるが、同じcallbackがGetTextureView/GetBufferViewを使うとbackend不在で例外になる。つまり「import済みresourceだけなら同じpassをRecordできる」とは限らない。Recordのcontext要件を明記するか、backend/view resolverを渡せる記録経路を設ける。[Record](E:/Lumyte/src/graphics/Lumyte.Graphics/RenderGraph/GpuRenderGraphPlan.cs:50)、[view解決](E:/Lumyte/src/graphics/Lumyte.Graphics/RenderGraph/GpuRenderGraphPassContextView.cs:32)

GetTextureがexecutionではexport済み資源の取得、pass contextでは宣言した資源の解決を意味する点も、GetExportedTextureなどの名前で説明できる。static callback＋明示stateは性能・寿命の利点があるため維持し、基本利用はAddClear/AddTwoD等の上位extensionで短くする。

エラーはpass名、resource名、期待access、実際の状態を含める。例えば現在の「access listに宣言した資源のみresolve可能」という例外では、何をどのpassに追加するかを再調査する必要がある。[RequireResource](E:/Lumyte/src/graphics/Lumyte.Graphics/RenderGraph/GpuRenderGraphPassContextView.cs:98)

## 5. 実装・性能の優先度付き所見

P1 は最初に扱う障害・寿命・容量問題、P2 は API/構造/性能の改善、P3 は計測に基づいて着手する項目。実測していない高速化率やメモリ削減率は約束しない。

### R01 / P1 / 再現済み: HotReload の通知と終了が競合する

OnChanged は lock の外で state を確認し、lock 内で shutdown.Token を取得する。確認後に DisposeAsync が進むと、83行で破棄した CTS にアクセスできる。イベント購読解除だけでは、すでに開始した callback を止められない。今回の全体テストで実際に未処理例外となった。[OnChanged](E:/Lumyte/Lumyte.Resources/ResourceHotReloadManager.cs:86)、[DisposeAsync](E:/Lumyte/Lumyte.Resources/ResourceHotReloadManager.cs:49)

対策は state、work 登録、停止開始の同期範囲を統一し、終了時に全作業と登録途中の callback の扱いを確定すること。debounce で辞書から外した旧 work も drain 対象にする必要があるか確認する。例外を握りつぶす修正では不十分。回帰テストは fake change source と明示的な同期で順序を制御し、実 FileSystemWatcher の試験は integration として分離する。

### R02 / P1 / 静的確認: Vulkan descriptor pool の規模が通常の Scene と合わない

各 descriptor set layout は64 descriptor、pool は各型1024 descriptor固定。SetResourceTable は呼出しごとに set を割り当て、完了まで解放しない。storage buffer だけを使う場合でも、同時に保持できる set は予算上16個。読み取り・書き込み buffer の両 table は同じ pool 型を消費する。pool が不足した場合の増設・再試行経路もない。[layout/pool](E:/Lumyte/src/graphics/Lumyte.Graphics.Vulkan/VulkanDevice.cs:1249)、[pool容量](E:/Lumyte/src/graphics/Lumyte.Graphics.Vulkan/VulkanDevice.cs:1325)、[set割当](E:/Lumyte/src/graphics/Lumyte.Graphics.Vulkan/VulkanDevice.cs:2128)

SceneSnapshot はノードごとに batch を生成し、RecordDraw は batch ごとに table を設定するため、この上限は大規模3Dだけの問題ではない。17回以上の buffer table bind を含む記録を最小再現候補とし、実機と validation layer で確認する。driver が余分な割当を許すことには依存しない。pool の descriptorCount は set 数ではなく descriptor の個数である。[Vulkan仕様](https://docs.vulkan.org/refpages/latest/refpages/source/VkDescriptorPoolSize.html)

まず容量追跡と pool page の増設・fence後の再利用を実装し、次に同一 table の再bindを安くする。単に1024を大きな定数にするだけでは draw 数と frames-in-flight に比例する問題が残る。

### R03 / P1 / 静的確認: 記録中断と部分 submission の後始末が不十分

GpuCommandBuffer に Dispose/Abort がなく、記録 callback が例外を投げた場合に native encoder/allocator/descriptor を回収する共通契約がない。DirectX12 は入力配列を1件ずつ検証しながら ExecuteCommandLists し、最後に signal.Track を呼ぶ。後続要素が不正なら、先行 command は実行済みでも fence の追跡に入らず例外終了し得る。[command契約](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuCommands.cs:72)、[DirectX12 Submit](E:/Lumyte/src/graphics/Lumyte.Graphics.DirectX12/DirectX12Commands.cs:34)

対策は Recording/Finished/Submitted/Aborted の状態・所有権を定義し、全要素の所属・状態を検証してから送信すること。記録失敗と送信後失敗を区別し、送信済み資源を未送信扱いで解放しない。device lost の終了経路もここに接続する。受入試験は「callback が途中で失敗」「同じ buffer の二重提出」「配列の2件目が別device/既提出」の各1挙動を独立して検証する。

### R04 / P1 / 静的確認: device-local ID は別 device の所有権検証にならない

WebGpuDevice は各 instance で nextTextureId/nextResourceId を1から発行する。操作時は辞書に同じ数値があるかを確認しているため、2台の device で同じ順に作ったハンドルを取り違えると、誤った device 上の別資源を操作し得る。エラーメッセージの「another device」はこのケースを検出できない。[ID発行](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuDevice.cs:34)、[CreateTexture](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuDevice.cs:97)、[table検証](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuDevice.cs:282)

公開 handle のビット幅をむやみに増やす前に、device namespace を含む opaque ID、プロセス内一意の発行、検証レイヤー等を比較する。slot 再利用を導入するなら generation も必要。2つの device で同値の local ID を作る回帰試験を用意し、Vulkan/DirectX12 の同種の辞書も点検する。

### R05 / P1 / 静的確認: Text のキャッシュと atlas に長時間利用の方針がない

TextRenderer の distanceFields/polygons/colorBitmaps は追加され続け、クリアは Dispose 時のみ。font、glyph、サイズ、DistanceRange 等が key に含まれる。固定サイズの atlas に空きがなくなると例外になる。Atlas 自体には Release/Collect があるが、TextRenderer の自動 eviction には接続されていない。[cache](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/TextRenderer.cs:21)、[追加](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/TextRenderer.cs:898)、[満杯時](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/DistanceFieldAtlas.cs:163)

CPU bytes、GPU bytes、page 数の予算を定め、LRUとfont単位の無効化、描画中 entry の pin、fence後の退避、atlas page 増設上限、満杯時の描画 fallback を設計する。FontFace 内の outline/path/bitmap キャッシュも同じ予算の可視化対象。日本語・絵文字・複数サイズを継続的に切り替える検証を追加する。

### R06 / P2 / 実装差分: 共通 API のbackend非依存性が保証されていない

RasterPipeline/ComputePipeline フラグだけでは、attachment数、MSAA、root data、binding個数、format usageを判定できない。WebGPU の SetRootData と SetComputeRootData は例外。Vulkan/WebGPU の raster 作成は1 color/1 sampleに制限される。一方、共通descriptionはより広い値を受け取る。[capabilities](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuBackend.cs:4)、[WebGPU root data](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuCommands.cs:340)、[Vulkan raster](E:/Lumyte/src/graphics/Lumyte.Graphics.Vulkan/VulkanDevice.cs:552)

共通APIで受け付ける操作と値を全対応backendで保証する。最大64-byteのroot data、共通parameter bufferへのAddDraw移行、shader ABI更新、共通validation、WebGPU内部の転送方式を含む実装・受入条件は [U02](#u02) に集約する。ここで挙げたattachment、MSAA、format/usage、binding個数も同じ共通契約の棚卸し対象とする。

### R07 / P2 / 実装差分: WebGPU Native と Browser の完成度を区別する

現 backend は Silk.NET native WebGPU/wgpu と DevicePoll を利用する。Create は adapter/device callback が呼出し直後に完了したかを確認する。Browser プロジェクトは native backend の参照のみで、JS interop、canvas、非同期初期化の実装はない。[WebGpuDevice.Create](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuDevice.cs:57)、[Browser.csproj](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU.Browser/Lumyte.Graphics.WebGPU.Browser.csproj:1)

Browser が必要な場合は、非同期 factory、queue completion、readback、device lost、canvas resize/presentation、WASM publish を独立した工程にする。WebGPU のブラウザ契約では adapter/device の取得は Promise であり、adapter の features/limits を問い合わせる。[GPU仕様](https://gpuweb.github.io/types/interfaces/GPU.html)、[GPUAdapter仕様](https://gpuweb.github.io/types/interfaces/GPUAdapter.html)

64 logical slot は現在の変換器の上限であり、すべてのdeviceが各種類64個を同時利用できる保証ではない。R06で種類・stageごとの共通上限を定め、shader buildと共通validationで検証する。deviceが共通の最低要件を満たすかはbackend初期化時に確認する。Browser完成時も同じ共通契約を適用する。また、WGSL の group/binding 書換えは正規表現によるため、対応入力を Slang の出力に限定して明示するか、build時のreflection/変換に移す。[binding変換](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuCommands.cs:104)

### R08 / P2 / 静的確認: descriptor 管理が draw 数と table instance 数に比例する

DirectX12 は SetResourceTable ごとに shader-visible heap を作成する。Vulkan は set と descriptor 更新を繰り返す。WebGPU には cache があるが key は table オブジェクトとlayoutで、同じ内容でも別instanceは別entry。cache に容量制限はなく、資源/view/pipeline破棄時は全件無効化する。新しい table を毎フレーム作り、資源を長く保持する利用では cache が増え続ける。[DX12 heap生成](E:/Lumyte/src/graphics/Lumyte.Graphics.DirectX12/DirectX12Commands.cs:444)、[WebGPU cache](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuDevice.cs:247)、[全件無効化](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuDevice.cs:643)

Native は device所有heap/poolのpageとfence retirement、descriptorの世代/dirty範囲を管理する。WebGPU は table の再利用または内容を表す安定key、上限付きcache、依存resource単位の無効化を検討する。ハッシュだけで同一性を判断しない。cache hit/miss、生成回数、保持bytesを可視化する。

### R09 / P2 / 静的確認: 2D/Text の準備処理で CPU-GPU を同期している

Renderer の path compute preparation、DistanceFieldRasterizer の glyph生成、ColorBitmapTexture の upload は submit後に Wait する。新しいpath/glyphが現れるフレームでCPUが止まり得る。DirectX12のWaitはThread.Yieldによるpoll loop。[path準備](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/Renderer.cs:622)、[glyph描画](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/DistanceFieldRasterizer.cs:276)、[bitmap upload](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/ColorBitmapTexture.cs:133)、[DX12 wait](E:/Lumyte/src/graphics/Lumyte.Graphics.DirectX12/DirectX12Commands.cs:65)

upload→compute preparation→draw を同じ graph/queue の依存として記録し、staging/view を token 完了まで保持する。[U01](#u01) の送信・待機・解放の契約へ移行し、既存retirement queueを土台にする。同期readback APIは明示的な用途として残し、通常描画と分ける。Nativeの必要なCPU待機はイベント通知型へ。待機回数と待機時間を計測する。

### R10 / P2 / 静的確認: Scene の dirty 更新は GPU upload の一部に限られる

Scene.Capture は全nodeの投影・sort・配列化を毎回実行する。SceneSnapshot.Update も List/Dictionary/HashSet を作り直し、nodeごとにbatchを作る。GPU uploadが0でもCPU処理・allocation・draw数は減らない。NodeStrideは256 bytes、16,384 slotで4 MiBのbuffer容量になる。[Capture](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/Scene.cs:81)、[Update](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/SceneSnapshot.cs:44)

内容dirty、可視性/clip dirty、順序dirtyを分け、変更がなければ Update を終了できるようにする。描画順を変更したときだけsortし、隣接する互換batchをまとめる。データstrideとbinding offset alignmentを同一視せず、インスタンス配列＋index方式を評価する。重なり順を壊す並べ替えはしない。

### R11 / P2 / 改善候補: arena の利用範囲と非同期所有権を揃える

TwoD の OwnedBuffer/OwnedTexture は各資源で backend.AllocateMemory を直接呼ぶ。保持型Sceneの容量増加では旧bufferを即時Disposeする。Graphが export/import lease を持つ一方、これらの外部所有資源は利用者がGPU完了まで維持する契約に依存する。[OwnedBuffer](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/OwnedBuffer.cs:49)、[OwnedTexture](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/OwnedTexture.cs:26)、[Scene再確保](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/SceneSnapshot.cs:51)

allocator注入、upload ring、device-local persistent data、tokenに紐づくretirementを用意する。非同期描画中のUpdate/Disposeは、lease、frame別buffer、または明示的な利用制約のどれで保証するか決める。ここはクラッシュを再現した所見ではなく、非同期化を広げる前の必須設計条件である。

### R12 / P2 / 改善候補: 残る RenderGraph・shader・font の allocation

Graphの構造cacheは既に有界で完全比較も行う。一方、Executeはnative memory requirementsを再照会し、physical memory plan、resource runtime、Dictionary/List、retirement用closureを作る。構造cacheとdevice依存plan cacheは別問題である。[Execute](E:/Lumyte/src/graphics/Lumyte.Graphics/RenderGraph/GpuRenderGraphPlan.cs:109)、[memory plan](E:/Lumyte/src/graphics/Lumyte.Graphics/RenderGraph/GpuRenderGraphMemoryPlan.cs:43)

さらにShaderArtifact.Payload/AbiHashはgetterごとに配列をコピーし、WebGPU pipeline作成はPayloadを複数回取得する。FontFaceはfont全体をコピーしてpinするため、同じfontのvariation/faceを増やすとデータが重複する。[shaderコピー](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuShaderPackage.cs:57)、[fontコピー](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/FontFace.cs:57)

構造＋device世代＋descriptionに対するrequirements/physical plan cache、execution scratchの再利用、単一ownerに基づく読み取りAPI、共有FontData ownerを比較する。borrowed spanを採用する場合は寿命をAPIで保証する。shaderコピーは通常ロード時なので、draw・glyph missの問題より優先度を下げる。

### R13 / P2 / 静的確認: DevTools の host切替が旧hostを再選択し得る

selectHost(id) は setHostIdState(id) の直後に現在のclosureの connect(true) を呼ぶ。connect は変更前の hostId を使って候補を選ぶため、A接続中にBを選んでもAに接続し直す経路がある。generation の確認も非同期処理の一部だけなので、古いrefreshが新しいhostの表示を上書きしない保証を加える。[useDevTools](E:/Lumyte/Lumyte.DevTools.Server/ClientApp/src/state/useDevTools.ts:8)

connectに希望hostを明示的に渡し、全async結果の反映前に世代を確認する。2hostのfake transportで選択先・購読先・表示先がBになる挙動を検証する。現在の密集したソース表記も、接続状態・購読・snapshot・操作履歴の単位で整形・分割する。

### R14 / P3 / 改善候補: Input/Resources/DevTools の負荷を計測してから局所改善

ActionRuntime は contributions をobjectで保持し、値更新で全contributionを走査する。float/Vector2のboxingとaction数に応じた走査が候補。ResourceStore.CollectAsyncは候補を全件抽出・sortする処理を、依存解放が進むたびに繰り返す。ResourceLoadSchedulerも選出時にpendingを走査する。[ActionRuntime](E:/Lumyte/Lumyte.Interaction/ActionRuntime.cs:596)、[collection](E:/Lumyte/Lumyte.Resources/ResourceStore.cs:376)、[scheduler](E:/Lumyte/Lumyte.Resources/ResourceLoadScheduler.cs:135)

Action別のtyped slot/index、依存解放のwork queue、lane別のqueueを計測後に検討する。agingや優先度変更を無視した単純heap化は避ける。DevToolsのPublishAsyncはlistenerを順にawaitするため、remote購読が実処理を遅らせる場合は上限付きqueue/coalescingをtransport境界に導入する。[PublishAsync](E:/Lumyte/Lumyte.DevTools/DevToolsHub.cs:134)

### R15 / P2 / 設計改善: CommandEncoderの状態・clip・layerの規則を統一する

CommandEncoderはSave/Restore、PushClip/PopClip、PushLayer/PopLayerを別管理し、Saveで戻る範囲にscoped clipとlayerが含まれない。またClip(Rect)は変換後の外接矩形、PushClip(Rect)は正確な変換後形状を扱う。これらは公開APIと状態モデルを再設計する対象とする。[状態・clip操作](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/CommandEncoder.cs:60)、[Finish](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/CommandEncoder.cs:294)

公開APIと内部状態モデルを作り直し、単一のscope規則、正確なclip、layerの合成境界、記録の終了契約を揃える。旧APIの移行・廃止を含む設計と検証条件は [U06](#u06) に集約する。コメントやwrapperの追加だけでは完了としない。

## 6. 改善工程

工数は1人が実装・レビュー対応・テスト整備を行う場合の粗い人日。既存APIの互換性、対象GPU/OS/ブラウザの範囲が未確定なため見積りには幅を持たせる。日程の約束ではなく、各工程の完了時に見直す。

| 工程 | 目安 | 依存 | 成果物と完了条件 |
| --- | ---: | --- | --- |
| E0: 再現・基準線 | 2–3人日 | なし | R01の競合、17以上のtable bind、recording例外、2deviceのID、2host切替を個別に再現。テスト分類とCPU/GPU計測シナリオを記録 |
| E1: 正しさと寿命 | 5–9人日 | E0 | R01/R03/R04/R13を修正。Vulkan pool容量対策を先行。callback失敗・誤使用でリーク/追跡外submissionなし。全体テスト成功 |
| E2: backend非依存の共通API | 6–10人日 | E1 | U01/U02/U09の非同期・寿命・共通上限・state・graph契約をADR化し、64-byte root data、parameter buffer、shader ABI、AddDrawを実装・移行。共通範囲外は共通validationで拒否。同じ標準描画・computeコードがbackend別の分岐なしで3backendで成功する |
| E2a: CommandEncoder再設計 | 3–5人日 | E2 | R15の単一scopeモデル、正確なclip、layer境界、記録の終了契約を実装。旧APIと利用箇所を移行し、通常・例外終了で状態が漏れず、3backendの描画結果が契約を満たす |
| E2b: 利用者向けAPIの統合 | 要見積り | E2、GPU所有権はE4 | U03/U04/U05/U09のbinding一元化、RenderContext/Frame・presentation adapter、resource lease/default handle、graphの記録・export入口を実装。標準consumerが公開APIだけで安全に描画・資源管理できる |
| E3: 配置と依存の整理 | 3–6人日 | E2 | src/tools/samples配置統一、RenderGraph抽出、shader containerとIR境界、DevTools.Protocol分離。runtimeにcompiler/server依存が入らないことを検証 |
| E4: descriptorとGPU実行 | 6–10人日 | E1/E2 | pool/heap page再利用、WebGPU有界cache、upload/compute/draw統合、U01の送信・非同期待機・lease/retirementを実装。1k/10k batchと複数frameで容量枯渇せず、生成・待機回数とGPU完了までの寿命を検証 |
| E5: 2D/Textの再利用とメモリ | 5–9人日 | E4のretirement契約、上位接続はE2b | font共有、atlas予算/eviction/fallback、Scene dirty/sort/batch、allocator接続に加え、U07のTextMetrics・ShapedText再利用・通常描画の入口を整備。長時間利用で予算内に収束し、表示内容を維持 |
| E6: その他hot pathと配布 | 3–5人日 | E0/E3、API公開例はE2a/E2b/E5 | Interaction/Resourcesを測定して改善。U08/U09のidentity・freeze・graph診断、各APIの独立consumer/Quick Start、CI、SDK/toolchain固定、runtime配布、performance記録を整備 |
| E7: Browserの完成（必要な場合） | 8–15人日 | E2/E3、E4の非同期契約 | JS/WASM adapter、非同期初期化・completion/readback、canvas表示、device lostを実装。E2と同じ共通契約・サンプルでbrowser conformanceを実行 |

既存工程の概算はE0–E6（E2aを含み、E2bを除く）で約33–57人日、Browserを含めて約41–72人日。API評価の統合で追加したE2bは別途見積りが必要であり、E2/E4/E5/E6へ追加したAPI契約・実装・公開例の工数も棚卸し後に見直す。この数値を統合後の全作業の総額として扱わない。最初に予算を確保すべき単位はE0/E1。E3の配置変更とE2a/E2b/E4の実装変更は同じファイルを触りやすいため、同時進行するなら変更範囲を分ける。

### API改善と工程の対応

API評価の改善順序を工程表へ対応付ける。契約をE2で確定し、所有権と実行の実装をE4、上位APIをE2b、2D/Textの改善をE5、診断・サンプル・配布をE6へつなぐ。

| 順序 | API所見 | 工程 | 完了条件 |
| --- | --- | --- | --- |
| 1 | [U01: 非同期と破棄](#u01) | E2で契約、E4で実装、E2bで標準経路に接続 | 送信・CPUを占有しない待機・GPU完了・解放をAPI名と型で区別でき、使用中の資源がGPU完了まで保持される |
| 1 | [U02: backend非依存](#u02) | E2、BrowserはE7 | 同じ標準描画・computeコードが3backendで成功し、共通範囲外は共通validationで拒否される |
| 2 | [U03: bindingの一元化](#u03) | E2b、descriptor再利用はE4 | slot・view/range・stage・accessを一度宣言すれば、resource tableとgraph依存が一致する |
| 2 | [U05: Resourcesの保持](#u05) | E2b、診断と例はE6 | scope/leaseで必要な資源・世代を保持でき、単一世代の取得に全体snapshotを要求しない。default handleのTry系契約も揃う |
| 2 | [U06: CommandEncoder再設計](#u06) | E2a | 変換・clip・layerが単一のscope規則で復元され、通常終了・例外終了のどちらでも外へ漏れない |
| 3 | [U04: 描画の組立て](#u04) | E2b、Browser接続はE7 | RenderContext/Frameとpresentation adapterで、標準描画のnative pointer操作や毎回のcache/retirement手配が不要になる |
| 4 | [U07: Textの入口と再利用](#u07) | E5、サンプルはE6 | rendererの所属を揃える負担を減らし、TextMetricsとShapedText再利用を標準の使用例で扱える |
| 4 | [U08: DSLと同一性](#u08) | E6 | 組立て・freeze・identityの規則と未登録時の挙動を公開APIとconsumer sampleで確認できる |
| 5 | [U09: RenderGraphの意味と診断](#u09) | E2で契約、E2bで入口を実装、E6で診断と公開例 | Read/Write/ReadWrite、Recordのcontext、exportの意味が明確になり、pass/resource名から修正箇所が分かる |

### 最初の PR 群

1. ResourceHotReloadManager の停止競合修正＋決定的な回帰テスト。
2. Vulkan descriptor pool の容量管理と増設＋繰返しbind試験。
3. CommandBuffer のabort/所有権とsubmission全件事前検証＋異常系試験。
4. deviceをまたぐID検証＋2device試験。
5. DevTools host選択の明示化＋hookの挙動テスト。
6. backend非依存の共通上限・64-byte root data・parameter buffer・lifetimeのADRと、同じconsumerを3backendで実行する試験計画。
7. root data、shader ABI、WebGPUの転送、AddDrawを一貫して移行するPR。64-byte境界、既存の2行列、複数draw/dispatchを共通conformanceで検証。
8. CommandEncoderのscope/state/clip/layerを再設計し、旧APIと利用箇所を移行するPR。例外時の状態復元とclip/layerの描画結果を回帰テストで検証。
9. 配置だけを変更するPR。その後、RenderGraph・shader・Protocolの抽出を各PRに分離。

各バグ修正にはxUnit回帰テストを付ける。Frontendの挙動は既存Vitest/Testing Library経路で検証する。生成物全体のsnapshotやCSS class/属性順によるchange-detectorテストを増やさず、observable behaviorを検証する。

## 7. 評価方法と受入条件

### 7.1. 性能・メモリ

既存 [Benchmarks README](E:/Lumyte/Lumyte.Benchmarks/README.md:27) は2026-09-03のShortRunとして、RenderGraph cache hit 4.105 µs / 6.42 KB、8-pass record 114.6 ns / 56 Bを記録している。今回再計測した値ではない。no-op recorder/immediate backendによるCPU計測なので、実機GPU描画全体の性能を示さない。

| シナリオ | 主な計測値 | 初期の受入条件 |
| --- | --- | --- |
| 静止Scene: 1k/10k/100k node | Update時間、allocated B/frame、upload bytes、draw/table数 | 無変更時upload 0を維持し、全件sortとbatch再生成を回避。変更1%時は変更量に応じて増える |
| 多数batch: 16/17/64/1k/10k | descriptor作成数、pool page、cache hit、CPU record時間 | fixed pool境界を超えて成功。frames-in-flight増加時も上限と回収が説明できる |
| 新規glyph/絵文字/CJK | hit/miss、raster/upload時間、Wait回数、atlas使用率 | 定常cache hitでGPU待機を増やさない。満杯時に制御されたfallback/evictionが働く |
| フォント切替・variable font | managed heap、pinされたbytes、font共有bytes | 同一font dataの複製を避け、予算・所有者・回収条件を表示できる |
| Graph反復/resize | compile hit/miss、requirements照会、物理plan作成、peak GPU bytes | 同じdevice/構造で不要なnative照会を削減。resize後は必要なcacheだけ無効化 |
| Resource load/reload/collect | queue長、lock待ち、依存数、収集時間、保持bytes | 1/100/10k件の規模別に計測し、キャンセル/優先度/依存寿命の既存挙動を維持 |
| Input/Animation | allocated B/update、1/4player、binding/channel数別CPU時間 | 経路別のbaselineを作り、boxing等の削減効果を確認してから採用 |
| 長時間実行 | managed/native/GPU bytes、retired bytes、frame p50/p95/p99 | 解放後の利用量が定常上限へ戻り、cacheやdescriptorが単調増加しない |

最初から「全描画0 allocation」「一律30%高速化」を目標にしない。cold start、cache hit、cache miss、resize、device lostを分ける。CPUはBenchmarkDotNet、GCはallocation profiler/counters、GPUは各backendのtimestamp・validation・frame captureで測る。ハードウェア、driver、Release設定、解像度、入力、warmupを記録し、時間比較は同一環境で行う。

### 7.2. APIの使いやすさ

人に試してもらうときは、次の課題を新規consumer projectから実行してもらう。

1. 矩形・画像・日本語テキストを表示する。
2. 毎フレーム同じ文字を再利用し、1個のScene nodeを動かす。
3. 2Dの間に独自compute/raster passを追加する。
4. 非同期描画後にprepared dataを差し替えて、安全に終了する。
5. resourceをscopeで読み込み、reload後も特定世代を処理する。
6. 入力actionでanimation/state transitionを起動する。
7. backend生成・ホスト接続だけを差し替え、同じ描画・computeコードを別backendで実行する。共通範囲の入力には追加のcapability判定や代替コードが不要であることを確認する。
8. CommandEncoderで変換・矩形/path clip・layerを入れ子にし、scope内で例外が起きても、外側の描画が元の状態で継続できることを確認する。

初回成功までの時間、必要な概念数、書き直した箇所、実行時例外、実装ソースを読んだ回数を記録する。性能も同時に確認し、短くなったコードが毎フレームのallocation・同期を増やしていないことを検証する。

既存のconsumer/conformance testを基盤にし、テスト専用helperを知らずに使えるQuick Startを作る。コード例は独立consumerとしてcompileする。公開例だけで標準用途を完結できることを確認し、DESIGN文書は内部理解の補助として位置付ける。

## 8. テスト・ビルド・配布の整備

- 純粋なunit、OS integration、GPU conformance、browser、performanceを明示して実行する。現在のCategory traitと共通test sourceは活用できる。GPU無しのCIで失敗を隠す自動returnや無条件skipを増やさない。
- リポジトリ内にCI定義・global.jsonが見当たらない。SDK、Slang、Node、package lockの再現条件と、Windows/Linux/browserで実行するjobを明文化する。外部CIの有無は今回確認していない。
- native conformanceでは小さい画像の正しさだけでなく、容量境界、失敗途中、再利用、複数device、複数frame、resizeを追加する。実画像全体を固定snapshotにせず、必要な画素領域・描画結果・資源寿命を検証する。
- clean build、incremental build、publish、pack後のconsumer buildを分ける。Shader.Offlineはbuild toolingとして配り、slang import/includeとtool version/optionsの変更がincremental inputsに入るか確認する。
- frontendは既存のlint、型検査、VitestをCIで実行する。csprojのbuildだけでfrontendの挙動テストが通ったと扱わない。
- API migrationでは、backend生成・ホスト接続だけを差し替える共通consumer sampleを先に作る。64-byte root dataとparameter buffer、syncとasync、所有と借用の使い方を説明する。native固有機能は明示的な拡張の例に置き、共通サンプルは内部の転送方式を選択しない。

## 9. 当面保留するもの

全領域の細かいinterface化、全classの別assembly化、全面的なECS化、マルチqueue、全面的なunsafe/pooling化、native pointerを全backendで共通化する設計は、この段階では採用しない。現在の正しさ・利用規模・測定値に対して必要性を判断する。

No Graphics API由来の責務分離とWebGPUの明示的な互換経路は維持する。その上で、寿命・対応機能・容量を契約として揃え、同じ意味の描画をより少ない割当・binding・待機で実行できる状態を目指す。

## 検証補記

本書のテスト結果は初回の構造・性能レビュー時に実行したもの。API評価、方針の改訂、本書への統合は文書のみの変更であり、製品コードの変更やテストの再実行は行っていない。実装変更時にはAGENTS.mdに従い、回帰テストとdotnet test Lumyte.slnxを実行する。

- Frontend型検査: tsconfig.app.json / tsconfig.node.jsonをそれぞれ noEmit、incremental falseで検証し成功。
- ESLint: 成功。
- npmは通常PATH上にないため、同梱Nodeから既存ローカルツールを実行した。
- 最初のVitest起動はsandboxのchild process制約（spawn EPERM）で失敗。制限外で再実行し、3ファイル10件が合格した。
- tsc -b は既存tsbuildinfoへの書込み権限で失敗したため、各projectを --noEmit --incremental falseで再検証した。型検査の成功と、build出力の書込み可否は区別している。
