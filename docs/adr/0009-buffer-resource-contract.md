# ADR-0009: バッファの利用 API とバックエンド実装契約

- 状態: 提案
- 日付: 2026-10-07

## 背景

[ADR-0004](0004-graphics-library.md) の Buffer と GPU データ参照を、生成、部分領域、Upload、コピー、Readback、解放まで実装できる契約にする。GPU アドレスに依存する設計では WebGPU と共通化できず、C# のメモリ配置を直接 GPU データとして扱うと Slang の target layout と食い違う。

本 ADR は BufferDesc の正本でもある。テクスチャへの転送は [ADR-0010](0010-texture-resource-contract.md)、シェーダーの schema と pack は [ADR-0005](0005-shader-compilation-and-data-interop.md) に従う。以下は拡張する共通 API の案であり、現在の UInt32 限定 API の仕様は [ADR-0008](0008-wgpu-first-backend.md) に残す。

## 決定

### 責務と公開型

共通型と staging の CPU コピー・読み出し API は `Lumyte.Graphics`、frame allocator と引数構築は `Lumyte.Graphics.Runtime` に置く。Core は backend や Ahjo に依存しない。利用者は共通 GraphicsDevice で生成し、バックエンドは内部 driver を通して処理する。Buffer は sealed な所有 class、BufferSlice と GpuReference は非所有の immutable value とする。利用者による Buffer の直接構築、Native handle、GPU アドレス、map pointer の取得は提供しない。

API は .NET の API review／API diff に倣い、namespace・型・メンバーを C# 宣言でまとめる。`+` は origin/main に対する追加 API、`-` は削除 API、無印は変更の文脈を表す。この PR の main には Graphics API がないため、掲載する宣言は追加として表示する。各ブロックは当該 ADR の対象メンバーの抜粋であり、実装コードではない。説明と検証条件は宣言の `//` コメントに記す。提案と実装済みの区別は ADR の状態と本文に従う。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed class GraphicsDevice : IDisposable
+    {
+        // Desc を snapshot して生成
+        // 確保失敗・未対応用途は GraphicsError
+        // 成功時だけ所有権を返す
+        public Result<Buffer> CreateBuffer(BufferDesc desc);
+
+        // CPU bytes を利用者が確保した idle な Memory=Upload／Usage=CopySource buffer へコピーする。
+        // CPU mapping とメモリコピーだけ。GPU コピー命令、queue write、送信は行わない。
+        // source.Length が destination.Length 以下。GPU 転送には別途 RecordCopyBuffer が必要。
+        public void CopyBuffer(BufferSlice destination, ReadOnlySpan<byte> source);
+
+        // 生成 serializer で target layout に pack して、既存 Upload buffer の CPU メモリへ書く。
+        // staging の確保、GPU 転送、送信をしない。schema／layout ID と参照依存を登録する。
+        // 参照を含む型も serializer で処理し、C# struct の memcpy をしない。
+        public void CopyBuffer<T>(BufferSlice destination, ShaderDataLayout<T> layout, ReadOnlySpan<T> values) where T : IShaderData;
+
+        // 登録済みの schema／target layout を持つ部分領域を参照
+        // 所属・用途・alignment・stride・要素数を検証
+        public GpuReference<T> CreateReference<T>(BufferSlice data) where T : IShaderData;
+
+        // 利用者が確保し、コピーを送信して GPU 完了を観測した Readback buffer だけを読む。
+        // Memory=Readback、Usage=CopyDestination、同じ Device、idle な範囲を要求する。
+        // staging 確保、GPU コピー、Submit、Submission の完了待機は行わない。
+        // backend の map／invalidate と CPU bytes への読み出しだけを非同期に実施する。
+        // 返す byte[] は source.Length の独立した格納 bytes。長さは .NET 配列上限以内。
+        public ValueTask<Result<byte[]>> ReadBufferAsync(BufferSlice source, CancellationToken cancellationToken = default);
+    }
+
+    public sealed class DeviceCaps
+    {
+        // ReadBufferAsync の source.Offset に要求する正の byte alignment。
+        public uint ReadBufferOffsetAlignment { get; }
+
+        // ReadBufferAsync の source.Length に要求する正の byte alignment。
+        public uint ReadBufferSizeAlignment { get; }
+    }
+
+    public sealed class Buffer : IDisposable
+    {
+        // 論理サイズと用途
+        // 物理配置・確保量は公開しない
+        public ulong SizeInBytes { get; }
+
+        // 論理サイズと用途
+        // 物理配置・確保量は公開しない
+        public BufferUsage Usage { get; }
+
+        // byte 単位の半開区間
+        // length > 0、offset ≤ size、length ≤ size - offset を要求
+        public BufferSlice Slice(ulong offset, ulong length);
+
+        // idle 時の即時解放
+        // idempotent
+        // 記録・GPU 使用・保持中の参照依存がある場合は InvalidOperationException
+        public void Dispose();
+    }
+
+    public readonly struct BufferSlice
+    {
+        // 元の Buffer と論理範囲
+        // 直接 constructor を公開しない
+        // default slice は無効
+        public Buffer Buffer { get; }
+
+        // 元の Buffer と論理範囲
+        // 直接 constructor を公開しない
+        // default slice は無効
+        public ulong Offset { get; }
+
+        // 元の Buffer と論理範囲
+        // 直接 constructor を公開しない
+        // default slice は無効
+        public ulong Length { get; }
+    }
+
+    public sealed class CommandEncoder : IDisposable
+    {
+        // 等しい長さの全範囲をコピー
+        // パス外、同じ Device、source CopySource／destination CopyDestination
+        public void RecordCopyBuffer(BufferSlice source, BufferSlice destination);
+    }
+}
+
```

### 明示的な Readback の手順

利用者が GraphicsDevice.CreateBuffer で Memory=Readback／Usage=CopyDestination の staging buffer を確保し、CommandEncoder で producer→CopySource の依存と RecordCopyBuffer を記録する。利用者が Finish／Submit し、そのコピーを含む Submission の完了を WaitAsync または IsCompleted で観測してから GraphicsDevice.ReadBufferAsync を呼ぶ。staging の pooling、必要サイズ、再利用、Dispose も利用者が管理する。

ReadBufferAsync は元の GPU buffer と lastWrite を受け取らず、コピー済みの staging BufferSlice だけを受け取る。staging の新規確保、GPU コピー、コマンド生成・送信、GPU 完了待機を内部で行わない。記録・送信の lease が残る場合は InvalidOperationException とし、未完了の使用が解消するまで内部で待たない。コピーを記録せずに読み出しても元の GPU buffer の内容を取得できる API ではなく、初期内容の保証も与えない。

CPU 読み出し開始時に対象を lease し、その間の Dispose、次のコピー先への利用、再利用を拒否する。成功時の byte[] は GPU resource の寿命から独立する。map／invalidate、CPU bytes の所有コピー、unmap と lease 解放は backend の読み出し責務であり、GPU コピーの自動化とは区別する。async は Browser の mapAsync を含む CPU mapping の完了に必要なもので、Submission の待機を代行する意味ではない。readback offset／length の mapping alignment は backend が報告する DeviceCaps.ReadBufferOffsetAlignment／ReadBufferSizeAlignment に照合する。両値は正数で、利用者は適合する staging slice を選ぶ。

### 明示的な Upload とコピー命令

CopyBuffer は既存 Upload buffer の CPU メモリへデータをコピーする操作とする。raw bytes は index／texture staging などに使用し、typed overload は生成 serializer で pack して schema／target layout の metadata を付ける。どちらも GPU buffer を更新しない。通常の DeviceLocal／Automatic buffer を destination に渡す要求は拒否し、queue write や staging の追加確保で代行しない。

利用者が Memory=Upload／Usage=CopySource の staging buffer と CopyDestination を持つ GPU buffer を確保する。CopyBuffer で CPU 側のデータを準備し、CommandEncoder.RecordCopyBuffer で staging→GPU の転送命令を記録し、Finish で CommandBuffer に確定して Submit する。Upload だけを暗黙に送信する helper は提供しない。FrameContext の自動 UploadBytes／Upload<T> はこの契約から外す。frame の引数構築と allocator の管理は別の責務として維持する。

CommandEncoder は CommandBuffer の記録 builder、CommandBuffer は Finish で確定した一回限りの送信単位とする。RecordCopyBuffer、RecordCopyBufferToTexture、RecordCopyTextureToBuffer、RecordCopyTexture は命令を記録するだけで CPU memcpy や GPU 実行を行わない。GPU コピーは利用者がその CommandBuffer を Submit した後に実行される。コピー命令の受け渡しに、利用者から見えないコマンドバッファや送信を作らない。

CPU mapping／flush／unmap は backend が CPU 可視性のために実装するが、これを GPU コピー命令と混同しない。mapping に非同期準備が必要な環境の async API は別途具体化し、同期 CopyBuffer の実装に GPU 転送を隠して代替しない。typed reference は shader 用途を持つコピー先の登録済み領域に対して作り、CopySource のみの staging 領域を直接 shader 引数にしない。


### BufferDesc と上限

全 Desc に `string? Label = null` を持たせる。ラベルは診断専用。値は init-only とする。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record BufferDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public BufferDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // 正数、DeviceCaps.MaxBufferSize 以下
+        // 論理 byte 数
+        public required ulong SizeInBytes { get; init; }
+
+        // CopySource／CopyDestination／ShaderRead／ShaderWrite／Index
+        // None と未知 bit は拒否
+        public required BufferUsage Usage { get; init; }
+
+        // Automatic／DeviceLocal／Upload／Readback
+        // 配置のヒント
+        public MemoryPreference Memory { get; init; } = MemoryPreference.Automatic;
+    }
+}
```

ShaderWrite と ShaderRead は併用可能。Upload は CopySource のみ、Readback は CopyDestination のみを許可し、直接 shader／index 利用を要求する場合は Automatic／DeviceLocal を使う。これにより WebGPU の map 用途の制約を共通化する。Memory は公開 map pointer の権限ではない。Upload／Readback は生成時点で CPU 可視の staging resource として実現する。backend は hint を理由に要求用途を変更しない。Upload は CopyBuffer の実行時に別 staging を確保したり queue write を発行したりしない。Readback は生成時点で CPU 読み出しを実現する resource を確保し、ReadBufferAsync の呼び出し時に別 staging を作ってコピーする fallback は禁止する。必要な配置を実現できない場合は CreateBuffer を失敗させる。

DeviceCaps に `ulong MaxBufferSize`、`ulong MaxStorageBufferBindingSize`、`uint CopyBufferOffsetAlignment`、`uint CopyBufferSizeAlignment`、`uint StorageBufferOffsetAlignment`、`uint MaxStorageBuffersPerStage` を報告する。各 alignment は正数。GPU コピー命令は offset と length をそれぞれの alignment に照合し、index は format の要素サイズに照合する。型付き storage 参照は binding offset、target alignment／stride、最大 binding size にも従う。

CreateBuffer の論理 size 自体には copy alignment を要求しない。backend が内部で切り上げる場合も論理末尾をアクセス可能にしない。切り上げの overflow と物理 API 上限を検証する。サイズを小さく補正して成功を返さない。

### 部分領域、データ型、競合

Slice は allocation を作らず、寿命も延ばさない。Runtime の suballocation は alignment と世代を保持し、再利用後の typed reference を拒否する。GpuReference の整数化、serialization、任意 token の生成は提供しない。

typed CPU copy の schema／layout ID は物理 buffer 全体ではなく対象領域に記録する。登録のない bytes から CreateReference は作れない。型付きコピーは source の登録済み範囲全体と一致し、destination の alignment・target layout が適合する場合だけメタデータと参照依存を伝播する。部分コピーまたは raw bytes による上書きは重複する登録を失効させる。古い GPU 参照を新しいデータの型として再利用しない。

同じ Buffer 内のコピーは半開区間が重ならない場合のみ許可する。重複は memmove として実装せず、記録前に拒否する。shader の範囲外 index は利用者のシェーダー契約であり、backend に全 GPU データの CPU 検査は要求しない。

### バックエンドが実装するもの

内部 driver の以下の操作は概念的な実装契約であり、利用側へ公開する API ではない。現行 IGraphicsDriver の全機能が実装済みという意味ではない。

```diff
+namespace Lumyte.Graphics.Implementation
+{
+    // 内部操作の設計用宣言。Token／Range／Plan などは非公開の概念型。
+    // 正式な driver signature、結果／診断型、C ABI の layout は別途具体化する。
+    // この表示は現行 IGraphicsDriver の実装を変更しない。
+    internal interface IBufferBackendContract
+    {
+        // ネイティブ確保、失敗時 rollback、Device 所属・世代、logical size／usage の保持、idle 時の解放
+        BufferToken CreateBuffer(BufferDesc normalizedDesc);
+
+        // ネイティブ確保、失敗時 rollback、Device 所属・世代、logical size／usage の保持、idle 時の解放
+        void DestroyBuffer(BufferToken token);
+
+        // 実際に実現する用途、コピー／storage／CPU 読み出し alignment、容量を報告
+        // native の制限を超えた値を返さない
+        BufferCapabilities GetBufferCapabilities();
+
+        // lease と有効性を検証し、Slang の反射と一致する実データ参照へ解決
+        // GPU address／descriptor／offset を利用側へ返さない
+        ResolvedBufferReference ResolveBufferReference(BufferToken token, BufferRange range, ShaderSchema schema, BindingPlan bindingPlan);
+
+        // 全入力の検証後に記録
+        // 使用 range、アクセス、参照依存を記録し、native failure は Encoder を Faulted にする
+        void RecordCopyBuffer(EncoderToken encoder, BufferRange sourceRange, BufferRange destinationRange);
+
+        // 利用者が確保した idle な Upload buffer の CPU memory に bytes をコピーする。
+        // map／flush／unmap は CPU 可視性の処理。queue write／GPU copy／Submit を行わない。
+        void CopyBufferCpu(BufferRange destination, ReadOnlySpan<byte> bytes);
+
+        // 利用者がコピーと GPU 完了観測を済ませた既存 Readback buffer のみを map／invalidate して読む。
+        // コマンド生成・コピー・送信・GPU 完了待機・追加 staging 確保は行わない。
+        // 未完了の GPU 使用は待機で隠さず拒否。CPU コピー後に unmap と lease 解放。
+        ValueTask<ReadOnlyMemory<byte>> ReadStagingAsync(BufferRange range);
+
+        // ADR-0004 の Barrier を各 backend のアクセス遷移へ変換
+        // 同一キューの送信順だけで memory visibility を保証したと扱わない
+        void ApplyBufferDependency(BufferRange range, ResourceAccess producer, ResourceAccess consumer);
+    }
+}
```

| backend | 対応方法と注意点 |
| --- | --- |
| managed wgpu | Ahjo binding の Buffer と queue／copy／map を使用。storage binding + offset、map 用途制約、4-byte copy alignment と device limits を検証。独自 .Native は不要 |
| DirectX | resource／heap と upload／readback heap、状態遷移、完了 fence。address または descriptor の pack は Native と Slang library 内部 |
| Vulkan | buffer／allocation、memory type、非 coherent 範囲の flush／invalidate、アクセス依存。device address は使用する profile が要求する場合だけ内部で採用 |
| Browser WebGPU | WebGPU buffer／mapAsync と promise 完了。JS／Wasm の入力は所有コピーを作り、ホスト Native に依存しない |

DirectX／Vulkan の C ABI には `uint64` の size／offset／length、`uint32` の usage／memory と opaque token を渡す。Native pointer の数値を token にしない。CPU bytes の pointer と length は呼び出し中にコピーし、GPU 完了まで caller の pin を保持しない。完全な ABI 宣言・エラー文字列の所有規約は別途定める。

### 所有権、同期、エラー

記録が使う buffer と参照依存は CommandEncoder → CommandBuffer → Submission に lease を移し、GPU 完了時に解放する。未送信のコマンドを破棄した場合も解放する。CPU 書き込み・Dispose・suballocation 再利用は使用中に拒否する。FrameContext の領域は EndFrame で登録した完了まで再利用しない。

Encoder と FrameContext は単一スレッド、Device 操作は利用側で直列化する。Submission 待機のキャンセルは利用者が送信した GPU コピーを取消さない。利用者は完了を改めて観測してから staging を解放・再利用する。CPU 読み出しのキャンセルは新しいコピーを発生させず、mapping を安全に終了して unmap／lease 解放を済ませてから OperationCanceledException を返す。読み出し API は staging 自体を Dispose しない。DeviceLost は未完了要求を GraphicsError で終了させ、使用中 handle を backend の安全な teardown 手順で解放する。

範囲・enum・用途・別 Device は引数例外、破棄済みは ObjectDisposedException、記録状態・使用中変更は InvalidOperationException。有効な要求に対する未対応機能、OutOfMemory、DeviceLost は Result の GraphicsError。Submission 待機と CPU 読み出しの cancellation は OperationCanceledException。native エラーを成功値へ変換しない。

### 利用例

以下は提案 API の Upload。RequireSuccess は利用者の Result エラー処理。stagingBytes は pack 後の必要サイズ、gpuBuffer は CopyDestination と必要な shader 用途を持つ。

```csharp
using var upload = RequireSuccess(device.CreateBuffer(new BufferDesc {
    SizeInBytes = stagingBytes,
    Usage = BufferUsage.CopySource,
    Memory = MemoryPreference.Upload,
}));
// CPU 側で pack／コピーする。ここでは GPU buffer は変化しない。
device.CopyBuffer(upload.Slice(0, stagingBytes), dataLayout, values);
using var uploadEncoder = RequireSuccess(device.CreateCommandEncoder());
uploadEncoder.RecordCopyBuffer(upload.Slice(0, stagingBytes), gpuBuffer.Slice(0, stagingBytes));
using var uploadCommands = uploadEncoder.Finish();
var uploaded = RequireSuccess(device.Submit(uploadCommands));
// 次の work と依存を構築するか、CPU で完了を観測する。staging は完了まで再利用しない。
await uploaded.WaitAsync();
```


以下は提案 API。RequireSuccess は利用者側で Result の失敗を処理して成功値を取り出す処理を表す。buffer は CopySource 用途、byteCount はコピーと読み出しの alignment を満たす。producerToCopySource は利用者が前の書き込みからコピーへの依存を構築したものとする。

```csharp
// 1. 利用者が staging buffer を確保する。必要なら既存 buffer を再利用できる。
using var staging = RequireSuccess(device.CreateBuffer(new BufferDesc {
    SizeInBytes = byteCount,
    Usage = BufferUsage.CopyDestination,
    Memory = MemoryPreference.Readback,
}));

// 2. 利用者が依存関係とコピー命令を記録する。
using var encoder = RequireSuccess(device.CreateCommandEncoder());
encoder.Barrier(producerToCopySource);
encoder.RecordCopyBuffer(buffer.Slice(0, byteCount), staging.Slice(0, byteCount));
using var commands = encoder.Finish();

// 3. 利用者が送信する。他の work と同じ送信にまとめることもできる。
var copyCompletion = RequireSuccess(device.Submit(commands));

// 4. 利用者が GPU 完了を観測する。この例はキャンセルせず完了まで待つ。
await copyCompletion.WaitAsync();

// 5. コピー済み staging から CPU bytes を読む。ここでは GPU work を作らない。
var bytes = RequireSuccess(await device.ReadBufferAsync(
    staging.Slice(0, byteCount), cancellationToken));
// CPU 読み出しと GPU 使用の完了後に、利用者が staging を解放・再利用する。
```

## 検討した代替案

### Readback helper が確保・コピー・送信・待機をまとめて実行する

staging の再利用、コピーと他の work のまとめ方、送信と完了観測の時期を利用側が選べなくなる。各操作を明示し、ReadBufferAsync は CPU 読み出しに限定する。

### public map／GPU address を基本経路にする

CPU 可視 heap と任意アドレスを前提にし、Browser と共通化できない。staging と不透明参照を基本にする。

### 任意の unmanaged struct を GPU schema として扱う

layout、padding、参照の pack を検証できない。raw bytes と登録済みの Slang データ型を分ける。

## 結果と影響

- 用途と部分領域を共通 API で検証し、backend の物理表現を隠せる。
- typed metadata、参照依存、lease、staging の管理コストが生じる。
- 上限と alignment は backend ごとに確認が必要で、すべてのサイズをコピーできるとは保証しない。
- 初期実装は raw CPU CopyBuffer と明示的な RecordCopyBuffer／CommandBuffer 送信を採用する。Slang 生成型を使う overload と一般的な metadata は未実装。UInt32 ReadBuffer は明示的な staging の CPU 読み出し契約への移行対象。

## 検証方針

共通 API のみを使うテストで size／range overflow、用途、alignment、同一 buffer の重複コピー、別 Device、破棄後参照を拒否する。Upload → Compute → Barrier → Copy → Readback の bytes と、非 coherent memory の可視性を確認する。typed metadata の伝播／失効、世代更新、未送信破棄、in-flight Dispose、読み出し中の staging lease、未完了コピーでの読み出し拒否、キャンセル／DeviceLost 時の map／lease 回収を backend ごとに検証する。CopyBuffer が CPU memory のみを変更し、RecordCopyBuffer の Finish 前／Submit 前に GPU work が生じないことを確認する。Upload の queue write と隠れた staging／送信がないこと、記録後の CPU 上書きを拒否することも検証する。ReadBufferAsync が staging 確保・GPU Copy・Submit・GPU 待機を実行しないことも backend の呼び出し記録で検証する。初期の raw CPU copy と明示的な transfer は ADR-0008 の実装で検証する。一般的な schema／metadata と追加契約は今回未検証。

## 別途決定する事項

- Runtime allocator の容量・拡張・fragmentation 方針、巨大 Readback の分割 API。
- uniform 専用／indirect／vertex input 用途、永続 map、複数キュー、外部 memory import。

## 参考資料

- [グラフィックス共通契約](0004-graphics-library.md)
- [Slang とデータ受け渡し](0005-shader-compilation-and-data-interop.md)
- [wgpu 初期実装](0008-wgpu-first-backend.md)
- [WebGPU buffers](https://www.w3.org/TR/webgpu/#buffers)
