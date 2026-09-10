# ADR 0013: DirectX 12 Native Backend 実装

## 状態

採用（目標設計）。DirectX 12 による Native 基盤の実装を定義する。現行実装の完了や旧 API との互換は示さない。

## 依存 ADR

- [0001 Graphics の層構造と共通契約](0001-graphics-api.md): 共通値、caller lifetime、native validation への委譲。
- [0002 Native Graphics API](0002-native-graphics-api.md): `INativeGpuBackend`、device 機能、初期化と失敗。
- [0003 Native Memory Allocation](0003-native-memory-allocation-api.md)・[0004 Native Linear Data](0004-native-linear-data-api.md)・[0005 Native Texture](0005-native-texture-api.md): heap、linear region/range、texture の配置と所有権。
- [0006 Native View](0006-native-view-api.md)・[0007 Native Bindless](0007-native-bindless-api.md)・[0008 Native Descriptor](0008-native-descriptor-api.md): view、caller index、descriptor storage と書込み。
- [0009 Native Shader](0009-native-shader-api.md)・[0010 Native Pipeline State](0010-native-pipeline-state-api.md): raw code、直接 root ABI、pipeline と state。
- [0011 Native Command Recording](0011-native-command-recording-api.md)・[0012 Native 提出と同期](0012-native-command-submission-and-synchronization.md): 記録、提出、completion と内部 memory の寿命。

この backend は Native API だけを実装し、Native の shader・Resources と機能 pass 本体から利用する。入力は raw DXIL、Native の heap/range/texture と直接 root である。共通 RenderGraph の機能 pass に対応する Native 実装が、自身の shader と GPU 入力、内部 graph、低層 command を構築する。backend は共通 Graph の型や機能 pass の意味へ依存しない。Portable 系統をこの backend へ変換する adapter は設けない。

## 決定

NoGraphicsAPI の caller-owned heap、descriptor slot、各 work の直接 root payload、global barrier、明示 completion を基礎とする。DirectX 12 に必要な resource identity、root signature、texture transition、depth/stencil を含む native PSO は明示的な差分として残す。

DirectX 12 の native object と有効機能を直接使う。resource の寿命・使用履歴・descriptor の到達先を追跡する層、slot allocator、global cache、GPU address の逆引き表は追加しない。

mesh shader は任意機能として追加する。device 初期化で `D3D12_FEATURE_D3D12_OPTIONS7.MeshShaderTier` を取得し、対応 tier では `Capabilities.MeshShaders` と `Capabilities.AmplificationShaders` を有効にする。mesh の非対応だけで Native backend 全体を使用不可にせず、従来の vertex/indexed draw を維持する。上限は選択した shader model と native 機能から `Limits.MeshShader` へ反映し、新しい照会 API や software emulation は設けない。[Mesh Shader の device 機能](https://microsoft.github.io/DirectX-Specs/d3d/MeshShader.html#checkfeaturesupport)

## Heap と address

`NativeGpuHeap` は `ID3D12Heap` に対応する純粋な allocation であり、linear data と texture の確保を `CreateGpuHeap`／`DestroyGpuHeap` に統一する。公開する値は `Size`、`Alignment`、`Kind` で、heap 自体に buffer resource、GPU address、CPU pointer は持たせない。

`CreateLinearRegion(size, heap, offset)` は caller が指定した heap 内に backing buffer を placed resource として生成する。`NativeGpuLinearRegion` がこの buffer と実 GPU virtual address を所有し、CPU 可視の memory kind では resource の `Map` から `CpuAddress` を提供する。`DestroyLinearRegion` は buffer を解放し、heap は解放しない。caller は一つの region を byte range に分割して使い、range ごとの resource を作る必要はない。

`NativeGpuRange(Region, Offset, Size)` の `Offset` は region 内の byte offset とする。index fetch には `Region.GpuAddress + Offset` と size、copy と indirect には region の backing resource と同じ `Offset` を渡す。heap への配置位置 `Region.HeapOffset` を resource 内の offset に重ねて加算しない。region identity の保持は DirectX 12 が resource object を要求する入口への変換に使い、global な address→resource 検索や使用状態追跡を必要としない。

texture も同じ caller-owned `NativeGpuHeap` の offset に placed resource として生成する。heap、linear region、texture、render view は caller が別々に所有し、resource を破棄しても heap を自動解放しない。配置と aliasing の同期は caller が管理する。

`GetLinearMemoryRequirements(size, kind)` と `GetTextureMemoryRequirements(description, kind)` は native の作成条件から共通の `NativeGpuMemoryRequirements` を返す。`Size` と `Alignment` は caller が予約する容量と整列であり、resource の実データ量とは区別する。backend は native allocation と placed resource に必要な整列を含めて返し、`Size` を最終的な `Alignment` の倍数へ切り上げる。確保時も同じ memory kind を使い、requirement 取得と resource 作成の native 条件を一致させる。

各 requirement の opaque な `Compatibility` を `CreateGpuHeap(size, alignment, kind, compatibilities)` に列として渡す。backend はその native 条件から一つの heap の flags を構成する。これは確保パラメータの構築であり、公開の統合・分類・互換判定 API は設けない。要求を一つの heap で満たせないときは確保を失敗させ、自動的な複数 heap への分割、resource 配置の予約、使用履歴の追跡は行わない。

Resource Heap Tier 2 では buffer、非 render-target/depth-stencil texture、render-target/depth-stencil texture の混在を許す heap を作れる。Tier 1 ではこれらを一つの heap に混在できないため、caller が同じ確保 API で別々の heap を用意する。`GpuOnly` だけを理由にすべての組合せの共用を保証しない。UPLOAD／READBACK heap に texture は配置できず、memory kind ごとに必要な native 生成条件を使う。[Resource Heap Tier](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/ne-d3d12-d3d12_resource_heap_tier)・[heap type](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/ne-d3d12-d3d12_heap_type)

`MutableFormat` は既定 false とし、true のときは native の typeless resource を使って追加の互換 format view を許可する。false でも depth/stencil の aspect 解釈や sampled depth など基本用途に必要な typeless 表現を内部で用意し、native resource/view の format がすべて同一であることを要求しない。公開 API に許可 format の列挙は持たず、指定 format と用途から native 作成情報へ変換する。true でも未対応の組を保証せず、format の互換範囲と view の合法性は DirectX 12 の作成処理と debug layer へ委ねる。

resource の整列・容量・heap 条件の合法性は native の確保・配置処理と debug layer へ委ねる。resource state の照会や履歴 tracker は設けない。

新しい texture は `CreatePlacedResource2` の initial layout を `D3D12_BARRIER_LAYOUT_UNDEFINED` とし、内容を不定とする。caller は最初の使用前に `TextureTransition(view, Undefined, 用途の layout)` を記録する。この transition は before access を `D3D12_BARRIER_ACCESS_NO_ACCESS` とし、`D3D12_TEXTURE_BARRIER_FLAG_DISCARD` で必要な圧縮 metadata を初期化する。内容を有効にする copy/clear とその後の依存は caller が指定する。これは固定した生成契約であり、初期 state を別途照会しない。[CreatePlacedResource2](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12device10-createplacedresource2)・[Enhanced Barriers の初期化契約](https://microsoft.github.io/DirectX-Specs/d3d/D3D12EnhancedBarriers.html#placed-resource-metadata-initialization)

`GpuAddress` の取得は shader が任意 pointer を dereference できるという保証ではない。DirectX 12 の `RawShaderPointers` は未対応とする。shader の線形 data 参照は `BufferDescriptors` による buffer descriptor と offset を使い、descriptor index を実 GPU address と偽装しない。

## Application-owned descriptor heap

resource と sampler の `NativeGpuDescriptorHeap` をそれぞれ専用の shader-visible native heap に対応付ける。caller が容量、slot、更新時点と寿命を決める。heap は storage の所有者であり、slot の貸出・返却を行う allocator ではない。型別の領域や profile、空 slot の補完、旧内容の snapshot を Native 層で管理しない。

descriptor heap は opaque な native descriptor storage であり、linear region と texture の共通 `NativeGpuHeap` に配置する resource にはしない。通常の memory allocation の統一と descriptor storage の契約は区別する。

resource heap は texture descriptor と補足の buffer descriptor を保持する。sampler heap は sampler descriptor を保持する。`WriteTextureDescriptor`／`WriteBufferDescriptor`／`WriteSamplerDescriptor` は caller 指定 slot に native API で書き込む。DirectX 12 の descriptor handle は opaque であり、CPU pointer として dereference したり native descriptor bytes を任意編集したりする契約にはしない。

slot の位置は heap の先頭 handle に `index * native handle increment` を加えて求める。increment は device と heap type ごとに取得する handle 演算の値であり、mapped memory の stride や descriptor の byte size ではない。Vulkan の descriptor bytes と共通の生 memory 表現を装わない。[DirectX 12 の descriptor handle](https://learn.microsoft.com/en-us/windows/win32/direct3d12/creating-descriptor-heaps#descriptor-heap-methods)

shader は対応 device の `ResourceDescriptorHeap`／`SamplerDescriptorHeap` を直接 index で参照する。必要な root-signature flags と native heap 選択の順序を実装する。heap 全体の選択は bindless の実行基盤であり、draw ごとの binding set・resource list・schema は作らない。

attachment の RTV/DSV は caller-owned `NativeGpuRenderViewHandle` で管理し、shader index と区別する。view の値計算だけでは native object を作らず、明示した render view の生成または descriptor 書込みで native 表現を用意する。view が親 texture を延命する保証はない。

sampler の `MinLod`、`MaxLod`、`MaxAnisotropy` は指定値を native sampler に反映する。`CreateRenderView(view, flags)` の `NativeGpuRenderViewFlags.DepthReadOnly/StencilReadOnly` は DSV の native flags に反映する。attachment の aspect ごとの nullable load/store は指定された有無を維持し、clear 値もそのまま渡す。存在しない aspect や read-only aspect の operation を補完して書込みへ変えない。合法性は native 作成処理と debug layer が診断する。

## DXIL と root payload

`NativeGpuShaderProgram` は DXIL code と entry point を受け取る。shader と heap indexing、root byte 配置は Native ABI に従い caller/offline compiler が一致させる。Native backend は reflection による resource 集合の復元、typed profile 比較、package の読込みを行わない。native に必要な root signature はこの ABI の内部表現であり、caller が program ごとの binding layout を組み立てる API は加えない。

各 draw/dispatch の `rootData` は呼出し時に CPU 記録へコピーし、`Submit` で native command に変換するとき root constants として直接渡す。caller は呼出し後に元の byte 領域を再利用できる。長さは 4 byte の倍数で `MaxRootDataSize` 以下、空入力は許可する。64 byte 固定、末尾 zero fill、独立した root state setter は Native の規則にしない。入力の意味や pointer を CPU で解釈せず、Parameter Data の生成・upload・保持や GPU buffer への fallback を行わない。

DirectX 12 の root signature 全体の上限は 64 DWORD である。実際に root payload へ使用できる範囲を `MaxRootDataSize` と Native ABI に反映し、64 byte と混同しない。row-major と座標規約の補正は compiler または native raster state の一方で行う。

mesh／amplification も同じ graphics root signature と直接 root constants を使う。root と heap indexing の visibility に両 stage を含め、`DispatchMesh` ごとに渡した root が両 stage と pixel stage から参照できるようにする。amplification から mesh へ渡す shader 内の payload は root data と別の GPU 内通信であり、command が payload buffer を生成・upload する契約にはしない。

## PSO と dynamic state

DirectX 12 は depth/stencil の動作を graphics PSO に含む。`CreateRasterPipeline` は論理 pipeline を作り、固定 state の description、raw DXIL と entry point を自身で保持する。後で caller の入力を読み直すことなく、必要な native PSO を生成できるようにする。

`NativeGpuQueue.Submit` 内で、batch の draw/indexed draw、mesh dispatch とそれぞれの indirect 版に実際に使う pipeline と depth/stencil の組を解決する。設定されても描画に使われない組は生成しない。不足する native PSO だけを生成し、成功した PSO は pipeline が Destroy まで所有して以後の提出で再利用する。global cache と自動 eviction は設けない。

mesh を選んだ `NativeGpuRasterPipelineDescription` は `MS`、必要なら `AS`、任意の `PS` を pipeline state stream へ写す。vertex pipeline の `VS`／input layout を同じ組に混ぜず、`MeshOutputTopology` の line／triangle を出力 primitive の topology type に写す。shader の output 宣言との一致と linkage は compiler／native PSO 作成処理に委ねる。mesh 用の別 PSO preparation API は加えず、論理 pipeline の所有、Submit 時の生成と失敗境界を共有する。[Mesh Shader の pipeline state](https://microsoft.github.io/DirectX-Specs/d3d/MeshShader.html#createpipelinestate)

組の key は native PSO に固定される state 値だけとする。stencil reference、viewport/scissor、descriptor index、resource identity、root bytes は含めない。reference は command の動的値として記録し、同じ動作の state を別 identity のために再生成しない。

front/back の個別 stencil reference は対応する native 機能で設定する。Native 契約を満たす device 機能を初期化時に選び、片面の値へ黙って揃えない。blend と rasterization はこの最小 Native 契約では PSO に残し、独立 blend の全面的な分離を提供したとは扱わない。

compute pipeline は作成時に native 生成を完了する。shader の linkage、format、sample count と state の適合性は compiler/native 作成処理と debug layer が診断し、wrapper が同じ validator を再実装しない。

## Global barrier と texture transition

`Barrier(beforeStages, beforeAccess, afterStages, afterAccess)` は enhanced global barrier に対応付け、resource の列挙なしに execution/memory dependency を表す。buffer に独自の current state tracker を設けない。

`GpuStage.AmplificationShader` と `GpuStage.MeshShader` は、両 stage を含む `D3D12_BARRIER_SYNC_VERTEX_SHADING` に対応する。indirect 引数の読み取りは `EXECUTE_INDIRECT`／`INDIRECT_ARGUMENT`、shader が読む meshlet や可視リストは対象 stage の shader access に分ける。compute が両方を書いた場合も caller の依存指定をこの範囲へ写し、同じ data だからという理由で片方を省略しない。[Enhanced Barriers の vertex shading scope](https://microsoft.github.io/DirectX-Specs/d3d/D3D12EnhancedBarriers.html#d3d12_barrier_sync_vertex_shading)

global barrier は texture layout を変更しない。DirectX 12 で必要な layout 変更は `TextureTransition(view, beforeLayout, afterLayout)` に caller が明示する。同期範囲の変換に必要な情報をその入力から得て、不足する before state を履歴から算出しない。texture の初期化・aliasing・presentation で native が必要とする処理を、一般的な自動 state 管理へ拡張しない。

DirectX 12 の enhanced barrier には host 専用 bit がない。`GpuStage.Host` は `ALL`、`GpuAccess.HostRead/Write` は `COMMON` の global access へ写し、CPU 読み取りの開始は caller の fence completion 待機で順序付ける。mapped range や Readback heap の利用を見て暗黙に barrier／wait を追加しない。

`DiscardTexture(view, afterLayout)` は指定 mip／array layer／aspect を native の subresource range に写し、`LayoutBefore = UNDEFINED`、`AccessBefore = NO_ACCESS`、`Flags = DISCARD` の enhanced texture barrier で必要な metadata を初期化する。`LayoutAfter` と対応する access は caller の次用途から変換し、初期化を前後の work へ順序付ける sync scope は `ALL` とする。古い layout からの decompression や旧 data の保存は行わない。

alias の write flush は caller の先行 global barrier に分ける。flush と discard を同時に実行し得る barrier 群へまとめず、先行 barrier の `SyncAfter` と discard の scope を `ALL` で接続し、同じ memory を更新する native barrier の順序条件を守る。既存 texture の再有効化も command に明示された回数だけ変換する。CPU 記録や native encode の時点で application resource を「再初期化済み」とする registry は持たず、受理されなかった記録は破棄する。[Enhanced Barriers の alias と順序](https://microsoft.github.io/DirectX-Specs/d3d/D3D12EnhancedBarriers.html#barrier-ordering)

NoGraphicsAPI の通常 texture を単一 layout に保つ方式を、DirectX 12 の全 texture に完全適用したとは扱わない。特に render target/depth-stencil の利用を global barrier だけで成立させる保証はない。

## Recording と completion

記録呼出し時点では必要な raster PSO が存在しない場合があるため、command の順序とその引数を CPU 記録に保持する。draw の値、root bytes、state、attachment の配列と clear 値などは呼出し時にコピーする。resource の identity は非所有とし、その保持で GPU resource の寿命を引き受けない。

`Submit` は必要な PSO の組の解決・不足 PSO の生成を行い、CPU 記録を順番どおりに native command list へ変換する。index fetch と基本 indirect は native 機能を使い、GPU 引数を CPU へ readback・展開しない。copy は指定 range/footprint を使い、staging や暗黙の追加 copy で不適合を修復しない。

`DispatchMesh(rootData, x, y, z)` は rendering 内で graphics root を設定し、`ID3D12GraphicsCommandList6.DispatchMesh` に対応付ける。`DispatchMeshIndirect(rootData, arguments)` は `D3D12_INDIRECT_ARGUMENT_TYPE_DISPATCH_MESH` だけを持つ command signature と `ExecuteIndirect` を使い、1 件の X／Y／Z を `arguments.Region` の backing resource と region 相対 `Offset` から読む。stride は 12 byte、`MaxCommandCount = 1`、count buffer は null とし、root は indirect record に含めない。mesh shader が自身で index／vertex data を読むため、IA の index buffer や `DrawIndexed` へ変換しない。[Mesh Shader の dispatch と indirect](https://microsoft.github.io/DirectX-Specs/d3d/MeshShader.html#executeindirect)

texture copy は footprint の `Aspect` を plane index に変換する。color と depth は plane 0、depth/stencil format の stencil は plane 1 とし、mip・layer・plane から `CopyTextureRegion` の subresource index を構成する。線形側は range の region 相対 offset と指定 pitch の `D3D12_PLACED_SUBRESOURCE_FOOTPRINT` を使う。24-bit depth の転送 format は `R32_TYPELESS` family、32-bit float depth は `R32_FLOAT`、16-bit depth は `R16_UNORM`、stencil は `R8_UINT` に対応する。DSV／SRV の view format をそのまま buffer footprint の format に流用しない。[Planar Depth Stencil](https://microsoft.github.io/DirectX-Specs/d3d/PlanarDepthStencilDDISpec.html)

`ImagePitch` による各 layer／slice の開始位置を caller の配置のまま native copy へ写す。必要なら指定された範囲を複数の native copy region に分けるが、新しい staging resource や data 変換は生成しない。native の pitch／offset 整列、depth/stencil copy の全 subresource 条件と sample count の制約を caller が満たす。これらの合法性を wrapper 内で再検証せず、debug layer の結果を伝える。[CopyTextureRegion](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12graphicscommandlist-copytextureregion)

batch 全体の PSO と command list の生成・終了が成功してから、queue が command list を実行して native fence を signal する。PSO 生成や変換・終了が失敗した batch は受理せず、GPU work を開始しない。caller はその記録を `Dispose` し、新しい記録を作る。生成に成功した PSO は pipeline の内部に保持してよい。受理と completion を区別し、受理済み work を再提出可能な状態へ戻さない。受理後の signal を保証できなくなれば device loss として停止し、永久に届かない completion を通常待機として残さない。

command allocator と記録用 memory だけを backend が所有する。未提出 command の `Dispose` は記録を破棄する。提出済み command の `Dispose` は GPU work を取消し・待機せず、対応 completion または確定した device 終了まで内部 memory を保持してから回収する。公開する command 状態は設けず、記録・終了・受理の管理は backend 内部に閉じる。application の heap、linear region、texture、render view、descriptor、pipeline の寿命は引き受けない。

queue は command memory 回収専用の内部 fence を一つ所有する。caller fence と内部 fence を実行後に signal し、caller が完了確認後に自身の fence を破棄しても回収できるようにする。回収用の管理領域は Execute 前に確保する。受理後に signal できなければ `RemoveDevice` で該当 device を停止し、以後の通常待機を device loss として拒否する。

## API

引数と共通契約は依存表の責務別 Native ADR を正とし、本章は DirectX 12 の対応を示す。

| API | DirectX 12 の対応 |
| --- | --- |
| `Capabilities`／`Limits`／`ShaderCodeFormat` | `RawShaderPointers` は非対応、`BufferDescriptors`・`ExplicitTextureTransitions` は対応、format は `Dxil`。mesh／amplification は `MeshShaderTier` に応じて個別の capability と上限を返す。 |
| `GetLinearMemoryRequirements`／`GetTextureMemoryRequirements` | 指定 kind と native resource description から共通の予約容量・整列・opaque compatibility を取得する。 |
| `CreateGpuHeap`／`DestroyGpuHeap` | compatibility の列と指定 size/alignment/kind から一つの `ID3D12Heap` を確保・解放する。線形 data と texture で同じ入口を使う。 |
| `CreateLinearRegion`／`DestroyLinearRegion`／`NativeGpuRange.GpuAddress` | 指定 heap offset の placed buffer を生成・破棄し、region の実 GPU address と range 内 offset から address を導出する。CPU pointer は対応 memory kind の buffer を Map して得る。 |
| `CreateTexture`／`DestroyTexture`／`CreateRenderView`／`DestroyRenderView` | `Undefined` の placed texture と attachment の RTV/DSV を明示的に生成・破棄する。`MutableFormat` は typeless resource に対応し、render view の flags は depth/stencil の read-only を保持する。 |
| descriptor heap の生成・破棄・3 種の書込み API | caller-owned native storage の指定 slot を native handle increment で求めて操作する。slot allocator は含めず、buffer descriptor は DX12 shader の線形参照に用いる。 |
| `SetResourceDescriptorHeap`／`SetSamplerDescriptorHeap` | command が使用する native heap を選択する。型別 profile は受け取らない。 |
| pipeline の生成・破棄と state 設定 API | raster は raw DXIL と固定 state を保持する論理 pipeline を作る。compute は作成時に native 生成を完了する。必要な raster PSO の組は `Submit` 内で生成する。 |
| rendering、viewport/scissor、draw/dispatch API | 引数・state・root bytes を順序付き CPU 記録へコピーし、`Submit` の native 変換時に各 work の root を直接渡す。 |
| `DispatchMesh`／`DispatchMeshIndirect` | graphics PSO と root を使う mesh dispatch と、1 件の `DISPATCH_MESH` を持つ `ExecuteIndirect`。amplification の有無は選択した pipeline に従う。 |
| `CopyMemory`／`CopyMemoryToTexture`／`CopyTextureToMemory` | range/footprint を記録し、`Submit` の native 変換時に backing resource/offset と指定 aspect の plane を使って copy を記録する。 |
| `Barrier`／`TextureTransition`／`DiscardTexture` | global dependency、caller 指定の texture layout 変更、旧内容を保持しない subresource の metadata 再初期化を分ける。 |
| queue の記録・提出・semaphore・照会・待機 API／`NativeGpuCommandBuffer.Dispose` | `Submit` 中に PSO を解決し、batch 全体の native 変換・終了後に queue へ提出する。記録状態を内部で管理し、未提出の破棄と提出済み memory の completion 後の回収を区別する。 |
| `NativeGpuBackendOptions.EnableValidation`／`NativeGpuException.NativeErrorCode` | DirectX 12 debug layer と native error を使用し、独自の使用検証層を重ねない。 |

## 検証の境界

host memory の安全、整数変換、backend が所有する identity/記録状態、使用する PSO の組の解決だけを確認する。descriptor の位置計算は device が返した heap type ごとの handle increment を使う。usage、format、shader/descriptor の適合性、GPU が導出する address/index、実際の before state は native validation と caller の責務に委ねる。破棄の安全性を証明する参照グラフや自動 pin は作らない。

aspect ごとの footprint／subresource 変換と、先行 flush → discard の記録順序は native 呼出しを差し替えて確認する。実 GPU では depth と stencil の独立転送、および同じ配置領域の A → B 書込み → A 再初期化を確認する。後続 PSO 生成失敗で batch が未提出となった後も、次の記録の discard を省略しないことを別に試験する。

mesh は、amplification なし／ありの描画、GPU が書いた indirect 引数からの 1 件の描画、各 work の root と stage barrier の写像を確認する。PSO を設定しただけでは生成せず、mesh dispatch が実際に Submit されたとき生成することと、生成失敗時に batch を受理しないことを確認する。非対応 device では capability を返し、vertex/indexed path の動作を維持する。shader の出力数や payload の合法性を独自 validator で再検証しない。

## コード配置

以下は repository root 相対の目標配置とする。既存の `Lumyte.Graphics.DirectX12` と隣の `Lumyte.Graphics.DirectX12.Tests` を Native 専用へ改編し、公開契約は新設予定の `Lumyte.Graphics.Native` を参照する。フォルダ分割は同一 backend project 内で行う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.DirectX12/Interop/` | DirectX 12／DXGI の呼出し、COM 所有と native の構造体・定数への接続。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Device/` | backend の入口、device・queue の生成、機能選択、debug layer と error/device loss。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Memory/`、`src/graphics/Lumyte.Graphics.DirectX12/LinearData/`、`src/graphics/Lumyte.Graphics.DirectX12/Textures/` | 純粋 heap、placed buffer と mapping、placed texture をそれぞれ実装し、allocation と resource の所有を分ける。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Views/`、`src/graphics/Lumyte.Graphics.DirectX12/Bindless/`、`src/graphics/Lumyte.Graphics.DirectX12/Descriptors/` | RTV／DSV、index ABI、caller-owned shader-visible heap と明示 slot 書込み。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Shaders/`、`src/graphics/Lumyte.Graphics.DirectX12/Pipelines/` | raw DXIL と root-signature ABI、vertex／mesh を選ぶ論理 raster pipeline と所有 PSO、即時生成する compute pipeline。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Commands/`、`src/graphics/Lumyte.Graphics.DirectX12/Submission/`、`src/graphics/Lumyte.Graphics.DirectX12/Synchronization/` | aspect 別 copy と mesh dispatch／indirect を含む CPU 記録と native 変換、Submit 時の PSO 解決・batch 受理、enhanced barrier／discard と fence を分担する。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/` | xUnit の unit test を上記の担当フォルダに対応させて配置する。native 呼出しを差し替え、変換・失敗境界・backend-owned object の回収を確認する。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/` | 担当別サブフォルダに実 GPU／debug layer を使う試験を配置し、通常の unit test から分離する。 |

公開 Native 型を backend 内に複製せず、Portable、上位 Resources、shader package、RenderGraph、Generic Host の登録処理への逆依存も作らない。既存 backend の改編は旧 API の adapter を残す配置を意味しない。

## 使用例

`native` は初期化済みの DirectX 12 `INativeGpuBackend`、`resources` と `samplers` は caller-owned descriptor heap、`pipeline` は論理 raster pipeline とする。attachment は必要な layout へ遷移済みで、`rootData` は shader の Native ABI に適合する。`completion` と単調増加する `completionValue` を用意し、caller が使用する object をその GPU 完了まで保持する。

```csharp
using var commands = native.MainQueue.StartCommandRecording();
commands.SetResourceDescriptorHeap(resources);
commands.SetSamplerDescriptorHeap(samplers);
commands.BeginRendering(colorAttachments, depthStencilAttachment);
commands.SetPipeline(pipeline);
commands.SetDepthStencilState(depthStencilState);
commands.Draw(rootData, 3);
commands.EndRendering();
native.MainQueue.Submit([commands], completion, completionValue);
```

draw は引数と root bytes を CPU 記録へコピーする。`Submit` がこの draw の PSO の組を解決して native command を生成し、batch 全体の成功後に提出する。scope 終了時の `Dispose` は提出済み work を取消し・待機せず、内部 command memory の回収は completion に従う。

mesh 対応 device で、`meshPipeline` は Mesh と任意の Amplification entry を持つ論理 raster pipeline、`meshRoot` はその ABI に従う値とする。前の例の rendering 内の `SetPipeline` と `Draw` は次のように指定できる。

```csharp
commands.SetPipeline(meshPipeline);
commands.DispatchMesh(meshRoot, meshGroupCount);
```

amplification entry があれば指定 group 数は amplification を起動し、なければ mesh を直接起動する。group 数を index 数として扱わない。

## 採用差分と未実装範囲

- caller-owned heap/slot、各 work の直接 root、global dependency と明示 completion を採用する。
- address-only command range、任意 shader pointer、descriptor bytes の直接編集、texture transition 不要、depth/stencil の native PSO からの完全分離は部分採用または未対応とする。
- 共通 allocation と linear region の明示分離、range の region identity、buffer descriptor、明示 texture transition／discard、単一 aspect の copy footprint、`Submit` 内の depth/stencil PSO 解決と CPU 記録の native 変換は Lumyte の補足である。NoGraphicsAPI 本体が同じ API や記録方式を持つとは説明しない。
- heap 共用は native の条件を満たす組合せに限る。Tier 1 の分類制限と CPU 可視 heap の texture 制限を取り除いたとは扱わず、別 heap に分ける場合も公開の allocation 型と確保 API は共通とする。
- mesh／amplification は任意機能として採用する。tier／limit の写像、AS／MS の PSO stream、直接／indirect dispatch、stage barrier と実 GPU 検証は未実装である。mesh 非対応 device への自動 emulation、multi-draw/count buffer と GPU 生成 root は今回の範囲に含めない。
- Native 専用の `DirectX12Backend` と公開型群に、純粋 allocation、線形 region と texture の requirement・明示配置・独立破棄を実装した。保持した Device10 から同じ `ResourceDesc1` で `GetResourceAllocationInfo2`／`CreatePlacedResource2` を呼び、texture は `Undefined` で生成する。混在配置と heap 再利用は実機確認済み。実装と試験の範囲は [進捗記録](../designs/graphics-implementation-progress.md) を参照する。
- CPU command 記録から Submit 内での一括 native 変換、線形／aspect 別 texture copy、global barrier、明示 texture transition／discard、queue と fence completion を実装した。native copy footprint の plane format を取得し、caller の row／image pitch と region 相対 offset をそのまま native copy へ変換する。
- render view／descriptor、shader／pipeline と描画の移行は未実装である。GPU 生成 root、全面的な raster/blend 分離、追加の描画機能を実装済みとは扱わない。実機試験結果と未検証の失敗経路は進捗記録に分ける。

## 参照

- [NoGraphicsAPI の比較文書](https://github.com/sebbbi/NoGraphicsAPI/blob/main/docs/no-graphics-api-comparison.md): raw range、descriptor 所有、直接 payload、global barrier と採用差分。
- [D3D12 placed resource](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12device-createplacedresource): heap と resource の分離。
- [D3D12 copy](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12graphicscommandlist-copybufferregion)・[indirect](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12graphicscommandlist-executeindirect): backing resource と offset の必要性。
- [Descriptor heap](https://learn.microsoft.com/en-us/windows/win32/direct3d12/creating-descriptor-heaps)・[SM 6.6 heap indexing](https://microsoft.github.io/DirectX-Specs/d3d/HLSL_SM_6_6_DynamicResources.html): opaque handle、slot と native heap 選択。
- [Root signature limits](https://learn.microsoft.com/en-us/windows/win32/direct3d12/root-signature-limits)・[Slang pointer 対応](https://github.com/shader-slang/slang/blob/master/docs/user-guide/03-convenience-features.md): root payload と任意 shader pointer の違い。
- [Enhanced barriers](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/enhanced-barriers)・[Graphics PSO](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/ns-d3d12-d3d12_graphics_pipeline_state_desc): texture layout と PSO に残る state。
- [個別 stencil reference](https://devblogs.microsoft.com/directx/preview-agility-sdk-1-706-3-preview-sm-6-7-enhanced-barriers-and-more/): 対応 device の front/back reference 設定。
