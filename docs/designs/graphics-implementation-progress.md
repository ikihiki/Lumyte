# Graphics 実装進捗

37 ADR の目標設計に対する実装状況を記録する。最初の統合完了条件は [ADR 0034 の段階 0](../adr/0034-render-pass-categories.md#実装順と完了条件) にある起動 → Clear／Copy／Output → 提出結果 → 回収 → 終了であり、以下のメモリ基盤だけで達成したとは扱わない。

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

## 未実装と次の順序

1. Native texture の requirement と明示配置を追加し、線形 region と同じ heap への混在配置を native 条件の下で確認する。
2. Native command、copy、主 queue、提出と completion を実装し、アップロード → GPU copy → readback の結果と明示的な寿命管理を実機確認する。limits は対応する操作の実装と合わせて追加する。
3. Native descriptor／view、shader、pipeline と描画を接続する。root data の直接入力、parameter data を command で扱わない方針を維持する。
4. 独立した Portable API と WebGPU、両系統の Resources・shader・RenderGraph provider、共通 Hosting を実装し、同じ consumer binary で段階 0 を通す。

mesh／amplification、保持型 Model／2D、Slang toolchain の製品実装への統合と既存描画系の移行は、これらの後続作業である。driver 更新により、この PC で Native Vulkan の初期化とメモリ基盤を実機検証できるようになった。各描画機能の実装後には、その機能を使う conformance 試験を追加する。
