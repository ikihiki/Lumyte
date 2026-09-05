# Lumyte レビューと改善工程

レビュー日: 2026-09-05 / 構造・性能の確認対象: c9b9839 / API評価の確認対象: b2b9eaf / DevTools追加評価の確認対象: 191afeb。共通API・CommandEncoderの方針変更と、MagicOnionを基盤とするリモート編集・ツリー/画像選択・デバッグ・OpenTelemetryの設計案を本書に統合した。

優先すべきなのは、Resources の終了競合、GPU コマンドの失敗時の寿命管理、descriptor の容量・再利用、Text キャッシュの上限である。その後に API の対応範囲を定義し、プロジェクト境界を整理する。既存の arena、RenderGraph、明示的な resource/view の分離を土台に改善する。

本書はプロジェクト構造・性能・メモリとAPIの使いやすさの評価、および改善工程をまとめたもの。製品コードの変更は含まない。全53プロジェクトの定義・依存関係を確認し、主要な公開 API、実行経路、寿命管理、テスト、ベンチマークを重点的に読んだ。すべてのメソッドを網羅する監査ではない。性能の指摘は、今回計測した数値と、ソースから判断した改善候補を区別する。

DevToolsは[第6章](#devtools-design)で、MagicOnionを基盤としたゲーム側の編集サービス、エディタとの状態同期、[OpenTelemetryによる観測](#devtools-opentelemetry)を設計する。TypeScriptはエディタ内部の実装として扱う。[D0–D6の工程](#devtools-roadmap)のうち、D3の「UI・シーンのobjectを[ツリーと画像の両方](#devtools-selection)から選択・編集し、再接続後も結果照会・undoできること」を最初の実用的な到達点にする。

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
| DevTools | Hub、Agent、Server、Host を分離済み。ただし Server が Agent の wire contract を参照し、Host にデモと収集機構が混在 | MagicOnionの共有契約をProtocol、ゲーム処理をRuntime、接続をC# Clientへ分離。TypeScript/表示用bridgeはEditor内部へ。UI・シーンの階層/画像選択、OTel、hostingはadapterで接続する。[詳細](#devtools-design) |

行数は bin/obj を除いたプロジェクト配下の C# ソースの概数。リンクされた共通テストは二重加算していない。行数は調査の入口であり、分割理由は依存方向・所有権・変更理由・独立配布で判断する。

推奨する配置:

~~~text
src/
  foundation/   Core, Mathematics, Composition, Generators
  platform/     Input, Platform, Windows, Silk
  interaction/  Interaction, StateMachine, Animation
  resources/    Resources
  graphics/     Graphics, RenderGraph, Shader runtime, 各backend, Library, TwoD, Text
  devtools/     DevTools, Protocol, Runtime, Client, Agent, Server, Hosting, OTel統合
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
| DevTools（現行の監視・domain操作） | 4/5 | 型付きquery/command/event、登録解除をIDisposableで表現 | 登録scopeと最短例に加え、session・schema・ツリー/画像選択・安全な編集・状態同期をC# ClientとRuntimeで提供。リモートEditor用途は未実装として[別途評価](#devtools-design) |

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

Action別のtyped slot/index、依存解放のwork queue、lane別のqueueを計測後に検討する。agingや優先度変更を無視した単純heap化は避ける。DevToolsのPublishAsyncはlistenerを順にawaitするため、remote購読が実処理を遅らせる場合は上限付きqueueをtransport境界に導入する。coalescingは最新値への置換が可能な観測に限り、編集結果や順序付き差分は[第6章の配信保証](#devtools-design)に従う。[PublishAsync](E:/Lumyte/Lumyte.DevTools/DevToolsHub.cs:134)

### R15 / P2 / 設計改善: CommandEncoderの状態・clip・layerの規則を統一する

CommandEncoderはSave/Restore、PushClip/PopClip、PushLayer/PopLayerを別管理し、Saveで戻る範囲にscoped clipとlayerが含まれない。またClip(Rect)は変換後の外接矩形、PushClip(Rect)は正確な変換後形状を扱う。これらは公開APIと状態モデルを再設計する対象とする。[状態・clip操作](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/CommandEncoder.cs:60)、[Finish](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/CommandEncoder.cs:294)

公開APIと内部状態モデルを作り直し、単一のscope規則、正確なclip、layerの合成境界、記録の終了契約を揃える。旧APIの移行・廃止を含む設計と検証条件は [U06](#u06) に集約する。コメントやwrapperの追加だけでは完了としない。

## 6. DevToolsをリモート編集・デバッグ基盤へ発展させる

<a id="devtools-design"></a>

確認対象: 191afeb / 2026-09-05。以下は既存コードと公開仕様に基づく設計提案であり、リモート編集・DAP・OpenTelemetry SDKの接続を実装・検証した結果ではない。

**到達点は、エディタから実行中のゲームへ接続し、UI・シーンのオブジェクトをツリーと描画画像の両方から選択・編集し、安全な更新境界で反映し、停止・ステップ実行・診断まで行えること。** DevToolsHubの型付きquery/command/eventを土台に、MagicOnionの共有C#契約でセッション、schema、状態同期、編集transaction、実行制御を提供する。TypeScriptはエディタ側の内部実装とし、DevToolsの公開protocolには含めない。OpenTelemetryはエディタの操作からゲーム内の処理までを結ぶ観測基盤として組み込む。

### 6.1. 現在の強みと、編集基盤にする際の不足

現在の接続は、Browser → WebSocket → Server → MagicOnion / named pipe → Agent → DevToolsHub → 各domainである。Serverはlocalhost、Agentは同一マシンのnamed pipeを使う。別マシン上のゲームへ接続するtransportや、ゲームのWorldを編集する契約はこれからの設計対象になる。[Server起動](E:/Lumyte/Lumyte.DevTools.Server/Program.cs:10)、[Agent接続](E:/Lumyte/Lumyte.DevTools.Agent/DevToolsAgent.cs:39)

型付きの登録、登録解除のlifetime、Resources/Inputの操作例、Activity/Meterの計測点は再利用できる。既存4/5評価は監視・domain操作の入口に対するもの。以下の編集・同期・デバッグ基盤まで完成している評価ではない。

| 所見 | 現在の根拠 | 編集・デバッグに向けた改善 |
| --- | --- | --- |
| DT01 / 最優先: 通信callbackからhandlerへ直接到達する | Agent Receiverがhub.InvokeAsyncを呼ぶ。Hubはhandlerの実行threadやフレーム境界を規定しない。[Receiver](E:/Lumyte/Lumyte.DevTools.Agent/DevToolsAgent.cs:95)、[Hub](E:/Lumyte/Lumyte.DevTools/DevToolsHub.cs:79) | game thread上の適用とsnapshot取得をdispatcherで管理。各domainに任意のlock対応を委ねない |
| DT02 / 最優先: 接続と実行結果の契約が弱い | wire DTOには呼出しIDと結果/文字列errorがあるが、runtime世代、編集revision、重複抑止ID、deadlineがない。[wire契約](E:/Lumyte/Lumyte.DevTools.Agent/DevToolsAgentContract.cs:1) | 再接続・再起動・結果不明を区別し、操作の再送による二重編集を防ぐ |
| DT03 / 高: 送受信の寿命と上限を統一する必要がある | Agentのevent callbackはPublishEventAsyncを非待機で直接呼ぶ。一方で接続ごとに未使用channelのreader taskを起動し、切断時に停止・awaitする処理がない。[Agent](E:/Lumyte/Lumyte.DevTools.Agent/DevToolsAgent.cs:56) | 接続scopeに送信queue、受信、実行中taskを所属させ、終了時にcancel・回収。全queueとpending要求に件数・bytes上限を設ける |
| DT04 / 高: 長い操作が同じ接続の次の要求を塞ぐ | remote WebSocketの受信loopがProcessAsyncの完了を待ち、fragmentも拒否する。[endpoint](E:/Lumyte/Lumyte.DevTools.Server/DevToolsRemoteWebSocketEndpoint.cs:27) | 受信・dispatch・送信を分離し、長い編集/reload中もcancel、status、heartbeatを処理。messageを上限内で再構成する |
| DT05 / 高: browserのcancelがゲームまで届かない | AbortSignalはclientのpendingを除くだけ。Registryのtokenは応答待機を止めるが、Agent Invokeに伝わらない。remote_errorにはretryableが付く。[client](E:/Lumyte/Lumyte.DevTools.Server/ClientApp/src/protocol/transport.ts:8)、[Registry](E:/Lumyte/Lumyte.DevTools.Server/DevToolsHostRegistry.cs:56)、[error](E:/Lumyte/Lumyte.DevTools.Server/DevToolsRemoteJsonSession.cs:148) | 待機キャンセルと編集取消を分け、結果不明の更新を自動再実行しない。operationの状態をゲーム側に照会する |
| DT06 / 高: 型名だけではInspectorを構築できない | discoveryはCLR型名を返し、Agentのcatalogは構築時のsnapshot。更新可能fieldやschema更新の契約がない。[Feature](E:/Lumyte/Lumyte.DevTools/DevToolsFeature.cs:10)、[catalog](E:/Lumyte/Lumyte.DevTools.Agent/DevToolsAgent.cs:25) | 安定したtype/field ID、値型、制約、権限、revision付きschemaを共有C# DTOで提供。TypeScript向けview modelの変換・生成はEditor内部で行う |
| DT07 / 高: snapshotとeventの連続性がない | browserはrefreshしてからsubscribeし、eventにrevision/sequenceがない。途中の更新欠落や旧hostのeventを識別する契約が不足する。[state](E:/Lumyte/Lumyte.DevTools.Server/ClientApp/src/state/useDevTools.ts:5) | snapshotと差分の境界を一体で発行し、gap・再接続時は再同期。R13のhost切替修正も同時に適用 |
| DT08 / 高: protocolの実装差と役割が混在する | local/remoteのJSON sessionが別実装で、交渉・error・fragmentの扱いが異なる。ServerがAgent assemblyのwire契約を参照する。[local](E:/Lumyte/Lumyte.DevTools.Server/DevToolsJsonSession.cs:9)、[remote](E:/Lumyte/Lumyte.DevTools.Server/DevToolsRemoteJsonSession.cs:20)、[参照](E:/Lumyte/Lumyte.DevTools.Server/Lumyte.DevTools.Server.csproj:4) | 公開契約をMagicOnionへ統一し、既存Browser bridgeはEditor内部へ移す。共有Protocol、接続session、UI、ゲーム処理の責務を分離 |
| DT09 / 高: 独自Collectorと標準テレメトリの意味が揃っていない | 全対象ActivityにAllDataAndRecordedを要求し、active Activityを参照保持する。Gaugeも非Histogram経路のCurrent += valueで集計する。属性をすべてstringへ変換する。[Collector](E:/Lumyte/Lumyte.DevTools.Host/DiagnosticsCollector.cs:25)、[集計](E:/Lumyte/Lumyte.DevTools.Host/DiagnosticsCollector.cs:91) | SDKによるsampling・型付き属性・instrument別集計を採用。ライブ表示の保持上限、active記録、exportの責務を分ける |
| DT10 / 高: 階層と画像から同じ編集対象へ辿る契約がない | 現行wireはdomain/feature/bytes中心で、階層、frame、pickの型付き契約がない。TwoDのSceneNodeStateは描画状態を持つが、親子や編集ownerの情報は持たない。[wire契約](E:/Lumyte/Lumyte.DevTools.Agent/DevToolsAgentContract.cs:1)、[SceneNodeState](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/SceneNodeState.cs:5) | UI/Scene adapterがObjectRefと階層・描画要素の対応を提供。表示frameに対するpickとツリー選択を同じInspectorへ接続する |

DT09のGaugeは同じ値7を再観測すると14へ加算され得るという静的所見。既存のGaugeテストは1回のsnapshot取得なので、この継続観測を検証していない。[既存テスト](E:/Lumyte/Lumyte.DevTools.Host.Tests/DiagnosticsCollectorTests.cs:27)。未完了Activity、source/instrumentのcatalogにも独立した上限が必要。今回これらを新しい実行試験で再現したとは扱わない。

### 6.2. 内部構造と責務

下図は処理経路を示す。実行時の状態はゲームが、永続的なscene/assetの編集文書とviewの選択状態はエディタがそれぞれ管理する。ゲーム内UIのツリーは検査対象であり、Editor UIのDOMとは別である。BrokerはMagicOnion接続の中継と認証を担う。

~~~mermaid
flowchart LR
  subgraph EditorProcess[Editor]
    View[Tree / Image / Inspector UI] --> Bridge[Internal UI bridge]
    Bridge --> Editor[Editor model / Selection]
    Editor --> Client[C# DevTools Client]
  end
  Client -->|MagicOnion| Broker[Server / Broker]
  Broker -->|MagicOnion| Agent[Game Agent / Session]
  Agent --> Dispatcher[Game Dispatcher]
  Dispatcher --> Adapters[UI / Scene / Resources / Input adapters]
  Adapters --> Game[Game runtime]
  Editor --> DAP[External Debug Adapter]
  DAP --> Game
  Client -. telemetry .-> OTEL[OpenTelemetry SDK / OTLP]
  Broker -. telemetry .-> OTEL
  Agent -. telemetry .-> OTEL
  Game -. telemetry .-> OTEL
  OTEL --> Collector[Optional OpenTelemetry Collector]
  OTEL --> Live[Bounded local diagnostics store]
~~~

| 単位（新しい名前は仮案） | 責務と依存 |
| --- | --- |
| Lumyte.DevTools | 型付きdomain登録、feature descriptor、登録scope。通信、Editor UI、ゲームの具体的Worldへ依存させない |
| Lumyte.DevTools.Protocol | MagicOnionのHub/service/receiver interface、MessagePack DTO、schema、ID、error、revisionと互換性fixture。MagicOnion.Abstractions/MessagePackの契約依存を許容し、Client/Serverの実装、Editor、TypeScriptへ依存させない |
| Lumyte.DevTools.Runtime | object registry、階層/snapshot/delta、frameとpickの対応、operation台帳、編集transaction、game dispatcherへの接続。ゲーム側のadapterを注入する |
| Lumyte.DevTools.Agent | MagicOnionの接続・再接続、認証情報、session。game stateの変更判断を持たずRuntimeへ渡す |
| Lumyte.DevTools.Server | MagicOnionのruntime登録、editor session、route、接続権限。game stateを独自に書き換えない。現在同梱するClientApp/表示用bridgeはEditor側の責務として切り出す |
| Lumyte.DevTools.Client（C#） | 共有Protocolから生成するMagicOnion clientを利用し、negotiation、target選択、operation、schema cache、subscriptionとmirror storeを管理する |
| Editor model / UI bridge / TypeScript | 選択状態、ツリー展開、画像表示、Inspector、workspace保存を管理。TypeScriptを使うviewとのbridgeと型はEditor内で閉じ、DevToolsのwireや公開SDKの定義元にしない |
| 任意のOpenTelemetry統合層 | SDK/provider、propagator、OTLP exporter、ライブ診断storeへのadapterを構成。既存DiagnosticsCollectorから再利用部分を抽出し、OpenTelemetry Collectorとの名称混同も解消する |
| Host / adapters / samples | 再利用するGeneric Host接続を抽出し、現在のdemo domain/windowはsampleへ。UI/Scene adapterはゲームの表示階層・描画要素・Entity/Component構造を変換し、特定UI frameworkやECSの採用を要求しない |

最初の物理分割はProtocol、Runtime、C# Clientを中心とする。現在AgentにあるIStreamingHub/receiverとMessagePack DTOはProtocolへ抽出し、Editor用契約を追加する。通信契約の正本は共有C# interface/DTOとし、JSON-RPCやTypeScript用のDevTools protocolを別に定義しない。各製品のxUnit projectは隣接配置にする。

Editor内部のbridgeはC# Clientを呼び、表示に必要なview modelへ変換する。WebSocket/WebView IPCなどの選択、TypeScript型の生成、JSONでの数値表現はEditorの実装判断とし、game/Agentはその方式へ依存しない。C# Clientを直接使うconsumerも同じDevTools機能を利用できる。

Runtime内部では、SessionManager、SchemaRegistry、ObjectRegistry、HierarchyService、SnapshotStore、FrameStore、PickingService、EditService、OperationStore、ExecutionController、TelemetryBridgeに責務を分ける。小さいinterfaceでgame dispatcherと各moduleを注入し、すべてをDevToolsHubや1つのserviceへ集約しない。Network callbackは入力検証とenqueueまで、domain操作はadapterが明示した実行contextで行う。これらは内部サービスの分割案であり、個別assembly化は要求しない。

### 6.3. MagicOnionを基盤とするprotocol v2の契約

公開protocolはMagicOnionの共有C# interfaceとMessagePack DTOを基盤とする。双方向sessionと変更通知にはStreamingHubを使い、独立したschema/page取得やbinary chunk取得には必要に応じてMagicOnionのUnary serviceを使う。標準機能の編集・階層・画像・pickは型付きmethod/DTOとして定義する。ゲーム固有featureもfeature IDと登録済みschema/codecで扱い、任意JSONのmethod名やCLR型名を公開契約にしない。[MagicOnionの共有interface](https://cysharp.github.io/MagicOnion/streaminghub/define-interface)、[Unary/StreamingHub](https://cysharp.github.io/MagicOnion/fundamentals/unary-or-streaminghub)

同一マシンでは現在のnamed pipe接続を活かし、別マシンでは認証付きHTTP/2/TLS接続を用意する。EditorのC# ClientとGame AgentがBrokerへ接続し、Brokerはtargetに対応するAgentへ中継する。DevTools protocol v2、MagicOnionのversion、domain version、schema revisionは別に管理する。採用中のMagicOnion 7.10.2 / MessagePack 3.1.8を基準に、旧新client/serverの互換性を試験する。[現在の参照](E:/Lumyte/Directory.Packages.props:8)、[MagicOnion互換性](https://cysharp.github.io/MagicOnion/fundamentals/version-compatibility)

MagicOnionは同じHub instanceのmethodを逐次処理する。そのためHub methodは短い検証・受付・enqueueまでにし、game処理、snapshot構築、pick、GPU readback、reloadの完了を待たない。接続scopeのworkerが要求をRuntimeへ渡し、receiver通知と結果照会で完了を伝える。受理済み編集の実行と台帳はRuntimeの寿命で管理し、接続scopeの終了を編集rollbackと同一視しない。Hub内でAgentの結果を待ちながら同じHubへの完了通知を待つ循環も作らない。[Hubの処理順序](https://cysharp.github.io/MagicOnion/streaminghub/fundamentals)

編集側Hubの形を示す抜粋。DTO名とmethod名は未実装の設計案であり、完全なAPI定義ではない。Agent登録・中継用Hubは別の役割としてProtocol内に定義する。

~~~csharp
public interface IDevToolsEditorHub
    : IStreamingHub<IDevToolsEditorHub, IDevToolsEditorReceiver>
{
    ValueTask<RequestSubmission> OpenViewAsync(OpenViewRequest request);
    ValueTask<RequestSubmission> PickAsync(PickRequest request);
    ValueTask<EditSubmission> ApplyEditsAsync(EditRequest request);
    ValueTask<OperationStatus> GetOperationAsync(OperationQuery request);
    ValueTask<CancelResult> CancelOperationAsync(CancelOperationRequest request);
}

public interface IDevToolsEditorReceiver
{
    void OnRequestCompleted(RequestCompletion completion);
    void OnOperationChanged(OperationUpdate update);
    void OnHierarchyChanged(HierarchyDelta delta);
    void OnFrameAvailable(FrameDescriptor frame);
}
~~~

RequestSubmission/EditSubmissionは受付拒否または受付IDを表す。OpenView/Pickのような読取jobの受付IDは接続内のrequestId、編集の受付IDは再接続可能なoperationIdと区別し、結果DTOも型を分ける。RequestCompletionは登録済みのOpenView/Pick等の結果unionを持つ。GetOperationは時刻・世代付きの台帳mirrorを短時間で返し、Agentへの再照会が必要なら読取jobへ回す。Agentでの受理・適用状況をBrokerの転送受付と混同しない。operation台帳はHubの寿命の外でRuntimeが保持する。

C# Clientは受付と結果通知の照合を内部で行い、通常のpickは結果をawaitできる入口、編集は状態照会・完了待機・明示的な取消を持つOperationHandleとして提供する。Editorの各viewがHub receiver、再送、revision検証を個別に実装しない。待機のcancelと編集自体のcancelは別の操作としてAPIに表す。

| 契約 | 方針 |
| --- | --- |
| 初期化 | 接続認証 → Initialize/Attach → schema取得 → ready。version範囲と必要profileを交渉し、ready前に編集を受け付けない。heartbeat、deadline、message/queue上限も実装と一致する値を返す |
| 必須と拡張 | observationを基本profileとし、object inspection/selection、runtime editing、simulation control、source debugging、asset transferはversion付きprofile。編集profileは対象UI/Sceneのツリー・画像選択を含むselection profileを必須とし、両経路を保証する |
| target | projectId、runtimeId、runtimeEpoch、worldId、worldEpochを明示。接続し直しただけか、プロセス/Worldが再生成されたかを区別する。更新要求で暗黙に別hostを選ばない |
| 要求と操作 | MagicOnionの応答照合IDと、BrokerをまたぐrequestId、接続をまたぐ論理更新のoperationIdを区別する。認証済みclientIdと正規化した要求内容hashを保存し、同じoperationIdで異なる更新は拒否する。hashに再送時のtrace等は含めない |
| 版 | schemaRevision、object/fieldRevision、changeSetRevision、subscription sequenceを区別。simulation tickは編集のversionとして流用しない |
| キャンセル | CancelRequest/CancelOperationを明示的なmethod/DTOとしてAgentまで中継する。待機停止、queueからの取消、commit済みを区別する。deadline超過をrollback完了として返さない |
| error | gRPC/MagicOnionの接続・RPC失敗と、型付きdomain errorを分ける。DTOにtarget、operationId、field、期待/実際のrevision、適用結果を載せる。Conflict、StaleTarget、OutcomeUnknown、QueueFull、FrameExpired、ReadOnlyなどを区別 |
| serialization | MessagePackの型付きDTOを使い、64-bit ID/revisionは整数精度を維持する。値はbool/数値/string/enum/vector/参照などの明示したunionとし、許可していない型をreflectionで復元しない。TypeScript向けの文字列等への変換はEditor bridge内に置く |
| 互換性 | MessagePack Key/union tag、enum値、公開methodの識別を固定し、既存番号を再利用しない。追加fieldの既定値・unknown field/enumの扱いを定義し、破壊変更はversion/profileを更新する |
| 配信 | Hubの受付処理と長いjobを分離する。receiverは短いenqueueまでとし、送信は順序を管理するwriterと上限付きqueueに集約。binary転送は別枠で制限し、cancel/status・操作結果・状態差分を塞がない |

MagicOnionのClient ResultsにCancellationTokenを渡しても、相手で実行中の処理へのcancel伝播にはならない。DevTools側の取消methodとphase検証を省略しない。StreamingHubの接続順序だけで、複数clientや切断後の二重適用を防げるとは扱わない。[Client Resultsのcancel](https://cysharp.github.io/MagicOnion/streaminghub/client-results)

編集operationの状態はAccepted → Queued → Applying → Committed / Rejected / Failed / Canceledとし、実行結果を確認できないclientの状態をOutcomeUnknownとする。Acceptedはゲーム側Runtimeが受理した時点であり、Brokerだけが転送を受け付けた状態はForwardingとして区別する。Committedはauthority側の適用と結果記録が完了した時点であり、エディタの保存完了やGPU表示完了は別のreceiptとする。snapshot/pickなどの読取jobはSucceeded等の読取用結果を返し、編集commitと同一の状態機械にはしない。

再接続ではoperationIdで結果を照会し、同じruntimeEpochで保持期間内なら同じreceiptを返す。重複抑止をゲーム側の適用箇所まで到達させ、同じoperationを同時に受け取っても1回だけ適用する。台帳の件数・bytes・保持期間を公開し、期限切れやプロセス再起動後はOutcomeUnknown/StaleTargetとして再同期を要求する。永続journalなしでクラッシュをまたぐexactly-onceを保証しない。

### 6.4. object schemaと状態同期

Inspectorには型名に加えて編集用のschemaが必要になる。typeId/fieldId、表示名、値型、nullable、enum、単位、範囲、読み書き可否、適用phase、永続化可否を共有DTOに含める。vector/rotation/matrixでは軸、角度単位、行列の配置を固定する。ゲーム固有のdescriptorとaccessorを登録し、必要ならsource generatorで生成する。protocol/UIが任意のCLR propertyを走査・実行する構造にしない。

実行中の参照はObjectRef(target, objectId, generation)で表す。targetはruntime/worldのIDとepochを含み、1つの要求内で共有する場合も所属を省略して解釈しない。名前、ツリー内の位置、メモリアドレスを識別子にしない。保存文書のasset/entity IDとは別にし、永続化可能なobjectだけauthoring IDへの対応を持つ。削除・再生成時はtombstone/世代更新を通知する。

snapshotと購読は1つのopenSubscription操作で開始する。ゲームの安全な読取境界で基準revision Rを確定し、そのsnapshotとRより後の差分を一続きとして提供する。大きいsnapshotは固定revisionに対するcursorでページ化し、pinする世代と差分bufferに上限・有効期限を設ける。上限超過時は途中の差分を黙って落とさず、ResetRequiredで取り直す。

差分はsubscriptionId、targetの世代、sequence、baseRevision、newRevisionを持ち、作成・更新・削除・並替えを表す。clientは重複を無視し、gapまたはbaseRevision不一致を検出したらsnapshotを再取得する。snapshotの応答前にeventが到着する場合もSDK内で順序を整える。query/filterが変わった場合は購読世代も更新する。

選択中object、表示中の階層page、watch対象だけを購読する。field mask、深さ、件数、byte数、更新頻度の上限を持ち、参照循環をinline展開しない。ゲーム側は変更通知/accessorによるdirty記録を基本にし、未対応domainのsamplingも範囲と頻度を制限する。全Worldを毎フレームJSON化して差分比較しない。

### 6.5. UI・シーンをツリーと画像の2経路で選択する

<a id="devtools-selection"></a>

編集対象は、ツリーの行と描画画像内の領域のどちらから選んでも同じObjectRefに解決する。UIとシーンをそれぞれ対象にし、画像選択をD5の付加機能へ先送りしない。D2で両経路を用意し、D3ではどちらで選んだobjectも同じInspector・編集・undoを利用できることを受入条件にする。

| 対象 | ツリーから選ぶ経路 | 画像から選ぶ経路 |
| --- | --- | --- |
| ゲーム内UI | window/canvas/rootと実際の表示階層をadapterから取得。表示名、種類、表示/有効状態、編集可否、子の有無を表示する | ゲームのUI描画画像をクリックし、transform、clip/mask、重なり順を反映した表示要素を選ぶ。disabledや入力透過のUIも検査対象にできる |
| シーン内object | World/sceneとobjectの親子をadapterから取得。描画batchの並びをゲームobjectの親子と見なさない | camera/viewportの描画画像をクリックし、見えている描画要素から編集ownerを解決する。UI/Scene/Allの対象filterと、重なった候補の選択を提供する |

UI/Scene adapterが、ObjectRef、階層node、描画primitive/instance、編集ownerの対応を登録する。1つのobjectが複数描画要素を持つ場合や、同じassetから複数instanceが生まれる場合もruntime instanceを識別する。draw indexや一時的なGPU IDを永続的なobject IDにしない。編集対象を持たない補助描画は非選択とするか、登録されたownerへ解決する。

階層はHierarchyIdとTreeNodeIdを持つprojectionとし、実体のObjectRefと分ける。group行には実体がない場合もある。同じobjectが複数のツリーに現れる場合は、PickResultに対応する階層と祖先pathの手掛かりを返してreveal先を定める。子はpage単位で取得し、reparent・順序変更・削除もrevision付き差分で同期する。循環参照は子として再帰展開しない。画像に映らない非表示objectもツリーから検査できる。

#### 表示したframeに対してpickする

FrameDescriptorにはtarget、viewportId/世代、frameId、capture時のtick/stateRevision、画像のpixel寸法、切出し領域・向き、camera/UI layoutの世代を含める。画像本体と、同じ描画状態から作ったpick用情報をframeIdで対応付ける。画像の非同期圧縮やGPU readbackで到着順が変わっても、この対応を変えない。

EditorはDPI、zoom、余白、表示変換を除いてクリック位置を元画像のpixel座標へ変換する。protocol上は左上原点、x右向き・y下向きの整数pixel indexとし、0 ≤ x < width、0 ≤ y < height、評価点はpixel中心(x + 0.5, y + 0.5)に固定する。余白や画像外のクリックを別のpixelへ丸めない。PickRequestはtarget、viewport世代、frameId、pixel、対象filter、候補取得modeを含み、現在の最新frameで代用しない。

RuntimeはそのframeのID bufferまたは固定したhit-test snapshotからPickResultを作り、元のframeId、選ばれたObjectRef、順序付き候補、編集owner、画像上の領域を返す。frameの保持期限切れはFrameExpired、対象の削除・世代変更はStaleObjectとして返す。Editorは再取得を案内し、古いクリックを新frameで自動再実行しない。frameが示す値と現在のInspector値の差を識別できるようにし、編集時は現在のrevisionで再検証する。

| MagicOnionの型付き契約（名称は仮案） | 返す情報・用途 |
| --- | --- |
| OpenView / HierarchyPage | 検査対象UI/Scene、階層snapshot/cursor、revision、後続差分の購読 |
| FrameDescriptor / GetFrameChunk | frameと描画状態の対応、画像形式、寸法、期限、binary本体の取得 |
| PickRequest / PickResult | 表示frameのpixelをObjectRefへ解決。UI/Sceneの候補順とownerを明示 |
| ResolveObjectPath | 画像で選んだobjectのツリー内の位置を取得し、必要な祖先pageだけを開く |
| GetSelectionGeometry | ツリーで選んだobjectを、指定した表示frame上で強調するための輪郭/領域。非表示・画面外・該当frameなしも結果として区別 |

Hub methodはこれらの長い処理の受付までにし、読取jobの結果通知・照会とbounded binary転送を使う。画像、pick、階層の各要求は同じtargetと閲覧権限を検証する。

#### 判定と選択状態の規則

標準pickは最前面の見えている編集対象を選ぶ。UIではclip/mask、描画順、表示領域を、Sceneでは実際のcoverageとdepthを反映し、矩形boundsだけで見えていないobjectを確定しない。透過・alpha test・text/pathのedgeを含むcoverageの閾値を契約化し、拡大boundsによる補助選択は別modeにする。disabled/入力透過UIを選べるよう、ゲーム入力のhit testをそのまま編集用の判定にしない。UI/Scene/Allのfilterと候補の巡回で背後のobjectも選べるようにする。

背面を含む候補modeには、同じframeの固定したgeometry/表示順などの候補用情報を保持する。候補はUIの描画順またはSceneのdepthと安定したtie-breakで並べ、上限件数と打切りの有無を返す。最前面のIDを返す標準modeと、遮蔽されたobjectも含む候補modeの結果を区別する。

backend共通のpick結果をrenderer adapterが提供する。GPU ID passなら表示画像と同じtransform/clip/depth/coverageを使い、CPU判定なら同じframeの固定snapshotを使う。方式選択は内部に置き、EditorがVulkan/WebGPU/DirectX12で判定方式を分岐しない。3Dに拡張する場合もこの契約を使い、raycastのbounds候補だけを可視pixelの結果として返さない。

Editorはselected ObjectRef集合、active ObjectRef、selectionRevisionを持つ1つのSelectionModelを使う。画像クリックでツリーの該当行とInspectorを更新し、ツリー選択で画像上の領域を強調する。hoverはcommitした選択から分ける。展開状態やショートカット、複数選択の操作方法はEditor内部とし、選択操作でゲームの通常入力を送信しない。

選択はeditor sessionごとに独立させ、複数observerが互いの選択を奪わない。選択・highlightは原則として閲覧権限で使え、編集controller leaseの取得とは分ける。object削除やtarget世代変更時は選択を無効化する。同じslotに新しいobjectができても自動的に選び直さず、遅いpick/highlight応答はtarget・frame・selectionRevisionで検証してから反映する。古いframe由来のoutlineを別frameへ重ねない。

画像、ID map、hit-test snapshot、圧縮bufferはFrameStoreで件数・bytes・保持期間を制限する。閲覧中viewportだけを必要な解像度/頻度で取得し、非同期readbackと小領域pickを使う。保持中のframeは対応するpick情報と一緒に解放し、解放済み情報への要求には期限切れを返す。capture中もcancel/status/編集結果を処理でき、画像の購読停止・切断でGPU/CPU資源を回収する。

### 6.6. リモート編集、undo、保存

最初の編集契約は、1つのWorld内の登録済みfieldをtransactionとして更新することに絞る。型・値域・schema・target世代・編集権限・expectedRevisionを検証し、適用直前にgame threadで再検証する。必要な変更を準備してから一括commitし、失敗なら変更0件を保証できるadapterだけをtransaction対応として登録する。任意のcommandやI/Oを同じ原子的transactionに含めない。

ApplyEditsAsyncのEditRequestは、target、operationId、schemaRevision、変更配列、適用phaseを持つMessagePack DTOとする。各変更はObjectRef、fieldId、expectedRevision、型付きの新しい値を持つ。たとえばtransform.positionを(1, 2, 0)へ変更する場合、ツリーと画像のどちらで選んでも同じObjectRefと現在のfieldRevisionから要求を組み立てる。画像のpixelやTreeNodeIdを編集先のIDにはしない。

Brokerの受付後、Runtimeで受理したAcceptedとoperationIdを通知・照会可能にする。実際の適用後に、Committed、appliedTick、newRevision、正規化後の値、undo用のchangeSetIdを返す。値の競合は相手の更新を上書きせずConflictにする。expectedRevisionの範囲は編集対象field/objectとし、別objectの毎フレーム更新で全編集が衝突するglobal revisionだけの設計を避ける。

初期は複数observer＋1つの編集controller leaseを標準にする。leaseのowner/期限と、接続断時に入力captureやpreviewを解除する処理を実装する。ゲーム自身による更新との競合はleaseだけでは解決しないため、revision検証は維持する。将来の複数writerは同じtransaction/競合モデルの上に追加し、最初からCRDTを導入しない。

undoは適用済みchangeSetに対する新しい条件付き編集とする。更新後に他者やゲームが値を変えていれば競合として返し、無条件に過去の値へ戻さない。ドラッグ操作はgesture単位にまとめ、previewとcommitを分離する。undo履歴、previewの保持時間、差分bytesに上限を設ける。entity生成・削除・reparentは専用adapterと参照整合性を準備してから追加する。

実行時の変更とscene/assetファイルへの保存は別commandにする。エディタが保存文書のrevisionとruntimeのchangeSetを突き合わせ、適用可能な変更だけをworkspaceへ保存する。動的に生成されたobjectなど対応先のない変更を暗黙に保存しない。reloadはビルド・validation後に安全な境界で世代を切り替え、失敗時は旧世代を維持する。GPU資源の旧世代は既存のretirement設計に従う。コンパイル・ファイル転送・GPU待機でgame threadを占有しない。

### 6.7. 停止・ステップ実行とソースデバッグ

ゲームの実行制御はRunning、PauseRequested、Paused、Stepping、Stoppingの状態機械とする。Pauseは安全なsimulation境界で成立したときに応答し、Stepは定義済みのsimulation tickを指定数だけ進める。render、UI/入力、wall-clock、simulation clockの扱いを分け、停止中もtransport、編集用dispatcher、status照会を処理できるようにする。任意threadの強制停止を実装方法にしない。

ゲーム上のwatch、StateMachineの遷移、Resourcesのload/reload、frame/tickの停止条件はdomain adapterから観測・制御する。watch式は副作用のない登録済みaccessorと評価予算に限定する。pauseとreload/editが重なったときの適用phase、入力captureの解除、controller離脱後の継続/停止方針も契約に含める。

ソースのbreakpoint、stack、locals、step-in/over/outは外部Debug AdapterとDAPで連携する。DAPは開発ツールとdebug adapter間の標準を定め、機能交渉と停止時の変数取得を持つ。採用する.NET debuggerのruntime/OS対応と配布条件をD6で確認し、DevTools自身に汎用debuggerを実装しない。[DAP概要](https://microsoft.github.io/debug-adapter-protocol/overview)

プロセス全体がdebuggerで止まると、ゲーム内Agentも処理できなくなる。外部adapter/Brokerは接続状態を保持し、UIにはDebuggerStoppedと取得済みsnapshotの時刻を示す。停止中のruntime RPCは有限deadlineで拒否/保留方針を明示し、resume後に古い編集を自動適用しない。DAPの停止中variable referenceと、runtime/world世代内で安定したObjectRefを同一視しない。ソースdebuggerの停止・再開をEditor sessionへ関連付けるが、DAP messageを編集transactionへ変換しない。

### 6.8. OpenTelemetryを標準の観測経路にする

<a id="devtools-opentelemetry"></a>

.NET側は既存のActivitySource/ActivityとMeterを維持し、構造化logにはILoggerのadapterを使う。SDK/provider/exporterの構成はgame/editor/serverのcomposition rootに置き、ResourcesやInteractionからSDKやCollectorへ直接依存させない。現在の計測名・単位は継続し、変更が必要ならversion付きの移行として扱う。[既存Resources計測](E:/Lumyte/Lumyte.Resources/ResourcesDiagnostics.cs:6)、[Interaction計測](E:/Lumyte/Lumyte.Interaction/InteractionDiagnostics.cs:6)、[OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/instrumentation/)

通信と観測の役割を次のように分ける。

| 経路 | 役割と保証 |
| --- | --- |
| DevTools control | 編集、operation結果、pause/step、schema、権限。操作のreceiptを持ち、結果不明を区別する |
| DevTools state sync | Inspectorのsnapshot/delta。sequenceとrevisionで連続性を検証し、欠落時に再同期する |
| OpenTelemetry / OTLP | trace、metric、logの収集・export。samplingや欠落を許容し、編集成功やundoの唯一の記録として使わない |
| bulk / capture | asset、スクリーンショット、GPU capture、profile添付。参照IDとmetadataはcontrolで送り、本体はchunk化した別の転送枠を使う |

OTLPはテレメトリの転送仕様であり、編集commandの信頼性を保証するプロトコルとして流用しない。各SDKから任意のOpenTelemetry CollectorへOTLPで送り、processor/exporterで保存先へ接続する。Collectorなしでもローカルの上限付きstoreとDiagnostics UIを使える構成を用意する。[OTLP仕様](https://opentelemetry.io/docs/specs/otlp/)、[Collector構造](https://opentelemetry.io/docs/collector/architecture/)

#### traceを操作単位で伝播する

MagicOnion StreamingHub接続を開始した時のtraceだけでは、その後の個々の編集を関連付けられない。各logical requestの共有RequestContext DTOにW3C traceparent/tracestateを載せ、SDKのpropagatorでextract/injectする。接続headerだけに頼らず、EditorのC# Client → Broker → Agentでmethod単位に中継する。TypeScript viewからの相関情報もEditor内のbridgeでC#側へ渡す。trace metadataは任意であり、無効化・不正値・未記録でも編集の意味を変えない。[context propagation](https://opentelemetry.io/docs/concepts/context-propagation/)

基本の因果関係はEditor操作 → client RPC → Broker route → Agent受理 → queue待ち → game threadでの適用 → resource reloadなどの子処理とする。要求をqueueへ入れる時点でActivityContextを値として保持し、dispatcherで明示的に復元する。Activity.Currentを通信threadから後で参照したり、長いWebSocket sessionをすべてのframeの親にしたりしない。

受理応答と実際のcommitは別spanにし、後で実行される処理は保存したcontextと結ぶ。複数の要求をまとめるbatchや、独立したframeとの関係はActivityLinkで表し、1つのframeを任意の1編集の子へ押し込まない。再送ごとに通信spanを持てるが、重複抑止された編集のapply spanを再発行しない。[Tracing APIのparent/link](https://opentelemetry.io/docs/specs/otel/trace/api/)

operationId、runtimeEpoch、world、appliedTick、変更対象の識別はDevToolsの契約として保持し、traceId/spanIdは任意の相関情報としてreceipt/logに添える。samplingでtraceが残らなくても、操作結果とundoは照会可能にする。ILoggerの構造化logは有効なActivityに関連付け、UIから操作・trace・logを相互に辿れるようにする。[.NET log correlation](https://opentelemetry.io/docs/languages/dotnet/logs/correlation/)

#### metric、sampling、保持上限

| 項目 | 方針 |
| --- | --- |
| instrumentの意味 | Counter/UpDownCounterは累積または増減、Gaugeは観測値、Histogramは分布としてSDKで集計する。ObservableCounterの累積観測を再加算しない |
| 時間と集計 | monotonic clockでqueue待ち/適用時間を測る。OTLPではtemporality、start time、bucketを保持し、UI表示用の有限窓percentileを標準Histogramの代わりにexportしない |
| 属性 | string/bool/numberなど型を保持する。operationId、objectId、frame番号、assetの完全pathはtrace/logへ記録し、metric labelへ入れない。resource metadataもservice/instance等の安定した識別に限定し、操作ごとに増やさない |
| 計測候補 | queue depth/bytes、pending操作数、queue待ち、edit適用時間、競合/重複抑止、snapshot bytes、resync、frame時間、export/drop数。method種別や結果など制限した分類で集計 |
| sampling | composition rootでsourceごとの方針とcapture期間を定める。毎object・毎draw・毎frameの全量traceを常時要求しない。通常はSDKのsamplingに従い、詳細captureは期間・件数・bytesを制限する |
| ライブ表示 | sampled span/metric/logのcompactなコピーをstoreへ流す。未完了operationの表示はRuntimeのoperation台帳から得る。Activity本体や任意object graphを無制限に保持しない |
| 負荷と寿命 | game thread上のlistener/processorは短いコピー・enqueueまで。serialize/export/通信はworkerへ。queue満杯はtelemetryのdropと件数通知で処理し、gameやcontrolを待たせない |
| UI操作 | 画面のPause/Clearはそのview/storeを対象にする。全体sampling・exportの停止は別権限の設定にし、UIのclearでSDKの累積metricを初期化しない |

標準のmetric集計とexportにはSDKを使い、必要なViewで属性やHistogram境界を制御する。[OpenTelemetry .NET metrics](https://opentelemetry.io/docs/languages/dotnet/metrics/)。現在の独自ActivityListenerが常時AllDataAndRecordedを要求する構成は、SDKのsamplingとは別に全件の詳細生成を要求するため見直す。ライブstoreはSDKのprocessor/exporter adapterを基本とし、SDKなしの軽量経路を残すなら別modeとして明示し、二重収集・二重exportを防ぐ。

process間の時間差を無視して受信時刻から処理時間を計算しない。サービス名・version・instance、runtimeEpochを識別できるmetadataと、spanの親子・link、各process内のdurationを使う。ゲームのsimulation clockをtelemetryの時刻へ流用しない。trace/baggageには編集payload、認証token、任意のasset内容を自動転記せず、転送するmetadataを制限する。

OTLP送信の失敗・再送・部分成功はSDK/exporterの仕様に従う。DevToolsのoperationをOTLPの再送に連動させない。export先停止、Collector未接続、shutdown時も上限付きで処理し、flushの完了を無期限にgame終了の条件にしない。OTLPは転送先での検索用の標準query APIではないため、履歴検索はTelemetryQuery adapterでローカルstoreまたは保存先のquery APIへ接続する。

### 6.9. transport、権限、メモリ

MagicOnionの共有契約を維持して、同一マシンのpipeとネットワークのHTTP/2/TLS接続を切り替える。targetごとのobserve/edit/simulation-control/source-debug/persist権限をattach時に確定する。Editor内部でBrowser bridgeを提供する場合もOrigin検証とsession認証を設け、hostIdを知るだけで編集できる形にしない。release構成でリモート編集を有効にするかはcompositionで明示する。

schema accessorsの登録範囲だけを編集可能にし、任意のmethod実行・file pathへの書込みを汎用RPCとして公開しない。ログやtelemetryが漏れなく残ることに編集権限を依存させない。必要な編集履歴はサンプリングされないchangeSet/operation台帳で管理する。

制御要求、状態差分、telemetry、bulkは別の予算を持つ。操作結果は台帳から回復可能にし、満杯なら受付時に拒否する。状態差分はgap通知と再snapshot、telemetryは集約/drop、bulkは中断・再開で対処する。単にすべてを無制限queueへ入れる構造を避ける。

ProtocolではMessagePack DTOと登録済みcodecを使い、現在のbyte[]内のJSONやJsonElement → JsonNode → string → UTF-8の反復を解消する。schemaとfeature lookupはrevision付きcache、snapshotはページ化と差分を使う。frame captureは必要な解像度/頻度だけを非同期readbackし、GPU完了tokenに従ってbufferを再利用する。画像・asset・profile本体はMagicOnionの別service/streamでbounded chunkとして転送し、control用Hubの巨大な通知に詰め込まない。Editor bridgeでもbase64への反復変換を避ける。

### 6.10. 段階的な実装工程と受入条件

<a id="devtools-roadmap"></a>

| 工程 | 内容 | 完了条件 |
| --- | --- | --- |
| D0: 契約と最小sample | MagicOnionの共有C#契約、Protocol/Runtime/C# ClientとEditor内部bridgeの境界、target・operation・schema・trace contextをADR化。UI/Scene adapterとframe/pickの対応を定義 | UIとSceneそれぞれで「同じobjectをツリー/画像から選ぶ」consumerを設計し、TypeScriptや特定UI framework/ECSなしでも契約を試せる |
| D1: 接続の信頼性とOTel基礎 | DT02–DT05/DT08、R13を修正。MagicOnion Hub/receiver/DTOをProtocolへ抽出し、短い受付とjob実行を分離。session/世代、bounded queue、cancel/deadline、task回収、v2交渉、trace伝播 | 長い要求中もstatus/cancelを処理でき、切断時にtask/購読/pendingが残らない。旧接続の結果を新接続に混ぜず、TypeScriptが公開protocolの依存に入らない |
| D2: ツリー・画像選択とInspector | DT06/DT07/DT10を対象に、schema、階層page/差分、ObjectRef、C# mirror store、UI/Scene画像、frameに対応するpick、reveal/highlightを実装 | sampleのUIとSceneをそれぞれ両経路から選ぶと同じInspectorへ到達する。DPI・重なり・clip・古いframe・削除・gap・再接続を正しく扱う |
| D3: リモート編集の最小実用版 | game dispatcher、条件付きtransaction、operation台帳、controller lease、gesture/undo。両選択経路を編集へ接続し、Editor操作から適用までtrace/logを関連付ける | UI/Scene双方でツリーまたは画像から選択して変更・undoできる。失敗時は変更0、応答消失後の再送は二重適用0。runtime変更と保存は区別される |
| D4: 実行制御と観測 | pause/step/watch、SDKによるmetric/log/trace、上限付きライブstore、OTLP、collector/export障害の隔離。DT09を修正 | 1tick進行後の状態を検査でき、停止中も編集/controlが応答する。Collectorなし/低速/停止中でもゲーム処理と編集結果が成立する |
| D5: 永続編集とasset連携 | authoring ID対応、scene/UI文書の差分保存、resource reload、artifact転送、世代切替。必要に応じて動画・高解像度captureを追加 | 保存revisionの競合を検出し、再起動後も保存した内容を再現。reload失敗時に旧資源を維持し、captureがcontrolを塞がない。基本の画像選択はD2で完了済み |
| D6: ソースデバッグと拡張 | 外部DAP adapter、source mapping、breakpoint/locals、停止状態の統合。必要なゲームで複数writerや高度な編集を追加 | process全停止中のAgent不応答を正常に扱い、adapterから再開できる。runtime IDと停止中variable referenceが混同されない |

D3を最初の実用的な到達点とする。最初から全エディタ機能を実装する前提にはせず、1つの実ゲームまたはsampleでUI adapterとScene adapterの両方を通す。D2の画像・pickは共通capture/readbackと資源寿命の契約を先に確認し、単なる最新画像の表示だけで完了としない。OTelのtrace伝播はD1から入れ、後からrequest/operationを識別し直す手戻りを避ける。

旧Agent契約はMagicOnionの移行adapterで段階的に更新し、既存Browserの接続はEditor内部bridgeへ移す。新しい編集profileはv2だけで提供し、named pipe/ネットワークのC# client/serverで同じprotocol conformanceを使う。TypeScriptの型・bridge・view挙動はEditorのテスト対象とし、DevTools protocolの互換性matrixへ含めない。現行の型付きdomain handlerはadapterで接続できるが、編集可能schema・適用phase・transaction対応は明示的な登録を追加する。

xUnitのC# protocol/runtime/client consumer testでは、ManualClock/TimeProvider、fake dispatcher、frame/pick adapter、制御可能なtransport、in-memory exporterで次を検証する。

- 同一operationの同時再送、応答消失、台帳期限切れ、runtime再起動、別targetへの誤送信。
- snapshot境界の更新、sequence gap、旧購読の遅延event、schema変更、object削除/再生成。
- MagicOnionの短い受付と結果通知、同じHubを経由する完了・cancelの循環待ち防止、旧新DTOの追加field/unknown値とmethod互換性。全DTOの巨大snapshotではなく、維持すべき少数のwire契約を検証する。
- UI/Scene各objectのツリー選択と画像pickが同じObjectRefへ到達し、画像選択から祖先pageを開け、ツリー選択から対応する画像領域を強調できること。
- DPI/zoom/余白/resizeの座標変換、clip/mask・重なり・disabled/入力透過UI、UI/Scene filter、同一assetの複数instance、非表示objectのツリー選択。
- 表示frameとpick情報の一致、FrameExpired、対象削除/再生成、遅いpick/highlight応答、frame切替中のoutline、複数editorの独立選択、選択時にゲーム入力を発生させないこと。
- 2writerまたはgame更新とのrevision競合、commit直前のcancel、undo競合、transaction途中のvalidation失敗。
- game thread以外からの適用防止、pause中の処理、tick数、接続断後のinput/preview lease解除。
- Editor → Broker → Agent → dispatcherでのtrace parent/link、traceがsamplingで除外されても成立するreceipt、重複抑止時のapply span非重複。
- Gauge/ObservableCounterの繰返し観測、Histogramのbucket/temporality、型付き属性、metric cardinality、logsとtraceの相関。
- telemetry飽和時のdrop、export失敗/再送、未完了operationの保持上限、shutdownの回収。UIのPause/Clearが他のclientやOTLP exportを止めないこと。

OS transport、実ゲームthread、GPU capture/pick、DAP、実Collectorとの相互運用はintegration/conformanceとして分ける。実MagicOnion client/serverで長いjob実行中のcancel/statusを確認し、fake transportだけでHubの逐次実行を検証済みとしない。描画conformanceでは複数frameの画像とpick結果を対応付け、3backendの同じ可視objectへ到達することを確かめる。小さいCollectorの設定と実OTLP受信までを検証し、SDK内のfake exporterだけで相互運用済みとしない。

Editor側ではツリー、画像、Inspectorの選択連動と内部bridgeの数値変換をVitest/Testing Library等で検証する。性能は接続なし、監視のみ、差分同期、画像購読/連続pick、連続drag、遅いclient、export先停止の各条件でCPU p95、allocated bytes/frame、queue/frame store bytes、帯域、pick応答時間、GPU待機を比較する。編集の正しさと観測の負荷を両方受入条件にする。

## 7. 改善工程

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
| E8: DevToolsのリモートEditor基盤 | 要見積り | D0/D1は独立着手可。Protocol抽出はE3と共有、D2のcapture/pickはE2/E4のGPU契約と調整 | [D0–D6](#devtools-roadmap)に分け、MagicOnion/C#契約でD2のUI・Sceneツリー/画像選択、D3の条件付き編集・再接続・undoを実現。D1からOTel相関を組み込み、実行制御・OTLP・保存・DAPへ拡張する |

既存工程の概算はE0–E6（E2aを含み、E2bを除く）で約33–57人日、Browserを含めて約41–72人日。API評価の統合で追加したE2bとDevToolsのE8は別途見積りが必要であり、E2/E4/E5/E6へ追加したAPI契約・実装・公開例の工数も棚卸し後に見直す。この数値を統合後の全作業の総額として扱わない。最初に予算を確保すべき単位はE0/E1。E3の配置変更とE2a/E2b/E4の実装変更は同じファイルを触りやすいため、同時進行するなら変更範囲を分ける。

E8はGraphicsの全工程完了を待たず進める。R13のhost切替修正はE1/D1、Protocol抽出はE3/D1で共有し、二重に実装しない。D0の契約・小さいsample、D1の接続とtrace伝播、D2のツリー/画像選択・状態同期、D3の編集をそれぞれ独立PRにする。基本capture/pickに必要な非同期readbackとGPU寿命はD2の前提として整備し、D5のreload・大容量転送ではResourcesの停止競合修正とGPUのretirementを利用する。

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

## 8. 評価方法と受入条件

### 8.1. 性能・メモリ

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
| DevTools/OTel: 未接続・監視・編集・低速client/export先停止 | game CPU p95、allocated B/frame、queue/store bytes、帯域、drop/resync、操作待ち時間 | 各予算内へ収束し、通信・export待ちでgame threadを停止させない。traceが欠落しても編集結果の照会・重複抑止が成立する |
| DevTools画像選択: UI/Scene・viewport切替・連続pick・低速受信 | frame/ID map/snapshot bytes、readback/圧縮時間、転送量、pick応答時間、GPU待機 | 表示画像とpick対象が一致し、期限切れは明示される。保持量が予算内へ戻り、captureがcancel/status・編集結果を塞がない |
| 長時間実行 | managed/native/GPU bytes、retired bytes、frame p50/p95/p99 | 解放後の利用量が定常上限へ戻り、cacheやdescriptorが単調増加しない |

最初から「全描画0 allocation」「一律30%高速化」を目標にしない。cold start、cache hit、cache miss、resize、device lostを分ける。CPUはBenchmarkDotNet、GCはallocation profiler/counters、GPUは各backendのtimestamp・validation・frame captureで測る。ハードウェア、driver、Release設定、解像度、入力、warmupを記録し、時間比較は同一環境で行う。

### 8.2. APIの使いやすさ

人に試してもらうときは、次の課題を新規consumer projectから実行してもらう。

1. 矩形・画像・日本語テキストを表示する。
2. 毎フレーム同じ文字を再利用し、1個のScene nodeを動かす。
3. 2Dの間に独自compute/raster passを追加する。
4. 非同期描画後にprepared dataを差し替えて、安全に終了する。
5. resourceをscopeで読み込み、reload後も特定世代を処理する。
6. 入力actionでanimation/state transitionを起動する。
7. backend生成・ホスト接続だけを差し替え、同じ描画・computeコードを別backendで実行する。共通範囲の入力には追加のcapability判定や代替コードが不要であることを確認する。
8. CommandEncoderで変換・矩形/path clip・layerを入れ子にし、scope内で例外が起きても、外側の描画が元の状態で継続できることを確認する。
9. C# DevTools ClientとEditorでtargetを選び、UIとSceneの各objectをツリー・画像の両方から選択して、同じInspectorの値を編集・undoする。画像選択でツリーの該当行が開き、ツリー選択で画像の対象が強調されることも確認する。
10. ゲームをpauseして1tick進め、変更した値と処理結果を調べる。Editor操作からgame処理のtrace/logへ辿り、Collector未接続時にも編集とローカル診断を使えることを確認する。
11. 遅延した画像の期限切れ、object削除、target切替、応答前の切断を試す。新しいobjectの誤選択・誤編集を防ぎ、同じoperationの結果を再接続後に確認できることを検証する。

初回成功までの時間、必要な概念数、書き直した箇所、実行時例外、実装ソースを読んだ回数を記録する。性能も同時に確認し、短くなったコードが毎フレームのallocation・同期を増やしていないことを検証する。

既存のconsumer/conformance testを基盤にし、テスト専用helperを知らずに使えるQuick Startを作る。コード例は独立consumerとしてcompileする。公開例だけで標準用途を完結できることを確認し、DESIGN文書は内部理解の補助として位置付ける。

## 9. テスト・ビルド・配布の整備

- 純粋なunit、OS integration、GPU conformance、browser、performanceを明示して実行する。現在のCategory traitと共通test sourceは活用できる。GPU無しのCIで失敗を隠す自動returnや無条件skipを増やさない。
- リポジトリ内にCI定義・global.jsonが見当たらない。SDK、Slang、Node、package lockの再現条件と、Windows/Linux/browserで実行するjobを明文化する。外部CIの有無は今回確認していない。
- native conformanceでは小さい画像の正しさだけでなく、容量境界、失敗途中、再利用、複数device、複数frame、resizeを追加する。実画像全体を固定snapshotにせず、必要な画素領域・描画結果・資源寿命を検証する。
- clean build、incremental build、publish、pack後のconsumer buildを分ける。Shader.Offlineはbuild toolingとして配り、slang import/includeとtool version/optionsの変更がincremental inputsに入るか確認する。
- frontendは既存のlint、型検査、VitestをCIで実行する。csprojのbuildだけでfrontendの挙動テストが通ったと扱わない。
- DevToolsはProtocol/Runtime/C# Clientの決定的なconsumer testと、実MagicOnion・GPU capture/pick・OTLP Collector・DAPの相互運用試験を分ける。v1移行とv2 profileの共有C#契約・MessagePack互換性を確認する。TypeScript/bridgeはEditor内部のテストとして扱い、[D0–D6の異常系・負荷条件](#devtools-roadmap)をgateに加える。
- API migrationでは、backend生成・ホスト接続だけを差し替える共通consumer sampleを先に作る。64-byte root dataとparameter buffer、syncとasync、所有と借用の使い方を説明する。native固有機能は明示的な拡張の例に置き、共通サンプルは内部の転送方式を選択しない。

## 10. 当面保留するもの

全領域の細かいinterface化、全classの別assembly化、全面的なECS化、マルチqueue、全面的なunsafe/pooling化、native pointerを全backendで共通化する設計は、この段階では採用しない。現在の正しさ・利用規模・測定値に対して必要性を判断する。

No Graphics API由来の責務分離とWebGPUの明示的な互換経路は維持する。その上で、寿命・対応機能・容量を契約として揃え、同じ意味の描画をより少ない割当・binding・待機で実行できる状態を目指す。

## 検証補記

本書のテスト結果は初回の構造・性能レビュー時に実行したもの。API評価、方針の改訂、DevTools/OpenTelemetryの追加評価と本書への統合は文書のみの変更であり、製品コードの変更やテストの再実行は行っていない。実装変更時にはAGENTS.mdに従い、回帰テストとdotnet test Lumyte.slnxを実行する。

- Frontend型検査: tsconfig.app.json / tsconfig.node.jsonをそれぞれ noEmit、incremental falseで検証し成功。
- ESLint: 成功。
- npmは通常PATH上にないため、同梱Nodeから既存ローカルツールを実行した。
- 最初のVitest起動はsandboxのchild process制約（spawn EPERM）で失敗。制限外で再実行し、3ファイル10件が合格した。
- tsc -b は既存tsbuildinfoへの書込み権限で失敗したため、各projectを --noEmit --incremental falseで再検証した。型検査の成功と、build出力の書込み可否は区別している。
