# Native Vulkan asynchronous copy fixtures

非同期 copy 試験は既存の [compute](README.md) と [raster](RASTER.md) fixtures を使う。
新しい compiler、shader ABI、root buffer は追加しない。

## Queue と allocation

`CopyQueue` は Main とは異なる native queue である。専用 transfer family を優先し、
なければ Main family の別 index、または別の transfer 対応 family を使う。独立した
queue を取得できなければ null を返し、Main の別名を返さない。

Main／Copy の family が異なる場合、公開 linear region の backing buffer と texture は
その2 family間の `CONCURRENT` sharingで作成する。同familyなら `EXCLUSIVE` のままとする。
requirements取得とresource作成は同じ生成処理を使う。callerは GPU依存を指定し、
backendはqueue ownershipを追跡したり暗黙waitを挿入したりしない。
descriptor heapはMain側のshaderが使用し、copy queueへ移さない。

専用transfer familyのimage転送にはnative制約がある。memoryからimageへのdepth／stencil
copyはgraphics対応queueが必要である。またaddressの整列とfamilyの
`minImageTransferGranularity` を満たす必要がある。試験はcolor imageの全subresourceを
転送し、depth／stencilの既存試験はMainで実行する。native validationと重複する
validatorは追加しない。[nativeのcopy条件](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdCopyMemoryToImageKHR.html)

`CONCURRENT` の選択や独立したqueueの存在だけで、専用DMA engineの使用、高速化、
graphicsとの実処理の重なりを保証するものではない。

## Timeline と初期化順序

timelineはdeviceが生成し、GPUのsignal／waitとCPUの照会／待機／明示signalに使う。
CPU操作はqueueの回収リストを触らず、queue Submitと並行して実行できる。
各SubmitはGPU waitを含むworkと内部completion signal、その後にcaller signalだけの
native batchを一度の `vkQueueSubmit2` で提出する。GPU waitは `ALL_COMMANDS` を対象とする。

textureの最初のGENERAL初期化を担当するproducer Submitを先に受理させ、その後で
consumerをGPU wait付きでSubmitする。consumerのrecordingを先に組み立てることはできる。
未初期化imageのconsumerを先にSubmitして未来のproducer signalを待つ順序は使わない。
初期化済みresourceやresourceを持たない依存では、forward progressを保証した
wait-before-signalを利用できる。`Common`はVulkanではGENERALに対応する。

timeline、upload memory、descriptor、textureの破棄と再利用はcallerが決める。
timelineはそのsignalだけでなく、それを参照するconsumerのGPU waitが完了するまで保持する。
内部command poolの回収はqueue所有timelineを使い、caller timelineの破棄に依存しない。

## 検証内容

- `VulkanNativeComputeTests.AsyncCopy.cs` はCPU gateを閉じた状態で3 frame分のuploadと
  computeを提出し、全Submitとrecording DisposeがGPU完了を待たずに返ることを確認する。
  gateを明示signalした後、copyされた入力から計算した3 frameの結果をreadbackする。
- `VulkanNativeRasterTests.AsyncCopy.cs` はCopyで初期化・uploadしたcolor textureをMainで
  sampleし、逆方向のMain attachmentからCopy readbackも確認する。
- `VulkanNativeTimelineConcurrencyTests.cs` はCPU WaitとSubmit、別queue同士のSubmitを
  並行実行する。sleepや経過時間に依存する成否判定は使わない。
- `VulkanNativeCommandsTests.Timelines.cs` は初期値、CPU signal、複数GPU wait、空batch、
  queue間signal、wait-before-signal、device loss、受理前失敗を確認する。

GPU試験は `VulkanNativeConformance` trait に分離する。CopyQueueがない環境では該当試験を
具体的な理由付きでskipする。通常のGPU readback成功をvalidation layerでの検証とは扱わない。
