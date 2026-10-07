# ADR-0008: .NET バインディングによる最初の wgpu バックエンド

- 状態: 採用
- 日付: 2026-10-07

## 背景

最初のグラフィックス実装は wgpu を使用し、.NET にあるバインディングを直接参照する。Lumyte の C++ ラッパーや `.Native` プロジェクトは作らない。先にデバイス生成、Compute、RenderEncoder、GPU 完了、読み戻しを実行して、設計の成立を確認する。

[ADR-0004](0004-graphics-library.md)〜[ADR-0007](0007-graphics-descriptors.md) は広い共通 API の提案であり、全機能を初回実装の完了条件にはしない。本 ADR は最初のバックエンド選択と動く初期契約を採用する。共通 API の初期サブセットと未実装の広い契約を区別する。

## 決定

### バインディングと配置

`src/Graphics/Lumyte.Graphics.Wgpu` に C# の `Lumyte.Graphics.Wgpu` を配置する。`Ahjo.Wgpu` 0.7.0 を直接 PackageReference し、既存の wrapper と必要な raw binding を利用する。wgpu-native の実行ファイルは第三者パッケージの推移的依存 `Ahjo.Wgpu.Native` が RID ごとに供給する。Lumyte の `.Native` プロジェクト、C++、独自 P/Invoke 宣言、ネイティブビルドは追加しない。

バインディングは pre-1.0 のためバージョンを固定する。既存 wrapper の handle struct はコピーと二重解放を許してしまうので、公開 API は所有権を検証する managed class に包む。Native handle とアドレスは internal に留める。

最初はネイティブプロセスで動くヘッドレス実装とする。wgpu-native が内部で Vulkan／DirectX などを選択する。Browser の WebGPU 接続は別実装であり、wgpu-native のパッケージを Browser に配布できると扱わない。既存の DirectX／Vulkan `.Native` の設計は将来の独立バックエンドに残すが、最初の実装には使用しない。

### 初期契約と公開 API

公開する利用 API の名前空間は `Lumyte.Graphics` とし、所有型は managed class とする。テストとサンプルはこの共通 API のみを使用し、バックエンドのクラスや binding を直接参照しない。初期機能と Desc の範囲は以下に限定し、ADR-0004〜0007 の未実装部分を公開しない。

| プロジェクト | 依存と責務 |
| --- | --- |
| `Lumyte.Graphics.Core` | binding／バックエンドへの依存なし。公開 resource、Desc、GraphicsDevice、CommandEncoder、RenderEncoder、Submission と内部 driver 契約 |
| `Lumyte.Graphics.Wgpu` | Core と Ahjo.Wgpu に依存。内部 driver が managed backend object を保持し、共通契約を wgpu に変換 |
| `Lumyte.Graphics` | Core と対応 backend に依存する生成層。`Graphics.CreateDevice(GraphicsBackend)` で選択し、共通 GraphicsDevice を返す |
| テスト／サンプル | 生成層を ProjectReference し、Core の共通型だけで操作。wgpu／Ahjo の直接参照は禁止 |

Core の wrapper は内部 driver と backend object を非公開で保持する。GPU 参照も不透明な内部データを保持し、利用者が backend 型や物理表現へ変換する API は提供しない。生成層から backend への依存は構成のためだけに使う。frame allocator／deferred deletion を扱う Runtime とは別の責務とする。

`GraphicsBackend` は現在 `Wgpu` のみ。`Graphics.CreateDevice(GraphicsBackend backend = GraphicsBackend.Wgpu)` は未定義値を ArgumentOutOfRangeException で拒否する。バックエンドの `WgpuBackend.CreateDevice()` は生成層用の接続点であり、サンプル／テストからは呼ばない。

API は .NET の API review／API diff に倣い、namespace・型・メンバーを C# 宣言でまとめる。`+` は origin/main に対する追加 API、`-` は削除 API、無印は変更の文脈を表す。この PR の main には Graphics API がないため、掲載する宣言は追加として表示する。各ブロックは当該 ADR の対象メンバーの抜粋であり、実装コードではない。説明と検証条件は宣言の `//` コメントに記す。提案と実装済みの区別は ADR の状態と本文に従う。

```diff
+namespace Lumyte.Graphics
+{
+    // 現行のデバイス生成 API が受け付けるバックエンド。
+    public enum GraphicsBackend { Wgpu }
+
+    // CPU 可視 Upload／Readback と通常の GPU resource を区別する。
+    public enum MemoryPreference { Automatic, Readback, Upload }
+
+    // リソースの所有基底型。GPU 使用中の解放は拒否する。
+    public abstract class GpuResource : IDisposable
+    {
+        public void Dispose();
+    }
+
+    public static class Graphics
+    {
+        // blocking の Instance／Adapter／Device 生成
+        // バインディングの初期化失敗は例外で返す
+        public static GraphicsDevice CreateDevice(GraphicsBackend backend = GraphicsBackend.Wgpu);
+    }
+
+    public sealed class GraphicsDevice : IDisposable
+    {
+        // サイズ・用途・範囲を検証
+        // 初期 buffer のサイズは 4-byte の倍数
+        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc) where T : unmanaged;
+        // 確保せず T の解決済みレイアウトと要素単位のコピー制約を取得する。
+        public BufferLayout<T> GetBufferLayout<T>() where T : unmanaged;
+
+        // 初期の登録済みデータ schema は UInt32 配列のみ
+        // 非所有・型付きの不透明参照を作る
+        public GpuReference<T> CreateReference<T>(BufferSlice<T> data) where T : unmanaged;
+
+        // DLL の埋め込み WGSL を読み込む
+        // resource 不在は ArgumentException
+        // stream は内部で解放
+        public ShaderModule CreateShader(System.Reflection.Assembly assembly, string resourceName);
+
+        // 一つの論理 RWStructuredBuffer<uint> を使う Compute 引数
+        // binding と実データ参照の解決は library 内部
+        public ComputePipeline CreateComputePipeline(ComputePipelineDesc desc);
+
+        // single-sample、単一 mip／layer の RGBA8Unorm オフスクリーン target
+        public Texture CreateTexture(TextureDesc desc);
+
+        // rootless vertex／fragment、triangle-list、blend／depth 無効の graphics pipeline
+        public GraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc);
+
+        // 一回限りの送信
+        // 二重送信は Native に渡す前に拒否
+        public Submission Submit(CommandBuffer commands);
+    }
+
+    // backend が T の格納 stride と GPU コピー制約を解決した値型。constructor は非公開。
+    public readonly struct BufferLayout<T> where T : unmanaged
+    {
+        // host 要素の byte 数と、この backend の buffer 内で一要素が占める stride。
+        public ulong ElementSizeInBytes { get; }
+        public ulong ElementStrideInBytes { get; }
+        // native GPU copy の制約。いずれも正数。
+        public ulong CopyOffsetAlignmentInBytes { get; }
+        public ulong CopySizeAlignmentInBytes { get; }
+        // 要素単位でコピー offset／count が満たす必要のある最小の倍数。
+        // respective byte alignment / gcd(byte alignment, ElementStrideInBytes)。
+        public ulong CopyOffsetAlignmentInElements { get; }
+        public ulong CopyCountAlignment { get; }
+        // checked(count * ElementStrideInBytes)。overflow は OverflowException。
+        // default layout は未解決として InvalidOperationException。
+        public ulong GetSizeInBytes(ulong count);
+    }
+
+    public sealed record BufferDesc<T> where T : unmanaged
+    {
+        // 正数の要素数。初期 backend は byte 換算後のサイズに 4-byte alignment を要求。
+        public required ulong Count { get; init; }
+        public required BufferUsage Usage { get; init; }
+        public MemoryPreference Memory { get; init; } = MemoryPreference.Automatic;
+    }
+
+    // T は数値型や unmanaged struct。byte は raw storage、Slang 互換性は別途検証する。
+    // factory が返す具象 backend 自身が実装し、直接 allocation の所有権を持つ。
+    public interface IGraphicsBuffer<T> : IDisposable where T : unmanaged
+    {
+        // backend の解決済み数値。論理サイズは Layout.GetSizeInBytes(Count)。
+        public BufferLayout<T> Layout { get; }
+        // 要素数。
+        public ulong Count { get; }
+        public ulong SizeInBytes { get; }
+        public BufferUsage Usage { get; }
+        public MemoryPreference Memory { get; }
+        // 要素単位の半開区間。count > 0、offset <= Count、count <= Count - offset。
+        // allocation を作らず、元 buffer の寿命を延ばさない。
+        public BufferSlice<T> Slice(ulong offset, ulong count);
+        // idle な Upload memory へ source.Length 要素をコピー。余りは変更しない。
+        // GPU 命令、queue write、staging 確保、送信は行わない。
+        public void CopyFrom(ReadOnlySpan<T> source);
+        // GPU 完了を観測した idle な Readback の Count 要素を caller memory にコピー。
+        // destination.Length >= Count。余りは変更せず、GPU コピー・送信・完了待機をしない。
+        public void CopyTo(Span<T> destination);
+        // idle 時の解放、idempotent。lease 中は InvalidOperationException。
+        public void Dispose();
+    }
+
+    // 非所有の値型。直接 constructor は非公開。default は無効。
+    public readonly struct BufferSlice<T> where T : unmanaged
+    {
+        public IGraphicsBuffer<T> Buffer { get; }
+        // Offset と Count は要素単位。byte 換算は checked で検証する。
+        public ulong Offset { get; }
+        public ulong Count { get; }
+        public ulong OffsetInBytes { get; }
+        public ulong SizeInBytes { get; }
+        // 元 buffer と同じ CPU コピー契約をこの範囲に適用。default は ArgumentException。
+        public void CopyFrom(ReadOnlySpan<T> source);
+        public void CopyTo(Span<T> destination);
+    }
+
+    public sealed class ComputePipeline : GpuResource
+    {
+        // 一つの論理 RWStructuredBuffer<uint> を使う Compute 引数
+        // binding と実データ参照の解決は library 内部
+        public ShaderArguments CreateArguments(GpuReference<uint> data);
+    }
+
+    public sealed class CommandEncoder : IDisposable
+    {
+        // 単一 queue の Compute とコピー
+        // パス中は禁止
+        public void Dispatch(ComputePipeline pipeline, ShaderArguments arguments, uint x, uint y = 1, uint z = 1);
+
+        // 単一 queue の Compute とコピー
+        // パス中は禁止
+        public void RecordCopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination) where TSource : unmanaged where TDestination : unmanaged;
+
+        // 一つの color attachment の Clear／Load、Store／Discard
+        public RenderEncoder BeginRenderPass(RenderPassDesc desc);
+
+        // 一回限りの送信
+        // 二重送信は Native に渡す前に拒否
+        public CommandBuffer Finish();
+
+        // 256-byte pitch の color コピー、完了後の caller 所有 CPU memory へのコピー
+        // サンプルは bytes として pixel を検証
+        public void RecordCopyTextureToBuffer<T>(Texture source, IGraphicsBuffer<T> destination, uint bytesPerRow) where T : unmanaged;
+    }
+
+    public sealed class Texture : GpuResource
+    {
+        // single-sample、単一 mip／layer の RGBA8Unorm オフスクリーン target
+        public TextureView CreateView();
+    }
+
+    public sealed class RenderEncoder : IDisposable
+    {
+        // 一つの color attachment の Clear／Load、Store／Discard
+        public void SetPipeline(GraphicsPipeline pipeline);
+
+        // パス状態、範囲、index 用途を検証する記録 API
+        public void SetViewport(Viewport viewport);
+
+        // パス状態、範囲、index 用途を検証する記録 API
+        public void SetScissor(Scissor scissor);
+
+        // パス状態、範囲、index 用途を検証する記録 API
+        public void SetIndexBuffer<T>(BufferSlice<T> indices, IndexFormat format) where T : unmanaged;
+
+        // パス状態、範囲、index 用途を検証する記録 API
+        public void Draw(uint vertexCount, uint instanceCount = 1);
+
+        // パス状態、範囲、index 用途を検証する記録 API
+        public void Draw(DrawDesc desc);
+
+        // パス状態、範囲、index 用途を検証する記録 API
+        public void DrawIndexed(IndexedDrawDesc desc);
+    }
+
+    public sealed class Submission
+    {
+        // callback を poll して実 GPU 完了を確認
+        // 待機キャンセルは GPU 処理を取り消さない
+        public bool IsCompleted { get; }
+
+        // callback を poll して実 GPU 完了を確認
+        // 待機キャンセルは GPU 処理を取り消さない
+        public void Wait(CancellationToken cancellationToken = default);
+
+        // callback を poll して実 GPU 完了を確認
+        // 待機キャンセルは GPU 処理を取り消さない
+        public ValueTask WaitAsync(CancellationToken cancellationToken = default);
+    }
+}
```

wgpu の具象 allocation が直接 IGraphicsBuffer<T> と IBufferBackendContract を実装する。GetBufferLayout<T>() の ElementStrideInBytes と BufferDesc<T>.Count から SizeInBytes を checked で算出し、slice は要素単位とする。T は数値型や unmanaged struct で、UInt32 以外の shader schema は未対応。要素ごとの object と typed facade の追加確保をしない。

初期の CPU CopyFrom／CopyTo は元 buffer のサイズが int.MaxValue 以下の範囲に限定する。初期 MemoryPreference は Automatic／Readback／Upload。Upload は CopySource のみ、Readback は CopyDestination のみで、CPU mapping のみを許可する。初期の Desc は実装するフィールドだけを持つ。TextureDesc は Width／Height、RenderPassDesc は Target／Load／Store／ClearValue、GraphicsPipelineDesc／ComputePipelineDesc は Shader と entry point を持ち、詳細な固定状態は省略する。サポートしない形式や状態を受け取って黙って無視する API は提供しない。全 format、depth／stencil、MSAA resolve、複数 attachment、Sampler、生成 serializer、一般的な ShaderArtifact、オンライン Slang compiler と共通の Result API はまだ未実装であり、ADR-0005／0007 の全仕様を満たしたとは扱わない。

### GPU 参照、所有権、同期

GpuReference は元の managed resource と範囲を内部で保持し、整数変換、実アドレス、binding index の公開と serialization を持たない。失効した resource、別 Device の resource、default の参照、未対応の型と Storage の alignment／size を検証する。初期の Slang schema の WGSL binding は library 内部で所有し、利用者は参照だけを渡す。

CommandEncoder が記録に使った resource を lease し、Finish で CommandBuffer、Submit で Submission へ引き継ぐ。完了確認で lease を解放する。記録破棄時も解放する。ShaderArguments は pipeline と参照先 Buffer を、その破棄まで lease する。lease 中の Dispose と CPU 書き込み、Device の子 resource／Encoder／Submission が残った状態の Device.Dispose は拒否する。

Dispose は idempotent とし、End は一回限りの状態変更とする。親 Encoder の破棄は開いた pass を無効化する。操作は Device の gate で直列化し、Native error callback は所有 Device の診断 queue へ書き込み、managed 例外を Native 境界外へ出さない。queue 完了の失敗は待機で通知する。未完了の callback storage はキャンセルで解放しない。利用者はキャンセル後も Submission の完了を確認する。

初期版では resource は明示的に Dispose、Submission は Wait／WaitAsync または IsCompleted で完了まで poll する。最終フレームを含め、完了を観測せず Device を破棄しない。一般的な Runtime の deferred deletion とフレーム allocator は後続実装とする。

### リソース設計の拡張範囲

[ADR-0009](0009-buffer-resource-contract.md)、[ADR-0010](0010-texture-resource-contract.md)、[ADR-0011](0011-sampler-resource-contract.md) が buffer／texture／sampler の詳細な共通契約を提案する。初期 driver は IBufferBackendContract の instance を生成し、ICommandBufferBackendContract による命令操作と分離する。具象 buffer instance が native allocation と所属・lease を保持し、buffer 側は CPU CopyFrom／CopyTo、コマンド側は GPU コピーの記録・送信を持つ。Barrier の利用者向け拡張は ADR-0004 の提案に残す。本 ADR の初期実装の範囲や検証済みの機能は、それらの提案だけでは拡張されない。移行時もテストとサンプルは共通 API のみを使用する。

### シェーダーと検証

サンプルの `.slang` を正本とし、mise の固定 Slang から offline に WGSL を生成する。ビルド時に共有 MSBuild targets が obj 内へ生成し、サンプル／テスト DLL の EmbeddedResource に格納する。生成 WGSL は Git に含めず、別ファイルとして配布しない。実行時は DLL の manifest resource を読み出し、Slang compiler を必要としない。コンパイラはビルド時にのみ必要とする。

テストで consumer と Core の assembly reference に wgpu／Ahjo がないことも確認する。初期 Upload も利用者が Upload buffer を確保し、CPU CopyFrom／CopyTo 後に RecordCopyBuffer を記録して CommandBuffer を送信する。Ahjo Queue.WriteBuffer による隠れた転送は使用しない。CPU 上書きと GPU 転送の分離、記録後の CPU 書き込み拒否を共通 API で検証する。GPU integration test は adapter 不在を成功や skip とせず、実行環境を準備して実行する。Linux の lavapipe で Slang Compute の UInt32 配列、通常／indexed triangle の color readback、pass 状態、二重送信、resource lifetime、破棄、別 Device と invalid range を検証する。Windows／Browser の動作は今回の検証結果に含めない。

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
- 現在の API と Desc は限定的であり、共通契約の拡張や一般化で変更する可能性がある。
- lavapipe の成功は実 GPU の性能や全プラットフォームの互換性を保証しない。

## 参考資料

- [フォルダ構成](0002-repository-layout.md)
- [グラフィックス共通契約](0004-graphics-library.md)
- [Slang とデータ受け渡し](0005-shader-compilation-and-data-interop.md)
- [RenderEncoder](0006-render-encoder.md)
- [Desc 型](0007-graphics-descriptors.md)
- [wgpu 実装](../../src/Graphics/Lumyte.Graphics.Wgpu/WgpuDevice.cs)
- [Ahjo.Wgpu](https://github.com/pekkah/Ahjo-Wgpu)
