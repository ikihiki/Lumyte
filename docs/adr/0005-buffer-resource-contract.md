# ADR-0005: バッファの利用 API とバックエンド実装契約

- 状態: 提案
- 日付: 2026-10-07

## 背景

[ADR-0004](0004-graphics-device.md) の Buffer と GPU データ参照を、生成、部分領域、Upload、コピー、Readback、解放まで実装できる契約にする。GPU アドレスに依存する設計では WebGPU と共通化できず、C# のメモリ配置を直接 GPU データとして扱うと Slang の target layout と食い違う。

本 ADR は `BufferDesc<T>` の正本でもある。テクスチャへの転送は [ADR-0006](0006-texture-resource-contract.md)、シェーダーの schema と pack は [ADR-0010](0010-shader-compilation-and-data-interop.md) に従う。以下は拡張する共通 API の案であり、現在の UInt32 限定 shader schema と初期 API の仕様は [ADR-0011](0011-wgpu-first-backend.md) に残す。

## 決定

### 責務と公開型

共通型と staging の CPU コピー・読み出し API は `Lumyte.Graphics`、frame allocator と引数構築は `Lumyte.Graphics.Runtime` に置く。Core は backend や Ahjo に依存しない。利用者は共通 GraphicsDevice で生成し、バックエンドは内部 driver を通して処理する。`IGraphicsBuffer<T>` は具象 backend が直接実装する所有 interface、`BufferSlice<T>` は非所有の immutable value、IGpuRef は非所有の型付き interface とする。利用者による具象 allocation の直接構築、Native handle、GPU アドレス、map pointer の取得は提供しない。

API 差分の比較元は origin/main（Graphics API は未導入）。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed class GraphicsDevice : IDisposable
+    {
+        // Desc を snapshot して生成
+        // 確保失敗・未対応用途は GraphicsError
+        // 成功時だけ所有権を返す
+        public Result<IGraphicsBuffer<T>> CreateBuffer<T>(BufferDesc<T> desc) where T : unmanaged;
+        // 確保せず T の解決済みレイアウトと要素単位のコピー制約を取得する。
+        public BufferLayout<T> GetBufferLayout<T>() where T : unmanaged;
+
+        // GPU 参照は ADR-0008 の IArgumentTable.WriteBuffer で登録して取得する。
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
+}
+
```

### 型、サイズ、instance 数

`BufferDesc<T>` と `IGraphicsBuffer<T>` の Count は要素数、SizeInBytes は backend が返す `BufferLayout<T>.GetSizeInBytes(Count)` で算出する。Desc 自体は Device を持たず Count／Usage／Memory の入力だけを保持し、生成前のサイズは `device.GetBufferLayout<T>().GetSizeInBytes(desc.Count)` で問い合わせる。T は数値型、enum、参照フィールドを含まない unmanaged struct を許可する。bool／char／native-sized integer を含む host 表現も CPU storage として扱えるが、Slang の型、stride、alignment と互換とは仮定しない。参照を含む logical shader data は unmanaged 制約の buffer 要素にせず、生成 serializer で `IGraphicsBuffer<byte>` に pack する。生成 wire struct を T として使う場合も shader schema と反射 layout を検証する。backend は T の ElementSizeInBytes／ElementStrideInBytes、GPU copy の byte alignment と、それを T の要素単位に解決した CopyOffsetAlignmentInElements／CopyCountAlignment を数値で返す。要素数と SizeInBytes はこの解決済み layout から計算する。利用者とコピー記録 API は同じ数値を使い、コピー offset／count の倍数条件と最終 byte 範囲を検証する。

具象 allocation が public interface を直接実装する。一つの typed buffer に facade や要素ごとの object を作らない。`BufferSlice<T>` は値型なので slice ごとの heap allocation も必須にしない。ただし native binding の内部 object、Desc、CPU 結果領域などまで allocation がゼロになるとは保証しない。大きな buffer の部分利用と pooling は引き続き利用側で選択する。

### backend が認識する T ごとの数値

ElementStrideInBytes は GPU buffer の格納表現を認識する backend が決定する。初期 wgpu の raw storage は `Unsafe.SizeOf<T>`() を stride とし、GPU copy の offset／size alignment はともに 4 bytes。公開する要素単位の制約は `alignment / gcd(alignment, stride)` で求める。stride が 3 bytes の packed struct は 4 要素ごとの offset／count が必要であり、単純な切り上げ除算では求めない。byte 換算も公開 Buffer.Layout と同じ解決済み値を使う。CPU CopyFrom／CopyTo はこの GPU コピー用の倍数条件を要求しない。

| 初期 wgpu の T | ElementStrideInBytes | CopyOffsetAlignmentInElements | CopyCountAlignment |
| --- | ---: | ---: | ---: |
| byte／sbyte／bool | 1 | 4 | 4 |
| short／ushort／Half／char | 2 | 2 | 2 |
| int／uint／float | 4 | 1 | 1 |
| long／ulong／double | 8 | 1 | 1 |
| packed 3-byte struct | 3 | 4 | 4 |
| 12-byte struct | 12 | 1 | 1 |

この対応表は shader ABI の互換表ではない。Slang の wire layout は schema と profile の反射情報で検証し、host 表現と異なる stride を採用する backend は CopyFrom／CopyTo に必要な pack／unpack を実装する。対応する変換を実装していない場合は明示的な未対応として拒否し、host の memcpy で代用しない。現在の wgpu は host stride と同じ raw storage、shader schema は UInt32 に限定する。layout は値型として buffer に保持し、要素ごとの object を追加しない。

### 明示的な Readback の手順

利用者が Memory=Readback／Usage=CopyDestination の staging buffer を確保し、コマンド API の Barrier と RecordCopyBuffer を記録する。利用者が Finish／Submit し、コピーを含む Submission の完了を観測した後、CPU の destination 領域を用意して stagingSlice.CopyTo(destination) で読む。CPU 結果の確保・staging の pooling・再利用・Dispose も利用者が管理する。

CopyFrom／CopyTo は CPU メモリ間のコピーだけを行う。ReadBufferAsync／ReadStagingAsync や専用の読み戻し helper を設けない。読み取り元に記録・送信の lease が残る場合は InvalidOperationException とし、GPU 完了を内部で待たない。元の GPU buffer から staging へのコピーや送信を行ったと仮定しない。

CPU メモリの map／invalidate／flush／unmap は backend が CPU 可視性のために扱う。コピーと必要な CPU memory 処理の間は対象を保護して Dispose／再利用を拒否する。CPU コピーの公開範囲は要素単位で、checked な byte 換算後の範囲を検証し、GPU copy alignment を CPU Span の offset／length に適用しない。native mapping の範囲と alignment は backend 内部で適合させる。Browser の非同期 mapping を利用者へ明示する準備 API は別途具体化し、同期 CopyFrom／CopyTo を GPU 転送や完了待機で代替しない。

### 明示的な Upload とコピー命令

CopyFrom は既存 Upload buffer の CPU メモリへ T の要素をコピーする。CopyTo は Readback から caller の `Span<T>` に読む。byte storage は index／texture staging や Slang の wire data に使用する。参照を含む生成型の serializer は ADR-0010 の byte storage 向け拡張で pack し、schema／target layout の metadata を付ける。CPU コピーは GPU buffer を更新しない。通常の DeviceLocal／Automatic buffer を destination に渡す要求は拒否し、queue write や staging の追加確保で代行しない。

利用者が Memory=Upload／Usage=CopySource の staging buffer と CopyDestination を持つ GPU buffer を確保する。CopyFrom で CPU 側のデータを準備し、CommandEncoder.RecordCopyBuffer で staging→GPU の転送命令を記録し、Finish で CommandBuffer に確定して Submit する。Upload だけを暗黙に送信する helper は提供しない。FrameContext の自動 UploadBytes／`Upload<T>` はこの契約から外す。frame の引数構築と allocator の管理は別の責務として維持する。

CommandEncoder は CommandBuffer の記録 builder、CommandBuffer は Finish で確定した一回限りの送信単位とする。RecordCopyBuffer、RecordCopyBufferToTexture、RecordCopyTextureToBuffer、RecordCopyTexture は命令を記録するだけで CPU memcpy や GPU 実行を行わない。GPU コピーは利用者がその CommandBuffer を Submit した後に実行される。コピー命令の受け渡しに、利用者から見えないコマンドバッファや送信を作らない。

CPU mapping／flush／unmap は backend が CPU 可視性のために実装するが、これを GPU コピー命令と混同しない。mapping に非同期準備が必要な環境の async API は別途具体化し、同期 CopyFrom／CopyTo の実装に GPU 転送を隠して代替しない。typed reference は shader 用途を持つコピー先の登録済み領域に対して作り、CopySource のみの staging 領域を直接 shader 引数にしない。

### `BufferDesc<T>` と上限

全 Desc に `string? Label = null` を持たせる。ラベルは診断専用。値は init-only とする。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record BufferDesc<T> where T : unmanaged
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public BufferDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // 正数の要素数。要素ごとの object は生成しない。
+        public required ulong Count { get; init; }
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

Slice は allocation を作らず、寿命も延ばさない。Runtime の suballocation は alignment と世代を保持し、再利用後の typed reference を拒否する。IGpuRef の整数化、serialization、任意 token の生成は提供しない。

typed CPU copy の schema／layout ID は物理 buffer 全体ではなく対象領域に記録する。登録のない bytes から shader data の IGpuRef は作れない。明示的な GPU コピーが登録済みの完全要素に整列し、destination の alignment・target layout が適合する場合だけメタデータと参照依存を伝播する。要素を切る部分コピーまたは raw bytes による上書きは重複する要素の登録を失効させる。古い GPU 参照を新しいデータの型として再利用しない。

GPU の同一 Buffer 内コピーはコマンド契約が扱い、半開区間の重複を記録前に拒否する。IBufferBackendContract の CPU CopyFrom／CopyTo は GPU 命令を検査・記録せず、CPU のコピーは Span.CopyTo の重複領域の規約に従う。shader の範囲外 index は利用者のシェーダー契約であり、backend に全 GPU データの CPU 検査は要求しない。

### バックエンドが実装するもの

内部 driver の以下の操作は概念的な実装契約であり、利用側へ公開する API ではない。backend が対応する具体的な範囲は ADR-0011 に定める。

```diff
+namespace Lumyte.Graphics.Implementation
+{
+    // factory は確保・rollback を担い、成功時だけ具象 buffer instance を返す。
+    internal interface IGraphicsDriver
+    {
+        // Device の backend 制約と T の wire layout を解決する。allocation は作らない。
+        BufferLayout<T> GetBufferLayout<T>() where T : unmanaged;
+        IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> normalizedDesc) where T : unmanaged;
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

バッファの backend はコピー命令、resource state／layout 遷移、バリアを所有しない。RecordCopyBuffer はコマンドバッファの backend 契約、Barrier は利用者が明示するコマンドの契約に置く。IBufferBackendContract にこれらの操作を追加せず、自動で推測して挿入もしない。コマンド側が受け取った依存を native の遷移へ変換する詳細は [ADR-0009](0009-command-buffer.md) を参照する。CPU memory の flush／invalidate はバリア宣言の代行ではない。

| backend | 対応方法と注意点 |
| --- | --- |
| managed wgpu | Ahjo binding の Buffer と CPU mapping を使用。GPU copy／queue はコマンド側。storage binding + offset、map 用途制約、4-byte copy alignment と device limits を検証。独自 .Native は不要 |
| DirectX | resource／heap と upload／readback heap。状態遷移と完了 fence はコマンド／送信側。address または descriptor の pack は Native と Slang library 内部 |
| Vulkan | buffer／allocation、memory type、非 coherent 範囲の flush／invalidate。アクセス依存はコマンド側。device address は使用する profile が要求する場合だけ内部で採用 |
| Browser WebGPU | WebGPU buffer／mapAsync と promise 完了。JS／Wasm の入力は所有コピーを作り、ホスト Native に依存しない |

具象 backend は native allocation と Device 所属を自身に保持する。具象 instance は `IGraphicsBuffer<T>` と IBufferBackendContract を同時に実装し、factory はそのまま返す。共通層に Buffer wrapper は設けない。`BufferSlice<T>` は同じ instance と要素 offset／count を保持する値型で、byte 範囲への変換も値型とする。コマンド backend は同じ instance と範囲を受け取り、所属と lease を検証する。

DirectX／Vulkan の C ABI には `uint64` の size／offset／length、`uint32` の usage／memory を渡す。native object の非公開 handle は具象 backend 内だけで管理し、共通 buffer token は設けない。CPU bytes の pointer と length は呼び出し中にコピーし、GPU 完了まで caller の pin を保持しない。完全な ABI 宣言・エラー文字列の所有規約は別途定める。

### 所有権、同期、エラー

記録が使う buffer と参照依存は CommandEncoder → CommandBuffer → Submission に lease を移し、GPU 完了時に解放する。未送信のコマンドを破棄した場合も解放する。CPU 書き込み・Dispose・suballocation 再利用は使用中に拒否する。FrameContext の領域は EndFrame で登録した完了まで再利用しない。

Encoder と FrameContext は単一スレッド、Device 操作は利用側で直列化する。Submission 待機のキャンセルは利用者が送信した GPU コピーを取消さない。利用者は完了を改めて観測してから staging を解放・再利用する。CPU CopyFrom／CopyTo は同期のメモリコピーで、独自のキャンセルと GPU 待機を持たず、staging 自体を Dispose しない。DeviceLost は未完了要求を GraphicsError で終了させ、使用中 handle を backend の安全な teardown 手順で解放する。

範囲・enum・用途・別 Device は引数例外、破棄済みは ObjectDisposedException、記録状態・使用中変更は InvalidOperationException。有効な要求に対する未対応機能、OutOfMemory、DeviceLost は Result の GraphicsError。Submission 待機の cancellation は OperationCanceledException。native エラーを成功値へ変換しない。

### 利用例

以下は提案 API の Upload。RequireSuccess は利用者の Result エラー処理。stagingBytes は pack 後の必要サイズ、gpuBuffer は `IGraphicsBuffer<byte>` で CopyDestination と必要な shader 用途を持つ。

```csharp
using var upload = RequireSuccess(device.CreateBuffer(new BufferDesc<byte> {
    Count = stagingBytes,
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

以下は提案 API。RequireSuccess は利用者側で Result の失敗を処理して成功値を取り出す処理を表す。buffer は `IGraphicsBuffer<byte>` で CopySource 用途、byteCount はコピーと読み出しの alignment を満たす。producerToCopySource は利用者が前の書き込みからコピーへの依存を構築したものとする。

```csharp
// 1. 利用者が staging buffer を確保する。必要なら既存 buffer を再利用できる。
using var staging = RequireSuccess(device.CreateBuffer(new BufferDesc<byte> {
    Count = byteCount,
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

参照を含む利用側の shader data の byte pack と論理 Argument Table 参照は [ADR-0008](0008-resource-bindings.md)、要素単位の依存 metadata とコピー先の予定状態・確定・失効は [ADR-0008](0008-resource-bindings.md) に従う。

## 検討した代替案

### Readback helper が確保・コピー・送信・待機をまとめて実行する

staging の再利用、コピーと他の work のまとめ方、送信と完了観測の時期を利用側が選べなくなる。各操作を明示し、CPU 書き込みと読み出しを `IGraphicsBuffer<T>`／`BufferSlice<T>` の CopyFrom／CopyTo に分ける。

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

共通 API のみを使うテストで size／range overflow、用途、alignment、同一 buffer の重複コピー、別 Device、破棄後参照を拒否する。Upload → Compute → Barrier → Copy → Readback の bytes と、非 coherent memory の可視性を確認する。typed metadata の伝播／失効、世代更新、未送信破棄、in-flight Dispose、読み出し中の staging lease、未完了コピーでの読み出し拒否、キャンセル／DeviceLost 時の map／lease 回収を backend ごとに検証する。CopyFrom／CopyTo が CPU memory のみを変更し、RecordCopyBuffer の Finish 前／Submit 前に GPU work が生じないことを確認する。Upload の queue write と隠れた staging／送信がないこと、記録後の CPU 上書きを拒否することも検証する。読み取り側 CopyTo が staging／結果領域の確保・GPU Copy・Submit・GPU 待機を実行しないこと、命令とバリアが buffer 契約に含まれないことを検証する。初期の raw CPU copy と明示的な transfer は ADR-0011 の実装で検証する。一般的な schema／metadata と追加契約は今回未検証。

## 別途決定する事項

- Runtime allocator の容量・拡張・fragmentation 方針、Browser の明示的な CPU mapping 準備 API。巨大 Readback は利用者が Slice と CPU destination を分割する。
- uniform 専用／indirect／vertex input 用途、永続 map、複数キュー、外部 memory import。

## 参考資料

- [グラフィックス共通契約](0004-graphics-device.md)
- [Slang とデータ受け渡し](0010-shader-compilation-and-data-interop.md)
- [wgpu 初期実装](0011-wgpu-first-backend.md)
- [WebGPU buffers](https://www.w3.org/TR/webgpu/#buffers)
