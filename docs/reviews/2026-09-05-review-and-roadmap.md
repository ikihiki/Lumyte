# Lumyte レビューと改善工程

レビュー日: 2026-09-05 / 対象: c9b9839（レビュー開始時の HEAD）

優先すべきなのは、Resources の終了競合、GPU コマンドの失敗時の寿命管理、descriptor の容量・再利用、Text キャッシュの上限である。その後に API の対応範囲を定義し、プロジェクト境界を整理する。既存の arena、RenderGraph、明示的な resource/view の分離を土台に改善する。

今回の成果物はレビューと工程表であり、製品コードの変更は含まない。全53プロジェクトの定義・依存関係を確認し、主要な公開 API、実行経路、寿命管理、テスト、ベンチマークを重点的に読んだ。すべてのメソッドを網羅する監査ではない。性能の指摘は、今回計測した数値と、ソースから判断した改善候補を区別する。

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
| TwoD | 66ファイル5340行。Renderer 1031行、RenderGraphExtensions 1026行 | Recording/Preparation、Scene、Geometry、GPU、Composition、GraphIntegration に内部分割。最初から6個の assembly にはしない |
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

| 観点 | 現在の Lumyte | 改善方針 |
| --- | --- | --- |
| allocation と texture/view | 分離済み。persistent arena、alias plan も存在 | 維持し、TwoD の直接 allocation も必要に応じて同じ allocator に接続 |
| WebGPU の memory model | DeviceOwnedResources を明示し、placed allocation を偽装していない | 維持。WebGPU でのメモリ削減は descriptor/resource の再利用、pool、使用期間短縮を中心にする |
| root input | 128-byte inline data が ABI に含まれる。GpuDeviceAddress は型のみで入力経路なし | 共通の parameter block/range 契約を定義。Native の高速経路は capability と shader variant で明示する |
| shader resource | 5種類の論理 table があるが Native も都度 native table を構築 | public の論理 index は維持し、Native は persistent descriptor/ページ再利用へ。WebGPU は小さい table/layout の変換経路へ |
| shader input | Backend の pipeline 作成が複数ターゲット入り GpuShaderPackage を受け取る | package の読込・選択を上位へ移し、backend-ready IR を受け取る |
| graphics state | GpuDepthStencilState は公開されるが recorder に設定経路がない。CullMode 等は pipeline に入る | 実装可能な state 契約を定義。動的状態の利用可否・PSOに含む状態を backend ごとに明示 |
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

## 4. 優先度付きの所見

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

### R06 / P2 / 実装差分: capability と共通 API の対応範囲が粗い

RasterPipeline/ComputePipeline フラグだけでは、attachment数、MSAA、root data、binding個数、format usageを判定できない。WebGPU の SetRootData と SetComputeRootData は例外。Vulkan/WebGPU の raster 作成は1 color/1 sampleに制限される。一方、共通descriptionはより広い値を受け取る。[capabilities](E:/Lumyte/src/graphics/Lumyte.Graphics/GpuBackend.cs:4)、[WebGPU root data](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuCommands.cs:340)、[Vulkan raster](E:/Lumyte/src/graphics/Lumyte.Graphics.Vulkan/VulkanDevice.cs:552)

GpuDeviceLimits、formatごとのusage/sample support、ParameterBindingMode、NativeAddress/Placement/Aliasing の可否を明示する。全methodを細かいinterfaceに分割する前に、この機能契約と共通 conformance を作る。128-byte root input を共通機能にするなら WebGPU buffer-backed 経路を実装し、そうしないなら Native 限定として API を分離する。

### R07 / P2 / 実装差分: WebGPU Native と Browser の完成度を区別する

現 backend は Silk.NET native WebGPU/wgpu と DevicePoll を利用する。Create は adapter/device callback が呼出し直後に完了したかを確認する。Browser プロジェクトは native backend の参照のみで、JS interop、canvas、非同期初期化の実装はない。[WebGpuDevice.Create](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuDevice.cs:57)、[Browser.csproj](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU.Browser/Lumyte.Graphics.WebGPU.Browser.csproj:1)

Browser が必要な場合は、非同期 factory、queue completion、readback、device lost、canvas resize/presentation、WASM publish を独立した工程にする。WebGPU のブラウザ契約では adapter/device の取得は Promise であり、adapter の features/limits を問い合わせる。[GPU仕様](https://gpuweb.github.io/types/interfaces/GPU.html)、[GPUAdapter仕様](https://gpuweb.github.io/types/interfaces/GPUAdapter.html)

64 logical slot は現在の変換器の上限であり、すべてのdeviceが各種類64個を同時利用できる保証ではない。shaderが実際に使うbindingと選択deviceのlimitsを検証する。また、WGSL の group/binding 書換えは正規表現によるため、対応入力を Slang の出力に限定して明示するか、build時のreflection/変換に移す。[binding変換](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuCommands.cs:104)

### R08 / P2 / 静的確認: descriptor 管理が draw 数と table instance 数に比例する

DirectX12 は SetResourceTable ごとに shader-visible heap を作成する。Vulkan は set と descriptor 更新を繰り返す。WebGPU には cache があるが key は table オブジェクトとlayoutで、同じ内容でも別instanceは別entry。cache に容量制限はなく、資源/view/pipeline破棄時は全件無効化する。新しい table を毎フレーム作り、資源を長く保持する利用では cache が増え続ける。[DX12 heap生成](E:/Lumyte/src/graphics/Lumyte.Graphics.DirectX12/DirectX12Commands.cs:444)、[WebGPU cache](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuDevice.cs:247)、[全件無効化](E:/Lumyte/src/graphics/Lumyte.Graphics.WebGPU/WebGpuDevice.cs:643)

Native は device所有heap/poolのpageとfence retirement、descriptorの世代/dirty範囲を管理する。WebGPU は table の再利用または内容を表す安定key、上限付きcache、依存resource単位の無効化を検討する。ハッシュだけで同一性を判断しない。cache hit/miss、生成回数、保持bytesを可視化する。

### R09 / P2 / 静的確認: 2D/Text の準備処理で CPU-GPU を同期している

Renderer の path compute preparation、DistanceFieldRasterizer の glyph生成、ColorBitmapTexture の upload は submit後に Wait する。新しいpath/glyphが現れるフレームでCPUが止まり得る。DirectX12のWaitはThread.Yieldによるpoll loop。[path準備](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/Renderer.cs:622)、[glyph描画](E:/Lumyte/src/graphics/Lumyte.Graphics.TwoD/DistanceFieldRasterizer.cs:276)、[bitmap upload](E:/Lumyte/src/graphics/Lumyte.Graphics.Text/ColorBitmapTexture.cs:133)、[DX12 wait](E:/Lumyte/src/graphics/Lumyte.Graphics.DirectX12/DirectX12Commands.cs:65)

upload→compute preparation→draw を同じ graph/queue の依存として記録し、staging/view を token 完了まで保持する。既存 ExecuteAsync と retirement queue を再利用する。同期readback APIは明示的な用途として残し、通常描画と分ける。Nativeの必要なCPU待機はイベント通知型へ。待機回数と待機時間を計測する。

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

## 5. 改善工程

工数は1人が実装・レビュー対応・テスト整備を行う場合の粗い人日。既存APIの互換性、対象GPU/OS/ブラウザの範囲が未確定なため見積りには幅を持たせる。日程の約束ではなく、各工程の完了時に見直す。

| 工程 | 目安 | 依存 | 成果物と完了条件 |
| --- | ---: | --- | --- |
| E0: 再現・基準線 | 2–3人日 | なし | R01の競合、17以上のtable bind、recording例外、2deviceのID、2host切替を個別に再現。テスト分類とCPU/GPU計測シナリオを記録 |
| E1: 正しさと寿命 | 5–9人日 | E0 | R01/R03/R04/R13を修正。Vulkan pool容量対策を先行。callback失敗・誤使用でリーク/追跡外submissionなし。全体テスト成功 |
| E2: APIと対応表 | 3–5人日 | E1 | limits、root input、state、device/queue/thread/lifetime契約をADR化。backend共通API例が3backendで成功するか、呼出し前に明確に対応判定できる |
| E3: 配置と依存の整理 | 3–6人日 | E2 | src/tools/samples配置統一、RenderGraph抽出、shader containerとIR境界、DevTools.Protocol分離。runtimeにcompiler/server依存が入らないことを検証 |
| E4: descriptorとGPU実行 | 6–10人日 | E1/E2 | pool/heap page再利用、WebGPU有界cache、upload/compute/draw統合、Native待機改善。1k/10k batchと複数frameで容量枯渇せず、生成・待機回数を記録 |
| E5: 2D/Textメモリ | 5–9人日 | E4のretirement契約 | font共有、atlas予算/eviction/fallback、Scene dirty/sort/batch改善、allocator接続。長時間利用で予算内に収束し、表示内容を維持 |
| E6: その他hot pathと配布 | 3–5人日 | E0/E3 | Interaction/Resourcesを測定して改善。CI、SDK/toolchain固定、runtime配布、サンプル、performance記録を整備 |
| E7: Browserの完成（必要な場合） | 8–15人日 | E2/E3、E4の非同期契約 | JS/WASM adapter、非同期初期化・completion/readback、canvas表示、device lost、browser conformanceを実行 |

E0–E6 は約27–47人日。Browserまで含めると約35–62人日。最初に予算を確保すべき単位は E0/E1。設計・テスト結果に応じて後続を再見積りする。E3の配置変更とE4のbackend変更は同じファイルを触りやすいため、同時進行するなら変更範囲を分ける。

### 最初の PR 群

1. ResourceHotReloadManager の停止競合修正＋決定的な回帰テスト。
2. Vulkan descriptor pool の容量管理と増設＋繰返しbind試験。
3. CommandBuffer のabort/所有権とsubmission全件事前検証＋異常系試験。
4. deviceをまたぐID検証＋2device試験。
5. DevTools host選択の明示化＋hookの挙動テスト。
6. capability/limits/root input/lifetime のADRと小さいconformance例。
7. 配置だけを変更するPR。その後、RenderGraph・shader・Protocolの抽出を各PRに分離。

各バグ修正にはxUnit回帰テストを付ける。Frontendの挙動は既存Vitest/Testing Library経路で検証する。生成物全体のsnapshotやCSS class/属性順によるchange-detectorテストを増やさず、observable behaviorを検証する。

## 6. 性能・メモリの計測と受入条件

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

## 7. テスト・ビルド・配布の整備

- 純粋なunit、OS integration、GPU conformance、browser、performanceを明示して実行する。現在のCategory traitと共通test sourceは活用できる。GPU無しのCIで失敗を隠す自動returnや無条件skipを増やさない。
- リポジトリ内にCI定義・global.jsonが見当たらない。SDK、Slang、Node、package lockの再現条件と、Windows/Linux/browserで実行するjobを明文化する。外部CIの有無は今回確認していない。
- native conformanceでは小さい画像の正しさだけでなく、容量境界、失敗途中、再利用、複数device、複数frame、resizeを追加する。実画像全体を固定snapshotにせず、必要な画素領域・描画結果・資源寿命を検証する。
- clean build、incremental build、publish、pack後のconsumer buildを分ける。Shader.Offlineはbuild toolingとして配り、slang import/includeとtool version/optionsの変更がincremental inputsに入るか確認する。
- frontendは既存のlint、型検査、VitestをCIで実行する。csprojのbuildだけでfrontendの挙動テストが通ったと扱わない。
- API migrationでは小さなconsumer sampleを先に作る。native高速経路とportable経路、syncとasync、所有と借用の使い方を説明する。

## 8. 当面保留するもの

全領域の細かいinterface化、全classの別assembly化、全面的なECS化、マルチqueue、全面的なunsafe/pooling化、native pointerを全backendで共通化する設計は、この段階では採用しない。現在の正しさ・利用規模・測定値に対して必要性を判断する。

No Graphics API由来の責務分離とWebGPUの明示的な互換経路は維持する。その上で、寿命・対応機能・容量を契約として揃え、同じ意味の描画をより少ない割当・binding・待機で実行できる状態を目指す。

## 検証補記

- Frontend型検査: tsconfig.app.json / tsconfig.node.jsonをそれぞれ noEmit、incremental falseで検証し成功。
- ESLint: 成功。
- npmは通常PATH上にないため、同梱Nodeから既存ローカルツールを実行した。
- 最初のVitest起動はsandboxのchild process制約（spawn EPERM）で失敗。制限外で再実行し、3ファイル10件が合格した。
- tsc -b は既存tsbuildinfoへの書込み権限で失敗したため、各projectを --noEmit --incremental falseで再検証した。型検査の成功と、build出力の書込み可否は区別している。
