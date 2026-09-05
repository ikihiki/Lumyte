# E4: ディスクリプタと GPU 実行

## 状態

採用済み。2026-09-06 に実装した。

## 背景

従来は、同じ `GpuResourceTable` を繰り返し設定しても DirectX 12 と Vulkan で native descriptor を再確保していた。WebGPU の bind group cache は table オブジェクトの同一性をキーにしており、上限もなかった。また `ExecuteAsync` は awaitable ではない提出 API で、GPU 完了待機や提出後の 2D データの寿命が利用者から読み取りにくかった。

## 決定

- GPU へ送信して直ちに返る入口を `GpuRenderGraphPlan.Submit` とする。同期実行は `ExecuteAndWait`、GPU 完了は `GpuRenderGraphExecution.WaitForCompletionAsync` で待つ。互換性のため既存の `Execute` と `ExecuteAsync` は維持する。
- 待機のキャンセルは CPU 側の待機だけを中断する。提出済み GPU コマンドは取り消さず、retirement queue が保持する資源も GPU 完了前には解放しない。
- `GpuFrame.Retain` で frame が利用する lease を提出へ移し、execution の破棄後も completion token まで保持する。TwoD の標準入口は `PreparedDisplayList.AcquireLease` を自動取得する。
- DirectX 12 は同じ記録中の同一 table/revision を再 bind し、shader-visible heap を GPU 完了時に容量別 pool へ返して次 frame で再利用する。生成数・再利用数・待機中 heap 数を公開する。
- Vulkan は同じ記録中の同一 table/revision に割り当てた descriptor set を再 bind する。既存の fence 単位 descriptor pool page 回収と合わせ、完了前の page を再利用しない。
- WebGPU は layout と resource ID の並びを安定キーにする。同じ内容なら別の `GpuResourceTable` インスタンスでも bind group を再利用する。LRU の上限を 256 件とし、texture view、sampler、buffer view の破棄では依存 entry だけを無効化する。entry 数・生成数・hit 数・eviction 数・上限を公開する。
- `AddTextureUpload` を RenderGraph の pass として追加する。copy の書込み依存を後続 compute/draw が読むため、upload、compute、draw は一つの提出と barrier 計画に統合される。

## 結果

同じ table を 10,000 回 bind しても DirectX 12 と Vulkan の descriptor 容量を消費し続けない。DirectX 12 の heap は GPU 完了後の次 frame で再利用される。WebGPU は同一内容の 10,000 bind を一つの bind group にまとめ、異なる内容を 257 件投入しても 256 件へ収束する。

Prepared drawing を利用者が提出直後に破棄しても、frame lease によって GPU 完了まで backing buffer が残る。非同期待機をキャンセルしてもこの寿命は短くならない。

## 検証

- 共通 RenderGraph: copy→compute→draw の順序と barrier、キャンセル後の寿命、frame lease の completion 後解放
- DirectX 12: 10,000 bind、同一記録内再利用、次 frame の heap pool 再利用と生成・再利用回数
- Vulkan: 1,000/10,000 bind、descriptor page 数、複数提出後の page 再利用
- WebGPU: 別 table インスタンスを使う 10,000 bind、256 件上限と eviction、依存 view 破棄時の無効化
