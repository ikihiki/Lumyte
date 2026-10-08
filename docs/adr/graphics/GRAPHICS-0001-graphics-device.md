# ADR-GRAPHICS-0001: グラフィックデバイスとバックエンドの共通境界

- 状態: 採用
- 日付: 2026-10-07
- 更新日: 2026-10-08

## 背景

Lumyte のグラフィックスライブラリについて、描画バックエンドとエンジン側の境界、機能検出、デバイスの所有権を定める。[ADR-0002](../0002-repository-layout.md) に従い、公開 API は C#、対象は Windows、Linux、Browser、バックエンドは DirectX、Vulkan、WebGPU とする。

NoGraphicsAPI の、低レベル GPU 操作とアロケータ・Upload・遅延解放を分ける設計を参考にする。ただし、WebGPU に任意の GPU アドレスをシェーダーから間接参照する共通機構はない。共通デバイスは物理アドレスや binding を公開せず、リソースとコマンドの共通契約を提供する。

生成には native instance、adapter、validation、拡張機能や Browser の非同期 request など、backend 固有の条件がある。これらを共通 factory へ集めると、生成時の差が共通層へ広がる。生成後の操作を共有する interface と、固有の生成 API を分ける。

## 決定

### 責務と依存関係

生成済みデバイスの共通契約を `Lumyte.Graphics.IGraphicDevice` とし、各 backend の具象デバイスが直接実装する。共通 GraphicsDevice class や、内部 driver に処理を転送する device wrapper は置かない。

| 層 | 配置・名前空間 | 責務 |
| --- | --- | --- |
| GPU Core | `Lumyte.Graphics.Core`／`Lumyte.Graphics` | 生成済みデバイス、resource、pipeline、command、送信完了の共通契約 |
| Graphics Runtime | `Lumyte.Graphics.Runtime` | フレーム領域、引数の構築、完了後の再利用と遅延解放 |
| backend | `Lumyte.Graphics.DirectX`／`.Vulkan`／`.WebGPU`／`.Wgpu` | 固有の生成・機能検出、共通契約の直接実装、native 同期と binding への変換 |
| Renderer | エンジン側。配置は別途決定 | Mesh、Material、描画パス、必要なら Render Graph |
| アプリケーションの起動部分 | 利用側 | backend の選択、固有 API によるデバイス生成、終了処理 |

依存は Renderer → Runtime → Core、各 backend → Core とする。起動部分が選択した backend を直接参照し、具象デバイスを生成して Runtime／Renderer へ IGraphicDevice として渡す。Core は backend・ウィンドウ・シーン実装を参照せず、backend の列挙・選択・生成を担当しない。共通の生成プロジェクトや IGraphicsBackend interface も設けない。

表示先は Platform が生成し、Graphics はウィンドウ自体を所有しない。DirectX／Vulkan の C# 実装は対応する `.Native` パッケージを参照する。最初の実装候補である wgpu は既存 .NET binding を直接使い、Lumyte 独自の `.Native` は作らない。Browser に native C++ や実行時 native compiler を必須としない。

### 今回の範囲

本 ADR はデバイス境界の設計だけを扱う。実装プロジェクト、サンプル、GPU テストをこの ADR のために追加しない。公開宣言に登場する resource・Desc・pipeline・command・shader 成果物の型は、それぞれの役割を示す参照であり、詳細な型定義は後続で設計する。

- buffer: 型付き領域、CPU コピー、サイズと GPU コピー制約。
- texture／view: 画像、subresource と転送条件。
- sampler: sampling 設定と画像との適合。
- argument table／GPU 参照: 登録、要素参照と binding 解決。
- command buffer: 転送、バリア、RenderEncoder、pipeline、送信と完了。
- shader: Slang、成果物、オフライン／オンラインコンパイルと data layout。
- wgpu: 固有の生成条件と最初の実装サブセット。

### 公開 API 一覧

比較元は origin/main。Graphics API は未導入であり、以下は追加する公開契約の設計宣言。

```diff
+namespace Lumyte.Graphics
+{
+    // backend の具象デバイスが直接実装。生成 factory は含めない。
+    public interface IGraphicDevice : IDisposable
+    {
+        // 生成後に実際に利用可能な機能・上限の immutable snapshot。
+        // native handle、adapter identity、GPU address は含めない。
+        public DeviceCaps Caps { get; }
+
+        // 型付き allocation を生成。Count と用途を検証する。
+        // GPU address や CPU mapping を保証しない。
+        public Result<IGraphicsBuffer<T>> CreateBuffer<T>(BufferDesc<T> desc) where T : unmanaged;
+        // allocation をせず、T の格納 stride とコピー制約を数値で返す。
+        public BufferLayout<T> GetBufferLayout<T>() where T : unmanaged;
+
+        // サイズ、format、用途を検証し、所有する texture を生成。
+        public Result<IGraphicsTexture> CreateTexture(TextureDesc desc);
+        // texture と独立した、immutable な sampling 設定を生成。
+        public Result<Sampler> CreateSampler(SamplerDesc desc);
+        // 登録容量を検証。resource の転送や送信を行わない。
+        public Result<IArgumentTable> CreateArgumentTable(ArgumentTableDesc desc);
+
+        // backend 用成果物の対象 profile・ABI・必須機能を検証。
+        // shader compiler を暗黙に起動しない。
+        public Result<ShaderModule> CreateShader(ShaderArtifact artifact);
+        // shader、引数 layout、出力 format、固定状態を検証。
+        public Result<GraphicsPipeline> CreateGraphicsPipeline(GraphicsPipelineDesc desc);
+        // shader と引数 layout が一致する compute pipeline を生成。
+        public Result<ComputePipeline> CreateComputePipeline(ComputePipelineDesc desc);
+
+        // 単一スレッドで所有する記録を開始。初期契約は一つの汎用 queue。
+        public Result<CommandEncoder> CreateCommandEncoder();
+        // 一回限りの送信。resource を GPU 完了まで保持。
+        public Result<Submission> Submit(CommandBuffer commands);
+        // 子 resource または未完了 GPU 使用があれば InvalidOperationException。
+        // 暗黙に待機・送信しない。解放済みなら何もしない。
+        public void Dispose();
+    }
+
+    // 要求値ではなく、生成したデバイスで利用できる実値。
+    // backend 内部で構築し、利用者は変更しない。
+    public sealed class DeviceCaps
+    {
+        // 共通操作として実装・利用可能な機能だけを報告する。
+        public GraphicsFeatures Features { get; }
+        public ulong MaxBufferSize { get; }
+        public ulong MaxStorageBufferBindingSize { get; }
+        public uint MaxTextureDimension2D { get; }
+        public uint MaxColorAttachments { get; }
+        public uint MaxSampledTexturesPerStage { get; }
+        public uint MaxSamplersPerStage { get; }
+        public uint MaxUniformBuffersPerStage { get; }
+        public uint MaxStorageBuffersPerStage { get; }
+        public uint MaxComputeInvocationsPerWorkgroup { get; }
+        // byte 単位。正数。要素数や SizeInBytes の補正に使わない。
+        public uint CopyBufferOffsetAlignment { get; }
+        public uint CopyBufferSizeAlignment { get; }
+        public uint CopyBytesPerRowAlignment { get; }
+        public uint StorageBufferOffsetAlignment { get; }
+        // 既知の未対応 format は空の capability。不明値は引数例外。
+        // sampled／filterable／storage／renderable と sample count などを返す。
+        public FormatCapabilities GetFormatCapabilities(TextureFormat format);
+    }
+}
```

resource と command の戻り値、用途、Desc の詳細は後続の契約で確定する。`Result<T>`／GraphicsError は実行環境に起因する失敗を保持する共通の結果契約、GraphicsFeatures は共通で利用可能な機能を示す flags とする。これらの詳細なメンバー・エラーコードも別途具体化する。未実装の操作を native API が対応しているという理由だけで Caps に報告しない。

### backend 固有の生成 API

IGraphicDevice に static factory、共通の CreateDevice／CreateDeviceAsync、adapter 列挙や backend 自動選択を追加しない。共通 DeviceDesc、AdapterId、adapter 列挙 interface も要求しない。native instance、adapter、validation、拡張機能、生成順序と同期／非同期処理は各 backend が定める。

backend 固有の factory は具象デバイスを返す。起動部分が具体的な backend を選び、成功した instance を IGraphicDevice として渡す。別の device wrapper を生成せず、具象 instance 自身が resource と native device の所有権を管理する。

以下は wgpu の生成 API の形を示す例。他 backend に同じ factory 名・Desc・非同期処理を要求しない。具体的な adapter・instance・必須機能・validation の指定は wgpu の設計で確定する。

```diff
+namespace Lumyte.Graphics.Wgpu
+{
+    public sealed class WgpuDevice : IGraphicDevice
+    {
+        // wgpu 固有の生成条件を検証し、adapter／device の request を実行。
+        // 成功時だけ具象 device の所有権を返す。
+        public static ValueTask<Result<WgpuDevice>> CreateAsync(WgpuDeviceDesc desc, CancellationToken cancellationToken = default);
+        // IGraphicDevice の instance members は省略。
+    }
+
+    // backend 固有の生成条件。共通 Desc の継承は要求しない。
+    public sealed record WgpuDeviceDesc
+    {
+        // 診断専用。生成方法や互換性を変更しない。
+        public string? Label { get; init; }
+        // adapter・instance・features・limits・validation は wgpu 契約で設計。
+    }
+}
```

生成 API は入力を snapshot し、必須機能と必要な上限を満たさない場合に失敗する。要求値を黙って補正したり、別 backend へ暗黙に切り替えたりしない。失敗とキャンセルでは生成途中の native instance・adapter・device を回収し、不完全なデバイスを共通層へ公開しない。

既存 native instance／adapter の貸与を受け付ける backend は、その所有権と有効期間も固有契約に明記する。backend 固有の adapter を別 backend へ渡す共通経路は用意しない。

### Desc と入力の契約

共通 resource・command の Desc は `public sealed record XxxDesc`、設定は public init-only property とする。意味を推測できない項目は required とし、生成・記録 API で null、範囲、enum、用途を検証する。診断用 Label は動作・互換性を変えず、Desc 自体は GPU resource を所有しない。

配列や参照する設定は呼び出し時に snapshot し、呼び出し後の変更が GPU 記録に影響しないようにする。constructor は GPU 操作を行わず、生成・記録 API が検証する。Desc を黙って補正せず、GPU address、native handle、物理 binding slot を共通 Desc に含めない。backend 固有の生成 Desc の項目と native 相互運用は、その backend の契約で定める。

### 所有権、同期、スレッド

IGraphicDevice の Dispose は即時解放を要求し、子 resource と未完了 GPU 使用があれば拒否する。利用者は Submission の完了を観測し、子 resource を先に解放してからデバイスを解放する。CPU フレーム番号だけで GPU 完了と判定せず、Dispose に隠れた待機を持たせない。

記録中・送信済みコマンドが参照する引数や resource を変更、破棄、再利用してはならない。Runtime は frame 領域と遅延解放を管理するが、Core を直接使う利用者も同じ寿命と同期を守る。GPU 参照、buffer slice、view、内部 descriptor slot の再利用も送信完了に結び付ける。

CPU staging への CopyFrom／CopyTo と、GPU コピーの記録・送信は別操作とする。C# の CPU 書き込みだけで GPU 可視性が成立すると扱わない。利用者がバリアと依存を明示し、backend が native 同期へ変換する。WebGPU の使用範囲やパス境界では同時使用できない組合せを拒否する。

初期経路は一つの汎用 queue で送信順を保持する。Encoder と FrameContext は単一スレッドで所有し、device の生成・resource 生成・送信は利用側で直列化する。Browser の実行コンテキストの制約は backend が守る。並列記録と複数 queue は後続の拡張とする。

### エラーと Native 相互運用

共通の生成・送信操作では、確保失敗・未対応機能・Device Lost などを Result／GraphicsError で通知する。引数範囲や記録状態などの契約違反は C# の引数・状態例外とする。Device Lost は device 単位で保持し、以降の送信を拒否して未完了の待機をエラーで終了させる。Browser の非同期検証エラーも操作の成功だけで隠さない。初期化固有の結果とキャンセル条件は backend の生成 API に従う。

DirectX／Vulkan の C#／C++ 境界は C ABI とし、固定幅整数、明示レイアウトの POD、opaque handle、戻り値のエラーコードを使用する。C++ 例外を境界の外へ出さず、Managed object のアドレスを GPU address として扱わない。Span や pin した memory は呼び出し中だけ有効で、native 側が保存するデータは同期的にコピーする。GPU 使用期間の memory は専用の所有領域に置く。全宣言、handle・エラー文字列の寿命、shader の wire ABI は後続で設計する。

### プラットフォーム対応

| 環境 | backend | 方針 |
| --- | --- | --- |
| Windows | DirectX／Vulkan | 共通契約を実装。バージョンと最低機能は別途決定 |
| Linux | Vulkan | 共通契約を実装。NoGraphicsAPI 固有の拡張を必須にしない |
| Browser | WebGPU | 共通契約を実装。任意 GPU address への間接参照を要求しない |

対応方針は実装・実機検証の完了を表さない。shader は Slang を基準とし、オフライン／オンラインコンパイルと backend ごとの成果物・実データの受け渡しを後続で設計する。

## 検討した代替案

### 共通 backend factory と GraphicsDevice wrapper

利用側の生成コードを統一できるが、instance、adapter、拡張機能、validation と初期化順序の差を共通 Desc や driver へ持ち込む。具象型が IGraphicDevice を直接実装し、利用側の起動部分が固有 API を呼び出す方式を採用する。

### NoGraphicsAPI をそのままラップする

GPU pointer と小さな API を利用できるが、対象 backend と必須機能は Lumyte の DirectX／WebGPU を含む対象と一致しない。設計の参考とし、直接依存は採用しない。

### GPU pointer と bindless だけを共通 API にする

binding を省略できるが、WebGPU で同じ意味を保証できない。不透明な参照を共通の契約として設計し、物理表現は backend に任せる。

### Resource 管理と Renderer を一つに統合する

利用開始は簡単になるが、描画方式と GPU 操作の契約が結合する。Core、Runtime、Renderer の責務を分け、Core をオフスクリーン描画や compute にも利用できるようにする。

### Native API の Desc を共通層へ公開する

実装との対応は容易だが、native handle、layout、binding と platform 条件が利用側へ広がる。共通の操作は共通 Desc を使い、初期化固有の設定は backend の生成 API に閉じる。

## 結果と影響

- 生成後の操作を IGraphicDevice として共有でき、device wrapper の追加 instance と呼び出し転送を避けられる。
- アプリケーションの起動部分は選択する backend に依存する。backend を切り替えるには、その固有生成コードを用意する。
- 機能・上限と寿命の契約を共通化しながら、native instance／adapter／validation の差を各 backend に閉じられる。
- 共通 factory による自動選択や統一した adapter 列挙は提供しない。
- 本 ADR は設計文書であり、device・resource API の実装、GPU の性能、各環境での成立は未検証。

## 検証方針

今回は ADR の必須項目、公開 API のコメントと依存境界、参照先、番号・配置、Markdown lint を確認する。graphics 実装を追加しないため、アプリのビルドや GPU テストはこの文書変更の検証対象にしない。

実装時は backend 固有の生成 API で instance／adapter／機能・上限・入力 snapshot を検証し、失敗・キャンセルで native resource を回収することを確認する。生成後の同じ具象 instance が IGraphicDevice を直接実装し、共通 wrapper・factory を経由しないことを確認する。子 resource と GPU 使用が終了するまで device の解放を拒否し、Device Lost で待機を終了することも検証する。

buffer upload → compute → オフスクリーン描画 → readback の共通操作、binding 解決、shader 成果物の ABI と性能は後続の設計・実装で検証する。

## 別途決定する事項

- 各 backend 固有の生成 API、adapter／instance／validation とキャンセル・所有権。
- DeviceCaps の flags、format capability、Result／GraphicsError の全宣言。
- resource、argument table、command、shader と runtime の詳細。
- DirectX のバージョン、Vulkan／WebGPU の最低機能と上限。
- C ABI、P/Invoke、Native パッケージの RID とバージョニング。
- Swapchain と Platform の接続、acquire／present、リサイズと画面喪失。
- メモリ予算、frame 数、並列記録、複数 queue と Renderer の配置。

## 参考資料

- [ADR の書き方と運用](../0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](../0002-repository-layout.md)
- [開発環境](../0003-development-environment.md)
- [NoGraphicsAPI README（参照コミット固定）](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/README.md)
- [NoGraphicsAPI 設計比較](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/docs/no-graphics-api-comparison.md)
- [NoGraphicsAPI 公開 API](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/include/NoGraphicsAPI/NoGraphicsAPI.hpp)
- [WebGPU 仕様](https://www.w3.org/TR/webgpu/)
