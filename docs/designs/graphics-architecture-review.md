# Graphics ADR 全体レビュー

確認・修正日: 2026-09-10。対象は ADR 0001〜0037 と配置一覧。初回レビューの指摘と、その後に承認された ADR 修正を記録する。設計の正本は各 ADR とし、ここでは API の実装、GPU 適合試験、性能測定の完了を示さない。

機能 RenderGraph を共通境界とし、その下を Native／Portable に分ける方針を維持した。初回レビューで指摘した API の不足三点、失敗の接続一点、表記不一致一点と、改善三点を ADR に反映した。

P1 は対象機能の成立を妨げる問題、P2 は API や失敗処理を実装する前に確定すべき問題、P3 は局所的な整理とする。未実装と明記された機能や、初期要件から明示的に除外された機能は、それだけでは欠陥に数えない。

## 対応状況

| 項目 | ADR への反映 | 実装・検証の状態 |
| --- | --- | --- |
| R1 alias 再利用 | [0011](../adr/0011-native-command-recording-api.md) に DiscardTexture を追加し、0003／0005 と 0013／0014／0031 の所有・同期へ接続 | 設計反映済み。A → B → A と未提出失敗の実機試験は未実装 |
| R2 copy aspect | [0005](../adr/0005-native-texture-api.md) に単一 Aspect と転送 bytes／pitch、0013／0014 に plane／aspect 写像を定義。0006 は型の正本を参照 | 設計反映済み。depth／stencil の独立転送試験は未実装 |
| R3 cache 世代の結果 | [0031](../adr/0031-native-render-graph-implementation.md)／[0032](../adr/0032-portable-render-graph-implementation.md) に RegisterContent／TryUseContent と provider 所有の ticket を定義 | 設計反映済み。culling・受理前失敗・結果依存・失効中の保持の試験は未実装 |
| R4 非同期診断 | [0026](../adr/0026-command-submission-and-synchronization.md)／[0027](../adr/0027-webgpu-backend-implementation.md) で GPU 使用終了と成功を区別し、0029〜0033 の upload／export／cache へ接続 | 設計反映済み。上位への統合は未実装。下位の診断・完了の検証状況は [実装進捗](graphics-implementation-progress.md) を参照 |
| R5 stage 名 | [0023](../adr/0023-shader-design-and-api.md) を GpuShaderStage.Pixel（WebGPU fragment）へ統一 | 文書修正済み。新 API の実装は別作業 |
| I1 規範の集約 | [0030](../adr/0030-render-graph-api.md#所有同期失敗) を所有・snapshot・提出結果の正本とし、0031〜0033／0035／0036 の重複を整理 | 文書反映済み |
| I2 初回・resize の例 | [0030 の使用例](../adr/0030-render-graph-api.md#使用例) に既存 BeginFrameAsync による初回取得と、形状不一致の通知からの再構築を追加 | 文書反映済み。取得間の resize 競合と保持の試験は未実装 |
| I3 最小統合 | [0034 の段階 0](../adr/0034-render-pass-categories.md#実装順と完了条件) を起動 → Clear／Copy／Output → 提出結果 → 回収 → 終了に固定 | 文書反映済み。統合実装・実測は未実施 |

以下は初回レビュー時の問題と、採用した修正方針を残す。未修正の一覧ではない。内容世代の失効については追加の相互レビューを行い、GPU 受理前の Build／記録も停止・破棄までは取得済み保持を失わないよう補足した。

## レビュー後に追加した設計

2026-09-14 に [ADR 0028](../adr/0028-resource-utilities.md) の utility を arena／pool の明示的な貸出・返却へ限定し、依存関係が必要な寿命、提出 token、遅延回収と非同期転送を [ADR 0029](../adr/0029-resource-management-api.md) の上位管理層へ集約した。二つの Resources assembly は維持する。utility が貸出の返却可否を推測せず、上位が全利用と必要な依存を終了した後に返却する。utility の実装完了と、上位管理層・RenderGraph の未実装を区別する。

Native の mesh／amplification 対応を [ADR 0002](../adr/0002-native-graphics-api.md)、[0009〜0015 の shader・pipeline・command・backend 契約](../adr/0011-native-command-recording-api.md) と [Model 描画](../adr/0035-model-render-passes.md) に追加した。Native の任意機能とし、共通 AddModelPass は同じ入力を維持する。Portable は自身の vertex／indexed 描画経路を使い、mesh command のエミュレーションを追加しない。

[ADR 0033](../adr/0033-feature-render-passes.md#slang-source-の部分共有) では Slang の計算 module を部分共有し、entry・resource・root・GPU ABI・package は二系統に分ける。その後の [Slang／WGSL 直接 root の実験](slang-wgsl-root-investigation.md) で、通常の push constant 宣言は直接入力に変換されない一方、Slang 2026.17 の Portable 専用 accessor から生成した WGSL は GPU で正しく実行できると確認した。[Portable の採用条件](../adr/0023-shader-design-and-api.md#slang-の共有範囲と採用条件) を固定版と局所的な相互運用機能の採用へ更新した。未対応機能の直接 WGSL authoring は残す。toolchain 統合、各 pass の GPU 実装・性能確認は未実施であり、最小実験の成功と区別する。

## 初回レビューの指摘と修正方針

### R1 — P1: Vulkan の alias 再利用に必要な再初期化が表現できない

対象: [ADR 0003 の重複配置と再利用](../adr/0003-native-memory-allocation-api.md)、[ADR 0014 の image 初期化](../adr/0014-vulkan-backend-implementation.md)。

Native heap は重なりと再利用を caller に委ねる一方、Vulkan の `UNDEFINED → GENERAL` は新規 image の初回だけとしている。既存 image A と別の解釈を持つ image B または buffer を同じ領域へ置き、A の利用後に B が書き込み、再び A を初期化して利用するケースを表現できない。

Vulkan では、その alias 書込みによって内容が undefined になった image subresource は、利用前に layout を再遷移させる必要がある。global memory barrier だけで置き換えることはできない。[Vulkan Memory Aliasing](https://docs.vulkan.org/spec/latest/chapters/resources.html#resources-memory-aliasing)

推奨する修正は、caller が alias 切替点を明示する限定的な discard／再有効化操作を定義すること。Vulkan は初期 layout から GENERAL への遷移へ接続し、DX12 側も必要な初期化との対応を定める。backend が全 resource の重なりや状態を追跡する必要はない。当面提供しない場合は、既存 image を再有効化する alias 利用を未対応として制限を明記する。

完了確認では、同じ領域の A → B 書込み → A の再初期化を明示操作で実行し、未提出で失敗した場合も再初期化を完了扱いにしないことを確認する。

### R2 — P2: Native texture copy が depth／stencil を選択できない

対象: [ADR 0005 の NativeGpuTextureCopyFootprint](../adr/0005-native-texture-api.md)、[ADR 0011 の copy API](../adr/0011-native-command-recording-api.md)。

footprint は mip、layer、origin、extent、pitch を持つが、aspect／plane を持たない。depth と stencil の両方を含む texture では、どちらを upload／readback するかを指定できない。これは staging の配置計算では補えない入力の不足である。

Vulkan の address-based image copy も単一 aspect を要求する。[VkDeviceMemoryImageCopyKHR](https://docs.vulkan.org/refpages/latest/refpages/source/VkDeviceMemoryImageCopyKHR.html)

推奨する修正は、footprint に単一の Color／Depth／Stencil を指定する Aspect を追加し、その転送 bytes と pitch の意味を定めること。[Portable の footprint](../adr/0019-texture-api.md) には既に aspect があり、二系統の型を共通化しなくても意味を揃えられる。

完了確認では、depth と stencil の異なる値を独立して転送し、選択しない aspect を変更しないことを backend の適合試験で確認する。

### R3 — P2: pass の GPU cache へ提出結果を伝える SPI が足りない

対象: [ADR 0031 の世代公開と Retain](../adr/0031-native-render-graph-implementation.md)、[ADR 0032 の世代公開](../adr/0032-portable-render-graph-implementation.md)。

両 provider の契約は、内部 graph へ copy を登録しただけでは GPU 内容世代を公開せず、queue 受理後は書込み completion と依存を引き継ぐとしている。この原則は妥当である。しかし pass 作者の入口は BuildAsync、Retain(IDisposable)、DisposeAsync までで、最終提出の受理結果と completion を、その pass が所有する cache に渡す経路が定まっていない。

例えば A が V2 への更新を内部 graph へ登録した後、B の BuildAsync または最終 Submit が失敗すると、A は V2 の公開可否を判断できない。Retain の Dispose だけでは未提出失敗と GPU 完了を区別できず、既存 reader に順序付ける必要がある更新を private な事前 upload に置き換えることもできない。

推奨する修正は、provider が管理する GPU 内容世代の登録と確定を pass SPI に一つ定義すること。受理時の completion／依存、未提出失敗時の破棄、内容を保証できない失敗を provider が確定し、pass の cache が参照できるようにする。consumer 向けの登録や command の状態公開を増やす必要はない。

完了確認では、後続 pass の構築失敗、Submit の失敗、受理済みだが未完了の世代共有、古い snapshot の再提出を別々に試験する。GPU 未完了の世代を共有する場合と、まだ提出されていない世代を混同しないことが要点になる。

### R4 — P2: WebGPU の非同期診断と成功 completion の対応を確定する

対象: [ADR 0027 の Completion と失敗](../adr/0027-webgpu-backend-implementation.md)、[ADR 0026 の提出後の失敗](../adr/0026-command-submission-and-synchronization.md)。

現行 ADR には「GPU 成功を確認できない状態を成功完了へ変換しない」という原則があるため、単純な矛盾ではない。ただし同期 Submit と、非同期の shader／pipeline／encode／submit 診断を、どの提出の成功・失敗へ結び付けるかが未定義である。

WebGPU の object 作成は内部的には非同期であり、error scope と queue 完了の Promise の解決順には一般的な保証がない。onSubmittedWorkDone だけで成功を確定すると、実行されなかった upload や無効な出力を上位が利用できる余地がある。[WebGPU の非同期作成](https://gpuweb.github.io/gpuweb/#invalid-internal-objects-and-contagious-invalidity)、[Promise Ordering](https://gpuweb.github.io/gpuweb/#promise-ordering)

推奨する修正は、生成 object と batch の診断を当該提出へ帰属させ、queue の到達による安全な回収と、処理の成功判定を区別すること。validation／pipeline の失敗も上位 completion の失敗へ接続し、失敗した export／GPU 内容世代を公開しない。この処理は runtime の診断を伝えるものであり、独自 validator の追加ではない。

完了確認では、error scope と queue completion の順序を入れ替えられる fake を使う。実 WebGPU でも無効な pipeline／command の結果を成功した package upload として返さないことを確認する。

### R5 — P3: shader stage の Pixel／Fragment 表記を統一する

対象: [ADR 0023 の GpuShaderEntryPoint](../adr/0023-shader-design-and-api.md)、[ADR 0021 の GpuShaderStage](../adr/0021-binding-layout-api.md)。

共通 enum は Vertex／Pixel／Compute だが、entry point の説明だけが Vertex／Fragment／Compute になっている。独立した enum を増やす意図でなければ、GpuShaderStage を使用すると明記し、Pixel（WebGPU fragment）へ揃える。実装や生成器が別の公開型を作る前に直せる局所的な不一致である。

## 採用した改善

### 共通規範の重複を減らす

snapshot、保持、受理前後の失敗、cache 世代の一般規則が 0030・0031・0032・0033・0035・0036 に繰り返されている。原則は 0030、機能作者の共通 SPI は 0033、系統固有の接続は 0031／0032、個別 cache の依存は各機能 ADR という分担にすると更新漏れを減らせる。各文書の API 章と小さい使用例は維持する。

### 持続 plan の初回・resize を既存 API の例でつなぐ

[ADR 0030](../adr/0030-render-graph-api.md) の初回取得は BeginFrameAsync → TargetResource.Description → 未提出 frame の Dispose で示した。probe と次の取得の間にも resize は起きるため、helper が取得した target と plan の形状不一致を、準備前に GpuPresentationTargetChangedException.Description で通知する契約も追加した。その description で plan を作り直して次の取得から再試行する。他の提出失敗を再試行の対象にせず、native resource の照会 API も追加しない。

### 実装の最初の到達点を小さく固定する

[ADR 0034 の実装順](../adr/0034-render-pass-categories.md) に段階 0 を追加した。最初の統合は「起動 → Clear／Copy／Output → 提出結果 → 回収 → 終了」を一度ビルドした consumer で通す。続いて同じ plan の bindings 更新と失敗経路を確認し、その上で保持型 Model／2D の最適化へ進む。全 PBR・文字・atlas の最適化を最初の受入れ条件にしない。

## 維持してよい方針

- 共通 API を機能 RenderGraph に置き、shader・GPU 入力・pass 本体を二系統に分けること。
- Native の統一 allocation と caller の明示配置、Portable の Buffer／Texture 一体生成。Portable に Bindless emulation を戻す必要はない。
- root を直接入力し、Parameter Data の準備を pass に置き、実 raster PSO を Submit 時に解決すること。
- ロードを Lumyte.Resources、ECS／node 評価を利用側に保つこと。
- 完全な不変 snapshot と構造共有、plan／bindings の分離。性能は実測し、60 FPS を設計だけで保証しないこと。
- Generic Host を composition と終了の境界にし、同期 DI factory で GPU 初期化を待たず、毎 frame の service 解決をしないこと。
- native／WebGPU が検証する条件を複製せず、Lumyte 自身の所有・世代・宣言だけを確認すること。

これらの原則に反して低レベル backend に資産探索、scene の差分検出、自動 resource 延命を追加する必要はない。R1 の明示再有効化と R3／R4 の結果伝達も、それぞれの層が所有する範囲へ限定できる。

## 確認範囲と限界

| ADR | 確認した内容 |
| --- | --- |
| 0001・README | 共通境界、座標・値型、配置、project の依存方向 |
| 0002〜0008 | Native device、heap、linear data、texture／view、bindless／descriptor |
| 0009〜0015 | Native shader／PSO、記録・提出・同期、DX12／Vulkan 実装、shader toolchain |
| 0016〜0027 | Portable resource／binding、shader／pipeline、記録・提出と WebGPU の失敗 |
| 0028〜0032 | utility／manager、scope／pin／batch、plan／bindings、provider の cache と所有 |
| 0033〜0036 | 機能契約、標準画像処理、汎用 Model／glTF 能力、2D／glyph、保持と部分更新 |
| 0037 | 起動順、DI の所有、途中失敗、consumer の停止、GPU と window の終了 |

以前指摘した TwoD／Text の循環と Portable shader／pipeline の循環は、現在の Primitives と低レベル shader 構成値の分離で対処されている。Host の起動失敗・停止順・二重破棄・window 保持についても既存の契約があり、同じ問題として再掲しない。

全 37 ADR に API・コード配置・使用例・未実装範囲があることと、ローカルリンク・番号依存を修正後も検査した。文書検査の成功は GPU 実装の正しさや実機対応の証明ではない。外部仕様に依存する R1／R2／R4 は一次仕様を確認し、Lumyte の操作・結果伝達と仕様上の要求を区別して記載した。変更は ADR と本レビューの文書に限り、ソースコード変更、dotnet test、GPU 実機試験は実施していない。
