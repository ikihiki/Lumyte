# ADR-0004: NoGraphicsAPI を参考にしたグラフィックスライブラリの共通契約

- 状態: 提案
- 日付: 2026-10-07

## 背景

Lumyte のグラフィックスライブラリについて、描画バックエンドとエンジン側の境界、データの受け渡し、GPU リソースの寿命と同期を定める。[ADR-0002](0002-repository-layout.md) に従い、公開 API は C#、対象は Windows、Linux、Browser、バックエンドは DirectX、Vulkan、WebGPU とする。

NoGraphicsAPI は GPU ポインタ、アプリケーション所有のディスクリプタヒープ、draw／dispatch ごとの単一 root 引数により、リソースバインディングをデータとして扱う。低レベル GPU 操作とアロケータ・Upload・遅延解放を分ける点も参考になる。

一方、参照時点の NoGraphicsAPI は Metal 4 と特定拡張を備えた Vulkan 1.4 を対象とする。WebGPU には任意の GPU アドレスをシェーダーから間接参照する共通機構がない。実 GPU アドレスを公開契約にすると対象環境と一致しないため、利用者から実表現を識別できない GPU データ参照を共通契約にする。Slang を基準とするシェーダー、コンパイル方式、バックエンドごとの受け渡し方式は [ADR-0005](0005-shader-compilation-and-data-interop.md) に分離する。

## 決定

以下を提案する。本 ADR はライブラリの境界と主要 API の設計を扱い、実装言語の変更や対応環境の削除は行わない。

最初の実装は [ADR-0008](0008-wgpu-first-backend.md) の managed wgpu バックエンドとし、既存 .NET binding を直接使用する。初期の共通 API サブセットを Core で公開し、テスト／サンプルは共通 API のみを使用する。本 ADR の残りの契約と、独立した DirectX／Vulkan 実装は後続とする。wgpu に Lumyte の `.Native` プロジェクトは追加しない。

### 責務と依存関係

| 層 | 配置・名前空間 | 責務 |
| --- | --- | --- |
| GPU Core | `Lumyte.Graphics` | デバイス、リソース、パイプライン、コマンド、送信完了の共通契約 |
| Graphics Runtime | `Lumyte.Graphics.Runtime`（当初は同じプロジェクト内） | フレームごとの領域確保、Upload、引数の構築、完了後の再利用・遅延解放 |
| バックエンド | `Lumyte.Graphics.DirectX`／`.Vulkan`／`.WebGPU` | 共通契約の実装、機能検出、ネイティブ同期とバインディングへの変換 |
| Renderer | エンジン側。具体的なプロジェクトは別途決定 | Mesh、Material、描画パス、必要なら Render Graph |

依存は Renderer → Runtime → Core、各バックエンド → Core とする。Core はバックエンド、シーン、ウィンドウ実装に依存しない。初期実装では `Lumyte.Graphics.Core` が共通型を持ち、生成層の `Lumyte.Graphics` が Core と backend に依存してデバイスを生成する。テストとサンプルは生成層と共通型のみを使用する。アプリケーションがバックエンドを選択してデバイスを作る。画面表示では Platform が生成した表示先を渡し、Graphics がウィンドウ自体を所有しない。

DirectX／Vulkan の C# バインディングは既存方針どおり各 `.Native` パッケージを参照する。WebGPU にネイティブ C++ 依存を要求しない。初期実装のプロジェクト構成と API サブセットは ADR-0008 に記録する。

### データとバインディング

共通 API は `Buffer` と非所有の `BufferSlice`（バッファ、バイトオフセット、バイト長）を使用する。GPU データを小さなオブジェクトごとに確保せず、大きなバッファを Runtime が部分確保する。範囲、用途、アラインメントを検証できる表現を保持する。

draw／dispatch は単一の `ShaderArguments` を受け取る。利用者は Slang の論理スキーマに対応する生成済み C# 引数型に定数と `GpuReference<T>`、テクスチャビュー、サンプラを設定する。GPU データ参照の実表現、root アドレス、ディスクリプタ番号、バインディングスロットを利用者に公開しない。

`GpuReference<T>` は型付きの非所有参照であり、実 GPU アドレスを含むかどうかも公開契約に含めない。整数・CPU ポインタへの変換、任意の値からの生成、物理表現のシリアライズを提供しない。バックエンドは Native 実装と Slang のライブラリモジュールを組み合わせ、反射情報に従って参照と引数を実データへ変換する。DirectX／Vulkan の実 GPU アドレスや descriptor、WebGPU の buffer binding と offset の違いはこの境界に閉じ込める。

`ShaderArgumentsLayout<T>`、生成引数型、バックエンド別の物理レイアウトと成果物の契約は ADR-0005 を正本とする。Core はコンパイラを必須依存にせず、オフラインまたはオンラインで生成された同じ成果物契約を受け取る。頂点はこのデータ参照を通してシェーダーが読み出す方式を基本とし、初期の共通 API に固定の頂点レイアウトを導入しない。

Mesh Shader、間接描画、共通スキーマで表現できない動的リソース参照などは任意機能とする。実 GPU アドレスの利用可否は内部の方式選択に使い、共通の GPU データ参照を利用者に放棄させる条件にしない。対象機能・上限を満たさないシェーダーや参照は生成時に明示的に拒否する。

### 公開 API 一覧

RenderEncoder の操作・状態・寿命の正本は [ADR-0006](0006-render-encoder.md)、本 ADR に登場する Desc 型と補助型のフィールド・既定値・検証条件は [ADR-0007](0007-graphics-descriptors.md) に分離する。以下は共通境界を示す主要 API の要約であり、詳細は各 ADR に従う。

以下は判断対象となる C# の主要シグネチャ案であり、既存の実装 API ではない。型は特記がなければ `Lumyte.Graphics`、Runtime 型は `Lumyte.Graphics.Runtime` に置く。`Desc` 型は生成条件、`Result<T>` は値または `GraphicsError` を持つ。各所有型は `IDisposable` を実装する。

| 公開 API | 役割 | 契約・注意事項 |
| --- | --- | --- |
| `ValueTask<Result<IReadOnlyList<AdapterInfo>>> IGraphicsBackend.EnumerateAdaptersAsync(CancellationToken cancellationToken)` | backend の候補を列挙 | AdapterInfo は不透明な AdapterId、表示名、機能・上限を持つ。Browser の列挙制約下では backend が取得できる候補だけを返す |
| `ValueTask<Result<GraphicsDevice>> IGraphicsBackend.CreateDeviceAsync(DeviceDesc desc, CancellationToken cancellationToken)` | バックエンドを指定して初期化 | 必須機能不足を明示して失敗。Browser の非同期初期化にも対応 |
| `DeviceCaps GraphicsDevice.Caps { get; }` | 機能・上限の取得 | Mesh Shader、間接描画、参照可能リソース数、キュー構成などの機能・上限を公開。参照の物理表現は公開しない |
| `Result<Buffer> GraphicsDevice.CreateBuffer(BufferDesc desc)` | データ領域の生成 | サイズ、用途、メモリ種別を指定。GPU アドレスや CPU mapping を保証しない |
| `BufferSlice Buffer.Slice(ulong offset, ulong length)` | 非所有の部分領域 | 境界を検証。元の Buffer の寿命を延ばさない |
| `GpuReference<T> GraphicsDevice.CreateReference<T>(BufferSlice data) where T : IShaderData` | GPU データの型付き参照を作る | 登録済み Slang データスキーマ、デバイス、範囲、用途、要素 stride・アラインメントを検証。実アドレスを公開しない |
| `Result<Texture> GraphicsDevice.CreateTexture(TextureDesc desc)` | テクスチャの生成 | サイズ、形式、用途を検証。共通 API は CPU mapping を公開しない |
| `Result<TextureView> Texture.CreateView(TextureViewDesc desc)` | mip・layer・aspect 範囲の参照 | 元の Texture を所有しない。互換形式と範囲を検証 |
| `Result<Sampler> GraphicsDevice.CreateSampler(SamplerDesc desc)` | サンプリング設定 | テクスチャとは独立した所有リソース |
| `Result<ShaderModule> GraphicsDevice.CreateShader(ShaderArtifact artifact)` | バックエンド用のシェーダー成果物を読み込む | ADR-0005 の成果物・対象 profile・ABI を検証。Core は Slang コンパイラを起動しない |
| `Result<GraphicsPipeline> GraphicsDevice.CreateGraphicsPipeline(GraphicsPipelineDesc desc)` | 描画状態の生成 | シェーダー、引数レイアウト、出力形式、固定状態を保持 |
| `Result<ComputePipeline> GraphicsDevice.CreateComputePipeline(ComputePipelineDesc desc)` | Compute 状態の生成 | シェーダーと引数レイアウトの一致が必要 |
| `void RenderEncoder.DrawIndexed(ShaderArguments arguments, IndexedDrawDesc desc)`／`SetIndexBuffer(BufferSlice indices, IndexFormat format)` | index 描画 | 詳細は ADR-0006／0007。index 範囲と pipeline の topology／strip format を検証 |
| `void RenderEncoder.End()`／`Dispose()` | 記録 scope の終了 | GPU 完了を意味しない。二重 End と Dispose の契約は ADR-0006 |
| `Result<CommandEncoder> GraphicsDevice.CreateCommandEncoder()` | コマンド記録の開始 | 単一スレッドで所有。初期設計では一つの汎用キューを対象とする |
| `void CommandEncoder.CopyBuffer(BufferSlice source, BufferSlice destination)` | データの転送 | コピー用途、サイズ、アラインメントが有効であること |
| `void CommandEncoder.CopyBufferToTexture(BufferSlice source, Texture destination, TextureCopyDesc desc)` | テクスチャ Upload | 行ピッチとコピー範囲を検証 |
| `RenderEncoder CommandEncoder.BeginRenderPass(RenderPassDesc desc)` | 描画パスの開始 | attachment と load／store を指定。終了まで別パスを開始しない |
| `void RenderEncoder.SetPipeline(GraphicsPipeline pipeline)` | パイプライン選択 | attachment の形式と一致すること |
| `void RenderEncoder.SetViewport(Viewport viewport)`／`SetScissor(Scissor scissor)` | 動的状態の指定 | パス内の状態。共通経路の depth／stencil はパイプラインに含める |
| `void RenderEncoder.Draw(ShaderArguments arguments, uint vertexCount, uint instanceCount = 1)` | 描画 | Pipeline 設定済み、引数レイアウト一致。終了は `End()` で行う |
| `void CommandEncoder.Dispatch(ComputePipeline pipeline, ShaderArguments arguments, uint x, uint y, uint z)` | Compute 実行 | 描画パスの外。グループ数と引数レイアウトを検証 |
| `void CommandEncoder.Barrier(ReadOnlySpan<ResourceDependency> dependencies)` | リソースの依存関係を宣言 | 前後のアクセス、BufferSlice／TextureView を指定。パス外で呼ぶ |
| `CommandBuffer CommandEncoder.Finish()` | 記録の確定 | 一度だけ呼べる。以後 Encoder への記録は禁止 |
| `Result<Submission> GraphicsDevice.Submit(CommandBuffer commands)` | キューへの送信 | 一回限りの送信。リソースを GPU 完了まで保持 |
| `bool Submission.IsCompleted { get; }`／`ValueTask Submission.WaitAsync(CancellationToken cancellationToken)` | GPU 完了確認 | 待機のキャンセルは送信済み処理を取り消さない |
| `FrameContext GraphicsRuntime.BeginFrame()` | 再利用可能なフレーム領域を選ぶ | 使用中の領域は再利用しない。不足時の待機・拡張は Runtime が管理 |
| `ShaderArguments FrameContext.CreateArguments<T>(ShaderArgumentsLayout<T> layout, in T values) where T : IShaderArgumentsData` | 単一引数の構築 | 生成引数型を同期的に pack。参照の型・所属・寿命とレイアウトを検証。利用者は物理スロットを指定しない |
| `void FrameContext.EndFrame(Submission completion)` | フレーム領域の再利用条件を登録 | 対象フレームの使用を含む Submission と結び付ける |
| `void GraphicsRuntime.DeferDispose(IDisposable resource, Submission lastUse)` | GPU 完了後の解放 | 最後の使用を含む Submission が必要 |

`GpuReference<T>` は所有権を持たず元の Buffer の寿命を延ばさない。`ResourceDependency` は対象範囲と producer／consumer のアクセスを表す。`ShaderArguments` は Runtime 所有領域の非所有参照で、所属フレームの完了後は再使用できない。`GraphicsPipelineDesc` と ShaderModule の引数レイアウトが一致しない場合は生成を失敗させる。

Swapchain の生成・acquire・present はこの ADR の公開 API 範囲に含めず、後続 ADR で定める。最初の検証経路は画面表示を必要としないオフスクリーン描画とする。

### 所有権、同期、スレッド

Core の `Dispose()` は即時解放を要求する操作とし、利用者は未完了の GPU 使用がないことを保証する。通常の利用では Runtime の遅延解放を使用する。BufferSlice、GpuReference、TextureView の参照先、引数、内部ディスクリプタスロットの再利用にも同じ完了条件を適用する。Device は子リソースと GPU 使用の終了後に解放する。

記録中・送信済みコマンドが参照する引数やリソースを変更、破棄、再利用してはならない。記録していないリソースの Upload は staging 経由を共通経路とする。書き込み内容の可視化は送信とバックエンドの同期規約で保証し、C# の CPU 書き込みだけで GPU 可視性が成立すると扱わない。

初期の共通経路は単一キューで、送信順を保持する。GPU 完了は Submission の完了で判定し、CPU フレーム番号だけでは判定しない。Encoder／RenderEncoder／FrameContext は単一スレッドから操作する。Device の生成・送信操作は利用側で直列化し、Browser ではバックエンドが実行コンテキストの制約を守る。並列記録と複数キューは後続拡張とする。

NoGraphicsAPI のリソース一覧を持たないグローバルバリアは共通 API にそのまま採用しない。共通契約では対象とアクセスを保持し、Vulkan のレイアウト・アクセス遷移、DirectX のリソース遷移、WebGPU の使用範囲・パス境界の検証へ変換する。WebGPU では必要に応じてパスを分ける。依存の宣言は任意の読み書きの組み合わせを合法化するものではなく、同時使用できない組み合わせは拒否する。

### エラーと Native 相互運用

初期化、確保、パイプライン生成、送信の実行環境に起因する失敗は `Result<T>` と `GraphicsError` で通知する。引数範囲や記録状態などの契約違反は C# の引数・状態例外として通知する。Device Lost はデバイス単位で保持し、以降の送信を拒否して未完了の待機をエラーで終了させる。Browser の非同期検証エラーも記録操作の成功だけで隠さない。

DirectX／Vulkan の境界は C ABI とし、固定幅整数、明示レイアウトの POD、opaque handle、戻り値のエラーコードを使用する。C++ 例外を境界の外へ出さない。Managed オブジェクトのアドレスを GPU アドレスとして扱わない。Span や pin したメモリのポインタは呼び出し中だけ有効で、Native 側が保存するデータは同期的にコピーする。GPU 使用期間にわたるメモリは専用の所有領域に置く。GPU データの pack と Slang の物理レイアウトは ADR-0005 に従う。C ABI の全宣言、ハンドルとエラー文字列の寿命の詳細は後続 ADR で定める。

### プラットフォーム対応

| 環境 | バックエンド | 本 ADR の扱い |
| --- | --- | --- |
| Windows | DirectX／Vulkan | 共通契約を実装する目標。DirectX のバージョンと最低機能は別途決定 |
| Linux | Vulkan | 共通契約を実装する目標。NoGraphicsAPI 固有の最新拡張は共通経路に必須化しない |
| Browser | WebGPU | 共通契約を実装する目標。任意 GPU アドレスへの間接参照を要求しない |

この表は対応方針であり実装・実機検証の完了を表さない。必須機能不足はデバイス生成時に報告し、任意機能不足は Caps と明示的な非対応エラーで扱う。Metal は ADR-0002 の対象外であり今回追加しない。

シェーダーは [ADR-0005](0005-shader-compilation-and-data-interop.md) に従って Slang を基準とし、オフライン／オンラインコンパイルと各バックエンドへの実データの受け渡しを行う。

## 検討した代替案

### NoGraphicsAPI をそのままラップする

GPU ポインタと小さな API をすぐ利用できるが、現在のバックエンドと必須機能は DirectX／WebGPU を含む Lumyte の対象と一致しない。参考実装として扱い、直接依存は今回採用しない。

### GPU ポインタと Bindless だけを共通 API にする

バインディングと頂点レイアウトを省略できるが、WebGPU に同じ意味を保証できない。型付きの不透明な参照を共通経路に残し、実 GPU アドレスを使うかどうかは Native／Slang の内部実装で選択する。

### 描画のたびに個別のリソースをバインドする

既存 API に対応させやすいが、Material ごとの状態変更が呼び出し列として分散する。リソース参照と定数を一つの ShaderArguments にまとめ、バックエンドが必要なバインディングを構築する。

### リソース管理と Renderer を一つのライブラリに統合する

利用開始は簡単になるが、描画方式と GPU 操作の契約が結合する。Core、Runtime、Renderer の責務を分け、Core をオフスクリーン描画や Compute にも利用できるようにする。

## 結果と影響

- NoGraphicsAPI のデータ中心の引数モデルと責務分離を取り入れながら、既存の対象環境を維持できる。
- WebGPU 対応のため、共通経路には Buffer と引数レイアウトが残る。NoGraphicsAPI と同じ API の小ささや呼び出しコストは保証しない。
- 利用者が GPU アドレスとバインディング表現を識別せずにデータ参照を扱える一方、Native と Slang の対応モジュール、生成コード、成果物 ABI の管理が必要になる。
- Runtime がフレーム領域と解放を管理しても、Core を直接使用する利用者は寿命と同期の契約を守る必要がある。
- シェーダー成果物の反射情報と C#／Native のレイアウトを検証する仕組みが必要になる。
- 文書のみの提案であり、API の実装、GPU 上の性能、各環境での成立はまだ検証していない。

## 検証方針

実装時は共通経路で Buffer Upload → Compute → オフスクリーン描画 → Readback を行い、DirectX、Vulkan、WebGPU で結果とバインディングの対応を比較する。引数範囲とレイアウトの不一致を拒否すること、GPU 完了前にフレーム領域やリソースを再利用しないこと、Device Lost で待機が終了することを確認する。

GPU データ参照の解決とシェーダーコンパイルは ADR-0005 の検証方針に従う。性能はバックエンドごとの CPU 記録時間、割り当て回数、GPU 時間を実測し、効果が確認できるまで優位性を保証しない。今回の PR では ADR の規約、参照先、既存方針との整合性を確認する。

## 別途決定する事項

- DirectX のバージョン、Vulkan／WebGPU の最低機能・上限と、対応環境ごとの必須機能。
- C ABI の全宣言、P/Invoke、Native パッケージの RID とバージョニング。
- Swapchain と Platform の接続、acquire／present、リサイズ、画面喪失への対応。
- Runtime のメモリ予算、枯渇時の方針、フレーム数、並列記録、複数キュー。
- Renderer のプロジェクト配置、Mesh／Material、Render Graph と自動依存解析。

## 参考資料

- [ADR の書き方と運用](0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](0002-repository-layout.md)
- [開発環境](0003-development-environment.md)
- [Slang のコンパイルと GPU データ受け渡し](0005-shader-compilation-and-data-interop.md)
- [RenderEncoder](0006-render-encoder.md)
- [Graphics の Desc 型](0007-graphics-descriptors.md)
- [NoGraphicsAPI README（参照コミット固定）](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/README.md)
- [NoGraphicsAPI 設計比較](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/docs/no-graphics-api-comparison.md)
- [NoGraphicsAPI 公開 API](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/include/NoGraphicsAPI/NoGraphicsAPI.hpp)
- [WebGPU 仕様](https://www.w3.org/TR/webgpu/)
