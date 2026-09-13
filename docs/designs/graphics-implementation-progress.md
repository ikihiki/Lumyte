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

## 未実装と次の順序

1. Portable の view／binding、shader／pipeline、copy を含む command、queue completion と Browser runtime を実装する。Portable に bindless、explicit placement、mesh の必須条件を持ち込まない。
2. 両系統の Resources・shader package／loader・RenderGraph provider と共通 Hosting を実装し、同じ consumer binary で段階 0 を通す。

保持型 Model／2D、Slang toolchain の製品実装への統合と既存描画系の移行は、これらの後続作業である。各描画機能の実装後には、その機能を使う conformance 試験を追加する。
