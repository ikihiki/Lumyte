# ADR 0014: Vulkan Native Backend 実装

## 状態

採用（目標設計）。この ADR は Native Graphics API を Vulkan に対応付ける。現行 Lumyte の実装完了は示さず、従来の descriptor set／pipeline layout 経路を互換実装として残さない。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0001: 層構造と共通契約](0001-graphics-api.md) | 共通値、座標、検証の境界と caller の責務 |
| [0002: Native Graphics API](0002-native-graphics-api.md) | device、capability/limit、初期化と失敗 |
| [0003 Native Memory](0003-native-memory-allocation-api.md)・[0004 Native Linear Data](0004-native-linear-data-api.md)・[0005 Native Texture](0005-native-texture-api.md) | heap、linear region/range、texture の配置と所有権 |
| [0006 Native View](0006-native-view-api.md)・[0007 Native Bindless](0007-native-bindless-api.md)・[0008 Native Descriptor](0008-native-descriptor-api.md) | view、caller index、descriptor storage と書込み |
| [0009 Native Shader](0009-native-shader-api.md)・[0010 Native Pipeline State](0010-native-pipeline-state-api.md) | raw program、直接 root ABI、pipeline と state |
| [0011 Native Command Recording](0011-native-command-recording-api.md)・[0012 Native 提出と同期](0012-native-command-submission-and-synchronization.md) | 記録、提出、completion と内部 memory の寿命 |

この backend は Native の shader・Resources と機能 pass 本体から利用する。入力は raw SPIR-V、Native の heap/range/texture と直接 root であり、上位の package と RenderGraph を実装上の依存にしない。共通 Graph に追加された機能 pass の Native 実装が、自身の shader と GPU 入力、内部 graph、低層 command を構築する。Portable 系統はこの backend の上層には置かない。

## 決定

[NoGraphicsAPI の Vulkan 対応](https://github.com/sebbbi/NoGraphicsAPI/blob/main/docs/vulkan-support.md) を設計の基礎とする。Vulkan 1.4 と `VK_EXT_descriptor_heap`、`VK_KHR_device_address_commands`、`VK_KHR_shader_untyped_pointers` を使用する。`VK_KHR_unified_image_layouts` は利用可能な場合に有効化する。

descriptor heap、GPU address を使う command、layout を持たない pipeline と直接 push data を組み合わせる。`VkDescriptorSetLayout`、`VkDescriptorPool`、`VkDescriptorSet`、`VkPipelineLayout` を作らず、従来の descriptor indexing/set/binding 方式へ fallback しない。必要な拡張・feature は device 初期化時に照会する。

mesh shader は `VK_EXT_mesh_shader` を用いた任意機能として採用する。初期化時に利用可能な `meshShader` を有効化して `Capabilities.MeshShaders` に写し、`taskShader` は別に `Capabilities.AmplificationShaders` へ写す。task がなくても mesh を使用でき、mesh がなくても Native の vertex/indexed path は使用できる。参照実装が持つ presentation などの全機能を、この Native interface の要件にはしない。[Mesh Shader の feature](https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceMeshShaderFeaturesEXT.html)

`Limits.MeshShader` の dispatch 上限は `VkPhysicalDeviceMeshShaderPropertiesEXT` の mesh／task ごとの各軸・総 group 数へ対応させ、出力頂点数、primitive 数と task payload 上限も反映する。local size、共有 memory、出力 memory の複合制限は shader compiler と native validation の条件とし、wrapper が独自に合法性を再検証しない。機能と固定上限は既存の capability／limit へまとめ、新しい照会 API は作らない。[Mesh Shader の properties](https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceMeshShaderPropertiesEXT.html)

## Heap と GPU range

`NativeGpuHeap` は一つの `VkDeviceMemory` を所有する純粋な allocation であり、linear data と texture の確保を `CreateGpuHeap`／`DestroyGpuHeap` に統一する。公開する値は `Size`、`Alignment`、`Kind` で、heap 自体の GPU address や CPU pointer は公開しない。

`CreateLinearRegion(size, heap, offset)` は backing `VkBuffer` を作り、caller が指定した heap の byte offset に bind する。`NativeGpuLinearRegion` は buffer と `vkGetBufferDeviceAddress` で取得した実 GPU address を所有する。`DestroyLinearRegion` は buffer を破棄し、heap を解放しない。caller は region を byte range に分割して利用し、range ごとの buffer や suballocator を backend に作らせない。

range の `Region/Offset/Size` から `Region.GpuAddress + Offset` と byte size を取り出し、address-based index/indirect/copy command へ直接渡す。`Offset` は region 相対であり、heap 内の配置位置 `Region.HeapOffset` を GPU address に再加算しない。Vulkan の記録で別の buffer を回復する処理は不要であり、region identity を持たせる理由は ADR 0004 の DirectX 12 対応である。

共通 heap は `VK_MEMORY_ALLOCATE_DEVICE_ADDRESS_BIT` を指定して確保し、linear region の buffer は device-address usage を持つ。これにより同じ allocation に image と device-address buffer を配置できる。ただし全 resource の native memory 条件が適合する必要があり、image 自体のアドレスを buffer の pointer として公開しない。[buffer の bind 条件](https://docs.vulkan.org/refpages/latest/refpages/source/vkBindBufferMemory.html)

CPU 可視の kind では対応する coherent memory を使い、heap 内部で `VkDeviceMemory` の mapping を共有する。region の `CpuAddress` は mapped memory の先頭と `HeapOffset` から得る。同じ memory を region ごとに重複 Map しない。GPU-only region は CPU pointer を公開しない。allocation 内の配置と再利用、GPU が算出する pointer の整列・範囲と寿命は caller が管理する。

`GetLinearMemoryRequirements(size, kind)` と `GetTextureMemoryRequirements(description, kind)` は native の作成条件から共通の `NativeGpuMemoryRequirements` を返す。取得時と heap 確保時で kind を合わせ、後の resource 作成も同じ native 条件に従う。`Compatibility` は `memoryTypeBits` など確保に必要な native 条件を含む opaque 値とする。

`CreateGpuHeap(size, alignment, kind, compatibilities)` は渡された各 token の native 条件を合わせ、すべての `memoryTypeBits` に含まれ指定 kind を満たす一つの `memoryTypeIndex` を選ぶ。同じ `heapIndex` や `DEVICE_LOCAL` 属性だけで互換とは判断しない。候補がなければ確保を明示的に失敗させ、自動分割や別 heap への fallback は行わない。この処理は allocation の引数構築であり、公開の Merge・分類・互換照会や、配置・使用の独自 validator にはしない。token の列は配置予約や resource の生存追跡にも使用しない。[image の bind 条件](https://docs.vulkan.org/refpages/latest/refpages/source/vkBindImageMemory.html)

requirement の `Size` は予約容量、`Alignment` は配置整列であり、native の生の値をそのまま返す保証ではない。linear/non-linear の隣接配置に対応するため、少なくとも `Alignment = max(nativeAlignment, bufferImageGranularity)` として必要な allocation 条件も含め、最終的な値で `Size = AlignUp(nativeSize, Alignment)` とする。caller がこれらの容量と整列で重ならない領域を確保すれば、境界の granularity page も共有しない。backend に隣接 resource の分類表や配置 tracker を持たせない。[Buffer-Image Granularity](https://docs.vulkan.org/spec/latest/chapters/resources.html#resources-bufferimagegranularity)

この Native 契約の通常 resource は external memory、DRM modifier、sparse binding を入力にしない。専用 allocation の推奨 `prefersDedicatedAllocation` は共用を禁止しない。必須の `requiresDedicatedAllocation` が生じる入力を将来追加する場合は対象 resource と専用 allocation の対応も設計する必要があり、現在の共通 heap に任意に配置できるとは扱わない。[dedicated allocation の条件](https://docs.vulkan.org/refpages/latest/refpages/source/VkMemoryDedicatedRequirements.html)

`RawShaderPointers` を提供し、shader が実 GPU address を typed pointer として使用できる。`BufferDescriptors` も補足として提供するが、native の pointer 経路に buffer descriptor を強制しない。

## Texture と native view

texture は linear region と同じ caller-owned `NativeGpuHeap` の指定 offset に image を bind する。heap と image は別の所有対象であり、`DestroyTexture` は heap を解放しない。`GpuOnly` でも全 format・usage の共用を保証せず、native 条件を一つの allocation で満たせない resource は caller が同じ確保 API で別 heap に配置する。format/usage や bind の合法性は native の処理と validation layer へ委ね、独自 validator は作らない。

`MutableFormat` は既定 false とし、true のときは `VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT` を指定して native で互換な追加の format view を許可する。false でも depth/stencil の aspect 解釈と sampled depth など基本用途を維持する。公開 API に許可 format の列挙は持たない。true は未対応の組を保証せず、format の互換条件と指定 view の合法性は Vulkan の作成処理と validation layer へ委ねる。

shader descriptor の生成では image と view の作成情報を descriptor 書込みへ渡し、shader 参照のための永続 `VkImageView` は作らない。sampler も作成情報から descriptor bytes を生成する。attachment の `NativeGpuRenderViewHandle` は必要な永続 image view を所有し、caller が破棄する。これらの対応は参照実装の `write_texture_descriptor`、`write_sampler_descriptor`、`create_render_view` に基づく。[実装ソース](https://github.com/sebbbi/NoGraphicsAPI/blob/main/src/NoGraphicsAPI.cpp)

sampler の `MinLod/MaxLod/MaxAnisotropy` は要求値を native の sampler 作成情報へ渡し、参照プロトタイプの固定値へ丸めない。render view は read-only flags を保持し、attachment の aspect 別 nullable operation とともに rendering へ伝える。

通常の image は sampling、storage、attachment、copy に `GENERAL` を使う。公開値の `General` と `Common` は discard の到達先としてともに GENERAL に写し、queue 間の新しい layout は追加しない。`unified_image_layouts` はこの単一 layout の最適化に使うもので、global barrier を自動化する機能ではない。

新しい image の `UNDEFINED → GENERAL` を初回初期化として使用前に順序付ける。初期化記録を未提出で中止した場合は完了扱いにせず、後の使用に必要な初期化を残す。この局所状態を、resource の使用履歴から現在 layout を推論する汎用 tracker に拡張しない。通常利用の `TextureTransition` は必要としない。

通常の texture を `GENERAL` で使用することは固定した生成契約とし、初期 state の照会や property は設けない。初期内容は不定であり、caller が内容を初期化して、その後の使用間に必要な global dependency を明示する。

別の解釈を持つ image または buffer が同じ memory に書き込んだ後は、既存 image も影響する subresource の再初期化を必要とする。caller の `DiscardTexture(view, General)` を、指定 aspect・mip・layer の `VkImageMemoryBarrier2` に変換し、`oldLayout = UNDEFINED`、`newLayout = GENERAL` を記録する。subresource 全体の旧内容を失う操作であり、alias 間で data が保存されるという意味ではない。`unified_image_layouts` もこの再初期化を不要にはしない。[Vulkan Memory Aliasing](https://docs.vulkan.org/spec/latest/chapters/resources.html#resources-memory-aliasing)

discard の source access は旧 image 内容を保存しないため空とし、前後の stage は `ALL_COMMANDS`、destination access は以後の memory read/write を覆う。先行 alias の write availability は caller が別途記録する global dependency または queue wait が担う。native の barrier 順序を維持し、alias の write flush を discard の source access で省略したことにしない。選択範囲が depth/stencil の双方を含む必要がある場合は caller が両 aspect を渡す。backend は heap の重複領域や現在の alias を追跡せず、明示された再初期化を初回初期化フラグで省略しない。未提出または提出失敗の記録を破棄した場合、caller は次の記録で必要な discard を再度指定する。

read-only aspect は内容を load し、store には `VK_ATTACHMENT_STORE_OP_NONE` を使う。caller は当該 aspect に書かない depth/stencil state を使用する。`GENERAL` 自体が読み取り専用を意味するわけではなく、flags を捨てて writable attachment として扱わない。[Vulkan の attachment store 操作](https://docs.vulkan.org/refpages/latest/refpages/source/VkAttachmentStoreOp.html)

## Descriptor heap

`NativeGpuDescriptorHeap` を `VK_EXT_descriptor_heap` の resource heap または sampler heap の専用 storage に対応付ける。caller が容量と slot 使用を管理し、command で heap 全体を設定する。heap は空き slot を選択する allocator を持たず、device 共通の型別 profile や使用 resource の集合も持たない。

DirectX 12 の opaque descriptor heap と共通の契約を保つため、descriptor storage は専用の生成・破棄 API を持つ。linear region と texture の allocation 統一によって、descriptor heap を caller の `NativeGpuHeap` に配置する契約は加えない。

texture/sampler の書込みは `vkWriteResourceDescriptorsEXT`／`vkWriteSamplerDescriptorsEXT`、heap 設定は `vkCmdBindResourceHeapEXT`／`vkCmdBindSamplerHeapEXT` に対応する。descriptor bytes、native alignment と reserved range を満たす backing storage は heap が所有する。resource と sampler の index 空間は分かれる。

storage は descriptor-heap usage と device address を持つ buffer、HOST_VISIBLE／HOST_COHERENT memory と mapping を所有する。heap の先頭整列の余裕を backing に加え、caller の slot 範囲の後に native が要求する reserved range を置く。`samplerAnisotropy` と `imageCubeArray` は device が提供する場合だけ有効化し、新しい必須 feature にはしない。anisotropy の指定値は float のまま保持する。

Lumyte の `WriteBufferDescriptor` は、`NativeGpuRange` の実 address と size を `VkResourceDescriptorInfoEXT.data.pAddressRange` へ渡す補足である。storage buffer descriptor を native heap に直接書き、address lookup table や root の追加領域は作らない。これは NoGraphicsAPI の public API にある機能ではなく、Vulkan の descriptor-heap 仕様が提供する buffer descriptor を使う選択である。[Vulkan の resource descriptor 仕様](https://docs.vulkan.org/refpages/latest/refpages/source/VkResourceDescriptorInfoEXT.html)

Vulkan の `ReadOnly`／`ReadWrite` は同じ storage buffer descriptor を使い、読み書きの可否は SPIR-V 側の宣言で表す。DirectX 12 の SRV／UAV の違いを別種の Vulkan descriptor や lookup table へ模倣しない。

resource heap は texture と buffer の両 descriptor の native size/alignment を満たす固定 slot stride を使う。sampler heap は別の stride を使う。各 descriptor の native size と slot stride は別の値であり、write 側と shader 側が利用 slot 領域の先頭から `byteOffset = index * resourceSlotStride` で同じ位置を求める。slot の種別によって index の意味を変えない。この配置契約は ADR 0008 に従い、raw shader と descriptor storage が同じ Native ABI を使う。追加の root/address 表は設けない。

SPIR-V の `ArrayStrideIdEXT` は配列の stride を指定し、`OpConstantSizeOfEXT` は型ごとの descriptor size を返す。既存 Slang の `ResourceDescriptorHeap[T][i]` が自動的に全型共通の slot stride を使うとは仮定しない。Native caller はこの byte offset 計算と一致する raw SPIR-V を用意し、toolchain が必要な場合は同じ計算へ lowering する。これは Lumyte の混在 heap ABI への対応であり、参照実装の全 shader がそのまま適合するという主張ではない。[SPV_EXT_descriptor_heap](https://github.khronos.org/SPIRV-Registry/extensions/EXT/SPV_EXT_descriptor_heap.html)

Slang 2026.17 の `-spirv-unified-descriptor-heap-stride` を使い、型ごとの descriptor size から共通 stride を求める artifact を生成・検証した。device の size を固定値として埋め込まない。native の descriptor size と alignment は2の冪で、alignment は対応 size 以下のため、image と buffer の最大 size が両方の整列を満たす。storage と shader が同じ stride を使うことを、混在 slot を読む compute の実機試験で確認した。size／alignment と slot stride は `Limits.Descriptors` で公開する。[descriptor heap properties](https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceDescriptorHeapPropertiesEXT.html)

descriptor が指す resource を自動保持しない。slot の上書き・再利用や heap の破棄は caller が未提出参照と GPU 利用を解消してから行う。native validation が診断する shader 型や descriptor の条件を独自に再検証しない。

descriptor の書込みと heap の選択は texture の初期化義務を消費しない。shader から初めて参照する texture は caller が事前に明示 discard で `GENERAL` へ初期化するか、先行する texture copy で初期化する。descriptor slot の参照先 registry や、全 texture を巡回する未初期化リストは置かない。recording の native command 区間が切り替わる際は、選択済み resource／sampler heap を次の区間へ再設定する。この2個の選択値は resource 到達先の追跡ではない。

## SPIR-V と直接 root data

`NativeGpuShaderProgram` は raw SPIR-V と entry point を受け取る。shader package の選択・reflection・profile 比較は行わない。heap の配置、typed pointer と root の byte 配置を一致させるのは caller と shader compiler の責務である。

pipeline は `VK_PIPELINE_CREATE_2_DESCRIPTOR_HEAP_BIT_EXT` と null pipeline layout で生成する。Vulkan は各 draw/dispatch の記録呼出し時に caller の root bytes をコピーし、その場で `vkCmdPushDataEXT` に直接記録する。`vkCmdPushConstants` は使わない。native 命令を生成する時点は backend の実装差であり、共通契約は caller の byte 領域を呼出し後に参照しないことと、native root へ直接渡すことである。`MaxRootDataSize` は native の `maxPushDataSize` から得る。入力は4 byte の倍数で上限以下とし、その native 条件は独自に再検証しない。空入力では push data を記録しない。[直接 push data](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdPushDataEXT.html)

compute pipeline は raw SPIR-V と指定 entry から同期生成する。byte slice の開始位置が uint32 境界にない場合も aligned な作業領域から native shader module を作り、生成後に module を解放する。`DispatchIndirect` は `vkCmdDispatchIndirect2KHR` に実 GPU address と range を渡し、先頭3個の uint32 で1件を実行する。texture 初期化のため native command 区間が変わる際は、選択済み compute pipeline と descriptor heaps を次の区間にも再設定する。

NoGraphicsAPI の Slang では `[[vk::push_constant]]` の宣言からこの push-data 経路を使う。宣言の名前を理由に `VkPipelineLayout` と従来の push-constant range が必要だとは解釈しない。shared POD の C layout、row-major、descriptor-heap capability と SPIR-V の事前検証は shader toolchain の契約である。[Slang shader 契約](https://github.com/sebbbi/NoGraphicsAPI/blob/main/docs/slang.md)

draw の開始位置と符号付き base vertex は native 引数へそのまま渡す。試験用 Slang shader は `SV_VulkanVertexID`／`SV_VulkanInstanceID` を使い、開始位置を含む SPIR-V の `VertexIndex`／`InstanceIndex` を直接取得する。Slang の通常の `SV_VertexID`／`SV_InstanceID` はそれぞれ base 値を差し引く変換となるため、shader を移植する際は選択した semantic を ABI の一部として扱う。command は開始位置を root に追加せず、SPIR-V の書換えも行わない。[Slang の system-value 対応](https://shader-slang.org/slang/user-guide/spirv-target-specific#using-sv_instanceid-and-sv_vertexid-with-spir-v-target)

root には GPU pointer、descriptor index と値を直接含められる。64 byte 固定や末尾 zero fill を Vulkan Native 層へ課さず、buffer fallback、Parameter Data の生成・upload・保持も行わない。CPU root 値は呼出し後に破棄できるが、参照先の heap/resource は caller が GPU 完了まで保持する。

mesh／amplification も同じ descriptor-heap と push-data ABI を使う。公開 stage の `Mesh` を `VK_SHADER_STAGE_MESH_BIT_EXT`、`Amplification` を `VK_SHADER_STAGE_TASK_BIT_EXT` に写し、shader の execution model も `MeshEXT`／`TaskEXT` とする。task から mesh に渡す shader 内 payload は root data と別の GPU 内通信であり、command が payload buffer を生成・upload する契約にはしない。[descriptor heap を使う mesh draw](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdDrawMeshTasksEXT.html)

## PSO と command state

raw SPIR-V から raster/compute pipeline を作成時に生成する。raster pipeline は attachment format、rasterization と blend を固定 state に含め、viewport/scissor と公開 depth/stencil は Vulkan の dynamic state で設定する。depth/stencil の組ごとの PSO variant は作らない。

mesh を選んだ raster pipeline は mesh、任意の task、任意の fragment stage を含む graphics pipeline とする。vertex/input assembly を混在させず、従来の vertex pipeline と同じ `CreateRasterPipeline`／`SetPipeline` を使う。`MeshOutputTopology` は shader が宣言する出力 primitive を caller が示す共通 metadata であり、Vulkan では shader の execution mode が topology を決める。wrapper は SPIR-V reflection で output 宣言を推定・書換えしない。line／triangle の Native 契約に従い、Vulkan 固有の mesh 機能を一律の Native 要件にしない。

`Submit` 内で depth/stencil の組に応じた追加 PSO を生成する必要はない。作成済み pipeline を記録呼出し時に設定し、native command へ直接変換する。draw 中の初回 pipeline 生成や global cache、LRU を導入しない。

rendering は dynamic rendering を使い、開始時に attachment 領域の viewport/scissor と無効の depth/stencil を設定する。root は各 work の引数なので、前の work の root state を引き継ぐ操作は提供しない。rendering 境界で必要な memory hazard が解消されたとは扱わない。

vertex raster pipeline は空の vertex input と caller の topology を使い、vertex data は shader が取得する。depth test/write/compare、stencil test/operation/mask/reference は dynamic state とし、front/back を個別に設定する。viewport は `y + height` と負の height を native に渡し、clip の Y 上向きと framebuffer の Y 下向きを一度だけ対応させる。shader 側で Y を再反転しない。

read-only aspect の未指定 operation は native の Load／StoreOp.None に写し、clear や store write を行わない。存在しない aspect は `VkRenderingInfo` に attachment pointer を渡さない。`GENERAL` のまま使い、depth/stencil state が read-only aspect を書かないことは caller が保証する。参照先の layout を追跡して writable な operation へ補完しない。

すべての attachment を rendering の開始前に初回参照処理へ渡し、native command 区間の切替と初期化を rendering の途中へ挿入しない。区間が変われば選択済み raster／compute pipeline と descriptor heaps を再設定する。`DrawIndexed` は `vkCmdBindIndexBuffer3KHR` に実 address、range size と index format を渡す。draw の間接版は `vkCmdDrawIndirect2KHR`／`vkCmdDrawIndexedIndirect2KHR` で1件を実行し、root は引き続き直接 push data にする。

raw shader と draw に使う optional feature は、device が提供する `shaderDrawParameters`、`drawIndirectFirstInstance`、`independentBlend`、vertex／fragment の storage 書込みを有効化する。未提供の feature を要求する shader や state は native の作成・診断に従う。shader reflection による機能推定や不足機能の emulation は行わない。

rendering 内の `DispatchMesh(rootData, x, y, z)` は root を直接記録した後に `vkCmdDrawMeshTasksEXT` を呼ぶ。task stage があればその group 数、なければ mesh の group 数となる。`DispatchMeshIndirect(rootData, arguments)` は既定の `VK_KHR_device_address_commands` と mesh 拡張の連携命令 `vkCmdDrawMeshTasksIndirect2EXT` を使う。`VkDrawIndirect2InfoKHR` に `arguments.GpuAddress`、range の size、12 byte の stride と `drawCount = 1` を渡し、GPU 上の X／Y／Z から 1 件だけ起動する。root は CPU 呼出しで渡したものを用い、count buffer、GPU 生成 root、CPU readback による展開は加えない。[address による mesh indirect](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdDrawMeshTasksIndirect2EXT.html)

## Queue family と device timeline

`MainQueue` は graphics／compute／transfer を持つ queue、`CopyQueue` はそれと異なる queue とする。transfer 専用 family を優先し、なければ同 family の2番目の queue、最後に別の対応 family を選ぶ。専用転送がない場合は同 family を使い、CONCURRENT が不要な経路を優先する。独立した queue が得られない場合は null を返す。各 queue に対応する command pool と内部 completion を持たせ、同じ queue の host 操作は caller が直列化する。queue の存在から、物理 engine の重なりや性能向上を保証しない。

Main／Copy の family が異なる場合、公開 linear region の buffer と texture の image は、その2 family を列挙した `VK_SHARING_MODE_CONCURRENT` で生成する。同 family、または CopyQueue がなければ EXCLUSIVE とする。memory requirements の取得と実際の生成に同じ設定を使う。descriptor storage は MainQueue 専用の EXCLUSIVE のままとする。固定した device-wide な共有契約であり、resource ごとの所有者追跡、release／acquire の推定や公開 sharing flags は追加しない。CONCURRENT は native の配置・性能へ影響し得るため、EXCLUSIVE と同等の性能を保証しない。[Resource sharing](https://docs.vulkan.org/spec/latest/chapters/resources.html#resources-sharing)

CopyQueue の共通用途は線形データと color texture の転送とし、depth／stencil は graphics を持つ MainQueue を使う。転送の alignment、granularity と用途条件は native validation に委ねる。[vkCmdCopyMemoryToImageKHR](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdCopyMemoryToImageKHR.html)

caller semaphore は device に属する timeline semaphore とする。`IsComplete` は `vkGetSemaphoreCounterValue`、`WaitCpu` は `vkWaitSemaphores`、`SignalCpu` は `vkSignalSemaphore` に写す。CPU 操作は queue の回収 list を変更せず、Submit や host signal と並行できる。全 queue と semaphore が同じ device loss を参照し、一方の queue の停止を他方で通常の未完了と扱わない。[Timeline semaphores](https://docs.vulkan.org/spec/latest/chapters/synchronization.html#synchronization-semaphores)

## Global barrier と提出

`Barrier` は stage/access から一つの global `VkMemoryBarrier2` を作り、`vkCmdPipelineBarrier2` で記録する。通常の texture/buffer を列挙せず、per-resource transition list を求めない。caller が producer/consumer の実際の依存を指定する。

`GpuStage.Host` と `GpuAccess.HostRead/Write` は `HOST` stage と host access bit に対応する。readback の前には caller が producer から `HostRead` への dependency を記録し、提出後に completion を CPU で待つ。coherent mapping は host cache の明示 invalidate を省くが、device から host への memory domain operation と完了待機は省かない。backend が毎回の Submit に readback barrier を補うことはしない。[Vulkan の CPU readback 例](https://docs.vulkan.org/guide/latest/synchronization_examples.html#_cpu_read_back_of_data_written_by_a_compute_shader)

`DescriptorRead` は `VK_ACCESS_2_RESOURCE_HEAP_READ_BIT_EXT` と `VK_ACCESS_2_SAMPLER_HEAP_READ_BIT_EXT` に写す。従来の descriptor-buffer 用 access bit を代用しない。

`GpuStage.AmplificationShader`／`MeshShader` はそれぞれ `VK_PIPELINE_STAGE_2_TASK_SHADER_BIT_EXT`／`MESH_SHADER_BIT_EXT` に写す。meshlet data の shader read と indirect 引数の `DRAW_INDIRECT`／`INDIRECT_COMMAND_READ` は別の依存先であり、caller が実際の consumer を指定する。mesh を compute stage にまとめず、従来の shader-stage 定数 `VK_SHADER_STAGE_ALL_GRAPHICS` に mesh／task が含まれるとも仮定しない。[Mesh Shader の stage と同期](https://docs.vulkan.org/features/latest/features/proposals/VK_EXT_mesh_shader.html)

texture copy は `NativeGpuTextureCopyFootprint.Aspect` を `VkDeviceMemoryImageCopyKHR.imageSubresource.aspectMask` の単一 `COLOR`／`DEPTH`／`STENCIL` bit に写す。mip と layer 範囲を同じ `imageSubresource` に渡し、layout は `GENERAL` とする。depth と stencil は別々の線形 plane として転送する。16-bit depth は `D16_UNORM`、24-bit depth は `X8_D24_UNORM_PACK32`、32-bit depth は `D32_SFLOAT`、stencil は `S8_UINT` の element 配置を使う。[Vulkan の aspect 別 copy](https://docs.vulkan.org/spec/latest/chapters/copies.html#copies-buffers-images)

byte 単位の `RowPitch`／`ImagePitch` は選択 aspect の element／block サイズから texel 単位の `addressRowLength` と `addressImageHeight` に変換する。copy の address は range の先頭であり、texture 内の opaque な offset から生成しない。整数変換で表現できない入力は失敗とし、staging や pitch 補正で救済しない。native の整列・範囲・format 条件は validation に委ねる。[VkDeviceMemoryImageCopyKHR](https://docs.vulkan.org/refpages/latest/refpages/source/VkDeviceMemoryImageCopyKHR.html)

native command buffer は記録呼出し時に一回提出用の native command を生成し、queue が `Submit` 内で batch 全体の記録を終了してから受理する。終了に失敗した batch は GPU へ提出せず、caller が `Dispose` して新しく記録する。記録・終了・受理の区別は内部管理とし、command 状態を公開しない。timeline semaphore で caller の completion 値を表す。未提出 command の `Dispose` は記録を破棄し、提出済み command の `Dispose` は GPU work の取消しや待機を行わず、内部 command memory を必要な完了まで保持してから回収する。caller-owned heap、linear region、descriptor、texture と pipeline の寿命を backend が引き受けない。

新規 texture の初回参照位置では native command の区間を分け、未初期化候補を非所有参照で記録する。Submit が区間を順に走査し、まだ必要な image の `UNDEFINED → GENERAL` command だけを参照の直前に挿入する。初期化は caller の先行 barrier を追い越さず、先に複数の recording を組み立てても同じ image を重複初期化しない。明示的な `DiscardTexture` はこの初回処理とは別に毎回記録する。通常 layout の履歴や subresource ごとの状態 tracker は追加しない。

全区間の終了と回収用の管理領域の確保を終え、一回の `vkQueueSubmit2` が成功した後だけ初回初期化義務を消費する。確実に未提出の失敗と未提出 Dispose では義務を残す。この一回の呼出しに、指定された GPU waits・command・内部 timeline signal を含む `VkSubmitInfo2`、その後に caller timeline を signal する `VkSubmitInfo2` を渡す。wait／signal の stage は `ALL_COMMANDS` とし、wait は work 全体より前に働く。同じ native batch の複数 signal は互いに順序を保証しないため、分けることで caller の WaitCpu が内部 signal の終了も保証する。空 command 列も同じ同期経路で受理する。command memory の回収は各 queue の内部 timeline のみに依存し、追加の提出呼出しや暗黙待機は行わない。[Vulkan の signal 順序](https://docs.vulkan.org/spec/latest/chapters/synchronization.html#synchronization-signal-operation-order)

`vkQueueSubmit2` の `OUT_OF_HOST_MEMORY`／`OUT_OF_DEVICE_MEMORY` は resource と同期状態の不変が仕様で保証されるため、当該 batch を未提出として破棄できる。`DEVICE_LOST` や結果が不明な失敗にはこの保証がなく、`NativeGpuSubmissionException` に要求した `Completion` と元の例外を保持する。recording と初期化用 command pool は queue 側に残し、後続の大きい timeline 値だけで不明な batch を回収しない。device の継続利用を止め、初期化義務を根拠に同じ image を再利用しない。native の failure を検証前に推定する独自 validator は追加しない。[vkQueueSubmit2 の失敗保証](https://docs.vulkan.org/refpages/latest/refpages/source/vkQueueSubmit2.html)

別 queue へ新規 texture を渡すときは、初回初期化する producer の Submit を先に受理させ、その後に producer の timeline を待つ consumer を Submit する。初回初期化義務は host 側の受理時に消費するため、consumer を未来値の待機付きで先に提出する順序は初回 texture 利用では許さない。初期化済み resource／線形データの wait-before-signal は、caller が進行と signal 順序を保証して使える。GPU wait を CPU wait に置換したり、resource 参照から queue の順序を自動生成したりしない。

queue 受理と GPU completion を区別し、native の失敗を ADR 0002 の error/device loss に接続する。参照プロトタイプの `assert` やプロセス終了の方針を、そのまま Lumyte の失敗 API に置き換えない。未提出中止と受理後の停止も区別する。

## API

署名と Native 型は依存表の責務別 ADR を正とし、ここでは Vulkan 固有の対応を示す。

| API | Vulkan での対応 |
| --- | --- |
| `Capabilities`／`Limits` | native device の機能と上限を返す。`RawShaderPointers` と `BufferDescriptors` を提供し、通常 image の `ExplicitTextureTransitions` は不要。mesh と amplification は `meshShader`／`taskShader` に従って別々に返す。 |
| `GetLinearMemoryRequirements`／`GetTextureMemoryRequirements` | 指定 kind と native resource 条件から、granularity を含む予約容量・整列・opaque compatibility を返す。 |
| `CreateGpuHeap`／`DestroyGpuHeap` | compatibility の列と kind を満たす一つの memory type を選び、指定容量の `VkDeviceMemory` を確保・解放する。 |
| `CreateLinearRegion`／`DestroyLinearRegion`／`NativeGpuRange.GpuAddress` | 指定 heap offset に backing buffer を bind・破棄する。region の実 address と range の相対 offset から GPU address を導出し、対応 kind では内部 mapping から CPU pointer を提供する。 |
| `CreateTexture`／`DestroyTexture` | 指定 heap への image bind と image の解放。`MutableFormat` は mutable-format image に対応する。使用前の `GENERAL` 初期化を順序付け、heap を同時に解放しない。 |
| `CreateRenderView`／`DestroyRenderView` | read-only flags を保持する attachment 用 view と永続 image view の生成・破棄。 |
| `CreateDescriptorHeap`／`DestroyDescriptorHeap` | native resource/sampler 専用 storage と必要な reserved range の管理。texture/buffer を混在させる resource heap は固定 slot stride を使い、slot の貸出・返却は行わない。 |
| `WriteTextureDescriptor`／`WriteSamplerDescriptor` | view/sampler 作成情報から descriptor bytes を書く。LOD 範囲と数値 anisotropy を保ち、shader 用の永続 view/sampler object は増やさない。 |
| `WriteBufferDescriptor` | range の address/size から storage buffer descriptor を書く Lumyte の補足。 |
| `SetResourceDescriptorHeap`／`SetSamplerDescriptorHeap` | native heap 全体の設定。 |
| `CreateRasterPipeline`／`CreateComputePipeline` と破棄 API | 作成時に null-layout pipeline の native 生成を完了する。depth/stencil の組による追加生成はない。 |
| `SetPipeline`／`SetComputePipeline`／`SetDepthStencilState`／`SetViewport`／`SetScissor` | native pipeline と dynamic state の記録。 |
| `BeginRendering`／`EndRendering` | attachment view と read-only/nullable load/store/clear の指定を dynamic rendering の内容保持または書込み操作へ変換する。 |
| `Draw`／`DrawIndexed`／`DrawIndirect`／`DrawIndexedIndirect` | 各呼出しの root を push data とし、indexed/indirect は device-address command を使う。 |
| `DispatchMesh`／`DispatchMeshIndirect` | 同じ push data と graphics pipeline を使い、`vkCmdDrawMeshTasksEXT`／address-range による `vkCmdDrawMeshTasksIndirect2EXT` へ写す。indirect は 1 件である。 |
| `Dispatch`／`DispatchIndirect` | 各呼出しの root と直接 group count または device-address の引数を使う。 |
| `CopyMemory`／`CopyMemoryToTexture`／`CopyTextureToMemory` | `VK_KHR_device_address_commands` の copy に address/range と単一 aspect の footprint を渡す。 |
| `Barrier`／`TextureTransition`／`DiscardTexture` | global execution/memory barrier と明示された `UNDEFINED → GENERAL` の再初期化を記録する。通常 image の用途ごとの `TextureTransition` は提供しない。 |
| `MainQueue`／`CopyQueue`／`StartCommandRecording`／`NativeGpuCommandBuffer.Dispose` | 別 queue とその family の command pool を使う。未提出の破棄と提出済み memory の内部 completion 後の回収を区別する。 |
| `Submit(commands, signal, waits)`／`NativeGpuTimelinePoint` | batch 全体の終了後、GPU waits → work・内部 signal → caller signal を一回の vkQueueSubmit2 に渡す。空 batch も同期として受理する。 |
| `CreateSemaphore`／`NativeGpuSemaphore.IsComplete`／`WaitCpu`／`SignalCpu`／`Dispose` | device に属する caller-owned timeline の生成、完了照会、CPU wait／signal と解放。queue の回収から独立する。 |

## コード配置

以下は repository root 相対の目標配置とする。既存の `Lumyte.Graphics.Vulkan` と隣の `Lumyte.Graphics.Vulkan.Tests` を Native 専用へ改編し、公開契約は作成済みの `Lumyte.Graphics.Native` を参照する。フォルダ分割は同一 backend project 内で行う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Vulkan/Interop/` | Vulkan の native 定義と呼出し、instance／device の dispatch、必要な拡張 entry point。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Device/` | instance・physical device・device と queue の初期化、feature 選択、validation/debug utils と error/device loss。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Memory/`、`src/graphics/Lumyte.Graphics.Vulkan/LinearData/`、`src/graphics/Lumyte.Graphics.Vulkan/Textures/` | memory type・granularity・共有 mapping、明示配置の buffer/address、image と初回 `GENERAL` 初期化の局所状態。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Views/`、`src/graphics/Lumyte.Graphics.Vulkan/Bindless/`、`src/graphics/Lumyte.Graphics.Vulkan/Descriptors/` | attachment 用 image view、index ABI、専用 descriptor storage・固定 slot stride と native 書込み。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Shaders/`、`src/graphics/Lumyte.Graphics.Vulkan/Pipelines/` | raw SPIR-V と push-data ABI、vertex／mesh／task の null-layout pipeline と dynamic state の native 対応。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Commands/`、`src/graphics/Lumyte.Graphics.Vulkan/Submission/`、`src/graphics/Lumyte.Graphics.Vulkan/Synchronization/` | mesh dispatch／indirect を含む呼出し時の native 記録と address command、batch の終了・受理、global barrier と timeline semaphore を分担する。 |
| `src/graphics/Lumyte.Graphics.Vulkan.Tests/` | xUnit の unit test を上記の担当フォルダに対応させて配置する。native 呼出しを差し替え、変換・失敗境界・backend-owned object の回収を確認する。 |
| `src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/` | 担当別サブフォルダに、必要な Vulkan feature と validation layer を使う実 GPU 試験を配置する。 |

公開 Native 型を backend 内に複製せず、shader の ABI lowering は offline toolchain が担当する。Portable、上位 Resources、shader package、RenderGraph と Generic Host 登録への逆依存、および従来の descriptor set／pipeline layout への互換経路は作らない。

## 使用例

`native` は Vulkan の `INativeGpuBackend` とする。`pipeline`、`rootData`、`nextRootData` は同じ native shader ABI に従い、root 内の実 GPU pointer だけで data を参照し、descriptor heap は使わない。使用する allocation は caller が保持する。

```csharp
using var commands = native.MainQueue.StartCommandRecording();
commands.SetComputePipeline(pipeline);
commands.Dispatch(rootData, 1);
commands.Barrier(
    GpuStage.ComputeShader, GpuAccess.ShaderWrite,
    GpuStage.ComputeShader, GpuAccess.ShaderRead);
commands.Dispatch(nextRootData, 1);
```

各 dispatch の root を `vkCmdPushDataEXT` にコピーし、その間に global memory dependency を記録する。例は提出しないため、scope 終了時の `Dispose` で記録だけを破棄する。

mesh 対応 device では、`meshPipeline` に Mesh と任意の Amplification entry を指定する。`meshArguments` は GPU が用意した 1 件の X／Y／Z を持つ range とし、その producer から indirect 読み取りと mesh data 読み取りへの依存は記録済みとする。

```csharp
using var meshCommands = native.MainQueue.StartCommandRecording();
meshCommands.BeginRendering(colorAttachments, depthStencilAttachment);
meshCommands.SetPipeline(meshPipeline);
meshCommands.DispatchMeshIndirect(meshRoot, meshArguments);
meshCommands.EndRendering();
```

`meshRoot` の pointer が指す data と indirect range の所有・寿命は caller が管理する。この例も scope 終了時に未提出の記録を破棄する。

## 検証方針

native の条件は Vulkan validation layer と Vulkan の返す結果で診断する。`EnableValidation` で利用可能な Khronos validation と debug utils を有効化する。host memory の安全と自前の object 状態を除き、format/usage/descriptor/shader/layout の独自 validator は作らない。

Lumyte の検証は address/range と aspect 別 footprint の変換、descriptor bytes の書込み先と shader の共通 slot stride、直接 root の byte コピー、global barrier／明示 discard の写像、一回提出と内部 object の回収に絞る。shader の合法性は compiler と SPIR-V validator に委ねる。

実 GPU では同じ領域の image A → 別 alias B の書込み → A の明示 discard と再初期化、および depth／stencil の独立した upload／readback を確認する。copy しなかった aspect の内容を保持することと、未提出 `Dispose`／提出失敗の後に必要な再初期化を省略しないことを個別に確認する。

mesh は task なし／ありの graphics pipeline と描画、compute が書いた address-range 引数からの indirect 描画、直接 root と task／mesh／indirect の stage 変換を検証する。task のない device で mesh 単体を利用できること、および mesh 非対応 device で vertex/indexed path を維持することも機能選択の試験対象とする。非対応 GPU への emulation や native shader validation の再実装は行わない。

## 採用差分と未実装範囲

- NoGraphicsAPI の descriptor heap、null-layout pipeline、直接 push data、GPU pointer、global barrier と単一 image layout を採用する。
- 独立した CopyQueue、異 family 間の固定 CONCURRENT sharing、device timeline の GPU wait／CPU 操作を実装した。複数 queue を caller が選択する API は NoGraphicsAPI prototype への拡張であり、queue owner の追跡や application resource の自動退役は含めない。
- 共通 allocation と linear region の明示分離、`NativeGpuRange` の region identity、複数 requirement の opaque compatibility を受け取る確保、buffer descriptor、sampler の LOD/anisotropy 指定、read-only attachment、単一 aspect の copy footprint、alias 再利用の明示 discard と Lumyte の例外・`Dispose` 契約は、本 API の追加または差分である。NoGraphicsAPI の public API に同じ機能があるとは扱わない。
- heap の共用は native memory 条件が適合する範囲に限り、GPU-only memory の全用途共通化や image の線形 pointer 化は保証しない。Native 専用の `VulkanBackend` に共通 allocation、線形 region の配置と mapping、texture の明示配置・独立破棄、granularity を含む requirements を実装した。必須拡張を満たす device で初期化、共有 mapping、texture との混在配置と heap 再利用を実機確認済み。validation layer を使う検証は未実施である。
- texture の要件取得と生成は同じ `ImageCreateInfo` の変換を使う。optimal image を `UNDEFINED` で生成・bind し、初回の `GENERAL` 初期化義務を非公開状態に保持する。command／submit には参照直前の区間で接続し、生成時の暗黙提出・待機は行わない。
- mesh／task は任意機能として採用し、feature／limit の写像、mesh／task pipeline、直接／address-range indirect dispatch と stage barrier を実装した。拡張のない device の初期化には追加要求せず、task だけがない場合も mesh 単体を利用できる。point 出力、mesh 固有の multiview／query など拡張全体の一括採用はしない。
- PSO の rasterization/blend の全面分離、GPU が生成・選択する root は未採用。ray tracing、multi-draw/count buffer と presentation API はこの最小 Native interface の範囲外である。
- 参照実装の swapchain には `GENERAL ↔ PRESENT_SRC_KHR` の内部 transition があるが、本 ADR は presentation API の実装完了を宣言しない。
- address-command の線形／texture copy、global barrier、HostRead、明示 discard、queue の一回提出と timeline completion を実装した。転送先 range は source の byte 数だけに制限し、公開した余剰容量を変更しない。内部 command pool の回収は caller semaphore の寿命から分離する。
- render view の永続 image view、専用 descriptor storage と固定 slot stride、view／address range／sampler 作成情報からの descriptor 書込み、resource／sampler heap の設定を実装した。さらに null-layout compute pipeline、直接 push data、直接／address-range 間接 dispatch、混在 slot stride と shader lowering を実機確認し、`RawShaderPointers` と `BufferDescriptors` を true とする。
- render view の attachment 使用、vertex／mesh raster pipeline、直接／address-range 間接 draw・indexed draw・mesh dispatch を実装した。depth/stencil は dynamic state を使い、read-only aspect は LOAD／STORE_OP_NONE で保持する。全 attachment の初期化区間を `vkCmdBeginRendering` の前に置き、pipeline／heap を再設定する。製品用 shader toolchain の移行は未実装である。実機試験結果と未検証の失敗経路は [進捗記録](../designs/graphics-implementation-progress.md) を参照する。
