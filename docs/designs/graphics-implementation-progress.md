# Graphics 実装進捗

37 ADR の目標設計に対する実装状況を記録する。最初の統合完了条件は [ADR 0034 の段階 0](../adr/0034-render-pass-categories.md#実装順と完了条件) にある起動 → Clear／Copy／Output → 提出結果 → 回収 → 終了であり、以下の低層基盤だけで達成したとは扱わない。各段階の検証件数は実施当時の記録である。2026-09-14 に旧共通 backend、旧 RenderGraph、旧 Library／TwoD／Text、旧 shader system と専用テストを削除した。現行機能とテストの結果は末尾の更新に記録する。

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

DirectX 12／Vulkan は既存 project 内へ新しい実装を追加した。この初回段階では旧 `IGpuBackend` と旧描画系を残していたが、2026-09-14 に削除した。新 API から呼び出す adapter は作っていない。`Lumyte.Graphics.Native` は共通 `Lumyte.Graphics` の code format と device loss 例外などの基礎型を参照する。

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

## 第6段階: Native vertex raster・indexed draw

2026-09-10 に [Pipeline State](../adr/0010-native-pipeline-state-api.md) と [Command Recording](../adr/0011-native-command-recording-api.md) の vertex 描画を追加した。

| 範囲 | 実装内容 |
| --- | --- |
| 公開契約 | 外部 backend が派生する raster pipeline handle、blend／raster／depth-stencil の値、viewport／scissor、color／depth-stencil attachment を追加。vertex／pixel の raw program から pipeline を生成・破棄する |
| 描画 | Begin／EndRendering、直接 Draw／DrawIndexed、16 byte／20 byte の一件の間接 draw を追加。native の index fetch へ Uint16／Uint32 と range を渡し、vertex data は shader が取得する |
| Root と引数 | 各 work の root を直接渡し、呼出し後の CPU 入力再利用を許す。firstVertex／firstIndex／signed baseVertex／firstInstance を native command へそのまま渡し、command による Parameter Data の解釈・upload・root への追加補正は行わない |
| DirectX 12 | 論理 raster pipeline が description と raw DXIL を保持し、実際の draw の Submit 内で不足する PSO を生成する。depth/stencil の動作を key にし、pipeline が破棄まで所有する。viewport／scissor／stencil reference／root は key に含めない |
| Vulkan | descriptor-heap flag と null layout の graphics pipeline を作成時に完成させる。depth/stencil を dynamic state、index／indirect を device-address command へ写し、attachment 初期化に伴う native command 分割後も pipeline／heap を再設定する |
| Attachment | 最初の color、または depth/stencil の mip 領域を rendering の大きさとする。開始時に viewport／scissor と無効の depth/stencil を設定する。read-only は view flags から導出し、writable aspect の load／store だけ caller が指定する |
| 所有と検証 | pipeline／view／resource の寿命は caller が管理する。別 device、破棄済み object、rendering の局所状態、論理 range と値を失う変換を確認し、shader／format／state の native validator は複製しない |

DirectX 12 は front/back の stencil reference を個別に渡すため `D3D12_OPTIONS14.IndependentFrontAndBackStencilRefMaskSupported`、native render pass のため `D3D12_OPTIONS18.RenderPassesValid` を初期化時に要求する。read-only aspect は read-only binding flags と Preserve、存在しない aspect は NoAccess に写す。raw shader の storage 書込みを許す `ALLOW_UAV_WRITES` を指定し、shader の resource 参照を reflection で復元しない。

Vulkan は必要な attachment をすべて `vkCmdBeginRendering` の前に初期化し、read-only aspect を GENERAL の LOAD／STORE_OP_NONE で保持する。負の viewport height で座標規約を合わせ、shader 生成時に Y を再反転しない。device が提供する ShaderDrawParameters、independent blend、indirect first instance と vertex／fragment storage 書込みの任意 feature を有効化し、Native 全体の新しい必須条件にはしない。

Vulkan の5個の raster／indirect argument fixture は Slang 2026.17 で再生成し、内蔵 SPIR-V validator と再生成前後の hash 一致を確認した。source と再生成手順をテストの `Integration/Shaders/` に置き、製品 backend に compiler 依存を追加しない。DirectX 12 は既存のテスト用 DXC で raw DXIL を生成する。

shader の system-value は target の raw ABI に従う。Vulkan の fixture は `SV_VulkanVertexID`／`SV_VulkanInstanceID` で native index を読む。DirectX 12 はこの GPU が提供する SM 6.8 Extended Command Info を任意機能の試験として使い、非 indexed／indexed と直接／間接の4通りで start vertex 6／base vertex -3、start instance 13、通常の instance ID 0 を GPU buffer への書込みと readback で確認した。両 target の通常の ID が同じ意味であるとは扱わず、command による shader 引数の追加・補正はしない。

連続 render pass の DirectX 12 試験では、color と depth の access を一つの global barrier に OR した場合に `Close` が `E_INVALIDARG` を返した。depth read bit を除いた同一 write mask、同期 scope を All に揃えた場合でも再現した。一方、color と depth を個別の global barrier で指定した場合と、layout を維持する個別 texture barrier では同じ描画結果を得た。試験は依存を個別に記録し、viewport／scissor／depth の reset を検証する元の判定を維持する。backend に自動分割や native validation の複製は追加しない。Microsoft の現行仕様は global barrier の OR 指定を許し、DepthStencilWrite layout と depth read access の共存も認めるため、一般的な禁止規則と断定しない。この GPU／runtime が複合指定を拒否する詳細原因は debug layer 有効時の診断が残る。[Enhanced Barriers](https://microsoft.github.io/DirectX-Specs/d3d/D3D12EnhancedBarriers.html#d3d12_global_barrier)

新規テストは Native 17件、DirectX 12 32件、Vulkan 24件の計73件。直接 root の snapshot、Uint16／Uint32 と region 相対 offset、GPU が生成した間接引数、culling、blend／write mask、depth/stencil、read-only 内容保持、fragment の descriptor 参照を実 GPU で確認した。DirectX 12 は未使用 pipeline の PSO 未生成、固定 state の variant 再利用、PSO 生成失敗で batch 全体を提出しないことも検証した。Native の別 assembly から public／protected 契約だけで raster を実装・利用する consumer test も成功した。

2026-09-13 に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=native-raster-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、25 test project の **1,531件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は363件、Vulkan は366件、WebGPU は154件、Native は79件。TRX は各 test project の `TestResults/` に保存した。連続 render pass の試験は前記の個別 global barrier を使用した最終コードで成功している。

37 ADR の必須章、44文書の344ローカルリンクと20アンカー、208件の番号依存を確認した。`InternalsVisibleTo` 9指定はすべて test assembly 向けで、Native の内部公開は0件。独立レビューと staged diff の空白検査も問題なし。

実機は RTX 2080、driver 616.92。DirectX 12 debug layer と Vulkan Khronos validation layer を有効にした検証は未実施である。全 format／dimension／MSAA の描画組合せ、native allocation failure と device loss の強制試験も未実施であり、正常な実機描画と明示的な所有・同期、CPU 側の失敗境界の確認とは区別する。mesh／amplification の capability は引き続き false とする。

## 第7段階: Native mesh・amplification

2026-09-13 に [Native device](../adr/0002-native-graphics-api.md)、[Pipeline State](../adr/0010-native-pipeline-state-api.md)、[Command Recording](../adr/0011-native-command-recording-api.md) の mesh 経路を追加した。

| 範囲 | 実装内容 |
| --- | --- |
| 公開契約 | `NativeGpuMeshShaderLimits` と nullable な `Limits.MeshShader`、`DispatchMesh`／`DispatchMeshIndirect` を追加。既存の raster pipeline と rendering scope で vertex／mesh を選択する |
| 任意機能 | DirectX 12 の Mesh Shader Tier、Vulkan の meshShader／taskShader を確認し、有効な機能だけを capability に反映する。mesh 非対応でも Native device の生成を拒否せず、emulation は行わない |
| Limits | compute と分けて mesh／amplification の各軸・積の dispatch 上限、mesh workgroup ごとの出力頂点・primitive 数、payload byte 数を公開する。amplification がなければその dispatch limits は null、payload size は0 |
| Root と payload | caller の root を各 work の呼出し中に消費し、AS／MS／PS の直接入力へ渡す。amplification から mesh への payload は shader が生成する。command は Parameter Data や payload 用 buffer を生成・upload しない |
| DirectX 12 | Submit 中の実際の mesh work で MS／任意 AS／任意 PS の pipeline state stream を生成する。既存の pipeline 所有 PSO variants と depth/stencil key を使い、vertex input／IA topology を追加しない |
| Vulkan | 任意の `VK_EXT_mesh_shader` を有効化し、mesh／任意 task／任意 fragment の pipeline を作成時に完成させる。descriptor-heap flag、null layout、dynamic depth/stencil と command 区間切替時の pipeline／heap 再設定を維持する |
| 間接実行 | caller の range 内の3個の uint、計12 byte を一件の native 引数として読む。DirectX 12 は ExecuteIndirect、Vulkan は device-address 版 vkCmdDrawMeshTasksIndirect2EXT を使う。GPU producer と indirect read の依存は caller が barrier で指定する |
| 所有と検証 | 外部 backend は public／protected 契約だけで実装できる。論理 range、表現できない変換と所有状態を確認し、dispatch 数、shader の出力・payload・native state の検証器は複製しない |

amplification を含む program の command 引数は amplification workgroup 数を表し、mesh workgroup 数は amplification shader が決める。amplification がなければ command 引数が直接 mesh workgroup 数となる。単なる compute dispatch の別名とはしない。

DirectX 12 の標準 profile は各軸65,535、積4,194,303、出力頂点256、出力 primitive256、payload16,384 byte とする。積は現行 DirectX-Headers の定数と更新された仕様を採用し、古い mesh 仕様の境界記述との差を [backend ADR](../adr/0013-directx12-backend-implementation.md) に記した。任意の OPTIONS25 による一次元だけの上限拡張は今回の profile に含めない。Vulkan は mesh と task それぞれの実 device properties を使い、DirectX 12 の上限に揃えない。payload と shared／output memory の組合せ条件は native の契約に従う。

Vulkan の fixture は Slang 2026.17 で直接 root、物理 pointer と mesh／task entry を生成する。source と再生成手順を `Integration/Shaders/NativeMesh.slang` と `MESH.md` に置く。DirectX 12 は既存のテスト用 DXC で mesh／amplification DXIL を生成し、製品 backend には compiler 依存を加えない。

実機の RTX 2080、driver 616.92 では両 backend とも mesh と amplification を実行できた。Vulkan が報告した mesh／task の各上限は X=4,194,304、Y/Z=65,535、積4,194,304、出力頂点／primitive 各256、payload16,384 byte であり、そのまま公開する。これらは device が報告した limits であり、上限までの巨大な dispatch を実行した結果ではない。

実 GPU で DirectX 12 は256 byte、Vulkan は80 byte の root を使い、AS／MS／PS の直接入力、amplification payload、記録後に変更した CPU 入力の snapshot、3軸の group 数と一件の間接引数を確認した。line／triangle、pixel なしの depth 描画、vertex／mesh の切替にも対応する。DirectX 12 は個別 mesh／amplification stage の UAV 書込みを明示 barrier 後に読み戻し、Vulkan は texture 初期化による command 区間切替後の descriptor sampling を確認した。Vulkan の7個の SPIR-V は内蔵 validator を通し、再生成前後の SHA256 一致も確認した。

追加テストは Native 6件、DirectX 12 21件、Vulkan 21件の計48件。focused tests は Native 全85件、DirectX 12 の Native 関連203件、Vulkan の Native 関連162件が成功し、失敗・skip はなかった。DirectX 12 では mesh PSO の生成延期、depth/stencil variant の再利用、生成失敗による batch 全体の未実行を確認した。外部 backend の consumer test は内部公開を使わず、直接／間接 command、root の span、64-bit range と nullable limits を受け渡す。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=native-mesh-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、25 test project の **1,579件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は384件、Vulkan は387件、WebGPU は154件、Native は85件。TRX は各 test project の `TestResults/` に保存した。

独立レビューで公開拡張契約、所有・失敗処理、root 入力と native 検証への委譲を確認した。37 ADR の必要章、44文書の348ローカルリンクと20アンカー、208件の番号依存も問題なし。fixture 文書2本は別途確認した。`InternalsVisibleTo` は repository 全体で9指定すべて test assembly 向けで、Native の内部公開は0件。

DirectX 12 debug layer と Vulkan Khronos validation layer を有効にした試験は未実施である。mesh 非対応 device と Vulkan の mesh-only device はこの PC にないため、実機での起動確認ではなく capability／limits の変換と optional baseline の試験で確認した。全 format／MSAA、最大出力・payload の複合条件、native allocation failure と device loss の強制試験は未実施とする。

## 第8段階: Native 非同期 copy・device timeline・CPU の先行

2026-09-13 に [Native device](../adr/0002-native-graphics-api.md) と [Command Submission と同期](../adr/0012-native-command-submission-and-synchronization.md) を拡張し、MainQueue と独立した CopyQueue、GPU wait と CPU timeline 操作を追加した。

| 範囲 | 実装内容 |
| --- | --- |
| 公開契約 | optional `CopyQueue`、device の `CreateSemaphore`、非所有の `NativeGpuTimelinePoint(Semaphore, Value)`、`Submit(commands, signal, waits)` を追加。旧 Native queue の生成・CPU 照会・待機 member は削除する |
| GPU 依存 | 提出は全 wait point を満たしてから batch 全体を実行し、単一 point を signal する。空 command 列の wait／signal 専用提出も可能。全 batch の記録・終了・管理領域確保は最初の GPU wait より前に完了する |
| CPU の先行 | semaphore の `IsComplete`／`WaitCpu`／`SignalCpu` は queue の可変状態から独立。CPU は複数 frame を先行提出し、再利用する slot の最終 consumer だけを待てる。frame slot と先行数は caller が管理する |
| DirectX 12 | DIRECT と COPY queue、各 type の allocator／list、各 queue の内部 fence を使用。queue wait → work → 内部 fence → caller fence の順で積む。最初の native wait 以後の失敗は device を停止して伝播する |
| Vulkan | transfer 専用 family を優先し、なければ Main family の第2 queue、最後に他の対応 family を選ぶ。公開 buffer／image は異なる Main／Copy family の場合だけ CONCURRENT、同 family と CopyQueue なしの場合は EXCLUSIVE。requirements と生成は同じ設定を使う |
| Texture | `GpuTextureLayout.Common` を追加。DirectX 12 は Main で Common へ移す → Copy が wait・転送・signal → Main が wait・次用途へ移す。Vulkan は General／Common を GENERAL へ写し、新規 texture の初期化 producer を consumer より先に Submit する |
| 所有と回収 | 各 queue の private completion が内部 command memory のみを回収し、CPU wait／照会はその list を変更しない。caller semaphore は producer と全 consumer の利用終了まで保持する。root／Parameter Data の契約と application resource の明示寿命を維持する |

CopyQueue の共通用途は線形データと color texture の転送で、depth／stencil と shader work は MainQueue を使う。Vulkan の consumer を未来値待機だけで初回 producer より先に提出する順序は許さない。初期化済み resource／線形データでは、caller が進行と signal 順序を保証して wait-before-signal を使える。device 全体で loss を共有し、片方の queue の停止を他方で通常の未完了として待ち続けない。

実機の RTX 2080、driver 616.92 は Vulkan に graphics／compute／transfer family 0 の16 queue、transfer 専用 family 1 の2 queue、compute／transfer family 2 の8 queue を報告した。今回の CopyQueue は family 1 を選ぶ。これは queue family の報告値と実行経路の確認であり、物理 engine 数、描画との同時実行率や高速化の測定結果ではない。

CPU gate の timeline を GPU に待たせ、未解放の間に3 frame を CopyQueue／MainQueue へ提出してから CPU signal で進める。転送したデータの compute 参照、texture の描画、MainQueue から CopyQueue への readback と最終内容を実 GPU で確認する。別 CPU thread の WaitCpu と Submit の並行、複数待機点、同期専用提出、別 device／破棄済み object と失敗前の GPU wait 未挿入も検証する。sleep や処理速度を同期の判定にしない。

非同期テストの終了時に、既存の `GpuBackendTestGate` が Mutex を取得した thread と別の thread で解放して collection cleanup に失敗した。named Mutex の取得と解放をテスト用の一つの専用 thread に置き、別 thread の Dispose、他 handle からの占有／解放、abandoned Mutex を回帰試験で確認した。製品 backend の CPU wait に worker は追加しない。

追加テストは Native 7件、DirectX 12 17件、Vulkan 26件と共通テスト基盤4件の計54件。focused tests は Native 92件、DirectX 12 の Native 関連220件、Vulkan の Native 関連183件、共通 gate 4件が成功した。Vulkan の queue 選択優先順位を確認する最後の1件は、その後の全体試験で確認した。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=native-async-copy-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、25 test project の **1,633件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は401件、Vulkan は413件、WebGPU は154件、Native は92件、共通 Graphics は152件。TRX は各 test project の `TestResults/` に保存した。

独立レビューで公開拡張契約、GPU wait／CPU 操作と内部回収の分離、device loss、texture 引渡しとサンプルの寿命を確認した。37 ADR の API・コード配置・使用例・未実装章、依存章の182個のリンクが若い番号へ向くこと、44文書の350ローカルリンクと20アンカーを確認した。非同期 copy のfixture文書2本を含めると46文書・352ローカルリンクで、リンク切れはない。`InternalsVisibleTo` は9指定すべて test assembly 向けで、Native の内部公開は0件。空白検査も問題なし。

DirectX 12 debug layer と Vulkan Khronos validation layer を有効にした試験、native allocation failure と実 device loss の強制、全 GPU の queue 構成と性能測定は未実施とする。device loss の伝播は結果の注入で確認する。任意数の queue 作成、専用 compute queue、stage を選ぶ wait、複数 signal、presentation、上位 Resources の自動退役と Portable への移行はこの段階に含めない。

## 第9段階: Portable device・Buffer／Texture 基盤

2026-09-13 に [Portable device](../adr/0016-portable-api.md)、[Resource のメモリ所有](../adr/0017-resource-memory-model.md)、[Buffer](../adr/0018-buffer-api.md)、[Texture](../adr/0019-texture-api.md) と [WebGPU backend](../adr/0027-webgpu-backend-implementation.md) の基盤を追加した。

| 範囲 | 実装内容 |
| --- | --- |
| 公開契約 | 独立した `Lumyte.Graphics.Portable` に `IPortableGpuBackend`、要求 feature／limits、opaque Buffer／Texture、mapped range と runtime 診断を追加。外部 assembly は public／protected 契約だけで実装する |
| Device | `WebGpuBackend.CreateAsync(options)` が WebGPUSharp 0.5.7 同梱 Dawn の instance／adapter／device を所有する。Native backend や既存の `ModernWebGpuApi` を経由しない |
| 直接入力 | WGSL `immediate_address_space` と非ゼロの `MaxImmediateSize` を初期化要件にする。未指定なら adapter の直接入力容量を要求し、有効値は生成した device から読む。固定 byte ABI と buffer fallback は設けない |
| 要求と有効値 | 任意 feature は明示要求し、device で有効な値を返す。要求 limits の null は未指定、0は明示値として扱う。C API の未指定 sentinel と同じ明示値は表現不能として拒否する |
| Buffer／Texture | usage を native descriptor に渡し、必要な memory を含めて生成・破棄する。heap、配置 requirement、GPU address、application resource の registry は持たない。Texture は1D／2D array／3Dと内部の互換 view format 設定を扱う |
| Mapping | native map 完了後の mapped pointer を `MemoryManager` で直接包む。Write は writable memory、Read は read-only memory とし、Dispose で unmap する。保存済み Memory からの Span／Pin 再取得も拒否する |
| 診断 | validation／out-of-memory／internal scope を生成操作ごとに保持する。map は元 Buffer の生成診断と native map の双方を観測し、無関係な object の失敗を混ぜない。操作失敗は `GpuOperationException`、device loss は共有例外へ接続する |

caller は取得済み Span／pointer の利用と mapping を終えてから resource を破棄し、その後に backend を破棄する。すでに取得した Span 自体を .NET で失効させることはできない。map の byte length は `Memory<byte>` の int 長とホストの pointer 幅で表現できる必要があるが、GPU の usage／alignment／size の検証は Dawn に委ねる。二重 map の失敗を理由に、先に成功した mapping を unmap しない。

新しい `WebGpuBackend` は Portable 専用とし、旧 `IGpuBackend` の factory は `Legacy.WebGpuBackend` へ移した。未移行の旧描画系はその factory を使用する。旧実装を新契約の adapter として動かす経路は追加していない。

この PC の Dawn では8 byte／16 byte の直接入力を要求した device の有効値が、ともに64 byteだった。要求 limit は必要容量の下限であり、runtime がそれ以上を有効にすることを許す。実 GPU 試験で要求を満たすことを確認し、interop の単体試験で8／16の要求をそのまま descriptor へ渡すことを確認する。64 byteへ固定する wrapper の実装とは区別する。

並列生成・mapping の試験では、生成と map の全 error scope が完了しても4個の map callback が返らず、無応答検出で終了した。trace では native 呼出しはすべて復帰しており、managed lock の待機ではなかった。同梱 Dawn に `AllowSpontaneous` を指定するだけでは queue event が進行せず、runtime の `spontaneous_queue_events` toggle 単独でも解消しなかったため、instance ごとの `TimedWaitAny` によるイベント進行を実装した。未完了 future だけを一件ずつ有限時間待ち、空なら thread を休止する。公開の非同期呼出し、mapping 結果の判定と並列テストの条件は維持する。

イベント進行と native resource 操作の並行試験では、Dawn の host 同期機能を要求しない構成で access violation も再現した。Dawn の device mutex は `ImplicitDeviceSynchronization` feature の有効時に作られることを公式実装で確認し、native host の device 要求へ追加した。GPU の同期を省略する代替機能とは扱わない。初期化の失敗で遅れて返る adapter／device も回収し、native future の登録や error scope 操作が失敗した場合は、壊れた runtime 接続の再利用を拒否する。

公開契約の consumer test は [Portable.Tests](../../src/graphics/Lumyte.Graphics.Portable.Tests/Lumyte.Graphics.Portable.Tests.csproj)、実 Dawn device を使う試験は [WebGPU の適合試験](../../src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/PORTABLE.md) に置く。実行可能な生成・mapping の例は [Portable README](../../src/graphics/Lumyte.Graphics.Portable/README.md) にある。

追加テストは Portable の公開契約12件、WebGPU の38件で計50件。WebGPU は実 device 24件、制御した非同期診断9件、要求 limit の interop 5件を含む。focused tests はそれぞれ全件成功し、失敗・skip・ビルド警告はなかった。前記の並列 mapping、失敗した二重 map による既存 lease の保全、無効 resource の診断分離も成功した。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=portable-foundation-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、26 test project の **1,683件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は401件、Vulkan は413件、WebGPU は192件、Native は92件、Portable は12件。最後のレビューで追加した event 登録失敗時の device fault 処理も、この全体実行に含む。TRX は各 test project の `TestResults/` に保存した。

独立レビューで外部 backend の実装契約、native future の進行と終了、初期化の失敗、callback の保持・回収、mapping の寿命と診断の分離を確認した。37 ADR の必要章、依存章の182個のリンクが若い番号へ向くこと、46文書の366ローカルリンクと20アンカーに問題がないことを確認した。`InternalsVisibleTo` は10指定すべて test assembly 向けで、Native と Portable の内部公開は0件。staged diff の空白検査も問題なし。

この段階は resource 基盤であり、新しい Portable 経路での shader 実行、GPU copy／描画結果、提出 batch の成功確定は未実装である。Texture の試験も生成・診断・破棄の確認に限る。Browser runtime、実 GPU の allocation failure と device loss の強制試験は未実施とし、診断の到着順と device loss の伝播は制御した Task で検証する。

## 第10段階: Portable View・Binding Layout・Bindings

2026-09-13 に [View](../adr/0020-view-api.md)、[Binding Layout](../adr/0021-binding-layout-api.md)、[Binding](../adr/0022-binding-api.md) と WebGPU backend の接続を追加した。

| 範囲 | 実装内容 |
| --- | --- |
| 公開契約 | 非所有の Buffer range、6種類の Texture view、sampler と attachment の値、immutable layout／binding entry、外部 backend が派生できる opaque handle を追加する |
| 値の計算 | range の Normalize／Slice と view の Normalize は caller が持つ description から省略値と部分範囲を計算する。byte offset と length は64 bit とし、範囲外と算術 overflow を確認する |
| Binding Layout | uniform／read-only storage／storage Buffer、sampled／storage Texture、sampler の型、可視 stage、最小 Buffer size と dynamic offset の有無を native layout へ渡す |
| Bindings | 明示した layout と entry の span を呼出し中にコピーし、native bind group を作成・破棄する。root や Parameter Data の解釈、resource の自動選択・upload は行わない |
| 内部再利用 | view は Texture identity、解決済み description と用途、sampler は description を key にする。生存する Bindings の参照だけを保持し、最後の参照で cache entry と native object を解放する |
| 診断と失敗 | layout、resource、内部 view／sampler と group 自身の生成診断を Bindings に引き継ぐ。途中の生成失敗で取得済み内部参照を戻し、caller の resource 所有は変更しない |
| 検証の分担 | 別 device／破棄済み handle、未知の enum と値を失う native 変換を拒否する。番号重複、offset／size／usage／format と layout の合法性は Dawn の診断を使う |

Texture view の用途は sampled／storage binding の実際の要求から導出し、元 Texture の attachment 用途を継承しない。同じ view 値でも用途が異なれば内部 entry を分ける。論理的な packed depth/stencil format と aspect を、Dawn の depth 専用／stencil 専用 view format へ写す。公開の ViewFormats 列や view の所有 handle は追加しない。

backend は public Normalize を native 呼出し前の validator に使わず、省略値だけを解決する。Buffer の null length と Texture view の未解決 count は native sentinel へ写し、同じ値を明示した場合は表現不能として拒否する。sampler の有効な既定値は `new GpuSamplerDescription()` で得る。構造体のゼロ値を黙って置換しない。

公開契約と consumer の試験は Portable.Tests、実 Dawn device の試験は WebGPU.Tests の `Integration/Bindings/` と `Integration/Views/` に置く。後者は6種類の view、depth／stencil aspect、sRGB reinterpretation、uniform／storage、入力の変更、診断の依存と分離、cache の共有・最終解放・途中失敗の解放を確認する。API の使用例は Portable README と各担当 ADR を更新した。

追加テストは Portable 44件と WebGPU 48件の計92件。focused tests は Portable 全56件と新規 WebGPU 48件が成功し、失敗・skip・ビルド警告はなかった。途中のレビューで range Slice の返却末尾 overflow を修正し、回帰試験を加えた。同じ view 値の sampled／storage 用途分離と、C API sentinel／整数幅との衝突も確認した。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=portable-bindings-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、26 test project の **1,775件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は401件、Vulkan は413件、WebGPU は240件、Native は92件、Portable は56件。TRX は各 test project の `TestResults/` に保存した。

独立レビューで外部 backend の public／protected 契約、内部参照の所有・回収、runtime 診断と検証の委譲、ADR と API／使用例の整合性を確認した。37 ADR の必要章、182個の番号依存、46文書の372ローカルリンクと20アンカーに問題はなかった。`InternalsVisibleTo` は10指定すべて test assembly 向けで、Native と Portable の内部公開は0件。staged diff の空白検査も成功した。

この段階は resource を group に接続するところまでであり、shader の sampling／storage 実行、attachment encoding、dynamic offset の実行、copy／draw、queue completion と Browser は未実装である。allocation failure と実 device loss の強制試験は未実施とする。

## 第11段階: Portable compute・buffer copy・CPU completion

2026-09-13 に [低レベル Shader](../adr/0023-shader-design-and-api.md)、[Compute Pipeline](../adr/0024-pipeline-state-api.md)、[Command Recording](../adr/0025-command-recording-api.md)、[Submission と Completion](../adr/0026-command-submission-and-synchronization.md) を WebGPU の実行経路へ接続した。

| 範囲 | 実装内容 |
| --- | --- |
| 公開契約 | raw WGSL module、entry point、不変の program description、compute pipeline、one-shot command、queue と CPU timeline を追加。外部 backend は public／protected 契約だけを使う |
| Shader／pipeline | module は生成時に runtime が処理し、診断を保持する。compute の論理 pipeline は構成値だけを保存し、dispatch を含む最初の Submit で native pipeline／layout を生成して再利用する |
| 記録 | compute 区間、明示 group と dynamic offsets、直接 root、3軸 dispatch、12 byte の indirect 引数と buffer 間 copy を記録する。root と動的引数は呼出し中にコピーする |
| Root | program の ImmediateSize に一致する全 byte 数を各 work で要求する。不足した末尾の暗黙保持、固定容量への zero-fill、root の buffer 化と Parameter Data の暗黙 upload は行わない |
| 提出 | 全 recording の pipeline と encode を準備してから、単一の native QueueSubmit に渡す。GPU 利用終了を待たず復帰し、内部 command memory は完了後に回収する |
| Completion | initial value と受理済みの signal value だけを観測する。IsComplete は GPU 利用終了、WaitAsync の正常復帰は当該 batch の診断も含む成功を示す |
| 診断と履歴 | module／layout／binding／resource、pipeline と encode／submit の診断を batch に結び付ける。成功は発行値の整数区間へまとめ、過去の失敗診断だけを timeline の寿命まで保持する |
| 失敗と所有 | 受理前の managed 失敗は batch 全体を未提出のまま解放する。受理・完了が不明な interop 障害は device の共有失敗にし、残る内部 command は device 停止時に回収する。application resource の寿命は caller が管理する |

Portable timeline は CPU から一つの queue の進行を観測する契約であり、Native の GPU semaphore wait、CPU signal、CopyQueue は持ち込まない。利用者は複数の batch を先行提出し、再利用する resource の完了だけを非同期に待てる。取消しは一つの await だけを終了し、work や他の待機を取り消さない。ある値の成功を、別の値の処理成功の代用にしない。

shader は手動で準備した WGSL と明示した binding layout／ImmediateSize を使う。backend は shader source、group、usage、alignment と dispatch limits の validator を複製しない。copy の同長、indirect の論理 range の12 byte、完全な root 入力と一回提出など、native へ写す際に失われる Portable の契約だけを確認する。

独立レビューでは、native setImmediates の部分更新で以前の root の末尾を残せる点を確認し、提出前に全 work の root 長を確認するようにした。旧 WebGPU の pipeline 再設定試験が示した zero-fill は旧 wrapper による明示的な固定64 byte設定であり、native setPipeline の仕様とは扱わない。新しい Portable へはその処理を移していない。

追加テストは Portable の公開契約9件、WebGPU の純粋な timeline 試験21件と実 Dawn 試験20件で計50件。focused tests は Portable 全65件、timeline 21件、実 GPU 20件が成功し、失敗・skip・最終ビルド警告はなかった。初回コンパイルで見つかった primary constructor の二重 capture とテストの型推論を修正し、同じ試験条件で再実行した。

実 GPU で8 byte／padding を含む32 byteの root と generic unmanaged 値、呼出し後の入力変更、2本の dynamic offset の binding 番号順、GPU producer が非ゼロ offset に書いた一件の indirect 引数を読み戻した。pipeline の再 bind で root 値を消さず、未使用 pipeline を生成せずに最初の提出で実体化・再利用することも確認した。無効な module／entry／resource の診断が提出へ残り、先頭の valid copy と後半の native-invalid command をまとめた batch が全体として実行されないこと、後続の独立した提出が成功しても先行の失敗診断が残ることを確認した。

timeline の試験は GPU 完了と診断の両到着順、取消し、device loss、接続 Task の失敗、未発行の値、最大 ulong 値と履歴の区間集約を制御した Task で検証する。sleep や GPU の実行速度を判定条件にしない。独立レビューで公開拡張契約、提出・回収、診断の所有と ADR／使用例の整合を確認した。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=portable-compute-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、26 test project の **1,825件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は401件、Vulkan は413件、WebGPU は281件、Native は92件、Portable は65件。TRX は各 test project の `TestResults/` に保存した。

37 ADR の API・コード配置・使用例・未実装章、182個の番号依存、46文書の381ローカルリンクと20アンカーを確認した。`InternalsVisibleTo` は10指定すべて test assembly 向けで、Native と Portable の内部公開は0件。staged diff の空白検査も成功した。

低レベル compute と buffer copy を今回の実装範囲とし、shader package／loader、Slang の製品 toolchain、生成 host 型、raster／texture copy と Browser は後続とする。Texture view／sampler の実 shader 利用、native allocation failure と実 device loss の強制は今回の実機検証に含めない。

## 第12段階: Portable texture copy・raster 描画

2026-09-13 に [Texture](../adr/0019-texture-api.md)、[View](../adr/0020-view-api.md)、[Pipeline](../adr/0024-pipeline-state-api.md)、[Command](../adr/0025-command-recording-api.md) を native host の WebGPU 描画・転送経路へ接続した。

| 範囲 | 実装内容 |
| --- | --- |
| Texture copy | mip／aspect／origin／extent と byte 単位の row/image pitch を持つ footprint、RequiredBytes(format)、Buffer→Texture／Texture→Buffer／Texture→Texture を追加する |
| 固定状態 | topology、strip index format、culling／front face、depth/stencil、color target ごとの blend/write mask、sample count/mask、alpha-to-coverage を immutable raster description に保持する |
| Pipeline | Vertex 一つと optional な Pixel entry、明示 group layout と ImmediateSize から、draw に使う論理 handle の native pipeline／layout を Submit 時に生成・再利用する |
| Render pass | color／depth-stencil の clear/load/store、read-only aspect、MSAA resolve、mip／layer と3D depth slice を明示する。attachment 値の span は記録時にコピーする |
| Work | 直接 draw、Uint16／Uint32 index range、firstIndex・signed baseVertex・firstInstance、GPU buffer による direct／indexed indirect draw を追加する。vertex data は明示 storage binding から読む |
| 動的入力 | viewport／scissor、stencil reference、blend constant、group と dynamic offsets、program 全体の直接 root を記録する。root／dynamic offsets はコピーし、pipeline 再設定で zero-fill しない |
| 所有と診断 | attachment の内部 view を用途付き cache で共有し、GPU 利用終了または提出前の encode 失敗で回収する。resource／view／shader／pipeline と encode の生成診断を当該 batch に保持する |

API は public／protected の実装契約だけで外部 backend を追加できる。論理 range の長さ、Texture 間の extent 一致、native sentinel との衝突、byte image pitch を rowsPerImage へ写す際の整数幅・余りを確認する。GPU が診断できる format、usage、pitch alignment、sample count、shader 適合性と resource 範囲の validator は追加しない。Depth24PlusStencil8 の depth aspect は定義済み Buffer byte 表現がないため RequiredBytes で表現しないが、その転送操作の拒否は Dawn に任せる。

公開契約・固定状態・footprint の新規テスト37件、Dawn の実機テスト38件を追加した。Portable の focused test は全102件、GPU の focused test は新規38件が成功し、失敗・skip はなかった。初回コンパイル時の例外 parameter 名に対する analyzer エラーとテストの enum 名を修正し、コンパイル後の同じ条件で検証した。

実機では8 byteの root を vertex／pixel shader へ直接渡し、再 bind と呼出し後の配列変更を確認した。vertex pulling、dynamic uniform offset、Texture／sampler sampling、mip／array layer／3D slice、MRT、depth/stencil、blend constant、MSAA resolve を pixel の読み戻しで確認した。copy は複数行・複数 image の隙間、非ゼロ Buffer offset、選択 mip／layer、Texture 間の領域、native-invalid pitch・aspect の診断を含む。

indexed draw は16／32 bitの range、firstIndex・baseVertex・firstInstance と、compute が生成した indirect 引数を検証する。独立レビューでテスト用 shader の範囲外 index が robust access によって同じ三角形を作る可能性を見つけ、範囲外を先頭頂点へ写して誤った引数では退化三角形になるようにした。添付 view を作った後の managed encode 失敗、invalid texture／shader／pipeline の診断、全 root 長の拒否による batch 全体の未提出と signal 値の再利用も確認する。

使用例は [Portable README](../../src/graphics/Lumyte.Graphics.Portable/README.md)、実機の対象範囲は [適合試験](../../src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/PORTABLE.md) に置く。独立レビューで公開 API／ADR の整合と内部 view の回収を確認した。

最後に `dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=portable-raster-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none` を実行し、26 test project の **1,900件成功、失敗0、skip 0**、終了コード0を確認した。DirectX 12 は401件、Vulkan は413件、WebGPU は319件、Native は92件、Portable は102件。最終的に強化した indexed draw の入力検証もこの全体実行に含む。TRX は各 test project の `TestResults/` に保存した。

37 ADR の API・コード配置・使用例・未実装章、182個の番号依存、46文書の387ローカルリンクと20アンカーを確認した。`InternalsVisibleTo` は10指定すべて test assembly 向けで、Native と Portable の内部公開は0件。staged diff の空白検査も成功した。

今回の実機検証は同梱 Dawn の native host とこの PC の GPU を対象とする。全 format／固定状態の組合せ、dual-source blending の実 shader、allocation failure と実 device loss の強制は検証していない。圧縮 Texture format、shader package／loader、Slang の製品 toolchain、Browser runtime、上位 Resources と RenderGraph provider は後続である。

## 第13段階: Portable Browser WebGPU backend

2026-09-13 に [WebGPU backend](../adr/0027-webgpu-backend-implementation.md) の Browser 実装を追加した。`Lumyte.Graphics.WebGPU.Browser` は Portable だけを参照し、同梱 Dawn の assembly から独立して browser の WebGPU を呼び出す。

| 範囲 | 実装内容 |
| --- | --- |
| 起動 | `WebGpuBrowserRuntime.LoadAsync(moduleUrl)` で配布 ES module を読み込み、`WebGpuBackend.CreateAsync(runtime, options)` が device を作る。runtime は caller 所有で、backend は借用する |
| 実行契約 | 現在の Portable interface の全操作を接続する。raw WGSL、明示 binding、raster／compute pipeline、直接・indexed・indirect draw、直接・間接 dispatch、buffer／texture copy を扱う |
| Root | `immediate_address_space` と有効な直接入力容量を初期化時に要求する。記録した byte 列を `setImmediates` へ渡し、固定容量、GPU buffer への退避、Parameter Data の解析・upload は設けない |
| Mapping | mapped ArrayBuffer と WASM memory の間でホスト側コピーを行う。Write mapping の Dispose で同じ ArrayBuffer へ書き戻してから unmap する。GPU buffer の追加生成はしない |
| Pipeline・提出 | 使用する pipeline だけを最初の Submit で生成して再利用する。全記録を encode してから一つの `queue.submit` に渡す |
| Completion | `onSubmittedWorkDone` と object／batch の error scope を別に観測する。内部記録と attachment view は GPU 利用終了後に回収し、診断も成功した batch だけ WaitAsync を正常完了させる |
| 数値と寿命 | JavaScript へ渡す GPU size／offset は正確に表せる整数範囲を確認する。timeline 値は C# の ulong 全域を保持する。JSObject を扱う操作は runtime を作成した JavaScript thread で行う |
| 検証の分担 | GPU の usage、alignment、format、binding と shader の条件は browser が検証する。WebIDL の同期例外も元の message を保持して操作診断に写す |

Browser の低層は application resource の GC、staging、自動退役や device 全体の resource registry を設けない。内部 view／sampler の再利用、queue の内部記録と完了履歴だけを管理する。backend は外部 assembly と同じ public／protected 契約で Portable を実装し、本体向けの内部公開を追加しない。

[Browser README](../../src/graphics/Lumyte.Graphics.WebGPU.Browser/README.md) に module の配信、起動、copy／readback と終了の例を置いた。[隣接する xUnit 適合試験](../../src/graphics/Lumyte.Graphics.WebGPU.Browser.Tests/Integration/README.md) は loopback HTTP server と独立した Chromium profile を起動し、C# WebAssembly consumer から実際の backend を呼ぶ。JavaScript だけで GPU の結果を作る代替試験ではない。Browser process の起動・終了とGPU試験の排他制御は fixture が所有する。

検証 host は .NET WebAssembly interpreter、trimming 無効、reflection JSON serialization 有効で構成する。WASM workload の追加なしにビルドできる。初回実行では試作時の trimmed framework と現在の untrimmed framework の生成物が混在し、WASM 起動前に TypeLoadException が発生した。該当 host の生成物を削除して再生成し、C# host の起動を確認した。

初回 module import の失敗が `JSHost` の固定名に記憶され、正しい URL を指定しても再試行できない問題を実際の C# host で再現した。URL ごとの候補名で読み込めることを確認してから固定名へ接続し、失敗した URL が本体の import を妨げないように修正した。回帰試験で失敗した URL から正しい module と device を生成できることを確認する。同じ失敗 URL に対する runtime 自身の cache は強制消去しない。

Edge **153.0.4234.32** では、間接 dispatch が4 byteの直接 root 値37を65535へ上書きし、期待値 `[37, 38]` に対して `[65535, 65536]` が返った。C# と Lumyte を使わない raw JavaScript でも同じ結果になり、validation error は発生しなかった。Dawn の内部 indirect 引数検証が使う immediate data と利用者の入力の衝突に一致する。[上流の修正](https://dawn.googlesource.com/dawn/+/c4e47b5eddc06f271cb07c3108cfccb1bb4704ec) を含む Chrome for Testing **155.0.8048.0** を作業フォルダーへ取得し、元の C# 試験を同じ期待値で再実行して成功した。ユーザーの browser のインストール・profile は変更していない。独立した再現 source と実行方法は [実験記録](../../tools/experiments/browser-webgpu-indirect-immediates/README.md) に保存した。

Browser の実機試験20件と純粋な timeline 試験21件、計41件の focused tests が成功した。8／32 byteの root、間接 dispatch、indexed raster と signed baseVertex、dynamic binding、texture sampling と複数行 copy、mapping の部分更新・二重 map 失敗時の既存 lease 保全を確認した。診断の依存・分離、失敗 batch の後の独立した成功、WebIDL の同期 encode 失敗後の未発行値の再利用、JavaScript の正確な整数範囲を超える GPU size の拒否と、同範囲を超える CPU timeline 値の保持も確認した。失敗を回避するための test skip、期待値の変更、validation の無効化や直接 root の buffer 置換は加えていない。

最初の全体実行では Browser 41件、DirectX 12 401件、Dawn 319件を含む26 project が成功したが、Vulkan の test host がテスト列挙中に終了した。Windows の障害記録は `0xc0000005`、例外アドレス0、障害 module 不明であり、Vulkan のテストは実行されていない。調査で、Vulkan の条件付き属性8種類・124か所が、collection fixture の排他を取得する前にそれぞれ device を作る問題を見つけた。対応機能の不変 snapshot を、同じ GPU 排他付きの Lazy で一度だけ取得する形へ変更した。GPU を使わない回帰試験3件を加え、gate 全7件と Vulkan 単独全413件が成功した。探索時の排他不足は修正したが、元の native crash との直接の因果は断定しない。

最後に `LUMYTE_WEBGPU_BROWSER` へ Chrome for Testing 155.0.8048.0 の絶対パスを指定し、`dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=portable-browser-solution-verified" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini` を実行した。27 test project の **1,944件成功、失敗0、skip 0**、終了コード0を確認した。Browser 41件、Dawn 319件、DirectX 12 401件、Vulkan 413件、Native 92件、Portable 102件、共通 Graphics 155件を含む。今回の追加は Browser 41件と排他の回帰3件の計44件である。全 TRX は各 test project の `TestResults/` に保存した。最終実行で build 警告と crash はなかった。

独立レビューで JSObject の寿命、mapping の失敗時回収、recording の一回提出と内部参照の完了後解放、scope／Promise の診断帰属、公開拡張契約を確認した。37 ADR の必要章、182個の番号依存、49文書の403ローカルリンクと21アンカーに問題はなかった。`InternalsVisibleTo` は11指定すべて test assembly 向けで、Native／Portable の内部公開は0件。staged diff の空白検査も成功した。

shader package／loader、Slang の製品 toolchain、上位 Resources、RenderGraph provider、Hosting と canvas presentation は後続である。trimming／AOT 配布、worker／thread 間移送、全 Browser／GPU の組合せ、実 device loss と allocation failure の強制試験は未検証とする。

## 第14段階: Native／Portable shader package と runtime loader

2026-09-13 に [Native Shader](../adr/0015-native-shader-package-api.md) と [Portable Shader](../adr/0023-shader-design-and-api.md) の runtime を、それぞれ独立した assembly として追加した。いずれも下位の公開 backend 契約だけを参照する。Graphics の Resources が shader の入力 schema を使えるよう、まず準備済み package と program の所有を揃える段階とした。

| 範囲 | 実装内容 |
| --- | --- |
| Native の入力 | 所有済み stage bytes、target 別 artifact、root／parameter layout と ABI 識別子を不変値として保持する。一つの package は compute、vertex raster、mesh raster のいずれかの program family を表す |
| Native の選択 | version、公開 code format、有効 capability、descriptor ABI に一致する一つの artifact を選ぶ。Mesh／Amplification の必要機能は stage 構成から必ず要求する。候補なし・複数候補・任意の expectedAbiHash との不一致は失敗する |
| Native の所有 | Load ごとに独立した raw code を保持する。低レベル Code から到達する memory が変更されても package と別 program は変化しない。program の Dispose は CPU 側保持を解放し、pipeline／device の破棄や GPU 待機はしない |
| Descriptor ABI | DirectX 12 の opaque index、Vulkan の device 由来の統一 stride、固定配置を別の ABI として扱う。統一方式は Slang が生成する max(imageSize, bufferSize) と samplerSize の式を公開 slot stride と照合し、shader の式に host padding を追加しない |
| Portable の入力 | 所有する WGSL、entry、group 順の不変 layout、root／parameter の配置、意味名による binding schema と ABI 識別子を保持する |
| Portable の初期化 | version、program 構成、schema の対応、要求 feature、直接入力容量と任意の expectedAbiHash を確認し、module と group layout を生成する。各 program は専用の object を所有する |
| Portable の所有 | Description は pipeline に渡す非所有の構成値。binding、pipeline、未提出記録と GPU 利用を終えてから program を破棄する。初期化途中の同期失敗では生成済み object を逆順に回収し、解放の一つが失敗しても残りを試みる |
| 検証の分担 | shader の合法性は compiler／runtime が扱う。Portable の Load 成功を非同期 compilation の成功と扱わず、module／layout の診断を提出後の WaitAsync へ引き継ぐ |

Native の shader bytes、Portable の WGSL と入力列を caller の元配列から独立させた。両 library はファイル、stream、URI、container の decoder、runtime compiler、上位 Resources と RenderGraph を参照しない。root は既存の直接入力経路を使い、command／loader による Parameter Data の生成・upload や GPU buffer への fallback を加えていない。今回の RequiredFeatures は既存の Portable device が明示的に確認できる直接入力と dual-source blend に限定する。

使い方と所有権は [Native shader README](../../src/graphics/Lumyte.Graphics.Native.Shaders/README.md) と [Portable shader README](../../src/graphics/Lumyte.Graphics.Portable.Shaders/README.md) に記載する。ADR の API・配置・使用例・未実装範囲も現在の runtime に合わせて更新した。

GPU を使わない新規テストは Native 47件、Portable 28件。配列・code の所有、別 Load への変更の分離、version／capability／ABI 選択、stage family、rollback と解放失敗の保持を確認した。Vulkan の統一 ABI には、shader の stride と host 側の padding が異なる場合の拒否を回帰試験として含める。

新規の実機テスト8件が成功した。DirectX 12 は compute と、program を破棄した後に Submit で raster PSO を生成する描画を確認する。Vulkan は raw address による書込みと、統一 descriptor heap の buffer／texture／sampler の参照を読み戻す。Dawn と Browser は各2件で、package が所有する module／layout を使った直接 root の compute と、無効な WGSL の診断が提出の完了結果に保持されることを確認する。Browser は既存の Chrome for Testing 155.0.8048.0 を使う。

両 runtime の独立レビューで入力の所有、public な backend 拡張契約、失敗時の全 object 解放、非同期診断の伝播を確認した。37 ADR の必要章、182個の番号依存、52文書の410ローカルリンクと21アンカーに問題はなかった。`InternalsVisibleTo` は既存の11指定すべて test assembly 向けで、今回追加した library に指定はない。

最後に `LUMYTE_WEBGPU_BROWSER` へ Chrome for Testing 155.0.8048.0 の絶対パスを指定し、`dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=shader-packages-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini` を実行した。29 test project の **2,027件成功、失敗0、skip 0**、終了コード0を確認した。新規テストは75件の unit と8件の実機の計83件である。DirectX 12 全403件、Vulkan 全415件、Dawn 全321件、Browser 全43件が成功し、全 TRX を各 test project の `TestResults/` に保存した。staged diff の空白検査も成功した。

offline compiler、Slang の製品 toolchain、最終 WGSL からの layout 取得、C# 構造体／BindingInputs 生成、container の出力と CPU 資産側 decoder は後続である。実行に使う fixture の shader code と入力 layout は事前に準備したもので、生成器の完成を示さない。shader package 経由の全 stage／feature、全 GPU／runtime と実 device loss／allocation failure の適合は未検証とする。

## 第15段階: Native memory arena と Portable resource pool

2026-09-13 に [Resources utility](../adr/0028-resource-utilities.md) の最初の実装として、`Lumyte.Graphics.Native.Resources` と `Lumyte.Graphics.Portable.Resources` を追加した。それぞれ同じ系統の低レベル公開 backend 契約だけを参照し、新しい backend 向けの内部公開を要求しない。

| 範囲 | 実装内容 |
| --- | --- |
| Native arena | `GpuMemoryArena` が backing heap を所有し、`Allocate` が alignment を満たす `GpuMemorySlice` を返す。空き範囲の分割と隣接範囲の結合、block の再利用と `Trim` を実装する |
| Native の互換性 | memory kind と取得済み opaque compatibility token の参照 identity の集合で pool を分ける。同じ集合の順序・重複を正規化し、全 token を backend に渡す。token の内部表現や別 token の同等性を推測しない |
| Native の配置 | caller が保存した requirements の予約 size と alignment を使用する。arena は resource を配置せず、caller が配置 resource と全使用を終了してから slice を返す |
| Portable pool | `GpuBufferPool`／`GpuTexturePool` が description の全フィールドが一致する object を再利用する。heap、placement、大きめの resource への置換を追加しない |
| 貸出 identity | Native の slice と Portable の typed lease は貸出ごとに一意。同じ heap 範囲や raw handle が再利用されても、古い貸出で現在の貸出を返却できない |
| 所有と解放 | `Release` は使用終了を caller が保証する明示返却。`Trim` は未使用分だけを破棄する。未返却がある `Dispose` は状態を変更せず拒否し、返却後に再試行できる |
| 破棄失敗 | 解放対象を cache から切り離してから、全対象の破棄を一度ずつ試す。副作用が不明な handle は再利用・再破棄せず、単一例外または複数例外を保持する |

両 utility は backend を借用し、操作の直列化と実行 context は caller に従う。GPU state、format、usage、native alignment の validator を複製せず、resource の内容と状態を再初期化しない。mapping、view／binding、未提出記録と GPU 利用の終了を確認してから返却する。使用例と所有契約は [Native Resources README](../../src/graphics/Lumyte.Graphics.Native.Resources/README.md) と [Portable Resources README](../../src/graphics/Lumyte.Graphics.Portable.Resources/README.md) に記載した。

GPU 不要の単体テストは Native 27件、Portable 29件。範囲の非重複と再結合、整列、token の参照集合、description の差、古い貸出の拒否、作成・解放失敗と所有状態を公開動作で検証した。両実装の独立レビューを行った。

実機試験で、既存 DX12 backend が二つの resource category の requirement から deny flag 一つを生成し、`CreateHeap` が `E_INVALIDARG` になる不具合を確認した。単一 category は従来の `ALLOW_ONLY_*`、混在は `ALLOW_ALL_BUFFERS_AND_TEXTURES` へ写すよう修正し、二つずつの全3組で heap と placed resource を作る回帰試験を追加した。Tier の照会や managed な配置検証は増やしていない。[DX12 heap flag の仕様](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/ne-d3d12-d3d12_heap_flags#remarks)

3組の回帰試験で debug layer を明示要求した初回は、この PC に当該 component がなく `D3D12GetDebugInterface` が `0x887A002D` となった。既存の memory 適合試験と同じ既定 device 作成へ戻し、問題を直接拒否する native `CreateHeap` と配置処理で検証する。debug layer を使った試験は未実施で、OS component の追加やテストの skip は行っていない。

新規の実機試験は11件が成功した。DX12／Vulkan はそれぞれ arena の二つの線形 slice の非重複・完了後の同じ範囲の再貸出と、同一 heap に配置した線形領域→Texture→readback の転送を確認した。Dawn／Browser はそれぞれ Buffer と Texture の完了後の再貸出で同じ raw handle と内容が保持されることを確認した。これら8件に DX12 の category pair の回帰3件を加える。Vulkan の共有試験は公開の `ExplicitTextureTransitions` に従い GENERAL layout と barrier を使い、DX12 の明示 layout 遷移を持ち込まない。Browser は既存の Chrome for Testing 155.0.8048.0 を使う。

最初のソリューション全体の並列実行は、DX12 408件と Dawn 323件が成功した一方、Browser と Vulkan が2分の無進行監視で中断し、終了コード1だった。Browser は unit 21件の終了から約96秒を GPU mutex 待機に費やし、DX12 の完了直後に起動処理を開始したが、約24秒後に test host が停止した。ケース開始前の fixture 待機にも監視時間が消費されるため、並列実行を全件成功とは扱わない。中断したテスト専用 profile の Chrome と子プロセスを確認して終了し、同じ監視条件と全テストを保ったまま `-m:1` で project を順番に再実行した。

独立レビューで arena／pool の所有、貸出 identity、CPU 計算、失敗時の保持と全対象の解放、使用例と Native／Portable の境界を確認した。37 ADR の必要章、182個の番号依存、54文書の415ローカルリンクと21アンカーに問題はなかった。`InternalsVisibleTo` は既存の11指定すべて test assembly 向けで、新しい Resources には指定がない。

最終実行は `LUMYTE_WEBGPU_BROWSER` に Chrome for Testing 155.0.8048.0 の絶対パスを指定し、`dotnet test Lumyte.slnx -m:1 --logger "trx;LogFilePrefix=resource-pools-serial-final" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini` を使用した。31 test project の **2,094件成功、失敗0、skip 0**、終了コード0を確認した。全 TRX の outcome は Completed で、新規67件は unit 56件と実機11件からなる。DX12 全408件、Vulkan 全417件、Dawn 全323件、Browser 全45件を含み、最終の Dawn の返却順序修正もこの実行で確認した。TRX は各 test project の `TestResults/` に保存した。staged diff の空白検査も成功した。

completion token と `Retire`／`Collect` はまだ公開しない。raw Submit は native queue へ渡した後でも fence signal や interop の後処理で例外になり得るため、例外だけから未提出・完了を推測できない。次に受理と利用終了の契約を整えてから、自動 retirement を接続する。`GpuDeviceLostException` の XML 説明も、例外自体は GPU 利用終了や resource 解放を保証しない表現に修正した。command の状態照会 API は追加していない。

upload／readback utility、resource manager、scope／pin／batch、自動 descriptor／binding 管理、package upload と RenderGraph の接続は後続である。今回の pool の返却を自動 GC や完了追跡としては扱わない。

実機試験は正常な提出と完了後の再利用を対象とする。native allocation failure、GPU device loss と不明な提出完了を強制した回収試験、全 memory type／format／adapter の組合せは未検証である。管理上の失敗と所有維持は fake backend の単体テストで確認する。

## テスト実行の改善: backend ごとの並列化

2026-09-13 に全 backend 共通の GPU 排他を、DirectX 12／Vulkan／Dawn／Browser ごとの named mutex へ分けた。同じ backend の GPU テストは一つの collection と専用 mutex で直列化し、別 backend の device と test host は並行できる。Vulkan の探索時の capability probe も実行用 fixture と同じ mutex 名を使う。

GPU collection の `DisableParallelization = true` と Dawn の assembly 全体の並列禁止を除去した。これらは同じ GPU collection 内の順序だけでなく、CPU 単体テストとの並行まで禁止していた。同一 collection のテストは xUnit の通常規則で直列のまま、独立した CPU collection と他 project は並行する。native の検証、テストの期待値、skip 条件、2分の無進行監視は変更していない。[xUnit の並列実行](https://xunit.net/docs/running-tests-in-parallel)

独立レビューで CPU test の可変状態、native callback の帰属、初期化と device 所有を確認し、cross-backend の共有可変状態は見つからなかった。以前の Vulkan discovery の native crash との因果は未確定の記録を維持する。今回の変更は同一 backend の探索・実行の保護を残しており、同一 device への host 操作を無条件で並行化するものではない。

gate の8件の回帰試験を追加し、focused test 全15件が成功した。別名の gate の同時所有、一方の解放による他方の保護への非干渉、無名 mutex になる null／空文字と空白の拒否を確認する。既存の thread をまたぐ Dispose、同名の排他、abandoned mutex、probe の一回実行・失敗後の解放も維持する。

通常の実行手順と新しい backend／テストの追加条件は [テストの実行と並列化](../testing.md)へまとめた。同じ backend を含む別コマンドを重ねた場合の mutex 待機は引き続き監視対象なので、一つの全体実行を使う。

`LUMYTE_WEBGPU_BROWSER` に既存の Chrome for Testing 155.0.8048.0 を指定し、`dotnet test Lumyte.slnx --logger "trx;LogFilePrefix=backend-parallel-final" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini` を `-m:1` なしで実行した。31 project の **2,102件成功、失敗0、skip 0**、全 TRX の outcome が Completed、コマンドの終了コード0を確認した。DX12 408件、Vulkan 417件、Dawn 323件、Browser 45件を含む。restore／build を含む今回一回の実測は320.08秒で、厳密な速度比較の benchmark ではない。native crash と無進行 timeout は発生しなかった。

初回の TRX では DX12 と Vulkan の提出・readback を含む実機 case の重複を確認できたが、build の完了時刻が異なり4 backend 全体は重ならなかった。9月14日に同じソリューションを `--no-build --no-restore -m:4`、logger prefix `backend-overlap-final`、同じ2分の監視条件で再実行した。再度31 project の **2,102件成功、失敗0、skip 0**、全 outcome が Completed、終了コード0となった。ビルドを含まない今回一回の実測は194.05秒だった。

GPU collection に属する class と TRX の testId／開始・終了を対応させ、次の4 case が **00:00:56.932424–00:00:57.043410 JST の約111ms** 同時に実行され、すべて成功したことを確認した。partial／継承による共通 conformance method も実行先 class で判定した。

| Backend | 実機 case | 9月14日 00:00 の開始秒–終了秒 |
| --- | --- | --- |
| DirectX 12 | `TextureDimensionsCanBePlaced(TwoD)` | 56.597217–57.168308 |
| Vulkan | `ColrLayersAndColrGlyphReuseRenderBothLayers("B")` | 56.932424–57.043410 |
| Dawn | `AutoRenderingDrawsEveryPolicyRoute(56, Polygon)` | 56.789204–57.641991 |
| Browser | `RasterSamplesAnImmutableTextureAndSamplerBinding` | 56.292495–57.150953 |

DX12 は実 device／heap／texture の作成、他3件は描画と readback を行うことも source で確認した。これはテストの実行期間が重なった証拠であり、GPU engine 内部の同時実行率や速度向上の測定ではない。テストの期待値や native 検証は両実行で同じである。

55文書の418ローカルリンク、21アンカー、37 ADR の必要章と182個の番号依存に問題はなかった。production 向け `InternalsVisibleTo` は追加せず、既存11指定はすべて test assembly 向けのままである。

## 第16段階: 提出失敗の識別と内部 command memory の保持

2026-09-14 に raw Native／Portable の提出失敗契約を整えた。Resources の自動回収へ進む前提として、GPU へ渡した可能性がある work を未提出として破棄しないようにする。

| 範囲 | 実装内容 |
| --- | --- |
| Native 公開契約 | `NativeGpuSubmissionException` が要求した `NativeGpuTimelinePoint` の `Completion` と元の `InnerException` を保持する。`Submit` の署名は維持する |
| DirectX 12 | GPU Wait／Execute／内部 Signal／caller Signal の区間で失敗した場合も、準備済み内部 command memory を保持する。後続の操作は共有障害を通知し、受理不明の work を再提出しない |
| Vulkan | `QueueSubmit2` のメモリ不足だけを不変保証のある拒否として扱う。device loss／不明な結果では recording と初期化 pool を queue 側に移し、通常の破棄・counter による回収を行わない |
| Portable 公開契約 | `GpuSubmissionException` が要求した raw `GpuFenceValue` と元の障害を保持する。GPU 利用終了後の診断失敗を示す既存 `GpuExecutionException` と区別する |
| Dawn／Browser | GPU 利用終了と診断の観測を受渡し前に結び付ける。受渡し後の同期障害でも point を保持し、診断接続の失敗だけで独立した完了通知登録を省かない |
| Browser 終了 | device の destroy interop が同期失敗した場合、finally で残った内部 command を解放しない。正常な Dispose の全利用終了という caller 前提は維持する |

Vulkan の従来の catch は、`QueueSubmit2` の device loss を含む全失敗で初期化 pool を解放していた。native 仕様が resource／同期状態の不変を保証するのは `OUT_OF_HOST_MEMORY`／`OUT_OF_DEVICE_MEMORY` であるため、受理不明の結果を分けた。後の大きい timeline 値だけで不明な記録を回収せず、同じ image の初期化を再実行しないよう backend の継続利用も止める。[vkQueueSubmit2 の失敗保証](https://docs.vulkan.org/refpages/latest/refpages/source/vkQueueSubmit2.html)

新しい例外の completion は識別子であり、値の到達や GPU 利用終了を保証しない。例外を包む処理自体が成立しない致命的な host 障害も未提出の証拠にしない。device loss、待機の取消し、backend の Dispose を GPU 停止の代用にはしない。Browser の Promise と device loss の扱いも保守的な既存契約を維持し、診断接続の障害後に保持を自動で解く機構は加えていない。

Native／Portable の下位例外は Resources の型を参照しない。native 呼出し順序を扱う内部 helper は実際の提出から使い、Dawn／Browser の観測 helper は source link で各 assembly に含める。production 向けの `InternalsVisibleTo`、公開 command 状態、暗黙の GPU wait、application resource の追跡は追加していない。関連 ADR の API・所有契約と両 API の README を更新した。

新規の CPU 回帰試験50件が成功した。Native は例外2件、DX12 の実呼出し順序と途中失敗7件、Vulkan の拒否・保留と内部 memory の保持11件である。Portable は公開例外3件、両 WebGPU assembly で実際に使う提出 helper の各13件、Browser の destroy 失敗1件を追加した。Vulkan は通常 completion 前の回収禁止、受理不明の記録に最大 counter 値を与えても解放しないこと、利用終了を caller が別途確定した後の一度だけの解放を検証する。WebGPU は scope 回収と GPU 完了登録の独立性、受渡し前の失敗時だけの解放、受渡し後・非同期障害で利用終了を作り出さないことを確認する。既存 timeline／診断を含む focused tests 計101件もすべて成功した。

独立レビューで実際の queue から helper・回収処理への接続と、公開契約・ADR の整合を確認した。故障の試験は制御した native 結果・runtime 呼出し・Task を使うもので、実 driver の device loss や Browser の loss 通知順序を強制した適合試験ではない。停止確認の公開操作、利用終了が不明な保持の drain と Resources への接続は後続である。

`LUMYTE_WEBGPU_BROWSER` に既存の Chrome for Testing 155.0.8048.0 を指定し、`dotnet test Lumyte.slnx -m:4 --logger "trx;LogFilePrefix=submission-handoff-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini` を実行した。全31 project の **2,152件成功、失敗0、skip 0**、全 TRX の outcome が Completed、終了コード0を確認した。DX12 415件、Vulkan 428件、Dawn 336件、Browser 59件を含む。既存のコピー・描画・同期・完了後の再利用も元の期待値で成功し、native crash と無進行 timeout はなかった。

55文書の419ローカルリンク、21アンカー、37 ADR の必要章と182個の番号依存を確認した。既存11指定の `InternalsVisibleTo` はすべて test assembly 向けで、差分の空白検査も成功した。

## 第17段階: Resource Utilities の責務確定と完成

2026-09-14 に依存関係の追跡を必要とする処理を上位へ集約し、[ADR 0028](../adr/0028-resource-utilities.md) の utility を Native arena と Portable Buffer／Texture pool の貸出・返却に限定した。この範囲の機能は実装済みである。前段階に記録した utility の completion／upload 追加予定は、この方針変更で置き換える。

| 責務 | 担当 |
| --- | --- |
| Native heap block、整列、範囲分割・結合、貸出 identity、Release／Trim／Dispose | Native Resources の utility |
| 完全な description による Buffer／Texture object の再利用、lease、Release／Trim／Dispose | Portable Resources の utility |
| 配置 object と view／descriptor／binding の依存、scope／pin／use／batch、提出 token と完了、遅延回収 | 上位の resource manager |
| staging、非同期 upload／readback、package 配置と結果公開 | manager が所有する uploader |

[ADR 0029](../adr/0029-resource-management-api.md) に管理層の token 発行、内部 retirement、非同期転送と破棄順序をまとめた。同じ二つの Resources assembly 内で責務を分ける。utility に Retire／Collect、公開 retirement queue や独立した転送 API を追加しない。上位は全利用を確認し、Native の配置 object を破棄してから slice を返す。Portable は mapping／view／binding／記録の依存を終えて lease を返し、pool が所有する handle を直接破棄しない。

返却前提を満たさない loan は保持を続ける。device loss、取消し、backend Dispose を GPU 使用終了の証拠にしない。manager の非公開 timeline と提出 token の発行・観測、停止未確認時の保持解消は上位の未実装事項であり、utility の完成とは区別する。CPU layout helper や raw copy の同義 wrapper を追加するための API は作らない。

実装監査で、両 utility が未使用 object を cache から外した後に、Destroy の障害を記録する可変長 list を確保・拡張していた点を補強した。回収対象の snapshot と全対象分の error storage を先に確保し、成功後だけ切り離す。破棄中は固定配列へエラーを保持するため、その一覧の拡張失敗で残りの Destroy 試行を失わない。単一の元例外と複数エラーの集約、破棄が不明な object の再利用禁止、借用 backend の維持は従来の契約を保つ。

Native の README にあった二重 finally の例は、配置 resource の破棄が失敗しても外側で slice を返すため修正した。破棄の成功後にだけ Release へ進む。両 README、project description、ADR 一覧と関連 Graph ADR も新しい境界へ揃えた。

focused unit tests は Native 32件、Portable 31件の **計63件成功、失敗0、skip 0**。新規7件は、断片化と整列を組み合わせた固定 seed の512操作後の全範囲再利用、ulong 上限での分割・結合、null compatibility の拒否、Trim 失敗後の live block の保持、複数の破棄エラーと成功が混在する場合の全件試行を検証する。実際の host メモリ不足を発生させる試験ではなく、制御した backend の失敗と公開された所有・再利用動作で確認する。

独立レビューで ADR・README の所有契約と返却例、utility 完了と管理層未実装の区別を確認した。production 向けの InternalsVisibleTo は追加せず、既存11指定はすべて test assembly 向けのままである。

`LUMYTE_WEBGPU_BROWSER` に既存の Chrome for Testing 155.0.8048.0 を指定し、`dotnet test Lumyte.slnx -m:4 --logger "trx;LogFilePrefix=resource-utilities-complete" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini` を実行した。全31 project の **2,159件成功、失敗0、skip 0**、全 TRX の outcome が Completed、終了コード0を確認した。DX12 415件、Vulkan 428件、Dawn 336件、Browser 59件を含み、既存の GPU 転送と利用終了後の arena／pool 再利用も成功した。native crash と無進行 timeout はなかった。

55文書の422ローカルリンク、21アンカー、37 ADR の必要章と179個の番号依存に問題はなく、差分の空白検査も成功した。

## 第18段階: ResourceManager の実装

2026-09-14 に [ADR 0029](../adr/0029-resource-management-api.md) の管理層を Native／Portable の両 Resources assembly に実装した。下位 utility は明示的な arena／pool のまま維持し、利用終了と依存関係を判断する処理を manager が担当する。

| 範囲 | 実装内容 |
| --- | --- |
| 所有 | 非所有の typed ref、scope、pin、use、batch。明示依存を保持し、循環を拒否する。raw handle が再利用されても古い ref を復活させない |
| 提出と回収 | 非公開 timeline、manager 発行の token、成功と GPU 利用終了の分離、取消しで保持を放さない非同期待機、依存順の Collect／drain／終了 |
| Native | 用途別 arena、descriptor slot と view／sampler cache、安定した index／address、package group の実 SingleAllocation 配置 |
| Portable | device-owned Buffer／Texture pool、明示 view／sampler、typed binding 入力と immutable binding cache、mapping lease。heap／placement／bindless は追加しない |
| 転送 | 不変の準備済み package plan、初期 upload と成功後の export 公開、buffer／texture の単独更新・readback。file／URI／glTF のロードは含めない |
| 入力生成 | Native／Portable の独立した Roslyn analyzer。準備済み XML schema から管理参照の入力型を生成し、native address／index または Portable binding writer へ解決する |
| Native 非同期待機 | 公開 `NativeGpuSemaphore.WaitAsync` と DX12／Vulkan の観測中 lifetime。timer と counter 照会を使い、待機専用 thread を作らない |

回収は記録、retained lease、resource の順に行う。記録の破棄に失敗したら mapping などの借用先を保持し、同じ破棄を自動再試行しない。raw Submit を呼んだ後は例外の型から拒否を推測せず、GPU 利用終了が不明なら保持して drain を失敗させる。下位の raw signal point を含む例外は再帰的に包み直し、通知権限を token の外へ公開しない。外部の pin／use／mapping を batch へ渡す場合は外部所有数も移管し、正常な非同期終了を妨げない。

独立レビューで、失敗した drain が別の未完了提出を待ち続ける問題、完了済み提出履歴の蓄積、default view cache の残留、失敗した記録より先に mapping の保持が外れる問題を修正した。Portable の古い IsComplete 結果が非同期観測済みの完了を false に戻さないことも、順序を制御した回帰試験で確認する。GPU の format／usage／binding layout の検証を manager に複製せず、入力の欠落も下位へ渡す。

最初の DX12 package 試験では `ID3D12GraphicsCommandList.Close` が `0x80070057` を返した。HostWrite を enhanced barrier の COMMON（全 GPU write access）へ写像していたため、純粋な CPU 書込みに不要な GPU flush を要求していた。HostWrite は NO_ACCESS とし、混在した GPU access は維持するよう修正した。CPU の書込みは Submit 前に完了させ、ExecuteCommandLists 開始時の cache coherence を利用する。[DirectX の external dependency 規約](https://microsoft.github.io/DirectX-Specs/d3d/D3D12EnhancedBarriers.html#external-dependencies-and-d3d12_barrier_access_global)

生成器は実際の analyzer と MSBuild targets を使って consumer をコンパイルし、公開 manager と fake backend で入力の解決と保持を実行して確認した。keyword や変数名との衝突を含め、生成 source 全文の snapshot は使わない。両 NuGet analyzer package をローカルに作成し、同一 consumer から導入・コンパイル・実行できることも確認した。

focused 試験では Native Resources 72件、Portable Resources 75件、Native 生成器12件、Portable 生成器10件が成功した。DX12 は barrier の CPU 写像22件と実機4件、manager の実機4件の計30件、Vulkan は manager の実機4件が成功した。両 Native backend の非同期 semaphore 各3件、Dawn／Browser の管理された binding と package 各2件も成功した。Native の転送は非ゼロ buffer offset と複数行 texture の pitch、最小の readback 範囲を通し、Vulkan は GENERAL layout、DX12 は明示的な layout 遷移を使う。

最終検証は `LUMYTE_WEBGPU_BROWSER` に既存の Chrome for Testing 155.0.8048.0 を指定し、`dotnet test Lumyte.slnx -m:4 --logger "trx;LogFilePrefix=resource-manager-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini` を実行した。全33 project の **2,294件成功、失敗0、skip 0**、全 TRX の outcome が Completed、終了コード0を確認した。DX12 427件、Vulkan 435件、Dawn 338件、Browser 61件を含む。新規135件は CPU 113件と実機22件で、native crash と無進行 timeout はなかった。TRX は各 test project の `TestResults/` に保存した。

57文書の425ローカルリンク、21アンカー、37 ADR の必要章と179個の番号依存に問題はなかった。production 向けの `InternalsVisibleTo` は追加せず、既存11指定はすべて test assembly 向けである。staged diff の空白検査も成功した。

## Shader build と管理入力生成の接続

2026-09-14 に Native／Portable の独立した offline tool を追加した。shader と build 設定から
package と対応する host C#、Resources 入力 XML を生成し、手書きの offset／binding schema を不要にした。

| 系統 | 実装と確認内容 |
| --- | --- |
| Native | Slang 2026.17 の同じ target compile の reflection から DXIL／SPIR-V、root 配置、host 型を生成。実 fixture の DX12 root は32 byte／colour offset20、Vulkan は48 byte／offset32。型を target 別 namespace に生成する。 |
| Native の参照 | `LumyteResource` 属性から GpuAddress／View／Sampler を区別し、`NativeShaderResourceKind` に保持する。parameter 型は同じ設定の StructuredBuffer probe で反映する。通常の整数の意味を名前から推測しない。 |
| Portable | 公式 Dawn v20260911.162847 の Tint JSON、型配置表示、最初の WGSL IR を利用する。root の型を自動識別し、binding、32 bit scalar／vector／入れ子構造体を生成。実 fixture は32 byte、vec3 offset16。元の WGSL を package に保持する。 |
| 管理入力 | compiler の XML から既存 analyzer が Native の参照解決入力と Portable の group 入力を生成。package／host／管理入力に同じ AbiHash を持たせ、loader へ期待値を渡せる。shader runtime から Resources／offline への依存を追加しない。 |
| Build | NativeShaderCompile／PortableShaderCompile は source と entry／target／名前を指定する JSON を受け取る。全 compile 成功後に manifest 所有ファイルを更新し、削除された入力を除去する。生成 C# と XML は CoreCompile 前に明示登録する。 |

両 [offline tool](../../tools/README.md) の CLI と targets、[MSBuild consumer](../../tools/experiments/shader-build-inputs/README.md) を追加した。
consumer は異なる targets import 順で build／run に成功し、生成型の size と管理入力の AbiHash が package に一致した。
Native は JSON container と package factory、Portable は準備済み package と factory C# を出力する。
ファイルのロード／デシリアライズは引き続き `Lumyte.Resources` が担当する。

生成型の名前衝突、Native の root なし指定と shader 宣言の矛盾、未対応 ABI、未知の Tint IR、padding と実 field 名の衝突には診断と回帰試験を追加した。
出力 publisher は古い group の削除、同値出力の保持、準備失敗時の保護、inventory 外のファイルを変更しないことを検証する。
production 向け InternalsVisibleTo は追加せず、新規2指定も test assembly のみである。

Native の matrix／非 float vector は反映された正確な byte storage とし、matrix 要素 serializer は未対応。
Portable の matrix／array host 型は stride 取得が未対応のため明示的に拒否する。
Portable Slang の root accessor／setLanguagePrelude 統合、生成入力を使う実 GPU 適合、VulkanFixed 生成、
Portable container 形式と compile cache は後続とする。現在は毎 build で compiler を呼び、import 変更も反映する。
既存 backend の実機 fixture と、今回の compiler／C# consumer 試験は異なる検証範囲である。

最終検証は上記の compiler／Browser の環境変数を指定して
`dotnet test Lumyte.slnx -m:4 --logger "trx;LogFilePrefix=shader-input-toolchain-final" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini`
を実行した。**35 project・2,373件成功、失敗0、skip 0**、全 TRX が Completed、終了コード0。
Native offline 48件、Portable offline 15件、両 Resources generator 19／17件、Native shader runtime 49件を含む。
前回からの追加79件は CPU／外部 compiler の試験であり、GPU 試験を追加したとは扱わない。
既存の DX12 427件、Vulkan 435件、Dawn 338件、Browser 61件もすべて成功した。
61文書・437ローカルリンク・21アンカー、37 ADR の必要章と179個の番号依存に問題はなかった。

## RenderGraph 段階 0: 共通機能と二系統の実行

2026-09-14 に [共通 RenderGraph](../adr/0030-render-graph-api.md)、
[Native provider](../adr/0031-native-render-graph-implementation.md)、
[Portable provider](../adr/0032-portable-render-graph-implementation.md) と
[Generic Host 接続](../adr/0037-graphics-hosting-and-di.md) の最小統合を実装した。

| 範囲 | 実装内容 |
| --- | --- |
| 共通 graph | shader や GPU 記録 callback を受けない機能 contract、宣言と culling、CPU snapshot、型付き入力、不変 bindings、共通資源参照、成功後の export |
| Native／Portable | 独立 registry と公開 SPI、実行ごとの資源と内部 pass、ResourceManager の scope／batch／pin、submit と completion、受理停止と drain |
| 標準機能 | 共通 Clear／Texture Copy／Output と両本体。Clear 値は同じ plan の bindings で差し替えられ、shader と binding は利用側に公開しない |
| Hosting | 同じ一回の StartAsync／GetAsync 初期化、Options snapshot、runtime 単位の CPU scope、session 借用、任意 presentation 接続、consumer 停止後の GPU／表示／CPU の順次終了 |
| 移行 | 初回統合時は旧 graph を隔離していたが、後続の旧システム削除で旧 graph／TwoD／Text／Library と専用試験を削除。互換 API を残さない |
| 適合 | `Lumyte.Graphics.RenderGraph.Conformance` に共通 consumer を一度 build し、各 backend の Host 設定から同じ Clear → Copy → Output を実行する |

CPU 試験では宣言失敗の rollback、未初期化 read、culling、snapshot、別 plan／runtime の参照、
export の成功確認、target の形状変更、提出前後の失敗、取消しと保持を個別に確認する。
独立レビューで、同期的に送出された受理不明例外を未提出として Discard する問題、
bindings の世代衝突、後続の生存 writer による過去 output の上書きを修正した。
過去内容を残す場合は別 texture への明示 Copy を要求する。

Host の consumer が Graphics より先に起動しても accessor が初期化を開始し、
停止時は IHostedLifecycleService の段階を使って consumer の StopAsync 後に drain する。
非同期の target 取得中に停止した場合も、接続の所有を終了する前に取得と返却を完了する。
scope／pin／execution は caller が返し、Host が外部所有を強制回収しない。

共通 facade の明示 resource 操作は下位 manager と同様に caller が直列化する。
import／upload は await してから次の manager 操作へ進み、これを一般の concurrent resource API と表示しない。
plan cache、contributor builder、内部 template の差分更新、内容世代 ticket、Blit 以降の画像機能、
新しい Model／2D、実 window／canvas の接続は後続である。60 FPS の性能達成を示す段階ではない。

段階 0 の実機適合は DirectX 12 が4件、Vulkan が4件、Dawn WebGPU が5件成功した。
同じコンパイル済み consumer を Host の provider 設定から実行し、Clear → Copy → Output、
線形／sRGB と premultiplied alpha の画素、headless target の Acquire → Submit → Present、
export の回収と Host 終了を確認した。単一 offscreen target は使用終了まで再取得させず、
終了時は取得済み未提出 target の返却も待つ。OS の window／canvas を表示した試験ではない。

最初の DX12 試験は command list の Close が `0x80070057` で失敗した。
provider の global barrier に texture の attachment／copy／shader access を混在させていたため、
明示 layout を使う backend では texture の依存を同一 layout 間も含めた TextureTransition に、
buffer の依存を global Barrier に分けた。修正後の画素試験と headless presentation が成功した。
この初回実行時は D3D12 debug layer（`0x887A002D`）と `VK_LAYER_KHRONOS_validation` が利用できなかったため、
新規 DX12／Vulkan 適合は通常 device で実行した。追加 validation layer による検証成功とは扱わない。

最終検証は既存の Browser、Slang、DXC、Tint の環境変数を指定し、
`dotnet test Lumyte.slnx -m:4 --logger "trx;LogFilePrefix=render-graph-stage0-solution-final" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini`
を実行した。**42 project・2,472件成功、失敗0、skip 0**、全 TRX が Completed、終了コード0。
新規99件は CPU 86件と実機13件で、DX12 431件、Vulkan 439件、Dawn 343件、Browser 61件を含む。
新しい共通 graph 36件、Hosting 12件、共通 Passes 8件、Native provider／Passes 11／3件、
Portable provider／Passes 14／2件もすべて成功した。
66文書・446ローカルリンク・21アンカー、37 ADR の必要章と179個の番号依存を確認し、問題はなかった。
production 向け InternalsVisibleTo を追加せず、既存13指定はすべて test assembly 向けである。

## 検証レイヤーと旧システムの削除

2026-09-14 に Windows Graphics Tools と Vulkan SDK 1.4.357 の導入を確認した。
DX12 の検証付き段階 0 適合で、texture copy の中間 buffer を COPY_DEST からコピー元として使う際に
`MessageIDInvalidSubresourceState` が検出された。線形 buffer だけが旧 `CreatePlacedResource` と
legacy resource state で作成されており、enhanced global barrier との混在が原因だった。
線形 buffer の requirement と作成を `ResourceDesc1`、`GetResourceAllocationInfo2`、
`CreatePlacedResource2` と `Undefined` に統一した。新しい状態追跡や暗黙 barrier は追加しない。

DX12 と Vulkan の段階 0 適合は各4ケースとも検証を必須にし、画素だけでなく警告・エラーの不在も確認する。
DX12 は InfoQueue のメッセージ破棄も検査する。Vulkan は同期検証を有効にして確認する。
環境と実行方法は [テスト文書](../testing.md#native-検証レイヤー) を参照する。

旧共通 backend、resource／command／PSO／shader ABI、手動 allocator、固定共通上限、
旧 RenderGraph、旧 Library／TwoD／Text、旧 shader library と offline compiler、
旧 Vulkan window sample と旧 graph benchmark を削除した。旧 shader の書換え処理や Legacy factory、
旧 format の reader、型転送・互換 wrapper は提供しない。
現行の Native／Portable の backend、各 Resources／shader／generator／provider、共通 Passes と Hosting は維持する。
共通 Graphics は現在必要な基本値と device loss 例外のみを定義する。

撤去した機能に専用のテストは、その機能と一緒に削除する。新方式の Model／2D／文字描画を
実装済みと扱わず、必要な能力と過去の調査結果は ADR 0035／0036 に残す。
これらの次回実装では新契約の適合試験を追加し、旧 API の再導入を前提にしない。

最終検証は Browser／Slang／Tint の環境変数と `VK_LAYER_VALIDATE_SYNC=1` を設定し、
DXC は各現行 project の NuGet dependency から解決して、
`dotnet test Lumyte.slnx -m:4 --logger "trx;LogFilePrefix=graphics-cleanup-final" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini`
を実行した。**37 project・1,741件成功、失敗0、skip 0**、全 TRX が Completed、終了コード0。
DirectX 12 は276件、Vulkan は281件、Dawn は189件、Browser は61件成功し、
DX12／Vulkan の各4件の検証付き feature graph も警告・エラー0で成功した。
前回からの731件の減少は旧機能専用のテストと5 test project の撤去によるもので、
現行機能の失敗を skip や期待値の緩和で回避したものではない。
88現行文書のローカルリンクと84 project の参照先に欠落はなく、`git diff --check` も成功した。

## RenderGraph 基盤の完成

2026-09-14 に ADR 0030〜0032 の共通 graph と Native／Portable provider の基盤を完成した。
利用側は同じ機能 contract と入力を使い、内部 pass、shader、資源と binding の準備は二系統で扱う。

| 範囲 | 実装と確認 |
| --- | --- |
| 共通の計画 | 容量制限付き LRU の plan cache、複数 contributor の順序・名前空間・資源共有、出力からの culling と資源寿命 |
| CPU 入力 | 不変 snapshot の共有、変更枝だけの保持情報更新、plan／bindings の反復利用。graph の identity は builder を所有せず、除外した入力を計画に残さない |
| 内部 graph | Read／Write／ReadWrite、未初期化 read、機能側宣言との対応、内部 culling、index だけの schedule cache、CPU template と準備 cache |
| 一時資源 | execution 内で description が同じ、寿命が重ならない resource object を再利用。公開出力は最後まで保護し、同時実行間では共有しない |
| 内容世代 | writer と reader の保持、受理済み upload からの依存、受理前失敗・遅延診断による失効、GPU 使用終了後の回収 |
| 外部所有 | 両 provider の raw buffer／texture import と lease、受理済み提出からの接続。共通 execution と frame も GPU 使用終了まで所有を保つ |
| 生成器 | `.portable.resources.xml` から内部 pass の buffer／view と sampler の binding 入力を生成。Output の本体で使用し、生成 API の consumer 試験を追加 |
| 起動 | 選択した provider だけを準備し、DI scope と runtime options を typed pass factory に渡す。登録衝突は GPU 準備前に検出 |

Native Texture Copy の内部処理を texture → scratch buffer と scratch buffer → texture に分け、
中間 buffer の初期化と依存を graph へ正しく宣言した。複数 mip／layer を一つの全体書込として扱い、
途中の書込が culling されないことを確認する。Portable Clear も全 mip／layer を一つの内部 pass から初期化する。

同じ plan に異なる色の bindings を渡し、CPU を待たせず二回提出して両方の画素を検査する実機試験を追加した。
5 回の Copy を含む graph で一時資源の再利用と出力の独立性を確認し、
DX12／Vulkan の feature graph は各5ケースを検証レイヤー有効で実行する。

終了時に一つの lease の解放が失敗しても他の frame と lease の返却を試み、診断をまとめて通知する。
Hosting は進行中の終了を共有し、失敗後の明示 Stop／Dispose では残存所有の cleanup を再試行する。
GPU 受理済みの保持登録に失敗した場合も、完了情報と target の退役を失わず、使用終了まで lease を保持する。
これらの失敗と、import だけを MarkOutput する buffer／texture の GPU 保持を CPU 回帰試験で確認した。

CPU planning と bindings 更新の BenchmarkDotNet 項目を `Lumyte.Benchmarks` に追加し、
benchmark project の build を確認した。性能測定は未実施であり、60 FPS の達成を主張しない。

最終検証は Browser／Slang／Tint の環境変数と `VK_LAYER_VALIDATE_SYNC=1` を設定し、
DXC は各 project の NuGet dependency から解決して、
`dotnet test Lumyte.slnx --disable-build-servers -m:4 --logger "trx;LogFilePrefix=render-graph-complete-verified" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini`
を実行した。**38 project・1,859件成功、失敗0、skip 0**、全 TRX が Completed、終了コード0。
前回から118件を追加し、共通 graph 60件、Native provider 41件、Portable provider 42件、
Hosting 25件、新しい Portable graph generator 14件が成功した。
DirectX 12 は277件、Vulkan は282件、Dawn は190件、Browser は61件成功し、
DX12／Vulkan の各5件の feature graph は検証の警告・エラー0だった。
92現行文書のローカルリンク、86 project と solution の参照先に欠落はなく、`git diff --check` も成功した。
InternalsVisibleTo の9指定はすべて test assembly 向けであり、production 向けの指定はない。

## 2D の新 RenderGraph への統合

2026-09-16 に、[ADR 0036](../adr/0036-2d-render-passes.md) の CPU scene と Native／Portable の 2D pass を実装した。
利用側は `Draw2DSceneBuilder` または `Draw2DSceneStore` の snapshot を `Add2DPass` に渡す。
`AddLumyteGraphics(...).Add2DRendering()` で二系統を登録し、選択した provider が shader、GPU data、内部 pass と資源を準備する。
旧 renderer／backend への互換経路は追加していない。

- 図形、Bezier path と fill rule、stroke の join／cap／dash、各種 gradient、画像と image brush、affine transform と clip を GPU で描画する。
- layer の opacity／mask／blur／shadow、Porter–Duff と blend mode、prepared text／color glyph、供給済み coverage／SDF／MSDF を扱う。font／画像のロードと shaping は引き続き Resources の責務である。
- 変更枝だけを更新する CPU store、入力 snapshot の所有共有、plan と bindings の再利用、provider 別の準備 cache と GPU 内容保持により、未変更の内容を再利用する。
- slot から利用する graph texture は固定の Read 集合で宣言する。自身の描画先を画像として読むことや、入力更新で未宣言の依存を増やすことを防ぐ。前段の 2D pass が作った画像を後段の 2D pass で利用できる。
- 線形 premultiplied RGBA、RGBA8／BGRA8／RGBA16Float の描画先を扱う。Native は bindless descriptor と直接 root data、Portable は明示 binding と直接 immediate data を使用する。

SkiaSharp は試験用 Conformance project にだけ追加した。同じ scene を独立した Skia API で描いて実機 readback と比較し、描画範囲内の誤差と AA 境界の誤差を分けて確認する。
参照画像の色空間、補間、AA、HDR と失敗時の PNG 出力は [2D 比較試験](../../src/graphics/Lumyte.Graphics.RenderGraph.Conformance/TwoD/README.md) に記録する。

本実装は各描画要素の GPU coverage／paint／合成と全画面の中間処理を使う。
atlas の packing と退役領域の再利用、coverage／距離場の自動生成、tile／scissor 最適化、batch 化、共有 Slang module と性能計測は残る。
部分更新の再利用は確認対象とするが、実際の描画量に対する 60 FPS はまだ保証しない。

検証は Browser／Slang／Tint の環境変数と `VK_LAYER_VALIDATE_SYNC=1` を設定し、
`dotnet build Lumyte.slnx --disable-build-servers -m:4 --no-restore` が警告0・エラー0で成功した。
続く `dotnet test Lumyte.slnx --no-build --no-restore --disable-build-servers -m:4 --logger "trx;LogFilePrefix=two-d-verified" --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini`
は **41 project・2,149件成功、失敗0、skip 0**、全 TRX が Completed、終了コード0だった。
DirectX 12 は357件、Vulkan は358件、Dawn は266件、Browser は64件成功した。

その後、Portable の `Close()` に続く `LineTo()` が閉じた輪郭の点列へ追記してしまう不具合を修正した。
修正前だけに戻した WebGPU の比較試験で余分な塗りを検出し、元へ戻した修正済みコードに対して
`dotnet test Lumyte.slnx --disable-build-servers -m:4 --no-restore --filter "DisplayName~post-close-path" --logger "trx;LogFilePrefix=two-d-post-close-verified" --blame-hang-timeout 2m --blame-hang-dump-type none`
を実行した。追加した DirectX 12／Vulkan／Dawn の **3件すべて成功、失敗0、skip 0**。
全体実行と追加回帰を合わせ、SkiaSharp による実 GPU の 2D 比較は234件成功した。
DirectX 12／Vulkan の比較は検証レイヤーを有効にし、警告・エラーがないことも確認した。

## 標準画像処理と実画面表示

2026-09-16 に ADR の実装状況を更新し、独立した Blit／Blur／Composite／ToneMap を追加した。
Clear／Copy／Output と合わせて七機能を `AddImageProcessing()` で登録する。
Native は Slang から生成した shader と root 型、Portable は直接 WGSL と immediate data を使う。
パラメータを command 側の buffer へ退避する経路は追加していない。

- Blit は pixel center と nearest／linear、Blur は端を clamp する二段 box filter。巨大な radius は端の重みへ集約する。
- Composite は順序を保つ premultiplied SourceOver と opacity、Clear／Preserve、空の layer 列を扱う。
- ToneMap は exposure と Reinhard を適用し、alpha 0 と非常に大きい有限 exposure でも結果を維持する。
- 対象は線形 RGBA8／BGRA8／RGBA16Float の 2D・1 mip・1 layer・1 sample。Blur／ToneMap の出力は RGBA16Float。
- 同じ consumer の 12 シナリオを独立した CPU 参照値へ比較する。2D の SkiaSharp 比較も既存 suite として継続する。

`INativeGpuSurface`／`IPortableGpuSurface`、`NativeGraphPresentation`／`PortableGraphPresentation` と
`GpuSurfacePresentation` を追加し、DirectX 12／Vulkan／Dawn の Win32 window と Browser canvas に接続した。
表示先の画像は surface が所有し、adapter が graph への一時 import と返却を担当する。
Generic Host は `UsePresentation` の delegate と `GpuGraphicsSurfaceConnection` でこれを所有する。
window の生成・message pump・破棄は application に残す。

DirectX 12 は描画完了と Present 後の fence を分け、backbuffer の General を COMMON／PRESENT に対応させる。
修正前は Present 後の使用が残った状態で resize し、DXGI が異常終了した。
回帰試験は raw acquire／Present／Discard と、検証レイヤー付き RenderGraph の表示・三回の extent 変更を含む。
Vulkan は acquire fence、表示用 binary semaphore、maintenance1 の present fence を使い、
OUT_OF_DATE の再生成と非表示の画像返却を分ける。使用終了を確認できない acquire／present の失敗では保持する。
Browser は非同期 graph 準備を跨ぐ owned texture から、同じ JavaScript turn で canvas current texture へ copy する。
実 canvas の画素は自動 expiry より前に snapshot して検証する。

Host の停止は待機中の acquire にも取消しを伝える。取消せず遅れて届いた画像は返してから終了し、
描画・表示の使用終了が不明な場合に強制解放しない。
[ウィンドウサンプル](../../samples/graphics/Lumyte.Graphics.Hosting.Sample/README.md) は同じ実行ファイルを
`dx12`／`vulkan`／`webgpu` に切り替えて、2D と標準画像処理から表示・終了までを確認する。

この表示実装は一つずつ画像を取得・返却する基準経路である。複数の表示フレームを並列に先行させる最適化、
HDR display、Win32 以外の native WSI、全 adapter／driver と device loss の強制試験は後続とする。

検証は Browser／Slang／Tint の環境変数と `VK_LAYER_VALIDATE_SYNC=1` を設定し、
`dotnet test Lumyte.slnx --disable-build-servers -m:4 --no-restore --logger "trx;LogFilePrefix=standard-images-presentation" --blame-hang-timeout 2m --blame-hang-dump-type none`
が **41 project・2,212 件成功、失敗 0、skip 0、終了コード 0** だった。全 TRX が Completed。
DirectX 12 は 373 件、Vulkan は 372 件、Dawn は 280 件、Browser は 66 件成功した。
全体実行後の Host 取得取消しの回帰追加は Hosting の 26 件を再実行してすべて成功し、
全体実行と新規回帰を合わせた重複を除く確認数は **2,213 件**。
Vulkan の acquire 異常時保持の変更も、検証レイヤー付き window 試験を再実行して成功した。
標準画像処理の 12 シナリオは 4 経路で確認し、Browser は一つの試験内で全シナリオを実行する。
最終サンプル build は警告 0・エラー 0、3 backend で各 3 フレームの実表示から終了まで成功した。
ADR と関連文書のローカルリンクに欠落はなく、production 向け InternalsVisibleTo はない。

## 未実装と次の順序

1. Model の個別機能を追加する。形式に依存しない転送データ、保持集合、PBR／skin／morph、Native の mesh 経路を専用 ADR に従って実装する。
2. 2D の残る batch／atlas 最適化と部分更新の性能、複数表示フレームの先行、ResourceManager の性能を確認する。表示の基準実装は一つずつ取得・返却する方式であり、60 FPS の測定結果とは区別する。
3. Portable Slang の accessor／prelude、残る matrix／array の host 表現、生成入力を使う GPU conformance を追加し、後続機能 pass へ広げる。NoGraphicsAPI に合わせるためだけの Native 入力 ABI 変更は今回の優先作業にしない。

任意 graph の tracing GC、予算による資産 eviction、CLR GC 連動、ResourceManager の性能 benchmark はこの段階に含めない。device loss や停止未確認の work を強制回収する API はなく、保持して終了を失敗させる。全 adapter／format と実 driver の device loss を強制する適合性は未検証である。

保持型 Model と、Slang toolchain の残る共通化は後続作業である。旧描画系を復元する互換経路は設けない。各描画機能の実装後には、その機能を使う conformance 試験を追加する。
