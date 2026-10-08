# ADR-0004: グラフィックデバイスとバックエンドの共通境界

- 状態: 提案
- 日付: 2026-10-07

## 背景

Lumyte のグラフィックスライブラリについて、描画バックエンドとエンジン側の境界、データの受け渡し、GPU リソースの寿命と同期を定める。[ADR-0002](0002-repository-layout.md) に従い、公開 API は C#、対象は Windows、Linux、Browser、バックエンドは DirectX、Vulkan、WebGPU とする。

NoGraphicsAPI は GPU ポインタ、アプリケーション所有のディスクリプタヒープ、draw／dispatch ごとの単一 root 引数により、リソースバインディングをデータとして扱う。低レベル GPU 操作とアロケータ・Upload・遅延解放を分ける点も参考になる。

一方、参照時点の NoGraphicsAPI は Metal 4 と特定拡張を備えた Vulkan 1.4 を対象とする。WebGPU には任意の GPU アドレスをシェーダーから間接参照する共通機構がない。実 GPU アドレスを公開契約にすると対象環境と一致しないため、利用者から実表現を識別できない GPU データ参照を共通契約にする。Slang を基準とするシェーダー、コンパイル方式、バックエンドごとの受け渡し方式は [ADR-0010](0010-shader-compilation-and-data-interop.md) に分離する。

## 決定

以下を提案する。本 ADR はデバイスとライブラリの境界の設計を扱い、実装言語の変更や対応環境の削除は行わない。

最初の実装は [ADR-0011](0011-wgpu-first-backend.md) の managed wgpu バックエンドとし、既存 .NET binding を直接使用する。初期の共通 API サブセットを Core で公開し、テスト／サンプルは共通 API のみを使用する。本 ADR の残りの契約と、独立した DirectX／Vulkan 実装は後続とする。wgpu に Lumyte の `.Native` プロジェクトは追加しない。

### 責務と依存関係

| 層 | 配置・名前空間 | 責務 |
| --- | --- | --- |
| GPU Core | `Lumyte.Graphics` | デバイス、リソース、パイプライン、コマンド、送信完了の共通契約 |
| Graphics Runtime | `Lumyte.Graphics.Runtime`（当初は同じプロジェクト内） | フレームごとの領域確保、Upload、引数の構築、完了後の再利用・遅延解放 |
| バックエンド | `Lumyte.Graphics.DirectX`／`.Vulkan`／`.WebGPU` | 共通契約の実装、機能検出、ネイティブ同期とバインディングへの変換 |
| Renderer | エンジン側。具体的なプロジェクトは別途決定 | Mesh、Material、描画パス、必要なら Render Graph |

依存は Renderer → Runtime → Core、各バックエンド → Core とする。Core はバックエンド、シーン、ウィンドウ実装に依存しない。初期実装では `Lumyte.Graphics.Core` が共通型を持ち、生成層の `Lumyte.Graphics` が Core と backend に依存してデバイスを生成する。テストとサンプルは生成層と共通型のみを使用する。アプリケーションがバックエンドを選択してデバイスを作る。画面表示では Platform が生成した表示先を渡し、Graphics がウィンドウ自体を所有しない。

DirectX／Vulkan の C# バインディングは既存方針どおり各 `.Native` パッケージを参照する。WebGPU にネイティブ C++ 依存を要求しない。初期実装のプロジェクト構成と API サブセットは ADR-0011 に記録する。

### 関連契約の配置

本 ADR はデバイスの生成、機能検出、共通層と backend の依存境界、デバイス単位の所有権を扱う。生成 factory の戻り値の詳細は各リソースの ADR、記録と送信はコマンドバッファの ADR を正本とする。

- [バッファ](0005-buffer-resource-contract.md): 型付き領域、CPU コピー、サイズとコピー制約。
- [テクスチャ](0006-texture-resource-contract.md): 画像、view、subresource と転送条件。
- [サンプラー](0007-sampler-resource-contract.md): sampling 設定と画像との適合。
- [バインディング](0008-resource-bindings.md): 不透明参照、引数とマテリアルの参照解決。
- [コマンドバッファ](0009-command-buffer.md): 転送、バリア、RenderEncoder、pipeline、送信と完了。
- [シェーダー](0010-shader-compilation-and-data-interop.md): Slang、成果物とデータ layout。
- [wgpu](0011-wgpu-first-backend.md): 実装済みサブセットと .NET binding。

### 公開 API 一覧

API 差分の比較元は origin/main（Graphics API は未導入）。以下はデバイス境界の設計案であり、実装済みサブセットは wgpu の ADR に従う。

```diff
+namespace Lumyte.Graphics
+{
+    public interface IGraphicsBackend
+    {
+        // backend の候補を列挙
+        // AdapterInfo は不透明な AdapterId、表示名、機能・上限を持つ
+        // Browser の列挙制約下では backend が取得できる候補だけを返す
+        ValueTask<Result<IReadOnlyList<AdapterInfo>>> EnumerateAdaptersAsync(CancellationToken cancellationToken);
+
+        // バックエンドを指定して初期化
+        // 必須機能不足を明示して失敗
+        // Browser の非同期初期化にも対応
+        ValueTask<Result<GraphicsDevice>> CreateDeviceAsync(DeviceDesc desc, CancellationToken cancellationToken);
+    }
+
+    public sealed class GraphicsDevice : IDisposable
+    {
+        // 機能・上限の取得
+        // Mesh Shader、間接描画、参照可能リソース数、キュー構成などの機能・上限を公開
+        // 参照の物理表現は公開しない
+        public DeviceCaps Caps { get; }
+
+        // データ領域の生成
+        // サイズ、用途、メモリ種別を指定
+        // GPU アドレスや CPU mapping を保証しない
+        public Result<IGraphicsBuffer<T>> CreateBuffer<T>(BufferDesc<T> desc) where T : unmanaged;
+        // 確保せず T の解決済みレイアウトと要素単位のコピー制約を取得する。
+        public BufferLayout<T> GetBufferLayout<T>() where T : unmanaged;
+
+        // GPU データの型付き参照を作る
+        // 登録済み Slang データスキーマ、デバイス、範囲、用途、要素 stride・アラインメントを検証
+        // 実アドレスを公開しない
+        public GpuReference<T> CreateReference<T>(BufferSlice<byte> data) where T : IShaderData;
+
+        // テクスチャの生成
+        // サイズ、形式、用途を検証
+        // 共通 API は CPU mapping を公開しない
+        public Result<IGraphicsTexture> CreateTexture(TextureDesc desc);
+
+        // サンプリング設定
+        // テクスチャとは独立した所有リソース
+        public Result<Sampler> CreateSampler(SamplerDesc desc);
+
+        // バックエンド用のシェーダー成果物を読み込む
+        // ADR-0010 の成果物・対象 profile・ABI を検証
+        // Core は Slang コンパイラを起動しない
+        public Result<ShaderModule> CreateShader(ShaderArtifact artifact);
+
+        // 描画状態の生成
+        // シェーダー、引数レイアウト、出力形式、固定状態を保持
+        public Result<GraphicsPipeline> CreateGraphicsPipeline(GraphicsPipelineDesc desc);
+
+        // Compute 状態の生成
+        // シェーダーと引数レイアウトの一致が必要
+        public Result<ComputePipeline> CreateComputePipeline(ComputePipelineDesc desc);
+
+        // コマンド記録の開始
+        // 単一スレッドで所有
+        // 初期設計では一つの汎用キューを対象とする
+        public Result<CommandEncoder> CreateCommandEncoder();
+
+        // キューへの送信
+        // 一回限りの送信
+        // リソースを GPU 完了まで保持
+        public Result<Submission> Submit(CommandBuffer commands);
+    }
+}
```

### 型と公開契約

本 ADR の Desc 型は `Lumyte.Graphics` 名前空間に置く未実装の C# 公開 API 案である。基本形は `public sealed record XxxDesc` とし、フィールドは public の init-only property にする。宣言の必須項目には `required` を付け、生成・記録 API でも null、範囲、enum 値を検証する。`new XxxDesc { ... }` で組み立てる。Desc は GPU リソースの所有者ではなく、Dispose を持たない。

すべての Desc に診断用の `string? Label = null` を設ける。ラベルは動作や互換性を変えない。各 Desc の宣言にも Label を示す。既定のネストした Desc は宣言の既定値で生成したものを使い、null は受け付けない。配列は `IReadOnlyList<T>` とし、呼び出し時に値を snapshot する。呼び出し後の配列変更が GPU 記録に影響しないようにする。

一般の constructor には GPU 操作を持たせず、検証は CreateDeviceAsync／CreateBuffer／CreateTexture／CreateView／CreateSampler／CreateGraphicsPipeline／CreateComputePipeline／BeginRenderPass／Draw／DrawIndexed／RecordCopyBufferToTexture が行う。契約違反は引数例外、未対応機能・確保失敗は ADR-0004 の Result／GraphicsError を使用する。recording API での unsupported な入力も記録前の引数・状態エラーとして通知し、backend 記録失敗は ADR-0009 に従って Encoder を Faulted にする。Desc を黙って補正しない。

DeviceCaps は本 ADR が参照する `MaxBufferSize`、`MaxColorAttachments`、`MaxAnisotropy`、`CopyBufferOffsetAlignment`、`CopyBytesPerRowAlignment` と compute／texture／binding の上限を公開する。`FormatCapabilities DeviceCaps.GetFormatCapabilities(TextureFormat format)` は sampled／filterable／comparison-sampleable／storage-read／storage-write／renderable／blendable の可否、対応 sample count、許可する view format を返す。不明な format は引数例外、既知の未対応 format は空の capability とする。これらは候補選択・Desc 検証に使用し、参照の物理表現を公開しない。

GPU アドレス、Native handle、物理 binding slot を Desc のフィールドに含めない。shader の成果物と論理引数 layout は [ADR-0010](0010-shader-compilation-and-data-interop.md) の型を参照する。

### DeviceDesc

API 差分の比較元は origin/main（Graphics API は未導入）。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record DeviceDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public DeviceDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // null は backend が選択
+        // ID はその backend の列挙で得た不透明値
+        // 別 backend の ID は拒否
+        public AdapterId? Adapter { get; init; } = null;
+
+        // Default／LowPower／HighPerformance
+        // 選択のヒントであり性能の保証ではない
+        public PowerPreference PowerPreference { get; init; } = PowerPreference.Default;
+
+        // 必須機能の flags
+        // 満たさない候補を使用しない
+        // 実 GPU アドレス方式の指定は含めない
+        public GraphicsFeatures RequiredFeatures { get; init; } = GraphicsFeatures.None;
+
+        // 未指定の上限は追加要求なし
+        // 各 nullable フィールドは必要な最小容量を表す
+        public DeviceLimitsRequest RequiredLimits { get; init; } = new();
+
+        // Default／Enabled／Disabled
+        // Enabled を実現できなければ生成を失敗させる
+        public ValidationMode Validation { get; init; } = ValidationMode.Default;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // GraphicsFeatures の初期 flags は None／MeshShader／IndirectDraw／AnisotropicFiltering／DepthBiasClamp とする
+    // feature の有無と実 API の提供範囲を区別し、未実装の拡張操作を利用可能として報告しない
+    // AdapterInfo は AdapterId Id、string Name、GraphicsFeatures Features、DeviceCaps Caps を持ち、名前や ID に実 GPU アドレスを含めない
+    public sealed record AdapterInfo
+    {
+        public AdapterId Id { get; }
+        public string Name { get; }
+        public GraphicsFeatures Features { get; }
+        public DeviceCaps Caps { get; }
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // DeviceLimitsRequest は ulong? MaxBufferSize、uint? MaxTextureDimension2D、uint? MaxColorAttachments、uint? MaxSampledTexturesPerStage、uint? MaxStorageBuffersPerStage、uint? MaxComputeInvocationsPerWorkgroup を持つ
+    // 名前は要求するデバイス上限に対応し、指定値は正数とする
+    // 満たした実値は DeviceCaps に返す
+    // キューは ADR-0004 の単一汎用キューを初期契約とし、任意の Native queue 指定は提供しない
+    public sealed record DeviceLimitsRequest
+    {
+        public ulong? MaxBufferSize { get; init; } = null;
+        public uint? MaxTextureDimension2D { get; init; } = null;
+        public uint? MaxColorAttachments { get; init; } = null;
+        public uint? MaxSampledTexturesPerStage { get; init; } = null;
+        public uint? MaxStorageBuffersPerStage { get; init; } = null;
+        public uint? MaxComputeInvocationsPerWorkgroup { get; init; } = null;
+    }
+}
```

### 所有権、同期、スレッド

Core の `Dispose()` は即時解放を要求する操作とし、利用者は未完了の GPU 使用がないことを保証する。通常の利用では Runtime の遅延解放を使用する。BufferSlice、GpuReference、TextureView の参照先、引数、内部ディスクリプタスロットの再利用にも同じ完了条件を適用する。Device は子リソースと GPU 使用の終了後に解放する。

記録中・送信済みコマンドが参照する引数やリソースを変更、破棄、再利用してはならない。Upload は利用者が確保した staging への CPU CopyFrom／CopyTo と、明示的な RecordCopyBuffer／CommandBuffer の送信に分ける。書き込み内容の可視化は送信とバックエンドの同期規約で保証し、C# の CPU 書き込みだけで GPU 可視性が成立すると扱わない。

初期の共通経路は単一キューで、送信順を保持する。GPU 完了は Submission の完了で判定し、CPU フレーム番号だけでは判定しない。Encoder／RenderEncoder／FrameContext は単一スレッドから操作する。Device の生成・送信操作は利用側で直列化し、Browser ではバックエンドが実行コンテキストの制約を守る。並列記録と複数キューは後続拡張とする。

NoGraphicsAPI のリソース一覧を持たないグローバルバリアは共通 API にそのまま採用しない。共通契約では対象とアクセスを保持し、Vulkan のレイアウト・アクセス遷移、DirectX のリソース遷移、WebGPU の使用範囲・パス境界の検証へ変換する。WebGPU では必要に応じてパスを分ける。依存の宣言は任意の読み書きの組み合わせを合法化するものではなく、同時使用できない組み合わせは拒否する。

### エラーと Native 相互運用

初期化、確保、パイプライン生成、送信の実行環境に起因する失敗は `Result<T>` と `GraphicsError` で通知する。引数範囲や記録状態などの契約違反は C# の引数・状態例外として通知する。Device Lost はデバイス単位で保持し、以降の送信を拒否して未完了の待機をエラーで終了させる。Browser の非同期検証エラーも記録操作の成功だけで隠さない。

DirectX／Vulkan の境界は C ABI とし、固定幅整数、明示レイアウトの POD、opaque handle、戻り値のエラーコードを使用する。C++ 例外を境界の外へ出さない。Managed オブジェクトのアドレスを GPU アドレスとして扱わない。Span や pin したメモリのポインタは呼び出し中だけ有効で、Native 側が保存するデータは同期的にコピーする。GPU 使用期間にわたるメモリは専用の所有領域に置く。GPU データの pack と Slang の物理レイアウトは ADR-0010 に従う。C ABI の全宣言、ハンドルとエラー文字列の寿命の詳細は後続 ADR で定める。

### プラットフォーム対応

| 環境 | バックエンド | 本 ADR の扱い |
| --- | --- | --- |
| Windows | DirectX／Vulkan | 共通契約を実装する目標。DirectX のバージョンと最低機能は別途決定 |
| Linux | Vulkan | 共通契約を実装する目標。NoGraphicsAPI 固有の最新拡張は共通経路に必須化しない |
| Browser | WebGPU | 共通契約を実装する目標。任意 GPU アドレスへの間接参照を要求しない |

この表は対応方針であり実装・実機検証の完了を表さない。必須機能不足はデバイス生成時に報告し、任意機能不足は Caps と明示的な非対応エラーで扱う。Metal は ADR-0002 の対象外であり今回追加しない。

シェーダーは [ADR-0010](0010-shader-compilation-and-data-interop.md) に従って Slang を基準とし、オフライン／オンラインコンパイルと各バックエンドへの実データの受け渡しを行う。

## 検討した代替案

### NoGraphicsAPI をそのままラップする

GPU ポインタと小さな API をすぐ利用できるが、現在のバックエンドと必須機能は DirectX／WebGPU を含む Lumyte の対象と一致しない。参考実装として扱い、直接依存は今回採用しない。

### GPU ポインタと Bindless だけを共通 API にする

バインディングと頂点レイアウトを省略できるが、WebGPU に同じ意味を保証できない。型付きの不透明な参照を共通経路に残し、実 GPU アドレスを使うかどうかは Native／Slang の内部実装で選択する。

### 描画のたびに個別のリソースをバインドする

既存 API に対応させやすいが、Material ごとの状態変更が呼び出し列として分散する。リソース参照と定数を一つの ShaderArguments にまとめ、バックエンドが必要なバインディングを構築する。

### リソース管理と Renderer を一つのライブラリに統合する

利用開始は簡単になるが、描画方式と GPU 操作の契約が結合する。Core、Runtime、Renderer の責務を分け、Core をオフスクリーン描画や Compute にも利用できるようにする。

### Native API の Desc をそのまま公開する

実装との対応は容易だが、Native handle、layout、binding と platform 条件が利用側へ広がる。共通フィールドと検証条件を定め、各 backend が Native の表現へ変換する。

### 既定値で全フィールドを埋める

短い呼び出しになるが、format、size、用途や shader の未指定が意図せず有効になる。意味を推測できないフィールドは必須にし、固定状態の便利な既定値と区別する。

### 可変の Desc を記録後も参照する

コピーは省けるが、GPU 記録の途中で条件が変わり、非同期処理と寿命管理が複雑になる。入力は呼び出し時に snapshot する。

## 結果と影響

- NoGraphicsAPI のデータ中心の引数モデルと責務分離を取り入れながら、既存の対象環境を維持できる。
- WebGPU 対応のため、共通経路には Buffer と引数レイアウトが残る。NoGraphicsAPI と同じ API の小ささや呼び出しコストは保証しない。
- 利用者が GPU アドレスとバインディング表現を識別せずにデータ参照を扱える一方、Native と Slang の対応モジュール、生成コード、成果物 ABI の管理が必要になる。
- Runtime がフレーム領域と解放を管理しても、Core を直接使用する利用者は寿命と同期の契約を守る必要がある。
- シェーダー成果物の反射情報と C#／Native のレイアウトを検証する仕組みが必要になる。
- 文書のみの提案であり、API の実装、GPU 上の性能、各環境での成立はまだ検証していない。

## 検証方針

実装時は共通経路で Buffer Upload → Compute → オフスクリーン描画 → Readback を行い、DirectX、Vulkan、WebGPU で結果とバインディングの対応を比較する。引数範囲とレイアウトの不一致を拒否すること、GPU 完了前にフレーム領域やリソースを再利用しないこと、Device Lost で待機が終了することを確認する。

DeviceDesc の必須項目、既定値、enum、null、上限と機能不足、snapshot、別 backend の AdapterId を検証する。GPU データ参照の解決は ADR-0008、シェーダーコンパイルは ADR-0010 の検証方針に従う。性能はバックエンドごとの CPU 記録時間、割り当て回数、GPU 時間を実測し、効果が確認できるまで優位性を保証しない。今回の PR では ADR の規約、参照先、既存方針との整合性を確認する。

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
- [Slang のコンパイルと GPU データ受け渡し](0010-shader-compilation-and-data-interop.md)
- [コマンドバッファ・RenderEncoder・描画 Desc](0009-command-buffer.md)
- [NoGraphicsAPI README（参照コミット固定）](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/README.md)
- [NoGraphicsAPI 設計比較](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/docs/no-graphics-api-comparison.md)
- [NoGraphicsAPI 公開 API](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/include/NoGraphicsAPI/NoGraphicsAPI.hpp)
- [WebGPU 仕様](https://www.w3.org/TR/webgpu/)
