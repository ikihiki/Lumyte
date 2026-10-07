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
+        // 登録済みの schema／target layout を持つ部分領域を参照
+        // 所属・用途・alignment・stride・要素数を検証
+        public GpuReference<T> CreateReference<T>(BufferSlice data) where T : IShaderData;
+    }
+
+    public sealed class Buffer : IDisposable
+    {
+        // idle な Upload の CPU memory に書く。残りの領域は変更しない。GPU 命令・確保・送信は行わない。
+        public void CopyFrom(ReadOnlySpan<byte> source);
+
+        // 完了を観測した idle な Readback の全対象範囲を caller memory に読む。
+        // destination は対象サイズ以上。余りは変更しない。GPU コピー・送信・待機は行わない。
+        public void CopyTo(Span<byte> destination);
+
+        // 生成 serializer で target layout に pack。schema／layout ID と参照依存を登録する。
+        // staging 確保・GPU 転送・送信は行わず、参照を含む C# struct の memcpy はしない。
+        public void CopyFrom<T>(ReadOnlySpan<T> values, ShaderDataLayout<T> layout) where T : IShaderData;
+
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
+        // idle な Upload の CPU memory に書く。残りの領域は変更しない。GPU 命令・確保・送信は行わない。
+        public void CopyFrom(ReadOnlySpan<byte> source);
+
+        // 完了を観測した idle な Readback の全対象範囲を caller memory に読む。
+        // destination は対象サイズ以上。余りは変更しない。GPU コピー・送信・待機は行わない。
+        public void CopyTo(Span<byte> destination);
+
+        // 生成 serializer で target layout に pack。schema／layout ID と参照依存を登録する。
+        // staging 確保・GPU 転送・送信は行わず、参照を含む C# struct の memcpy はしない。
+        public void CopyFrom<T>(ReadOnlySpan<T> values, ShaderDataLayout<T> layout) where T : IShaderData;
+        // 上記はこの slice の範囲だけを対象とする。default は ArgumentException。
+
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
+}
+
```

### 明示的な Readback の手順

利用者が Memory=Readback／Usage=CopyDestination の staging buffer を確保し、コマンド API の Barrier と RecordCopyBuffer を記録する。利用者が Finish／Submit し、コピーを含む Submission の完了を観測した後、CPU の destination 領域を用意して stagingSlice.CopyTo(destination) で読む。CPU 結果の確保・staging の pooling・再利用・Dispose も利用者が管理する。

CopyFrom／CopyTo は CPU メモリ間のコピーだけを行う。ReadBufferAsync／ReadStagingAsync や専用の読み戻し helper を設けない。読み取り元に記録・送信の lease が残る場合は InvalidOperationException とし、GPU 完了を内部で待たない。元の GPU buffer から staging へのコピーや送信を行ったと仮定しない。

CPU メモリの map／invalidate／flush／unmap は backend が CPU 可視性のために扱う。コピーと必要な CPU memory 処理の間は対象を保護して Dispose／再利用を拒否する。CPU コピー範囲は byte 単位で検証し、GPU copy alignment を CPU Span の offset／length に適用しない。native mapping の範囲と alignment は backend 内部で適合させる。Browser の非同期 mapping を利用者へ明示する準備 API は別途具体化し、同期 CopyFrom／CopyTo を GPU 転送や完了待機で代替しない。

### 明示的な Upload とコピー命令

CopyFrom／CopyTo は既存 Upload buffer の CPU メモリへデータをコピーする操作とする。raw bytes は index／texture staging などに使用し、typed overload は生成 serializer で pack して schema／target layout の metadata を付ける。どちらも GPU buffer を更新しない。通常の DeviceLocal／Automatic buffer を destination に渡す要求は拒否し、queue write や staging の追加確保で代行しない。

利用者が Memory=Upload／Usage=CopySource の staging buffer と CopyDestination を持つ GPU buffer を確保する。CopyFrom で CPU 側のデータを準備し、CommandEncoder.RecordCopyBuffer で staging→GPU の転送命令を記録し、Finish で CommandBuffer に確定して Submit する。Upload だけを暗黙に送信する helper は提供しない。FrameContext の自動 UploadBytes／Upload<T> はこの契約から外す。frame の引数構築と allocator の管理は別の責務として維持する。

CommandEncoder は CommandBuffer の記録 builder、CommandBuffer は Finish で確定した一回限りの送信単位とする。RecordCopyBuffer、RecordCopyBufferToTexture、RecordCopyTextureToBuffer、RecordCopyTexture は命令を記録するだけで CPU memcpy や GPU 実行を行わない。GPU コピーは利用者がその CommandBuffer を Submit した後に実行される。コピー命令の受け渡しに、利用者から見えないコマンドバッファや送信を作らない。

CPU mapping／flush／unmap は backend が CPU 可視性のために実装するが、これを GPU コピー命令と混同しない。mapping に非同期準備が必要な環境の async API は別途具体化し、同期 CopyFrom／CopyTo の実装に GPU 転送を隠して代替しない。typed reference は shader 用途を持つコピー先の登録済み領域に対して作り、CopySource のみの staging 領域を直接 shader 引数にしない。

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

ShaderWrite と ShaderRead は併用可能。Upload は CopySource のみ、Readback は CopyDestination のみを許可し、直接 shader／index 利用を要求する場合は Automatic／DeviceLocal を使う。これにより WebGPU の map 用途の制約を共通化する。Memory は公開 map pointer の権限ではない。Upload／Readback は生成時点で CPU 可視の staging resource として実現する。backend は hint を理由に要求用途を変更しない。Upload は CopyFrom の実行時に別 staging を確保したり queue write を発行したりしない。Readback は生成時点で CPU 読み出しを実現する resource を確保し、CPU 読み出し時に別 staging を作って GPU コピーする fallback は禁止する。必要な配置を実現できない場合は CreateBuffer を失敗させる。

DeviceCaps に `ulong MaxBufferSize`、`ulong MaxStorageBufferBindingSize`、`uint CopyBufferOffsetAlignment`、`uint CopyBufferSizeAlignment`、`uint StorageBufferOffsetAlignment`、`uint MaxStorageBuffersPerStage` を報告する。コピー命令の alignment はコマンド backend から DeviceCaps に集約する。各 alignment は正数。GPU コピー命令は offset と length をそれぞれの alignment に照合し、index は format の要素サイズに照合する。型付き storage 参照は binding offset、target alignment／stride、最大 binding size にも従う。

CreateBuffer の論理 size 自体には copy alignment を要求しない。backend が内部で切り上げる場合も論理末尾をアクセス可能にしない。切り上げの overflow と物理 API 上限を検証する。サイズを小さく補正して成功を返さない。

### 部分領域、データ型、競合

Slice は allocation を作らず、寿命も延ばさない。Runtime の suballocation は alignment と世代を保持し、再利用後の typed reference を拒否する。GpuReference の整数化、serialization、任意 token の生成は提供しない。

typed CPU copy の schema／layout ID は物理 buffer 全体ではなく対象領域に記録する。登録のない bytes から CreateReference は作れない。型付きコピーは source の登録済み範囲全体と一致し、destination の alignment・target layout が適合する場合だけメタデータと参照依存を伝播する。部分コピーまたは raw bytes による上書きは重複する登録を失効させる。古い GPU 参照を新しいデータの型として再利用しない。

GPU の同一 Buffer 内コピーはコマンド契約が扱い、半開区間の重複を記録前に拒否する。IBufferBackendContract の CPU CopyFrom／CopyTo は GPU 命令を検査・記録せず、CPU のコピーは Span.CopyTo の重複領域の規約に従う。shader の範囲外 index は利用者のシェーダー契約であり、backend に全 GPU データの CPU 検査は要求しない。

### バックエンドが実装するもの

内部 driver の以下の操作は概念的な実装契約であり、利用側へ公開する API ではない。現行 IGraphicsDriver の全機能が実装済みという意味ではない。

```diff
+namespace Lumyte.Graphics.Implementation
+{
+    // factory は確保・rollback を担い、成功時だけ具象 buffer instance を返す。
+    internal interface IGraphicsDriver
+    {
+        IBufferBackendContract CreateBuffer(BufferDesc normalizedDesc);
+        BufferCapabilities GetBufferCapabilities();
+    }
+
+    // 一つの具象 instance が native allocation、Device 所属、世代、lease、metadata を管理する。
+    // 独立した buffer token／登録表を共通層に設けない。GPU 命令・バリアは持たない。
+    internal interface IBufferBackendContract : IDisposable
+    {
+        ulong SizeInBytes { get; }
+        BufferUsage Usage { get; }
+        MemoryPreference Memory { get; }
+        // offset／length を自身の論理範囲と寿命に対して検証する。
+        void ValidateRange(ulong offset, ulong length);
+        // 自身の範囲を Slang 反射と一致する実データ参照へ解決。物理参照は利用側へ公開しない。
+        ResolvedBufferReference ResolveBufferReference(ulong offset, ulong length, ShaderSchema schema, BindingPlan bindingPlan);
+        // idle な Upload の指定範囲へ CPU bytes をコピー。source は length 以下。
+        void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length);
+        // idle な Readback の指定範囲から length bytes を caller memory にコピー。
+        void CopyTo(Span<byte> destination, ulong offset, ulong length);
+        // lease が残る間は拒否。idle 時に自身の native allocation を解放、idempotent。
+        void Dispose();
+    }
+}
```

バッファの backend はコピー命令、resource state／layout 遷移、バリアを所有しない。RecordCopyFrom／CopyTo はコマンドバッファの backend 契約、Barrier は利用者が明示するコマンドの契約に置く。IBufferBackendContract にこれらの操作を追加せず、自動で推測して挿入もしない。コマンド側が受け取った依存を native の遷移へ変換する詳細は ADR-0004 を参照する。CPU memory の flush／invalidate はバリア宣言の代行ではない。

| backend | 対応方法と注意点 |
| --- | --- |
| managed wgpu | Ahjo binding の Buffer と CPU mapping を使用。GPU copy／queue はコマンド側。storage binding + offset、map 用途制約、4-byte copy alignment と device limits を検証。独自 .Native は不要 |
| DirectX | resource／heap と upload／readback heap。状態遷移と完了 fence はコマンド／送信側。address または descriptor の pack は Native と Slang library 内部 |
| Vulkan | buffer／allocation、memory type、非 coherent 範囲の flush／invalidate。アクセス依存はコマンド側。device address は使用する profile が要求する場合だけ内部で採用 |
| Browser WebGPU | WebGPU buffer／mapAsync と promise 完了。JS／Wasm の入力は所有コピーを作り、ホスト Native に依存しない |

具象 backend は native allocation と Device 所属を自身に保持する。共通 Buffer はこの instance を保持し、BufferSlice は元 Buffer と offset／length を保持する。コマンド backend は同じ instance と範囲を受け取り、所属と lease を検証する。

DirectX／Vulkan の C ABI には `uint64` の size／offset／length、`uint32` の usage／memory を渡す。native object の非公開 handle は具象 backend 内だけで管理し、共通 buffer token は設けない。CPU bytes の pointer と length は呼び出し中にコピーし、GPU 完了まで caller の pin を保持しない。完全な ABI 宣言・エラー文字列の所有規約は別途定める。

### 所有権、同期、エラー

記録が使う buffer と参照依存は CommandEncoder → CommandBuffer → Submission に lease を移し、GPU 完了時に解放する。未送信のコマンドを破棄した場合も解放する。CPU 書き込み・Dispose・suballocation 再利用は使用中に拒否する。FrameContext の領域は EndFrame で登録した完了まで再利用しない。

Encoder と FrameContext は単一スレッド、Device 操作は利用側で直列化する。Submission 待機のキャンセルは利用者が送信した GPU コピーを取消さない。利用者は完了を改めて観測してから staging を解放・再利用する。CPU CopyFrom／CopyTo は同期のメモリコピーで、独自のキャンセルと GPU 待機を持たず、staging 自体を Dispose しない。DeviceLost は未完了要求を GraphicsError で終了させ、使用中 handle を backend の安全な teardown 手順で解放する。

範囲・enum・用途・別 Device は引数例外、破棄済みは ObjectDisposedException、記録状態・使用中変更は InvalidOperationException。有効な要求に対する未対応機能、OutOfMemory、DeviceLost は Result の GraphicsError。Submission 待機の cancellation は OperationCanceledException。native エラーを成功値へ変換しない。

### 利用例

以下は提案 API の Upload。RequireSuccess は利用者の Result エラー処理。stagingBytes は pack 後の必要サイズ、gpuBuffer は CopyDestination と必要な shader 用途を持つ。

```csharp
using var upload = RequireSuccess(device.CreateBuffer(new BufferDesc {
    SizeInBytes = stagingBytes,
    Usage = BufferUsage.CopySource,
    Memory = MemoryPreference.Upload,
}));
// CPU 側で pack／コピーする。ここでは GPU buffer は変化しない。
upload.Slice(0, stagingBytes).CopyFrom(values, dataLayout);
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
var bytes = new byte[checked((int)byteCount)];
staging.Slice(0, byteCount).CopyTo(bytes.AsSpan());
// CPU 読み出しと GPU 使用の完了後に、利用者が staging を解放・再利用する。
```

## 検討した代替案

### Readback helper が確保・コピー・送信・待機をまとめて実行する

staging の再利用、コピーと他の work のまとめ方、送信と完了観測の時期を利用側が選べなくなる。各操作を明示し、CPU 読み出しと書き込みは Buffer／BufferSlice の CopyFrom／CopyTo に分けるする。

### public map／GPU address を基本経路にする

CPU 可視 heap と任意アドレスを前提にし、Browser と共通化できない。staging と不透明参照を基本にする。

### 任意の unmanaged struct を GPU schema として扱う

layout、padding、参照の pack を検証できない。raw bytes と登録済みの Slang データ型を分ける。

## 結果と影響

- 用途と部分領域を共通 API で検証し、backend の物理表現を隠せる。
- typed metadata、参照依存、lease、staging の管理コストが生じる。
- 上限と alignment は backend ごとに確認が必要で、すべてのサイズをコピーできるとは保証しない。
- 初期実装は raw CPU CopyFrom／CopyTo と明示的な RecordCopyBuffer／CommandBuffer 送信を採用する。Slang 生成型を使う overload と一般的な metadata は未実装。初期実装の CPU 読み出しも caller の Span への CopyTo に統一する。

## 検証方針

共通 API のみを使うテストで size／range overflow、用途、alignment、同一 buffer の重複コピー、別 Device、破棄後参照を拒否する。Upload → Compute → Barrier → Copy → Readback の bytes と、非 coherent memory の可視性を確認する。typed metadata の伝播／失効、世代更新、未送信破棄、in-flight Dispose、読み出し中の staging lease、未完了コピーでの読み出し拒否、キャンセル／DeviceLost 時の map／lease 回収を backend ごとに検証する。CopyFrom／CopyTo が CPU memory のみを変更し、RecordCopyBuffer の Finish 前／Submit 前に GPU work が生じないことを確認する。Upload の queue write と隠れた staging／送信がないこと、記録後の CPU 上書きを拒否することも検証する。読み取り側 CopyTo が staging／結果領域の確保・GPU Copy・Submit・GPU 待機を実行しないこと、命令とバリアが buffer 契約に含まれないことを検証する。初期の raw CPU copy と明示的な transfer は ADR-0008 の実装で検証する。一般的な schema／metadata と追加契約は今回未検証。

## 別途決定する事項

- Runtime allocator の容量・拡張・fragmentation 方針、Browser の明示的な CPU mapping 準備 API。巨大 Readback は利用者が Slice と CPU destination を分割する。
- uniform 専用／indirect／vertex input 用途、永続 map、複数キュー、外部 memory import。

## 参考資料

- [グラフィックス共通契約](0004-graphics-library.md)
- [Slang とデータ受け渡し](0005-shader-compilation-and-data-interop.md)
- [wgpu 初期実装](0008-wgpu-first-backend.md)
- [WebGPU buffers](https://www.w3.org/TR/webgpu/#buffers)
