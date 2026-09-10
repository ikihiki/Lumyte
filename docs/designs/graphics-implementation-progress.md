# Graphics 実装進捗

37 ADR の目標設計に対する実装状況を記録する。最初の統合完了条件は [ADR 0034 の段階 0](../adr/0034-render-pass-categories.md#実装順と完了条件) にある起動 → Clear／Copy／Output → 提出結果 → 回収 → 終了であり、以下のメモリ・転送基盤だけで達成したとは扱わない。

## 第1段階: Native メモリ基盤

2026-09-10 に [Native device](../adr/0002-native-graphics-api.md)、[Memory Allocation](../adr/0003-native-memory-allocation-api.md)、[Linear Data](../adr/0004-native-linear-data-api.md) の実装を開始した。

| 範囲 | 実装と確認内容 |
| --- | --- |
| 公開契約 | `Lumyte.Graphics.Native` に device options、capabilities の型、native 例外、memory kind、opaque requirement、heap、region、range を追加。`INativeGpuBackend` は実装済みのメモリ操作だけを宣言する |
| Heap | `CreateGpuHeap` は純粋な backing allocation。resource、address、配置 registry を内包しない。requirement 列から native allocation の引数を構成する |
| Region | 指定 heap offset に独立した線形 resource を配置し、実 GPU address と CPU mapping を提供。破棄しても heap は解放しない |
| Range | region 相対の offset と size を持ち、Slice と address 計算で範囲外・整数 overflow を防ぐ。heap offset を再加算しない |
| DirectX 12 | `DirectX12Backend` が device を直接生成し、Device10、Enhanced Barriers、SM6.6 を要求。`ID3D12Heap` と独立した placed buffer を使い、region ごとに Map／Unmap する |
| Vulkan | `VulkanBackend` が Vulkan 1.4 と必須拡張・feature を要求。memoryTypeBits の交差から allocation を選び、bind した buffer の device address を使う。CPU mapping は coherent memory に限り、同一 allocation の mapping を利用 region 間で共有する |
| 所有・同期 | caller が resource → heap → backend の解放順、GPU 利用終了、同一 heap／region の host 操作の直列化を保証する。backend は application resource の自動破棄、暗黙の待機、全 resource の寿命追跡を行わない |

DirectX 12／Vulkan は既存 project 内へ新しい実装を追加した。旧 `IGpuBackend` と既存の描画実装は未移行として残り、新 API から呼び出す adapter は作っていない。`Lumyte.Graphics.Native` は共通 `Lumyte.Graphics` の code format と device loss 例外などの基礎型を参照する。

公開実行機能の capability はすべて false としている。GPU address を取得できることや初期化時に native feature を有効にすることだけで、shader、descriptor、mesh command の実装完了を報告しない。

CPU 書き込みまでの実行可能な利用例は [Native project の README](../../src/graphics/Lumyte.Graphics.Native/README.md) に置く。

## 検証

### 初回実装時: driver 591.86

- Native の range 単体試験: 21 件成功。region 相対 address、部分 range、論理容量、空範囲、範囲外と overflow を確認。
- DirectX 12 の新規実機試験: 6 件成功。非ゼロ heap offset の独立 mapping、region 相対 address、region 破棄後の heap 再利用、3 memory kind の mapping を確認。
- Vulkan の新規試験: 14 件成功、実機 allocation 試験 2 件は必須拡張不足の理由を付けて skip。version／extension の初期化条件、native error、memory type 選択、予約量と実 device の不足要件診断を確認。
- ソリューション全体: 25 test project、1,074 件成功、失敗 0、Vulkan の上記 2 件を skip。`dotnet test Lumyte.slnx` で全体をビルド・試験し、初回の待機を受けて同じ成果物を `dotnet test Lumyte.slnx --no-build --no-restore --blame-hang-timeout 2m --blame-hang-dump-type none` で再確認した。最終実行は終了コード 0。
- 文書: 37 ADR、43 文書の章構成・ローカルリンク・番号依存を検査し、Native README のリンクも確認。`git diff --check` は問題なし。

初回の全体実行では、既存 `Lumyte.Resources.Tests` が 64 件の完了後に約 6 分間終了せず、該当 test host を停止した。単独再実行は 66 件すべて成功し、同じビルド成果物を使った全体再実行でも Resources の 66 件は成功した。初回の待機原因は特定しておらず、今回 Resources の source／test は変更していない。

初回の実機は NVIDIA GeForce RTX 2080、driver 591.86、Vulkan API 1.4.325。この driver は `VK_KHR_shader_untyped_pointers` を提供するが、必須の `VK_EXT_descriptor_heap` と `VK_KHR_device_address_commands` を提供しなかった。新 Vulkan backend が不足名を含む `NotSupportedException` で初期化を拒否することを確認した。

この環境には `VK_LAYER_KHRONOS_validation` もなかった。手書きの Vulkan feature 構造体は [Vulkan-Headers v1.4.362](https://github.com/KhronosGroup/Vulkan-Headers/blob/v1.4.362/include/vulkan/vulkan_core.h) の layout／sType を参照しているが、ソースの確認は実機 conformance の代わりにはならない。

この段階の DirectX 12 試験も配置と CPU mapping の確認であり、新 API の GPU copy や shader 実行で読み戻した結果ではない。

### ドライバ更新後の再確認: driver 616.92

2026-09-10 に同じ RTX 2080 で再確認した。新しい OS プロセスの `nvidia-smi`／`vulkaninfo` により、driver 616.92、device の Vulkan API 1.4.351、loader 1.4.341 を確認した。

| 拡張 | 更新後の提供状況 |
| --- | --- |
| `VK_EXT_descriptor_heap` | revision 1 |
| `VK_KHR_device_address_commands` | revision 1 |
| `VK_KHR_shader_untyped_pointers` | revision 1 |
| `VK_KHR_unified_image_layouts`（任意） | revision 1 |
| `VK_EXT_mesh_shader`（任意） | revision 1 |

`VulkanBackend.Create()` が必須 feature の確認・有効化を含めて成功した。新規 Native conformance 試験 3 件はすべて成功し、以前 skip した allocation 試験 2 件も実行できた。複数 region の共有 mapping と独立した書込み範囲、region 破棄後の heap 再利用、GpuOnly region の実 GPU address を確認した。

続いて `dotnet test src/graphics/Lumyte.Graphics.Vulkan.Tests/Lumyte.Graphics.Vulkan.Tests.csproj --no-build --no-restore` に診断 logger と無応答検出を付けて実行し、既存試験を含む **174 件成功、失敗 0、skip 0**、終了コード 0 を確認した。ソース変更はなく、ビルド済み成果物で更新後 driver を検証した。初回のソリューション全体の件数と、今回の Vulkan 限定の再確認結果は区別する。

raw 診断は `artifacts/diagnostics/vulkan-driver-recheck/`、実機試験の TRX は Vulkan test project の `TestResults/` に保存した。列挙された GPU は RTX 2080 一台だった。`VK_LAYER_KHRONOS_validation` は引き続き未導入のため、validation 有効時の確認は残る。descriptor／address-command／shader の実行本体は未実装であり、今回確認したのは対応 feature の有効化とメモリ基盤の動作である。

## 外部 backend の追加と assembly 境界

`InternalsVisibleTo` はテスト assembly だけに限定した。Native の DirectX 12／Vulkan 向け指定を削除し、heap・region・compatibility を public abstract 基底型と protected constructor に変更した。各 backend は非公開派生型に native handle、所属 device と破棄状態を保持する。共通 `Owner`／`BackendData` は削除し、object 自体を identity とする。requirements の値は public constructor で作れる。

`Lumyte.Graphics.Native.Tests` 向けの内部公開も不要になったため削除し、外部 assembly と同じ条件で backend を実装する consumer test を追加した。既存 range 試験と合わせて 22 件が成功した。DirectX 12 の Native メモリ・所有権試験 10 件、Vulkan の新規契約・メモリ・所有権試験 23 件も成功した。別の派生型だけでなく、同じ実装の別 backend instance の object も拒否する。

既存 `Lumyte.Graphics` → `Lumyte.Graphics.RenderGraph` の本体向け内部公開も削除した。RenderGraph 専用の `GpuRetirementQueue`／`GpuSubmissionToken`／`GpuSubmissionException` は RenderGraph assembly と namespace に移し、内部操作をその中へ閉じた。共通の `GpuDeviceLostException` は Graphics に残す。arena の借用 `Backend` identity と `GpuCommandBuffer.AliasingBarrier` は通常の公開契約として整理した。これは既存描画系の assembly 依存整理であり、新しい機能 RenderGraph の実装完了を示さない。

ソースと project の検索で本体向け内部公開は 0 件、残る 8 指定はすべて test assembly 向け。方針を repository の `AGENTS.md` と ADR にも記録した。`dotnet test Lumyte.slnx` に診断 logger と無応答検出を付けて再実行し、25 test project の **1,091 件成功、失敗 0、skip 0**、終了コード 0 を確認した。DirectX 12 は 165 件、Vulkan は 181 件、WebGPU は 154 件を含む。文書の章構成・ローカルリンク・番号依存と `git diff --check` も問題なし。

## 第2段階: Native Texture の明示配置

2026-09-10 に [Texture API](../adr/0005-native-texture-api.md) の requirement・配置・独立破棄を追加した。

| 範囲 | 実装と確認内容 |
| --- | --- |
| 公開契約 | dimension、usage、description、opaque handle と `INativeGpuBackend` の3操作を追加。handle は public abstract／protected constructor とし、外部 backend が非公開派生型で実装できる |
| 要件と配置 | description を同じ変換で native requirement 取得と生成に使用。texture の compatibility を線形 region と同じ `CreateGpuHeap` へ渡し、caller が offset と予約範囲を管理する |
| DirectX 12 | Device10 を保持し、`GetResourceAllocationInfo2`／`CreatePlacedResource2` を使用。`MutableFormat` と sampled depth は typeless backing、初期 layout は `Undefined`。線形 region・sampled texture・color attachment・depth attachment の混在を実機確認 |
| Vulkan | optimal image の requirement と memoryTypeBits を共通 heap へ統合し、指定 offset に bind。alias-capable とし、適合する2D配列は cube view、3D attachment は slice view のための作成 flag を付ける。線形 region と texture の混在を実機確認 |
| 所有・解放 | texture の破棄は heap を解放しない。同じ heap offset への再配置と、別実装／device の object を拒否する局所的な所有権確認を検証。配置 registry、自動回収と待機は追加しない |

Vulkan は image を `UNDEFINED` で生成し、使用前の `GENERAL` 遷移義務を非公開の texture 状態に保持する。現段階に command／submit の実行経路はなく、`CreateTexture` 内で queue を生成・提出・待機しない。DirectX 12 の初回遷移も今後の command 実装が扱う。公開の初期 state 照会 API は設けない。

focused tests は Native consumer／range が **23 件**、DirectX 12 の texture／memory が **54 件**、Vulkan の texture／memory／初期化契約が **80 件**成功し、失敗・skip はなかった。両 backend で1D／2D／3D、array／mip、MSAA、9 format、MutableFormat を扱う変換と実機配置を確認した。texture を新規追加した試験は DirectX 12 が44件（実機18件）、Vulkan が57件（実機22件）である。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=native-texture-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、25 test project の **1,193 件成功、失敗 0、skip 0**、終了コード 0 を確認した。DirectX 12 は209件、Vulkan は238件、WebGPU は154件、Native は23件を含む。TRX は各 test project の `TestResults/` に保存した。

Native の内部公開は0件で、repository の `InternalsVisibleTo` 9件はすべて test assembly 向け。37 ADR の章構成・番号依存、43文書のローカルリンク、Native README のリンクと staged diff の空白検査も問題なし。

実機は RTX 2080、driver 616.92。これらは配置・破棄の試験であり、texture の描画・転送結果を検証したものではない。Vulkan の Khronos validation layer は未導入のため、その有効時の試験は引き続き未実施である。view、aspect／copy footprint、初回 layout 遷移の実行、depth／stencil 別転送と alias の再初期化は未実装として残す。

## 第3段階: Native 転送・提出・同期

2026-09-10 に [Command Recording](../adr/0011-native-command-recording-api.md) と [Submission・同期](../adr/0012-native-command-submission-and-synchronization.md) の転送経路を追加した。

| 範囲 | 実装と確認内容 |
| --- | --- |
| 公開契約 | `NativeGpuQueue`、`NativeGpuCommandBuffer`、`NativeGpuSemaphore` は公開基底型と protected constructor で外部 backend が実装する。MainQueue、copy、barrier、transition／discard、Submit／Wait／IsComplete を追加し、状態照会や独自 resource registry は設けない |
| 転送の値 | 非所有 texture view、単一 aspect の copy footprint、texel 原点／extent、byte pitch を追加。copy は source の byte 数だけを転送し、destination の余剰範囲を変更しない |
| DirectX 12 | CPU 記録を Submit 内で一括変換し、全 command list の終了と回収用管理領域の確保に成功してから一回 Execute。CopyBufferRegion と plane ごとの CopyTextureRegion を使い、明示 layout／discard を enhanced barrier へ写す |
| Vulkan | `VK_KHR_device_address_commands` の copy を記録時に生成。初回 image 参照の直前で native command の区間を分け、Submit 時に必要な `GENERAL` 初期化だけを挿入する。caller の先行 alias barrier を追い越さず、複数 recording の初期化重複も防ぐ |
| Completion | caller-owned timeline と queue 所有の内部 completion を分け、内部 command memory だけを回収する。Vulkan は一回の QueueSubmit2 に内部 signal を含む batch → caller signal の batch を渡し、caller の Wait が内部 signal の完了も保証する |
| Host access | `GpuStage.Host` と `GpuAccess.HostRead/Write` を追加。caller が GPU producer → HostRead の barrier と completion 待機を明示する。coherent mapping や Wait だけで GPU→CPU の visibility が成立するとは扱わない |
| 所有・失敗 | 未提出 recording は自身の native memory を所有し、queue は受理した command memory のみ追跡。提出前失敗で batch の一部を実行せず、一度受理した recording は再利用しない。DX12 で受理後 signal 不能なら device を停止して通常待機を拒否する |

command の Dispose は暗黙待機せず、caller は GPU 完了まで application resource を保持する。backend の終了前には未提出分を含む recording、semaphore と application resource を解放する。queue が未提出 recording を大域的に登録・自動破棄する仕組みは置かない。

Vulkan の単独 depth／stencil discard は、対応 device で `separateDepthStencilLayouts` を有効化する。未対応 device での native 条件は変えず、他方の aspect を勝手に含めて discard しない。depth／stencil の単一 aspect copy と、discard が要求する native feature は別の条件として扱う。

focused tests は Native の consumer／range が **25件**、DirectX 12 の Native memory／texture／commands が **93件**、Vulkan の Native 契約／memory／texture／commands が **121件**成功し、失敗・skip はなかった。今回の追加は Native が2件、DirectX 12 が39件（実機20件）、Vulkan が41件である。

両 backend で region 相対 offset、batch の順序、source より大きい destination の余剰保持、mip／layer／3D slice と pitch、depth／stencil の独立転送、同じ heap 範囲の A → B → A 再利用を確認した。未提出 Dispose、一回提出、拒否された batch の未実行、caller semaphore 破棄後の後続処理も検証した。Vulkan は事前に記録した同じ texture の同 batch／別 batch 使用、部分 layer の discard と depth-only discard 後の他方の内容保持も確認した。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=native-transfer-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、25 test project の **1,275件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は248件、Vulkan は279件、WebGPU は154件、Native は25件を含む。TRX は各 test project の `TestResults/` に保存した。

Native の内部公開は0件で、repository の `InternalsVisibleTo` 9件はすべて test assembly 向け。37 ADR の章構成・番号依存、43文書の334ローカルリンクと17アンカー、Native README のリンク、staged diff の空白検査も問題なし。

今回の実機検証では RTX 2080、driver 616.92 を使用した。DirectX 12 の debug layer は未導入で、`D3D12GetDebugInterface` は `0x887A002D` を返した。Vulkan の `VK_LAYER_KHRONOS_validation` も未導入のため、両 backend の conformance は通常 device で実行した。validation option 自体を無効化する変更や、不足を隠す fallback は追加していない。

実機試験は正常な転送結果と明示的な所有・同期、CPU 変換段階の失敗境界を対象とする。native queue の device loss、DX12 の Execute 後の Signal 失敗、Vulkan の EndCommandBuffer／QueueSubmit2 の memory allocation failure を実 GPU に強制する試験は未実施であり、これらの例外処理はコードレビューで確認した。

## 第4段階: Native render view・descriptor storage

2026-09-10 に [View](../adr/0006-native-view-api.md)、[Bindless の分類値](../adr/0007-native-bindless-api.md)、[Descriptor](../adr/0008-native-descriptor-api.md) の実装を追加した。

| 範囲 | 実装内容 |
| --- | --- |
| 公開拡張契約 | render view と descriptor heap は public abstract／protected constructor とし、外部 backend が非公開派生型を返す。view は Flags、heap は Kind／Capacity を公開し、内部公開指定を増やさない |
| Render view | 非所有 texture view から attachment 用 native view を生成・破棄する。DirectX 12 は1 descriptor の RTV／DSV heap、Vulkan は永続 image view を所有する。親 texture と allocation は延命しない |
| Descriptor storage | resource／sampler の専用 heap を作り、caller の指定 index に texture／buffer／sampler を書く。slot allocator、空き slot の補完、参照先 registry と自動待機は持たない |
| DirectX 12 | shader-visible heap の opaque handle increment で位置を計算する。buffer range は raw SRV／UAV の32-bit element へ変換し、余りや整数変換の切捨てを拒否する |
| Vulkan | `VK_EXT_descriptor_heap` の専用 buffer に、固定 resource stride／sampler stride と reserved tail を確保する。view／address range／sampler 作成情報から直接 descriptor bytes を書き、shader 用の永続 view／sampler を増やさない |
| Heap 選択 | resource／sampler を個別に選択する。DirectX 12 の native heap pair 更新時に他方の選択を保持し、Vulkan の native command 区間切替時には選択済み heap を再設定する |
| 3D slice | 2D attachment view の BaseLayer／LayerCount は depth slice を表す。transition／discard は slice 部分を表現できないため、3D texture には ThreeD view を要求し、mip 全体への暗黙拡張を拒否する |

descriptor の書込みと heap 選択は texture を初期化しない。Vulkan の最初の利用が shader 参照の場合、caller は事前に明示 discard または texture copy で `GENERAL` 初期化を済ませる。参照先の探索や生成時の暗黙提出は追加していない。

Native shader／pipeline はまだ接続していないため、`BufferDescriptors` は両 backend とも false を維持する。Vulkan の固定 slot stride は storage 内に実装したが、shader 生成側への size／alignment／stride の受渡しと混在 heap の lowering は後続段階で接続する。

focused tests は Native が **30件**、DirectX 12 の Native memory／texture／commands／view／descriptor が **163件**、Vulkan の Native 契約／memory／texture／commands／view／descriptor が **169件**成功し、失敗・skip はなかった。今回の追加は Native が5件、DirectX 12 が70件（変換37件・実機33件）、Vulkan が48件（変換24件・実機24件）である。

別 assembly からの public／protected 契約、caller 指定容量・slot・sampler 全項目と大きな range の受渡しを確認した。両 backend では native view／storage の生成・書込み・解放、heap 選択の提出・完了、slice view の誤拡張拒否、別 device／破棄済み object／slot 範囲外の局所契約を確認した。Vulkan の cube-array／anisotropy の任意機能試験も、この GPU では実行できた。TRX は `artifacts/test-results/native-descriptors/` に保存した。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=native-descriptors-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、25 test project の **1,398件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は318件、Vulkan は327件、WebGPU は154件、Native は30件を含む。全体実行の TRX は各 test project の `TestResults/` に保存した。

Native の内部公開は0件で、repository の `InternalsVisibleTo` 9件はすべて test assembly 向け。37 ADR の API・コード配置・使用例・未実装範囲と番号依存、43文書と Native README のローカルリンク、staged diff の空白検査も問題なし。

実機は RTX 2080、driver 616.92。DirectX 12 debug layer と Vulkan Khronos validation layer は引き続き未導入で、有効時の試験は未実施である。DirectX 12 の view／sampler 作成は戻り値のない native API であり、今回の実機成功は呼出し・提出・完了と寿命の確認までを意味する。shader による descriptor の読出し、attachment の clear／描画と read-only aspect の内容保持を検証済みとは扱わない。native allocation failure の強制試験は行わず、失敗時の owned object の解放はコードレビューで確認した。

## 第5段階: Native compute・直接 root・shader 参照

2026-09-10 に [Shader](../adr/0009-native-shader-api.md)、[Compute pipeline](../adr/0010-native-pipeline-state-api.md)、[Command Recording](../adr/0011-native-command-recording-api.md) の実行経路を追加した。

| 範囲 | 実装内容 |
| --- | --- |
| 公開契約 | raw code／entry の shader 値、stage 構成を持つ program、外部 backend が派生できる compute pipeline handle、生成・破棄・選択と直接／間接 dispatch |
| Root | 各呼出しが caller の byte 領域を消費し、DX12 は CPU 記録から root constants、Vulkan は記録時に push data へ渡す。root 用 GPU buffer、Parameter Data の生成・解釈・upload は追加しない |
| DirectX 12 | 同期生成する native compute PSO、64 DWORD の固定 root signature、直接 heap indexing と ExecuteIndirect。shader に使用しない場合も caller が resource／sampler の両 heap を選択し、native 変換は heap → root signature → constants → dispatch の順とする |
| Vulkan | descriptor-heap flag と null layout の compute pipeline、vkCmdPushDataEXT と vkCmdDispatchIndirect2KHR。texture 初期化で native command 区間を分ける場合に compute pipeline と選択済み heaps を再設定する |
| Shader ABI | DX12 は b0／space0 の直接 constants と descriptor index。Vulkan は実 GPU pointer と固定 slot stride の mixed descriptor heap。Slang 2026.17 の unified stride を使い、特定 GPU の descriptor size を artifact に固定しない |
| Capability／limits | 両 backend の BufferDescriptors、Vulkan の RawShaderPointers を有効化。root size、dispatch 各軸・積、Vulkan の descriptor size／alignment／stride を公開し、DX12 の opaque handle increment は byte stride にしない |
| 所有と検証 | pipeline は caller-owned で、生成後に caller の raw code を再利用できる。root の最大 size・dispatch 数・shader の native 条件を独自に再検証せず、値の切捨て防止、論理 range と所有状態だけを確認する |

DirectX 12 は従来の SM 6.6 と Enhanced Barriers に加え、heap 直接 indexing が要求する Resource Binding Tier 3 を初期化時に確認する。固定 root signature の両 heap flags に従うため、使わない heap を backend 内部で代用したり、shader reflection で省略条件を推定したりしない。

Slang 2026.17 で生成した Vulkan 用 artifact を内蔵 SPIR-V validator で検証し、直接 PushConstant、物理 pointer、descriptor heap と共通 stride を確認した。試験は準備済み SPIR-V を使用し、製品 backend に compiler・package loader 依存を追加しない。DirectX 12 は既存のテスト用 DXC で raw DXIL を用意する。

新規テストは Native が32件、DirectX 12 が13件、Vulkan が15件で、計60件を追加した。focused tests は Native の全62件、DirectX 12 の Native 名を持つ関連150件、Vulkan の compute 15件が成功し、失敗・skip はなかった。実GPUで64 byte を超える root と呼出し後の CPU 値変更、GPU が書いた indirect 引数、非ゼロ slot／range offset、texture／buffer／sampler の参照、storage texture 書込みと heap 切替を確認した。Vulkan は初回 texture 区間をまたぐ pipeline／heap の再設定、unaligned な shader byte slice と同期生成後の入力再利用も確認した。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=native-compute-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、25 test project の **1,458件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は331件、Vulkan は342件、WebGPU は154件、Native は62件を含む。TRX は各 test project の `TestResults/` に保存した。初回は制限付き実行環境でビルド出力が進まず停止し、その実行が残した MSBuild 子プロセスだけを回収してから再実行した。

37 ADR の API・コード配置・使用例・未実装章、44文書の340ローカルリンクと18アンカー、208件の番号依存を確認した。本体向け `InternalsVisibleTo` は0件で、9指定はすべて test assembly 向け。Native は内部公開を使わない。独立したコードレビューと空白検査でも問題はなかった。

実機は RTX 2080、driver 616.92。DirectX 12 debug layer と Vulkan Khronos validation layer は未導入のため、有効時の検証は未実施である。native pipeline の allocation failure と device loss を強制する試験は行わず、失敗時の owned object 解放はコードレビューで確認した。

## 未実装と次の順序

1. Native raster pipeline と rendering／draw／indexed draw を接続し、render view の attachment 使用と描画結果を確認する。DirectX 12 の depth/stencil を含む PSO は実際の Submit 時に解決し、root の直接入力と command で Parameter Data を扱わない方針を維持する。描画用 indirect と残る limits を対応する操作とともに追加する。
2. 独立した Portable API と WebGPU、両系統の Resources・shader・RenderGraph provider、共通 Hosting を実装し、同じ consumer binary で段階 0 を通す。

mesh／amplification、保持型 Model／2D、Slang toolchain の製品実装への統合と既存描画系の移行は、これらの後続作業である。driver 更新により、この PC で Native Vulkan の初期化と転送基盤を実機検証できるようになった。各描画機能の実装後には、その機能を使う conformance 試験を追加する。
