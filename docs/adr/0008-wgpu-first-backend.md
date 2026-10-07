# ADR-0008: .NET バインディングによる最初の wgpu バックエンド

- 状態: 採用
- 日付: 2026-10-07

## 背景

最初のグラフィックス実装は wgpu を使用し、.NET にあるバインディングを直接参照する。Lumyte の C++ ラッパーや `.Native` プロジェクトは作らない。先にデバイス生成、Compute、RenderEncoder、GPU 完了、読み戻しを実行して、設計の成立を確認する。

[ADR-0004](0004-graphics-library.md)〜[ADR-0007](0007-graphics-descriptors.md) は広い共通 API の提案であり、全機能を初回実装の完了条件にはしない。本 ADR は最初のバックエンド選択と動く初期契約を採用する。共通契約へ未統合の部分と実装済みの API を区別する。

## 決定

### バインディングと配置

`src/Graphics/Lumyte.Graphics.Wgpu` に C# の `Lumyte.Graphics.Wgpu` を配置する。`Ahjo.Wgpu` 0.7.0 を直接 PackageReference し、既存の wrapper と必要な raw binding を利用する。wgpu-native の実行ファイルは第三者パッケージの推移的依存 `Ahjo.Wgpu.Native` が RID ごとに供給する。Lumyte の `.Native` プロジェクト、C++、独自 P/Invoke 宣言、ネイティブビルドは追加しない。

バインディングは pre-1.0 のためバージョンを固定する。既存 wrapper の handle struct はコピーと二重解放を許してしまうので、公開 API は所有権を検証する managed class に包む。Native handle とアドレスは internal に留める。

最初はネイティブプロセスで動くヘッドレス実装とする。wgpu-native が内部で Vulkan／DirectX などを選択する。Browser の WebGPU 接続は別実装であり、wgpu-native のパッケージを Browser に配布できると扱わない。既存の DirectX／Vulkan `.Native` の設計は将来の独立バックエンドに残すが、最初の実装には使用しない。

### 初期契約と公開 API

現段階の名前空間は `Lumyte.Graphics.Wgpu`、所有型は managed class とする。初期 API は広い共通契約の実装を偽装せず、将来 `Lumyte.Graphics` と統合する前提の backend 固有 API として公開する。

| API | 実装する契約 |
| --- | --- |
| `WgpuDevice.Create()` | blocking の Instance／Adapter／Device 生成。バインディングの初期化失敗は例外で返す |
| `CreateBuffer(BufferDesc)`／`Buffer.Slice(ulong, ulong)` | サイズ・用途・範囲を検証。初期 buffer のサイズは 4-byte の倍数 |
| `WriteBuffer<T>(BufferSlice, ReadOnlySpan<T>)` | blittable データの明示的な byte Upload。未知の Slang 型の ABI を自動保証しない |
| `CreateReference<T>(BufferSlice)` | 初期の登録済みデータ schema は UInt32 配列のみ。非所有・型付きの不透明参照を作る |
| `CreateShader(string wgsl)` | Slang から生成済みの WGSL を読み込む。Native の validation error は例外で通知 |
| `CreateComputePipeline(ComputePipelineDesc)`／`ComputePipeline.CreateArguments(GpuReference<uint>)` | 一つの論理 RWStructuredBuffer<uint> を使う Compute 引数。binding と実データ参照の解決は library 内部 |
| `CommandEncoder.Dispatch(...)`／`CopyBuffer(...)` | 単一 queue の Compute とコピー。パス中は禁止 |
| `CreateTexture(TextureDesc)`／`Texture.CreateView()` | single-sample、単一 mip／layer の RGBA8Unorm オフスクリーン target |
| `CreateGraphicsPipeline(GraphicsPipelineDesc)` | rootless vertex／fragment、triangle-list、blend／depth 無効の graphics pipeline |
| `BeginRenderPass(RenderPassDesc)`／`RenderEncoder.SetPipeline` | 一つの color attachment の Clear／Load、Store／Discard |
| `RenderEncoder.SetViewport`／`SetScissor`／`SetIndexBuffer`／`Draw`／`DrawIndexed` | パス状態、範囲、index 用途を検証する記録 API |
| `Finish()`／`Submit(CommandBuffer)` | 一回限りの送信。二重送信は Native に渡す前に拒否 |
| `Submission.IsCompleted`／`Wait`／`WaitAsync` | callback を poll して実 GPU 完了を確認。待機キャンセルは GPU 処理を取り消さない |
| `CopyTextureToBuffer(Texture, Buffer, uint bytesPerRow)`／`ReadBuffer(Buffer)` | 256-byte pitch の color コピー、完了後の UInt32 読み戻し。サンプルは bytes として pixel を検証 |

初期の Desc は実装するフィールドだけを持つ。TextureDesc は Width／Height、RenderPassDesc は Target／Load／Store／ClearValue、GraphicsPipelineDesc／ComputePipelineDesc は Shader と entry point を持ち、詳細な固定状態は省略する。サポートしない形式や状態を受け取って黙って無視する API は提供しない。全 format、depth／stencil、MSAA resolve、複数 attachment、Sampler、生成 serializer、一般的な ShaderArtifact、オンライン Slang compiler と共通の Result API はまだ未実装であり、ADR-0005／0007 の全仕様を満たしたとは扱わない。

### GPU 参照、所有権、同期

GpuReference は元の managed resource と範囲を内部で保持し、整数変換、実アドレス、binding index の公開と serialization を持たない。失効した resource、別 Device の resource、default の参照、未対応の型と Storage の alignment／size を検証する。初期の Slang schema の WGSL binding は library 内部で所有し、利用者は参照だけを渡す。

CommandEncoder が記録に使った resource を lease し、Finish で CommandBuffer、Submit で Submission へ引き継ぐ。完了確認で lease を解放する。記録破棄時も解放する。ShaderArguments は pipeline と参照先 Buffer を、その破棄まで lease する。lease 中の Dispose と CPU 書き込み、Device の子 resource／Encoder／Submission が残った状態の Device.Dispose は拒否する。

Dispose は idempotent とし、End は一回限りの状態変更とする。親 Encoder の破棄は開いた pass を無効化する。操作は Device の gate で直列化し、Native error callback は所有 Device の診断 queue へ書き込み、managed 例外を Native 境界外へ出さない。queue 完了の失敗は待機で通知する。未完了の callback storage はキャンセルで解放しない。利用者はキャンセル後も Submission の完了を確認する。

初期版では resource は明示的に Dispose、Submission は Wait／WaitAsync または IsCompleted で完了まで poll する。最終フレームを含め、完了を観測せず Device を破棄しない。一般的な Runtime の deferred deletion とフレーム allocator は後続実装とする。

### シェーダーと検証

サンプルの `.slang` を正本とし、mise の固定 Slang から offline に WGSL を生成する。生成物も保存し、最小 backend の build／run に compiler を必須依存としない。`tools/shaders/compile-samples.py --check` で生成物の一致を確認する。

GPU integration test は adapter 不在を成功や skip とせず、実行環境を準備して実行する。Linux の lavapipe で Slang Compute の UInt32 配列、通常／indexed triangle の color readback、pass 状態、二重送信、resource lifetime、破棄、別 Device と invalid range を検証する。Windows／Browser の動作は今回の検証結果に含めない。

## 検討した代替案

### wgpu-native を C++ と独自 P/Invoke で包む

ABI を制御できるが、既存の .NET binding と runtime 配布を重複実装する。最初は既存 binding を直接使う。

### 最初から DirectX／Vulkan を個別実装する

Native API 固有の最適化は可能だが、データと記録契約の初期検証が複数実装に分散する。wgpu で共通の利用経路を先に動かす。

### 提案済みの全 API を空の実装で公開する

API の形は揃うが、未対応の機能を使用できると誤認させる。初期 schema と描画形式を限定した具体 API を実装し、未実装部分を明示する。

## 結果と影響

- .NET binding と既存 runtime package だけで Compute／描画／Readback の経路を実行できる。
- Lumyte の Native ビルドを増やさずに最初の backend を確認できる。
- pre-1.0 binding の更新時に API と callback／所有権を再検証する必要がある。
- 現在の API と Desc は限定的であり、共通契約への統合や一般化で変更する可能性がある。
- lavapipe の成功は実 GPU の性能や全プラットフォームの互換性を保証しない。

## 参考資料

- [フォルダ構成](0002-repository-layout.md)
- [グラフィックス共通契約](0004-graphics-library.md)
- [Slang とデータ受け渡し](0005-shader-compilation-and-data-interop.md)
- [RenderEncoder](0006-render-encoder.md)
- [Desc 型](0007-graphics-descriptors.md)
- [wgpu 実装](../../src/Graphics/Lumyte.Graphics.Wgpu/WgpuDevice.cs)
- [Ahjo.Wgpu](https://github.com/pekkah/Ahjo-Wgpu)
